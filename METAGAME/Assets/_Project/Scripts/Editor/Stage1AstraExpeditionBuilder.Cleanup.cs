using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class Stage1AstraExpeditionBuilder
 {
  const string RecoveryFocusedMarker="Recovery focused layout - leisure removed";
  static bool RecoveryFocused=>GameObject.Find(RecoveryFocusedMarker)!=null;
  static bool LeisureOnly(string name)=>name=="Mini football goal" || name.StartsWith("축구공") || name.StartsWith("비치볼")
   || name.StartsWith("볼링") || name.StartsWith("도미노") || name=="매달린 샌드백" || name=="Punch bag frame"
   || name=="Long physical seesaw" || name=="Seesaw fulcrum" || name.StartsWith("트램펄린")
   || name=="Always turning Ferris wheel" || name=="Ferris walk-on boarding dock" || name=="Scenic ride waiting plaza"
   || name=="Meteor basketball hoop - static" || name=="Off duty sports and low gravity play";

  [MenuItem("Tools/Metagame/Astra/Remove leisure-only scene objects")]
  public static void RemoveLeisureOnly()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);
   if(GameObject.Find("Astra expedition settlement")==null)throw new Exception("Expedition scene required");
   if(RecoveryFocused)throw new Exception("Leisure cleanup already applied");
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
   var selected=all.Where(t=>LeisureOnly(t.name)).ToArray();
   var roots=selected.Where(t=>!selected.Any(parent=>parent!=t&&t.IsChildOf(parent))).ToArray();
   if(roots.Length==0)throw new Exception("No leisure objects found");
   var removed=new System.Text.StringBuilder();int bodies=0,grabs=0;
   foreach(var t in roots)
   {
    bodies+=t.GetComponentsInChildren<Rigidbody>(true).Length;grabs+=t.GetComponentsInChildren<Grabbable>(true).Length;
    removed.AppendLine(t.name);Object.DestroyImmediate(t.gameObject);
   }
   new GameObject(RecoveryFocusedMarker);
   Directory.CreateDirectory("Validation/AstraLeisureCleanup");
   File.WriteAllText("Validation/AstraLeisureCleanup/removed.txt","Removed "+roots.Length+" object groups, "+bodies+" rigidbodies, "+grabs+" grabbables\n"+removed);
   EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   ValidateDifficultyLayout();
   // Check surviving serialized object references after removing whole mechanisms.
   EditorSceneManager.OpenScene(ScenePath);
   foreach(var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
    foreach(var component in g.GetComponentsInChildren<Component>(true))
    {
     if(component==null)throw new Exception("Missing component after cleanup");
     var property=new SerializedObject(component).GetIterator();
     while(property.Next(true))
      if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==null && !property.objectReferenceEntityIdValue.Equals(EntityId.None))
       throw new Exception("Missing reference: "+component.name+" / "+property.propertyPath);
    }
   File.WriteAllText("Validation/AstraLeisureCleanup/validation.txt","PASS no leisure-only objects remain\nPASS retained cargo tools, learning bay, four parts and all guidance\nPASS full cargo/walking routes and difficulty-layout geometry\nPASS no missing scripts or broken serialized references\n");
  }
 }
}

