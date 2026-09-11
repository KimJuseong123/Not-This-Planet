using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class DemoEndingTests
    {
        readonly List<GameObject> _objects = new();
        readonly List<SpaceshipPart> _visited = new();
        SpaceshipPart[] _parts;
        double _clock;

        [SetUp]
        public void SetUp()
        {
            PartAssemblyLink.All.Clear();
            _parts = new SpaceshipPart[4];
            for (var i = 0; i < 4; i++)
            {
                var go = Make(PartSpec.All[i].Name);
                go.transform.position = new Vector3(50f + i * 3f, 15f - i, 30f);
                go.transform.rotation = Quaternion.Euler(75f, i * 40f, 20f);
                _parts[i] = go.AddComponent<SpaceshipPart>();
                go.GetComponent<Rigidbody>().isKinematic = true;
            }
            _clock = 100d;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            PartAssemblyLink.All.Clear();
            GameSession.Instance = null;
        }

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        PartAssemblyLink Join(int a, int b, float ratio = 0.01f)
        {
            var joint = _parts[a].gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = _parts[b].GetComponent<Rigidbody>();
            var link = _parts[a].gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(_parts[a], _parts[b], joint);
            link.SetBeadWeldRatio(ratio);
            return link;
        }

        void Connect() { Join(0, 1); Join(1, 2); Join(2, 3); }

        DemoEnding Ending(bool authority, out GameSession session)
        {
            var go = Make("Ending Session");
            session = go.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var ending = go.AddComponent<DemoEnding>();
            ending.Configure(session, _parts, () => authority, () => _clock,
                () => new[] { new DemoEndingPlayer { Name = "A", Suit = Color.white, Accent = Color.red } });
            return ending;
        }

        [Test]
        public void WeakBeadChainCompletesOutsidePadWithoutOrderingOrStanding()
        {
            Connect();
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.True);
        }

        [Test]
        public void StarGraphAndRedundantCycleBothComplete()
        {
            Join(0, 1); Join(0, 2); Join(0, 3); Join(1, 3);
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.True);
        }

        [Test]
        public void TwoSeparateWeldedPairsDoNotComplete()
        {
            Join(0, 1); Join(2, 3);
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.False);
        }

        [Test]
        public void ContactAndZeroBeadStrengthDoNotComplete()
        {
            for (var i = 0; i < 4; i++) _parts[i].transform.position = Vector3.zero;
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.False);
            Join(0, 1); Join(1, 2); Join(2, 3, 0f);
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.False);
        }

        [Test]
        public void DestroyedPhysicalJointDoesNotComplete()
        {
            Connect();
            Object.DestroyImmediate(_parts[2].GetComponent<FixedJoint>());
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.False);
        }

        [Test]
        public void DuplicateCoreTypeDoesNotReplaceMissingCoreType()
        {
            Connect();
            _parts[3].name = _parts[2].name;
            Assert.That(DemoEnding.AreConnected(_parts, _visited), Is.False);
        }

        [Test]
        public void ThreePartsNeverComplete()
        {
            Connect();
            Assert.That(DemoEnding.AreConnected(new[] { _parts[0], _parts[1], _parts[2] }, _visited), Is.False);
        }

        [Test]
        public void AuthorityStartsImmediatelyOnceAndPreservesCompletionPose()
        {
            Connect();
            var ending = Ending(true, out var session);
            var original = _parts[0].transform.position;
            Assert.That(ending.TryComplete(), Is.True);
            Assert.That(session.Cleared, Is.True);
            Assert.That(session.InputSuppressed, Is.True);
            Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.Boarding));
            _parts[0].transform.position = Vector3.zero;
            Assert.That(ending.Snapshot.Parts[0].Position, Is.EqualTo(original));
            Assert.That(ending.TryComplete(), Is.False);
            session.SetInputSuppressed(false);
            Assert.That(session.InputSuppressed, Is.True);
        }

        [Test]
        public void ParticipantCannotDecideSuccessLocally()
        {
            Connect();
            var ending = Ending(false, out var session);
            Assert.That(ending.TryComplete(), Is.False);
            Assert.That(session.Cleared, Is.False);
        }

        [Test]
        public void ReplicatedSnapshotRejoinsCurrentStageAndCannotRestartEnding()
        {
            var ending = Ending(false, out _);
            var snapshot = new DemoEndingSnapshot
            {
                StartedAt = 93d, Parts = new DemoEndingPart[0],
                Players = new[] { new DemoEndingPlayer { Name = "재접속" } }
            };
            var wire = JsonUtility.ToJson(snapshot);
            ending.ApplySnapshot(JsonUtility.FromJson<DemoEndingSnapshot>(wire));
            Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.Launching));
            ending.ApplySnapshot(new DemoEndingSnapshot { StartedAt = 100d });
            Assert.That(ending.Snapshot.StartedAt, Is.EqualTo(93d));
            _clock = 93d + DemoEnding.TotalSeconds;
            Assert.That(ending.Finished, Is.True);
        }

        [TestCase(0f, DemoEndingStage.Boarding)]
        [TestCase(4.999f, DemoEndingStage.Boarding)]
        [TestCase(5f, DemoEndingStage.Launching)]
        [TestCase(12f, DemoEndingStage.Photograph)]
        [TestCase(16f, DemoEndingStage.EndTitle)]
        [TestCase(18.999f, DemoEndingStage.EndTitle)]
        [TestCase(19f, DemoEndingStage.Lobby)]
        public void AllStagesFollowServerClockWithExactlyThreeSecondsOfEndTitle(float elapsed, DemoEndingStage stage)
        {
            Assert.That(DemoEnding.StageAt(elapsed), Is.EqualTo(stage));
        }
    }
}
