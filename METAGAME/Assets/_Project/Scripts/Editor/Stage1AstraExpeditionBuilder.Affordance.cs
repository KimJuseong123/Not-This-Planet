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
  const string WordlessRoot="Wordless recovery guidance";
  static AstraGlyph[] SignMeaning(string text)
  {
   if(text.Contains("FUEL"))return new[]{AstraGlyph.Can};
   if(text=="TNT"||text.Contains("BLAST"))return new[]{AstraGlyph.Burst,AstraGlyph.Can};
   if(text.Contains("HOLD"))return new[]{AstraGlyph.Hold,AstraGlyph.Down,AstraGlyph.Release,AstraGlyph.Brake};
   if(text.Contains("ALIGN"))return new[]{AstraGlyph.Dial,AstraGlyph.Arrow,AstraGlyph.Feet,AstraGlyph.Arrow,AstraGlyph.Door};
   if(text.Contains("TURN")||text.Contains("SERVICE")||text.StartsWith("D "))return new[]{AstraGlyph.Square,AstraGlyph.Roller,AstraGlyph.Turn};
   if(text.Contains("ASSEMBLY"))return new[]{AstraGlyph.Box,AstraGlyph.Arrow,AstraGlyph.Ship};
   if(text.Contains("HAB"))return new[]{AstraGlyph.Bed};
   if(text.Contains("MESS"))return new[]{AstraGlyph.Food};
   if(text.Contains("COMMONS"))return new[]{AstraGlyph.Person,AstraGlyph.Bed};
   if(text.Contains("FREIGHT"))return new[]{AstraGlyph.Box,AstraGlyph.Down};
   if(text.Contains("TRANSFER")||text.Contains("LANDING"))return new[]{AstraGlyph.Person,AstraGlyph.Cannon,AstraGlyph.Arrow};
   if(text.Contains("QUARANTINE")||text.StartsWith("C "))return new[]{AstraGlyph.Diamond,AstraGlyph.Door};
   if(text.Contains("SURVEY")||text.StartsWith("B "))return new[]{AstraGlyph.Triangle,AstraGlyph.Up};
   if(text.Contains("DRILL"))return new[]{AstraGlyph.Lever,AstraGlyph.Box};
   return new[]{AstraGlyph.Box,AstraGlyph.Roller};
  }
  static void WordlessSigns()
  {
   foreach(var text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
   {
    var r=text.GetComponent<Renderer>();if(r!=null)r.enabled=false;
    // Keep legacy TextMesh references (fuel script writes its label), but never render the prose.
    foreach(Transform child in text.transform.Cast<Transform>().ToArray())if(child.name=="Mounted facility sign"||child.name=="Sign support")Object.DestroyImmediate(child.gameObject);
    if(text.text.Contains("FUEL")||text.text=="TNT")continue;
    float size=text.name.StartsWith("Facility")?.95f:.48f;
    PicturePanel("Replaced world text with pictures",text.transform,text.transform.position-text.transform.forward*.12f,text.transform.rotation,size,SignMeaning(text.text));
   }
  }
  static Transform VisualBox(string name,Transform parent,Vector3 local,Vector3 size,Material mat)
  {var g=Box(name,parent,parent.TransformPoint(local),size,mat,false);g.transform.localRotation=Quaternion.identity;g.transform.localScale=size;return g.transform;}
  static Transform VisualShape(string name,PrimitiveType kind,Transform parent,Vector3 local,Vector3 size,Material mat)
  {var g=Shape(name,kind,parent,parent.TransformPoint(local),size,mat,false);g.transform.localRotation=Quaternion.identity;g.transform.localScale=size;return g.transform;}
  static void ToolSilhouettes()
  {
   foreach(var tool in Object.FindObjectsByType<AstraHandTool>())
   {
    var model=tool.transform.Find("Model");if(model==null)continue;
    // Preserve the root, muzzle, collider, script and effect references.
    if(tool is AstraRocketTool)
    {
     foreach(var r in model.GetComponentsInChildren<Renderer>())r.enabled=false;
     var shell=VisualShape("Rocket cylindrical pressure body",PrimitiveType.Capsule,model,V(0,.02f,.12f),V(.34f,.32f,.34f),white);shell.localRotation=Quaternion.Euler(90,0,0);
     VisualShape("Rear exhaust bell",PrimitiveType.Cylinder,model,V(0,.02f,-.25f),V(.38f,.09f,.38f),dark).localRotation=Quaternion.Euler(90,0,0);
     foreach(float x in new[]{-.2f,.2f})VisualBox("Rocket stabilizer fin",model,V(x,.02f,-.1f),V(.08f,.28f,.22f),blue);
     VisualBox("Rocket hand grip",model,V(0,-.25f,-.05f),V(.13f,.22f,.2f),orange);
     Icon(AstraGlyph.Person,model,V(0,.05f,-.35f),.2f);
    }
    else if(tool is AstraInkLauncher)
    {
     VisualShape("Ink reservoir bulb",PrimitiveType.Sphere,model,V(0,.27f,-.05f),V(.38f,.34f,.38f),colors[2]);
     Icon(AstraGlyph.Drop,model,V(0,.08f,-.32f),.28f);
    }
    else if(tool is AstraFuelCan)
    {
     var spout=VisualShape("Fuel pouring spout",PrimitiveType.Cylinder,model,V(.15f,.26f,.14f),V(.1f,.1f,.1f),orange);spout.localRotation=Quaternion.Euler(0,0,-35);
     Icon(AstraGlyph.Drop,model,V(0,0,-.32f),.26f);
     Feedback("Fuel remaining window",model,model.TransformPoint(V(0,-.15f,-.34f)),model.rotation,.28f,tool);
    }
    else if(tool is AstraAirBlower)
    {
     for(int i=0;i<4;i++){var blade=VisualBox("Visible fan blade",model,V(0,.1f,-.475f),V(.45f,.045f,.025f),green);blade.localRotation=Quaternion.Euler(0,0,i*45);}
     Icon(AstraGlyph.Wind,model,V(0,.11f,-.51f),.32f);
    }
    else if(tool is AstraGrappleTool)Icon(AstraGlyph.Hook,model,V(0,.02f,-.33f),.3f);
   }
   var tank=Object.FindAnyObjectByType<AstraFuelTank>();var port=tank.transform.Find("Yellow fuel port and gauge");
   if(port!=null)
   {
    var plate=PicturePanel("Fuel socket matches can",port,port.position,port.rotation,.3f,AstraGlyph.Can,AstraGlyph.Drop);
    Feedback("Vehicle fuel ready gauge",port,port.position+port.up*.4f,port.rotation,.7f,tank);
   }
  }
  static void Feedback(string name,Transform parent,Vector3 p,Quaternion rotation,float width,Component source)
  {
   var t=Group(name,p,parent);t.rotation=rotation;
   var lamp=VisualShape("Ready status lens",PrimitiveType.Sphere,t,V(-width*.65f,0,0),Vector3.one*width*.18f,orange).GetComponent<Renderer>();
   VisualBox("Gauge frame",t,Vector3.zero,V(width+.04f,.11f,.04f),dark);
   var scale=Group("Gauge dimensions",p,t);scale.localRotation=Quaternion.identity;scale.localScale=V(width,.075f,.02f);
   var fill=VisualBox("Gauge fill",scale,V(0,0,-1.5f),Vector3.one,green);
   // Keep depth outside the frame while the feedback only changes X scale/position.
   scale.localPosition=V(0,0,-.04f);fill.localPosition=Vector3.zero;
   t.gameObject.AddComponent<AstraAffordanceFeedback>().Configure(source,lamp,fill);
  }
  static void MechanismPictures()
  {
   var q=Quaternion.identity;
   PicturePanel("Practice transfer sequence",root,V(-22,2.65f,-30.5f),q,.48f,AstraGlyph.Hand,AstraGlyph.Box,AstraGlyph.Bridge,AstraGlyph.Arrow,AstraGlyph.Roller);
   var lift=Object.FindAnyObjectByType<AstraV2FreightLift>();
   var release=Find("Release after all pins removed");PicturePanel("Unpin before freight release",lift.transform,release.position+V(0,.7f,-.25f),q,.42f,AstraGlyph.Pin,AstraGlyph.Arrow,AstraGlyph.Hand);
   var brake=Find("HOLD - lower / RELEASE - brake");PicturePanel("Held brake sequence",brake.parent,brake.position+V(0,.65f,-.3f),q,.4f,AstraGlyph.Hold,AstraGlyph.Down,AstraGlyph.Release,AstraGlyph.Brake);
   var reset=Find("Return empty platform");PicturePanel("Empty platform return",lift.transform,reset.position+V(0,.65f,-.2f),q,.4f,AstraGlyph.Box,AstraGlyph.Cross,AstraGlyph.Return,AstraGlyph.Up);
   Feedback("Freight released status",lift.transform,release.position+V(0,-.3f,-.3f),q,.8f,lift);
   var puzzle=Object.FindAnyObjectByType<AstraV2Puzzle>();
   PicturePanel("Circuit then person on plate then transport",puzzle.transform,V(40.3f,3.05f,27.4f),q,.43f,AstraGlyph.Hand,AstraGlyph.Dial,AstraGlyph.Arrow,AstraGlyph.Feet,AstraGlyph.Arrow,AstraGlyph.Door);
   PicturePanel("Plate needs a person not a crate",puzzle.transform,V(44,2.05f,30.75f),q,.35f,AstraGlyph.Person,AstraGlyph.Feet,AstraGlyph.Box,AstraGlyph.Cross);
   Feedback("Airlock solved and open state",puzzle.transform,V(46.7f,4.55f,34.72f),q,1.3f,puzzle);
   PicturePanel("Open gate edge stop",puzzle.transform,V(43.3f,2.05f,37.5f),q,.35f,AstraGlyph.Door,AstraGlyph.Box,AstraGlyph.Brake);
   PicturePanel("Roll and turn inside service room",root,V(48,2.7f,-40.8f),q,.42f,AstraGlyph.Person,AstraGlyph.Roller,AstraGlyph.Turn,AstraGlyph.Person);
   PicturePanel("Survey stop scan mark sequence",root,V(39,2.3f,24.7f),q,.43f,AstraGlyph.Hold,AstraGlyph.Feet,AstraGlyph.Mine,AstraGlyph.Arrow,AstraGlyph.Flag);
   var cannon=Object.FindAnyObjectByType<AstraPlayerCannon>();var mouth=Find("Cannon mouth interact");var igniter=Find("Cannon rear igniter");
   PicturePanel("Enter cannon",cannon.transform,mouth.position+V(0,.8f,0),cannon.transform.rotation,.42f,AstraGlyph.Hand,AstraGlyph.Person,AstraGlyph.Arrow,AstraGlyph.Cannon);
   PicturePanel("Second crew member lights fuse",cannon.transform,igniter.position+V(0,.9f,0),cannon.transform.rotation,.4f,AstraGlyph.Person,AstraGlyph.Hand,AstraGlyph.Clock,AstraGlyph.Arrow,AstraGlyph.Person);
   Feedback("Cannon passenger and fuse gauge",cannon.transform,igniter.position+V(0,.5f,-.25f),cannon.transform.rotation,1,cannon);
   var blast=Object.FindAnyObjectByType<AstraBlastGate>();var rubble=Find("Destructible entrance boulders");
   float rockFront=rubble.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.z)-.08f;
   PicturePanel("Explosive placement matches charge",rubble,V(68,3.5f,rockFront),q,.6f,AstraGlyph.Burst,AstraGlyph.Down);
   var slot=PicturePanel("Charge placement floor outline",rubble,V(68,1.8f,35.8f),Quaternion.Euler(90,0,0),.6f,AstraGlyph.Burst);
   var charge=Find("Carryable wired TNT");PicturePanel("Explosive bundle symbol",charge,charge.position+V(0,.28f,0),Quaternion.Euler(90,0,0),.3f,AstraGlyph.Burst);
   PicturePanel("Place retreat activate blast",blast.transform,V(73,3.3f,25.9f),q,.45f,AstraGlyph.Burst,AstraGlyph.Down,AstraGlyph.Person,AstraGlyph.Return,AstraGlyph.Hand);
   Feedback("Charge in range indicator",blast.transform,V(73,2.75f,25.6f),q,1,blast);
   // Permanent matching shape + color: color is never the only site identifier.
   var glyphs=new[]{AstraGlyph.Box,AstraGlyph.Triangle,AstraGlyph.Diamond,AstraGlyph.Square};
   var parts=Object.FindObjectsByType<SpaceshipPart>().OrderBy(p=>p.name).ToArray();
   for(int i=0;i<parts.Length;i++)
   {
    parts[i].TryGetHullBounds(out var b);PicturePanel("Matching module silhouette",parts[i].transform,b.center+V(0,0,-b.extents.z-.035f),q,.4f,glyphs[i]);
    var end=CargoRoutes[i][CargoRoutes[i].Length-2];PicturePanel("Matching return road symbol",root,V(end.x,1.055f,end.y),Quaternion.Euler(90,0,0),.65f,glyphs[i],AstraGlyph.Arrow,AstraGlyph.Ship);
   }
   foreach(var rack in Object.FindObjectsByType<Transform>().Where(t=>t.name.StartsWith("Utility rack")).ToArray()){
    PicturePanel("Hook pulls while held",rack,rack.position+V(-.8f,1.85f,-.5f),q,.35f,AstraGlyph.Hold,AstraGlyph.Hook,AstraGlyph.Return);
    PicturePanel("Wind pushes while held",rack,rack.position+V(.8f,1.85f,-.5f),q,.35f,AstraGlyph.Hold,AstraGlyph.Wind,AstraGlyph.Arrow);
   }
   foreach(var station in Object.FindObjectsByType<AstraRocketTool>())
   {
    var p=station.transform.position;
    PicturePanel("Rocket carries holder",root,p+V(0,.9f,-.65f),q,.26f,AstraGlyph.Click,AstraGlyph.Rocket,AstraGlyph.Person);
    PicturePanel("Ink makes temporary marks",root,p+V(1,.9f,-.65f),q,.26f,AstraGlyph.Click,AstraGlyph.Drop,AstraGlyph.Clock);
    PicturePanel("Fuel fills vehicle",root,p+V(2.5f,.9f,-.65f),q,.29f,AstraGlyph.Click,AstraGlyph.Can,AstraGlyph.Arrow,AstraGlyph.Ship);
   }
  }
  [MenuItem("Tools/Metagame/Astra/Prepare wordless recovery guidance")]
  public static void AddWordlessGuidance()
  {
   var scene=EditorSceneManager.OpenScene(ScenePath);if(!RecoveryFocused)throw new Exception("Cleaned expedition scene required");
   if(GameObject.Find(WordlessRoot)!=null)throw new Exception("Wordless guidance already authored");
   Materials();BuildGlyphAssets();root=new GameObject(WordlessRoot).transform;
   root.gameObject.AddComponent<AstraPictogramGuide>().Configure(glyphAtlas);WordlessSigns();ToolSilhouettes();MechanismPictures();
   // Legacy bat and welder previously obscured the new rocket when approaching the shelf.
   Place(Find("MeteorBat_Stage1"),-28.2f,-39);Place(Find("WeldingPlasmaGun_Stage1"),-29.5f,-39.5f);
   PicturePanel("Existing meteor bat identity",root,V(-28.2f,2.5f,-38.7f),Quaternion.identity,.32f,AstraGlyph.Hand,AstraGlyph.Bat);
   PicturePanel("Existing welding tool identity",root,V(-29.5f,2.5f,-39.2f),Quaternion.identity,.32f,AstraGlyph.Hand,AstraGlyph.Weld);
   Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   ValidateWordlessGuidance();
  }
  public static void ValidateWordlessGuidance()
  {
   Directory.CreateDirectory("Validation/AstraAffordance");EditorSceneManager.OpenScene(ScenePath);
   var text=Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include);
   if(text.Any(t=>t.GetComponent<Renderer>()!=null&&t.GetComponent<Renderer>().enabled))throw new Exception("Authored world text still visible");
   var guide=Object.FindAnyObjectByType<AstraPictogramGuide>();if(guide==null||guide.Atlas==null)throw new Exception("Missing pictogram atlas");
   foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t=>t.name.StartsWith("Pictogram ")))
    if(t.GetComponent<Collider>()!=null)throw new Exception("Visual icon unexpectedly has a collider");
   ValidateDifficultyLayout();ExportModelCatalog();
   File.WriteAllText("Validation/AstraAffordance/validation.txt","PASS "+text.Length+" authored world TextMeshes hidden; references preserved\nPASS scene-specific pictogram HUD and shared atlas\nPASS picture boards have no gameplay colliders\nPASS original part routes, learning tools, mine routes and mechanism inventory\n");
   var camera=Object.FindObjectsByType<Camera>().First();
   Capture(camera,V(36,6,21),V(41,2,28),"wordless-mine-and-dials");
   Capture(camera,V(-21,6,-16),V(-26,2,-8),"wordless-cannon");
   Capture(camera,V(77,7,20),V(69,2.4f,33),"wordless-demolition");
   Capture(camera,V(-24,4,-43),V(-23,2,-39),"wordless-tools");
  }
 }
}
