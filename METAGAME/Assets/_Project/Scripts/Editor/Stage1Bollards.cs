using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Fits imported safety bollards to Astra's authored lamps without changing physics.</summary>
    public static class Stage1Bollards
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string ModelFolder = "Assets/_Project/Models/Bollard";
        const string MaterialFolder = "Assets/_Project/Materials/Bollard";
        const string PrefabFolder = "Assets/_Project/Prefabs/Bollard";
        const string PrefabPath = PrefabFolder + "/PF_Stage1_Bollard.prefab";
        const string ShaderPath = "Assets/_Project/Shaders/BollardSignal.shader";
        const string GroupName = "Safety Bollards - Imported FBX";
        static readonly string Output = Path.GetFullPath("../tmp/bollard-integration");
        static readonly string[] Pieces = { "Lamp · foot", "Lamp · ceramic body", "Lamp · light", "Lamp · cap" };

        static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [MenuItem("Tools/Not This Planet/Safety Bollards/1 - Prepare Assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);
            Folder(MaterialFolder); Folder(PrefabFolder);
            string fbx = Directory.GetFiles(ModelFolder, "*.fbx", SearchOption.AllDirectories).Single().Replace('\\', '/');
            string fbm = Path.ChangeExtension(fbx, ".fbm");
            string Texture(string suffix) => Directory.GetFiles(fbm, "*_" + suffix + ".*").Single(p => !p.EndsWith(".meta")).Replace('\\', '/');
            var normal = (TextureImporter)AssetImporter.GetAtPath(Texture("normal"));
            if (normal.textureType != TextureImporterType.NormalMap)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.SaveAndReimport();
            }
            PackTextures(Texture("basecolor"), Texture("metallic"), Texture("roughness"));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Bollard shader is missing or has compile errors.");
            string matPath = MaterialFolder + "/M_Bollard_Neutral.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture("basecolor")));
            mat.SetTexture("_DetailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialFolder + "/T_Bollard_SignalMask.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture("normal")));
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialFolder + "/T_Bollard_MetallicSmoothness.png"));
            mat.SetColor("_BaseColor", new Color(.6f, .6f, .6f));
            mat.SetColor("_EmissionColor", Color.black);
            mat.SetFloat("_BumpScale", .65f); mat.SetFloat("_Smoothness", .65f);
            mat.EnableKeyword("_NORMALMAP"); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
            {
                var root = new GameObject("PF_Stage1_Bollard");
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                    visual.transform.SetParent(root.transform, false);
                    visual.name = "Visual";
                    foreach (var renderer in visual.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = mat;
                    var bounds = Bounds(root);
                    if (bounds.size.y < bounds.size.x) throw new InvalidOperationException("Imported bollard is not upright.");
                    visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
        }

        static Texture2D ReadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read " + path);
            return texture;
        }

        static void PackTextures(string albedoPath, string metallicPath, string roughnessPath)
        {
            var albedo = ReadTexture(albedoPath);
            var metallic = ReadTexture(metallicPath);
            var roughness = ReadTexture(roughnessPath);
            try
            {
                if (metallic.width != roughness.width || metallic.height != roughness.height)
                    throw new InvalidOperationException("Metallic/roughness texture sizes differ.");
                var colors = albedo.GetPixels32();
                int masked = 0;
                for (int i = 0; i < colors.Length; i++)
                {
                    Color c = colors[i];
                    float chroma = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    // The authored signal is achromatic; warm ivory and purple have appreciable chroma.
                    float mask = (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.045f, .085f, chroma)))
                        * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .55f, c.grayscale));
                    byte v = (byte)Mathf.RoundToInt(mask * 255);
                    if (v > 127) masked++;
                    colors[i] = new Color32(v, v, v, 255);
                }
                albedo.SetPixels32(colors); albedo.Apply();
                WriteTexture(albedo, "T_Bollard_SignalMask.png");
                var m = metallic.GetPixels32(); var r = roughness.GetPixels32();
                for (int i = 0; i < m.Length; i++) m[i] = new Color32(m[i].r, 0, 0, (byte)(255 - r[i].r));
                metallic.SetPixels32(m); metallic.Apply();
                WriteTexture(metallic, "T_Bollard_MetallicSmoothness.png");
                File.WriteAllText(Output + "/mask.txt", "neutral signal mask pixels=" + masked + "/" + colors.Length);
            }
            finally { Object.DestroyImmediate(albedo); Object.DestroyImmediate(metallic); Object.DestroyImmediate(roughness); }
        }

        static void WriteTexture(Texture2D texture, string filename)
        {
            string path = MaterialFolder + "/" + filename;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        static Material SignalMaterial(Material source)
        {
            if (!source) throw new InvalidOperationException("Original lamp has no material.");
            string path = MaterialFolder + "/M_Bollard_" + source.name.Replace(' ', '_') + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Bollard_Neutral.mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            material.SetColor("_BaseColor", color);
            var emission = source.IsKeywordEnabled("_EMISSION") && source.HasProperty("_EmissionColor")
                ? source.GetColor("_EmissionColor") : color * .25f;
            material.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Scene TargetScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Open Stage1_Astra in Edit Mode first.");
            return scene;
        }

        [MenuItem("Tools/Not This Planet/Safety Bollards/2 - Replace Astra Bollards")]
        public static void Apply()
        {
            var scene = TargetScene();
            if (scene.isDirty) throw new InvalidOperationException("Preserve unsaved scene edits before replacement.");
            var all = All(scene);
            if (all.Any(t => t.name == GroupName)) throw new InvalidOperationException("Bollards already installed; refusing duplicates.");
            var lights = all.Where(t => t.name == Pieces[2] && t.gameObject.activeInHierarchy).ToArray();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab || lights.Length == 0) throw new InvalidOperationException("Missing prefab or original lamps.");
            // Resolve every four-piece lamp before mutating any scene objects.
            var groups = lights.Select(light => Pieces.Select(name => all.Single(t => t.parent == light.parent && t.name == name
                && new Vector2(t.position.x-light.position.x, t.position.z-light.position.z).sqrMagnitude < .000001f)).ToArray()).ToArray();
            var guidance = all.Select(t => t.GetComponent<AssemblyGuidanceLight>()).Single(g => g);
            var config = new SerializedObject(guidance);
            var originalGuides = Enumerable.Range(0, config.FindProperty("lamps").arraySize)
                .Select(i => config.FindProperty("lamps").GetArrayElementAtIndex(i).objectReferenceValue).ToHashSet();
            var floorProperty = config.FindProperty("floorMaterials");
            var colors = Enumerable.Range(0, floorProperty.arraySize).Select(i => SignalMaterial((Material)floorProperty.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray();
            var ready = SignalMaterial((Material)config.FindProperty("completeMaterial").objectReferenceValue);
            var initialMaterials = lights.Select(t => SignalMaterial(t.GetComponent<Renderer>().sharedMaterial)).ToArray();
            Directory.CreateDirectory(Output);
            File.Copy(ScenePath, Output + "/Stage1_Astra.before.unity", false);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Astra safety bollards");
            var report = new StringBuilder();
            try
            {
                var owner = new GameObject(GroupName);
                Undo.RegisterCreatedObjectUndo(owner, "Create bollard group");
                var guideRenderers = new List<Renderer>();
                for (int i = 0; i < lights.Length; i++)
                {
                    var old = groups[i].Select(t => t.GetComponent<Renderer>()).ToArray();
                    var bounds = old[0].bounds;
                    foreach (var renderer in old.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    Undo.RegisterCreatedObjectUndo(instance, "Create imported bollard");
                    instance.name = "Bollard " + (i+1).ToString("D2") + " - " + lights[i].GetComponent<Renderer>().sharedMaterial.name;
                    instance.transform.SetParent(owner.transform, false);
                    var native = Bounds(instance);
                    instance.transform.localScale = new Vector3(bounds.size.x/native.size.x, bounds.size.y/native.size.y, bounds.size.z/native.size.z);
                    var scaled = Bounds(instance);
                    instance.transform.position += new Vector3(bounds.center.x-scaled.center.x, bounds.min.y-scaled.min.y, bounds.center.z-scaled.center.z);
                    var newRenderer = instance.GetComponentInChildren<Renderer>();
                    newRenderer.sharedMaterial = initialMaterials[i];
                    PrefabUtility.RecordPrefabInstancePropertyModifications(newRenderer);
                    foreach (var child in instance.GetComponentsInChildren<Transform>()) child.gameObject.layer = lights[i].gameObject.layer;
                    if (originalGuides.Contains(lights[i].GetComponent<Renderer>())) guideRenderers.Add(newRenderer);
                    foreach (var renderer in old)
                    {
                        Undo.RecordObject(renderer, "Hide original lamp visual");
                        renderer.enabled = false; // Keep authored colliders, transforms and serialized references.
                    }
                    report.AppendLine(instance.name + " position=" + instance.transform.position.ToString("F5") + " size=" + Bounds(instance).size.ToString("F5")
                        + " color=" + initialMaterials[i].GetColor("_BaseColor") + " originalColliderCount=" + groups[i].Count(t => t.GetComponent<Collider>()));
                }
                Undo.RecordObject(guidance, "Connect assembly state to textured bollards");
                guidance.ConfigureBollards(guideRenderers.ToArray(), colors, ready);
                EditorUtility.SetDirty(guidance);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                Validate();
                EditorSceneManager.SaveScene(scene);
                Undo.CollapseUndoOperations(undo);
                report.AppendLine("replaced=" + lights.Length + " dynamic=" + guideRenderers.Count);
                File.WriteAllText(Output + "/placement.txt", report.ToString());
                Debug.Log("[Bollards] Replaced " + lights.Length + " lamps, including " + guideRenderers.Count + " assembly guidance lights.");
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
        }

        [MenuItem("Tools/Not This Planet/Safety Bollards/3 - Validate")]
        public static void Validate()
        {
            var scene = TargetScene();
            var all = All(scene);
            var owner = all.Single(t => t.name == GroupName);
            var lights = all.Where(t => t.name == Pieces[2] && t.gameObject.activeInHierarchy).ToArray();
            if (owner.childCount != lights.Length) throw new InvalidOperationException("Replacement count mismatch.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Bollard shader compile failure.");
            var report = new StringBuilder();
            foreach (Transform instance in owner)
            {
                var b = Bounds(instance.gameObject);
                var light = lights.Single(t => new Vector2(t.position.x-b.center.x, t.position.z-b.center.z).sqrMagnitude < .000001f);
                var pieces = Pieces.Select(name => all.Single(t => t.parent == light.parent && t.name == name
                    && new Vector2(t.position.x-light.position.x,t.position.z-light.position.z).sqrMagnitude < .000001f)).ToArray();
                var original = pieces[0].GetComponent<Renderer>().bounds;
                foreach (var p in pieces.Skip(1)) original.Encapsulate(p.GetComponent<Renderer>().bounds);
                if ((b.min-original.min).magnitude > .002f || (b.max-original.max).magnitude > .002f)
                    throw new InvalidOperationException("Placement mismatch: " + instance.name);
                if (pieces.Any(t => t.GetComponent<Renderer>().enabled || !t.GetComponent<Collider>().enabled))
                    throw new InvalidOperationException("Original visuals or physics changed unexpectedly: " + instance.name);
                var renderer = instance.GetComponentInChildren<Renderer>();
                if (renderer.sharedMaterial.shader != shader || !renderer.sharedMaterial.GetTexture("_BaseMap") || !renderer.sharedMaterial.GetTexture("_DetailMask"))
                    throw new InvalidOperationException("Missing textured signal material: " + instance.name);
                var source = light.GetComponent<Renderer>().sharedMaterial;
                if (renderer.sharedMaterial.GetColor("_BaseColor") != source.GetColor("_BaseColor"))
                    throw new InvalidOperationException("Initial color mismatch: " + instance.name);
            }
            var guidance = all.Select(t => t.GetComponent<AssemblyGuidanceLight>()).Single(g => g);
            var so = new SerializedObject(guidance);
            if (so.FindProperty("bollards").arraySize != 4 || so.FindProperty("bollardFloorMaterials").arraySize != so.FindProperty("floorMaterials").arraySize)
                throw new InvalidOperationException("Dynamic assembly light mapping is incomplete.");
            for (int i=0; i<so.FindProperty("floorMaterials").arraySize; i++)
            {
                var original=(Material)so.FindProperty("floorMaterials").GetArrayElementAtIndex(i).objectReferenceValue;
                var replacement=(Material)so.FindProperty("bollardFloorMaterials").GetArrayElementAtIndex(i).objectReferenceValue;
                if (!replacement || replacement.shader != shader || replacement.GetColor("_BaseColor") != original.GetColor("_BaseColor"))
                    throw new InvalidOperationException("Incorrect assembly signal color " + i);
                report.AppendLine("state " + i + ": " + original.name + " -> " + replacement.name);
            }
            report.AppendLine("PASS: " + owner.childCount + " FBX instances, matched bounds/colors, preserved " + lights.Length*4 + " original colliders, 4 dynamic lights, shader compile.");
            File.WriteAllText(Output + "/validation.txt", report.ToString());
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        [MenuItem("Tools/Not This Planet/Safety Bollards/4 - Align Feet to Current Surfaces")]
        public static void AlignSupports()
        {
            var scene=TargetScene();
            var all=All(scene);
            var owner=all.Single(t=>t.name==GroupName);
            var candidates=all.Select(t=>t.GetComponent<Collider>()).Where(c=>c && c.enabled && c.gameObject.activeInHierarchy
                && !c.isTrigger && c.gameObject.layer==8 && !Pieces.Contains(c.name)).ToArray();
            Physics.SyncTransforms();
            var plan=new List<(Transform instance, Transform[] pieces, float offset)>();
            var report=new StringBuilder();
            foreach(Transform instance in owner)
            {
                var b=Bounds(instance.gameObject);
                var ray=new Ray(new Vector3(b.center.x,b.max.y+.2f,b.center.z),Vector3.down);
                float surface=float.NegativeInfinity;
                string support="";
                foreach(var c in candidates)
                    if(c.Raycast(ray,out var hit,b.size.y+3) && hit.normal.y>.35f && hit.point.y>surface)
                    { surface=hit.point.y; support=c.name; }
                if(float.IsNegativeInfinity(surface)) throw new InvalidOperationException("No support: "+instance.name);
                float offset=surface-b.min.y;
                if(Mathf.Abs(offset)>1.5f) throw new InvalidOperationException("Unexpected support displacement: "+instance.name+" "+offset);
                var pieces=all.Where(t=>Pieces.Contains(t.name) && new Vector2(t.position.x-b.center.x,t.position.z-b.center.z).sqrMagnitude<.000001f).ToArray();
                if(pieces.Length!=4) throw new InvalidOperationException("Ambiguous collision group");
                plan.Add((instance,pieces,offset));
                report.AppendLine(instance.name+" bottom="+b.min.y.ToString("F4")+" supportY="+surface.ToString("F4")+" offset="+offset.ToString("F4")+" support="+support);
            }
            Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Align bollard feet to current platform surfaces");
            foreach(var step in plan)
            {
                if(Mathf.Abs(step.offset)<.002f) continue;
                Undo.RecordObject(step.instance,"Align bollard foot");
                step.instance.position+=Vector3.up*step.offset;
                PrefabUtility.RecordPrefabInstancePropertyModifications(step.instance);
                foreach(var piece in step.pieces)
                {
                    Undo.RecordObject(piece,"Move matching authored collision");
                    piece.position+=Vector3.up*step.offset;
                }
            }
            Physics.SyncTransforms();
            Validate();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(undo);
            File.WriteAllText(Output+"/supports.txt",report.ToString());
        }

        public static void Capture()
        {
            var scene = TargetScene();
            var owner = All(scene).Single(t => t.name == GroupName);
            var cameraGo = new GameObject("Bollard Review Camera") { hideFlags=HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true; camera.nearClipPlane=.01f; camera.farClipPlane=300;
            camera.clearFlags=CameraClearFlags.Skybox;
            try
            {
                foreach (var item in owner.Cast<Transform>().GroupBy(t=>t.GetComponentInChildren<Renderer>().sharedMaterial).Select(g=>g.First()))
                {
                    var b=Bounds(item.gameObject);
                    camera.orthographicSize=b.size.y*.8f;
                    camera.transform.position=b.center+new Vector3(1,.5f,-1.5f).normalized*b.size.y*3;
                    camera.transform.LookAt(b.center);
                    CaptureCamera(camera, Output+"/"+item.name.Replace(' ','_')+".png",800,1000);
                }
                camera.orthographicSize=13;
                camera.transform.position=new Vector3(15,20,-18); camera.transform.LookAt(new Vector3(0,1,0));
                CaptureCamera(camera,Output+"/central-assembly.png",1400,1000);
            }
            finally { Object.DestroyImmediate(cameraGo); }
        }

        static void CaptureCamera(Camera camera, string path, int width, int height)
        {
            var rt=new RenderTexture(width,height,24); rt.Create();
            var previous=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation=false;
            try
            {
                camera.aspect=(float)width/height;
                var request=new RenderPipeline.StandardRequest {destination=rt};
                for(int i=0;i<3;i++) RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=rt;
                var image=new Texture2D(width,height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG()); Object.DestroyImmediate(image);
            }
            finally { ShaderUtil.allowAsyncCompilation=async; RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
}
