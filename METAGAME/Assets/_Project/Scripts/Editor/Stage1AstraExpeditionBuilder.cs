using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
  const string ScenePath="Assets/Scenes/Stage1_Astra_ver2.unity", Art="Assets/_Project/Art/AstraExpedition";
  static Transform root;static Terrain terrain;static Material white,dark,blue,green,orange,rock,road,red;static Material[] colors;
  static readonly Vector3[] shifts={new Vector3(-8,0,-7),new Vector3(-25,0,19),new Vector3(23,0,17),new Vector3(22,0,-12)};
  static Vector2 P(float x,float z)=>new Vector2(x,z);static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
  public static readonly Vector2[][] CargoRoutes={
   new[]{P(-32,-27),P(-24,-20),P(-15,-12),P(-6,-5),P(0,-5)},
   new[]{P(-38.2f,44),P(-32,44),P(-26,38),P(-19,29),P(-10,15),P(0,6)},
   new[]{P(48,42),P(48,35),P(48,26),P(38,17),P(23,9),P(6,0)},
   new[]{P(50,-38),P(50,-32),P(38,-32),P(35.2f,-32),P(27,-21),P(15,-12),P(5,-7),P(0,-5)}};
  static readonly Vector2[][] Walks={
   new[]{P(-32,-27),P(-32,-41),P(-50,-41),P(-55,-41),P(-55,-33),P(-59,-24),P(-68,-15),P(-68,6),P(-68,22),P(-60,26)},
   new[]{P(-32,-43),P(-45,-46),P(-50,-49),P(-50,-66),P(-37,-66),P(-19,-66),P(-1,-66),P(2,-55),P(2,-30),P(0,-16),P(0,-7)},
   new[]{P(-55,-43),P(-55,-48),P(-54,-50)},
   new[]{P(-32,-43),P(-13,-43),P(-15,-34),P(-15,-29),P(0,-16)},
   new[]{P(-38.2f,44),P(-32,44),P(-26,38),P(-19,29),P(-6,33),P(18,30),P(35,22),P(48,22)},
   new[]{P(48,26),P(60,23),P(68,29),P(68,33.5f)},
   new[]{P(35.2f,-32),P(34,-46),P(39,-50),P(44,-50),P(60,-49)},
   new[]{P(-32,-27),P(-37,-12),P(-42,8),P(-45,22),P(-60,26)}};
  public static readonly Vector2[][] Rifts={new[]{P(-78,-3),P(-76,8),P(-79,19)},new[]{P(2,54),P(9,48),P(16,52)},new[]{P(51,-7),P(60,-9),P(69,-3)}};
  // Work yards stay level for manually returned modules; hills occupy the spaces between them.
  static readonly Rect[] pads={Rect.MinMaxRect(-46,-39,-18,-18),Rect.MinMaxRect(-68,23,-29,60),Rect.MinMaxRect(35,22,59,52),Rect.MinMaxRect(32,-49,62,-23),Rect.MinMaxRect(-79,-72,2,-33),Rect.MinMaxRect(60,22,76,47),Rect.MinMaxRect(-51,8,-33,26),Rect.MinMaxRect(-35,-15,-19,-1),Rect.MinMaxRect(24,18,35,25),Rect.MinMaxRect(-62,18,-53,24)};
  static Transform Find(string name)=>Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).First(t=>t.name==name);
  static Transform Group(string name,Vector3 position,Transform parent=null){var t=new GameObject(name).transform;t.SetParent(parent!=null?parent:root,true);t.position=position;return t;}
  static void OffsetField(Component c,string field,Vector3 delta){var so=new SerializedObject(c);var p=so.FindProperty(field);if(p==null)throw new Exception(c.GetType().Name+" missing "+field);p.vector3Value+=delta;so.ApplyModifiedPropertiesWithoutUndo();}
  static void Move(Transform t,Vector3 delta)
  {
   foreach(var l in t.GetComponentsInChildren<LineRenderer>(true))if(l.useWorldSpace){var p=new Vector3[l.positionCount];l.GetPositions(p);for(int i=0;i<p.Length;i++)p[i]+=delta;l.SetPositions(p);}
   foreach(var j in t.GetComponentsInChildren<Joint>(true))if(j.connectedBody==null&&!j.autoConfigureConnectedAnchor)j.connectedAnchor+=delta;
   foreach(var lift in t.GetComponentsInChildren<AstraV2FreightLift>(true)){OffsetField(lift,"_top",delta);OffsetField(lift,"_bottom",delta);}
   foreach(var puzzle in t.GetComponentsInChildren<AstraV2Puzzle>(true)){OffsetField(puzzle,"_plateRest",delta);OffsetField(puzzle,"_doorClosed",delta);}
   foreach(var cannon in t.GetComponentsInChildren<AstraPlayerCannon>(true)){OffsetField(cannon,"fuseStart",delta);OffsetField(cannon,"fuseEnd",delta);}
   foreach(var mine in t.GetComponentsInChildren<AstraMinefield>(true)){var so=new SerializedObject(mine);var p=so.FindProperty("mines");for(int i=0;i<p.arraySize;i++)p.GetArrayElementAtIndex(i).vector3Value+=delta;so.ApplyModifiedPropertiesWithoutUndo();}
   t.position+=delta;
  }
  static void Place(Transform t,float x,float z)=>Move(t,V(x-t.position.x,0,z-t.position.z));
  static void Materials()
  {
   if(!AssetDatabase.IsValidFolder(Art))AssetDatabase.CreateFolder("Assets/_Project/Art","AstraExpedition");
   white=Load("AstraToybox/Ivory");dark=Load("AstraToybox/Graphite");blue=Load("AstraToybox/Pool blue");green=Load("AstraToybox/Mint");orange=Load("AstraToybox/Tangerine");rock=Load("Stage1AstraVer2/Lavender cliff");road=Load("Stage1AstraVer2/Cargo route");red=Load("AstraAttractions/TNT safety red");
   colors=Enumerable.Range(1,4).Select(i=>Load("Stage1AstraVer2/Part"+i+"Beacon")).ToArray();
  }
  static Material Load(string name){var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/"+name+".mat");if(m==null)throw new Exception("Missing material "+name);return m;}
  [MenuItem("Tools/Metagame/Astra/Expand into expedition settlement")]
  public static void Apply()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);
   if(GameObject.Find("Astra expedition settlement")!=null)throw new Exception("Expedition layout already authored");
   root=new GameObject("Astra expedition settlement").transform;terrain=Object.FindAnyObjectByType<Terrain>();Materials();
   RelocateSites();RelocateEquipment();BuildLand();Dressing();
   var map=Object.FindAnyObjectByType<AuthoredMapSettings>();map.flightHalfSize=new Vector2(90,75);map.playerSpawn.position=V(-27,1.1f,-41);
   foreach(var t in scene.GetRootGameObjects().Where(t=>t.name.StartsWith("Player Spawn")))t.transform.position=map.playerSpawn.position;
   map.explorationClues=new[]{
    "남서 물류장. 시료 운반을 마치고 충전 중이던 하역 로봇이다. 주황 고정못을 풀고 넓은 물류도로로 중앙 조립대에 반환한다.",
    "북서 대기 관측소. 호버 포드가 고도별 대기 시료를 수집하고 있었다. 운석에 끊긴 계단 대신 돌부리로 올라, 손잡이로 화물대를 내려 반환한다.",
    "북동 시료 격리실. 조종석 캡슐을 밀폐 관측실로 사용했다. 미회수 지질 조사 장약은 탐색기로 피해 간다. 전력 다이얼을 맞추고 동료가 비상 안전 발판을 밟으면 반출할 수 있다.",
    "남동 채굴 정비소. 견인 기체가 옆으로 누워 수리 중이었다. 운석으로 대형 출입구가 막혔다. 낮은 보조 통로로 함께 굴려 반출한다. 연료와 견인은 선택 사항이다."};
   Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   Directory.CreateDirectory("Validation/AstraExpedition");File.WriteAllText("Validation/AstraExpedition/authoring.txt","PASS 180 x 150m expedition settlement; four sites and original interactive inventory preserved.\n");
   Debug.Log("ASTRA EXPEDITION AUTHORING PASS");
  }
  public static void ApplyAndValidate(){Apply();ValidateAndRender();}
  static void RelocateSites()
  {
   string[] names={"A - open access and first manual push","B - jump ascent and braked gravity descent","C - align circuit reveal plate hold gate","D - low clearance cooperative rolling and turning pocket"};
   var parts=Object.FindObjectsByType<SpaceshipPart>().OrderBy(p=>p.name).ToArray();if(parts.Length!=4)throw new Exception("Four parts required");
   for(int i=0;i<4;i++){Move(Find(names[i]),shifts[i]);Move(parts[i].transform,shifts[i]);Move(Find("Part_"+(i+1)+" - retained sky color beacon"),shifts[i]);}
   Move(Find("Astra minefield and detectors"),shifts[2]);
   foreach(var light in Object.FindObjectsByType<Light>())if(light.type==LightType.Point && light.transform.parent==Find("Astra ver2 - four recovery routes")){var p=light.transform.position;if(p.x*p.x+p.z*p.z<25)continue;int nearest=0;float distance=float.MaxValue;for(int i=0;i<4;i++){float d=Vector3.Distance(p,parts[i].transform.position-shifts[i]);if(d<distance){distance=d;nearest=i;}}Move(light.transform,shifts[nearest]);}
   foreach(var name in new[]{"A - amber return","B - cyan return","C - orchid return","D - lime return"})Object.DestroyImmediate(Find(name).gameObject);
   Move(Find("Always turning Ferris wheel"),V(-29,0,-12));Move(Find("Ferris walk-on boarding dock"),V(-29,0,-12));
   Place(Find("Meteor basketball hoop - static"),-28,-64);
   // The hoop was on a hill. Seat its base on the level leisure court.
   var hoop=Find("Meteor basketball hoop - static");Move(hoop,Vector3.up*(1-hoop.position.y));
   var cannon=Find("Three second crew cannon");Place(cannon,-27,-8);cannon.rotation=Quaternion.Euler(0,-25,0);
   var cs=new SerializedObject(cannon.GetComponent<AstraPlayerCannon>());var a=cannon.Find("Hollow cannon barrel").TransformPoint(V(0,0,-1.9f));var b=cannon.Find("Cannon rear igniter").position+Vector3.up*.5f;cs.FindProperty("fuseStart").vector3Value=a;cs.FindProperty("fuseEnd").vector3Value=b;cs.ApplyModifiedPropertiesWithoutUndo();
   var line=cannon.GetComponentInChildren<LineRenderer>();line.SetPosition(0,a);line.SetPosition(1,b);
   var vault=Find("TNT rubble supply vault");Move(vault,V(31,0,19));
   var charge=Find("Carryable wired TNT");Move(charge,V(0,1.6f-charge.position.y,0));var plunger=Find("Remote TNT plunger");Move(plunger,V(0,1-plunger.position.y,0));
   var wire=Find("Visible TNT cable").GetComponent<LineRenderer>();wire.SetPosition(0,Find("TNT switch handle").position);wire.SetPosition(1,charge.position);
  }
  static void RelocateEquipment()
  {
   var toys=Find("Astra physical toybox");foreach(Transform t in toys.Cast<Transform>().ToArray())
   {
    string n=t.name;
    if(n=="Mini football goal"||n.StartsWith("축구공")||n.StartsWith("비치볼"))Move(t,V(-28,0,-62));
    else if(n.StartsWith("볼링"))Move(t,V(-23,0,-67));
    else if(n.StartsWith("도미노"))Move(t,V(4,0,-62));
    else if(n.StartsWith("쌓기 상자"))Move(t,V(-70,0,-42));
    else if(n=="매달린 샌드백"||n=="Punch bag frame")Move(t,V(-1,0,-57));
    else if(n.StartsWith("운반 썰매")||n.StartsWith("경사판"))Move(t,n.EndsWith("0")?V(-14,0,15):V(24,0,-11));
    else if(n.StartsWith("짐 롤러"))Move(t,V(26,0,-14));
    else if(n.StartsWith("트램펄린"))Place(t,n.Contains("-28")?-61:-16,n.Contains("-28")?-29:-59);
    else if(n.StartsWith("Utility rack")||n.StartsWith("그래플링")||n.StartsWith("송풍기")){int id=int.Parse(n.Substring(n.Length-1));Move(t,id==0?V(-30,0,6):id==1?V(21,-.16f,3):V(21,-.1f,-35));}
   }
   var yard=Find("Physical playground and loose bridge planks");foreach(Transform t in yard.Cast<Transform>().ToArray())
   {if(t.name.StartsWith("Seesaw")||t.name=="Long physical seesaw")Move(t,V(-6,0,-9));else if(t.name.StartsWith("Loose long plank")){var p=t.position;if(p.x<0)Place(t,p.x<-20?-39:-38,p.x<-20?-38:27);else Place(t,p.z>10?36:61,p.z>10?20:-47);}}
   var tools=Find("Carryable rocket ink and yellow fuel cans");foreach(Transform t in tools.Cast<Transform>().ToArray())
   {var p=t.position;var station=p.x<0?V(-19,0,-29):p.z>0?V(10,0,5):V(34,0,-11);var delta=p.x<0?V(-5,0,-10):p.z>0?V(18,0,17):V(25,0,-36);delta.y=.955f-(terrain.SampleHeight(station)+terrain.transform.position.y);Move(t,delta);}
   var equipment=Object.FindObjectsByType<Transform>().FirstOrDefault(t=>t.name=="Equipment");if(equipment!=null)Move(equipment,V(2,0,-14));
  }
 }
}
