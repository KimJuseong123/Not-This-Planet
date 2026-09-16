"""Render consistent orthographic inspection views from the real authored meshes."""
import bpy
from mathutils import Vector

def render_views(indices):
    kit=bpy.app.driver_namespace['astra_lwt'];base=kit['BASE'];out=base/'Previews~/views';out.mkdir(exist_ok=True)
    result=[]
    for index in indices:
        slug,name,_,_=kit['SPECS'][index]
        scene=next(s for s in kit['STATE']['scenes'] if s.name=='Astra_LWT_'+name)
        bpy.context.window.scene=scene;obj=scene.objects['SM_Astra_'+name];cam=scene.camera
        saved=(cam.location.copy(),cam.rotation_euler.copy(),cam.data.ortho_scale)
        info=kit['metrics']([obj]);mn=Vector(info['bounds_min']);mx=Vector(info['bounds_max']);center=(mn+mx)/2;size=max(mx-mn)
        scene.render.resolution_x=720;scene.render.resolution_y=480
        for label,direction in [('front',(0,-1,0)),('right',(1,0,0)),('left',(-1,0,0)),('top',(0,0,1)),('back',(0,1,0))]:
            cam.location=center+Vector(direction)*size*3
            cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
            bpy.context.view_layer.update()
            points=[cam.matrix_world.inverted()@(obj.matrix_world@Vector(c)) for c in obj.bound_box]
            sx=max(p.x for p in points)-min(p.x for p in points);sy=max(p.y for p in points)-min(p.y for p in points)
            cam.data.ortho_scale=max(sx,sy*1.5)*1.14
            scene.render.filepath=str(out/(slug+'-'+label+'.png'));bpy.ops.render.render(write_still=True)
        cam.location,cam.rotation_euler,cam.data.ortho_scale=saved
        scene.render.resolution_x=1200;scene.render.resolution_y=850
        result.append(slug)
    return {'rendered':result}
