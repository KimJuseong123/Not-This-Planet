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
        public bool IsConnected => State == NetworkConnectionState.Connected;
        public long UserId { get; private set; }
        public string Nickname { get; private set; } = string.Empty;
        public string CurrentRoomId { get; private set; } = string.Empty;
        public RoomSnapshot CurrentRoom { get; private set; }
        public bool IsLocalReady { get; private set; }
        public string ServerAddress { get; private set; } = DefaultServerAddress;

        public const string DefaultServerAddress = "127.0.0.1:8080";
        private const string ServerAddressPrefsKey = "metagame.server-address";
        private const int DefaultServerPort = 8080;

        public event Action<NetworkConnectionState> ConnectionStateChanged;
        public event Action<RoomSnapshot> RoomStateChanged;
        public event Action GameStarted;
        public event Action<string> ErrorReceived;
        public event Action ConnectionClosed;

        [SerializeField] private string httpBaseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string webSocketUrl = "ws://127.0.0.1:8080/ws/game";

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

        public void StartGame() => Send("START_GAME", new OutgoingData());

        public void LeaveRoom()
        {
            Send("LEAVE_ROOM", new OutgoingData());
            CurrentRoomId = string.Empty;
            CurrentRoom = null;
            IsLocalReady = false;
            RoomStateChanged?.Invoke(null);
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
                    GameStarted?.Invoke();
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
            PlayerPrefs.SetString(ServerAddressPrefsKey, ServerAddress);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>
        /// "192.168.0.15", "192.168.0.15:8080", "http://192.168.0.15:8080" 을 모두 받아
        /// REST와 WebSocket 주소를 만든다. 포트를 생략하면 기본 포트를 쓴다.
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

            // 접두어(http://, ws://)와 뒤따르는 경로는 떼어내고 host:port 만 본다.
            int schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0)
                trimmed = trimmed.Substring(schemeEnd + 3);
            int pathStart = trimmed.IndexOf('/');
            if (pathStart >= 0)
                trimmed = trimmed.Substring(0, pathStart);

            string host = trimmed;
            int port = DefaultServerPort;
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
            }

            host = host.Trim();
            if (host.Length == 0)
            {
                error = "호스트 주소가 비어 있습니다.";
                return false;
            }

            httpUrl = "http://" + host + ":" + port;
            webSocketUrl = "ws://" + host + ":" + port + "/ws/game";
            return true;
        }

        /// <summary>마지막으로 입력한 서버 주소. 없으면 <paramref name="fallback"/>, 그것도 없으면 기본값.</summary>
        public static string ResolveServerAddress(string fallback = null)
        {
            if (PlayerPrefs.HasKey(ServerAddressPrefsKey))
            {
                string saved = PlayerPrefs.GetString(ServerAddressPrefsKey);
                if (!string.IsNullOrWhiteSpace(saved))
                    return saved.Trim();
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
