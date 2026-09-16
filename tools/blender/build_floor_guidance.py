"""Build via Blender MCP. Metres, X width/Y depth/Z up, front -Y.
Only creates new task scenes; never clears or saves over the user's scene.
"""
import bpy, bmesh, math, json, numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'METAGAME/Assets/_Project/Models/FloorGuidance'
OUT.mkdir(parents=True,exist_ok=True)
SRC=OUT/'Source~'; SRC.mkdir(exist_ok=True)
PRE=OUT/'Previews~'; PRE.mkdir(exist_ok=True)
SPECS=[('01-floor-light','FloorLight'),('02-direction-tile','DirectionTile'),('03-stair-edge','StairEdgeLight'),('04-terrain-light','TerrainLight')]
BUILDERS={}
COLORS=[(.82,.84,.88),(.40,.30,.59),(.12,.15,.21),(.025,.033,.048),(.94,.94,.94),(.38,.43,.52),(.24,.18,.36),(.075,.09,.13)]
def active(o):
 bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o

def metrics(ob):
 bpy.context.view_layer.update()
 pts=[ob.matrix_world@Vector(v) for v in ob.bound_box]
 return {'dimensions':[max(v[i] for v in pts)-min(v[i] for v in pts) for i in range(3)],
 'min':[min(v[i] for v in pts) for i in range(3)],'max':[max(v[i] for v in pts) for i in range(3)],
 'vertices':len(ob.data.vertices),'triangles':len(ob.data.polygons),'uv_layers':len(ob.data.uv_layers),'materials':len(ob.data.materials)}

class Builder:
 def __init__(self,i):
  self.i=i; self.slug,self.label=SPECS[i]; self.name='SM_Astra_'+self.label
  self.folder=OUT/self.slug; self.folder.mkdir(exist_ok=True)
  self.tex=self.folder/(self.name+'.fbm'); self.tex.mkdir(exist_ok=True)
  self.prev=bpy.context.scene
  self.scene=bpy.data.scenes.new('FG_'+self.label); bpy.context.window.scene=self.scene
  self.scene.unit_settings.system='METRIC'; self.scene.unit_settings.scale_length=1
  self.parts=[]; self.images={}; self.materials=[]
  self.textures(); self.make_materials()

 def textures(self):
  n=512; yy,xx=np.mgrid[0:n,0:n]; tile=(xx//128)+4*(yy//256)
  rng=np.random.default_rng(560); noise=rng.normal(0,.002,(n,n))
  maps={}
  for kind in ['BaseColor','Metallic','Roughness','Normal','Emission','MS']:
   a=np.ones((n,n,4),dtype=np.float32)
   for k in range(8):
    mask=tile==k
    metal=[.05,.12,.8,0,0,.9,.15,.4][k]; rough=[.38,.4,.36,.72,.25,.3,.42,.5][k]
    if kind=='BaseColor':
     for c in range(3): a[:,:,c][mask]=np.clip(COLORS[k][c]+noise[mask],0,1)
    elif kind=='Metallic': a[:,:,:3][mask]=metal
    elif kind=='Roughness': a[:,:,:3][mask]=rough
    elif kind=='Normal': a[:,:,:3][mask]=(.5,.5,1)
    elif kind=='Emission': a[:,:,:3][mask]=1 if k==4 else 0
    elif kind=='MS': a[:,:,:3][mask]=(metal,0,0); a[:,:,3][mask]=1-rough
   im=bpy.data.images.new(self.name+'_'+kind,width=n,height=n,alpha=True)
   im.colorspace_settings.name='sRGB' if kind=='BaseColor' else 'Non-Color'
   im.pixels.foreach_set(a.ravel()); im.filepath_raw=str(self.tex/(self.name+'_'+kind+'.png')); im.file_format='PNG'; im.save()
   self.images[kind]=im

 def make_materials(self):
  for lens in [False,True]:
   m=bpy.data.materials.new('M_Astra_'+self.label+('_Lens' if lens else '_Body')); m.use_nodes=True
   ns=m.node_tree.nodes; lk=m.node_tree.links; bs=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
   for kind,socket in [('BaseColor','Base Color'),('Metallic','Metallic'),('Roughness','Roughness')]:
    t=ns.new('ShaderNodeTexImage'); t.image=self.images[kind]; lk.new(t.outputs['Color'],bs.inputs[socket])
   if lens:
    t=ns.new('ShaderNodeTexImage'); t.image=self.images['Emission']
    mix=ns.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=1
    mix.inputs[2].default_value=(.018,.72,.85,1); lk.new(t.outputs['Color'],mix.inputs[1])
    lk.new(mix.outputs[0],bs.inputs['Emission Color']); bs.inputs['Emission Strength'].default_value=1.4
    self.tint=mix; self.lens_bs=bs
   self.materials.append(m)

 def finish(self,o,name,tile,bevel=0):
  o.name=self.label+'__'+name; o.data.materials.append(self.materials[1 if tile==4 else 0])
  if bevel:
   active(o); b=o.modifiers.new('Edge radii','BEVEL'); b.width=bevel; b.segments=2
   bpy.ops.object.modifier_apply(modifier=b.name)
  o.data.update()
  uv=o.data.uv_layers.new(name='UVMap') if not o.data.uv_layers else o.data.uv_layers[0]
  for p in o.data.polygons:
   axis=max(range(3),key=lambda a:abs(p.normal[a])); axes=[a for a in range(3) if a!=axis]
   coords=[o.data.vertices[o.data.loops[j].vertex_index].co for j in p.loop_indices]
   lo=[min(c[a] for c in coords) for a in axes]; hi=[max(c[a] for c in coords) for a in axes]
   for j,c in zip(p.loop_indices,coords):
    u=(c[axes[0]]-lo[0])/max(hi[0]-lo[0],1e-8); v=(c[axes[1]]-lo[1])/max(hi[1]-lo[1],1e-8)
    uv.data[j].uv=((tile%4+.12+.76*u)/4,(tile//4+.12+.76*v)/2)
  o['palette_cell']=tile; self.parts.append(o); return o

 def mesh(self,name,vs,fs,tile,bevel=0):
  me=bpy.data.meshes.new(name); me.from_pydata(vs,[],fs); me.update()
  o=bpy.data.objects.new(name,me); self.scene.collection.objects.link(o)
  return self.finish(o,name,tile,bevel)

 def box(self,name,loc,dim,tile,bevel=.001):
  bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.scale=dim
  bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  return self.finish(o,name,tile,min(bevel,min(dim)*.2))

 def outline(self,w,d,r):
  r=min(r,w/2,d/2); pts=[]
  for cx,cy,start in [(w/2-r,d/2-r,0),(-w/2+r,d/2-r,90),(-w/2+r,-d/2+r,180),(w/2-r,-d/2+r,270)]:
   for j in range(5):
    a=math.radians(start+90*j/4); pts.append((cx+r*math.cos(a),cy+r*math.sin(a)))
  return pts

 def prism(self,name,xy,z0,z1,tile,bevel=.0005):
  n=len(xy); vs=[(x,y,z) for z in (z0,z1) for x,y in xy]
  fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)]
  return self.mesh(name,vs,fs,tile,bevel)

 def plate(self,name,w,d,z,h,tile,r=.008,x=0,y=0):
  return self.prism(name,[(a+x,b+y) for a,b in self.outline(w,d,r)],z,z+h,tile,min(.0007,h*.15))

 def ring(self,name,w,d,iw,id,z0,z1,tile,r=.01,ir=.007):
  outer=self.outline(w,d,r); inner=self.outline(iw,id,ir); n=len(outer)
  vs=[(x,y,z) for z in [z0,z1] for loop in [outer,inner] for x,y in loop]; fs=[]
  for a in range(n):
   b=(a+1)%n
   fs.extend([(a,b,b+2*n,a+2*n),(a+n,a+3*n,b+3*n,b+n),
    (a+2*n,b+2*n,b+3*n,a+3*n),(a,a+n,b+n,b)])
  return self.mesh(name,vs,fs,tile,.0006)

 def cylinder(self,name,x,y,z,r,h,tile,verts=16):
  bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=h,location=(x,y,z+h/2))
  return self.finish(bpy.context.object,name,tile,min(.0003,h*.15))

 def screw(self,x,y,z):
  self.cylinder('Recessed screw well',x,y,z,.0048,.0008,3)
  self.cylinder('Steel screw head',x,y,z+.0006,.0036,.0008,5)
  self.cylinder('Hex socket',x,y,z+.0014,.0016,.0002,3,6)

 def floor(self,z=0):
  self.plate('Sealed base gasket',.348,.118,z,.004,3)
  self.ring('Pearl white housing',.35,.12,.294,.063,z+.004,z+.0335,0,.012,.011)
  self.plate('Optical well',.300,.069,z+.004,.026,2,.012)
  self.plate('Diffuser lens',.288,.057,z+.030,.003,4,.010)
  for x in [-.169,.169]:
   cap=self.plate('Lavender end bumper',.012,.10,z+.004,.0285,1,.003,x=x)
   cap.location.x=.001 if x>0 else -.001
  front=self.prism('Front service insert',[(-.031,-.002),(.031,-.002),(.025,.017),(-.025,.017)],-.001,.001,1,.0004)
  # local plane xy -> front world xz, local thickness -> y
  front.rotation_euler.x=math.pi/2; front.location=(0,-.0602,z+.010)
  for x in [-.013,.013]: self.box('Twin service slot',(x,-.0615,z+.017),(.012,.0005,.0025),3,.0002)
  self.box('Rear service hatch',(0,.0603,z+.018),(.060,.0012,.017),2,.001)
  for x in [-.15,.15]:
   for y in [-.045,.045]: self.screw(x,y,z+.0335)

 def direction(self):
  self.plate('Base gasket',.218,.218,0,.003,3)
  self.ring('White frame',.22,.22,.17,.17,.003,.0235,0,.014,.010)
  self.plate('Lavender inset',.171,.171,.003,.018,1,.010)
  # Both chevrons point to +Y (Unity back/+Z after import).
  for y in [-.035,.032]:
   pts=[(-.052,y-.019),(0,y+.012),(.052,y-.019),(.052,y+.002),(0,y+.035),(-.052,y+.002)]
   self.prism('Chevron gasket',pts,.0208,.0218,3,.0004)
   cx=sum(x for x,y in pts)/len(pts); cy=sum(y for x,y in pts)/len(pts)
   inset=[(cx+(x-cx)*.93,cy+(y-cy)*.90) for x,y in pts]
   self.prism('Chevron diffuser',inset,.0218,.023,4,.0004)
  for x in [-.092,.092]:
   for y in [-.092,.092]: self.screw(x,y,.0235)
  for x in [-.105,.105]: self.plate('End accents',.009,.14,.004,.018,1,.003,x=x)

 def stair(self):
  # Open L-section: rear is +Y, stair nose/front is -Y. Base pivot at bottom of front drop.
  self.box('Titanium upper backing',(0,.003,.051),(.398,.074,.009),2,.001)
  self.box('Titanium front backing',(0,-.034,.026),(.398,.009,.052),2,.001)
  self.box('White tread flange',(0,0,.056),(.4,.08,.005),0,.001)
  self.box('White upper front rail',(0,-.037,.052),(.39,.006,.012),0,.001)
  self.box('White lower front rail',(0,-.037,.008),(.39,.006,.012),0,.001)
  self.box('Dark optical bed',(0,-.038,.030),(.357,.003,.030),3,.001)
  self.box('Front diffuser',(0,-.040,.030),(.344,.0018,.023),4,.0003)
  for x in [-.194,.194]:
   self.box('Lavender vertical end',(x,-.035,.030),(.012,.010,.06),1,.001)
   self.box('Lavender upper end',(x,.001,.055),(.012,.074,.008),1,.001)
  for y in [-.018,-.004,.010,.024]: self.box('Antislip inset',(0,y,.0586),(.345,.0015,.0004),6,.0001)
  for x in [-.175,.175]:
   for y in [-.026,.027]: self.screw(x,y,.0585)

 def terrain(self):
  self.floor(.105)
  self.plate('Lavender cradle seal',.354,.124,.102,.003,1)
  self.plate('Tapered mounting cradle',.38,.14,.089,.013,2,.014)
  for x in [-.153,.153]:
   self.cylinder('Stake collar',x,0,.075,.018,.014,5)
   self.cylinder('Lavender collar ring',x,0,.073,.0185,.005,1)
   bpy.ops.mesh.primitive_cone_add(vertices=8,radius1=.002,radius2=.014,depth=.076,location=(x,0,.038))
   self.finish(bpy.context.object,'Ground anchor',2,.0004)

 def merge(self):
  bpy.context.view_layer.update(); vs=[]; fs=[]; uvs=[]; slots=[]
  for o in self.parts:
   off=len(vs); me=o.data
   vs.extend(tuple(o.matrix_world@v.co) for v in me.vertices)
   fs.extend(tuple(off+j for j in p.vertices) for p in me.polygons)
   slots.extend([1 if o['palette_cell']==4 else 0]*len(me.polygons))
   uvs.extend(tuple(a.uv) for a in me.uv_layers[0].data)
  me=bpy.data.meshes.new(self.name); me.from_pydata(vs,[],fs); me.update()
  for m in self.materials: me.materials.append(m)
  for p,idx in zip(me.polygons,slots): p.material_index=idx
  uv=me.uv_layers.new(name='UVMap')
  for a,co in zip(uv.data,uvs): a.uv=co
  ob=bpy.data.objects.new(self.name,me); self.scene.collection.objects.link(ob); active(ob)
  tri=ob.modifiers.new('Export triangles','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=tri.name)
  uv2=ob.data.uv_layers.new(name='LightmapUV'); ob.data.uv_layers.active_index=1
  bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
  bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.015,scale_to_bounds=True)
  bpy.ops.object.mode_set(mode='OBJECT'); ob.data.uv_layers.active_index=0; ob.data.uv_layers[0].active_render=True
  return ob

 def build(self):
  [self.floor,self.direction,self.stair,self.terrain][self.i]()
  ob=self.merge(); before=metrics(ob)
  bm=bmesh.new(); bm.from_mesh(ob.data)
  audit={'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-12 for f in bm.faces)}
  bm.free(); assert not any(audit.values()),audit
  fbx=self.folder/(self.name+'.fbx')
  bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},
   apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',mesh_smooth_type='FACE',
   use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,
   path_mode='RELATIVE',embed_textures=False,axis_forward='-Z',axis_up='Y')
  check=bpy.data.scenes.new('FG_CHECK_'+self.label); bpy.context.window.scene=check
  bpy.ops.import_scene.fbx(filepath=str(fbx),use_image_search=True)
  imported=[o for o in check.objects if o.type=='MESH']; assert len(imported)==1
  after=metrics(imported[0]); missing=[]; paths=[]
  for m in imported[0].data.materials:
   for n in m.node_tree.nodes:
    if n.type=='TEX_IMAGE' and n.image:
     p=Path(bpy.path.abspath(n.image.filepath)); paths.append(str(p))
     if not p.exists(): missing.append(str(p))
  assert after['triangles']==before['triangles']
  assert after['uv_layers']==2 and after['materials']==2
  assert max(abs(a-b) for a,b in zip(before['dimensions'],after['dimensions']))<.00001
  assert not missing
  report={'asset':self.name,'before_export':before,'reimport':after,'geometry_audit':audit,
   'missing_textures':missing,'texture_links':sorted(set(paths)),'roundtrip':'PASS','materials':[m.name for m in self.materials],
   'fbx':str(fbx.relative_to(ROOT)),'parts':len(self.parts),'units':'metres','pivot':'bottom center'}
  (self.folder/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
  bpy.context.window.scene=self.scene; bpy.data.objects.remove(ob,do_unlink=True)
  self.studio()
  bpy.data.libraries.write(str(SRC/(self.name+'.blend')),{self.scene},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
  bpy.context.window.scene=self.prev
  return report

 def studio(self):
  # Preview-only surface tint. Exported BaseColor stays neutral for recoloring.
  for link in list(self.lens_bs.inputs['Base Color'].links): self.materials[1].node_tree.links.remove(link)
  self.lens_bs.inputs['Base Color'].default_value=(.018,.32,.38,1)
  s=self.scene; s.render.engine='BLENDER_EEVEE'; s.render.resolution_x=1200; s.render.resolution_y=850
  s.render.resolution_percentage=100; s.render.image_settings.file_format='PNG'
  s.world=bpy.data.worlds.new('FG world '+self.label); s.world.use_nodes=True
  bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND'); bg.inputs[0].default_value=(.3,.27,.38,1); bg.inputs[1].default_value=.5
  s.view_settings.view_transform='AgX'
  bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.001))
  ground=bpy.context.object; ground.name='STUDIO_ONLY'
  m=bpy.data.materials.new('FG studio ground'); m.use_nodes=True
  next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(.20,.18,.26,1); ground.data.materials.append(m)
  center=Vector((0,0,.05 if self.i==3 else .02)); size=.4
  for name,vec,power in [('Key',(1,-1.3,2),60),('Fill',(-1,-.3,1),30),('Rim',(.5,1,1.5),55)]:
   d=bpy.data.lights.new('FG '+name,'AREA'); d.energy=power*size*size; d.shape='DISK'; d.size=size*1.4
   o=bpy.data.objects.new('FG '+name,d); s.collection.objects.link(o); o.location=center+Vector(vec)*size
   o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
  d=bpy.data.cameras.new('FG camera'); c=bpy.data.objects.new('FG camera',d); s.collection.objects.link(c)
  c.location=center+Vector((.8,-1.3,1))*size; c.rotation_euler=(center-c.location).to_track_quat('-Z','Y').to_euler()
  d.type='ORTHO'; d.ortho_scale=.50 if self.i!=1 else .37; s.camera=c
  s.render.filepath=str(PRE/(self.slug+'.png'))

def build_asset(i):
 b=Builder(i); BUILDERS[i]=b; return b.build()
def render_asset(i):
 b=BUILDERS[i]; prev=bpy.context.scene
 try:
  bpy.context.window.scene=b.scene; bpy.ops.render.render(write_still=True)
  return {'preview':b.scene.render.filepath}
 finally: bpy.context.window.scene=prev

