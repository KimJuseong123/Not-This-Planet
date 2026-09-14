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
  static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b){var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude)));}
  static float RouteDistance(Vector2 p,Vector2[][] paths){float d=999;foreach(var path in paths)for(int i=1;i<path.Length;i++)d=Mathf.Min(d,SegmentDistance(p,path[i-1],path[i]));return d;}
  static float RectDistance(Vector2 p,Rect r)=>new Vector2(Mathf.Max(r.xMin-p.x,0,p.x-r.xMax),Mathf.Max(r.yMin-p.y,0,p.y-r.yMax)).magnitude;
  static float Clear(Vector2 p){float d=Mathf.Max(0,p.magnitude-10);foreach(var r in pads)d=Mathf.Min(d,RectDistance(p,r));d=Mathf.Min(d,Mathf.Max(0,RouteDistance(p,CargoRoutes)-5));return Mathf.Min(d,Mathf.Max(0,RouteDistance(p,Walks)-2.6f));}
  public static float Height(float x,float z)
  {
   var p=P(x,z);float blend=Mathf.SmoothStep(0,1,Clear(p)/6);
   float noise=Mathf.PerlinNoise((x+110)*.028f,(z+91)*.031f);float h=.955f+blend*(1+noise*noise*10);
   float d=RouteDistance(p,Rifts);float cut=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.4f,4.4f,d));return Mathf.Lerp(h,-8.5f,cut);
  }
  static void BuildLand()
  {
   terrain=Object.FindAnyObjectByType<Terrain>();var data=Object.Instantiate(terrain.terrainData);data.name="Expedition rolling terrain 180x150";
   AssetDatabase.CreateAsset(data,Art+"/Expedition Terrain.asset");data.heightmapResolution=1025;data.size=V(180,30,150);
   var heights=new float[1025,1025];for(int z=0;z<1025;z++)for(int x=0;x<1025;x++)heights[z,x]=(Height(x/1024f*180-90,z/1024f*150-75)+12)/30;
   data.SetHeights(0,0,heights);data.alphamapResolution=512;var alpha=new float[512,512,2];
   for(int z=0;z<512;z++)for(int x=0;x<512;x++){float stone=Mathf.InverseLerp(15,45,data.GetSteepness(x/511f,z/511f));alpha[z,x,0]=1-stone;alpha[z,x,1]=stone;}data.SetAlphamaps(0,0,alpha);
   terrain.terrainData=data;terrain.GetComponent<TerrainCollider>().terrainData=data;terrain.transform.position=V(-90,-12,-75);terrain.heightmapPixelError=4;
   var basin=Find("01 - continuous mineral basin");foreach(Transform t in basin.Cast<Transform>().ToArray())
   {
    if(t.name.StartsWith("Boundary")){var p=t.position;if(t.name.EndsWith("west")||t.name.EndsWith("east")){p.x=Mathf.Sign(p.x)*90;t.localScale=V(1,24,152);}else{p.z=Mathf.Sign(p.z)*75;t.localScale=V(182,24,1);}t.position=p;continue;}
    var old=t.position;var now=V(old.x*1.77f,old.y,old.z*1.63f);now.y+=Height(now.x,now.z)-1;Move(t,now-old);
   }
   var riftGroup=Find("Three marked fatal trenches");var oldRifts=Stage1AstraExpansionBuilder.Trenches;
   Vector3[] riftShifts={V(-39,0,25),V(3,0,23),V(28,0,-4)};
   foreach(Transform t in riftGroup.Cast<Transform>().ToArray())
   {int id=0;if(t.name.StartsWith("Fatal rift"))id=int.Parse(t.name.Split(' ')[2]);else{float best=999;for(int r=0;r<oldRifts.Length;r++)for(int i=1;i<oldRifts[r].Length;i++){float d=SegmentDistance(P(t.position.x,t.position.z),oldRifts[r][i-1],oldRifts[r][i]);if(d<best){best=d;id=r;}}}Move(t,riftShifts[id]);if(!t.name.StartsWith("Fatal rift"))t.position=V(t.position.x,Height(t.position.x,t.position.z)+.06f,t.position.z);}
   var ecology=Find("05 - purple ecology framing open sightlines");int index=0;foreach(Transform t in ecology.Cast<Transform>().ToArray())
   {
    var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
    var p=P(b.center.x*1.6f,b.center.z*1.45f);
    float radius=Mathf.Max(b.extents.x,b.extents.z)+2;
    for(int attempt=0;attempt<200&&(Clear(p)<radius||RouteDistance(p,Rifts)<radius+3);attempt++)
    {float a=index++*2.39996f;float band=62+(attempt%4)*5;p=P(Mathf.Cos(a)*band,Mathf.Sin(a)*(band*.8f));}
    if(Clear(p)<radius||RouteDistance(p,Rifts)<radius+3){Object.DestroyImmediate(t.gameObject);continue;}
    Move(t,V(p.x-b.center.x,Height(p.x,p.y)-b.min.y,p.y-b.center.z));
   }
   for(int i=0;i<CargoRoutes.Length;i++)Path("Module "+(i+1)+" service road",CargoRoutes[i],6.8f,colors[i],true);
   for(int i=0;i<Walks.Length;i++)Path("Crew walking loop "+i,Walks[i],3.2f,white,false);
  }
  static GameObject Shape(string name,PrimitiveType type,Transform parent,Vector3 p,Vector3 scale,Material material,bool solid=true)
  {
   var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,true);g.transform.position=p;g.transform.localScale=scale;g.layer=GameLayers.Ground;g.GetComponent<Renderer>().sharedMaterial=material;
   if(!solid)Object.DestroyImmediate(g.GetComponent<Collider>());else if(type==PrimitiveType.Cylinder){Object.DestroyImmediate(g.GetComponent<Collider>());var c=g.AddComponent<MeshCollider>();c.sharedMesh=g.GetComponent<MeshFilter>().sharedMesh;c.convex=true;}return g;
  }
  static GameObject Box(string name,Transform parent,Vector3 p,Vector3 scale,Material m,bool solid=true)=>Shape(name,PrimitiveType.Cube,parent,p,scale,m,solid);
  static void Beam(string name,Transform parent,Vector3 a,Vector3 b,float size,Material material,bool solid=false)
  {var g=Box(name,parent,(a+b)*.5f,V(size,size,Vector3.Distance(a,b)),material,solid);g.transform.rotation=Quaternion.LookRotation(b-a);}
  static void Circle(string name,Transform parent,Vector3 center,float radius,Material m,float width=.05f)
  {var t=Group(name,center,parent);var line=t.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=m;line.useWorldSpace=true;line.loop=true;line.positionCount=64;line.startWidth=line.endWidth=width;for(int i=0;i<64;i++){float a=i*Mathf.PI/32;line.SetPosition(i,center+V(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}}
  static void Path(string name,Vector2[] points,float width,Material accent,bool freight)
  {
   var t=Group(name,Vector3.zero);
   for(int i=1;i<points.Length;i++)
   {var a=points[i-1];var b=points[i];var d=b-a;var middle=(a+b)*.5f;var g=Box("Continuous route surface",t,V(middle.x,.993f,middle.y),V(width,.02f,d.magnitude+.4f),road,false);g.transform.rotation=Quaternion.LookRotation(V(d.x,0,d.y));
    var side=new Vector2(-d.y,d.x).normalized;int count=Mathf.CeilToInt(d.magnitude/3);
    for(int j=0;j<=count;j++)foreach(int s in new[]{-1,1}){var p=Vector2.Lerp(a,b,j/(float)count)+side*(width*.45f*s);var mark=Box(freight?"Module color return marker":"Promenade edge",t,V(p.x,1.025f,p.y),V(.12f,.035f,.6f),accent,false);mark.transform.rotation=g.transform.rotation;}}
  }
 }
}
