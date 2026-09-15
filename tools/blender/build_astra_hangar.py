"""Astra Hangar authored through Blender MCP. Standalone, non-destructive new scene."""
import bpy, math, os, json, numpy as np
from mathutils import Vector
from pathlib import Path
BASE=Path(r"C:\workspace\2-PJT\S15P21B202\artifacts\stage1-hangar")
BASE.mkdir(parents=True,exist_ok=True)
TEX=BASE/"AstraHangar.fbm"; TEX.mkdir(exist_ok=True)
if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
scene=bpy.data.scenes.new("Astra_Hangar_Production")
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
root=bpy.data.objects.new("AstraHangar_ROOT",None); scene.collection.objects.link(root)
root["dimensions_m"]="20 W x 18 D; front ramp 3.2 D; roof equipment up to 10.8 H"
root["front"]="-Y in Blender; exported with -Z forward / Y up"
root["cargo_clearance"]="16m wide below chamfers; approximately 6.56m from deck to retracted hoist"
groups={}
def group(n):
    if n not in groups:
        c=bpy.data.collections.new(n); scene.collection.children.link(c); groups[n]=c
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
    img=bpy.data.images.new("AstraHangar_"+name,width=N,height=N,alpha=True)
    img.colorspace_settings.name='Non-Color'
    img.pixels.foreach_set(pixels.ravel()); img.filepath_raw=str(TEX/(img.name+".png")); img.file_format='PNG'; img.save()
    if name in ("BaseColor","Emission"): img.colorspace_settings.name='sRGB'
    images[name]=img
del arrays
mat=bpy.data.materials.new("M_AstraHangar_PBR"); mat.use_nodes=True
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
# Deck and continuous cargo ramp. Floor level is +0.36m, bottom rests at 0.
box("Foundation_sealed_plinth",(0,0,.16),(20,18,.32),8,"02_Floor",.14)
for ix in range(8):
    for iy in range(9):
        box(f"Interior_floor_tile_{ix:02}_{iy:02}",(-8.65+ix*2.47,-7.95+iy*1.98,.335),(2.43,1.94,.07),9,"02_Floor",.016)
for s in (-1,1):
    box(f"Cargo_rail_channel_{s}",(s*5,0,.382),(.26,17.8,.048),3,"02_Floor",.008)
    box(f"Cargo_rail_metal_{s}",(s*5,0,.414),(.17,17.8,.028),2,"02_Floor",.008)
    box(f"Cargo_guide_cyan_{s}",(s*4.83,0,.405),(.045,17.8,.012),5,"10_Emission",.003)
for iy in range(9):
    for s in (-1,1):
        box(f"Perimeter_drain_{s}_{iy}",(s*8.45,-8+iy*2,.39),(.48,1.75,.05),3,"02_Floor",.015)
        for j in range(9):
            box(f"Drain_louver_{s}_{iy}_{j}",(s*8.45,-8.73+iy*2+j*.18,.421),(.43,.065,.025),2,"09_Grilles",.006)
# Sloped ramp mesh across frontage (same slope for all detail strips).
rampverts=[(-8,-12.2,.025),(8,-12.2,.025),(8,-9,.36),(-8,-9,.36),(-8,-12.2,0),(8,-12.2,0),(8,-9,0),(-8,-9,0)]
mesh("Cargo_access_ramp",rampverts,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],9,"02_Floor",.015)
angle=math.atan2(.335,3.2)
for x in (-7.9,7.9,-6.2,6.2):
    o=box("Ramp_edge_runner",(x,-10.6,.226),(.16,3.2,.055),0,"02_Floor",.025); o.rotation_euler[0]=angle
for s in (-1,1):
    o=box("Ramp_guide_light",(s*4.83,-10.6,.205),(.055,3.16,.025),5,"10_Emission",.004); o.rotation_euler[0]=angle
    for j in range(17):
        y=-12.08+j*.178; z=.025+(y+12.2)*.335/3.2+.034
        o=box("Ramp_grip_tread",(s*7.03,y,z),(1.48,.065,.026),8,"09_Grilles",.006); o.rotation_euler[0]=angle
# Three longitudinal wall bays, true upper window openings.
bay_centers=(-6,0,6)
for side in (-1,1):
    for i,y in enumerate(bay_centers):
        g=f"03_Wall_{'Left' if side<0 else 'Right'}"
        # Lower wall split around a real personnel door opening in the right front bay.
        if side==1 and i==0:
            for ya,yb in [(-8.55,-7.8),(-6.2,-3.45)]:
                box("Sidewall_door_flank",(side*9.35,(ya+yb)/2,2.71),(.5,yb-ya,4.70),14,g,.06)
            box("Personnel_door_lintel",(9.35,-7,4.06),(.5,1.6,2.0),14,g,.04)
        else:
            box(f"Wall_lower_{side}_{i}",(side*9.35,y,2.71),(.5,5.12,4.70),14,g,.06)
        box(f"Wall_upper_{side}_{i}",(side*9.35,y,6.86),(.5,5.12,.68),0,g,.05)
        box(f"Wall_sill_{side}_{i}",(side*9.35,y,5.37),(.5,5.12,.40),0,g,.025)
        # Glazed band is physically separated from walls.
        box(f"Window_recess_{side}_{i}",(side*9.40,y,6.02),(.19,4.88,1.10),3,g,.08)
        box(f"Window_glass_{side}_{i}",(side*9.52,y,6.02),(.035,4.53,.79),4,g,.055)
        for sign in (-1,1):
            box("Window_horizontal_frame",(side*9.55,y,6.02+sign*.46),(.07,4.8,.065),2,g,.015)
        for yy in (y-2.34,y+2.34):
            box("Window_vertical_frame",(side*9.55,yy,6.02),(.07,.07,.98),2,g,.015)
        for off in (-.78,.78):
            box("Window_mullion",(side*9.57,y+off,6.02),(.08,.055,.80),8,g,.012)
        for off in (-1.1,1.1):
            box("Window_cyan_strip",(side*9.60,y+off,6.32),(.015,1.60,.042),5,"10_Emission",.004)
        # White base skirt / lavender service panel and real vent louver stacks.
        box("Wall_lower_skirt",(side*9.62,y,.78),(.16,5.08,.70),0,g,.04)
        pc=y+.8 if side==1 and i==0 else y
        pw=3.3 if side==1 and i==0 else 4.65
        box("Service_panel_recess",(side*9.62,pc,3.01),(.10,pw,2.85),3,g,.065)
        box("Lavender_service_panel",(side*9.69,pc,3.01),(.075,pw-.13,2.71),1,g,.06)
        box("Vent_dark_recess",(side*9.72,pc,1.17),(.055,pw-.25,.52),3,"09_Grilles",.045)
        for j in range(5):
            o=box("Service_vent_louver",(side*9.76,pc,.99+j*.088),(.08,pw-.40,.038),2,"09_Grilles",.007)
            o.rotation_euler[1]=side*.25
        for yy in (pc-pw/2+.16,pc+pw/2-.16):
            for z in (1.80,4.25):
                cyl("Panel_captive_hex",(side*9.747,yy,z),.037,.025,2,axis='X')
        # Interior sacrificial bumper rail, wall liner and working light.
        box("Interior_wall_bumper",(side*9.04,y,1.02),(.16,5.1,.26),8,"06_Interior",.04)
        box("Interior_lavender_band",(side*9.04,y,2.15),(.075,5.08,.48),10,"06_Interior",.02)
        box("Interior_worklight_housing",(side*9.04,y,4.93),(.12,3.4,.17),3,"06_Interior",.035)
        box("Interior_worklight_lens",(side*8.966,y,4.93),(.02,3.18,.07),7,"10_Emission",.01)
# Personnel door is a separate closed panel, can be opened/animated independently.
for y in (-7.88,-6.12):
    box("Personnel_door_jamb",(9.69,y,1.92),(.27,.15,3.12),0,"05_PersonnelDoor",.035)
box("Personnel_door_header",(9.69,-7,3.48),(.27,1.92,.19),0,"05_PersonnelDoor",.04)
box("Personnel_door_seal",(9.38,-7,1.85),(.12,1.70,2.99),3,"05_PersonnelDoor",.06)
door=box("PersonnelDoor_MOVABLE",(9.48,-7,1.84),(.12,1.51,2.88),8,"05_PersonnelDoor",.08)
box("Personnel_door_lavender_inset",(9.56,-7,1.91),(.035,1.23,2.43),10,"05_PersonnelDoor",.04)
box("Personnel_door_cyan_access",(9.585,-7,2.11),(.03,.065,1.20),5,"10_Emission",.012)
box("Personnel_door_status",(9.80,-7,3.80),(.055,.74,.19),6,"10_Emission",.04)
box("Personnel_step",(10.07,-7,.16),(1.25,2.15,.32),2,"05_PersonnelDoor",.075)
box("Personnel_keypad",(9.85,-5.91,1.65),(.16,.23,.38),3,"05_PersonnelDoor",.035)
for z in (1.56,1.66,1.76): box("Keypad_status",(9.946,-5.91,z),(.016,.12,.025),5,"10_Emission",.002)
# Structural arch ribs: an actual open polygon arch at four longitudinal stations.
path=[(-9.52,.38),(-9.52,6.85),(-7.7,8.82),(-5.1,9.18),(0,9.34),(5.1,9.18),(7.7,8.82),(9.52,6.85),(9.52,.38)]
for iy,y in enumerate((-8.8,-3,3,8.8)):
    for j,(a,b) in enumerate(zip(path,path[1:])):
        beam(f"Arch_rib_{iy}_{j}",(a[0],y,a[1]),(b[0],y,b[1]),.78,.76,0,"01_Structure",.09)
    for side in (-1,1):
        box("Structural_foot",(side*9.52,y,.55),(1.10,1.10,1.05),0,"01_Structure",.13)
        box("Foot_dark_boot",(side*9.52,y,.15),(1.20,1.17,.29),8,"01_Structure",.06)
        if iy in (0,3):
            box("Pillar_lavender_inset",(side*9.52,y-.41 if iy==0 else y+.41,3.62),(.50,.055,4.50),1,"04_Facade",.04)
            box("Pillar_amber_lamp",(side*9.52,y-.453 if iy==0 else y+.453,.76),(.47,.035,.20),6,"10_Emission",.03)
# Front portal inner seal, jambs and rolled shutter cassette leave an unobstructed opening.
front=[(-8.52,.39),(-8.52,6.76),(-6.65,8.08),(6.65,8.08),(8.52,6.76),(8.52,.39)]
for j,(a,b) in enumerate(zip(front,front[1:])):
    beam("Portal_dark_recess_"+str(j),(a[0],-9.03,a[1]),(b[0],-9.03,b[1]),.33,.22,3,"04_Facade",.055)
    beam("Portal_titanium_lip_"+str(j),(a[0],-9.18,a[1]),(b[0],-9.18,b[1]),.12,.10,2,"04_Facade",.026)
for side in (-1,1):
    box("Portal_lamp_socket",(side*8.57,-9.30,4.30),(.20,.12,2.20),3,"04_Facade",.055)
    box("Portal_cyan_lens",(side*8.57,-9.37,4.30),(.073,.032,2.06),5,"10_Emission",.022)
    box("Portal_threshold_impact_rail",(side*8.24,-8.69,1.4),(.13,.23,1.9),8,"04_Facade",.03)
box("Front_white_header",(0,-8.92,8.79),(13.70,1.05,.97),0,"04_Facade",.16)
box("Front_lavender_header_inset",(0,-9.46,8.81),(8.1,.025,.38),1,"04_Facade",.04)
box("Header_cyan_status",(0,-9.48,8.33),(12.50,.035,.07),5,"10_Emission",.015)
box("Retracted_shutter_cassette",(0,-8.32,8.16),(13.40,.70,.38),8,"04_Facade",.06)
for i in range(4): box("Retracted_shutter_slat",(0,-8.77,8.03+i*.058),(13.15,.08,.028),2,"04_Facade",.005)
# Rear facade closes the whole cross section, with actual panels and ventilation.
for i,x in enumerate((-6.3,0,6.3)):
    box("Rear_wall_liner",(x,8.94,3.87),(6.18,.38,7.02),14,"07_RearWall",.05)
    box("Rear_exterior_lavender",(x,9.17,3.15),(5.65,.13,3.24),1,"07_RearWall",.09)
    box("Rear_upper_window_recess",(x,9.19,6.10),(5.70,.12,.95),3,"07_RearWall",.07)
    box("Rear_upper_window",(x,9.26,6.10),(5.42,.035,.69),4,"07_RearWall",.04)
    for off in (-1.3,1.3): box("Rear_window_status",(x+off,9.29,6.37),(1.8,.02,.043),5,"10_Emission",.007)
    box("Rear_vent_recess",(x,9.27,1.03),(5.35,.09,.63),3,"09_Grilles",.035)
    for j in range(6): box("Rear_vent_louver",(x,9.33,.79+j*.092),(5.1,.08,.04),2,"09_Grilles",.008)
    box("Rear_inside_bumper",(x,8.69,1.12),(5.84,.15,.32),8,"06_Interior",.035)
    box("Rear_inside_color_band",(x,8.71,2.23),(5.84,.07,.45),10,"06_Interior",.015)
    box("Rear_inside_task_light",(x,8.68,5.13),(3.1,.07,.10),7,"10_Emission",.022)
for x in (-3.15,3.15):
    box("Rear_vertical_rib",(x,9.25,4.02),(.37,.38,7.28),0,"07_RearWall",.065)
prism("Rear_upper_gable",[(-9.27,7.12),(-7.62,8.87),(-5,9.18),(0,9.30),(5,9.18),(7.62,8.87),(9.27,7.12)],8.73,9.02,1,"07_RearWall",.04)
# Solid modular roof panels including interior undersurface, separated from central skylight.
roof=[(-9.28,7.1),(-7.6,8.78),(-5.05,9.13),(-1.60,9.29),(1.60,9.29),(5.05,9.13),(7.6,8.78),(9.28,7.1)]
for bay,(y0,y1) in enumerate(((-8.55,-3.39),(-2.60,2.60),(3.39,8.50))):
    for j in range(len(roof)-1):
        if j==3: continue
        a,b=roof[j],roof[j+1]
        # Panels subdivided for actual narrow reveal seams.
        for sub in range(3):
            ya=y0+(y1-y0)*sub/3+.025; yb=y0+(y1-y0)*(sub+1)/3-.025
            prism(f"Roof_panel_{bay}_{j}_{sub}",[(a[0],a[1]),(b[0],b[1]),(b[0],b[1]-.18),(a[0],a[1]-.18)],ya,yb,1,"11_RoofPanels",.032)
for x in (-1.68,1.68):
    box("Skylight_white_long_frame",(x,0,9.35),(.24,17.65,.20),0,"12_Skylight",.045)
    box("Skylight_cyan_edge",(x*.91,0,9.43),(.035,16.95,.026),5,"10_Emission",.006)
for i in range(9):
    y=-7.80+i*1.95
    box("Skylight_glass_pane",(0,y,9.27),(3.02,1.82,.085),4,"12_Skylight",.035)
    box("Skylight_cross_mullion",(0,y+.95,9.36),(3.35,.11,.14),0,"12_Skylight",.023)
    # Interior guard bars below the glass
    beam("Skylight_internal_crossbrace",(-1.4,y-.7,9.07),(1.4,y+.7,9.07),.048,.05,8,"12_Skylight",.012)
# Two separate rear ventilation pods with visible grilles and top maintenance lids.
for side in (-1,1):
    x=side*3.95
    box("Roof_vent_base",(x,6.47,9.26),(2.85,2.55,.20),8,"13_RoofEquipment",.08)
    box("Roof_vent_white_shell",(x,6.47,9.96),(2.56,2.25,1.25),0,"13_RoofEquipment",.22)
    box("Roof_vent_top_lid",(x,6.47,10.60),(1.97,1.66,.065),2,"13_RoofEquipment",.09)
    for sy in (-1,1):
        y=6.47+sy*1.136
        box("Vent_pod_recess",(x,y,9.96),(2.08,.035,.81),3,"13_RoofEquipment",.08)
        for j in range(7):
            box("Vent_pod_louver",(x,y+sy*.04,9.64+j*.106),(1.89,.065,.055),2,"09_Grilles",.013)
    box("Vent_side_amber",(x+side*1.29,6.35,9.85),(.025,.55,.16),6,"10_Emission",.026)
    for dx in (-.82,.82):
        for dy in (-.62,.62):
            cyl("Roof_lid_bolt",(x+dx,6.47+dy,10.65),.045,.03,2,axis='Z')
# Functional-looking crane: longitudinal rails, paired girders, bogies, trolley, retracted hoist.
for side in (-1,1):
    x=side*7.75
    box("Crane_longitudinal_rail",(x,0,7.12),(.26,16.80,.27),2,"14_Crane",.045)
    for y in (-7.5,-3,3,7.5):
        box("Crane_rail_bracket",(x,y,6.94),(.61,.47,.21),8,"14_Crane",.035)
for y in (-2.15,-1.40):
    box("Bridge_girder_web",(0,y,7.38),(15.27,.18,.56),1,"14_Crane",.035)
    for z in (7.08,7.69):
        box("Bridge_girder_flange",(0,y,z),(15.49,.45,.11),2,"14_Crane",.025)
for x in (-7.63,7.63):
    box("Crane_end_bogie",(x,-1.78,7.40),(.65,1.68,.73),6,"14_Crane",.08)
    for y in (-2.30,-1.22):
        cyl("Crane_bogie_wheel",(x,y,7.13),.21,.16,8,"14_Crane",axis='X',verts=20)
    box("Crane_drive_motor",(x,-1.78,7.87),(.47,.65,.22),8,"14_Crane",.05)
box("Crane_trolley_MOVABLE",(1.50,-1.78,7.65),(1.62,1.42,.32),8,"14_Crane",.08)
box("Hoist_casing",(1.50,-1.78,7.30),(.88,.83,.55),6,"14_Crane",.10)
cyl("Hoist_cable_drum",(1.50,-1.78,7.35),.25,.98,2,"14_Crane",axis='X',verts=24)
box("Hoist_retracted_pickup",(1.50,-1.78,6.99),(.42,.43,.12),2,"14_Crane",.035)
for x in range(-7,8,2):
    box("Girder_stiffener",(x,-2.27,7.4),(.075,.06,.52),2,"14_Crane",.014)
# Visible interior technical detail kept near walls; cargo floor remains clear.
for side in (-1,1):
    for z in (3.1,3.32):
        beam("Interior_service_conduit",(side*8.98,-7.9,z),(side*8.98,8.4,z),.07,.07,2,"06_Interior",.015)
    for y in (-7,-3,1,5,8):
        box("Conduit_clamp",(side*8.94,y,3.21),(.09,.12,.39),8,"06_Interior",.014)
    for i,y in enumerate((-6,0,6)):
        box("Interior_vertical_panel_reveal",(side*9.02,y,3.8),(.065,.028,1.6),3,"06_Interior",.004)
# Fasteners across facade and ribs, deliberately kept readable at game scale.
for side in (-1,1):
    for y in (-9.245,9.35):
        for z in (1.70,3.0,4.9,6.50):
            cyl("Arch_panel_hex_fastener",(side*9.52,y,z),.045,.028,2,axis='Y')
# Stage and cameras are explicitly excluded from export.
stage=group("99_Presentation_ONLY")
def stage_obj(o):
    for c in list(o.users_collection): c.objects.unlink(o)
    stage.objects.link(o); return o
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.055))
ground=stage_obj(bpy.context.object); ground.name="Studio_ground_NOT_EXPORTED"
gm=bpy.data.materials.new("Studio_ground"); gm.diffuse_color=(.115,.145,.205,1); gm.use_nodes=True
ground_bsdf=next(n for n in gm.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
ground_bsdf.inputs["Base Color"].default_value=(.115,.145,.205,1)
ground_bsdf.inputs["Roughness"].default_value=.8; ground.data.materials.append(gm)
def area(n,loc,power,color,size,target=(0,0,4)):
    d=bpy.data.lights.new(n,'AREA'); d.energy=power; d.shape='DISK'; d.size=size; d.color=color
    o=bpy.data.objects.new(n,d); stage.objects.link(o); o.location=loc
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area("Key_softbox",(1,-12,24),4200,(.84,.9,1),15)
area("Fill_softbox",(-17,-5,12),3000,(.75,.80,1),12)
area("Right_rim",(16,8,17),5200,(.82,.80,1),12)
area("Rear_fill",(-2,17,13),2600,(.70,.80,1),12)
area("Interior_ceiling",(0,2,7.9),800,(.81,.89,1),8,target=(0,0,.4))
camdata=bpy.data.cameras.new("Camera_Concept"); cam=bpy.data.objects.new("Camera_Concept",camdata); stage.objects.link(cam)
cam.location=(29,-36,23); cam.rotation_euler=(Vector((0,-.8,4.5))-cam.location).to_track_quat('-Z','Y').to_euler()
camdata.type='ORTHO'; camdata.ortho_scale=34; camdata.lens=45; scene.camera=cam
scene.render.filepath=str(BASE/"preview-concept.png")
# Set a useful material-preview viewport.
for screen in bpy.data.screens:
    for a in screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.region_3d.view_distance=37
            a.spaces.active.region_3d.view_location=(0,0,4)
            a.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            a.spaces.active.shading.type='MATERIAL'
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(BASE/"AstraHangar.blend"))
summary={"scene":scene.name,"mesh_objects":sum(o.type=='MESH' for g,c in groups.items() if not g.startswith('99') for o in c.objects),"collections":list(groups),"base":str(BASE)}
(BASE/"build-summary.json").write_text(json.dumps(summary,indent=2),encoding="utf8")
result=summary
