"""Astra ver2 authored art library. Execute inside Blender via MCP.

All recipe coordinates use Unity metres (X right, Y up, Z forward).
Exported objects have applied transforms; runtime effects remain in Unity.
"""
import bpy, math, json, os
from mathutils import Vector
ROOT='C:/workspace/2-PJT/S15P21B202'
OUT=ROOT+'/METAGAME/Assets/_Project/Art/AstraFullArt'
DOC=ROOT+'/docs/art-production/astra-ver2/full-art'
os.makedirs(OUT,exist_ok=True);os.makedirs(DOC,exist_ok=True)
scene=bpy.data.scenes.get('Astra authored equipment library')
if scene is None:scene=bpy.data.scenes.new('Astra authored equipment library')
else:
 for owned in list(scene.objects):bpy.data.objects.remove(owned,do_unlink=True)
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
palette={
 'Ceramic':((.70,.77,.76),.25,.38),'Graphite':((.055,.085,.11),.4,.4),
 'Steel':((.29,.38,.42),.75,.28),'Grip':((.95,.24,.035),.12,.44),
 'Fuel':((.95,.65,.025),.16,.36),'Blue':((.035,.37,.58),.25,.35),
 'Purple':((.35,.075,.5),.2,.28),'Teal':((.025,.65,.48),.18,.4),
 'Red':((.62,.025,.035),.18,.4),'Lens':((.035,.32,.4),.4,.18),
 'Wood':((.27,.18,.11),0,.68),'Fabric':((.22,.32,.36),0,.9),
 'Rock':((.26,.20,.32),0,.95)}
mats={}
for name,(color,metal,rough) in palette.items():
 m=bpy.data.materials.get('AST_'+name) or bpy.data.materials.new('AST_'+name);m.diffuse_color=(*color,1);m.use_nodes=True
 p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 mats[name]=m
parts=[];models=[];rules=[]
def B(p):return (p[0],-p[2],p[1])
def mesh(name,vs,fs,mat='Ceramic',bevel=0):
 m=bpy.data.meshes.new(name);m.from_pydata([B(v) for v in vs],[],fs);m.update()
 o=bpy.data.objects.new(name,m);scene.collection.objects.link(o);m.materials.append(mats[mat]);parts.append(o)
 if bevel:
  bpy.context.view_layer.objects.active=o;o.select_set(True)
  mod=o.modifiers.new('Soft industrial edges','BEVEL');mod.width=bevel;mod.segments=2
  bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
 if len(fs)==1 or name=='Concave reflector':
  bpy.context.view_layer.objects.active=o;o.select_set(True)
  mod=o.modifiers.new('Physical sheet thickness','SOLIDIFY');mod.thickness=.008;mod.offset=0
  bpy.ops.object.modifier_apply(modifier=mod.name);o.select_set(False)
 return o
def box(name,p,s,mat='Ceramic',bevel=.02):
 if name in {'Inset frame','Face plate','Door armor panel','Recessed side panel','Sealed door','Corner bumper',
             'Flush service seam','Lower service channel','Recessed panel joint','Fold seam','Back seam'} and p[2]!=0:
  p=(p[0],p[1],p[2]+math.copysign(.008,p[2]))
 x,y,z=p;a,b,c=[v/2 for v in s]
 vs=[(x+dx*a,y+dy*b,z+dz*c) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
 return mesh(name,vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)],mat,min(bevel,min(s)*.18))
def cyl(name,p,r,l,mat='Steel',axis='Y',inner=0,n=32):
 # Flat annular ends and smooth sides; hollow cylinders remain open.
 axes={'Y':(0,2,1),'Z':(0,1,2),'X':(1,2,0)};a,b,c=axes[axis]
 rings=[(-l/2,r),(l/2,r),(l/2,inner),(-l/2,inner)]
 vs=[]
 for h,radius in rings:
  for i in range(n):
   v=list(p);v[a]+=math.cos(i*math.tau/n)*radius;v[b]+=math.sin(i*math.tau/n)*radius;v[c]+=h;vs.append(v)
 fs=[]
 for j in range(4):
  for i in range(n):fs.append((j*n+i,j*n+(i+1)%n,((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i))
 o=mesh(name,vs,fs,mat)
 for poly in o.data.polygons:poly.use_smooth=poly.index//n in (0,2)
 return o
def sphere(name,p,s,mat='Lens'):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=B(p))
 o=bpy.context.object;o.name=name;o.scale=(s[0]/2,s[2]/2,s[1]/2)
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 o.data.materials.append(mats[mat]);parts.append(o)
 for poly in o.data.polygons:poly.use_smooth=True
 o.select_set(False);return o
def rod(name,a,b,r,mat='Steel',n=16):
 delta=Vector(B(b))-Vector(B(a));mid=(Vector(B(b))+Vector(B(a)))*.5
 bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=delta.length,location=mid)
 o=bpy.context.object;o.name=name;o.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 o.data.materials.append(mats[mat]);parts.append(o)
 for p in o.data.polygons:p.use_smooth=len(p.vertices)==4
 o.select_set(False);return o
def loop(name,p,w,h,t,mat='Grip',plane='XY'):
 x,y,z=p
 if plane=='XY':pts=[(x-w/2,y-h/2,z),(x-w/2,y+h/2,z),(x+w/2,y+h/2,z),(x+w/2,y-h/2,z)]
 else:pts=[(x-w/2,y,z-h/2),(x-w/2,y,z+h/2),(x+w/2,y,z+h/2),(x+w/2,y,z-h/2)]
 for i in range(4):rod(name+str(i),pts[i],pts[(i+1)%4],t,mat)
def bolts(p,w,h,mat='Steel',r=.025):
 for x in (-w/2,w/2):
  for y in (-h/2,h/2):cyl('Recessed fastener',(p[0]+x,p[1]+y,p[2]),r,.015,mat,'Z',n=8)
def vent(p,w,h,rows=5):
 box('Recessed vent',p,(w,h,.018),'Graphite',.006)
 for i in range(rows):box('Vent louver',(p[0],p[1]-h*.4+i*h*.8/max(1,rows-1),p[2]-.012),(w*.85,h*.07,.018),'Steel',.005)
def banded_beam(s,mat='Steel',axis='X'):
 box('Structural spine',(0,0,0),s,mat)
 k={'X':0,'Y':1,'Z':2}[axis]
 for sign in (-1,1):
  p=[0,0,0];p[k]=sign*s[k]*.42
  size=[v*.98 for v in s];size[k]=s[k]*.08
  box('End sleeve',p,size,'Graphite',.015)
def arrow(p,s,mat='Fuel'):
 x,y,z=p
 poly=[(-.5,-.12),(.05,-.12),(.05,-.32),(.5,0),(.05,.32),(.05,.12),(-.5,.12)]
 vs=[(x+a*s,y+b*s,z) for a,b in poly]
 return mesh('Direction pictogram',vs,[tuple(range(7))],mat)
def plaque(name,p,w,h,mat='Graphite'):
 box(name,p,(w,h,.08),mat,.035);bolts((p[0],p[1],p[2]-.045),w*.86,h*.78,r=min(w,h)*.024)
def start():
 global parts
 parts=[];bpy.ops.object.select_all(action='DESELECT')
def finish(key,category):
 global parts
 # Correct normals, weld poles and export only this asset, never lighting or backgrounds.
 for o in parts:
  bpy.context.view_layer.objects.active=o;o.select_set(True)
  bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
  bpy.ops.mesh.remove_doubles(threshold=.000001);bpy.ops.mesh.normals_make_consistent(inside=False)
  bpy.ops.object.mode_set(mode='OBJECT');o.select_set(False)
 root=bpy.data.objects.new(key,None);scene.collection.objects.link(root)
 for o in parts:o.parent=root;o.select_set(True)
 root.select_set(True);bpy.context.view_layer.objects.active=root
 bpy.ops.export_scene.fbx(filepath=OUT+'/'+key+'.fbx',use_selection=True,object_types={'MESH','EMPTY'},
  axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
  bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
 coords=[o.matrix_world@v.co for o in parts for v in o.data.vertices]
 low=Vector(tuple(min(v[i] for v in coords) for i in range(3)));high=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
 models.append({'key':key,'category':category,'root':root,'size':high-low,'center':(high+low)*.5,
  'triangles':sum(len(p.vertices)-2 for o in parts for p in o.data.polygons)})
def rule(key,target,under='',kind='leaf',keep=False):rules.append(dict(key=key,target=target,under=under,kind=kind,keepMaterial=keep))

# T01--T06: distinct, wordless silhouettes, preserving Unity Model/Muzzle/LED/gauges.
for kind in ['Rocket','Ink','FuelCan','Grapple','Blower','Detector']:
 start()
 if kind=='Rocket':
  cyl('Pressure body',(0,.015,.14),.17,.64,'Ceramic','Z')
  sphere('Rounded nose',(0,.015,.46),(.31,.31,.22),'Blue')
  cyl('Exhaust throat',(0,.015,-.205),.16,.09,'Graphite','Z',inner=.11)
  cyl('Exhaust rim',(0,.015,-.25),.19,.035,'Steel','Z',inner=.135)
  for sign in (-1,1):
   mesh('Stabilizer fin',[(sign*.15,-.1,-.19),(sign*.24,-.1,-.25),(sign*.24,.1,-.25),(sign*.15,.11,.12)],[(0,1,2,3)],'Blue')
  loop('Grip',(0,-.255,-.06),.19,.19,.036)
  cyl('Forward seam',(0,.015,.35),.171,.035,'Blue','Z')
 elif kind=='Ink':
  box('Pump body',(0,-.04,.10),(.34,.32,.5),'Ceramic',.045)
  sphere('Ink reservoir',(0,.21,-.03),(.35,.34,.39),'Purple')
  cyl('Reservoir lid',(0,.366,-.03),.11,.035,'Graphite')
  cyl('Stub nozzle',(0,.015,.425),.115,.25,'Steel','Z',inner=.075)
  cyl('Nozzle guard',(0,.015,.55),.125,.035,'Purple','Z',inner=.075)
  loop('Grip',(0,-.28,-.07),.18,.18,.032)
  for x in (-.18,.18):box('Tank restraint',(x,.18,-.03),(.03,.25,.32),'Graphite')
 elif kind=='FuelCan':
  box('Tank',(0,-.015,.10),(.40,.48,.53),'Fuel',.055)
  for x in (-.2,.2):box('Corner protection',(x,-.015,.10),(.05,.45,.51),'Graphite')
  loop('Closed carry grip',(0,.30,.04),.27,.13,.032,'Grip')
  rod('Pour neck',(.13,.21,.23),(.19,.30,.30),.045,'Fuel')
  cyl('Spout cap',(.19,.31,.30),.054,.045,'Graphite')
  box('Fuel window',(0,-.04,-.173),(.075,.26,.018),'Lens')
  for y in (-.15,-.05,.05):box('Level tick',(.075,y,-.177),(.035,.012,.015),'Ceramic')
 elif kind=='Grapple':
  box('Reel housing',(0,.03,.02),(.37,.34,.42),'Ceramic',.05)
  cyl('Cable spool',(0,.035,-.02),.15,.44,'Graphite','X')
  for x in (-.23,.23):cyl('Spool flange',(x,.035,-.02),.175,.03,'Steel','X')
  for x in [-.17+i*.035 for i in range(10)]:cyl('Wound cable',(x,.035,-.02),.154,.017,'Graphite','X',inner=.14)
  cyl('Hook barrel',(0,.02,.32),.09,.40,'Blue','Z',inner=.052)
  for sign in (-1,1):
   rod('Hook arm',(0,.02,.5),(sign*.2,.02,.59),.033,'Steel')
   rod('Hook return',(sign*.2,.02,.59),(sign*.22,.02,.43),.033,'Steel')
  loop('Grip',(0,-.245,-.08),.18,.19,.035)
 elif kind=='Blower':
  cyl('Fan duct',(0,.075,.08),.26,.63,'Blue','Z',inner=.205)
  for z in (-.245,.40):cyl('Duct lip',(0,.075,z),.28,.04,'Steel','Z',inner=.205)
  cyl('Hub',(0,.075,-.205),.065,.06,'Graphite','Z')
  for i in range(5):
   a=i*math.tau/5
   rod('Guard spoke',(0,.075,-.27),(.2*math.cos(a),.075+.2*math.sin(a),-.27),.011,'Steel')
   a+=.2
   mesh('Fan blade',[(.055*math.cos(a),.075+.055*math.sin(a),-.2),(.18*math.cos(a+.5),.075+.18*math.sin(a+.5),-.2),(.19*math.cos(a+.95),.075+.19*math.sin(a+.95),-.17),(.07*math.cos(a+.9),.075+.07*math.sin(a+.9),-.17)],[(0,1,2,3)],'Teal')
  loop('Grip',(0,-.25,-.06),.19,.18,.032)
 else:
  box('Handle',(0,0,0),(.15,.16,.5),'Grip',.035)
  rod('Detector shaft',(0,-.15,.17),(0,-.2,.97),.032,'Steel')
  box('Display case',(0,.10,.15),(.33,.105,.29),'Graphite',.025)
  box('Display glass',(0,.157,.15),(.24,.009,.20),'Lens',.01)
  # LED at (0,.19,.15) remains the original live Renderer; leave its position open.
  cyl('Open search coil',(0,-.2,.95),.375,.06,'Graphite','Y',inner=.31,n=48)
  rod('Coil bridge',(-.31,-.2,.95),(.31,-.2,.95),.018,'Teal')
  rod('Coil connector',(0,-.2,.70),(0,-.2,1.23),.018,'Teal')
 key='AST_T%02d_%s'%(1+['Rocket','Ink','FuelCan','Grapple','Blower','Detector'].index(kind),kind)
 finish(key,'T');rule(key,{'Rocket':'AstraRocketTool','Ink':'AstraInkLauncher','FuelCan':'AstraFuelCan','Grapple':'AstraGrappleTool','Blower':'AstraAirBlower','Detector':'AstraMineDetector'}[kind],kind='tool')

# Passive cargo props; each keeps its existing collider and physical pivot.
for axis in ('X','Z'):
 start();s=(3,.12,.18) if axis=='X' else (.14,.14,2.6);banded_beam(s,'Steel',axis)
 k=0 if axis=='X' else 2
 for j in range(7):
  p=[0,0,0];p[k]=-s[k]*.37+j*s[k]*.018
  grip=list(s);grip[k]=s[k]*.012
  box('Recessed grip rib',p,grip,'Grip',.006)
 key='AST_P02_Lever'+axis;finish(key,'P')
 rule(key,'Manual lever bar' if axis=='X' else 'Grippable body',under='' if axis=='X' else 'lifting bar')
 if axis=='Z':rule(key,'Grippable body',under='turning lever')
start();cyl('Bearing drum',(0,-.005,0),.225,.29,'Graphite');cyl('Load surface',(0,.145,0),.21,.01,'Steel')
finish('AST_P03_RoundFulcrum','P');rule('AST_P03_RoundFulcrum','Lever fulcrum')
start();box('Solid fulcrum',(0,-.005,0),(.5,.19,.35),'Graphite');box('Bearing top',(0,.095,0),(.42,.01,.29),'Steel')
finish('AST_P03_BlockFulcrum','P')
for n in ['Practice lever fulcrum','Workshop interior lever rest']:rule('AST_P03_BlockFulcrum',n)
for axis in ('X','Z'):
 start();s=(8,.2,1.15) if axis=='X' else (1.1,.18,4.5)
 box('Flat laminated plank',(0,0,0),s,'Wood',.012)
 for sign in (-1,1):
  p=[0,0,0];p[0 if axis=='X' else 2]=sign*s[0 if axis=='X' else 2]*.48
  size=list(s);size[0 if axis=='X' else 2]=.15
  box('Flush end binding',p,size,'Steel',.009)
 # Dark seam strips stay on the side faces, leaving both load surfaces flat.
 for j in (-.3,.3):
  if axis=='X':box('Laminate side seam',(0,j*.2,-.575),(7.6,.008,.002),'Graphite',0)
  else:box('Laminate side seam',(-.55,j*.18,0),(.002,.008,4.2),'Graphite',0)
 key='AST_P04_Plank'+axis;finish(key,'P');rule(key,'^Loose long plank ' if axis=='X' else 'Grippable body',under='' if axis=='X' else 'Practice bridge plank')
start();box('Cargo shell',(0,0,0),(1.2,1.2,1.2),'Ceramic',.06)
for sign in (-1,1):
 box('Recessed side panel',(0,0,sign*.595),(.98,.92,.008),'Blue',.025)
 for x in (-.48,.48):box('Corner bumper',(x,0,sign*.57),(.12,1.16,.06),'Graphite')
 box('Inset lifting recess',(0,.36,sign*.60),(.38,.09,.012),'Graphite')
finish('AST_P05_Crate','P');rule('AST_P05_Crate','Stacking crate');rule('AST_P05_Crate','Grippable body',under='Practice light sample crate')
def deck(key,s,category,accent='Blue'):
 start();box('Structural tray',(0,-s[1]*.05,0),(s[0],s[1]*.9,s[2]),'Graphite',.025)
 box('Flush load plate',(0,s[1]*.45,0),(s[0]*.94,s[1]*.1,s[2]*.94),'Ceramic',.009)
 for x in (-.47,.47):box('Edge identification strip',(s[0]*x,s[1]*.5+.002,0),(s[0]*.025,.002,s[2]*.88),accent,0)
 for i in range(6):box('Non slip inlay',(0,s[1]*.5+.002,(i-2.5)*s[2]*.13),(s[0]*.8,.002,s[2]*.008),'Steel',0)
 finish(key,category)
deck('AST_P06_SledDeck',(3.5,.2,4.4),'P');rule('AST_P06_SledDeck','Low cargo deck')
start();banded_beam((.3,.2,4.3),'Graphite','Z');finish('AST_P06_Runner','P');rule('AST_P06_Runner','Low friction skid')
start();banded_beam((1.8,.15,.15),'Grip');finish('AST_P06_PullGrip','P');rule('AST_P06_PullGrip','Pull handle')
start();banded_beam((.13,.65,4.4),'Steel','Z');finish('AST_P06_SideRail','P');rule('AST_P06_SideRail','Cargo side rail',under='Astra physical toybox')
start()
mesh('Wedge solid',[(-1.3,-.5,-2),(1.3,-.5,-2),(-1.3,-.5,2),(1.3,-.5,2),(-1.3,.5,2),(1.3,.5,2)],[(0,2,3,1),(2,4,5,3),(0,1,5,4),(0,4,2),(1,3,5)],'Steel')
for x in (-1.22,1.22):rod('Flush edge',(x,-.49,-1.94),(x,.49,1.94),.014,'Fuel')
finish('AST_P07_Ramp','P');rule('AST_P07_Ramp','^경사판 ')
start();box('Weighted foot',(0,.04,0),(.44,.18,.44),'Graphite',.025)
rod('Marker mast',(0,.10,0),(0,.90,0),.03,'Steel')
loop('Hollow route flag',(.22,.72,0),.38,.28,.035,'Teal')
finish('AST_P08_RouteMarker','P');rule('AST_P08_RouteMarker','^Carryable survey route marker ',kind='assembly')
start();box('Rubber door stop',(0,0,0),(.5,.4,.8),'Graphite',.035)
for z in (-.32,.32):box('Contact bumper',(0,0,z),(.48,.36,.12),'Fuel')
loop('Stop grip',(0,.30,0),.32,.14,.035)
finish('AST_P09_DoorStop','P');rule('AST_P09_DoorStop','^Airlock maintenance stop ',kind='assembly')
start()
for x in (-.32,0,.32):
 cyl('Charge cartridge',(x,0,0),.155,1.27,'Red','Z');cyl('Recessed end',(x,0,-.638),.12,.015,'Graphite','Z')
for z in (-.36,.36):box('Retaining belt',(0,0,z),(1.02,.43,.075),'Graphite')
cyl('Cable socket',(0,.19,-.48),.048,.08,'Steel','Y',inner=.025)
finish('AST_P10_Charge','P');rule('AST_P10_Charge','Carryable wired TNT',kind='assembly')

# Mechanisms. Moving grips, platform, needle and gate remain separate Unity nodes.
start();cyl('Assembly apron',(0,-.005,0),8,.35,'Graphite',n=96)
cyl('Flat apron top',(0,.175,0),7.92,.01,'Steel',n=96)
for i in range(24):
 a=i*math.tau/24
 rod('Deck sector joint',(math.cos(a)*6.8,.18,math.sin(a)*6.8),(math.cos(a)*7.9,.18,math.sin(a)*7.9),.008,'Graphite')
finish('AST_M01_AssemblyApron','M');rule('AST_M01_AssemblyApron','Flush assembly apron')
deck('AST_M06_CatchShelf',(2.8,.3,5.4),'M','Teal')
for n in ['Lower climb catch shelf','Upper climb catch shelf']:rule('AST_M06_CatchShelf',n)
deck('AST_M07_FreightDeck',(5.5,.4,5),'M','Fuel');rule('AST_M07_FreightDeck','Gravity cargo deck')
for label in ('Brake','Release','Return'):
 start();sphere('Control base',(0,0,.04),(.46,.46,.35),'Graphite')
 loop(label+' grasp loop',(0,0,-.12),.31,.27,.046,'Grip')
 if label=='Brake':box('Brake stop tab',(0,-.19,-.1),(.22,.075,.10),'Fuel')
 elif label=='Release':cyl('Release hub',(0,0,-.18),.075,.035,'Red','Z')
 else:cyl('Return hub',(0,0,-.18),.075,.035,'Teal','Z')
 key='AST_M07_'+label+'Control';finish(key,'M')
 rule(key,{'Brake':'HOLD - lower / RELEASE - brake','Release':'Release after all pins removed','Return':'Return empty platform'}[label])
start();banded_beam((.2,6.6,.24),'Steel','Y');finish('AST_M07_GuideRail','M');rule('AST_M07_GuideRail','Guide rack')
start();banded_beam((5.4,.18,.16),'Steel','X');finish('AST_M07_SafetyRail','M');rule('AST_M07_SafetyRail','Cargo side rail',under='four recovery routes')
start();box('Power pedestal',(0,0,0),(1.05,1.2,.5),'Graphite',.06)
box('Face plate',(0,.05,-.243),(.88,.93,.014),'Ceramic',.03);vent((0,-.32,-.26),.48,.15,3)
finish('AST_M08_Pedestal','M');rule('AST_M08_Pedestal','Circuit pedestal')
start();cyl('Dial rim',(0,0,0),.25,.20,'Graphite','Z',inner=.18)
cyl('Dial hub',(0,0,0),.09,.27,'Steel','Z')
for i in range(4):
 a=i*math.pi/2;rod('Dial spoke',(0,0,-.02),(.19*math.cos(a),.19*math.sin(a),-.02),.04,'Grip')
finish('AST_M08_Dial','M');rule('AST_M08_Dial','^Circuit dial ')
start();mesh('Directional needle',[(-.045,-.20,-.02),(.045,-.20,-.02),(.045,.10,-.02),(.075,.10,-.02),(0,.20,-.02),(-.075,.10,-.02)],[(0,1,2,3,4,5)],'Fuel')
# Give the needle thickness so fitting never divides by a zero extent.
box('Needle depth',(0,0,0),(.04,.34,.04),'Fuel',.005)
finish('AST_M08_Needle','M');rule('AST_M08_Needle','Needle',under='Rotating circuit needle',keep=True)
start();box('Sealed moving gate',(0,0,0),(5.5,3.4,.35),'Graphite',.045)
for x in (-1.37,1.37):
 box('Door armor panel',(x,0,-.17),(2.61,3.18,.014),'Ceramic',.04)
 vent((x,-.94,-.181),1.9,.34,4)
for x in (-2.60,2.60):box('Gate edge hazard',(x,0,-.18),(.09,3.15,.015),'Fuel')
box('Meeting seam',(0,0,-.178),(.045,3.2,.012),'Steel')
finish('AST_M09_Gate','M');rule('AST_M09_Gate','Pressure gate - flush threshold')
start();cyl('Plate housing',(0,-.005,0),.8,.24,'Graphite',n=48)
cyl('Load disk',(0,.12,0),.74,.01,'Teal',n=48)
finish('AST_M09_PressurePlate','M');rule('AST_M09_PressurePlate','Revealed pressure plate')
start();box('Foot silhouette',(0,0,.05),(.18,.012,.30),'Ceramic',.02);sphere('Toe',(0,0,-.13),(.18,.012,.18),'Ceramic')
finish('AST_M09_Footprint','M');rule('AST_M09_Footprint','Footprint - a player must stand here')
start();cyl('Fuel socket housing',(0,0,0),.25,.16,'Fuel','Z',inner=.16)
cyl('Fuel cap',(0,0,-.03),.15,.10,'Graphite','Z');box('Cap grip',(0,0,-.1),(.22,.055,.04),'Steel')
finish('AST_M12_FuelPort','M');rule('AST_M12_FuelPort','Yellow fuel port and gauge',kind='port')
deck('AST_M13_PracticeDock',(3,.5,3),'M','Blue')
for n in ['Practice loading dock','Practice receiving dock']:rule('AST_M13_PracticeDock',n)
start();cyl('Open pressure barrel',(0,0,0),1.15,3.6,'Blue','Z',inner=.93,n=64)
for z in (-1.74,1.74):cyl('Barrel hoop',(0,0,z),1.163,.10,'Fuel' if z>0 else 'Steel','Z',inner=.93,n=64)
for z in (-.9,.9):cyl('Barrel band',(0,0,z),1.156,.08,'Graphite','Z',inner=1.14,n=64)
finish('AST_M14_HollowBarrel','M');rule('AST_M14_HollowBarrel','Hollow cannon barrel',kind='barrel')
start();cyl('Closed breech',(0,0,0),.95,.16,'Graphite','Z',n=48)
cyl('Breech service ring',(0,0,.075),.72,.012,'Steel','Z',inner=.68,n=48)
finish('AST_M14_Breech','M');rule('AST_M14_Breech','Barrel rear')
start();cyl('Wheel tire',(0,0,0),.8,.44,'Graphite',inner=.55,n=48)
cyl('Wheel hub',(0,0,0),.19,.43,'Steel')
for i in range(8):
 a=i*math.pi/4;rod('Wheel spoke',(.12*math.cos(a),0,.12*math.sin(a)),(.57*math.cos(a),0,.57*math.sin(a)),.065,'Blue')
finish('AST_M14_Wheel','M');rule('AST_M14_Wheel','Cannon wheel')
start();loop('Entry handhold',(0,0,0),.40,.40,.065,'Grip');finish('AST_M14_EntryGrip','M');rule('AST_M14_EntryGrip','Cannon mouth interact')
start();box('Igniter station',(0,0,0),(.65,.8,.55),'Graphite',.05)
box('Ignition guard',(0,.08,-.265),(.48,.43,.02),'Fuel');loop('Ignition grip',(0,.08,-.28),.26,.18,.035)
finish('AST_M14_Igniter','M');rule('AST_M14_Igniter','Cannon rear igniter')
deck('AST_M14_Chassis',(3.5,.5,3.8),'M','Blue');rule('AST_M14_Chassis','Cannon plinth')
start();box('Reinforced detonator',(0,0,0),(1.2,1,.9),'Red',.055)
for x in (-.55,.55):box('Protective corner',(x,0,-.42),(.1,.96,.06),'Graphite')
plaque('Safety face',(0,0,-.449),.77,.56);bolts((0,0,-.495),.65,.43)
finish('AST_M15_Detonator','M');rule('AST_M15_Detonator','Detonator box')
start();banded_beam((1.2,.18,.22),'Grip');finish('AST_M15_PlungerGrip','M');rule('AST_M15_PlungerGrip','TNT switch handle')
start();cyl('Plunger shaft',(0,0,0),.07,.4,'Steel');finish('AST_M15_PlungerStem','M');rule('AST_M15_PlungerStem','Plunger stem')
start()
# Actual fractured rock asset is only applied to the destructible rubble and its flying fragments.
mesh('Fractured rock',[(-1,-.65,-.7),(.8,-.8,-.72),(1,.2,-.6),(.5,.8,-.7),(-.7,.7,-.5),(-.9,-.5,.7),(.9,-.55,.6),(.7,.65,.65),(-.5,.85,.7)],[(0,4,3,2,1),(0,1,6,5),(1,2,7,6),(2,3,7),(3,4,8,7),(4,0,5,8),(5,6,7,8)],'Rock',.06)
finish('AST_M16_FracturedRubble','M');rule('AST_M16_FracturedRubble','^Fractured boulder ');rule('AST_M16_FracturedRubble','^Flying rock fragment ')
def warning(kind):
 start()
 for z,mat,factor in [(0,'Graphite',1),(-.046,'Fuel',.88),(.046,'Fuel',.88)]:
  pts=[(-.65*factor,-.50*factor,z),(.65*factor,-.50*factor,z),(0,.70*factor,z)]
  mesh('Warning triangle',pts,[(0,1,2),(2,1,0)],mat)
 for sign in (-1,1):
  z=sign*.051
  if kind=='Fall':
   sphere('Falling head',(-.12,.30,z),(.13,.13,.018),'Graphite')
   rod('Falling torso',(-.12,.20,z),(.02,-.03,z),.037,'Graphite')
   for a,b in [((-.1,.15),(-.28,.04)),((-.1,.15),(.1,.22)),((.02,-.03),(.23,-.13)),((.02,-.03),(-.04,-.24))]:rod('Falling limb',(a[0],a[1],z),(b[0],b[1],z),.025,'Graphite')
   box('Cliff edge',(-.30,-.28,z),(.30,.075,.015),'Graphite',0)
  else:
   cyl('Mine symbol',(0,-.10,z),.13,.018,'Graphite','Z',n=20)
   for i in range(8):
    a=i*math.pi/4;rod('Blast ray',(.17*math.cos(a),-.1+.17*math.sin(a),z),(.25*math.cos(a),-.1+.25*math.sin(a),z),.022,'Graphite')
 box('Plate core',(0,-.07,0),(.18,.67,.075),'Graphite',.015)
 finish('AST_M'+('18_FallWarning' if kind=='Fall' else '17_MineWarning'),'M')
warning('Mine');warning('Fall');rule('AST_M17_MineWarning','Yellow warning triangle')
start();banded_beam((.09,1.45,.09),'Steel','Y');finish('AST_M18_WarningPost','M');rule('AST_M18_WarningPost','Warning post')

# E01--E06 modular station dressing; low contrast, clearly different from live orange grips.
start();box('Container hull',(0,0,0),(6,3.2,4),'Fabric',.10)
for z in (-1.99,1.99):
 for x in (-1.5,1.5):box('Sealed door',(x,0,z),(2.86,2.93,.02),'Ceramic',.04)
 for i in range(11):box('Recessed wall rib',(-2.6+i*.52,0,z),(.06,2.85,.019),'Steel',.01)
 for x in (-2.86,2.86):box('Corner casting',(x,0,z),(.20,3.16,.03),'Graphite')
finish('AST_E01_Container','E');rule('AST_E01_Container','Ribbed expedition container')
def cabinet(key,s,category='E',face='Lens',drawers=0):
 start();x,y,z=s;box('Cabinet shell',(0,0,0),s,'Graphite',min(s)*.06)
 box('Inset frame',(0,0,-z*.493),(x*.87,y*.90,z*.014),'Ceramic',.025)
 if drawers:
  for i in range(drawers):
   yy=(i-(drawers-1)/2)*y*.78/drawers
   box('Drawer',(0,yy,-z*.504),(x*.76,y*.68/drawers,.018),'Fabric',.01)
   box('Flush drawer recess',(0,yy+y*.19/drawers,-z*.516),(x*.3,y*.045/drawers,.008),'Graphite',.005)
 else:
  box('Protected display',(0,y*.14,-z*.503),(x*.69,y*.42,.02),face,.02)
  vent((0,-y*.29,-z*.509),x*.67,y*.16,4)
 bolts((0,0,-z*.515),x*.78,y*.77,r=min(x,y)*.02)
 finish(key,category)
cabinet('AST_E01_ChargingTerminal',(1,2,.7),face='Blue');rule('AST_E01_ChargingTerminal','Robot charging terminal')
start();box('Sorting tray',(0,0,0),(1.1,.5,1.1),'Fabric',.035)
box('Tray inset',(0,.249,0),(.95,.006,.95),'Graphite',.025)
for x in (-.28,0,.28):box('Sample partition',(x,.245,0),(.02,.01,.92),'Steel')
finish('AST_E01_SortingTray','E');rule('AST_E01_SortingTray','Sample sorting tray')
for axis in ('X','Y'):
 start();banded_beam((6,.4,.5) if axis=='X' else (.35,6,.35),'Steel',axis)
 finish('AST_E01_Gantry'+axis,'E')
 for n in (['Cargo loading gantry crossbeam','Repair gantry beam'] if axis=='X' else ['Cargo loading gantry leg','Repair gantry mast','Survey instrument pier']):rule('AST_E01_Gantry'+axis,n)
start()
# True shallow concave reflector in the original ellipsoid envelope.
vs=[];fs=[];nr=10;ns=64
for j in range(nr+1):
 r=j/nr*2.9;y=.55*(r/2.9)**2-.275
 for i in range(ns):vs.append((r*math.cos(i*math.tau/ns),y,r*math.sin(i*math.tau/ns)))
for j in range(nr):
 for i in range(ns):fs.append((j*ns+i,j*ns+(i+1)%ns,(j+1)*ns+(i+1)%ns,(j+1)*ns+i))
o=mesh('Concave reflector',vs,fs,'Ceramic')
for p in o.data.polygons:p.use_smooth=True
cyl('Dish outer lip',(0,.26,0),2.90,.03,'Steel','Y',inner=2.84,n=64)
finish('AST_E02_RadarDish','E');rule('AST_E02_RadarDish','Atmospheric radar dish')
cabinet('AST_E02_ObservationConsole',(2.5,1.4,.8),face='Blue');rule('AST_E02_ObservationConsole','Observation console')
cabinet('AST_E02_SampleRack',(1.4,1.6,.7),drawers=4);rule('AST_E02_SampleRack','Air sample rack')
start();banded_beam((.18,6.8,.18),'Steel','Y');finish('AST_E02_Antenna','E');rule('AST_E02_Antenna','Sampling antenna')
deck('AST_E02_BrokenWalkway',(3.4,.3,2),'E','Steel');rule('AST_E02_BrokenWalkway','Broken elevated walkway')
deck('AST_E02_StairRemnant',(3,.25,.85),'E','Steel');rule('AST_E02_StairRemnant','Sheared service stair remnant')
cabinet('AST_E03_SampleCabinet',(1.2,2.2,1.5),face='Purple');rule('AST_E03_SampleCabinet','Hermetic sample cabinet')
start();cyl('Vial body',(0,0,0),.24,.95,'Lens')
for y in (-.48,.48):cyl('Seal lid',(0,y,0),.25,.04,'Steel')
finish('AST_E03_SpecimenVial','E');rule('AST_E03_SpecimenVial','Sealed specimen vial')
start();box('Window frame',(0,0,0),(.03,1.2,2),'Steel',.012)
box('Protected glazing',(-.016,0,0),(.004,.98,1.74),'Lens',.001)
for z in (-.85,.85):box('Window seal',(-.018,0,z),(.004,1.06,.035),'Graphite',0)
finish('AST_E03_ObservationWindow','E');rule('AST_E03_ObservationWindow','Observation porthole')
start();cyl('Filter canister',(0,0,0),.35,1.6,'Ceramic')
for y in (-.6,-.3,0,.3,.6):cyl('Filter rib',(0,y,0),.348,.06,'Graphite')
finish('AST_E03_Filter','E');rule('AST_E03_Filter','Filtered exhaust')
cabinet('AST_E03_BatteryCabinet',(1,1.2,1.1),face='Teal');rule('AST_E03_BatteryCabinet','Emergency battery cabinet')
start();banded_beam((.12,3.4,.12),'Steel','Y');finish('AST_E03_DeconUpright','E');rule('AST_E03_DeconUpright','Decontamination spray upright')
start();banded_beam((5.5,.12,.12),'Steel','X');finish('AST_E03_DeconHeader','E');rule('AST_E03_DeconHeader','Decontamination spray header')
cabinet('AST_E04_Workbench',(4,1.4,1.3),drawers=3);rule('AST_E04_Workbench','Workshop tool bench')
start();rod('Spanner shaft',(-.17,0,0),(.17,0,0),.035,'Steel')
for x in (-.18,.18):cyl('Spanner head',(x,0,0),.075,.06,'Steel','Y',inner=.04,n=12)
finish('AST_E04_RepairTool','E');rule('AST_E04_RepairTool','Abandoned repair tool')
start();banded_beam((.18,.18,4),'Graphite','Z');finish('AST_E04_ServiceCradle','E');rule('AST_E04_ServiceCradle','Sideways service cradle')
start()
for i in range(16):loop('Static chain link',(0,0,-1.30+i*.173),.06,.12,.012,'Steel',plane='XZ')
finish('AST_E04_HoistChain','E');rule('AST_E04_HoistChain','Maintenance hoist chain')
start();box('Bunk storage base',(0,0,0),(1.6,.6,2.6),'Graphite',.055)
for z in (-.85,0,.85):box('Underbunk compartment',(-.797,0,z),(.008,.4,.69),'Fabric')
finish('AST_E05_Bunk','E');rule('AST_E05_Bunk','Sleeping bunk')
start();box('Stitched mattress',(0,0,0),(1.55,.1,2.5),'Fabric',.024)
for x in (-.70,.70):box('Mattress seam',(x,.052,0),(.012,.004,2.3),'Steel',.001)
finish('AST_E05_Mattress','E');rule('AST_E05_Mattress','Sleeping mat')
start();box('Folded duvet',(0,0,0),(1.2,.22,.55),'Ceramic',.045)
for y in (-.055,.015):box('Fold seam',(0,y,-.272),(1.1,.009,.003),'Fabric',0)
finish('AST_E05_Bedding','E');rule('AST_E05_Bedding','Folded bedding')
deck('AST_E05_Table',(3,.15,1.6),'E','Steel');rule('AST_E05_Table','Mess table')
start();cyl('Open cup',(0,0,0),.09,.29,'Ceramic','Y',inner=.073)
cyl('Cup foot',(0,-.151,0),.09,.018,'Graphite')
loop('Cup handle',(.115,0,0),.06,.15,.015,'Steel')
finish('AST_E05_Cup','E');rule('AST_E05_Cup','Interrupted meal cup')
start();box('Bench seat',(0,0,0),(2.8,.16,.8),'Fabric',.045)
for x in (-1.0,0,1.0):box('Cushion seam',(x,.084,0),(.02,.004,.70),'Graphite',0)
finish('AST_E05_BenchSeat','E');rule('AST_E05_BenchSeat','Bench seat')
start();box('Bench back',(0,0,0),(2.8,.9,.12),'Fabric',.035)
for x in (-1.0,0,1.0):box('Back seam',(x,0,-.059),(.02,.75,.003),'Graphite',0)
finish('AST_E05_BenchBack','E');rule('AST_E05_BenchBack','Bench back')
start();box('Solar module frame',(0,0,0),(2.3,.14,3),'Steel',.03)
for x in range(6):
 for z in range(8):box('Photovoltaic cell',(-.94+x*.377,.075,-1.28+z*.366),(.34,.006,.33),'Blue',.012)
finish('AST_E05_SolarPanel','E');rule('AST_E05_SolarPanel','Camp solar panel')
cabinet('AST_E05_AirRecycler',(2,2.8,2),face='Teal');rule('AST_E05_AirRecycler','Air recycling tank')
start();box('Insulated habitat panel',(0,0,0),(7,3.6,.3),'Ceramic',.055)
for x in (-2.3,0,2.3):box('Recessed panel joint',(x,0,-.149),(.025,3.4,.004),'Steel',0)
finish('AST_E05_HabitatWall','E');rule('AST_E05_HabitatWall','Habitat rear bulkhead')
cabinet('AST_E06_Recorder',(1.4,.9,.7),face='Lens');rule('AST_E06_Recorder','Disconnected survey recorder')
start();cyl('Ground sensor',(0,0,0),.085,.80,'Graphite');cyl('Sensor cap',(0,.36,0),.09,.08,'Steel')
finish('AST_E06_Geophone','E');rule('AST_E06_Geophone','Geophone sample probe')
start();box('Beacon housing',(0,0,0),(.6,.3,.6),'Graphite',.045)
cyl('Beacon lens',(0,.08,0),.24,.14,'Lens');finish('AST_E06_Beacon','E');rule('AST_E06_Beacon','Evacuation beacon',keep=True)
start();plaque('Pictogram panel',(0,0,0),1.8,.65,'Graphite')
finish('AST_E08_Panel','E');rule('AST_E08_Panel','Pictogram backing - no collider')

# M10/M11 modular architectural skins retain the exact authored passage envelopes.
for axis in ('Z','X'):
 start();box('Insulated bulkhead',(0,0,0),(6,3,.6),'Ceramic',.035)
 for z in (-.299,.299):
  for x in (-2,0,2):box('Flush service seam',(x,0,z),(.025,2.85,.002),'Steel',0)
  box('Lower service channel',(0,-1.15,z),(5.7,.12,.002),'Graphite',0)
 if axis=='X':
  # Rotate the complete authored mesh, not the physics transform in Unity.
  for o in parts:
   for v in o.data.vertices:v.co.x,v.co.y=-v.co.y,v.co.x
 key='AST_M10_Bulkhead'+axis;finish(key,'M')
 for n in (['Back retaining wall','Front left','Front right'] if axis=='Z' else ['West retaining wall','East retaining wall']):rule(key,n,under='C - align circuit')
 for n in (['Back wall','North wall','Corridor north','Corridor south'] if axis=='Z' else ['East wall','Blind west wall']):rule(key,n,under='D - low clearance')
 if axis=='X':rule(key,'Habitat side')
 else:rule(key,'Open habitat doorway jamb')
deck('AST_M11_RoofPanel',(10,.5,10),'M','Steel')
for n in ['Low roof - upright part cannot pass','Corridor low roof','Sealed moss roof']:rule('AST_M11_RoofPanel',n)

start();box('Shelf surface',(0,0,0),(4.8,.16,1.4),'Fabric',.025)
for x in (-2.2,2.2):box('Recessed end cap',(x,0,0),(.08,.155,1.38),'Steel')
finish('AST_E06_ToolShelf','E');rule('AST_E06_ToolShelf','Tool shelf')

manifest={'models':[{'key':m['key'],'category':m['category'],'triangles':m['triangles']} for m in models],
 'rules':rules,'materials':[{'name':'AST_'+n,'color':list(v[0]),'metallic':v[1],'smoothness':1-v[2]} for n,v in palette.items()]}
open(OUT+'/manifest.json','w',encoding='utf-8').write(json.dumps(manifest,ensure_ascii=False,indent=2))
open(DOC+'/model-manifest.json','w',encoding='utf-8').write(json.dumps(manifest,ensure_ascii=False,indent=2))

# Native Blender gallery retains each asset's named parts and metre dimensions.
for i,m in enumerate(models):m['root'].location=((i%9)*20,(i//9)*20,0)
bpy.ops.wm.save_as_mainfile(filepath=DOC+'/AST_Expedition_ArtLibrary.blend')
result={'models':len(models),'rules':len(rules),'triangles':sum(m['triangles'] for m in models),'output':OUT}
