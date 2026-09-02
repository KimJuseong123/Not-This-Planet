using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Metagame.Network
{
    /// <summary>
    /// REST 로그인과 로비 WebSocket의 수명 및 상태를 씬과 분리해 보관한다.
    /// WebSocket의 백그라운드 수신 결과는 큐를 거쳐 Unity 메인 스레드에서만 반영한다.
    /// </summary>
    public sealed class NetworkSession : MonoBehaviour
    {
        public static NetworkSession Instance { get; private set; }

        public NetworkConnectionState State { get; private set; } = NetworkConnectionState.Disconnected;
        public bool IsLoggedIn => !string.IsNullOrEmpty(accessToken);

        /// <summary>
        /// 게임 서버에 접속할 때 신원 증명으로 넘긴다(S15P21B202-161).
        /// 화면에 찍거나 로그에 남기지 말 것 — 이 값이면 이 사람 행세를 할 수 있다.
        /// </summary>
        public string AccessToken => accessToken;
        public bool IsConnected => State == NetworkConnectionState.Connected;
        public long UserId { get; private set; }
        public string Nickname { get; private set; } = string.Empty;
        public string CurrentRoomId { get; private set; } = string.Empty;
        public RoomSnapshot CurrentRoom { get; private set; }
        public bool IsLocalReady { get; private set; }
        public string ServerAddress { get; private set; } = DefaultServerAddress;

        /// <summary>
        /// 이 매치를 어디로 붙을지. GAME_STARTED 가 실어 준 방장의 주소다(-263).
        /// 비어 있으면 예전처럼 로비와 같은 호스트로 붙는다(전용 서버 경로).
        /// </summary>
        public string MatchAddress { get; private set; } = string.Empty;

        /// <summary>내가 이 방의 방장인가. 방장의 클라이언트가 게임 서버를 켠다(-263).</summary>
        public bool IsRoomHost => CurrentRoom != null && CurrentRoom.HostUserId == UserId;

        /// <summary>
        /// 이 매치에 쓸 릴레이 세션(-264). 값이 없으면 방장에게 직접 붙는다.
        /// 서버가 GAME_STARTED 로 각자에게 따로 보내 준다 — 사람마다 userToken 이 다르다.
        /// </summary>
        public MatchTransport.Relay? MatchRelay { get; private set; }

        /// 배포 서버. 포트를 적지 않으므로 https(443)·wss 로 붙는다.
        /// 로컬 개발은 로비 입력칸에 "127.0.0.1:8080" 을 넣으면 된다.
        public const string DefaultServerAddress = "j15b202.p.ssafy.io";

        private const string ServerAddressPrefsKey = "metagame.server-address";
        private const int DefaultServerPort = 8080;
        private const int SecurePort = 443;

        public event Action<NetworkConnectionState> ConnectionStateChanged;
        public event Action<RoomSnapshot> RoomStateChanged;
        public event Action GameStarted;
        public event Action<PlayerMove> PlayerMoved;
        public event Action<string> ErrorReceived;
        public event Action ConnectionClosed;

        [SerializeField] private string httpBaseUrl = "https://j15b202.p.ssafy.io:443";
        [SerializeField] private string webSocketUrl = "wss://j15b202.p.ssafy.io:443/ws/game";

        private string accessToken;
        private Coroutine loginCoroutine;
        private ClientWebSocket socket;
        private CancellationTokenSource socketCancellation;
        private Task connectTask;
        // 연결 세대 번호. CloseSocket()·재연결마다 올라간다. 백그라운드에서 늦게 끝난 옛 ConnectAsync 가
        // 새 소켓을 덮어쓰던 경합을 막는다(2026-08-27 전수 AI 리뷰 지적, S15P21B202-136).
        private int connectGeneration;
        // 세대 검사와 socket 대입 사이에 CloseSocket() 이 끼어들면 닫은 뒤에 옛 소켓이 다시 꽂힌다.
        // 그 창을 막기 위해 대입과 종료를 같은 잠금 안에서 한다(MR !80 리뷰 지적).
        private readonly object socketGate = new object();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<NetworkEvent> pendingEvents = new ConcurrentQueue<NetworkEvent>();
        private bool shuttingDown;

        private enum NetworkEventType
        {
            Connected,
            Text,
            Failed,
            Closed
        }

        private sealed class NetworkEvent
        {
            public NetworkEventType Type;
            public string Text;

            public NetworkEvent(NetworkEventType type, string text = null)
            {
                Type = type;
                Text = text;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void Update()
        {
            int processed = 0;
            NetworkEvent pending;
            while (processed++ < 32 && pendingEvents.TryDequeue(out pending))
                HandleEvent(pending);
        }

        public void Configure(string httpUrl, string webSocketUrlOverride)
        {
            if (!string.IsNullOrWhiteSpace(httpUrl))
                httpBaseUrl = httpUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(webSocketUrlOverride))
                webSocketUrl = webSocketUrlOverride.TrimEnd('/');
        }

        public void LoginGuest(string nickname, string deviceId)
        {
            if (State == NetworkConnectionState.LoggingIn || State == NetworkConnectionState.Connecting)
                return;

            nickname = (nickname ?? string.Empty).Trim();
            deviceId = (deviceId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(nickname))
            {
                Fail("닉네임을 입력해 주세요.");
                return;
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                Fail("기기 식별자를 만들 수 없습니다.");
                return;
            }

            if (loginCoroutine != null)
                StopCoroutine(loginCoroutine);
            loginCoroutine = StartCoroutine(LoginGuestRoutine(nickname, deviceId));
        }

        private IEnumerator LoginGuestRoutine(string nickname, string deviceId)
        {
            SetState(NetworkConnectionState.LoggingIn);

            var payload = JsonUtility.ToJson(new GuestLoginRequest
            {
                nickname = nickname,
                deviceId = deviceId
            });

            using (var request = new UnityWebRequest(httpBaseUrl + "/auth/guest", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    loginCoroutine = null;
                    Fail("로그인 실패: " + request.error);
                    yield break;
                }

                GuestLoginResponse response;
                try
                {
                    response = JsonUtility.FromJson<GuestLoginResponse>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                    loginCoroutine = null;
                    Fail("로그인 응답을 읽을 수 없습니다.");
                    yield break;
                }

                if (response == null || response.userId <= 0 || string.IsNullOrEmpty(response.accessToken))
                {
                    loginCoroutine = null;
                    Fail("로그인 응답이 올바르지 않습니다.");
                    yield break;
                }

                UserId = response.userId;
                Nickname = response.nickname ?? nickname;
                accessToken = response.accessToken;
                loginCoroutine = null;
            }

            Connect();
        }

        public void Connect()
        {
            if (!IsLoggedIn)
            {
                Fail("먼저 게스트 로그인을 해 주세요.");
                return;
            }

            if (State == NetworkConnectionState.Connecting || State == NetworkConnectionState.Connected)
                return;

            CloseSocket();
            socketCancellation = new CancellationTokenSource();
            SetState(NetworkConnectionState.Connecting);
            int generation = Volatile.Read(ref connectGeneration);
            connectTask = ConnectSocketAsync(accessToken, socketCancellation.Token, generation);
        }

        private async Task ConnectSocketAsync(string token, CancellationToken cancellationToken, int generation)
        {
            var client = new ClientWebSocket();
            try
            {
                var uri = new Uri(webSocketUrl + "?token=" + UnityWebRequest.EscapeURL(token));
                await client.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                bool adopted;
                lock (socketGate)
                {
                    // 내 세대가 지났다 = 그 사이 끊거나 다시 연결했다. 이 소켓을 socket 에 넣으면
                    // 새 연결을 덮어써 조용히 옛 소켓으로 통신하게 된다.
                    adopted = !cancellationToken.IsCancellationRequested && !shuttingDown
                              && generation == Volatile.Read(ref connectGeneration);
                    if (adopted) socket = client;
                }

                if (!adopted)
                {
                    client.Dispose();
                    return;
                }

                pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Connected));
                _ = ReceiveLoopAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
            }
            catch (Exception exception)
            {
                client.Dispose();
                if (!cancellationToken.IsCancellationRequested && !shuttingDown)
                    pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Failed, "서버 연결 실패: " + exception.Message));
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                                .ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Closed));
                                return;
                            }

                            message.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        pendingEvents.Enqueue(new NetworkEvent(
                            NetworkEventType.Text,
                            Encoding.UTF8.GetString(message.ToArray())));
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                    pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Closed));
            }
            catch (OperationCanceledException)
            {
                // 정상적인 씬 종료 또는 재연결 취소다.
            }
            catch (Exception exception)
            {
                if (!cancellationToken.IsCancellationRequested && !shuttingDown)
                    pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Failed, "수신 오류: " + exception.Message));
            }
        }

        public void CreateRoom() => Send("CREATE_ROOM", new OutgoingData());

        public void JoinRoom(string roomId)
        {
            roomId = (roomId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(roomId))
            {
                ReportError("방 ID를 입력해 주세요.");
                return;
            }

            Send("JOIN_ROOM", new OutgoingData { roomId = roomId });
        }

        public void SetReady(bool ready) => Send("READY", new OutgoingData { ready = ready });

        /// <summary>
        /// 방을 시작한다. 방장의 클라이언트가 곧 게임 서버이므로(S15P21B202-263)
        /// 참가자들이 붙을 주소를 함께 알린다 — 그 값은 방장만 안다.
        /// </summary>
        public void StartGame(string hostAddress = null)
        {
            if (string.IsNullOrWhiteSpace(hostAddress)) hostAddress = LocalMatchAddress();
            Send("START_GAME", new OutgoingData { hostAddress = hostAddress });
        }

        /// <summary>
        /// 참가자가 나에게 붙을 때 쓸 주소. 내 랜 주소를 찾는다(-263).
        ///
        /// 서버가 보는 내 주소(공인 IP)로는 안 된다 — 공유기가 밖에서 들어오는 연결을
        /// 막기 때문이다. 같은 네트워크 안에서는 사설 주소로 서로 닿는다. 인터넷 너머는
        /// 릴레이가 붙은 뒤에 그 세션 값을 대신 싣는다(후속 이슈).
        ///
        /// 포트는 붙이지 않는다. 참가자는 자기 빌드의 전송 설정(KcpTransport)에 적힌
        /// 포트를 쓰고, 그 값은 방장과 같은 빌드라 항상 일치한다.
        /// </summary>
        /// <summary>
        /// 서버가 보낸 릴레이 값을 전송이 쓰는 모양으로 옮긴다(-264).
        /// 값이 모자라면 아무것도 돌려주지 않는다 — 그때는 직접 접속으로 간다.
        /// </summary>
        private static MatchTransport.Relay? ReadRelay(RelayData data)
        {
            if (data == null) return null;

            var relay = new MatchTransport.Relay
            {
                Address = data.address,
                ServerPort = (ushort)Mathf.Clamp(data.serverPort, 0, ushort.MaxValue),
                ClientPort = (ushort)Mathf.Clamp(data.clientPort, 0, ushort.MaxValue),
                SessionToken = ToToken(data.sessionToken),
                UserToken = ToToken(data.userToken)
            };

            if (!relay.IsUsable)
            {
                Debug.LogWarning("[NetworkSession] 릴레이 값이 모자라 직접 접속으로 간다.");
                return null;
            }

            return relay;
        }

        /// <summary>
        /// 토큰은 uint 범위다. JSON 은 부호 있는 정수로 오므로 범위를 벗어나면 0 으로
        /// 두어 "쓸 수 없는 값" 이 되게 한다 — 잘라 쓰면 엉뚱한 세션에 붙는다.
        /// </summary>
        private static uint ToToken(long value)
            => value > 0 && value <= uint.MaxValue ? (uint)value : 0u;

        public static string LocalMatchAddress()
        {
            try
            {
                // 밖으로 나가는 소켓을 하나 열어 보면 커널이 "이 주소로 나간다" 를 알려 준다.
                // 실제로 보내지는 않는다. 랜카드가 여럿일 때 어느 것이 쓰이는지 고르는
                // 가장 확실한 방법이다 — 이름으로 고르면 가상 어댑터를 집는다.
                using (var probe = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp))
                {
                    probe.Connect("8.8.8.8", 65530);
                    var local = probe.LocalEndPoint as System.Net.IPEndPoint;
                    if (local != null) return local.Address.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NetworkSession] 내 주소를 찾지 못했다: " + e.Message);
            }

            return string.Empty;
        }

        public void LeaveRoom()
        {
            Send("LEAVE_ROOM", new OutgoingData());
            CurrentRoomId = string.Empty;
            CurrentRoom = null;
            IsLocalReady = false;
            RoomStateChanged?.Invoke(null);
        }

        /// <summary>
        /// 내 위치를 방의 다른 사람에게 중계시킨다. 매 프레임 가까이 불리므로
        /// 연결이 없을 때 조용히 넘어간다 — 여기서 오류를 띄우면 화면이 오류로 뒤덮인다.
        /// </summary>
        public void SendMove(float x, float y, float z, float yaw)
        {
            ClientWebSocket client = socket;
            CancellationTokenSource cancellation = socketCancellation;
            if (!IsConnected || client == null || cancellation == null) return;

            _ = SendAsync(client, BuildMovePayload(x, y, z, yaw), cancellation.Token);
        }

        /// <summary>PLAYER_MOVE 본문. 형식을 테스트에서 확인할 수 있게 분리해 둔다.</summary>
        public static string BuildMovePayload(float x, float y, float z, float yaw)
        {
            return JsonConvert.SerializeObject(new OutgoingMoveMessage
            {
                type = "PLAYER_MOVE",
                data = new MoveData { x = x, y = y, z = z, yaw = yaw }
            });
        }

        private void Send(string type, OutgoingData data)
        {
            // 소켓과 취소 토큰을 지역 변수로 먼저 붙잡는다. CloseSocket() 이 CTS 를 Cancel → Dispose → null 로
            // 만들기 때문에, 종료·재연결과 겹치면 socketCancellation.Token 접근에서 NullReference 또는
            // ObjectDisposedException 이 났다(2026-08-27 전수 AI 리뷰 지적, S15P21B202-136).
            ClientWebSocket client;
            CancellationTokenSource cts;
            lock (socketGate)
            {
                // 둘을 따로 읽으면 그 사이에 CloseSocket + 재연결이 끼어들어 '옛 소켓 + 새 토큰' 짝이
                // 만들어질 수 있다. 그러면 SendAsync 가 client.State 검사에서 조용히 반환해 메시지가
                // 오류 표시도 로그도 없이 사라진다(MR !80 리뷰 지적). 같은 잠금에서 한 짝으로 읽는다.
                client = socket;
                cts = socketCancellation;
            }

            if (!IsConnected || client == null || cts == null)
            {
                ReportError("서버에 연결되어 있지 않습니다.");
                return;
            }

            CancellationToken token;
            try
            {
                token = cts.Token;
            }
            catch (ObjectDisposedException)
            {
                ReportError("서버 연결이 방금 끊어졌습니다. 다시 시도해 주세요.");
                return;
            }

            string payload = JsonUtility.ToJson(new OutgoingMessage { type = type, data = data });
            _ = SendAsync(client, payload, token);
        }

        private async Task SendAsync(ClientWebSocket client, string payload, CancellationToken cancellationToken)
        {
            try
            {
                await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (client.State != WebSocketState.Open) return;
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    sendGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                // 보내는 사이에 소켓이 닫혔다(나가기·재연결). 사용자에게 보일 오류가 아니라 조용히 넘긴다
                // — 예전에는 "송신 오류" 로 올라가 CloseSocket + Fail 까지 이어졌다(MR !80 리뷰 지적).
            }
            catch (Exception exception)
            {
                if (!shuttingDown)
                    pendingEvents.Enqueue(new NetworkEvent(NetworkEventType.Failed, "송신 오류: " + exception.Message));
            }
        }

        private void HandleEvent(NetworkEvent pending)
        {
            switch (pending.Type)
            {
                case NetworkEventType.Connected:
                    SetState(NetworkConnectionState.Connected);
                    break;
                case NetworkEventType.Text:
                    HandleMessage(pending.Text);
                    break;
                case NetworkEventType.Failed:
                    CloseSocket();
                    Fail(pending.Text);
                    break;
                case NetworkEventType.Closed:
                    CloseSocket();
                    SetState(NetworkConnectionState.Disconnected);
                    ConnectionClosed?.Invoke();
                    break;
            }
        }

        private void HandleMessage(string json)
        {
            WebSocketEnvelope message;
            try
            {
                message = ParseEnvelope(json);
            }
            catch (Exception)
            {
                ReportError("서버 메시지를 읽을 수 없습니다.");
                return;
            }

            if (message == null || string.IsNullOrEmpty(message.type))
            {
                ReportError("서버 메시지 형식이 올바르지 않습니다.");
                return;
            }

            var data = message.data ?? new IncomingData();
            switch (message.type)
            {
                case "ROOM_CREATED":
                    CurrentRoomId = data.roomId ?? string.Empty;
                    break;
                case "ROOM_STATE":
                    CurrentRoomId = data.roomId ?? CurrentRoomId;
                    CurrentRoom = new RoomSnapshot(data);
                    PlayerSnapshot local;
                    IsLocalReady = CurrentRoom.TryGetPlayer(UserId, out local) && local.Ready;
                    RoomStateChanged?.Invoke(CurrentRoom);
                    break;
                case "GAME_STARTED":
                    // 방장이 알려 준 매치 주소. 없으면(옛 서버) 로비 주소로 대신한다.
                    MatchAddress = string.IsNullOrWhiteSpace(data.hostAddress)
                        ? string.Empty
                        : data.hostAddress.Trim();
                    MatchRelay = ReadRelay(data.relay);
                    GameStarted?.Invoke();
                    break;
                case "PLAYER_MOVED":
                    PlayerMoved?.Invoke(new PlayerMove(data.userId, data.x, data.y, data.z, data.yaw));
                    break;
                case "STATE_SYNC":
                    // 재접속한 사람에게 오는 게임 상태 전체. 복구 처리는 아직 범위 밖이라
                    // 받기만 하고 넘긴다 — 모르는 메시지로 취급해 오류를 띄우지 않기 위해서다.
                    break;
                case "ERROR":
                    ReportError(string.IsNullOrEmpty(data.message) ? "요청을 처리할 수 없습니다." : data.message);
                    break;
                default:
                    ReportError("알 수 없는 서버 메시지입니다: " + message.type);
                    break;
            }
        }

        /// <summary>
        /// ROOM_STATE 의 players 가 userId 를 키로 하는 객체라 JsonUtility 로는 읽을 수 없다.
        /// 테스트에서도 쓰도록 밖으로 열어 둔다.
        /// </summary>
        public static WebSocketEnvelope ParseEnvelope(string json)
        {
            return JsonConvert.DeserializeObject<WebSocketEnvelope>(json);
        }

        /// <summary>
        /// 로비에서 입력한 "호스트[:포트]"를 실제 접속 주소로 바꾸고 다음 실행을 위해 저장한다.
        /// 서버가 아직 배포되지 않아 방장 PC를 직접 가리켜야 하므로 주소를 실행 중에 바꿀 수 있어야 한다.
        /// </summary>
        public bool ApplyServerAddress(string address, out string error)
        {
            string httpUrl;
            string socketUrl;
            if (!TryBuildEndpoints(address, out httpUrl, out socketUrl, out error))
                return false;

            Configure(httpUrl, socketUrl);
            ServerAddress = address.Trim();
            return true;
        }

        /// <summary>
        /// "j15b202.p.ssafy.io", "127.0.0.1:8080", "https://j15b202.p.ssafy.io" 을 모두 받아
        /// REST와 WebSocket 주소를 만든다.
        ///
        /// 암호화 여부는 이렇게 정한다:
        /// 1. 접두어를 쓰면 그대로 따른다 (https·wss → 암호화, http·ws → 평문)
        /// 2. 접두어가 없으면 포트로 판단한다 — 포트를 생략했거나 443 이면 암호화, 아니면 평문
        ///
        /// 배포 서버는 도메인만 넣으면 https 로, 로컬 개발은 "127.0.0.1:8080" 그대로 http 로 붙는다.
        /// Unity 는 127.0.0.1 을 제외한 평문 http 를 막으므로(S15P21B202-165) 배포 서버는 https 여야 한다.
        /// </summary>
        public static bool TryBuildEndpoints(string address, out string httpUrl, out string webSocketUrl, out string error)
        {
            httpUrl = null;
            webSocketUrl = null;
            error = null;

            string trimmed = (address ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                error = "서버 주소를 입력해 주세요.";
                return false;
            }

            // 접두어를 떼면서 암호화 여부를 함께 읽는다.
            bool? secureFromScheme = null;
            int schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0)
            {
                string scheme = trimmed.Substring(0, schemeEnd).ToLowerInvariant();
                if (scheme == "https" || scheme == "wss") secureFromScheme = true;
                else if (scheme == "http" || scheme == "ws") secureFromScheme = false;
                else
                {
                    error = "알 수 없는 주소 형식입니다: " + scheme;
                    return false;
                }

                trimmed = trimmed.Substring(schemeEnd + 3);
            }

            int pathStart = trimmed.IndexOf('/');
            if (pathStart >= 0)
                trimmed = trimmed.Substring(0, pathStart);

            string host = trimmed;
            bool portGiven = false;
            int port = 0;
            int colon = trimmed.LastIndexOf(':');
            if (colon >= 0)
            {
                host = trimmed.Substring(0, colon);
                string portText = trimmed.Substring(colon + 1);
                if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
                {
                    error = "포트 번호가 올바르지 않습니다: " + portText;
                    return false;
                }

                portGiven = true;
            }

            host = host.Trim();
            if (host.Length == 0)
            {
                error = "호스트 주소가 비어 있습니다.";
                return false;
            }

            bool secure = secureFromScheme ?? (!portGiven || port == SecurePort);
            if (!portGiven)
                port = secure ? SecurePort : DefaultServerPort;

            httpUrl = (secure ? "https://" : "http://") + host + ":" + port;
            webSocketUrl = (secure ? "wss://" : "ws://") + host + ":" + port + "/ws/game";
            return true;
        }

        /// <summary>
        /// 주소에서 호스트만 떼어 낸다. 게임 서버는 로비와 같은 PC 에 있고 포트만 다르므로,
        /// 로비에 입력한 주소의 호스트를 그대로 쓴다(S15P21B202-161).
        /// </summary>
        public static string HostOf(string address)
        {
            string trimmed = (address ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;

            int schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0) trimmed = trimmed.Substring(schemeEnd + 3);

            int pathStart = trimmed.IndexOf('/');
            if (pathStart >= 0) trimmed = trimmed.Substring(0, pathStart);

            int colon = trimmed.LastIndexOf(':');
            if (colon >= 0) trimmed = trimmed.Substring(0, colon);

            return trimmed.Trim();
        }

        /// <summary>마지막으로 입력한 서버 주소. 없으면 <paramref name="fallback"/>, 그것도 없으면 기본값.</summary>
        /// <summary>
        /// 어느 서버를 볼지 정한다. 평소에는 배포 서버 한 곳이다.
        ///
        /// 예전에는 로비 화면에서 주소를 고쳐 PlayerPrefs 에 저장했는데, 화면에서 입력칸을
        /// 없애면서(-162) 저장값도 읽지 않는다. 한 번 잘못 저장되면 게임 안에서 고칠
        /// 방법이 없어 로그인이 조용히 멈추기 때문이다 — 실제로 예전 LAN 테스트 값
        /// (192.168.x.x:8081)이 남아 접속이 막힌 적이 있다.
        ///
        /// 개발자가 다른 서버를 봐야 할 때는 실행 인자로 준다:
        /// <c>MetaGame.exe -metagame-server=127.0.0.1:8080</c>
        /// </summary>
        public static string ResolveServerAddress(string fallback = null)
        {
            const string argumentPrefix = "-metagame-server=";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string value = args[i].Substring(argumentPrefix.Length).Trim();
                if (value.Length > 0) return value;
            }

            return string.IsNullOrWhiteSpace(fallback) ? DefaultServerAddress : fallback.Trim();
        }

        public static string ResolveDeviceId(string overrideValue = null)
        {
            if (!string.IsNullOrWhiteSpace(overrideValue))
                return overrideValue.Trim();

            const string argumentPrefix = "-metagame-device-id=";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(argumentPrefix.Length).Trim();
            }

            const string playerPrefsKey = "metagame.device-id";
            if (PlayerPrefs.HasKey(playerPrefsKey))
                return PlayerPrefs.GetString(playerPrefsKey);

            string generated = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(playerPrefsKey, generated);
            PlayerPrefs.Save();
            return generated;
        }

        private void SetState(NetworkConnectionState state)
        {
            State = state;
            ConnectionStateChanged?.Invoke(state);
        }

        private void Fail(string message)
        {
            SetState(NetworkConnectionState.Failed);
            ReportError(message);
        }

        private void ReportError(string message)
        {
            ErrorReceived?.Invoke(string.IsNullOrEmpty(message) ? "알 수 없는 네트워크 오류입니다." : message);
        }

        private void CloseSocket()
        {
            ClientWebSocket closing;
            lock (socketGate)
            {
                Interlocked.Increment(ref connectGeneration);   // 진행 중인 연결을 무효화한다
                socketCancellation?.Cancel();
                socketCancellation?.Dispose();
                socketCancellation = null;
                closing = socket;
                socket = null;                                  // 잠금 안에서 비운다 — 대입과 엇갈리지 않게
            }

            if (closing == null) return;
            try { closing.Abort(); } catch (Exception) { }
            closing.Dispose();
        }

        private void Shutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            CloseSocket();
        }

        void OnApplicationQuit() => Shutdown();

        void OnDestroy()
        {
            Shutdown();
            if (Instance == this) Instance = null;
        }
    }
}
