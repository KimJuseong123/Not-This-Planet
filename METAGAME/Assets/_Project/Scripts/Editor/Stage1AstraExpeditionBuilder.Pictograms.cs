using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
 public static partial class Stage1AstraExpeditionBuilder
 {
  const string GuideArt="Assets/_Project/Art/AstraAffordance";
  static Texture2D glyphAtlas;static Material glyphMaterial;static Color32[] pixels;static int ox,oy;
  static void Dot(float x,float y,float r=2.1f)
  {int cx=ox+14+Mathf.RoundToInt(x),cy=oy+14+Mathf.RoundToInt(y);for(int dy=-(int)r-1;dy<=r+1;dy++)for(int dx=-(int)r-1;dx<=r+1;dx++)if(dx*dx+dy*dy<=r*r&&cx+dx>=0&&cx+dx<1024&&cy+dy>=0&&cy+dy<1024)pixels[(cy+dy)*1024+cx+dx]=new Color32(255,255,255,255);}
  static void Stroke(float x,float y,float a,float b,float thickness=2.1f)
  {int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(new Vector2(x,y),new Vector2(a,b))*2));for(int i=0;i<=n;i++)Dot(Mathf.Lerp(x,a,i/(float)n),Mathf.Lerp(y,b,i/(float)n),thickness);}
  static void Ring(float x,float y,float r){for(int i=0;i<80;i++){float a=i*Mathf.PI/40,b=(i+1)*Mathf.PI/40;Stroke(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r,x+Mathf.Cos(b)*r,y+Mathf.Sin(b)*r);}}
  static void Outline(float x,float y,float w,float h){Stroke(x,y,x+w,y);Stroke(x+w,y,x+w,y+h);Stroke(x+w,y+h,x,y+h);Stroke(x,y+h,x,y);}
  static void Arrow(float x,float y,float a,float b){Stroke(x,y,a,b);var d=new Vector2(x-a,y-b).normalized*12;var s=new Vector2(-d.y,d.x)*.65f;Stroke(a,b,a+d.x+s.x,b+d.y+s.y);Stroke(a,b,a+d.x-s.x,b+d.y-s.y);}
  static void Human(float x=50,float y=48,float scale=1){Ring(x,y+30*scale,9*scale);Stroke(x,y+20*scale,x,y-10*scale);Stroke(x-18*scale,y+7*scale,x+18*scale,y+7*scale);Stroke(x,y-10*scale,x-15*scale,y-32*scale);Stroke(x,y-10*scale,x+15*scale,y-32*scale);}
  static void DrawGlyph(AstraGlyph glyph)
  {
   switch(glyph)
   {
    case AstraGlyph.Hand:
     Outline(34,25,32,30);for(int i=0;i<4;i++){Stroke(35+i*9,53,35+i*9,78+(i==1?8:0));}Stroke(34,27,18,48);Stroke(18,48,18,62);Stroke(18,62,34,53);Stroke(36,25,40,14);Stroke(64,25,60,14);break;
    case AstraGlyph.Click:case AstraGlyph.Hold:case AstraGlyph.Release:
     for(int i=0;i<80;i++){float a=i*Mathf.PI/40,b=(i+1)*Mathf.PI/40;Stroke(50+Mathf.Cos(a)*26,52+Mathf.Sin(a)*35,50+Mathf.Cos(b)*26,52+Mathf.Sin(b)*35);}Stroke(50,86,50,55);Stroke(25,55,75,55);if(glyph!=AstraGlyph.Release)for(int x=34;x<46;x+=3)Stroke(x,63,x,76,1.7f);
     if(glyph==AstraGlyph.Hold){Stroke(25,7,75,7);Dot(25,7,4);Dot(75,7,4);}else if(glyph==AstraGlyph.Click){Stroke(16,89,9,96);Stroke(32,94,32,100);}break;
    case AstraGlyph.Person:Human();break;
    case AstraGlyph.Box:Outline(20,20,60,60);Stroke(42,20,42,80);Stroke(58,20,58,80);break;
    case AstraGlyph.Roller:Outline(16,39,68,35);Ring(25,22,9);Ring(50,22,9);Ring(75,22,9);Arrow(22,91,80,91);break;
    case AstraGlyph.Arrow:Arrow(15,50,85,50);break;
    case AstraGlyph.Feet:for(int s=-1;s<=1;s+=2){float x=50+s*18;for(int i=0;i<60;i++){float a=i*Mathf.PI/30;Dot(x+Mathf.Cos(a)*8+s*Mathf.Sin(a)*4,42+Mathf.Sin(a)*19,2.4f);}for(int i=0;i<4;i++)Dot(x-8+i*5,69-Mathf.Abs(i-1)*2,3.2f-i*.35f);}Stroke(14,12,86,12);break;
    case AstraGlyph.Door:Outline(17,12,66,76);Stroke(50,12,50,88);Arrow(55,52,76,52);break;
    case AstraGlyph.Dial:Ring(50,50,30);Stroke(50,50,69,71);Arrow(15,89,45,89);break;
    case AstraGlyph.Rocket:Outline(37,29,26,40);Stroke(37,69,50,89);Stroke(50,89,63,69);Stroke(37,42,23,23);Stroke(23,23,37,29);Stroke(63,42,77,23);Stroke(77,23,63,29);Stroke(40,21,50,4);Stroke(50,4,60,21);break;
    case AstraGlyph.Hook:Ring(30,50,20);Stroke(30,50,64,50);Stroke(64,50,78,77);Stroke(78,77,83,45);Stroke(83,45,65,30);Arrow(60,15,18,15);break;
    case AstraGlyph.Wind:for(int i=0;i<3;i++){Arrow(15,24+i*25,80-i*7,24+i*25);}break;
    case AstraGlyph.Drop:for(int i=0;i<40;i++){float a=Mathf.PI+i*Mathf.PI/40,b=Mathf.PI+(i+1)*Mathf.PI/40;Stroke(50+Mathf.Cos(a)*25,40+Mathf.Sin(a)*25,50+Mathf.Cos(b)*25,40+Mathf.Sin(b)*25);}Stroke(25,40,50,87);Stroke(50,87,75,40);break;
    case AstraGlyph.Can:Outline(24,13,50,61);Outline(33,77,26,11);Stroke(74,61,87,76);Stroke(87,76,94,69);Stroke(39,35,49,54);Stroke(49,54,59,35);Stroke(59,35,55,27);Stroke(55,27,43,27);Stroke(43,27,39,35);break;
    case AstraGlyph.Mine:Ring(50,38,18);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Stroke(50+Mathf.Cos(a)*18,38+Mathf.Sin(a)*18,50+Mathf.Cos(a)*28,38+Mathf.Sin(a)*28);}Stroke(15,85,85,85);Stroke(25,72,75,72);Dot(50,38,4);break;
    case AstraGlyph.Flag:Stroke(25,12,25,90);Outline(25,56,50,29);Stroke(12,12,43,12);break;
    case AstraGlyph.Burst:for(int i=0;i<12;i++){float a=i*Mathf.PI/6,b=(i+.5f)*Mathf.PI/6;Stroke(50+Mathf.Cos(a)*17,50+Mathf.Sin(a)*17,50+Mathf.Cos(b)*43,50+Mathf.Sin(b)*43);}break;
    case AstraGlyph.Cannon:Ring(32,24,15);Ring(75,24,12);Stroke(20,43,76,73,8);Stroke(72,81,84,60);Arrow(78,87,95,96);break;
    case AstraGlyph.Clock:Ring(50,50,36);Stroke(50,50,50,77);Stroke(50,50,72,37);break;
    case AstraGlyph.Brake:Stroke(36,20,36,80,6);Stroke(64,20,64,80,6);break;
    case AstraGlyph.Down:Arrow(50,87,50,15);break;
    case AstraGlyph.Up:Arrow(50,15,50,87);break;
    case AstraGlyph.Return:Stroke(20,25,80,25);Stroke(80,25,80,75);Arrow(80,75,20,75);break;
    case AstraGlyph.Pin:Outline(35,22,30,20);Stroke(50,22,50,5);Ring(50,63,17);Stroke(50,46,50,42);Arrow(79,48,79,88);break;
    case AstraGlyph.Ship:Stroke(50,89,24,24);Stroke(24,24,50,40);Stroke(50,40,76,24);Stroke(76,24,50,89);Stroke(36,18,32,7);Stroke(64,18,68,7);break;
    case AstraGlyph.Triangle:Stroke(50,86,14,16);Stroke(14,16,86,16);Stroke(86,16,50,86);break;
    case AstraGlyph.Diamond:Stroke(50,90,10,50);Stroke(10,50,50,10);Stroke(50,10,90,50);Stroke(90,50,50,90);break;
    case AstraGlyph.Square:Outline(16,16,68,68);break;
    case AstraGlyph.Warning:DrawGlyph(AstraGlyph.Triangle);Stroke(50,63,50,39,4);Dot(50,27,4);break;
    case AstraGlyph.Check:Stroke(18,50,40,27,4);Stroke(40,27,83,80,4);break;
    case AstraGlyph.Bed:Outline(14,26,72,23);Ring(28,62,10);Stroke(12,15,12,79);Stroke(88,15,88,57);break;
    case AstraGlyph.Food:Ring(55,50,30);Stroke(10,15,10,82);Stroke(4,64,17,64);Stroke(17,64,17,84);Stroke(90,15,90,84);break;
    case AstraGlyph.Lever:Stroke(12,35,88,70,4);Stroke(48,40,36,18);Stroke(36,18,64,18);Stroke(64,18,48,40);Arrow(16,84,16,50);break;
    case AstraGlyph.Bridge:Outline(6,10,24,26);Outline(70,10,24,26);Stroke(18,45,82,45,5);Arrow(23,80,80,80);break;
    case AstraGlyph.Weld:Outline(14,44,37,21);Stroke(28,44,21,19,5);Stroke(51,55,68,55,4);for(int i=-1;i<=1;i++)Stroke(75,55,90,55+i*20);break;
    case AstraGlyph.Bat:Stroke(25,15,72,77,9);Stroke(25,15,18,7,4);break;
    case AstraGlyph.Turn:for(int i=0;i<26;i++){float a=i*.17f;Dot(50+Mathf.Cos(a)*30,50+Mathf.Sin(a)*30);}Arrow(39,19,18,28);break;
    case AstraGlyph.Cross:Stroke(22,22,78,78,4);Stroke(22,78,78,22,4);break;
   }
  }
  static void BuildGlyphAssets()
  {
   if(!AssetDatabase.IsValidFolder(GuideArt))AssetDatabase.CreateFolder("Assets/_Project/Art","AstraAffordance");
   pixels=new Color32[1024*1024];foreach(AstraGlyph glyph in Enum.GetValues(typeof(AstraGlyph))){int i=(int)glyph;ox=(i%8)*128;oy=(i/8)*128;DrawGlyph(glyph);}
   var tex=new Texture2D(1024,1024,TextureFormat.RGBA32,false);tex.SetPixels32(pixels);tex.Apply();File.WriteAllBytes(GuideArt+"/Pictograms.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
   AssetDatabase.ImportAsset(GuideArt+"/Pictograms.png");var importer=(TextureImporter)AssetImporter.GetAtPath(GuideArt+"/Pictograms.png");importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
   glyphAtlas=AssetDatabase.LoadAssetAtPath<Texture2D>(GuideArt+"/Pictograms.png");glyphMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Wordless pictogram atlas"};glyphMaterial.SetTexture("_BaseMap",glyphAtlas);glyphMaterial.SetColor("_BaseColor",Color.white);glyphMaterial.SetFloat("_AlphaClip",1);glyphMaterial.SetFloat("_Cutoff",.25f);glyphMaterial.EnableKeyword("_ALPHATEST_ON");glyphMaterial.SetFloat("_Cull",0);AssetDatabase.CreateAsset(glyphMaterial,GuideArt+"/Pictograms.mat");
   foreach(AstraGlyph glyph in Enum.GetValues(typeof(AstraGlyph)))
   {var uv=AstraPictogramGuide.Uv(glyph);var m=new Mesh{name="Pictogram "+glyph};m.vertices=new[]{V(-.5f,-.5f,0),V(.5f,-.5f,0),V(.5f,.5f,0),V(-.5f,.5f,0)};m.uv=new[]{new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMax,uv.yMin),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMin,uv.yMax)};m.triangles=new[]{0,2,1,0,3,2};m.RecalculateNormals();AssetDatabase.CreateAsset(m,GuideArt+"/"+glyph+".asset");}
  }
  static Transform Icon(AstraGlyph glyph,Transform parent,Vector3 local,float size)
  {var t=new GameObject("Pictogram "+glyph).transform;t.SetParent(parent,false);t.localPosition=local;t.localScale=Vector3.one*size;t.gameObject.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(GuideArt+"/"+glyph+".asset");var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=glyphMaterial;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return t;}
  static Transform PicturePanel(string name,Transform parent,Vector3 world,Quaternion rotation,float size,params AstraGlyph[] sequence)
  {
   var t=Group(name,world,parent);t.rotation=rotation;float width=sequence.Length*size;
   var back=Box("Pictogram backing - no collider",t,world,V(width+.16f,size+.16f,.045f),dark,false);back.transform.localRotation=Quaternion.identity;
   for(int i=0;i<sequence.Length;i++)Icon(sequence[i],t,V((i-(sequence.Length-1)*.5f)*size,0,-.04f),size);
   return t;
  }
 }
}
