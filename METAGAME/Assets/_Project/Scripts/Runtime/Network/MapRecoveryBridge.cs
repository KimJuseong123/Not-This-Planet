using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;

namespace Metagame.Network
{
    /// <summary>복구 권한 및 연결을 유지하는 게임 씬 재로드.</summary>
    public sealed class MapRecoveryBridge : MonoBehaviour
    {
        MapRecovery recovery;
        static AsyncOperation _localReload;
        public static bool IsReloading => NetworkServer.isLoadingScene || NetworkClient.isLoadingScene
            || (NetworkManager.loadingSceneAsync != null && !NetworkManager.loadingSceneAsync.isDone)
            || (_localReload != null && !_localReload.isDone);

        public void Configure(MapRecovery target)
        {
            if (recovery != null) recovery.SceneResetRequested -= ReloadScene;
            recovery = target;
            recovery.SceneResetRequested += ReloadScene;
        }

        void OnDestroy()
        {
            if (recovery != null) recovery.SceneResetRequested -= ReloadScene;
        }

        void FixedUpdate()
        {
            if (recovery == null) return;

            // 이어받는 중에는 아무도 권한이 없다 (S15P21B202-401).
            //
            // <b>아래 조건은 이전 도중에 통과해 버린다.</b> <c>StopClient</c> 와
            // <c>StartHost</c> 사이에는 서버도 아니고 클라이언트도 아니므로
            // <c>!NetworkClient.active</c> 가 참이 되고, 그러면 "혼자 하는 판" 으로 오인해
            // 복구 판정을 돌린다. 그 판정이 부품 하나를 맵 밖으로 읽으면
            // <see cref="ReloadScene"/> 이 <b>씬을 통째로 다시 로드해</b> 진행 상황을
            // 날린다 — 2026-09-10 팀 테스트의 "게임은 되나 초기화" 가 이 모양이다.
            //
            // 넘겨받은 직후도 쉰다. 세계가 그림에서 물리로 바뀌는 몇 프레임은 물체가
            // 튈 수 있고, 그것은 부품을 잃은 것이 아니다.
            if (HostMigration.Pending || HostMigration.Taking || HostMigration.Settling) return;

            if (NetworkServer.active || !NetworkClient.active)
                recovery.Tick();
        }

        void ReloadScene()
        {
            TryReloadScene(gameObject.scene.path);
        }

        /// <summary>새 판은 플레이어 몸까지 다시 만든다. 연결과 방은 유지한다.</summary>
        public static bool TryReloadScene(string scenePath, System.Action onStarting = null)
        {
            if (HostMigration.Pending || HostMigration.Taking || HostMigration.Settling || IsReloading)
                return false;
            if (!Application.CanStreamedLevelBeLoaded(scenePath))
            {
                Debug.LogError("[MapRecovery] 빌드 목록에 재시작할 씬이 없습니다: " + scenePath);
                return false;
            }
            if (NetworkServer.active)
            {
                var manager = NetworkManager.singleton;
                if (manager == null)
                {
                    Debug.LogError("[MapRecovery] 서버의 NetworkManager가 없어 씬을 재시작할 수 없습니다.");
                    return false;
                }
                // Mirror가 새 씬 Ready 후 플레이어를 다시 만들도록 기존 접속 객체만 제거한다.
                // 연결·방은 유지하며 옛 플레이어의 복귀 위치는 새 판에 넘기지 않는다.
                onStarting?.Invoke();
                var connections = new List<NetworkConnectionToClient>(NetworkServer.connections.Values);
                foreach (var connection in connections)
                    if (connection.identity != null)
                        NetworkServer.RemovePlayerForConnection(connection, RemovePlayerOptions.Destroy);
                RejoinMemory.Clear();
                manager.ServerChangeScene(scenePath);
                return true;
            }
            if (!NetworkClient.active)
            {
                onStarting?.Invoke();
                _localReload = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                return _localReload != null;
            }
            return false;
        }
    }
}
