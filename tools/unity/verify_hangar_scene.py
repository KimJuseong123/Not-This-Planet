"""Audit Unity-serialized scene changes without rewriting the scene."""
import json, re, hashlib, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/stage1-related-hangar/integration'
before = OUT / 'Stage1_Astra.before.unity'
after = ROOT / ('METAGAME/Assets/Scenes/Stage1_Astra.unity' if '--delivered' in sys.argv else 'tmp/hangar-unity/Assets/Scenes/Stage1_Astra.unity')

def blocks(path):
    text = path.read_text(encoding='utf-8-sig')
    result = {}
    for m in re.finditer(r'^--- !u!(\d+) &(-?\d+).*?\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S):
        result[m[2]] = (m[1], m[0])
    return result

a, b = blocks(before), blocks(after)
removed = set(a) - set(b)
added = set(b) - set(a)
changed = {k for k in a.keys() & b.keys() if a[k] != b[k]}
names = []
for k in removed:
    if a[k][0] == '1':
        names.append(re.search(r'  m_Name: (.*)', a[k][1])[1])
old_refs = []
for k, (_, block) in b.items():
    for r in re.finditer(r'\{fileID: (-?\d+)\}', block):
        if r[1] in removed:
            old_refs.append({'owner': k, 'removed_target': r[1]})
unexpected_changes = []
for k in changed:
    # Only parent child lists and SceneRoots may change when replacing objects.
    if a[k][0] not in ('4', '1660057539'):
        unexpected_changes.append({'id': k, 'type': a[k][0]})
guids = set(re.findall(r'guid: ([0-9a-f]{32})', after.read_text(encoding='utf-8-sig')))
meta_guids = set()
for p in (ROOT/'METAGAME/Assets').rglob('*.meta'):
    m=re.search(r'^guid: ([0-9a-f]{32})',p.read_text(encoding='utf-8-sig'),re.M)
    if m: meta_guids.add(m[1])
# Only newly introduced external GUIDs matter; built-ins and packages exist in baseline.
introduced = guids-set(re.findall(r'guid: ([0-9a-f]{32})',before.read_text(encoding='utf-8-sig')))
missing = sorted(introduced-meta_guids)
result = dict(removed_gameobjects=sorted(names), removed_blocks=len(removed),
              added_blocks=len(added), changed_existing_blocks=len(changed),
              changed_existing_types=[a[k][0] for k in changed],
              stale_references=old_refs, unexpected_changes=unexpected_changes,
              missing_new_guids=missing,
              original_sha256=hashlib.sha256(before.read_bytes()).hexdigest(),
              integrated_sha256=hashlib.sha256(after.read_bytes()).hexdigest())
result['pass'] = len(names)==15 and not old_refs and not unexpected_changes and not missing
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'serialization-audit.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
print(json.dumps(result,indent=2,ensure_ascii=False))
assert result['pass']
