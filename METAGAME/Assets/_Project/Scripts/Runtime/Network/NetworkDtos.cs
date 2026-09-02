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

        /// <summary>
        /// START_GAME 에서 방장이 자기 매치 주소를 알린다(S15P21B202-263).
        /// 방장의 클라이언트가 곧 게임 서버이므로, 참가자가 어디로 붙을지는
        /// 방장만 알 수 있다. 서버는 이 값을 GAME_STARTED 로 방 전원에게 전달한다.
        /// </summary>
        public string hostAddress;
    }

    /// <summary>PLAYER_MOVE — 내 위치를 서버에 알린다. 서버는 계산하지 않고 중계만 한다.</summary>
    public sealed class OutgoingMoveMessage
    {
        public string type;
        public MoveData data;
    }

    public sealed class MoveData
    {
        public float x;
        public float y;
        public float z;
        public float yaw;
    }

    /// <summary>PLAYER_MOVED — 다른 사람의 위치. 보낸 사람에게는 오지 않는다.</summary>
    public readonly struct PlayerMove
    {
        public long UserId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }

        public PlayerMove(long userId, float x, float y, float z, float yaw)
        {
            UserId = userId;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// 서버 → 클라이언트 메시지. players 가 userId 를 키로 하는 객체라(backend/docs/PROTOCOL.md)
    /// 사전을 다루지 못하는 JsonUtility 대신 Newtonsoft 로 읽는다.
    /// </summary>
    public sealed class WebSocketEnvelope
    {
        public string type;
        public IncomingData data;
    }

    public sealed class IncomingData
    {
        public string roomId;
        public long hostUserId;
        public string status;
        public bool ready;
        public string message;

        /// <summary>GAME_STARTED 에 실려 오는 방장의 매치 주소(-263).</summary>
        public string hostAddress;

        // PLAYER_MOVED 전용
        public long userId;
        public float x;
        public float y;
        public float z;
        public float yaw;

        /// <summary>키는 userId, 값은 닉네임과 준비 여부.</summary>
        public Dictionary<long, PlayerSummary> players;

        /// <summary>연결이 끊겨 유예 시간을 기다리는 중인 userId. players 에는 아직 남아 있다.</summary>
        public long[] disconnected;
    }

    public sealed class PlayerSummary
    {
        public string nickname;
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
        public string Nickname { get; }
        public bool Ready { get; }
        public bool Disconnected { get; }

        internal PlayerSnapshot(long userId, PlayerSummary source, bool disconnected)
        {
            UserId = userId;
            // 닉네임이 비어 오더라도 화면이 비지 않도록 userId 로 대신 표시한다.
            Nickname = string.IsNullOrWhiteSpace(source?.nickname)
                ? userId.ToString()
                : source.nickname;
            Ready = source != null && source.ready;
            Disconnected = disconnected;
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

        /// <summary>ROOM_STATE 페이로드를 화면에 쓸 형태로 바꾼다. null 이면 null 을 돌려준다.</summary>
        public static RoomSnapshot From(IncomingData source)
        {
            return source == null ? null : new RoomSnapshot(source);
        }

        internal RoomSnapshot(IncomingData source)
        {
            RoomId = source.roomId ?? string.Empty;
            HostUserId = source.hostUserId;
            Status = source.status ?? string.Empty;
            players = new List<PlayerSnapshot>();

            if (source.players == null) return;

            var offline = new HashSet<long>();
            if (source.disconnected != null)
                for (int i = 0; i < source.disconnected.Length; i++)
                    offline.Add(source.disconnected[i]);

            foreach (KeyValuePair<long, PlayerSummary> entry in source.players)
                players.Add(new PlayerSnapshot(entry.Key, entry.Value, offline.Contains(entry.Key)));

            // 사전은 순서를 보장하지 않는다. 방 목록이 갱신될 때마다 줄이 뒤바뀌지 않도록 고정한다.
            players.Sort((a, b) => a.UserId.CompareTo(b.UserId));
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
