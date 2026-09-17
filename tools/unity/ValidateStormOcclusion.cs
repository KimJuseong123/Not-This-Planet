using System;
using System.IO;
using System.Collections.Generic;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ValidateStormOcclusion
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch only");
        string output = Path.GetFullPath("../../artifacts/storm-occlusion");
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        ShaderUtil.allowAsyncCompilation = false;
        var root = Object.Instantiate(Resources.Load<GameObject>(MagneticStormVisual.ResourcePath));
        var visual = root.GetComponent<MagneticStormVisual>();
        visual.SetExternallyDriven(); visual.Evaluate(1.5);
        var camera = new GameObject("Occlusion Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0,0,-30);
        camera.transform.rotation = Quaternion.identity;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f,.04f,.06f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.On;
        var rt = new RenderTexture(480,270,24); rt.Create();
        camera.targetTexture = rt;
        var texture = new Texture2D(480,270,TextureFormat.RGB24,false);
        Color32[] Capture(string name)
        {
            // Warm the pipeline, then capture the stable frame.
            for (int i=0;i<2;i++) RenderPipeline.SubmitRenderRequest(camera,
                new RenderPipeline.StandardRequest { destination = rt });
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0,0,480,270),0,0); texture.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        int Changed(Color32[] a,Color32[] b)
        {
            int count=0;
            for(int i=0;i<a.Length;i++)
                if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>6) count++;
            return count;
        }
        root.SetActive(false); var clear=Capture("clear");
        root.SetActive(true); var visible=Capture("unoccluded");
        var report=new List<string> { "Unoccluded storm pixels="+Changed(clear,visible) };
        var failures=new List<string>();
        if(Changed(clear,visible)==0) failures.Add("Unoccluded storm is invisible");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(wall.GetComponent<Collider>());
        wall.transform.position=new Vector3(0,0,-12);
        wall.transform.localScale=new Vector3(80,60,1);
        foreach(string shaderName in new[]{"Universal Render Pipeline/Lit","NotThisPlanet/Flat","NotThisPlanet/Dough","NotThisPlanet/CharacterSuitTint","NotThisPlanet/PlanetNoFog","Metagame/Guidance/NoFog","NotThisPlanet/CockpitNoFog"})
        {
            var shader=Shader.Find(shaderName);
            if(shader==null) throw new Exception("Missing shader "+shaderName);
            var material=new Material(shader);
            material.SetColor(material.HasProperty("_BaseColor")?"_BaseColor":"_Color",new Color(.2f,.3f,.25f));
            wall.GetComponent<Renderer>().sharedMaterial=material;
            string name=shaderName.Substring(shaderName.LastIndexOf('/')+1);
            wall.transform.position=new Vector3(0,0,-12);
            root.SetActive(false); var before=Capture(name+"-wall-only");
            root.SetActive(true); var after=Capture(name+"-storm-behind");
            int leaked=Changed(before,after);
            report.Add(name+" colliderless wall leaked pixels="+leaked);
            if(leaked!=0) failures.Add(name+" leaks behind opaque wall");
            foreach(float wallZ in new[]{0f,12f})
            {
                wall.transform.position=new Vector3(0,0,wallZ);
                string scenario=wallZ==0?"intersecting":"storm-in-front";
                root.SetActive(false); before=Capture(name+"-"+scenario+"-wall");
                root.SetActive(true); after=Capture(name+"-"+scenario);
                int pixels=Changed(before,after);
                report.Add(name+" "+scenario+" visible pixels="+pixels);
                if(pixels==0) failures.Add(name+" hides smoke in front of wall");
            }
            if(ShaderUtil.ShaderHasError(shader)) failures.Add(name+" shader error");
            Object.DestroyImmediate(material);
        }
        wall.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("NotThisPlanet/Flat"));
        wall.transform.position=new Vector3(0,0,-12);
        visual.Evaluate(1.5,0);
        var wake=new StormWake(new StormPose(true,Vector3.zero,Vector3.forward,new Vector3(12,2.5f,5),0),0,2,4);
        visual.HideWakes(); var noWake=Capture("wake-wall-only");
        visual.EvaluateWake(0,wake,1.5,.7f); var hiddenWake=Capture("wake-behind-wall");
        int wakeLeak=Changed(noWake,hiddenWake);
        report.Add("Wake colliderless wall leaked pixels="+wakeLeak);
        if(wakeLeak!=0) failures.Add("Wake leaks behind wall");
        wall.SetActive(false);
        visual.HideWakes(); noWake=Capture("wake-clear");
        visual.EvaluateWake(0,wake,1.5,.7f); var visibleWake=Capture("wake-unoccluded");
        int wakePixels=Changed(noWake,visibleWake);
        report.Add("Unoccluded wake pixels="+wakePixels);
        if(wakePixels==0) failures.Add("Wake invisible without wall");
        File.WriteAllLines(Path.Combine(output,"results.txt"),report);
        if(failures.Count>0) throw new Exception(string.Join("; ",failures));
        if(ShaderUtil.ShaderHasError(Shader.Find("Metagame/Atmosphere/MagneticStormVolume")))
            throw new Exception("Storm shader error");
        RenderTexture.active=null; camera.targetTexture=null;
        rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture);
    }
}

