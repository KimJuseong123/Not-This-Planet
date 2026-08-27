using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            connectTask = ConnectSocketAsync(accessToken, socketCancellation.Token);
        }

        private async Task ConnectSocketAsync(string token, CancellationToken cancellationToken)
        {
            var client = new ClientWebSocket();
            try
            {
                var uri = new Uri(webSocketUrl + "?token=" + UnityWebRequest.EscapeURL(token));
                await client.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested || shuttingDown)
                {
                    client.Dispose();
                    return;
                }

                socket = client;
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
            if (!IsConnected || socket == null)
            {
                ReportError("서버에 연결되어 있지 않습니다.");
                return;
            }

            string payload = JsonUtility.ToJson(new OutgoingMessage { type = type, data = data });
            _ = SendAsync(socket, payload, socketCancellation.Token);
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
                message = JsonUtility.FromJson<WebSocketEnvelope>(json);
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
            socketCancellation?.Cancel();
            socketCancellation?.Dispose();
            socketCancellation = null;

            if (socket == null) return;
            try { socket.Abort(); } catch (Exception) { }
            socket.Dispose();
            socket = null;
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
