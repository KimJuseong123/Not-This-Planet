// Run only in an isolated copy of METAGAME with Unity -executeMethod
// IntegrateHangarRelated.Run. Copy the resulting scene back after validation.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class IntegrateHangarRelated
{
 const string ScenePath="Assets/Scenes/Stage1_Astra.unity";
 const string Models="Assets/_Project/Models/Environment/Stage1/HangarRelated";
 static readonly string Output=Path.GetFullPath("../../artifacts/stage1-related-hangar/integration");
 static Transform root;
 static StringBuilder report;
 static readonly string[] Names={"SouthApproachWalkway","EastEntranceLanding","PedestrianAccess","FoundationApproachDeck","BrokenPersonnelBridge0","BrokenPersonnelBridge1","BrokenPersonnelBridge2","BrokenPersonnelBridge5","BrokenPersonnelBridge6","ModularEdgeCurb","CargoApronEntryStairs","AdjustableDeckSupport","RouteWayfindingBeacon"};
 static int placed;
 static Transform[] All()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static GameObject Model(int index,string name,Vector3 pos,float yaw=0,Vector3? scale=null)
 {
  string path=Directory.GetFiles(Models,"SM_Astra_"+Names[index]+".fbx",SearchOption.AllDirectories).Single().Replace('\\','/');
  var wrapper=new GameObject(name);wrapper.transform.SetParent(root,false);
  var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
  if(!asset)throw new Exception("Missing model: "+path);
  var mesh=(GameObject)PrefabUtility.InstantiatePrefab(asset,wrapper.transform);
  // FBX maps Blender +Y to Unity -Z; restore +Z as the construction direction.
  mesh.transform.localRotation=Quaternion.Euler(0,180,0)*mesh.transform.localRotation;
  PrefabUtility.RecordPrefabInstancePropertyModifications(mesh.transform);
  wrapper.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));
  wrapper.transform.localScale=scale??Vector3.one;
  foreach(var t in wrapper.GetComponentsInChildren<Transform>())t.gameObject.layer=8;
  foreach(var r in wrapper.GetComponentsInChildren<Renderer>())
   if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name.Contains("Error")))throw new Exception("Missing material: "+name);
  placed++; report.AppendLine("PLACED "+name+" model="+Names[index]+" position="+pos.ToString("F3")+" yaw="+yaw+" scale="+wrapper.transform.localScale);
  return wrapper;
 }
 static BoxCollider Box(Transform parent,string name,Vector3 center,Vector3 size,Quaternion? rotation=null)
 {
  var g=new GameObject(name);g.layer=8;g.transform.SetParent(parent,false);
  g.transform.localPosition=center;g.transform.localRotation=rotation??Quaternion.identity;
  var c=g.AddComponent<BoxCollider>();c.size=size;return c;
 }
 static void Slab(int index,string name,Vector3 center,Vector3 size,float yaw=0)
 {
  var g=Model(index,name,center-Vector3.up*size.y*.5f,yaw);
  Box(g.transform,"Walkable slab",V(0,size.y*.5f,0),size);
 }
 static void CurbRow(string name,Vector3 start,float length,float yaw)
 {
  var direction=Quaternion.Euler(0,yaw,0)*Vector3.right;
  for(float x=0;x<length-.001f;x+=2){float n=Mathf.Min(2,length-x);var g=Model(9,name+" "+(x/2+1),start+direction*(x+n/2),yaw,V(n/2,1,1));Box(g.transform,"Curb collision",V(0,.1f,0),V(2,.2f,.16f));}
 }
 static void Support(string name,Vector3 top,float yaw=0)
 {
  var hits=Physics.RaycastAll(top-Vector3.up*.3f,Vector3.down,30,1<<8,QueryTriggerInteraction.Ignore).Where(h=>!h.transform.IsChildOf(root)).OrderBy(h=>h.distance).ToArray();
  if(hits.Length==0){report.AppendLine("SKIPPED support: no terrain "+name);return;}
  float bottom=hits[0].point.y,height=top.y-bottom;
  if(height<.8f){report.AppendLine("SKIPPED support: ground already supports deck "+name);return;}
  var g=Model(11,name,V(top.x,bottom,top.z),yaw,V(1,height/4.8f,1));
  Box(g.transform,"Support column",V(0,2.4f,0),V(.65f,4.8f,.65f));
 }
 public static void Run()
 {
  if(!Application.isBatchMode||!Path.GetFullPath(Application.dataPath).Replace('\\','/').Contains("/tmp/hangar-unity/"))throw new Exception("Use the isolated work copy.");
  Directory.CreateDirectory(Output);report=new StringBuilder();placed=0;
  EditorSceneManager.OpenScene(ScenePath);
  var all=All();
  var oldNames=new[]{"Hangar south approach walkway","Hangar east entrance landing","Hangar pedestrian access","Hangar foundation and approach deck","Walkway south curb","Walkway east curb","Broken personnel bridge 0","Broken personnel bridge 1","Broken personnel bridge 2","Broken personnel bridge 5","Broken personnel bridge 6"};
  var old=all.Where(t=>oldNames.Contains(t.name)||t.name.StartsWith("Cargo pickup apron")&&t.name.Contains("entry stair")).ToArray();
  if(old.Length!=15||all.Any(t=>t.name=="Hangar Related - Concept Models"))throw new Exception("Unexpected replacement baseline: "+old.Length);
  foreach(var t in old){if(t.childCount!=0||t.GetComponents<Component>().Any(c=>!(c is Transform||c is MeshFilter||c is MeshRenderer||c is BoxCollider)))throw new Exception("Unexpected content on "+t.name);}
  File.Copy(ScenePath,Output+"/Stage1_Astra.before.unity",true);
  root=new GameObject("Hangar Related - Concept Models").transform;
  // Save the original scalar collision dimensions; only the old objects are removed.
  foreach(var t in old){report.AppendLine("REMOVED "+t.name+" at "+t.position);UnityEngine.Object.DestroyImmediate(t.gameObject);}
  Slab(0,"South approach - panel walkway",V(-23.25f,5.25f,18.25f),V(2.8f,.3f,14.1f),90);
  Slab(1,"East entrance - landing",V(-17,5.25f,21.9f),V(3.5f,.3f,9.5f));
  Slab(3,"Hangar foundation - panel deck",V(-27.05f,5.15f,27),V(23.1f,.5f,15.5f));
  // The mesh already contains its slope. Its nominal top rises 5.33m along +Z.
  // Meet the FRONT edge of the walkway rather than continuing underneath it.
  float angle=Mathf.Asin(5.33f/17.144428f),yaw=Mathf.Atan2(5.5f,13.95f)*Mathf.Rad2Deg;
  float runScale=Mathf.Sqrt(5.5f*5.5f+13.95f*13.95f)/(17.144428f*Mathf.Cos(angle));
  var q=Quaternion.Euler(0,yaw,0);
  var ramp=Model(2,"Pedestrian access - inclined panels",V(-32.25f,.05f-.4f*Mathf.Cos(angle),9.975f)+q*V(0,0,.4f*Mathf.Sin(angle)*runScale),yaw,V(1,1,runScale));
  var rampDelta=V(5.5f,5.33f,13.95f);
  var rampRotation=Quaternion.LookRotation(rampDelta);
  // Keep the collision box outside the scaled mesh hierarchy: a rotated box
  // under non-uniform scale is approximated by PhysX and changes the slope.
  Box(root,"Continuous ramp collision",V(-32.25f,2.715f,9.975f)-rampRotation*V(0,.2f,0),V(3.2f,.4f,rampDelta.magnitude),rampRotation);
  int[] bridgeNumbers={0,1,2,5,6};
  // The original fragments intruded into the newer diagonal ramp. Shift the
  // abandoned bridge 2m east, retaining segment spacing and its 2.9m break.
  for(int i=0;i<5;i++){int n=bridgeNumbers[i];Slab(4+i,"Personnel bridge - fragment "+n,V(-27,5,12+1.35f*n),V(2.6f,.4f,1.15f));}
  // Leave an entrance opening where the diagonal ramp reaches the walkway.
  CurbRow("South edge curb",V(-27.55f,5.4f,16.94f),10.8f,0);
  CurbRow("East edge curb",V(-15.32f,5.4f,16.85f),6.8f,-90);
  // The authored circular apron extends farther south than its old blockout.
  // Move the stairs out from underneath its rim.
  Physics.SyncTransforms();
  var apron=All().Single(t=>t.name=="CapsuleHangar");
  float apronEdge=0,apronHeight=0;
  for(float z=16;z<26;z+=.02f){
   if(Physics.Raycast(V(-9.5f,8,z),Vector3.down,out var hit,5,1<<8)&&hit.transform.IsChildOf(apron)){
    apronEdge=z;apronHeight=hit.point.y;break;
   }
  }
  if(apronEdge==0)throw new Exception("Cannot locate authored apron edge");
  if(!Physics.Raycast(V(-9.5f,7,apronEdge-1.5f),Vector3.down,out var ground,10,1<<8))throw new Exception("No ground for stairs");
  float stairScale=(apronHeight-ground.point.y)/.81f;
  if(stairScale<.3f||stairScale>2)throw new Exception("Unexpected apron rise: "+stairScale);
  var stairs=Model(10,"Cargo apron - four step entry",V(-9.5f,ground.point.y,apronEdge-.84f),0,V(1,stairScale,1));
  report.AppendLine("Stair fit: apron edge="+apronEdge+" surface="+apronHeight+" terrain="+ground.point.y);
  for(int i=0;i<4;i++)Box(stairs.transform,"Step "+(i+1),V(0,(.24f+.19f*i)/2,-.72f+.48f*i),V(3.3f,.24f+.19f*i,.6f));
  Physics.SyncTransforms();
  Support("Foundation support southwest",V(-36,4.9f,21));
  Support("Foundation support northwest",V(-36,4.9f,33));
  Support("Foundation support northeast",V(-19,4.9f,33));
  Support("Foundation support southeast",V(-19,4.9f,21));
  Support("South walkway support",V(-24,5.1f,18.25f),90);
  Support("Abandoned bridge abutment",V(-27,4.8f,12));
  foreach(var p in new[]{V(-34.9f,0,2.5f),V(-30.4f,5.4f,18.1f),V(-16.25f,5.4f,23.8f)}){
   var g=Model(12,"Route beacon "+placed,p);Box(g.transform,"Beacon post",V(0,.8f,0),V(.45f,1.6f,.4f));
  }
  Physics.SyncTransforms();
  Validate();
  EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
  if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))throw new Exception("Scene save failed");
  report.AppendLine("PASS saved scene; models="+placed+"; removed=15");
  File.WriteAllText(Output+"/placement-report.txt",report.ToString());
  Capture();
  VerifySaved();
 }
 static void Validate()
 {
  int count=0;
  Vector3[] path={V(-34.8f,0,3.5f),V(-29.5f,0,16.95f),V(-29,0,18.25f),V(-17,0,18.25f),V(-17,0,27),V(-26,0,27)};
  for(int j=1;j<path.Length;j++){
   int steps=Mathf.CeilToInt(Vector3.Distance(path[j-1],path[j])/.25f);
   for(int i=0;i<=steps;i++){
    var p=Vector3.Lerp(path[j-1],path[j],i/(float)steps);
    if(!Physics.Raycast(V(p.x,6.6f,p.z),Vector3.down,out var h,10,1<<8,QueryTriggerInteraction.Ignore))throw new Exception("Missing route support "+p);
    var blockers=Physics.OverlapCapsule(h.point+Vector3.up*.48f,h.point+Vector3.up*1.6f,.33f,1<<8,QueryTriggerInteraction.Ignore);
    if(blockers.Length>0)throw new Exception("Route blocked "+h.point+" by "+string.Join(",",blockers.Select(c=>c.name)));
    count++;
   }
  }
  if(All().Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new Exception("Missing scene scripts");
  report.AppendLine("PASS supported route and capsule clearance: "+count+" samples; radius .33m, body top 1.93m");
  foreach(var r in root.GetComponentsInChildren<Renderer>())if(!r.enabled||r.bounds.size.sqrMagnitude<.001f)throw new Exception("Invalid renderer "+r.name);
  report.AppendLine("PASS all imported renderers, shaders, materials; missing scripts=0");
 }
 public static void VerifySaved()
 {
  EditorSceneManager.OpenScene(ScenePath);
  root=All().Single(t=>t.name=="Hangar Related - Concept Models");report=new StringBuilder();
  Capture();
  Physics.SyncTransforms();Validate();
  float worst=0;
  for(int i=1;i<40;i++){
   float f=i/40f;var p=Vector3.Lerp(V(-35,.05f,3),V(-29.5f,5.38f,16.95f),f);
   if(!Physics.Raycast(p+Vector3.up*2,Vector3.down,out var h,3,1<<8))throw new Exception("Missing ramp support");
   worst=Mathf.Max(worst,Mathf.Abs(p.y-h.point.y));
  }
  if(worst>.03f)throw new Exception("Ramp surface error "+worst);
  report.AppendLine("PASS ramp plane: 39 samples, max height error="+worst);
  for(int i=0;i<4;i++){
   var stairs=root.Find("Cargo apron - four step entry");
   var expected=stairs.TransformPoint(V(0,.24f+.19f*i,-.72f+.48f*i));
   var p=expected+Vector3.up*2;
   if(!Physics.Raycast(p,Vector3.down,out var h,3,1<<8)||Mathf.Abs(h.point.y-expected.y)>.02f)throw new Exception("Stair height mismatch "+i+" hit="+h.point+" collider="+(h.collider?h.collider.name:"none"));
   report.AppendLine("PASS stair "+(i+1)+" surface="+h.point.y);
  }
  foreach(var r in root.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials){
   if(!m.GetTexture("_BaseMap")||!m.GetTexture("_BumpMap")||!m.GetTexture("_MetallicGlossMap"))throw new Exception("Missing texture on "+m.name);
  }
  report.AppendLine("PASS saved/reopened scene; renderers="+root.GetComponentsInChildren<Renderer>().Length+"; box colliders="+root.GetComponentsInChildren<BoxCollider>().Length+"; texture references complete");
  File.WriteAllText(Output+"/saved-scene-validation.txt",report.ToString());
  Capture();
 }
 static void Capture()
 {
  var g=new GameObject("Temporary inspection camera");var c=g.AddComponent<Camera>();c.fieldOfView=55;c.nearClipPlane=.05f;c.farClipPlane=500;
  var rt=new RenderTexture(1600,1000,24);var old=RenderTexture.active;
  try{
   c.targetTexture=rt;
   void Shot(string name,Vector3 pos,Vector3 target){c.transform.position=pos;c.transform.LookAt(target);c.Render();RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
   Shot("hangar-overview",V(-2,24,1),V(-26,6,23));
   Shot("hangar-approach",V(-40,12,-1),V(-27,5,18));
   Shot("hangar-entrance",V(-4,12,14),V(-19,5.5f,24));
   var stairTarget=root.Find("Cargo apron - four step entry").position+Vector3.up*.5f;
   Shot("cargo-entry-stairs",stairTarget+V(6,3,-6),stairTarget);
  }finally{RenderTexture.active=old;c.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(g);}
 }
}
