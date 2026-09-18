"""Check actual delivery files and serialized GUIDs, never generated GUIDs."""
from pathlib import Path
import json, re, struct, hashlib
from package_hangar_related import ROOT, OUT, SOURCE

CHANNELS=('BaseColor','Normal','Emission','Metallic','Roughness','MS')
SLOTS={'_BaseMap':'BaseColor','_BumpMap':'Normal','_EmissionMap':'Emission','_MetallicGlossMap':'MS'}

def existing_guid(path):
 if not path.is_file(): raise FileNotFoundError(path)
 meta=Path(str(path)+'.meta')
 match=re.search(r'^guid: ([0-9a-f]{32})$',meta.read_text(encoding='utf-8'),re.M)
 if not match: raise ValueError(f'Invalid metadata: {meta}')
 return match[1]

def validate_material(folder,row):
 tex=folder/row['textures']
 references={ch:existing_guid(tex/(row['asset']+'_'+ch+'.png')) for ch in CHANNELS}
 text=(folder/row['unity_material']).read_text(encoding='utf-8')
 for slot,ch in SLOTS.items():
  match=re.search(r'- '+re.escape(slot)+r':\s*\n\s*m_Texture: \{fileID: 2800000, guid: ([0-9a-f]{32}), type: 3\}',text)
  if not match or match[1]!=references[ch]:
   raise ValueError(f'{folder}: {slot} must reference existing {ch} texture')

def validate():
 catalog=json.loads((OUT/'asset-catalog.json').read_text(encoding='utf-8'))
 assert len(catalog)==13
 assert len(list(OUT.rglob('*.fbx')))==13
 assert len(list(OUT.rglob('*.fbm')))==13
 assert not list(OUT.rglob('*.zip'))
 paths=[p.relative_to(ROOT).as_posix() for p in OUT.rglob('*')]
 assert all(len(p)<=180 for p in paths),[p for p in paths if len(p)>180]
 checks=[]
 for row in catalog:
  slug=Path(row['concept'].replace('\\','/')).stem
  folder=OUT/slug; fbx=folder/row['fbx']; tex=folder/row['textures']
  assert fbx.read_bytes().startswith(b'Kaydara FBX Binary  \x00\x1a\x00'),fbx
  assert hashlib.sha256(fbx.read_bytes()).hexdigest()==row['sha256']
  assert (ROOT/row['source_blend']).exists()
  assert (folder/'preview.png').exists()
  report=json.loads((ROOT/row['validation_report']).read_text(encoding='utf-8'))
  assert report['roundtrip']=='PASS' and not report['missing_textures']
  assert report['before_export']['triangles']==report['after_reimport']['triangles']
  assert not any(report['geometry_audit'].values())
  assert {p.name for p in tex.glob('*.png')}=={row['asset']+'_'+ch+'.png' for ch in CHANNELS}
  for image in tex.glob('*.png'):
   data=image.read_bytes(); assert data[:8]==b'\x89PNG\r\n\x1a\n'
   assert struct.unpack('>II',data[16:24])==(2048,2048)
   meta=Path(str(image)+'.meta').read_text(encoding='utf-8')
   if image.stem.endswith('Normal'): assert 'textureType: 1' in meta and 'sRGBTexture: 0' in meta
   elif image.stem.endswith(('Metallic','Roughness','MS')): assert 'sRGBTexture: 0' in meta
  validate_material(folder,row)
  importer=Path(str(fbx)+'.meta').read_text(encoding='utf-8')
  assert existing_guid(folder/row['unity_material']) in importer and 'name: '+report['material'] in importer
  assert 'globalScale: 1' in importer and 'generateSecondaryUV: 0' in importer
  checks.append({'asset':row['asset'],'triangles':row['triangles'],'status':'PASS'})
 all_guids=[]
 for p in OUT.rglob('*'):
  if p.suffix=='.meta':
   assert Path(str(p)[:-5]).exists(),f'Orphan metadata: {p}'
   all_guids.append(re.search(r'^guid: ([0-9a-f]{32})$',p.read_text(encoding='utf-8'),re.M).group(1))
  else: assert Path(str(p)+'.meta').exists(),p
 assert len(set(all_guids))==len(all_guids)
 result={'assets':13,'fbx':13,'fbm_folders':13,'pbr_textures':78,'source_blends':13,
  'zip_files':0,'texture_size':2048,'unique_guids':len(all_guids),'max_path_length':max(map(len,paths)),
  'checks':checks,'scope':'Blender FBX roundtrip, file completeness, metadata and texture linkage; Unity runtime not tested'}
 (SOURCE/'package-validation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 return result

if __name__=='__main__': print(json.dumps(validate(),indent=2))
