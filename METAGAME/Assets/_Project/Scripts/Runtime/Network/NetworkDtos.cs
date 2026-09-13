using System;
using System.Collections.Generic;

namespace Metagame.Network
{
    [Serializable]
    public sealed class GuestLoginRequest
    {
        public string nickname;
        public string deviceId;

        /// <summary>
        /// 이 빌드의 릴리스 버전 — 서버가 구버전을 막는 데 쓴다 (S15P21B202-404).
        /// 릴리스 빌드가 아니면 빈 문자열이고, 그것을 어떻게 볼지는 서버가 정한다.
        /// </summary>
        public string clientVersion;
    }

    [Serializable]
    public sealed class GuestLoginResponse
    {
        public long userId;
        public string nickname;
        public string accessToken;
    }

    /// <summary>
    /// 버전이 낮아 거절당했을 때 서버가 주는 본문 (S15P21B202-404, HTTP 426).
    /// <b>받을 곳까지 온다</b> — 사유만 보여 주면 사용자는 무엇을 해야 하는지 모른다.
    /// </summary>
    [Serializable]
    public sealed class ClientOutdatedResponse
    {
        public string message;
        public string downloadUrl;
        public string minVersion;
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

        /// <summary>
        /// 방을 만들거나 들어올 때 자기 랜 주소를 알린다(S15P21B202-264).
        ///
        /// 서버는 공인 IP 만으로 같은 망인지 판단했는데, 규모가 큰 망은 공인 IP 를
        /// 여러 개 돌려써서 같은 WiFi 에 앉아 있어도 서로 달라 보인다. 그러면 릴레이를
        /// 태우게 되고, 릴레이는 공인 IP 로 사람을 알아보므로 그마저 실패한다
        /// (2026-09-02 SSAFY 교육장 실측). 랜 주소를 알려 주면 그 자리에서 갈린다.
        /// </summary>
        public string localAddress;
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

    /// <summary>
    /// 릴레이 세션 한 벌(S15P21B202-264). Edgegap 응답을 서버가 이 모양으로 옮겨 준다.
    ///
    /// 주의: Edgegap 의 session_id 는 문자열이고 여기 sessionToken 은 숫자다 — 서로 다른
    /// 것이다. 전송에 넣는 값은 세션 단위 authorization_token 이다(-145 §3-8-1).
    /// </summary>
    [Serializable]
    public sealed class RelayData
    {
        public string address;
        public int serverPort;
        public int clientPort;
        public long sessionToken;
        public long userToken;
    }

    public sealed class IncomingData
    {
        public string roomId;
        public long hostUserId;
        public string status;
        public bool ready;
        public string message;

        /// <summary>
        /// ERROR 가 실어 오는 사유 코드 (S15P21B202-413). 없으면 빈 문자열이다.
        ///
        /// <para>화면에 보일 문구는 <b>화면을 가진 쪽</b>이 고른다 — 그래야 말을 다듬을 때
        /// 서버를 다시 배포하지 않는다. 모르는 코드가 오면 <see cref="message"/> 를 그대로 쓴다.</para>
        /// </summary>
        public string reason;

        /// <summary>GAME_STARTED 에 실려 오는 방장의 매치 주소(-263).</summary>
        public string hostAddress;

        /// <summary>
        /// GAME_STARTED 에 실려 오는 릴레이 세션(-264). 없으면 방장에게 직접 붙는다.
        /// 사람마다 userToken 이 다르므로 이 메시지는 방송이 아니라 각자에게 따로 온다.
        /// </summary>
        public RelayData relay;

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

        /// <summary>판이 돌고 있는가. 호스트 이전은 이때만 일어난다 (S15P21B202-389).</summary>
        public bool IsPlaying => string.Equals(Status, "PLAYING", StringComparison.OrdinalIgnoreCase);
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
