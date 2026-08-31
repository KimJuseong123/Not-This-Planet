using NUnit.Framework;
using UnityEngine;
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
    }
}
