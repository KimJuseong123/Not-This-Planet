"""Run in Blender through MCP. Meter-sized, centered P01 transport rollers."""
import bpy
import math
import os
from mathutils import Vector

ROOT = 'C:/workspace/2-PJT/S15P21B202'
OUT = ROOT + '/METAGAME/Assets/_Project/Art/AstraP01'
SOURCE = ROOT + '/docs/art-production/astra-ver2/P01'
os.makedirs(OUT, exist_ok=True)
os.makedirs(SOURCE, exist_ok=True)
scene = bpy.data.scenes.new('P01 Transport Rollers')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def material(name, color, metal, rough):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    return m

mats = [material('Body', (.69,.75,.73), .35,.3),
        material('Accent', (.95,.24,.045), .15,.34),
        material('EndCap', (.10,.15,.18), .65,.3),
        material('Steel', (.38,.46,.49), .8,.25)]
variants = [('Standard',3,.28),('Heavy',3.2,.8),('Practice',1.6,.32),('Interior',1.8,.32)]
models = []
for name, length, diameter in variants:
    r, h = diameter/2, length/2
    bevel = min(.014,r*.07)
    band = min(.10,length*.045)
    # Continuous lathed shell. Recessed end disks and annuli never exceed the tread.
    profile = [(-h+bevel*2,0),(-h+bevel*2,r*.36),(-h+bevel*2,r*.70),
               (-h+bevel,r*.78),(-h,r-bevel),(-h+bevel,r),
               (-h+band,r),(-h+band*1.45,r),
               (h-band*1.45,r),(h-band,r),(h-bevel,r),(h,r-bevel),
               (h-bevel,r*.78),(h-bevel*2,r*.70),(h-bevel*2,r*.36),(h-bevel*2,0)]
    verts=[(radius*math.cos(i*math.tau/48),radius*math.sin(i*math.tau/48),z)
           for z,radius in profile for i in range(48)]
    faces=[]
    for j in range(len(profile)-1):
        for i in range(48):
            a=j*48+i; b=j*48+(i+1)%48
            faces.append((a,b,b+48,a+48))
    mesh=bpy.data.meshes.new('P01_'+name+'_Mesh')
    mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new('AST_P01_Roller_'+name,mesh)
    scene.collection.objects.link(obj)
    for m in mats:mesh.materials.append(m)
    for p in mesh.polygons:
        row=p.index//48
        p.material_index = (3 if row in (0,14) else 2 if row in (1,2,12,13) else
                            1 if row in (5,6,8,9) or (name=='Heavy' and row==7) else 0)
        p.use_smooth = row not in (0,1,13,14)
    # Merge pole vertices and recalculate normals for watertight geometry.
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=.00001);bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.export_scene.fbx(filepath=OUT+'/'+obj.name+'.fbx',use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,
        use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
    models.append(obj)

# Separate calibration file, never placed in the gameplay scene.
bpy.ops.mesh.primitive_cube_add(size=1)
cube=bpy.context.object;cube.name='P01_OneMeterCalibration'
bpy.ops.export_scene.fbx(filepath=OUT+'/P01_OneMeterCalibration.fbx',use_selection=True,
    object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True,bake_anim=False)
bpy.data.objects.remove(cube,do_unlink=True)
for i,obj in enumerate(models):
    obj.rotation_euler=(0,math.pi/2,0)
    obj.location=(0,(i-1.5)*1.15,variants[i][2]/2)

world=bpy.data.worlds.new('P01 Studio');scene.world=world;world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
background.inputs[0].default_value=(.09,.12,.16,1)
background.inputs[1].default_value=.5
for loc,power,size in [((1,-3,6),1800,5),((-4,2,4),1400,4)]:
    data=bpy.data.lights.new('P01 Softbox','AREA');data.energy=power;data.shape='DISK';data.size=size
    light=bpy.data.objects.new(data.name,data);scene.collection.objects.link(light);light.location=loc
    light.rotation_euler=(-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('P01 Preview');cam=bpy.data.objects.new('P01 Preview',data)
scene.collection.objects.link(cam);scene.camera=cam;data.type='ORTHO';data.ortho_scale=6
scene.render.engine='CYCLES';scene.cycles.samples=32
scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
for label,loc in [('overview',(5,-7,7)),('front',(0,-8,2)),('side',(8,0,2)),('rear',(0,8,2)),('bottom',(0,0,-8))]:
    cam.location=loc;cam.rotation_euler=(Vector((0,0,.2))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=SOURCE+'/'+label+'.png'
    bpy.ops.render.render(write_still=True)
cam.location=(5,-7,7);cam.rotation_euler=(Vector((0,0,.2))-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.select_all(action='DESELECT');models[0].select_set(True);bpy.context.view_layer.objects.active=models[0]
bpy.ops.wm.save_as_mainfile(filepath=SOURCE+'/AST_P01_TransportRollers.blend')
result={'models':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)} for o in models], 'output':OUT}
