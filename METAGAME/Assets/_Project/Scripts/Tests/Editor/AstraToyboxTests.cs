using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AstraToyboxTests
    {
        readonly List<GameObject> objects=new List<GameObject>();
        GameObject New(string name,Vector3 p=default){var go=new GameObject(name);go.transform.position=p;objects.Add(go);return go;}
        [SetUp]public void Setup()=>WorldEvents.RemoteDecides=false;
        [TearDown]public void Cleanup(){for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null)Object.DestroyImmediate(objects[i]);objects.Clear();WorldEvents.RemoteDecides=false;}
        T Tool<T>()where T:ToolBase{var go=New("tool");var rb=go.AddComponent<Rigidbody>();var t=go.AddComponent<T>();t.Equip(New("owner").AddComponent<PlayerRig>(),rb);return t;}
        GameObject Wall(float z){var wall=New("wall",Vector3.forward*z);wall.AddComponent<BoxCollider>().size=new Vector3(4,4,.4f);Physics.SyncTransforms();return wall;}
        [Test]public void GrappleRequiresAnActualSurface(){var t=Tool<AstraGrappleTool>();Assert.IsFalse(t.Attach(Vector3.forward));Assert.IsFalse(t.Attached);}
        [Test]public void GrappleRespectsRange(){var t=Tool<AstraGrappleTool>();Wall(28);Assert.IsFalse(t.Attach(Vector3.forward));}
        [Test]public void GrappleHooksNearestWall(){var t=Tool<AstraGrappleTool>();Wall(10);Wall(5);Assert.IsTrue(t.Attach(Vector3.forward));Assert.That(t.Snapshot.End.z,Is.EqualTo(4.8f).Within(.01));}
        [Test]public void ReleaseInputImmediatelyDetaches(){var t=Tool<AstraGrappleTool>();Wall(5);t.Attach(Vector3.forward);t.SetInput(new ToolInputFrame{PrimaryReleased=true},default);Assert.IsFalse(t.Attached);}
        [Test]public void UnequipCancelsCable(){var t=Tool<AstraGrappleTool>();Wall(5);t.Attach(Vector3.forward);t.Unequip();Assert.IsFalse(t.Attached);Assert.IsFalse(t.Attach(Vector3.forward));}
        [Test]public void ObstructionBreaksCable(){var t=Tool<AstraGrappleTool>();Wall(10);t.Attach(Vector3.forward);Wall(5);t.Step(.02f);Assert.IsFalse(t.Attached);}
        [Test]public void DestroyingTargetDetaches(){var t=Tool<AstraGrappleTool>();var wall=Wall(10);t.Attach(Vector3.forward);Object.DestroyImmediate(wall);t.Step(.02f);Assert.IsFalse(t.Attached);}
        [Test]public void GrappleTimesOutInsteadOfHoldingForever(){var t=Tool<AstraGrappleTool>();Wall(10);t.Attach(Vector3.forward);t.Step(8.1f);Assert.IsFalse(t.Attached);}
        [Test]public void ClientsCannotStartTools(){var t=Tool<AstraGrappleTool>();var b=Tool<AstraAirBlower>();Wall(5);WorldEvents.RemoteDecides=true;Assert.IsFalse(t.Attach(Vector3.forward));Assert.AreEqual(0,b.Blow(Vector3.forward));}
        [Test]public void BlowerPushesVisibleObjectOnlyOnceDespiteCompoundColliders()
        {
            var t=Tool<AstraAirBlower>();var prop=New("prop",Vector3.forward*4);prop.AddComponent<Rigidbody>();prop.AddComponent<BoxCollider>();prop.AddComponent<SphereCollider>();Physics.SyncTransforms();
            Assert.AreEqual(1,t.Blow(Vector3.forward));Wall(2);Assert.AreEqual(0,t.Blow(Vector3.forward));
        }
        [Test]public void BlowerRejectsObjectsOutsideCone(){var t=Tool<AstraAirBlower>();var prop=New("prop",new Vector3(4,0,3));prop.AddComponent<Rigidbody>();prop.AddComponent<BoxCollider>();Physics.SyncTransforms();Assert.AreEqual(0,t.Blow(Vector3.forward));}
        [Test]public void BouncePreservesHorizontalVelocityAndDebounces()
        {
            var p=New("pad").AddComponent<AstraBouncePad>();var body=New("ball").AddComponent<Rigidbody>();body.linearVelocity=new Vector3(2,-3,4);
            Assert.IsTrue(p.Launch(body));Assert.AreEqual(new Vector3(2,11,4),body.linearVelocity);Assert.IsFalse(p.Launch(body));
        }
        [Test]public void BounceDoesNotMoveRemoteOrKinematicBody()
        {
            var p=New("pad").AddComponent<AstraBouncePad>();var body=New("ball").AddComponent<Rigidbody>();body.isKinematic=true;Assert.IsFalse(p.Launch(body));
            body.isKinematic=false;WorldEvents.RemoteDecides=true;Assert.IsFalse(p.Launch(body));
        }
    }
}
