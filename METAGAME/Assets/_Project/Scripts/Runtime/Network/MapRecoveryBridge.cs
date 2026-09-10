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
            if (NetworkServer.active || !NetworkClient.active)
                recovery.Tick();
        }

        void ReloadScene()
        {
            string scenePath = gameObject.scene.path;
            if (NetworkServer.active)
            {
                var manager = NetworkManager.singleton;
                if (manager == null)
                {
                    Debug.LogError("[MapRecovery] 서버의 NetworkManager가 없어 씬을 재시작할 수 없습니다.");
                    return;
                }
                // Mirror가 새 씬 Ready 후 플레이어를 다시 만들도록 기존 접속 객체만 제거한다.
                // 연결·방은 유지하며 옛 플레이어의 복귀 위치는 새 판에 넘기지 않는다.
                var connections = new List<NetworkConnectionToClient>(NetworkServer.connections.Values);
                foreach (var connection in connections)
                    if (connection.identity != null)
                        NetworkServer.RemovePlayerForConnection(connection, RemovePlayerOptions.Destroy);
                RejoinMemory.Clear();
                manager.ServerChangeScene(scenePath);
                return;
            }
            if (!NetworkClient.active)
                SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
        }
    }
}
