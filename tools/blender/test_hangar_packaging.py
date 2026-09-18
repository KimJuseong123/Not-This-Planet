"""Regression cases for phantom texture references and Windows checkout limits."""
import json, shutil, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import package_hangar_related as package
from validate_hangar_related import validate_material

class ReferenceTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory()
  self.addCleanup(self.temp.cleanup)
  self.root=Path(self.temp.name)

 def test_missing_asset_rejected_even_with_meta(self):
  p=self.root/'missing.png'
  Path(str(p)+'.meta').write_text('guid: '+'a'*32+'\n')
  with self.assertRaises(FileNotFoundError): package.guid(p)
  with self.assertRaises(FileNotFoundError): package.write_meta(p,'guid: __GUID__\n')

 def test_lookup_cannot_create_metadata(self):
  p=self.root/'existing.png';p.write_bytes(b'asset')
  with self.assertRaises(FileNotFoundError): package.guid(p)
  self.assertFalse(Path(str(p)+'.meta').exists())
  with patch.object(package,'ROOT',self.root): package.write_meta(p,'guid: __GUID__\n')
  value=package.guid(p)
  package.write_meta(p,'guid: __GUID__\n')
  self.assertEqual(package.guid(p),value)
  Path(str(p)+'.meta').write_text('guid: malformed\n')
  with self.assertRaises(ValueError): package.guid(p)

 def test_validator_rejects_missing_packed_texture_and_wrong_slot(self):
  src=package.OUT/'06-broken-personnel-bridge-1'
  row=json.loads((src/'asset-info.json').read_text())
  folder=self.root/'asset';folder.mkdir()
  shutil.copytree(src/row['textures'],folder/row['textures'])
  shutil.copyfile(src/row['unity_material'],folder/row['unity_material'])
  validate_material(folder,row)
  packed=folder/row['textures']/(row['asset']+'_MS.png')
  contents=packed.read_bytes();packed.unlink()
  with self.assertRaises(FileNotFoundError): validate_material(folder,row)
  packed.write_bytes(contents)
  meta=Path(str(packed)+'.meta');meta_contents=meta.read_bytes();meta.unlink()
  with self.assertRaises(FileNotFoundError): validate_material(folder,row)
  meta.write_bytes(meta_contents)
  mat=folder/row['unity_material'];text=mat.read_text()
  ms=package.guid(packed)
  base=package.guid(folder/row['textures']/(row['asset']+'_BaseColor.png'))
  mat.write_text(text.replace(ms,base))
  with self.assertRaises(ValueError): validate_material(folder,row)

 def test_delivered_paths_include_meta_within_windows_limit(self):
  paths=[p.relative_to(package.ROOT).as_posix() for p in package.OUT.rglob('*')]
  self.assertFalse([p for p in paths if len(p)>180])

 def test_repackage_preserves_material_guids_and_scene_records(self):
  # Exercise the actual packager on a disposable copy, not the user's assets.
  work=package.ROOT/'tmp';work.mkdir(exist_ok=True)
  with tempfile.TemporaryDirectory(dir=work) as temp:
   out=Path(temp)/'HangarRelated'
   shutil.copytree(package.OUT,out)
   originals={p.relative_to(out):p.read_bytes() for p in out.rglob('*.mat')}
   infos={p.relative_to(out):json.loads(p.read_text()) for p in out.rglob('asset-info.json')}
   with patch.object(package,'OUT',out): package.package()
   for relative,content in originals.items(): self.assertEqual((out/relative).read_bytes(),content)
   for relative,info in infos.items():
    updated=json.loads((out/relative).read_text())
    self.assertEqual(updated.get('scene_placement'),info.get('scene_placement'))
    self.assertEqual(updated.get('scene_instances'),info.get('scene_instances'))
    validate_material((out/relative).parent,updated)

if __name__=='__main__': unittest.main()
