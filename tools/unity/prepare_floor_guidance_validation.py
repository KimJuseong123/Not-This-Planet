"""Prepare isolated Unity validation using locally cached packages, without changing production settings."""
import json,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
CHECK=ROOT/'tmp/floor-guidance-unity'
OUT=ROOT/'METAGAME/Assets/_Project/Models/FloorGuidance'
CACHE=ROOT.parent/'S15P21B202/METAGAME/Library/PackageCache'
CHECK.mkdir(parents=True,exist_ok=True)
for name in ['Assets/Editor','Packages','ProjectSettings']: (CHECK/name).mkdir(parents=True,exist_ok=True)
packages={}
for p in CACHE.glob('*/package.json'):
 d=json.loads(p.read_text(encoding='utf-8-sig'));packages[d['name']]=(p.parent,d)
deps={}
def add(name,version):
 if name in deps:return
 if name.startswith('com.unity.modules.'):deps[name]=version;return
 if name not in packages:raise RuntimeError('Missing cached dependency '+name)
 p,d=packages[name];deps[name]='file:'+p.as_posix()
 for n,v in d.get('dependencies',{}).items():add(n,v)
add('com.unity.render-pipelines.universal','17.5.0')
for n in ['physics','imageconversion','jsonserialize','imgui']:add('com.unity.modules.'+n,'1.0.0')
(CHECK/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2),encoding='utf-8')
shutil.copy2(ROOT/'METAGAME/ProjectSettings/ProjectVersion.txt',CHECK/'ProjectSettings/ProjectVersion.txt')
shutil.copy2(ROOT/'tools/unity/BuildFloorGuidance.cs',CHECK/'Assets/Editor/BuildFloorGuidance.cs')
target=CHECK/'Assets/FloorGuidance';target.mkdir(exist_ok=True)
for d in OUT.iterdir():
 if d.is_dir() and not d.name.endswith('~'):shutil.copytree(d,target/d.name,dirs_exist_ok=True)
print(json.dumps({'project':str(CHECK),'dependencies':list(deps)},indent=2))

