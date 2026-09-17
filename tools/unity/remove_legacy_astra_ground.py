"""Remove only the disabled Astra source floor; retain all unrelated scene bytes."""
import argparse
import hashlib
import json
import re
from pathlib import Path

from publish_astra_terrain import blocks

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "METAGAME/Assets/Scenes/Stage1_Astra.unity"


def cleanup(snapshot):
    text = snapshot.decode("utf-8")
    before = blocks(text)
    sources = [k for k, v in before.items() if "sculpted ground" in v]
    assert len(sources) == 1, "Expected exactly one legacy floor"
    source = sources[0]
    components = {int(c) for c in re.findall(r"component: \{fileID: (\d+)\}", before[source])}
    removed = {source, *components}
    assert len(removed) == 5, "Unexpected legacy components"
    transform = next(c for c in components if before[c].startswith("--- !u!4 "))
    assert "  m_Children: []" in before[transform], "Legacy floor has children"
    parent = int(re.search(r"m_Father: \{fileID: (\d+)\}", before[transform])[1])
    disabled = [c for c in components if before[c].startswith(("--- !u!23 ", "--- !u!64 "))]
    assert len(disabled) == 2 and all("  m_Enabled: 0" in before[c] for c in disabled)
    terrain = [v for v in before.values() if v.startswith("--- !u!218 ")]
    assert len(terrain) == 1 and "  m_Enabled: 1" in terrain[0], "Active Terrain required"
    terrain_data = re.search(r"m_TerrainData: (.*)", terrain[0])[1]
    assert any(v.startswith("--- !u!154 ") and "  m_Enabled: 1" in v and
               "m_TerrainData: " + terrain_data in v for v in before.values()), "TerrainCollider required"
    newline = "\r\n" if b"\r\n" in snapshot else "\n"
    entry = f"  - {{fileID: {transform}}}{newline}"
    assert before[parent].count(entry) == 1
    changed_parent = before[parent].replace(entry, "", 1)
    for k, v in before.items():
        if k in removed:
            continue
        remaining = changed_parent if k == parent else v
        refs = {int(r) for r in re.findall(r"\{fileID: (\d+)\}", remaining)}
        assert not refs & removed, f"External reference to legacy floor from {k}"
    result = text[:text.index("--- !u!")] + "".join(
        changed_parent if k == parent else v for k, v in before.items() if k not in removed)
    after = blocks(result)
    assert all(after[k] == v for k, v in before.items() if k not in removed and k != parent)
    assert after.keys() == before.keys() - removed
    result = result.encode("utf-8")
    return result, {
        "status": "PASS", "validation": "serialized scene preservation; not Unity runtime",
        "removedBlocks": sorted(removed), "modifiedParent": parent,
        "unrelatedBlocksPreserved": len(before) - len(removed) - 1,
        "sourceSHA256": hashlib.sha256(snapshot).hexdigest(),
        "sceneSHA256": hashlib.sha256(result).hexdigest(),
        "terrainAndColliderPreserved": True, "referencesToRemovedBlocks": 0,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    snapshot = SCENE.read_bytes()
    result, audit = cleanup(snapshot)
    if args.apply:
        assert SCENE.read_bytes() == snapshot, "Concurrent scene edit"
        SCENE.write_bytes(result)
        (ROOT / "artifacts/stage1-terrain/legacy-removal-audit.json").write_text(
            json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(audit, indent=2))
