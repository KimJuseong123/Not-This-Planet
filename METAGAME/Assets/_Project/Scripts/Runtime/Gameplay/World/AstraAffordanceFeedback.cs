using UnityEngine;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
 /// <summary>Reads authoritative or replicated mechanism state; renders a ready lamp and gauge only.</summary>
 public sealed class AstraAffordanceFeedback : MonoBehaviour
 {
  [SerializeField] Component source;
  [SerializeField] Renderer lamp;
  [SerializeField] Transform fill;
  MaterialPropertyBlock block;
  public void Configure(Component value,Renderer light,Transform bar){source=value;lamp=light;fill=bar;Refresh();}
  public float Progress {get;private set;}
  public bool Ready {get;private set;}
  void LateUpdate()=>Refresh();
  void Refresh()
  {
   float value=0;bool ready=false;
   if(source is AstraFuelCan can){value=can.Units/AstraFuelCan.CanCapacity;ready=value>0;}
   else if(source is AstraFuelTank tank){value=tank.Units/tank.Capacity;ready=tank.HasFuel;}
   else if(source is AstraV2Puzzle puzzle){value=puzzle.OpenFraction;ready=puzzle.Solved;}
   else if(source is AstraV2FreightLift lift){value=lift.Snapshot.Travel;ready=lift.Snapshot.Unlocked;}
   else if(source is AstraPlayerCannon cannon){var s=cannon.PresentationState;ready=s.Occupied;value=s.Burning?s.Remaining/AstraPlayerCannon.FuseSeconds:s.Occupied?1:0;}
   else if(source is AstraBlastGate gate){ready=gate.Cleared||gate.ChargePlacedForClear;value=ready?1:0;}
   Progress=Mathf.Clamp01(value);Ready=ready;
   if(lamp!=null){if(block==null)block=new MaterialPropertyBlock();var c=ready?new Color(.1f,1,.5f):new Color(1,.45f,.05f);block.SetColor("_BaseColor",c);block.SetColor("_Color",c);block.SetColor("_EmissionColor",c*.4f);lamp.SetPropertyBlock(block);}
   if(fill!=null){fill.localScale=new Vector3(Mathf.Max(.015f,Progress),1,1);fill.localPosition=new Vector3((Progress-1)*.5f,0,0);}
  }
 }
}
