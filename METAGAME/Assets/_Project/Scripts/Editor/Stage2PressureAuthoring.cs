using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay.Stage2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage2PressureAuthoring
    {
        const string Model="Assets/_Project/Models/Stage2/S2_SurgePump.fbx";
        const string Folder="Assets/_Project/Art/Stage2Earth/PressureSurge";
        const string Root="Stage2 drainage pressure surge";
        public static void Apply()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==Root);
            if(old!=null)Object.DestroyImmediate(old);
            var yard=Object.FindFirstObjectByType<Stage2Yard>();
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if(model==null)throw new InvalidOperationException("SurgePump FBX missing");
            var importer=(ModelImporter)AssetImporter.GetAtPath(Model);importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            foreach(var key in new[]{"Teal","Ivory","Orange","Steel","Dark","Lamp","Yellow"})
            {
                var material=Mat("S2Surge_"+key,"Universal Render Pipeline/Lit");
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Models/Stage2/S2_SurgePump.fbm/S2Surge_"+key+"_BaseColor.png");
                if(texture==null)throw new InvalidOperationException("FBM texture missing: "+key);
                material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.4f);material.SetFloat("_Metallic",key=="Dark"?0:.35f);
                if(key=="Lamp"){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(1,.3f,.015f)*1.5f);}
                EditorUtility.SetDirty(material);importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),"S2Surge_"+key),material);
            }
            importer.SaveAndReimport();model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var water=Mat("Pressure jet water","Metagame/Pressure Spray");
            water.SetColor("_BaseColor",new Color(.55f,.86f,.95f,.48f));water.SetFloat("_Surface",1);water.SetFloat("_Blend",0);
            water.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);water.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);water.SetFloat("_ZWrite",0);
            water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");water.renderQueue=3000;EditorUtility.SetDirty(water);
            var amber=Mat("Pressure warning","Universal Render Pipeline/Unlit");amber.SetColor("_BaseColor",new Color(1,.5f,.025f));EditorUtility.SetDirty(amber);
            var root=new GameObject(Root);var pumps=new Stage2PressurePump[3];
            var positions=new[]{new Vector3(2,.04f,-5),new Vector3(20,.04f,3),new Vector3(25,.04f,12)};
            var directions=new[]{Vector3.right,Vector3.forward,Vector3.left};
            for(int i=0;i<3;i++)
            {
                var go=new GameObject("Pressure pump "+(i+1));go.transform.SetParent(root.transform);go.transform.position=positions[i];
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(model,go.transform);
                Transform Find(string n)=>visual.GetComponentsInChildren<Transform>().First(t=>t.name==n);
                // Derive the exported forward axis from the actual nozzle offset, not an assumed FBX convention.
                var outlet=Find("JetOrigin");var forward=visual.transform.TransformDirection(Vector3.forward);
                forward=Vector3.ProjectOnPlane(outlet.position-Find("WarningBeacon").position,Vector3.up).normalized;
                go.transform.rotation=Quaternion.FromToRotation(forward,directions[i]);
                var pump=go.AddComponent<Stage2PressurePump>();pumps[i]=pump;pump.Yard=yard;pump.Index=i;
                pump.Wheel=Find("ReliefWheel");pump.Needle=Find("GaugeNeedle");pump.Flap=Find("OutletFlap");
                var nozzle=new GameObject("Water jet origin").transform;nozzle.SetParent(go.transform);nozzle.position=outlet.position;nozzle.rotation=Quaternion.LookRotation(directions[i]);pump.Nozzle=nozzle;
                var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,1.1f,0);collider.size=new Vector3(2.4f,2.2f,1f);
                var target=new GameObject("Manual pressure relief E");target.transform.SetParent(go.transform);
                target.transform.position=pump.Wheel.position-directions[i]*.05f+Vector3.Cross(Vector3.up,directions[i])*.05f;
                // Put the reachable interaction point just outside the front collision slab, off the jet axis.
                target.transform.position=pump.Wheel.position+directions[i]*.45f;
                target.AddComponent<Stage2Target>().Configure(100+i,YardTargetKind.PressureRelief,i,"감압 밸브 — 45초 분출 억제");
                var beacon=new GameObject("Pulsing amber warning");beacon.transform.SetParent(go.transform);beacon.transform.position=Find("WarningBeacon").position+Vector3.up*.15f;
                pump.Beacon=beacon.AddComponent<Light>();pump.Beacon.color=new Color(1,.4f,.025f);pump.Beacon.range=4;pump.Beacon.intensity=3;pump.Beacon.enabled=false;
                var particles=new GameObject("Water spray");particles.transform.SetParent(nozzle,false);pump.Jet=particles.AddComponent<ParticleSystem>();pump.Jet.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=pump.Jet.main;main.playOnAwake=false;main.loop=true;main.startLifetime=.67f;main.startSpeed=12;main.startSize=new ParticleSystem.MinMaxCurve(.06f,.16f);main.maxParticles=600;main.simulationSpace=ParticleSystemSimulationSpace.World;main.gravityModifier=.08f;
                main.startColor=new ParticleSystem.MinMaxGradient(new Color(.65f,.88f,1,.25f),new Color(.9f,.98f,1,.65f));
                var emission=pump.Jet.emission;emission.rateOverTime=550;
                var shape=pump.Jet.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=3;shape.radius=.13f;
                var collision=pump.Jet.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.World;collision.mode=ParticleSystemCollisionMode.Collision3D;collision.lifetimeLoss=1;collision.collidesWith=~0;collision.quality=ParticleSystemCollisionQuality.Medium;
                var render=particles.GetComponent<ParticleSystemRenderer>();render.sharedMaterial=water;render.renderMode=ParticleSystemRenderMode.Stretch;render.lengthScale=2;render.velocityScale=.05f;render.shadowCastingMode=ShadowCastingMode.Off;
                var warning=new GameObject("Marked discharge lane");warning.transform.SetParent(go.transform);pump.Warning=warning.AddComponent<LineRenderer>();pump.Warning.sharedMaterial=amber;
                pump.Warning.positionCount=2;pump.Warning.startWidth=.11f;pump.Warning.endWidth=.6f;
                var a=nozzle.position;a.y=.08f;pump.Warning.SetPosition(0,a);pump.Warning.SetPosition(1,a+directions[i]*Stage2PressurePump.Range);pump.Warning.enabled=false;
            }
            var so=new SerializedObject(yard);var ps=so.FindProperty("_surgePumps");ps.arraySize=3;
            for(int i=0;i<3;i++)ps.GetArrayElementAtIndex(i).objectReferenceValue=pumps[i];
            var targets=Object.FindObjectsByType<Stage2Target>(FindObjectsSortMode.None).OrderBy(t=>t.Id).ToArray();
            var ts=so.FindProperty("_targets");ts.arraySize=targets.Length;for(int i=0;i<targets.Length;i++)ts.GetArrayElementAtIndex(i).objectReferenceValue=targets[i];so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Stage2ArtIntegration.Validate();Render();
            Debug.Log("[PressureSurge] Three textured FBX pumps authored, 29 targets, server-owned hazard.");
        }
        static Material Mat(string name,string shader)
        {
            string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(shader)){name=name};AssetDatabase.CreateAsset(m,path);}else m.shader=Shader.Find(shader);return m;
        }
        public static void Render()
        {
            var folder=Path.GetFullPath("../artifacts/stage2-pressure");Directory.CreateDirectory(folder);
            var go=new GameObject("Temporary pressure review camera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.enabled=false;camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=55;camera.farClipPlane=250;
            camera.GetUniversalAdditionalCameraData().requiresDepthOption=CameraOverrideOption.On;camera.GetUniversalAdditionalCameraData().requiresColorOption=CameraOverrideOption.On;
            var rt=new RenderTexture(1600,1000,24);rt.Create();var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                foreach(var pump in Object.FindObjectsByType<Stage2PressurePump>(FindObjectsSortMode.None))
                {
                    var p=pump.transform.position;camera.transform.position=p+pump.Direction*5+Vector3.Cross(Vector3.up,pump.Direction)*3+Vector3.up*2.7f;camera.transform.LookAt(p+Vector3.up);camera.targetTexture=rt;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;
                    var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(folder+"/pump-"+pump.Index+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
            }
            finally{ShaderUtil.allowAsyncCompilation=async;camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
        }
    }
}
