"""Follow the Blender geometry repair with a vertex-only FBX update.

Preserves the original IDs, triangulation, UVs, normals and texture connections.
The repair translates complete upper bevels; their face normals stay unchanged.
"""
from pathlib import Path
from io_scene_fbx import parse_fbx, encode_bin
import json

root = Path(r'C:\workspace\2-PJT\S15P21B202')
out = root / 'artifacts/observatory-stairs-fix'
target = root / 'METAGAME/Assets/_Project/Models/LongWalkingTrail/07-observatory-entry-stairs/SM_Astra_ObservatoryEntryStairs.fbx'
original, version = parse_fbx.parse(str(out/'SM_Astra_ObservatoryEntryStairs.fbx.before'))
updated, _ = parse_fbx.parse(str(target))
def vertices(tree):
    objects = next(e for e in tree.elems if e.id == b'Objects')
    geometry = next(e for e in objects.elems if e.id == b'Geometry')
    return next(e for e in geometry.elems if e.id == b'Vertices')
old = vertices(original)
new = vertices(updated)
changes = [(i, a, b) for i, (a,b) in enumerate(zip(old.props[0], new.props[0])) if a != b]
assert len(old.props[0]) == len(new.props[0])
assert len(changes) == 112
assert all(i % 3 == 2 and abs((a-b)-.01) < 1e-6 for i,a,b in changes)
methods = dict(zip('YCIFDLSRfdilbc', ['add_int16','add_char','add_int32','add_float32',
    'add_float64','add_int64','add_string','add_bytes','add_float32_array',
    'add_float64_array','add_int32_array','add_int64_array','add_bool_array','add_byte_array']))
methods.update(B='add_bool', Z='add_int8')
def convert(node):
    elem = encode_bin.FBXElem(node.id)
    props = new.props if node is old else node.props
    for typ, value in zip(node.props_type, props):
        getattr(elem, methods[chr(typ)])(value)
    elem.elems = [convert(child) for child in node.elems]
    return elem
encode_bin.write(str(target), convert(original), version)
check, _ = parse_fbx.parse(str(target))
def compare(a,b,path=''):
    assert a.id == b.id and a.props_type == b.props_type
    assert len(a.elems) == len(b.elems)
    if a is old:
        assert b.props == new.props
    elif a.id not in (b'FileId', b'CreationTime', b'CreationTimeStamp') and 'CreationTimeStamp' not in path:
        assert a.props == b.props, path + '/' + str(a.id)
    for x,y in zip(a.elems,b.elems):
        compare(x,y,path+'/'+a.id.decode())
compare(original,check)
report = json.loads((out/'validation.json').read_text())
report['original_fbx_structure_preserved'] = True
report['changed_fbx_vertex_coordinates'] = len(changes)
(out/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
result = report
