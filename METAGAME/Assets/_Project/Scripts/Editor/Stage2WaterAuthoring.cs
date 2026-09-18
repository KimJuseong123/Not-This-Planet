using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage2WaterAuthoring
    {
        const string Folder="Assets/_Project/Art/Stage2Earth";
        const string Root="Stage2 flowing drainage";
        public static readonly string Output=Path.GetFullPath("../artifacts/stage2-water");
        [MenuItem("Not This Planet/Scenes/Add Stage2 Drainage Water")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit play mode first");
            var loaded=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Stage2SceneBuilder.ScenePath);
            if(loaded.IsValid()&&loaded.isDirty)throw new InvalidOperationException("Save scene edits first");
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            if(scene.GetRootGameObjects().Any(g=>g.name==Root))throw new InvalidOperationException("Drainage already authored; edit existing assets");
            var terrain=Object.FindFirstObjectByType<Terrain>();var data=terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data,"Carve Stage2 drainage");
            int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                float wx=terrain.transform.position.x+x*data.size.x/(n-1),wz=terrain.transform.position.z+z*data.size.z/(n-1);
                float dx=Mathf.Abs(wx-33),dz=Mathf.Abs(wz);
                if(dx>4.5f||dz>32)continue;
                float bed=Mathf.Lerp(-2.5f,-.27f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.3f,2.7f,dx)));
                bed=Mathf.Lerp(bed,-.27f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(20,28,dz)));
                float edge=Mathf.Max(Mathf.InverseLerp(2.9f,4.5f,dx),Mathf.InverseLerp(28,32,dz));
                float original=terrain.transform.position.y+heights[z,x]*data.size.y;
                heights[z,x]=(Mathf.Lerp(bed,original,Mathf.SmoothStep(0,1,edge))-terrain.transform.position.y)/data.size.y;
            }
            data.SetHeights(0,0,heights);terrain.Flush();EditorUtility.SetDirty(data);
            var root=new GameObject(Root);Undo.RegisterCreatedObjectUndo(root,"Add drainage");
            var surface=new GameObject("Flowing water - shallow banks and deep channel");surface.transform.SetParent(root.transform);
            surface.transform.position=new Vector3(33,-.12f,0);
            var mesh=new Mesh{name="Drainage surface including shoreline overlap"};const int nx=36,nz=256;
            var vertices=new Vector3[(nx+1)*(nz+1)];var triangles=new int[nx*nz*6];int t=0;
            for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)vertices[z*(nx+1)+x]=new Vector3(x*9f/nx-4.5f,0,z*64f/nz-32);
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
            {
                int a=z*(nx+1)+x,b=a+nx+1;
                triangles[t++]=a;triangles[t++]=b;triangles[t++]=a+1;triangles[t++]=a+1;triangles[t++]=b;triangles[t++]=b+1;
            }
            mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Folder+"/DrainageSurface.asset");
            surface.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("Metagame/Drainage Water")){name="Stage2 flowing drainage water"};
            AssetDatabase.CreateAsset(material,Folder+"/DrainageWater.mat");
            var renderer=surface.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            surface.AddComponent<DrainageWater>();surface.AddComponent<DrainageWaterVisual>();
            var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Models/Stage2/Materials/M_S2_Steel.mat");
            if(steel==null)steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Stage2/M_Stage2_Steel.mat");
            void Box(string name,Vector3 p,Vector3 scale)
            {
                var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root.transform);g.transform.position=p;g.transform.localScale=scale;
                g.GetComponent<Renderer>().sharedMaterial=steel;
            }
            foreach(float z in new[]{-16f,10f})
            {
                Box("Drainage crossing deck",new Vector3(33,.08f,z),new Vector3(9,.16f,2.3f));
                foreach(float dz in new[]{-1.1f,1.1f})
                {
                    Box("Crossing handrail",new Vector3(33,.83f,z+dz),new Vector3(8.7f,.055f,.055f));
                    for(int x=0;x<6;x++)Box("Crossing post",new Vector3(29+x*1.6f,.46f,z+dz),new Vector3(.07f,.78f,.07f));
                }
            }
            // Previously solid drain models would hide the flowing channel. Preserve them as disabled authored objects.
            foreach(var tr in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>()).ToArray())
            {
                if(tr.name=="Art Drain")tr.gameObject.SetActive(false);
                if(tr.name.StartsWith("Art ")&&(tr.name.Contains("Grass")||tr.name.Contains("Reeds")||tr.name.Contains("Shrub")))
                    if(Mathf.Abs(tr.position.x-33)<3.8f&&Mathf.Abs(tr.position.z)<31)tr.gameObject.SetActive(false);
            }
            var probeObject=new GameObject("Drainage environment reflection");probeObject.transform.SetParent(root.transform);probeObject.transform.position=new Vector3(33,2,0);
            var probe=probeObject.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;probe.resolution=128;probe.size=new Vector3(18,16,70);probe.boxProjection=true;
            probe.farClipPlane=150;probe.cullingMask=~0;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            RefineBanks();
        }
        public static void RefineBanks()
        {
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            var terrain=Object.FindFirstObjectByType<Terrain>();
            var water=Object.FindFirstObjectByType<DrainageWater>();
            water.gameObject.layer=4; // Built-in Water layer, excluded from its environment capture.
            var settings=new SerializedObject(water);settings.FindProperty("_size").vector2Value=new Vector2(9,64);settings.ApplyModifiedPropertiesWithoutUndo();
            var mesh=water.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var size=mesh.bounds.size;
            for(int i=0;i<vertices.Length;i++){vertices[i].x*=9/size.x;vertices[i].z*=64/size.z;}
            mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var probe=Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).FirstOrDefault(p=>p.name=="Drainage environment reflection");
            if(probe!=null)probe.cullingMask=~(1<<4);
            foreach(var tr in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray())
            {
                if(!tr.name.StartsWith("Art ")||Mathf.Abs(tr.position.z)>32)continue;
                var rs=tr.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)continue;
                var bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);
                if(bounds.max.x<29||bounds.min.x>37)continue;
                bool plant=tr.parent.name.StartsWith("Cluster ")||tr.name=="Art Reeds";
                if(plant)
                {
                    var p=tr.position;p.x=p.x<33?28.4f-bounds.extents.x:37.6f+bounds.extents.x;
                    foreach(float bridge in new[]{-16f,10f})if(Mathf.Abs(p.z-bridge)<2)p.z=bridge+(p.z<bridge?-3:3);
                    tr.position=p;
                    tr.position+=Vector3.up*(terrain.SampleHeight(p)+terrain.transform.position.y-bounds.min.y);
                    tr.gameObject.SetActive(true);
                }
                else if(tr.name=="Art Facade"&&tr.parent.name.StartsWith("05 Factory"))
                    tr.position+=Vector3.left*(bounds.max.x-28);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);BakeReflection();
        }
        public static void BakeReflection()
        {
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            foreach(var stale in scene.GetRootGameObjects().Where(g=>g.name=="Temporary drainage cubemap camera"))Object.DestroyImmediate(stale);
            var water=Object.FindFirstObjectByType<DrainageWater>();
            var go=new GameObject("Temporary drainage cubemap camera");var camera=go.AddComponent<Camera>();
            go.hideFlags=HideFlags.HideAndDontSave;
            camera.enabled=false;camera.transform.position=new Vector3(33,1,0);camera.clearFlags=CameraClearFlags.Skybox;
            camera.cullingMask=~(1<<4);camera.farClipPlane=200;camera.nearClipPlane=.1f;camera.allowHDR=true;
            camera.fieldOfView=90;camera.aspect=1;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var cube=new Cubemap(256,TextureFormat.RGBAHalf,true){name="Stage2 drainage environment",wrapMode=TextureWrapMode.Clamp};
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGBHalf);target.Create();
            var oldTarget=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                // Explicit GPU readback keeps the saved asset valid after the editor closes.
                var pixels=new Texture2D(256,256,TextureFormat.RGBAHalf,false,true);
                try
                {
                    var directions=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
                    for(int face=0;face<6;face++)
                    {
                        camera.transform.rotation=Quaternion.LookRotation(directions[face],face==2?Vector3.back:face==3?Vector3.forward:Vector3.up);
                        camera.targetTexture=target;
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                        pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();cube.SetPixels(pixels.GetPixels(),(CubemapFace)face);
                    }
                    cube.Apply(true);
                    if(cube.GetPixels(CubemapFace.PositiveY).Average(c=>c.r+c.g+c.b)<.01f)throw new InvalidOperationException("Captured environment is black");
                }
                finally{Object.DestroyImmediate(pixels);RenderTexture.active=oldTarget;}
                string path=Folder+"/DrainageReflection.asset";var existing=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if(existing==null)AssetDatabase.CreateAsset(cube,path);
                else {EditorUtility.CopySerialized(cube,existing);Object.DestroyImmediate(cube);cube=existing;EditorUtility.SetDirty(cube);}
                var material=water.GetComponent<Renderer>().sharedMaterial;material.SetTexture("_Environment",cube);EditorUtility.SetDirty(material);
                var probe=Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).FirstOrDefault(p=>p.name=="Drainage environment reflection");
                if(probe!=null)Object.DestroyImmediate(probe.gameObject);
            }
            finally{ShaderUtil.allowAsyncCompilation=async;camera.targetTexture=null;RenderTexture.active=oldTarget;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(go);}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Render();
        }
        public static void Render()
        {
            if(!UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Stage2SceneBuilder.ScenePath).IsValid())EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Directory.CreateDirectory(Output);
            var go=new GameObject("Water review camera");var camera=go.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.Skybox;
            camera.nearClipPlane=.04f;camera.farClipPlane=300;camera.fieldOfView=63;
            var extra=camera.GetUniversalAdditionalCameraData();extra.requiresColorOption=CameraOverrideOption.On;extra.requiresDepthOption=CameraOverrideOption.On;
            var rt=new RenderTexture(1600,1000,24);rt.Create();var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                void Shot(string name,Vector3 p,Vector3 target)
                {
                    camera.transform.position=p;camera.transform.LookAt(target);camera.targetTexture=rt;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
                    var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                    File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
                Shot("01-drainage-overview",new Vector3(49,16,-33),new Vector3(32,-.3f,1));
                Shot("02-water-surface",new Vector3(30,.65f,-23),new Vector3(33,-.2f,-12));
                Shot("03-shallow-bank",new Vector3(28.8f,1.2f,0),new Vector3(33,-.3f,3));
            }
            finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
            var shader=Shader.Find("Metagame/Drainage Water");
            foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new InvalidOperationException(message.message);
        }
    }
}
