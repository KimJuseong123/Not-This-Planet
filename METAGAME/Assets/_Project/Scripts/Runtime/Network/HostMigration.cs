using System.Collections;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 방장이 사라지면 남은 사람 중 하나가 이어받아 <b>그 판을 계속한다</b>
    /// (S15P21B202-389).
    ///
    /// <b>Mirror 는 서버를 옮기지 못한다.</b> 서버를 켠 프로세스가 곧 서버다. 그래서
    /// 이어받는다는 것은 실제로 <b>새 방장이 서버를 새로 켜고 남은 사람들이 거기 다시
    /// 붙는 것</b>이다. 그 사이 몇 초는 끊기고, 그것을 없앨 방법은 없다.
    ///
    /// <b>그런데 세계를 옮길 필요는 없다.</b> 처음에는 지금 상태를 자료로 떠서 새 씬에
    /// 다시 세우는 것을 생각했는데, 그럴 이유가 없었다 —
    /// <c>NetworkManager.IsServerOnlineSceneChangeNeeded()</c> 가
    /// <c>!Utils.IsSceneActive(onlineScene)</c> 를 보므로, <b>게임 씬이 이미 떠 있으면
    /// StartHost 가 씬을 다시 열지 않는다</b>(NetworkManager.cs 281·496행). 참가자 쪽도
    /// 같은 판정이 걸려 있다(1236행). 씬이 안 내려가면 부품·용접·조립·도구·운석이
    /// 메모리에 그대로 남는다 — 옮길 것이 없다.
    ///
    /// <b>그것이 -386·-388 이 필요했던 이유다.</b> 참가자 화면의 부품은 자세만 따라가고
    /// 관절이 없어서 "보이는 것" 이었을 뿐 "물리" 가 아니었다. 용접 상태(-386)와 조립
    /// 연결·속도(-388)가 오면서 그 화면이 <b>이어받을 수 있는 세계</b>가 되었다.
    ///
    /// <b>로비를 거치지 않는다.</b> <c>StopClient</c> 는 <c>offlineScene</c> 을 열어
    /// 로비로 내려보낸다(NetworkManager.cs 1303행) — 그러면 씬이 내려가 세계가 사라진다.
    /// 그래서 이전하는 동안만 그 값을 비우고, 끝나면 되돌린다.
    ///
    /// <b>이 컴포넌트는 게임 씬에만 있다.</b> 그래서 여기서 받는 <c>GAME_STARTED</c> 는
    /// 언제나 이전이다 — 처음 시작은 로비의 <c>LobbyBootstrap</c> 이 받는다. 백엔드가
    /// 이전용 메시지를 따로 만들지 않아도 되는 이유가 이것이다.
    /// </summary>
    public sealed class HostMigration : MonoBehaviour
    {
        /// <summary>
        /// 이전을 기다리는 중인가. <see cref="HostLinkWatch"/> 가 참을성을 늘리는 데 쓴다.
        ///
        /// <para>방장이 바뀐 것은 <b>로비 웹소켓</b>으로 안다 — 그 연결은 Spring 에 붙어
        /// 있어 방장의 PC 와 무관하게 살아 있다. 그래서 "잠깐 끊긴 것" 과 "방장이 정말
        /// 사라진 것" 을 게임 연결의 침묵만으로 추측하지 않아도 된다.</para>
        /// </summary>
        public static bool Pending { get; private set; }

        /// <summary>
        /// 이 기계가 물리를 이어받는 중인가. <see cref="WorldSync"/> 가
        /// <c>OnStopClient</c> 에서 본다 — 그 컴포넌트는 연결이 끊길 때 사라지므로,
        /// 넘겨받을 준비는 사라지기 전에 그 안에서 해야 한다.
        /// </summary>
        public static bool Taking { get; private set; }

        /// <summary>
        /// 새 접속 정보를 이만큼 기다린다(초). 넘기면 포기하고 로비로 간다.
        ///
        /// <para>새 방장도 함께 죽는 경우가 있다. 그때는 아무 소식이 오지 않으므로
        /// 언젠가는 포기해야 한다. 백엔드가 방장을 넘기는 것은 끊김을 감지한 즉시이고
        /// (유예를 기다리지 않는다), 거기서 릴레이 세션을 새로 여는 데 몇 초가 걸릴 수
        /// 있어 넉넉히 잡았다.</para>
        /// </summary>
        public const float WaitForTakeover = 20f;

        /// <summary>
        /// 이어받은 직후 이만큼은 맵 이탈 복구를 쉰다(초) — S15P21B202-401.
        ///
        /// <para>넘겨받는 순간 세계가 그림에서 물리로 바뀐다. 조인트가 걸리고 속도가
        /// 들어가는 그 몇 프레임은 물체가 튈 수 있고, 그때 <c>MapRecovery</c> 가
        /// "부품이 맵을 벗어났다" 로 읽으면 <b>씬을 통째로 다시 로드해</b> 판을 날린다.
        /// 물리가 다시 맞물릴 틈을 준다.</para>
        /// </summary>
        public const float SettleAfterTakeover = 3f;

        static float settleUntil;

        /// <summary>이어받은 직후라 복구 판정을 미뤄야 하는가.</summary>
        public static bool Settling => Time.time < settleUntil;

        NetworkSession session;
        long knownHost;
        bool migrating;
        float waited;

        void OnEnable()
        {
            Pending = false;
            Taking = false;

            session = NetworkSession.Instance;
            if (session == null) return;

            knownHost = session.CurrentRoom != null ? session.CurrentRoom.HostUserId : 0L;
            session.RoomStateChanged += OnRoomState;
            session.GameStarted += OnGameStarted;
        }

        void OnDisable()
        {
            if (session != null)
            {
                session.RoomStateChanged -= OnRoomState;
                session.GameStarted -= OnGameStarted;
            }

            Pending = false;
            Taking = false;
        }

        /// <summary>
        /// 방 상태가 왔다. 방장이 바뀌었으면 이전이 시작된 것이다.
        ///
        /// <para><b>내가 새 방장이면 내가 먼저 말을 걸어야 한다.</b> 참가자들이 붙을 주소는
        /// 나만 안다 — 서버가 보는 내 주소는 공인 IP 라 공유기가 밖에서 들어오는 연결을
        /// 막는다. <c>START_GAME</c> 이 <c>hostAddress</c> 를 싣는 것과 같은 이유다.</para>
        /// </summary>
        void OnRoomState(RoomSnapshot room)
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (room == null || session == null) return;
            if (!room.IsPlaying) return;

            long host = room.HostUserId;
            if (host == 0L || host == knownHost) return;

            long previous = knownHost;
            knownHost = host;

            Pending = true;
            waited = 0f;
            Debug.Log($"[HostMigration] 방장이 바뀌었다 — {previous} → {host} · 새 접속 정보를 기다린다");

            if (host == session.UserId) session.SendHostTakeover();
        }

        /// <summary>
        /// 이전이 오지 않는 것을 포기한다 — <b>방을 대기로 풀어 주고</b> 나간다.
        ///
        /// <para>새 방장까지 함께 죽으면 아무 소식이 오지 않는다. 그때 그냥 나가면 방이
        /// 영구히 <c>PLAYING</c> 으로 남고, 그러면 로비의 시작 버튼이 비활성이라
        /// (<c>room.IsWaiting</c> 조건) 아무도 다시 시작할 수 없다 — S15P21B202-385 가
        /// 고친 바로 그 증상이다.</para>
        ///
        /// <para>그래서 <c>GAME_END</c> 를 보낸다. 방장만 보낼 수 있는 메시지이고
        /// (<see cref="NetworkSession.SendGameEnd"/> 가 스스로 확인한다) 지금 방장은
        /// 새로 지목된 사람이므로, 전원이 불러도 <b>그 사람의 것만</b> 통한다.</para>
        ///
        /// <para><see cref="Pending"/> 을 내리면 <see cref="HostLinkWatch"/> 의 원래
        /// 5초 기준이 되살아나 곧바로 로비로 내려보낸다 — 여기서 씬을 직접 건드리지 않는다.</para>
        /// </summary>
        void Update()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (!Pending || migrating || session == null) return;

            waited += Time.deltaTime;
            if (waited < WaitForTakeover) return;

            Debug.LogWarning($"[HostMigration] {WaitForTakeover:0}초 동안 새 접속 정보가 오지 않았다 — "
                             + "판을 끝내고 로비로 돌아간다");

            session.SendGameEnd();
            Pending = false;
        }

        /// <summary>
        /// 새 접속 정보가 왔다. 이 씬에서 받는 <c>GAME_STARTED</c> 는 이전뿐이다.
        /// </summary>
        void OnGameStarted()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (migrating) return;
            migrating = true;
            StartCoroutine(Migrate());
        }

        IEnumerator Migrate()
        {
            var manager = NetworkManager.singleton;
            if (manager == null || session == null)
            {
                Debug.LogError("[HostMigration] NetworkManager 나 세션이 없어 이어받을 수 없다.");
                migrating = false;
                Pending = false;
                yield break;
            }

            bool becomingHost = session.IsRoomHost;

            // 이 동안만 offlineScene 을 비운다. 비우지 않으면 StopClient 가 로비를 열어
            // 씬이 내려가고, 이어받을 세계가 사라진다.
            string offline = manager.offlineScene;
            manager.offlineScene = string.Empty;

            Debug.Log($"[HostMigration] 이어받기 시작 · 내가 서버가 되는가={becomingHost}");

            // 이 표시를 보고 두 곳이 움직인다.
            //
            //  · <c>WorldSync.OnStopClient</c> — 물리를 넘겨받는다
            //  · <c>NetPlayer.OnStopClient</c> — 팀원이 서 있던 자리를 적어 둔다
            //
            // 둘 다 <b>사라지기 전에</b> 해야 하므로 StopClient 앞에 세운다. 그리고
            // <c>WorldSyncInstaller.SpawnIfNeeded</c> 가 이것을 보고 그 자리 기록을
            // 지우지 않으므로, <b>StartHost 가 끝날 때까지</b> 내리지 않는다.
            Taking = becomingHost;

            if (NetworkClient.active || NetworkServer.active) manager.StopClient();

            // 전송이 소켓을 정리할 틈을 준다. 같은 프레임에 다시 켜면 옛 소켓이 아직
            // 살아 있어 포트를 물고 있다.
            yield return null;
            yield return null;

            if (session.MatchRelay.HasValue) MatchTransport.UseRelay(manager, session.MatchRelay.Value);
            else MatchTransport.UseDirect(manager);

            if (becomingHost)
            {
                // 게임 씬이 이미 떠 있으므로 Mirror 가 씬을 다시 열지 않는다.
                manager.StartHost();
                Debug.Log("[HostMigration] 이제 내 PC 가 게임 서버다.");
            }
            else
            {
                string host = NetworkSession.HostOf(session.MatchAddress);
                if (string.IsNullOrEmpty(host)) host = NetworkSession.HostOf(session.ServerAddress);
                if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
                manager.networkAddress = host;

                manager.StartClient();
                Debug.Log(session.MatchRelay.HasValue
                    ? "[HostMigration] 새 방장에게 릴레이를 거쳐 붙는다."
                    : "[HostMigration] 새 방장 " + host + " 에 직접 붙는다.");
            }

            // 여기서 내린다 — SpawnIfNeeded 가 자리 기록을 지키는 것까지 끝났다.
            Taking = false;

            // 물리가 다시 맞물릴 틈을 준다 — SettleAfterTakeover 의 주석 참고.
            settleUntil = Time.time + SettleAfterTakeover;

            manager.offlineScene = offline;
            Pending = false;
            migrating = false;
        }
    }
}
