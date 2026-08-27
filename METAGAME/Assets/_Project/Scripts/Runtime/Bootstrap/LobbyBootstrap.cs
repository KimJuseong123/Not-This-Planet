using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Network;
using Metagame.UI;

namespace Metagame.Bootstrap
{
    /// <summary>
    /// Lobby 씬의 조합 루트. 네트워크 세션을 씬보다 오래 살리고,
    /// 게임 시작 메시지를 받으면 Surface 씬으로 넘긴다.
    /// </summary>
    public sealed class LobbyBootstrap : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private string httpBaseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string webSocketUrl = "ws://127.0.0.1:8080/ws/game";

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

            var hud = gameObject.AddComponent<LobbyHud>();
            hud.Initialize(network, defaultNickname, deviceIdOverride);
        }

        private void LoadGameScene()
        {
            if (sceneLoading) return;
            sceneLoading = true;
            SceneManager.LoadScene("Surface");
        }

        void OnDestroy()
        {
            if (network != null)
                network.GameStarted -= LoadGameScene;
        }
    }
}
