using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Copy into an isolated Unity project's Editor folder; never opens the user's stage.
public static class ValidateMagneticStormWake
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch validation only");
        string output = Path.GetFullPath("../../artifacts/storm-wake");
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        ShaderUtil.allowAsyncCompilation = false;
        var owner = new GameObject("Preview owner");
        var root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(MagneticStormVisual.ResourcePath), owner.transform);
        var visual = root.GetComponent<MagneticStormVisual>();
        visual.SetExternallyDriven();
        var settings = new AtmosphereProfile.DustStormSettings { enabled = true };
        var schedule = new DustStormSchedule(settings);
        var bounds = new Bounds(new Vector3(0, 5, 0), new Vector3(80, 10, 80));
        var pose = schedule.Evaluate(16, bounds);
        root.transform.SetPositionAndRotation(pose.Center, pose.Rotation);
        visual.SetExtents(pose.HalfExtents);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -.2f, 0);
        ground.transform.localScale = new Vector3(180, .2f, 180);
        var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        groundMaterial.SetColor("_BaseColor", new Color(.11f,.12f,.16f));
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        RenderSettings.ambientLight = new Color(.3f,.3f,.4f);
        var sun = new GameObject("Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.3f;
        sun.transform.rotation = Quaternion.Euler(45, -25, 0);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f,.04f,.07f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 250;
        camera.GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.On;
        var rt = new RenderTexture(960, 540, 24);
        rt.Create();
        camera.targetTexture = rt;
        var image = new Texture2D(960,540,TextureFormat.RGB24,false);
        void Frame(Vector3 center)
        {
            camera.transform.position = center + pose.Rotation * new Vector3(30, 16, 22);
            camera.transform.LookAt(center);
        }
        void Capture(string name)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = rt });
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0,0,960,540),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"), image.EncodeToPNG());
        }
        visual.Evaluate(16);
        visual.EvaluateWake(0, schedule.EvaluateWake(16,bounds),16,settings.wakeOpacity);
        Frame(pose.Center - pose.Direction * 8);
        Capture("billow-and-wake");
        camera.orthographic = true;
        camera.orthographicSize = 19;
        camera.transform.position = pose.Center - pose.Direction * 7 + Vector3.up * 55;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, pose.Direction);
        Capture("travel-up-wake-down");
        camera.orthographic = false;
        Frame(pose.Center - pose.Direction * 8);
        var so = new SerializedObject(visual);
        so.FindProperty("frontBillowStrength").floatValue = 0;
        so.FindProperty("rearBillowStrength").floatValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        visual.Evaluate(16);
        visual.EvaluateWake(0, schedule.EvaluateWake(16,bounds),16,settings.wakeOpacity);
        Capture("zero-billow");
        double end = 11d + (new Vector2(80,80).magnitude + 17f) / 6f;
        var tail = schedule.EvaluateWake(end+1,bounds,1);
        visual.SetBodyVisible(false);
        visual.EvaluateWake(0,tail,end+1,settings.wakeOpacity);
        Frame(tail.Pose.Center);
        Capture("residual-only");
        var shader = Shader.Find("Metagame/Atmosphere/MagneticStormVolume");
        if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Storm shader compilation failed");
        File.WriteAllText(Path.Combine(output,"render-validation.txt"), "PASS: URP render; shader error check; main+wake, zero billow and residual-only captures.");
        RenderTexture.active = null;
        camera.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(groundMaterial);
    }
}
