"""Publish the isolated spiral route, preserving unrelated scene serialization."""
import json,re,hashlib
from publish_part2_trails import ROOT,WORK,SCENE,blocks
original=(WORK/'Stage1_Astra.before-spiral.unity').read_text(encoding='utf-8')
authored=(WORK/'project/Assets/Scenes/Stage1_Astra.unity').read_text(encoding='utf-8')
report=json.loads((WORK/'spiral-validation.json').read_text())
assert report['status']=='PASS' and not report['blockers']
assert len(report['removed'])==60
before,after=blocks(original),blocks(authored)
removed=before.keys()-after.keys();added=after.keys()-before.keys()
touched=set(map(int,report['changedIds']))-{0}
changes={}
for key,value in before.items():
    if key in removed:continue
    if key in touched and (value.startswith('--- !u!4 ') or value.startswith('--- !u!1001 ')):
        changes[key]=after[key]
    elif value.startswith('--- !u!4 ') and '  m_Children:' in value:
        a=re.search(r'  m_Children:.*?(?=  m_Father:)',value,re.S)[0]
        b=re.search(r'  m_Children:.*?(?=  m_Father:)',after[key],re.S)[0]
        if a!=b:changes[key]=value.replace(a,b)
merged=original[:original.index('--- !u!')]+''.join(changes.get(k,v) for k,v in before.items() if k not in removed)+''.join(re.sub(r'[ \t]+$','',v,flags=re.M) for k,v in after.items() if k in added)
for key in before.keys() & after.keys():
    def positions(value):
        return re.findall(r'propertyPath: m_LocalPosition\.[xyz]\n\s+value: ([^\n]+)',value)+re.findall(r'^  m_LocalPosition: (.+)$',value,re.M)
    if positions(before[key])!=positions(after[key]):
        assert key in changes,f'Moved object was not included in publish: {key}'
final=blocks(merged)
for key,value in final.items():
    for ref in re.findall(r'\{fileID: (\d+)\}',value):assert int(ref)==0 or int(ref) in final,(key,ref)
out=ROOT/'artifacts/part2-trail-integration'
allowed={(WORK/'spiral-source.sha256').read_text().strip().lower()}
audit_path=out/'spiral-serialization-audit.json'
if audit_path.exists():allowed.add(json.loads(audit_path.read_text())['sceneSHA256'])
assert hashlib.sha256(SCENE.read_bytes()).hexdigest() in allowed,'Concurrent source edits'
SCENE.write_text(merged,encoding='utf-8',newline='\n')
audit={'removedBlocks':len(removed),'newBlocks':len(added),'changedBlocks':len(changes),'unrelatedBlocksPreserved':len(before)-len(removed)-len(changes),'localReferences':'PASS','sceneSHA256':hashlib.sha256(SCENE.read_bytes()).hexdigest()}
audit_path.write_text(json.dumps(audit,indent=2),encoding='utf-8')
(out/'spiral-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(audit,indent=2))
