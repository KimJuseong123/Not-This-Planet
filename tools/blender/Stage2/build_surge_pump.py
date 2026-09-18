"""Reference-led surge pump. Execute through Blender MCP, preserving user's scene."""
import bpy, math, importlib.util, json, traceback
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]

def run():
    previous=bpy.context.window.scene
    spec=importlib.util.spec_from_file_location('surge_geometry',ROOT/'tools/blender/Stage2/build_assets.py')
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    refs=ROOT/'docs/assets/concepts/stage2/pressure-surge'
    for v in ['front','left','right','back']:assert (refs/(v+'.png')).exists()
    try:
        scene=m.create_scene({'id':'SurgePump'})
        palette={'Teal':(.065,.25,.26),'Ivory':(.73,.72,.61),'Orange':(.9,.28,.035),'Steel':(.22,.25,.25),'Dark':(.025,.035,.038),'Lamp':(1,.53,.025),'Yellow':(.95,.67,.04)}
        texdir=m.OUT/'S2_SurgePump.fbm';texdir.mkdir(exist_ok=True)
        # Bake UV-space weathering into portable FBM images; no external material dependencies.
        scene.render.engine='CYCLES';scene.cycles.samples=1
        scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
        bpy.ops.mesh.primitive_plane_add(size=2);plane=bpy.context.object
        for key,c in palette.items():
            mat=bpy.data.materials.new('S2Surge_'+key);mat.use_nodes=True;mat.diffuse_color=(*c,1)
            n=mat.node_tree.nodes;l=mat.node_tree.links;bs=next((v for v in n if v.type=='BSDF_PRINCIPLED'),None)
            if bs is None:
                bs=n.new('ShaderNodeBsdfPrincipled');out=n.new('ShaderNodeOutputMaterial');l.new(bs.outputs[0],out.inputs[0])
            bs.inputs['Roughness'].default_value=.52;bs.inputs['Metallic'].default_value=.45 if key not in ['Dark','Lamp'] else .05
            uv=n.new('ShaderNodeTexCoord');noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=90;noise.inputs['Detail'].default_value=3
            ramp=n.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.32;ramp.color_ramp.elements[0].color=(.13,.065,.025,1)
            ramp.color_ramp.elements[1].position=.48;ramp.color_ramp.elements[1].color=(*c,1)
            l.new(uv.outputs['UV'],noise.inputs['Vector']);l.new(noise.outputs['Fac'],ramp.inputs[0]);l.new(ramp.outputs['Color'],bs.inputs['Base Color'])
            im=bpy.data.images.new('S2Surge_'+key+'_BaseColor',width=512,height=512,alpha=False)
            target=n.new('ShaderNodeTexImage');target.image=im;n.active=target
            plane.data.materials.clear();plane.data.materials.append(mat);bpy.context.view_layer.objects.active=plane
            bpy.ops.object.bake(type='DIFFUSE');im.filepath_raw=str(texdir/(im.name+'.png'));im.file_format='PNG';im.save();im.pack()
            l.new(target.outputs['Color'],bs.inputs['Base Color']);m.MAT[key]=mat
        bpy.data.objects.remove(plane,do_unlink=True)
        box,tube,arc,lathe=m.box,m.tube,m.arc,m.lathe
        box('Skid bed',(0,0,.12),(2.4,1.45,.18),'Steel')
        for x in [-1.05,1.05]:box('Teal skid rail',(x,0,.25),(.16,1.42,.18),'Teal')
        for y in [-.66,.66]:box('Cross member',(0,y,.25),(2.35,.12,.19),'Teal')
        for x in [-1,1]:
            for y in [-.55,.55]:
                arc('Lifting eye',(x,y,.42),.07,.022,'Steel',axis='Y',n=20);m.bolt((x,y,.3),.035)
        box('Safety stripe plate',(0,-.735,.25),(1.05,.018,.16),'Yellow',.004)
        for x in [-.42,-.21,0,.21,.42]:
            o=box('Black warning slash',(x,-.748,.25),(.075,.008,.18),'Dark',.001)
            # Mesh coordinates are world authored: tilt the stripe vertices around its center.
            for v in o.data.vertices:v.co.x+=(v.co.z-.25)*.5
        lathe('Accumulator',[ (.20,.36),(.28,.42),(.32,.56),(.32,1.92),(.29,2.06),(.20,2.17),(.065,2.20)],'Ivory',48,(-.72,.22,0))
        for z in [.62,1.35,1.92]:arc('Tank weld seam',(-.72,.22,z),.322,.01,'Steel',n=48)
        for x in [-.91,-.53]:box('Tank foot',(x,.22,.38),(.10,.38,.24),'Steel')
        tube('Tank service cap',(-.72,.22,2.19),(-.72,.22,2.29),.065,'Steel',16)
        for x in [-.28,.38]:
            for y in [-.25,.36]:
                tube('Rubber isolator',(x,y,.28),(x,y,.40),.09,'Dark',16);m.bolt((x,y,.405),.055)
        box('Pump pedestal',(.08,0,.46),(.7,.65,.18),'Teal')
        tube('Volute housing',(.08,-.24,.87),(.08,.24,.87),.43,'Teal',48)
        for y in [-.26,.26]:
            tube('Volute flange',(.08,y-.025,.87),(.08,y+.025,.87),.455,'Teal',48)
            for i in range(12):
                a=i*math.tau/12;m.bolt((.08+.405*math.cos(a),y-.03,.87+.405*math.sin(a)),.032,axis='Y')
        tube('Motor',(.08,.25,.87),(.08,.60,.87),.25,'Teal',32)
        for i in range(16):
            a=i*math.tau/16;tube('Motor cooling rib',(.08+.26*math.cos(a),.29,.87+.26*math.sin(a)),(.08+.26*math.cos(a),.58,.87+.26*math.sin(a)),.013,'Steel',6)
        tube('Discharge riser',(.1,0,1.05),(.1,0,1.65),.12,'Teal',32)
        tube('Header',(-.45,0,1.60),(.69,0,1.60),.105,'Teal',32)
        tube('Tank bypass',(-.48,.22,.63),(-.48,.22,1.62),.038,'Steel',16)
        tube('Bypass elbow',(-.48,.22,1.62),(-.1,0,1.62),.04,'Steel',16)
        tube('Orange downpipe',(.80,0,.92),(.80,0,1.52),.15,'Orange',32)
        arc('Orange elbow',(.66,0,1.46),.14,.15,'Orange',0,math.pi/2,'Y',n=16)
        tube('Outlet barrel',(.80,0,.92),(.80,-.48,.92),.17,'Orange',32)
        arc('Outlet lip',(.80,-.50,.92),.19,.045,'Orange',axis='Y',n=40)
        tube('Dark recessed outlet',(.80,-.48,.92),(.80,-.485,.92),.148,'Dark',32)
        for i in range(8):
            a=i*math.tau/8;m.bolt((.8+.21*math.cos(a),-.51,.92+.21*math.sin(a)),.023,axis='Y')
        for z in [1.22,1.47]:lathe('Bolted pipe collar',[(.17,z),(.17,z+.065)],'Steel',32,(.8,0,0))
        wheel=m.empty('ReliefWheel',(.08,-.43,.87),m.P)
        arc('Five spoke wheel',(.08,-.43,.87),.24,.024,'Orange',axis='Y',parent=wheel,n=40)
        for i in range(5):
            a=i*math.tau/5;tube('Valve spoke',(.08,-.43,.87),(.08+.22*math.cos(a),-.43,.87+.22*math.sin(a)),.014,'Orange',8,wheel)
        tube('Valve hub',(.08,-.47,.87),(.08,-.29,.87),.065,'Steel',16,wheel)
        tube('Gauge stem',(.12,0,1.65),(.12,0,1.87),.035,'Steel',16)
        tube('Gauge rim',(.12,-.07,2),(.12,.035,2),.185,'Steel',40)
        tube('Ivory gauge face',(.12,-.077,2),(.12,-.083,2),.167,'Ivory',40)
        for i in range(25):
            a=math.radians(-40+260*i/24);r=.13
            tube('Gauge tick',(.12+r*math.cos(a),-.086,2+r*math.sin(a)),(.12+(r+.022)*math.cos(a),-.086,2+(r+.022)*math.sin(a)),.004,'Dark',4)
        needle=m.empty('GaugeNeedle',(.12,-.09,2),m.P)
        tube('Pressure needle',(.12,-.09,2),(.03,-.09,2.08),.008,'Orange',6,needle)
        flap=m.empty('OutletFlap',(.8,-.51,1.16),m.P)
        tube('Flap lid',(.8,-.505,1.38),(.8,-.535,1.38),.20,'Steel',32,flap)
        tube('Flap hinge',(.65,-.51,1.16),(.95,-.51,1.16),.024,'Steel',12)
        m.empty('JetOrigin',(.8,-.58,.92),m.P)
        beacon=m.empty('WarningBeacon',(.8,0,1.77),m.P)
        lathe('Beacon housing',[(.085,1.61),(.095,1.64),(.095,1.72),(.075,1.73)],'Steel',24,(.8,0,0))
        lathe('Amber lens',[(.074,1.73),(.074,1.90),(.06,1.93),(.02,1.95)],'Lamp',32,(.8,0,0),beacon)
        for z in [1.75,1.8,1.85,1.9]:arc('Beacon lens ridges',(.8,0,z),.076,.006,'Orange',n=24,parent=beacon)
        box('Junction box',(-.85,-.24,.83),(.30,.18,.38),'Steel')
        for x in [-.96,-.74]:
            for z in [.68,.98]:m.bolt((x,-.34,z),.016,axis='Y')
        bpy.context.view_layer.update()
        report=m.save_asset('SurgePump',render=True)
        spec=importlib.util.spec_from_file_location('surge_fbm',ROOT/'tools/blender/Stage2/package_fbm.py');p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
        report['fbm']=p.export_one('SurgePump')
        return report
    finally:bpy.context.window.scene=previous

def start():
    path=ROOT/'artifacts/stage2-models/surge-progress.json'
    path.write_text(json.dumps({'running':True}))
    def job():
        try:result={'running':False,'result':run()}
        except Exception:result={'running':False,'error':traceback.format_exc()}
        path.write_text(json.dumps(result,indent=2));return None
    bpy.app.timers.register(job,first_interval=.1)
