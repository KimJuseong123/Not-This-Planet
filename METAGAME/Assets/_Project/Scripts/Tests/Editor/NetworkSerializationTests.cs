using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Network;

namespace Metagame.Network.Tests
{
    public sealed class NetworkSerializationTests
    {
        // backend/docs/PROTOCOL.md 의 ROOM_STATE 예시 형태. players 는 배열이 아니라
        // userId 를 키로 하는 객체다.
        private const string RoomStateJson =
            "{\"type\":\"ROOM_STATE\",\"data\":{" +
            "\"roomId\":\"K7M2QX\",\"hostUserId\":1,\"status\":\"WAITING\"," +
            "\"players\":{" +
            "\"2\":{\"nickname\":\"영희\",\"ready\":false}," +
            "\"1\":{\"nickname\":\"철수\",\"ready\":true}," +
            "\"3\":{\"nickname\":\"민수\",\"ready\":false}}," +
            "\"disconnected\":[3]}}";

        [Test]
        public void RoomTimerSurvivesStateRefreshAndResetsWhenHostChanges()
        {
            var go = new GameObject("Room timer test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", flags);
                var timer = typeof(NetworkSession).GetField("roomTimerStartedAt", flags);
                handle.Invoke(session, new object[] { "{\"type\":\"ROOM_CREATED\",\"data\":{\"roomId\":\"K7M2QX\"}}" });
                handle.Invoke(session, new object[] { RoomStateJson });
                timer.SetValue(session, 0d);
                handle.Invoke(session, new object[] { RoomStateJson });
                Assert.That(timer.GetValue(session), Is.EqualTo(0d));
                Assert.That(session.RoomPlayTimeSeconds, Is.GreaterThan(0d));
                handle.Invoke(session, new object[] { RoomStateJson.Replace("\"hostUserId\":1", "\"hostUserId\":2") });
                Assert.That(session.RoomPlayTimeSeconds, Is.LessThan(1d));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RoomStateParsesPlayerMap()
        {
            WebSocketEnvelope envelope = NetworkSession.ParseEnvelope(RoomStateJson);

            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope.type, Is.EqualTo("ROOM_STATE"));
            Assert.That(envelope.data.roomId, Is.EqualTo("K7M2QX"));
            Assert.That(envelope.data.hostUserId, Is.EqualTo(1));
            Assert.That(envelope.data.players, Has.Count.EqualTo(3));
            Assert.That(envelope.data.players[1].nickname, Is.EqualTo("철수"));
            Assert.That(envelope.data.players[1].ready, Is.True);
            Assert.That(envelope.data.players[2].ready, Is.False);
            Assert.That(envelope.data.disconnected, Is.EqualTo(new long[] { 3 }));
        }

        [Test]
        public void RoomSnapshotExposesNicknamesInUserIdOrder()
        {
            RoomSnapshot room = RoomSnapshot.From(NetworkSession.ParseEnvelope(RoomStateJson).data);

            Assert.That(room.Players, Has.Count.EqualTo(3));
            // 사전은 순서를 보장하지 않으므로 userId 순으로 고정되어야 한다.
            Assert.That(room.Players[0].UserId, Is.EqualTo(1));
            Assert.That(room.Players[0].Nickname, Is.EqualTo("철수"));
            Assert.That(room.Players[1].Nickname, Is.EqualTo("영희"));
            Assert.That(room.Players[2].Nickname, Is.EqualTo("민수"));
            Assert.That(room.IsWaiting, Is.True);
            Assert.That(room.AllReady, Is.False);
        }

        [Test]
        public void DisconnectedPlayerIsMarkedButStaysInList()
        {
            RoomSnapshot room = RoomSnapshot.From(NetworkSession.ParseEnvelope(RoomStateJson).data);

            PlayerSnapshot player;
            Assert.That(room.TryGetPlayer(3, out player), Is.True);
            Assert.That(player.Disconnected, Is.True);
            Assert.That(room.Players[0].Disconnected, Is.False);
        }

        [Test]
        public void MissingNicknameFallsBackToUserId()
        {
            const string json =
                "{\"type\":\"ROOM_STATE\",\"data\":{\"roomId\":\"A\",\"hostUserId\":7,\"status\":\"WAITING\"," +
                "\"players\":{\"7\":{\"nickname\":\"\",\"ready\":false}}}}";

            RoomSnapshot room = RoomSnapshot.From(NetworkSession.ParseEnvelope(json).data);

            Assert.That(room.Players[0].Nickname, Is.EqualTo("7"));
        }

        [Test]
        public void EmptyPlayerMapIsNotAllReady()
        {
            const string json =
                "{\"type\":\"ROOM_STATE\",\"data\":{\"roomId\":\"A\",\"hostUserId\":1,\"status\":\"WAITING\"," +
                "\"players\":{}}}";

            RoomSnapshot room = RoomSnapshot.From(NetworkSession.ParseEnvelope(json).data);

            Assert.That(room.Players, Is.Empty);
            Assert.That(room.AllReady, Is.False);
        }

        [Test]
        public void ErrorMessageIsRead()
        {
            WebSocketEnvelope envelope =
                NetworkSession.ParseEnvelope("{\"type\":\"ERROR\",\"data\":{\"message\":\"room is full\"}}");

            Assert.That(envelope.type, Is.EqualTo("ERROR"));
            Assert.That(envelope.data.message, Is.EqualTo("room is full"));
        }

        [Test]
        public void PlayerMovedIsParsed()
        {
            WebSocketEnvelope envelope = NetworkSession.ParseEnvelope(
                "{\"type\":\"PLAYER_MOVED\",\"data\":{\"userId\":2,\"x\":1.5,\"y\":0.0,\"z\":-3.25,\"yaw\":90.0}}");

            Assert.That(envelope.type, Is.EqualTo("PLAYER_MOVED"));
            Assert.That(envelope.data.userId, Is.EqualTo(2));
            Assert.That(envelope.data.x, Is.EqualTo(1.5f));
            Assert.That(envelope.data.z, Is.EqualTo(-3.25f));
            Assert.That(envelope.data.yaw, Is.EqualTo(90f));
        }

        [Test]
        public void MovePayloadCarriesProtocolFields()
        {
            string payload = NetworkSession.BuildMovePayload(1.5f, 0f, -3.25f, 90f);

            // backend/docs/PROTOCOL.md: { "type": "PLAYER_MOVE", "data": { x, y, z, yaw } }
            Assert.That(payload, Does.Contain("\"type\":\"PLAYER_MOVE\""));
            Assert.That(payload, Does.Contain("\"x\":1.5"));
            Assert.That(payload, Does.Contain("\"z\":-3.25"));
            Assert.That(payload, Does.Contain("\"yaw\":90"));
        }

        [Test]
        public void OutgoingReadyMessageUsesExpectedEnvelope()
        {
            string json = JsonUtility.ToJson(new OutgoingMessage
            {
                type = "READY",
                data = new OutgoingData { ready = true }
            });

            OutgoingMessage message = JsonUtility.FromJson<OutgoingMessage>(json);

            Assert.That(message.type, Is.EqualTo("READY"));
            Assert.That(message.data.ready, Is.True);
        }

        // ---------- AI 자리 · 내보내기 (S15P21B202-713) ----------

        // 사람 둘인 방. AI 는 둘까지 들어갈 수 있다. 끝을 열어 두고 aiCount 를 붙여 닫는다.
        private const string TwoPlayerRoomOpen =
            "{\"type\":\"ROOM_STATE\",\"data\":{" +
            "\"roomId\":\"K7M2QX\",\"hostUserId\":1,\"status\":\"WAITING\"," +
            "\"players\":{" +
            "\"1\":{\"nickname\":\"철수\",\"ready\":false}," +
            "\"2\":{\"nickname\":\"영희\",\"ready\":false}}";

        private static readonly System.Reflection.BindingFlags Hidden =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        [TearDown]
        public void ForgetTheRoomsAiCount() => AiHostSettings.BeginRoom();

        [Test]
        public void RoomStateCarriesTheAiCount()
        {
            RoomSnapshot room = RoomSnapshot.From(
                NetworkSession.ParseEnvelope(TwoPlayerRoomOpen + ",\"aiCount\":2}}").data);

            Assert.That(room.SupportsAi, Is.True);
            Assert.That(room.AiCount, Is.EqualTo(2));
        }

        /// <summary>필드가 없으면 옛 서버다 — 0 과 구분해야 옛 서버에 AI 요청을 보내지 않는다.</summary>
        [Test]
        public void AMissingAiCountMeansAnOldServer()
        {
            RoomSnapshot room = RoomSnapshot.From(NetworkSession.ParseEnvelope(TwoPlayerRoomOpen + "}}").data);

            Assert.That(room.SupportsAi, Is.False);
            Assert.That(room.AiCount, Is.Zero);
        }

        /// <summary>범위를 넘은 값은 줄이고 한 줄 남긴다 — 서버와 규칙이 갈렸다는 뜻이다.</summary>
        [TestCase(3, 2)]
        [TestCase(-1, 0)]
        public void AnOutOfRangeAiCountIsClamped(int sent, int expected)
        {
            LogAssert.Expect(LogType.Warning, new Regex("RoomSnapshot"));

            RoomSnapshot room = RoomSnapshot.From(
                NetworkSession.ParseEnvelope(TwoPlayerRoomOpen + ",\"aiCount\":" + sent + "}}").data);

            Assert.That(room.AiCount, Is.EqualTo(expected));
        }

        /// <summary>
        /// backend/docs/PROTOCOL.md: SET_AI_COUNT { aiCount } · KICK_PLAYER { targetUserId }.
        /// 보내는 함수가 쓰는 바로 그 본문을 본다.
        /// </summary>
        [Test]
        public void AiCountAndKickPayloadsCarryProtocolFields()
        {
            string set = NetworkSession.BuildAiCountPayload(2);
            Assert.That(set, Does.Contain("\"type\":\"SET_AI_COUNT\""));
            Assert.That(set, Does.Contain("\"aiCount\":2"));

            string kick = NetworkSession.BuildKickPayload(7);
            Assert.That(kick, Does.Contain("\"type\":\"KICK_PLAYER\""));
            Assert.That(kick, Does.Contain("\"targetUserId\":7"));
        }

        /// <summary>
        /// 방장이 아니어도 방의 AI 수를 받아 둔다 — 판 중에 방장을 이어받으면 이 값으로 AI 를 띄운다.
        /// 보낸 요청의 기다림은 답(ROOM_STATE)이든 거절(ERROR)이든 풀린다.
        /// </summary>
        [Test]
        public void EveryoneAdoptsTheRoomsAiCountAndAnswersReleaseThePendingRequest()
        {
            var go = new GameObject("AI count adopt test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", Hidden);
                var pending = typeof(NetworkSession).GetProperty(nameof(NetworkSession.PendingAiCount));

                pending.SetValue(session, (int?)2);
                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":2}}" });
                Assert.That(session.IsRoomHost, Is.False, "이 시험은 방장이 아닌 사람의 화면이다");
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2));
                Assert.That(session.PendingAiCount, Is.Null, "답이 왔는데 계속 처리 중이다");

                pending.SetValue(session, (int?)1);
                handle.Invoke(session, new object[] { "{\"type\":\"ERROR\",\"data\":{\"message\":\"cannot set ai count\",\"reason\":\"NOT_HOST\"}}" });
                Assert.That(session.PendingAiCount, Is.Null, "거절됐는데 계속 처리 중이다");
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2), "거절이 방의 수를 바꿨다");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// 내보내지면 방에서 나온 것으로 치고 이유를 말한다. 예전에는 모르는 메시지로 떨어져
        /// 방 화면이 그대로 남았다 — 더는 오지 않는 방송을 기다리며.
        /// </summary>
        [Test]
        public void KickedLeavesTheRoomAndSaysWhy()
        {
            var go = new GameObject("Kicked test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                string error = null;
                session.ErrorReceived += message => error = message;
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", Hidden);

                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":1}}" });
                Assert.That(session.CurrentRoom, Is.Not.Null);
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(1));

                handle.Invoke(session, new object[] { "{\"type\":\"KICKED\",\"data\":{\"roomId\":\"K7M2QX\"}}" });

                Assert.That(session.CurrentRoom, Is.Null);
                Assert.That(session.CurrentRoomId, Is.Empty);
                Assert.That(error, Is.EqualTo("방장이 방에서 내보냈습니다."));
                Assert.That(AiHostSettings.RequestedCount, Is.Zero, "방을 떠났는데 AI 수가 남는다");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// 방송은 순서 없이 닿는다 — <b>번호가 줄면 버린다</b> (S15P21B202-713 리뷰). 옛 aiCount 가 새 값을
        /// 덮으면 PC 마다 AI 수가 달라진 채 판이 시작된다(START_GAME 은 방 상태를 다시 싣지 않는다).
        /// </summary>
        [Test]
        public void AnOlderRoomStateDoesNotOverwriteANewerOne()
        {
            var go = new GameObject("State version test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", Hidden);

                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":2,\"stateVersion\":6}}" });
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2));

                LogAssert.Expect(LogType.Log, new Regex("방 상태를 버린다"));
                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":0,\"stateVersion\":5}}" });
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(2),
                    "늦게 온 옛 방송이 AI 수를 덮었다 — PC 마다 수가 갈린다");

                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":1,\"stateVersion\":7}}" });
                Assert.That(AiHostSettings.RequestedCount, Is.EqualTo(1), "새 방송을 안 받는다");

                // 번호가 없는 옛 서버는 늘 받는다 — 막으면 옛 서버와는 방 상태가 한 번만 들어온다.
                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":0}}" });
                Assert.That(AiHostSettings.RequestedCount, Is.Zero, "번호 없는 방송을 버렸다");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// <b>내가 없는 방 상태는 받지 않는다</b> (S15P21B202-713 리뷰). 내보내진 직후 그 전에 만들어진
        /// 방송이 늦게 닿으면 이미 나간 방이 화면에 되살아나고, 서버는 나를 그 방 사람으로 치지 않아
        /// 준비·시작이 조용히 무시된다.
        /// </summary>
        [Test]
        public void AStateWithoutMeDoesNotBringTheRoomBack()
        {
            var go = new GameObject("Stale state after kick test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", Hidden);
                typeof(NetworkSession).GetProperty(nameof(NetworkSession.UserId)).SetValue(session, 2L);

                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":0,\"stateVersion\":3}}" });
                Assert.That(session.CurrentRoom, Is.Not.Null, "검증 준비: 내가 있는 방송은 받아야 한다");
                handle.Invoke(session, new object[] { "{\"type\":\"KICKED\",\"data\":{\"roomId\":\"K7M2QX\"}}" });
                Assert.That(session.CurrentRoom, Is.Null);

                LogAssert.Expect(LogType.Log, new Regex("방 상태를 버린다"));
                string withoutMe = "{\"type\":\"ROOM_STATE\",\"data\":{\"roomId\":\"K7M2QX\",\"hostUserId\":1,"
                    + "\"status\":\"WAITING\",\"players\":{\"1\":{\"nickname\":\"철수\",\"ready\":false}},"
                    + "\"aiCount\":0,\"stateVersion\":4}}";
                handle.Invoke(session, new object[] { withoutMe });
                Assert.That(session.CurrentRoom, Is.Null, "내보내진 방이 늦은 방송으로 되살아났다");
                Assert.That(session.CurrentRoomId, Is.Empty);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary><see cref="NetworkSession.AcceptsRoomState"/> 의 표.</summary>
        [Test]
        public void RoomStateAcceptanceTable()
        {
            // 같은 방 · 번호가 늘면 받는다 / 같거나 줄면 버린다
            Assert.That(NetworkSession.AcceptsRoomState("R", 5, "R", 6, 2, true), Is.True);
            Assert.That(NetworkSession.AcceptsRoomState("R", 5, "R", 5, 2, true), Is.False, "같은 번호를 두 번 받는다");
            Assert.That(NetworkSession.AcceptsRoomState("R", 5, "r", 4, 2, true), Is.False, "방 코드 대소문자로 옛 방송이 샌다");
            // 다른 방이거나 방이 없으면 번호를 견주지 않는다
            Assert.That(NetworkSession.AcceptsRoomState("R", 9, "Q", 1, 2, true), Is.True);
            Assert.That(NetworkSession.AcceptsRoomState("", 9, "R", 1, 2, true), Is.True);
            // 옛 서버(번호 0)는 늘 받는다
            Assert.That(NetworkSession.AcceptsRoomState("R", 9, "R", 0, 2, true), Is.True);
            // 내가 없으면 버린다 — 로그인 전(0)에는 이 규칙을 쓰지 않는다
            Assert.That(NetworkSession.AcceptsRoomState("R", 0, "R", 7, 2, false), Is.False);
            Assert.That(NetworkSession.AcceptsRoomState("R", 0, "R", 7, 0, false), Is.True);
        }

        /// <summary>그 사이 다른 방에 들어갔으면 옛 방의 늦은 통지가 지금 방을 지우지 않는다.</summary>
        [Test]
        public void AKickFromAnotherRoomIsIgnored()
        {
            var go = new GameObject("Stale kick test");
            try
            {
                var session = go.AddComponent<NetworkSession>();
                var handle = typeof(NetworkSession).GetMethod("HandleMessage", Hidden);
                handle.Invoke(session, new object[] { TwoPlayerRoomOpen + ",\"aiCount\":0}}" });

                LogAssert.Expect(LogType.Warning, new Regex("NetworkSession"));
                handle.Invoke(session, new object[] { "{\"type\":\"KICKED\",\"data\":{\"roomId\":\"ZZZZZZ\"}}" });

                Assert.That(session.CurrentRoom, Is.Not.Null);
                Assert.That(session.CurrentRoomId, Is.EqualTo("K7M2QX"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase("NOT_HOST", "방장만 할 수 있습니다.")]
        [TestCase("NOT_WAITING", "게임 중에는 할 수 없습니다.")]
        [TestCase("BAD_TARGET", "내보낼 수 없는 대상입니다.")]
        public void AiAndKickRefusalsAreDescribed(string reason, string expected)
        {
            Assert.That(NetworkSession.DescribeError(reason, "fallback"), Is.EqualTo(expected));
        }
    }
}
