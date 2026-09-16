"""Publish Unity-authored floor fixtures while preserving unrelated scene blocks."""
import hashlib
import json
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'tmp/floor-guidance-integration'
PROJECT = ROOT / 'tmp/part2-trail-integration/project'
SCENE = ROOT / 'METAGAME/Assets/Scenes/Stage1_Astra.unity'
OUT = ROOT / 'artifacts/floor-guidance-integration'


def blocks(text):
    return {int(m[1]): m[0] for m in re.finditer(
        r'^--- !u!\d+ &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', text, re.M | re.S)}


def main():
    original = (WORK / 'Stage1_Astra.before.unity').read_text(encoding='utf-8')
    authored = (PROJECT / 'Assets/Scenes/Stage1_Astra.unity').read_text(encoding='utf-8')
    report = json.loads((WORK / 'validation.json').read_text())
    assert report['status'] == 'PASS' and report['replaced'] == 138
    before, after = blocks(original), blocks(authored)
    removed = before.keys() - after.keys()
    expected = set()
    target_ids = []
    for key, value in before.items():
        name = re.search(r'^  m_Name: (.+)$', value, re.M)
        if name and (name[1] in ('Runway color', 'Runway housing', 'Pickup / departure marker') or 'light strip ' in name[1]):
            target_ids.append(key)
            expected.add(key)
            expected.update(map(int, re.findall(r'component: \{fileID: (\d+)\}', value)))
    assert len(target_ids) == 232, len(target_ids)
    assert removed == expected, ('Unexpected removed blocks', removed ^ expected)
    added = after.keys() - before.keys()
    changed = {}
    touched = set(map(int, report['changedIds']))
    for key in before.keys() & after.keys():
        old, new = before[key], after[key]
        if key in touched:
            changed[key] = new
        elif old.startswith('--- !u!4 ') and '  m_Children:' in old:
            a = re.search(r'  m_Children:.*?(?=  m_Father:)', old, re.S)[0]
            b = re.search(r'  m_Children:.*?(?=  m_Father:)', new, re.S)[0]
            if a != b:
                changed[key] = old.replace(a, b)
    merged = original[:original.index('--- !u!')]
    merged += ''.join(changed.get(k, v) for k, v in before.items() if k not in removed)
    merged += ''.join(re.sub(r'[ \t]+$', '', after[k], flags=re.M) for k in after if k in added)
    final = blocks(merged)
    for key, value in final.items():
        for ref in re.findall(r'\{fileID: (-?\d+)\}', value):
            assert int(ref) == 0 or int(ref) in final, (key, ref)
    assert SCENE.read_bytes() == (WORK / 'Stage1_Astra.before.unity').read_bytes(), 'Concurrent source scene edit'
    # Stage for a second Unity reload before publishing to the live project.
    (WORK / 'Stage1_Astra.merged.unity').write_text(merged, encoding='utf-8', newline='\n')
    shutil.copy2(WORK / 'Stage1_Astra.merged.unity', PROJECT / 'Assets/Scenes/Stage1_Astra.unity')
    audit = dict(status='PASS', replacedFixtures=138, removedLegacyObjects=232,
                 removedBlocks=len(removed), addedBlocks=len(added), changedBlocks=len(changed),
                 unrelatedBlocksPreserved=len(before)-len(removed)-len(changed),
                 localReferences='PASS', sourceSceneSHA256=hashlib.sha256(SCENE.read_bytes()).hexdigest(),
                 sceneSHA256=hashlib.sha256((WORK / 'Stage1_Astra.merged.unity').read_bytes()).hexdigest())
    (WORK / 'serialization-audit.json').write_text(json.dumps(audit, indent=2), encoding='utf-8')
    print(json.dumps(audit, indent=2))


def publish():
    audit = json.loads((WORK / 'serialization-audit.json').read_text())
    assert hashlib.sha256(SCENE.read_bytes()).hexdigest() == audit['sourceSceneSHA256'], 'Concurrent scene edit'
    final = WORK / 'Stage1_Astra.merged.unity'
    assert hashlib.sha256(final.read_bytes()).hexdigest() == audit['sceneSHA256']
    assert (PROJECT / 'Assets/Scenes/Stage1_Astra.unity').read_bytes() == final.read_bytes(), 'Validated scene changed'
    assert (WORK / 'reload-validation.txt').read_text().startswith('PASS')
    assert (WORK / 'play-smoke.txt').read_text().startswith('PASS')
    tests = ET.parse(WORK / 'tests.xml').getroot()
    assert tests.attrib['result'] == 'Passed' and int(tests.attrib['failed']) == 0
    relative = Path('Assets/_Project/Materials/FloorGuidance')
    source, destination = PROJECT / relative, ROOT / 'METAGAME' / relative
    assert not destination.exists(), 'Materials already published; inspect before replacing'
    shutil.copytree(source, destination)
    shutil.copy2(source.with_suffix('.meta'), destination.with_suffix('.meta'))
    shutil.copy2(final, SCENE)
    OUT.mkdir(parents=True, exist_ok=True)
    for name in ['serialization-audit.json', 'validation.json', 'reload-validation.txt',
                 'play-smoke.txt', 'tests.xml', 'render-volume.txt']:
        shutil.copy2(WORK / name, OUT / name)
    for path in WORK.glob('after-*.png'):
        shutil.copy2(path, OUT / path.name)
    shutil.copy2(WORK / 'before-0.png', OUT / 'before-0.png')
    print('Published: scene, seven lens materials, scene-specific volume, validation artifacts.')


if __name__ == '__main__':
    publish() if '--publish' in sys.argv else main()
