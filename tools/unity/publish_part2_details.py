"""Merge isolated Unity-authored observatory details without resaving unrelated scene data."""
import hashlib
import json
import re
from publish_part2_trails import ROOT, WORK, SCENE, blocks

original=(WORK/'Stage1_Astra.before-details.unity').read_text(encoding='utf-8')
authored=(WORK/'project/Assets/Scenes/Stage1_Astra.unity').read_text(encoding='utf-8')
report=json.loads((WORK/'details-validation.json').read_text())
assert report['status']=='PASS'
before,after=blocks(original),blocks(authored)
removed=before.keys()-after.keys()
removed_go=[k for k in removed if before[k].startswith('--- !u!1 ')]
assert len(removed_go)==10
for k in removed_go:
    v=before[k]
    assert ('m_Name: Observatory instrument\n' in v or 'm_Name: Instrument indicator\n' in v or ('Hill observatory' in v and 'entry stair' in v))
allowed=set(removed_go)
for k in removed_go:
    allowed.update(map(int,re.findall(r'component: \{fileID: (\d+)\}',before[k])))
assert removed==allowed
added=after.keys()-before.keys()
changes={}
for k,v in before.items():
    if k in removed:continue
    if v.startswith('--- !u!4 ') and '  m_Children:' in v:
        old=re.search(r'  m_Children:.*?(?=  m_Father:)',v,re.S)[0]
        new=re.search(r'  m_Children:.*?(?=  m_Father:)',after[k],re.S)[0]
        if old!=new:changes[k]=v.replace(old,new)
merged=original[:original.index('--- !u!')]+''.join(changes.get(k,v) for k,v in before.items() if k not in removed)+''.join(v for k,v in after.items() if k in added)
final=blocks(merged)
for k,v in final.items():
    for ref in re.findall(r'\{fileID: (\d+)\}',v):assert int(ref)==0 or int(ref) in final,(k,ref)
allowed_hashes={(WORK/'details-source.sha256').read_text().strip().lower()}
audit_path=ROOT/'artifacts/part2-trail-integration/details-serialization-audit.json'
if audit_path.exists():allowed_hashes.add(json.loads(audit_path.read_text())['sceneSHA256'])
assert hashlib.sha256(SCENE.read_bytes()).hexdigest() in allowed_hashes,'Concurrent scene edits'
SCENE.write_text(merged,encoding='utf-8',newline='\n')
out=ROOT/'artifacts/part2-trail-integration'
(out/'details-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
audit={'removedObjects':len(removed_go),'addedBlocks':len(added),'modifiedParentChildLists':list(changes),'unrelatedBlocksPreserved':len(before)-len(removed)-len(changes),'localReferences':'PASS','sourceHashCheck':'PASS','sceneSHA256':hashlib.sha256(SCENE.read_bytes()).hexdigest()}
(out/'details-serialization-audit.json').write_text(json.dumps(audit,indent=2),encoding='utf-8')
print(json.dumps(audit,indent=2))
