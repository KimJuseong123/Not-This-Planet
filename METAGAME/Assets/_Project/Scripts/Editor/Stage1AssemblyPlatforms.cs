using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Metagame.Gameplay.Parts;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Builds URP platform prefabs and replaces only Astra platform geometry.</summary>
    public static class Stage1AssemblyPlatforms
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string Models = "Assets/_Project/Models/AssemblyParts";
        const string Materials = "Assets/_Project/Materials/AssemblyParts";
        const string Prefabs = "Assets/_Project/Prefabs/AssemblyParts";
        static readonly string Output = Path.GetFullPath("../tmp/assembly-platform-integration");
        static readonly string[] Keys = { "central-assembly-dock", "part1-engine-camp", "part2-hover-observatory", "part3-capsule-hangar", "part4-tractor-dock" };
        static readonly string[] Names = { "CentralAssemblyDock", "EngineCamp", "HoverObservatory", "CapsuleHangar", "TractorDock" };
        static string PrefabPath(int i) => Prefabs + "/PF_Stage1_" + Names[i] + ".prefab";

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [MenuItem("Tools/Not This Planet/Assembly Platforms/1 - Prepare Assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);
            Folder(Materials); Folder(Prefabs);
            var report = new StringBuilder();
            for (int i = 0; i < Keys.Length; i++)
            {
                string file = Directory.GetFiles(Models + "/" + Keys[i], "*.fbx").Single().Replace('\\', '/');
                string textureFolder = Path.ChangeExtension(file, ".fbm");
                string Texture(string suffix) => Directory.GetFiles(textureFolder, "*_" + suffix + ".*").Single(x => !x.EndsWith(".meta")).Replace('\\', '/');
                var normalImporter = (TextureImporter)AssetImporter.GetAtPath(Texture("normal"));
                if (normalImporter.textureType != TextureImporterType.NormalMap)
                {
                    normalImporter.textureType = TextureImporterType.NormalMap;
                    normalImporter.SaveAndReimport();
                }
                // URP reads metallic from R and smoothness from A, not the source RM JPEG.
                string packedPath = Materials + "/T_" + Names[i] + "_MetallicSmoothness.png";
                if (!File.Exists(packedPath))
                {
                    var metal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    metal.LoadImage(File.ReadAllBytes(Texture("metallic")));
                    rough.LoadImage(File.ReadAllBytes(Texture("roughness")));
                    if (metal.width != rough.width || metal.height != rough.height) throw new InvalidOperationException("PBR size mismatch: " + Keys[i]);
                    var m = metal.GetPixels32(); var r = rough.GetPixels32();
                    for (int p = 0; p < m.Length; p++) m[p] = new Color32(m[p].r, 0, 0, (byte)(255 - r[p].r));
                    metal.SetPixels32(m); metal.Apply();
                    File.WriteAllBytes(packedPath, metal.EncodeToPNG());
                    Object.DestroyImmediate(metal); Object.DestroyImmediate(rough);
                    AssetDatabase.ImportAsset(packedPath);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(packedPath);
                    ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = false; ti.SaveAndReimport();
                }
                string matPath = Materials + "/M_Stage1_" + Names[i] + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (!shader) throw new InvalidOperationException("URP Lit missing");
                    mat = new Material(shader);
                    mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture("basecolor")));
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture("normal")));
                    mat.SetFloat("_BumpScale", 0.65f); mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
                    mat.SetFloat("_Smoothness", 0.65f); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)) == null)
                {
                    var root = new GameObject("PF_Stage1_" + Names[i]) { layer = 8 };
                    try
                    {
                        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(file));
                        visual.transform.SetParent(root.transform, false);
                        visual.name = "Visual";
                        foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                            renderer.sharedMaterials = Enumerable.Repeat(mat, renderer.sharedMaterials.Length).ToArray();
                        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>())
                        {
                            filter.gameObject.layer = 8;
                            var collider = filter.gameObject.AddComponent<MeshCollider>();
                            collider.sharedMesh = filter.sharedMesh;
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(i));
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                var preview = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)));
                try
                {
                    var b = Bounds(preview);
                    float y = Surface(preview, b.center.x, b.center.z);
                    report.AppendLine(Keys[i] + " boundsMin=" + b.min.ToString("F5") + " size=" + b.size.ToString("F5") + " centerSurface=" + y.ToString("F5"));
                    RenderModel(preview, Keys[i] + "-model", false);
                    RenderModel(preview, Keys[i] + "-top", true);
                }
                finally { Object.DestroyImmediate(preview); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Output + "/models.txt", report.ToString());
        }

        [MenuItem("Tools/Not This Planet/Assembly Platforms/2 - Replace Astra Platforms")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before platform replacement.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open Stage1_Astra before replacement.");
            if (scene.isDirty) throw new InvalidOperationException("The scene contains unsaved edits; preserve them before replacement.");
            if (scene.GetRootGameObjects().Any(g => g.name == "Assembly Platforms - Imported FBX"))
                throw new InvalidOperationException("Platforms are already installed; use Undo instead of duplicating them.");
            for (int i=0;i<Keys.Length;i++)
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i))) throw new InvalidOperationException("Prepare assets first.");

            Directory.CreateDirectory(Output);
            File.Copy(ScenePath, Output + "/Stage1_Astra.before.unity", true);
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Astra assembly platforms");
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var owner = new GameObject("Assembly Platforms - Imported FBX");
            Undo.RegisterCreatedObjectUndo(owner, "Create platform group");
            SceneManager.MoveGameObjectToScene(owner, scene);
            var report = new StringBuilder();
            try
            {
                var instances = new GameObject[Keys.Length];
                for (int i=0;i<Keys.Length;i++)
                {
                    instances[i] = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)), scene);
                    Undo.RegisterCreatedObjectUndo(instances[i], "Create platform");
                    instances[i].transform.SetParent(owner.transform, false);
                    instances[i].name = Names[i];
                }
                Fit(instances[0], new Vector3(0,.99f,0), new Vector2(22,22), .1f, 0);
                Fit(instances[1], new Vector3(-20,.5f,-20), new Vector2(6.6f,4.5f), .08f, 0);
                Fit(instances[2], new Vector3(28,8.055f,19), new Vector2(12.2f,12.2f), 7.4f, 0);
                FitHangar(instances[3]);
                Fit(instances[4], new Vector3(23,.86f,-19), new Vector2(14,11), .12f, 0);

                string[] prefixes = { "Assembly · ", "Hill observatory · ", "Tractor dock · ", "Cargo pickup apron · ",
                    "Ring · Assembly ", "Ring · Hill observatory ", "Ring · Tractor dock ", "Ring · Cargo pickup apron " };
                string[] exact = { "Assembly socket", "Engine parking plinth", "Cargo connector" };
                foreach(var t in all)
                {
                    if (!prefixes.Any(p => t.name.StartsWith(p, StringComparison.Ordinal)) && !exact.Contains(t.name)) continue;
                    Undo.RecordObject(t.gameObject, "Hide replaced blockout");
                    t.gameObject.SetActive(false);
                    report.AppendLine("Replaced: " + t.name);
                }
                // The broad hangar slab also supports the surrounding rock building and walkways.
                // Keep it as a structural floor below the new imported loading surface.
                var slab = all.Single(t => t.name == "Hangar slab");
                report.AppendLine("Retained hangar structural slab: " + slab.position);
                Physics.SyncTransforms();
                foreach (var part in all.Select(t=>t.GetComponent<SpaceshipPart>()).Where(p=>p!=null))
                {
                    int i = Array.FindIndex(Keys, k=>k.StartsWith("part" + part.name.Substring(5) + "-", StringComparison.Ordinal));
                    if(i<1) continue;
                    AlignPart(part, instances[i], report);
                }
                foreach(var instance in instances)
                {
                    var b = Bounds(instance);
                    report.AppendLine(instance.name + " position=" + instance.transform.position.ToString("F4")
                        + " scale=" + instance.transform.localScale.ToString("F4") + " size=" + b.size.ToString("F4")
                        + " rangeY=" + b.min.y.ToString("F3") + ".." + b.max.y.ToString("F3"));
                }
                Undo.CollapseUndoOperations(undo);
                EditorSceneManager.MarkSceneDirty(scene);
                File.WriteAllText(Output + "/placement.txt", report.ToString());
                Validate();
                CaptureScene();
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[AssemblyPlatforms] Installed five imported platforms in Stage1_Astra.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static void Fit(GameObject instance, Vector3 deckCenter, Vector2 size, float bottom, float yaw)
        {
            var t=instance.transform;
            t.rotation=Quaternion.Euler(0,yaw,0);
            var b=Bounds(instance);
            float surface=Surface(instance,b.center.x,b.center.z);
            float height=surface-b.min.y;
            if(height < .001f) throw new InvalidOperationException("Cannot measure platform deck height");
            t.localScale=new Vector3(size.x/b.size.x, (deckCenter.y-bottom)/height, size.y/b.size.z);
            b=Bounds(instance);
            t.position+=new Vector3(deckCenter.x-b.center.x,0,deckCenter.z-b.center.z);
            t.position+=Vector3.up*(deckCenter.y-Surface(instance,deckCenter.x,deckCenter.z));
        }

        static void FitHangar(GameObject instance)
        {
            // Source rectangle is on +X and pickup disk is on -X; rotate toward the existing exit.
            // These sample points are checked against the actual imported top preview.
            var t=instance.transform;
            var b=Bounds(instance);
            Vector3 from=new Vector3(b.min.x+b.size.x*.82f,0,b.center.z);
            Vector3 to=new Vector3(b.min.x+b.size.x*.21f,0,b.center.z);
            Vector3 targetFrom=new Vector3(-29,5.65f,25);
            Vector3 targetTo=new Vector3(-9.5f,5.65f,29);
            var delta=targetTo-targetFrom;
            float scale=new Vector2(delta.x,delta.z).magnitude/Mathf.Abs(to.x-from.x);
            float surface=Surface(instance,from.x,from.z);
            float sy=.7f/(surface-b.min.y);
            t.localScale=new Vector3(scale,sy,scale);
            t.rotation=Quaternion.Euler(0,180-Mathf.Atan2(delta.z,delta.x)*Mathf.Rad2Deg,0);
            var anchor=t.TransformPoint(from);
            t.position+=new Vector3(targetFrom.x-anchor.x,0,targetFrom.z-anchor.z);
            t.position+=Vector3.up*(targetFrom.y-Surface(instance,targetFrom.x,targetFrom.z));
        }

        static void AlignPart(SpaceshipPart part, GameObject platform, StringBuilder report)
        {
            var hull = PartBounds(part);
            float surface=Surface(platform,hull.center.x,hull.center.z);
            var before=part.transform.position;
            Undo.RecordObject(part.transform,"Align part to imported deck");
            part.transform.position+=Vector3.up*(surface+.025f-hull.min.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform);
            Physics.SyncTransforms();
            foreach(var pin in part.GetComponentsInChildren<PartClamp>(true))
            {
                var point=pin.transform.position;
                float support=Surface(platform,point.x,point.z);
                Undo.RecordObject(pin.transform,"Align release pin");
                pin.transform.position=new Vector3(point.x,support,point.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pin.transform);
            }
            report.AppendLine(part.name+" before="+before.ToString("F4")+" after="+part.transform.position.ToString("F4")+" deckY="+surface.ToString("F4"));
        }

        [MenuItem("Tools/Not This Planet/Assembly Platforms/3 - Validate Placement")]
        public static void Validate()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath) throw new InvalidOperationException("Wrong active scene");
            var group=scene.GetRootGameObjects().Single(g=>g.name=="Assembly Platforms - Imported FBX");
            if(group.transform.childCount!=5) throw new InvalidOperationException("Expected five platforms");
            var report=new StringBuilder();
            Physics.SyncTransforms();
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))
                throw new InvalidOperationException("Missing scene script");
            foreach(var r in group.GetComponentsInChildren<Renderer>())
                if(r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name!="Universal Render Pipeline/Lit" || !m.GetTexture("_BaseMap")))
                    throw new InvalidOperationException("Invalid platform material: "+r.name);
            var parts=all.Select(t=>t.GetComponent<SpaceshipPart>()).Where(p=>p!=null).ToArray();
            if(parts.Length!=4) throw new InvalidOperationException("Expected four gameplay parts");
            foreach(var part in parts)
            {
                int i=int.Parse(part.name.Substring(5));
                var platform=group.transform.Find(Names[i]).gameObject;
                var hull = PartBounds(part);
                float support=Surface(platform,hull.center.x,hull.center.z);
                float gap=hull.min.y-support;
                if(Mathf.Abs(gap-.025f)>.04f) throw new InvalidOperationException(part.name+" floating/intersecting: "+gap);
                foreach(var pin in part.GetComponentsInChildren<PartClamp>(true))
                {
                    float pinY=Surface(platform,pin.transform.position.x,pin.transform.position.z);
                    if(Mathf.Abs(pin.transform.position.y-pinY)>.03f) throw new InvalidOperationException("Pin unsupported: "+pin.name);
                }
                report.AppendLine(part.name+" support="+support.ToString("F4")+" hullGap="+gap.ToString("F4")
                    +" pins="+part.GetComponentsInChildren<PartClamp>(true).Length);
            }
            var assembly=all.Select(t=>t.GetComponent<ShipAssembly>()).Single(a=>a!=null);
            float center=Surface(group.transform.Find(Names[0]).gameObject,assembly.LaunchPad.position.x,assembly.LaunchPad.position.z);
            if(Mathf.Abs(center-assembly.LaunchPad.position.y)>.02f) throw new InvalidOperationException("Dock height mismatch");
            report.AppendLine("Assembly dock surface="+center.ToString("F4")+" gameplayHeight="+assembly.LaunchPad.position.y);
            report.AppendLine("Five FBX prefabs / URP materials / static mesh colliders / four parts / pins / docking height: PASS");
            File.WriteAllText(Output+"/validation.txt",report.ToString());
        }

        public static void CaptureScene()
        {
            var group=SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Assembly Platforms - Imported FBX");
            var go=new GameObject("PlatformReviewCamera"){hideFlags=HideFlags.HideAndDontSave};
            var camera=go.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.Skybox; camera.farClipPlane=400; camera.nearClipPlane=.05f;
            camera.orthographic=true;
            try
            {
                foreach(Transform child in group.transform)
                {
                    var b=Bounds(child.gameObject);
                    float size=Mathf.Max(b.size.x,b.size.z);
                    camera.orthographicSize=size*.58f;
                    var focus=b.center;
                    camera.transform.position=focus+new Vector3(0,.95f,-1).normalized*size*2;
                    camera.transform.LookAt(focus);
                    Capture(camera,Output+"/"+child.name+"-scene.png",1400,1000);
                }
                camera.orthographicSize=56;
                camera.transform.position=new Vector3(45,95,-65);camera.transform.LookAt(new Vector3(0,3,0));
                Capture(camera,Output+"/Astra-platforms-overview.png",1600,1100);
            }
            finally{Object.DestroyImmediate(go);}
        }

        // Inspect serialized hull geometry without initializing gameplay state in Edit Mode.
        static Bounds PartBounds(SpaceshipPart part)
        {
            var result = new Bounds();
            bool found = false;
            foreach (var c in part.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || c.GetComponentInParent<PartClamp>() != null) continue;
                Bounds local;
                if (c is MeshCollider mesh && mesh.sharedMesh) local = mesh.sharedMesh.bounds;
                else if (c is BoxCollider box) local = new Bounds(box.center, box.size);
                else continue;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = c.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!found) { result = new Bounds(p, Vector3.zero); found = true; }
                    else result.Encapsulate(p);
                }
            }
            if (!found) throw new InvalidOperationException("No serialized part hull: " + part.name);
            return result;
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No renderer: " + go.name);
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
        static float Surface(GameObject go, float x, float z)
        {
            var b = Bounds(go);
            var ray = new Ray(new Vector3(x, b.max.y + 10, z), Vector3.down);
            float top = float.NegativeInfinity;
            Physics.SyncTransforms();
            foreach (var c in go.GetComponentsInChildren<Collider>())
                if (c.Raycast(ray, out var hit, b.size.y + 20)) top = Mathf.Max(top, hit.point.y);
            if (float.IsNegativeInfinity(top)) throw new InvalidOperationException("No deck surface at " + x + "," + z + " for " + go.name);
            return top;
        }

        static void RenderModel(GameObject model, string filename, bool top)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var clone = Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.transform.position = Vector3.zero;
            var cameraGo = new GameObject("PlatformPreviewCamera");
            var key = new GameObject("PlatformPreviewLight");
            var fill = new GameObject("PlatformPreviewFill");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            SceneManager.MoveGameObjectToScene(key, scene);
            SceneManager.MoveGameObjectToScene(fill, scene);
            var b = Bounds(clone);
            var camera = cameraGo.AddComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .21f, .25f);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(b.size.z, b.size.x / 1.25f) * .66f;
            camera.nearClipPlane = .005f; camera.farClipPlane = 200;
            camera.transform.position = b.center + (top ? Vector3.up : new Vector3(0, .8f, -1)).normalized * Mathf.Max(b.size.x, b.size.z) * 3;
            camera.transform.rotation = top ? Quaternion.LookRotation(Vector3.down, Vector3.forward) : Quaternion.LookRotation(b.center - camera.transform.position);
            var light = key.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            key.transform.rotation = Quaternion.Euler(45, -30, 0);
            var light2 = fill.AddComponent<Light>(); light2.type = LightType.Directional; light2.intensity = .7f;
            fill.transform.rotation = Quaternion.Euler(35, 150, 0);
            try { Capture(camera, Output + "/" + filename + ".png", 1100, 880); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Capture(Camera camera, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24);
            rt.Create();
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.aspect = (float)width / height;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                for (int i = 0; i < 3; i++) RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = previous; rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
}
