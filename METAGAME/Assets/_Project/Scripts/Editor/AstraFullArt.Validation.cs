using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class AstraFullArt
 {
  public static void ValidateFirstPerson()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);int checkedTools=0;
   var camera=new GameObject("Temporary first-person validation camera");
   try
   {
    foreach(var root in All(scene).Where(t=>t.Find("Model")!=null&&t.GetComponent<ToolBase>()!=null))
    {
     var model=root.Find("Model");if(!model.GetComponentsInChildren<MeshFilter>(true).Any(m=>AssetDatabase.GetAssetPath(m.sharedMesh).StartsWith(Art)))continue;
     var renderers=model.GetComponentsInChildren<MeshRenderer>(true);var enabled=renderers.Select(r=>r.enabled).ToArray();
     var shadows=renderers.Select(r=>r.shadowCastingMode).ToArray();var force=renderers.Select(r=>r.forceRenderingOff).ToArray();
     using(var copy=new FirstPersonToolVisual(model,camera.transform))
     {
      copy.SetVisible(true);copy.SyncAnimation();var copied=copy.Model.GetComponentsInChildren<MeshRenderer>(true);
      Check(copied.Length==renderers.Length,"First-person renderer inventory "+root.name);
      for(int i=0;i<renderers.Length;i++)
      {
       Check(copied[i].enabled==enabled[i],"First-person visibility "+root.name);
       Check(copied[i].GetComponent<MeshFilter>().sharedMesh==renderers[i].GetComponent<MeshFilter>().sharedMesh,"First-person mesh "+root.name);
       Check(copied[i].sharedMaterials.SequenceEqual(renderers[i].sharedMaterials),"First-person materials "+root.name);
      }
      var led=model.Find("Red LED");
      if(led!=null)
      {
       var block=new MaterialPropertyBlock();led.GetComponent<Renderer>().GetPropertyBlock(block);
       var original=new MaterialPropertyBlock();led.GetComponent<Renderer>().GetPropertyBlock(original);
       block.SetColor("_BaseColor",Color.red);led.GetComponent<Renderer>().SetPropertyBlock(block);copy.SyncAnimation();
       var copiedLed=copy.Model.Find("Red LED").GetComponent<Renderer>();copiedLed.GetPropertyBlock(block);
       Check(block.GetColor("_BaseColor")==Color.red,"Detector live LED not copied");led.GetComponent<Renderer>().SetPropertyBlock(original);
      }
      copy.SetVisible(false);
     }
     for(int i=0;i<renderers.Length;i++)Check(renderers[i].enabled==enabled[i]&&renderers[i].shadowCastingMode==shadows[i]&&renderers[i].forceRenderingOff==force[i],"First-person hide/show leaked state");
     checkedTools++;
    }
    Check(checkedTools==23,"Expected 23 replaced handheld tools, got "+checkedTools);
    Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/first-person.txt","PASS: 23 tools clone the new meshes/materials; disabled legacy renderers stay disabled; three detector LEDs copy live property blocks; hide/show restores original visibility and shadows. Editor exercise, not player/multi-peer gameplay.\n");
    Debug.Log("ASTRA_FULL_ART_FIRST_PERSON_PASS "+checkedTools);
   }
   finally { Object.DestroyImmediate(camera); }
  }
  static void RenderCamera(Vector3 position,Vector3 look,string name,float fov=52)
  {
   var go=new GameObject("Temporary art validation camera");var camera=go.AddComponent<Camera>();
   go.transform.position=position;go.transform.LookAt(look);camera.fieldOfView=fov;camera.nearClipPlane=.025f;camera.farClipPlane=400;
   camera.clearFlags=CameraClearFlags.Skybox;ShaderUtil.allowAsyncCompilation=false;
   var target=new RenderTexture(1400,1000,24);target.Create();
   var request=new RenderPipeline.StandardRequest{destination=target};
   for(int i=0;i<5;i++)RenderPipeline.SubmitRenderRequest(camera,request);
   var prior=RenderTexture.active;RenderTexture.active=target;
   var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();
   File.WriteAllBytes(Evidence+"/"+name+".png",image.EncodeToPNG());RenderTexture.active=prior;
   Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(go);
  }
  public static void RenderPreviews()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory(Evidence);var all=All(scene);
   foreach(var type in new[]{"AstraRocketTool","AstraInkLauncher","AstraFuelCan","AstraGrappleTool","AstraAirBlower","AstraMineDetector"})
   {
    var t=all.First(n=>n.GetComponents<MonoBehaviour>().Any(c=>c!=null&&c.GetType().Name==type));
    RenderCamera(t.TransformPoint(new Vector3(1.15f,.8f,-1.45f)),t.TransformPoint(new Vector3(0,0,.22f)),type,48);
   }
   RenderCamera(new Vector3(-25,9,-43),new Vector3(-37,2,-27),"E01-logistics");
   RenderCamera(new Vector3(-34,17,33),new Vector3(-48,9,46),"E02-observatory");
   RenderCamera(new Vector3(40,8,25),new Vector3(48,3,36),"M09-quarantine-exterior");
   RenderCamera(new Vector3(45,4,37),new Vector3(52,2.5f,41),"E03-quarantine-interior");
   RenderCamera(new Vector3(39,7,-47),new Vector3(50,2,-38),"E04-workshop");
   RenderCamera(new Vector3(-29,8,-58),new Vector3(-16,2,-45),"E05-camp");
   var cannon=all.First(t=>t.name=="Three second crew cannon");
   RenderCamera(cannon.TransformPoint(new Vector3(5,4,7)),cannon.TransformPoint(new Vector3(0,1.6f,0)),"M14-cannon");
   var charge=all.First(t=>t.name=="Carryable wired TNT");RenderCamera(charge.position+new Vector3(2,1.3f,-2),charge.position,"P10-charge");
   var dial=all.First(t=>t.name=="Circuit dial 1 - grab and release");RenderCamera(dial.position+new Vector3(.8f,.5f,-2),dial.position,"M08-dial",48);
   var warning=all.First(t=>t.name=="Fall warning 0");RenderCamera(warning.TransformPoint(new Vector3(2,2,-4.5f)),warning.position+Vector3.up,"M18-fall-warning");
   var marker=all.First(t=>t.name=="Carryable survey route marker 0");RenderCamera(marker.position+new Vector3(1.6f,1.2f,-2.3f),marker.position+Vector3.up*.35f,"P08-route-marker");
   ValidateFirstPerson();
   Debug.Log("ASTRA_FULL_ART_RENDER_PASS");
  }
 }
}
