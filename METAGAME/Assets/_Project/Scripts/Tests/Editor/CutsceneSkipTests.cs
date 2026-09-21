using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using Metagame.Bootstrap;
using Mirror;

namespace Metagame.Gameplay.Tests
{
    public sealed class CutsceneSkipTests
    {
        [UnityTest]
        public IEnumerator ServerDispatchRequiresFourConnectionsAndRejectsDuplicateAndStalePackets()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Four connection skip test");
            var previousTransport = Transport.active;
            var transport = root.AddComponent<SkipTestTransport>();
            Transport.active = transport;
            var session = root.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var sequence = root.AddComponent<Stage1Sequence>();
            sequence.Configure(session, null, () => NetworkServer.active, () => 0d);
            NetworkServer.Listen(4);
            for (int i = 1; i <= 4; i++)
                NetworkServer.AddConnection(new NetworkConnectionToClient(i) { isAuthenticated = true, isReady = true });
            var owner = EndingSceneFlow.Ensure();
            var flow = owner.GetComponent<CutsceneSkipFlow>();
            try
            {
                sequence.TryBeginEntry();
                yield return null;
                yield return null;
                int revision = flow.State.Revision;
                Assert.That(flow.State.Total, Is.EqualTo(4));
                transport.Vote(1, revision);
                transport.Vote(1, revision);
                Assert.That(flow.State.Votes, Is.EqualTo(1));
                transport.Vote(2, revision);
                transport.Vote(3, revision);
                Assert.That(flow.State.Votes, Is.EqualTo(3));
                Assert.That(sequence.IsCinematic, Is.True);
                NetworkServer.connections[4].isReady = false;
                transport.Vote(4, revision);
                Assert.That(sequence.IsCinematic, Is.True);
                NetworkServer.connections[4].isReady = true;
                transport.Vote(4, revision);
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
                sequence.TryBeginAssault();
                transport.Vote(1, revision);
                Assert.That(flow.State.Votes, Is.Zero);
                revision = flow.State.Revision;
                transport.Vote(1, revision); transport.Vote(2, revision); transport.Vote(3, revision);
                NetworkServer.RemoveConnection(4);
                yield return null;
                yield return null;
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
                Assert.That(sequence.AssaultEnabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(owner.gameObject);
                NetworkServer.Shutdown();
                Transport.active = previousTransport;
                Object.DestroyImmediate(root);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SoloAdapterSkipsAndRestoresInputWithoutClosingPauseMenu()
        {
            yield return new EnterPlayMode();
            var root = new GameObject("Solo skip adapter test");
            var session = root.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var sequence = root.AddComponent<Stage1Sequence>();
            sequence.Configure(session, null, () => true, () => 0d);
            var owner = EndingSceneFlow.Ensure();
            var flow = owner.GetComponent<CutsceneSkipFlow>();
            sequence.TryBeginEntry();
            yield return null;
            yield return null;
            Assert.That(flow.State.Total, Is.EqualTo(1));
            Assert.That(flow.State.Votes, Is.Zero);
            Assert.That(session.CursorLocked, Is.False);
            session.SetInputSuppressed(true);
            Assert.That(flow.RequestSkip(), Is.False, "The pause/settings overlay owns interaction.");
            session.SetInputSuppressed(false);
            Assert.That(flow.RequestSkip(), Is.True);
            yield return null;
            Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
            Assert.That(session.CinematicInputBlocked, Is.False);
            Assert.That(session.CursorLocked, Is.True);
            Assert.That(flow.RequestSkip(), Is.False);
            sequence.TryBeginAssault();
            yield return null;
            yield return null;
            Assert.That(flow.State.Votes, Is.Zero);
            Assert.That(flow.RequestSkip(), Is.True);
            Assert.That(sequence.AssaultEnabled, Is.True);
            Object.Destroy(root);
            Object.Destroy(owner.gameObject);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator MirrorSkipMessagesPreserveBallotIdentityAndPersonalVote()
        {
            yield return new EnterPlayMode();
            var writer = new NetworkWriter();
            writer.Write(new CutsceneSkipState { Revision = 12, Kind = 2, Shot = 7, StartedAt = 42.25,
                Votes = 3, Total = 4, Voted = true });
            var state = new NetworkReader(writer.ToArraySegment()).Read<CutsceneSkipState>();
            Assert.That(state.Revision, Is.EqualTo(12));
            Assert.That(state.Kind, Is.EqualTo(2));
            Assert.That(state.Shot, Is.EqualTo(7));
            Assert.That(state.StartedAt, Is.EqualTo(42.25));
            Assert.That(state.Votes, Is.EqualTo(3));
            Assert.That(state.Total, Is.EqualTo(4));
            Assert.That(state.Voted, Is.True);
            writer.Reset();
            writer.Write(new CutsceneSkipRequest { Revision = 12 });
            Assert.That(new NetworkReader(writer.ToArraySegment()).Read<CutsceneSkipRequest>().Revision, Is.EqualTo(12));
            yield return new ExitPlayMode();
        }

        [Test]
        public void FourPlayersRequireFourDistinctCurrentVotes()
        {
            var votes = new CutsceneSkipVotes();
            votes.Reset(7);
            votes.SetParticipants(new[] { 1, 2, 3, 4 });
            Assert.That(votes.Count, Is.Zero);
            Assert.That(votes.Total, Is.EqualTo(4));
            Assert.That(votes.Vote(1, 6), Is.False, "Delayed vote from the previous shot.");
            Assert.That(votes.Vote(5, 7), Is.False, "Nonparticipants cannot vote.");
            Assert.That(votes.Vote(1, 7), Is.True);
            Assert.That(votes.Vote(1, 7), Is.False);
            Assert.That(votes.Count, Is.EqualTo(1));
            votes.Vote(2, 7); votes.Vote(3, 7);
            Assert.That(votes.Unanimous, Is.False);
            votes.Vote(4, 7);
            Assert.That(votes.Unanimous, Is.True);
            votes.Reset(8);
            votes.SetParticipants(new[] { 1, 2, 3, 4 });
            Assert.That(votes.Count, Is.Zero);
            Assert.That(votes.Vote(1, 7), Is.False);
        }

        [Test]
        public void RosterChangesRemoveDepartedVotesAndRequireNewParticipants()
        {
            var votes = new CutsceneSkipVotes();
            votes.Reset(1);
            votes.SetParticipants(new[] { 1, 2, 3 });
            votes.Vote(1, 1); votes.Vote(2, 1);
            votes.SetParticipants(new[] { 1, 2, 3, 4 });
            Assert.That(votes.Unanimous, Is.False);
            votes.SetParticipants(new[] { 1, 3, 4 });
            Assert.That(votes.Count, Is.EqualTo(1));
            votes.SetParticipants(new[] { 1 });
            Assert.That(votes.Unanimous, Is.True);
            votes.SetParticipants(System.Array.Empty<int>());
            Assert.That(votes.Unanimous, Is.False);
            votes.SetParticipants(new[] { 2 });
            Assert.That(votes.HasVoted(2), Is.False);
        }

        [Test]
        public void EntryAndAssaultSkipsPreserveGameplayGatesAndAuthority()
        {
            var root = new GameObject("Skip sequence test");
            try
            {
                var session = root.AddComponent<GameSession>();
                session.Begin(null, null, Vector3.zero);
                var spawner = root.AddComponent<MeteorSpawner>();
                var sequence = root.AddComponent<Stage1Sequence>();
                bool authority = true;
                sequence.Configure(session, spawner, () => authority, () => 0d);
                Assert.That(sequence.TrySkip(0), Is.False);
                sequence.TryBeginEntry();
                int entry = sequence.Revision;
                authority = false;
                Assert.That(sequence.TrySkip(entry), Is.False);
                authority = true;
                Assert.That(sequence.TrySkip(entry - 1), Is.False);
                Assert.That(sequence.TrySkip(entry), Is.True);
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Exploration));
                Assert.That(session.CinematicInputBlocked, Is.False);
                Assert.That(spawner.SequenceHold, Is.False);
                Assert.That(spawner.AssaultEnabled, Is.False);
                sequence.TryBeginAssault();
                Assert.That(sequence.TrySkip(entry), Is.False);
                Assert.That(sequence.TrySkip(sequence.Revision), Is.True);
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Combat));
                Assert.That(spawner.AssaultEnabled, Is.True);
                Assert.That(sequence.AssaultEnabled, Is.True);
                Assert.That(sequence.TrySkip(sequence.Revision), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(1)]
        [TestCase(4)]
        public void EndingSkipsStopAtClearTimeAndPreserveItsFullDuration(int players)
        {
            var root = new GameObject("Ending skip test");
            try
            {
                double clock = 100;
                var ending = root.AddComponent<DemoEnding>();
                var snapshot = new DemoEndingSnapshot
                {
                    StartedAt = clock, PlayTimeSeconds = 3723,
                    Players = new DemoEndingPlayer[players], Parts = new DemoEndingPart[0]
                };
                ending.ConfigurePlayback(snapshot, () => clock);
                Assert.That(ending.TryGetSkipTarget(out float target), Is.True);
                Assert.That(target, Is.EqualTo(ending.BoardingEndsAt));
                clock = 100 + target;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.Launching));
                Assert.That(ending.TryGetSkipTarget(out target), Is.True);
                clock = 100 + target;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.Photograph));
                Assert.That(ending.TryGetSkipTarget(out target), Is.True);
                clock = 100 + target;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.EndTitle));
                Assert.That(ending.TryGetSkipTarget(out _), Is.False);
                clock += DemoEnding.EndTitleSeconds - .01;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.EndTitle));
                Assert.That(ending.TryGetSkipTarget(out _), Is.False);
                clock = 100 + ending.ArrivalStartsAt;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.SpaceDrift));
                Assert.That(ending.TryGetSkipTarget(out target), Is.True);
                clock = 100 + target;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.MeteorImpact));
                Assert.That(ending.TryGetSkipTarget(out target), Is.True);
                clock = 100 + target;
                Assert.That(ending.Stage, Is.EqualTo(DemoEndingStage.EmergencyDescent));
                Assert.That(ending.TryGetSkipTarget(out target), Is.True);
                clock = 100 + target;
                Assert.That(ending.Finished, Is.True);
                Assert.That(ending.TryGetSkipTarget(out _), Is.False);
                Assert.That(snapshot.PlayTimeSeconds, Is.EqualTo(3723));
                clock = 99;
                Assert.That(ending.TryGetSkipTarget(out _), Is.False, "Preparation cannot be skipped.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    // In-memory packets exercise Mirror's real framing, authentication and message dispatch.
    // This is not a substitute for four independent game processes over a real transport.
    public sealed class SkipTestTransport : Transport
    {
        bool _active;
        public void Vote(int connection, int revision)
        {
            var message = new NetworkWriter();
            NetworkMessages.Pack(new CutsceneSkipRequest { Revision = revision }, message);
            var batcher = new Batcher(1500);
            batcher.AddMessage(message.ToArraySegment(), NetworkTime.localTime);
            var batch = new NetworkWriter();
            Assert.That(batcher.GetBatch(batch), Is.True);
            OnServerDataReceived.Invoke(connection, batch.ToArraySegment(), Channels.Reliable);
        }
        public override bool Available() => true;
        public override bool ClientConnected() => false;
        public override void ClientConnect(string address) { }
        public override void ClientSend(System.ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ClientDisconnect() { }
        public override System.Uri ServerUri() => new System.Uri("memory://localhost");
        public override bool ServerActive() => _active;
        public override void ServerStart() => _active = true;
        public override void ServerSend(int connectionId, System.ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
        public override void ServerDisconnect(int connectionId) { }
        public override string ServerGetClientAddress(int connectionId) => "memory";
        public override void ServerStop() => _active = false;
        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 1500;
        public override void Shutdown() => _active = false;
    }
}
