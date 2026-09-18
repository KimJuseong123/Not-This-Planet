"""Finalize authoring scene, grouped FBX, portable PBR package and reimport checks."""
import bpy, json, os, math
from mathutils import Vector
from pathlib import Path
BASE=Path(r"C:\workspace\2-PJT\S15P21B202\artifacts\stage1-hangar")
source=bpy.context.scene
if not source.name.startswith("Astra_Hangar_Production"): raise RuntimeError("Select the authoring scene first")
source.name="Astra_Hangar_Production"
dg=bpy.context.evaluated_depsgraph_get()
src_objects=[o for o in source.objects if o.type=='MESH' and 'surface_atlas_tile' in o]
material=src_objects[0].data.materials[0]
# Keep the 6 textures external and packed in the authoring file.
for node in material.node_tree.nodes:
    if node.type=='TEX_IMAGE':
        node.image.filepath="//AstraHangar.fbm/"+Path(bpy.path.abspath(node.image.filepath)).name
        node.image.pack()
# Save a standalone scene library, preserving the user's initial scene in the live Blender session.
source["asset_notes"]="831 separately editable authored components. Export groups in separate FBX. Static open cargo portal."
bpy.data.libraries.write(str(BASE/"AstraHangar.blend"),{source},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
export=bpy.data.scenes.new("Astra_Hangar_FBX_Export")
export.unit_settings.system='METRIC'; export.unit_settings.scale_length=1
buckets={}
for o in src_objects:
    name=o.users_collection[0].name
    # Movable elements stay separate. They are geometry only; no gameplay animation supplied.
    if o.name.startswith("PersonnelDoor_MOVABLE") or o.name.startswith("Personnel_door_lavender_inset") or o.name.startswith("Personnel_door_cyan_access"):
        name="16_PersonnelDoor_MOVABLE"
    if o.name.startswith(("Crane_trolley_MOVABLE","Hoist_")):
        name="17_HoistTrolley_MOVABLE"
    buckets.setdefault(name,[]).append(o)
export_root=bpy.data.objects.new("AstraHangar",None); export.collection.objects.link(export_root)
export_root["authoring"]="Blender MCP / Stage1 Astra Hangar concept"
export_root["units"]="metres"
merged=[]
for key,objects in sorted(buckets.items()):
    vertices=[]; faces=[]; coords=[]
    for obj in objects:
        ev=obj.evaluated_get(dg); me=ev.to_mesh()
        offset=len(vertices)
        vertices.extend(tuple(obj.matrix_world@v.co) for v in me.vertices)
        faces.extend(tuple(offset+i for i in p.vertices) for p in me.polygons)
        coords.extend(tuple(p.uv) for p in me.uv_layers.active.data)
        ev.to_mesh_clear()
    me=bpy.data.meshes.new("SM_AstraHangar_"+key)
    me.from_pydata(vertices,[],faces); me.update()
    uv=me.uv_layers.new(name="UVMap")
    for data,co in zip(uv.data,coords): data.uv=co
    ob=bpy.data.objects.new("SM_AstraHangar_"+key,me)
    export.collection.objects.link(ob); ob.parent=export_root; me.materials.append(material)
    merged.append(ob)
bpy.context.window.scene=export
# Keep primary atlas UVs. Dedicated automatic lightmap UVs are generated per export mesh.
for ob in merged:
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
    uv2=ob.data.uv_layers.new(name="LightmapUV")
    ob.data.uv_layers.active_index=1
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.012,area_weight=.2,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.data.uv_layers.active_index=0
    ob.data.uv_layers[0].active_render=True
bpy.ops.object.select_all(action='DESELECT')
for o in merged+[export_root]: o.select_set(True)
bpy.context.view_layer.objects.active=export_root
bpy.ops.export_scene.fbx(filepath=str(BASE/"AstraHangar.fbx"),use_selection=True,object_types={'MESH','EMPTY'},
    apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,
    mesh_smooth_type='FACE',use_triangles=True,use_tspace=True,bake_anim=False,
    add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False,axis_forward='-Z',axis_up='Y',use_custom_props=True)
# Snapshot geometry metrics before reimporting.
def metrics(objects):
    points=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    mn=[min(v[i] for v in points) for i in range(3)]
    mx=[max(v[i] for v in points) for i in range(3)]
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
    return {"meshes":len(objects),"vertices":sum(len(o.data.vertices) for o in objects),"triangles":tris,
            "bounds_min":mn,"bounds_max":mx,"dimensions":[mx[i]-mn[i] for i in range(3)]}
bpy.context.view_layer.update()
before=metrics(merged)
# Real FBX import into an isolated scene validates serialization and material links.
check=bpy.data.scenes.new("Astra_Hangar_FBX_Roundtrip_Check")
bpy.context.window.scene=check
bpy.ops.import_scene.fbx(filepath=str(BASE/"AstraHangar.fbx"),use_image_search=True)
bpy.context.view_layer.update()
imported=[o for o in check.objects if o.type=='MESH']
after=metrics(imported)
missing=[]; linked=[]
for ob in imported:
    if len(ob.data.uv_layers)!=2: raise RuntimeError("Lost UV layers: "+ob.name)
    for m in ob.data.materials:
        if not m or not m.use_nodes: continue
        for n in m.node_tree.nodes:
            if n.type=='TEX_IMAGE' and n.image:
                p=bpy.path.abspath(n.image.filepath)
                linked.append(p)
                if not n.image.packed_file and not os.path.isfile(p): missing.append(p)
if before["meshes"]!=after["meshes"] or before["triangles"]!=after["triangles"]: raise RuntimeError("FBX geometry changed unexpectedly")
if max(abs(a-b) for a,b in zip(before["dimensions"],after["dimensions"]))>.01: raise RuntimeError("FBX bounds mismatch")
if missing: raise RuntimeError("Missing texture references")
# Watertightness per evaluated source component and degenerate-face audit.
import bmesh
bad=[]
for o in merged:
    bm=bmesh.new(); bm.from_mesh(o.data)
    count=sum(not e.is_manifold for e in bm.edges)
    degenerate=sum(f.calc_area()<1e-9 for f in bm.faces)
    if count or degenerate: bad.append({"mesh":o.name,"non_manifold_edges":count,"degenerate_faces":degenerate})
    bm.free()
report={"before_export":before,"after_reimport":after,"texture_files":sorted(set(linked)),
        "missing_textures":missing,"geometry_audit":bad,
        "source_components":len(src_objects),"atlas_resolution":2048,
        "units":"metres","export_axes":"-Z forward, Y up","roundtrip":"PASS"}
(BASE/"validation.json").write_text(json.dumps(report,indent=2),encoding='utf8')
bpy.context.window.scene=source
# Model-only source file has all dependencies, no imported duplicate geometry or original user scene.
bpy.data.libraries.write(str(BASE/"AstraHangar.blend"),{source},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
result=report

