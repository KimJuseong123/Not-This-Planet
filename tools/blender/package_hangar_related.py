"""Package authored Blender exports with project-compatible URP materials and metadata."""
from pathlib import Path
import json, re, uuid, shutil, hashlib

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'METAGAME/Assets/_Project/Models/Environment/Stage1/HangarRelated'
SOURCE=ROOT/'artifacts/stage1-related-hangar'
TEMPLATE=ROOT/'METAGAME/Assets/_Project/Art/Buildings/Hangar'
CONCEPT=ROOT/'docs/assets/concepts/stage1-related-hangar'

def guid(p):
 meta=Path(str(p)+'.meta')
 if meta.exists():
  m=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8'),re.M)
  if m: return m.group(1)
 return uuid.uuid5(uuid.NAMESPACE_URL,'astra-hangar-related/'+p.relative_to(ROOT).as_posix()).hex

def write_meta(p,text):
 Path(str(p)+'.meta').write_text(text.replace('__GUID__',guid(p)),encoding='utf-8')

def folders():
 for p in [OUT]+sorted([p for p in OUT.rglob('*') if p.is_dir()]):
  write_meta(p,'fileFormatVersion: 2\nguid: __GUID__\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')

def package():
 records=[]
 for report_path in sorted(SOURCE.glob('*/validation.json')):
  r=json.loads(report_path.read_text(encoding='utf-8')); name=r['asset']; slug=report_path.parent.name
  folder=OUT/slug; fbx=folder/(name+'.fbx'); tex=folder/(name+'.fbm')
  if not fbx.exists(): continue
  for p in tex.glob('*.png'):
   channel=p.stem.removeprefix(name+'_')
   template=TEMPLATE/'AstraHangar.fbm'/('AstraHangar_'+channel+'.png.meta')
   meta=re.sub(r'^guid: \w+','guid: __GUID__',template.read_text(encoding='utf-8'),count=1,flags=re.M)
   write_meta(p,meta)
  mat=folder/('M_Astra_'+name.removeprefix('SM_Astra_')+'.mat')
  text=(TEMPLATE/'M_AstraHangar.mat').read_text(encoding='utf-8')
  text=text.replace('m_Name: M_AstraHangar','m_Name: '+mat.stem)
  for channel in ['BaseColor','Normal','Emission','MetallicSmoothness']:
   old=guid(TEMPLATE/'AstraHangar.fbm'/('AstraHangar_'+channel+'.png'))
   text=text.replace(old,guid(tex/(name+'_'+channel+'.png')))
  text=text.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
  mat.write_text(text,encoding='utf-8')
  write_meta(mat,'fileFormatVersion: 2\nguid: __GUID__\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
  fm=(TEMPLATE/'AstraHangar.fbx.meta').read_text(encoding='utf-8')
  fm=re.sub(r'^guid: \w+','guid: __GUID__',fm,count=1,flags=re.M)
  fm=fm.replace('externalObjects: {}','externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: '+r['material']+'\n    second: {fileID: 2100000, guid: '+guid(mat)+', type: 2}',1)
  fm=fm.replace('materialImportMode: 1','materialImportMode: 2').replace('importAnimation: 1','importAnimation: 0')
  fm=fm.replace('importCameras: 1','importCameras: 0').replace('importLights: 1','importLights: 0')
  fm=fm.replace('generateSecondaryUV: 1','generateSecondaryUV: 0')
  write_meta(fbx,fm)
  # Preview and compact manifest accompany each FBX; editable Blender files stay outside Assets.
  preview=report_path.parent/'preview.png'
  if preview.exists(): shutil.copyfile(preview,folder/'preview.png')
  summary={'asset':name,'concept':str((CONCEPT/(slug+'.png')).relative_to(ROOT)),
   'fbx':fbx.name,'textures':tex.name,'unity_material':mat.name,'metres':True,
   'blender_dimensions_XYZ':r['before_export']['dimensions'],
   'triangles':r['before_export']['triangles'],'editable_components':r['components'],
   'source_blend':str((report_path.parent/(name+'.blend')).relative_to(ROOT)),
   'validation_report':str(report_path.relative_to(ROOT)),
   'fbx_roundtrip':r['roundtrip'],'sha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),
   'scene_placement':'not placed; imported asset only','pivot':r['pivot']}
  (folder/'asset-info.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
  records.append(summary)
 folders()
 (OUT/'asset-catalog.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
 # Add stable metadata only for our new files.
 for p in OUT.rglob('*'):
  if p.is_file() and p.suffix!='.meta' and not Path(str(p)+'.meta').exists():
   if p.suffix=='.png':
    template=(TEMPLATE/'AstraHangar.fbm/AstraHangar_BaseColor.png.meta').read_text(encoding='utf-8')
    write_meta(p,re.sub(r'^guid: \w+','guid: __GUID__',template,count=1,flags=re.M))
   else: write_meta(p,'fileFormatVersion: 2\nguid: __GUID__\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
 print(json.dumps({'packaged':len(records),'output':str(OUT),'triangles':sum(r['triangles'] for r in records)},indent=2))

if __name__=='__main__': package()
