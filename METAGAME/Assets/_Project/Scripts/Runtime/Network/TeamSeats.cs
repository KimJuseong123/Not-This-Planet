using Mirror;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Network
{
    /// <summary>
    /// 자리 네 개의 <b>몸을 판 내내 살려 둔다</b>. 조종자만 갈아끼운다.
    ///
    /// <para><b>왜 필요한가.</b> 지금까지는 연결이 몸을 만들고, 끊기면 그 몸이 사라졌다.
    /// 그러면 들고 있던 도구가 떨어지고 탑승 상태가 풀린다. 네 자리를 고정하고 빈 자리를
    /// AI 가 채우려면 <b>몸이 연결보다 오래 살아야</b> 한다.</para>
    ///
    /// <para><b>서버에만 있다.</b> 참가자 화면에는 정할 것이 없다 — 몸의 모습은
    /// <see cref="NetPlayer"/> 의 SyncVar 를 타고 내려가고, 남의 몸은 각자
    /// <c>BuildRemoteBody</c> 가 그린다.</para>
    ///
    /// <para><b>방장 몸은 여기서 만들지 않는다.</b> 게임 씬(<c>SpaceBootstrap</c>)이 이미
    /// 세웠고 카메라·HUD·태블릿이 거기 붙어 있다. 대신 <see cref="RegisterHostBody"/> 로
    /// 그 몸을 자리에 등록해, 이 관리자가 같은 자리에 하나 더 만들지 않게 한다.</para>
    /// </summary>
    public sealed class TeamSeats : MonoBehaviour
    {
        /// <summary>사람이 나간 뒤 AI 가 이어받기까지 기다리는 시간(초).</summary>
        ///
        /// <para>짧으면 잠깐의 랙에도 AI 가 조종을 낚아채고, 길면 몸이 멍하니 서 있는다.
        /// <c>RejoinMemory.GraceSeconds</c>(30초)는 자리를 지켜 주는 시간이라 뜻이 다르다 —
        /// 그동안 AI 가 대신 움직여도 자리는 그대로 그 사람 것이다.</para>
        ///
        /// <para><b>사람이 받아 둔 자리에는 더 이상 쓰이지 않는다</b> (S15P21B202-713). 배정
        /// (<see cref="TeamSlots.IsAssigned"/>)은 판 내내 남아 사람 자리로 세므로, 끊긴 사람의
        /// 자리는 돌아올 때까지 비워 둔다. 이 시간은 AI 가 비운 자리에만 걸린다.</para>
        public const float AiTakeoverSeconds = 5f;

        /// <summary>자리끼리 떼어 놓는 거리(m). 같은 지점에 넷을 세우면 서로 밀어낸다.</summary>
        const float SeatSpacing = 1.5f;

        /// <summary>발이 바닥에 박히지 않게 띄우는 높이(m). <see cref="NetPlayer"/> 와 같은 값이다.</summary>
        const float FootLift = 0.10f;

        /// <summary>띄우기에 실패했을 때 다시 시도하기까지 쉬는 시간(초).</summary>
        const float RetrySeconds = 5f;

        /// <summary>
        /// 띄운 AI 가 이 시간 안에 자리를 못 잡으면 포기하고 다시 띄운다(초).
        ///
        /// <para>보통은 다음 물리 스텝에 잡는다. 이 값은 '영원히 매달려 있지 않게' 하는
        /// 안전장치라 넉넉히 둔다.</para>
        /// </summary>
        const float AiPendingTimeout = 10f;

        /// <summary>같은 '자리가 막혔다' 경고를 다시 찍기까지의 간격(초).</summary>
        const float BlockedLogInterval = 5f;

        /// <summary>서버에만 선다. 참가자에서는 늘 null 이다.</summary>
        public static TeamSeats Server { get; private set; }

        sealed class Seat
        {
            /// <summary>이 자리의 몸. 판 내내 같은 것을 쓴다.</summary>
            public PlayerRig Body;

            /// <summary>지금 이 몸을 모는 쪽. 없으면 아무도 안 몰고 있다.</summary>
            public NetPlayer Driver;

            /// <summary>비워진 시각(<c>Time.time</c>). 음수면 비어 있지 않다.</summary>
            public float FreeSince = -1f;

            /// <summary>
            /// 띄웠지만 아직 몸을 세우지 못한 AI.
            ///
            /// <para><see cref="NetPlayer"/> 는 게임 씬을 찾은 다음 물리 스텝에야 자리를
            /// 잡는다. 그 사이를 표시해 두지 않으면 매 프레임 같은 자리에 AI 를 하나씩
            /// 더 띄운다.</para>
            /// </summary>
            public NetPlayer AiPending;

            /// <summary>
            /// <see cref="AiPending"/> 을 띄운 시각. 만료를 재는 데 쓴다.
            ///
            /// <para>만료가 없으면, 띄운 AI 가 어떤 이유로든 자리를 못 잡을 때(게임 씬을
            /// 못 찾는 등) 그 표시가 영원히 남아 <b>자리가 조용히 계속 빈다.</b></para>
            /// </summary>
            public float AiPendingSince;

            /// <summary>자리가 막혔다고 마지막으로 알린 시각. 같은 경고를 쏟지 않으려고 센다.</summary>
            public float LastBlockedLog = -999f;
        }

        readonly Seat[] seats = new Seat[TeamSlots.Count];
        GameSession session;

        // 매 프레임 자리를 세는 데 쓰는 칸 (S15P21B202-713). 미리 만들어 둔다 — 서버의 Update 에서
        // 프레임마다 배열을 새로 만들면 쓰레기가 쌓여 GC 멈춤이 랙으로 보인다.
        readonly bool[] fillEligible = new bool[TeamSlots.Count];
        readonly bool[] humanSeat = new bool[TeamSlots.Count];
        readonly bool[] aiSeat = new bool[TeamSlots.Count];
        readonly int[] fillSlots = new int[TeamSlots.Count];

        /// <summary>넘치는 AI 를 이미 알렸나. 같은 경고를 매 프레임 쏟지 않으려고 센다.</summary>
        bool warnedExcess;

        /// <summary>
        /// 사람이 한 명이라도 앉은 적 있나.
        ///
        /// <para>이것 전에는 자리를 채우지 않는다. 판이 서자마자 네 자리를 AI 로 메우면
        /// 방장이 앉을 자리가 남지 않는다 — 방장의 <see cref="NetPlayer"/> 는 게임 씬이
        /// 몸을 세운 뒤에야 자리를 잡기 때문이다.</para>
        /// </summary>
        bool humanSeated;

        /// <summary>띄우기에 실패한 뒤 쉬는 시각. <see cref="Complain"/> 이 민다.</summary>
        float retryAfter;

        /// <summary>서버에 관리자를 세운다. 이미 있으면 그것을 돌려준다.</summary>
        public static TeamSeats EnsureServer(GameSession forSession)
        {
            if (!NetworkServer.active || forSession == null) return null;
            if (forSession.GetComponent<AiNavigationWorld>() == null)
                forSession.gameObject.AddComponent<AiNavigationWorld>();
            if (Server != null)
            {
                Server.session = forSession;
                return Server;
            }

            var go = new GameObject("팀 자리");
            Server = go.AddComponent<TeamSeats>();
            Server.session = forSession;
            for (int i = 0; i < Server.seats.Length; i++) Server.seats[i] = new Seat();
            Debug.Log($"[TeamSeats] 자리 {TeamSlots.Count}개를 연다");
            return Server;
        }

        void OnDestroy()
        {
            if (Server == this) Server = null;
        }

        /// <summary>
        /// 사람이 앉기 전에 그 자리의 AI 를 모두 내린다. <b>사람이 오는 모든 길에서 부른다</b>
        /// (<see cref="ClaimBody"/>, <see cref="RegisterHostBody"/>).
        ///
        /// <para>띄우기만 하고 아직 몸을 못 잡은 것(<see cref="Seat.AiPending"/>)까지 내린다.
        /// 빠뜨리면 그 AI 는 자리를 영영 못 잡고 매 물리 스텝 경고를 쏟는 좀비로 남으며,
        /// 표시가 남아 그 자리의 AI 이어받기도 조용히 멈춘다.</para>
        /// </summary>
        void EvictAi(Seat seat, int slot, NetPlayer newDriver, string nickname)
        {
            if (seat.AiPending != null && seat.AiPending != newDriver)
            {
                Debug.Log($"[TeamSeats] {slot}번 자리에 뜨던 AI 를 내린다 — 사람이 앉는다");
                NetworkServer.Destroy(seat.AiPending.gameObject);
            }
            seat.AiPending = null;

            if (seat.Driver == null || seat.Driver == newDriver || !seat.Driver.IsAiDriver) return;

            NetPlayer leaving = seat.Driver;

            // 자리를 먼저 넘긴다. 아래 Destroy 가 그 AI 의 OnStopServer 를 그 자리에서
            // 부르는데, 그때 자리가 아직 AI 것이면 방금 앉은 사람이 지워진다.
            seat.Driver = newDriver;
            Debug.Log($"[TeamSeats] {slot}번({TeamSlots.Name(slot)}) 자리를 사람이 이어받는다 · {nickname}");
            NetworkServer.Destroy(leaving.gameObject);
        }

        /// <summary>
        /// 그 자리의 몸을 내준다. 아직 없으면 지금 만든다.
        ///
        /// <para>이미 다른 조종자가 쥐고 있으면 <c>null</c> 을 돌려준다 — 한 몸을 둘이
        /// 몰면 입력이 뒤섞인다. 정상 흐름에서는 오지 않는 길이다(AI 를 먼저 내린 뒤
        /// 사람이 잡는다).</para>
        /// </summary>
        public PlayerRig ClaimBody(int slot, NetPlayer driver, bool human, string nickname)
        {
            Seat seat = At(slot);
            if (seat == null || driver == null) return null;

            // 사람이 오면 AI 는 비켜 준다. 자리 주인은 언제나 사람이다.
            //
            // <b>아직 몸을 못 잡은 AI(AiPending)도 같이 치운다.</b> 이것을 빠뜨리면 그 AI 는
            // 다음 물리 스텝부터 계속 null 을 받아 몸을 영영 못 세우고, 매 스텝 경고를 쏟는
            // 좀비로 남는다. 게다가 AiPending 이 그 AI 를 가리킨 채 남아 NeedsAi 가 늘 false 가
            // 되어, 나중에 그 자리가 진짜 비어도 AI 이어받기가 조용히 멈춘다.
            if (human) EvictAi(seat, slot, driver, nickname);

            if (seat.Driver != null && seat.Driver != driver)
            {
                // 사람 NetPlayer 는 null 을 받으면 다음 물리 스텝에 다시 온다. 매번 찍으면
                // 초당 50줄이 쏟아져 진짜 문제를 덮는다 — 간격을 두고 한 번씩만 알린다.
                if (Time.time - seat.LastBlockedLog >= BlockedLogInterval)
                {
                    seat.LastBlockedLog = Time.time;
                    Debug.LogWarning($"[TeamSeats] {slot}번 자리를 이미 {seat.Driver.name} 이(가) 쥐고 있다");
                }
                return null;
            }

            if (seat.Body == null)
            {
                Vector3 foot = SpawnFootFor(slot);
                seat.Body = PlayerRig.Create(
                    string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                    foot + Vector3.up * FootLift,
                    Quaternion.LookRotation(Vector3.right, Vector3.up),
                    TeamSlots.Suit(slot), TeamSlots.Accent(slot),
                    // 헤드리스 서버에는 모델을 씌우지 않는다. 그릴 일이 없고 로딩만 든다.
                    useModel: !Application.isBatchMode);
                Debug.Log($"[TeamSeats] {slot}번({TeamSlots.Name(slot)}) 몸을 세웠다 · {foot}");
            }

            seat.Driver = driver;
            seat.FreeSince = -1f;
            if (seat.AiPending == driver) seat.AiPending = null;
            if (human) humanSeated = true;
            return seat.Body;
        }

        /// <summary>
        /// 방장이 고른 수만큼만 빈 자리를 AI 로 채운다. 서버에서만 돈다.
        ///
        /// <para><b>예전에는 빈 자리를 전부 채웠다.</b> 수는 방(<c>ROOM_STATE</c> 의 aiCount)이
        /// 정하고 <see cref="AiHostSettings.RequestedCount"/> 로 받아 둔다(S15P21B202-713).
        /// 자리는 <see cref="AiSeatMath.SlotsToFill"/> 가 높은 번호부터 고른다.</para>
        /// </summary>
        void Update()
        {
            if (!NetworkServer.active || !humanSeated) return;
            if (!AiHostPolicy.CanFill(AiHostSettings.ServerState)) return;
            if (Time.time < retryAfter) return;

            for (int slot = 0; slot < seats.Length; slot++)
            {
                // NeedsAi 를 <b>먼저, 한 번만</b> 부른다. 오래 매달린 AiPending 을 여기서 치우므로,
                // 아래에서 AI 를 셀 때 이미 버린 것을 세지 않는다.
                fillEligible[slot] = NeedsAi(slot);

                Seat seat = seats[slot];
                // 끊겨 돌아올 사람의 자리(TeamSlots 에 남은 배정)도 사람 자리다 — AI 가 앉으면
                // 돌아온 사람이 AI 를 밀어내고, 그 AI 가 다른 자리에 다시 뜨며 색이 바뀐다.
                humanSeat[slot] = NetPlayer.HumanHoldsSlot(slot) || TeamSlots.IsAssigned(slot)
                                  || (seat.Driver != null && !seat.Driver.IsAiDriver);
                aiSeat[slot] = seat.AiPending != null || (seat.Driver != null && seat.Driver.IsAiDriver);
            }

            int requested = AiSeatMath.EffectiveRequest(AiHostSettings.RequestedCount,
                AiHostSettings.IdleTest && AiHostSettings.TestModeAvailable);
            int count = AiSeatMath.SlotsToFill(humanSeat, aiSeat, fillEligible, requested, fillSlots);
            for (int i = 0; i < count; i++)
            {
                SpawnAi(fillSlots[i]);
                // 띄우기에 실패했으면(Complain) 남은 자리도 같은 이유로 실패한다. 같은 오류를 한
                // 프레임에 여러 번 찍지 않고 쉰다.
                if (Time.time < retryAfter) break;
            }

            WarnIfExcess(requested);
        }

        /// <summary>
        /// 고른 수보다 AI 가 많으면 한 번 알린다. <b>내리지는 않는다</b> — 판 중에 수가 줄어드는
        /// 정상 경로가 없으므로(서버가 대기 중에만 바꾸게 한다) 내리는 길을 두면 그 길이 오히려
        /// 버그의 증상을 가린다.
        /// </summary>
        void WarnIfExcess(int requested)
        {
            int excess = AiSeatMath.Excess(humanSeat, aiSeat, requested);
            if (excess == 0)
            {
                warnedExcess = false;
                return;
            }
            if (warnedExcess) return;

            warnedExcess = true;
            Debug.LogWarning($"[TeamSeats] AI 가 {excess}명 더 떠 있다 — 고른 수 {requested}명. "
                             + "내리지 않고 그대로 둔다");
        }

        /// <summary>같은 오류를 매 프레임 쏟지 않도록 한 번 알리고 잠시 쉰다.</summary>
        void Complain(string message)
        {
            Debug.LogError("[TeamSeats] " + message + $" — {RetrySeconds}초 뒤 다시 시도한다");
            retryAfter = Time.time + RetrySeconds;
        }

        /// <summary>
        /// 그 자리에 사람 없는 <see cref="NetPlayer"/> 를 띄운다.
        ///
        /// <para>사람과 <b>같은 프리팹</b>을 쓴다. 그래야 색·이름표·쓰러짐·탑승이 사람과
        /// 같은 길로 내려가고, 참가자 화면에서 팀원과 구분되지 않는다.</para>
        ///
        /// <para>아직 두뇌는 붙이지 않는다 — 이 어셈블리는 AI 코드를 참조하지 않는다.
        /// 지금은 자리를 지키고 서 있기만 한다.</para>
        /// </summary>
        void SpawnAi(int slot)
        {
            GameObject prefab = NetworkManager.singleton != null
                ? NetworkManager.singleton.playerPrefab
                : null;
            if (prefab == null)
            {
                // 컴포넌트를 영구 정지시키지 않는다 — 그러면 나중에 정상이 되어도 자리
                // 채우기가 되살아나지 않고 조용히 4인 미만으로 진행된다. 대신 잠시 쉰다.
                Complain("플레이어 프리팹을 찾지 못해 AI 자리를 띄우지 못한다");
                return;
            }

            GameObject go = Instantiate(prefab);
            var player = go.GetComponent<NetPlayer>();
            if (player == null)
            {
                Complain("플레이어 프리팹에 NetPlayer 가 없다");
                Destroy(go);
                return;
            }

            // 스폰보다 먼저 정해야 처음 상태에 실려 내려간다.
            player.ServerConfigureAi((byte)slot, "동료 " + TeamSlots.Name(slot));

            // 주인 없이 띄운다. 어느 화면에서도 isLocalPlayer 가 아니므로 모두가
            // '남의 몸'으로 그린다 — 사람 팀원과 같은 경로다.
            NetworkServer.Spawn(go);

            // 자리는 NetPlayer 가 몸을 세울 때 잡는다. 여기서는 '띄우는 중'만 표시한다 —
            // 미리 잡아 버리면 몸을 못 세운 채 자리만 차지한다.
            seats[slot].AiPending = player;
            seats[slot].AiPendingSince = Time.time;
            seats[slot].FreeSince = -1f;
            Debug.Log($"[TeamSeats] {slot}번({TeamSlots.Name(slot)}) 자리를 AI 가 맡는다");
        }

        /// <summary>
        /// 조종자가 자리를 놓는다. <b>몸은 그대로 둔다.</b>
        ///
        /// <para>지금 쥔 사람이 아니면 아무것도 하지 않는다 — 사람이 들어와 AI 를 밀어낸
        /// 뒤 그 AI 의 <c>OnStopServer</c> 가 뒤늦게 부르는 경우가 있다. 그때 자리를 비우면
        /// 방금 들어온 사람의 몸이 주인 없는 것으로 표시된다.</para>
        /// </summary>
        public void ReleaseBody(int slot, NetPlayer driver)
        {
            Seat seat = At(slot);
            if (seat == null || driver == null || seat.Driver != driver) return;

            seat.Driver = null;
            seat.FreeSince = Time.time;
        }

        /// <summary>
        /// 게임 씬이 세운 방장 몸을 이 자리 것으로 등록한다.
        ///
        /// <para>등록해 두지 않으면 이 관리자가 같은 자리에 몸을 하나 더 만든다. 방장 몸은
        /// 여기서 만들지도 지우지도 않는다 — 카메라·HUD 가 그것을 보고 있다.</para>
        /// </summary>
        public void RegisterHostBody(int slot, NetPlayer driver, PlayerRig hostBody)
        {
            Seat seat = At(slot);
            if (seat == null || hostBody == null) return;

            // 방장도 사람이다 — ClaimBody 와 같게 AI 를 먼저 내린다. 안 내리면 그 AI 는
            // pooledBody 라 제 몸도 지우지 않아, 주인 없는 캐릭터가 씬에 남는다.
            EvictAi(seat, slot, driver, driver != null ? driver.nickname : "방장");

            // 자리에 있던 몸은 더 쓰이지 않는다. 방장 몸(게임 씬 것)이 그 자리를 대신하므로
            // 남겨 두면 아무도 몰지 않는 캐릭터가 된다.
            if (seat.Body != null && seat.Body != hostBody)
            {
                Debug.Log($"[TeamSeats] {slot}번 자리의 옛 몸을 치운다 — 방장 몸이 대신한다");
                Destroy(seat.Body.gameObject);
            }

            seat.Body = hostBody;
            seat.Driver = driver;
            seat.FreeSince = -1f;

            // 방장도 사람이다. 이것을 켜지 않으면 방장 혼자 들어간 판에서 나머지 자리가
            // 영영 비어 있는다 — 방장은 ClaimBody 를 거치지 않고 여기로 온다.
            humanSeated = true;
        }

        /// <summary>이 자리를 지금 누가 몰고 있나. 비어 있으면 null.</summary>
        public NetPlayer DriverOf(int slot) => At(slot)?.Driver;

        /// <summary>이 자리에 몸이 이미 서 있나.</summary>
        public bool HasBody(int slot) => At(slot)?.Body != null;

        /// <summary>
        /// 자리가 비었고 AI 가 이어받을 때가 됐나.
        ///
        /// <para>몸이 아직 없는 자리(아무도 온 적 없는 자리)는 기다리지 않는다 — 판이
        /// 시작될 때부터 AI 가 채워야 한다.</para>
        /// </summary>
        public bool NeedsAi(int slot)
        {
            Seat seat = At(slot);
            if (seat == null || seat.Driver != null) return false;

            // 이미 띄워 두었고 아직 앉는 중이다. Unity 의 == null 은 파괴된 것도 잡아내므로
            // 사람에게 밀려난 AI 는 여기서 걸리지 않는다.
            //
            // <b>다만 영원히 기다리지는 않는다.</b> 띄운 AI 가 어떤 이유로든 자리를 못 잡으면
            // (게임 씬을 못 찾는 등) 이 표시가 남아 그 자리가 조용히 계속 빈다.
            if (seat.AiPending != null)
            {
                if (Time.time - seat.AiPendingSince < AiPendingTimeout) return false;

                Debug.LogWarning($"[TeamSeats] {slot}번 자리의 AI 가 {AiPendingTimeout}초 안에 "
                                 + "몸을 세우지 못했다 — 물리고 다시 띄운다");
                NetworkServer.Destroy(seat.AiPending.gameObject);
                seat.AiPending = null;
            }

            // <b>이미 사람이 받아 둔 자리는 건드리지 않는다.</b> 자리(teamSlot)는 접속하자마자
            // 정해지지만 몸은 다음 물리 스텝에야 선다. 그 틈에 '몸이 없다'는 이유로 AI 를
            // 띄우면 방금 들어온 사람의 자리를 선점한다 — 두 명 이상이 거의 동시에 들어오면
            // 흔히 걸린다.
            if (NetPlayer.HumanHoldsSlot(slot)) return false;

            if (seat.Body == null) return true;
            return seat.FreeSince >= 0f && Time.time - seat.FreeSince >= AiTakeoverSeconds;
        }

        /// <summary>자리마다 조금씩 떼어 놓는다. 같은 지점에 넷을 세우면 서로 밀어낸다.</summary>
        Vector3 SpawnFootFor(int slot)
        {
            Vector3 origin = session != null ? session.SpawnFoot : transform.position;
            return origin + Vector3.right * ((slot - (TeamSlots.Count - 1) * 0.5f) * SeatSpacing);
        }

        Seat At(int slot) =>
            slot >= 0 && slot < seats.Length ? seats[slot] : null;
    }
}
