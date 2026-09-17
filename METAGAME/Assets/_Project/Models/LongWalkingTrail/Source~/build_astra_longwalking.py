"""Dimensioned Astra trail kit, authored via the connected Blender MCP.

Only newly created scenes/datablocks are modified. Call build_asset(index),
then finalize_library(). Blender coordinates: X width, Y length, Z height.
"""
import bpy, bmesh, math, json, shutil, numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path(r'C:\workspace\2-PJT\S15P21B202')
BASE = ROOT / 'METAGAME/Assets/_Project/Models/LongWalkingTrail'
BASE.mkdir(parents=True, exist_ok=True)
(BASE/'Source~').mkdir(exist_ok=True)
(BASE/'Previews~').mkdir(exist_ok=True)
ORIGINAL_SCENE = bpy.context.scene
SPECS = [
 ('01-long-walking-trail-0','Trail0',13.810682,5),
 ('02-long-walking-trail-1','Trail1',13.824023,5),
 ('03-long-walking-trail-2','Trail2',15.951557,6),
 ('04-long-walking-trail-3','Trail3',11.277806,4),
 ('05-long-walking-trail-4','Trail4',8.51025,3),
 ('06-hill-deck-transition','HillDeckTransition',2.7832742,2),
 ('07-observatory-entry-stairs','ObservatoryEntryStairs',0,0),
 ('08-observatory-instrument','ObservatoryInstrument',0,0),
 ('09-adjustable-trail-junction','TrailJunction',0,0),
 ('10-cliff-edge-guardrail','CliffEdgeGuardrail',0,0),
 ('11-spiral-hill-ascent','SpiralHillAscent',0,0),
]
# sRGB colour, metalness, perceptual roughness, emission.
PALETTE = [
 ((.82,.85,.89),.28,.36,(0,0,0)),
 ((.53,.48,.67),.32,.48,(0,0,0)),
 ((.27,.31,.38),.78,.34,(0,0,0)),
 ((.055,.070,.094),.12,.70,(0,0,0)),
 ((.13,.38,.46),.50,.22,(.01,.04,.05)),
 ((.24,.88,.96),.18,.27,(.12,.80,1)),
 ((.88,.62,.26),.22,.45,(.15,.07,.01)),
 ((.70,.72,.78),.65,.30,(0,0,0)),
 ((.13,.16,.22),.72,.46,(0,0,0)),
 ((.38,.35,.50),.34,.55,(0,0,0)),
 ((.86,.89,.92),.1,.55,(0,0,0)),
 ((.026,.033,.045),0,.88,(0,0,0)),
 ((.39,.45,.52),.80,.32,(0,0,0)),
 ((.31,.28,.42),.40,.49,(0,0,0)),
 ((.61,.67,.73),.45,.40,(0,0,0)),
 ((.025,.03,.04),.25,.55,(0,0,0)),
]
STATE = {'assets': [], 'scenes': []}

def textures(folder, stem):
    folder.mkdir(exist_ok=True)
    n=1024; t=n//4; rng=np.random.default_rng(20260916)
    arrays={k:np.ones((n,n,4),np.float32) for k in
            ['BaseColor','Metallic','Roughness','Normal','Emission','MetallicSmoothness']}
    for i,(color,metal,rough,emission) in enumerate(PALETTE):
        row,col=divmod(i,4); ys=slice(row*t,(row+1)*t); xs=slice(col*t,(col+1)*t)
        yy,xx=np.mgrid[0:t,0:t]; grain=rng.normal(0,.002,(t,t)).astype(np.float32)
        brush=np.sin(xx*.45)*.0015; field=grain+brush
        arrays['BaseColor'][ys,xs,:3]=np.clip(np.array(color)+field[:,:,None],0,1)
        arrays['Metallic'][ys,xs,:3]=metal
        rr=np.clip(rough+field*2,0,1)
        arrays['Roughness'][ys,xs,:3]=rr[:,:,None]
        arrays['Normal'][ys,xs,:3]=np.stack((.5+grain,.5+brush,np.ones_like(grain)),axis=-1)
        arrays['Emission'][ys,xs,:3]=emission
        arrays['MetallicSmoothness'][ys,xs,0]=metal
        arrays['MetallicSmoothness'][ys,xs,1:3]=0
        arrays['MetallicSmoothness'][ys,xs,3]=1-rr
    images={}
    for key,arr in arrays.items():
        image=bpy.data.images.new(stem+'_'+key,width=n,height=n,alpha=True)
        image.colorspace_settings.name='Non-Color'
        image.pixels.foreach_set(arr.ravel())
        image.filepath_raw=str(folder/(stem+'_'+key+'.png')); image.file_format='PNG'; image.save()
        if key in ('BaseColor','Emission'): image.colorspace_settings.name='sRGB'
        images[key]=image
    mat=bpy.data.materials.new('M_'+stem); mat.use_nodes=True
    ns=mat.node_tree.nodes; ls=mat.node_tree.links; bs=next(n for n in ns if n.type=='BSDF_PRINCIPLED')
    for i,(key,socket) in enumerate([('BaseColor','Base Color'),('Metallic','Metallic'),('Roughness','Roughness'),('Emission','Emission Color')]):
        node=ns.new('ShaderNodeTexImage'); node.image=images[key]; node.label=key; node.location=(-650,300-i*220)
        ls.new(node.outputs['Color'],bs.inputs[socket])
    bs.inputs['Emission Strength'].default_value=1.8
    node=ns.new('ShaderNodeTexImage'); node.image=images['Normal']; node.location=(-650,-600)
    nm=ns.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.25
    ls.new(node.outputs['Color'],nm.inputs['Color']);ls.new(nm.outputs['Normal'],bs.inputs['Normal'])
    return mat,images

def source_uv(obj):
    me=obj.data;cat=int(obj.get('atlas_tile',0))
    uv=me.uv_layers.active or me.uv_layers.new(name='UVMap')
    for poly in me.polygons:
        drop=max(range(3),key=lambda k:abs(poly.normal[k]));axes=[k for k in range(3) if k!=drop]
        pts=[me.vertices[me.loops[j].vertex_index].co for j in poly.loop_indices]
        lo=[min(v[a] for v in pts) for a in axes];hi=[max(v[a] for v in pts) for a in axes]
        for j,v in zip(poly.loop_indices,pts):
            u=(v[axes[0]]-lo[0])/max(hi[0]-lo[0],.000001);vv=(v[axes[1]]-lo[1])/max(hi[1]-lo[1],.000001)
            uv.data[j].uv=((cat%4+.05+.9*u)/4,(cat//4+.05+.9*vv)/4)

class Builder:
    def __init__(self, index):
        self.index=index; self.slug,self.name,self.length,self.panels=SPECS[index]
        self.stem='SM_Astra_'+self.name; self.path=BASE/self.slug; self.path.mkdir(exist_ok=True)
        if (self.path/(self.stem+'.fbx')).exists(): raise RuntimeError('Refusing to overwrite existing deliverable: '+self.stem)
        self.scene=bpy.data.scenes.new('Astra_LWT_'+self.name); bpy.context.window.scene=self.scene
        self.scene.unit_settings.system='METRIC';self.scene.unit_settings.scale_length=1
        self.parts=[];self.colliders=[]
        self.mat,self.images=textures(self.path/(self.stem+'.fbm'),self.stem)
    def finish(self,obj,name,cat,bevel=.025,collision=False):
        obj.name=self.name+'_'+name
        for c in list(obj.users_collection): c.objects.unlink(obj)
        self.scene.collection.objects.link(obj)
        obj['atlas_tile']=cat
        if collision:
            self.colliders.append(obj);obj.hide_render=True; obj.display_type='WIRE'
        else:
            self.parts.append(obj);obj.data.materials.append(self.mat)
            source_uv(obj)
            if bevel:
                mod=obj.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=bevel;mod.segments=2
                mod.limit_method='ANGLE'
        return obj
    def box(self,name,loc,dims,cat=0,bevel=.025,collision=False):
        bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=dims
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        return self.finish(o,name,cat,min(bevel,min(dims)*.22),collision)
    def mesh(self,name,verts,faces,cat=0,bevel=.015,collision=False):
        me=bpy.data.meshes.new(self.name+'_'+name);me.from_pydata(verts,[],faces);me.update()
        bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
        o=bpy.data.objects.new(self.name+'_'+name,me);self.scene.collection.objects.link(o)
        return self.finish(o,name,cat,bevel,collision)
    def poly(self,name,xy,z0,z1,cat=0,bevel=.015,collision=False):
        n=len(xy);v=[(x,y,z) for z in (z0,z1) for x,y in xy]
        f=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        return self.mesh(name,v,f,cat,min(bevel,(z1-z0)*.22),collision)
    def cylinder(self,name,loc,radius,depth,cat=2,axis='Z',vertices=24,bevel=.008,collision=False):
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
        o=bpy.context.object
        if axis=='Y':o.rotation_euler[0]=math.pi/2
        if axis=='X':o.rotation_euler[1]=math.pi/2
        for f in o.data.polygons:f.use_smooth=len(f.vertices)==4
        return self.finish(o,name,cat,bevel,collision)
    def beam(self,name,a,b,width,depth,cat=2):
        a,b=Vector(a),Vector(b);o=self.box(name,(a+b)/2,(width,depth,(b-a).length),cat)
        o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
    def ring(self,name,loc,r,thickness,cat=5):
        bpy.ops.mesh.primitive_torus_add(major_segments=48,minor_segments=8,location=loc,major_radius=r,minor_radius=thickness)
        o=bpy.context.object
        for f in o.data.polygons:f.use_smooth=True
        return self.finish(o,name,cat,0)
    def bolt(self,x,y,z,axis='Z',radius=.026):
        return self.cylinder('Captive_hex_bolt',(x,y,z),radius,.018,7,axis,6,.003)
    def arc(self,name,r0,r1,a0,a1,zfun,depth,cat=0,segments=12,bevel=0,collision=False):
        verts=[]
        for i in range(segments+1):
            a=a0+(a1-a0)*i/segments; z=zfun(a)
            for r,dz in ((r0,0),(r1,0),(r0,-depth),(r1,-depth)):
                verts.append((r*math.cos(a),r*math.sin(a),z+dz))
        faces=[]
        for i in range(segments):
            k=4*i;j=k+4
            faces.extend([(k,k+1,j+1,j),(k+2,j+2,j+3,k+3),(k,j,k+2+4,k+2),(k+1,k+3,j+3,j+1)])
        faces.extend([(0,2,3,1),(4*segments,4*segments+1,4*segments+3,4*segments+2)])
        return self.mesh(name,verts,faces,cat,bevel,collision)

def walkway(b):
    w=3.8 if b.index==5 else 4.4;h=.36 if b.index==5 else .6;l=b.length
    top=h/2;bottom=-h/2
    b.box('Continuous_chassis',(0,0,-.045),(w-.12,l,h-.09),8,.06)
    b.box('Smooth_walk_collision',(0,0,0),(w,l,h),0,0,True)
    for side in (-1,1):
        b.box('White_edge_frame',(side*(w/2-.11),0,top-.045),(.22,l,.09),0,.035)
        b.box('Guide_gasket',(side*(w/2-.27),0,top-.03),(.11,l-.32,.045),3,.012)
        for k in range(b.panels):
            y=-l/2+(k+.5)*l/b.panels
            b.box('Cyan_guide_segment',(side*(w/2-.27),y,top-.006),(.055,l/b.panels-.24,.012),5,.004)
            b.box('Side_service_recess',(side*(w/2-.045),y,-.045),(.04,l/b.panels-.30,h*.40),3,.015)
            b.box('Side_service_cover',(side*(w/2-.020),y,-.045),(.016,l/b.panels-.44,h*.30),2,.014)
            for off in (-.29,.29):
                b.bolt(side*(w/2-.008),y+off,-.04,'X',.023)
            for off in (-l/b.panels/2+.18,l/b.panels/2-.18):
                b.bolt(side*(w/2-.10),y+off,top-.006)
    pw=w-.70
    for k in range(b.panels):
        y=-l/2+(k+.5)*l/b.panels;length=l/b.panels-.05
        b.box('Lavender_floor_panel',(0,y,top-.026),(pw,length,.052),1,.016)
        for dy in (-length/2+.07,-length/2+.11,-length/2+.15):
            b.box('Flush_grip_inlay',(0,y+dy,top-.0005),(pw-.24,.016,.001),9,.0002)
        b.box('Underside_crossmember',(0,y,bottom+.045),(w-.22,.16,.09),2,.02)
    for end in (-1,1):
        b.box('Flush_end_threshold',(0,end*(l/2-.055),top-.025),(w-.3,.11,.05),0,.015)
        for side in (-1,1):
            b.box('Coupler_socket',(side*(w/2-.50),end*(l/2-.017),-.04),(.38,.035,h*.48),3,.02)
            b.box('Coupler_cyan_key',(side*(w/2-.50),end*(l/2-.004),-.04),(.07,.012,h*.26),5,.004)
            b.box('Underside_mount_pad',(side*(w/2-.4),end*(l/2-.4),bottom+.03),(.42,.45,.06),7,.025)
    if b.index in (0,3):
        y=-l/2+1 if b.index==0 else l/2-1
        b.poly('Direction_chevron',[(-.36,y-.11),(0,y+.15),(.36,y-.11),(.36,y+.03),(0,y+.31),(-.36,y+.03)],top,top+.001,5,.0001)
    if b.index==4:
        y=l/2-1.5
        b.ring('Arrival_waypoint',(0,y,top-.012),.48,.015,5)
        b.ring('Arrival_inner_ring',(0,y,top-.012),.31,.015,5)
        b.cylinder('Arrival_center',(0,y,top),.10,.002,5,vertices=32,bevel=0)

def stairs(b):
    width=3.355;depth=2.04
    for i in range(4):
        front=-depth/2+i*.48;height=.24+i*.19
        segment_depth=.60 if i==3 else .48
        # Recess the chassis 1 cm beneath the tread to prevent coplanar faces.
        chassis_height=height-.01
        b.box('Step_structural_'+str(i),(0,front+segment_depth/2,chassis_height/2),(width-.18,segment_depth,chassis_height),8,.025)
        b.box('Step_lavender_tread',(0,front+.235,height-.022),(width-.22,.45,.044),1,.012)
        b.box('Riser_light_gasket',(0,front-.006,height-.09),(width-.36,.025,.065),3,.01)
        b.box('Cyan_riser_marker',(0,front-.021,height-.09),(width-.55,.012,.025),5,.003)
        for side in (-1,1):
            b.box('White_step_cheek',(side*(width/2-.045),front+.24,height/2),(.09,.48,height),0,.018)
            b.bolt(side*(width/2-.045),front+.12,height)
        cd=.60 if i==3 else .48
        b.box('Step_collision',(0,front+cd/2,height/2),(width,cd,height),0,0,True)
    b.box('Rear_service_panel',(0,depth/2+.008,.43),(width-.3,.022,.55),1,.025)

def instrument(b):
    b.cylinder('Rubber_foot',(0,0,.045),.39,.09,11,vertices=48)
    b.cylinder('Titanium_base',(0,0,.115),.4,.08,8,vertices=48)
    b.cylinder('Pearl_housing',(0,0,.475),.38,.65,0,vertices=64,bevel=.028)
    b.cylinder('Upper_gasket',(0,0,.811),.375,.025,3,vertices=64)
    b.ring('Cyan_sensor_rim',(0,0,.838),.346,.010,5)
    b.cylinder('Sensor_glass',(0,0,.840),.332,.022,4,vertices=64)
    for r in (.22,.28):b.ring('Etched_sensor_reticle',(0,0,.852),r,.0025,5)
    b.box('Front_hatch_gasket',(0,-.367,.47),(.43,.03,.45),3,.03)
    b.box('Lavender_access_hatch',(0,-.388,.47),(.38,.019,.395),1,.028)
    b.box('Hatch_status',(0,-.401,.605),(.14,.008,.023),5,.003)
    b.box('Hatch_latch',(0,-.403,.305),(.10,.015,.043),8,.01)
    for x in (-.145,.145):
        for z in (.32,.62):b.bolt(x,-.405,z,'Y',.009)
    b.cylinder('Vent_recess',(.372,0,.50),.115,.035,3,'X',32)
    for k in range(-3,4):
        z=.50+k*.025;length=2*math.sqrt(max(0,.102**2-(k*.025)**2))
        b.box('Vent_louver',(.396,0,z),(.025,length,.012),2,.003)
    b.cylinder('Rear_socket',(0,.375,.25),.065,.034,3,'Y',24)
    for k in range(5):
        a=2*math.pi*k/5;b.cylinder('Socket_contact',(.032*math.cos(a),.397,.25+.032*math.sin(a)),.006,.01,7,'Y',8,.001)
    b.ring('Base_cyan_band',(0,0,.155),.394,.008,5)
    b.cylinder('Sensor_collision',(0,0,.43),.405,.86,0,vertices=24,bevel=0,collision=True)

def junction(b):
    xy=[(-1.9,-1.2),(1.9,-1.2),(2.2,-.9),(2.2,.7),(1.6,1.2),(-1.6,1.2),(-2.2,.7),(-2.2,-.9)]
    b.poly('Octagonal_chassis',xy,-.1,.06,8,.025)
    inner=[(x*.96,y*.94) for x,y in xy];b.poly('White_perimeter',inner,.04,.10,0,.02)
    inner=[(x*.87,y*.79) for x,y in xy];b.poly('Lavender_top',inner,.1002,.104,1,.0006)
    for side in (-1,1):
        a=Vector((side*1.65,.85,.1046));c=Vector((0,-.28,.1046))
        b.beam('Flush_radial_seam',a,c,.018,.001,3)
        b.box('Edge_light',(side*2.01,-.1,.095),(.035,1.10,.012),5,.003)
        b.box('Underside_slide_rail',(side*.85,0,-.08),(.13,1.8,.04),7,.01)
        for y in (-.80,.65):b.bolt(side*1.80,y,.102)
    b.box('Front_cyan_strip',(0,-1.08,.096),(1.15,.04,.012),5,.004)
    b.poly('Direction_arrow',[(-.19,-.48),(0,-.28),(.19,-.48),(.19,-.34),(0,-.13),(-.19,-.34)],.1041,.105,5,.0001)
    b.poly('Junction_collision',xy,-.1,.104,0,0,True)

def guardrail(b):
    for x in (-.86,.86):
        b.box('Mounting_foot',(x,0,.025),(.28,.18,.05),8,.02)
        b.box('Pearl_upright',(x,0,.51),(.13,.12,.97),0,.045)
        b.box('Lavender_post_insert',(x,-.061,.46),(.063,.015,.5),1,.012)
        for dx in (-.08,.08):b.bolt(x+dx,0,.052,radius=.016)
        b.beam('Foot_gusset',(x,.075,.055),(x,.045,.19),.095,.018,2)
    b.box('Rounded_handrail',(0,0,.94),(1.85,.14,.12),0,.055)
    b.box('Cyan_inward_guide',(0,-.073,.90),(1.58,.018,.025),5,.006)
    b.box('Lavender_kickplate',(0,0,.18),(1.68,.07,.22),1,.022)
    for x in (-.73,.73):
        for z in (.12,.24):b.bolt(x,-.044,z,'Y',.012)
    b.box('Guardrail_collision',(0,0,.5),(2,.18,1),0,0,True)

def spiral(b):
    # Single open 270-degree helix: radius 14m, rise 7m, southern low entry.
    a0=-math.pi/2;a1=math.pi;span=a1-a0;rise=7; r0=11.8;r1=16.2
    z=lambda a:.6+rise*(a-a0)/span
    b.arc('Continuous_rising_chassis',r0,r1,a0,a1,z,.6,8,144)
    for rlo,rhi in ((r0,r0+.19),(r1-.19,r1)):
        b.arc('Pearl_continuous_edge',rlo,rhi,a0,a1,lambda a:z(a)+.022,.10,0,144)
    count=36;step=span/count
    for i in range(count):
        aa=a0+i*step;ab=aa+step
        b.arc('Lavender_curve_panel_%02d'%i,r0+.32,r1-.32,aa+.0009,ab-.0009,lambda a:z(a)+.001,.045,1,4)
        for ra,rb in ((r0+.235,r0+.280),(r1-.280,r1-.235)):
            b.arc('Cyan_curve_guide',ra,rb,aa+.006,ab-.006,lambda a:z(a)+.010,.015,5,4)
        ac=(aa+ab)/2
        if i%2==0:
            b.beam('Underside_radial_rib',(r0*math.cos(ac),r0*math.sin(ac),z(ac)-.48),(r1*math.cos(ac),r1*math.sin(ac),z(ac)-.48),.13,.14,2)
            for r in (r0+.1,r1-.1):b.bolt(r*math.cos(ac),r*math.sin(ac),z(ac)+.027,radius=.035)
    for i in range(1,10):
        a=a0+span*i/10
        for r in (r0+.55,r1-.55):
            x,y=r*math.cos(a),r*math.sin(a);height=z(a)-.62
            b.box('Support_foot',(x,y,.04),(.66,.66,.08),8,.035)
            b.cylinder('Telescopic_lower',(x,y,height*.30),.14,height*.6,2,vertices=12)
            b.cylinder('Telescopic_upper',(x,y,height*.74),.105,height*.52,7,vertices=12)
            b.cylinder('Adjustment_collar',(x,y,height*.56),.18,.15,8,vertices=12)
            b.box('Support_saddle',(x,y,height-.04),(.44,.44,.16),0,.025)
            for dx in (-.22,.22):
                for dy in (-.22,.22):b.bolt(x+dx,y+dy,.09,radius=.034)
    b.arc('Continuous_spiral_collision',r0,r1,a0,a1,lambda a:z(a)+.022,.622,0,144,0,True)
    b.scene['spiral_spec']='centerline radius 14m; width 4.4m; rise 7m; 270 degrees; static non-convex collider'

def merge_evaluated(b,objects,name,material=True):
    bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
    vertices=[];faces=[];tiles=[];smooth=[]
    for ob in objects:
        ev=ob.evaluated_get(dg);me=ev.to_mesh();offset=len(vertices)
        vertices.extend(tuple(ob.matrix_world@v.co) for v in me.vertices)
        for f in me.polygons:
            faces.append(tuple(offset+j for j in f.vertices));tiles.append(int(ob.get('atlas_tile',0)));smooth.append(f.use_smooth)
        ev.to_mesh_clear()
    me=bpy.data.meshes.new(name);me.from_pydata(vertices,[],faces);me.update()
    uv=me.uv_layers.new(name='UVMap')
    for poly,cat,sm in zip(me.polygons,tiles,smooth):
        poly.use_smooth=sm
        drop=max(range(3),key=lambda k:abs(poly.normal[k]));axes=[k for k in range(3) if k!=drop]
        pts=[me.vertices[me.loops[j].vertex_index].co for j in poly.loop_indices]
        lo=[min(v[a] for v in pts) for a in axes];hi=[max(v[a] for v in pts) for a in axes]
        for j,v in zip(poly.loop_indices,pts):
            u=(v[axes[0]]-lo[0])/max(hi[0]-lo[0],.000001);vv=(v[axes[1]]-lo[1])/max(hi[1]-lo[1],.000001)
            uv.data[j].uv=((cat%4+.05+.9*u)/4,(cat//4+.05+.9*vv)/4)
    ob=bpy.data.objects.new(name,me);b.scene.collection.objects.link(ob)
    if material:me.materials.append(b.mat)
    return ob

def metrics(objects):
    points=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box]
    mn=[min(p[i] for p in points) for i in range(3)];mx=[max(p[i] for p in points) for i in range(3)]
    return {'vertices':sum(len(o.data.vertices) for o in objects),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects),'bounds_min':mn,'bounds_max':mx,'dimensions_blender_xyz':[mx[i]-mn[i] for i in range(3)]}

def audit(mesh):
    bm=bmesh.new();bm.from_mesh(mesh)
    data={'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-10 for f in bm.faces)}
    bm.free();return data

def export_selected(ob,path):
    bpy.ops.object.select_all(action='DESELECT');ob.hide_set(False);ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,use_tspace=True,bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False,axis_forward='-Z',axis_up='Y',use_custom_props=True)

def studio_render(b,ob,view='concept'):
    scene=b.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1200;scene.render.resolution_y=850;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new(b.name+'_Studio');scene.world.use_nodes=True
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs[0].default_value=(.32,.36,.44,1)
    bg.inputs[1].default_value=.55
    scene.view_settings.view_transform='AgX'
    bounds=metrics([ob]);mn=Vector(bounds['bounds_min']);mx=Vector(bounds['bounds_max']);center=(mn+mx)/2;size=max(mx-mn)
    rig=[]
    for name,offset,power,scale in [('Key',(-.6,-.8,1.3),1600,1),('Fill',(.8,-.2,.6),1100,.7),('Rim',(.3,.9,1),1900,.8)]:
        ld=bpy.data.lights.new(b.name+'_'+name,'AREA');lo=bpy.data.objects.new(b.name+'_'+name,ld);scene.collection.objects.link(lo)
        lo.location=center+Vector(offset)*size;ld.energy=power*(size/8)**2;ld.shape='DISK';ld.size=size*scale
        lo.rotation_euler=(center-lo.location).to_track_quat('-Z','Y').to_euler();rig.append(lo)
    cd=bpy.data.cameras.new(b.name+'_Camera');cam=bpy.data.objects.new(b.name+'_Camera',cd);scene.collection.objects.link(cam)
    scene.camera=cam;cd.type='ORTHO';cd.lens=45;cd.clip_end=1000
    direction=Vector((1,-1.4,1.15)) if view=='concept' else Vector((0,0,1))
    cam.location=center+direction.normalized()*size*2.8;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.view_layer.update()
    corners=[cam.matrix_world.inverted()@(ob.matrix_world@Vector(c)) for c in ob.bound_box]
    sx=max(c.x for c in corners)-min(c.x for c in corners);sy=max(c.y for c in corners)-min(c.y for c in corners)
    cd.ortho_scale=max(sx,sy*(1200/850))*1.16
    scene.render.filepath=str(BASE/'Previews~'/(b.slug+'-'+view+'.png'))
    bpy.ops.render.render(write_still=True)
    return scene.render.filepath

def build_asset(index,render=True):
    if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    b=Builder(index)
    if index<6:walkway(b)
    else:{6:stairs,7:instrument,8:junction,9:guardrail,10:spiral}[index](b)
    visual=merge_evaluated(b,b.parts,b.stem);collider=merge_evaluated(b,b.colliders,'UCX_'+b.stem,False)
    for o in b.parts+b.colliders:o.hide_render=True;o.hide_set(True)
    collider.hide_render=True;collider.hide_set(True)
    bpy.context.view_layer.update();before=metrics([visual]);qa=audit(visual.data);cq=audit(collider.data)
    if qa['degenerate_faces'] or cq['degenerate_faces']:raise RuntimeError('Degenerate geometry '+str((qa,cq)))
    visual['authoring']='Blender MCP; Astra trail concepts';visual['units']='metres';visual['front']='Blender -Y; Unity +Z after FBX conversion'
    export_selected(visual,b.path/(b.stem+'.fbx'))
    cp=b.path/'Colliders';cp.mkdir(exist_ok=True)
    export_selected(collider,cp/('UCX_'+b.stem+'.fbx'));collider.hide_set(True)
    check=bpy.data.scenes.new('LWT_CHECK_'+b.name);bpy.context.window.scene=check
    bpy.ops.import_scene.fbx(filepath=str(b.path/(b.stem+'.fbx')),use_image_search=True)
    bpy.context.view_layer.update();imported=[o for o in check.objects if o.type=='MESH'];after=metrics(imported)
    missing=[];linked=[]
    for o in imported:
        if not o.data.uv_layers:raise RuntimeError('Missing UV')
        for mat in o.data.materials:
            if mat and mat.use_nodes:
                for nd in mat.node_tree.nodes:
                    if nd.type=='TEX_IMAGE' and nd.image:
                        path=Path(bpy.path.abspath(nd.image.filepath));linked.append(str(path))
                        if not path.is_file():missing.append(str(path))
    if before['triangles']!=after['triangles'] or max(abs(x-y) for x,y in zip(before['dimensions_blender_xyz'],after['dimensions_blender_xyz']))>.002:raise RuntimeError('FBX roundtrip mismatch')
    if missing or not linked:raise RuntimeError('FBX texture links missing '+str(missing))
    bpy.context.window.scene=b.scene
    # Remove only the temporary QA import and its own scene, never user's objects.
    for o in list(check.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(check)
    report={'id':'L%02d'%(index+1),'folder':b.slug,'fbx':b.stem+'.fbx','material':b.mat.name,'source_components':len(b.parts),'geometry':before,'roundtrip':after,'roundtrip_status':'PASS','visual_mesh_audit':qa,'collision_mesh_audit':cq,'collision_triangles':metrics([collider])['triangles'],'texture_links':sorted(set(linked)),'missing_textures':missing,'uv0':'4x4 PBR trim atlas','unity_lightmap_uv':'generate in ModelImporter','units':'metres','pivot':'scene-compatible centered deck for L01-L06/L09; ground-center for L07/L08/L10; spiral center at ground level for L11'}
    if render:
        report['preview']=studio_render(b,visual)
    for image in b.images.values():image.pack()
    (b.path/'asset-info.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    STATE['assets'].append(report);STATE['scenes'].append(b.scene)
    (BASE/'validation.json').write_text(json.dumps(STATE['assets'],indent=2),encoding='utf-8')
    return report

def finalize_library():
    bpy.data.libraries.write(str(BASE/'Source~/Astra_LongWalkingTrail.blend'),set(STATE['scenes']),path_remap='RELATIVE_ALL',fake_user=True,compress=True)
    shutil.copy2(__file__,BASE/'Source~/build_astra_longwalking.py')
    bpy.context.window.scene=ORIGINAL_SCENE
    return {'assets':len(STATE['assets']),'source_blend':str(BASE/'Source~/Astra_LongWalkingTrail.blend'),'zip_files':[str(p) for p in BASE.rglob('*.zip')]}
