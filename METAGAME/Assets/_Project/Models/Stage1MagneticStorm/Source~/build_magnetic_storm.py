"""Run in Blender through MCP. Creates a separate scene, exports selected assets only."""
import bpy, math, json
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[6]
OUT=ROOT/"METAGAME/Assets/_Project/Models/Stage1MagneticStorm"
FBM=OUT/"AstraMagneticStorm.fbm"
SOURCE=OUT/"Source~"
ART=ROOT/"artifacts/stage1-magnetic-storm"
for p in (OUT,FBM,SOURCE,ART): p.mkdir(parents=True,exist_ok=True)
if bpy.context.object and bpy.context.object.mode!="OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
scene=bpy.data.scenes.new("Astra_MagneticStorm_Production")
bpy.context.window.scene=scene
scene.unit_settings.system="METRIC"
scene.unit_settings.scale_length=1
scene.render.engine="BLENDER_EEVEE"
root=bpy.data.objects.new("AstraMagneticStorm_ROOT",None)
scene.collection.objects.link(root)
root["unity_bounds"]="24 x 5 x 10 metres, center pivot"
root["motion"]="Existing StormPose: negative local X is the travel direction"
export_objects=[root]
def attach(o):
    o.parent=root
    export_objects.append(o)
    return o
def save_image(name, rgba, filename, data=False):
    h,w=rgba.shape[:2]
    im=bpy.data.images.new(name,width=w,height=h,alpha=True)
    if data: im.colorspace_settings.name="Non-Color"
    im.pixels.foreach_set(np.ascontiguousarray(rgba,dtype=np.float32).ravel())
    im.filepath_raw=str(filename); im.file_format="PNG"; im.save()
    return im
# Density is built from the same volumetric lobes that are visible in the source scene.
rng=np.random.default_rng(564)
lobes=[]
for i in range(10):
    x=-9.8+i*2.12
    lobes.append(((x,-.25+.3*math.sin(i*1.9),.6*math.sin(i)),
                  (2.35,1.75+.3*math.cos(i),2.7)))
for i in range(60):
    x=float(rng.uniform(-10.1,10.1))
    lobes.append(((x,float(rng.uniform(-1.6,.15)),float(rng.uniform(-2.5,2.5))),
                  (float(rng.uniform(.85,1.65)),float(rng.uniform(.6,1.15)),float(rng.uniform(.85,1.7)))))
def tileable_noise(n,grid,seed):
    r=np.random.default_rng(seed).random((grid,grid,grid),dtype=np.float32)
    v=np.arange(n,dtype=np.float32)/n*grid
    a=np.floor(v).astype(int)%grid;b=(a+1)%grid;f=v-np.floor(v);f=f*f*(3-2*f)
    z0=r[a[:,None,None],a[None,:,None],a[None,None,:]]
    z1=r[a[:,None,None],a[None,:,None],b[None,None,:]]
    z2=r[a[:,None,None],b[None,:,None],a[None,None,:]]
    z3=r[a[:,None,None],b[None,:,None],b[None,None,:]]
    z4=r[b[:,None,None],a[None,:,None],a[None,None,:]]
    z5=r[b[:,None,None],a[None,:,None],b[None,None,:]]
    z6=r[b[:,None,None],b[None,:,None],a[None,None,:]]
    z7=r[b[:,None,None],b[None,:,None],b[None,None,:]]
    fx=f[None,None,:];fy=f[None,:,None];fz=f[:,None,None]
    return ((z0*(1-fx)+z1*fx)*(1-fy)+(z2*(1-fx)+z3*fx)*fy)*(1-fz)+((z4*(1-fx)+z5*fx)*(1-fy)+(z6*(1-fx)+z7*fx)*fy)*fz
N=128
z,y,x=np.meshgrid((np.arange(N)+.5)/N*10-5,(np.arange(N)+.5)/N*5-2.5,(np.arange(N)+.5)/N*24-12,indexing="ij")
field=np.zeros((N,N,N),dtype=np.float32)
for (cx,cy,cz),(rx,ry,rz) in lobes:
    d=((x-cx)/rx)**2+((y-cy)/ry)**2+((z-cz)/rz)**2
    field=np.maximum(field,np.clip(1-d,0,1).astype(np.float32))
noise=tileable_noise(N,12,564)*.60+tileable_noise(N,26,565)*.28+tileable_noise(N,48,566)*.12
shape=np.clip((field+(noise-.5)*.7-.10)/.43,0,1)
density=shape*shape*(3-2*shape)
density*=np.clip(field*8,0,1)
# Fade every face of the box so there is never a rectangular smoke edge.
edge=np.minimum.reduce([np.broadcast_to(1-np.abs(x)/12,field.shape),np.broadcast_to(1-np.abs(y)/2.5,field.shape),np.broadcast_to(1-np.abs(z)/5,field.shape)])
density*=np.clip(edge/.055,0,1)
def atlas(volume,columns,name):
    n=volume.shape[0];a=np.zeros((n*math.ceil(n/columns),n*columns,4),dtype=np.float32);a[:,:,3]=1
    for k in range(n): a[(k//columns)*n:(k//columns+1)*n,(k%columns)*n:(k%columns+1)*n,:3]=volume[k,:,:,None]
    return save_image(name,a,FBM/(name+".png"),True)
atlas(density,16,"SmokeDensity_128")
noise64=tileable_noise(64,5,700)*.6+tileable_noise(64,11,701)*.3+tileable_noise(64,23,702)*.1
atlas(noise64,8,"SmokeNoise_64")
palette=np.ones((32,32,4),np.float32);palette[:,:,:3]=(.11,.09,.18)
base=save_image("StormDebris_BaseColor",palette,FBM/"StormDebris_BaseColor.png")
palette[:,:,:3]=(.02,.68,1)
emission=save_image("Flux_Emission",palette,FBM/"Flux_Emission.png")
def surface(name,color,emit=0,image=None):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    p=next(n for n in mat.node_tree.nodes if n.type=="BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value=(*color,1)
    p.inputs["Roughness"].default_value=.46
    p.inputs["Metallic"].default_value=.45 if emit==0 else .1
    p.inputs["Emission Color"].default_value=(*color,1);p.inputs["Emission Strength"].default_value=emit
    if image:
        node=mat.node_tree.nodes.new("ShaderNodeTexImage");node.image=image
        mat.node_tree.links.new(node.outputs["Color"],p.inputs["Base Color"])
    return mat
debris_mat=surface("StormDebris",(.12,.09,.20),0,base)
flux_mat=surface("MagneticFlux",(.015,.6,1),4,emission)
proxy_mat=surface("VolumeProxy_ReplaceWithUnityRaymarch",(.38,.2,.63))
# FBX holds a volume domain; volumetric shading is reconstructed by URP.
bpy.ops.mesh.primitive_cube_add(size=1)
box=attach(bpy.context.object);box.name="SmokeVolume";box.scale=(24,10,5)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
box.data.materials.append(proxy_mat)
box.hide_render=True;box.display_type="WIRE"
# Closed 3D flux streamlines with longitudinal UVs for Unity emission animation.
for j in range(7):
    verts=[];faces=[];steps=160;sides=6
    center=(-8+j*2.65,-.12+.12*math.sin(j),0)
    rx=2.7+.3*(j%2);ry=.85+.20*(j%3);rz=2.3
    def point(t):
        return Vector((center[0]+rx*math.cos(t)+.24*math.sin(3*t+j),center[1]+ry*math.sin(t)+.32*math.cos(2*t+j),rz*math.sin(t+.8+j*.38)+.3*math.cos(3*t+j)))
    for k in range(steps):
        t=math.tau*k/steps;p=point(t)
        tangent=(point(t+.001)-p).normalized()
        normal=tangent.cross(Vector((0,1,0))).normalized()
        bi=tangent.cross(normal).normalized()
        radius=.025+.009*(j%3)
        for q in range(sides):
            v=p+radius*(normal*math.cos(math.tau*q/sides)+bi*math.sin(math.tau*q/sides))
            verts.append((v.x,-v.z,v.y)) # Unity-like coordinates -> Blender.
    for k in range(steps):
        for q in range(sides):faces.append((k*sides+q,((k+1)%steps)*sides+q,((k+1)%steps)*sides+(q+1)%sides,k*sides+(q+1)%sides))
    mesh=bpy.data.meshes.new("FluxMesh_%02d"%j);mesh.from_pydata(verts,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name="UVMap")
    for p in mesh.polygons:
        k=p.index//sides;q=p.index%sides
        for li,co in zip(p.loop_indices,[(k/steps,q/sides),((k+1)/steps,q/sides),((k+1)/steps,(q+1)/sides),(k/steps,(q+1)/sides)]):uv.data[li].uv=co
        p.use_smooth=True
    o=bpy.data.objects.new("Flux_%02d"%j,mesh);scene.collection.objects.link(o);attach(o);mesh.materials.append(flux_mat)
for j in range(9):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=float(rng.uniform(.12,.27)),location=(float(rng.uniform(-10,10)),float(rng.uniform(-3,3)),float(rng.uniform(-.8,1.4))))
    o=attach(bpy.context.object);o.name="Debris_%02d"%j;o.scale=(1,.7,1.15)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(debris_mat)
# Non-exported authoring collection with true volume nodes in Blender.
author=bpy.data.collections.new("AUTHORING_VOLUMETRIC_LOBES");scene.collection.children.link(author)
smoke=bpy.data.materials.new("Smoke_Authoring_Volume");smoke.use_nodes=True
nodes=smoke.node_tree.nodes;nodes.clear()
output=nodes.new("ShaderNodeOutputMaterial");vol=nodes.new("ShaderNodeVolumePrincipled")
vol.inputs["Color"].default_value=(.39,.20,.64,1);vol.inputs["Anisotropy"].default_value=.2
tex=nodes.new("ShaderNodeTexNoise");tex.inputs["Scale"].default_value=3.2;tex.inputs["Detail"].default_value=4
ramp=nodes.new("ShaderNodeMath");ramp.operation="MULTIPLY";ramp.inputs[1].default_value=1.35
smoke.node_tree.links.new(tex.outputs["Fac"],ramp.inputs[0]);smoke.node_tree.links.new(ramp.outputs[0],vol.inputs["Density"]);smoke.node_tree.links.new(vol.outputs["Volume"],output.inputs["Volume"])
for j,((cx,cy,cz),(rx,ry,rz)) in enumerate(lobes):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,location=(cx,-cz,cy))
    o=bpy.context.object;o.name="DensityLobe_%02d"%j;o.scale=(rx,rz,ry)
    for col in list(o.users_collection):col.objects.unlink(o)
    author.objects.link(o);o.data.materials.append(smoke)
# Export selected FBX with explicit portable image paths.
bpy.ops.object.select_all(action="DESELECT")
for o in export_objects:o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(OUT/"AstraMagneticStorm.fbx"),use_selection=True,object_types={"MESH","EMPTY"},apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",axis_forward="-Z",axis_up="Y",bake_space_transform=True,add_leaf_bones=False,bake_anim=False,path_mode="RELATIVE",embed_textures=False)
# Studio camera and lighting for a source preview.
world=bpy.data.worlds.new("Storm_Studio");world.use_nodes=True
background=next(n for n in world.node_tree.nodes if n.type=="BACKGROUND")
background.inputs[0].default_value=(.025,.018,.06,1)
background.inputs[1].default_value=.3;scene.world=world
def area(name,location,energy,size,color):
    data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size;data.color=color
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=location;o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat("-Z","Y").to_euler()
area("Lavender_Softbox",(1,-5,10),2200,10,(.7,.45,1))
area("Cyan_Rim",(-8,3,4),1600,7,(.05,.6,1))
data=bpy.data.cameras.new("Storm_Camera");cam=bpy.data.objects.new("Storm_Camera",data);scene.collection.objects.link(cam)
cam.location=(17,-28,9);cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat("-Z","Y").to_euler();data.type="ORTHO";data.ortho_scale=29;scene.camera=cam
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.render.image_settings.file_format="PNG";scene.render.filepath=str(ART/"blender-volume-preview.png")
scene.view_settings.view_transform="AgX"
bpy.context.view_layer.update()
# Write only this new scene and its dependencies, retaining the user's unsaved Blender file.
bpy.data.libraries.write(str(SOURCE/"AstraMagneticStorm.blend"),{scene},path_remap="RELATIVE",fake_user=True,compress=True)
manifest={"model":"AstraMagneticStorm.fbx","boundsUnity":[24,5,10],"densityResolution":128,"densityAtlasColumns":16,"noiseResolution":64,"noiseAtlasColumns":8,"fluxMeshes":7,"debrisMeshes":9,"vertices":sum(len(o.data.vertices) for o in export_objects if o.type=="MESH"),"triangles":sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in export_objects if o.type=="MESH"),"densityLobes":len(lobes),"blenderVersion":bpy.app.version_string}
(OUT/"authoring-manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
result=manifest
