using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1Buildings
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string Art = "Assets/_Project/Art/Buildings";
        static readonly string Output = Path.GetFullPath("../tmp/building-integration");
        [InitializeOnLoadMethod]
        static void Connect() { EditorApplication.update -= Poll; EditorApplication.update += Poll; EditorApplication.update -= PollPlay; EditorApplication.update += PollPlay; }
        static double nextPoll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Output + "/request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (command == "inspect") Inspect();
                else if (command == "prepare") Prepare();
                else if (command == "apply") Apply();
                else if (command == "validate") Validate();
                else if (command == "capture") Capture();
                else if (command == "polish") Polish();
                else if (command == "playcheck") PlayCheck();
                else if (command == "restorebeacon") RestorePart3Beacon();
                else if (command == "refresh") AssetDatabase.Refresh();
                else throw new InvalidOperationException("Unknown building command: " + command);
                File.WriteAllText(Output+"/result.txt", command+" PASS");
            }
            catch (Exception e) { File.WriteAllText(Output+"/result.txt", e.ToString()); Debug.LogException(e); }
        }
        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        static Bounds Bounds(GameObject g)
        {
            var renderers = g.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static string PrefabPath(string name) => Art + "/" + name + "/PF_Astra" + name + ".prefab";
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit play mode first.");
            foreach (string name in new[]{"Shelter", "Hangar"})
            {
                string folder = Art + "/" + name;
                string tex = folder + "/Astra" + name + ".fbm/Astra" + name + "_";
                foreach (string suffix in new[]{"BaseColor", "Normal", "MetallicSmoothness", "Emission", "Metallic", "Roughness"})
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(tex + suffix + ".png");
                    if (ti == null) throw new InvalidOperationException("Missing texture " + suffix);
                    ti.textureType = suffix == "Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    ti.sRGBTexture = suffix == "BaseColor" || suffix == "Emission";
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = false; ti.maxTextureSize = 2048;
                    ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.SaveAndReimport();
                }
                string matPath = folder + "/M_Astra" + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"BaseColor.png"));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"Normal.png"));
                mat.SetFloat("_BumpScale", .35f); mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"MetallicSmoothness.png"));
                mat.SetFloat("_Smoothness", 1); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex+"Emission.png"));
                mat.SetColor("_EmissionColor", Color.white * 1.4f); mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                EditorUtility.SetDirty(mat);
                var importer = (ModelImporter)AssetImporter.GetAtPath(folder + "/Astra" + name + ".fbx");
                importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                var root = new GameObject("Astra " + name) { layer = GameLayers.Ground };
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Astra"+name+".fbx"));
                    visual.name = "Visual"; visual.transform.SetParent(root.transform, false);
                    var facingPart = visual.GetComponentsInChildren<Renderer>().First(r => r.name.Contains(name == "Shelter" ? "Entry" : "Floor"));
                    if (facingPart.bounds.center.z < 0) visual.transform.localRotation = Quaternion.Euler(0,180,0);
                    foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = GameLayers.Ground;
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                    }
                    // Only shell geometry collides. Tiny bolts, grilles and light strips cannot snag the ragdoll.
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                    {
                        string n = mf.name;
                        if (n.Contains("Floor") || n.Contains("Fasteners") || n.Contains("Grilles") || n.Contains("Emission") || n.Contains("Solar") || n.Contains("HVAC") || n.Contains("Antenna") || n.Contains("Service") || n.Contains("RoofEquipment") || n.Contains("Interior")) continue;
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    }
                    float width = name == "Shelter" ? 14 : 20;
                    float depth = name == "Shelter" ? 12 : 18;
                    float floorTop = name == "Shelter" ? .38f : .37f;
                    var floor = new GameObject("Walkable floor") { layer = GameLayers.Ground };
                    floor.transform.SetParent(root.transform, false);
                    var fc = floor.AddComponent<BoxCollider>(); fc.center = new Vector3(0,floorTop*.5f,0); fc.size = new Vector3(width,floorTop,depth);
                    // A six-face ramp gives smooth continuous contact, independent of decorative floor seams.
                    float near = depth*.5f, far = near + (name == "Shelter" ? 2.6f : 3.2f);
                    float half = name == "Shelter" ? 2.3f : 8f;
                    var mesh = new Mesh { name = "Astra" + name + "_RampCollision" };
                    mesh.vertices = new[]{ new Vector3(-half,0,near),new Vector3(half,0,near),new Vector3(half,0,far),new Vector3(-half,0,far),new Vector3(-half,floorTop,near),new Vector3(half,floorTop,near),new Vector3(half,.025f,far),new Vector3(-half,.025f,far) };
                    mesh.triangles = new[]{ 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5 };
                    var winding = mesh.triangles;
                    for (int i=0; i<winding.Length; i+=3) { int v=winding[i]; winding[i]=winding[i+2]; winding[i+2]=v; }
                    mesh.triangles = winding;
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    string collisionPath = folder + "/Astra" + name + "_RampCollision.asset";
                    var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(collisionPath);
                    if (existingMesh) { EditorUtility.CopySerialized(mesh, existingMesh); Object.DestroyImmediate(mesh); mesh = existingMesh; }
                    else AssetDatabase.CreateAsset(mesh, collisionPath);
                    var ramp = new GameObject("Walkable entrance ramp") { layer = GameLayers.Ground };
                    ramp.transform.SetParent(root.transform,false); ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
        }
        public static void Inspect()
        {
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            Directory.CreateDirectory(Output);
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) {
                if (!Application.isBatchMode) throw new InvalidOperationException("Open Stage1_Astra first.");
                scene = EditorSceneManager.OpenScene(ScenePath);
            }
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var report = new StringBuilder();
            foreach (var t in all.Where(t => t.name.IndexOf("shelter", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("hangar", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.Contains("Spawn") || (t.parent == null) || t.name.StartsWith("Part")))
            {
                report.AppendLine(Hierarchy(t) + " pos=" + t.position.ToString("F3") + " rot=" + t.eulerAngles.ToString("F2") + " scale=" + t.lossyScale.ToString("F3") + (t.GetComponentInChildren<Renderer>() ? " bounds=" + Bounds(t.gameObject).ToString("F3") : ""));
                if (t.name.StartsWith("Expedition shelter") || t.name.StartsWith("06 "))
                    foreach (var c in t.GetComponentsInChildren<Transform>(true)) report.AppendLine("  " + c.name + " pos=" + c.position.ToString("F3") + " scale=" + c.lossyScale.ToString("F3") + " components=" + string.Join(",", c.GetComponents<Component>().Select(x => x == null ? "MISSING" : x.GetType().Name)));
            }
            foreach (string name in new[]{"Shelter", "Hangar"})
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/"+name+"/Astra"+name+".fbx");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                report.AppendLine("MODEL " + name + " " + Bounds(go).ToString("F4"));
                foreach(var mf in go.GetComponentsInChildren<MeshFilter>()) report.AppendLine("  " + mf.name + " " + mf.GetComponent<Renderer>().bounds.ToString("F4"));
                Object.DestroyImmediate(go);
            }
            foreach (var r in all.Select(t => t.GetComponent<Renderer>()).Where(r => r && r.gameObject.activeInHierarchy && r.bounds.center.x < -14 && (r.bounds.center.z < -12 || r.bounds.center.z > 14)))
                report.AppendLine("NEARBY " + Hierarchy(r.transform) + " " + r.bounds.ToString("F3"));
            report.AppendLine("Scene dirty=" + scene.isDirty);
            File.WriteAllText(Output+"/inspection.txt", report.ToString());
        }
        static Transform[] All() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        [MenuItem("Tools/Not This Planet/Buildings/Replace Astra Buildings")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Open Astra in edit mode.");
            if (All().Any(t=>t.name=="Buildings - Authored FBX")) throw new InvalidOperationException("Buildings already installed.");
            var all = All();
            var oldShelter = all.Single(t=>t.name.StartsWith("Expedition shelter"));
            var camp = oldShelter.parent;
            var hangarZone = all.Single(t=>t.name.StartsWith("06 · NORTHWEST"));
            var slab = all.Single(t=>t.name=="Hangar slab");
            var deckMaterial = slab.GetComponent<Renderer>().sharedMaterial;
            var map = all.Select(t=>t.GetComponent<AuthoredMapSettings>()).Single(m=>m);
            if (!map.playerSpawn) throw new InvalidOperationException("Missing authored spawn.");
            foreach(string name in new[]{"Shelter","Hangar"}) if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name))) throw new InvalidOperationException("Prepare buildings first.");
            Directory.CreateDirectory(Output);
            if (!File.Exists(Output+"/Stage1_Astra.before-buildings.unity")) File.Copy(ScenePath, Output+"/Stage1_Astra.before-buildings.unity");
            if(scene.isDirty) EditorSceneManager.SaveScene(scene,Output+"/Stage1_Astra.before-buildings-live.unity",true);
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Replace Astra buildings and spawn");
            var report = new StringBuilder();
            try
            {
                var owner = new GameObject("Buildings - Authored FBX"); Undo.RegisterCreatedObjectUndo(owner,"Create buildings");
                SceneManager.MoveGameObjectToScene(owner,scene);
                void Remove(Transform t) { report.AppendLine("Removed " + Hierarchy(t)); Undo.DestroyObjectImmediate(t.gameObject); }
                Remove(oldShelter);
                foreach(var t in camp.Cast<Transform>().ToArray())
                {
                    string n=t.name;
                    if(n=="Habitat front deck" || n.StartsWith("Amber awning") || n.StartsWith("Awning warm") || n=="Canopy support" || n.StartsWith("Supply crate") || n.StartsWith("Crate ") || n.StartsWith("Local bounce") || (n.StartsWith("Lamp ·") && t.position.x < -24 && t.position.z < -27)) Remove(t);
                }
                string[] obsolete = {"Back wall","Left wall","Low solid ceiling","Front portal","Portal ","Hangar roof panel","Rear wall panel","Rear utility light","Roof inner rib","Cargo guide","Cargo route marking"};
                foreach(var t in hangarZone.Cast<Transform>().ToArray())
                    if(obsolete.Any(n=>t.name.StartsWith(n,StringComparison.Ordinal)) || (t.name.StartsWith("Lamp ·") && t.position.y>10)) Remove(t);
                foreach(var t in all.Where(t=>t && t.name.StartsWith("Hangar front cargo stop"))) Remove(t);
                Undo.RecordObject(slab.gameObject,"Retain structural foundation"); slab.name="Hangar foundation and approach deck";
                GameObject Place(string name, Vector3 p, float scale)
                {
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)),scene);
                    go.name="Astra " + name;
                    Undo.RegisterCreatedObjectUndo(go,"Place building");
                    go.transform.SetParent(owner.transform,false); go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,90,0)); go.transform.localScale=Vector3.one*scale;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                    report.AppendLine(name+" position="+p.ToString("F3")+" scale="+scale+" bounds="+Bounds(go).ToString("F3")); return go;
                }
                var shelter=Place("Shelter",new Vector3(-30.5f,.03f,-24),.7f);
                var hangar=Place("Hangar",new Vector3(-27,5.14f,27),.75f);
                // Existing ground ramp now meets a clear walkway around the south side to the east-facing cargo door.
                var access=all.Single(t=>t && t.name=="Hangar pedestrian access");
                Undo.RecordObject(access,"Connect pedestrian ramp");
                Vector3 a=new Vector3(-35,.05f,3), b=new Vector3(-29,5.38f,18.15f);
                access.position=(a+b)*.5f; access.rotation=Quaternion.LookRotation(b-a); access.localScale=new Vector3(3.2f,.4f,Vector3.Distance(a,b));
                PrefabUtility.RecordPrefabInstancePropertyModifications(access);
                GameObject Deck(string name,Vector3 p,Vector3 size)
                {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube); Undo.RegisterCreatedObjectUndo(go,"Connect walkway"); go.name=name;
                    go.transform.SetParent(owner.transform,false);go.transform.position=p;go.transform.localScale=size;go.layer=GameLayers.Ground;
                    go.GetComponent<Renderer>().sharedMaterial=deckMaterial;return go;
                }
                Deck("Hangar south approach walkway",new Vector3(-23.25f,5.25f,18.25f),new Vector3(14.1f,.3f,2.8f));
                Deck("Hangar east entrance landing",new Vector3(-17,5.25f,21.9f),new Vector3(3.5f,.3f,9.5f));
                // The landing replaces the obsolete outer cargo lip without blocking the open front.
                Deck("Walkway south curb",new Vector3(-23.5f,5.50f,16.94f),new Vector3(13.5f,.20f,.16f));
                Deck("Walkway east curb",new Vector3(-15.32f,5.50f,20.25f),new Vector3(.16f,.20f,6.8f));
                Undo.RecordObject(map.playerSpawn,"Start inside shelter");
                Undo.RecordObject(map.playerSpawn.gameObject,"Name shelter spawn");
                map.playerSpawn.SetPositionAndRotation(new Vector3(-31.8f,.346f,-24),Quaternion.Euler(0,90,0));
                map.playerSpawn.name="Player Spawn - inside Shelter";
                PrefabUtility.RecordPrefabInstancePropertyModifications(map.playerSpawn);
                foreach(var marker in all.Where(t=>t && t.name.StartsWith("Player Spawn ·")))
                { Undo.RecordObject(marker,"Align preview spawn");marker.SetPositionAndRotation(map.playerSpawn.position,map.playerSpawn.rotation); }
                foreach(var building in new[]{shelter,hangar})
                {
                    var lamp=new GameObject("Interior soft light"); Undo.RegisterCreatedObjectUndo(lamp,"Light interior");lamp.transform.SetParent(building.transform,false);
                    lamp.transform.localPosition=new Vector3(0,building==shelter?4.3f:5.8f,0);
                    var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.83f,.66f);light.intensity=1.4f;light.range=building==shelter?9:15;light.shadows=LightShadows.None;
                }
                Physics.SyncTransforms(); Undo.CollapseUndoOperations(undo); EditorSceneManager.MarkSceneDirty(scene);
                File.WriteAllText(Output+"/placement.txt",report.ToString());
                Polish();
            }
            catch { Undo.RevertAllDownToGroup(undo); throw; }
        }
        [MenuItem("Tools/Not This Planet/Buildings/Validate Astra Buildings")]
        public static void Validate()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Wrong scene.");
            var all=All(); var report=new StringBuilder(); Physics.SyncTransforms();
            if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new InvalidOperationException("Missing script in scene.");
            var owner=all.Single(t=>t.name=="Buildings - Authored FBX");
            foreach(var r in owner.GetComponentsInChildren<Renderer>())
                if(r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name!="Universal Render Pipeline/Lit"))throw new InvalidOperationException("Invalid material: "+r.name);
            if(all.Any(t=>t.name.StartsWith("Expedition shelter") || t.name=="Hangar roof panel" || t.name=="Shelter floor"))throw new InvalidOperationException("Old building remains.");
            var spawn=all.Select(t=>t.GetComponent<AuthoredMapSettings>()).Single(m=>m).playerSpawn;
            if(!Physics.Raycast(spawn.position+Vector3.up*.3f,Vector3.down,out var floor,1,GameLayers.GroundMask,QueryTriggerInteraction.Ignore) || floor.collider.name!="Walkable floor")throw new InvalidOperationException("Spawn lacks shelter floor.");
            if(Physics.CheckCapsule(spawn.position+Vector3.up*.45f,spawn.position+Vector3.up*1.6f,.32f,GameLayers.GroundMask,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Spawn intersects geometry.");
            report.AppendLine("Spawn "+spawn.position.ToString("F4")+" support="+floor.point.ToString("F4"));
            foreach(var point in new[]{new Vector3(-28,1.5f,-24),new Vector3(-26.5f,1.5f,-24),new Vector3(-25,1.5f,-24)})
                if(Physics.CheckCapsule(point,point+Vector3.up*.4f,.32f,GameLayers.GroundMask,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Shelter exit blocked "+point);
            foreach(var name in new[]{"Astra Shelter","Astra Hangar"})
            {
                var t=all.Single(t=>t.name==name); var b=Bounds(t.gameObject);
                report.AppendLine(name+" "+b.ToString("F3")+" colliders="+t.GetComponentsInChildren<Collider>().Length);
            }
            var parts=all.Where(t=>t.GetComponent<Metagame.Gameplay.Parts.SpaceshipPart>()).ToArray();
            if(parts.Length!=4)throw new InvalidOperationException("Expected four parts.");
            report.AppendLine("Parts=4; missing scripts=0; materials valid; old shells removed; shelter exit clear.");
            foreach(var p in parts)report.AppendLine(p.name+" "+p.position.ToString("F3"));
            Vector3[] approach={new Vector3(-29,0,18.25f),new Vector3(-17,0,18.25f),new Vector3(-17,0,27),new Vector3(-26,0,27)};
            int routeSamples=0;
            for(int segment=1;segment<approach.Length;segment++)
            {
                int steps=Mathf.CeilToInt(Vector3.Distance(approach[segment-1],approach[segment])/.25f);
                for(int i=0;i<=steps;i++)
                {
                    var p=Vector3.Lerp(approach[segment-1],approach[segment],i/(float)steps);
                    if(!Physics.Raycast(new Vector3(p.x,6.6f,p.z),Vector3.down,out var h,3,GameLayers.GroundMask,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Hangar path lacks floor: "+p);
                    if(Physics.CheckCapsule(h.point+Vector3.up*.45f,h.point+Vector3.up*1.6f,.3f,GameLayers.GroundMask,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Hangar walking path blocked: "+h.point);
                    routeSamples++;
                }
            }
            report.AppendLine("Hangar approach: "+routeSamples+" floor and body-clearance samples passed.");
            File.WriteAllText(Output+"/validation.txt",report.ToString());
        }
        public static void Capture()
        {
            var go=new GameObject("Building inspection camera");
            var cam=go.AddComponent<Camera>();cam.fieldOfView=55;cam.nearClipPlane=.05f;cam.farClipPlane=500;
            var rt=new RenderTexture(1600,1000,24); var previous=RenderTexture.active;
            try
            {
                cam.targetTexture=rt;
                void Shot(string name,Vector3 p,Vector3 look)
                {
                    cam.transform.position=p;cam.transform.LookAt(look);cam.Render();RenderTexture.active=rt;
                    var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                    File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
                Shot("shelter-in-scene",new Vector3(-12,13,-39),new Vector3(-29,2.4f,-24));
                Shot("shelter-spawn-view",new Vector3(-31.8f,1.9f,-24),new Vector3(-21,1.8f,-24));
                Shot("hangar-in-scene",new Vector3(-1,23,9),new Vector3(-25,8,27));
                Shot("hangar-interior",new Vector3(-16,7.6f,27),new Vector3(-30,7,27));
                Shot("building-map-overview",new Vector3(48,65,-65),new Vector3(-5,1,0));
            }
            finally {RenderTexture.active=previous;cam.targetTexture=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(go);}
        }
        public static void Polish()
        {
            var all=All();var hangar=all.Single(t=>t.name=="Astra Hangar");
            Undo.RecordObject(hangar,"Fit hangar around loading deck");
            hangar.position=new Vector3(-29,5.14f,27);hangar.localScale=new Vector3(.75f,.75f,1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hangar);
            var slab=all.Single(t=>t.name=="Hangar foundation and approach deck");
            Undo.RecordObject(slab,"Support expanded hangar");slab.position=new Vector3(-27.05f,5.15f,27);slab.localScale=new Vector3(23.1f,.5f,15.5f);
            var report=new StringBuilder();
            var bollards=all.Single(t=>t.name=="Safety Bollards - Imported FBX");
            foreach(Transform t in bollards)
            {
                if(t.name.StartsWith("Bollard 26") || t.name.StartsWith("Bollard 27"))
                {Undo.RecordObject(t,"Move camp lights clear of shelter walls");t.position=new Vector3(-24.2f,0,t.name.StartsWith("Bollard 26")?-26:-22);PrefabUtility.RecordPrefabInstancePropertyModifications(t);}
                if(t.name.StartsWith("Bollard 37"))
                {
                    Physics.SyncTransforms();
                    var roofHits=Physics.RaycastAll(new Vector3(t.position.x,18,t.position.z),Vector3.down,10,GameLayers.GroundMask,QueryTriggerInteraction.Ignore).Where(h=>h.transform.IsChildOf(hangar)).OrderBy(h=>h.distance).ToArray();
                    if(roofHits.Length>0)
                    {Undo.RecordObject(t,"Seat beacon on new hangar roof");t.position=new Vector3(t.position.x,roofHits[0].point.y+.01f,t.position.z);PrefabUtility.RecordPrefabInstancePropertyModifications(t);}
                }
            }
            foreach(Transform t in bollards)report.AppendLine(t.name+" "+t.position.ToString("F3")+" "+Bounds(t.gameObject).ToString("F3"));
            foreach(var t in all.Where(t=>t.name.Contains("CapsuleHangar")))foreach(var r in t.GetComponentsInChildren<Renderer>())report.AppendLine("Platform "+r.name+" "+r.bounds.ToString("F3"));
            Physics.SyncTransforms();
            // Follow the accessible side of the existing loading deck instead of crossing the part itself.
            Vector3[] route={new Vector3(-29,0,18.25f),new Vector3(-23,0,18.25f),new Vector3(-17,0,18.25f),new Vector3(-17,0,24),new Vector3(-21,0,27),new Vector3(-27,0,27)};
            foreach(var p in route)
            {
                bool hit=Physics.Raycast(new Vector3(p.x,6.6f,p.z),Vector3.down,out var h,3,GameLayers.GroundMask,QueryTriggerInteraction.Ignore);
                report.AppendLine("Route "+p+" ground="+(hit?h.point.ToString("F3")+" "+h.collider.name:"MISSING"));
                if(!hit)throw new InvalidOperationException("Unsupported hangar approach: "+p);
            }
            File.WriteAllText(Output+"/polish.txt",report.ToString());
            RestorePart3Beacon();
            Validate();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        public static void RestorePart3Beacon()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Open Astra in edit mode.");
            var all=All();
            var zone=all.Single(t=>t.name.StartsWith("06 · NORTHWEST"));
            var lamp=all.Single(t=>t.name.StartsWith("Bollard 37 -"));
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("3ca2442b361a4f3f94d863cfdde4ed56"));
            if(!material)throw new InvalidOperationException("Original Part3 beacon material missing.");
            var existing=zone.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Module beacon")).ToArray();
            if(existing.Length>1)throw new InvalidOperationException("Duplicate Part3 beacons.");
            GameObject beam;
            if(existing.Length==1)beam=existing[0].gameObject;
            else
            {
                beam=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Undo.RegisterCreatedObjectUndo(beam,"Restore Part3 navigation beacon");
                Object.DestroyImmediate(beam.GetComponent<Collider>());
                beam.transform.SetParent(zone,false);
            }
            Undo.RecordObject(beam.transform,"Align Part3 beacon above roof lamp");
            Undo.RecordObject(beam,"Restore Part3 beacon name and visibility");
            beam.name="Module beacon · visual guide";beam.SetActive(true);
            var lampBounds=Bounds(lamp.gameObject);
            beam.transform.SetPositionAndRotation(new Vector3(lampBounds.center.x,lampBounds.max.y+5,lampBounds.center.z),Quaternion.identity);
            beam.transform.localScale=new Vector3(.075f,5,.075f);
            var renderer=beam.GetComponent<MeshRenderer>();Undo.RecordObject(renderer,"Restore original orchid beacon material");
            renderer.sharedMaterial=material;renderer.enabled=true;
            var b=renderer.bounds;
            if(Mathf.Abs(b.size.y-10)>.01f || b.min.y<lampBounds.max.y-.01f || beam.GetComponent<Collider>())throw new InvalidOperationException("Invalid Part3 beacon geometry.");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText(Output+"/beacon-validation.txt","Part3 beacon restored; count=1; material="+material.name+"; bounds="+b.ToString("F3")+"; roofLampTop="+lampBounds.max.y.ToString("F3")+"; collider=none\n");
        }
        public static void PlayCheck()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Already playing.");
            Validate();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            SessionState.SetBool("AstraBuildings.PlayCheck",true);
            SessionState.SetBool("AstraBuildings.SpawnCaptured",false);
            File.WriteAllText(Output+"/playcheck.txt","Starting solo spawn and exit check\n");
            EditorApplication.EnterPlaymode();
        }
        static void PollPlay()
        {
            if(!SessionState.GetBool("AstraBuildings.PlayCheck",false)||!EditorApplication.isPlaying)return;
            try
            {
                var boot=Object.FindFirstObjectByType<Metagame.Bootstrap.SpaceBootstrap>();
                if(!boot || !boot.Player) {if(Time.timeSinceLevelLoad>25)throw new InvalidOperationException("Player was not created.");return;}
                var player=boot.Player;var session=Object.FindFirstObjectByType<GameSession>();
                session.ScriptedInput=true;player.Cam.Yaw=90;
                float t=Time.timeSinceLevelLoad;
                if(t<4) {player.SetInput(new Metagame.Core.InputFrame());return;}
                if(!SessionState.GetBool("AstraBuildings.SpawnCaptured",false))
                {
                    File.AppendAllText(Output+"/playcheck.txt","Settled spawn foot="+player.FootPosition.ToString("F3")+" camera="+player.Cam.transform.position.ToString("F3")+"\n");
                    if(Vector3.Distance(player.FootPosition,new Vector3(-31.8f,.296f,-24))>1.5f)throw new InvalidOperationException("Spawn did not settle inside shelter.");
                    SaveCamera(player.Cam.Cam,"play-spawn");
                    SessionState.SetBool("AstraBuildings.SpawnCaptured",true);
                }
                player.SetInput(new Metagame.Core.InputFrame {Move=t<7.5f?new Vector2(0,1):Vector2.zero});
                if(t<9)return;
                File.AppendAllText(Output+"/playcheck.txt","Exit foot="+player.FootPosition.ToString("F3")+"\n");
                SaveCamera(player.Cam.Cam,"play-exit");
                if(player.FootPosition.x < -24.1f || player.FootPosition.y < -1 || Mathf.Abs(player.FootPosition.z+24)>2)throw new InvalidOperationException("Player could not walk out of Shelter.");
                File.AppendAllText(Output+"/playcheck.txt","PASS: actual player spawned, settled, and walked through shelter exit.\n");
                player.SetInput(new Metagame.Core.InputFrame());session.ScriptedInput=false;
                SessionState.SetBool("AstraBuildings.PlayCheck",false);EditorApplication.ExitPlaymode();
            }
            catch(Exception e)
            {
                File.AppendAllText(Output+"/playcheck.txt","FAIL "+e+"\n");SessionState.SetBool("AstraBuildings.PlayCheck",false);EditorApplication.ExitPlaymode();
            }
        }
        static void SaveCamera(Camera camera,string name)
        {
            var old=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1600,1000,24);
            try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}
            finally {camera.targetTexture=old;RenderTexture.active=active;Object.DestroyImmediate(rt);}
        }
    }
}
