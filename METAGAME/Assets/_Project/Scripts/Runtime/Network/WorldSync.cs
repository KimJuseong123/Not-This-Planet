using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using MeteorPose = Metagame.Gameplay.Hazards.MeteorSpawner.NetworkMeteorPose;
using ScarState = Metagame.Gameplay.Hazards.MeteorSpawner.NetworkScarState;
using ScarRecord = Metagame.Gameplay.Hazards.MeteorSpawner.Scar;

namespace Metagame.Network
{
    /// <summary>
    /// 세계에서 움직이는 것들(부품·운반체)의 자세를 서버에서 모두에게 내려보낸다
    /// (S15P21B202-152).
    ///
    /// <b>왜 오브젝트마다 NetworkIdentity 를 달지 않았나.</b> 부품과 운반체는 코드가
    /// 만드는 것이 아니라 <b>씬에 배치돼 있다</b>. 씬에 놓인 오브젝트를 Mirror 가
    /// 짝지으려면 sceneId 가 필요한데, 그 값은 Unity 에디터가 씬을 저장할 때 부여한다.
    /// 손으로 넣을 수 있는 값이 아니라, 씬 파일을 직접 고치는 방법으로는 안전하게
    /// 만들 수 없다. 그래서 중계용 오브젝트 하나를 서버가 띄우고, 그것이 씬 오브젝트들의
    /// 자세를 실어 나른다.
    ///
    /// 서버와 클라이언트는 <b>같은 씬</b>을 열고 있으므로 같은 오브젝트를 갖고 있다.
    /// 양쪽이 같은 규칙으로 줄을 세우면(<see cref="Collect"/>) 번호만으로 짝지을 수 있다.
    ///
    /// 물리는 서버에서만 돈다(-145). 클라이언트의 같은 오브젝트는 키네마틱으로 두고
    /// 받은 자세를 따라가기만 한다 — 양쪽이 각자 굴리면 결과가 갈린다.
    /// </summary>
    public sealed class WorldSync : NetworkBehaviour
    {
        /// <summary>
        /// 세계 상태를 내보내는 주기. <b>NetworkManager 의 <c>sendRate</c> 를 따라간다</b>
        /// (S15P21B202-328).
        ///
        /// <para>예전에는 여기에 20Hz 를 따로 박아 두었다. 그래서 씬에서 <c>sendRate</c> 를
        /// 올려도 부품만 옛 주기에 남았다 — 조절 손잡이가 둘이면 하나를 돌린 사람이
        /// 나머지를 모른다. 손잡이는 하나여야 한다.</para>
        /// </summary>
        static float SendInterval =>
            NetworkServer.sendInterval > 0f ? NetworkServer.sendInterval : 1f / 20f;

        /// <summary>자세와 도킹 상태: Vector3 12 + Quaternion 16 + bool 1.</summary>
        const int PoseBytes = 29; // position + rotation + pad docking flag

        /// <summary>RPC 머리말·배치 여유. 정확히 셀 이유는 없고 넉넉히 잡는다.</summary>
        const int PacketHeadroom = 64;

        /// <summary>
        /// 고정쇠 상태는 바뀌지 않아도 이 간격마다 한 번은 다시 보낸다.
        /// 신뢰 없는 채널이라 한 번 잃으면 다음 변화까지 어긋난 채로 남기 때문이다.
        /// </summary>
        const float ClampKeepalive = 0.5f;

        /// <summary>
        /// 용접 상태를 조용해도 이 간격마다 한 번은 다시 보낸다(초) — S15P21B202-386.
        ///
        /// <para>ClientRpc 는 <b>지금 붙어 있는 사람에게만</b> 간다. 그래서 늦게 들어온
        /// 사람에게는 이 주기가 유일한 경로다. 고정쇠(0.5초)보다 길게 잡았다 — 스냅샷이
        /// 더 크고, 늦참자가 1초 안에 보게 되면 충분하다.</para>
        /// </summary>
        const float WeldKeepalive = 1f;

        /// 받은 자세로 옮겨 가는 속도. 20Hz 로 받은 사이를 메운다.
        const float Smoothing = 20f;

        [Serializable]
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool DockedToPad;
        }

        readonly List<Rigidbody> tracked = new List<Rigidbody>();

        /// <summary>고정쇠. 위치가 아니라 <b>진행도</b>가 사람마다 달라지는 것들이다.</summary>
        readonly List<PartClamp> clamps = new List<PartClamp>();
        ClampState[] clampTargets = Array.Empty<ClampState>();

        /// <summary>
        /// 저중력장을 가진 탈것의 <see cref="tracked"/> 색인 (S15P21B202-505).
        /// <see cref="Collect"/> 가 줄 세우기를 끝낸 뒤 채운다.
        /// </summary>
        readonly List<int> vehicleSlots = new List<int>();

        FieldState[] sendFields = Array.Empty<FieldState>();
        FieldState[] lastSentFields = Array.Empty<FieldState>();
        float fieldKeepaliveTimer;
        uint lastFieldSeq;
        bool everField;
        Pose[] targets = Array.Empty<Pose>();
        float sendTimer;

        // ── 보내는 쪽에서 다시 쓰는 버퍼 ──────────────────────────
        // 20~30Hz 로 매번 새 배열을 만들면 초당 수십 개의 쓰레기가 생긴다. Mirror 는
        // 부르는 그 자리에서 직렬화하므로 같은 배열을 계속 써도 안전하다.
        Pose[] sendPoses = Array.Empty<Pose>();
        ClampState[] sendClamps = Array.Empty<ClampState>();

        /// <summary>마지막으로 내보낸 고정쇠 상태. 바뀐 것이 없으면 보내지 않는다.</summary>
        ClampState[] lastSentClamps = Array.Empty<ClampState>();

        // 운석 예고 (S15P21B202-352). 고정쇠와 같은 모양 — 바뀔 때만 + keepalive.
        DropState[] sendDrops = Array.Empty<DropState>();
        DropState[] lastSentDrops = Array.Empty<DropState>();
        System.Collections.Generic.List<Metagame.Gameplay.Hazards.PendingMeteorDrop> recvDrops;
        float dropKeepaliveTimer;
        uint lastDropSeq;
        bool everDrop;
        Metagame.Gameplay.Hazards.MeteorSpawner meteorSpawnerCached;

        /// <summary>못 찾은 동안 다시 보는 간격(초). <see cref="MeteorSpawner"/> 의 주석 참고.</summary>
        const float SpawnerLookInterval = 1f;
        float lastSpawnerLook = float.NegativeInfinity;
        float clampKeepaliveTimer;

        // ── 용접 스냅샷 (S15P21B202-386) ───────────────────────────
        //
        // 용접은 RpcWeldGrown 으로 **행동**만 나르고 있었다. 처음부터 있던 사람은 그
        // 행동들을 다 들어 같은 모양이 되지만 **중간에 들어온 사람은 아무 비드도 없다.**
        // 그래서 상태도 함께 보낸다. 파츠 순서는 자세와 같은 색인을 쓴다.
        WeldBead.ChainState[] sendWeld = Array.Empty<WeldBead.ChainState>();
        int[] sendWeldCounts = Array.Empty<int>();
        WeldBead.ChainState[] lastSentWeld = Array.Empty<WeldBead.ChainState>();
        int[] lastSentWeldCounts = Array.Empty<int>();
        float weldKeepaliveTimer;
        readonly List<WeldBead.ChainState> weldChains = new List<WeldBead.ChainState>();
        bool warnedWeldMismatch;

        /// <summary>
        /// 조립 연결을 조용해도 이 간격마다 한 번은 다시 보낸다(초) — S15P21B202-388.
        /// 용접과 같은 이유·같은 값이다: <c>ClientRpc</c> 는 지금 붙어 있는 사람에게만 가므로
        /// 늦게 들어온 사람에게는 이 주기가 유일한 경로다.
        /// </summary>
        const float LinkKeepalive = 1f;

        /// <summary>
        /// 속도를 보내는 간격(초) — S15P21B202-388.
        ///
        /// <para><b>바뀔 때만 보내지 않는다.</b> 속도는 물체가 움직이는 동안 매 틱 달라지므로
        /// "바뀔 때만" 이 곧 "매 틱" 이 되어 아무것도 아끼지 못한다. 대신 <b>드물게, 무조건</b>
        /// 보낸다 — 이 값을 읽는 곳은 이어받는 순간 하나뿐이고, 그 순간은 이미 몇 초가
        /// 끊긴 뒤라 0.5초 묵은 속도로 충분하다.</para>
        ///
        /// <para>비용은 자세에 비하면 없는 것과 같다. 물체 8개면 한 번에 192바이트이고
        /// 2Hz 라 초당 384바이트다 — 자세는 같은 8개를 30Hz 로 보내 초당 6.7KB 다.</para>
        /// </summary>
        const float VelocityInterval = 0.5f;

        // ── 조립 연결과 속도 (S15P21B202-388) ─────────────────────
        //
        // 참가자 화면의 부품은 isKinematic 으로 자세만 따라가고 **관절이 없다**. 보기에는
        // 이어붙은 탑인데 실제로는 나란히 놓인 부품들이라, 누군가 이어받아 물리를 켜면
        // 그 순간 무너진다. 속도도 안 가서 날아가던 것이 제자리에 멈춘 채 다시 출발한다.
        //
        // 둘 다 **보이는 것을 바꾸지 않는다** — 참가자 부품은 계속 kinematic 이다.
        // 이어받을 때만 쓰이는 값이고, 그것이 호스트 이전의 전제다(S15P21B202-389).
        readonly List<SpaceshipPart> trackedParts = new List<SpaceshipPart>();
        readonly List<PartAssemblyLink.LinkState> linkStates = new List<PartAssemblyLink.LinkState>();
        PartAssemblyLink.LinkState[] sendLinks = Array.Empty<PartAssemblyLink.LinkState>();
        PartAssemblyLink.LinkState[] lastSentLinks = Array.Empty<PartAssemblyLink.LinkState>();
        float linkKeepaliveTimer;
        bool warnedLinkMismatch;

        Velocity[] sendVelocities = Array.Empty<Velocity>();
        Velocity[] recvVelocities = Array.Empty<Velocity>();
        float velocityTimer;
        bool warnedVelocityMismatch;

        // 도구 휴대 (S15P21B202-352). 고정쇠와 같은 모양 — 바뀔 때만 + keepalive.
        //
        // 자세만 나르면 부족하다. 장착 판정은 서버에서만 도는데(-145) 그때
        // `ToolBase.OnEquipped` 가 자식 `Visual_ZUpToYUp` 을 손에 쥔 각도로 돌리고
        // 휴대 중 충돌을 끈다. 참가자 기계에서는 그 함수가 한 번도 불리지 않아
        // **뿌리는 제자리에 오는데 메시가 어긋나** 있었고 남의 도구가 몸을 막았다.
        readonly List<ToolBase> tools = new List<ToolBase>();
        public struct ToolViewState
        {
            public uint RhythmSequence;
            public byte RhythmGrade;
            public float RhythmCooldown;
            public uint Owner;
            public byte Action;
            public float Progress;
            public Vector3 Direction;
        }
        ToolViewState[] sendToolViews = Array.Empty<ToolViewState>();
        uint lastToolViewSeq;
        bool everToolView;

        // ── 착지한 운석(흔적) (S15P21B202-355) ────────────────────────────
        ScarState[] sendScarSet = System.Array.Empty<ScarState>();
        ScarState[] sendScarMoves = System.Array.Empty<ScarState>();
        int[] lastScarIds = System.Array.Empty<int>();
        float scarKeepaliveTimer;
        uint lastScarSeq;
        bool everScar;
        bool warnedScarCap;
        bool warnedToolMismatch;

        // 낙하 운석 그림 (S15P21B202-352). 자세와 같이 **매 틱** 보낸다 — 예고와 달리
        // 매 순간 값이 달라지므로 "바뀔 때만" 이 의미가 없다.
        MeteorPose[] sendMeteors = Array.Empty<MeteorPose>();
        uint lastMeteorSeq;
        bool everMeteor;
        bool warnedMeteorCap;

        /// <summary>
        /// "운석 0개" 를 몇 번 더 보낼지. 신뢰 없는 채널에서 그림을 지우는 신호가 한 번에
        /// 유실되는 것을 막는다 — <see cref="SendMeteors"/> 의 주석 참고.
        /// </summary>
        const int ZeroRepeats = 15;
        int zeroSends;

        /// <summary>
        /// 이만큼 낙하 자세가 안 오면 남은 그림을 치운다 — <see cref="ClearGhostsIfSilent"/>.
        /// 공세 간격의 최소가 15초이므로(기획 §17.4) 정상 조용함과 헷갈리지 않는다.
        /// </summary>
        const float GhostSilenceTimeout = 2f;

        /// <summary>
        /// 흔적 전체 목록을 다시 보내는 간격 (S15P21B202-355).
        ///
        /// <para>목록은 <b>바뀔 때만</b> 나가는데, 그것만으로는 <b>늦게 들어온 참가자</b>가
        /// 다음 착탄까지 빈 땅을 본다. 그리고 이 묶음은 신뢰 채널이라 잃지는 않지만,
        /// 참가자가 <c>ready</c> 가 되기 전에 나간 것은 걸러진다.</para>
        ///
        /// <para>고정쇠·도구의 되보내기와 같은 방식이다. 그쪽보다 간격이 긴 것은 묶음이
        /// 크기 때문이다 — 40개 × 32B 는 약 1.3KB 다.</para>
        /// </summary>
        const float ScarKeepalive = 2f;

        /// <summary>흔적 자세 하나 — 자세 28 + 번호 4. 속도는 싣지 않는다.</summary>
        const int ScarStateBytes = PoseBytes + 4;
        float lastMeteorAt;

        /// <summary>
        /// 보낸 순서. <b>신뢰 없는 채널은 순서를 지켜 주지 않는다</b> — 늦게 도착한
        /// 옛 묶음을 그대로 받으면 물체가 뒤로 한 번 튄다. 번호로 걸러 낸다.
        /// </summary>
        uint sendSeq;
        uint lastPoseSeq;
        bool everPose;
        uint lastClampSeq;
        bool everClamp;
        bool ready;
        bool warnedMismatch;
        /// <summary>고정쇠별로 굳혀 둔 줄 세우기 키 — <see cref="KeyOf"/> 참고.</summary>
        readonly Dictionary<PartClamp, string> clampKeys = new Dictionary<PartClamp, string>();
        float notReadySeconds;
        bool warnedNotReady;
        bool warnedClampMismatch;

        /// <summary>이번 방의 먼지 폭풍 시드. 서버가 정하고 참가자에게 한 번 내려보낸다.</summary>
        [SyncVar(hook = nameof(OnDustStormSeedChanged))]
        int dustStormSeed;

        /// <summary>클라이언트로서 챙길 것이 남았는가. 스폰이 등록된 뒤에만 참이 된다.</summary>
        bool needsClientPrepare;

        public override void OnStartServer()
        {
            // 번호로 탈것을 되찾을 수 있게 지금 살아 있는 것을 알린다 (S15P21B202-434).
            Current = this;

            dustStormSeed = DustStormMatchSeed.EnsureServerMatch();
            Collect();
            ready = true;

            // 견인 장치는 **나중에** 생긴다(사람이 Part_1 을 해제하면 AddComponent). 한 번만
            // 수집하면 그것은 끝까지 추적 밖에 남아 혼자만 움직인다(S15P21B202-315).
            TransportVehicle.AnyActivated += OnVehicleActivated;

            // 용접은 서버에서만 판정된다. 그 사건을 받아 모두에게 중계한다.
            WorldEvents.WeldGrown += RpcWeldGrown;
            WorldEvents.WeldShrunk += RpcWeldShrunk;

            Debug.Log($"[WorldSync] 서버가 {tracked.Count}개를 맡는다 · 고정쇠 {clamps.Count}개 · {Order()}");
        }

        public override void OnStartClient()
        {
            // 호스트 모드면 OnStartServer 가 이미 세웠다. 같은 값이라 무해하다.
            Current = this;

            if (dustStormSeed != 0)
                DustStormMatchSeed.ApplyServerSeed(dustStormSeed);

            // 호스트 모드면 서버 쪽에서 이미 준비됐다.
            if (isServer) return;

            // 여기서 챙기지 않는다. 이 스폰은 방장의 씬이 준비되면 오는데, 내 게임 씬은
            // 아직 뜨는 중일 수 있다. 그때 챙기면 0개를 챙긴 채 "준비 완료" 가 되고,
            // 이후로는 서버가 보내는 개수와 영원히 어긋나 아무것도 따라가지 않는다
            // (2026-09-03 실측 — 참가자만 오브젝트가 동기화되지 않았다).
            // Update 가 씬이 올라올 때까지 기다렸다 챙긴다.
            needsClientPrepare = true;

            // 서버와 같은 이유로 클라이언트도 견인 장치가 생기면 다시 센다(S15P21B202-315).
            TransportVehicle.AnyActivated += OnVehicleActivated;
        }

        /// <summary>
        /// Mirror 가 스폰하기 전에 서버가 초기 SyncVar 값을 채운다.
        /// </summary>
        internal void InitializeDustStormSeed(int seed)
        {
            dustStormSeed = seed;
        }

        void OnDustStormSeedChanged(int oldSeed, int newSeed)
        {
            DustStormMatchSeed.ApplyServerSeed(newSeed);
        }

        /// <summary>
        /// 클라이언트가 따라갈 것들을 챙긴다. 게임 씬이 아직이면 거짓을 돌려주고 다음에 다시 본다.
        /// </summary>
        bool PrepareClient()
        {
            // 씬이 올라왔는지는 "옮길 것이 있는가" 로 판단한다(WorldSyncSpawner 와 같은 기준).
            // **SpaceshipPart 로 본다**(S15P21B202-315) — TransportVehicle 은 씬에 없는 것이
            // 정상이라 그것을 기다리면 영영 준비되지 않는다. 자세한 사정은 WorldSyncInstaller.
            if (FindAnyObjectByType<SpaceshipPart>() == null) return false;

            Collect();
            if (tracked.Count == 0) return false;

            foreach (var body in tracked)
            {
                if (body == null) continue;
                body.isKinematic = true;   // 서버가 보낸 자세만 따라간다
            }

            targets = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                targets[i] = Read(tracked[i]);

            // 이 화면은 그림이다. 세계를 스스로 바꾸면 서버가 보낸 것과 겹친다.
            WorldEvents.RemoteDecides = true;

            ready = true;
            Debug.Log($"[WorldSync] 클라이언트가 {tracked.Count}개를 받는다 · 고정쇠 {clamps.Count}개 · {Order()}");
            return true;
        }

        /// <summary>
        /// 움직이는 것들을 <b>양쪽이 같은 순서로</b> 줄 세운다.
        ///
        /// 이름만으로는 같은 이름이 여럿일 때 순서가 흔들린다. 씬이 정해 준 처음
        /// 위치는 서버와 클라이언트가 똑같으므로, 이름이 같으면 그 위치로 가른다.
        /// </summary>
        /// <summary>고정쇠 하나의 상태. 위치로는 알 수 없어 따로 실어 보낸다.</summary>
        public struct ClampState
        {
            public float Progress;      // 0~1
            public bool Released;
        }

        /// <summary>
        /// 탈것의 저중력장이 켜져 있는가 (S15P21B202-505).
        ///
        /// <para><b>번호는 <see cref="tracked"/> 의 색인이다.</b> 새 줄을 세우지 않는다 —
        /// S15P21B202-434 가 그 이유를 적어 두었다(-315 의 "개수는 같은데 순서만 어긋남").
        /// 받는 쪽은 <see cref="VehicleAt"/> 로 되찾는다.</para>
        ///
        /// <para><b>켜짐/꺼짐만 보낸다.</b> 장의 모양(꼭짓점·반경)은 기체 위치에서 매번
        /// 다시 잡으므로(<c>TransportVehicle.Field</c> 게터가 <c>Configure</c> 를 부른다)
        /// 탈것 자세가 이미 동기화되는 것으로 따라온다.</para>
        /// </summary>
        public struct FieldState
        {
            public int Vehicle;         // tracked 색인
            public bool Active;
        }

        // ── 로그 진단용 (S15P21B202-352) ──────────────────────────────────
        //
        // 화면의 GUI 로 값을 읽어 손으로 적는 것은 느리고 놓치기 쉽다. 대신 양쪽 로그에
        // 같은 형식으로 남겨 **대조로 일치 여부를 증명**한다. NetDiagnostics 가 쓴다.

        /// <summary>지금 나르고 있는 물체 수. 양쪽이 다르면 그 순간 동기화가 멈춘다.</summary>
        public int TrackedCount => tracked.Count;

        // ── 탈것을 번호로 가리키기 (S15P21B202-434) ─────────────────
        //
        // 탑승 상태는 bool 하나였다. 그래서 참가자는 "타고 있다" 는 알아도 <b>무엇을
        // 타는지</b>를 몰랐고, 조종석 시점 앵커를 세울 수 없었다.
        //
        // <b>번호는 이미 있는 것을 쓴다.</b> 짝짓기 순서는 계층 경로와 형제 순번으로만
        // 정해지므로(<see cref="Compare"/>) 양쪽이 같은 줄을 얻는다. 여기서 탈것만
        // 따로 줄을 세우면 -315 가 기록한 사고(개수는 같은데 순서만 어긋남)를 새로
        // 만든다. 그리고 탈것은 파츠 오브젝트에 붙으므로(<c>PartTractorActivator</c>)
        // 그 몸이 이미 이 목록 안에 있다.

        /// <summary>지금 살아 있는 것. 양쪽 모두 스폰된 뒤에 채워진다.</summary>
        public static WorldSync Current { get; private set; }

        /// <summary>이 몸의 짝짓기 번호. 목록에 없으면 -1.</summary>
        public int IndexOfTracked(Rigidbody body)
        {
            if (body == null) return -1;

            for (int i = 0; i < tracked.Count; i++)
                if (ReferenceEquals(tracked[i], body)) return i;

            return -1;
        }

        /// <summary>
        /// 그 번호의 탈것. 번호가 범위를 벗어나거나 그 자리가 탈것이 아니면 null.
        ///
        /// <para>아직 <see cref="ready"/> 가 아니면 목록이 비어 있어 null 이 나온다.
        /// 부르는 쪽은 될 때까지 다시 물어봐야 한다 — 스폰과 씬 준비 순서가 고정이
        /// 아니다(<see cref="OnStartClient"/> 의 주석 참고).</para>
        /// </summary>
        public TransportVehicle VehicleAt(int index)
        {
            if (index < 0 || index >= tracked.Count) return null;

            var body = tracked[index];
            return body != null ? body.GetComponent<TransportVehicle>() : null;
        }

        /// <summary>고정쇠 수. 개수 검사가 따로 걸리는 배열이다.</summary>
        public int ClampCount => clamps.Count;

        /// <summary>
        /// 받은 예고 수(참가자) 또는 보낸 예고 수(호스트).
        ///
        /// <para><b>예전에는 늘 <c>sendDrops.Length</c> 였다</b>(AI 리뷰 지적, 2026-09-08).
        /// 그건 보내는 쪽 버퍼라 참가자에서는 언제나 0이고, 그래서 <c>netdiag</c> 의
        /// <c>drops</c> 항이 <b>매 표본 갈려 보였다</b> — 검증 도구가 상시 거짓 불일치를
        /// 내는 것이 이 이슈에서 가장 피하려던 일이다.</para>
        ///
        /// <para>역할을 <c>isServer</c> 로 묻지 않는다 — 스폰 등록 전에는 그 속성이
        /// <c>NullReference</c> 를 낸다(<see cref="Update"/> 의 주석에 실측이 적혀 있고,
        /// 이 값은 진단이 매 초 읽는다). <see cref="recvDrops"/> 는 <b>받는 쪽만</b>
        /// 채우므로 그것이 있으면 참가자다.</para>
        /// </summary>
        public int DropCount => recvDrops != null ? recvDrops.Count : sendDrops.Length;

        /// <summary>나르는 도구 수. 휴대 비트 배열의 길이 검사가 이 값에 걸린다.</summary>
        public int ToolCount => tools.Count;

        /// <summary>
        /// 체크섬을 남기는 묶음 간격. <c>sendRate 30</c> 에서 약 1초에 한 번이라
        /// <c>NetDiagnostics</c> 의 기록 주기와 맞는다.
        /// </summary>
        const uint HashEvery = 30;

        uint bundleSeq;
        uint bundleHash;
        bool everBundle;

        /// <summary>
        /// 체크섬을 남긴 묶음의 번호. 0 이면 아직 하나도 없다.
        ///
        /// <para><b>번호로 짝짓는 이유.</b> 예전에는 <b>지금 물체들의 자세</b>를 접어
        /// 시각(<c>t</c>)으로 짝지었다. 그건 움직이는 것에 대해 **반드시 갈린다** —
        /// 참가자 쪽 물체는 받은 자세로 <b>보간해 가는 중</b>이라(<c>FollowOnClient</c> 의
        /// <c>Smoothing</c>) 호스트의 실시간 자세보다 뒤에 있고, 그 차이는 초당 2m 로
        /// 끌리는 파츠에서 10cm, 초당 10m 로 떨어지는 운석에서 50cm 다. 1cm 로
        /// 양자화한 값이 늘 달라져 **검증 도구가 거짓 빨강만 내게 된다.**</para>
        ///
        /// <para>그래서 <b>실제로 주고받은 묶음</b>을 접는다. 같은 번호의 묶음은 양쪽에서
        /// 바이트가 같으므로 양자화도 필요 없고 —
        /// 오일러 각의 <c>359.6°</c>↔<c>0.4°</c> 경계 문제도 사라진다 — 값이 다르면
        /// <b>정말로 다른 데이터가 적용된 것</b>이다.</para>
        ///
        /// <para>줄 세우기가 어긋난 사고(개수는 같은데 순서만 다른 것)는 이 값으로는
        /// 안 잡힌다. 그건 <see cref="TrackedNames"/> 줄을 그대로 비교하는 쪽이
        /// <b>더 정확하게</b> 잡는다 — <c>netdiag.py</c> 가 그것을 먼저 본다.</para>
        /// </summary>
        public uint BundleSeq => everBundle ? bundleSeq : 0u;

        /// <summary>체크섬. <see cref="BundleSeq"/> 의 주석이 뜻을 적었다.</summary>
        public uint BundleHash => bundleHash;

        /// <summary>
        /// 이 묶음의 체크섬을 남길 차례인가. 남길 차례면 접어서 기억한다.
        /// </summary>
        void MarkBundle(uint seq, Pose[] poses, int count)
        {
            if (seq % HashEvery != 0) return;

            bundleSeq = seq;
            bundleHash = HashBundle(poses, count);
            everBundle = true;
        }

        /// <summary>
        /// 묶음을 한 값으로 접는다 (FNV-1a).
        ///
        /// <para><b>비트를 그대로 접는다.</b> 양쪽이 같은 번호의 같은 묶음을 보므로
        /// 반올림할 이유가 없다. <c>BitConverter.DoubleToInt64Bits</c> 를 쓰는 것은
        /// <c>float</c> → <c>double</c> 이 무손실이고 이 API 는 모든 런타임에 있기
        /// 때문이다.</para>
        /// </summary>
        static uint HashBundle(Pose[] poses, int count)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = FoldInt(h, count);

                for (int i = 0; i < count && i < poses.Length; i++)
                {
                    h = FoldFloat(h, poses[i].Position.x);
                    h = FoldFloat(h, poses[i].Position.y);
                    h = FoldFloat(h, poses[i].Position.z);
                    h = FoldFloat(h, poses[i].Rotation.x);
                    h = FoldFloat(h, poses[i].Rotation.y);
                    h = FoldFloat(h, poses[i].Rotation.z);
                    h = FoldFloat(h, poses[i].Rotation.w);
                    h = FoldInt(h, poses[i].DockedToPad ? 1 : 0);
                }

                return h;
            }
        }

        static uint FoldFloat(uint h, float value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            unchecked
            {
                for (int k = 0; k < 8; k++)
                {
                    h = (h ^ (uint)(bits & 0xFF)) * 16777619u;
                    bits >>= 8;
                }
                return h;
            }
        }

        static uint FoldInt(uint h, int v)
        {
            unchecked
            {
                uint u = (uint)v;
                for (int k = 0; k < 4; k++)
                {
                    h = (h ^ (u & 0xFFu)) * 16777619u;
                    u >>= 8;
                }
                return h;
            }
        }

        /// <summary>
        /// 나르는 물체의 이름을 순서대로. **개수가 갈렸을 때 어느 물체가 다른지** 가르는 데
        /// 쓴다 — 시작할 때 한 번만 찍는다(매 초 찍으면 로그가 덮인다).
        /// </summary>
        public string TrackedNames()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < tracked.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(tracked[i] != null ? tracked[i].name : "(없음)");
            }
            return sb.ToString();
        }


        void Collect()
        {
            tracked.Clear();

            foreach (var part in FindObjectsByType<SpaceshipPart>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(part != null ? part.GetComponent<Rigidbody>() : null);

            foreach (var vehicle in FindObjectsByType<TransportVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(vehicle != null ? vehicle.GetComponent<Rigidbody>() : null);

            // 씬에 놓인 도구도 나른다 (S15P21B202-352). 방망이·용접 플라스마 건은
            // 처음 위치만 같고 **움직임이 상대에게 가지 않았다** — 한쪽이 집어 옮기면
            // 다른 쪽 화면에서는 제자리에 그대로 있었다. 조립의 주 도구가 그랬다.
            //
            // **술어를 `ToolBase` 로 좁힌 이유.** `Grabbable` 이나 Prop 레이어로 잡으면
            // 개수가 갈려 **지금 잘 되는 부품 동기화까지 멈춘다**: `PartClamp` 는 해제될 때
            // 자기 오브젝트를 Prop 레이어로 바꾸고 `Rigidbody` 를 붙이고
            // `Grabbable.Mark(gameObject, "해제된 고정쇠")` 를 부른다(`PartClamp.cs:423·437·456`).
            // 그 해제 시점이 서버와 클라이언트에서 다르므로 재계수하는 순간 집합이 어긋난다.
            // 착지한 운석도 `Meteor` 가 같은 표시를 한다.
            //
            // `ToolBase` 파생은 방망이와 용접 건 둘뿐이고 **런타임에 만들어지지 않는다**
            // (전수 확인). 그래서 양쪽이 같은 집합을 같은 순서로 발견한다 — 정렬은
            // 계층 경로+형제 순번이라 시간이 지나도 안 변한다(`Compare` 참고).
            // ⚠ 나중에 도구를 런타임에 만들게 되면 이 술어가 깨진다. 그때는 씬 배치
            // 전용 표식 컴포넌트로 바꿀 것.
            tools.Clear();
            foreach (var tool in FindObjectsByType<ToolBase>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (tool == null) continue;

                // **`ToolBase.CollisionBody` 와 같은 술어여야 한다**(AI 리뷰 지적,
                // 2026-09-08). 그쪽은 부모까지 올라가 몸을 찾는데 여기서 자기 오브젝트만
                // 보면, 스크립트가 자식에 있는 배치에서 **휴대 비트만 가고 자세는 안 나간다**
                // — 도구가 상대 화면에서 제자리에 멈추는, 고치려던 그 증상이다.
                // `Add` 가 중복을 걸러 주므로 부모 몸이 이미 목록에 있으면 두 번 들어가지 않는다.
                //
                // 그리고 이 술어는 **`PlayerRig.BodyOf`(`PlayerRig.cs:2649`)와 같다** —
                // 잡기 판정이 도구의 몸을 정하는 그 규칙이다. 여기서 다르게 정하면
                // "한 성질을 두 곳에서 판단" 하는 것이 되어 반드시 갈린다.
                Add(tool.GetComponentInParent<Rigidbody>());

                // 휴대 비트는 몸이 없어도 나른다 — 보이는 것만 바꾸는 값이라
                // `Rigidbody` 가 필요 없다. 그래서 목록을 따로 둔다.
                tools.Add(tool);
            }
            tools.Sort(CompareTools);


            tracked.Sort(Compare);

            clamps.Clear();
            foreach (var clamp in FindObjectsByType<PartClamp>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None))
                if (clamp != null) clamps.Add(clamp);

            clamps.Sort(CompareClamps);

            // 번호로 부품을 되찾을 수 있게 자리를 맞춰 들고 있는다 (S15P21B202-388).
            // **줄 세우기가 끝난 뒤에** 만들어야 자세와 같은 색인이 된다.
            // 부품이 아닌 자리(탈것·도구)는 null 이다.
            trackedParts.Clear();
            for (int i = 0; i < tracked.Count; i++)
                trackedParts.Add(tracked[i] != null ? tracked[i].GetComponent<SpaceshipPart>() : null);

            // 저중력장을 실어 보낼 자리 (S15P21B202-505).
            //
            // **새 줄을 세우지 않는다.** 여기 담는 것은 `tracked` 의 색인이고, 그
            // 색인은 위에서 이미 확정됐다. S15P21B202-434 가 "탈것만 따로 줄을 세우면
            // -315 가 기록한 사고(개수는 같은데 순서만 어긋남)를 새로 만든다" 고 적어
            // 두었다 — 그래서 부분집합만 들고 있는다.
            //
            // 매 틱 GetComponent 를 돌지 않으려고 여기서 한 번만 모은다. trackedParts
            // 가 같은 이유로 같은 자리에 있다.
            vehicleSlots.Clear();
            for (int i = 0; i < tracked.Count; i++)
            {
                if (tracked[i] == null) continue;

                var vehicle = tracked[i].GetComponent<TransportVehicle>();
                if (vehicle != null) vehicleSlots.Add(i);
            }
        }

        /// <summary>
        /// 고정쇠의 줄 세우기 키. <b>처음 본 순간의 것을 굳혀 두고 그 뒤로는 바꾸지 않는다</b>
        /// (S15P21B202-315).
        ///
        /// <para>고정쇠는 해제되면 부모에서 떨어져 나온다 —
        /// <c>PartClamp.Release()</c> 의 <c>transform.SetParent(null, true)</c>.
        /// 그러면 계층 경로가 <c>".../Part_1/Clamp_x"</c> 에서 <c>"Clamp_x"</c> 로 바뀌고
        /// 형제 순번도 달라진다. 서버와 클라이언트가 <b>서로 다른 해제 상태에서</b> 줄을
        /// 세우면 개수는 16 대 16 으로 같은데 <b>순서만 어긋나</b> 남의 고정쇠에 진행도가
        /// 들어간다. 개수 검사로는 절대 못 잡는다.</para>
        ///
        /// <para>2026-09-03 에 자세 목록에서 같은 종류의 사고가 있었는데(그때는 '지금 위치'로
        /// 동점을 갈랐다) 고정쇠 쪽에는 같은 처리가 없었다. 다시 세는 일이 생기면서
        /// 실제로 드러날 수 있는 자리가 됐다.</para>
        /// </summary>
        string KeyOf(PartClamp clamp)
        {
            if (clampKeys.TryGetValue(clamp, out var key)) return key;
            key = PathOf(clamp.transform) + "#" + clamp.transform.GetSiblingIndex();
            clampKeys[clamp] = key;
            return key;
        }

        int CompareClamps(PartClamp a, PartClamp b) => string.CompareOrdinal(KeyOf(a), KeyOf(b));

        /// <summary>
        /// 도구의 줄 세우기. <see cref="Compare"/> 와 <b>같은 규칙</b>이다 — 계층 경로와
        /// 형제 순번만 본다. 도구는 손에 들려도 부모가 바뀌지 않으므로(<c>PlayerRig</c> 는
        /// 자세만 잡는다) 고정쇠처럼 키를 굳혀 둘 필요가 없다.
        /// </summary>
        static int CompareTools(ToolBase a, ToolBase b)
        {
            int byPath = string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform));
            if (byPath != 0) return byPath;

            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        void Add(Rigidbody body)
        {
            if (body != null && !tracked.Contains(body)) tracked.Add(body);
        }

        /// <summary>
        /// 양쪽이 반드시 같은 순서를 내야 한다 — 번호 하나가 밀리면 그 뒤 전부가 남의 자리로 간다.
        ///
        /// 예전에는 이름이 같을 때 <b>지금 위치</b>로 동점을 갈랐다. 그런데 서버는 시작하자마자
        /// 목록을 만들고 클라이언트는 자기 게임 씬이 다 뜬 뒤에 만든다. 그 사이에 서버가 물체를
        /// 움직였으면 위치가 달라져 순서가 갈리고, 탈것 자리에 다른 물체의 좌표가 들어간다 —
        /// 탈것은 그대로인데 사람만 날아다니는 것처럼 보인다(2026-09-03).
        ///
        /// 그래서 <b>시간이 지나도 변하지 않는 것</b>으로만 가른다. 계층 경로와 형제 순번은
        /// 같은 씬을 같은 코드로 세우는 한 양쪽이 똑같다.
        /// </summary>
        static int Compare(Rigidbody a, Rigidbody b)
        {
            int byPath = string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform));
            if (byPath != 0) return byPath;

            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        /// <summary>
        /// 지금 줄 세운 순서를 이름으로 늘어놓는다. 양쪽 로그를 나란히 놓으면 어긋남이 바로 보인다 —
        /// 개수만 같고 순서가 다르면 좌표가 남의 자리로 가는데, 그건 개수 검사로 못 잡는다.
        /// </summary>
        string Order()
        {
            var names = new string[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                names[i] = tracked[i] != null ? tracked[i].name : "(없음)";
            return string.Join(", ", names);
        }

        /// <summary>뿌리부터의 계층 경로. 이름이 겹쳐도 부모가 다르면 갈린다.</summary>
        static string PathOf(Transform t)
        {
            var path = new System.Text.StringBuilder(t.name);
            for (var parent = t.parent; parent != null; parent = parent.parent)
                path.Insert(0, parent.name).Insert(0, '/');
            return path.ToString();
        }

        static Pose Read(Rigidbody body)
        {
            return body == null
                ? default
                : new Pose { Position = body.position, Rotation = body.rotation,
                    DockedToPad = body.TryGetComponent<SpaceshipPart>(out var part) && part.IsDockedToPad };
        }

        public override void OnStopClient()
        {
            // 내가 새 방장이 되는 길이면 세계를 물리로 돌려놓는다 (S15P21B202-389).
            // 이 컴포넌트가 사라지기 전이어야 한다 — 받아 둔 속도와 줄 세운 순서를
            // 아는 것이 여기뿐이다. RemoteDecides 는 그 안에서 내린다.
            if (!isServer && HostMigration.Taking) TakeOverPhysics();

            if (!isServer) WorldEvents.RemoteDecides = false;
            if (!isServer) TransportVehicle.AnyActivated -= OnVehicleActivated;

            // 사라진 것을 가리키고 있으면 다음 매치에서 옛 목록으로 번호를 푼다
            // (S15P21B202-434). 내가 지금 것일 때만 지운다 — 이어받기로 새 것이 이미
            // 들어와 있으면 그것을 지워서는 안 된다.
            if (ReferenceEquals(Current, this)) Current = null;

            // 낙하 그림은 충돌러가 꺼져 있어 남으면 지면을 뚫고 영원히 떨어진다.
            if (!isServer && MeteorSpawner != null) MeteorSpawner.ClearNetworkMeteors();

            // 흔적 그림도 함께 치운다 (S15P21B202-355). 이쪽은 중력을 끄고 세워 두므로
            // 지면을 뚫지는 않지만, 남으면 다음 경기의 빈 땅에 옛 돌이 서 있다.
            if (!isServer && MeteorSpawner != null) MeteorSpawner.ClearNetworkScars();
        }

        public override void OnStopServer()
        {
            // 남겨 두면 다음 매치에서 이미 사라진 오브젝트를 향해 사건이 날아간다.
            WorldEvents.WeldGrown -= RpcWeldGrown;
            WorldEvents.WeldShrunk -= RpcWeldShrunk;
            TransportVehicle.AnyActivated -= OnVehicleActivated;

            // OnStopClient 와 같은 이유로 내가 지금 것일 때만 지운다 (S15P21B202-434).
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>
        /// 견인 장치가 활성화됐다 — 추적 목록을 다시 세운다 (S15P21B202-315).
        ///
        /// <para>양쪽이 각자 자기 시점에 다시 세우므로 잠깐 개수가 어긋날 수 있다. 그 사이
        /// <see cref="RpcApply"/> 가 개수 불일치를 보고 <b>손대지 않고 넘긴다</b> — 억지로 맞추면
        /// 엉뚱한 물체가 엉뚱한 자리로 간다. 몇 프레임 뒤 양쪽이 같아지면 저절로 이어진다.</para>
        ///
        /// <para>순서는 계층 경로와 형제 순번으로만 가르므로(<see cref="Compare"/>) 다시 세워도
        /// 양쪽이 같은 줄을 얻는다.</para>
        /// </summary>
        void OnVehicleActivated(TransportVehicle vehicle)
        {
            if (!ready) return;                 // 아직 처음 수집도 안 됐다 — 그때 같이 잡힌다

            int before = tracked.Count;
            Collect();
            if (tracked.Count == before) return;

            if (isServer)
            {
                Debug.Log($"[WorldSync] 견인 장치가 생겨 다시 세었다 · {before} → {tracked.Count} · {Order()}");
                return;
            }

            // 클라이언트는 따라가기만 하므로 새로 들어온 것도 서버 자세를 받게 만든다.
            foreach (var body in tracked)
                if (body != null) body.isKinematic = true;

            targets = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                targets[i] = Read(tracked[i]);
            warnedMismatch = false;             // 개수가 바뀌었으니 다시 알릴 수 있게 한다
            Debug.Log($"[WorldSync] 견인 장치가 생겨 다시 세었다(클라) · {before} → {tracked.Count} · {Order()}");
        }

        void Update()
        {
            if (!ready)
            {
                // 여기서 isServer 를 보면 안 된다 — 스폰 등록 전에는 netIdentity 가 없어
                // 그 자리에서 NullReference 가 난다(실측). OnStartClient 가 세운 표시만 본다.
                if (!needsClientPrepare) return;
                if (!PrepareClient())
                {
                    // 준비되지 않는 동안 서버가 보내는 것을 **전부 버린다**(RpcApply·
                    // RpcApplyClamps 의 !ready 조기 반환). 그런데 그 조기 반환이 개수 검사보다
                    // 위에 있어 경고가 한 줄도 안 찍힌다 — 2026-09-07 에 그래서 원인을 찾는 데
                    // 한참 걸렸다. 오래 이어지면 여기서 한 번 알린다 (S15P21B202-315).
                    notReadySeconds += Time.deltaTime;
                    if (notReadySeconds > 15f && !warnedNotReady)
                    {
                        warnedNotReady = true;
                        Debug.LogWarning("[WorldSync] 15초째 준비되지 않았다 — 게임 씬에 "
                                         + "SpaceshipPart 가 있는지 확인할 것. 이 동안 서버가 "
                                         + "보내는 자세·고정쇠를 전부 버린다.");
                    }
                    return;
                }
            }

            if (isServer)
            {
                SendFromServer();
                return;
            }

            FollowOnClient();
            ClearGhostsIfSilent();
        }

        void SendFromServer()
        {
            sendTimer += Time.deltaTime;
            clampKeepaliveTimer += Time.deltaTime;
            fieldKeepaliveTimer += Time.deltaTime;
            weldKeepaliveTimer += Time.deltaTime;
            linkKeepaliveTimer += Time.deltaTime;
            velocityTimer += Time.deltaTime;
            dropKeepaliveTimer += Time.deltaTime;
            scarKeepaliveTimer += Time.deltaTime;
            if (sendTimer < SendInterval) return;
            sendTimer = 0f;

            sendSeq++;

            // ── 자세 ────────────────────────────────────────────
            if (sendPoses.Length != tracked.Count) sendPoses = new Pose[tracked.Count];
            for (int i = 0; i < tracked.Count; i++)
                sendPoses[i] = Read(tracked[i]);

            // 신뢰 없는 채널은 조각내지 않는다 — 한 묶음이 kcp 의
            // UnreliableMaxMessageSize 를 넘으면 그 자리에서 예외가 된다. 씬의 부품은
            // 4개이고 견인 장치까지 합쳐도 8개라 닿을 일이 없지만, 나중에 늘어났을 때
            // 조용히 끊기는 대신 신뢰 채널로 되돌아가게 해 둔다.
            // 진단용 체크섬 (S15P21B202-352). **보낸 그 묶음**을 접어 둔다.
            MarkBundle(sendSeq, sendPoses, tracked.Count);

            if (tracked.Count <= UnreliablePoseCapacity) RpcApply(sendSeq, sendPoses);
            else RpcApplySafe(sendSeq, sendPoses);

            // ── 고정쇠 ──────────────────────────────────────────
            if (sendClamps.Length != clamps.Count) sendClamps = new ClampState[clamps.Count];
            for (int i = 0; i < clamps.Count; i++)
            {
                var clamp = clamps[i];
                sendClamps[i] = clamp == null
                    ? default
                    : new ClampState { Progress = clamp.Progress, Released = clamp.IsReleased };
            }

            // 고정쇠는 사람이 당기는 몇 초를 빼면 내내 그대로다. 자세와 달리 매 틱마다
            // 실어 보낼 이유가 없다 — 핫스팟 업링크에서는 패킷 수 자체가 비용이다.
            // 다만 신뢰 없는 채널이라 잃을 수 있으므로 조용해도 가끔은 다시 보낸다.
            bool changed = !SameClamps(sendClamps, lastSentClamps);
            if (changed || clampKeepaliveTimer >= ClampKeepalive)
            {
                RpcApplyClamps(sendSeq, sendClamps);
                clampKeepaliveTimer = 0f;

                if (lastSentClamps.Length != sendClamps.Length)
                    lastSentClamps = new ClampState[sendClamps.Length];
                Array.Copy(sendClamps, lastSentClamps, sendClamps.Length);
            }

            // ── 저중력장 (S15P21B202-505) ───────────────────────
            //
            // **빔은 켜짐/꺼짐이 전송되지 않아 호스트에게만 보였다.** 빔 그림은
            // LowGravityField.SetActive 안에서 IsActive 가 참일 때만 붙는데, 그 값을
            // 바꾸는 유일한 경로가 TractorBeamController 의 `if (!Authoritative) return;`
            // 뒤에 있다. 실측으로 이 파일에 `Field` 라는 글자가 0회였다.
            //
            // 고정쇠와 같은 성질이라 같은 방식으로 보낸다 — 사람이 F 를 누르는 순간을
            // 빼면 내내 그대로이므로 바뀔 때만, 그리고 신뢰 없는 채널이라 가끔 다시.
            if (sendFields.Length != vehicleSlots.Count)
                sendFields = new FieldState[vehicleSlots.Count];

            for (int i = 0; i < vehicleSlots.Count; i++)
            {
                var vehicle = VehicleAt(vehicleSlots[i]);

                // **게터를 한 번만 읽는다**(!417 AI 리뷰). `TransportVehicle.Field` 는
                // 부를 때마다 `Configure` 와 `SetPhysicsAuthority` 를 다시 부른다 —
                // 부작용이 있는 게터다. 처음에는 이 줄에서 두 번 읽었고, 그러면 보내는
                // 틱마다 탈것 수의 두 배로 그 일이 돈다.
                var field = vehicle != null ? vehicle.Field : null;

                sendFields[i] = new FieldState
                {
                    Vehicle = vehicleSlots[i],
                    Active = field != null && field.IsActive,
                };
            }

            if (ShouldSendFields(sendFields, lastSentFields, fieldKeepaliveTimer, ClampKeepalive))
            {
                RpcApplyFields(sendSeq, sendFields);
                fieldKeepaliveTimer = 0f;

                if (lastSentFields.Length != sendFields.Length)
                    lastSentFields = new FieldState[sendFields.Length];
                Array.Copy(sendFields, lastSentFields, sendFields.Length);
            }

            // ── 용접 ────────────────────────────────────────────
            //
            // 파츠 순서는 **자세와 같은 색인**을 쓴다. 여기서 따로 줄을 세우면 -315 가
            // 기록한 종류의 사고(개수는 같은데 순서만 어긋남)를 새로 만들게 된다.
            // 파츠가 아닌 항목(탈것·도구)은 개수 0 이다.
            BuildWeldSnapshot();
            if (!SameWeld() || weldKeepaliveTimer >= WeldKeepalive)
            {
                RpcApplyWeld(sendWeldCounts, sendWeld);
                weldKeepaliveTimer = 0f;
                RememberSentWeld();
            }


            // ── 조립 연결 ────────────────────────────────────────
            //
            // 용접 **뒤에** 보낸다. 접합을 세우면 Bind 가 비드에게 강도를 물어보므로
            // (WeldBead.RefreshBetween) 비드가 먼저 서 있어야 제 강도가 나온다.
            // 같은 틱에 신뢰 채널로 잇달아 보내므로 순서가 지켜진다.
            BuildLinkSnapshot();
            if (!SameLinks() || linkKeepaliveTimer >= LinkKeepalive)
            {
                RpcApplyLinks(tracked.Count, sendLinks);
                linkKeepaliveTimer = 0f;
                RememberSentLinks();
            }

            // ── 속도 ────────────────────────────────────────────
            //
            // 이어받을 때만 쓰이는 값이라 드물게 보낸다 — VelocityInterval 의 주석 참고.
            if (velocityTimer >= VelocityInterval)
            {
                velocityTimer = 0f;

                if (sendVelocities.Length != tracked.Count)
                    sendVelocities = new Velocity[tracked.Count];

                for (int i = 0; i < tracked.Count; i++)
                {
                    var body = tracked[i];
                    sendVelocities[i] = body == null
                        ? default
                        : new Velocity
                        {
                            Linear = body.linearVelocity,
                            Angular = body.angularVelocity,
                            Kinematic = body.isKinematic,
                        };
                }

                RpcApplyVelocities(sendVelocities);
            }
            // ── 운석 예고 ────────────────────────────────────────
            SendDrops();

            // ── 도구 휴대 ────────────────────────────────────────
            // 기존 월드 전송 주기에 묶는다. 손 좌표 대신 소유자/동작/진행도만 전달한다.
            if (sendToolViews.Length != tools.Count) sendToolViews = new ToolViewState[tools.Count];
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                sendToolViews[i] = tool != null ? new ToolViewState
                {
                    RhythmCooldown = tool is MeteorBat cooldownBat ? cooldownBat.Cooldown : 0f,
                    RhythmSequence = tool is MeteorBat bat ? bat.RhythmSequence : 0,
                    RhythmGrade = tool is MeteorBat rhythmBat ? rhythmBat.RhythmGrade : (byte)0,
                    Owner = tool.IsEquipped ? NetPlayer.NetIdOfRig(tool.Owner) : 0,
                    Action = tool.IsEquipped ? tool.ViewAction : (byte)0,
                    Progress = tool.ViewProgress, Direction = tool.Owner != null ? tool.Owner.AimForward : tool.transform.forward
                } : default;
            }
            RpcToolViews(sendSeq, sendToolViews);
            // ── 낙하 운석 그림 ──────────────────────────────────
            SendMeteors();

            // ── 착지한 운석(흔적) ───────────────────────────────
            SendScars();
        }

        /// <summary>한 묶음에 실을 수 있는 흔적 개수. 움직임 묶음(신뢰 없음)에만 쓴다.</summary>
        static int UnreliableScarCapacity
        {
            get
            {
                int max = Transport.active != null
                    ? Transport.active.GetMaxPacketSize(Channels.Unreliable)
                    : 1194;
                return Mathf.Max(0, (max - PacketHeadroom) / ScarStateBytes);
            }
        }

        /// <summary>
        /// 착지한 운석을 내려보낸다 (S15P21B202-355).
        ///
        /// <para><b>왜 필요한가.</b> 참가자는 착탄을 스스로 계산하지 않으므로
        /// (<c>MeteorClockBridge</c> 가 조기 반환한다) 착지한 돌이 <b>아예 없다</b>.
        /// 그런데 상호작용은 서버가 판정하므로 <b>안 보이는 돌을 집을 수 있었다</b> —
        /// 2026-09-10 QA: "호스트 화면에서 운석이 보이는데 나는 안 보여. 그런데 보이지만
        /// 않고 상호작용은 돼."</para>
        ///
        /// <para><b>두 묶음으로 나눈다.</b> 낙하 운석처럼 매 틱 전부 보낼 수 없다 —
        /// 동시 흔적이 30~40개(상한 200, 수명 30초)라 한 묶음이 1.3KB 이고, 그것을 30Hz
        /// 로 내면 사람당 40KB/s 다.</para>
        ///
        /// <list type="bullet">
        /// <item><b>전체 목록</b> — 번호 집합이 바뀔 때(착탄·상한 정리)와 되보내기 간격마다.
        /// <b>신뢰 채널</b>이라 한 번 가면 도착한다. 40개 × 32B 는 약 1.3KB 이고 kcp 가
        /// 신뢰 채널을 조각내 나르므로 쪼갤 필요가 없다.</item>
        /// <item><b>움직임</b> — <b>깨어 있는 강체만</b>. 쉬는 돌은 잠들어 있어 보낼 것이
        /// 없다. 사람이 끌고 있는 것은 사람당 최대 하나라 현실적으로 1~2개다 —
        /// 2개 × 32B × 30Hz 는 2KB/s 이고, 이미 도는 낙하 채널의 최대치(28개 × 40B ×
        /// 30Hz 는 약 34KB/s)에 비하면 없는 것과 같다.</item>
        /// </list>
        /// </summary>
        void SendScars()
        {
            var spawner = MeteorSpawner;
            if (spawner == null) return;

            var scars = spawner.Scars;
            int total = scars != null ? scars.Count : 0;

            if (!SameScarIds(scars) || scarKeepaliveTimer >= ScarKeepalive)
            {
                int n = 0;
                for (int i = 0; i < total; i++)
                    if (scars[i] != null && scars[i].Meteor != null) n++;

                if (sendScarSet.Length != n) sendScarSet = new ScarState[n];
                if (lastScarIds.Length != n) lastScarIds = new int[n];

                int w = 0;
                for (int i = 0; i < total && w < n; i++)
                {
                    var scar = scars[i];
                    if (scar == null || scar.Meteor == null) continue;

                    lastScarIds[w] = scar.Id;
                    sendScarSet[w++] = StateOf(scar);
                }

                RpcScarSet(sendSeq, sendScarSet);
                scarKeepaliveTimer = 0f;
                return;     // 같은 틱에 두 묶음을 내지 않는다
            }

            // 움직이는 것만. 잠든 강체는 자세가 그대로다.
            int cap = UnreliableScarCapacity;
            int awake = 0;
            for (int i = 0; i < total && awake < cap; i++)
                if (IsScarAwake(scars[i])) awake++;

            if (awake == 0) return;

            // **상한에 닿았을 때만 전부 센다** (`!321` AI 리뷰). 처음에는 경고를 아직
            // 내지 않았으면 매 틱 전체를 한 번 더 훑었는데, 경고가 한 번도 안 뜨는
            // 정상 상황에서 그 순회가 영구히 매 틱 돌았다.
            if (!warnedScarCap && cap > 0 && awake == cap)
            {
                int moving = 0;
                for (int i = 0; i < total; i++) if (IsScarAwake(scars[i])) moving++;
                if (moving > cap)
                {
                    warnedScarCap = true;
                    Debug.LogWarning($"[WorldSync] 움직이는 흔적이 한 묶음에 안 들어간다 — {moving}개 중 {cap}개만 보낸다");
                }
            }

            if (sendScarMoves.Length != awake) sendScarMoves = new ScarState[awake];

            int m = 0;
            for (int i = 0; i < total && m < awake; i++)
            {
                var scar = scars[i];
                if (!IsScarAwake(scar)) continue;
                sendScarMoves[m++] = StateOf(scar);
            }

            RpcScarMoves(sendSeq, sendScarMoves);
        }

        /// <summary>
        /// 이 흔적이 지금 움직이는가. <b>잠든 강체는 보낼 것이 없다</b> — 유니티가
        /// 쉬는 물체를 재우고, 사람이 집어 흔들면 깨어난다. 그래서 이 한 줄이
        /// "끌리는 돌만" 을 그대로 뜻한다.
        /// </summary>
        static bool IsScarAwake(ScarRecord scar)
        {
            if (scar == null || scar.Meteor == null) return false;

            // **`Meteor.Body` 를 쓴다 — 여기서 GetComponent 를 부르면 안 된다**
            // (`!321` AI 리뷰). 이 함수는 송신 틱마다 흔적 전체에 대해 불리므로,
            // 상한 200개 × 30Hz 면 프레임당 수백 회가 된다. `-374` 가 열려 있는
            // 동안 그런 것을 얹으면 원인을 가릴 수 없다.
            var body = scar.Meteor.Body;
            if (body == null) return false;

            // **잡힌 돌은 kinematic 이라 "잠든" 것으로 보인다** (`!321` AI 리뷰 높음).
            //
            // `PlayerRig.BeginCarriedBody`(`:3110`)가 집는 순간 `isKinematic = true` 로
            // 만들고, 유니티에서 kinematic 강체는 `IsSleeping()` 이 **항상 참**이다.
            // `IsSleeping()` 만 봤을 때는 **끌고 다니는 내내 이동 묶음이 비어서**,
            // 참가자 화면에서 돌이 2초 되보내기마다 툭툭 순간이동했다 — 이 이슈의
            // 완료 조건 1번("한쪽이 옮기면 다른 쪽에서도 움직인다")이 무너지는 자리다.
            //
            // 흔적에게 `isKinematic` 은 곧 "누가 들고 있다" 다. 착지한 돌은 `Settle` 이
            // `isKinematic = false` 로 두고(`Meteor.cs:749`), 그것을 참으로 바꾸는 곳은
            // 운반뿐이며 `RestoreCarriedBodyIfUnused` 가 놓을 때 되돌린다.
            return !body.IsSleeping() || body.isKinematic;
        }

        static ScarState StateOf(ScarRecord scar)
        {
            var body = scar.Meteor.Body;      // 위와 같은 이유로 GetComponent 를 쓰지 않는다
            return body != null
                ? new ScarState { Id = scar.Id, Position = body.position, Rotation = body.rotation }
                : new ScarState
                {
                    Id = scar.Id,
                    Position = scar.Meteor.transform.position,
                    Rotation = scar.Meteor.transform.rotation,
                };
        }

        /// <summary>
        /// 번호 집합이 지난번과 같은가. <b>자세는 보지 않는다</b> — 자세가 바뀐 것은
        /// 움직임 묶음이 나르고, 전체 목록은 <b>무엇이 생기고 사라졌는지</b>만 알린다.
        /// </summary>
        bool SameScarIds(System.Collections.Generic.IReadOnlyList<ScarRecord> scars)
        {
            int w = 0;
            int total = scars != null ? scars.Count : 0;

            for (int i = 0; i < total; i++)
            {
                var scar = scars[i];
                if (scar == null || scar.Meteor == null) continue;

                if (w >= lastScarIds.Length || lastScarIds[w] != scar.Id) return false;
                w++;
            }

            return w == lastScarIds.Length;
        }

        /// <summary>
        /// 흔적 전체 목록. <b>신뢰 채널</b>이다 — 이 묶음을 잃으면 참가자 화면에서 돌이
        /// 사라지거나 유령이 남고, 다음 착탄까지 그대로다.
        /// </summary>
        [ClientRpc]
        void RpcScarSet(uint seq, ScarState[] states)
        {
            if (isServer) return;               // 호스트에는 진짜 흔적이 있다
            if (!ready) return;

            // **이동 묶음과 같은 순번 축으로 본다** (`!321` AI 리뷰).
            //
            // 두 묶음은 채널이 달라(이쪽은 신뢰, 이동은 비신뢰) 서로 순서가 보장되지
            // 않는다. 순번을 안 보면 **늦게 도착한 전체 목록이 최신 이동을 덮어**,
            // 끌고 있는 돌이 2초마다 옛 자리로 되돌아간다.
            //
            // 늦은 목록은 버린다. 그러면 그 목록이 실어 온 생성·삭제도 함께 놓치는데,
            // 되보내기가 2초마다 오므로 스스로 낫는다 — 돌이 뒤로 튀는 것보다 낫다.
            if (!IsNewer(seq, lastScarSeq, everScar)) return;
            lastScarSeq = seq;
            everScar = true;

            var spawner = MeteorSpawner;
            if (spawner != null) spawner.ApplyNetworkScars(states, full: true);
        }

        /// <summary>
        /// 움직이는 흔적의 자세. 신뢰 없는 채널이고 <b>아무것도 만들지 않는다</b> —
        /// 만드는 것은 전체 목록의 몫이다(<c>ApplyNetworkScars</c> 의 <c>full</c>).
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcScarMoves(uint seq, ScarState[] states)
        {
            if (isServer) return;
            if (!ready) return;
            if (!IsNewer(seq, lastScarSeq, everScar)) return;
            lastScarSeq = seq;
            everScar = true;

            var spawner = MeteorSpawner;
            if (spawner != null) spawner.ApplyNetworkScars(states, full: false);
        }

        /// <summary>한 묶음에 실을 수 있는 자세 개수. 전송 계층이 정하는 값이다.</summary>
        static int UnreliablePoseCapacity
        {
            get
            {
                int max = Transport.active != null
                    ? Transport.active.GetMaxPacketSize(Channels.Unreliable)
                    : 1194;
                return Mathf.Max(0, (max - PacketHeadroom) / PoseBytes);
            }
        }

        /// <summary>
        /// 운석 예고 하나. <see cref="Metagame.Gameplay.Hazards.PendingMeteorDrop"/> 은 필드가
        /// 열이지만 <b>여섯은 <c>PhysicsScale</c> 상수</b>라 양쪽이 이미 같다 — 달라지는 넷만
        /// 싣는다(24바이트). 그리고 그쪽은 <c>readonly struct</c> 라 Mirror 위버가 쓰기를
        /// 만들지 못하므로, <see cref="ClampState"/> 처럼 평범한 필드 구조체를 따로 둔다.
        /// </summary>
        public struct DropState
        {
            public Vector3 Point;
            public float Remaining;
            public float Warning;
            public float Radius;
        }

        /// <summary>
        /// 이 씬의 운석 스포너. 없으면 <c>null</c> 이고 그때는 예고를 주고받지 않는다
        /// (샌드박스·테스트 씬).
        ///
        /// <para><b>못 찾으면 다시 찾는다</b>(AI 리뷰 지적, 2026-09-08). 예전에는 첫 조회에
        /// 표시를 세워 두고 다시 보지 않았다 — 그 순간 스포너가 아직 없으면
        /// <b>영구히 null</b> 이고, 예고·낙하 그림 동기화가 <b>경고 한 줄 없이 통째로
        /// 꺼진다.</b> 이 클래스가 이미 두 번 겪은 모양이다(늦게 뜨는 게임 씬 ·
        /// 나중에 생기는 견인 장치). <c>FindAnyObjectByType</c> 은 공짜가 아니므로
        /// 못 찾은 동안은 1초에 한 번만 다시 본다.</para>
        /// </summary>
        Metagame.Gameplay.Hazards.MeteorSpawner MeteorSpawner
        {
            get
            {
                if (meteorSpawnerCached != null) return meteorSpawnerCached;

                if (Time.unscaledTime - lastSpawnerLook < SpawnerLookInterval)
                    return null;

                lastSpawnerLook = Time.unscaledTime;
                meteorSpawnerCached =
                    FindAnyObjectByType<Metagame.Gameplay.Hazards.MeteorSpawner>();

                return meteorSpawnerCached;
            }
        }


        /// <summary>
        /// 지금 용접 상태를 <see cref="sendWeld"/> 에 담는다 (S15P21B202-386).
        ///
        /// <para>개수 배열은 <see cref="tracked"/> 와 길이가 같고, 파츠가 아닌 자리는 0 이다.
        /// 사슬은 그 뒤로 이어 붙인 하나의 평평한 배열이다 — 배열의 배열은 Mirror 가
        /// 직렬화하지 못한다.</para>
        /// </summary>
        void BuildWeldSnapshot()
        {
            if (sendWeldCounts.Length != tracked.Count)
                sendWeldCounts = new int[tracked.Count];

            weldChains.Clear();

            for (int i = 0; i < tracked.Count; i++)
            {
                sendWeldCounts[i] = 0;

                // 부품 조회는 Collect 가 만들어 둔 색인을 쓴다 (S15P21B202-388) — "몇 번이
                // 어느 부품인가" 를 두 곳에서 각자 답하면 언젠가 갈린다.
                var part = i < trackedParts.Count ? trackedParts[i] : null;
                if (part == null) continue;

                var bead = part.GetComponent<WeldBead>();
                if (bead == null) continue;

                int count = bead.ChainCount;
                for (int c = 0; c < count; c++) weldChains.Add(bead.ReadChain(c));
                sendWeldCounts[i] = count;
            }

            if (sendWeld.Length != weldChains.Count)
                sendWeld = new WeldBead.ChainState[weldChains.Count];
            weldChains.CopyTo(sendWeld);
        }

        /// <summary>보낸 것과 같은가. 용접은 사람이 쏘는 몇 초를 빼면 내내 그대로다.</summary>
        bool SameWeld()
        {
            if (sendWeldCounts.Length != lastSentWeldCounts.Length) return false;
            if (sendWeld.Length != lastSentWeld.Length) return false;

            for (int i = 0; i < sendWeldCounts.Length; i++)
                if (sendWeldCounts[i] != lastSentWeldCounts[i]) return false;

            for (int i = 0; i < sendWeld.Length; i++)
            {
                var a = sendWeld[i];
                var b = lastSentWeld[i];
                if (a.BlobCount != b.BlobCount) return false;
                if (a.PadWeldIntact != b.PadWeldIntact) return false;
                if (a.HasBrushFrom != b.HasBrushFrom) return false;
                if (Mathf.Abs(a.TipRadius - b.TipRadius) > 1e-4f) return false;
                if ((a.Origin - b.Origin).sqrMagnitude > 1e-8f) return false;
                if ((a.Direction - b.Direction).sqrMagnitude > 1e-8f) return false;
                if ((a.BrushFrom - b.BrushFrom).sqrMagnitude > 1e-8f) return false;
            }

            return true;
        }

        void RememberSentWeld()
        {
            if (lastSentWeldCounts.Length != sendWeldCounts.Length)
                lastSentWeldCounts = new int[sendWeldCounts.Length];
            Array.Copy(sendWeldCounts, lastSentWeldCounts, sendWeldCounts.Length);

            if (lastSentWeld.Length != sendWeld.Length)
                lastSentWeld = new WeldBead.ChainState[sendWeld.Length];
            Array.Copy(sendWeld, lastSentWeld, sendWeld.Length);
        }

        /// <summary>
        /// 용접 상태를 그대로 세운다 (S15P21B202-386).
        ///
        /// <para><b>신뢰 채널이다.</b> 스냅샷은 도착해야 뜻이 있다. 그리고 이 RPC 가
        /// 지금 붙어 있는 사람에게만 가므로, <b>늦게 들어온 사람에게는 keepalive 가
        /// 유일한 경로</b>다 — 그래서 조용해도 주기적으로 한 번은 보낸다.</para>
        /// </summary>
        [ClientRpc]
        void RpcApplyWeld(int[] counts, WeldBead.ChainState[] chains)
        {
            if (isServer) return;
            if (counts == null || chains == null) return;

            if (counts.Length != tracked.Count)
            {
                if (!warnedWeldMismatch)
                {
                    warnedWeldMismatch = true;
                    Debug.LogWarning($"[WorldSync] 용접 개수가 다르다 — 서버 {counts.Length} · 여기 {tracked.Count}");
                }
                return;
            }

            int cursor = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                int count = counts[i];

                var part = i < trackedParts.Count ? trackedParts[i] : null;
                if (part != null)
                {
                    var bead = part.GetComponent<WeldBead>();

                    // 없던 곳에 생겼으면 붙인다. 둘 다 없으면 할 일이 없다 —
                    // 빈 비드 컴포넌트를 만들어 두지 않는다.
                    if (bead == null && count > 0) bead = WeldBead.Attach(part);
                    if (bead != null) bead.RestoreChains(chains, cursor, count);
                }

                cursor += count;
            }
        }

        /// <summary>
        /// 물체 하나의 속도 (S15P21B202-388).
        ///
        /// <para><see cref="Pose"/> 에 끼워 넣지 않았다. 자세는 신뢰 없는 채널로 매 틱
        /// 가는데 속도는 신뢰 채널로 드물게 가면 된다 — 한 구조체에 넣으면 둘 중 하나의
        /// 주기를 상대에게 맞춰야 하고, 그러면 아끼려던 것을 그대로 잃는다.</para>
        /// </summary>
        public struct Velocity
        {
            public Vector3 Linear;
            public Vector3 Angular;

            /// <summary>
            /// 서버에서 이 물체가 kinematic 인가 (S15P21B202-389).
            ///
            /// <para><b>이어받을 때 이것이 없으면 무엇을 물리에 돌려줄지 알 수 없다.</b>
            /// 참가자 화면은 <see cref="tracked"/> 전부를 kinematic 으로 세우므로
            /// (<see cref="PrepareClient"/>) 자기 상태로는 답이 나오지 않는다. 부품은
            /// 상태에서 되계산할 수 있지만 탈것·도구는 그럴 근거가 없다 — 서버만 아는
            /// 값이라 서버가 말해 준다.</para>
            /// </summary>
            public bool Kinematic;
        }

        /// <summary>
        /// 지금 조립 연결을 <see cref="sendLinks"/> 에 담는다 (S15P21B202-388).
        ///
        /// <para>부품 순서는 <b>자세와 같은 색인</b>이다 — <see cref="trackedParts"/> 가
        /// <see cref="tracked"/> 와 자리를 맞춰 들고 있고, 부품이 아닌 자리는 null 이다.
        /// 여기서 따로 줄을 세우면 -315 가 기록한 종류의 사고(개수는 같은데 순서만 어긋나
        /// 남의 부품에 값이 들어감)를 새로 만든다.</para>
        /// </summary>
        void BuildLinkSnapshot()
        {
            PartAssemblyLink.ReadAll(trackedParts, linkStates);

            if (sendLinks.Length != linkStates.Count)
                sendLinks = new PartAssemblyLink.LinkState[linkStates.Count];
            linkStates.CopyTo(sendLinks);
        }

        /// <summary>보낸 것과 같은가. 접합은 조립·용접·파손이 있을 때만 바뀐다.</summary>
        bool SameLinks()
        {
            if (sendLinks.Length != lastSentLinks.Length) return false;

            for (int i = 0; i < sendLinks.Length; i++)
            {
                var a = sendLinks[i];
                var b = lastSentLinks[i];
                if (a.MovingPart != b.MovingPart) return false;
                if (a.TargetPart != b.TargetPart) return false;
                if (a.MovingSocket != b.MovingSocket) return false;
                if (a.TargetSocket != b.TargetSocket) return false;
            }

            return true;
        }

        void RememberSentLinks()
        {
            if (lastSentLinks.Length != sendLinks.Length)
                lastSentLinks = new PartAssemblyLink.LinkState[sendLinks.Length];
            Array.Copy(sendLinks, lastSentLinks, sendLinks.Length);
        }

        /// <summary>
        /// 조립 연결을 그대로 세운다 (S15P21B202-388).
        ///
        /// <para><b>부품 수를 함께 보낸다.</b> 접합 배열의 길이는 접합 수라서 그것만으로는
        /// 양쪽 부품 목록이 같은지 알 수 없다. 어긋난 채로 적용하면 번호가 남의 부품을
        /// 가리켜 <b>엉뚱한 두 부품이 이어진다</b> — 용접·고정쇠와 같은 이유로 개수를
        /// 먼저 확인하고, 다르면 손대지 않는다.</para>
        /// </summary>
        [ClientRpc]
        void RpcApplyLinks(int partCount, PartAssemblyLink.LinkState[] states)
        {
            if (isServer) return;
            if (!ready) return;

            if (partCount != tracked.Count)
            {
                if (!warnedLinkMismatch)
                {
                    warnedLinkMismatch = true;
                    Debug.LogWarning($"[WorldSync] 조립 개수가 다르다 — 서버 {partCount} · 여기 {tracked.Count}");
                }
                return;
            }

            PartAssemblyLink.RestoreAll(trackedParts, states, states != null ? states.Length : 0);
        }

        /// <summary>
        /// 속도를 받아 둔다 (S15P21B202-388).
        ///
        /// <para><b>여기서 Rigidbody 에 넣지 않는다.</b> 참가자 부품은 kinematic 이라 속도를
        /// 넣어도 움직이지 않는다 — 넣는 것은 물리를 이어받는 그 순간이고,
        /// <see cref="RestoreVelocities"/> 가 그때 부른다.</para>
        /// </summary>
        [ClientRpc]
        void RpcApplyVelocities(Velocity[] velocities)
        {
            if (isServer) return;
            if (!ready) return;
            if (velocities == null) return;

            if (velocities.Length != tracked.Count)
            {
                if (!warnedVelocityMismatch)
                {
                    warnedVelocityMismatch = true;
                    Debug.LogWarning($"[WorldSync] 속도 개수가 다르다 — 서버 {velocities.Length} · 여기 {tracked.Count}");
                }
                return;
            }

            if (recvVelocities.Length != velocities.Length)
                recvVelocities = new Velocity[velocities.Length];
            Array.Copy(velocities, recvVelocities, velocities.Length);
        }

        /// <summary>
        /// 마지막으로 받은 속도를 물체에 넣는다. 물리를 이어받을 때 부른다 (S15P21B202-388).
        ///
        /// <para><b>kinematic 인 물체는 건너뛴다.</b> 이어받는 쪽은 먼저 kinematic 을 끄고
        /// 이것을 부른다 — 순서가 뒤바뀌면 아무 일도 일어나지 않으므로 그 순서가 곧 조건이다.</para>
        ///
        /// <para>받은 것이 없으면 아무것도 하지 않는다. 속도가 0 인 것과 "모르는 것" 은
        /// 다르다 — 모를 때 0 을 넣으면 날아가던 물체를 세워 버린다.</para>
        /// </summary>
        public void RestoreVelocities()
        {
            if (recvVelocities.Length != tracked.Count) return;

            for (int i = 0; i < tracked.Count; i++)
            {
                var body = tracked[i];
                if (body == null || body.isKinematic) continue;

                body.linearVelocity = recvVelocities[i].Linear;
                body.angularVelocity = recvVelocities[i].Angular;
            }
        }

        /// <summary>받아 둔 속도가 있는가. 이어받는 쪽이 물어본다.</summary>
        public bool HasVelocities => recvVelocities.Length == tracked.Count && tracked.Count > 0;

        /// <summary>
        /// 그림이던 세계를 물리로 돌려놓는다 (S15P21B202-389).
        ///
        /// <para><b>이 컴포넌트가 사라지기 전에 해야 한다.</b> 이것은 서버가 스폰한
        /// 오브젝트라 연결이 끊기면 Mirror 가 지운다. 그런데 받아 둔 속도와
        /// <see cref="tracked"/> 순서를 아는 것이 여기뿐이므로, 넘겨받는 일은
        /// <see cref="OnStopClient"/> 안에서 끝내야 한다.</para>
        ///
        /// <para><b>씬은 내려가지 않는다.</b> 이어받는 동안 <c>offlineScene</c> 을 비워
        /// 두므로(<see cref="HostMigration"/>) 부품·용접·조립이 메모리에 그대로 남는다 —
        /// 그래서 여기서 할 일은 "다시 세우기" 가 아니라 <b>물리를 켜는 것</b>뿐이다.</para>
        ///
        /// <para>순서가 중요하다. <c>RemoteDecides</c> 를 먼저 내려야
        /// <see cref="SpaceshipPart.RefreshRepresentation"/> 이 kinematic 을 풀 수 있고,
        /// kinematic 이 풀린 뒤에야 속도가 들어간다.</para>
        /// </summary>
        void TakeOverPhysics()
        {
            // ① 이제 내가 정한다. 이 줄이 없으면 아래 RefreshRepresentation 이
            //    여전히 kinematic = true 를 쓴다(SpaceshipPart.ApplyRepresentation).
            WorldEvents.RemoteDecides = false;

            // ② 부품은 자기 상태로 물리 표현을 다시 쓴다 — 질량·레이어·구속까지 함께다.
            for (int i = 0; i < trackedParts.Count; i++)
            {
                var part = trackedParts[i];
                if (part != null) part.RefreshRepresentation();
            }

            // ③ 부품이 아닌 것(탈것·도구)은 되계산할 근거가 없다. 서버가 보내 준
            //    kinematic 을 그대로 쓴다.
            if (recvVelocities.Length == tracked.Count)
            {
                for (int i = 0; i < tracked.Count; i++)
                {
                    var body = tracked[i];
                    if (body == null) continue;
                    if (i < trackedParts.Count && trackedParts[i] != null) continue;

                    body.isKinematic = recvVelocities[i].Kinematic;
                }
            }

            // ④ 마지막으로 속도. kinematic 이 풀린 물체에만 들어간다.
            RestoreVelocities();

            Debug.Log($"[WorldSync] 물리를 이어받았다 · 물체 {tracked.Count}개 · "
                      + $"속도 {(HasVelocities ? "복원" : "없음")}");
        }
        void SendDrops()
        {
            var spawner = MeteorSpawner;
            if (spawner == null) return;

            var pending = spawner.Pending;
            int n = pending != null ? pending.Count : 0;
            if (sendDrops.Length != n) sendDrops = new DropState[n];
            for (int i = 0; i < n; i++)
            {
                var d = pending[i];
                sendDrops[i] = new DropState
                {
                    Point = d.ImpactPoint,
                    Remaining = d.RemainingSeconds,
                    Warning = d.WarningSeconds,
                    Radius = d.ImpactRadius,
                };
            }

            // 남은 시간은 참가자가 로컬에서 센다(AgeNetworkPending). 그래서 **목록이
            // 달라질 때만** 보낸다 — 고정쇠와 같은 이유다. 신뢰 없는 채널이라 잃을 수
            // 있으므로 조용해도 가끔 다시 보낸다.
            //
            // 한 묶음 한도를 따로 검사하지 않는 이유: 예고 하나가 24바이트이고 동시
            // 개수는 **한 공세의 상한**을 넘지 않는다(표의 최대가 5 — MeteorWaveTable.Rows).
            // 신뢰 없는 채널의 여유가 47개쯤이라 자리 수가 다르다. 낙하 자세(40바이트)는
            // 여유가 절반이라 그쪽에만 상한 검사를 두었다.
            bool changed = !SameDrops(sendDrops, lastSentDrops);
            if (!changed && dropKeepaliveTimer < ClampKeepalive) return;

            RpcApplyDrops(sendSeq, sendDrops);
            dropKeepaliveTimer = 0f;

            if (lastSentDrops.Length != sendDrops.Length)
                lastSentDrops = new DropState[sendDrops.Length];
            Array.Copy(sendDrops, lastSentDrops, sendDrops.Length);
        }

        /// <summary>
        /// 예고가 <b>달라졌는가</b>. 남은 시간은 참가자가 세므로 비교에서 뺀다 — 넣으면
        /// 매 틱 달라져서 "바뀔 때만 보낸다" 가 무의미해진다. 자리·크기·개수만 본다.
        /// </summary>
        static bool SameDrops(DropState[] a, DropState[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if ((a[i].Point - b[i].Point).sqrMagnitude > 0.0001f) return false;
                if (Mathf.Abs(a[i].Radius - b[i].Radius) > 0.0005f) return false;
                if (Mathf.Abs(a[i].Warning - b[i].Warning) > 0.0005f) return false;
            }
            return true;
        }

        /// <summary>
        /// 호스트가 정한 예고를 참가자에게 맞춘다.
        ///
        /// <para><b>호스트에서는 돌지 않아야 한다</b>(AI 리뷰 지적, 2026-09-08).
        /// <c>[ClientRpc]</c> 는 호스트에서도 실행되는데, <c>ApplyNetworkPending</c> 은
        /// 목록을 <b>갈아 끼운다</b> — 그쪽 주석이 "권위 쪽에서 부르면 안 된다, 자기
        /// 스케줄을 지운다" 고 적어 둔 그 호출이다. 게이트가 없으면 호스트의 예고가
        /// 마지막으로 보낸 값(최대 keepalive 0.5초 전)으로 <b>되감긴다.</b></para>
        ///
        /// <para>채널은 고정쇠와 같다 — 끊임없이 다시 보내는 <b>현재 상태</b>이므로
        /// 신뢰 채널의 재전송 줄에 세우지 않는다(<see cref="RpcApply"/> 의 설명).
        /// 잃은 것은 keepalive 가 메우고 순서는 <paramref name="seq"/> 로 가른다.</para>
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyDrops(uint seq, DropState[] drops)
        {
            if (isServer) return;               // 호스트는 자기 스케줄이 정답이다
            if (!ready) return;
            if (!IsNewer(seq, lastDropSeq, everDrop)) return;
            lastDropSeq = seq;
            everDrop = true;

            var spawner = MeteorSpawner;
            if (spawner == null) return;

            // **이미 끝난 예고를 되살리지 않는다**(AI 리뷰 지적, 2026-09-08).
            //
            // `SameDrops` 는 남은 시간을 비교에서 뺀다 — 그래야 "바뀔 때만 보낸다" 가
            // 뜻이 있다. 그래서 조용한 사이에 나가는 keepalive 는 **보낼 때의 남은 시간**을
            // 싣는다. 참가자가 로컬 카운트다운으로 먼저 만료시킨 뒤 그 묶음을 그대로 넣으면
            // 원이 사라졌다가 **되살아나 깜빡인다.** 피하고 쳐내는 데 쓰는 표시다.
            //
            // 그래서 끝난 것은 아예 빼고 넣는다. 개수가 줄어드는 순간은 목록이 달라지는
            // 순간이라 호스트가 곧 보내 준다(`SameDrops` 가 길이부터 본다).
            if (recvDrops == null) recvDrops = new List<Metagame.Gameplay.Hazards.PendingMeteorDrop>();
            recvDrops.Clear();

            for (int i = 0; i < drops.Length; i++)
            {
                if (drops[i].Remaining <= 0f) continue;

                recvDrops.Add(new Metagame.Gameplay.Hazards.PendingMeteorDrop(
                    drops[i].Point, drops[i].Remaining, drops[i].Warning, drops[i].Radius));
            }

            spawner.ApplyNetworkPending(recvDrops);
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcToolViews(uint seq, ToolViewState[] states)
        {
            if (isServer || !ready || states == null) return;
            if (states.Length != tools.Count)
            {
                if (!warnedToolMismatch)
                    Debug.LogWarning($"[WorldSync] 도구 개수가 다르다 — 서버 {states.Length} · 여기 {tools.Count}");
                warnedToolMismatch = true;
                return;
            }
            warnedToolMismatch = false;
            if (!IsNewer(seq, lastToolViewSeq, everToolView)) return;
            lastToolViewSeq = seq;
            everToolView = true;
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                if (tool == null) continue;
                var state = states[i];
                PlayerRig owner = null;
                if (state.Owner != 0 && NetworkClient.spawned.TryGetValue(state.Owner, out var identity))
                    owner = identity.GetComponent<NetPlayer>()?.VisualRig;
                if (tool is MeteorBat bat) bat.ApplyRhythmResult(state.RhythmSequence, state.RhythmGrade, state.RhythmCooldown);
                tool.ApplyRemoteHeld(state.Owner != 0);
                tool.ApplyRemotePresentation(owner, state.Action, state.Progress, state.Direction);
            }
        }

        /// <summary>낙하 자세 하나의 바이트 수 — 자세 28 + 속도 12 + 타격 가이드 13.</summary>
        const int MeteorPoseBytes = PoseBytes + 25;

        /// <summary>
        /// 한 묶음에 실을 수 있는 낙하 자세 개수. 공세 최대가 표에서 8개이므로 닿을 일이
        /// 없지만, 넘겼을 때 <b>전송 계층이 예외를 던지는 대신</b> 앞쪽만 보내고 알린다.
        /// </summary>
        static int UnreliableMeteorCapacity
        {
            get
            {
                int max = Transport.active != null
                    ? Transport.active.GetMaxPacketSize(Channels.Unreliable)
                    : 1194;
                return Mathf.Max(0, (max - PacketHeadroom) / MeteorPoseBytes);
            }
        }

        /// <summary>
        /// 살아 있는 운석의 자세를 내려보낸다 (S15P21B202-352).
        ///
        /// <para>참가자는 공세를 스스로 계산하지 않으므로(<c>MeteorSpawner.ApplyNetworkPending</c>
        /// 의 주석) 예고 원만 받으면 <b>떨어지는 돌이 안 보인다</b>. 그 그림을 여기서 나른다.</para>
        /// </summary>
        void SendMeteors()
        {
            var spawner = MeteorSpawner;
            if (spawner == null) return;

            var live = spawner.Live;
            int cap = UnreliableMeteorCapacity;

            // **없어진 운석 자리에 원점의 돌을 실어 보내지 않는다**(AI 리뷰 지적,
            // 2026-09-08). 예전에는 `live[i]` 가 null 이면 `default` 를 넣었는데, 그러면
            // 참가자 화면에 좌표 (0,0,0)·속도 0 의 돌 그림이 하나 생긴다. 착탄 프레임에
            // 목록이 비는 것은 정상 경로라 실제로 일어난다. 유효한 것만 세어 압축한다.
            int n = 0;
            if (live != null)
            {
                for (int i = 0; i < live.Count && n < cap; i++)
                    if (live[i] != null) n++;
            }

            int liveCount = live != null ? live.Count : 0;
            if (liveCount > cap && !warnedMeteorCap)
            {
                warnedMeteorCap = true;
                Debug.LogWarning($"[WorldSync] 운석이 한 묶음에 안 들어간다 — {liveCount}개 중 {cap}개만 보낸다");
            }

            // **0개일 때 빈 묶음을 계속 보내지 않는다** (AI 리뷰 지적, 2026-09-08).
            // 운석은 대부분의 시간에 0개다. 그런데도 매 송신 틱마다 RPC 를 내면 사람 수만큼
            // 빈 패킷이 상시 나간다 — 이 MR 이 문제로 든 그 업링크 비용이다.
            //
            // "0개" 는 참가자의 그림을 지우는 신호이고 이 채널은 신뢰가 없다. 그래서 0이
            // 된 뒤 ZeroRepeats 번은 계속 보낸다. **다만 그것만으로는 부족하다**(AI 리뷰
            // 3차 지적) — 그 0.5초 창이 손실 버스트에 겹치거나 `!ready`·seq 필터에 걸리면
            // 신호가 영영 안 간다. 그 경우를 받는 쪽 타임아웃이 막는다
            // (`ClearGhostsIfSilent`). 여기는 **빠른 정리**, 그쪽은 **최후 보증**이다.
            if (n == 0)
            {
                if (zeroSends >= ZeroRepeats) return;
                zeroSends++;
            }
            else zeroSends = 0;

            if (sendMeteors.Length != n) sendMeteors = new MeteorPose[n];

            int w = 0;
            for (int i = 0; i < liveCount && w < n; i++)
            {
                var meteor = live[i];
                if (meteor == null) continue;

                // 보내는 쪽도 받는 쪽도 `body` 로 읽고 쓴다 — 운석은 보간이 걸려 있어
                // `transform.position` 은 **표시 위치**다(FollowOnClient 의 주석과 같은 이유).
                var body = meteor.GetComponent<Rigidbody>();
                sendMeteors[w++] = body != null
                    ? new MeteorPose
                    {
                        Position = body.position,
                        Rotation = body.rotation,
                        Velocity = body.linearVelocity,
                        GroundHeight = meteor.GroundHeight,
                        Harmless = meteor.Harmless,
                        BatWindow = meteor.BatSwingWindowSeconds,
                        BatRadius = meteor.BatAssistRadius,
                    }
                    : new MeteorPose
                    {
                        Position = meteor.transform.position,
                        Rotation = meteor.transform.rotation,
                        Velocity = Vector3.zero,
                        GroundHeight = meteor.GroundHeight,
                        Harmless = meteor.Harmless,
                        BatWindow = meteor.BatSwingWindowSeconds,
                        BatRadius = meteor.BatAssistRadius,
                    };
            }

            RpcApplyMeteors(sendSeq, sendMeteors);
        }

        /// <summary>
        /// 낙하 그림이 <b>오래 소식이 없으면</b> 치운다 (S15P21B202-352).
        ///
        /// <para><b>왜 필요한가</b>(AI 리뷰 3차 지적). 그림을 지우는 신호는 "운석 0개"
        /// 묶음뿐이고 그 채널은 신뢰가 없다. 보내는 쪽이 0.5초 동안 반복해 주지만, 그
        /// 창이 손실 버스트에 겹치거나 아직 <c>ready</c> 가 아니라 걸러지면 신호가 영영
        /// 오지 않는다. 그림은 충돌러가 꺼진 채 중력만 받으므로 <b>지면을 뚫고 영원히
        /// 떨어진다</b> — 경기 중이라 <see cref="OnStopClient"/> 정리도 오지 않는다.</para>
        ///
        /// <para>그래서 마지막으로 받은 시점을 기억해 두고, 조용한 시간이 길어지면
        /// 스스로 치운다. 호스트가 운석을 계속 보내는 동안에는 매 틱 갱신되므로 이 길로
        /// 오지 않는다. <b>보내는 쪽의 반복은 빠른 정리, 이것은 최후 보증</b>이다.</para>
        /// </summary>
        void ClearGhostsIfSilent()
        {
            var spawner = MeteorSpawner;
            if (spawner == null || spawner.Ghosts.Count == 0) return;

            if (Time.unscaledTime - lastMeteorAt < GhostSilenceTimeout) return;

            Debug.LogWarning("[WorldSync] 낙하 자세가 "
                             + GhostSilenceTimeout.ToString("0.0")
                             + "초째 오지 않는다 — 남은 그림을 치운다(지우는 신호를 놓쳤을 수 있다)");
            spawner.ClearNetworkMeteors();
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyMeteors(uint seq, MeteorPose[] poses)
        {
            if (isServer) return;               // 호스트에는 진짜 운석이 있다
            if (!ready) return;
            if (!IsNewer(seq, lastMeteorSeq, everMeteor)) return;
            lastMeteorSeq = seq;
            everMeteor = true;

            lastMeteorAt = Time.unscaledTime;

            var spawner = MeteorSpawner;
            if (spawner != null) spawner.ApplyNetworkMeteors(poses);
        }

        static bool SameClamps(ClampState[] a, ClampState[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Released != b[i].Released) return false;
                if (Mathf.Abs(a[i].Progress - b[i].Progress) > 0.0005f) return false;
            }
            return true;
        }

        /// <summary>
        /// 이 묶음이 지금 쓸 것인가. <b>신뢰 없는 채널은 순서를 지켜 주지 않는다</b> —
        /// 늦게 온 옛 묶음을 그대로 쓰면 물체가 한 번 뒤로 튄다.
        ///
        /// <para>부호 없는 뺄셈으로 비교하므로 번호가 한 바퀴 돌아도 옳게 갈린다
        /// (uint 라 초당 30번 보내도 4년 반이 걸리지만, 공짜로 맞출 수 있는 것은 맞춰 둔다).</para>
        /// </summary>
        static bool IsNewer(uint seq, uint last, bool ever) => !ever || (int)(seq - last) > 0;

        /// <summary>
        /// 고정쇠의 진행도와 해제 여부를 맞춘다. 판정은 서버가 하고 여기서는 따라만 간다.
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyFields(uint seq, FieldState[] states)
        {
            if (isServer) return;               // 호스트는 자기 물리가 정답이다
            if (states == null || !ready) return;
            if (!IsNewer(seq, lastFieldSeq, everField)) return;
            lastFieldSeq = seq;
            everField = true;

            // **개수가 달라도 버리지 않는다.** 고정쇠와 다른 점이다 — 저쪽은 색인이
            // 곧 자리라서 개수가 어긋나면 남의 고정쇠에 값이 들어간다. 여기는 번호를
            // 함께 실어 보내므로 짝이 틀릴 수 없고, 못 찾은 번호만 건너뛰면 된다.
            for (int i = 0; i < states.Length; i++)
            {
                var vehicle = VehicleAt(states[i].Vehicle);
                if (vehicle == null) continue;

                var field = vehicle.Field;
                if (field != null) field.ApplyNetworkActive(states[i].Active);
            }
        }

        /// <summary>
        /// 지금 저중력장 상태를 보낼 차례인가 (S15P21B202-505).
        ///
        /// <para><b>왜 순수 메서드로 빼는가.</b> 이 판단이 <see cref="SendFromServer"/> 안의
        /// 조건문으로만 있으면 검증이 닿지 못한다 — <c>-400</c>·<c>-438</c>·<c>-448</c> 에서
        /// 세 번 겪은 자리다. 밖에서 부를 수 있게 두면 대조가 실제 경로를 지난다.</para>
        ///
        /// <para>바뀌면 보내고, 조용해도 <paramref name="keepalive"/> 마다 다시 보낸다 —
        /// 신뢰 없는 채널이라 한 번 잃으면 끝나기 때문이다(고정쇠와 같은 이유).</para>
        /// </summary>
        public static bool ShouldSendFields(FieldState[] now, FieldState[] lastSent,
                                            float keepaliveTimer, float keepalive) =>
            !SameFields(now, lastSent) || keepaliveTimer >= keepalive;

        static bool SameFields(FieldState[] a, FieldState[] b)
        {
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
                if (a[i].Vehicle != b[i].Vehicle || a[i].Active != b[i].Active) return false;

            return true;
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyClamps(uint seq, ClampState[] states)
        {
            if (isServer) return;               // 호스트는 자기 물리가 정답이다
            if (states == null || !ready) return;
            if (!IsNewer(seq, lastClampSeq, everClamp)) return;
            lastClampSeq = seq;
            everClamp = true;

            if (states.Length != clamps.Count)
            {
                if (!warnedClampMismatch)
                {
                    warnedClampMismatch = true;
                    Debug.LogWarning($"[WorldSync] 고정쇠 개수가 다르다 — 서버 {states.Length} · 여기 {clamps.Count}");
                }
                return;
            }
            warnedClampMismatch = false;

            for (int i = 0; i < clamps.Count; i++)
                if (clamps[i] != null) clamps[i].ApplyNetworkState(states[i].Progress, states[i].Released);
        }

        // ── 용접 — 상태가 아니라 행동을 실어 나른다 ─────────────

        /// <summary>
        /// 서버에서 용접이 진행되면 그 사실을 모두에게 알린다.
        /// 부품이 이미 같은 자리에 맞춰져 있으므로, 같은 자리에서 같은 양만큼 자라게 하면
        /// 같은 모양이 나온다 — 덩어리 사슬을 통째로 보내지 않아도 된다(-152).
        /// </summary>
        [ClientRpc]
        void RpcWeldGrown(Vector3 nozzle, float amount)
        {
            if (isServer) return;
            WeldBead.TryGrowNear(nozzle, amount, out _);
        }

        [ClientRpc]
        void RpcWeldShrunk(Vector3 nozzle, float amount)
        {
            if (isServer) return;
            WeldBead.TryShrinkNear(nozzle, amount, out _);
        }

        /// <summary>
        /// 자세를 내려보낸다 — <b>신뢰 없는 채널</b>이다 (S15P21B202-328).
        ///
        /// <para>예전에는 기본값인 신뢰 채널로 보냈다. 그런데 이것은 <b>끊임없이 다시
        /// 보내지는 현재 상태</b>이지 놓치면 안 되는 사건이 아니다. 신뢰 채널에 실으면
        /// 패킷 하나가 빠졌을 때 그 뒤에 도착한 <b>더 새로운</b> 자세들이 재전송을
        /// 기다리며 줄을 선다(head-of-line blocking). 방장이 모바일 핫스팟처럼 흔들리는
        /// 업링크에 있으면 그 줄이 계속 길어져, 참가자 화면만 점점 뒤로 밀린다 —
        /// 2026-09-08 2대 PC 실측에서 나온 그 증상이다.</para>
        ///
        /// <para>신뢰 없는 채널에서는 잃은 묶음을 그냥 버리고 다음 묶음으로 넘어간다.
        /// 매 틱 전체 상태를 다시 보내므로 손실은 저절로 메워진다. 대신 순서가 보장되지
        /// 않아 <paramref name="seq"/> 로 옛 묶음을 걸러 낸다.</para>
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApply(uint seq, Pose[] poses) => ApplyPoses(seq, poses);

        /// <summary>
        /// 자세가 한 묶음에 안 들어갈 만큼 많아졌을 때만 쓰는 신뢰 채널 경로.
        /// 지금 씬에서는 불리지 않는다 — 늘어났을 때 조용히 끊기지 않게 두는 안전줄이다.
        /// </summary>
        [ClientRpc]
        void RpcApplySafe(uint seq, Pose[] poses) => ApplyPoses(seq, poses);

        void ApplyPoses(uint seq, Pose[] poses)
        {
            if (isServer) return;               // 호스트 모드에서는 내 물리가 곧 정답이다
            if (poses == null) return;
            if (!IsNewer(seq, lastPoseSeq, everPose)) return;

            // 아직 챙기는 중이다. 이때의 개수 차이는 정상이라 알릴 것이 없다 —
            // 내 게임 씬이 방장보다 늦게 뜨면 반드시 한 번은 지나가는 자리다.
            if (!ready) return;

            // 개수가 다르면 서로 다른 세계를 보고 있다는 뜻이다. 억지로 맞추면
            // 엉뚱한 물체가 엉뚱한 자리로 간다 — 그럴 때는 손대지 않고 알린다.
            if (poses.Length != tracked.Count)
            {
                // 매 전송마다 찍으면 로그가 덮인다. 처음 한 번만 알린다.
                if (!warnedMismatch)
                {
                    warnedMismatch = true;
                    Debug.LogWarning($"[WorldSync] 개수가 다르다 — 서버 {poses.Length} · 여기 {tracked.Count}");
                }
                return;
            }
            warnedMismatch = false;

            lastPoseSeq = seq;
            everPose = true;
            targets = poses;

            // 호스트와 **같은 규칙**으로 접는다 — 같은 번호의 묶음이면 값이 같아야 한다.
            MarkBundle(seq, poses, poses.Length);
        }

        void FollowOnClient()
        {
            float t = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);

            for (int i = 0; i < tracked.Count && i < targets.Length; i++)
            {
                var body = tracked[i];
                if (body == null) continue;

                // **방어선** (S15P21B202-352). PrepareClient 가 kinematic 으로 세워 두지만
                // 게임플레이 코드가 나중에 다시 풀 수 있다. 탈것으로 바뀐 파츠는
                // `part.enabled = false` 라 SpaceshipPart 의 게이트를 타지 않으므로 그쪽은
                // 이 한 줄만이 막는다. 풀린 채로 두면 로컬 물리가 서버 자세와 다툰다.
                if (!body.isKinematic) body.isKinematic = true;

                if (body.TryGetComponent<SpaceshipPart>(out var part))
                    part.ApplyDockedToPad(targets[i].DockedToPad);

                // `transform` 이 아니라 `body` 로 읽고 쓴다. 탈것은 보간
                // (`RigidbodyInterpolation.Interpolate`)이 걸려 있어 `transform.position` 은
                // **보간된 표시 위치**다 — 그 값을 Lerp 의 출발점으로 다시 읽으면 되먹임으로
                // 계속 뒤로 끌린다. 보내는 쪽(`Read`)도 `body.position` 을 쓰므로 대칭이 맞는다.
                body.position = Vector3.Lerp(body.position, targets[i].Position, t);
                body.rotation = Quaternion.Slerp(body.rotation, targets[i].Rotation, t);
            }
        }
    }
}
