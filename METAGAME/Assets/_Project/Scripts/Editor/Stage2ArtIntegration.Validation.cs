using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay.Stage2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static partial class Stage2ArtIntegration
    {
        // Blockout labels float through equipment and read backwards from the arrival route.
        // Interaction prompts remain provided by Stage2Target and the gameplay HUD.
        static void HideDraftLabels(Scene scene)
        {
            foreach(var label in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))
                label.GetComponent<Renderer>().enabled=false;
        }
        static void AlignBoltTargets(Scene scene)
        {
            var yard=scene.GetRootGameObjects().Select(g=>g.GetComponent<Stage2Yard>()).First(y=>y!=null);
            foreach(var target in yard.GetComponentsInChildren<Stage2Target>(true).Where(t=>t.Kind==YardTargetKind.Bolt))
            {
                var pivot=Find(target.transform.parent,"Bolt_"+(target.Index%3+1));
                var renderer=pivot.GetComponentInChildren<Renderer>();
                target.transform.position=renderer.bounds.center;
            }
        }
        public static void FinalizePresentation()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            HideDraftLabels(scene);
            AlignBoltTargets(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed");
            Validate();Render();
        }
        [Serializable] sealed class Report
        {public bool passed;public int interactions,bolts,modules,missingScripts,visibleMeshes,vegetationMeshes;public string[] errors;public string unityVersion;}
        public static void Validate()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.IsValid())scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var roots=scene.GetRootGameObjects();var yard=roots.Select(g=>g.GetComponent<Stage2Yard>()).First(y=>y!=null);
            var targets=yard.GetComponentsInChildren<Stage2Target>(true);var errors=new System.Collections.Generic.List<string>();
            int missing=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if(missing>0)errors.Add("Missing scripts");
            if(targets.Length!=26||targets.Select(t=>t.Id).Distinct().Count()!=26)errors.Add("Interaction IDs changed");
            if(targets.Count(t=>t.Kind==YardTargetKind.Bolt)!=9)errors.Add("Bolt interaction count");
            var serialized=new SerializedObject(yard);
            foreach(string name in new[]{"_cranePivot","_arm","_hook","_seat","_lift","_ladder","_launch","_shower"})
                if(serialized.FindProperty(name).objectReferenceValue==null)errors.Add("Missing yard reference "+name);
            foreach(string name in new[]{"_parts","_sockets","_lids","_gasZones","_targets"})
            {var arr=serialized.FindProperty(name);for(int i=0;i<arr.arraySize;i++)if(arr.GetArrayElementAtIndex(i).objectReferenceValue==null)errors.Add("Missing array reference "+name);}
            var renderers=roots.SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled).ToArray();
            if(renderers.Any(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader")))errors.Add("Missing materials or shaders");
            var art=roots.FirstOrDefault(g=>g.name==ArtRoot);if(art==null)errors.Add("Missing art root");
            var plants=art!=null?art.transform.Find("07 Earth vegetation - edge clusters"):null;
            if(plants==null||plants.GetComponentsInChildren<MeshRenderer>().Length<500)errors.Add("Vegetation density below authored target");
            if(plants!=null&&plants.GetComponentsInChildren<Collider>(true).Length>0)errors.Add("Vegetation has blocking colliders");
            var bodies=serialized.FindProperty("_parts");var pivot=(Transform)serialized.FindProperty("_cranePivot").objectReferenceValue;
            for(int i=0;i<bodies.arraySize;i++)
            {
                var body=(Rigidbody)bodies.GetArrayElementAtIndex(i).objectReferenceValue;
                float radius=Vector3.ProjectOnPlane(body.position-pivot.position,Vector3.up).magnitude;
                if(radius<Stage2Rules.RadiusMin||radius>Stage2Rules.RadiusMax)errors.Add("Unreachable container "+i);
                if(body.GetComponentsInChildren<Renderer>().Count(r=>r.enabled)<4)errors.Add("Missing rocket art "+i);
            }
            var report=new Report{passed=errors.Count==0,interactions=targets.Length,bolts=targets.Count(t=>t.Kind==YardTargetKind.Bolt),modules=bodies.arraySize,
                missingScripts=missing,visibleMeshes=renderers.Length,vegetationMeshes=plants!=null?plants.GetComponentsInChildren<MeshRenderer>().Length:0,errors=errors.ToArray(),unityVersion=Application.unityVersion};
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/scene-validation.json",JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException(string.Join("; ",errors));
            Debug.Log("[Stage2Art] Scene validation passed: 26 interactions, 9 bolts, 3 independent rocket modules.");
        }
        public static void Render()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.IsValid())scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var previous=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory(Output);
            var go=new GameObject("Stage2 temporary review camera");var camera=go.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.Skybox;camera.nearClipPlane=.15f;camera.farClipPlane=350;camera.fieldOfView=54;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;
            var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.Create();
            var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                void Shot(string name,Vector3 position,Vector3 target)
                {
                    camera.transform.position=position;camera.transform.LookAt(target);camera.targetTexture=rt;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
                    var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
                    File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
                Shot("overview",new Vector3(69,58,-76),new Vector3(0,3,5));
                Shot("arrival",new Vector3(-29,2,-29),new Vector3(-8,5,8));
                Shot("assembly",new Vector3(24,3,-12),new Vector3(0,6,12));
                Shot("ruins-and-vegetation",new Vector3(-37,2,17),new Vector3(-19,3,33));
                Shot("crane-and-containers",new Vector3(-21,11,-2),new Vector3(0,7,20));
            }
            finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=old;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);SceneManager.SetActiveScene(previous);}
        }
    }
}
