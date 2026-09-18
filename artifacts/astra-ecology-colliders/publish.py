import re
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
out = root / 'artifacts/astra-ecology-colliders'
scene = root / 'METAGAME/Assets/Scenes/Stage1_Astra.unity'
work = root / 'tmp/part2-trail-integration'
original_bytes = (out / 'Stage1_Astra.before.unity').read_bytes()
original = original_bytes.decode('utf-8').replace('\r\n', '\n')
authored = (work / 'project/Assets/Scenes/Stage1_Astra.unity').read_text(encoding='utf-8')
report = (work / 'ecology-colliders-validation.txt').read_text()
assert report.startswith('PASS\n'), report

def blocks(text):
    return {int(m[1]): m[0] for m in re.finditer(r'^--- !u!\d+ &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)', text, re.M | re.S)}

before, after = blocks(original), blocks(authored)
assert before.keys() <= after.keys()
targets = {k for k,v in before.items() if re.search(r'^  m_Name: Ion crystals ',v,re.M) or re.search(r'^      value: Mushroom grove ',v,re.M)}
assert targets
merged = dict(before)
for k in targets:
    old, new = before[k], after[k]
    if '\nPrefabInstance:\n' in old:
        pattern = r'    m_AddedComponents:.*?(?=  m_SourcePrefab:)'
    else:
        pattern = r'  m_Component:.*?(?=  m_Layer:)'
    a, b = re.search(pattern,old,re.S), re.search(pattern,new,re.S)
    assert a and b and a[0] != b[0], k
    merged[k] = old[:a.start()] + b[0] + old[a.end():]

added = after.keys() - before.keys()
assert added
for k in added:
    assert re.match(r'--- !u!(?:1|64) ',after[k]), after[k]
    merged[k] = after[k]
for k,v in merged.items():
    if k not in targets and k not in added:
        assert v == before[k]
    for ref in re.findall(r'\{fileID: (-?\d+)\}',v):
        assert int(ref) == 0 or int(ref) in merged, (k,ref)
result = original[:original.index('--- !u!')] + ''.join(merged.values())
assert scene.read_bytes() == original_bytes, 'Source changed while validating'
newline = '\r\n' if b'\r\n' in original_bytes else '\n'
scene.write_bytes(result.replace('\n',newline).encode('utf-8'))
(out/'validation.txt').write_text(report,encoding='utf-8')
audit = {'targetObjects':len(targets),'addedBlocks':len(added),'unchangedOriginalBlocks':len(before)-len(targets),'onlyComponentListsAndPrefabAddedComponentsChanged':True,'localReferences':'PASS'}
(out/'preservation-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
print(report)
print(json.dumps(audit,indent=2))
