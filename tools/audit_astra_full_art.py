"""Audit original Unity YAML records against the Blender visual integration."""
from pathlib import Path
import re, json, collections
ROOT = Path(__file__).resolve().parents[1]
before = ROOT / '.local-validation/astra-full-art-baseline.unity'
after = ROOT / '.local-validation/astra-ver2/Assets/Scenes/Stage1_Astra_ver2.unity'
def records(path):
    text = path.read_text(encoding='utf-8-sig')
    return {m[2]: (int(m[1]), m[3]) for m in re.finditer(r'^--- !u!(\d+) &(\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}
a,b=records(before),records(after)
removed=set(a)-set(b)
assert not removed, removed
changed=collections.Counter(); unexpected=[]; moved=[]
def strip(text, pattern): return re.sub(pattern, '', text, flags=re.M)
for key,(kind,old) in a.items():
    newkind,new=b[key]
    assert kind==newkind
    if old==new: continue
    changed[kind]+=1
    if kind==33:
        old,new=(strip(t,r'^  m_Mesh:.*\n') for t in (old,new))
    elif kind==23:
        old,new=(strip(t,r'^  m_Enabled:.*\n|^  m_Materials:\n(?:  - .*\n)*') for t in (old,new))
    elif kind==4:
        old,new=(strip(t,r'^  m_Children:(?: \[\])?\n(?:  - .*\n)*') for t in (old,new))
        go=re.search(r'm_GameObject: \{fileID: (\d+)\}',old)[1]
        if '  m_Name: Enter cannon\n' in a[go][1]:
            moved.append(key)
            old,new=(strip(t,r'^  m_Local(?:Rotation|Position|EulerAnglesHint):.*\n') for t in (old,new))
    elif kind==1660057539:
        original_roots=re.findall(r'^  - \{fileID: (\d+)\}',old,re.M)
        final_roots=re.findall(r'^  - \{fileID: (\d+)\}',new,re.M)
        assert final_roots[:len(original_roots)]==original_roots
        assert all(root not in a for root in final_roots[len(original_roots):])
        old,new=(strip(t,r'^  m_Roots:\n(?:  - .*\n)*') for t in (old,new))
    if old!=new: unexpected.append({'fileID':key,'class':kind,'old':old,'new':new})
assert not unexpected, json.dumps(unexpected[:3],ensure_ascii=False,indent=2)
assert len(moved)==1,moved
assets=ROOT/'METAGAME/Assets'
guids={}
for meta in assets.rglob('*.meta'):
    match=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
    if match: guids[match[1]]=str(meta)
new_refs=set(re.findall(r'guid: ([0-9a-f]{32})',after.read_text(encoding='utf-8-sig')))-set(re.findall(r'guid: ([0-9a-f]{32})',before.read_text(encoding='utf-8-sig')))
assert all(g in guids for g in new_refs),sorted(new_refs-set(guids))
missing_meta=[str(p) for p in (assets/'_Project/Art/AstraFullArt').iterdir() if p.is_file() and p.suffix!='.meta' and not p.with_name(p.name+'.meta').exists()]
assert not missing_meta,missing_meta
report={'pass':True,'original_records':len(a),'deleted_original_records':len(removed),
 'added_records':len(b)-len(a),'changed_by_class':dict(changed),'intentional_sign_transform':moved,
 'new_scene_asset_guids_resolved':len(new_refs),'missing_art_meta':len(missing_meta),
 'preserved':'All original records except MeshFilter mesh, MeshRenderer visibility/materials, Transform child lists and Enter cannon presentation transform.'}
out=ROOT/'docs/art-production/astra-ver2/full-art/serialized-audit.json'
out.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report))
