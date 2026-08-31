using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Network;
using Metagame.UI;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// 로비의 조합 루트. 네트워크 세션을 씬보다 오래 살리고,
    /// 게임 시작 메시지를 받으면 게임 씬으로 넘긴다.
    ///
    /// 로비는 Assets/Scenes/Lobby.unity, 게임은 Assets/Scenes/Main.unity 다.
    /// 씬을 Main 하나로 합치면서(S15P21B202-75) 로비 씬이 사라져 전환이 막혀 있었고,
    /// S15P21B202-125에서 로비 씬을 되살려 전환을 복구했다.
    /// 세션 오브젝트는 DontDestroyOnLoad라 씬이 바뀌어도 연결이 유지된다.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        [Header("Backend")]
        [Tooltip("로비 입력칸의 기본값. 저장된 주소가 있으면 그쪽이 우선한다. 형식은 호스트[:포트].")]
        [SerializeField] private string defaultServerAddress = NetworkSession.DefaultServerAddress;
        [SerializeField] private string httpBaseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string webSocketUrl = "ws://127.0.0.1:8080/ws/game";

        [Header("Scene")]
        [Tooltip("게임 시작 시 넘어갈 씬. 빌드 설정에 포함되어 있어야 한다.")]
        [SerializeField] private string gameSceneName = "Main";

        [Header("Guest")]
        [SerializeField] private string defaultNickname = "비행사";
        [Tooltip("비워 두면 설치별 PlayerPrefs GUID를 사용합니다.")]
        [SerializeField] private string deviceIdOverride;

        private NetworkSession network;
        private bool sceneLoading;

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
            network.GameStarted += LoadGameScene;

            // 같은 오브젝트가 재활성·재초기화되면 HUD 가 두 개 붙는다(전수 AI 리뷰 지적, S15P21B202-136).
            // ?? 는 UnityEngine.Object 의 == 오버로드를 우회하므로 쓰지 않고 == null 로 검사한다.
            var hud = GetComponent<LobbyHud>();
            if (hud == null) hud = gameObject.AddComponent<LobbyHud>();
            hud.Initialize(network, defaultNickname, deviceIdOverride, defaultServerAddress);
        }

        private void LoadGameScene()
        {
            if (sceneLoading) return;

            // 로비 씬에서만 의미가 있다. 게임 씬이 이미 열려 있다면 다시 로드해서는 안 된다 —
            // 씬을 새로 로드하면 방금 만든 방과 플레이어가 함께 초기화된다.
            if (SceneManager.GetActiveScene().name == gameSceneName)
                return;

            sceneLoading = true;
            SceneManager.LoadScene(gameSceneName);
        }

        void OnDestroy()
        {
            if (network != null)
                network.GameStarted -= LoadGameScene;
        }
    }
}
