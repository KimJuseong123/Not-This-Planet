using UnityEngine;
using UnityEngine.Video;
using Metagame.Core;
using Metagame.Network;
using Metagame.UI;
using Metagame.Gameplay.Audio;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 로비의 조합 루트. 네트워크 세션을 씬보다 오래 살리고,
    /// 게임 시작 신호를 받으면 게임 서버(Mirror)에 접속시킨다.
    ///
    /// 로비까지는 Spring(방 코드·준비·시작), 매치는 Mirror 게임 서버가 맡는다.
    /// 그 경계가 GAME_STARTED 다 — 이 신호를 받은 클라이언트가 게임 서버에 붙고,
    /// 씬 전환은 Mirror 가 Online Scene 으로 처리한다(S15P21B202-161).
    ///
    /// 세션 오브젝트는 DontDestroyOnLoad라 씬이 바뀌어도 로비 연결이 유지된다.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        // **주소를 여기에 두지 않는다** (S15P21B202-284). 개발·배포 서버가 갈렸고,
        // 갈려야 하는 주소가 셋인데 하나만 어긋나도 조용히 틀린다 —
        // `MatchAuthenticator.VerifyUrl` 은 [SerializeField] 도 아니라 눈에 안 보였다.
        // `BackendEndpoints` 가 빌드 스탬프(`백엔드=dev|prod`)를 보고 셋을 함께 정한다.
        //
        // 씬에 직렬화된 옛 값이 남아 있어도 읽지 않는다 — 필드를 없앴으므로 Unity 가
        // 그 데이터를 버린다. 임시로 다른 서버를 가리키려면 실행 인자를 쓴다:
        //   NotThisPlanet.exe -backend=https://…
        //
        // **`defaultServerAddress` 도 같은 이유로 없앴다** (S15P21B202-584). 위 주석은
        // `httpBaseUrl`·`webSocketUrl` 을 뺀 뒤에 쓴 것인데, 같은 일을 하는 필드가 하나
        // 남아 있었다. 씬에 `j15b202.p.ssafy.io`(배포)가 저장돼 있었고 그 값이
        // `ResolveServerAddress` 에서 **빌드 채널보다 먼저** 읽혀서, 개발 빌드인데
        // 로비만 배포 서버로 갔다 — 방은 배포 쪽에 생기고 입장 검증은 개발 쪽으로 가
        // 401 이 난다(2026-09-17 nginx 로그 실측). 셋을 다 없애야 끝난다.

        [Header("Guest")]
        [SerializeField] private string defaultNickname = "비행사";
        [Tooltip("비워 두면 설치별 PlayerPrefs GUID를 사용합니다.")]
        [SerializeField] private string deviceIdOverride;

        [Header("Lobby Visuals")]
        [SerializeField] private Texture2D lobbyBackgroundImage;
        [SerializeField] private VideoClip lobbyBackgroundVideo;

        [Header("Lobby BGM")]
        [Tooltip("로비 음악의 클립·기본 음량·페이드 시간을 설정합니다. 비워 두면 Resources/Audio/LobbyAudioManager를 사용합니다.")]
        [SerializeField] private AudioManager lobbyAudioManager;

        private NetworkSession network;
        private GameSettingsRuntime settings;
        private LobbyTransition transition;
        private bool joiningMatch;

        void Awake()
        {
            // 혼자 돌리는 점검(README 의 -space-selftest · -space-screenshot)은 로비를
            // 거치지 않는다. 씬 분리(-165) 뒤로 빌드가 로비에서 시작하는데 점검 코드는
            // 게임 씬에 있어서, 로비에 멈춰 선 채 아무것도 하지 않았다.
            if (SkipToGameScene()) return;

            settings = GameSettingsRuntime.Ensure();
            FpsOverlay.Ensure(settings);
            if (!Application.isBatchMode && !MatchRole.DedicatedServer)
            {
                var music = GetComponent<GameMusicPlayer>();
                if (music == null) music = gameObject.AddComponent<GameMusicPlayer>();
                music.Configure(lobbyAudioManager != null ? lobbyAudioManager
                    : Resources.Load<AudioManager>("Audio/LobbyAudioManager"));
            }
            PrepareLobbyCursor();

            network = NetworkSession.Instance;
            if (network == null)
            {
                var networkObject = new GameObject("~네트워크");
                DontDestroyOnLoad(networkObject);
                network = networkObject.AddComponent<NetworkSession>();
            }

            network.Configure(BackendEndpoints.HttpBaseUrl, BackendEndpoints.WebSocketUrl);
            network.GameStarted += EnterMatch;

            transition = LobbyTransition.Ensure();
            transition.Failed += OnTransitionFailed;

            // 같은 오브젝트가 재활성·재초기화되면 HUD 가 두 개 붙는다(전수 AI 리뷰 지적, S15P21B202-136).
            // ?? 는 UnityEngine.Object 의 == 오버로드를 우회하므로 쓰지 않고 == null 로 검사한다.
            var hud = GetComponent<LobbyHud>();
            if (hud == null) hud = gameObject.AddComponent<LobbyHud>();
            hud.Initialize(network, defaultNickname, deviceIdOverride,
                lobbyBackgroundImage, lobbyBackgroundVideo, settings);
        }

        private void OnEnable()
        {
            PrepareLobbyCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
                PrepareLobbyCursor();
        }

        /// <summary>
        /// 게임 씬에서 잠근 커서 상태가 에디터 씬 전환이나 로비 재진입 뒤에
        /// 남아 IMGUI 버튼을 막지 않도록 로비에서 명시적으로 포인터를 복구한다.
        /// </summary>
        private static void PrepareLobbyCursor()
        {
            if (Application.isBatchMode)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 점검용 실행이면 게임 씬으로 바로 넘어간다.
        /// </summary>
        /// <returns>넘어갔으면 참 — 로비를 세우지 않는다.</returns>
        static bool SkipToGameScene()
        {
            var args = System.Environment.GetCommandLineArgs();
            bool selfTest = System.Array.IndexOf(args, "-space-selftest") >= 0;
            bool shot = System.Array.IndexOf(args, "-space-screenshot") >= 0;
            if (!selfTest && !shot) return false;

            // 씬 이름은 Mirror 의 Online Scene 을 그대로 쓴다 — 한 곳에서만 관리한다.
            // singleton 은 NetworkManager 자신의 Awake 에서 채워지는데 씬 오브젝트의
            // Awake 순서는 정해져 있지 않다. 컴포넌트를 직접 찾으면 순서와 무관하다.
            var manager = FindAnyObjectByType<Mirror.NetworkManager>();
            string scene = manager != null ? manager.onlineScene : null;
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[LobbyBootstrap] 게임 씬을 알 수 없다. NetworkManager 의 Online Scene 을 확인할 것.");
                return false;
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(
                System.IO.Path.GetFileNameWithoutExtension(scene));
            return true;
        }

        /// <summary>
        /// 게임 시작 신호를 받으면 게임 서버에 붙는다.
        ///
        /// 씬을 직접 로드하지 않는다 — Mirror 가 접속 후 Online Scene 으로 넘겨준다.
        /// 둘 다 하면 씬이 두 번 로드되어 방금 만든 연결이 끊긴다.
        /// </summary>
        private void EnterMatch()
        {
            if (joiningMatch) return;

            joiningMatch = true;
            var transition = LobbyTransition.Ensure();
            if (!transition.BeginNetwork(StartMirrorMatch))
                joiningMatch = false;
        }

        /// <summary>
        /// 로비 fade가 끝난 뒤 호출되는 실제 Mirror 시작 부분.
        /// UI 연출과 네트워크 초기화를 분리해 화면이 먼저 검게 덮이도록 한다.
        /// </summary>
        private bool StartMirrorMatch()
        {
            var manager = Mirror.NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[LobbyBootstrap] NetworkManager 가 없어 매치에 들어갈 수 없다.", this);
                return false;
            }

            // 게임 서버가 Spring 에 물어볼 때 쓸 값. 토큰은 여기 말고 어디에도 남기지 않는다.
            MatchAuthenticator.ClientToken = network.AccessToken;
            MatchAuthenticator.ClientRoomId = network.CurrentRoomId;

            // 릴레이 세션이 왔으면 그 길로 간다(-264). 서로 다른 네트워크에서는 공유기가
            // 막아 방장에게 직접 붙을 수 없다. 값이 없거나 모자라면 직접 접속으로 남는다 —
            // 릴레이 하나 때문에 같은 네트워크 시연까지 멈추면 안 된다.
            if (network.MatchRelay.HasValue)
                MatchTransport.UseRelay(manager, network.MatchRelay.Value);
            else
                MatchTransport.UseDirect(manager);

            // 로비를 거쳐 들어오는 사람은 방장이든 참가자든 화면이 있다.
            // 게임 씬이 이 값을 보고 캐릭터·카메라·HUD 를 만들지 정한다.
            MatchRole.DedicatedServer = false;

            // 이 방을 맡은 게 나라면 내가 게임 서버가 된다(S15P21B202-263).
            // 물리와 판정이 내 PC 에서 돌고, 참가자들이 나에게 붙는다.
            if (network.IsRoomHost)
            {
                manager.StartHost();
                Debug.Log("[LobbyBootstrap] 방장이므로 이 PC 가 게임 서버가 된다.");
                return true;
            }

            // 참가자는 방장이 알려 준 곳으로 붙는다. 그 값이 없으면(주소를 안 싣는 옛
            // 서버이거나, 전용 서버로 돌릴 때) 예전처럼 로비와 같은 호스트로 붙는다.
            string host = NetworkSession.HostOf(network.MatchAddress);
            if (string.IsNullOrEmpty(host)) host = NetworkSession.HostOf(network.ServerAddress);
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            manager.networkAddress = host;

            manager.StartClient();
            // 릴레이를 탈 때는 이 주소를 쓰지 않는다 — 전송이 릴레이 주소로 붙는다.
            Debug.Log(network.MatchRelay.HasValue
                ? "[LobbyBootstrap] 참가자로 릴레이를 거쳐 붙는다."
                : "[LobbyBootstrap] 참가자로 " + host + " 에 직접 붙는다.");
            return true;
        }

        void OnDestroy()
        {
            if (network != null)
                network.GameStarted -= EnterMatch;

            if (transition != null)
                transition.Failed -= OnTransitionFailed;
        }

        private void OnTransitionFailed(string message)
        {
            joiningMatch = false;
        }
    }
}
