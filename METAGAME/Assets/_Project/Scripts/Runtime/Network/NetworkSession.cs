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
        double roomTimerStartedAt = -1d;
        public double RoomPlayTimeSeconds => roomTimerStartedAt < 0d ? 0d
            : Math.Max(0d, Time.realtimeSinceStartupAsDouble - roomTimerStartedAt);
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

        /// <summary>로비 WebSocket 의 경로. 주소에 이것이 이미 붙어 와도 두 번 붙이지 않는다.</summary>
        private const string SocketPath = "/ws/game";

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

        /// <summary>
        /// 구버전이라 막혔을 때 서버가 주는 상태 코드 — <c>426 Upgrade Required</c>.
        /// 401·403 과 뜻이 달라야 클라이언트가 **다시 로그인해 보는 헛수고**를 하지 않는다.
        /// </summary>
        public const long OutdatedStatus = 426;

        /// <summary>
        /// 426 본문을 사람이 읽을 한 줄로 바꾼다 (S15P21B202-404).
        ///
        /// <para><b>본문을 못 읽어도 쓸모 있는 말을 해야 한다.</b> 서버가 형식을 바꾸거나
        /// 중간에서 무언가 끼어들면 본문이 비거나 깨지는데, 그때 빈 화면을 주면
        /// 사용자는 게임이 고장 난 줄 안다.</para>
        /// </summary>
        public static string OutdatedMessage(string body)
        {
            const string fallback = "게임 버전이 낮아 접속할 수 없습니다. 최신 버전을 받아 주세요.";

            ClientOutdatedResponse parsed = null;
            if (!string.IsNullOrEmpty(body))
            {
                try { parsed = JsonUtility.FromJson<ClientOutdatedResponse>(body); }
                catch (Exception)
                {
                    parsed = null;
                    // 삼키지 않고 한 줄 남긴다. 폴백 문구는 정상 경로와 구분되지 않아서,
                    // 서버가 본문 형식을 바꿔도 **아무도 모른 채** 기본 문장만 나간다.
                    Debug.LogWarning("[로그인] 426 본문을 읽지 못했다: " + body);
                }
            }

            string message = parsed != null && !string.IsNullOrEmpty(parsed.message)
                             ? parsed.message.Trim()
                             : fallback;

            // 받을 곳이 있으면 붙인다. 없으면 문장만 — 빈 괄호를 남기지 않는다.
            string url = parsed != null && parsed.downloadUrl != null ? parsed.downloadUrl.Trim() : string.Empty;
            return string.IsNullOrEmpty(url) ? message : message + "\n받을 곳: " + url;
        }

        private IEnumerator LoginGuestRoutine(string nickname, string deviceId)
        {
            SetState(NetworkConnectionState.LoggingIn);

            var payload = JsonUtility.ToJson(new GuestLoginRequest
            {
                nickname = nickname,
                deviceId = deviceId,
                // 서버가 이 값으로 구버전을 막는다 (S15P21B202-404). 릴리스 빌드가 아니면
                // 빈 문자열이고, **빈 값을 어떻게 볼지는 서버가 정한다** — 여기서 "나는
                // 최신" 이라고 판단하면 그 판단째로 우회된다.
                clientVersion = Metagame.Core.BuildInfo.ReleaseVersion
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
                    // **구버전이라 막힌 것은 "로그인 실패" 와 다르다** (S15P21B202-404).
                    // 그냥 실패로 보여 주면 사용자가 다시 눌러 보고, 몇 번을 눌러도
                    // 같은 화면이 나온다 — 무엇을 해야 하는지 알려 줘야 한다.
                    Fail(request.responseCode == OutdatedStatus
                         ? OutdatedMessage(request.downloadHandler != null ? request.downloadHandler.text : null)
                         : "로그인 실패: " + request.error);
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

            // **지난 실행의 로그를 여기서 보낸다** (S15P21B202-443).
            //
            // 로그인 직후인 이유: 서버가 게스트 토큰을 요구한다 — 아무나 우리 서버에
            // 파일을 부어넣지 못하게 하는 것이 그 토큰의 역할이다.
            //
            // 플레이가 시작되기 **전**이라 이슈의 팀 결정("플레이 중에는 네트워킹을
            // 쓰지 않는다")을 지킨다. 종료 훅에서 보내지 않는 이유는 `RunLogUpload`
            // 주석에 있다 — 요약하면 종료가 지연되고, 크래시 판은 아예 못 받는다.
            //
            // 실패해도 `Connect()` 를 막지 않는다. 로그를 못 보내는 것이 게임을
            // 막을 이유는 아니다.
            StartCoroutine(Metagame.Core.RunLogUpload.SweepOnce(
                Metagame.Core.BackendEndpoints.HttpBaseUrl, accessToken, UserId));

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

        public void CreateRoom() =>
            Send("CREATE_ROOM", new OutgoingData { localAddress = LocalMatchAddress() });

        public void JoinRoom(string roomId)
        {
            roomId = (roomId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(roomId))
            {
                ReportError("방 ID를 입력해 주세요.");
                return;
            }

            Send("JOIN_ROOM",
                new OutgoingData { roomId = roomId, localAddress = LocalMatchAddress() });
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


        /// <summary>
        /// 판이 끝났음을 백엔드에 알린다 — 방이 <c>WAITING</c> 으로 돌아온다
        /// (S15P21B202-316 · S15P21B202-54).
        ///
        /// <para><b>방장만 부르는 것이다.</b> 백엔드도 방장이 아니면 거절한다. 클리어를
        /// 판정하는 것은 호스트이고, 호스트가 곧 방장이다(-145 호스트 권위).</para>
        ///
        /// <para>본문은 비어 있다 — 서버는 누가 보냈는지로 방을 찾는다. 결과 화면에
        /// 필요한 기록(클리어 시간·추락 횟수)은 이 메시지가 아니라 Mirror 의 SyncVar 로
        /// 이미 모두에게 가 있다(<c>ProgressSync</c>).</para>
        /// </summary>
        public void SendGameEnd()
        {
            if (!IsConnected || CurrentRoom == null) return;

            // 방장만 보낼 수 있다. 부르는 쪽(ProgressSync)도 검사하지만 여기서도 막는다 —
            // 다른 호출자가 생기면 조용히 ERROR 왕복만 생기고 원인이 안 보인다.
            if (!IsRoomHost) return;

            Send("GAME_END", new OutgoingData());
        }

        /// <summary>
        /// 매치를 이어받겠다고 알린다 (S15P21B202-389). 참가자들이 붙을 내 주소를 함께 보낸다.
        ///
        /// <para><b>내가 말을 걸어야 하는 이유.</b> 그 주소는 나만 안다 — 서버가 보는 내
        /// 주소는 공인 IP 라 공유기가 밖에서 들어오는 연결을 막는다. <see cref="StartGame"/>
        /// 가 <c>hostAddress</c> 를 싣는 것과 같은 이유이고, 그래서 이전도 같은 모양이다.</para>
        ///
        /// <para>서버가 방장인지 다시 확인하므로 여기서는 막지 않는다 — 방장이 아닌데
        /// 보내면 서버가 조용히 무시한다.</para>
        /// </summary>
        public void SendHostTakeover(string hostAddress = null)
        {
            if (string.IsNullOrWhiteSpace(hostAddress)) hostAddress = LocalMatchAddress();
            Send("HOST_TAKEOVER", new OutgoingData { hostAddress = hostAddress });
        }

        public void LeaveRoom()
        {
            if (IsConnected && CurrentRoom != null)
                Send("LEAVE_ROOM", new OutgoingData());
            ClearRoomState();
        }

        /// <summary>
        /// 로비 연결만 끊고 게스트 신원은 유지한다. 혼자 하기로 전환하거나
        /// 로비 씬을 떠날 때 사용한다.
        /// </summary>
        public void DisconnectLobby()
        {
            if (IsConnected && CurrentRoom != null)
                Send("LEAVE_ROOM", new OutgoingData());

            CloseSocket();
            ClearRoomState();
            SetState(NetworkConnectionState.Disconnected);
        }

        /// <summary>
        /// 게스트 세션을 클라이언트에서 정리한다. 백엔드는 JWT 무효화 API가 없는
        /// 구조이므로 서버 호출은 하지 않으며, 다음 로그인 때 같은 deviceId로
        /// 닉네임을 다시 갱신할 수 있다.
        /// </summary>
        public void LogoutGuest()
        {
            if (loginCoroutine != null)
            {
                StopCoroutine(loginCoroutine);
                loginCoroutine = null;
            }

            DisconnectLobby();
            accessToken = null;
            UserId = 0;
            Nickname = string.Empty;
            MatchAddress = string.Empty;
            MatchRelay = null;
            MatchAuthenticator.ClientToken = null;
            MatchAuthenticator.ClientRoomId = null;
        }

        private void ClearRoomState()
        {
            roomTimerStartedAt = -1d;
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
                    roomTimerStartedAt = Time.realtimeSinceStartupAsDouble;
                    CurrentRoomId = data.roomId ?? string.Empty;
                    break;
                case "ROOM_STATE":
                    // 같은 방의 상태 갱신/준비/재시작에는 유지하고 새 방 또는 방장 교체에만 리셋한다.
                    if (roomTimerStartedAt < 0d || (CurrentRoom != null
                        && (CurrentRoomId != data.roomId || CurrentRoom.HostUserId != data.hostUserId)))
                        roomTimerStartedAt = Time.realtimeSinceStartupAsDouble;
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

                    // **플레이가 시작되면 남은 로그 전송을 멈춘다** (S15P21B202-443).
                    // 느린 회선에서는 로비에서 시작한 전송이 여기까지 이어질 수 있고,
                    // 그러면 "플레이 중에는 네트워킹을 쓰지 않는다" 를 못 지킨다.
                    // 못 보낸 것은 다음 실행에서 다시 간다.
                    Metagame.Core.RunLogUpload.StopNow();

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
                    // 사유 코드가 있으면 그것으로 문구를 고른다 (S15P21B202-413).
                    ReportError(DescribeError(data.reason,
                        string.IsNullOrEmpty(data.message) ? "요청을 처리할 수 없습니다." : data.message));
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

            // **경로를 지킨다** (S15P21B202-573). 예전에는 여기서 버렸는데, 그러면
            // `https://…/dev` 로 띄워도 로비와 WebSocket 만 **배포 서버로 간다.**
            // 그 상태에서는 방이 배포 쪽 메모리에 생기고 입장 검증은 개발 쪽으로 가서,
            // 개발 서버가 "그런 방 없다" 로 401 을 낸다 — **로비까지는 멀쩡히 되고
            // 입장에서만 막혀** 원인이 눈에 안 보였다(2026-09-16 실측).
            string path = string.Empty;
            int pathStart = trimmed.IndexOf('/');
            if (pathStart >= 0)
            {
                path = trimmed.Substring(pathStart).TrimEnd('/');
                trimmed = trimmed.Substring(0, pathStart);
            }

            // **끝의 `/ws/game` 은 접두가 아니다.** 사람이 WebSocket 주소를 통째로 붙여
            // 넣는 경우가 있어서, 그대로 두면 `/ws/game/ws/game` 이 된다. 떼어 내면
            // `wss://host/ws/game` 과 `https://host/dev/ws/game` 이 둘 다 옳게 풀린다.
            if (path.EndsWith(SocketPath, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - SocketPath.Length);

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

            httpUrl = (secure ? "https://" : "http://") + host + ":" + port + path;
            webSocketUrl = (secure ? "wss://" : "ws://") + host + ":" + port + path + SocketPath;
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

            if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();

            // **백엔드가 정한 주소를 따른다** (S15P21B202-573). 전에는 상수로 떨어져
            // `-backend=…/dev` 로 띄워도 **로비만 배포 서버로 갔다.** 그러면 방은 배포
            // 쪽에 생기고 입장 검증은 개발 쪽으로 가서 401 이 난다.
            //
            // `BackendEndpoints.Base` 는 실행 인자(`-backend=`)와 빌드 채널을 이미
            // 반영한 값이라, 여기서 그것을 쓰면 **한 실행이 한 환경만 본다.**
            string backend = Metagame.Core.BackendEndpoints.Base;
            return string.IsNullOrWhiteSpace(backend) ? DefaultServerAddress : backend.Trim();
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

        /// <summary>
        /// 서버가 준 사유 코드를 사람이 읽는 문구로 바꾼다 (S15P21B202-413).
        ///
        /// <para><b>왜 클라이언트가 정하나.</b> 예전에는 서버가 보낸
        /// <c>"cannot join room"</c> 을 그대로 띄웠다 — 영어이고, 방이 꽉 찬 것인지 이미
        /// 시작된 것인지 코드가 없는 것인지 구분되지 않았다. 사용자가 <b>다음에 뭘 해야
        /// 하는지</b>가 그 셋에서 다른데 한 문구로 뭉쳐 있었다.</para>
        ///
        /// <para>문구를 여기 두면 말을 다듬을 때 서버를 다시 배포하지 않는다. 모르는 코드가
        /// 오면(옛 서버·새 사유) 서버가 보낸 <paramref name="fallback"/> 을 그대로 쓴다 —
        /// 조용히 삼키면 사용자가 아무 설명도 못 받는다.</para>
        /// </summary>
        public static string DescribeError(string reason, string fallback)
        {
            switch (reason)
            {
                case "ROOM_FULL":
                    return $"방 인원이 다 찼습니다. (최대 {MaxRoomPlayers}명)";
                case "ROOM_PLAYING":
                    return "이미 게임이 시작된 방입니다. 끝나면 다시 시도해 주세요.";
                case "ROOM_NOT_FOUND":
                    return "그런 방이 없습니다. 방 코드를 다시 확인해 주세요.";
                case "REJOIN_DENIED":
                    return "이 판에 참가했던 사람만 돌아올 수 있습니다.";
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// 한 방의 최대 인원. 백엔드 <c>Room.MAX_PLAYERS</c> 와 같은 값이다 —
        /// 문구에 숫자를 박아 두면 정원이 바뀔 때 여기가 거짓말을 한다.
        /// </summary>
        public const int MaxRoomPlayers = 4;

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
            // 로그 전송 코루틴은 이 오브젝트가 돌린다. 여기서 정리하지 않으면 "돌고
            // 있다" 는 표시가 선 채로 남아 **그 프로세스가 끝날 때까지 다시는 안 돈다**
            // (S15P21B202-443).
            Metagame.Core.RunLogUpload.StopNow();

            Shutdown();
            if (Instance == this) Instance = null;
        }
    }
}
