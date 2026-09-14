using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
namespace Metagame.Gameplay.Tests
{
    public sealed class AstraMinefieldTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        GameObject New(){var go=new GameObject("mine test");objects.Add(go);return go;}
        [SetUp] public void Setup()=>WorldEvents.RemoteDecides=false;
        [TearDown] public void Cleanup(){for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();WorldEvents.RemoteDecides=false;}
        AstraMinefield Field(){var f=New().AddComponent<AstraMinefield>();f.Configure(new[]{Vector3.zero,Vector3.right*4},new Transform[0],null);typeof(AstraMinefield).GetMethod("OnEnable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(f,null);return f;}
        PlayerRig Player(Vector3 foot){var p=PlayerRig.Create("mine tester",foot,Quaternion.identity,Color.blue,Color.white,false);objects.Add(p.gameObject);p.ServerAuthority=true;return p;}
        [Test]public void DetectorFindsNearestUnspentMine(){var f=Field();Assert.That(AstraMinefield.Nearest(Vector3.right,f.gameObject.scene,6),Is.EqualTo(1));f.ApplySnapshot(new AstraMinefield.State{Spent=1,Age=20});Assert.That(AstraMinefield.Nearest(Vector3.right,f.gameObject.scene,6),Is.EqualTo(3));}
        [Test]public void ElevatedMineDoesNotReportThroughOtherFloor(){var f=Field();Assert.That(AstraMinefield.Nearest(Vector3.up*5,f.gameObject.scene,6),Is.EqualTo(6));}
        [Test]public void MineOnlyTriggersOnce(){var f=Field();var p=Player(Vector3.zero);Assert.IsTrue(f.TryTrigger(p));Assert.IsTrue(f.IsSpent(0));Assert.IsFalse(f.TryTrigger(p));}
        [Test]public void SafeGapDoesNotExplode(){var f=Field();Assert.IsFalse(f.TryTrigger(Player(Vector3.right*2)));}
        [Test]public void AirbornePlayerDoesNotStepOnMine(){var f=Field();Assert.IsFalse(f.TryTrigger(Player(Vector3.up*2)));}
        [Test]public void ClientCannotDetonate(){var f=Field();var p=Player(Vector3.zero);WorldEvents.RemoteDecides=true;Assert.IsFalse(f.TryTrigger(p));Assert.AreEqual(0,f.Snapshot.Spent);}
        [Test]public void LateSnapshotKeepsSpentMines(){var f=Field();f.ApplySnapshot(new AstraMinefield.State{Spent=3,Age=30});Assert.AreEqual(6,AstraMinefield.Nearest(Vector3.zero,f.gameObject.scene,6));Assert.AreEqual(30,f.Snapshot.Age);}
        [Test]public void DetectorReleaseAndDropStopScanning(){var f=Field();var p=Player(Vector3.right);var go=New();var body=go.AddComponent<Rigidbody>();var tool=go.AddComponent<AstraMineDetector>();tool.Equip(p,body);tool.SetInput(new Metagame.Core.ToolInputFrame{PrimaryHeld=true},new ToolAimContext(p,null,Vector3.forward));Assert.AreEqual(1,tool.ViewAction);Assert.Less(tool.PulseInterval,1.2f);tool.SetInput(default,new ToolAimContext(p,null,Vector3.forward));Assert.AreEqual(0,tool.ViewAction);tool.Unequip();Assert.AreEqual(0,tool.ViewAction);}
    }
}

