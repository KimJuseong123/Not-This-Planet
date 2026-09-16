"""Check complete delivery, roundtrip reports, PBR links and stable Unity metadata."""
from pathlib import Path
import json, re, struct, hashlib
from package_hangar_related import ROOT, OUT, SOURCE, guid

catalog=json.loads((OUT/'asset-catalog.json').read_text(encoding='utf-8'))
assert len(catalog)==13
assert len(list(OUT.rglob('*.fbx')))==13
assert len(list(OUT.rglob('*.fbm')))==13
assert not list(OUT.rglob('*.zip'))
checks=[]
for row in catalog:
 slug=Path(row['concept']).stem
 folder=OUT/slug; fbx=folder/row['fbx']; tex=folder/row['textures']
 assert fbx.read_bytes().startswith(b'Kaydara FBX Binary  \x00\x1a\x00'),fbx
 assert hashlib.sha256(fbx.read_bytes()).hexdigest()==row['sha256']
 assert (ROOT/row['source_blend']).exists()
 assert (folder/'preview.png').exists()
 report=json.loads((ROOT/row['validation_report']).read_text(encoding='utf-8'))
 assert report['roundtrip']=='PASS' and not report['missing_textures']
 assert report['before_export']['triangles']==report['after_reimport']['triangles']
 assert not any(report['geometry_audit'].values())
 assert len(list(tex.glob('*.png')))==6
 for image in tex.glob('*.png'):
  data=image.read_bytes(); assert data[:8]==b'\x89PNG\r\n\x1a\n'
  assert struct.unpack('>II',data[16:24])==(2048,2048)
  meta=Path(str(image)+'.meta').read_text(encoding='utf-8')
  if image.stem.endswith('Normal'): assert 'textureType: 1' in meta and 'sRGBTexture: 0' in meta
  elif image.stem.endswith(('Metallic','Roughness','MetallicSmoothness')): assert 'sRGBTexture: 0' in meta
 mat=folder/row['unity_material']; text=mat.read_text(encoding='utf-8')
 for ch in ['BaseColor','Normal','Emission','MetallicSmoothness']:
  assert guid(tex/(row['asset']+'_'+ch+'.png')) in text
 importer=Path(str(fbx)+'.meta').read_text(encoding='utf-8')
 assert guid(mat) in importer and 'name: '+report['material'] in importer
 assert 'globalScale: 1' in importer and 'generateSecondaryUV: 0' in importer
 checks.append({'asset':row['asset'],'triangles':row['triangles'],'status':'PASS'})
all_guids=[]
for p in OUT.rglob('*'):
 if p.suffix=='.meta':
  all_guids.append(re.search(r'^guid: (\w+)',p.read_text(encoding='utf-8'),re.M).group(1))
 else: assert Path(str(p)+'.meta').exists(),p
assert len(set(all_guids))==len(all_guids)
result={'assets':13,'fbx':13,'fbm_folders':13,'pbr_textures':78,'source_blends':13,
 'zip_files':0,'texture_size':2048,'unique_guids':len(all_guids),'checks':checks,
 'scope':'Blender FBX roundtrip, file completeness, metadata and texture linkage; Unity runtime not tested'}
(SOURCE/'package-validation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
