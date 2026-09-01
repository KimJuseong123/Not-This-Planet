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

            // 게임 서버는 로비와 같은 PC 에 있고 포트만 다르다. 로비에 입력한 호스트를 그대로 쓴다.
            // 서버가 GAME_STARTED 에 주소를 실어 보내게 되면 그 값을 우선하도록 바꾼다.
            string host = NetworkSession.HostOf(network.ServerAddress);
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            manager.networkAddress = host;

            // 게임 서버가 Spring 에 물어볼 때 쓸 값. 토큰은 여기 말고 어디에도 남기지 않는다.
            MatchAuthenticator.ClientToken = network.AccessToken;
            MatchAuthenticator.ClientRoomId = network.CurrentRoomId;

            joiningMatch = true;
            manager.StartClient();
        }

        void OnDestroy()
        {
            if (network != null)
                network.GameStarted -= EnterMatch;
        }
    }
}
