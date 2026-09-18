using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class EnvironmentGripTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(3000, 3000, 3000);
            spawned.Add(go);
            return go;
        }

        MeshCollider MakeHollowSurface()
        {
            // Two front-facing panels, with a one-metre empty gap in the middle.
            var mesh = new Mesh();
            spawned.Add(mesh);
            mesh.vertices = new[] { new Vector3(-2,-1,0), new Vector3(-2,1,0), new Vector3(-.5f,1,0), new Vector3(-.5f,-1,0),
                                    new Vector3(.5f,-1,0), new Vector3(.5f,1,0), new Vector3(2,1,0), new Vector3(2,-1,0) };
            mesh.triangles = new[] {0,1,2,0,2,3,4,5,6,4,6,7};
            mesh.RecalculateBounds();
            var collider = NewObject("Static concave surface").AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            return collider;
        }

        [Test]
        public void ConcaveSurfaceUsesRealHitAndRejectsEmptyGapAndOutOfReachPoint()
        {
            var collider = MakeHollowSurface();
            var origin = collider.transform.position;
            Assert.IsTrue(Hand.TryGetGripPoint(collider,origin+new Vector3(1,0,-.1f),.2f,out var point));
            Assert.AreEqual(origin.z,point.z,.001f);
            Assert.IsFalse(Hand.TryGetGripPoint(collider,origin+new Vector3(0,0,-.1f),.2f,out _));
            Assert.IsFalse(Hand.TryGetGripPoint(collider,origin+new Vector3(1,0,-2),.2f,out _));
        }

        [Test]
        public void SearchFindsConcaveEnvironmentAndHandAttachesToWorldThenReleases()
        {
            var collider = MakeHollowSurface();
            var grip = collider.gameObject.AddComponent<Grabbable>();
            grip.Climbable = true;
            grip.PhysicsGrip = true;
            var probe = collider.transform.position+new Vector3(1,0,-.1f);
            var found = Hand.Search(probe,.2f,probe-Vector3.forward,Vector3.forward,60,out _,out var body);
            Assert.AreSame(grip,found);
            Assert.IsNull(body);
            var hand = NewObject("Hand").AddComponent<Hand>();
            hand.Body = hand.gameObject.AddComponent<Rigidbody>();
            hand.transform.position = probe;
            Assert.IsTrue(hand.GripTo(found,body));
            Assert.IsTrue(hand.Hanging);
            Assert.IsFalse(hand.Carrying);
            Assert.IsNull(hand.Grip.connectedBody);
            Assert.AreEqual(1,grip.HandCount);
            hand.Release();
            Assert.IsFalse(hand.Gripping);
            Assert.AreEqual(0,grip.HandCount);
            grip.enabled = false;
            Assert.IsNull(Hand.Search(probe,.2f,probe-Vector3.forward,Vector3.forward,60,out _,out _));
        }

        [Test]
        public void PrimitiveCarryBehaviorIsPreservedAndTriggersAreRejected()
        {
            var go = NewObject("Cargo");
            var collider = go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 1;
            var grip = go.AddComponent<Grabbable>();
            Physics.SyncTransforms();
            Assert.IsTrue(Hand.TryGetGripPoint(collider,go.transform.position+Vector3.forward*.6f,.2f,out _));
            var hand = NewObject("Hand").AddComponent<Hand>();
            hand.Body = hand.gameObject.AddComponent<Rigidbody>();
            Assert.IsTrue(hand.GripTo(grip,body));
            Assert.IsTrue(hand.Carrying);
            Assert.IsFalse(hand.Hanging);
            hand.Release();
            collider.isTrigger = true;
            Assert.IsFalse(Hand.TryGetGripPoint(collider,go.transform.position,.2f,out _));
        }

        [Test]
        public void CrosshairAndPlayerGrabInputRecognizeConcaveEnvironment()
        {
            var player = PlayerRig.Create("Environment grip player", new Vector3(3100,3000,3000),
                Quaternion.identity,Color.white,Color.gray,false);
            spawned.Add(player.gameObject);
            var collider = MakeHollowSurface();
            collider.transform.rotation = Quaternion.LookRotation(player.AimForward);
            collider.transform.position = player.AimOrigin + player.AimForward*.55f - collider.transform.right;
            var grip = collider.gameObject.AddComponent<Grabbable>();
            grip.Climbable = true;
            grip.PhysicsGrip = true;
            Physics.SyncTransforms();
            player.RefreshInteractionFocus();
            Assert.AreSame(grip,player.CurrentInteractionTarget.Grabbable);
            player.SetInput(new InputFrame { GrabLeft = true });
            player.Tick();
            Assert.AreSame(grip,player.LeftHand.Held);
            Assert.IsTrue(player.LeftHand.Hanging);
            Assert.IsFalse(player.LeftHand.Carrying);
            player.LeftHand.Release();
        }
    }
}
