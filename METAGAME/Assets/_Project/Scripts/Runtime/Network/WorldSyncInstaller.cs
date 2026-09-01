using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// <see cref="WorldSync"/> 를 Mirror 에 등록하고, 서버가 게임 씬에 들어갈 때 하나 띄운다
    /// (S15P21B202-152).
    ///
    /// 프리팹을 NetworkManager 의 Spawnable Prefabs 목록에 넣지 않고 코드로 등록한다.
    /// 그 목록은 로비 씬 안에 있어 편집하려면 에디터가 필요한데, 여기서 하면 씬을
    /// 건드리지 않고도 같은 일이 된다 — 씬 파일 충돌도 그만큼 줄어든다.
    /// </summary>
    public static class WorldSyncInstaller
    {
        const string PrefabPath = "Network/PF_WorldSync";

        static GameObject prefab;
        static WorldSync live;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[WorldSync] 프리팹을 찾지 못했다: Resources/{PrefabPath}");
                return;
            }

            // 클라이언트는 서버가 이것을 띄웠을 때 무엇을 만들지 알아야 한다.
            NetworkClient.RegisterPrefab(prefab);

            NetworkManager.singleton?.gameObject.AddComponent<WorldSyncSpawner>();
        }

        /// <summary>서버가 게임 씬에 들어간 뒤 한 번 띄운다.</summary>
        internal static void SpawnIfNeeded()
        {
            if (!NetworkServer.active || prefab == null) return;
            if (live != null) return;

            var go = Object.Instantiate(prefab);
            go.name = "~월드 동기화";
            NetworkServer.Spawn(go);
            live = go.GetComponent<WorldSync>();
        }
    }

    /// <summary>
    /// 씬이 바뀌는 시점을 잡기 위한 얇은 컴포넌트. 게임 씬의 오브젝트들이 다 선 뒤에
    /// 띄워야 <see cref="WorldSync"/> 가 그것들을 찾을 수 있다.
    /// </summary>
    public sealed class WorldSyncSpawner : MonoBehaviour
    {
        bool spawned;

        void Update()
        {
            if (spawned || !NetworkServer.active) return;

            // 게임 씬이 올라왔는지는 "옮길 것이 있는가" 로 판단한다. 씬 이름을 박아 두면
            // 스테이지가 늘 때마다 여기를 고쳐야 한다.
            if (FindAnyObjectByType<Metagame.Gameplay.Parts.TransportVehicle>() == null) return;

            WorldSyncInstaller.SpawnIfNeeded();
            spawned = true;
        }
    }
}
