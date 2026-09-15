"""Astra Shelter authored through Blender MCP. Standalone, non-destructive new scene."""
import bpy, math, os, json, numpy as np
from mathutils import Vector
from pathlib import Path
BASE=Path(r"C:\workspace\2-PJT\S15P21B202\artifacts\stage1-shelter")
BASE.mkdir(parents=True,exist_ok=True)
TEX=BASE/"AstraShelter.fbm"; TEX.mkdir(exist_ok=True)
if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.data.scenes.new("Astra_Shelter_Production")
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=1600; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.film_transparent=False
scene.world=bpy.data.worlds.new("Astra_Studio_World")
scene.world.use_nodes=True
background=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
background.inputs[0].default_value=(.22,.26,.35,1)
background.inputs[1].default_value=.45
try: scene.view_settings.view_transform='AgX'
except: pass
root=bpy.data.objects.new("AstraShelter_ROOT",None); scene.collection.objects.link(root)
root["dimensions_m"]="14 W x 12 D plus 2.6m front ramp; roof and antenna above"
root["front"]="-Y in Blender; exported with -Z forward / Y up"
root["cargo_clearance"]="Open 4.4m doorway; large empty explorable interior"
groups={}
def group(n):
    if n not in groups:
        c=bpy.data.collections.new("SH_"+n); scene.collection.children.link(c); groups[n]=c
    return groups[n]
def link(o,g):
    for c in list(o.users_collection): c.objects.unlink(o)
    group(g).objects.link(o); o.parent=root
    return o
# One portable PBR atlas. Geometry supplies seams, recesses, louvers, fasteners and structural detail.
palette=[
("Pearl",(.79,.81,.85),.35,.34,(0,0,0)),
("Lavender",(.43,.39,.60),.4,.4,(0,0,0)),
("Titanium",(.40,.45,.52),.82,.29,(0,0,0)),
("Gasket",(.055,.075,.105),.1,.64,(0,0,0)),
("Glass",(.075,.19,.25),.65,.18,(.015,.07,.09)),
("Cyan",(.24,.85,.95),.15,.24,(.16,.80,1)),
("Amber",(.94,.56,.12),.2,.3,(1,.40,.055)),
("WarmLight",(.9,.9,.82),.1,.3,(1,.90,.66)),
("DarkSteel",(.17,.20,.26),.78,.43,(0,0,0)),
("Floor",(.32,.35,.42),.65,.44,(0,0,0)),
("PurpleDark",(.24,.19,.36),.45,.44,(0,0,0)),
("Rubber",(.028,.037,.052),0,.85,(0,0,0)),
("WhiteMark",(.80,.85,.87),.1,.7,(0,0,0)),
("YellowMark",(.82,.54,.13),.1,.6,(0,0,0)),
("Interior",(.50,.53,.58),.4,.47,(0,0,0)),
("Black",(.025,.030,.039),.3,.6,(0,0,0))]
N=2048; tile=N//4
rng=np.random.default_rng(217)
arrays={k:np.ones((N,N,4),dtype=np.float32) for k in ("BaseColor","Metallic","Roughness","Normal","Emission","MetallicSmoothness")}
for idx,(_,color,metal,rough,emit) in enumerate(palette):
    row,col=divmod(idx,4)
    ys=slice(row*tile,(row+1)*tile); xs=slice(col*tile,(col+1)*tile)
    yy,xx=np.mgrid[0:tile,0:tile]
    grain=rng.normal(0,.003,(tile,tile)).astype(np.float32)
    brushing=np.sin(xx*.38)*.002
    field=grain+brushing
    arrays["BaseColor"][ys,xs,:3]=np.clip(np.array(color)+field[:,:,None],0,1)
    arrays["Metallic"][ys,xs,:3]=metal
    arrays["Roughness"][ys,xs,:3]=np.clip(rough+field[:,:,None]*1.8,0,1)
    arrays["MetallicSmoothness"][ys,xs,0]=metal
    arrays["MetallicSmoothness"][ys,xs,1:3]=0
    arrays["MetallicSmoothness"][ys,xs,3]=1-np.clip(rough+field*1.8,0,1)
    arrays["Emission"][ys,xs,:3]=emit
    arrays["Normal"][ys,xs,:3]=np.stack((.5+grain*.4,.5+grain*.4,np.ones_like(grain)),axis=-1)
images={}
for name,pixels in arrays.items():
    img=bpy.data.images.new("AstraShelter_"+name,width=N,height=N,alpha=True)
    img.colorspace_settings.name='Non-Color'
    img.pixels.foreach_set(pixels.ravel()); img.filepath_raw=str(TEX/(img.name+".png")); img.file_format='PNG'; img.save()
    if name in ("BaseColor","Emission"): img.colorspace_settings.name='sRGB'
    images[name]=img
del arrays
mat=bpy.data.materials.new("M_AstraShelter_PBR"); mat.use_nodes=True
nodes=mat.node_tree.nodes; links=mat.node_tree.links; bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
for i,(name,socket) in enumerate([("BaseColor","Base Color"),("Metallic","Metallic"),("Roughness","Roughness"),("Emission","Emission Color")]):
    node=nodes.new("ShaderNodeTexImage"); node.image=images[name]; node.label=name; node.location=(-650,300-i*250)
    links.new(node.outputs['Color'],bsdf.inputs[socket])
bsdf.inputs["Emission Strength"].default_value=2.3
normal=nodes.new("ShaderNodeTexImage"); normal.image=images["Normal"]; normal.location=(-650,-700)
nm=nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value=.35
links.new(normal.outputs["Color"],nm.inputs["Color"]); links.new(nm.outputs["Normal"],bsdf.inputs["Normal"])
def finish(o,n,g,category,bevel=.04):
    o.name=n; link(o,g); o.data.materials.append(mat)
    o["surface_atlas_tile"]=category
    if bevel:
        m=o.modifiers.new("Manufactured edge bevel","BEVEL"); m.width=bevel; m.segments=3
    uv=o.data.uv_layers.new(name="UVMap") if not o.data.uv_layers else o.data.uv_layers[0]
    verts=o.data.vertices
    # Each face uses its dominant axis for trim-sheet coordinates.
    for poly in o.data.polygons:
        normal=poly.normal; drop=max(range(3),key=lambda k:abs(normal[k]))
        axes=[k for k in range(3) if k!=drop]
        pts=[verts[o.data.loops[j].vertex_index].co for j in poly.loop_indices]
        lo=[min(p[a] for p in pts) for a in axes]; hi=[max(p[a] for p in pts) for a in axes]
        for j,p in zip(poly.loop_indices,pts):
            u=(p[axes[0]]-lo[0])/max(hi[0]-lo[0],.001)
            v=(p[axes[1]]-lo[1])/max(hi[1]-lo[1],.001)
            uv.data[j].uv=((category%4+.03+.94*u)/4,(category//4+.03+.94*v)/4)
    return o
def box(n,loc,dim,cat=0,g="01_Structure",bev=.04):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.scale=dim
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,n,g,cat,min(bev,min(dim)*.22) if bev else 0)
def mesh(n,verts,faces,cat,g,bev=.04):
    me=bpy.data.meshes.new(n); me.from_pydata(verts,[],faces); me.update()
    o=bpy.data.objects.new(n,me); group(g).objects.link(o)
    return finish(o,n,g,cat,bev)
def prism(n,coords,y0,y1,cat=0,g="01_Structure",bev=.06):
    # Polygon in X/Z extruded along Y.
    count=len(coords)
    vs=[(x,y,z) for y in (y0,y1) for x,z in coords]
    fs=[tuple(range(count-1,-1,-1)),tuple(range(count,count*2))]
    fs += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    o=mesh(n,vs,fs,cat,g,bev)
    # Recalculate consistent outward normals.
    import bmesh
    bm=bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(o.data); bm.free()
    return o
def beam(n,a,b,width,depth,cat=0,g="01_Structure",bev=.035):
    a,b=Vector(a),Vector(b)
    o=box(n,(a+b)/2,(width,depth,(b-a).length),cat,g,bev)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return o
def cyl(n,loc,radius,depth,cat=2,g="08_Fasteners",axis='Y',verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=loc)
    o=bpy.context.object
    if axis=='Y': o.rotation_euler[0]=math.pi/2
    elif axis=='X': o.rotation_euler[1]=math.pi/2
    return finish(o,n,g,cat,.009)

# Shelter-specific geometry. Front=-Y, usable interior remains unobstructed.
def recalc(o):
    import bmesh
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    return o
def roofz(x): return 5.70+1.35*math.sqrt(max(0,1-(x/7.0)**2))
def roofpatch(n,x0,x1,y0,y1,offset,thickness,cat,g,segments=12):
    pts=[(x0+(x1-x0)*i/segments,roofz(x0+(x1-x0)*i/segments)+offset) for i in range(segments+1)]
    return prism(n,pts+[(x,z-thickness) for x,z in reversed(pts)],y0,y1,cat,g,.0015 if 'busbar' in n else .013)
def rr(w,h,r):
    pts=[]
    for cx,cz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,90),(-w/2+r,-h/2+r,180),(w/2-r,-h/2+r,270)]:
        for j in range(7):
            a=math.radians(start+j*90/6)
            pts.append((cx+r*math.cos(a),cz+r*math.sin(a)))
    return pts
def orient(u,d,z,center,axis):
    return (center[0]+u,center[1]+d,center[2]+z) if axis=='Y' else (center[0]+d,center[1]+u,center[2]+z)
def frame(n,center,w,h,thick,depth,cat,g,axis='Y',radius=.28):
    outer=rr(w,h,radius);inner=rr(w-2*thick,h-2*thick,max(.04,radius-thick))
    vs=[orient(u,d,z,center,axis) for d in (-depth/2,depth/2) for loop in (outer,inner) for u,z in loop]
    L=len(outer);fs=[]
    for i in range(L):
        j=(i+1)%L
        fs.extend([(i,j,L+j,L+i),(2*L+i,3*L+i,3*L+j,2*L+j),
                   (i,2*L+i,2*L+j,j),(L+i,L+j,3*L+j,3*L+i)])
    return recalc(mesh(n,vs,fs,cat,g,.018))
def window(n,center,w,h,axis,sign):
    g="03_Windows"
    frame(n+"_outer_white",center,w+.25,h+.25,.15,.18,0,g,axis,.35)
    c=list(center);c[1 if axis=='Y' else 0]+=sign*.12
    frame(n+"_charcoal_seal",c,w,h,.11,.10,3,g,axis,.27)
    c[1 if axis=='Y' else 0]+=sign*.065
    frame(n+"_titanium_inner",c,w-.20,h-.20,.048,.065,2,g,axis,.18)
    dim=(w-.32,.036,h-.32) if axis=='Y' else (.036,w-.32,h-.32)
    box(n+"_tinted_glazing",c,dim,4,g,.10)
    c[2]+=h/2-.25;c[1 if axis=='Y' else 0]+=sign*.035
    dim=(w-.68,.028,.06) if axis=='Y' else (.028,w-.68,.06)
    box(n+"_cyan_top_strip",c,dim,5,"10_Emission",.014)
# Base plate and walkable tiled deck.
box("SH_Insulated_base",(0,0,.16),(14,12,.32),8,"02_Floor",.19)
for ix in range(7):
    for iy in range(6):
        box(f"SH_Floor_tile_{ix}_{iy}",(-5.92+ix*1.97,-4.93+iy*1.97,.35),(1.94,1.94,.06),9,"02_Floor",.012)
for x in (-2.15,2.15):
    box("SH_Path_inset",(x,0,.387),(.11,11.40,.025),10,"02_Floor",.004)
    for j in range(6):
        box("SH_Wayfinding_tick",(x,-4.8+j*1.92,.405),(.05,.48,.014),5,"10_Emission",.002)
for s in (-1,1):
    for j in range(6):
        box("SH_Floor_perimeter_vent",(s*6.18,-4.83+j*1.9,.389),(.31,1.60,.04),3,"09_Grilles",.009)
        for k in range(8):
            box("SH_Floor_vent_slit",(s*6.18,-5.48+j*1.9+k*.18,.415),(.28,.047,.018),2,"09_Grilles",.004)
# Broad front ramp, continuous nonzero structural thickness.
rverts=[(-2.48,-8.6,.03),(2.48,-8.6,.03),(2.48,-6,.38),(-2.48,-6,.38),
        (-2.48,-8.6,0),(2.48,-8.6,0),(2.48,-6,0),(-2.48,-6,0)]
recalc(mesh("SH_Entrance_ramp",rverts,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],9,"02_Floor",.01))
pitch=math.atan2(.35,2.6)
for s in (-1,1):
    o=box("SH_Ramp_white_edge",(s*2.40,-7.3,.249),(.18,2.66,.13),0,"02_Floor",.035);o.rotation_euler[0]=pitch
    for j in range(13):
        y=-8.47+j*.19;z=.03+(y+8.6)*.35/2.6+.018
        o=box("SH_Ramp_grip_strip",(s*2.00,y,z),(.39,.065,.025),2,"09_Grilles",.006);o.rotation_euler[0]=pitch
    o=box("SH_Ramp_amber_marker",(s*2.4,-6.32,.426),(.10,.24,.015),6,"10_Emission",.02);o.rotation_euler[0]=pitch
for j in range(5):
    y=-8.3+j*.48;z=.03+(y+8.6)*.35/2.6+.012
    o=box("SH_Ramp_panel_joint",(0,y,z),(3.46,.024,.014),3,"02_Floor",.002);o.rotation_euler[0]=pitch
# Four chunky corner supports with smooth manufacturing bevels.
for sx in (-1,1):
    for sy in (-1,1):
        x=sx*6.68;y=sy*5.68
        box("SH_Corner_boot",(x,y,.17),(1.23,1.30,.34),8,"01_Frame",.14)
        box("SH_Corner_flared_foot",(x,y,.75),(1.10,1.17,1.06),0,"01_Frame",.20)
        box("SH_Corner_pillar",(x,y,3.37),(.88,.99,4.38),0,"01_Frame",.17)
        box("SH_Corner_shoulder",(x,y,5.52),(1.0,1.11,.93),0,"01_Frame",.21)
        for axis in ('Y','X'):
            center=(x,y+sy*.515,3.45) if axis=='Y' else (x+sx*.465,y,3.45)
            frame("SH_Pillar_inset_border",center,.50,3.28,.065,.06,3,"01_Frame",axis,.14)
            dims=(.36,.047,3.10) if axis=='Y' else (.047,.36,3.10)
            box("SH_Pillar_lavender_inset",center,dims,1,"01_Frame",.025)
            c=list(center);c[2]=2.02;c[1 if axis=='Y' else 0]+=(sy if axis=='Y' else sx)*.043
            box("SH_Pillar_amber_bar",c,(.18,.025,.075) if axis=='Y' else (.025,.18,.075),6,"10_Emission",.017)
        box("SH_Corner_foot_lamp",(x,y+sy*.603,.69),(.46,.045,.10),6,"10_Emission",.024)
        box("SH_Shoulder_amber",(x,y+sy*.575,5.35),(.38,.025,.07),6,"10_Emission",.015)
# Front flanking walls: clear 4.4m-wide central passage.
for side in (-1,1):
    x=side*4.53
    box("SH_Front_lower_wall",(x,-5.69,1.23),(4.08,.36,1.80),14,"04_Front",.05)
    box("SH_Front_upper_wall",(x,-5.69,4.84),(4.08,.36,1.66),14,"04_Front",.05)
    for xx in (side*2.67,side*6.39):
        box("SH_Front_window_jamb",(xx,-5.69,3.06),(.36,.36,1.88),0,"04_Front",.04)
    box("SH_Front_lavender_skirt",(x,-5.92,1.15),(3.98,.11,.92),1,"04_Front",.08)
    box("SH_Front_lavender_brow",(x,-5.92,4.80),(4.06,.11,.46),1,"04_Front",.04)
    window("SH_Front_window",(x,-5.96,3.07),3.55,1.67,'Y',-1)
# Sidewalls with two large real window apertures on each side.
for s in (-1,1):
    x=s*6.69
    box("SH_Side_lower_wall",(x,0,1.29),(.36,10.48,1.92),14,"05_SideWalls",.05)
    box("SH_Side_upper_wall",(x,0,4.82),(.36,10.48,1.74),14,"05_SideWalls",.05)
    for y in (-5.01,0,5.01):
        box("SH_Side_window_jamb",(x,y,3.11),(.36,.44,1.82),0,"05_SideWalls",.04)
    for y in (-2.55,2.55):
        window("SH_Side_window",(x+s*.25,y,3.12),4.34,1.70,'X',s)
        box("SH_Side_lavender_skirt",(x+s*.25,y,1.10),(.10,4.86,.93),1,"05_SideWalls",.06)
        box("SH_Side_lavender_brow",(x+s*.25,y,4.85),(.10,4.90,.46),1,"05_SideWalls",.04)
    # Low service plate matched to concept, with plugs and latch details.
    box("SH_Service_recess",(s*6.96,0,1.15),(.065,6.65,.99),3,"07_Service",.07)
    box("SH_Service_panel",(s*7.015,0,1.15),(.055,6.49,.85),0,"07_Service",.08)
    for yy in (-2.13,2.13):box("SH_Service_panel_seam",(s*7.05,yy,1.15),(.012,.018,.78),8,"07_Service",.002)
    box("SH_Power_socket_surround",(s*7.07,0,1.17),(.10,.65,.60),3,"07_Service",.07)
    box("SH_Power_socket_cap",(s*7.13,0,1.15),(.04,.45,.39),8,"07_Service",.05)
    box("SH_Power_socket_cyan",(s*7.155,0,1.36),(.018,.29,.039),5,"10_Emission",.008)
    box("SH_Service_status_amber",(s*7.07,2.52,1.36),(.025,.47,.068),6,"10_Emission",.015)
    for yy in (-3.06,-1.98,1.98,3.06):
        for z in (.88,1.44):cyl("SH_Service_captive_fastener",(s*7.06,yy,z),.033,.023,2,"08_Fasteners",'X')
# Rear wall with narrow paired windows and louvers.
box("SH_Rear_lower_wall",(0,5.69,1.55),(12.50,.36,2.44),14,"06_Rear",.05)
box("SH_Rear_upper_wall",(0,5.69,4.85),(12.50,.36,1.66),14,"06_Rear",.05)
for x in (-6.1,0,6.1):
    box("SH_Rear_window_jamb",(x,5.69,3.38),(.40,.36,1.23),0,"06_Rear",.04)
for s in (-1,1):
    x=s*3.12
    window("SH_Rear_window",(x,5.94,3.39),5.48,.99,'Y',1)
    box("SH_Rear_lavender_panel",(x,5.93,1.47),(5.92,.11,1.71),1,"06_Rear",.11)
    box("SH_Rear_vent_housing",(x,6.01,1.01),(2.93,.17,.70),0,"09_Grilles",.09)
    box("SH_Rear_vent_recess",(x,6.11,1.01),(2.63,.04,.47),3,"09_Grilles",.04)
    for j in range(5):box("SH_Rear_louver",(x,6.155,.84+j*.08),(2.44,.075,.036),2,"09_Grilles",.008)
    box("SH_Rear_lavender_brow",(x,5.94,4.83),(6.0,.10,.45),1,"06_Rear",.04)
box("SH_Rear_center_spine",(0,5.98,2.94),(.28,.16,5.12),0,"06_Rear",.035)
# Rounded front portal, door pockets, entry canopy and access control.
for s in (-1,1):
    box("SH_Door_pocket",(s*2.33,-5.73,2.70),(.35,.69,4.72),8,"11_Entry",.12)
    box("SH_Door_white_jamb",(s*2.55,-6.00,2.76),(.30,.26,4.70),0,"11_Entry",.10)
    box("SH_Retracted_door_leaf",(s*2.35,-5.75,2.54),(.20,.17,4.21),10,"12_DoorLeaves",.05)
    box("SH_Portal_amber_lens",(s*2.54,-6.155,3.75),(.058,.028,.41),6,"10_Emission",.02)
    box("SH_Portal_base_protector",(s*2.47,-6.05,.68),(.30,.35,.47),2,"11_Entry",.06)
    box("SH_Door_actuator",(s*2.49,-5.38,4.66),(.25,.35,.46),2,"11_Entry",.055)
box("SH_Portal_header",(0,-5.85,5.17),(5.26,.64,.67),0,"11_Entry",.15)
box("SH_Inner_door_track",(0,-5.49,4.93),(4.63,.13,.11),8,"11_Entry",.022)
box("SH_Threshold",(0,-5.97,.39),(4.4,.26,.055),2,"11_Entry",.012)
box("SH_Canopy_white_shell",(0,-6.31,5.39),(5.81,1.65,.36),0,"11_Entry",.18)
box("SH_Canopy_lavender_roof_inset",(0,-6.28,5.59),(4.82,1.08,.06),1,"11_Entry",.08)
box("SH_Canopy_dark_front_band",(0,-7.10,5.36),(4.69,.065,.16),3,"11_Entry",.045)
box("SH_Canopy_cyan_strip",(0,-7.144,5.36),(4.33,.025,.070),5,"10_Emission",.016)
for s in (-1,1):box("SH_Canopy_corner_amber",(s*2.54,-7.11,5.35),(.30,.026,.065),6,"10_Emission",.018)
box("SH_Door_control_plate",(2.86,-5.95,1.74),(.23,.12,.44),3,"11_Entry",.055)
for z in (1.62,1.75,1.88):box("SH_Door_control_status",(2.86,-6.02,z),(.12,.016,.027),5,"10_Emission",.004)
# End gables close the hollow room under the roof; roof panels remain separable.
for y in (-5.70,5.70):
    top=[(-6.72+13.44*i/32,roofz(-6.72+13.44*i/32)-.17) for i in range(33)]
    prism("SH_Upper_end_cap",[(-6.72,5.42),(6.72,5.42)]+list(reversed(top)),y-.15,y+.15,0,"01_Frame",.025)
# Continuous insulated roof liner prevents daylight leaks between decorative outer panels.
roofpatch("SH_Roof_insulation_liner",-6.98,6.98,-5.64,5.64,-.15,.13,14,"13_Roof",64)
for ix,(x0,x1) in enumerate([(-6.98,-5.0),(-4.97,-2.35),(-2.32,0),(.025,2.32),(2.35,4.97),(5,6.98)]):
    for iy,(y0,y1) in enumerate([(-5.51,-1.88),(-1.85,1.85),(1.88,5.51)]):
        roofpatch(f"SH_White_roof_panel_{ix}_{iy}",x0,x1,y0,y1,0,.14,0,"13_Roof",16)
# Front and rear roof structural ribs.
for y in (-5.71,5.71):
    roofpatch("SH_Rounded_roof_arch",-6.99,6.99,y-.32,y+.32,.14,.20,0,"01_Frame",64)
# Two curved photovoltaic banks, actual cell gaps and busbars, matching reference.
for side in (-1,1):
    xa,xb=(-5.1,-1.1) if side<0 else (1.1,5.1)
    roofpatch("SH_Solar_bank_white_frame",xa-.13,xb+.13,-4.22,2.48,.12,.11,0,"14_Solar",28)
    roofpatch("SH_Solar_bank_dark_seal",xa,xb,-4.08,2.34,.185,.07,3,"14_Solar",28)
    for col in range(6):
        x0=xa+.10+col*3.80/6;x1=x0+3.80/6-.032
        for row in range(9):
            y0=-3.97+row*6.20/9;y1=y0+6.20/9-.03
            roofpatch("SH_Photovoltaic_cell",x0,x1,y0,y1,.227,.018,1,"14_Solar",3)
            for bx in (.23,.68):
                xx=x0+(x1-x0)*bx
                roofpatch("SH_Solar_silver_busbar",xx,xx+.011,y0+.025,y1-.025,.250,.012,2,"14_Solar",1)
    for x in (xa+.15,xb-.15):
        for y in (-4.13,2.39):
            cyl("SH_Solar_frame_screw",(x,y,roofz(x)+.191),.035,.02,2,"08_Fasteners",'Z')
# Rear central air handler with actual vents, maintenance hatch and duct interface.
box("SH_HVAC_gasket",(0,4.20,7.08),(2.72,2.14,.22),3,"15_HVAC",.12)
box("SH_HVAC_shell",(0,4.20,7.54),(2.51,1.96,.80),0,"15_HVAC",.20)
box("SH_HVAC_lavender_top",(0,4.20,7.96),(1.99,1.44,.045),1,"15_HVAC",.09)
for sy in (-1,1):
    y=4.20+sy*.99
    frame("SH_HVAC_vent_bezel",(0,y,7.55),2.15,.57,.08,.075,8,"15_HVAC",'Y',.14)
    box("SH_HVAC_dark_recess",(0,y+sy*.02,7.55),(1.99,.04,.42),3,"15_HVAC",.045)
    for j in range(5):
        box("SH_HVAC_louver",(0,y+sy*.08,7.39+j*.08),(1.92,.07,.035),2,"15_HVAC",.008)
for sx in (-1,1):
    box("SH_HVAC_side_panel",(sx*1.245,4.2,7.57),(.05,1.13,.41),1,"15_HVAC",.055)
    box("SH_HVAC_side_status",(sx*1.278,4.18,7.59),(.02,.26,.05),6,"10_Emission",.01)
for x in (-.82,.82):
    for y in (3.64,4.76):cyl("SH_HVAC_lid_screw",(x,y,7.997),.035,.022,2,"08_Fasteners",'Z')
# Rear-left twin mast assembly; physically consistent across all views.
ax,ay=-5.6,4.45;az=roofz(ax)
box("SH_Antenna_mount",(ax,ay,az+.20),(1.01,.95,.33),0,"16_Antenna",.12)
for off,height,cat in [(-.21,2.38,5),(.24,1.55,6)]:
    x=ax+off
    cyl("SH_Antenna_socket",(x,ay,az+.46),.18,.22,8,"16_Antenna",'Z',24)
    cyl("SH_Antenna_base",(x,ay,az+.68),.115,.34,2,"16_Antenna",'Z',20)
    cyl("SH_Antenna_status_lens",(x,ay,az+1.01),.09,.38,cat,"16_Antenna",'Z',20)
    cyl("SH_Antenna_dark_collar",(x,ay,az+1.24),.11,.08,8,"16_Antenna",'Z',20)
    cyl("SH_Antenna_whip",(x,ay,az+1.25+(height-1.25)/2),.025,height-1.25,2,"16_Antenna",'Z',12)
    cyl("SH_Antenna_tip",(x,ay,az+height),.036,.06,8,"16_Antenna",'Z',12)
# Interior: framed wall liners, utility routing, protected storage recesses, ceiling service strips.
for s in (-1,1):
    for y in (-4.65,0,4.65):
        box("SH_Interior_wall_rib",(s*6.45,y,2.90),(.18,.25,5.02),0,"17_Interior",.07)
    for y in (-2.55,2.55):
        box("SH_Interior_sill",(s*6.40,y,2.13),(.21,4.36,.10),2,"17_Interior",.028)
        box("SH_Interior_low_band",(s*6.46,y,1.23),(.055,4.35,.42),10,"17_Interior",.022)
        box("SH_Interior_bumper",(s*6.36,y,.69),(.19,4.39,.22),8,"17_Interior",.045)
        box("SH_Interior_light_socket",(s*6.39,y,4.42),(.10,3.36,.17),3,"17_Interior",.04)
        box("SH_Interior_light_lens",(s*6.322,y,4.42),(.025,3.10,.065),7,"10_Emission",.014)
    for z in (1.57,1.75):
        beam("SH_Interior_conduit",(s*6.34,-4.9,z),(s*6.34,4.9,z),.048,.048,2,"17_Interior",.01)
    for y in (-4,0,4):box("SH_Conduit_clamp",(s*6.31,y,1.66),(.055,.095,.30),8,"17_Interior",.012)
for x in (-4.40,4.40):
    box("SH_Rear_inside_sill",(x,5.40,2.76),(3.11,.20,.12),2,"17_Interior",.026)
    box("SH_Rear_inside_bumper",(x,5.38,.72),(3.66,.18,.26),8,"17_Interior",.04)
box("SH_Rear_equipment_backplate",(0,5.43,2.30),(1.48,.13,2.20),3,"17_Interior",.11)
box("SH_Rear_equipment_panel",(0,5.33,2.30),(1.27,.10,1.97),0,"17_Interior",.10)
box("SH_Interior_service_display",(0,5.268,2.78),(.87,.023,.43),4,"17_Interior",.045)
for x in (-.28,0,.28):box("SH_Interior_service_display_tick",(x,5.245,2.78),(.10,.013,.042),5,"10_Emission",.005)
for j in range(6):box("SH_Interior_service_air_slot",(0,5.265,1.71+j*.061),(.80,.028,.025),8,"17_Interior",.004)
# Ceiling has a separate shallow service spine and task-light pairs.
box("SH_Ceiling_spine",(0,0,6.80),(.80,10.84,.12),0,"18_Ceiling",.04)
for y in (-3.7,0,3.7):
    box("SH_Ceiling_lamp_surround",(0,y,6.708),(.55,1.47,.08),3,"18_Ceiling",.05)
    box("SH_Ceiling_lamp",(0,y,6.66),(.31,1.19,.025),7,"18_Ceiling",.03)
# Captive fasteners around white facade panel joints.
for sy in (-1,1):
    for sx in (-1,1):
        for z in (1.53,4.55):
            cyl("SH_Facade_hex_screw",(sx*5.97,sy*5.96,z),.033,.023,2,"08_Fasteners",'Y')
# Presentation objects live in their own non-exported collection.
stage=group("99_Presentation_ONLY")
def stage_obj(o):
    for c in list(o.users_collection): c.objects.unlink(o)
    stage.objects.link(o);return o
bpy.ops.mesh.primitive_plane_add(size=180,location=(0,0,-.04))
ground=stage_obj(bpy.context.object);ground.name="SH_Studio_ground_NOT_EXPORTED"
gm=bpy.data.materials.new("SH_Studio_ground");gm.use_nodes=True
p=next(n for n in gm.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs["Base Color"].default_value=(.115,.145,.205,1);p.inputs["Roughness"].default_value=.8
ground.data.materials.append(gm)
def area(n,loc,power,size,target=(0,0,3)):
    d=bpy.data.lights.new(n,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=(.85,.90,1)
    o=bpy.data.objects.new(n,d);stage.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area("SH_Key",(2,-12,19),3400,12)
area("SH_Fill",(-15,-5,9),2400,10)
area("SH_Rim",(13,10,15),4200,10)
area("SH_Back",(-1,14,10),1800,10)
area("SH_Interior",(0,0,6.3),450,5,(0,0,.4))
cd=bpy.data.cameras.new("SH_ReviewCamera");cam=bpy.data.objects.new("SH_ReviewCamera",cd);stage.objects.link(cam)
cam.location=(22,-28,18);cam.rotation_euler=(Vector((0,-.6,3.5))-cam.location).to_track_quat('-Z','Y').to_euler();cd.type='ORTHO';cd.ortho_scale=25;scene.camera=cam
if hasattr(scene.eevee,'taa_render_samples'):scene.eevee.taa_render_samples=128
scene.render.filepath=str(BASE/"preview-concept.png")
for screen in bpy.data.screens:
    for a in screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.region_3d.view_distance=29;a.spaces.active.region_3d.view_location=(0,0,3.5)
            a.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion();a.spaces.active.shading.type='MATERIAL'
bpy.context.view_layer.update()
# Write an isolated source scene, leaving existing live scenes intact.
bpy.data.libraries.write(str(BASE/"AstraShelter.blend"),{scene},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
summary={"scene":scene.name,"source_components":sum('surface_atlas_tile' in o for o in scene.objects),"groups":list(groups),"output":str(BASE)}
(BASE/"build-summary.json").write_text(json.dumps(summary,indent=2),encoding='utf8')
result=summary
