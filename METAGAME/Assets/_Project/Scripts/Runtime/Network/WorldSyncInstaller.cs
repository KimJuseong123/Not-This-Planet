using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// <see cref="WorldSync"/> 를 Mirror 에 등록하고, 서버가 게임 씬에 들어갈 때 하나 띄운다
    /// (S15P21B202-152).
    ///
    /// <b>프리팹을 쓰지 않는다.</b> 처음에는 프리팹 에셋을 만들어 Resources 에서 읽었는데,
    /// 그 프리팹이 리눅스 헤드리스 서버를 통째로 내렸다 — 로비 씬만 띄워도
    /// <c>ThreadLog initialized</c> 직후 SIGSEGV 로 죽었다(-162 실측). 네이티브에서
    /// 죽는 것이라 C# 의 try/catch 로도 막을 수 없었다.
    ///
    /// Mirror 는 프리팹 대신 <b>스폰 핸들러</b>를 등록할 수 있다. 번호(assetId)만 정해 두면
    /// 서버는 그 번호로 스폰하고, 클라이언트는 같은 번호를 보고 여기 적힌 코드로 만든다.
    /// 에셋 파일이 아예 없으니 읽다가 죽을 일도 없고, 로비 씬의 스폰 목록을 건드리지
    /// 않아도 된다.
    /// </summary>
    public static class WorldSyncInstaller
    {
        /// <summary>
        /// 서버와 클라이언트가 같은 것을 가리키기 위한 번호. 프리팹의 GUID 를 대신한다.
        /// 아무 값이나 되지만 다른 스폰 대상과 겹치면 안 된다.
        /// </summary>
        const uint AssetId = 0x57530152;   // 'W''S' + 이슈 번호 152

        static WorldSync live;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            RegisterHandler();

            // 감시자를 우리 오브젝트에 둔다. 예전에는 NetworkManager.singleton 에 붙였는데,
            // 그 값이 이 시점에 없으면 감시자가 아예 만들어지지 않고 월드 동기화가 통째로
            // 조용히 빠진다 — 실제로 그랬다(2026-09-03 실측: 서버·클라이언트 모두
            // [WorldSync] 로그가 한 줄도 없었다). 스스로 만들면 그 의존이 사라진다.
            var watcher = new GameObject("~월드 동기화 감시");
            Object.DontDestroyOnLoad(watcher);
            watcher.AddComponent<WorldSyncSpawner>();
            Debug.Log("[WorldSync] 감시자 설치 완료");
        }

        /// <summary>
        /// 스폰 핸들러를 등록한다. <b>매 접속마다 다시 불러야 한다</b>(S15P21B202-315).
        ///
        /// <para><c>NetworkClient.Shutdown()</c> 이 <c>ClearSpawners()</c> 를 부르기 때문에
        /// 접속이 끊기면 이 등록이 사라진다. 그런데 <see cref="Install"/> 은
        /// <c>RuntimeInitializeOnLoadMethod</c> 라 <b>프로세스당 한 번</b>만 돈다 — 그래서
        /// 두 번째 매치나 재접속에서는 핸들러가 없고, 서버가 띄운 WorldSync 가 클라이언트에
        /// 만들어지지 않는다. 게임을 껐다 켜야만 고쳐지는 종류의 고장이다.</para>
        ///
        /// <para>같은 번호로 다시 등록하는 것은 무해하다 — Mirror 가 덮어쓴다.</para>
        /// </summary>
        internal static void RegisterHandler()
        {
            NetworkClient.RegisterSpawnHandler(AssetId, SpawnOnClient, UnSpawnOnClient);
        }

        /// <summary>서버가 이것을 띄웠을 때 클라이언트가 만드는 것.</summary>
        static GameObject SpawnOnClient(SpawnMessage message)
        {
            return Build().gameObject;
        }

        static void UnSpawnOnClient(GameObject spawned)
        {
            if (spawned != null) Object.Destroy(spawned);
        }

        /// <summary>양쪽이 같은 코드로 만든다 — 에셋을 읽지 않는다.</summary>
        static WorldSync Build()
        {
            var go = new GameObject("~월드 동기화");

            // 꺼진 채로 만들어 부품을 다 붙인 뒤에 켠다.
            //
            // NetworkIdentity 는 Awake 에서 자기 밑의 NetworkBehaviour 목록을 굳힌다.
            // 켜진 오브젝트에 AddComponent 하면 그 자리에서 Awake 가 돌아 목록이 비어 있는
            // 채로 굳고, 뒤에 붙인 WorldSync 는 목록에 없어 OnStartServer·OnStartClient 를
            // 영영 받지 못한다 — 오브젝트 동기화가 통째로 조용히 빠졌다(2026-09-03 실측:
            // 스폰은 되는데 [WorldSync] 로그가 한 줄도 없었다).
            //
            // 꺼져 있으면 AddComponent 가 Awake 를 미루고, 켜는 순간 한꺼번에 돌아
            // Identity 가 WorldSync 를 제대로 본다.
            go.SetActive(false);
            go.AddComponent<NetworkIdentity>();
            var sync = go.AddComponent<WorldSync>();
            go.AddComponent<Stage1SequenceSync>();

            // 진행도 동기화도 같은 오브젝트에 얹는다 (S15P21B202-54). 여기 붙이면
            // 위 주석의 함정들(프리팹 SIGSEGV·핸들러 재등록·Awake 순서)을 그대로 피한다.
            // **반드시 SetActive(true) 앞에서** 붙일 것 — 위 설명과 같은 이유다.
            go.AddComponent<ProgressSync>();
            go.SetActive(true);

            return sync;
        }

        /// <summary>서버가 게임 씬에 들어간 뒤 한 번 띄운다.</summary>
        internal static void SpawnIfNeeded()
        {
            if (!NetworkServer.active || live != null)
            {
                Debug.LogWarning($"[WorldSync] 띄우지 않았다 · 서버켜짐={NetworkServer.active} · 이미있음={live != null}");
                return;
            }

            // 새 판이다 — 지난 판의 복귀 자리를 버린다 (S15P21B202-150).
            // 남겨 두면 판을 다시 시작한 직후 들어온 사람이 옛 자리에 선다.
            //
            // <b>이어받는 중이면 버리지 않는다</b> (S15P21B202-389). 새 판이 아니라 같은
            // 판이고, 방금 <c>NetPlayer.OnStopClient</c> 가 팀원들이 서 있던 자리를 여기
            // 적어 두었다 — 버리면 전원이 시작 지점으로 되돌아간다.
            if (!HostMigration.Taking) RejoinMemory.Clear();

            // 자리(색) 배정도 새로 시작한다 (S15P21B202-375). 남겨 두면 두 명만 남았는데
            // 2번·3번 자리를 쓰는 식이 된다.
            //
            // <b>이어받는 중이면 버리지 않는다</b> (S15P21B202-396). 위와 같은 이유다 —
            // 새 판이 아니라 같은 판이고, 방금 NetPlayer.OnStopClient 가 각자 쓰던 색을
            // 여기 적어 두었다. 버리면 접속 순서대로 다시 배정돼 사람마다 색이 바뀐다.
            if (!HostMigration.Taking) TeamSlots.Clear();

            live = Build();
            live.InitializeDustStormSeed(DustStormMatchSeed.EnsureServerMatch());
            NetworkServer.Spawn(live.gameObject, AssetId);
        }
    }

    /// <summary>
    /// 씬이 바뀌는 시점을 잡기 위한 얇은 컴포넌트. 게임 씬의 오브젝트들이 다 선 뒤에
    /// 띄워야 <see cref="WorldSync"/> 가 그것들을 찾을 수 있다.
    /// </summary>
    public sealed class WorldSyncSpawner : MonoBehaviour
    {
        bool spawned;

        float waited;
        bool warned;
        bool clientWasActive;

        void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;
            spawned = false;
            waited = 0f;
            warned = false;
        }

        void Update()
        {
            // 접속할 때마다 스폰 핸들러를 다시 등록한다 — 끊길 때 지워지기 때문이다.
            // 자세한 사정은 WorldSyncInstaller.RegisterHandler (S15P21B202-315).
            bool clientActive = NetworkClient.active;
            if (clientActive && !clientWasActive) WorldSyncInstaller.RegisterHandler();
            clientWasActive = clientActive;

            // 엔딩 씬에는 물리 월드가 없다. 네트워크 연결은 엔딩 시작 시각 공유용으로 유지한다.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path
                == Metagame.Gameplay.Parts.DemoEnding.ScenePath) return;

            if (spawned || !NetworkServer.active) return;
            // Chapter 2 owns its different machinery snapshot through Stage2Bridge.
            if (FindAnyObjectByType<Metagame.Gameplay.Stage2.Stage2Yard>() != null) return;

            // 게임 씬이 올라왔는지는 "옮길 것이 있는가" 로 판단한다. 씬 이름을 박아 두면
            // 스테이지가 늘 때마다 여기를 고쳐야 한다.
            //
            // **찾는 대상이 SpaceshipPart 여야 한다**(S15P21B202-315). 예전에는
            // TransportVehicle 을 찾았는데 그것은 **씬에 없는 것이 정상**이다 — 견인 장치는
            // 사람이 Part_1 을 해제한 뒤 런타임에 AddComponent 로 생긴다
            // (PartTractorActivator·TractorBeamController). 저장소의 QA 검사도 그것을 못박는다:
            // Stage1QaAudit 의 "씬 시작 시 별도 TransportVehicle 없음".
            //
            // 그래서 이 문지기는 **영영 열리지 않았다.** WorldSync 가 한 번도 스폰되지 않아
            // 월드 동기화가 통째로 빠진 채 게임이 돌았고, 두 사람이 각자 물리를 굴려
            // 물체가 서로 다르게 보였다(2026-09-07 실측: 방장 로그에 "20초째 옮길 것을
            // 찾지 못했다" 만 남고 그 뒤 아무 일도 없었다).
            //
            // SpaceshipPart 는 씬에 실제로 있고(Main.unity 4기) Collect 가 주로 추적하는
            // 대상이기도 하다. 혼자 테스트하면 이 결함이 보이지 않는다 — 호스트는 자기
            // 물리가 곧 정답이라 화면이 멀쩡하다.
            if (FindAnyObjectByType<Metagame.Gameplay.Parts.SpaceshipPart>() == null)
            {
                // 영영 못 찾으면 월드 동기화가 조용히 빠진 채로 게임이 돌아간다.
                // 오브젝트가 서로 다르게 보이는데 로그에는 아무 단서가 없어 찾기 어렵다.
                waited += Time.deltaTime;
                if (waited > 20f && !warned)
                {
                    warned = true;
                    Debug.LogWarning("[WorldSync] 20초째 SpaceshipPart 를 찾지 못했다 — "
                                     + "월드 동기화가 시작되지 않는다. 게임 씬이 맞는지 확인할 것.");
                }
                return;
            }

            WorldSyncInstaller.SpawnIfNeeded();
            spawned = true;
        }
    }
}
