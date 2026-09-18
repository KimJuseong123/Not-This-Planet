"""FBX collision roundtrip and continuous spiral surface validation in Blender."""
import bpy,json,math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

kit=bpy.app.driver_namespace['astra_lwt'];reports=[]
for item in sorted(kit['STATE']['assets'],key=lambda r:r['id']):
    name=item['fbx'][:-4];path=kit['BASE']/item['folder']/'Colliders'/('UCX_'+name+'.fbx')
    scene=bpy.data.scenes.new('LWT_COLLIDER_QA');bpy.context.window.scene=scene
    bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
    bpy.context.view_layer.update();objects=[o for o in scene.objects if o.type=='MESH']
    metrics=kit['metrics'](objects)
    assert metrics['triangles']==item['collision_triangles'],item['id']
    audits=[kit['audit'](o.data) for o in objects]
    assert all(x['non_manifold_edges']==0 and x['degenerate_faces']==0 for x in audits)
    rec={'id':item['id'],'collision_roundtrip':'PASS','triangles':metrics['triangles'],'audit':audits}
    if item['id']=='L11':
        o=objects[0];verts=[o.matrix_world@v.co for v in o.data.vertices]
        faces=[list(f.vertices) for f in o.data.polygons];tree=BVHTree.FromPolygons(verts,faces)
        deviations=[]
        # 3 radial tracks, 143 points each; inspect actual exported collision surface.
        for r in (12.05,14,15.95):
            for i in range(1,144):
                t=i/144;a=-math.pi/2+1.5*math.pi*t
                hit,normal,face,distance=tree.ray_cast(Vector((r*math.cos(a),r*math.sin(a),12)),Vector((0,0,-1)),20)
                assert hit is not None,('surface hole',r,t)
                deviations.append(abs(hit.z-(.622+7*t)))
        assert max(deviations)<.006,max(deviations)
        rec['spiral_surface_samples']=len(deviations);rec['max_height_deviation_m']=max(deviations)
        rec['slope_degrees']=math.degrees(math.atan2(7,14*1.5*math.pi))
    reports.append(rec)
    bpy.context.window.scene=kit['STATE']['scenes'][0]
    for o in list(scene.objects):bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(scene)
(kit['BASE']/'collision-validation.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
result={'collision_roundtrips':len(reports),'spiral':reports[-1]}
