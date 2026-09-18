"""Publish the validated Terrain and remove the replaced legacy floor object."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "tmp/part2-trail-integration"
OUT = ROOT / "artifacts/stage1-terrain"
SCENE = ROOT / "METAGAME/Assets/Scenes/Stage1_Astra.unity"


def blocks(text):
    return {int(m[1]): m[0] for m in re.finditer(
        r"^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\Z)", text, re.M | re.S)}


def main():
    snapshot = (OUT / "Stage1_Astra.before.unity").read_bytes()
    assert SCENE.read_bytes() == snapshot, "Concurrent user scene edit; refusing overwrite."
    report = json.loads((WORK / "terrain-conversion.json").read_text())
    reload_report = json.loads((WORK / "terrain-reload.json").read_text())
    assert report["status"] == reload_report["status"] == "PASS"
    assert reload_report["physicsDropSamples"] == 5
    original = snapshot.decode("utf-8").replace("\r\n", "\n")
    authored = (WORK / "project/Assets/Scenes/Stage1_Astra.unity").read_text(encoding="utf-8")
    before, after = blocks(original), blocks(authored)
    added = after.keys() - before.keys()
    assert len([k for k in added if after[k].startswith("--- !u!1 ")]) == 1
    source = next(v for v in before.values() if "sculpted ground" in v)
    components = [int(c) for c in re.findall(r"component: \{fileID: (\d+)\}", source)]
    transform = next(before[c] for c in components if before[c].startswith("--- !u!4 "))
    parent = int(re.search(r"m_Father: \{fileID: (\d+)\}", transform)[1])
    source_id = next(k for k, v in before.items() if v == source)
    removed = {source_id, *components}
    assert before.keys() - after.keys() == removed, "Unexpected scene removal"
    assert "  m_Children: []\n" in transform, "Legacy floor has children"
    assert report["sourceComparisonPerformed"], "Initial mesh comparison required"
    assert reload_report["legacyObjectRemoved"], "Legacy floor survived reload"
    changes = {}
    old_children = re.search(r"  m_Children:.*?(?=  m_Father:)", before[parent], re.S)[0]
    new_children = re.search(r"  m_Children:.*?(?=  m_Father:)", after[parent], re.S)[0]
    old_refs = re.findall(r"fileID: (\d+)", old_children)
    new_refs = re.findall(r"fileID: (\d+)", new_children)
    transform_id = next(c for c in components if before[c].startswith("--- !u!4 "))
    assert [v for v in new_refs if v in old_refs] == [v for v in old_refs if int(v) != transform_id]
    new_ref = set(new_refs) - set(old_refs)
    assert len(new_ref) == 1 and int(next(iter(new_ref))) in added
    changes[parent] = before[parent].replace(old_children, new_children)
    merged = original[:original.index("--- !u!")]
    merged += "".join(changes.get(k,v) for k,v in before.items() if k not in removed)
    merged += "".join(v for k,v in after.items() if k in added)
    final = blocks(merged)
    assert all(final[k] == v for k,v in before.items() if k not in changes and k not in removed)
    for k,v in final.items():
        for ref in re.findall(r"\{fileID: (\d+)\}",v):
            assert int(ref) == 0 or int(ref) in final, (k,ref)
    paths = [
        "_Project/Terrain/Stage1/TerrainData/TD_Stage1_AstraBasin.asset",
        "_Project/Terrain/Stage1/TerrainLayers/TL_Stage1_AstraSand.terrainlayer",
        "_Project/Materials/Environment/Stage1/M_Stage1_AstraTerrain.mat",
    ]
    for path in paths:
        source_path = WORK / "project/Assets" / path
        destination = ROOT / "METAGAME/Assets" / path
        assert not destination.exists(), f"Existing asset: {destination}"
        shutil.copy2(source_path, destination)
        shutil.copy2(Path(str(source_path)+".meta"), Path(str(destination)+".meta"))
    meta = "_Project/Scripts/Editor/Stage1AstraTerrainConversion.cs.meta"
    if not (ROOT / "METAGAME/Assets" / meta).exists():
        shutil.copy2(WORK / "project/Assets" / meta, ROOT / "METAGAME/Assets" / meta)
    newline = "\r\n" if b"\r\n" in snapshot else "\n"
    SCENE.write_bytes(merged.replace("\n",newline).encode("utf-8"))
    audit = {
        "status":"PASS",
        "existingBlocksPreserved":len(before)-len(changes)-len(removed),
        "existingObjectTransformsChanged":0,
        "modifiedBlocks":list(changes),
        "removedBlocks":sorted(removed),
        "changes":"Remove legacy floor object/components; replace its parent child entry with Terrain",
        "addedBlocks":len(added),
        "sourceSHA256":hashlib.sha256(snapshot).hexdigest(),
        "sceneSHA256":hashlib.sha256(SCENE.read_bytes()).hexdigest(),
        "localReferences":"PASS"
    }
    (OUT/"preservation-audit.json").write_text(json.dumps(audit,indent=2),encoding="utf-8")
    for filename in ("terrain-conversion.json","terrain-reload.json"):
        shutil.copy2(WORK/filename,OUT/filename)
    print(json.dumps(audit,indent=2))


if __name__ == "__main__":
    main()
