using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class IndependentMeteorTests
    {
        readonly List<GameObject> _objects = new();
        GameObject Make(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        [TearDown] public void Cleanup()
        {
            foreach (var meteor in Object.FindObjectsByType<Meteor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(meteor.gameObject);
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test] public void EightClocksIndependentlyResampleWithinOneToFiveSeconds()
        {
            var schedule = new MeteorDropSchedule(661);
            var previous = new float[8];
            var counts = new int[8];
            var distinct = new HashSet<int>();
            for (int tick = 1; tick <= 30000; tick++)
            {
                var mask = schedule.Tick(0.01f, 8);
                for (int lane = 0; lane < 8; lane++)
                {
                    if ((mask & (1 << lane)) == 0) continue;
                    float now = tick * 0.01f;
                    Assert.That(now - previous[lane], Is.InRange(0.999f, 5.011f));
                    previous[lane] = now;
                    counts[lane]++;
                    distinct.Add(tick);
                }
            }
            foreach (int count in counts) Assert.That(count, Is.InRange(80, 120));
            Assert.That(distinct.Count, Is.GreaterThan(650), "8개가 같은 틱에 묶여 나오지 않는다.");
        }

        [Test] public void ResetIsReproducibleAndLongFrameDoesNotQueueCatchup()
        {
            var first = new MeteorDropSchedule(19);
            var second = new MeteorDropSchedule(19);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(first.Tick(0.02f, 8), second.Tick(0.02f, 8));
            first.Reset(19); second.Reset(19);
            Assert.AreEqual(first.Tick(2f, 8), second.Tick(2f, 8));
            Assert.AreEqual(255, first.Tick(100f, 8));
            Assert.AreEqual(0, first.Tick(0.5f, 8));
            Assert.AreEqual(0, first.Tick(float.NaN, 8));
        }

        MeteorSpawner Spawner()
        {
            var spawner = Make("spawner").AddComponent<MeteorSpawner>();
            spawner.Configure(661);
            spawner.EnableIndependentDrops();
            return spawner;
        }

        [TestCase(1)] [TestCase(2)] [TestCase(4)]
        public void AssaultUsesEightClocksForEveryCrewAndKeepsWarningAndLiveCap(int crew)
        {
            var spawner = Spawner();
            spawner.SetCrew(crew);
            spawner.BeginAssault();
            int count = 0;
            spawner.DropScheduled += drop => { count++; Assert.That(drop.WarningSeconds, Is.GreaterThan(0)); };
            for (int i = 0; i < 500; i++) spawner.Tick(0.01f);
            Assert.That(count, Is.GreaterThanOrEqualTo(8));
            Assert.That(spawner.Live.Count, Is.EqualTo(count));
            for (int i = 0; i < 1000; i++) spawner.Tick(0.02f);
            Assert.That(spawner.Live.Count, Is.LessThanOrEqualTo(16));
            Assert.That(spawner.WaveActive, Is.False);
        }

        [Test] public void PauseAndSequenceHoldFreezeClocksWithoutBacklog()
        {
            var spawner = Spawner();
            spawner.BeginAssault();
            float remaining = spawner.SecondsUntilWave;
            spawner.SetPaused(true); spawner.Tick(100f);
            spawner.SetPaused(false); spawner.SetSequenceHold(true); spawner.Tick(100f);
            Assert.That(spawner.Live.Count, Is.Zero);
            Assert.That(spawner.SecondsUntilWave, Is.EqualTo(remaining));
            spawner.SetSequenceHold(false); spawner.Tick(0.01f);
            Assert.That(spawner.Live.Count, Is.Zero);
        }

        [Test] public void ExplorationDropsOnMapAndAssaultTargetsWithinConfiguredDisc()
        {
            var spawner = Spawner();
            spawner.SetArea(null, new Vector2(100, 80));
            var player = Make("target").AddComponent<PlayerRig>();
            player.transform.position = new Vector3(200, 0, 200);
            player.Doll = new Doll { Hips = player.gameObject.AddComponent<Rigidbody>() };
            if (!PlayerRig.All.Contains(player)) PlayerRig.All.Add(player);
            var settings = new SerializedObject(spawner);
            settings.FindProperty("_environmentWeight").floatValue = 0;
            settings.FindProperty("_nearPlayerWeight").floatValue = 1;
            settings.FindProperty("_workAreaWeight").floatValue = 0;
            settings.FindProperty("_launchPadWeight").floatValue = 0;
            settings.FindProperty("_assaultPlayerTargetRadius").floatValue = 7;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var points = new List<Vector3>();
            spawner.DropScheduled += drop => points.Add(drop.ImpactPoint);
            for (int i = 0; i < 500; i++) spawner.Tick(0.01f);
            Assert.That(points.Count, Is.GreaterThan(0));
            foreach (var point in points) { Assert.That(Mathf.Abs(point.x), Is.LessThanOrEqualTo(50)); Assert.That(Mathf.Abs(point.z), Is.LessThanOrEqualTo(40)); }
            spawner.ResetRun(); points.Clear(); spawner.BeginAssault();
            for (int i = 0; i < 500; i++) spawner.Tick(0.01f);
            Assert.That(points.Count, Is.GreaterThanOrEqualTo(8));
            var unique = new HashSet<Vector3>(points);
            Assert.That(unique.Count, Is.EqualTo(points.Count));
            foreach (var point in points)
            {
                var offset = point - player.Position; offset.y = 0;
                Assert.That(offset.magnitude, Is.InRange(0.001f, 7.001f), "솔로도 연속 표적이 맵 무작위로 이탈하지 않는다.");
            }
            PlayerRig.All.Remove(player);
        }

        [Test] public void MultiplayerTargetsAlternateWithinEachPlayersRadius()
        {
            var spawner = Spawner();
            var settings = new SerializedObject(spawner);
            settings.FindProperty("_environmentWeight").floatValue = 0;
            settings.FindProperty("_nearPlayerWeight").floatValue = 1;
            settings.FindProperty("_workAreaWeight").floatValue = 0;
            settings.FindProperty("_launchPadWeight").floatValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var players = new PlayerRig[2];
            for (int i = 0; i < 2; i++)
            {
                players[i] = Make("peer " + i).AddComponent<PlayerRig>();
                players[i].IsLocal = i == 0;
                players[i].transform.position = new Vector3(i * 100, 0, 0);
                players[i].Doll = new Doll { Hips = players[i].gameObject.AddComponent<Rigidbody>() };
                if (!PlayerRig.All.Contains(players[i])) PlayerRig.All.Add(players[i]);
            }
            var targets = new List<int>();
            spawner.DropScheduled += drop => {
                int chosen = drop.ImpactPoint.x < 50 ? 0 : 1;
                var offset = drop.ImpactPoint - players[chosen].Position; offset.y = 0;
                Assert.That(offset.magnitude, Is.LessThanOrEqualTo(spawner.AssaultPlayerTargetRadius + 0.001f));
                if (targets.Count > 0) Assert.AreNotEqual(targets[targets.Count - 1], chosen);
                targets.Add(chosen);
            };
            spawner.SetCrew(2); spawner.BeginAssault();
            for (int i = 0; i < 500; i++) spawner.Tick(0.01f);
            Assert.That(targets.Count, Is.GreaterThanOrEqualTo(8));
        }

        [Test] public void PinReleasedDuringEntryStartsAssaultWhenEntryFinishes()
        {
            var go = Make("session");
            var session = go.AddComponent<GameSession>(); session.Begin(null, null, Vector3.zero);
            var sequence = go.AddComponent<Stage1Sequence>();
            double now = 0;
            sequence.Configure(session, Spawner(), () => true, () => now);
            sequence.TryBeginEntry();
            Make("early pin").AddComponent<PartClamp>().Release();
            sequence.Tick();
            Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Entry));
            now = Stage1Sequence.EntrySeconds; sequence.Tick();
            Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Assault));
        }

        [Test] public void OldAreaTriggerDoesNotBeginAssault()
        {
            var go = Make("session");
            var session = go.AddComponent<GameSession>(); session.Begin(null, null, Vector3.zero);
            var sequence = go.AddComponent<Stage1Sequence>();
            double now = 0;
            sequence.Configure(session, Spawner(), () => true, () => now);
            sequence.TryBeginEntry(); now = Stage1Sequence.EntrySeconds; sequence.Tick();
            var trigger = Make("old parking trigger").AddComponent<Stage1AssaultTrigger>();
            var collider = Make("player collider").AddComponent<BoxCollider>();
            collider.gameObject.AddComponent<PlayerRig>();
            trigger.SendMessage("OnTriggerStay", collider, SendMessageOptions.DontRequireReceiver);
            sequence.Tick();
            Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
        }

        [TestCase(true)] [TestCase(false)]
        public void FirstCompletedClampStartsOnlyAuthorityAndReplicaCannotRestart(bool authority)
        {
            var go = Make("session");
            var session = go.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var sequence = go.AddComponent<Stage1Sequence>();
            var spawner = Spawner();
            double now = 0;
            sequence.Configure(session, spawner, () => authority, () => now);
            if (authority) { sequence.TryBeginEntry(); now = 18; sequence.Tick(); }
            else sequence.ApplyReplica(Stage1SequencePhase.Exploration, 18, 2, false);
            var clamp = Make("pin").AddComponent<PartClamp>();
            clamp.TryJoinPull(); clamp.Pull(0.01f);
            Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
            if (authority) clamp.Pull(PhysicsScale.ClampPullSeconds * 10);
            else clamp.ApplyNetworkState(1, true);
            sequence.Tick();
            Assert.That(sequence.Phase, Is.EqualTo(authority ? Stage1SequencePhase.Assault : Stage1SequencePhase.Exploration));
            int revision = sequence.Revision;
            clamp.Release();
            Make("another pin").AddComponent<PartClamp>().Release();
            sequence.Tick();
            Assert.That(sequence.Revision, Is.EqualTo(revision));
            if (authority)
            {
                now += Stage1Sequence.WarningSeconds; sequence.Tick();
                Assert.That(spawner.AssaultEnabled, Is.True);
                now += Stage1Sequence.AssaultSeconds; sequence.Tick();
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
            }
            else
            {
                sequence.ApplyReplica(Stage1SequencePhase.Combat, 26, 4, true);
                Assert.That(spawner.AssaultEnabled, Is.True);
            }
        }
    }
}
