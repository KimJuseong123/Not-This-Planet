using UnityEngine;
using Metagame.Network;
using Metagame.UI;

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
        [Header("Backend")]
        [Tooltip("로비 입력칸의 기본값. 저장된 주소가 있으면 그쪽이 우선한다. 형식은 호스트[:포트].")]
        [SerializeField] private string defaultServerAddress = NetworkSession.DefaultServerAddress;
        [SerializeField] private string httpBaseUrl = "https://j15b202.p.ssafy.io:443";
        [SerializeField] private string webSocketUrl = "wss://j15b202.p.ssafy.io:443/ws/game";

        [Header("Guest")]
        [SerializeField] private string defaultNickname = "비행사";
        [Tooltip("비워 두면 설치별 PlayerPrefs GUID를 사용합니다.")]
        [SerializeField] private string deviceIdOverride;

        private NetworkSession network;
        private bool joiningMatch;

        void Awake()
        {
            // 혼자 돌리는 점검(README 의 -space-selftest · -space-screenshot)은 로비를
            // 거치지 않는다. 씬 분리(-165) 뒤로 빌드가 로비에서 시작하는데 점검 코드는
            // 게임 씬에 있어서, 로비에 멈춰 선 채 아무것도 하지 않았다.
            if (SkipToGameScene()) return;

            network = NetworkSession.Instance;
            if (network == null)
            {
                var networkObject = new GameObject("~네트워크");
                DontDestroyOnLoad(networkObject);
                network = networkObject.AddComponent<NetworkSession>();
            }

            network.Configure(httpBaseUrl, webSocketUrl);
            network.GameStarted += EnterMatch;

            // 같은 오브젝트가 재활성·재초기화되면 HUD 가 두 개 붙는다(전수 AI 리뷰 지적, S15P21B202-136).
            // ?? 는 UnityEngine.Object 의 == 오버로드를 우회하므로 쓰지 않고 == null 로 검사한다.
            var hud = GetComponent<LobbyHud>();
            if (hud == null) hud = gameObject.AddComponent<LobbyHud>();
            hud.Initialize(network, defaultNickname, deviceIdOverride, defaultServerAddress);
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

            var manager = Mirror.NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[LobbyBootstrap] NetworkManager 가 없어 매치에 들어갈 수 없다.", this);
                return;
            }

            // 게임 서버가 Spring 에 물어볼 때 쓸 값. 토큰은 여기 말고 어디에도 남기지 않는다.
            MatchAuthenticator.ClientToken = network.AccessToken;
            MatchAuthenticator.ClientRoomId = network.CurrentRoomId;

            // 이 방을 맡은 게 나라면 내가 게임 서버가 된다(S15P21B202-263).
            // 물리와 판정이 내 PC 에서 돌고, 참가자들이 나에게 붙는다.
            if (network.IsRoomHost)
            {
                joiningMatch = true;
                manager.StartHost();
                Debug.Log("[LobbyBootstrap] 방장이므로 이 PC 가 게임 서버가 된다.");
                return;
            }

            // 참가자는 방장이 알려 준 곳으로 붙는다. 그 값이 없으면(주소를 안 싣는 옛
            // 서버이거나, 전용 서버로 돌릴 때) 예전처럼 로비와 같은 호스트로 붙는다.
            string host = NetworkSession.HostOf(network.MatchAddress);
            if (string.IsNullOrEmpty(host)) host = NetworkSession.HostOf(network.ServerAddress);
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            manager.networkAddress = host;

            joiningMatch = true;
            manager.StartClient();
            Debug.Log("[LobbyBootstrap] 참가자로 " + host + " 에 붙는다.");
        }

        void OnDestroy()
        {
            if (network != null)
                network.GameStarted -= EnterMatch;
        }
    }
}
