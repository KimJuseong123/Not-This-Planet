"""Blender-only contact sheets. Presentation labels are excluded from every FBX."""
import bpy,json,math,os
from mathutils import Vector,Matrix
ROOT='C:/workspace/2-PJT/S15P21B202'
DOC=ROOT+'/docs/art-production/astra-ver2/full-art'
manifest=json.load(open(DOC+'/model-manifest.json',encoding='utf-8'))
source=bpy.data.scenes['Astra authored equipment library']
studio=bpy.data.scenes.new('Astra art review studio');bpy.context.window.scene=studio
world=bpy.data.worlds.new('Astra studio world');studio.world=world;world.use_nodes=True
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs[0].default_value=(.13,.17,.21,1);bg.inputs[1].default_value=.65
studio.render.engine='CYCLES';studio.cycles.samples=20
studio.render.resolution_x=1600;studio.render.resolution_y=1200;studio.render.resolution_percentage=100
studio.render.image_settings.file_format='PNG';studio.view_settings.view_transform='AgX'
camdata=bpy.data.cameras.new('Contact sheet camera');camdata.type='ORTHO'
cam=bpy.data.objects.new('Contact sheet camera',camdata);studio.collection.objects.link(cam);studio.camera=cam
for loc,power,size in [((-6,-4,12),4000,8),((7,3,9),3200,7)]:
 data=bpy.data.lights.new('Gallery softbox','AREA');data.energy=power;data.shape='DISK';data.size=size
 obj=bpy.data.objects.new(data.name,data);studio.collection.objects.link(obj);obj.location=loc
 obj.rotation_euler=(-obj.location).to_track_quat('-Z','Y').to_euler()
labelmat=bpy.data.materials.new('Review captions');labelmat.diffuse_color=(.85,.92,.95,1)
labelmat.use_nodes=True;p=next(n for n in labelmat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(.85,.92,.95,1)
groups=[('T-tools',[m for m in manifest['models'] if m['category']=='T']),
 ('P-cargo',[m for m in manifest['models'] if m['category']=='P']),
 ('M-mechanisms',[m for m in manifest['models'] if m['category']=='M']),
 ('E01-E03-stations',[m for m in manifest['models'] if m['category']=='E' and any(m['key'].startswith('AST_E0'+str(i)) for i in (1,2,3))]),
 ('E04-E08-base',[m for m in manifest['models'] if m['category']=='E' and not any(m['key'].startswith('AST_E0'+str(i)) for i in (1,2,3))])]
for label,specs in groups:
 made=[];cols=3 if label=='T-tools' else 5;rows=math.ceil(len(specs)/cols)
 for i,spec in enumerate(specs):
  original=bpy.data.objects.get(spec['key']);assert original is not None,spec['key']
  children=[o for o in original.children_recursive if o.type=='MESH']
  verts=[original.matrix_world.inverted()@o.matrix_world@v.co for o in children for v in o.data.vertices]
  low=Vector(tuple(min(v[k] for v in verts) for k in range(3)));high=Vector(tuple(max(v[k] for v in verts) for k in range(3)))
  center=(low+high)*.5;scale=1.8/max(high-low)
  group=bpy.data.objects.new('Review '+spec['key'],None);studio.collection.objects.link(group);made.append(group)
  group.location=((i%cols-(cols-1)/2)*3.0,((rows-1)/2-i//cols)*3.0,0)
  # Tilt each thumbnail independently so labels and grid remain aligned.
  rotation=Matrix.Rotation(math.radians(20),4,'Z')@Matrix.Rotation(math.radians(30),4,'X')
  if spec['category']=='E':rotation=Matrix.Rotation(math.radians(200),4,'Z')@Matrix.Rotation(math.radians(30),4,'X')
  for o in children:
   dup=bpy.data.objects.new(o.name+' review',o.data);studio.collection.objects.link(dup);made.append(dup)
   local=original.matrix_world.inverted()@o.matrix_world
   dup.matrix_world=Matrix.Translation(group.location+Vector((0,0,1.1)))@Matrix.Scale(scale,4)@rotation@Matrix.Translation(-center)@local
  font=bpy.data.curves.new('Review caption','FONT');font.body=spec['key'][4:];font.align_x='CENTER';font.size=.16;font.extrude=0
  caption=bpy.data.objects.new('Review caption',font);studio.collection.objects.link(caption);made.append(caption)
  caption.location=group.location+Vector((0,-1.35,.02));font.materials.append(labelmat)
 cam.location=(0,0,30);cam.rotation_euler=(Vector((0,0,.2))-cam.location).to_track_quat('-Z','Y').to_euler()
 camdata.ortho_scale=max(cols*3.15,rows*3.1*1.33)
 studio.render.filepath=DOC+'/'+label+'.png';bpy.ops.render.render(write_still=True)
 for o in made:bpy.data.objects.remove(o,do_unlink=True)
bpy.context.window.scene=source
result={'contact_sheets':[x[0]+'.png' for x in groups]}
