using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Object=UnityEngine.Object;
public static class AstraDifficultySmoke
{
 static double began;static float at;static Rigidbody stop,plank;static Vector3 home;static AstraV2Puzzle puzzle;
 static void Check(bool ok,string label){File.AppendAllText("Validation/AstraDifficulty/runtime.txt",(ok?"PASS ":"FAIL ")+label+"\n");if(!ok)throw new Exception(label);}
 public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra_ver2.unity");SessionState.SetBool("DifficultySmoke",true);SessionState.SetInt("DifficultyStep",0);began=EditorApplication.timeSinceStartup;EditorApplication.update+=Start;}
 static void Start(){if(EditorApplication.timeSinceStartup-began<3)return;EditorApplication.update-=Start;EditorApplication.EnterPlaymode();}
 [InitializeOnLoadMethod]static void Resume(){if(SessionState.GetBool("DifficultySmoke",false))EditorApplication.update+=Tick;}
 static void Next(int i){at=Time.time;SessionState.SetInt("DifficultyStep",i);}
 static void Put(Rigidbody b,Vector3 p){b.position=p;b.rotation=Quaternion.identity;b.linearVelocity=Vector3.zero;b.angularVelocity=Vector3.zero;}
 static void Tick()
 {
  if(!EditorApplication.isPlaying)return;
  try{
   var boot=Object.FindAnyObjectByType<SpaceBootstrap>();if(boot==null||boot.Player==null)return;var p=boot.Player;int step=SessionState.GetInt("DifficultyStep",0);float age=Time.time-at;
   if(step==0){Directory.CreateDirectory("Validation/AstraDifficulty");File.WriteAllText("Validation/AstraDifficulty/runtime.txt","");boot.Session.ScriptedInput=true;foreach(var m in Object.FindObjectsByType<Metagame.Gameplay.Hazards.MeteorSpawner>())m.enabled=false;Next(1);}
   else if(step==1&&age>3){var bodies=GameObject.Find("Expedition physical learning and recovery").GetComponentsInChildren<Rigidbody>();Check(bodies.Length==16,"16 new physics objects exist during play");foreach(var b in bodies)Check(b.position.y>.8f&&b.linearVelocity.magnitude<2,"settled above terrain: "+b.name);
    plank=GameObject.Find("Practice bridge plank").GetComponent<Rigidbody>();Put(plank,new Vector3(-22,1.65f,-27.75f));Next(2);}
   else if(step==2&&age>1){Check(Mathf.Abs(plank.position.y-1.59f)<.15f&&Vector3.Dot(plank.transform.up,Vector3.up)>.95f,"movable plank spans two practice docks stably");p.Teleport(new Vector3(-22,1.8f,-27.75f),Quaternion.identity);Next(3);}
   else if(step==3&&age>1){Check(p.Grounded&&p.FootPosition.y>1.55f,"player can stand on the loose practice bridge");p.Teleport(new Vector3(-64.4f,3.55f,37.4f),Quaternion.identity);Next(4);}
   else if(step==4&&age>1){Check(p.Grounded&&p.FootPosition.y>3.3f,"lower rest shelf supports a real player");p.Teleport(new Vector3(-62.4f,5.8f,46),Quaternion.identity);Next(5);}
   else if(step==5&&age>1){Check(p.Grounded&&p.FootPosition.y>5.5f,"upper rest shelf supports a real player");p.Teleport(new Vector3(39,1.1f,24.6f),Quaternion.identity);puzzle=Object.FindAnyObjectByType<AstraV2Puzzle>();stop=GameObject.Find("Airlock maintenance stop 0").GetComponent<Rigidbody>();home=stop.position;Put(stop,new Vector3(45.6f,1.25f,35));Next(6);}
   else if(step==6&&age>1){Check(puzzle.OpenFraction<.01f,"door stop cannot bypass an unsolved closed airlock");puzzle.ApplySnapshot(new AstraV2Puzzle.State{A=0,B=1,C=2,Solved=true,Reveal=1,Open=1});Next(7);}
   else if(step==7&&age>1){Check(!puzzle.Occupied&&puzzle.OpenFraction>.99f,"edge stop keeps an already opened airlock open through existing safety beam");Put(stop,home);Next(8);}
   else if(step==8&&age>2){Check(puzzle.OpenFraction<.01f,"airlock closes normally after stop removal");Put(stop,new Vector3(44,1.4f,31));Next(9);}
   else if(step==9&&age>1){Check(!puzzle.Occupied&&puzzle.OpenFraction<.01f,"prop on pressure plate does not replace a living crew member");Put(stop,home);var flag=GameObject.Find("Carryable survey route marker 0").GetComponent<Rigidbody>();home=flag.position;Put(flag,new Vector3(home.x,-5,home.z));Next(10);}
   else if(step==10&&age>.6f){var flag=GameObject.Find("Carryable survey route marker 0").GetComponent<Rigidbody>();Check(flag.position.y>.8f&&Vector2.Distance(new Vector2(flag.position.x,flag.position.z),new Vector2(home.x,home.z))<.2f,"lost route marker recovers through existing cargo recovery");Check(!p.IsDead,"survey staging area is outside live mine triggers");SessionState.SetBool("DifficultySmoke",false);EditorApplication.update-=Tick;EditorApplication.Exit(0);}
  }catch(Exception e){Debug.LogException(e);SessionState.SetBool("DifficultySmoke",false);EditorApplication.Exit(2);}
 }
}

