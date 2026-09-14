using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Metagame.Gameplay;
using Object=UnityEngine.Object;
namespace Metagame.EditorTools
{
 public static partial class Stage1AstraExpeditionBuilder
 {
  static void Label(Transform parent,string text,Vector3 p,float size=.16f)
  {var t=Group("Facility placard - "+text,p,parent);var label=t.gameObject.AddComponent<TextMesh>();label.text=text;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.fontSize=64;label.characterSize=size;label.color=new Color(.86f,.96f,.97f);MountSign(label);}
  static void MountSign(TextMesh label)
  {
   if(label.transform.Find("Mounted facility sign")!=null)return;
   var b=label.GetComponent<Renderer>().bounds;float w=Mathf.Max(.8f,b.size.x+.3f),h=Mathf.Max(.45f,b.size.y+.25f);
   Box("Mounted facility sign",label.transform,b.center+Vector3.forward*.09f,V(w,h,.1f),dark,false);
   if(!Physics.Raycast(b.center-Vector3.forward*.1f,Vector3.forward,1,GameLayers.GroundMask))
   {float floor=Height(b.center.x,b.center.z),bottom=b.min.y-.12f;foreach(float side in new[]{-1f,1f})if(bottom>floor+.15f)Beam("Sign support",label.transform,V(b.center.x+side*w*.4f,floor,b.center.z+.13f),V(b.center.x+side*w*.4f,bottom,b.center.z+.13f),.06f,white,false);}
  }
  public static void FinishSigns()
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);Materials();root=GameObject.Find("Astra expedition settlement").transform;
   foreach(var label in root.GetComponentsInChildren<TextMesh>())if(label.name.StartsWith("Facility placard"))MountSign(label);
   UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);ValidateAndRender();
  }
  static void Container(Transform parent,Vector3 p,Material m,string label)
  {
   Box("Ribbed expedition container",parent,p+Vector3.up*1.6f,V(6,3.2f,4),m);
   foreach(float x in new[]{-2.8f,2.8f})Box("Container reinforced corner",parent,p+V(x,1.6f,-2.03f),V(.14f,3.2f,.14f),dark);
   for(int i=0;i<9;i++)Box("Container rib",parent,p+V(-2.5f+i*.62f,1.6f,-2.03f),V(.06f,3,.08f),white,false);
   Label(parent,label,p+V(0,2,-2.09f),.12f);
  }
  static void Bench(Transform parent,Vector3 p,float yaw=0)
  {
   var t=Group("Crew rest bench",p,parent);
   Box("Bench seat",t,p+V(0,.65f,0),V(2.8f,.16f,.8f),white);
   Box("Bench back",t,p+V(0,1.15f,.35f),V(2.8f,.9f,.12f),orange);
   foreach(float x in new[]{-1,1})Box("Bench foot",t,p+V(x,.3f,0),V(.15f,.6f,.65f),dark);t.rotation=Quaternion.Euler(0,yaw,0);
  }
  static void Habitat(Transform parent,Vector3 p,string name,Material accent)
  {
   var t=Group(name,p,parent);
   Box("Habitat insulated floor",t,p+V(0,.08f,0),V(7,.16f,6),white);
   Box("Habitat rear bulkhead",t,p+V(0,1.8f,2.8f),V(7,3.6f,.3f),white);
   foreach(float x in new[]{-3.3f,3.3f}){Box("Habitat side",t,p+V(x,1.8f,0),V(.3f,3.6f,6),white);Box("Habitat viewing window",t,p+V(x*1.05f,2,0),V(.04f,1.4f,3.4f),blue,false);}
   foreach(float x in new[]{-2.45f,2.45f})Box("Open habitat doorway jamb",t,p+V(x,1.8f,-2.8f),V(1.8f,3.6f,.3f),accent);
   var roof=Shape("Habitat rounded canopy",PrimitiveType.Cylinder,t,p+V(0,3.75f,0),V(7.8f,.22f,6.6f),white);roof.transform.localScale=V(7.8f,.22f,6.6f);
   Label(t,name,p+V(0,3.2f,-3.02f),.12f);
   for(int i=0;i<2;i++){Box("Sleeping bunk",t,p+V(-1.7f+i*3.4f,.5f,1),V(1.6f,.6f,2.6f),dark);Box("Sleeping mat",t,p+V(-1.7f+i*3.4f,.84f,1),V(1.55f,.1f,2.5f),accent);Box("Folded bedding",t,p+V(-1.7f+i*3.4f,1,1.9f),V(1.2f,.22f,.55f),white);}
  }
  static void Dressing()
  {
   Logistics();Observatory();Laboratory();Mining();Camp();Leisure();Scenic();
   var hub=Group("Emergency reunion and modular launch apron",Vector3.zero);
   for(int i=0;i<4;i++)
   {var p=V(i*3,Height(i*3,15),15);Box("Emergency mast",hub,p+Vector3.up*2,V(.18f,4,.18f),dark);Box("Evacuation beacon",hub,p+Vector3.up*4.2f,V(.6f,.3f,.6f),colors[i],false);}
   Label(hub,"EVAC / ASSEMBLY",V(0,3.2f,-10),.22f);
   var board=Group("Expedition evacuation overview",V(-18,1,-31));
   Box("Base evacuation board",board,V(-18,2.4f,-31),V(5,2.6f,.15f),dark);
   // Four color-coded work sites converge on one assembled craft silhouette.
   for(int i=0;i<4;i++){var p=V(-19.7f+i*1.13f,2.8f,-31.12f);Box("Recovered module pictogram",board,p,V(.65f,.5f,.04f),colors[i],false);Beam("Assembly diagram connector",board,p-Vector3.up*.35f,V(-18,1.8f,-31.12f),.04f,white);}
   Shape("Evacuation craft symbol",PrimitiveType.Capsule,board,V(-18,1.8f,-31.15f),V(.4f,.3f,.06f),white,false);
  }
  static void Logistics()
  {
   var t=Group("01 Logistics - robot unloading and charging",V(-32,1,-27));
   Container(t,V(-43,1,-24),orange,"SAMPLE INTAKE");Container(t,V(-43,1,-34),white,"FIELD SUPPLIES");
   foreach(float x in new[]{-40f,-24f})Box("Cargo loading gantry leg",t,V(x,4,-35),V(.35f,6,.35f),dark);
   Box("Cargo loading gantry crossbeam",t,V(-32,7,-35),V(16.5f,.4f,.5f),orange);
   Box("Robot charging terminal",t,V(-36,2,-23),V(1,2,.7f),dark);
   Box("Charging status dead display",t,V(-36,2.4f,-23.38f),V(.75f,.5f,.04f),colors[0],false);
   Beam("Robot charging umbilical",t,V(-36,1.1f,-23),V(-34,1.1f,-26),.09f,colors[0]);
   Circle("Robot charging bay marking",t,V(-32,1.025f,-27),4.2f,colors[0]);
   Label(t,"LOGISTICS / CHARGE",V(-32,5,-35.3f),.23f);
   // The interruption is visible as unfinished cargo unloading beside the waiting robot.
   for(int i=0;i<3;i++)Box("Sample sorting tray",t,V(-41+i*1.4f,1.3f,-19.5f),V(1.1f,.5f,1.1f),green);
  }
  static void Observatory()
  {
   var t=Group("02 Atmosphere observatory - stranded hover sampler",V(-48,7.4f,46));
   Box("Survey instrument pier",t,V(-52,9,50),V(.7f,3.2f,.7f),dark);
   var dish=Shape("Atmospheric radar dish",PrimitiveType.Sphere,t,V(-52,11,50),V(5.8f,.55f,5.8f),white,false);dish.transform.rotation=Quaternion.Euler(25,0,0);
   Beam("Dish feed arm",t,V(-52,11,50),V(-52,13,48),.12f,blue);
   Box("Sampling antenna",t,V(-43,10.8f,51),V(.18f,6.8f,.18f),white);
   for(int i=0;i<4;i++)Box("Antenna crosspiece",t,V(-43,11+i*.55f,51),V(3-i*.6f,.08f,.12f),blue,false);
   Box("Observation console",t,V(-44,8.1f,49.7f),V(2.5f,1.4f,.8f),dark);
   Box("Air sample rack",t,V(-51,8.2f,42),V(1.4f,1.6f,.7f),blue);
   Circle("Hover sampler docking stencil",t,V(-48,7.43f,46),3,colors[1]);
   // Remnants end before the cliff: the broken access never becomes an easier alternate stair.
   for(int i=0;i<4;i++)Box("Sheared service stair remnant",t,V(-48,1.2f+i*.4f,31+i*.9f),V(3,.25f,.85f),white);
   var snapped=Box("Broken elevated walkway",t,V(-48,4.5f,36),V(3.4f,.3f,2),dark);snapped.transform.rotation=Quaternion.Euler(22,0,8);
   for(int i=0;i<3;i++)Beam("Exposed broken reinforcement",t,V(-49+i,4.8f,37),V(-49+i,4.2f,38.1f),.06f,orange);
   Label(t,"ATMOSPHERE / SURVEY",V(-48,10.5f,51.8f),.22f);
   Label(t,"GRAVITY FREIGHT",V(-36,3,48.5f),.12f);
  }
  static void Laboratory()
  {
   var t=Group("03 Specimen quarantine and geological survey",V(48,1,41));
   Label(t,"SPECIMEN / QUARANTINE",V(48,6.2f,34.4f),.22f);
   for(int i=0;i<3;i++)
   {Box("Hermetic sample cabinet",t,V(52.5f,2.1f,39+i*2),V(1.2f,2.2f,1.5f),white);
    Shape("Sealed specimen vial",PrimitiveType.Cylinder,t,V(52.5f,3.4f,39+i*2),V(.5f,.5f,.5f),i==1?green:blue,false);}
   foreach(float z in new[]{39f,43f})Box("Observation porthole",t,V(54.85f,3.2f,z),V(.03f,1.2f,2),blue,false);
   foreach(float x in new[]{43f,53f}){Shape("Filtered exhaust",PrimitiveType.Cylinder,t,V(x,6.9f,43),V(.7f,.8f,.7f),white);Shape("Vent hood",PrimitiveType.Cylinder,t,V(x,7.7f,43),V(1.1f,.12f,1.1f),dark);}
   for(int i=0;i<3;i++)Box("Emergency battery cabinet",t,V(37.5f,1.6f,31+i*1.4f),V(1,1.2f,1.1f),dark);
   Beam("Restored emergency power conduit",t,V(39,1.05f,28.5f),V(37.5f,1.05f,29.6f),.09f,colors[2]);
   foreach(float x in new[]{45.3f,50.7f}){Box("Decontamination spray upright",t,V(x,2.8f,36),V(.12f,3.4f,.12f),blue,false);}
   Box("Decontamination spray header",t,V(48,4.45f,36),V(5.5f,.12f,.12f),blue,false);
   // Survey gear explains buried charges without turning the warning signs into text labels.
   for(int i=0;i<4;i++){var p=V(39.5f,1,23+i*.9f);Shape("Geophone sample probe",PrimitiveType.Cylinder,t,p+Vector3.up*.4f,V(.18f,.4f,.18f),orange);Beam("Survey recorder wire",t,p+V(0,.05f,0),V(38,1.05f,23),.035f,dark);}
   Box("Disconnected survey recorder",t,V(38,1.45f,23),V(1.4f,.9f,.7f),orange);
   var quarry=Group("Blasting permit supply gallery",V(68,1,37));Label(quarry,"FIELD BLAST SUPPLIES",V(68,6.2f,36.4f),.17f);
   for(int i=0;i<3;i++)Box("Quarry core sample stack",quarry,V(74,1.4f,32+i*2),V(1.5f,.8f,1.5f),rock);
  }
  static void Mining()
  {
   var t=Group("04 Excavation service workshop - collapsed garage",V(50,1,-38));
   Label(t,"EXCAVATION / SERVICE",V(50,5.4f,-42.5f),.23f);
   foreach(float x in new[]{46f,54f})Box("Repair gantry mast",t,V(x,4.2f,-40),V(.28f,6.4f,.28f),orange);
   Box("Repair gantry beam",t,V(50,7.4f,-40),V(8.5f,.4f,.6f),dark);
   Beam("Maintenance hoist chain",t,V(50,7.2f,-40),V(50,4.4f,-40),.06f,white);
   foreach(float x in new[]{48.3f,51.7f})Box("Sideways service cradle",t,V(x,1.12f,-38),V(.18f,.18f,4),orange,false);
   Box("Sealed main garage frame",t,V(55.4f,2.6f,-38),V(.18f,3.2f,5.2f),dark,false);
   for(int i=0;i<5;i++){var stone=Box("Meteor collapsed main vehicle exit",t,V(57+(i%2)*1.3f,1.8f+i*.24f,-40+i*1.1f),V(2,2.2f,2.3f),rock);stone.transform.rotation=Quaternion.Euler(i*8,i*17,i*13);}
   for(int i=0;i<4;i++)Box("Old vehicle rail now buried",t,V(60+i*1.1f,1.025f,-38),V(.8f,.04f,.16f),white,false);
   Container(t,V(49,1,-54),dark,"DRILL MAINTENANCE");
   Box("Workshop tool bench",t,V(43,1.7f,-45),V(4,1.4f,1.3f),white);
   for(int i=0;i<4;i++)Box("Abandoned repair tool",t,V(41.8f+i*.7f,2.45f,-45),V(.45f,.12f,.2f),orange,false);
   // A broad service road continues around the damaged garage, so taking the module home needs no tractor.
   Label(t,"MANUAL SERVICE EXIT",V(37,2.8f,-30),.12f);
  }
  static void Camp()
  {
   var t=Group("Crew habitation and interrupted daily life",V(-21,1,-43));
   Habitat(t,V(-23,1,-47),"CREW HAB 01",orange);Habitat(t,V(-12,1,-47),"CREW HAB 02",blue);
   Habitat(t,V(-3,1,-39),"FIELD MESS",green);
   Bench(t,V(-29,1,-40));Bench(t,V(-16,1,-39),180);
   Box("Mess table",t,V(-20,1.7f,-39),V(3,.15f,1.6f),white);
   for(int i=0;i<3;i++)Shape("Interrupted meal cup",PrimitiveType.Cylinder,t,V(-21+i,1.9f,-39),V(.2f,.16f,.2f),orange,false);
   for(int i=0;i<3;i++){var panel=Box("Camp solar panel",t,V(-8+i*3,3,-28),V(2.3f,.14f,3),blue);panel.transform.rotation=Quaternion.Euler(18,0,0);Box("Solar panel leg",t,V(-8+i*3,1.8f,-28),V(.15f,1.6f,.15f),white);}
   Box("Air recycling tank",t,V(-36,2.4f,-50),V(2,2.8f,2),white);
   Label(t,"CREW COMMONS",V(-30,3.3f,-37),.22f);
  }
  static void Leisure()
  {
   var t=Group("Off duty sports and low gravity play",V(-45,1,-57));
   Box("Football court",t,V(-50,1.005f,-57),V(25,.025f,16),green,false);
   foreach(float z in new[]{-64f,-50f})Beam("Football touch line",t,V(-62,1.04f,z),V(-38,1.04f,z),.09f,white);
   foreach(float x in new[]{-62f,-38f})Beam("Football end line",t,V(x,1.04f,-64),V(x,1.04f,-50),.09f,white);
   Beam("Football center line",t,V(-50,1.04f,-64),V(-50,1.04f,-50),.08f,white);Circle("Center circle",t,V(-50,1.045f,-57),2.8f,white);
   // The existing goals and balls are retained; the court explains their intended play space.
   Box("Meteor basketball court",t,V(-28,1.01f,-60),V(15,.025f,16),blue,false);Circle("Meteor free throw arc",t,V(-28,1.045f,-59),4,white);
   for(int i=0;i<3;i++)Bench(t,V(-37,1,-55+i*3),90);
   Box("Crew bowling lane",t,V(-7,1.01f,-58),V(7,.025f,15),white,false);
   foreach(float x in new[]{-10.8f,-3.2f})Box("Bowling low gutter",t,V(x,1.13f,-58),V(.15f,.25f,15),dark);
   Label(t,"OFF DUTY / LOW-G SPORTS",V(-45,3.8f,-48),.23f);
   var wheel=Group("Scenic ride waiting plaza",V(-60,1,-43));Circle("Ferris boarding plaza",wheel,V(-60,1.025f,-43),5,white);Bench(wheel,V(-57,1,-39),180);
   Label(wheel,"HORIZON WALK",V(-62,4,-36),.18f);
   var fast=Group("Survey launch training station",V(-27,1,-8));Label(fast,"FIELD TRANSFER",V(-31,3.3f,-10),.17f);
   Circle("Survey transfer landing zone",fast,V(-43,1.025f,19),5.5f,blue);Label(fast,"SURVEY LANDING",V(-49,2.6f,19),.12f);
  }
  static void Scenic()
  {
   var t=Group("Alien botanical walk and meteor impact evidence",Vector3.zero);
   foreach(var p in new[]{P(-74,-24),P(-72,-3),P(-64,13),P(-18,34),P(18,35)})
   {float h=Height(p.x,p.y);Bench(t,V(p.x,h,p.y),90);Box("Field observation marker",t,V(p.x+2,h+1,p.y),V(.1f,2,.1f),white);}
   // Meteor fragments sit below the warning rim; they never occupy the return roads.
   for(int r=0;r<Rifts.Length;r++)
   {var p=Rifts[r][1];for(int i=0;i<5;i++){var g=Shape("Impact meteor fragment",PrimitiveType.Sphere,t,V(p.x+(i-2)*.7f,-6.8f,p.y),Vector3.one*(1+i*.18f),dark);g.transform.rotation=Quaternion.Euler(i*17,i*31,0);}}
   var rng=new System.Random(813);string[] prefabNames={"PF_Stage1_MushroomTree","PF_Stage1_BoulderCluster","PF_Stage1_PurpleFlowerSmall"};
   var assets=prefabNames.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Environment/Stage1/"+n+".prefab")).Where(g=>g!=null).ToArray();
   if(assets.Length==0)return;
   for(int i=0;i<85;i++)
   {float x=(float)rng.NextDouble()*156-78,z=(float)rng.NextDouble()*128-64;var p=P(x,z);if(Clear(p)<5||RouteDistance(p,Rifts)<7)continue;
    var go=(GameObject)PrefabUtility.InstantiatePrefab(assets[i%assets.Length],t);PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
    foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
    var rs=go.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var rend in rs.Skip(1))b.Encapsulate(rend.bounds);
    float target=2.5f+(i%4);go.transform.localScale*=target/Mathf.Max(.1f,b.size.y);rs=go.GetComponentsInChildren<Renderer>();b=rs[0].bounds;foreach(var rend in rs.Skip(1))b.Encapsulate(rend.bounds);go.transform.position+=V(x-b.center.x,Height(x,z)-b.min.y,z-b.center.z);}
  }
 }
}
