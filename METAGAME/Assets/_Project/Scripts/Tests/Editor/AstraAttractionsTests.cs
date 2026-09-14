using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AstraAttractionsTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        GameObject New(string n,Vector3 p=default){var go=new GameObject(n);go.transform.position=p;objects.Add(go);return go;}
        [SetUp]public void Setup()=>WorldEvents.RemoteDecides=false;
        [TearDown]public void Cleanup(){for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();WorldEvents.RemoteDecides=false;}
        [Test]public void WheelCabinsFollowCircleWithAnOpenBoardingLowPoint()
        {
            Assert.AreEqual(new Vector3(0,-9,0),AstraFerrisWheel.CabinOffset(0,9));
            for(int i=0;i<360;i+=15)Assert.That(AstraFerrisWheel.CabinOffset(i,9).magnitude,Is.EqualTo(9).Within(.001));
            Assert.That(AstraFerrisWheel.CabinOffset(180,9).y,Is.EqualTo(9).Within(.001));
        }
        [Test]public void WheelPhaseWrapsWithoutAccumulatingTurns(){var w=New("wheel").AddComponent<AstraFerrisWheel>();w.ApplyPhase(725);Assert.AreEqual(5,w.Phase);}
        [Test]public void CannonCannotIgniteWithoutPassenger(){var c=New("cannon").AddComponent<AstraPlayerCannon>();Assert.IsFalse(c.TryIgnite(New("player").AddComponent<PlayerRig>()));Assert.IsFalse(c.Burning);}
        [Test]public void RemoteClientCannotBoardOrIgnite(){var c=New("cannon").AddComponent<AstraPlayerCannon>();var p=New("player").AddComponent<PlayerRig>();WorldEvents.RemoteDecides=true;Assert.IsFalse(c.TryEnter(p));Assert.IsFalse(c.TryIgnite(p));}
        AstraBlastGate Gate(out Rigidbody charge,out Grabbable button,out PlayerRig player)
        {
            var gate=New("gate").AddComponent<AstraBlastGate>();charge=New("TNT").AddComponent<Rigidbody>();charge.gameObject.AddComponent<Grabbable>();
            var rocks=New("rocks",Vector3.forward*10);button=New("button").AddComponent<Grabbable>();
            player=PlayerRig.Create("test operator",Vector3.zero,Quaternion.identity,Color.blue,Color.white,false);objects.Add(player.gameObject);
            var cable=New("cable").AddComponent<LineRenderer>();cable.positionCount=2;
            gate.Configure(charge,rocks,button,cable,New("flash").transform,new Transform[0]);return gate;
        }
        [Test]public void BlastOnlyClearsRockWhenTntIsNearEntrance()
        {var g=Gate(out var tnt,out _,out var p);tnt.position=Vector3.forward*9;Assert.IsTrue(g.TryDetonate(p));Assert.IsTrue(g.Cleared);Assert.IsFalse(g.TryDetonate(p));}
        [Test]public void WrongPlacementConsumesChargeButLeavesEntranceBlocked()
        {var g=Gate(out _,out _,out var p);Assert.IsTrue(g.TryDetonate(p));Assert.IsTrue(g.Snapshot.Spent);Assert.IsFalse(g.Cleared);}
        [Test]public void SwitchRequiresAPlayerNearby()
        {var g=Gate(out _,out var button,out var p);button.transform.position=Vector3.right*20;Assert.IsFalse(g.TryDetonate(p));}
        [Test]public void ClientCannotDetonateOrClearRocks()
        {var g=Gate(out var tnt,out _,out var p);tnt.position=Vector3.forward*10;WorldEvents.RemoteDecides=true;Assert.IsFalse(g.TryDetonate(p));Assert.IsFalse(g.Cleared);}
        [Test]public void LateSnapshotRetainsClearedEntrance()
        {var g=Gate(out _,out _,out _);g.ApplySnapshot(new AstraBlastGate.State{Cleared=true,Spent=true,Age=30,Explosion=Vector3.forward*10});Assert.IsTrue(g.Cleared);Assert.IsTrue(g.Snapshot.Spent);}
    }
}
