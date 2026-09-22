using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiHostPolicyTests
    {
        sealed class Runtime : IAiRuntimeStatus
        {
            public AiRuntimeState State { get; set; }
        }

        internal static NetworkSession CreateLobby(string status = "WAITING", long host = 0)
        {
            Assert.That(NetworkSession.Instance == null, Is.True);
            var session = new GameObject("AI policy lobby fixture").AddComponent<NetworkSession>();
            if (!Application.isPlaying) typeof(NetworkSession).GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(session, null);
            var room = RoomSnapshot.From(new IncomingData { roomId = "ai-policy-test", hostUserId = host, status = status });
            typeof(NetworkSession).GetProperty(nameof(NetworkSession.CurrentRoom)).SetValue(session, room);
            return session;
        }

        [TestCase("WAITING", 0, true)]
        [TestCase("WAITING", 1, false)]
        [TestCase("PLAYING", 0, false)]
        [TestCase("", 0, false)]
        public void OnlyWaitingLobbyHostCanConfigure(string status, long host, bool allowed)
        {
            var session = CreateLobby(status, host);
            try
            {
                AiHostSettings.BeginRoom();
                Assert.That(AiHostSettings.CanConfigure, Is.EqualTo(allowed));
                Assert.That(AiHostSettings.TrySetRequested(true), Is.EqualTo(allowed));
                Assert.That(AiHostSettings.Requested, Is.EqualTo(allowed));
                Assert.That(AiHostSettings.TrySetIdleTest(true), Is.EqualTo(allowed));
                Assert.That(AiHostSettings.IdleTest, Is.EqualTo(allowed));
            }
            finally { Object.DestroyImmediate(session.gameObject); }
        }

        [TearDown]
        public void Cleanup()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.BeginRoom();
        }

        [TestCase(false, AiRuntimeState.Missing, AiRoomState.Off)]
        [TestCase(false, AiRuntimeState.Loading, AiRoomState.Off)]
        [TestCase(false, AiRuntimeState.Ready, AiRoomState.Off)]
        [TestCase(false, AiRuntimeState.Failed, AiRoomState.Off)]
        [TestCase(true, AiRuntimeState.Missing, AiRoomState.Missing)]
        [TestCase(true, AiRuntimeState.Loading, AiRoomState.Loading)]
        [TestCase(true, AiRuntimeState.Ready, AiRoomState.Active)]
        [TestCase(true, AiRuntimeState.Failed, AiRoomState.Failed)]
        public void SelectionAndReadinessAreSeparate(bool enabled, AiRuntimeState runtime, AiRoomState expected)
        {
            Assert.That(AiHostPolicy.Evaluate(enabled, runtime, false), Is.EqualTo(expected));
            Assert.That(AiHostPolicy.CanFill(expected), Is.EqualTo(enabled && runtime == AiRuntimeState.Ready));
        }

        [Test]
        public void IdleTestIsNotReportedAsWorkingAi()
        {
            Assert.That(AiHostPolicy.Evaluate(false, AiRuntimeState.Missing, true), Is.EqualTo(AiRoomState.IdleTest));
            Assert.That(AiHostPolicy.Label(AiRoomState.IdleTest), Does.Contain("명령 수행 없음"));
        }

        [Test]
        public void TakeoverCopiesChoiceButEvaluatesNewHostsRuntime()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.InheritRoomChoice(AiRoomState.Active);
            Assert.That(AiHostSettings.Requested, Is.True);
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Missing));
            var runtime = new Runtime { State = AiRuntimeState.Loading };
            AiHostSettings.BindRuntime(runtime);
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Loading));
            runtime.State = AiRuntimeState.Ready;
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Active));
            AiHostSettings.InheritRoomChoice(AiRoomState.Off);
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Off));
            AiHostSettings.InheritRoomChoice(AiRoomState.IdleTest);
            Assert.That(AiHostSettings.IdleTest, Is.False);
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Off));
        }

        [Test]
        public void NewRoomResetsSelectionWithoutClaimingRuntimeReady()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.InheritRoomChoice(AiRoomState.Active);
            AiHostSettings.BeginRoom();
            Assert.That(AiHostSettings.Requested, Is.False);
            Assert.That(AiHostSettings.IdleTest, Is.False);
            Assert.That(AiHostSettings.RuntimeState, Is.EqualTo(AiRuntimeState.Missing));
        }

        [Test]
        public void NoHostCannotChangeTheRoomOrProduceServerState()
        {
            AiHostSettings.BeginRoom();
            Assert.That(AiHostSettings.TrySetRequested(true), Is.False);
            Assert.That(AiHostSettings.TrySetIdleTest(true), Is.False);
            AiHostSettings.BindRuntime(new Runtime { State = AiRuntimeState.Ready });
            Assert.That(AiHostSettings.ServerState, Is.EqualTo(AiRoomState.Unknown));
        }
    }
}
