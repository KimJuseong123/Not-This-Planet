using NUnit.Framework;
using UnityEngine;
using Metagame.Network;

namespace Metagame.Network.Tests
{
    public sealed class NetworkSerializationTests
    {
        [Test]
        public void RoomStateParsesPlayerArray()
        {
            const string json = "{\"type\":\"ROOM_STATE\",\"data\":{\"roomId\":\"abc123\",\"hostUserId\":1,\"status\":\"WAITING\",\"players\":[{\"userId\":1,\"ready\":true},{\"userId\":2,\"ready\":false}]}}";

            WebSocketEnvelope envelope = JsonUtility.FromJson<WebSocketEnvelope>(json);

            Assert.That(envelope, Is.Not.Null);
            Assert.That(envelope.type, Is.EqualTo("ROOM_STATE"));
            Assert.That(envelope.data.roomId, Is.EqualTo("abc123"));
            Assert.That(envelope.data.hostUserId, Is.EqualTo(1));
            Assert.That(envelope.data.players, Has.Length.EqualTo(2));
            Assert.That(envelope.data.players[0].ready, Is.True);
            Assert.That(envelope.data.players[1].userId, Is.EqualTo(2));
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
