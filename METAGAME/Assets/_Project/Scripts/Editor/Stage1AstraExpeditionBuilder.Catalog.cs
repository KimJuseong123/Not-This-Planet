using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class Stage1AstraExpeditionBuilder
 {
  static string Hierarchy(Transform t)=>t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name;
  static string Csv(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
  static string Vec(Vector3 v)=>v.x.ToString("0.###",CultureInfo.InvariantCulture)+" "+v.y.ToString("0.###",CultureInfo.InvariantCulture)+" "+v.z.ToString("0.###",CultureInfo.InvariantCulture);
  public static void ExportModelCatalog()
  {
   EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory("Validation/AstraAffordance");
   var csv=new StringBuilder("name,components,hierarchy,world_position,world_euler,root_scale,visual_local_min,visual_local_max,collider_world_size,mass,child_names\n");
   var objects=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t=>t.GetComponent<Grabbable>()!=null || t.GetComponent<SpaceshipPart>()!=null
    || t.GetComponent<AstraV2Puzzle>()!=null || t.GetComponent<AstraV2FreightLift>()!=null || t.GetComponent<AstraBlastGate>()!=null || t.GetComponent<AstraPlayerCannon>()!=null
    || t.GetComponent<AstraMinefield>()!=null || t.GetComponent<AstraFatalTrench>()!=null).OrderBy(Hierarchy).ToArray();
   foreach(var t in objects)
   {
    var bounds=new Bounds();bool first=true;
    foreach(var mf in t.GetComponentsInChildren<MeshFilter>(true))
    {
     if(mf.sharedMesh==null)continue;var b=mf.sharedMesh.bounds;
     foreach(float x in new[]{b.min.x,b.max.x})foreach(float y in new[]{b.min.y,b.max.y})foreach(float z in new[]{b.min.z,b.max.z})
     {var p=t.InverseTransformPoint(mf.transform.TransformPoint(new Vector3(x,y,z)));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
    }
    var cols=t.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger).ToArray();var cb=new Bounds(t.position,Vector3.zero);if(cols.Length>0){cb=cols[0].bounds;foreach(var c in cols.Skip(1))cb.Encapsulate(c.bounds);}
    var body=t.GetComponent<Rigidbody>();
    var values=new[]{t.name,string.Join(" | ",t.GetComponents<MonoBehaviour>().Where(c=>c!=null).Select(c=>c.GetType().Name)),Hierarchy(t),Vec(t.position),Vec(t.eulerAngles),Vec(t.localScale),Vec(bounds.min),Vec(bounds.max),Vec(cb.size),body!=null?body.mass.ToString(CultureInfo.InvariantCulture):"",string.Join(" | ",t.GetComponentsInChildren<Transform>(true).Skip(1).Select(c=>c.name))};
    csv.AppendLine(string.Join(",",values.Select(Csv)));
   }
   File.WriteAllText("Validation/AstraAffordance/scene-model-inventory.csv",csv.ToString(),new UTF8Encoding(true));
   File.WriteAllLines("Validation/AstraAffordance/world-text.txt",Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include).Select(t=>Hierarchy(t.transform)+" = "+t.text.Replace("\n"," / ")));
   File.WriteAllLines("Validation/AstraAffordance/hierarchy.txt",Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t=>t.parent==null||t.parent.parent==null).OrderBy(Hierarchy).Select(t=>Hierarchy(t)+" @ "+Vec(t.position)));
   Debug.Log("ASTRA MODEL CATALOG EXPORTED "+objects.Length);
  }
 }
}
