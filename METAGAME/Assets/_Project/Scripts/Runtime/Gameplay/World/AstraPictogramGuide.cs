using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
 public enum AstraGlyph { Hand,Click,Hold,Release,Person,Box,Roller,Arrow,Feet,Door,Dial,Rocket,Hook,Wind,Drop,Can,Mine,Flag,Burst,Cannon,Clock,Brake,Down,Up,Return,Pin,Ship,Triangle,Diamond,Square,Warning,Check,Bed,Food,Lever,Bridge,Weld,Bat,Turn,Cross }

 /// <summary>Scene-local, visual-only guidance. It never grants input or changes puzzle state.</summary>
 public sealed class AstraPictogramGuide : MonoBehaviour
 {
  static readonly List<AstraPictogramGuide> guides=new List<AstraPictogramGuide>();
  [SerializeField] Texture2D atlas;
  public Texture2D Atlas=>atlas;
  public void Configure(Texture2D value)=>atlas=value;
  void OnEnable(){if(!guides.Contains(this))guides.Add(this);}
  void OnDisable()=>guides.Remove(this);
  public static AstraPictogramGuide ForScene(Scene scene)
  {foreach(var guide in guides)if(guide!=null&&guide.gameObject.scene==scene)return guide;return null;}
  public static Rect Uv(AstraGlyph glyph){int i=(int)glyph;return new Rect((i%8)/8f,(i/8)/8f,1/8f,1/8f);}
  public static AstraGlyph ToolGlyph(ToolBase tool)
  {
   if(tool is AstraRocketTool)return AstraGlyph.Rocket;
   if(tool is AstraGrappleTool)return AstraGlyph.Hook;
   if(tool is AstraAirBlower)return AstraGlyph.Wind;
   if(tool is AstraFuelCan)return AstraGlyph.Can;
   if(tool is AstraMineDetector)return AstraGlyph.Mine;
   return AstraGlyph.Drop;
  }
  public void DrawHeldTool(PlayerRig player,float width,float height)
  {
   if(atlas==null)return;
   var tool=player.PresentationTool as AstraHandTool;
   var tank=player.MountedVehicle!=null?player.MountedVehicle.GetComponent<AstraFuelTank>():null;
   if(tank==null&&(tool==null||!tool.IsHeldForView))return;
   var saved=GUI.color;float x=width*.5f-105,y=height*.65f;
   GUI.color=new Color(.025f,.035f,.06f,.88f);GUI.DrawTexture(new Rect(x-10,y-6,220,74),Texture2D.whiteTexture);
   GUI.color=Color.white;
   bool held=tool is AstraGrappleTool||tool is AstraAirBlower||tool is AstraMineDetector;
   Draw(new Rect(x,y,48,48),tank!=null?AstraGlyph.Can:held?AstraGlyph.Hold:AstraGlyph.Click);
   Draw(new Rect(x+53,y+9,28,28),AstraGlyph.Arrow);
   Draw(new Rect(x+90,y,48,48),tank!=null?AstraGlyph.Ship:ToolGlyph(tool));
   if(tool is AstraMineDetector)Draw(new Rect(x+153,y,48,48),AstraGlyph.Feet);
   else if(tool is AstraRocketTool)Draw(new Rect(x+153,y,48,48),AstraGlyph.Person);
   else if(held)Draw(new Rect(x+153,y,48,48),AstraGlyph.Release);
   float progress=tank!=null?tank.Units/tank.Capacity:tool.ViewProgress;
   GUI.color=new Color(.2f,.25f,.3f);GUI.DrawTexture(new Rect(x,y+55,200,5),Texture2D.whiteTexture);
   GUI.color=tool is AstraRocketTool&&tool.ViewAction==2?new Color(1,.55f,.1f):new Color(.15f,.85f,1);
   GUI.DrawTexture(new Rect(x,y+55,200*Mathf.Clamp01(progress),5),Texture2D.whiteTexture);GUI.color=saved;
  }
  void Draw(Rect rect,AstraGlyph glyph)=>GUI.DrawTextureWithTexCoords(rect,atlas,Uv(glyph));
 }
}
