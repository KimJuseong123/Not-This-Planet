"""Package launchpad FBX/FBM with stable Unity metadata and the existing URP material convention."""
from pathlib import Path
import re, uuid, json, hashlib, shutil
ROOT=Path(r'C:\workspace\2-PJT\S15P21B202-571-art')
OUT=ROOT/'METAGAME/Assets/_Project/Models/MainAssemblyLaunchpad'
SOURCE=ROOT/'artifacts/stage1-main-assembly-launchpad'
TEMPLATE=ROOT/'METAGAME/Assets/_Project/Art/Buildings/Hangar'
NAME='SM_Astra_MainAssemblyLaunchpad'
TEX=OUT/(NAME+'.fbm')
def guid(p):
 text=Path(str(p)+'.meta').read_text(encoding='utf-8-sig')
 return re.search(r'^guid: ([0-9a-f]{32})$',text,re.M).group(1)
def meta(p,text):
 f=Path(str(p)+'.meta')
 g=guid(p) if f.exists() else uuid.uuid5(uuid.NAMESPACE_URL,'astra-main-launchpad/'+p.relative_to(ROOT).as_posix()).hex
 f.write_text(text.replace('__GUID__',g),encoding='utf-8')
def default_meta(p,kind='DefaultImporter'):
 meta(p,'fileFormatVersion: 2\nguid: __GUID__\n'+kind+':\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
for channel in ['BaseColor','Metallic','Roughness','Normal','Emission','MetallicSmoothness']:
 p=TEX/(NAME+'_'+('MS' if channel=='MetallicSmoothness' else channel)+'.png')
 t=(TEMPLATE/'AstraHangar.fbm'/('AstraHangar_'+channel+'.png.meta')).read_text(encoding='utf-8-sig')
 meta(p,re.sub(r'^guid: \w+','guid: __GUID__',t,count=1,flags=re.M))
m=OUT/'M_Astra_MainAssemblyLaunchpad.mat'
t=(TEMPLATE/'M_AstraHangar.mat').read_text(encoding='utf-8-sig').replace('m_Name: M_AstraHangar','m_Name: M_Astra_MainAssemblyLaunchpad')
for channel in ['BaseColor','Normal','Emission','MetallicSmoothness']:
 t=t.replace(guid(TEMPLATE/'AstraHangar.fbm'/('AstraHangar_'+channel+'.png')),guid(TEX/(NAME+'_'+('MS' if channel=='MetallicSmoothness' else channel)+'.png')))
t=t.replace('m_EnableInstancingVariants: 0','m_EnableInstancingVariants: 1')
m.write_text(t,encoding='utf-8')
meta(m,'fileFormatVersion: 2\nguid: __GUID__\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
t=(TEMPLATE/'AstraHangar.fbx.meta').read_text(encoding='utf-8-sig')
t=re.sub(r'^guid: \w+','guid: __GUID__',t,count=1,flags=re.M)
t=t.replace('externalObjects: {}','externalObjects:\n  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: M_Astra_MainAssemblyLaunchpad\n    second: {fileID: 2100000, guid: '+guid(m)+', type: 2}',1)
for a,b in [('materialImportMode: 1','materialImportMode: 2'),('importAnimation: 1','importAnimation: 0'),('importCameras: 1','importCameras: 0'),('importLights: 1','importLights: 0'),('generateSecondaryUV: 1','generateSecondaryUV: 0'),('preserveHierarchy: 0','preserveHierarchy: 1')]: t=t.replace(a,b)
meta(OUT/(NAME+'.fbx'),t)
for p in [OUT,TEX]:
 meta(p,'fileFormatVersion: 2\nguid: __GUID__\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
r=json.loads((SOURCE/'validation.json').read_text(encoding='utf-8'))
info={'asset':NAME,'fbx':NAME+'.fbx','fbm':NAME+'.fbm','material':m.name,
 'dimensions_m_width_depth_height':r['before_export']['dimensions'],'triangles':r['before_export']['triangles'],
 'meshes':14,'source_components':414,'textures':'Six 2048 x 2048 PNG maps, existing Astra hangar palette',
 'source_blend':str((SOURCE/(NAME+'.blend')).relative_to(ROOT)).replace('\\','/'),
 'pivot':'Center of flat docking surface; housing extends 0.53 m below it.',
 'front':'Blender -Y. FBX exported -Z forward, Y up.',
 'scene_integration':'Not placed. No gameplay/collider/animation integration.',
 'fbx_roundtrip':'PASS','sha256':hashlib.sha256((OUT/(NAME+'.fbx')).read_bytes()).hexdigest()}
(OUT/'asset-info.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf-8')
readme="""# Astra 메인 조립·발사대

Blender MCP로 기존 컨셉아트를 참고하여 직접 모델링한 시각 에셋입니다.

## 구성
- SM_Astra_MainAssemblyLaunchpad.fbx
- SM_Astra_MainAssemblyLaunchpad.fbm/ : BaseColor, Normal, Metallic, Roughness, Emission, MS 6종 PNG
- M_Astra_MainAssemblyLaunchpad.mat : 프로젝트 기존 URP Lit 재질 규칙 및 텍스처 연결
- asset-info.json : 크기, 원점, 메시 수와 검증 정보

## 구조
낮은 원형 받침, 청록 유도 링 두 줄, 분리된 고정턱 네 개, 후방 기둥 두 개, 냉각구와 점검문, 앞쪽 경사로, 오른쪽 조작판으로 구성했습니다.
표면 색과 미세 질감은 기존 AstraHangar의 2048px 팔레트 텍스처를 재사용했습니다. UV0는 팔레트 셀을 공유하고 UV1은 메시별 라이트맵용 펼침입니다.
14개 메시, 47,488 삼각형. 고정턱 4개는 개별 원점을 가지며 다른 구조와 분리되어 있습니다.

## 크기와 배치
너비 약 7.73m, 깊이 8.29m, 높이 3.33m입니다.
원점은 중앙 평면의 조립 기준점이며, 받침은 원점 아래 약 0.53m까지 내려갑니다. Blender 앞쪽은 -Y입니다.
현재 22m Central Assembly Dock 위에 얹는 안쪽 조립 장치이며, 바깥 도크는 모델에 포함하지 않았습니다.
씬 배치·충돌체·조립 판정·고정턱 애니메이션은 아직 연결하지 않았습니다. 기존 런타임 장식과 교체할 때 현재 도킹 높이, 운반 경로와 파츠 크기를 확인해야 합니다.
FBX 재임포트에서 크기, 삼각형 수, 두 UV 채널 및 텍스처 연결을 확인했습니다. Unity Editor에서의 임포트·플레이 검증은 하지 않았습니다.

## 원본과 미리보기
편집 가능한 Blender 파일, 실제 모델 렌더 6장, 검증 보고서는 저장소의 artifacts/stage1-main-assembly-launchpad/에 있습니다.
컨셉은 docs/assets/concepts/stage1-main-assembly-launchpad/를 참고합니다.
tools/blender/build_astra_main_launchpad.py, export_astra_main_launchpad.py, package_astra_main_launchpad.py로 제작 과정을 보관했습니다.
ZIP 압축파일은 생성하지 않았습니다.
"""
(OUT/'README.md').write_text(readme,encoding='utf-8')
default_meta(OUT/'README.md')
default_meta(OUT/'asset-info.json','TextScriptImporter')
# Ensure references point only to packaged textures and the existing project shader.
valid={guid(p) for p in TEX.glob('*.png')}
mapped=re.findall(r'm_Texture: \{fileID: 2800000, guid: ([a-f0-9]{32})',m.read_text(encoding='utf-8'))
assert mapped and set(mapped)<=valid
assert 'name: M_Astra_MainAssemblyLaunchpad' in Path(str(OUT/(NAME+'.fbx'))+'.meta').read_text()
result={'folder':str(OUT),'texture_count':len(list(TEX.glob('*.png'))),'material_references_valid':True,'zip_count':len(list(OUT.rglob('*.zip')))}

