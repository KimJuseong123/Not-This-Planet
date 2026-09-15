using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Incremental P01 visual replacement; leaves authored physics and IDs intact.</summary>
    public static class AstraP01RollerArt
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra_ver2.unity";
        const string Art = "Assets/_Project/Art/AstraP01";
        const string VisualName = "P01 Blender Visual";
        static readonly string[] Variants = { "Standard", "Heavy", "Practice", "Interior" };
        static readonly float[] Lengths = { 3, 3.2f, 1.6f, 1.8f };
        static readonly float[] Diameters = { .28f, .8f, .32f, .32f };

        static Bounds LocalBounds(GameObject obj)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var mf in obj.GetComponentsInChildren<MeshFilter>(true))
            foreach (var v in mf.sharedMesh.vertices)
            {
                var p = obj.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
            return bounds;
        }

        static void Require(bool valid, string message)
        { if (!valid) throw new InvalidOperationException(message); }

        static GameObject Import(string name)
        {
            string path = Art + "/" + name + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale = 1; importer.useFileScale = true;
            importer.importAnimation = false; importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Material MakeMaterial(string name, Color color, float metal, float smooth)
        {
            string path = Art + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name; mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metal); mat.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(mat, path); return mat;
        }

        static string PhysicsSnapshot(GameObject root)
        {
            var s = new StringBuilder();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == VisualName || HasVisualParent(t)) continue;
                s.Append(t.name).Append(t.localPosition).Append(t.localRotation).Append(t.localScale);
                foreach (var c in t.GetComponents<Component>())
                    if (c is Collider || c is Rigidbody || c is MonoBehaviour)
                        s.Append(c.GetType().FullName).Append(EditorJsonUtility.ToJson(c));
            }
            return s.ToString();
        }
        static bool HasVisualParent(Transform t)
        { for (var p=t.parent;p!=null;p=p.parent) if(p.name==VisualName)return true; return false; }

        // Batch entry point. Call only on the intended project; refuses an unexpected inventory.
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Require(!EditorApplication.isPlaying, "Edit mode required");
            var all = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var targets = all.Where(t=> t.name.StartsWith("Manual roller ") || t.name=="Cargo roller"
                || t.name=="Roller cylinder" || t.name=="Short workshop roller").ToArray();
            Require(targets.Length==19,"Expected exactly 19 P01 cylinders, got "+targets.Length);
            var calibration = Import("P01_OneMeterCalibration");
            Require(Vector3.Distance(LocalBounds(calibration).size, Vector3.one)<.001f,"FBX meter calibration failed");
            var names = new[]{"Body","Accent","EndCap","Steel"};
            var mats = new[]{MakeMaterial("Body",new Color(.69f,.75f,.73f),.35f,.7f),
                MakeMaterial("Accent",new Color(.95f,.24f,.045f),.15f,.66f),
                MakeMaterial("EndCap",new Color(.10f,.15f,.18f),.65f,.7f),
                MakeMaterial("Steel",new Color(.38f,.46f,.49f),.8f,.75f)};
            var prefabs = new GameObject[4];
            for (int i=0;i<4;i++)
            {
                string name="AST_P01_Roller_"+Variants[i];
                var imported=Import(name);
                var b=LocalBounds(imported);
                Require(Vector3.Distance(b.size,new Vector3(Diameters[i],Lengths[i],Diameters[i]))<.001f,
                    name+" incorrect imported axis/dimensions: "+b.size);
                Require(b.center.magnitude<.001f,name+" off-center pivot");
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(imported);
                foreach(var r in instance.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterials=r.sharedMaterials.Select(m=>{
                        int index=Array.FindIndex(names,n=>m.name==n || m.name.StartsWith(n+"."));
                        Require(index>=0,"Unknown material "+m.name); return mats[index]; }).ToArray();
                prefabs[i]=PrefabUtility.SaveAsPrefabAsset(instance,Art+"/"+name+".prefab");
                Object.DestroyImmediate(instance);
            }
            var report=new StringBuilder("P01: 1m calibration PASS; Blender +Z imports to Unity +Y; centered pivots PASS\n");
            int[] count=new int[4];
            foreach(var t in targets)
            {
                int i=t.name.StartsWith("Manual roller ")?0:t.name=="Cargo roller"?1:t.name=="Roller cylinder"?2:3;
                count[i]++;
                var rb=t.GetComponentInParent<Rigidbody>(); Require(rb!=null,"Missing rigidbody "+t.name);
                string before=PhysicsSnapshot(rb.gameObject);
                var old=t.GetComponent<MeshRenderer>(); Require(old!=null,"Missing original renderer");
                var originalBounds=old.bounds;
                Require(t.Find(VisualName)==null,"P01 already applied: "+t.name);
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefabs[i],t);
                visual.name=VisualName;
                visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;
                visual.transform.localScale=new Vector3(1/Diameters[i],2/Lengths[i],1/Diameters[i]);
                foreach(var child in visual.GetComponentsInChildren<Transform>())child.gameObject.layer=t.gameObject.layer;
                old.enabled=false;
                Require(visual.GetComponentsInChildren<Collider>().Length==0,"Visual collider added");
                var newBounds=visual.GetComponentsInChildren<Renderer>()[0].bounds;
                Require(Vector3.Distance(originalBounds.center,newBounds.center)<.002f &&
                    Vector3.Distance(originalBounds.size,newBounds.size)<.002f,"Visual bounds mismatch "+t.name);
                Require(before==PhysicsSnapshot(rb.gameObject),"Authored physics or scripts changed: "+t.name);
                Require(rb.mass==new[]{3f,10f,2f,3f}[i],"Unexpected original mass");
                report.AppendLine(Variants[i]+" | "+t.parent.name+"/"+t.name+" | "+newBounds.size+" | mass="+rb.mass);
            }
            Require(count.SequenceEqual(new[]{12,3,2,2}),"Variant count mismatch");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ScenePath);
            var restored=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(t=>t.name==VisualName).ToArray();
            Require(restored.Length==19,"Reload lost visuals");
            foreach(var v in restored)
            {
                Require(!v.parent.GetComponent<MeshRenderer>().enabled,"Original renderer active");
                foreach(var r in v.GetComponentsInChildren<MeshRenderer>())
                    Require(r.sharedMaterials.All(m=>m!=null && m.shader.name=="Universal Render Pipeline/Lit"),"Invalid URP material");
            }
            Directory.CreateDirectory("Validation/P01");
            File.WriteAllText("Validation/P01/integration.txt",report+"Scene reload: 19 visuals and all URP materials PASS\n");
            RenderScenePreviews();
            Debug.Log("P01_APPLY_PASS: 19 rollers, calibration, dimensions, bounds, materials and reload verified");
        }

        public static void RenderScenePreviews()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var restored=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Where(t=>t.name==VisualName).ToArray();
            Directory.CreateDirectory("Validation/P01");
            RenderPreview(restored.First(t=>t.parent.name=="Cargo roller"),"heavy-in-scene");
            RenderPreview(restored.First(t=>t.parent.name.StartsWith("Manual roller ")),"standard-in-scene");
            RenderPreview(restored.First(t=>t.parent.name=="Roller cylinder"),"practice-in-scene");
            RenderPreview(restored.First(t=>t.parent.name=="Short workshop roller"),"interior-in-scene");
        }

        static void RenderPreview(Transform visual,string name)
        {
            var bounds=visual.GetComponentsInChildren<Renderer>()[0].bounds;
            var go=new GameObject("P01 temporary inspection camera");var camera=go.AddComponent<Camera>();
            go.transform.position=bounds.center+new Vector3(2.8f,2,-3.1f);
            go.transform.LookAt(bounds.center);camera.nearClipPlane=.05f;camera.farClipPlane=250;
            camera.fieldOfView=48;
            var rt=new RenderTexture(1200,800,24);camera.targetTexture=rt;
            for(int frame=0;frame<4;frame++)camera.Render();
            var previous=RenderTexture.active;RenderTexture.active=rt;
            var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,1200,800),0,0);tex.Apply();
            File.WriteAllBytes("Validation/P01/"+name+".png",tex.EncodeToPNG());
            RenderTexture.active=previous;camera.targetTexture=null;
            Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);
        }
    }
}
