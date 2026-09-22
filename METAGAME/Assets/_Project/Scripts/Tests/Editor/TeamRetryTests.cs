using System.Collections;
using Metagame.Core;
using Metagame.Network;
using Metagame.Gameplay.Hazards;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class TeamRetryTests
    {
        [UnityTest, Category("Heavy")]
        public IEnumerator ServerVotesKeepConnectionsAndRequireFreshConsentAfterRestart()
        {
            yield return new EnterPlayMode();
            var previousTransport = Transport.active;
            var network = new GameObject("Retry test network");
            var transport = network.AddComponent<RetryTestTransport>();
            Transport.active = transport;
            var manager = network.AddComponent<RetryTestManager>();
            var stage = SceneManager.CreateScene(StageCatalog.Stage1SceneName);
            var root = new GameObject("Retry test session");
            SceneManager.MoveGameObjectToScene(root, stage);
            var session = root.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var sequence = root.AddComponent<Stage1Sequence>();
            var spawner = root.AddComponent<MeteorSpawner>();
            spawner.enabled = false;
            sequence.Configure(session, spawner, () => NetworkServer.active, () => 0d);
            NetworkServer.Listen(8);
            for (int i = 1; i <= 4; i++) AddPlayer(i);
            var flow = TeamRetryFlow.Ensure();
            try
            {
                yield return null;
                string ballot = flow.State.Ballot;
                Assert.That(flow.State.Total, Is.EqualTo(4), "AI seats must not be counted.");
                Assert.That(flow.State.Available, Is.True);
                session.SetInputSuppressed(true);
                transport.Vote(1, ballot);
                transport.Vote(1, ballot);
                transport.Vote(2, "previous-run");
                Assert.That(flow.State.Votes, Is.EqualTo(1));
                Assert.That(session.InputSuppressed, Is.True, "Another player's request must not close my menu.");
                NetworkServer.connections[2].isReady = false;
                transport.Vote(2, ballot);
                Assert.That(flow.State.Votes, Is.EqualTo(1));
                NetworkServer.connections[2].isReady = true;
                transport.Vote(2, ballot);
                transport.Vote(3, ballot);
                Assert.That(manager.Changes, Is.Zero, "3/4 is not unanimous.");

                // 새 참가자는 준비 중이어도 분모에 포함된다.
                var fifth = new NetworkConnectionToClient(5) { isAuthenticated = true };
                NetworkServer.AddConnection(fifth);
                yield return null;
                Assert.That(flow.State.Total, Is.EqualTo(5));
                transport.Vote(4, ballot);
                Assert.That(manager.Changes, Is.Zero);
                NetworkServer.RemoveConnection(5);
                RejoinMemory.Remember(99, Vector3.one * 50, Vector3.forward);
                yield return null;
                Assert.That(manager.Changes, Is.EqualTo(1));
                Assert.That(manager.Target, Is.EqualTo(TeamRetryFlow.Stage1Path));
                Assert.That(NetworkServer.connections.Count, Is.EqualTo(4));
                foreach (var connection in NetworkServer.connections.Values)
                    Assert.That(connection.identity, Is.Null, "Ready creates a fresh body in the new stage.");
                Assert.That(RejoinMemory.Count, Is.Zero);
                Assert.That(flow.State.Restarting, Is.True);
                Assert.That(flow.RequestRetry(), Is.False);
                var endingFlow = Metagame.Bootstrap.EndingSceneFlow.Ensure();
                endingFlow.Begin(new Metagame.Gameplay.Parts.DemoEndingSnapshot());
                Assert.That(endingFlow.IsTransferring, Is.False, "An ending cannot overtake the committed retry.");
                Assert.That(manager.Changes, Is.EqualTo(1));
                Object.DestroyImmediate(endingFlow.gameObject);

                // 같은 네트워크 세션의 다음 판. 지난 표/패킷은 새 판에서 무효다.
                Object.DestroyImmediate(root);
                root = new GameObject("Next retry session");
                SceneManager.MoveGameObjectToScene(root, stage);
                session = root.AddComponent<GameSession>();
                session.Begin(null, null, Vector3.zero);
                NetworkServer.isLoadingScene = false;
                for (int i = 1; i <= 4; i++) AddBody(NetworkServer.connections[i]);
                yield return null;
                Assert.That(flow.State.Ballot, Is.Not.EqualTo(ballot));
                Assert.That(flow.State.Votes, Is.Zero);
                transport.Vote(1, ballot);
                Assert.That(flow.State.Votes, Is.Zero);
                ballot = flow.State.Ballot;
                transport.Vote(1, ballot);

                // 연결 ID를 재사용해도 새 연결에는 기존 표가 없다.
                NetworkServer.RemovePlayerForConnection(NetworkServer.connections[1], RemovePlayerOptions.Destroy);
                NetworkServer.RemoveConnection(1);
                AddPlayer(1);
                yield return null;
                Assert.That(flow.State.Votes, Is.Zero);
                transport.Vote(2, ballot); transport.Vote(3, ballot); transport.Vote(4, ballot);
                Assert.That(manager.Changes, Is.EqualTo(1));
                transport.Vote(1, ballot);
                Assert.That(manager.Changes, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(flow.gameObject);
                NetworkServer.isLoadingScene = false;
                NetworkServer.Shutdown();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(network);
                Transport.active = previousTransport;
                RejoinMemory.Clear();
            }
            yield return SceneManager.UnloadSceneAsync(stage);
            yield return new ExitPlayMode();
        }

        static void AddPlayer(int id)
        {
            var connection = new NetworkConnectionToClient(id) { isAuthenticated = true, isReady = true };
            NetworkServer.AddConnection(connection);
            AddBody(connection);
        }

        static void AddBody(NetworkConnectionToClient connection)
        {
            connection.isReady = true;
            var body = new GameObject("Retry network body");
            body.AddComponent<NetworkIdentity>();
            NetworkServer.AddPlayerForConnection(connection, body);
        }

        [UnityTest, Category("Heavy")]
        public IEnumerator MessagesRoundTripAndSoloMenuDoesNotPauseWorldTime()
        {
            yield return new EnterPlayMode();
            var writer = new NetworkWriter();
            writer.Write(new TeamRetryState { Ballot = "round-2", Votes = 3, Total = 4,
                Available = true, Voted = true, Restarting = false });
            var state = new NetworkReader(writer.ToArraySegment()).Read<TeamRetryState>();
            Assert.That(state.Ballot, Is.EqualTo("round-2"));
            Assert.That(state.Votes, Is.EqualTo(3));
            Assert.That(state.Total, Is.EqualTo(4));
            Assert.That(state.Available && state.Voted && !state.Restarting, Is.True);
            writer.Reset();
            writer.Write(new TeamRetryRequest { Ballot = "round-2" });
            Assert.That(new NetworkReader(writer.ToArraySegment()).Read<TeamRetryRequest>().Ballot, Is.EqualTo("round-2"));

            var stage = SceneManager.CreateScene(StageCatalog.Stage1SceneName);
            var root = new GameObject("Solo retry menu");
            SceneManager.MoveGameObjectToScene(root, stage);
            var session = root.AddComponent<GameSession>();
            session.Begin(null, null, Vector3.zero);
            var menu = root.AddComponent<Metagame.UI.GamePauseMenu>();
            menu.Initialize(session);
            var flow = TeamRetryFlow.Ensure();
            yield return null;
            Assert.That(flow.State.Total, Is.EqualTo(1));
            Assert.That(menu.TryToggle(), Is.True);
            Assert.That(session.InputSuppressed, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(flow.CanVote, Is.True, "The menu must allow retry while movement is suppressed.");
            Assert.That(menu.TryToggle(), Is.True);
            Assert.That(session.InputSuppressed, Is.False);
            Assert.That(session.CursorLocked, Is.True);
            session.ScriptedInput = true;
            yield return null;
            Assert.That(flow.CanVote, Is.False);
            Assert.That(flow.RequestRetry(), Is.False);
            Object.DestroyImmediate(flow.gameObject);
            Object.DestroyImmediate(root);
            yield return SceneManager.UnloadSceneAsync(stage);
            yield return new ExitPlayMode();
        }

        [Test]
        public void NewStageStartsBeforeAssaultWithFreshMeteorSchedule()
        {
            var root = new GameObject("Old assault run");
            try
            {
                var session = root.AddComponent<GameSession>();
                session.Begin(null, null, Vector3.zero);
                var spawner = root.AddComponent<MeteorSpawner>();
                var sequence = root.AddComponent<Stage1Sequence>();
                sequence.Configure(session, spawner, () => true, () => 0d);
                sequence.TryBeginEntry();
                sequence.TrySkip(sequence.Revision);
                sequence.TryBeginAssault();
                sequence.TrySkip(sequence.Revision);
                Assert.That(sequence.AssaultEnabled && spawner.AssaultEnabled, Is.True);
                Object.DestroyImmediate(root);
                root = new GameObject("New stage run");
                session = root.AddComponent<GameSession>();
                session.Begin(null, null, Vector3.zero);
                spawner = root.AddComponent<MeteorSpawner>();
                sequence = root.AddComponent<Stage1Sequence>();
                sequence.Configure(session, spawner, () => true, () => 100d);
                Assert.That(sequence.Phase, Is.EqualTo(Stage1SequencePhase.Waiting));
                Assert.That(sequence.AssaultEnabled || spawner.AssaultEnabled, Is.False);
                Assert.That(spawner.SequenceHold, Is.True);
                Assert.That(session.Elapsed, Is.Zero);
                Assert.That(session.CheckpointIndex, Is.Zero);
                Assert.That(session.InputSuppressed, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    public sealed class RetryTestManager : NetworkManager
    {
        public int Changes;
        public string Target;
        public override void ServerChangeScene(string newSceneName)
        {
            Changes++;
            Target = newSceneName;
            NetworkServer.SetAllClientsNotReady();
            NetworkServer.isLoadingScene = true;
        }
    }

    public sealed class RetryTestTransport : Transport
    {
        bool _active;
        public void Vote(int connection, string ballot)
        {
            var message = new NetworkWriter();
            NetworkMessages.Pack(new TeamRetryRequest { Ballot = ballot }, message);
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
