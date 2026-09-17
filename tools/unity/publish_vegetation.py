"""Publish additive Unity-authored flora while preserving every original scene block."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "artifacts/stage1-vegetation"
WORK = ROOT / "tmp/part2-trail-integration"
SCENE = ROOT / "METAGAME/Assets/Scenes/Stage1_Astra.unity"


def blocks(text):
    return {
        int(m[1]): m[0]
        for m in re.finditer(
            r"^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\Z)", text, re.M | re.S
        )
    }


def main():
    original_bytes = (OUT / "Stage1_Astra.before.unity").read_bytes()
    assert SCENE.read_bytes() == original_bytes, "Scene changed during authoring; do not overwrite."
    report = (WORK / "vegetation-validation.txt").read_text()
    assert report.startswith("PASS\n")
    original = original_bytes.decode("utf-8").replace("\r\n", "\n")
    authored = (WORK / "project/Assets/Scenes/Stage1_Astra.unity").read_text(encoding="utf-8")
    before, after = blocks(original), blocks(authored)
    assert before.keys() <= after.keys(), "Existing scene object deleted"
    added = after.keys() - before.keys()
    assert added, "No additions"
    roots_id = next(k for k, v in before.items() if "\nSceneRoots:\n" in v)
    old_roots = set(re.findall(r"fileID: (\d+)", before[roots_id]))
    new_roots = set(re.findall(r"fileID: (\d+)", after[roots_id]))
    assert old_roots < new_roots and len(new_roots - old_roots) == 1
    root_ref = int(next(iter(new_roots - old_roots)))
    assert root_ref in added
    # Preserve the original root list/order and append only the new root.
    root_block = before[roots_id].rstrip("\n") + f"\n  - {{fileID: {root_ref}}}\n"
    merged = original[:original.index("--- !u!")]
    merged += "".join(root_block if k == roots_id else v for k, v in before.items())
    merged += "".join(v for k, v in after.items() if k in added)
    final = blocks(merged)
    for k, v in before.items():
        if k != roots_id:
            assert final[k] == v, f"Existing object modified: {k}"
    for k, v in final.items():
        for ref in re.findall(r"\{fileID: (\d+)\}", v):
            assert int(ref) == 0 or int(ref) in final, (k, ref)

    asset_root = ROOT / "METAGAME/Assets"
    authored_root = WORK / "project/Assets"
    for relative in [
        "_Project/Prefabs/Environment/Stage1/Vegetation",
        "_Project/Materials/Environment/Stage1/Vegetation",
    ]:
        source = authored_root / relative
        destination = asset_root / relative
        assert not destination.exists(), "Refusing to replace an existing asset folder"
        shutil.copytree(source, destination)
        shutil.copy2(Path(str(source) + ".meta"), Path(str(destination) + ".meta"))
    for relative in [
        "_Project/Scripts/Editor/Stage1VegetationBuilder.cs.meta",
        "_Project/Scripts/Tests/Editor/VegetationInteractionTests.cs.meta",
    ]:
        destination = asset_root / relative
        if not destination.exists():
            shutil.copy2(authored_root / relative, destination)
    # Preserve the original newline convention too.
    newline = "\r\n" if b"\r\n" in original_bytes else "\n"
    SCENE.write_bytes(merged.replace("\n", newline).encode("utf-8"))
    audit = {
        "status": "PASS",
        "existingBlocksPreserved": len(before) - 1,
        "existingTransformsAndComponentsChanged": 0,
        "addedBlocks": len(added),
        "addedSceneRoots": 1,
        "localReferences": "PASS",
        "originalSHA256": hashlib.sha256(original_bytes).hexdigest(),
        "sceneSHA256": hashlib.sha256(SCENE.read_bytes()).hexdigest(),
    }
    (OUT / "preservation-audit.json").write_text(json.dumps(audit, indent=2), encoding="utf-8")
    (OUT / "validation.txt").write_text(report, encoding="utf-8")
    print(json.dumps(audit, indent=2))


if __name__ == "__main__":
    main()
