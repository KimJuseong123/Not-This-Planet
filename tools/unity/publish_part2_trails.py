"""Publish only the six batch-authored replacements, preserving unrelated YAML byte-for-byte."""
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'tmp/part2-trail-integration'
SCENE = ROOT / 'METAGAME/Assets/Scenes/Stage1_Astra.unity'

def blocks(text):
    return {int(m.group(1)): m.group(0) for m in re.finditer(r'^--- !u!\d+ &(\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', text, re.M | re.S)}

def main():
    original = (WORK / 'Stage1_Astra.before.unity').read_text(encoding='utf-8')
    authored = (WORK / 'project/Assets/Scenes/Stage1_Astra.unity').read_text(encoding='utf-8')
    report = json.loads((WORK / 'validation.json').read_text())
    assert report['status'].startswith('PASS')
    before, after = blocks(original), blocks(authored)
    targets = [k for k, v in before.items() if re.search(r'^  m_Name: (Long walking trail [0-4]|Wayfinding hill deck transition)$', v, re.M)]
    assert len(targets) == 6
    removed = set(targets)
    parents = set()
    for key in targets:
        components = [int(v) for v in re.findall(r'component: \{fileID: (\d+)\}', before[key])]
        removed.update(components)
        for c in components:
            p = re.search(r'm_Father: \{fileID: (\d+)\}', before[c])
            if p:
                parents.add(int(p[1]))
    assert removed.isdisjoint(after), 'Original ramp objects must be deleted'
    added = after.keys() - before.keys()
    assert added
    for key, value in before.items():
        if key not in removed | parents:
            refs = {int(v) for v in re.findall(r'\{fileID: (\d+)\}', value)}
            assert not refs.intersection(removed), f'Gameplay reference to deleted ramp: {key}'
    changes = {}
    for parent in parents:
        oldchildren = re.search(r'  m_Children:.*?(?=  m_Father:)', before[parent], re.S)[0]
        newchildren = re.search(r'  m_Children:.*?(?=  m_Father:)', after[parent], re.S)[0]
        changes[parent] = before[parent].replace(oldchildren, newchildren)
    merged = original[:original.index('--- !u!')]
    for key, value in before.items():
        if key not in removed:
            merged += changes.get(key, value)
    merged += ''.join(after[k] for k in after if k in added)
    finalblocks = blocks(merged)
    for key, value in finalblocks.items():
        for ref in re.findall(r'\{fileID: (\d+)\}', value):
            assert int(ref) == 0 or int(ref) in finalblocks, f'Missing local reference {key} -> {ref}'
    source_hash = hashlib.sha256(SCENE.read_bytes()).hexdigest()
    allowed_hashes = {(WORK / 'source.sha256').read_text().strip().lower()}
    previous_audit = ROOT / 'artifacts/part2-trail-integration/serialization-audit.json'
    if previous_audit.exists():
        allowed_hashes.add(json.loads(previous_audit.read_text())['sceneSHA256'])
    assert source_hash in allowed_hashes, 'Source scene changed during work; stop and rebase'
    SCENE.write_text(merged, encoding='utf-8', newline='\n')
    audit = {'removedObjects': 6, 'removedSerializedBlocks': len(removed), 'newSerializedBlocks': len(added), 'modifiedParentChildLists': sorted(parents), 'unrelatedOriginalBlocksPreserved': len(before)-len(removed)-len(parents), 'localReferences': 'PASS', 'sourceConcurrentChangeCheck': 'PASS', 'sceneSHA256': hashlib.sha256(SCENE.read_bytes()).hexdigest()}
    out = ROOT / 'artifacts/part2-trail-integration'
    (out / 'serialization-audit.json').write_text(json.dumps(audit, indent=2), encoding='utf-8')
    (out / 'validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(audit, indent=2))

if __name__ == '__main__':
    main()
