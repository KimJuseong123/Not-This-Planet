"""Stage1 assembly launchpad. Run through Blender MCP in a new scene.
Blender: X right, -Y front, Z up. Pivot is center of flat docking surface.
"""
import bpy, math, bmesh, shutil, json
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(r'C:\workspace\2-PJT\S15P21B202-571-art')
NAME='SM_Astra_MainAssemblyLaunchpad'
OUT=ROOT/'METAGAME/Assets/_Project/Models/MainAssemblyLaunchpad'
SOURCE=ROOT/'artifacts/stage1-main-assembly-launchpad'
TEX=OUT/(NAME+'.fbm')
for p in (OUT,SOURCE,TEX): p.mkdir(parents=True,exist_ok=True)
if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.data.scenes.new('Astra_MainAssemblyLaunchpad_Authoring')
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
root=bpy.data.objects.new(NAME,None); scene.collection.objects.link(root)
root['datum']='Origin is docking surface center; base extends below zero. Blender front -Y.'
root['purpose']='Visual assembly/launch cradle. Gameplay integration is separate.'
groups={}; parts=[]
def group(key):
 if key not in groups:
  c=bpy.data.collections.new('Launchpad_'+key); scene.collection.children.link(c); groups[key]=c
 return groups[key]
mat=bpy.data.materials.new('M_Astra_MainAssemblyLaunchpad'); mat.use_nodes=True
nodes=mat.node_tree.nodes; links=mat.node_tree.links
bs=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
images={}
for k in ['BaseColor','Metallic','Roughness','Normal','Emission','MetallicSmoothness']:
 dest=TEX/(NAME+'_'+('MS' if k=='MetallicSmoothness' else k)+'.png')
 shutil.copyfile(ROOT/'METAGAME/Assets/_Project/Art/Buildings/Hangar/AstraHangar.fbm'/('AstraHangar_'+k+'.png'),dest)
 im=bpy.data.images.load(str(dest),check_existing=False)
 im.colorspace_settings.name='sRGB' if k in ('BaseColor','Emission') else 'Non-Color'
 images[k]=im
 if k=='MetallicSmoothness': continue
 n=nodes.new('ShaderNodeTexImage'); n.image=im; n.label=k
 if k=='Normal':
  nm=nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.3
  links.new(n.outputs['Color'],nm.inputs['Color']); links.new(nm.outputs['Normal'],bs.inputs['Normal'])
 else: links.new(n.outputs['Color'],bs.inputs[{'BaseColor':'Base Color','Emission':'Emission Color'}.get(k,k)])
bs.inputs['Emission Strength'].default_value=2
def finish(o,name,key,tile,bevel=.025):
 o.name=name
 for c in list(o.users_collection): c.objects.unlink(o)
 group(key).objects.link(o); o.parent=root; o.data.materials.append(mat)
 o['atlas_tile']=tile; o['export_group']=key
 me=o.data; me.update()
 uv=me.uv_layers.new(name='UVMap') if not me.uv_layers else me.uv_layers[0]
 for p in me.polygons:
  axes=[a for a in range(3) if a!=max(range(3),key=lambda a:abs(p.normal[a]))]
  pts=[me.vertices[me.loops[j].vertex_index].co for j in p.loop_indices]
  lo=[min(v[a] for v in pts) for a in axes]; hi=[max(v[a] for v in pts) for a in axes]
  for j,v in zip(p.loop_indices,pts):
   u=(v[axes[0]]-lo[0])/max(hi[0]-lo[0],1e-6)
   t=(v[axes[1]]-lo[1])/max(hi[1]-lo[1],1e-6)
   uv.data[j].uv=((tile%4+.10+.80*u)/4,(tile//4+.10+.80*t)/4)
 if bevel:
  m=o.modifiers.new('Soft manufactured bevel','BEVEL'); m.width=bevel; m.segments=3
  m=o.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL'); m.keep_sharp=True; m.weight=40
 parts.append(o); return o
def mesh(name,vs,fs,key,tile,bevel=.02):
 me=bpy.data.meshes.new(name); me.from_pydata(vs,[],fs); me.update()
 bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(me); bm.free()
 ob=bpy.data.objects.new(name,me); scene.collection.objects.link(ob)
 return finish(ob,name,key,tile,bevel)
def box(name,loc,dim,key='01_Chassis',tile=0,bevel=.03):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
 ob=bpy.context.object; ob.scale=dim; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(ob,name,key,tile,min(bevel,min(dim)*.24))
def cylinder(name,loc,r,depth,key,tile,verts=64):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc)
 return finish(bpy.context.object,name,key,tile,min(.012,depth*.18))
def arc(name,rin,rout,z0,z1,a0,a1,key,tile,n=12,bevel=.012,rtop=None):
 # Closed annular sector, optional tapered outer wall.
 routtop=rout if rtop is None else rtop
 vs=[]
 for z,ro in [(z0,rout),(z1,routtop)]:
  for r in [rin,ro]:
   vs.extend((r*math.cos(a0+(a1-a0)*i/n),r*math.sin(a0+(a1-a0)*i/n),z) for i in range(n+1))
 k=n+1; fs=[]
 for i in range(n):
  fs.extend([(i,i+1,k+i+1,k+i),(2*k+i,3*k+i,3*k+i+1,2*k+i+1),
             (i,2*k+i,2*k+i+1,i+1),(k+i,k+i+1,3*k+i+1,3*k+i)])
 fs.extend([(0,k,3*k,2*k),(n,2*k+n,3*k+n,k+n)])
 return mesh(name,vs,fs,key,tile,bevel)
def prism_x(name,yz,x0,x1,key,tile,bevel=.025):
 n=len(yz); vs=[(x,y,z) for x in (x0,x1) for y,z in yz]
 fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 return mesh(name,vs,fs,key,tile,bevel)
def rotate_set(objs,angle,translation=(0,0,0)):
 M=Matrix.Translation(Vector(translation))@Matrix.Rotation(angle,4,'Z')
 for ob in objs: ob.matrix_world=M@ob.matrix_world
def bolt(x,y,z,key='02_Deck',r=.035):
 cylinder('Recessed fastener',(x,y,z),r,.012,key,8,12)
 cylinder('Hex head',(x,y,z+.008),r*.65,.012,key,2,6)
def face_lamp(name,x,y,z,w,h,key='03_Fascia',tile=5):
 box(name+' socket',(x,y,z),(w+.12,.055,h+.1),key,3,.025)
 box(name+' rim',(x,y-.035,z),(w+.065,.028,h+.05),key,2,.012)
 box(name+' lens',(x,y-.055,z),(w,.022,h),key,tile,.008)

# Low solid body, split armored perimeter and flush central floor.
cylinder('Continuous lower chassis',(0,0,-.34),3.80,.28,'01_Chassis',8,96)
cylinder('Structural deck bed',(0,0,-.12),3.72,.20,'01_Chassis',3,96)
cylinder('Flat central assembly support',(0,0,-.056),2.16,.112,'02_Deck',1,96)
# Fine radial seams between four removable central panels, no hole in center.
for a in [0,math.pi/2]:
 ob=box('Central deck seam',(0,0,.001),(4.25,.012,.003),'02_Deck',10,.0005); ob.rotation_euler.z=a
for s in [-1,1]:
 for t in [-1,1]: bolt(s*1.40,t*1.40,.001,r=.023)
for r in [2.205,2.43]:
 for i in range(16):
  a=i*math.tau/16; b=(i+1)*math.tau/16
  arc('Induction channel',r-.050,r+.05,-.028,.009,a+.004,b-.004,'04_Induction',3,6,.002)
  arc('Cyan induction strip',r-.022,r+.022,-.013,.014,a+.012,b-.012,'04_Induction',5,6,.002)
for i in range(16):
 a=i*math.tau/16; b=(i+1)*math.tau/16; mid=(a+b)/2
 arc('Lavender inner collar',2.25,2.38,-.06,.005,a+.008,b-.008,'02_Deck',1,6,.004)
 arc('Ivory segmented deck',2.50,3.56,-.10,.035,a+.007,b-.007,'02_Deck',0,6,.015)
 arc('Titanium deck lip',3.56,3.65,-.16,-.005,a+.006,b-.006,'03_Fascia',2,6,.01)
 arc('Sloped lavender perimeter panel',3.53,3.88,-.45,-.03,a+.008,b-.008,'03_Fascia',1,6,.025,3.64)
 for r in [2.62,3.42]: bolt(r*math.cos(mid),r*math.sin(mid),.042,r=.025)
 # Recessed lights in every other outer armor panel.
 if i%2==0:
  start=len(parts)
  face_lamp('Perimeter guidance',0,-3.76,-.25,.42,.10)
  rotate_set(parts[start:],mid+math.pi/2)
# Radial service seams, brackets around chassis.
for i in range(8):
 a=i*math.tau/8
 start=len(parts)
 box('Chassis reinforced rib',(0,-3.72,-.32),(.16,.23,.35),'01_Chassis',0,.035)
 rotate_set(parts[start:],a)
# Main front entry wedge meets support floor at top.
prism_x('Front access ramp',[(-4.42,-.45),(-3.15,-.45),(-3.15,.045),(-3.48,.045),(-4.42,-.39)],-.78,.78,'05_Entry',0,.035)
prism_x('Ramp lavender tread',[(-4.29,-.345),(-3.46,.056),(-3.27,.056),(-3.27,.075),(-3.49,.075),(-4.30,-.328)],-.64,.64,'05_Entry',1,.005)
for j in range(7):
 y=-4.22+j*.108; z=-.307+j*.052
 ob=box('Ramp traction groove',(0,y,z),(1.19,.015,.008),'05_Entry',8,.002); ob.rotation_euler.x=.45

# Four diagonal low docking mechanisms. Each movable jaw has its own export group.
for i,angle in enumerate([math.radians(45),math.radians(135),math.radians(225),math.radians(315)]):
 key='Jaw_%02d_MOVABLE'%(i+1); start=len(parts)
 box('Jaw rail bed',(0,-3.0,.055),(.72,1.10,.105),'06_JawRails',8,.025)
 for x in [-.22,.22]:
  box('Jaw titanium runner',(x,-2.98,.13),(.085,.99,.07),'06_JawRails',2,.013)
  box('Jaw rail recess',(x,-2.98,.173),(.024,.82,.02),'06_JawRails',3,.003)
 # Local front is toward center (+Y).
 prism_x('Dock jaw armored casing',[(-3.45,.12),(-2.62,.12),(-2.53,.23),(-2.68,.49),(-3.18,.55),(-3.45,.33)],-.39,.39,key,0,.055)
 box('Jaw lavender back plate',(0,-3.453,.265),(.58,.035,.23),key,1,.024)
 box('Jaw top mechanical recess',(0,-2.97,.545),(.28,.49,.03),key,3,.009)
 box('Jaw piston cover',(0,-3.03,.573),(.19,.35,.04),key,2,.015)
 box('Jaw lock cyan window',(0,-2.76,.510),(.20,.10,.019),key,5,.004)
 face_lamp('Dock lock status',0,-3.49,.42,.21,.065,key,6)
 for x in [-.29,.29]:
  ob=cylinder('Jaw hinge axle',(x,-2.73,.25),.10,.11,key,2,16); ob.rotation_euler.y=math.pi/2
  box('Jaw contact rubber',(x,-2.56,.24),(.13,.09,.19),key,11,.015)
 rotate_set(parts[start:],angle)

# Twin rear stabilizer pylons, outward tilt and recessed luminous core.
for s in [-1,1]:
 key='07_Pylon_'+('Left' if s<0 else 'Right'); x=s*2.52; y=2.04
 box('Pylon foundation',(x,y,.18),(1.08,1.04,.42),key,8,.08)
 box('Pylon ivory pedestal',(x,y,.44),(1.0,.95,.52),key,0,.09)
 box('Pedestal front inset',(x,y-.48,.42),(.61,.042,.31),key,1,.025)
 bolt(x-.34,y-.33,.712,key,.035); bolt(x+.34,y-.33,.712,key,.035)
 start=len(parts)
 # Pylon built vertical locally then rotates outward around pedestal.
 box('Pylon graphite core',(0,0,1.60),(.72,.67,2.06),key,8,.06)
 for dx in [-.37,.37]:
  box('Pylon ivory edge',(dx,0,1.60),(.19,.74,2.18),key,0,.06)
 box('Pylon lavender front armor',(0,-.35,1.60),(.53,.07,1.96),key,1,.034)
 box('Pylon lavender rear armor',(0,.35,1.60),(.53,.07,1.96),key,1,.034)
 box('Pylon top cap',(0,0,2.69),(.83,.73,.16),key,2,.045)
 # One long cyan slot plus amber cap.
 box('Pylon vertical light recess',(-s*.16,-.405,1.62),(.14,.04,1.47),key,3,.017)
 box('Pylon cyan core',(-s*.16,-.431,1.62),(.072,.018,1.35),key,5,.007)
 face_lamp('Pylon amber beacon',0,-.413,2.45,.20,.115,key,6)
 for z in [.81,1.25,1.95,2.25]:
  box('Pylon rear service rib',(0,.396,z),(.38,.026,.032),key,2,.006)
 # Foot anchored at z .57; tilt top outward by ~.37m.
 M=Matrix.Translation((x,y,.57))@Matrix.Rotation(s*.15,4,'Y')@Matrix.Translation((0,0,-.57))
 for ob in parts[start:]: ob.matrix_world=M@ob.matrix_world

# Rear low service assembly with louver vents and removable rear hatch.
key='08_RearService'
box('Rear service chassis',(0,2.97,.36),(2.17,.80,.83),key,8,.08)
box('Rear ivory cabinet',(0,2.96,.42),(2.27,.76,.83),key,0,.08)
box('Rear cabinet lavender front',(0,2.55,.40),(1.92,.06,.65),key,1,.04)
box('Rear service hatch',(0,3.37,.42),(1.91,.045,.67),key,1,.04)
for x in [-.68,.68]:
 for z in [.20,.64]: box('Hatch hinge',(x,3.410,z),(.16,.04,.075),key,2,.012)
face_lamp('Rear service ready',0,2.50,.71,.28,.09,key,6)

# Front recessed central service grille.
box('Cabinet front vent recess',(0,2.503,.36),(1.30,.025,.32),key,3,.015)
for j in range(4):
 box('Cabinet front vent louver',(0,2.48,.245+j*.076),(1.20,.045,.035),key,2,.006)
box('Rear hatch pull',(0,3.425,.42),(.38,.06,.075),key,8,.012)
for s in [-1,1]:
 start=len(parts)
 box('Angled rear vent housing',(s*1.46,2.80,.36),(.70,.62,.69),key,0,.055)
 box('Vent black cavity',(s*1.46,2.468,.38),(.54,.042,.43),key,3,.018)
 for j in range(5):
  ob=box('Recessed cooling louver',(s*1.46,2.429,.215+j*.079),(.49,.075,.035),key,2,.006)
  ob.rotation_euler.x=-.3
 # rotate vent array about its own center toward the dock center
 C=Matrix.Translation((s*1.46,2.80,0)); M=C@Matrix.Rotation(s*.24,4,'Z')@C.inverted()
 for ob in parts[start:]: ob.matrix_world=M@ob.matrix_world
# Thin lavender cabinet cover, not a blocking overhead gantry.
box('Rear cabinet raised lid',(0,3.0,.90),(2.02,.62,.12),key,1,.035)
for x in [-.96,.96]: box('Cabinet lid edging',(x,3,.91),(.085,.67,.13),key,0,.019)

# Front-right control console, screen pitched up and towards arriving player.
key='09_Console'; start=len(parts)
prism_x('Console ivory body',[(-.32,0),(.34,0),(.34,.77),(-.32,.52)],-.37,.37,key,0,.045)
box('Console dark foot',(0,0,-.02),(.78,.72,.12),key,8,.025)
angle=math.atan2(.25,.66)
ob=box('Console screen gasket',(0,0,.676),(.60,.56,.035),key,3,.018); ob.rotation_euler.x=angle
ob=box('Console glass screen',(0,-.005,.703),(.49,.43,.018),key,4,.012); ob.rotation_euler.x=angle
for x in [-.145,0,.145]:
 ob=box('Cyan segmented screen',(x,-.028,.720),(.105,.26,.009),key,5,.004); ob.rotation_euler.x=angle
for j in range(3):
 box('Console tactile key',(-.16+j*.16,-.258,.58),(.09,.06,.018),key,2,.006)
face_lamp('Console power state',0,-.347,.34,.23,.085,key,6)
box('Console front ventilation',(0,-.35,.15),(.33,.023,.075),key,3,.008)
rotate_set(parts[start:],.65,(3.12,-1.25,0))
# Discreet underside pads and utility covers.
for i in range(8):
 a=i*math.tau/8
 box('Underside rubber mount',(3.0*math.cos(a),3.0*math.sin(a),-.49),(.30,.30,.08),'01_Chassis',11,.025)
scene['source_concepts']='docs/assets/concepts/stage1-main-assembly-launchpad'
scene['asset_name']=NAME
scene['notes']='Central support z=0, no overhead obstruction; visual-only jaw mechanisms; no collision mesh.'
bpy.context.view_layer.update()
result={'scene':scene.name,'parts':len(parts),'groups':{k:len(v.objects) for k,v in groups.items()},'output':str(OUT)}

# Final QA refinements: a separate sloped tread, intact jaw group placement and efficient bevels.
tread=next(o for o in scene.objects if o.name=='Ramp lavender tread')
def ramp_surface(y): return .045+(y+3.48)*(.435/.94)
yz=[(-4.28,ramp_surface(-4.28)+.013),(-3.53,ramp_surface(-3.53)+.013),(-3.53,ramp_surface(-3.53)+.036),(-4.28,ramp_surface(-4.28)+.036)]
me=tread.data; me.clear_geometry()
me.from_pydata([(x,y,z) for x in [-.64,.64] for y,z in yz],[],[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
me.update(); uv=me.uv_layers.new(name='UVMap')
for p in me.polygons:
 for j in p.loop_indices: uv.data[j].uv=(.375,.125)
for i,o in enumerate(sorted([o for o in scene.objects if o.name.startswith('Ramp traction groove')],key=lambda o:o.name)):
 y=-4.20+i*.103; o.location.y=y; o.location.z=ramp_surface(y)+.040; o.rotation_euler.x=math.atan(.435/.94)
for o in parts:
 k=o['export_group']
 if k in ['Jaw_02_MOVABLE','Jaw_03_MOVABLE']:
  sign=1 if k=='Jaw_02_MOVABLE' else -1
  o.location+=Vector((-sign*.3394,-.3394,0))
 elif k=='06_JawRails' and o.location.y>1:
  sign=1 if o.location.x>0 else -1
  o.location+=Vector((-sign*.3394,-.3394,0))
 for m in o.modifiers:
  if m.type=='BEVEL':
   m.segments=1 if any(t in o.name for t in ['fastener','Hex head','Induction','induction','collar','deck','perimeter panel','deck lip','groove']) else 2
   if o.name=='Front access ramp': m.width=.008; m.segments=1
bpy.context.view_layer.update()

