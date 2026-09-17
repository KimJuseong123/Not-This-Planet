"""Stage2 reference-led asset workshop. Run through Blender MCP in an isolated new scene.
Meters, Blender -Y front. Never clears the user's scene or overwrites existing models.
"""
import bpy, math, random, json, os
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'METAGAME/Assets/_Project/Models/Stage2'
REF = ROOT/'docs/assets/concepts/stage2/model-references'
PREVIEW = ROOT/'artifacts/stage2-models'
MAT = {}
C = None
P = None

def setup_materials():
    colors={'Ivory':(.70,.72,.62), 'Teal':(.075,.22,.24), 'Orange':(.84,.39,.055),
            'Steel':(.15,.18,.18), 'Dark':(.025,.036,.04), 'Concrete':(.37,.39,.34),
            'Rust':(.34,.12,.045), 'Glass':(.025,.22,.36), 'Olive':(.20,.27,.065),
            'LeafLight':(.34,.38,.12), 'Dry':(.48,.31,.085), 'Bark':(.19,.12,.055),
            'Flower':(.86,.84,.66), 'Lamp':(1,.68,.19), 'Moss':(.11,.17,.035)}
    for key,color in colors.items():
        name='S2_'+key
        mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color=(*color,1); mat.use_nodes=True
        bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if bs is None:
            bs=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
            out=next((n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL'),None) or mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
            mat.node_tree.links.new(bs.outputs[0],out.inputs[0])
        bs.inputs['Base Color'].default_value=(*color,1)
        bs.inputs['Roughness'].default_value=.64 if key!='Glass' else .2
        bs.inputs['Metallic'].default_value=.45 if key in ['Steel','Teal','Orange','Rust'] else .02
        if key=='Lamp':
            bs.inputs['Emission Color'].default_value=(*color,1)
            bs.inputs['Emission Strength'].default_value=2
        MAT[key]=mat

def link(o,mat=None,parent=None):
    for old in list(o.users_collection): old.objects.unlink(o)
    C.objects.link(o)
    if mat: o.data.materials.append(MAT[mat])
    par=P if parent is None else parent
    if par:
        bpy.context.view_layer.update()
        o.parent=par; o.matrix_parent_inverse=par.matrix_world.inverted()
    return o

def empty(name,loc=(0,0,0),parent=None):
    o=bpy.data.objects.new(name,None); C.objects.link(o); o.location=loc
    if parent: o.parent=parent
    o.empty_display_size=.2; return o

def mesh(name,verts,faces,mat='Steel',bevel=0,parent=None):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); link(o,mat,parent)
    uv=data.uv_layers.new(name='UVMap')
    for poly in data.polygons:
        n=poly.normal; axis=max(range(3),key=lambda i:abs(n[i])); axes=[i for i in range(3) if i!=axis]
        for li in poly.loop_indices:
            v=data.vertices[data.loops[li].vertex_index].co
            uv.data[li].uv=(v[axes[0]],v[axes[1]])
    if bevel:
        mod=o.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=bevel; mod.segments=2
    return o

def box(name,loc,size,mat='Steel',bevel=.025,parent=None):
    x,y,z=loc; a,b,c=[s/2 for s in size]
    vs=[(x+dx*a,y+dy*b,z+dz*c) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return mesh(name,vs,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat,min(bevel,min(size)/5),parent)

def lathe(name,profile,mat='Steel',n=32,loc=(0,0,0),parent=None,cap=True):
    x,y,z=loc; vs=[]
    for r,h in profile:
        vs.extend((x+r*math.cos(i*2*math.pi/n),y+r*math.sin(i*2*math.pi/n),z+h) for i in range(n))
    fs=[]
    for row in range(len(profile)-1):
        for i in range(n): j=(i+1)%n; fs.append((row*n+i,row*n+j,(row+1)*n+j,(row+1)*n+i))
    if cap: fs.extend([tuple(reversed(range(n))),tuple((len(profile)-1)*n+i for i in range(n))])
    o=mesh(name,vs,fs,mat,parent=parent)
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return o

def tube(name,a,b,r,mat='Steel',n=10,parent=None,r2=None):
    a,b=Vector(a),Vector(b); w=(b-a).normalized(); u=w.cross(Vector((0,0,1)))
    if u.length<.01: u=w.cross(Vector((0,1,0)))
    u.normalize();v=w.cross(u);vs=[]
    for center,rad in [(a,r),(b,r if r2 is None else r2)]:
        vs += [tuple(center+rad*(math.cos(i*2*math.pi/n)*u+math.sin(i*2*math.pi/n)*v)) for i in range(n)]
    fs=[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]+[tuple(reversed(range(n))),tuple(range(n,2*n))]
    return mesh(name,vs,fs,mat,parent=parent)

def arc(name,center,r,thick,mat='Steel',start=0,end=math.tau,axis='Z',parent=None,n=32):
    vs=[];cx,cy,cz=center; closed=abs(end-start-math.tau)<.001
    for i in range(n+1):
        a=start+(end-start)*i/n
        for k in range(8):
            b=k*math.tau/8; rr=r+thick*math.cos(b); h=thick*math.sin(b)
            q=(rr*math.cos(a),rr*math.sin(a),h)
            if axis=='Y':q=(q[0],q[2],q[1])
            if axis=='X':q=(q[2],q[0],q[1])
            vs.append((cx+q[0],cy+q[1],cz+q[2]))
    fs=[(i*8+k,i*8+(k+1)%8,(i+1)*8+(k+1)%8,(i+1)*8+k) for i in range(n) for k in range(8)]
    if not closed:fs.extend([tuple(reversed(range(8))),tuple(n*8+k for k in range(8))])
    o=mesh(name,vs,fs,mat,parent=parent)
    for p in o.data.polygons:p.use_smooth=True
    return o

def bolt(loc,r=.07,axis='Z',parent=None):
    a=Vector(loc);d=Vector({'Z':(0,0,1),'Y':(0,-1,0),'X':(1,0,0)}[axis])
    tube('Hex bolt',a,a+d*r*.6,r,'Steel',6,parent)

def rail(a,b,height=1.1,parent=None):
    for point in (a,b):tube('Rail post',point,Vector(point)+Vector((0,0,height)),.045,'Orange',8,parent)
    for z in (height*.5,height):tube('Safety rail',Vector(a)+Vector((0,0,z)),Vector(b)+Vector((0,0,z)),.04,'Orange',8,parent)

def wheel(loc,r=.16,parent=None):
    x,y,z=loc; tube('Rubber wheel',(x-.055,y,z),(x+.055,y,z),r,'Dark',16,parent)
    tube('Wheel hub',(x-.061,y,z),(x+.061,y,z),r*.46,'Steel',12,parent)

def platepoly(name,pts,thick,mat='Steel'):
    n=len(pts); vs=[(x,-thick/2,z) for x,z in pts]+[(x,thick/2,z) for x,z in pts]
    fs=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,vs,fs,mat,.006)

def leaf(name,base,tip,width,mat='Olive'):
    a,b=Vector(base),Vector(tip);d=b-a;side=d.cross(Vector((0,0,1)))
    if side.length<.01:side=Vector((1,0,0))
    side.normalize(); mid=a+d*.52; ridge=mid+Vector((0,0,width*.22))
    vs=[tuple(a),tuple(mid-side*width/2),tuple(b),tuple(mid+side*width/2),tuple(ridge)]
    fs=[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(4,1,0),(4,2,1),(4,3,2),(4,0,3)]
    return mesh(name,vs,fs,mat)

def rock(name,loc,size,mat='Concrete',seed=1):
    rng=random.Random(seed); bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=loc)
    o=bpy.context.object;o.name=name
    for v in o.data.vertices:
        v.co=Vector((v.co.x*size[0],v.co.y*size[1],v.co.z*size[2]))*rng.uniform(.8,1.13)
    link(o,mat);return o

def ring_joint(z,r=1.4):
    lathe('Machined joint flange',[(r-.08,z),(r+.05,z+.025),(r+.05,z+.14),(r-.08,z+.17)],'Steel')
    for i in range(12):
        a=math.tau*i/12;bolt((r*.94*math.sin(a),r*.94*math.cos(a),z+.17),.045)

def front_bolts(z=.35,r=1.4):
    for i,a in enumerate((-.75,0,.75)):
        x,y=r*math.sin(a),-r*math.cos(a)
        pivot=empty('Bolt_'+str(i+1),(x,y,z),P)
        tube('Orange bolt boss',(x*.97,y*.97,z),(x*1.055,y*1.055,z),.22,'Orange',12,pivot)
        tube('Hex fastener',(x*1.05,y*1.05,z),(x*1.14,y*1.14,z),.145,'Steel',6,pivot)

def lifting_eye(z):
    box('Lifting eye plate',(0,0,z),(.55,.30,.1),'Steel')
    arc('Orange lifting ring',(0,0,z+.24),.20,.075,'Orange',axis='Y')

def create_scene(asset):
    global C,P
    name='S2_'+asset['id']
    if bpy.data.scenes.get(name):raise RuntimeError('Scene already exists: '+name)
    scene=bpy.data.scenes.new(name);bpy.context.window.scene=scene
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    C=bpy.data.collections.new(name);scene.collection.children.link(C)
    P=empty(name+'_Root');P['front']='-Y';P['units']='meters';P['reference_asset']=asset['id']
    return scene

def build(asset_id):
    assets=json.loads((REF/'asset-manifest.json').read_text(encoding='utf-8'))['assets']
    asset=next(a for a in assets if a['id']==asset_id)
    for view in ('front','left','right','back'):
        if not (REF/asset_id/(view+'.png')).is_file():raise RuntimeError('Reference missing: '+asset_id+'/'+view)
    setup_materials();scene=create_scene(asset)
    BUILDERS[asset_id]()
    bpy.context.view_layer.update()
    return {'asset':asset_id,'objects':len(C.objects),'scene':scene.name}

BUILDERS={}

def crane():
    lathe('Concrete footing',[(2,0),(2,.3),(1.5,1.1)],'Concrete',8)
    lathe('Mast core',[(.68,1),(.68,19.6)],'Teal',16)
    for z in range(2,20,3):
        lathe('Mast collar',[(.73,z),(.73,z+.18)],'Steel',16)
        for x in (-.68,.68):tube('Mast reinforcement',(x,-.45,z),(x,.45,min(z+2.8,19.6)),.075,'Teal')
    for x in (-1.2,1.2):
        for y in (-1.2,1.2):bolt((x,y,.35),.13)
    box('Fixed balcony deck',(0,-1,8),(4,3,.22),'Teal')
    rail((-2,-2.5,8.1),(2,-2.5,8.1));rail((2,-2.5,8.1),(2,.5,8.1))
    rail((-2,-2.5,8.1),(-2,-.6,8.1))
    for x in (-1.4,1.4):tube('Balcony brace',(0,0,6),(x,-2,8),.10,'Steel')
    rot=empty('SlewPivot',(0,0,19),P)
    lathe('Slew bearing',[(.9,18.7),(.9,19.3)],'Steel',32,parent=rot)
    for y in (-.55,.55):
        for z in (19.1,20.2):box('Jib chord',(10,y,z),(30,.16,.16),'Teal',parent=rot)
        for i in range(15):
            a=-5+i*2; tube('Jib diagonal',(a,y,19.1),(a+2,y,20.2),.075,'Teal',parent=rot)
            rail((a,y,20.3),(a+2,y,20.3),.75,rot)
    for x in range(-5,26,2):box('Cross bearer',(x,0,19.1),(.13,1.3,.13),'Steel',parent=rot)
    box('Counterweight',(-4.2,0,18.8),(1.5,1.6,2),'Concrete',parent=rot)
    box('Hoist motor',(-2.7,0,20.6),(1.2,.85,.6),'Teal',parent=rot)
    tube('Jib support',(0,0,22),(16,0,20.2),.055,'Steel',parent=rot)
    tube('Counter support',(0,0,22),(-4,0,20.2),.055,'Steel',parent=rot)
    tube('Mast head',(0,0,19.3),(0,0,22),.12,'Teal',parent=rot)
    trolley=empty('Trolley',(15,0,19),rot)
    # Empty local transform is relative to rotating pivot.
    trolley.location=(15,0,0)
    box('Trolley carriage',(15,0,18.95),(1.4,1.5,.65),'Orange',parent=trolley)
    for y in (-.6,.6):tube('Hoist cable',(15,y,18.7),(15,y,11.5),.022,'Dark',8,trolley)
    hook=empty('HookHeight',(0,0,-7.6),trolley)
    box('Hook block',(15,0,11.5),(.8,.4,.7),'Orange',parent=hook)
    arc('Safety hook',(15,0,10.9),.32,.085,'Steel',start=.2,end=5.5,axis='Y',parent=hook)
    tube('Hook latch',(15.29,0,10.96),(15.07,0,11.2),.025,'Orange',parent=hook)

def elevator():
    for x in (-1.9,1.9):
        box('Rail footing',(x,0,.15),(.65,1,.3),'Concrete')
        box('Fixed guide rail',(x,0,5.5),(.25,.3,11),'Teal')
        box('Rack track',(x,-.19,5.5),(.08,.06,10.8),'Steel')
        for z in range(1,11):box('Rail fastener',(x,-.25,z),(.4,.06,.12),'Orange')
    cabin=empty('LiftCabin',(0,0,.2),P)
    box('Cabin floor',(0,0,.2),(3.5,3.5,.25),'Teal',parent=cabin)
    for x in (-1.65,1.65):
        rail((x,-1.65,.35),(x,1.65,.35),1.4,cabin)
        for y in (-1.65,1.65):tube('Cage upright',(x,y,.35),(x,y,2.7),.065,'Teal',parent=cabin)
    rail((-1.65,1.65,.35),(1.65,1.65,.35),1.4,cabin)
    box('Cage top',(0,0,2.7),(3.5,3.5,.1),'Teal',parent=cabin)
    gate=empty('CabinGate',(-1.65,-1.65,.35),cabin)
    for z in (.6,1.2):tube('Gate crossbar',(-1.65,-1.65,z),(1.65,-1.65,z),.055,'Orange',parent=gate)
    for x in [-1.65,-.8,0,.8,1.65]:tube('Gate upright',(x,-1.65,.35),(x,-1.65,1.5),.04,'Orange',parent=gate)
    box('Top landing',(0,1.6,8),(4,1.2,.25),'Concrete')
    rail((-1.9,2.2,8.15),(1.9,2.2,8.15))
    for x in (-1.9,1.9):box('Call panel',(x,-.4,1.1),(.25,.16,.4),'Orange')

def console():
    box('Base',(0,0,.08),(1,.65,.16),'Steel')
    box('Pedestal',(0,.06,.5),(.45,.4,.85),'Teal')
    box('Desk',(0,0,1),(1.5,.8,.18),'Teal')
    box('Rear sight shield',(0,.35,1.27),(1.5,.06,.55),'Steel')
    box('Shield edge',(0,.35,1.55),(1.5,.065,.045),'Orange')
    for i,x in enumerate((-.43,0,.43)):
        box('Lever boot',(x,-.08,1.14),(.25,.36,.08),'Dark')
        arm=empty('Lever_'+str(i),(x,-.08,1.18),P)
        tube('Lever stem',(x,-.08,1.18),(x,-.2,1.43),.033,'Steel',parent=arm)
        tube('Lever grip',(x,-.2,1.40),(x,-.23,1.57),.065,'Orange',parent=arm)
    for x in (-.6,.6):tube('Grab rail',(x,-.38,1.08),(x,.18,1.08),.035,'Steel')
    for x in (-.35,.35):bolt((x,-.25,.17),.065)

def launchpad():
    lathe('Concrete pad',[(3,0),(3,.25),(2.85,.45)],'Concrete',48)
    lathe('Central mount',[(1.4,.44),(1.4,.53)],'Steel',48)
    arc('Safety rim',(0,0,.46),2.83,.055,'Orange',n=64)
    for i in range(12):
        a=i*math.tau/12; u=Vector((math.cos(a),math.sin(a),0))
        tube('Radial bearer',u*1.5+Vector((0,0,.5)),u*2.8+Vector((0,0,.5)),.075,'Teal')
        for k in range(5):
            r=1.6+k*.22;arc('Grille sector',(0,0,.47),r,.012,'Steel',start=a+.04,end=a+math.tau/12-.04,n=5)
        bolt(tuple(u*2.65+Vector((0,0,.51))),.06)

def rocket(kind):
    if kind=='RocketBase':
        lathe('Base shell',[(1.25,.75),(1.4,.9),(1.4,1.9),(1.25,2.1)],'Ivory')
        lathe('Identity belt',[(1.405,1.5),(1.405,1.7)],'Orange')
        ring_joint(1.96,1.25)
        for a in (0,math.tau/3,math.tau*2/3):
            x,y=.78*math.cos(a),.78*math.sin(a)
            lathe('Engine bell',[(.48,.13),(.48,.19),(.23,.7),(.21,.82)],'Dark',24,(x,y,0),cap=False)
            arc('Engine rim',(x,y,.15),.46,.04,'Steel')
        for a in (math.pi/4,3*math.pi/4,5*math.pi/4,7*math.pi/4):
            x,y=math.cos(a),math.sin(a)
            tube('Landing strut',(x*1.16,y*1.16,1.2),(x*1.6,y*1.6,.15),.13,'Teal')
            box('Landing foot',(x*1.6,y*1.6,.08),(.5,.5,.16),'Steel')
        box('Service panel',(0,-1.405,1.18),(.72,.05,.6),'Ivory')
        return
    capsule=kind=='RocketCapsule';band='Teal' if kind=='RocketService' else 'Orange'
    profile=[(1.35,.12),(1.4,.28),(1.4,2.7),(1.25,2.88)] if not capsule else [(1.35,.12),(1.4,.3),(1.32,.75),(.57,2.5),(.34,2.85)]
    lathe('Pressure shell',profile,'Ivory',48)
    ring_joint(.01);ring_joint(2.76,1.23 if not capsule else .34)
    if capsule:
        lathe('Capsule belt',[(1.37,.38),(1.29,.68)],band,48)
        tube('Porthole frame',(0,-.99,1.63),(0,-1.10,1.63),.42,'Steel',32)
        tube('Porthole glass',(0,-1.10,1.63),(0,-1.12,1.63),.33,'Glass',32)
    else:
        lathe('Identity band',[(1.406,2.18),(1.406,2.48)],band,48)
        box('Access panel',(0,-1.405,1.35),(.85,.075,1.15),'Ivory')
        for x in (-.36,.36):
            for z in (.85,1.85):bolt((x,-1.45,z),.045,'Y')
        if kind=='RocketService':
            for z in (1.1,1.22,1.34,1.46):box('Service vent',(.78,-1.15,z),(.3,.04,.045),'Dark')
    front_bolts(.35);lifting_eye(2.9)
    for i in range(8):
        a=i*math.tau/8;x,y=1.397*math.cos(a),1.397*math.sin(a)
        if not capsule:tube('Panel seam',(x,y,.6),(x,y,2.12),.011,'Steel',6)

BUILDERS.update(Crane=crane,Elevator=elevator,Console=console,Launchpad=launchpad)
for k in ['RocketBase','RocketTank','RocketService','RocketCapsule']:BUILDERS[k]=lambda k=k:rocket(k)

def container():
    box('Heavy base',(0,0,.16),(5,5,.32),'Steel')
    for x in (-2.35,2.35):
        box('Side frame',(x,0,2),(.3,5,3.7),'Teal')
        box('Ivory side inset',(x*1.068,0,2),(.04,4.35,2.6),'Ivory')
        for y in (-2.15,0,2.15):box('Orange stiffener',(x*1.075,y,2),(.05,.13,3.4),'Orange')
    box('Rear wall',(0,2.35,2),(4.5,.3,3.7),'Teal')
    for x in (-2.15,2.15):box('Front jamb',(x,-2.35,2),(.4,.3,3.7),'Teal')
    lid=empty('RoofHinge',(0,2.5,4),P)
    box('Roof panel',(0,0,4),(5,5,.22),'Ivory',parent=lid)
    for x in (-2.35,2.35):box('Roof rim',(x,0,4.12),(.18,5,.15),'Teal',parent=lid)
    for y in (-2.35,0,2.35):box('Roof cross rib',(0,y,4.12),(5,.15,.15),'Teal',parent=lid)
    for x in (-1.6,1.6):
        tube('Roof hinge barrel',(x-.3,2.5,4),(x+.3,2.5,4),.13,'Steel',12,lid)
        box('Front latch',(x,-2.54,3.6),(.25,.15,.5),'Orange')
    lathe('Upright module cradle',[(1.55,.33),(1.55,.58),(1.37,.58),(1.37,.33)],'Dark',32,cap=False)
    for x in (-1.8,1.8):
        for y in (-1.8,1.8):box('Cargo anchor',(x,y,.38),(.35,.35,.15),'Orange')

def ladder():
    platform=empty('UpperPlatform',(0,0,4),P)
    box('Standing deck',(0,0,4),(1.7,1.6,.16),'Teal',parent=platform)
    rail((-.82,-.8,4.1),(-.82,.8,4.1),1,platform)
    rail((.82,-.8,4.1),(.82,.8,4.1),1,platform)
    rail((-.82,.8,4.1),(.82,.8,4.1),1,platform)
    for side in (-1,1):
        frame=empty('FrontFrame' if side<0 else 'RearFrame',(0,side*.65,4),P)
        for x in (-.7,.7):
            tube('Telescoping outer rail',(x,side*1.35,.2),(x,side*.65,4),.085,'Teal',8,frame)
            tube('Inner sliding rail',(x,side*.96,2.1),(x,side*.64,4.12),.055,'Steel',8,frame)
            box('Stabilizer foot',(x,side*1.35,.09),(.55,.55,.18),'Dark',parent=frame)
            wheel((x,side*1.45,.19),.15,frame)
            bolt((x,side*.65,3.9),.1,'Y',frame)
        for i in range(1,13):
            z=i*.3;y=side*(1.35-.7*z/4)
            box('Wide rung',(0,y,z),(1.4,.22,.09),'Orange' if i%3==0 else 'Steel',parent=frame)
    for x in (-.7,.7):tube('Folding spreader lock',(x,-1,2),(x,1,2),.045,'Orange')

def wrench():
    platepoly('Wrench forged body',[(-.045,0),(.045,0),(.035,.34),(.085,.38),(.10,.49),(.055,.55),(.018,.46),(-.028,.44),(-.07,.53),(-.11,.49),(-.10,.39),(-.035,.33)],.045,'Steel')
    box('Rubber grip',(0,-.026,.17),(.065,.015,.25),'Teal',.009)
    jaw=empty('AdjustableJaw',(.04,0,.44),P)
    box('Sliding jaw',(.065,0,.48),(.065,.055,.05),'Steel',.008,jaw)
    tube('Adjustment screw',(-.05,-.033,.39),(.03,-.033,.39),.019,'Dark',12)
    for x in [-.04,-.025,-.01,.005,.02]:arc('Screw ridge',(x,-.033,.39),.02,.004,'Steel',axis='X',n=12)
    arc('Handle ring',(0,0,.035),.022,.009,'Orange',axis='Y',n=16)

def toolrack():
    box('Rack foot',(0,0,.1),(1.2,.55,.2),'Steel')
    box('Rack back',(0,.12,.85),(1.2,.12,1.1),'Teal')
    box('Shelf',(0,-.12,.35),(1.2,.55,.1),'Teal')
    for x in (-.4,0,.4):
        tube('Tool peg',(x,.04,1.1),(x,-.18,1.1),.04,'Orange')
        box('Tool stop',(x,-.19,.45),(.17,.1,.15),'Orange')
    for x in (-.5,.5):box('Rack leg',(x,.1,.2),(.15,.3,.4),'Steel')

def sprayer():
    tube('Nozzle barrel',(0,-.26,.25),(0,.2,.25),.085,'Teal',24)
    tube('Wide muzzle',(0,-.28,.25),(0,-.18,.25),.13,'Dark',24)
    arc('Muzzle ring',(0,-.285,.25),.10,.019,'Steel',axis='Y',n=24)
    for i in range(8):
        a=i*math.tau/8; tube('Spray aperture',(.06*math.cos(a),-.29,.25+.06*math.sin(a)),(.06*math.cos(a),-.3,.25+.06*math.sin(a)),.012,'Dark',8)
    tube('Grip',(0,.03,.24),(0,.12,.03),.045,'Teal',10)
    tube('Trigger guard',(0,-.08,.2),(0,-.025,.04),.016,'Orange')
    tube('Guard bottom',(0,-.025,.04),(0,.12,.03),.016,'Orange')
    tube('Hose coupling',(0,.20,.25),(0,.28,.25),.06,'Steel',12)

def shower():
    box('Drain tray',(0,0,.09),(2,2,.18),'Teal')
    for x in range(-9,10):box('Drain grate',(x*.1,0,.19),(.032,1.8,.025),'Steel',0)
    for x in (-.9,.9):
        box('Wash upright',(x,.35,1.25),(.16,.18,2.5),'Ivory')
        box('Foot protector',(x,.35,.2),(.32,.35,.4),'Orange')
    box('Overhead crossbeam',(0,.35,2.48),(2,.22,.16),'Teal')
    tube('Supply pipe',(.9,.4,.2),(.9,.4,2.4),.038,'Steel')
    tube('Shower arm',(.9,.4,2.4),(0,-.12,2.4),.038,'Steel')
    lathe('Shower head',[(.17,2.29),(.21,2.31),(.14,2.38)],'Steel',24,(0,-.12,0))
    box('Wash button housing',(.85,-.02,1.15),(.27,.18,.32),'Teal')
    tube('Large wash button',(.85,-.12,1.15),(.85,-.15,1.15),.085,'Orange',16)

def gastank():
    # Tank long axis X; domed shoulders built from a revolved mesh and rotated as geometry.
    obj=lathe('Tank shell',[(.6,-1.8),(.9,-1.55),(1,-1.2),(1,1.2),(.9,1.55),(.6,1.8)],'Olive',32)
    for v in obj.data.vertices:v.co=Vector((v.co.z,v.co.y,v.co.x+1.5))
    for x in (-1,1):
        box('Tank saddle',(x,0,.4),(.4,1.7,.8),'Steel')
        arc('Tank strap',(x,0,1.5),1.015,.055,'Rust',axis='X')
    tube('Ruptured elbow riser',(1.4,0,2.3),(1.4,0,2.8),.14,'Rust')
    tube('Broken outlet',(1.4,0,2.8),(1.4,-.7,2.8),.14,'Rust')
    tube('Dark rupture mouth',(1.4,-.705,2.8),(1.4,-.72,2.8),.11,'Dark',12)
    arc('Valve wheel',(-1.3,-1.1,1.6),.25,.032,'Orange',axis='Y')
    for a in range(4):
        t=a*math.pi/2;tube('Valve spoke',(-1.3,-1.1,1.6),(-1.3+.24*math.cos(t),-1.1,1.6+.24*math.sin(t)),.018,'Steel')
    box('Beacon base',(-1.5,0,2.35),(.3,.3,.12),'Steel')
    lathe('Amber beacon',[(.11,2.4),(.11,2.6),(.05,2.65)],'Lamp',16,(-1.5,0,0))

def craft():
    shell=lathe('Damaged capsule shell',[(.7,-2.4),(1.3,-1.8),(1.6,-.6),(1.5,.7),(.8,2),(.25,2.5)],'Ivory',32)
    for v in shell.data.vertices:v.co=Vector((v.co.x,v.co.z,v.co.y+1.6))
    craft_face_details()
    for x in (-1.6,1.6):
        box('Damaged side fin',(x,.3,.9),(.75,2,.18),'Teal')
        for y in (-.3,.5,1.1):tube('Exposed structural rib',(x*.7,y,.8),(x*1.28,y,.8),.065,'Steel')
    for x in (-.9,.9):tube('Bent landing strut',(x,.3,.9),(x*1.4,.8,.13),.11,'Steel')
    box('Missing panel dark cavity',(1.48,.6,1.55),(.06,1.1,.75),'Dark')
    for y in (.2,.5,.8,1.1):tube('Cavity rib',(1.52,y,1.2),(1.52,y,1.9),.035,'Rust')

def craft_face_details():
    tube('Front cockpit rim',(0,-2.34,1.95),(0,-2.56,1.95),.79,'Teal',32)
    tube('Cockpit glass',(0,-2.565,1.95),(0,-2.59,1.95),.65,'Glass',32)
    tube('Nose coupling',(0,-2.35,1),(0,-2.65,1),.32,'Steel',24)
    arc('Nose ring',(0,-2.66,1),.30,.055,'Ivory',axis='Y',n=24)
    tube('Rear thruster bell',(0,2.22,1.6),(0,2.65,1.6),.58,'Steel',32,r2=.82)
    tube('Thruster dark recess',(0,2.655,1.6),(0,2.67,1.6),.69,'Dark',32)
    arc('Rear thruster lip',(0,2.68,1.6),.76,.065,'Rust',axis='Y',n=32)

BUILDERS.update(Container=container,Ladder=ladder,Wrench=wrench,ToolRack=toolrack,Sprayer=sprayer,Shower=shower,GasTank=gastank,CrashedCraft=craft)

def environment(kind):
    if kind=='Wall':
        top=[(-2,2.5),(-1.6,2.3),(-1.2,2.38),(-.9,1.95),(-.55,2.1),(0,1.7),(.4,1.8),(.7,2.25),(1.2,2.16),(1.65,2.5),(2,2.35)]
        platepoly('Broken wall',[(-2,0),(2,0)]+list(reversed(top)),.4,'Concrete')
        box('Faded safety stripe',(0,-.205,1.05),(4,.012,.18),'Orange',0)
        for x,z in [(-1.65,2.3),(1.65,2.5)]:tube('Exposed rebar',(x,0,z-.3),(x+.08,0,z+.35),.021,'Rust',6)
    elif kind=='Drain':
        box('Drain bottom',(0,0,.04),(1,2,.08),'Concrete')
        for x in (-.46,.46):box('Drain wall',(x,0,.14),(.08,2,.28),'Concrete')
        for i in range(17):box('Removable grate',(0,-.8+i*.1,.25),(.86,.035,.035),'Steel',.005)
    elif kind in ['Slab','RouteBlue','RouteAmber']:
        box('Concrete tile',(0,0,.06),(2,2,.12),'Concrete',.025)
        if kind!='Slab':box('Paint route',(0,0,.122),(.45,1.98,.004),'Teal' if kind=='RouteBlue' else 'Orange',0)
        for a,b in [((-.99,.1,.122),(-.3,.24,.122)),((-.3,.24,.122),(.12,.6,.122)),((.12,.6,.122),(.2,.99,.122))]:tube('Shallow crack',a,b,.006,'Dark',4)
        for x in (-.9,.9):
            for y in (-.9,.9):box('Corner repair',(x,y,.125),(.14,.14,.013),'Steel',.005)
    elif kind=='Rail':
        for x in (-.9,.9):
            box('Weighted foot',(x,0,.1),(.35,.35,.2),'Concrete')
            box('Post',(x,0,.6),(.08,.08,1),'Teal')
        for z in (.5,1):box('Safety crossrail',(0,0,z),(1.9,.09,.11),'Orange')
        for x in (-.9,.9):bolt((x,-.055,1),.045,'Y')
    elif kind=='Bollard':
        lathe('Base flange',[(.22,0),(.22,.07)],'Steel',24)
        lathe('Safety post',[(.12,.07),(.14,.13),(.14,.73),(.12,.81),(.06,.85)],'Orange',24)
        lathe('Dark band',[(.142,.32),(.142,.46)],'Dark',24)
        for a in range(4):bolt((.17*math.cos(a*math.pi/2),.17*math.sin(a*math.pi/2),.075),.026)
    elif kind=='Facade':
        for x in (-1.7,1.7):box('Concrete pier',(x,0,2.3),(.6,.5,4.6),'Concrete')
        box('Lower sill',(0,0,.5),(4,.5,1),'Concrete')
        box('Upper lintel',(0,0,4.05),(4,.5,.5),'Concrete')
        for x in (-1.2,0,1.2):box('Window mullion',(x,0,2.4),(.06,.10,2.7),'Rust',.008)
        for z in (1.1,2.4,3.7):box('Window crossbar',(0,0,z),(2.8,.10,.055),'Rust',.008)
        for x,h in [(-1.75,.7),(-1.35,.35),(1.55,.45)]:box('Broken upper masonry',(x,0,4.3+h/2),(.35,.5,h),'Concrete')
        for x in (-1.7,1.7):tube('Broken reinforcement',(x,0,4.4),(x+.1,0,5),.025,'Rust',6)
    elif kind=='CoolingTower':
        profile=[(4.8,1.8),(4.4,4),(3.4,8),(3,11),(3.25,14),(3.65,18),(3.5,18),(3.1,14),(2.85,11),(3.25,8),(4.25,4),(4.65,1.8)]
        lathe('Tower shell',profile,'Concrete',48,cap=False)
        for i in range(16):
            a=i*math.tau/16;tube('Tower support',(4.5*math.cos(a),4.5*math.sin(a),.1),(4.6*math.cos(a),4.6*math.sin(a),1.85),.18,'Concrete')
        arc('Top reinforcement',(0,0,18),3.6,.045,'Rust',start=.3,end=5.5,n=40)
        lathe('Faded tower band',[(3.65,17.5),(3.68,17.7)],'Orange',48,cap=False)
    elif kind=='Chimney':
        box('Stack footing',(0,0,.25),(2.5,2.5,.5),'Concrete')
        lathe('Chimney tapered shell',[(1.05,.5),(.75,2),(.54,11.8),(.58,12),(.47,12),(.45,11.7)],'Concrete',24,cap=False)
        lathe('Faded stack band',[(.65,7.7),(.635,8.3)],'Orange',24,cap=False)
        for z in (1.8,4.8,7.5,10.5):arc('Stack reinforcement',(0,0,z),1.05-(z-.5)*.046,.04,'Steel',n=24)
    elif kind=='Rubble':
        rng=random.Random(52)
        for i in range(22):
            x,y=rng.uniform(-.85,.85),rng.uniform(-.6,.6);h=rng.uniform(.1,.3)
            rock('Concrete rubble',(x,y,h*.5),(.22,.19,h),'Concrete',i)
        for y in (-.3,.1,.4):tube('Rusted fragment',(-.5,y,.25),(.65,y+.15,.38),.055,'Rust',6)
    elif kind=='Floodlight':
        tube('Lamp pole',(0,0,.2),(0,0,2.25),.055,'Teal')
        for a in (0,math.tau/3,math.tau*2/3):
            x,y=.6*math.cos(a),.6*math.sin(a);tube('Tripod leg',(0,0,1),(x,y,.08),.055,'Teal');box('Weighted foot',(x,y,.06),(.3,.3,.12),'Steel')
        box('Lamp crossarm',(0,0,2.3),(1.2,.1,.1),'Steel')
        for x in (-.4,.4):
            pivot=empty('LampTilt',(x,0,2.35),P)
            box('Lamp housing',(x,0,2.36),(.48,.24,.42),'Orange',parent=pivot)
            box('Lamp glass',(x,-.13,2.36),(.38,.025,.32),'Lamp',parent=pivot)
            for dx in (-.13,.13):tube('Lamp cage',(x+dx,-.15,2.18),(x+dx,-.15,2.54),.013,'Steel',6,pivot)
        tube('Cable reel',(-.12,0,.45),(.12,0,.45),.22,'Dark',24)

for k in ['Wall','Drain','Slab','Rail','Bollard','Facade','CoolingTower','Chimney','Rubble','Floodlight','RouteBlue','RouteAmber']:BUILDERS[k]=lambda k=k:environment(k)

def plants(kind):
    rng=random.Random(sum(map(ord,kind)))
    if kind in ['GrassDry','GrassSeed','Reeds']:
        height={'GrassDry':.35,'GrassSeed':.8,'Reeds':.9}[kind]
        for i in range(40 if kind=='GrassDry' else 24):
            a=rng.uniform(0,math.tau);r=rng.uniform(.01,.1);h=height*rng.uniform(.35,.9)
            base=(r*math.cos(a),r*math.sin(a),.015)
            tip=(math.cos(a)*(.14+h*.15),math.sin(a)*(.14+h*.15),h)
            leaf('Ribbon blade',base,tip,.025 if kind=='GrassDry' else .045,'Dry' if kind!='Reeds' else 'Olive')
        if kind!='GrassDry':
            for i in range(5):
                a=i*2.3;x,y=.15*math.cos(a),.15*math.sin(a);h=height*rng.uniform(.8,1)
                tube('Seed stem',(x*.25,y*.25,.02),(x,y,h),.006,'Dry',6)
                if kind=='Reeds':tube('Cattail head',(x,y,h-.16),(x,y,h-.025),.025,'Bark',8)
                else:
                    for j in range(6):
                        for s in (-1,1):leaf('Seed grain',(x,y,h-j*.02),(x+s*.035,y+.012,h-.04-j*.02),.026,'Dry')
    elif kind=='WeedRosette':
        for i in range(16):
            a=i*2.4;r=.28 if i<10 else .18;h=.07 if i<10 else .22
            leaf('Rosette broad leaf',(0,0,.03),(math.cos(a)*r,math.sin(a)*r,h),.11,'Olive' if i%3 else 'LeafLight')
        lathe('Central bud',[(.025,.01),(.025,.18),(.005,.24)],'LeafLight',8)
    elif kind=='Wildflowers':
        for i in range(5):
            a=i*2.4;x,y=.15*math.cos(a),.15*math.sin(a);h=rng.uniform(.3,.45)
            tube('Flower stalk',(0,0,0),(x,y,h),.005,'Olive',6)
            lathe('Flower disk',[(.025,h),(.03,h+.009),(.016,h+.02)],'Orange',12,(x,y,0))
            for j in range(7):
                b=j*math.tau/7;leaf('Petal',(x,y,h),(x+.072*math.cos(b),y+.072*math.sin(b),h+.005),.025,'Flower')
            leaf('Flower leaf',(x/2,y/2,h/2),(x/2+.075,y/2+.025,h*.62),.035)
    elif kind in ['ScrubDry','Shrub','DeadSapling']:
        height={'ScrubDry':.7,'Shrub':.8,'DeadSapling':1.1}[kind]
        tube('Trunk',(0,0,0),(.03,0,height*.65),.045 if kind!='DeadSapling' else .085,'Bark',8,r2=.023)
        for i in range(9 if kind!='DeadSapling' else 5):
            a=i*2.4;start=Vector((.02,0,height*.2));end=Vector((.3*math.cos(a),.28*math.sin(a),height*rng.uniform(.65,1)))
            tube('Branch',start,end,.013 if kind!='DeadSapling' else .035,'Bark',7,r2=.006)
            for j in range(3):
                q=start.lerp(end,.4+j*.2);v=q+Vector((.12*math.cos(a+.5),.12*math.sin(a+.5),.12))
                tube('Twig',q,v,.006,'Bark',5,r2=.002)
                count=6 if kind=='Shrub' else 2 if kind=='ScrubDry' else (1 if j==2 and i%2==0 else 0)
                for k in range(count):
                    b=k*2.4;leaf('Shrub leaf',v,v+Vector((.10*math.cos(b),.10*math.sin(b),.05)),.05,'Dry' if kind=='DeadSapling' else ('Olive' if k%2 else 'LeafLight'))
        for a in range(5):tube('Root',(0,0,.1),(.18*math.cos(a),.18*math.sin(a),.01),.018,'Bark',6,r2=.005)
    elif kind in ['IvyLow','IvyHanging']:
        hanging=kind=='IvyHanging';box('Small wall fragment',(0,0,1.45 if hanging else .055),(1.2,.24,.10),'Concrete')
        for i in range(7):
            x=-.5+i*.16;length=rng.uniform(.65,1.4) if hanging else .45
            a=Vector((x,-.1,1.47 if hanging else .12));b=a+Vector((.09,0,-length if hanging else .03))
            tube('Ivy vine',a,b,.009,'Bark',6)
            for j in range(9):
                q=a.lerp(b,j/9)
                for sign in (-1,1):leaf('Ivy leaf',q,q+Vector((sign*.14,-.045,-.07 if hanging else .1)),.12,'Olive' if j%3 else 'LeafLight')
    elif kind in ['Moss','Lichen']:
        rock('Base fragment',(0,0,.025),(.32,.24,.025),'Concrete' if kind=='Moss' else 'Rust',3)
        for i in range(40):
            x,y=rng.uniform(-.27,.27),rng.uniform(-.18,.18)
            rock('Moss cushion' if kind=='Moss' else 'Lichen disk',(x,y,.04),(.04,.035,.02 if kind=='Moss' else .006),'Moss' if kind=='Moss' else 'LeafLight',i)

for k in ['GrassDry','GrassSeed','WeedRosette','Wildflowers','ScrubDry','Shrub','IvyLow','IvyHanging','Moss','Lichen','Reeds','DeadSapling']:BUILDERS[k]=lambda k=k:plants(k)

def bounds(objects):
    pts=[o.matrix_world@Vector(corner) for o in objects if o.type=='MESH' for corner in o.bound_box]
    low=Vector([min(p[i] for p in pts) for i in range(3)])
    high=Vector([max(p[i] for p in pts) for i in range(3)])
    return low,high

def save_asset(asset_id,render=True,replace_generated=False):
    scene=bpy.data.scenes['S2_'+asset_id];bpy.context.window.scene=scene
    collection=bpy.data.collections['S2_'+asset_id]
    meshes=[o for o in collection.objects if o.type=='MESH']
    # Consolidate static detail per parent while retaining all functional pivot empties.
    groups={}
    for obj in meshes:groups.setdefault(obj.parent,[]).append(obj)
    for parent,objects in groups.items():
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects:
            obj.select_set(True);bpy.context.view_layer.objects.active=obj
            for mod in list(obj.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
        if len(objects)>1:
            bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join()
        objects[0].name=parent.name+'_Mesh' if parent else asset_id+'_Mesh'
    bpy.context.view_layer.update()
    meshes=[o for o in collection.objects if o.type=='MESH']
    lo,hi=bounds(meshes)
    errors=[]
    for obj in meshes:
        if not obj.data.uv_layers:errors.append(obj.name+':missing UV')
        if any(not math.isfinite(c) for v in obj.data.vertices for c in v.co):errors.append(obj.name+':nonfinite coordinate')
    if errors:raise RuntimeError(str(errors))
    OUT.mkdir(parents=True,exist_ok=True);(OUT/'Source~').mkdir(exist_ok=True);PREVIEW.mkdir(parents=True,exist_ok=True)
    fbx=OUT/('S2_'+asset_id+'.fbx');blend=OUT/'Source~'/('S2_'+asset_id+'.blend')
    if fbx.exists() or blend.exists():
        own_report=PREVIEW/(asset_id+'.json')
        if not replace_generated or not own_report.exists() or json.loads(own_report.read_text())['asset']!=asset_id:
            raise RuntimeError('Refusing overwrite '+asset_id)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in collection.objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        bake_anim=False,add_leaf_bones=False,use_custom_props=True,mesh_smooth_type='FACE',use_triangles=True)
    triangles=0
    for o in meshes:o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
    report={'asset':asset_id,'fbx':fbx.name,'blend':'Source~/'+blend.name,'mesh_objects':len(meshes),
        'triangles':triangles,'dimensions_m':list(hi-lo),'bounds_min':list(lo),'bounds_max':list(hi),
        'materials':sorted({m.name for o in meshes for m in o.data.materials if m}),
        'pivots':[o.name for o in collection.objects if o.type=='EMPTY'],'errors':errors,
        'reference_views':['front','left','right','back'],'source_created_after_references':True}
    if render: render_asset(scene,collection,asset_id,lo,hi)
    bpy.data.libraries.write(str(blend),{scene},fake_user=True,compress=True)
    (PREVIEW/(asset_id+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    return report

def render_asset(scene,collection,asset_id,lo,hi):
    folder=PREVIEW/asset_id;folder.mkdir(exist_ok=True)
    data=bpy.data.cameras.new('ReviewCamera');cam=bpy.data.objects.new('ReviewCamera',data);scene.collection.objects.link(cam);scene.camera=cam
    data.type='ORTHO';center=(lo+hi)/2;extent=max(hi-lo)
    scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL'
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH';scene.display.shading.show_specular_highlight=True
    scene.display.shading.background_type='WORLD';scene.world=bpy.data.worlds.new('S2_ReviewWorld');scene.world.color=(.65,.65,.65)
    scene.render.resolution_x=768;scene.render.resolution_y=768;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'
    for name,d in [('front',(0,-1,0)),('left',(-1,0,0)),('right',(1,0,0)),('back',(0,1,0)),('preview',(1,-1,.75))]:
        direction=Vector(d).normalized();cam.location=center+direction*extent*3
        cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        rotation=cam.rotation_euler.to_matrix();right=rotation.col[0];up=rotation.col[1];size=hi-lo
        data.ortho_scale=max(sum(abs(right[i])*size[i] for i in range(3)),sum(abs(up[i])*size[i] for i in range(3)))*1.12
        data.clip_end=max(100,extent*10)
        scene.render.filepath=str(folder/(name+'.png'));bpy.ops.render.render(write_still=True)

def validate_fbx(asset_id):
    """Reimport in a NEW temporary scene, verify scale, geometry, UVs and hierarchy."""
    previous=bpy.context.window.scene
    scene=bpy.data.scenes.new('S2_QA_'+asset_id);bpy.context.window.scene=scene
    bpy.ops.import_scene.fbx(filepath=str(OUT/('S2_'+asset_id+'.fbx')),use_anim=False)
    meshes=[o for o in scene.objects if o.type=='MESH'];bpy.context.view_layer.update();lo,hi=bounds(meshes)
    source=json.loads((PREVIEW/(asset_id+'.json')).read_text())
    expected=source['dimensions_m'];actual=list(hi-lo)
    scale_ok=all(abs(a-b)<max(.002,b*.001) for a,b in zip(actual,expected))
    uv_ok=all(len(o.data.uv_layers)>0 for o in meshes)
    pivots={o.name.split('.')[0] for o in scene.objects if o.type=='EMPTY'}
    result={'asset':asset_id,'scale_ok':scale_ok,'uv_ok':uv_ok,'mesh_count':len(meshes),
        'dimensions_m':actual,'pivots_count':len(pivots),'passed':scale_ok and uv_ok and len(meshes)==source['mesh_objects']}
    bpy.context.window.scene=previous
    # Remove only objects in this just-created QA scene, never objects in the user's scene.
    for obj in list(scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
    bpy.data.scenes.remove(scene)
    (PREVIEW/(asset_id+'-validation.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result
