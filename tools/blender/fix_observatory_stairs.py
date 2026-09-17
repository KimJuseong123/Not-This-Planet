"""Run via Blender MCP after appending the Astra trail source library scenes."""
import bpy
import bmesh
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(r'C:\workspace\2-PJT\S15P21B202')
BASE = ROOT / 'METAGAME/Assets/_Project/Models/LongWalkingTrail'
ASSET = BASE / '07-observatory-entry-stairs'
FBX = ASSET / 'SM_Astra_ObservatoryEntryStairs.fbx'
SOURCE = BASE / 'Source~/Astra_LongWalkingTrail.blend'
OUT = ROOT / 'artifacts/observatory-stairs-fix'
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes['Astra_LWT_ObservatoryEntryStairs']
visual = scene.objects['SM_Astra_ObservatoryEntryStairs']
mesh = visual.data
assert not visual.get('chassis_recess_fixed'), 'Already fixed in this session'
assert len(mesh.vertices) == 2072

def components(me):
    neighbors = [set() for _ in me.vertices]
    for edge in me.edges:
        a, b = edge.vertices
        neighbors[a].add(b)
        neighbors[b].add(a)
    unseen = set(range(len(me.vertices)))
    groups = []
    while unseen:
        stack = [unseen.pop()]
        group = set(stack)
        while stack:
            for other in neighbors[stack.pop()] & unseen:
                unseen.remove(other)
                group.add(other)
                stack.append(other)
        groups.append(group)
    return groups

groups = components(mesh)
chassis = []
for group in groups:
    coords = [mesh.vertices[i].co for i in group]
    width = max(v.x for v in coords) - min(v.x for v in coords)
    if abs(width - 3.175) < 1e-5:
        chassis.append((max(v.z for v in coords), group))
chassis.sort(key=lambda item: item[0])
assert len(chassis) == 4
for i, (height, group) in enumerate(chassis):
    assert abs(height - (.24 + i * .19)) < 1e-5

protected = [FBX.with_suffix('.fbx.meta'), ASSET/'M_Astra_ObservatoryEntryStairs.mat',
             ROOT/'METAGAME/Assets/Scenes/Stage1_Astra.unity']
protected += list((ASSET/'Colliders').glob('*.*'))
hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
for p in (FBX,):
    backup = OUT / (p.name + '.before')
    assert not backup.exists(), 'Preserve existing backup'
    shutil.copy2(p, backup)

before = [tuple(v.co) for v in mesh.vertices]
uv_before = [tuple(x.uv) for x in mesh.uv_layers.active.data]
changed = set()
for i, (height, group) in enumerate(chassis):
    # Keep the lower bevel and base fixed; translate the entire upper bevel.
    for index in group:
        v = mesh.vertices[index]
        if v.co.z > height / 2:
            v.co.z -= .01
            changed.add(index)
    part = scene.objects['ObservatoryEntryStairs_Step_structural_' + str(i)]
    assert len(part.data.vertices) == 8
    # Match the generator's reduced cube height, preserving bevel width.
    part.location.z -= .005
    for v in part.data.vertices:
        v.co.z += -.005 if v.co.z > 0 else .005
    part.data.update()
mesh.update()
bpy.context.window.scene = scene
bpy.context.view_layer.update()
assert uv_before == [tuple(x.uv) for x in mesh.uv_layers.active.data]
assert all(tuple(v.co) == before[v.index] for v in mesh.vertices if v.index not in changed)
bm = bmesh.new()
bm.from_mesh(mesh)
assert all(e.is_manifold for e in bm.edges)
assert all(f.calc_area() > 1e-10 for f in bm.faces)
bm.free()
for i, (height, group) in enumerate(chassis):
    assert abs(max(mesh.vertices[j].co.z for j in group) - (height-.01)) < 1e-6

# Preserve Unity's model, mesh and material names and original export settings.
assert mesh.name == 'SM_Astra_ObservatoryEntryStairs.004'
assert mesh.materials[0].name == 'M_SM_Astra_ObservatoryEntryStairs.004'
for image in bpy.data.images:
    if image.name.startswith('SM_Astra_ObservatoryEntryStairs_'):
        candidate = ASSET / 'SM_Astra_ObservatoryEntryStairs.fbm' / (image.name.split('.png')[0] + '.png')
        if candidate.exists():
            image.filepath = str(candidate)
bpy.ops.object.select_all(action='DESELECT')
visual.hide_set(False)
visual.select_set(True)
bpy.context.view_layer.objects.active = visual
bpy.ops.export_scene.fbx(filepath=str(FBX), use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', use_mesh_modifiers=True,
    mesh_smooth_type='FACE', use_triangles=True, use_tspace=True, bake_anim=False,
    add_leaf_bones=False, path_mode='RELATIVE', embed_textures=False,
    axis_forward='-Z', axis_up='Y', use_custom_props=True)

# Re-import the delivered file into a separate scene and compare its world geometry.
check = bpy.data.scenes.new('ObservatoryStairs_ExportCheck')
bpy.context.window.scene = check
bpy.ops.import_scene.fbx(filepath=str(FBX), use_image_search=True)
bpy.context.view_layer.update()
imported = [o for o in check.objects if o.type == 'MESH']
assert len(imported) == 1
actual = imported[0]
assert len(actual.data.vertices) == len(mesh.vertices)
expected_coords = sorted(tuple(round(x, 5) for x in v.co) for v in mesh.vertices)
actual_coords = sorted(tuple(round(x, 5) for x in actual.matrix_world @ v.co) for v in actual.data.vertices)
assert expected_coords == actual_coords, 'Export changed dimensions or axes'
assert len(actual.data.polygons) == 4012
assert len(actual.data.uv_layers) >= 1
for p in protected:
    assert hashlib.sha256(p.read_bytes()).hexdigest() == hashes[str(p)], str(p)
bpy.context.window.scene = scene
for obj in list(check.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.data.scenes.remove(check)
visual['chassis_recess_fixed'] = True
fixed_source = BASE / 'Source~/ObservatoryEntryStairs_Fixed.blend'
assert not fixed_source.exists(), 'Preserve existing source'
bpy.data.libraries.write(str(fixed_source), {scene}, path_remap='RELATIVE_ALL', fake_user=True, compress=True)
result = {'status':'PASS', 'modified_vertices':len(changed), 'chassis_recess_m':.01,
          'steps':4, 'vertices':len(mesh.vertices), 'triangles':4012,
          'uv_unchanged':True, 'non_chassis_vertices_unchanged':True,
          'closed_mesh':True, 'fbx_roundtrip':'PASS', 'protected_files_unchanged':True,
          'original_source_preserved':True, 'fixed_source':str(fixed_source),
          'runtime_visual_check':'not performed'}
(OUT/'validation.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
