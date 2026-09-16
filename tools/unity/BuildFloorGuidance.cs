using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class BuildFloorGuidance
{
 [Serializable] public class Entry { public string name; public Vector3 size; public Vector3 min; public int triangles; public int materials; public bool uv2; public string prefab; public string[] texturePaths; }
 [Serializable] public class Report { public string unityVersion; public string status; public string scope; public Entry[] assets; public string[] errors; }
 static string[] slugs={"01-floor-light","02-direction-tile","03-stair-edge","04-terrain-light"};
 static string[] labels={"FloorLight","DirectionTile","StairEdgeLight","TerrainLight"};
 static List<string> errors=new List<string>();
 static string Base="Assets/FloorGuidance";
 static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
 public static void Run()
 {
  Application.logMessageReceived += (message,trace,type) => { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message); };
  try
  {
   AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
   var shader=Shader.Find("Universal Render Pipeline/Lit"); Check(shader!=null,"URP Lit shader unavailable");
   var entries=new List<Entry>();
   for(int i=0;i<4;i++) entries.Add(Build(i,shader));
   AssetDatabase.SaveAssets();
   Check(errors.Count==0,string.Join("\n",errors));
   var report=new Report {unityVersion=Application.unityVersion,status="PASS",scope="Isolated Unity import, URP material remapping, mesh/UV/scale, prefab and collider validation; no production scene changes.",assets=entries.ToArray(),errors=errors.ToArray()};
   File.WriteAllText("validation.json",JsonUtility.ToJson(report,true));
   Debug.Log("FLOOR_GUIDANCE_VALIDATION_PASS");
  }
  catch(Exception e) { File.WriteAllText("validation-failure.txt",e.ToString()+"\n"+string.Join("\n",errors)); Debug.LogException(e); EditorApplication.Exit(1); }
 }
 static Entry Build(int i, Shader shader)
 {
  string folder=Base+"/"+slugs[i], label=labels[i], meshName="SM_Astra_"+label;
  string fbx=folder+"/"+meshName+".fbx",tex=folder+"/"+meshName+".fbm/";
  foreach(var file in Directory.GetFiles(tex,"*.png"))
  {
   var ti=(TextureImporter)AssetImporter.GetAtPath(file.Replace('\\','/'));
   ti.textureType=file.EndsWith("_Normal.png")?TextureImporterType.NormalMap:TextureImporterType.Default;
   ti.sRGBTexture=file.EndsWith("_BaseColor.png");
   ti.alphaSource=TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency=false;
   ti.mipmapEnabled=true; ti.wrapMode=TextureWrapMode.Clamp; ti.maxTextureSize=512;
   ti.textureCompression=TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
  }
  var body=MaterialFor(folder,label,false,shader,tex,meshName);
  var lens=MaterialFor(folder,label,true,shader,tex,meshName);
  string[] variantNames={"Amber","Lime","Orchid"};
  Color[] colors={new Color(1,.34f,.025f),new Color(.36f,1,.055f),new Color(.72f,.16f,1)};
  for(int j=0;j<3;j++)
  {
   string path=folder+"/M_Astra_"+label+"_Lens_"+variantNames[j]+".mat";
   Material mat=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(mat==null) { mat=new Material(lens); AssetDatabase.CreateAsset(mat,path); } else mat.CopyPropertiesFromMaterial(lens);
   mat.name="M_Astra_"+label+"_Lens_"+variantNames[j];
   mat.SetColor("_BaseColor",colors[j]*.45f+new Color(.08f,.08f,.08f,.55f));
   mat.SetColor("_EmissionColor",colors[j]*1.6f); EditorUtility.SetDirty(mat);
  }
  var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
  importer.globalScale=1; importer.useFileScale=true; importer.bakeAxisConversion=true;
  importer.importAnimation=false; importer.importCameras=false; importer.importLights=false;
  importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
  importer.generateSecondaryUV=false; importer.isReadable=true;
  importer.importNormals=ModelImporterNormals.Import; importer.importTangents=ModelImporterTangents.CalculateMikk;
  importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),body.name),body);
  importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),lens.name),lens);
  importer.SaveAndReimport();
  var model=AssetDatabase.LoadAssetAtPath<GameObject>(fbx); Check(model!=null,"Missing model "+label);
  var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
  var filters=instance.GetComponentsInChildren<MeshFilter>();
  Check(filters.Length==1,"Expected one mesh "+label);
  var mesh=filters[0].sharedMesh;
  Check(mesh.subMeshCount==2 && mesh.uv.Length==mesh.vertexCount && mesh.uv2.Length==mesh.vertexCount,"Mesh channels "+label);
  var renderer=filters[0].GetComponent<MeshRenderer>();
  Check(renderer.sharedMaterials.Length==2 && renderer.sharedMaterials[0]==body && renderer.sharedMaterials[1]==lens,"Material remapping "+label);
  Check(body.shader==shader && lens.shader==shader,"Shader "+label);
  Check(lens.IsKeywordEnabled("_EMISSION"),"Emission missing");
  Check(lens.GetTexture("_EmissionMap")!=null,"Emission map missing");
  foreach(var m in new[]{body,lens})
   foreach(var prop in new[]{"_BaseMap","_MetallicGlossMap","_BumpMap"})
    Check(m.GetTexture(prop)!=null,"Missing "+prop+" "+label);
  Bounds bounds=renderer.bounds;
  float[] expectedW={.352f,.220f,.400f,.380f};
  float[] expectedH={.0351f,.0251f,.0601f,.1401f};
  float[] expectedD={.12265f,.220f,.0809f,.140f};
  Vector3 expected=new Vector3(expectedW[i],expectedH[i],expectedD[i]);
  Check((bounds.size-expected).sqrMagnitude<.000001f,"Scale/orientation "+label+" "+bounds.size+" expected "+expected);
  Check(Mathf.Abs(bounds.min.y)<.00001,"Base pivot "+label);
  var root=new GameObject("PF_Astra_"+label); instance.transform.SetParent(root.transform,false);
  // Place terrain prefab origin at ground contact; exported FBX still uses bottom-of-stakes pivot.
  if(i==3) instance.transform.localPosition=new Vector3(0,-.089f,0);
  if(i==2)
  {
   var top=root.AddComponent<BoxCollider>(); top.center=new Vector3(0,.0555f,0); top.size=new Vector3(.4f,.011f,.08f);
   var face=root.AddComponent<BoxCollider>(); face.center=new Vector3(0,.027f,-.035f); face.size=new Vector3(.4f,.054f,.012f);
  }
  else
  {
   var col=root.AddComponent<BoxCollider>();
   col.center=i==3?new Vector3(0,.0255f,0):bounds.center;
   col.size=i==3?new Vector3(.38f,.051f,.14f):bounds.size;
  }
  string prefab=folder+"/PF_Astra_"+label+".prefab";
  PrefabUtility.SaveAsPrefabAsset(root,prefab);
  UnityEngine.Object.DestroyImmediate(root);
  var saved=AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
  Check(saved!=null && saved.GetComponentsInChildren<MeshRenderer>().Length==1,"Prefab invalid "+label);
  var colliders=saved.GetComponentsInChildren<BoxCollider>();
  Check(colliders.Length==(i==2?2:1),"Collider count "+label);
  Check(colliders.All(c=>c.size.x>0 && c.size.y>0 && c.size.z>0 && !c.isTrigger),"Collider geometry "+label);
  Check(saved.GetComponentsInChildren<Transform>().All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"Missing scripts "+label);
  if(i==3) Check(Mathf.Abs(saved.transform.GetChild(0).localPosition.y+.089f)<.000001f,"Terrain ground pivot");
  Check(lens.GetTexture("_BaseMap")==body.GetTexture("_BaseMap"),"Shared neutral atlas");
  foreach(var suffix in variantNames)
  {
   var variant=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_Astra_"+label+"_Lens_"+suffix+".mat");
   Check(variant!=null && variant.shader==shader && variant.IsKeywordEnabled("_EMISSION"),"Color variant "+suffix);
   Check(variant.GetTexture("_EmissionMap")==lens.GetTexture("_EmissionMap"),"Shared emission mask "+suffix);
   Check(variant.GetColor("_EmissionColor")!=lens.GetColor("_EmissionColor"),"Distinct color "+suffix);
  }
  var entry=new Entry{name=label,size=bounds.size,min=bounds.min,triangles=mesh.triangles.Length/3,materials=2,uv2=true,prefab=prefab,texturePaths=Directory.GetFiles(tex,"*.png")};
  importer.isReadable=false; importer.SaveAndReimport();
  return entry;
 }
 static Material MaterialFor(string folder,string label,bool lens,Shader shader,string tex,string mesh)
 {
  string name="M_Astra_"+label+(lens?"_Lens":"_Body"),path=folder+"/"+name+".mat";
  var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(m==null) {m=new Material(shader);m.name=name;AssetDatabase.CreateAsset(m,path);}
  m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+mesh+"_BaseColor.png"));
  m.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+mesh+"_MS.png"));
  m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+mesh+"_Normal.png"));
  m.SetFloat("_BumpScale",1);m.SetFloat("_Smoothness",1);m.SetFloat("_Surface",0);
  m.EnableKeyword("_METALLICSPECGLOSSMAP");m.EnableKeyword("_NORMALMAP");m.enableInstancing=true;
  m.SetColor("_BaseColor",lens?new Color(.08f,.55f,.62f,1):Color.white);
  if(lens) {m.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tex+mesh+"_Emission.png"));m.SetColor("_EmissionColor",new Color(.018f,.72f,.85f)*1.6f);m.EnableKeyword("_EMISSION");}
  else {m.DisableKeyword("_EMISSION");m.SetColor("_EmissionColor",Color.black);}
  EditorUtility.SetDirty(m);return m;
 }
}

