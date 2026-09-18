"""Export and verify the launchpad authored via Blender MCP."""
import bpy, json, math, os, bmesh
from pathlib import Path
from mathutils import Vector
ROOT=Path(r'C:\workspace\2-PJT\S15P21B202-571-art')
NAME='SM_Astra_MainAssemblyLaunchpad'
OUT=ROOT/'METAGAME/Assets/_Project/Models/MainAssemblyLaunchpad'
SOURCE=ROOT/'artifacts/stage1-main-assembly-launchpad'
source=bpy.context.scene
assert source.name.startswith('Astra_MainAssemblyLaunchpad_Authoring')
objs=[o for o in source.objects if o.type=='MESH' and 'export_group' in o]
material=objs[0].data.materials[0]
dg=bpy.context.evaluated_depsgraph_get()
buckets={}
for ob in objs: buckets.setdefault(ob['export_group'],[]).append(ob)
export=bpy.data.scenes.new('Astra_MainAssemblyLaunchpad_FBX')
export.unit_settings.system='METRIC'; export.unit_settings.scale_length=1
root=bpy.data.objects.new(NAME,None); export.collection.objects.link(root)
root['origin']='Center of docking surface. Base below origin.'
root['units']='metres'; root['front']='Blender -Y, FBX -Z forward / Y up'
merged=[]
for key,parts in sorted(buckets.items()):
 vs=[]; fs=[]; uvs=[]; smooth=[]; normals=[]
 for ob in parts:
  ev=ob.evaluated_get(dg); me=ev.to_mesh()
  offset=len(vs); vs.extend(tuple(ob.matrix_world@v.co) for v in me.vertices)
  fs.extend(tuple(offset+i for i in p.vertices) for p in me.polygons)
  uvs.extend(tuple(d.uv) for d in me.uv_layers.active.data)
  smooth.extend(p.use_smooth for p in me.polygons)
  N=ob.matrix_world.to_3x3().inverted().transposed()
  normals.extend(tuple((N@n.vector).normalized()) for n in me.corner_normals)
  ev.to_mesh_clear()
 origin=Vector((0,0,0))
 if key.startswith('Jaw_'):
  origin=Vector(tuple((min(v[a] for v in vs)+max(v[a] for v in vs))*.5 for a in range(3)))
 me=bpy.data.meshes.new(NAME+'_'+key)
 me.from_pydata([tuple(Vector(v)-origin) for v in vs],[],fs); me.update()
 uv=me.uv_layers.new(name='UVMap')
 for d,v in zip(uv.data,uvs): d.uv=v
 for p,sm in zip(me.polygons,smooth): p.use_smooth=sm
 if len(normals)==len(me.loops): me.normals_split_custom_set(normals)
 ob=bpy.data.objects.new(key,me); export.collection.objects.link(ob)
 ob.location=origin; ob.parent=root; me.materials.append(material)
 ob['role']='Independent movable visual jaw' if key.startswith('Jaw_') else 'Static visual geometry'
 merged.append(ob)
bpy.context.window.scene=export
# Generate non-overlapping lightmap UVs per export mesh. Primary palette UVs are intentionally shared.
for ob in merged:
 bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
 ob.data.uv_layers.new(name='LightmapUV'); ob.data.uv_layers.active_index=1
 bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
 bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015,area_weight=.2,correct_aspect=True,scale_to_bounds=True)
 bpy.ops.object.mode_set(mode='OBJECT'); ob.data.uv_layers.active_index=0; ob.data.uv_layers[0].active_render=True
bpy.ops.object.select_all(action='DESELECT')
for ob in merged+[root]: ob.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/(NAME+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},
 apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,mesh_smooth_type='FACE',
 use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False,
 axis_forward='-Z',axis_up='Y',use_custom_props=True)
def metrics(obs):
 points=[o.matrix_world@Vector(c) for o in obs for c in o.bound_box]
 mn=[min(v[i] for v in points) for i in range(3)]; mx=[max(v[i] for v in points) for i in range(3)]
 return {'meshes':len(obs),'vertices':sum(len(o.data.vertices) for o in obs),
 'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in obs),
 'min':mn,'max':mx,'dimensions':[mx[i]-mn[i] for i in range(3)]}
bpy.context.view_layer.update()
before=metrics(merged)
audit=[]
for ob in merged:
 bm=bmesh.new(); bm.from_mesh(ob.data)
 bad=sum(not e.is_manifold for e in bm.edges); deg=sum(f.calc_area()<1e-10 for f in bm.faces)
 if bad or deg: audit.append({'mesh':ob.name,'nonmanifold_edges':bad,'degenerate_faces':deg})
 bm.free()
check=bpy.data.scenes.new('Astra_MainAssemblyLaunchpad_Roundtrip')
bpy.context.window.scene=check
bpy.ops.import_scene.fbx(filepath=str(OUT/(NAME+'.fbx')),use_image_search=True)
bpy.context.view_layer.update()
imported=[o for o in check.objects if o.type=='MESH']; after=metrics(imported)
missing=[]; refs=[]
for ob in imported:
 assert len(ob.data.uv_layers)==2,ob.name
 for m in ob.data.materials:
  if m and m.use_nodes:
   for n in m.node_tree.nodes:
    if n.type=='TEX_IMAGE' and n.image:
     p=bpy.path.abspath(n.image.filepath); refs.append(p)
     if not os.path.isfile(p): missing.append(p)
assert before['meshes']==after['meshes'] and before['triangles']==after['triangles']
assert max(abs(a-b) for a,b in zip(before['dimensions'],after['dimensions']))<.002
assert not missing,missing
assert not audit,audit
assert len(set(refs))>=4,'Missing material texture connections'
report={'asset':NAME,'material':material.name,'components':len(objs),'before_export':before,'after_reimport':after,
 'texture_references':sorted(set(refs)),'missing_textures':missing,'geometry_audit':audit,
 'uv_layers':['UVMap (shared palette atlas)','LightmapUV (non-overlapping per mesh)'],
 'roundtrip':'PASS','pivot':'Docking plane center z=0 in Blender','front':'Blender -Y',
 'gameplay_integration':'Not performed; no collider or animation authored. Four separate movable jaw meshes.'}
(SOURCE/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.context.window.scene=source
for n in material.node_tree.nodes:
 if n.type=='TEX_IMAGE' and n.image: n.image.pack()
bpy.data.libraries.write(str(SOURCE/(NAME+'.blend')),{source},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
result=report

