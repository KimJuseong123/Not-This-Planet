"""Bake procedural base-color maps in Blender, export actual FBX .fbm dependencies.
Run only via the connected Blender MCP. Does not clear user scenes.
"""
import bpy, json, importlib.util, traceback
from io_scene_fbx import parse_fbx
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'METAGAME/Assets/_Project/Models/Stage2'
ART=ROOT/'artifacts/stage2-models'
TEX=OUT/'Textures'

def module():
    spec=importlib.util.spec_from_file_location('stage2_assets',ROOT/'tools/blender/Stage2/build_assets.py')
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def bake_palette():
    m=module();m.setup_materials();TEX.mkdir(exist_ok=True)
    previous=bpy.context.window.scene
    scene=bpy.data.scenes.new('S2_TextureBake_Workshop');bpy.context.window.scene=scene
    scene.render.engine='CYCLES';scene.cycles.samples=1
    scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
    scene.render.bake.margin=8
    bpy.ops.mesh.primitive_plane_add(size=2);plane=bpy.context.object
    palette=[]
    for key,mat in m.MAT.items():
        color=tuple(mat.diffuse_color)
        bake=bpy.data.materials.new('S2_Bake_'+key);bake.use_nodes=True
        n=bake.node_tree.nodes;l=bake.node_tree.links
        bs=next(x for x in n if x.type=='BSDF_PRINCIPLED')
        uv=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=18
        noise.inputs['Detail'].default_value=3;noise.inputs['Roughness'].default_value=.7
        l.new(uv.outputs['UV'],noise.inputs['Vector'])
        ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.23;ramp.color_ramp.elements[1].position=.8
        factor=.77 if key not in ['Glass','Lamp'] else .94
        ramp.color_ramp.elements[0].color=tuple(c*factor for c in color[:3])+(1,)
        ramp.color_ramp.elements[1].color=tuple(min(1,c*1.07+.01) for c in color[:3])+(1,)
        l.new(noise.outputs['Fac'],ramp.inputs[0]);l.new(ramp.outputs['Color'],bs.inputs['Base Color'])
        image=bpy.data.images.new('S2_'+key+'_BaseColor',width=512,height=512,alpha=False)
        target=n.new('ShaderNodeTexImage');target.image=image;n.active=target
        plane.data.materials.clear();plane.data.materials.append(bake)
        bpy.context.view_layer.objects.active=plane;plane.select_set(True)
        bpy.ops.object.bake(type='DIFFUSE')
        image.filepath_raw=str(TEX/(image.name+'.png'));image.file_format='PNG';image.save();image.pack()
        mn=mat.node_tree.nodes;ml=mat.node_tree.links
        mb=next(x for x in mn if x.type=='BSDF_PRINCIPLED')
        tex=next((x for x in mn if x.type=='TEX_IMAGE' and x.label=='Stage2 FBM BaseColor'),None) or mn.new('ShaderNodeTexImage')
        tex.label='Stage2 FBM BaseColor';tex.image=image;tex.extension='REPEAT';ml.new(tex.outputs['Color'],mb.inputs['Base Color'])
        palette.append({'material':mat.name,'texture':image.name+'.png','width':512,'height':512})
        plane.data.materials.clear();bpy.data.materials.remove(bake)
    bpy.context.window.scene=previous
    bpy.data.objects.remove(plane,do_unlink=True);bpy.data.scenes.remove(scene)
    (ART/'texture-palette.json').write_text(json.dumps(palette,indent=2),encoding='utf-8')
    return palette

def export_one(asset):
    m=module();scene=bpy.data.scenes['S2_'+asset];bpy.context.window.scene=scene
    collection=bpy.data.collections['S2_'+asset]
    bpy.ops.object.select_all(action='DESELECT')
    for o in collection.objects:o.select_set(True)
    meshes=[o for o in collection.objects if o.type=='MESH'];bpy.context.view_layer.objects.active=meshes[0]
    fbx=OUT/('S2_'+asset+'.fbx')
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        bake_anim=False,add_leaf_bones=False,use_custom_props=True,mesh_smooth_type='FACE',use_triangles=True,
        path_mode='COPY',embed_textures=False)
    expected=sorted({n.image.name+'.png' for o in meshes for mat in o.data.materials if mat for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image})
    folder=fbx.with_suffix('.fbm');missing=[f for f in expected if not (folder/f).is_file()]
    if not expected or missing:raise RuntimeError('Missing FBM textures: '+str(missing))
    bpy.data.libraries.write(str(OUT/'Source~'/('S2_'+asset+'.blend')),{scene},fake_user=True,compress=True)
    qa=m.validate_fbx(asset)
    paths=[]
    def collect(e):
        if e.id==b'RelativeFilename':paths.extend(v.decode('utf-8').replace('\\','/') for v in e.props if isinstance(v,bytes))
        for child in e.elems:collect(child)
    tree,version=parse_fbx.parse(str(fbx));collect(tree)
    relative_ok=bool(paths) and all(p.startswith(folder.name+'/') and (OUT/p).is_file() for p in paths)
    result={'asset':asset,'fbx':fbx.name,'fbm':folder.name,'textures':expected,'relative_texture_paths':sorted(set(paths)),
        'relative_paths_valid':relative_ok,'passed':not missing and qa['passed'] and relative_ok}
    (ART/(asset+'-fbm.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

def start(ids,bake=True):
    key='_stage2_fbm_job'
    if bpy.app.driver_namespace.get(key,{}).get('running'):raise RuntimeError('FBM job already active')
    report={'running':True,'pending':list(ids),'done':[],'errors':[],'bakePending':bake}
    bpy.app.driver_namespace[key]=report
    path=ART/'fbm-progress.json'
    def save():path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    def step():
        if report['bakePending']:
            try:bake_palette();report['bakePending']=False
            except Exception:report['errors'].append(traceback.format_exc());report['running']=False;save();return None
            save();return .25
        if not report['pending']:report['running']=False;save();return None
        asset=report['pending'].pop(0);report['current']=asset;save()
        try:report['done'].append(export_one(asset))
        except Exception:report['errors'].append({'asset':asset,'error':traceback.format_exc()})
        save();return .25
    save();bpy.app.timers.register(step,first_interval=1)
    return {'started':list(ids),'progress':str(path)}
