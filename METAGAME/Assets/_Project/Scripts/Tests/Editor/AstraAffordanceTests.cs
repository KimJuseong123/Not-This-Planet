using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Tests
{
 public sealed class AstraAffordanceTests
 {
  Scene scene;
  [SetUp]public void Setup(){scene=EditorSceneManager.NewPreviewScene();WorldEvents.RemoteDecides=false;}
  [TearDown]public void Cleanup(){EditorSceneManager.ClosePreviewScene(scene);WorldEvents.RemoteDecides=false;}
  GameObject New(){var g=new GameObject("affordance test");SceneManager.MoveGameObjectToScene(g,scene);return g;}
  static void Invoke(Component c,string method)=>c.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,null);
  [Test]public void PictogramRegistrationIsScopedToItsScene()
  {var guide=New().AddComponent<AstraPictogramGuide>();Invoke(guide,"OnEnable");Assert.AreSame(guide,AstraPictogramGuide.ForScene(scene));Assert.IsNull(AstraPictogramGuide.ForScene(default));Invoke(guide,"OnDisable");Assert.IsNull(AstraPictogramGuide.ForScene(scene));}
  [Test]public void FuelGaugeReadsFuelWithoutConsumingIt()
  {var tank=New().AddComponent<AstraFuelTank>();tank.Add(30);var f=New().AddComponent<AstraAffordanceFeedback>();f.Configure(tank,null,New().transform);Invoke(f,"LateUpdate");Assert.AreEqual(.5f,f.Progress);Assert.IsTrue(f.Ready);Assert.AreEqual(30,tank.Units);tank.ApplySnapshot(0);Invoke(f,"LateUpdate");Assert.IsFalse(f.Ready);Assert.AreEqual(0,f.Progress);}
  [Test]public void ClientCannonGaugeUsesReceivedState()
  {var cannon=New().AddComponent<AstraPlayerCannon>();var f=New().AddComponent<AstraAffordanceFeedback>();f.Configure(cannon,null,null);WorldEvents.RemoteDecides=true;cannon.ApplySnapshot(new AstraPlayerCannon.State{Occupied=true,Burning=true,Remaining=1.5f});Invoke(f,"LateUpdate");Assert.IsTrue(f.Ready);Assert.AreEqual(.5f,f.Progress);Assert.IsNull(cannon.Rider);}
  [Test]public void LiftGaugeNeverUnlocksThePlatform()
  {var lift=New().AddComponent<AstraV2FreightLift>();var f=New().AddComponent<AstraAffordanceFeedback>();f.Configure(lift,null,null);lift.ApplySnapshot(new AstraV2FreightLift.State{Travel=.7f,Unlocked=false});Invoke(f,"LateUpdate");Assert.IsFalse(f.Ready);Assert.AreEqual(.7f,f.Progress);Assert.IsFalse(lift.Unlocked);}
  [Test]public void DemolitionLampRequiresActualChargePlacement()
  {var gate=New().AddComponent<AstraBlastGate>();var charge=New().AddComponent<Rigidbody>();var rocks=New();rocks.transform.position=Vector3.forward*5;gate.Configure(charge,rocks,null,null,null,new Transform[0]);Assert.IsFalse(gate.ChargePlacedForClear);charge.position=Vector3.forward*4;Assert.IsTrue(gate.ChargePlacedForClear);Assert.IsFalse(gate.Cleared);gate.ApplySnapshot(new AstraBlastGate.State{Spent=true});Assert.IsFalse(gate.ChargePlacedForClear);}
  [Test]public void AtlasCellsStayInRangeAndDoNotAlias()
  {var cells=new System.Collections.Generic.HashSet<Rect>();foreach(AstraGlyph glyph in System.Enum.GetValues(typeof(AstraGlyph))){var r=AstraPictogramGuide.Uv(glyph);Assert.That(r.xMin,Is.GreaterThanOrEqualTo(0));Assert.That(r.yMax,Is.LessThanOrEqualTo(1));Assert.IsTrue(cells.Add(r));}}
 }
}
