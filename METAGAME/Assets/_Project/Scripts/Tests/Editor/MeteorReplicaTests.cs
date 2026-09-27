using System.Collections;
using System.Reflection;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class MeteorReplicaTests
    {
        MeteorSpawner _spawner;
        [SetUp] public void Setup()
        {
            _spawner = new GameObject("replica test").AddComponent<MeteorSpawner>();
            _spawner.Configure(718);
        }

        [TearDown] public void Cleanup()
        {
            if (_spawner == null) return;
            _spawner.ClearNetworkMeteors();
            _spawner.ClearNetworkScars();
            _spawner.ResetRun();
            Object.DestroyImmediate(_spawner.gameObject);
        }

        static MeteorSpawner.NetworkMeteorPose Falling(int id, float x) => new()
        {
            Id = id, Position = new Vector3(x, 10, 0), Rotation = Quaternion.identity,
            Velocity = Vector3.down,
        };
        static MeteorSpawner.NetworkScarState Scar(int id, float x, bool moving = false) => new()
        {
            Id = id, Position = new Vector3(x, 0, 0), Rotation = Quaternion.identity, Moving = moving,
        };

        [Test] public void RemovingTheFirstMeteorDoesNotReassignTheRemainingTrail()
        {
            _spawner.ApplyNetworkMeteors(new[] { Falling(11, 0), Falling(22, 10), Falling(33, 20) });
            var second = _spawner.Ghosts[1];
            var third = _spawner.Ghosts[2];
            _spawner.ApplyNetworkMeteors(new[] { Falling(33, 20), Falling(22, 10) });
            Assert.That(_spawner.Ghosts[0], Is.SameAs(third));
            Assert.That(_spawner.Ghosts[1], Is.SameAs(second));
        }

        [Test] public void PositionCorrectionMovesTheBodyImmediatelyButNotTheArtwork()
        {
            _spawner.ApplyNetworkMeteors(new[] { Falling(11, 0) });
            var ghost = _spawner.Ghosts[0];
            var visual = ghost.ReplicaVisual;
            Vector3 before = visual.Visual.position;
            _spawner.ApplyNetworkMeteors(new[] { Falling(11, 0.4f) });
            Assert.That(ghost.Body.position.x, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(Vector3.Distance(visual.Visual.position, before), Is.LessThan(0.001f));
            visual.Advance(0.05f);
            Assert.That(visual.Visual.position.x, Is.InRange(0.01f, 0.399f));
            visual.Advance(1f);
            Assert.That(visual.Visual.position.x, Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test] public void LargeCorrectionsSnapInsteadOfDraggingThroughTheLevel()
        {
            _spawner.ApplyNetworkMeteors(new[] { Falling(11, 0) });
            _spawner.ApplyNetworkMeteors(new[] { Falling(11, 100) });
            Assert.That(_spawner.Ghosts[0].ReplicaVisual.Visual.position.x, Is.EqualTo(100));
        }

        [Test] public void ScarPoolPreservesCollisionAndStopsTheFallingTrail()
        {
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0) }, true);
            Assert.That(_spawner.TryGetScarGhost(1, out var first), Is.True);
            _spawner.ApplyNetworkScars(System.Array.Empty<MeteorSpawner.NetworkScarState>(), true);
            Assert.That(first.gameObject.activeSelf, Is.False);
            _spawner.ApplyNetworkScars(new[] { Scar(2, 20) }, true);
            Assert.That(_spawner.TryGetScarGhost(2, out var reused), Is.True);
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.Body.isKinematic && reused.Body.detectCollisions, Is.True);
            foreach (var collider in reused.Colliders) Assert.That(collider.enabled, Is.True);
            foreach (var trail in reused.GetComponentsInChildren<TrailRenderer>())
                Assert.That(trail.emitting, Is.False);
            Assert.That(reused.ReplicaVisual.Visual.position.x, Is.EqualTo(20));
        }

        [Test] public void RemoteScarCannotApplyDamageEvenIfItsComponentIsReenabled()
        {
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0) }, true);
            _spawner.TryGetScarGhost(1, out var ghost);
            ghost.enabled = true;
            bool impact = false;
            ghost.Impacted += (_, _) => impact = true;
            ghost.ApplyImpact(Vector3.zero);
            Assert.That(ghost.HasImpacted || impact, Is.False);
            Assert.That(ghost.Body.isKinematic, Is.True);
        }

        [Test] public void MovingFullSnapshotsUseServerTimeEvenWhenDeliveredTogether()
        {
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0, true) }, true, 10);
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0.1f, true) }, true, 10.05);
            Assert.That(Motion(1, "Linear"), Is.EqualTo(new Vector3(2, 0, 0)));
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0.1f) }, true, 10.1);
            Assert.That(Motion(1, "Linear"), Is.EqualTo(Vector3.zero));
        }

        object Motion(int id, string field)
        {
            var records = (IDictionary)typeof(MeteorSpawner).GetField("_scarMotion",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_spawner);
            var value = records[id];
            return value.GetType().GetField(field).GetValue(value);
        }

        [Test] public void LateMembershipStillCreatesAndDeletesWithoutRewindingExistingStones()
        {
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0), Scar(2, 5) }, true, 10);
            _spawner.ApplyNetworkScars(new[] { Scar(1, 1, true) }, false, 10.05);
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0), Scar(3, 8) }, true, 10.01, false);
            Assert.That(_spawner.TryGetScarGhost(1, out var one), Is.True);
            Assert.That(one.Body.position.x, Is.EqualTo(1));
            Assert.That(_spawner.TryGetScarGhost(2, out _), Is.False);
            Assert.That(_spawner.TryGetScarGhost(3, out _), Is.True);
        }

        [Test] public void CatchUpPhysicsStepsShareOneRenderFrameSpawnBudget()
        {
            _spawner.EnableIndependentDrops();
            _spawner.BeginAssault();
            int launches = 0;
            _spawner.DropScheduled += _ => launches++;
            _spawner.Tick(5f, 100);
            _spawner.Tick(0.02f, 100);
            _spawner.Tick(0.02f, 100);
            Assert.That(launches, Is.EqualTo(2));
            _spawner.Tick(0.02f, 101);
            Assert.That(launches, Is.EqualTo(4));
        }

        [Test] public void ReliableDeltasPreserveOtherStonesAndRecoverFromAMissingMove()
        {
            _spawner.ApplyNetworkScars(new[] { Scar(1, 0), Scar(2, 2) }, true, 10);
            _spawner.TryGetScarGhost(1, out var one);
            _spawner.ApplyNetworkScarChanges(new[] { Scar(3, 3) }, new[] { 2 }, 10.1);
            Assert.That(_spawner.TryGetScarGhost(1, out var sameOne), Is.True);
            Assert.That(sameOne, Is.SameAs(one));
            Assert.That(_spawner.TryGetScarGhost(2, out _), Is.False);
            Assert.That(_spawner.TryGetScarGhost(3, out _), Is.True);
            // A periodic complete snapshot also repairs a client that missed readiness.
            _spawner.ApplyNetworkScars(new[] { Scar(3, 4), Scar(4, 5) }, true, 12);
            Assert.That(_spawner.TryGetScarGhost(1, out _), Is.False);
            Assert.That(_spawner.TryGetScarGhost(4, out _), Is.True);
        }

        [Test] public void OlderMovementDoesNotUndoANewerMembershipPose()
        {
            _spawner.ApplyNetworkScarChanges(new[] { Scar(1, 4, true) }, new int[0], 10.2);
            _spawner.ApplyNetworkScars(new[] { Scar(1, 1, true) }, false, 10.1);
            _spawner.TryGetScarGhost(1, out var ghost);
            Assert.That(ghost.Body.position.x, Is.EqualTo(4));
        }

        [Test] public void ScarDeltaPayloadContainsOnlyAddedAndRemovedIds()
        {
            var go = new GameObject("sync delta test");
            go.AddComponent<Mirror.NetworkIdentity>();
            var sync = go.AddComponent<Metagame.Network.WorldSync>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(Metagame.Network.WorldSync).GetField("lastScarIds", flags)
                    .SetValue(sync, new[] { 1, 2 });
                _spawner.ApplyNetworkScars(new[] { Scar(1, 0), Scar(3, 3) }, true);
                _spawner.TryGetScarGhost(1, out var one);
                _spawner.TryGetScarGhost(3, out var three);
                var records = new[]
                {
                    new MeteorSpawner.Scar { Id = 1, Meteor = one },
                    new MeteorSpawner.Scar { Id = 3, Meteor = three },
                };
                typeof(Metagame.Network.WorldSync).GetMethod("BuildScarChanges", flags)
                    .Invoke(sync, new object[] { records });
                var added = (MeteorSpawner.NetworkScarState[])typeof(Metagame.Network.WorldSync)
                    .GetField("sendScarAdded", flags).GetValue(sync);
                var removed = (int[])typeof(Metagame.Network.WorldSync)
                    .GetField("sendScarRemoved", flags).GetValue(sync);
                Assert.That(added.Length, Is.EqualTo(1));
                Assert.That(added[0].Id, Is.EqualTo(3));
                Assert.That(removed, Is.EqualTo(new[] { 2 }));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [UnityEngine.TestTools.UnityTest] public IEnumerator NetworkSerializationFitsTheDeclaredPacketBudgets()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            using var writer = Mirror.NetworkWriterPool.Get();
            writer.Write(Falling(int.MaxValue, 4));
            Assert.That(writer.Position, Is.EqualTo(59));
            writer.Reset();
            writer.Write(Scar(int.MaxValue, 4, true));
            Assert.That(writer.Position, Is.EqualTo(34));
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }
    }
}
