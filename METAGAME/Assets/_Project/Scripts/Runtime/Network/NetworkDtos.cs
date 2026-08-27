using System;
using System.Collections.Generic;

namespace Metagame.Network
{
    [Serializable]
    public sealed class GuestLoginRequest
    {
        public string nickname;
        public string deviceId;
    }

    [Serializable]
    public sealed class GuestLoginResponse
    {
        public long userId;
        public string nickname;
        public string accessToken;
    }

    [Serializable]
    public sealed class OutgoingMessage
    {
        public string type;
        public OutgoingData data;
    }

    [Serializable]
    public sealed class OutgoingData
    {
        public string roomId;
        public bool ready;
    }

    [Serializable]
    public sealed class WebSocketEnvelope
    {
        public string type;
        public IncomingData data;
    }

    [Serializable]
    public sealed class IncomingData
    {
        public string roomId;
        public long hostUserId;
        public string status;
        public bool ready;
        public string message;
        public PlayerSummary[] players;
    }

    [Serializable]
    public sealed class PlayerSummary
    {
        public long userId;
        public bool ready;
    }

    public enum NetworkConnectionState
    {
        Disconnected,
        LoggingIn,
        Connecting,
        Connected,
        Failed
    }

    public sealed class PlayerSnapshot
    {
        public long UserId { get; }
        public bool Ready { get; }

        internal PlayerSnapshot(PlayerSummary source)
        {
            UserId = source.userId;
            Ready = source.ready;
        }
    }

    public sealed class RoomSnapshot
    {
        private readonly List<PlayerSnapshot> players;

        public string RoomId { get; }
        public long HostUserId { get; }
        public string Status { get; }
        public IReadOnlyList<PlayerSnapshot> Players => players;
        public bool IsWaiting => string.Equals(Status, "WAITING", StringComparison.OrdinalIgnoreCase);
        public bool AllReady
        {
            get
            {
                if (players.Count == 0) return false;
                for (int i = 0; i < players.Count; i++)
                    if (!players[i].Ready) return false;
                return true;
            }
        }

        internal RoomSnapshot(IncomingData source)
        {
            RoomId = source.roomId ?? string.Empty;
            HostUserId = source.hostUserId;
            Status = source.status ?? string.Empty;
            players = new List<PlayerSnapshot>();

            if (source.players == null) return;
            for (int i = 0; i < source.players.Length; i++)
            {
                if (source.players[i] != null)
                    players.Add(new PlayerSnapshot(source.players[i]));
            }
        }

        public bool TryGetPlayer(long userId, out PlayerSnapshot player)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].UserId == userId)
                {
                    player = players[i];
                    return true;
                }
            }

            player = null;
            return false;
        }
    }
}
