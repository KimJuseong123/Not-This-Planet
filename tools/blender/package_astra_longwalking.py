"""Create URP import settings and documentation for the generated kit only."""
from pathlib import Path
import re, json, uuid, struct, hashlib

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'METAGAME/Assets/_Project/Models/LongWalkingTrail'
TEMPLATE=ROOT/'METAGAME/Assets/_Project/Art/Buildings/Hangar'

def guid(path):
    meta=Path(str(path)+'.meta')
    if meta.exists():
        m=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf-8'),re.M)
        if m:return m[1]
    return uuid.uuid4().hex

def write_meta(path,text):
    Path(str(path)+'.meta').write_text(text,encoding='utf-8')

reports=sorted(json.loads((BASE/'validation.json').read_text()),key=lambda r:r['id'])
catalog=[]
for report in reports:
    folder=BASE/report['folder'];fbx=folder/report['fbx'];stem=fbx.stem;texdir=folder/(stem+'.fbm')
    texguids={}
    for key in ('BaseColor','Metallic','Roughness','Normal','Emission','MetallicSmoothness'):
        path=texdir/(stem+'_'+key+'.png');g=guid(path);texguids[key]=g
        template='Normal' if key=='Normal' else 'BaseColor'
        s=(TEMPLATE/'AstraHangar.fbm'/('AstraHangar_'+template+'.png.meta')).read_text()
        s=re.sub(r'^guid: \w+', 'guid: '+g,s,flags=re.M)
        s=re.sub(r'sRGBTexture: \d+', 'sRGBTexture: '+('1' if key in ('BaseColor','Emission') else '0'),s)
        s=re.sub(r'maxTextureSize: \d+','maxTextureSize: 1024',s)
        # Keep atlas tile colours separate at seams and preserve packed smoothness alpha.
        s=re.sub(r'wrap([UVW]): \d+',r'wrap\1: 1',s)
        write_meta(path,s)
    mat=folder/('M_Astra_'+stem.removeprefix('SM_Astra_')+'.mat');mg=guid(mat)
    s=(TEMPLATE/'M_AstraHangar.mat').read_text(encoding='utf-8')
    s=s.split('--- !u!114')[0]
    s=s.replace('m_Name: M_AstraHangar','m_Name: '+mat.stem)
    for prop,key in [('_BaseMap','BaseColor'),('_MainTex','BaseColor'),('_BumpMap','Normal'),('_EmissionMap','Emission'),('_MetallicGlossMap','MetallicSmoothness')]:
        s=re.sub(r'(- '+re.escape(prop)+r':\n\s+m_Texture:) \{[^}]*\}',r'\1 {fileID: 2800000, guid: '+texguids[key]+', type: 3}',s)
    s=s.replace('_BumpScale: 0.35','_BumpScale: 0.25')
    mat.write_text(s,encoding='utf-8')
    write_meta(mat,f'fileFormatVersion: 2\nguid: {mg}\nNativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    for collider in (False,True):
        target=folder/'Colliders'/('UCX_'+stem+'.fbx') if collider else fbx
        s=(TEMPLATE/'AstraHangar.fbx.meta').read_text(encoding='utf-8')
        s=re.sub(r'^guid: \w+','guid: '+guid(target),s,flags=re.M)
        if not collider:
            mapping='externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: '+report['material']+'\n    second: {fileID: 2100000, guid: '+mg+', type: 2}'
            s=s.replace('externalObjects: {}',mapping,1)
        for field,value in {'materialImportMode':0 if collider else 2,'generateSecondaryUV':0 if collider else 1,'addColliders':0,'importCameras':0,'importLights':0,'importAnimation':0,'isReadable':1 if collider else 0,'globalScale':1,'bakeAxisConversion':1}.items():
            s=re.sub(r'('+field+r':) [^\n]+',r'\g<1> '+str(value),s)
        write_meta(target,s)
    catalog.append({'id':report['id'],'folder':report['folder'],'fbx':str(fbx.relative_to(BASE)).replace('\\','/'),'collider':str((folder/'Colliders'/('UCX_'+stem+'.fbx')).relative_to(BASE)).replace('\\','/'),'material':str(mat.relative_to(BASE)).replace('\\','/'),'texture_count':6,'triangles':report['geometry']['triangles'],'collision_triangles':report['collision_triangles'],'dimensions_width_height_length_m':[report['geometry']['dimensions_blender_xyz'][i] for i in (0,2,1)],'preview':'Previews~/'+report['folder']+'-concept.png','sha256':hashlib.sha256(fbx.read_bytes()).hexdigest()})
(BASE/'asset-catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
# Preserve Unity-generated GUIDs, and do not place meta files in Unity-ignored folders.
for path in [BASE]+sorted(BASE.rglob('*')):
    if path.suffix=='.meta' or any(part.endswith('~') for part in path.relative_to(BASE).parts):continue
    if Path(str(path)+'.meta').exists():continue
    g=guid(path)
    if path.is_dir():s=f'fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    else:s=f'fileFormatVersion: 2\nguid: {g}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    write_meta(path,s)
print(json.dumps({'models':len(catalog),'fbx':len(list(BASE.rglob('*.fbx'))),'textures':sum(x['texture_count'] for x in catalog),'zip_files':len(list(BASE.rglob('*.zip')))}))
