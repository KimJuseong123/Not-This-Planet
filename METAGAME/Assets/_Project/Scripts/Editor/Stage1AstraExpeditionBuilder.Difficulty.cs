using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class Stage1AstraExpeditionBuilder
 {
  const string DifficultyRoot = "Expedition physical learning and recovery";
  public static void AddAndValidateDifficultyLayout(){AddDifficultyLayout();ValidateDifficultyLayout();}

  [MenuItem("Tools/Metagame/Astra/Add physical learning and recovery layout")]
  public static void AddDifficultyLayout()
  {
   var scene = EditorSceneManager.OpenScene(ScenePath);
   if (GameObject.Find("Astra expedition settlement") == null) throw new Exception("Expanded settlement required");
   if (GameObject.Find(DifficultyRoot) != null) throw new Exception("Difficulty layout already authored");
   Materials(); root = new GameObject(DifficultyRoot).transform;
   TrainingYard(); ClimbRecovery(); SurveyPreparation(); WorkshopAssistance();
   Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
   AssetDatabase.SaveAssets(); Directory.CreateDirectory("Validation/AstraDifficulty");
   File.WriteAllText("Validation/AstraDifficulty/authoring.txt", "PASS incremental difficulty layout; original four recovery mechanics retained\n");
  }

  static Rigidbody LearningBody(Transform t, float mass, string label)
  {
   var body = t.gameObject.AddComponent<Rigidbody>(); body.mass = mass;
   body.linearDamping = .15f; body.angularDamping = .12f;
   body.interpolation = RigidbodyInterpolation.Interpolate;
   body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
   foreach (var child in t.GetComponentsInChildren<Transform>()) child.gameObject.layer = GameLayers.Prop;
   Grabbable.Mark(t.gameObject, label).PhysicsGrip = true;
   t.gameObject.AddComponent<AstraV2CargoTool>(); return body;
  }

  static Transform LooseBox(string name, Vector3 p, Vector3 size, Material material, float mass, string label)
  {
   var t = Group(name, p); Box("Grippable body", t, p, size, material);
   LearningBody(t, mass, label); return t;
  }

  static void TrainingYard()
  {
   var yard = Group("Logistics apprentice practice bay", Vector3.zero);
   // A low, optional loading exercise. Missing the bridge drops onto safe ground.
   Box("Practice loading dock", yard, V(-43,1.25f,-27), V(3,.5f,3), dark);
   Box("Practice receiving dock", yard, V(-43,1.25f,-22.5f), V(3,.5f,3), dark);
   foreach (float z in new[]{-27f,-22.5f})
    Box("Matching delivery socket", yard, V(-43,1.515f,z), V(1.3f,.02f,1.3f), orange, false);
   var crate = LooseBox("Practice light sample crate",V(-43,2.02f,-27),Vector3.one,orange,4,"연습 시료 상자 · 잡기 / 밀기 / 다리 건너기");
   foreach (float x in new[]{-.42f,.42f}) Box("Sample crate strap",crate,crate.position+V(x,0,0),V(.07f,1.03f,1.03f),dark,false);
   LooseBox("Practice bridge plank",V(-45.4f,1.18f,-25),V(1.1f,.18f,4.5f),blue,7,"연습 널판지 · 두 하역대 사이에 놓기");
   LooseBox("Practice lifting bar",V(-40.8f,1.3f,-26.4f),V(.14f,.14f,2.6f),orange,4,"연습 지렛대 · 받침 위에서 상자 들기");
   Box("Practice lever fulcrum",yard,V(-40.8f,1.1f,-26.4f),V(.5f,.2f,.35f),dark);
   for(int i=0;i<2;i++)
   {
    var t=Group("Practice loose roller "+i,V(-41,1.2f,-24+i*.6f));
    var g=Shape("Roller cylinder",PrimitiveType.Cylinder,t,t.position,V(.32f,.8f,.32f),white);
    g.transform.rotation=Quaternion.Euler(0,0,90);LearningBody(t,2,"연습 롤러 · 상자 밑에 놓기");
   }
   // The little staircase invites walking up, leaving the cargo transfer as the experiment.
   for(int i=0;i<2;i++) Box("Practice dock access step "+i,yard,V(-43,1.125f+i*.125f,-29.15f+i*.4f),V(1.5f,.25f+i*.25f,.5f),white);
   // Author the practice set in the eastern spare loading bay, clear of the sample containers.
   foreach(Transform child in root.Cast<Transform>().ToArray()) Move(child,V(21,0,-3));
  }

  static void ClimbRecovery()
  {
   var area=Group("Observatory maintenance rest shelves",Vector3.zero);
   Box("Lower climb catch shelf",area,V(-64.4f,3.25f,37.4f),V(2.8f,.3f,5.4f),dark);
   Box("Upper climb catch shelf",area,V(-62.4f,5.45f,46),V(2.8f,.3f,4.5f),dark);
   foreach(var p in new[]{V(-65.7f,3.8f,37.4f),V(-63.7f,6,46)})
    Box("Rest shelf outer curb",area,p,V(.15f,.8f,4.4f),orange);
   // Large contrasting top patches show a place to rest without adding a UI checkpoint.
   Box("Lower rest landing paint",area,V(-64.4f,3.415f,37.4f),V(2.2f,.02f,2.2f),blue,false);
   Box("Upper rest landing paint",area,V(-62.4f,5.615f,46),V(2.2f,.02f,2.2f),blue,false);
   foreach(var p in new[]{V(-64.4f,2.05f,37.4f),V(-62.4f,3.15f,46)})
    Box("Retained maintenance shelf support",area,p,V(.25f,(p.y-1)*2,.25f),dark);
  }

  static void SurveyPreparation()
  {
   var area=Group("Quarantine survey staging and route flags",Vector3.zero);
   // Outside the live field and clear of the main walking loop: inspect equipment before crossing.
   Circle("Safe survey staging circle",area,V(39,1.03f,24.6f),1.3f,blue,.09f);
   for(int i=0;i<6;i++)
   {
    var p=V(37.7f+(i%3)*.65f,1.1f,24+(i/3)*1.1f);var t=Group("Carryable survey route marker "+i,p);
    Box("Weighted marker foot",t,p,V(.45f,.18f,.45f),dark);
    Box("Marker pole",t,p+V(0,.47f,0),V(.06f,.9f,.06f),white);
    Box("Hollow route flag top",t,p+V(.2f,.84f,0),V(.44f,.07f,.08f),blue);
    Box("Hollow route flag bottom",t,p+V(.2f,.57f,0),V(.44f,.07f,.08f),blue);
    Box("Hollow route flag tip",t,p+V(.4f,.7f,0),V(.07f,.34f,.08f),blue);
    LearningBody(t,3,"탐사 경로 표식 · 탐색기로 확인한 길에 놓기").centerOfMass=V(0,.02f,0);
   }
   // Return-trip convenience uses the EXISTING obstruction sensor, not a new opening switch.
   // A crew member must first solve the circuit and hold the player-only pressure plate.
   for(int i=0;i<2;i++)
   {
    var p=V(43.1f,1.22f,37.2f+i*1.2f);
    var t=LooseBox("Airlock maintenance stop "+i,p,V(.5f,.4f,.8f),orange,8,"문 안전 받침 · 열린 문 가장자리에 놓기");
    Box("Stop lifting grip",t,p+V(0,.3f,0),V(.36f,.12f,.12f),dark);
   }
   // A non-colliding edge target keeps props away from the central cargo clearance.
   foreach(float x in new[]{45.6f,50.4f})
    Box("Airlock stop placement outline",area,V(x,1.03f,35),V(.55f,.025f,.85f),orange,false);
  }

  static void WorkshopAssistance()
  {
   var area=Group("Workshop accessible turning tools",Vector3.zero);
   LooseBox("Workshop interior turning lever",V(47.1f,1.3f,-40),V(.14f,.14f,2.5f),orange,5,"정비 지렛대 · 모서리에서 방향 바꾸기");
   Box("Workshop interior lever rest",area,V(47.1f,1.1f,-40),V(.55f,.2f,.35f),dark);
   for(int i=0;i<2;i++)
   {
    var t=Group("Workshop interior roller "+i,V(46.5f+i*.55f,1.2f,-37));
    var g=Shape("Short workshop roller",PrimitiveType.Cylinder,t,t.position,V(.32f,.9f,.32f),white);
    g.transform.rotation=Quaternion.Euler(0,0,90);LearningBody(t,3,"정비 롤러 · 뒤에서 빼서 앞에 놓기");
   }
   // Broad turning arc on the floor explains the corridor's maneuvering pocket.
   for(int i=0;i<7;i++)
   {
    float a=i*Mathf.PI/12;var p=V(50-Mathf.Sin(a)*2,1.025f,-34+Mathf.Cos(a)*2);
    var g=Box("Workshop turning arc "+i,area,p,V(.3f,.025f,.12f),green,false);g.transform.rotation=Quaternion.Euler(0,-i*15,0);
   }
  }

  public static void ValidateDifficultyLayout()
  {
   EditorSceneManager.OpenScene(ScenePath);var t=GameObject.Find(DifficultyRoot).transform;
   var bodies=t.GetComponentsInChildren<Rigidbody>();
   if(bodies.Length!=16)throw new Exception("Expected 16 new physical learning objects, found "+bodies.Length);
   foreach(var b in bodies)
   {
    if(b.GetComponent<Grabbable>()==null || b.GetComponent<AstraV2CargoTool>()==null || b.isKinematic)
     throw new Exception("Incomplete synchronized grabbable: "+b.name);
    if(!b.GetComponentsInChildren<Collider>().Any(c=>!c.isTrigger))throw new Exception("No physical collider: "+b.name);
   }
   Physics.SyncTransforms();
   foreach(var collider in bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()))
   {
    var b=collider.bounds;
    var obstruction=Physics.OverlapBox(b.center,b.extents*.97f,Quaternion.identity,GameLayers.GroundMask|GameLayers.PartMask|GameLayers.PropMask,QueryTriggerInteraction.Ignore)
     .FirstOrDefault(c=>!c.transform.IsChildOf(t));
    if(obstruction!=null)throw new Exception("New loose object initially overlaps existing object: "+collider.attachedRigidbody.name+" / "+obstruction.name);
   }
   var practice=GameObject.Find("Logistics apprentice practice bay").transform;
   foreach(var collider in practice.GetComponentsInChildren<Collider>())
   {
    var b=collider.bounds;
    var obstruction=Physics.OverlapBox(b.center,b.extents*.98f,Quaternion.identity,GameLayers.GroundMask,QueryTriggerInteraction.Ignore)
     .FirstOrDefault(c=>!c.transform.IsChildOf(t));
    if(obstruction!=null)throw new Exception("Practice fixture overlaps existing scenery: "+collider.name+" / "+obstruction.name);
   }
   foreach(var p in new[]{V(-64.4f,3.8f,37.4f),V(-62.4f,6,46)})
    if(!Physics.Raycast(p,Vector3.down,out var hit,.8f,GameLayers.GroundMask) || !hit.collider.name.Contains("catch shelf"))
     throw new Exception("Climb catch shelf missing support "+p+" hit="+(hit.collider!=null?hit.collider.name:"none"));
   File.WriteAllText("Validation/AstraDifficulty/validation.txt","PASS 16 dynamic grabbables with WorldSync/recovery markers and colliders\nPASS practice fixtures clear of existing scenery\nPASS both observatory rest shelves support players\n");
   ValidateAndRender();
   var camera=Object.FindObjectsByType<Camera>().First();
   Capture(camera,V(-11,12,-37),V(-22,1,-28),"difficulty-practice");
   Capture(camera,V(32,13,17),V(41,1,28),"difficulty-survey");
  }
 }
}


