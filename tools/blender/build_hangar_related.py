"""13 concept-derived assets. Execute through connected Blender MCP, one asset at a time.

All geometry is authored in metres: X width, Y depth, Z up; front = -Y.
New scenes only. Existing user scenes and files are never cleared.
"""
import bpy, math, json, shutil, bmesh
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(r'C:\workspace\2-PJT\S15P21B202')
OUT = ROOT/'METAGAME/Assets/_Project/Models/Environment/Stage1/HangarRelated'
SOURCE = ROOT/'artifacts/stage1-related-hangar'
TEX_SOURCE = ROOT/'METAGAME/Assets/_Project/Art/Buildings/Hangar/AstraHangar.fbm'
SPECS = [
 ('01-south-approach-walkway','SouthApproachWalkway',2.8,14.1,.3),
 ('02-east-entrance-landing','EastEntranceLanding',3.5,9.5,.3),
 ('03-pedestrian-access','PedestrianAccess',3.2,17.144428,.4),
 ('04-foundation-approach-deck','FoundationApproachDeck',23.1,15.5,.5),
 ('05-broken-personnel-bridge-0','BrokenPersonnelBridge0',2.6,1.15,.4),
 ('06-broken-personnel-bridge-1','BrokenPersonnelBridge1',2.6,1.15,.4),
 ('07-broken-personnel-bridge-2','BrokenPersonnelBridge2',2.6,1.15,.4),
 ('08-broken-personnel-bridge-5','BrokenPersonnelBridge5',2.6,1.15,.4),
 ('09-broken-personnel-bridge-6','BrokenPersonnelBridge6',2.6,1.15,.4),
 ('10-modular-edge-curb','ModularEdgeCurb',2,.16,.2),
 ('11-cargo-apron-entry-stairs','CargoApronEntryStairs',3.3,2.04,.81),
 ('12-adjustable-deck-support','AdjustableDeckSupport',2.4,1.4,4.8),
 ('13-route-wayfinding-beacon','RouteWayfindingBeacon',.45,.4,1.6),
]

class Builder:
 def __init__(self,index):
  self.index=index; self.slug,self.label,self.w,self.d,self.h=SPECS[index]
  self.name='SM_Astra_'+self.label
  self.folder=OUT/self.slug; self.folder.mkdir(parents=True,exist_ok=True)
  self.tex=self.folder/(self.name+'.fbm'); self.tex.mkdir(exist_ok=True)
  self.source=SOURCE/self.slug; self.source.mkdir(parents=True,exist_ok=True)
  self.previous=bpy.context.scene
  self.scene=bpy.data.scenes.new('HR_'+self.label)
  bpy.context.window.scene=self.scene
  self.scene.unit_settings.system='METRIC'; self.scene.unit_settings.scale_length=1
  self.parts=[]
  self.mat=bpy.data.materials.new('M_Astra_'+self.label)
  self.mat.use_nodes=True
  nodes=self.mat.node_tree.nodes; links=self.mat.node_tree.links
  bs=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
  self.images={}
  for k in ['BaseColor','Metallic','Roughness','Normal','Emission','MetallicSmoothness']:
   dest=self.tex/(self.name+'_'+k+'.png')
   shutil.copyfile(TEX_SOURCE/('AstraHangar_'+k+'.png'),dest)
   im=bpy.data.images.load(str(dest),check_existing=False)
   im.colorspace_settings.name='sRGB' if k in ['BaseColor','Emission'] else 'Non-Color'
   self.images[k]=im
   if k=='MetallicSmoothness': continue
   n=nodes.new('ShaderNodeTexImage'); n.image=im; n.label=k
   if k=='Normal':
    nm=nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.35
    links.new(n.outputs['Color'],nm.inputs['Color']); links.new(nm.outputs['Normal'],bs.inputs['Normal'])
   else: links.new(n.outputs['Color'],bs.inputs[{'BaseColor':'Base Color','Emission':'Emission Color'}.get(k,k)])
  bs.inputs['Emission Strength'].default_value=1.3

 def finish(self,o,name,tile,bevel=.015):
  o.name=self.label+'__'+name
  o.data.materials.append(self.mat)
  # Project each face into a padded palette cell. Geometry carries panel detail.
  uv=o.data.uv_layers.new(name='UVMap') if not o.data.uv_layers else o.data.uv_layers[0]
  o.data.update()
  for p in o.data.polygons:
   axis=max(range(3),key=lambda a:abs(p.normal[a])); axes=[a for a in range(3) if a!=axis]
   pts=[o.data.vertices[o.data.loops[j].vertex_index].co for j in p.loop_indices]
   lo=[min(v[a] for v in pts) for a in axes]; hi=[max(v[a] for v in pts) for a in axes]
   for j,v in zip(p.loop_indices,pts):
    u=(v[axes[0]]-lo[0])/max(hi[0]-lo[0],1e-6)
    t=(v[axes[1]]-lo[1])/max(hi[1]-lo[1],1e-6)
    uv.data[j].uv=((tile%4+.08+.84*u)/4,(tile//4+.08+.84*t)/4)
  if bevel:
   m=o.modifiers.new('Manufactured edge radii','BEVEL'); m.width=bevel; m.segments=2
  o['palette_cell']=tile; self.parts.append(o)
  return o

 def box(self,name,loc,dim,tile=0,bevel=.015):
  bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
  o=bpy.context.object; o.scale=dim
  bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  return self.finish(o,name,tile,min(bevel,min(dim)*.22))

 def prism(self,name,xy,z0,z1,tile=0,bevel=.008):
  # Normalize winding; caps triangulate correctly even with concave damage outlines.
  if sum(xy[i][0]*xy[(i+1)%len(xy)][1]-xy[(i+1)%len(xy)][0]*xy[i][1] for i in range(len(xy)))<0: xy=list(reversed(xy))
  n=len(xy); vs=[(x,y,z) for z in [z0,z1] for x,y in xy]
  fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
  me=bpy.data.meshes.new(name); me.from_pydata(vs,[],fs); me.update()
  o=bpy.data.objects.new(name,me); self.scene.collection.objects.link(o)
  return self.finish(o,name,tile,bevel)

 def plate(self,name,x,y,z,w,d,h,tile=0,c=.08):
  c=min(c,w*.2,d*.2)
  xy=[(-w/2+c,-d/2),(w/2-c,-d/2),(w/2,-d/2+c),(w/2,d/2-c),(w/2-c,d/2),(-w/2+c,d/2),(-w/2,d/2-c),(-w/2,-d/2+c)]
  return self.prism(name,[(a+x,b+y) for a,b in xy],z-h/2,z+h/2,tile,min(.012,h*.15))

 def cyl(self,name,loc,r,depth,tile=2,axis='Z',verts=12):
  bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc)
  o=bpy.context.object
  if axis=='Y': o.rotation_euler.x=math.pi/2
  if axis=='X': o.rotation_euler.y=math.pi/2
  bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
  return self.finish(o,name,tile,min(.008,depth*.15))

 def beam(self,name,a,b,width,depth,tile=2):
  a,b=Vector(a),Vector(b)
  o=self.box(name,(a+b)*.5,(width,depth,(b-a).length),tile)
  o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler(); return o

 def bolt(self,x,y,z,r=.035):
  self.cyl('Fastener washer',(x,y,z),r*1.35,.008,8)
  self.cyl('Hex bolt',(x,y,z+.009),r,.018,2,verts=6)
  self.box('Recessed bolt slot',(x,y,z+.019),(r*.95,r*.18,.002),3,.0003)

 def lamp(self,x,y,z,length=.5,width=.12,tile=5):
  self.plate('Light gasket',x,y,z,length+.07,width+.07,.016,3,c=.04)
  self.plate('Light bezel',x,y,z+.012,length+.035,width+.035,.015,2,c=.03)
  self.plate('Recessed lens',x,y,z+.021,length,width,.009,tile,c=.025)

 def deck(self,w,d,h,panel_length=1.65,wide=False):
  self.plate('Underside chassis',0,0,h*.32,w-.09,d-.09,h*.64,8,.13)
  self.plate('Perimeter shell',0,0,h*.70,w,d,h*.54,0,.16)
  # A shallow dark recess is visible between the individual top panels.
  self.plate('Panel seam bed',0,0,h-.0225,w-.36,d-.34,.035,3,.07)
  nx= max(1,round((w-.6)/2.8)) if wide else 1
  ny=max(1,round((d-.6)/panel_length)); pw=(w-.56)/nx; pd=(d-.56)/ny
  for ix in range(nx):
   for iy in range(ny):
    x=-w/2+.28+pw*(ix+.5); y=-d/2+.28+pd*(iy+.5)
    self.plate('Lavender removable floor',x,y,h-.02,pw-.035,pd-.035,.04,1,.055)
    for sx in [-1,1]:
     for sy in [-1,1]: self.bolt(x+sx*(pw/2-.11),y+sy*(pd/2-.12),h-.017,.021 if not wide else .033)
    if iy%2==0:
     self.plate('Service handle recess',x,y+pd/2-.16,h+.001,min(.32,pw*.25),.07,.006,8,.025)
     self.box('Service handle metal',(x,y+pd/2-.16,h+.006),(min(.24,pw*.2),.025,.014),2,.003)
  count=max(2,round(d/1.8))
  for side in [-1,1]:
   for i in range(count):
    y=-d/2+(i+.5)*d/count; x=side*(w/2-.11)
    self.lamp(x,y,h-.025,.1,min(.6,d/count*.42))
    self.box('Fascia lavender inset',(side*(w/2-.006),y,h*.52),(.018,d/count*.65,h*.25),1,.004)
   for y in [-d/2+.14,d/2-.14]: self.lamp(side*(w/2-.13),y,h-.022,.08,.10,6)
  for y in [-d/2+.10,d/2-.10]:
   self.box('End joining seam',(0,y,h*.45),(w-.45,.023,h*.17),3,.003)
  for y in [(-d/2+.35)+i*(d-.7)/max(1,ny-1) for i in range(ny)]:
   self.box('Underdeck transverse rib',(0,y,.07),(w-.17,.10,.12),2,.012)
  for x in [-w*.30,w*.30]: self.box('Longitudinal bearer',(x,0,.07),(.12,d-.17,.12),8,.012)

 def arrow(self,x,y,z,size=.8,tile=10):
  p=[(-.5,-.2),(0,.25),(.5,-.2),(.5,0),(0,.45),(-.5,0)]
  self.prism('Route chevron',[(x+a*size,y+b*size) for a,b in p],z,z+.004,tile,.001)

 def bridge(self,variant):
  w,d,h=self.w,self.d,self.h
  # Real torn footprints, not a crack painted onto an intact rectangular block.
  x0,x1=-w/2,w/2; f,b=-d/2,d/2
  edge_front=[(x0+.10,f),(x1-.10,f)]
  edge_back=[(x1-.10,b),(x0+.10,b)]
  if variant in [0,6]:
   edge_front=[(x0+.10,f),(x1-.35,f),(x1-.24,f+.13),(x1-.10,f+.09)] if variant==6 else [(x0+.10,f+.17),(x0+.26,f+.13),(x0+.37,f),(x1-.1,f)]
  if variant==2:
   edge_back=[(x1-.10,b-.10),(.95,b-.16),(.80,b-.03),(.59,b-.19),(.34,b-.11),(.17,b-.21),(-.08,b-.14),(-.32,b-.03),(-.48,b-.17),(-.72,b-.08),(-.91,b-.23),(x0+.1,b-.12)]
  if variant==5:
   edge_front=[(x0+.1,f+.04),(-.96,f+.14),(-.77,f+.06),(-.55,f+.21),(-.40,f+.30),(-.2,f+.20),(0,f+.33),(.22,f+.25),(.43,f+.32),(.62,f+.11),(.88,f+.18),(x1-.1,f+.05)]
  outline=edge_front+[(x1,f+.2),(x1,b-.18)]+edge_back+[(x0,b-.18),(x0,f+.25)]
  if variant==1:
   self.prism('Open frame bottom plate',outline,.05,.095,8,.009)
   self.box('Rear frame',(0,.46,.185),(2.40,.18,.18),8,.014)
   for x in [-1.15,1.15]: self.box('Side frame',(x,0,.185),(.20,.96,.18),8,.014)
   self.box('Front frame left',(-1.06,-.50,.185),(.30,.12,.18),8,.008)
   self.box('Front frame right',(.64,-.50,.185),(1.12,.12,.18),8,.008)
   shell_left=[(-1.28,-.36),(-1.15,-.565),(-.25,-.565),(-.16,-.22),(-.27,-.04),(-.08,.09),(-.19,.2),(-.02,.565),(-1.15,.565),(-1.28,.40)]
   shell_right=[(-.16,-.565),(1.15,-.565),(1.28,-.40),(1.28,.40),(1.15,.565),(.065,.565),(-.08,.2),(.02,.08),(-.17,-.05),(-.06,-.2)]
   self.prism('Cracked pearl left shell',shell_left,.25,.354,0,.005)
   self.prism('Cracked pearl right shell',shell_right,.26,.365,0,.005)
  else:
   self.prism('Torn structural plate',outline,.05,.27,8,.012)
   self.prism('Exposed pearl deck shell',[(x*.99,y*.99) for x,y in outline],.25,.36,0,.01)
  if variant==1:
   left=[(-1.04,-.37),(-.25,-.37),(-.14,-.20),(-.25,-.04),(-.06,.09),(-.17,.20),(-.02,.38),(-1.04,.38)]
   right=[(-.19,-.37),(1.04,-.37),(1.04,.38),(.06,.38),(-.09,.19),(.02,.08),(-.17,-.05),(-.06,-.2)]
   self.prism('Cracked left plate',left,.35,.388,1,.004)
   self.prism('Cracked right plate',right,.36,.4,1,.004)
   for x in [-.80,-.53,-.26]: self.beam('Exposed diagonal web',(x,-.53,.10),(x+.20,-.53,.28),.024,.028,2)
   flap=self.box('Peeled side armor',(-.55,-.58,.23),(.35,.025,.17),1,.008); flap.rotation_euler.x=-.5
  else:
   self.prism('Lavender worn walking panel',[(x*.82,y*.76) for x,y in outline],.355,.4,1,.006)
   # Panel seam trims across the broad slab.
   for x in [-.38,.38]: self.box('Panel joint',(x,0,.401),(.009,.53,.003),3,.0005)
  for x in [-1.15,1.15]:
   self.lamp(x,0,.355,.09,.28,6 if variant in [2,5] else 5)
   for y in [-.25,.25]: self.bolt(x,y,.36,.024)
  # Underframe I-beams with visible sockets and opposing terminal damage.
  for x in [-.78,.78]:
   self.box('Girder web',(x,0,.17),(.06,.97,.24),2,.006)
   for z in [.055,.275]: self.box('Girder flange',(x,0,z),(.19,.99,.035),8,.006)
   if variant in [2,5]:
    sign=1 if variant==2 else -1
    for z in [.08,.24]: self.box('Exposed terminal flange',(x,sign*.48,z),(.17,.27,.025),2,.003)
    self.box('Exposed terminal web',(x,sign*.48,.16),(.025,.27,.17),2,.003)
  if variant in [0,6]:
   for x in [-.85,.85]:
    self.box('Rear mount ear',(x,.50,.39),(.23,.12,.11),0,.018)
    self.cyl('Mount socket',(x,.435,.40),.055,.008,3,'Y')
    self.cyl('Mount insert',(x,.429,.40),.029,.011,2,'Y',6)
  # Fine irregular fracture scars remain shallow, walkable details.
  for i in range(3):
   x=-.9+i*.21; y=-.21+i*.10
   self.beam('Hairline fracture',(x,y,.402),(x+.10,y+.09,.402),.004,.004,8)

 def curb(self):
  self.plate('Black mounting plinth',0,0,.018,2,.16,.036,8,.04)
  self.plate('Pearl low curb',0,0,.106,1.99,.15,.178,0,.045)
  for sy in [-1,1]: self.box('Lavender fascia',(0,sy*.076,.11),(1.76,.006,.075),1,.007)
  self.lamp(0,0,.17,1.72,.046)
  for x in [-.91,.91]:
   self.bolt(x,0,.181,.016)
   self.box('Amber end marker',(x,-.080,.115),(.025,.006,.06),6,.002)

 def stairs(self):
  for i in range(4):
   y=-.72+i*.48; z=.24+i*.19
   self.plate('Step chassis',0,y,z-.145,3.3,.60,.19,8,.075)
   self.plate('Step pearl tread',0,y,z-.025,3.3,.60,.05,0,.08)
   self.plate('Step lavender insert',0,y+.018,z+.005,2.92,.43,.018,1,.04)
   self.box('Nosing gasket',(0,y-.285,z-.095),(2.91,.025,.08),3,.008)
   self.box('Nosing light',(0,y-.3,z-.092),(.47,.009,.035),5,.006)
   for x in [-1.53,1.53]: self.bolt(x,y-.18,z+.006,.027)
  for x in [-1.49,1.49]:
   self.beam('Side stringer',(x,-.88,.08),(x,.86,.66),.14,.18,2)
   for z in [.16,.35,.54]: self.box('Rear riser stiffener',(x,.9,z),(.16,.10,.12),8)

 def support(self):
  self.plate('Foundation shoe',0,0,.10,1.4,1.4,.20,8,.17)
  self.plate('Pearl anchor cap',0,0,.22,1.32,1.32,.14,0,.13)
  for x in [-.5,.5]:
   for y in [-.5,.5]: self.bolt(x,y,.3,.07)
  self.cyl('Main column',(0,0,2.02),.30,3.42,8,verts=24)
  self.cyl('Telescopic piston',(0,0,3.78),.235,1.24,2,verts=24)
  for z in [.52,3.05,3.37,3.63,4.14]: self.cyl('Collar',(0,0,z),.34,.12,2,verts=24)
  for x in [-.30,.30]:
   self.box('Pearl protective shell',(x,0,1.96),(.12,.48,2.7),0,.045)
   for sy in [-1,1]: self.beam('Lavender foot brace',(x*1.8,sy*.49,.3),(x,sy*.20,1.12),.12,.12,1)
  self.box('Maintenance hatch gasket',(0,-.313,2.02),(.42,.06,.92),3,.045)
  self.box('Lavender maintenance hatch',(0,-.351,2.02),(.35,.03,.83),1,.035)
  for z in [1.72,1.78,1.84]: self.box('Hatch louvers',(0,-.370,z),(.24,.016,.018),8,.003)
  self.box('Amber status',( .09,-.373,2.28),(.035,.012,.10),6,.004)
  for x in [-.1,.1]: self.beam('Rear conduit',(x,.29,.5),(x,.29,3.2),.036,.036,2)
  self.box('Crosshead body',(0,0,4.50),(2.4,.62,.52),8,.06)
  self.box('Pearl crosshead cap',(0,0,4.74),(2.4,.65,.12),0,.025)
  for x in [-.74,.74]:
   self.box('Pearl crosshead front',(x,-.33,4.51),(1.04,.08,.40),0,.04)
   self.box('Lavender crosshead front',(x,-.376,4.51),(.89,.025,.24),1,.025)
   self.box('Crosshead light',(x,-.394,4.40),(.39,.01,.032),5,.005)
  for x in [-1.19,1.19]:
   self.box('Crosshead end shell',(x,0,4.50),(.05,.68,.5),0,.015)
   for z in [4.37,4.44,4.51,4.58]: self.box('End ventilation',(x*1.024,0,z),(.015,.43,.028),8,.004)

 def beacon(self):
  self.plate('Anchored foot',0,0,.075,.45,.40,.15,8,.08)
  self.plate('Foot pearl trim',0,0,.16,.36,.32,.06,0,.06)
  self.plate('Pearl pillar',0,0,.87,.30,.245,1.40,0,.045)
  self.box('Front gasket',(0,-.128,.93),(.251,.012,1.15),3,.025)
  self.box('Lavender sign face',(0,-.139,.93),(.222,.012,1.12),1,.018)
  # Raised emissive pictogram: no font or decal dependency.
  for z in [1.22,1.37]:
   self.beam('Arrow left',(-.07,-.15,z-.055),(0,-.15,z+.015),.018,.009,5)
   self.beam('Arrow right',(0,-.15,z+.015),(.07,-.15,z-.055),.018,.009,5)
  for a,b in [((-.073,.78),(-.073,.95)),((-.073,.95),(0,1.0)),((0,1.0),(.073,.95)),((.073,.95),(.073,.78))]:
   self.beam('Hangar symbol',(a[0],-.15,a[1]),(b[0],-.15,b[1]),.012,.009,5)
  for z in [.82,.865,.91]: self.box('Door pictogram slat',(0,-.15,z),(.106,.009,.010),5,.002)
  self.box('Rear service cover',(0,.131,.63),(.22,.018,.67),1,.022)
  for z in [.39,.43,.47,.51]: self.box('Rear vent',(0,.144,z),(.15,.008,.014),8,.002)
  self.box('Rear amber status',(0,.145,1.28),(.032,.009,.10),6,.004)
  for x in [-.152,.152]: self.box('Side guide light',(x,0,.45),(.009,.045,.20),5,.008)
  self.plate('Crown',0,0,1.59,.25,.20,.02,2,.045)
  for x in [-.14,.14]:
   for y in [-.115,.115]: self.bolt(x,y,.18,.014)

 def build(self):
  i=self.index
  if i<4:
   self.deck(self.w,self.d,self.h,2.6 if i==3 else 1.65,i==3)
   if i==1:
    for y in [-.7,.15]: self.arrow(0,y,self.h+.002,1.4)
   if i==2:
    for j in range(15):
     y=-self.d/2+.65+j*(self.d-1.3)/14
     for off in [-.06,0,.06]: self.box('Traction rib',(0,y+off,self.h+.006),(self.w-.62,.025,.012),2,.002)
    # Rotate about the low end. Ramp top rise follows recorded endpoint difference.
    angle=math.asin(5.33/self.d); rot=Matrix.Rotation(angle,4,'X')
    for o in self.parts: o.matrix_world=Matrix.Translation((0,0,self.d*.5*math.sin(angle)))@rot@o.matrix_world
  elif i<9: self.bridge([0,1,2,5,6][i-4])
  elif i==9: self.curb()
  elif i==10: self.stairs()
  elif i==11: self.support()
  else: self.beacon()
  bpy.context.view_layer.update()
  return {'asset':self.name,'components':len(self.parts),'scene':self.scene.name}

 def merged(self):
  dg=bpy.context.evaluated_depsgraph_get(); vs=[]; fs=[]; uvs=[]
  for o in self.parts:
   ev=o.evaluated_get(dg); me=ev.to_mesh(); offset=len(vs)
   vs.extend(tuple(o.matrix_world@v.co) for v in me.vertices)
   fs.extend(tuple(offset+j for j in p.vertices) for p in me.polygons)
   uvs.extend(tuple(v.uv) for v in me.uv_layers[0].data); ev.to_mesh_clear()
  me=bpy.data.meshes.new(self.name); me.from_pydata(vs,[],fs); me.update()
  uv=me.uv_layers.new(name='UVMap')
  for v,co in zip(uv.data,uvs): v.uv=co
  ob=bpy.data.objects.new(self.name,me); self.scene.collection.objects.link(ob)
  me.materials.append(self.mat)
  bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active=ob
  # Triangulate explicitly before tangent export; second UV is separately packed.
  tri=ob.modifiers.new('Export triangulation','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=tri.name)
  uv2=ob.data.uv_layers.new(name='LightmapUV'); ob.data.uv_layers.active_index=1
  bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
  bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.012,area_weight=.2,scale_to_bounds=True)
  bpy.ops.object.mode_set(mode='OBJECT'); ob.data.uv_layers.active_index=0; ob.data.uv_layers[0].active_render=True
  return ob

 def export(self):
  ob=self.merged(); bpy.context.view_layer.update()
  before=metrics([ob]); audit=mesh_audit(ob)
  fbx=self.folder/(self.name+'.fbx')
  bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,
    use_tspace=True,bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False,
    axis_forward='-Z',axis_up='Y',use_custom_props=True)
  # Move delivery mesh into a new scene to avoid doubling the editable source.
  check=bpy.data.scenes.new('HR_CHECK_'+self.label); bpy.context.window.scene=check
  bpy.ops.import_scene.fbx(filepath=str(fbx),use_image_search=True)
  bpy.context.view_layer.update(); imported=[o for o in check.objects if o.type=='MESH']
  after=metrics(imported); missing=[]; image_links=[]
  for obj in imported:
   assert len(obj.data.uv_layers)==2,'UV layer lost'
   for m in obj.data.materials:
    for n in m.node_tree.nodes:
     if n.type=='TEX_IMAGE' and n.image:
      p=Path(bpy.path.abspath(n.image.filepath)); image_links.append(str(p))
      if not p.exists(): missing.append(str(p))
  assert before['triangles']==after['triangles'],'Triangle count changed'
  assert max(abs(a-b) for a,b in zip(before['dimensions'],after['dimensions']))<.002,'Scale mismatch'
  assert not missing,'Texture path missing'
  assert not audit['degenerate_faces'] and not audit['non_manifold_edges'],audit
  report={'asset':self.name,'concept':str(ROOT/'docs/assets/concepts/stage1-related-hangar'/(self.slug+'.png')),
   'components':len(self.parts),'before_export':before,'after_reimport':after,'geometry_audit':audit,
   'missing_textures':missing,'texture_links':sorted(set(image_links)),'roundtrip':'PASS',
   'units':'metres','front_blender':'-Y','export_axes':'-Z forward / Y up','pivot':'base center; ramp low-end elevation',
   'fbx':str(fbx),'material':self.mat.name}
  (self.source/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
  # Keep editable components, omit merged duplicate from authoring scene.
  bpy.context.window.scene=self.scene
  bpy.data.objects.remove(ob,do_unlink=True)
  self.presentation()
  for im in self.images.values(): im.pack()
  bpy.data.libraries.write(str(self.source/(self.name+'.blend')),{self.scene},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
  bpy.context.window.scene=self.previous
  return report

 def presentation(self):
  sc=self.scene; sc.render.engine='BLENDER_EEVEE'
  sc.render.resolution_x=1100; sc.render.resolution_y=850; sc.render.resolution_percentage=100
  sc.render.image_settings.file_format='PNG'; sc.render.film_transparent=False
  sc.world=bpy.data.worlds.new('HR Studio '+self.label); sc.world.use_nodes=True
  bg=next(n for n in sc.world.node_tree.nodes if n.type=='BACKGROUND')
  bg.inputs[0].default_value=(.25,.29,.38,1)
  bg.inputs[1].default_value=.6
  sc.view_settings.view_transform='AgX'
  bounds=metrics(self.parts); lo=Vector(bounds['bounds_min']); hi=Vector(bounds['bounds_max']); center=(lo+hi)*.5
  size=max(hi-lo); scale=max(size,1)
  # Grey ground only for presentation, excluded from asset component list.
  bpy.ops.mesh.primitive_plane_add(size=scale*200,location=(0,0,lo.z-.025))
  g=bpy.context.object; g.name='STUDIO_ground_NOT_EXPORTED'
  gm=bpy.data.materials.get('HR Studio grey') or bpy.data.materials.new('HR Studio grey')
  gm.use_nodes=True; bs=next(n for n in gm.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value=(.12,.145,.19,1); bs.inputs['Roughness'].default_value=.85
  g.data.materials.append(gm)
  for n,vec,power,col in [('Key',(1,-1.3,2),90,(1,.94,.9)),('Fill',(-1.2,-.4,1),55,(.78,.86,1)),('Rim',(.7,1.4,1.5),110,(.92,.85,1))]:
   data=bpy.data.lights.new('STUDIO_'+n,'AREA'); data.energy=power*scale*scale; data.shape='DISK'; data.size=scale*1.4
   data.color=col; obj=bpy.data.objects.new('STUDIO_'+n,data); sc.collection.objects.link(obj)
   obj.location=center+Vector(vec)*scale; obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler()
  camd=bpy.data.cameras.new('STUDIO_Camera'); cam=bpy.data.objects.new('STUDIO_Camera',camd); sc.collection.objects.link(cam)
  vec=Vector((1.1,-1.5,1.25)) if self.index<11 else Vector((1.0,-1.9,.8))
  cam.location=center+vec*scale; cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
  camd.type='ORTHO'; camd.ortho_scale=scale*1.35 if self.index<11 else scale*1.48
  sc.camera=cam; sc.render.filepath=str(self.source/'preview.png')
  sc['asset_component_count']=len(self.parts); sc['delivery_fbx']=str(self.folder/(self.name+'.fbx'))

def metrics(objects):
 pts=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
 lo=[min(v[i] for v in pts) for i in range(3)]; hi=[max(v[i] for v in pts) for i in range(3)]
 return {'meshes':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),
  'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects),
  'bounds_min':lo,'bounds_max':hi,'dimensions':[hi[i]-lo[i] for i in range(3)]}

def mesh_audit(ob):
 bm=bmesh.new(); bm.from_mesh(ob.data)
 ret={'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
 bm.free(); return ret

def build_asset(index):
 b=Builder(index); b.build(); return b.export()

def render_asset(index):
 previous=bpy.context.scene
 sc=next(s for s in bpy.data.scenes if s.name=='HR_'+SPECS[index][1])
 bpy.context.window.scene=sc
 bpy.ops.render.render(write_still=True)
 bpy.context.window.scene=previous
 return {'preview':sc.render.filepath}
