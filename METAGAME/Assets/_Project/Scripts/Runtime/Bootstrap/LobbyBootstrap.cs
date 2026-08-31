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
    /// 씬을 Assets/Scenes/Main.unity 하나로 합치면서(S15P21B202-75) 로비와 게임이
    /// 같은 씬에 있게 됐다. 그래서 넘어갈 다른 씬이 없다.
    /// 지금은 대상 씬이 이미 열려 있으면 다시 로드하지 않고 경고만 남긴다.
    /// 씬을 다시 나눌지, 같은 씬 안에서 상태로 전환할지는 Network 파트에서 정한다.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        [Header("Backend")]
        [Tooltip("로비 입력칸의 기본값. 저장된 주소가 있으면 그쪽이 우선한다. 형식은 호스트[:포트].")]
        [SerializeField] private string defaultServerAddress = NetworkSession.DefaultServerAddress;
        [SerializeField] private string httpBaseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string webSocketUrl = "ws://127.0.0.1:8080/ws/game";

        [Header("Scene")]
        [Tooltip("게임 시작 시 넘어갈 씬. 지금은 로비와 게임이 같은 씬이라 전환이 일어나지 않는다.")]
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

            // 로비와 게임이 같은 씬에 있으면 다시 로드하면 방금 만든 세션까지 초기화된다.
            if (SceneManager.GetActiveScene().name == gameSceneName)
            {
                Debug.LogWarning(
                    $"[LobbyBootstrap] 게임 씬('{gameSceneName}')이 이미 열려 있어 전환을 건너뛴다. " +
                    "로비와 게임이 한 씬에 있으므로 전환 방식을 다시 정해야 한다.", this);
                return;
            }

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
