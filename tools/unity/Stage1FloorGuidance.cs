using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Metagame.Gameplay.Parts;
using Object = UnityEngine.Object;

public static class Stage1FloorGuidance
{
    const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
    static string Output => Path.GetFullPath("../../floor-guidance-integration");
    static Transform[] All() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
    static void Open()
    {
        if (!Application.isBatchMode || !File.Exists("../ISOLATED_PART2_TRAIL")) throw new Exception("Isolated validation project required");
        SessionState.SetBool("Metagame.URPSetupApplied", true);
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(ScenePath);
    }
    static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
    [Serializable] public class Entry
    {
        public string path, name, material, id;
        public Vector3 position, scale, size;
        public Color color;
    }
    [Serializable] public class SurveyReport { public List<Entry> entries = new List<Entry>(); }
    public static void Survey()
    {
        Open(); var report = new SurveyReport();
        foreach (var t in All())
        {
            var r = t.GetComponent<Renderer>();
            if (!r || !(t.name.ToLowerInvariant().Contains("light") || t.name.ToLowerInvariant().Contains("lamp") || t.name.ToLowerInvariant().Contains("route"))) continue;
            report.entries.Add(new Entry {path=Hierarchy(t),name=t.name,position=t.position,scale=t.lossyScale,size=r.bounds.size,
                material=r.sharedMaterial ? AssetDatabase.GetAssetPath(r.sharedMaterial) : "",color=r.sharedMaterial ? r.sharedMaterial.color : Color.clear,
                id=GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).targetObjectId.ToString()});
        }
        File.WriteAllText(Path.Combine(Output,"survey.json"),JsonUtility.ToJson(report,true));
        Debug.Log("FLOOR_GUIDANCE_SURVEY_PASS " + report.entries.Count);
    }

    const string Model = "Assets/_Project/Models/FloorGuidance/01-floor-light/";
    const string Materials = "Assets/_Project/Materials/FloorGuidance";
    const string ProfilePath = Materials + "/Stage1_FloorGuidanceVolume.asset";
    const string Prefix = "Astra Floor Guidance - ";
    [Serializable] public class Replacement
    {
        public string source, sourceMaterial, newName;
        public Vector3 position, scale;
        public Color color;
        public float groundAdjustment;
    }
    [Serializable] public class Report
    {
        public string status;
        public int replaced, dynamicFixtures, remainingOldObjects, missingScripts;
        public List<string> changedIds = new List<string>();
        public List<Replacement> replacements = new List<Replacement>();
    }
    static Report report;
    static bool Target(Transform t) => t.name == "Runway color" || t.name == "Pickup / departure marker" || t.name.Contains(" · light strip ");
    static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Touch(Object o) => report.changedIds.Add(GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId.ToString());
    static Material Lens(Material source)
    {
        string path = Materials + "/M_FloorGuidance_" + source.name.Replace(' ', '_') + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!result)
        {
            result = new Material(AssetDatabase.LoadAssetAtPath<Material>(Model + "M_Astra_FloorLight_Lens.mat"));
            result.name = Path.GetFileNameWithoutExtension(path);
            var color = source.GetColor("_BaseColor");
            result.SetColor("_BaseColor", color);
            result.SetColor("_Color", color);
            var emission = color.linear; emission *= 2.4f / emission.maxColorComponent; emission.a = 1;
            result.SetColor("_EmissionColor", emission);
            result.SetFloat("_Smoothness", .25f);
            result.SetFloat("_SpecularHighlights",0);result.SetFloat("_EnvironmentReflections",0);
            result.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");result.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            result.EnableKeyword("_EMISSION");
            result.enableInstancing = true;
            result.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            AssetDatabase.CreateAsset(result, path);
        }
        return result;
    }
    public static void Apply()
    {
        Open(); report = new Report();
        Need(!All().Any(t => t.name.StartsWith(Prefix)), "Already integrated");
        if (!AssetDatabase.IsValidFolder(Materials)) AssetDatabase.CreateFolder("Assets/_Project/Materials", "FloorGuidance");
        var targets = All().Where(Target).ToArray();
        var housings = All().Where(t => t.name == "Runway housing").ToArray();
        Need(targets.Length == 138 && housings.Length == 94, "Unexpected source fixture inventory");
        var guide = All().Select(t => t.GetComponent<AssemblyGuidanceLight>()).Single(g => g);
        var settings = new SerializedObject(guide);
        var previousLamps = settings.FindProperty("lamps");
        var linked = Enumerable.Range(0,previousLamps.arraySize).Select(i => (Renderer)previousLamps.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        var retained = linked.Where(r => !Target(r.transform)).ToArray();
        var dynamicFixtures = new List<Renderer>();
        Render("before", false);
        foreach (var old in targets)
        {
            var renderer = old.GetComponent<Renderer>();
            var source = renderer.sharedMaterial;
            Transform housing = null;
            if (old.name == "Runway color")
            {
                housing = housings.Where(h => h && h.parent == old.parent)
                    .OrderBy(h => Vector2.Distance(new Vector2(h.position.x,h.position.z),new Vector2(old.position.x,old.position.z))).First();
                Need(Vector2.Distance(new Vector2(housing.position.x,housing.position.z),new Vector2(old.position.x,old.position.z)) < .02f, "Unpaired housing");
            }
            var reference = housing ? housing : old;
            var p = reference.position;
            p.y = reference.GetComponent<Renderer>().bounds.min.y;
            float originalY = p.y;
            // Fit to the nearest supporting surface without jumping onto overhead props.
            var hits = Physics.RaycastAll(p + Vector3.up * .3f, Vector3.down, .6f, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.gameObject.layer == LayerMask.NameToLayer("Ground") && Mathf.Abs(h.point.y - p.y) < .2f)
                .OrderBy(h => Mathf.Abs(h.point.y - p.y)).ToArray();
            if (hits.Length > 0) p.y = hits[0].point.y + .003f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model + "PF_Astra_FloorLight.prefab"));
            go.name = Prefix + targets.ToList().IndexOf(old).ToString("D3") + " - " + source.name;
            go.transform.SetParent(old.parent, true);
            go.transform.position = p;
            bool longZ = Mathf.Abs(reference.lossyScale.z) > Mathf.Abs(reference.lossyScale.x);
            go.transform.rotation = reference.rotation * Quaternion.Euler(0,longZ ? 90 : 0,0);
            if(hits.Length>0 && hits[0].normal.y>.8f)go.transform.rotation=Quaternion.FromToRotation(go.transform.up,hits[0].normal)*go.transform.rotation;
            float length = Mathf.Max(Mathf.Abs(reference.lossyScale.x),Mathf.Abs(reference.lossyScale.z));
            float width = Mathf.Min(Mathf.Abs(reference.lossyScale.x),Mathf.Abs(reference.lossyScale.z));
            go.transform.localScale = new Vector3(length/.352f,1,width/.12265f);
            // These were non-colliding visual markers; keep the route traversable.
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var newRenderer = go.GetComponentInChildren<Renderer>();
            var mats = newRenderer.sharedMaterials;
            Need(mats.Length == 2, "Expected chassis and lens slots");
            mats[1] = Lens(source); newRenderer.sharedMaterials = mats;
            newRenderer.shadowCastingMode = ShadowCastingMode.Off;
            if (linked.Contains(renderer)) dynamicFixtures.Add(newRenderer);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                foreach (var c in t.GetComponents<Component>()) if(c) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
            report.replacements.Add(new Replacement {source=Hierarchy(old),sourceMaterial=AssetDatabase.GetAssetPath(source),newName=go.name,position=p,scale=go.transform.lossyScale,color=source.color,groundAdjustment=p.y-originalY});
            Object.DestroyImmediate(old.gameObject);
            if (housing) Object.DestroyImmediate(housing.gameObject);
        }
        settings.Update(); previousLamps = settings.FindProperty("lamps"); previousLamps.arraySize=retained.Length;
        for(int i=0;i<retained.Length;i++)previousLamps.GetArrayElementAtIndex(i).objectReferenceValue=retained[i];
        settings.ApplyModifiedPropertiesWithoutUndo();
        var colors = settings.FindProperty("floorMaterials");
        guide.ConfigureFloorFixtures(dynamicFixtures.ToArray(),Enumerable.Range(0,colors.arraySize).Select(i=>Lens((Material)colors.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray(),Lens((Material)settings.FindProperty("completeMaterial").objectReferenceValue));
        EditorUtility.SetDirty(guide); Touch(guide);
        var volume = All().Select(t=>t.GetComponent<Volume>()).Single(v=>v && v.isGlobal);
        Need(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile),ProfilePath), "Cannot clone scene volume");
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        Need(volume.sharedProfile.TryGet<Bloom>(out var bloom), "Missing bloom");
        bloom.active=true; bloom.threshold.Override(.9f); bloom.intensity.Override(.8f); bloom.scatter.Override(.85f);
        bloom.tint.Override(Color.white); bloom.highQualityFiltering.Override(true);
        EditorUtility.SetDirty(bloom); EditorUtility.SetDirty(volume.sharedProfile); Touch(volume);
        foreach(var camera in All().Select(t=>t.GetComponent<Camera>()).Where(c=>c))
        {
            camera.allowHDR=true;
            var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true;
            Touch(camera); Touch(data);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        report.replaced=targets.Length; report.dynamicFixtures=dynamicFixtures.Count;
        ValidateState(); report.status="PASS";
        File.WriteAllText(Path.Combine(Output,"validation.json"),JsonUtility.ToJson(report,true));
        Render("after", true);
        Debug.Log("FLOOR_GUIDANCE_APPLY_PASS " + report.replaced);
    }
    static void ValidateState()
    {
        var all=All();
        Need(!all.Any(t=>Target(t)||t.name=="Runway housing"),"Old fixtures remain");
        var fixtures=all.Where(t=>t.name.StartsWith(Prefix)).ToArray();
        Need(fixtures.Length==138,"Expected 138 new fixtures");
        foreach(var t in fixtures)
        {
            var r=t.GetComponentInChildren<Renderer>(); var mats=r.sharedMaterials;
            Need(mats.Length==2 && mats.All(m=>m && !ShaderUtil.ShaderHasError(m.shader)),"Invalid materials");
            Need(mats[1].IsKeywordEnabled("_EMISSION") && mats[1].GetTexture("_EmissionMap") && mats[1].GetColor("_EmissionColor").maxColorComponent>1,"Emission missing");
            var sourceName=t.name.Substring(t.name.LastIndexOf(" - ",StringComparison.Ordinal)+3);
            var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Stage1Concept/Materials/"+sourceName+".mat");
            Need(source && Vector4.Distance(mats[1].GetColor("_BaseColor"),source.GetColor("_BaseColor"))<.0001f,"Source route color changed");
            Need(!t.GetComponentsInChildren<Collider>().Any(c=>c.enabled),"New collision obstacle");
            Need(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).EndsWith("PF_Astra_FloorLight.prefab"),"Missing prefab link");
        }
        Need(!all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0),"Missing script");
        var guide=all.Select(t=>t.GetComponent<AssemblyGuidanceLight>()).Single(g=>g);
        var so=new SerializedObject(guide);
        Need(so.FindProperty("_floorFixtures").arraySize==8,"Lost assembly fixture wiring");
        var volume=all.Select(t=>t.GetComponent<Volume>()).Single(v=>v && v.isGlobal);
        Need(AssetDatabase.GetAssetPath(volume.sharedProfile)==ProfilePath,"Scene-specific volume link lost");
        Need(volume.sharedProfile.TryGet<Bloom>(out var bloom) && bloom.active && bloom.intensity.value>.25f,"Bloom configuration lost");
        foreach(string field in new[]{"lamps","bollards","_floorFixtures"})
        {
            var a=so.FindProperty(field);
            for(int i=0;i<a.arraySize;i++)Need(a.GetArrayElementAtIndex(i).objectReferenceValue,"Missing signal reference");
        }
    }
    public static void Validate()
    {
        Open();ValidateState();
        var info=new System.Text.StringBuilder();
        info.AppendLine("Pipeline="+GraphicsSettings.currentRenderPipeline+" quality="+QualitySettings.GetQualityLevel()+" color="+QualitySettings.activeColorSpace);
        foreach(var v in All().Select(t=>t.GetComponent<Volume>()).Where(v=>v))info.AppendLine("Volume="+v.name+" enabled="+v.enabled+" layer="+v.gameObject.layer+" weight="+v.weight+" priority="+v.priority);
        foreach(var t in All().Where(t=>t.name.StartsWith(Prefix)).Take(6))
        {var r=t.GetComponentInChildren<Renderer>();info.AppendLine(t.name+" scale="+t.lossyScale+" pos="+t.position+" body="+r.sharedMaterials[0].name+" lens="+r.sharedMaterials[1].name+" color="+r.sharedMaterials[1].GetColor("_BaseColor")+" emission="+r.sharedMaterials[1].GetColor("_EmissionColor"));}
        File.WriteAllText(Path.Combine(Output,"diagnostics.txt"),info.ToString());
        File.WriteAllText(Path.Combine(Output,"reload-validation.txt"),"PASS: 138 prefabs, zero legacy floor fixtures, 8 assembly lens references, materials/shaders/emission/colliders validated after reload.\n");
        Debug.Log("FLOOR_GUIDANCE_RELOAD_PASS");
    }
    public static void Preview() { Validate(); Render("after",true); }
    public static void FinalSmoke() { Validate(); PlaySmoke(); }
    public static void PlaySmoke()
    {
        Open();ValidateState();
        SessionState.SetBool("FloorGuidanceSmoke",true);
        SessionState.SetFloat("FloorGuidanceSmokeTime",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update+=PollSmoke;EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] static void ResumeSmoke()
    {if(SessionState.GetBool("FloorGuidanceSmoke",false))EditorApplication.update+=PollSmoke;}
    static void PollSmoke()
    {
        float elapsed=(float)EditorApplication.timeSinceStartup-SessionState.GetFloat("FloorGuidanceSmokeTime",0);
        if(elapsed<12)return;
        if(!EditorApplication.isPlaying && elapsed<90)return;
        EditorApplication.update-=PollSmoke;SessionState.SetBool("FloorGuidanceSmoke",false);
        try
        {
            Need(EditorApplication.isPlaying,"Could not enter Play Mode");
            var boot=Object.FindAnyObjectByType<Metagame.Bootstrap.SpaceBootstrap>();
            Need(boot && boot.Player!=null,"Player did not spawn");
            var camera=boot.Player.Cam.Cam;
            Need(camera.allowHDR && camera.GetUniversalAdditionalCameraData().renderPostProcessing,"Player post-processing disabled");
            var fixtures=All().Where(t=>t.name.StartsWith(Prefix)).ToArray();Need(fixtures.Length==138,"Runtime fixture count");
            foreach(var t in fixtures)
            {
                var m=t.GetComponentInChildren<Renderer>().sharedMaterials;
                Need(m[0].name=="M_Astra_FloorLight_Body" && m[1].name.StartsWith("M_FloorGuidance_"),"Runtime overwrote chassis or lens");
            }
            File.WriteAllText(Path.Combine(Output,"play-smoke.txt"),"PASS: Stage1_Astra entered Play Mode; player spawned; actual player camera HDR/post-processing enabled; all 138 fixture chassis/lens materials survived runtime initialization.\n");
            Debug.Log("FLOOR_GUIDANCE_PLAY_PASS");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(2);}
    }
    public static void Tune()
    {
        Open();
        foreach(string path in Directory.GetFiles(Materials,"*.mat"))
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(path.Replace('\\','/'));
            var e=m.GetColor("_BaseColor").linear;e*=2.4f/e.maxColorComponent;e.a=1;
            m.SetColor("_EmissionColor",e);m.SetFloat("_Smoothness",.25f);
            m.SetFloat("_SpecularHighlights",0);m.SetFloat("_EnvironmentReflections",0);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");EditorUtility.SetDirty(m);
        }
        foreach(var t in All().Where(t=>t.name.StartsWith(Prefix)))
        {
            var hits=Physics.RaycastAll(t.position+Vector3.up*.15f,Vector3.down,.3f,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>h.collider.gameObject.layer==LayerMask.NameToLayer("Ground") && h.normal.y>.8f)
                .OrderBy(h=>Mathf.Abs(h.point.y-t.position.y)).ToArray();
            if(hits.Length==0)continue;
            var p=t.position;p.y=hits[0].point.y+.003f;t.position=p;
            t.rotation=Quaternion.FromToRotation(t.up,hits[0].normal)*t.rotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);profile.TryGet<Bloom>(out var bloom);
        bloom.threshold.Override(.9f);bloom.intensity.Override(.8f);bloom.scatter.Override(.85f);EditorUtility.SetDirty(bloom);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        ValidateState();Render("after",true);Render("no-bloom",false);
        Validate();
        Debug.Log("FLOOR_GUIDANCE_TUNE_PASS");
    }
    static void Render(string label, bool post)
    {
        var go=new GameObject("Temporary validation camera");var camera=go.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=300;camera.nearClipPlane=.03f;camera.allowHDR=true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=post;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask=~0;
        var previous=RenderTexture.active; bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGBHalf);rt.Create();
        try
        {
            var focus=All().Where(t=>t.name.StartsWith(Prefix)&&t.name.EndsWith("Route cyan")).OrderBy(t=>Vector3.Distance(t.position,new Vector3(12,0,-12))).FirstOrDefault();
            var close=focus?focus.position:new Vector3(12,0,-12);
            Vector3[] eyes={new Vector3(24,35,-42),new Vector3(-3.5f,1.8f,-12.5f),new Vector3(4,2.2f,-8),close+new Vector3(.65f,.5f,-.65f)};
            Vector3[] look={new Vector3(0,0,0),new Vector3(-1.15f,.05f,-10),new Vector3(0,.82f,-8.93f),close};
            for(int i=0;i<eyes.Length;i++)
            {
                camera.transform.position=eyes[i];camera.transform.LookAt(look[i]);camera.fieldOfView=i==0?55:48;
                for(int j=0;j<5;j++)
                {
                    RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
                    if(post){camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);camera.UpdateVolumeStack();}
                }
                if(post)
                {
                    var stack=camera.GetUniversalAdditionalCameraData().volumeStack;
                    var b=stack.GetComponent<Bloom>();var c=stack.GetComponent<ColorAdjustments>();var t=stack.GetComponent<Tonemapping>();
                    File.WriteAllText(Path.Combine(Output,"render-volume.txt"),"Bloom="+b.intensity.value+" threshold="+b.threshold.value+" scatter="+b.scatter.value+" tone="+t.mode.value+" exposure="+c.postExposure.value+" saturation="+c.saturation.value+" color="+c.colorFilter.value);
                }
                RenderTexture.active=rt;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
                if(i==3)
                {
                    var probe=new Texture2D(1600,900,TextureFormat.RGBAFloat,false,true);probe.ReadPixels(new Rect(0,0,1600,900),0,0);probe.Apply();
                    File.WriteAllText(Path.Combine(Output,label+"-hdr.txt"),"center="+probe.GetPixel(800,450)+" max="+probe.GetPixels().Max(c=>Mathf.Max(c.r,c.g,c.b)));
                    Object.DestroyImmediate(probe);
                }
                image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,label+"-"+i+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
            }
        }
        finally {RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
    }
}
