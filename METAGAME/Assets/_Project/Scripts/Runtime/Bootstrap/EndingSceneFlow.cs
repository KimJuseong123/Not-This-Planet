using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using Metagame.Gameplay.Audio;
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
    public struct EndingSceneAbortMessage : NetworkMessage { public string Reason; }
    public struct EndingSceneSnapshotChunk : NetworkMessage
    {
        public int Offset, TotalBytes;
        public byte[] Data;
    }

    /// <summary>씬과 무관한 엔딩 인계. 서버만 씬 이동 및 공통 재생 시작을 확정한다.</summary>
    public sealed class EndingSceneFlow : MonoBehaviour
    {
        public const string ScenePath = DemoEnding.ScenePath;
        public const string LobbyPath = "Assets/Scenes/Lobby.unity";
        public const double PreparationTimeoutSeconds = 45d;
        public const int MaximumSnapshotBytes = 8 * 1024 * 1024;
        public static EndingSceneFlow Instance { get; private set; }
        readonly HashSet<NetworkConnectionToClient> ready = new();
        DemoEnding source, playback;
        Stage1EndingBootstrap scene;
        string snapshotJson;
        byte[] snapshotBytes, incomingSnapshot;
        int receivedBytes;
        bool transferring, started, prepared, requested, returning, toldBackend;
        bool clientRegistered, serverRegistered, savedAutoCreate, hasSavedAutoCreate;
        bool networked;
        double startAt;
        double lastClock;
        double preparationBeganAt, failedAt;
        string failure;
        public bool IsTransferring => transferring;
        public string Failure => failure;
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
            networked = NetworkServer.active || NetworkClient.active;
            RegisterMessages(true);
        }

        public void AttachBeforeReady() => SuppressPlayerCreation();

        void RegisterMessages(bool force = false)
        {
            if (NetworkClient.active && (!clientRegistered || force))
            {
                NetworkClient.ReplaceHandler<EndingSceneStateMessage>(ReceiveState);
                NetworkClient.ReplaceHandler<EndingSceneAbortMessage>(message => Abort(message.Reason, false));
                NetworkClient.ReplaceHandler<EndingSceneSnapshotChunk>(ReceiveChunk);
                clientRegistered = true;
            }
            if (NetworkServer.active && (!serverRegistered || force))
            {
                NetworkServer.ReplaceHandler<EndingSceneReadyMessage>(ReceiveReady);
                serverRegistered = true;
            }
            if (!NetworkClient.active) clientRegistered = false;
            if (!NetworkServer.active) serverRegistered = false;
        }

        void Update()
        {
            RegisterMessages();
            if (!string.IsNullOrEmpty(failure))
            {
                if (!returning && Time.realtimeSinceStartupAsDouble - failedAt >= 3d) ReturnToLobby();
                return;
            }
            if (!transferring && source != null && source.Active
                && (NetworkServer.active || !networked)) Begin(source.Snapshot);
            if (transferring && !started && Time.realtimeSinceStartupAsDouble - preparationBeganAt >= PreparationTimeoutSeconds)
            {
                Abort("엔딩 준비 시간이 초과되었습니다. 연결 또는 씬 로딩을 확인해 주세요.");
                return;
            }
            if (scene == null || returning) return;

            // 씬을 늦게 로드하거나 엔딩 도중 재접속해도 서버의 보존된 스냅샷을 요청한다.
            if (!NetworkServer.active && NetworkClient.isConnected && !requested
                && NetworkClient.connection.isAuthenticated)
            {
                requested = true;
                NetworkClient.Send(new EndingSceneReadyMessage { Prepared = prepared });
            }
            if (NetworkServer.active && prepared && !started && AllParticipantsReady()) StartTogether();
            if (playback != null && playback.PlaybackStarted && playback.Finished) ContinueToStage2();
        }

        public void Begin(DemoEndingSnapshot snapshot)
        {
            if (transferring || ((networked || NetworkClient.active) && !NetworkServer.active)) return;
            if (SceneUtility.GetBuildIndexByScenePath(ScenePath) < 0)
            {
                Abort("빌드 목록에 Stage1_Ending 씬이 없습니다.");
                return;
            }
            snapshotJson = JsonUtility.ToJson(snapshot);
            snapshotBytes = System.Text.Encoding.UTF8.GetBytes(snapshotJson);
            if (snapshotBytes.Length > MaximumSnapshotBytes)
            { Abort("엔딩 정보가 전송 가능한 크기를 초과했습니다."); return; }
            networked = NetworkServer.active || NetworkClient.active;
            transferring = true;
            preparationBeganAt = Time.realtimeSinceStartupAsDouble;
            SuppressPlayerCreation();
            try
            {
                if (NetworkServer.active)
                {
                    if (NetworkManager.singleton == null) { Abort("엔딩 전환용 네트워크 관리자가 없습니다."); return; }
                    NetworkManager.singleton.ServerChangeScene(ScenePath);
                }
                else SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            }
            catch (System.Exception exception) { Abort("엔딩 씬을 열 수 없습니다: " + exception.Message); }
        }

        public void Attach(Stage1EndingBootstrap target)
        {
            scene = target;
            networked = NetworkServer.active || NetworkClient.active;
            if (!transferring) preparationBeganAt = Time.realtimeSinceStartupAsDouble;
            transferring = true;
            SuppressPlayerCreation();
            // StopClient/StartClient가 같은 프레임에 일어난 재접속에서도 사라진 핸들러를 복구한다.
            RegisterMessages(true);
            // 에디터에서 엔딩 씬만 직접 실행할 때의 표시용 샘플. 실제 매치에는 사용하지 않는다.
            if (string.IsNullOrEmpty(snapshotJson) && !NetworkClient.active && !NetworkServer.active)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                snapshotJson = JsonUtility.ToJson(target.CreatePreview());
#else
                Abort("완료된 게임의 엔딩 정보가 없습니다.");
                return;
#endif
            }
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
            if (!transferring || string.IsNullOrEmpty(snapshotJson) || !string.IsNullOrEmpty(failure)) return;
            if (message.Prepared) ready.Add(connection);
            else SendSnapshot(connection);
            connection.Send(StateMessage());
        }

        EndingSceneStateMessage StateMessage() => new()
        { Started = started, StartAt = startAt };

        void SendSnapshot(NetworkConnectionToClient connection)
        {
            snapshotBytes ??= System.Text.Encoding.UTF8.GetBytes(snapshotJson);
            // 문자열/전송 계층 한도보다 작은 신뢰성 메시지로 분할한다. UTF-8은 모두 모은 후 해독한다.
            int size = System.Math.Min(8192, NetworkMessages.MaxMessageSize(Channels.Reliable) - 32);
            if (size <= 0) { Abort("엔딩 정보를 전송할 네트워크 공간이 부족합니다."); return; }
            for (int offset = 0; offset < snapshotBytes.Length; offset += size)
            {
                var data = new byte[System.Math.Min(size, snapshotBytes.Length - offset)];
                System.Buffer.BlockCopy(snapshotBytes, offset, data, 0, data.Length);
                connection.Send(new EndingSceneSnapshotChunk { Offset = offset, TotalBytes = snapshotBytes.Length, Data = data });
            }
        }

        void ReceiveChunk(EndingSceneSnapshotChunk message)
        {
            if (NetworkServer.active || !string.IsNullOrEmpty(snapshotJson) || !string.IsNullOrEmpty(failure)) return;
            if (message.TotalBytes <= 0 || message.TotalBytes > MaximumSnapshotBytes || message.Data == null
                || message.Data.Length == 0 || message.Offset != receivedBytes
                || message.Offset > message.TotalBytes - message.Data.Length
                || (incomingSnapshot != null && incomingSnapshot.Length != message.TotalBytes))
            { Abort("엔딩 정보의 전송 순서 또는 크기가 올바르지 않습니다."); return; }
            incomingSnapshot ??= new byte[message.TotalBytes];
            System.Buffer.BlockCopy(message.Data, 0, incomingSnapshot, receivedBytes, message.Data.Length);
            receivedBytes += message.Data.Length;
            if (receivedBytes != incomingSnapshot.Length) return;
            snapshotJson = System.Text.Encoding.UTF8.GetString(incomingSnapshot);
            incomingSnapshot = null;
            if (scene == null) return;
            PreparePlayback();
            if (prepared && NetworkClient.isConnected) NetworkClient.Send(new EndingSceneReadyMessage { Prepared = true });
        }

        void ReceiveState(EndingSceneStateMessage message)
        {
            // 호스트는 자신의 서버 상태를 직접 사용한다.
            if (NetworkServer.active || !string.IsNullOrEmpty(failure)) return;
            if (started && !message.Started) return;
            if (string.IsNullOrEmpty(snapshotJson) && !string.IsNullOrEmpty(message.SnapshotJson)) snapshotJson = message.SnapshotJson;
            started = message.Started;
            startAt = message.StartAt;
            if (scene != null && !prepared && !string.IsNullOrEmpty(snapshotJson))
            {
                PreparePlayback();
                if (prepared) NetworkClient.Send(new EndingSceneReadyMessage { Prepared = true });
            }
            if (playback != null && started) playback.Snapshot.StartedAt = startAt;
        }

        void PreparePlayback()
        {
            if (prepared || !string.IsNullOrEmpty(failure)) return;
            try { BuildPlayback(); }
            catch (System.Exception exception) { Abort("엔딩 표시 준비에 실패했습니다: " + exception.Message); }
        }

        void BuildPlayback()
        {
            var snapshot = JsonUtility.FromJson<DemoEndingSnapshot>(snapshotJson);
            if (snapshot?.Parts == null || snapshot.Parts.Length != 4
                || snapshot.Players == null || snapshot.Players.Length == 0)
            {
                Abort("엔딩 스냅샷이 올바르지 않습니다.");
                return;
            }
            snapshot.StartedAt = started ? startAt : double.PositiveInfinity;
            playback = scene.gameObject.AddComponent<DemoEnding>();
            playback.ConfigurePlayback(snapshot, Clock);
            if (!MatchRole.DedicatedServer)
            {
                if (scene.PartVisuals == null || scene.PartVisuals.Length != 4)
                    throw new System.InvalidOperationException("파츠 표시 프리팹 4개가 필요합니다.");
                foreach (var prefab in scene.PartVisuals)
                    if (prefab == null) throw new System.InvalidOperationException("엔딩 파츠 표시 프리팹이 누락되었습니다.");
                var view = scene.gameObject.AddComponent<DemoEndingView>();
                view.Configure(playback, ContinueToStage2, scene.PartVisuals);
                view.Prepare();
                if (SceneMusicController.CanPlayLocally)
                    scene.gameObject.AddComponent<SceneMusicController>().ConfigureEnding(
                        Resources.Load<AudioManager>("Audio/Stage1AudioManager"), playback);
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

        bool ContinueToStage2()
        {
            if (returning) return true;
            if (playback == null || !playback.PlaybackStarted || !playback.Finished) return false;
            if (networked && !NetworkServer.active) return false;
            string path = Metagame.Core.StageCatalog.SceneFolder + Metagame.Core.StageCatalog.Stage2SceneName + ".unity";
            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
            { Abort("빌드 목록에 Stage2_Earth 씬이 없습니다."); return false; }
            var manager = NetworkManager.singleton;
            if (NetworkServer.active && manager == null) return false;
            returning = true;
            if (hasSavedAutoCreate && manager != null) manager.autoCreatePlayer = savedAutoCreate;
            hasSavedAutoCreate = false;
            if (NetworkServer.active) manager.ServerChangeScene(path);
            else SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            return true;
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
            if (returning && SceneManager.GetActiveScene().path == LobbyPath) Destroy(gameObject);
            return returning;
        }

        void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single && loaded.name == Metagame.Core.StageCatalog.Stage2SceneName)
            {
                if (hasSavedAutoCreate && NetworkManager.singleton != null)
                    NetworkManager.singleton.autoCreatePlayer = savedAutoCreate;
                hasSavedAutoCreate = false;
                Destroy(gameObject);
                return;
            }
            if (mode != LoadSceneMode.Single || loaded.path != LobbyPath) return;
            if (networked && transferring && !returning)
                Abort("엔딩 도중 서버 연결이 종료되었습니다. 로비로 돌아갑니다.", false);
            else Destroy(gameObject);
        }

        void Abort(string reason, bool broadcast = true)
        {
            if (!string.IsNullOrEmpty(failure)) return;
            failure = reason;
            transferring = true;
            failedAt = Time.realtimeSinceStartupAsDouble;
            Debug.LogWarning("[EndingScene] " + reason);
            if (broadcast && NetworkServer.active)
                NetworkServer.SendToAll(new EndingSceneAbortMessage { Reason = reason });
        }

        void OnGUI()
        {
            if (!transferring || (string.IsNullOrEmpty(failure) && started && Clock() >= startAt) || returning || MatchRole.DedicatedServer) return;
            var oldDepth = GUI.depth;
            var oldColor = GUI.color;
            GUI.depth = -9500;
            GUI.color = Color.black;
            var rect = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            loadingStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24, wordWrap = true };
            GUI.Label(rect, string.IsNullOrEmpty(failure) ? "엔딩 준비 중…" : failure + "\n잠시 후 로비로 이동합니다.", loadingStyle);
            GUI.depth = oldDepth;
            GUI.color = oldColor;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (hasSavedAutoCreate && NetworkManager.singleton != null)
                NetworkManager.singleton.autoCreatePlayer = savedAutoCreate;
            if (clientRegistered)
            {
                NetworkClient.UnregisterHandler<EndingSceneStateMessage>();
                NetworkClient.UnregisterHandler<EndingSceneAbortMessage>();
                NetworkClient.UnregisterHandler<EndingSceneSnapshotChunk>();
            }
            if (serverRegistered) NetworkServer.UnregisterHandler<EndingSceneReadyMessage>();
            Instance = null;
        }
    }
}
