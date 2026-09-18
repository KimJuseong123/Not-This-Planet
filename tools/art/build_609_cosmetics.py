"""Run through Blender MCP. Coordinates authored as Unity x/right, y/up, z/front."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'METAGAME/Assets/_Project/Models/Characters/Cosmetics609'
OUT.mkdir(parents=True, exist_ok=True)
# Rebuilding replaces only this tool's generated scene; user scenes are untouched.
for previous in list(bpy.data.scenes):
    if previous.get('cosmetics609_generated'):
        for obj in list(previous.objects): bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.scenes.remove(previous)
scene = bpy.data.scenes.new('Cosmetics609')
scene['cosmetics609_generated'] = True
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
colors = [(0.08,.13,.21,1),(.83,.91,.94,1),(.1,.8,.85,1),(1,.55,.13,1),(.65,.4,.86,1),(.96,.28,.4,1),(.3,.72,.43,1),(1,.88,.45,1)]
atlas = bpy.data.images.new('609_Palette', width=64, height=8)
atlas.pixels = [v for y in range(8) for x in range(64) for v in colors[x//8]]
mat = bpy.data.materials.new('609_Palette'); mat.use_nodes=True
node = mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=atlas; node.interpolation='Closest'
mat.node_tree.links.new(node.outputs['Color'],next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Base Color'])
next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Roughness'].default_value=.38
parts=[]; items=[]
def pos(p): return (p[0],-p[2],p[1])
def finish(o,c):
    o.data.materials.clear(); o.data.materials.append(mat)
    uv=o.data.uv_layers.active or o.data.uv_layers.new()
    uv.name='PaletteUV'
    for loop in uv.data: loop.uv=((c+.5)/8,.5)
    parts.append(o)
    return o
def ball(p,s,c):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,location=pos(p))
    o=bpy.context.object; o.scale=(s[0],s[2],s[1]); return finish(o,c)
def box(p,s,c):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos(p)); o=bpy.context.object; o.scale=(s[0],s[2],s[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    mod=o.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=min(s)*.18; mod.segments=2
    bpy.ops.object.modifier_apply(modifier=mod.name); return finish(o,c)
def rod(a,b,r,c):
    a,b=Vector(pos(a)),Vector(pos(b)); d=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=r,depth=d.length,location=(a+b)/2)
    o=bpy.context.object; o.rotation_euler=d.to_track_quat('Z','Y').to_euler(); return finish(o,c)
def ring(p,r,t,c):
    bpy.ops.mesh.primitive_torus_add(major_segments=32,minor_segments=8,location=pos(p),major_radius=r,minor_radius=t,rotation=(math.pi/2,0,0))
    return finish(bpy.context.object,c)
def star(p,r,c):
    # Extruded five-point insignia, front-facing.
    verts=[]
    for z in [-.008,.008]:
        for i in range(10):
            a=math.pi/2+i*math.pi/5; q=r if i%2==0 else r*.46
            verts.append(pos((p[0]+math.cos(a)*q,p[1]+math.sin(a)*q,p[2]+z)))
    faces=[tuple(reversed(range(10))),tuple(range(10,20))]+[(i,(i+1)%10,(i+1)%10+10,i+10) for i in range(10)]
    mesh=bpy.data.meshes.new('Star'); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new('Star',mesh); scene.collection.objects.link(o); return finish(o,c)
def export(name,slot,index):
    global parts
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); o=bpy.context.object; o.name=name
    scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    fbm=OUT/(name+'.fbm'); fbm.mkdir(exist_ok=True)
    atlas.filepath_raw=str(fbm/'Palette.png'); atlas.file_format='PNG'; atlas.save()
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,path_mode='COPY',embed_textures=False)
    o.data.calc_loop_triangles()
    items.append({'name':name,'slot':slot,'index':index,'triangles':len(o.data.loop_triangles),'dimensions':list(o.dimensions)})
    o.hide_render=True; o.hide_set(True); parts=[]
    return o

# Side-mounted hardware leaves the rabbit ears and face free.
box((.29,.10,0),(.10,.13,.12),0); rod((.29,.16,0),(.31,.40,0),.015,1); ball((.31,.43,0),(.043,.043,.043),2); ring((.31,.43,.005),.051,.006,7)
export('Helmet01_SurveyAntenna','Helmet',1)
for s in [-1,1]:
    ball((s*.32,0,0),(.065,.10,.105),0); ball((s*.365,0,0),(.025,.067,.072),2)
rod((.35,-.03,.05),(.30,-.12,.24),.013,1); ball((.30,-.12,.25),(.025,.025,.04),0)
export('Helmet02_CommsHeadset','Helmet',2)
box((-.27,.17,.15),(.13,.09,.11),0); ball((-.27,.17,.217),(.049,.032,.02),7); box((-.27,.17,.24),(.06,.035,.015),1)
export('Helmet03_Searchlight','Helmet',3)
for s in [-1,1]:
    box((s*.26,.20,0),(.12,.07,.12),0)
    rod((s*.26,.23,0),(s*.31,.37,0),.012,7); star((s*.31,.40,0),.061,7)
export('Helmet04_StarFeelers','Helmet',4)
box((-.29,.13,0),(.10,.09,.12),1); rod((-.29,.16,0),(-.29,.29,0),.012,6)
for s in [-1,1]:
    leaf=ball((-.29+s*.05,.31,0),(.065,.024,.029),6); leaf.rotation_euler[1]=s*.45
export('Helmet05_BioSprout','Helmet',5)
for s in [-1,1]:
    box((s*.32,.015,-.03),(.08,.13,.15),0)
    for i in range(3): box((s*(.37+i*.018),.045+i*.043,-.055),(.045,.025,.14-i*.025),4 if i%2 else 2)
export('Helmet06_OrbitFins','Helmet',6)
box((-.07,0,.24),(.13,.14,.035),0); ring((-.07,0,.265),.045,.008,7); star((-.07,0,.28),.034,2)
export('Badge01_Explorer','Badge',1)
box((0,0,.24),(.21,.10,.028),0)
for x in [-.05,.05]: star((x,0,.267),.047,7 if x<0 else 2)
export('Badge02_TwinStars','Badge',2)
ring((0,0,.255),.071,.009,7); ball((0,0,.26),(.047,.047,.018),4); rod((-.082,-.026,.285),(.082,.026,.285),.006,2)
export('Badge03_Saturn','Badge',3)
box((0,0,.245),(.14,.16,.035),0); box((0,.013,.269),(.065,.074,.023),3); ball((0,.04,.29),(.023,.023,.011),2)
for s in [-1,1]: star((s*.042,-.04,.274),.021,7)
export('Badge04_Rocket','Badge',4)
box((0,0,.24),(.17,.13,.035),1); box((0,0,.269),(.09,.025,.018),5); box((0,0,.27),(.025,.09,.018),5)
export('Badge05_Rescue','Badge',5)
box((0,0,.24),(.17,.14,.04),0); box((-.025,.017,.269),(.085,.058,.012),2)
for i in range(3): ball((.046,-.034+i*.028,.274),(.008,.008,.006),7)
for i in range(3): box((-.052+i*.025,-.037,.27),(.014,.018,.013),1)
export('Badge06_FlightRecorder','Badge',6)
(OUT/'catalog.json').write_text(json.dumps(items,indent=2))
# Product sheet uses the actual exported geometry, not concept art.
for i,item in enumerate(items):
    o=bpy.data.objects[item['name']]; o.hide_render=False; o.hide_set(False)
    o.location=(i%6*.95,0,-(i//6)*.85)
    if i>=6: o.scale=(2,2,2)
world=bpy.data.worlds.new('609 Studio'); scene.world=world; world.use_nodes=True; next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND').inputs[0].default_value=(.035,.05,.08,1)
bpy.ops.object.camera_add(location=(2.35,-8,2.0)); camera=bpy.context.object; camera.rotation_euler=(Vector((2.35,0,-.15))-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=6.1; scene.camera=camera
for p,power,size in [((1,-3,5),1100,5),((5,1,3),850,3)]:
    bpy.ops.object.light_add(type='AREA',location=p); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; light.rotation_euler=(Vector((2,0,0))-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.render.resolution_x=1600; scene.render.resolution_y=650; scene.render.resolution_percentage=100
preview=ROOT/'docs/assets/concepts/cosmetics609'; preview.mkdir(parents=True,exist_ok=True)
scene.render.filepath=str(preview/'modeled-catalog.png')
atlas.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(preview/'cosmetics609.blend'))
bpy.ops.render.render(write_still=True)
result={'items':items,'preview':scene.render.filepath}
