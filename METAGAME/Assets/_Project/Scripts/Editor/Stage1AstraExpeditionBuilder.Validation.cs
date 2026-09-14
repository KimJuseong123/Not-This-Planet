using System;
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
  public static void ValidateAndRender()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);var parts=Object.FindObjectsByType<SpaceshipPart>().OrderBy(p=>p.name).ToArray();
   var log=new System.Text.StringBuilder();int count=0;var failures=new System.Collections.Generic.List<string>();
   Action<bool,string> check=(ok,label)=>{log.AppendLine((ok?"PASS ":"FAIL ")+label);if(!ok)failures.Add(label);};
   check(parts.Length==4,"four original modules");check(Object.FindObjectsByType<PartClamp>().Length==16,"16 original clamps and tethers");
   check(Object.FindObjectsByType<AstraMineDetector>().Length==3 && Object.FindAnyObjectByType<AstraMinefield>().MinePositions.Length==16,"all mine equipment retained");
   check(Object.FindObjectsByType<AstraRocketTool>().Length==3&&Object.FindObjectsByType<AstraInkLauncher>().Length==3&&Object.FindObjectsByType<AstraFuelCan>().Length==8,"rocket ink and yellow fuel inventory retained");
   check(Object.FindObjectsByType<AstraGrappleTool>().Length==3&&Object.FindObjectsByType<AstraAirBlower>().Length==3&&Object.FindObjectsByType<AstraBouncePad>().Length==2,"grapples blowers and bounce pads retained");
   check(Object.FindAnyObjectByType<AstraFerrisWheel>()!=null&&Object.FindAnyObjectByType<AstraPlayerCannon>()!=null&&Object.FindAnyObjectByType<AstraBlastGate>()!=null,"wheel cannon and TNT retained");
   check(Object.FindAnyObjectByType<Terrain>().terrainData.size==new Vector3(180,30,150),"terrain area expanded 3x to 180 x 150m");
   foreach(var g in scene.GetRootGameObjects())foreach(var t in g.GetComponentsInChildren<Transform>(true))check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"script reference "+t.name);
   // Keep the report compact after the complete reference scan.
   log.Clear();log.AppendLine("PASS complete inventory and missing-script scan");
   Action<int,float,Vector2[]> route=(index,floor,points)=>
   {
    parts[index].TryGetHullBounds(out var hull);var half=hull.extents;Physics.SyncTransforms();
    for(int i=1;i<points.Length;i++)
    {int steps=Mathf.CeilToInt(Vector2.Distance(points[i-1],points[i])/.35f);for(int s=0;s<=steps;s++)
     {var p=Vector2.Lerp(points[i-1],points[i],s/(float)steps);var center=V(p.x,floor+.04f+half.y,p.y);
      var hits=Physics.OverlapBox(center,half,Quaternion.identity,GameLayers.GroundMask,QueryTriggerInteraction.Ignore);
      check(hits.Length==0,"module "+(index+1)+" clearance "+p+" "+string.Join(",",hits.Select(c=>c.name)));
      check(Physics.Raycast(V(p.x,floor+.12f,p.y),Vector3.down,.24f,GameLayers.GroundMask),"continuous support "+p);count++;}}
   };
   Find("Pressure gate - flush threshold").position+=Vector3.up*3.8f;
   route(1,7.4f,new[]{P(-48,46),P(-48,44),P(-38.2f,44)});Find("Gravity cargo deck").position=V(-38.2f,.8f,44);
   for(int i=0;i<4;i++)route(i,1,CargoRoutes[i]);
   int cargoCount=count;int walkCount=0;
   foreach(var path in Walks)for(int i=1;i<path.Length;i++){int steps=Mathf.CeilToInt(Vector2.Distance(path[i-1],path[i])/.5f);for(int s=0;s<=steps;s++){var p=Vector2.Lerp(path[i-1],path[i],s/(float)steps);var hits=Physics.OverlapCapsule(V(p.x,1.4f,p.y),V(p.x,2.1f,p.y),.3f,GameLayers.GroundMask,QueryTriggerInteraction.Ignore);check(hits.Length==0,"walking loop clear "+p+" "+string.Join(",",hits.Select(c=>c.name)));check(Physics.Raycast(V(p.x,1.12f,p.y),Vector3.down,.24f,GameLayers.GroundMask),"walking loop supported "+p);walkCount++;}}
   var mine=Object.FindAnyObjectByType<AstraMinefield>();var mines=mine.MinePositions;
   var safe=new[]{new[]{P(48,25),P(48,27.5f),P(50,28.2f),P(50,29.9f),P(49,31.6f),P(48,32.4f),P(48,34.7f)},new[]{P(48,32.4f),P(45,32.4f),P(44,31)}};
   foreach(var path in safe)for(int i=1;i<path.Length;i++)for(int s=0;s<=100;s++){var p=Vector2.Lerp(path[i-1],path[i],s/100f);check(mines.All(m=>Vector2.Distance(p,P(m.x,m.z))>.95f),"mine safe route "+p);}
   var lift=new SerializedObject(Object.FindAnyObjectByType<AstraV2FreightLift>());check(Vector3.Distance(lift.FindProperty("_bottom").vector3Value,V(-38.2f,.8f,44))<.01f,"relocated freight destination");
   var puzzle=new SerializedObject(Object.FindAnyObjectByType<AstraV2Puzzle>());check(Vector3.Distance(puzzle.FindProperty("_doorClosed").vector3Value,V(48,2.7f,35))<.01f,"relocated airlock destination");
   var seesaw=Find("Long physical seesaw").GetComponent<HingeJoint>();check(Vector3.Distance(seesaw.connectedAnchor,seesaw.transform.position)<.001f,"seesaw world anchor follows scene move");
   var bag=Find("매달린 샌드백").GetComponent<ConfigurableJoint>();check(Vector3.Distance(bag.connectedAnchor,bag.transform.TransformPoint(bag.anchor))<.01f,"punch bag world anchor follows scene move");
   if(failures.Count>0){File.WriteAllLines("Validation/AstraExpedition/validation.txt",failures.Distinct().Select(s=>"FAIL "+s));throw new Exception(failures.Count+" clearance/reference failures; see validation.txt");}
   File.WriteAllText("Validation/AstraExpedition/validation.txt","PASS all inventory and serialized references\nPASS "+cargoCount+" cargo hull/support samples\nPASS "+walkCount+" walk loop capsule/support samples\nPASS two mine safe routes (0.95m minimum center clearance)\nPASS moved lift/door/joint world anchors\n");
   EditorSceneManager.OpenScene(ScenePath);var camera=Object.FindObjectsByType<Camera>().First();
   Capture(camera,V(116,125,-140),V(-1,0,0),"settlement-overview");Capture(camera,V(-72,41,-83),V(-29,2,-42),"crew-commons");Capture(camera,V(-51,17,-48),V(-32,2,-27),"logistics");Capture(camera,V(-78,24,13),V(-47,7,45),"observatory");Capture(camera,V(73,25,6),V(48,2,38),"quarantine");Capture(camera,V(74,22,-65),V(48,2,-36),"mining-workshop");
   Debug.Log("ASTRA EXPEDITION VALIDATION PASS "+cargoCount+" cargo and "+walkCount+" walking samples");
  }
  static void Capture(Camera camera,Vector3 position,Vector3 look,string name)
  {
   camera.transform.position=position;camera.transform.LookAt(look);camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=600;camera.fieldOfView=55;
   ShaderUtil.allowAsyncCompilation=false;var rt=new RenderTexture(1600,1100,24);rt.Create();var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt};for(int i=0;i<5;i++)UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
   RenderTexture.active=rt;var img=new Texture2D(1600,1100,TextureFormat.RGB24,false);img.ReadPixels(new Rect(0,0,1600,1100),0,0);img.Apply();File.WriteAllBytes("Validation/AstraExpedition/"+name+".png",img.EncodeToPNG());RenderTexture.active=null;Object.DestroyImmediate(img);rt.Release();Object.DestroyImmediate(rt);
  }
 }
}
