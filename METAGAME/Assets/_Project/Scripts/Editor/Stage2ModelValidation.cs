using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>Imports the Stage2 model library with shared URP materials and verifies exported meshes.</summary>
    public static class Stage2ModelValidation
    {
        const string Folder = "Assets/_Project/Models/Stage2";
        static readonly Dictionary<string, Color> Palette = new()
        {
            {"Ivory",new Color(.70f,.72f,.62f)}, {"Teal",new Color(.075f,.22f,.24f)},
            {"Orange",new Color(.84f,.39f,.055f)}, {"Steel",new Color(.15f,.18f,.18f)},
            {"Dark",new Color(.025f,.036f,.04f)}, {"Concrete",new Color(.37f,.39f,.34f)},
            {"Rust",new Color(.34f,.12f,.045f)}, {"Glass",new Color(.025f,.22f,.36f)},
            {"Olive",new Color(.20f,.27f,.065f)}, {"LeafLight",new Color(.34f,.38f,.12f)},
            {"Dry",new Color(.48f,.31f,.085f)}, {"Bark",new Color(.19f,.12f,.055f)},
            {"Flower",new Color(.86f,.84f,.66f)}, {"Lamp",new Color(1f,.68f,.19f)},
            {"Moss",new Color(.11f,.17f,.035f)}
        };
        [Serializable] sealed class Entry
        { public string asset; public int meshes, triangles; public Vector3 dimensions; public bool materialsValid, uvValid, finite; }
        [Serializable] sealed class Report
        { public string unityVersion; public int modelCount; public bool passed; public List<Entry> models = new(); }

        [MenuItem("Not This Planet/Validation/Import and Verify Stage2 Models")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Folder+"/Materials")) AssetDatabase.CreateFolder(Folder,"Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            var materials = new Dictionary<string,Material>();
            foreach (var item in Palette)
            {
                string name="S2_"+item.Key, path=Folder+"/Materials/M_"+name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat=new Material(shader); AssetDatabase.CreateAsset(mat,path); }
                mat.shader=shader; mat.SetColor("_BaseColor",item.Value);
                mat.SetFloat("_Smoothness",item.Key=="Glass"?.8f:.36f);
                mat.SetFloat("_Metallic",new[]{"Steel","Teal","Orange","Rust"}.Contains(item.Key)?.45f:.02f);
                if(item.Key=="Lamp") { mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",item.Value*2); }
                EditorUtility.SetDirty(mat); materials.Add(name,mat);
            }
            AssetDatabase.SaveAssets();
            var report=new Report{unityVersion=Application.unityVersion};
            foreach (string path in Directory.GetFiles(Folder,"*.fbx").OrderBy(p=>p))
            {
                string assetPath=path.Replace('\\','/');
                var importer=(ModelImporter)AssetImporter.GetAtPath(assetPath);
                importer.globalScale=1; importer.useFileScale=true; importer.importAnimation=false;
                importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
                importer.isReadable=false;
                foreach(var pair in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
                importer.SaveAndReimport();
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var instance=UnityEngine.Object.Instantiate(source);
                try
                {
                    var meshes=instance.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).Where(m=>m!=null).ToArray();
                    var renderers=instance.GetComponentsInChildren<MeshRenderer>();
                    var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    var entry=new Entry{asset=Path.GetFileName(path),meshes=meshes.Length,
                        triangles=meshes.Sum(m=>(int)m.GetIndexCount(0)/3),dimensions=bounds.size,
                        materialsValid=renderers.All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader==shader)),
                        uvValid=meshes.All(m=>m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)),
                        finite=meshes.All(m=>!float.IsNaN(m.bounds.size.sqrMagnitude)&&!float.IsInfinity(m.bounds.size.sqrMagnitude))};
                    entry.triangles=meshes.Sum(m=>Enumerable.Range(0,m.subMeshCount).Sum(s=>(int)m.GetIndexCount(s)/3));
                    report.models.Add(entry);
                }
                finally {UnityEngine.Object.DestroyImmediate(instance);}
            }
            report.modelCount=report.models.Count;
            report.passed=report.modelCount==40&&report.models.All(e=>e.meshes>0&&e.materialsValid&&e.uvValid&&e.finite);
            Directory.CreateDirectory("../artifacts/stage2-models");
            File.WriteAllText("../artifacts/stage2-models/unity-import-validation.json",JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException("Stage2 model import validation failed; see report.");
            Debug.Log("[Stage2Models] 40 FBX models imported; URP materials, UVs and finite bounds verified.");
        }
    }
}
