using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>
    /// 로비에서 게임 씬으로 넘어갈 때 화면을 덮는 공용 fade/loading 연출.
    /// 씬을 넘나들어야 하므로 로비 오브젝트와 별도의 DontDestroyOnLoad 오브젝트로 산다.
    /// </summary>
    public sealed class LobbyTransition : MonoBehaviour
    {
        private const string GameSceneName = "Stage1_Astra";
        private const string TitleScenePath = "Assets/Scenes/Lobby.unity";
        private const float FadeOutSeconds = 0.35f;
        private const float LoadingHoldSeconds = 0.80f;
        private const float FadeInSeconds = 0.45f;
        private const float NetworkTimeoutSeconds = 10f;

        public static LobbyTransition Instance { get; private set; }

        /// <summary>네트워크 매치 전환 실패나 잘못된 씬 이름을 로비 UI에 알린다.</summary>
        public event Action<string> Failed;

        private Coroutine transitionRoutine;
        private float overlayAlpha;
        private bool busy;
        private GUIStyle loadingStyle;

        public static LobbyTransition Ensure()
        {
            if (Instance != null)
                return Instance;

            var host = new GameObject("~로비 전환");
            DontDestroyOnLoad(host);
            return host.AddComponent<LobbyTransition>();
        }

        public bool IsBusy => busy;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>혼자 하기 전환. 네트워크를 시작하지 않고 Stage1_Astra 씬을 직접 연다.</summary>
        public bool BeginSolo()
        {
            if (busy)
                return false;

            busy = true;
            transitionRoutine = StartCoroutine(LoadSoloRoutine());
            return true;
        }

        /// <summary>게임 씬에서 네트워크를 정리하고 로비 타이틀로 돌아간다.</summary>
        public bool BeginTitle()
        {
            if (busy)
                return false;

            busy = true;
            transitionRoutine = StartCoroutine(LoadTitleRoutine());
            return true;
        }

        /// <summary>
        /// GAME_STARTED 이후 fade-out을 먼저 끝낸 뒤 Mirror 호스트/클라이언트 시작을 호출한다.
        /// callback이 false를 반환하면 즉시 실패 화면으로 복귀한다.
        /// </summary>
        public bool BeginNetwork(Func<bool> startNetwork)
        {
            if (busy)
                return false;

            busy = true;
            transitionRoutine = StartCoroutine(StartNetworkRoutine(startNetwork));
            return true;
        }

        public bool BeginCustomizer()
        {
            if (busy) return false;
            busy = true;
            transitionRoutine = StartCoroutine(LoadSoloRoutine(CustomizerFlow.SceneName, false));
            return true;
        }

        private IEnumerator LoadSoloRoutine(string sceneName = GameSceneName, bool waitForGame = true)
        {
            yield return FadeTo(1f, FadeOutSeconds);
            yield return HoldLoading();

            AsyncOperation load = null;
            string loadError = null;
            try
            {
                load = SceneManager.LoadSceneAsync(sceneName);
            }
            catch (Exception exception)
            {
                loadError = "게임 씬을 열 수 없습니다: " + exception.Message;
            }

            if (!string.IsNullOrEmpty(loadError))
            {
                yield return FailRoutine(loadError);
                yield break;
            }

            if (load == null)
            {
                yield return FailRoutine("게임 씬을 열 수 없습니다.");
                yield break;
            }

            while (!load.isDone)
                yield return null;

            bool ready = !waitForGame;
            if (waitForGame) yield return WaitForGameScene(result => ready = result);
            if (!ready)
            {
                yield return FailRoutine("게임 씬 준비 시간이 초과되었습니다.");
                yield break;
            }

            yield return FadeTo(0f, FadeInSeconds);
            Finish();
        }

        private IEnumerator LoadTitleRoutine()
        {
            yield return FadeTo(1f, FadeOutSeconds);
            yield return HoldLoading();

            var manager = Mirror.NetworkManager.singleton;
            bool networkLoadsTitle = manager != null && !string.IsNullOrEmpty(manager.offlineScene)
                && (Mirror.NetworkClient.active || Mirror.NetworkServer.active);
            StopNetworkForTitle();

            // Mirror의 offlineScene이 먼저 Lobby를 열 수 있으므로 잠시 기다린 뒤,
            // 실제로 열리지 않은 경우에만 직접 로드한다.
            float deadline = Time.unscaledTime + NetworkTimeoutSeconds;
            while (networkLoadsTitle && !IsTitleSceneActive() && Time.unscaledTime < deadline)
                yield return null;

            if (!IsTitleSceneActive())
            {
                int titleBuildIndex = SceneUtility.GetBuildIndexByScenePath(TitleScenePath);
                if (titleBuildIndex < 0)
                {
                    yield return FailRoutine(
                        "빌드 설정에 타이틀 씬이 등록되지 않았습니다: " + TitleScenePath);
                    yield break;
                }

                AsyncOperation load = null;
                string loadError = null;
                try
                {
                    load = SceneManager.LoadSceneAsync(titleBuildIndex);
                }
                catch (Exception exception)
                {
                    loadError = "타이틀 씬을 열 수 없습니다 (" + TitleScenePath + "): " + exception.Message;
                }

                if (!string.IsNullOrEmpty(loadError) || load == null)
                {
                    yield return FailRoutine(string.IsNullOrEmpty(loadError)
                        ? "타이틀 씬을 열 수 없습니다." : loadError);
                    yield break;
                }

                while (!load.isDone)
                    yield return null;
            }

            yield return FadeTo(0f, FadeInSeconds);
            Finish();
        }

        private IEnumerator StartNetworkRoutine(Func<bool> startNetwork)
        {
            yield return FadeTo(1f, FadeOutSeconds);
            yield return HoldLoading();

            bool started = false;
            string startError = null;
            try
            {
                started = startNetwork != null && startNetwork();
            }
            catch (Exception exception)
            {
                startError = "게임 서버를 시작할 수 없습니다: " + exception.Message;
            }

            if (!string.IsNullOrEmpty(startError))
            {
                yield return FailRoutine(startError);
                yield break;
            }

            if (!started)
            {
                yield return FailRoutine("게임 서버를 시작할 수 없습니다.");
                yield break;
            }

            float deadline = Time.unscaledTime + NetworkTimeoutSeconds;
            bool ready = false;
            while (Time.unscaledTime < deadline)
            {
                if (IsGameSceneReady())
                {
                    ready = true;
                    break;
                }

                yield return null;
            }

            if (!ready)
            {
                yield return FailRoutine("게임 서버 연결 시간이 초과되었습니다.");
                yield break;
            }

            yield return FadeTo(0f, FadeInSeconds);
            Finish();
        }

        private IEnumerator WaitForGameScene(Action<bool> completed)
        {
            float deadline = Time.unscaledTime + NetworkTimeoutSeconds;
            while (Time.unscaledTime < deadline && !IsGameSceneReady())
                yield return null;

            completed?.Invoke(IsGameSceneReady());
        }

        private bool IsGameSceneReady()
        {
            if (!string.Equals(SceneManager.GetActiveScene().name, GameSceneName,
                    StringComparison.Ordinal))
                return false;

            return FindAnyObjectByType<GameSession>() != null;
        }

        private static bool IsTitleSceneActive()
        {
            return string.Equals(SceneManager.GetActiveScene().path, TitleScenePath,
                                 StringComparison.Ordinal);
        }

        private static void StopNetworkForTitle()
        {
            var manager = Mirror.NetworkManager.singleton;
            if (manager != null)
            {
                if (Mirror.NetworkServer.active && Mirror.NetworkClient.active)
                    manager.StopHost();
                else if (Mirror.NetworkClient.active)
                    manager.StopClient();
                else if (Mirror.NetworkServer.active)
                    manager.StopServer();
            }

            NetworkSession.Instance?.DisconnectLobby();
        }

        private IEnumerator HoldLoading()
        {
            float elapsed = 0f;
            while (elapsed < LoadingHoldSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator FailRoutine(string message)
        {
            yield return FadeTo(0f, FadeInSeconds);
            busy = false;
            transitionRoutine = null;
            Failed?.Invoke(message);
        }

        private IEnumerator FadeTo(float target, float duration)
        {
            float start = overlayAlpha;
            if (duration <= 0f)
            {
                overlayAlpha = target;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                overlayAlpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            overlayAlpha = target;
        }

        private void Finish()
        {
            busy = false;
            transitionRoutine = null;
        }

        private void OnGUI()
        {
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        void DrawScaledGui()
        {
            if (overlayAlpha <= 0.001f && !busy)
                return;

            if (loadingStyle == null)
            {
                loadingStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 27,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -10000;
            GUI.color = new Color(0f, 0f, 0f, overlayAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, UiScale.Width, UiScale.Height), Texture2D.whiteTexture);

            if (busy && overlayAlpha > 0.35f)
            {
                int dotCount = Mathf.FloorToInt(Time.unscaledTime * 2f) % 4;
                GUI.color = new Color(0.80f, 0.94f, 1f, Mathf.Clamp01(overlayAlpha + 0.15f));
                GUI.Label(new Rect(0f, UiScale.Height * 0.56f, UiScale.Width, 52f),
                    "LOADING" + new string('.', dotCount), loadingStyle);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void OnDestroy()
        {
            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);

            if (Instance == this)
                Instance = null;
        }
    }
}
