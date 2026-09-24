using System.Collections.Generic;
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

        /// <summary>
        /// 방에 들어가 있는 로비 세션을 세운다. <b>ROOM_STATE 를 받은 것과 같게</b> AI 수도 받아 둔다 —
        /// 실제 경로(<c>NetworkSession.HandleMessage</c>)가 방을 넣자마자 그렇게 한다.
        /// </summary>
        /// <param name="players">방의 사람 수. 방장(<paramref name="host"/>)부터 차례로 넣는다</param>
        /// <param name="aiCount">방이 싣고 온 AI 수. null 이면 그 필드가 없는 옛 서버다</param>
        internal static NetworkSession CreateLobby(string status = "WAITING", long host = 0,
            int players = 0, int? aiCount = 0)
        {
            Assert.That(NetworkSession.Instance == null, Is.True);
            var session = new GameObject("AI policy lobby fixture").AddComponent<NetworkSession>();
            if (!Application.isPlaying) typeof(NetworkSession).GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(session, null);
            var room = LobbyRoom(status, host, players, aiCount);
            typeof(NetworkSession).GetProperty(nameof(NetworkSession.CurrentRoom)).SetValue(session, room);
            AiHostSettings.AdoptRoom(room);
            return session;
        }

        internal static RoomSnapshot LobbyRoom(string status = "WAITING", long host = 0, int players = 0, int? aiCount = 0)
        {
            var map = new Dictionary<long, PlayerSummary>();
            for (int i = 0; i < players; i++) map[host + i] = new PlayerSummary { nickname = "사람 " + i };
            return RoomSnapshot.From(new IncomingData
            {
                roomId = "ai-policy-test", hostUserId = host, status = status, players = map, aiCount = aiCount
            });
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
                Assert.That(AiHostSettings.TrySetRequestedCount(1), Is.EqualTo(allowed));
                // 요청만 나간다. 답(ROOM_STATE)이 와야 바뀐다 — 서버가 줄이거나 거절할 수 있는데
                // 미리 바꾸면 이 PC 만 다른 수를 믿는다 (S15P21B202-713).
                Assert.That(AiHostSettings.RequestedCount, Is.Zero);
                Assert.That(AiHostSettings.Requested, Is.False);
                Assert.That(AiHostSettings.TrySetIdleTest(true), Is.EqualTo(allowed));
                Assert.That(AiHostSettings.IdleTest, Is.EqualTo(allowed));
            }
            finally { Object.DestroyImmediate(session.gameObject); }
        }

        /// <summary>옛 서버(aiCount 가 없는 ROOM_STATE)에는 묻지 않는다 — 모르는 메시지 오류만 돌아온다.</summary>
        [Test]
        public void AnOldServerIsNotAsked()
        {
            var session = CreateLobby(aiCount: null);
            try
            {
                Assert.That(AiHostSettings.CanConfigure, Is.True);
                Assert.That(AiHostSettings.TrySetRequestedCount(1), Is.False);
            }
            finally { Object.DestroyImmediate(session.gameObject); }
        }

        [TearDown]
        public void Cleanup()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.BeginRoom();
        }

        /// <summary>AI 수는 방이 정한다 — 받은 방 상태를 그대로 따른다. 방이 없거나 옛 서버면 0 이다.</summary>
        [Test]
        public void CountFollowsTheRoom()
        {
            AiHostSettings.AdoptRoom(LobbyRoom(players: 1, aiCount: 2));
            Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2));
            Assert.That(AiHostSettings.Requested, Is.True);

            AiHostSettings.AdoptRoom(LobbyRoom(players: 1, aiCount: null));
            Assert.That(AiHostSettings.RequestedCount, Is.Zero, "옛 서버의 방에서 AI 를 원한다고 본다");

            AiHostSettings.AdoptRoom(LobbyRoom(players: 1, aiCount: 1));
            AiHostSettings.AdoptRoom(null);
            Assert.That(AiHostSettings.RequestedCount, Is.Zero, "방을 떠났는데 AI 수가 남는다");
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

        /// <summary>
        /// 방장이 바뀌어도 수는 방에서 온 그대로다. 준비 상태는 <b>새 방장의 실행기</b>로 판단한다.
        ///
        /// <para>옛 방장의 상태(<c>previous</c>)로 수를 다시 짐작하지 않는다 — 두 사본은 어긋날 수
        /// 있고, 2맵처럼 WorldSync 가 없는 곳에서는 그 함수가 불리지도 않는다 (S15P21B202-713).</para>
        /// </summary>
        [Test]
        public void TakeoverKeepsRoomCountButEvaluatesNewHostsRuntime()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.AdoptRoom(LobbyRoom(players: 2, aiCount: 2));
            AiHostSettings.InheritRoomChoice(AiRoomState.Active);
            Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2));
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Missing));
            var runtime = new Runtime { State = AiRuntimeState.Loading };
            AiHostSettings.BindRuntime(runtime);
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Loading));
            runtime.State = AiRuntimeState.Ready;
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Active));

            AiHostSettings.InheritRoomChoice(AiRoomState.Off);
            Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2), "옛 방장의 상태가 방의 수를 덮었다");
            Assert.That(AiHostSettings.LocalState, Is.EqualTo(AiRoomState.Active));
            AiHostSettings.InheritRoomChoice(AiRoomState.IdleTest);
            Assert.That(AiHostSettings.IdleTest, Is.False);
        }

        [Test]
        public void NewRoomZeroesTheCountWithoutClaimingRuntimeReady()
        {
            AiHostSettings.BindRuntime(null);
            AiHostSettings.AdoptRoom(LobbyRoom(aiCount: 3));
            Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(3));
            AiHostSettings.BeginRoom();
            Assert.That(AiHostSettings.RequestedCount, Is.Zero);
            Assert.That(AiHostSettings.Requested, Is.False);
            Assert.That(AiHostSettings.IdleTest, Is.False);
            Assert.That(AiHostSettings.RuntimeState, Is.EqualTo(AiRuntimeState.Missing));
        }

        [Test]
        public void NoHostCannotChangeTheRoomOrProduceServerState()
        {
            AiHostSettings.BeginRoom();
            Assert.That(AiHostSettings.TrySetRequestedCount(1), Is.False);
            Assert.That(AiHostSettings.TrySetIdleTest(true), Is.False);
            AiHostSettings.BindRuntime(new Runtime { State = AiRuntimeState.Ready });
            Assert.That(AiHostSettings.ServerState, Is.EqualTo(AiRoomState.Unknown));
        }
    }
}
