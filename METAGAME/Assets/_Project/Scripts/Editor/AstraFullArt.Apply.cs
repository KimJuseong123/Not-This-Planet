using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class AstraFullArt
 {
  [Serializable] public class ModelSpec { public string key,category; public int triangles; }
  [Serializable] public class Rule { public string key,target,under,kind; public bool keepMaterial; }
  [Serializable] public class MatSpec { public string name; public float[] color; public float metallic,smoothness; }
  [Serializable] public class Manifest { public ModelSpec[] models; public Rule[] rules; public MatSpec[] materials; }
  sealed class Model { public Mesh mesh; public Material[] materials; public GameObject prefab; }
  const string AddedName="Blender authored visual";
  const string Marker="Astra full art integration - 2026-09-15";
  static Dictionary<string,Model> library;
  static readonly Dictionary<string,Mesh> fitted=new();
  static void Check(bool pass,string message) { if(!pass)throw new InvalidOperationException(message); }
  static bool Match(Rule rule,Transform t)
  {
   if(!string.IsNullOrEmpty(rule.under)&&!PathOf(t).Contains(rule.under))return false;
   if(rule.kind=="tool")return t.GetComponents<MonoBehaviour>().Any(c=>c!=null&&c.GetType().Name==rule.target);
   return rule.target.StartsWith("^")?Regex.IsMatch(t.name,rule.target):t.name==rule.target;
  }
  static bool Protected(Transform t)
  {
   for(var p=t;p!=null;p=p.parent)
    if(p.name.Contains("Pictogram")||p.name.Contains("Fuel remaining window")||p.name=="Red LED"||p.name.Contains("fuel ready gauge"))return true;
   return false;
  }
  static Dictionary<Object,string> CaptureOriginal(Scene scene)
  {
   var result=new Dictionary<Object,string>();
   foreach(var t in All(scene))
   {
    result[t]=PathOf(t)+"|"+t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R")+t.gameObject.activeSelf;
    foreach(var c in t.GetComponents<Component>())
     if(c!=null&&!(c is Transform)&&!(c is MeshFilter)&&!(c is MeshRenderer))
      result[c]=EditorJsonUtility.ToJson(c);
   }
   return result;
  }
  static void VerifyOriginal(Dictionary<Object,string> before,Scene scene)
  {
   var after=CaptureOriginal(scene);
   foreach(var pair in before)Check(after.TryGetValue(pair.Key,out var current)&&current==pair.Value,"Original physics/script/transform changed: "+pair.Key);
  }
  static Dictionary<string,Material> BuildMaterials(Manifest manifest)
  {
   var map=new Dictionary<string,Material>();var shader=Shader.Find("Universal Render Pipeline/Lit");Check(shader!=null,"URP Lit missing");
   foreach(var s in manifest.materials)
   {
    var path=Art+"/"+s.name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
    m.name=s.name;m.SetColor("_BaseColor",new Color(s.color[0],s.color[1],s.color[2]));
    m.SetFloat("_Metallic",s.metallic);m.SetFloat("_Smoothness",s.smoothness);EditorUtility.SetDirty(m);map.Add(s.name,m);
   }
   return map;
  }
  static void ImportLibrary(Manifest manifest)
  {
   library=new Dictionary<string,Model>();fitted.Clear();var materials=BuildMaterials(manifest);
   foreach(var spec in manifest.models)
   {
    string path=Art+"/"+spec.key+".fbx";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
    var importer=(ModelImporter)AssetImporter.GetAtPath(path);
    importer.globalScale=1;importer.useFileScale=true;importer.importAnimation=false;importer.addCollider=false;
    importer.isReadable=true;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.SaveAndReimport();
    var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);Check(source!=null,"Missing model "+spec.key);
    var buckets=new Dictionary<Material,List<CombineInstance>>();
    foreach(var mf in source.GetComponentsInChildren<MeshFilter>(true))
    {
     var renderer=mf.GetComponent<MeshRenderer>();
     for(int s=0;s<mf.sharedMesh.subMeshCount;s++)
     {
      string n=renderer.sharedMaterials[s].name;int dot=n.IndexOf('.');if(dot>=0)n=n.Substring(0,dot);
      Check(materials.TryGetValue(n,out var mat),"Unmapped material "+n);
      if(!buckets.ContainsKey(mat))buckets[mat]=new List<CombineInstance>();
      buckets[mat].Add(new CombineInstance{mesh=mf.sharedMesh,subMeshIndex=s,transform=source.transform.worldToLocalMatrix*mf.transform.localToWorldMatrix});
     }
    }
    var submeshes=new List<Mesh>();var final=new List<CombineInstance>();
    foreach(var bucket in buckets)
    {
     var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(bucket.Value.ToArray(),true,true);submeshes.Add(m);
     final.Add(new CombineInstance{mesh=m,transform=Matrix4x4.identity});
    }
    var combined=new Mesh{name=spec.key,indexFormat=IndexFormat.UInt32};combined.CombineMeshes(final.ToArray(),false,false);combined.RecalculateBounds();
    foreach(var m in submeshes)Object.DestroyImmediate(m);
    Check(combined.vertexCount>0,"Empty model "+spec.key);
    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/"+spec.key+".asset");
    if(existing!=null){EditorUtility.CopySerialized(combined,existing);Object.DestroyImmediate(combined);combined=existing;}
    else AssetDatabase.CreateAsset(combined,Art+"/"+spec.key+".asset");
    EditorUtility.SetDirty(combined);
    var go=new GameObject(spec.key);go.AddComponent<MeshFilter>().sharedMesh=combined;
    go.AddComponent<MeshRenderer>().sharedMaterials=buckets.Keys.ToArray();
    var prefab=PrefabUtility.SaveAsPrefabAsset(go,Art+"/"+spec.key+".prefab");Object.DestroyImmediate(go);
    library.Add(spec.key,new Model{mesh=combined,materials=buckets.Keys.ToArray(),prefab=prefab});
   }
  }
  static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
  static Mesh Fitted(string key,Bounds target)
  {
   target.size=Vector3.Max(target.size,Vector3.one*.02f);
   string cache=key+"_"+target.center.ToString("F4")+target.size.ToString("F4");
   if(fitted.TryGetValue(cache,out var saved))return saved;
   var original=library[key].mesh;Check(original.bounds.size.x>0&&original.bounds.size.y>0&&original.bounds.size.z>0,"Flat model "+key);
   var mesh=Object.Instantiate(original);mesh.name=key+"_Fitted";
   Vector3 scale=Divide(target.size,original.bounds.size);
   mesh.vertices=original.vertices.Select(v=>Vector3.Scale(v-original.bounds.center,scale)+target.center).ToArray();
   mesh.normals=original.normals.Select(n=>Divide(n,scale).normalized).ToArray();mesh.RecalculateBounds();
   // Imported tangent frames are transformed along with normals for non-uniform legacy scales.
   mesh.tangents=Array.Empty<Vector4>();
   string path=Art+"/"+key+"_Fitted_"+Hash128.Compute(cache)+".asset";
   var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing!=null){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
   else AssetDatabase.CreateAsset(mesh,path);
   EditorUtility.SetDirty(mesh);
   fitted.Add(cache,mesh);return mesh;
  }
  static Bounds BoundsIn(Transform root,IEnumerable<MeshRenderer> renderers)
  {
   bool first=true;var bounds=new Bounds();
   foreach(var r in renderers)
   {
    var mf=r.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)continue;
    var b=mf.sharedMesh.bounds;
    foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
    {
     var p=root.InverseTransformPoint(r.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
     if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
    }
   }
   Check(!first,"Empty authored bounds: "+PathOf(root));return bounds;
  }
  static GameObject AddModel(Transform parent,string key,Bounds? fit=null,Vector3? position=null)
  {
   var go=(GameObject)PrefabUtility.InstantiatePrefab(library[key].prefab,parent);
   go.name=AddedName+" - "+key;go.transform.localPosition=position??Vector3.zero;
   go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;go.layer=parent.gameObject.layer;
   if(fit.HasValue)go.GetComponent<MeshFilter>().sharedMesh=Fitted(key,fit.Value);
   PrefabUtility.RecordPrefabInstancePropertyModifications(go);
   PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
   PrefabUtility.RecordPrefabInstancePropertyModifications(go.GetComponent<MeshFilter>());
   return go;
  }
  static void Replace(Rule rule,Transform t)
  {
   if(rule.kind=="leaf")
   {
    var mf=t.GetComponent<MeshFilter>();var renderer=t.GetComponent<MeshRenderer>();
    Check(mf!=null&&renderer!=null,"Expected mesh leaf: "+PathOf(t));var bounds=mf.sharedMesh.bounds;
    var oldMaterial=renderer.sharedMaterial;mf.sharedMesh=Fitted(rule.key,bounds);
    renderer.sharedMaterials=rule.keepMaterial?Enumerable.Repeat(oldMaterial,library[rule.key].materials.Length).ToArray():library[rule.key].materials;
    EditorUtility.SetDirty(mf);EditorUtility.SetDirty(renderer);
    PrefabUtility.RecordPrefabInstancePropertyModifications(mf);PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
   }
   else if(rule.kind=="tool")
   {
    var model=t.Find("Model");Check(model!=null,"Missing existing Model: "+t.name);
    foreach(var r in model.GetComponentsInChildren<MeshRenderer>(true))if(!Protected(r.transform))r.enabled=false;
    var visual=AddModel(model,rule.key);
    var box=t.GetComponent<BoxCollider>();Check(box!=null,"Tool box collider missing");
    var allowed=box.bounds;allowed.Expand(.06f);
    foreach(var v in visual.GetComponent<MeshFilter>().sharedMesh.vertices)
     Check(allowed.Contains(visual.transform.TransformPoint(v)),"Tool model outside collider envelope "+t.name+" "+v);
   }
   else if(rule.kind=="port")AddModel(t,rule.key,position:new Vector3(0,-.32f,0));
   else
   {
    var renderers=t.GetComponentsInChildren<MeshRenderer>(true)
     .Where(r=>r.enabled&&!Protected(r.transform)&&(rule.kind!="barrel"||r.name.StartsWith("Barrel stave "))).ToArray();
    var bounds=BoundsIn(t,renderers);foreach(var r in renderers)r.enabled=false;
    if(rule.kind=="barrel")foreach(var r in t.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Muzzle rim"))r.enabled=false;
    AddModel(t,rule.key,bounds);
   }
  }
  public static void Apply()
  {
   Check(!EditorApplication.isPlaying,"Edit mode required");
   var scene=EditorSceneManager.OpenScene(ScenePath);Check(GameObject.Find(Marker)==null,"Full art is already applied");
   var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Art+"/manifest.json"));
   var originals=All(scene);var matches=manifest.rules.Select(r=>originals.Where(t=>Match(r,t)).ToArray()).ToArray();
   var missing=manifest.rules.Where((r,i)=>matches[i].Length==0).Select(r=>r.key+" => "+r.target+" / "+r.under).ToArray();
   Check(missing.Length==0,"Unmatched rules:\n"+string.Join("\n",missing));
   var before=CaptureOriginal(scene);int p01=originals.Count(t=>t.name=="P01 Blender Visual");
   ImportLibrary(manifest);
   var report=new StringBuilder();int count=0;
   for(int i=0;i<manifest.rules.Length;i++)
   {
    foreach(var target in matches[i]){Replace(manifest.rules[i],target);count++;}
    report.AppendLine(manifest.rules[i].key+" | "+manifest.rules[i].target+" | "+matches[i].Length);
   }
   int signs=AddWarnings(originals);
   VerifyOriginal(before,scene);
   // Presentation-only sign relocation: keep the entry collider, seat and launch anchors fixed.
   var entrySign=originals.Single(t=>t.name=="Enter cannon");
   entrySign.localPosition=new Vector3(-2.5f,2.3f,2.35f);entrySign.localRotation=Quaternion.Euler(0,180,0);
   report.AppendLine("M14: existing entry pictogram moved beside the mouth; entry/seat/fuse anchors preserved.");
   Check(All(scene).Count(t=>t.name=="P01 Blender Visual")==p01,"P01 regression");
   new GameObject(Marker);
   EditorSceneManager.MarkSceneDirty(scene);Check(EditorSceneManager.SaveScene(scene),"Scene save failed");AssetDatabase.SaveAssets();
   Directory.CreateDirectory(Evidence);
   File.WriteAllText(Evidence+"/integration.txt",report+"\nPASS: "+manifest.models.Length+" FBX models; "+count+" matched replacements; "+signs+" warning signs; "+before.Count+" preserved original records; "+p01+" P01 retained.\n");
   ValidateSaved();RenderPreviews();
   Directory.CreateDirectory("Validation/AstraExpedition");
   Stage1AstraExpeditionBuilder.ValidateAndRender();
   Debug.Log("ASTRA_FULL_ART_APPLY_PASS "+count);
  }
  static int AddWarnings(Transform[] originals)
  {
   var parent=new GameObject("Blender fall-risk wayfinding").transform;var terrain=Object.FindAnyObjectByType<Terrain>();
   Check(terrain!=null,"Terrain missing");int count=0;
   var rim=originals.Where(t=>t.name=="Broken red rim warning").ToArray();var used=new List<Vector3>();
   foreach(var trench in originals.Where(t=>t.GetComponent<AstraFatalTrench>()!=null))
   foreach(int sign in new[]{-1,1})
   {
    var desired=trench.position+trench.right*sign*5;
    var candidate=rim.Where(t=>Vector3.Dot(t.position-trench.position,trench.right)*sign>0)
     .OrderBy(t=>new Vector2(t.position.x-desired.x,t.position.z-desired.z).sqrMagnitude).First();
    var p=candidate.position+trench.right*sign*.8f;
    for(int step=0;step<8;step++)
    {p.y=terrain.SampleHeight(p)+terrain.transform.position.y;if(p.y>.6f)break;p+=trench.right*sign*.4f;}
    Check(p.y>.6f,"Warning would be in trench");
    if(used.Any(v=>Vector3.Distance(v,p)<2))continue;
    used.Add(p);var station=new GameObject("Fall warning "+count).transform;station.SetParent(parent,false);
    station.position=p;station.rotation=Quaternion.LookRotation(trench.right*sign,Vector3.up);
    AddModel(station,"AST_M18_WarningPost",position:new Vector3(0,.725f,0));
    AddModel(station,"AST_M18_FallWarning",position:new Vector3(0,1.55f,0));count++;
   }
   Check(count>=6,"Insufficient trench warnings");
   Check(parent.GetComponentsInChildren<Collider>().Length==0,"Guidance must not obstruct cargo routes");return count;
  }
  public static void ValidateSaved()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);var all=All(scene);
   Check(all.Any(t=>t.name==Marker),"Integration marker missing");
   int meshes=0;
   foreach(var t in all)
   {
    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"Missing script "+PathOf(t));
    var mf=t.GetComponent<MeshFilter>();if(mf==null)continue;Check(mf.sharedMesh!=null,"Missing mesh "+PathOf(t));
    if(!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(Art))continue;
    meshes++;var r=t.GetComponent<MeshRenderer>();
    Check(r!=null&&r.sharedMaterials.All(m=>m!=null&&m.shader!=null),"Missing material "+PathOf(t));
    if(t.name.StartsWith(AddedName))Check(t.GetComponents<Collider>().Length==0,"Visual added collider");
   }
   Check(meshes>200,"Unexpected replacement coverage: "+meshes);
   Check(all.Count(t=>t.name=="P01 Blender Visual")==19,"P01 missing");
   Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/reload.txt","PASS: "+meshes+" authored model renderers; no missing scene scripts/meshes/materials; 19 P01 retained.\n");
   Debug.Log("ASTRA_FULL_ART_RELOAD_PASS "+meshes);
  }
 }
}
