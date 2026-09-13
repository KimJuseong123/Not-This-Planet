using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using Metagame.UI;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Bootstrap
{
    public struct EndingSceneStateMessage : NetworkMessage
    {
        public string SnapshotJson;
        public bool Started;
        public double StartAt;
    }

    public struct EndingSceneReadyMessage : NetworkMessage { public bool Prepared; }

    /// <summary>씬과 무관한 엔딩 인계. 서버만 씬 이동 및 공통 재생 시작을 확정한다.</summary>
    public sealed class EndingSceneFlow : MonoBehaviour
    {
        public const string ScenePath = DemoEnding.ScenePath;
        public const string LobbyPath = "Assets/Scenes/Lobby.unity";
        public static EndingSceneFlow Instance { get; private set; }
        readonly HashSet<NetworkConnectionToClient> ready = new();
        DemoEnding source, playback;
        Stage1EndingBootstrap scene;
        string snapshotJson;
        bool transferring, started, prepared, requested, returning, toldBackend;
        bool clientRegistered, serverRegistered, savedAutoCreate, hasSavedAutoCreate;
        bool networked;
        double startAt;
        double lastClock;
        GUIStyle loadingStyle;

        public static EndingSceneFlow Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("Ending scene transfer").AddComponent<EndingSceneFlow>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            RegisterMessages();
        }

        public void Watch(DemoEnding ending)
        {
            source = ending;
            RegisterMessages();
        }

        public void AttachBeforeReady() => SuppressPlayerCreation();

        void RegisterMessages()
        {
            if (NetworkClient.active && !clientRegistered)
            {
                NetworkClient.RegisterHandler<EndingSceneStateMessage>(ReceiveState);
                clientRegistered = true;
            }
            if (NetworkServer.active && !serverRegistered)
            {
                NetworkServer.RegisterHandler<EndingSceneReadyMessage>(ReceiveReady);
                serverRegistered = true;
            }
            if (!NetworkClient.active) clientRegistered = false;
            if (!NetworkServer.active) serverRegistered = false;
        }

        void Update()
        {
            RegisterMessages();
            if (!transferring && source != null && source.Active
                && (NetworkServer.active || !NetworkClient.active)) Begin(source.Snapshot);
            if (scene == null || returning) return;

            // 씬을 늦게 로드하거나 엔딩 도중 재접속해도 서버의 보존된 스냅샷을 요청한다.
            if (!NetworkServer.active && NetworkClient.isConnected && !requested
                && NetworkClient.connection.isAuthenticated)
            {
                requested = true;
                NetworkClient.Send(new EndingSceneReadyMessage { Prepared = prepared });
            }
            if (NetworkServer.active && prepared && !started && AllParticipantsReady()) StartTogether();
            if (playback != null && playback.PlaybackStarted && playback.Finished) ReturnToLobby();
        }

        public void Begin(DemoEndingSnapshot snapshot)
        {
            if (transferring || (NetworkClient.active && !NetworkServer.active)) return;
            if (SceneUtility.GetBuildIndexByScenePath(ScenePath) < 0)
            {
                Debug.LogError("[EndingScene] 빌드 목록에 Stage1_Ending 씬이 없습니다.");
                return;
            }
            snapshotJson = JsonUtility.ToJson(snapshot);
            networked = NetworkServer.active || NetworkClient.active;
            transferring = true;
            SuppressPlayerCreation();
            if (NetworkServer.active)
                NetworkManager.singleton.ServerChangeScene(ScenePath);
            else SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
        }

        public void Attach(Stage1EndingBootstrap target)
        {
            scene = target;
            networked = NetworkServer.active || NetworkClient.active;
            transferring = true;
            SuppressPlayerCreation();
            RegisterMessages();
            // 에디터에서 엔딩 씬만 직접 실행할 때의 표시용 샘플. 실제 매치에는 사용하지 않는다.
            if (string.IsNullOrEmpty(snapshotJson) && !NetworkClient.active && !NetworkServer.active)
                snapshotJson = JsonUtility.ToJson(target.CreatePreview());
            if (!string.IsNullOrEmpty(snapshotJson)) PreparePlayback();
            if (!NetworkClient.active && !NetworkServer.active && prepared) StartTogether();
        }

        void SuppressPlayerCreation()
        {
            var manager = NetworkManager.singleton;
            if (manager == null || hasSavedAutoCreate) return;
            savedAutoCreate = manager.autoCreatePlayer;
            hasSavedAutoCreate = true;
            // 엔딩 씬에는 게임플레이 플레이어를 다시 스폰하지 않는다.
            manager.autoCreatePlayer = false;
        }

        void ReceiveReady(NetworkConnectionToClient connection, EndingSceneReadyMessage message)
        {
            if (!transferring || string.IsNullOrEmpty(snapshotJson)) return;
            if (message.Prepared) ready.Add(connection);
            connection.Send(StateMessage());
        }

        EndingSceneStateMessage StateMessage() => new()
        { SnapshotJson = snapshotJson, Started = started, StartAt = startAt };

        void ReceiveState(EndingSceneStateMessage message)
        {
            // 호스트는 자신의 서버 상태를 직접 사용한다.
            if (NetworkServer.active || string.IsNullOrEmpty(message.SnapshotJson)) return;
            if (string.IsNullOrEmpty(snapshotJson)) snapshotJson = message.SnapshotJson;
            started = message.Started;
            startAt = message.StartAt;
            if (scene != null && !prepared)
            {
                PreparePlayback();
                if (prepared) NetworkClient.Send(new EndingSceneReadyMessage { Prepared = true });
            }
            if (playback != null && started) playback.Snapshot.StartedAt = startAt;
        }

        void PreparePlayback()
        {
            var snapshot = JsonUtility.FromJson<DemoEndingSnapshot>(snapshotJson);
            if (snapshot?.Parts == null || snapshot.Parts.Length != 4
                || snapshot.Players == null || snapshot.Players.Length == 0)
            {
                Debug.LogError("[EndingScene] 엔딩 스냅샷이 올바르지 않습니다.");
                return;
            }
            snapshot.StartedAt = started ? startAt : double.PositiveInfinity;
            playback = scene.gameObject.AddComponent<DemoEnding>();
            playback.ConfigurePlayback(snapshot, Clock);
            if (!MatchRole.DedicatedServer)
            {
                var view = scene.gameObject.AddComponent<DemoEndingView>();
                view.Configure(playback, ReturnToLobby, scene.PartVisuals);
                view.Prepare();
            }
            prepared = true;
        }

        bool AllParticipantsReady()
        {
            foreach (var connection in NetworkServer.connections.Values)
                if (connection != NetworkServer.localConnection && connection.isAuthenticated
                    && !ready.Contains(connection)) return false;
            return true;
        }

        void StartTogether()
        {
            started = true;
            startAt = Clock() + (NetworkServer.active ? 1d : 0d);
            playback.Snapshot.StartedAt = startAt;
            if (NetworkServer.active) NetworkServer.SendToAll(StateMessage());
        }

        double Clock()
        {
            // 로비 복귀 중 연결이 먼저 종료돼도 참가자 로컬 실행 시간으로 갈아타지 않는다.
            // 늦게 접속한 참가자의 시계 원점은 서버와 다르므로 단계가 뒤로 돌아갈 수 있다.
            var now = networked
                ? (NetworkServer.active || NetworkClient.active ? NetworkTime.time : lastClock)
                : Time.realtimeSinceStartupAsDouble;
            lastClock = System.Math.Max(lastClock, now);
            return lastClock;
        }

        bool ReturnToLobby()
        {
            if (returning) return true;
            var lobby = NetworkSession.Instance;
            if (!toldBackend && lobby != null && lobby.IsConnected && lobby.IsRoomHost)
            {
                toldBackend = true;
                lobby.SendGameEnd();
            }
            if (MatchRole.DedicatedServer && NetworkServer.active)
            {
                returning = true;
                NetworkManager.singleton.ServerChangeScene(LobbyPath);
            }
            else returning = LobbyTransition.Ensure().BeginTitle();
            return returning;
        }

        void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single && loaded.path == LobbyPath) Destroy(gameObject);
        }

        void OnGUI()
        {
            if (!transferring || (started && Clock() >= startAt) || returning || MatchRole.DedicatedServer) return;
            var oldDepth = GUI.depth;
            var oldColor = GUI.color;
            GUI.depth = -9500;
            GUI.color = Color.black;
            var rect = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            loadingStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
            GUI.Label(rect, "엔딩 준비 중…", loadingStyle);
            GUI.depth = oldDepth;
            GUI.color = oldColor;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (hasSavedAutoCreate && NetworkManager.singleton != null)
                NetworkManager.singleton.autoCreatePlayer = savedAutoCreate;
            if (clientRegistered) NetworkClient.UnregisterHandler<EndingSceneStateMessage>();
            if (serverRegistered) NetworkServer.UnregisterHandler<EndingSceneReadyMessage>();
            Instance = null;
        }
    }
}
