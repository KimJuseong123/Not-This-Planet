using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using MeteorPose = Metagame.Gameplay.Hazards.MeteorSpawner.NetworkMeteorPose;

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

        /// <summary>자세 하나의 바이트 수 — Vector3 12 + Quaternion 16. Mirror 는 둘 다 그대로 쓴다.</summary>
        const int PoseBytes = 28;

        /// <summary>RPC 머리말·배치 여유. 정확히 셀 이유는 없고 넉넉히 잡는다.</summary>
        const int PacketHeadroom = 64;

        /// <summary>
        /// 고정쇠 상태는 바뀌지 않아도 이 간격마다 한 번은 다시 보낸다.
        /// 신뢰 없는 채널이라 한 번 잃으면 다음 변화까지 어긋난 채로 남기 때문이다.
        /// </summary>
        const float ClampKeepalive = 0.5f;

        /// 받은 자세로 옮겨 가는 속도. 20Hz 로 받은 사이를 메운다.
        const float Smoothing = 20f;

        [Serializable]
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        readonly List<Rigidbody> tracked = new List<Rigidbody>();

        /// <summary>고정쇠. 위치가 아니라 <b>진행도</b>가 사람마다 달라지는 것들이다.</summary>
        readonly List<PartClamp> clamps = new List<PartClamp>();
        ClampState[] clampTargets = Array.Empty<ClampState>();
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
        bool meteorSpawnerLooked;
        float clampKeepaliveTimer;

        // 도구 휴대 (S15P21B202-352). 고정쇠와 같은 모양 — 바뀔 때만 + keepalive.
        //
        // 자세만 나르면 부족하다. 장착 판정은 서버에서만 도는데(-145) 그때
        // `ToolBase.OnEquipped` 가 자식 `Visual_ZUpToYUp` 을 손에 쥔 각도로 돌리고
        // 휴대 중 충돌을 끈다. 참가자 기계에서는 그 함수가 한 번도 불리지 않아
        // **뿌리는 제자리에 오는데 메시가 어긋나** 있었고 남의 도구가 몸을 막았다.
        readonly List<ToolBase> tools = new List<ToolBase>();
        bool[] sendTools = Array.Empty<bool>();
        bool[] lastSentTools = Array.Empty<bool>();
        float toolKeepaliveTimer;
        uint lastToolSeq;
        bool everTool;
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

        /// <summary>클라이언트로서 챙길 것이 남았는가. 스폰이 등록된 뒤에만 참이 된다.</summary>
        bool needsClientPrepare;

        public override void OnStartServer()
        {
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

        // ── 로그 진단용 (S15P21B202-352) ──────────────────────────────────
        //
        // 화면의 GUI 로 값을 읽어 손으로 적는 것은 느리고 놓치기 쉽다. 대신 양쪽 로그에
        // 같은 형식으로 남겨 **대조로 일치 여부를 증명**한다. NetDiagnostics 가 쓴다.

        /// <summary>지금 나르고 있는 물체 수. 양쪽이 다르면 그 순간 동기화가 멈춘다.</summary>
        public int TrackedCount => tracked.Count;

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

                Add(tool.GetComponent<Rigidbody>());

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
                : new Pose { Position = body.position, Rotation = body.rotation };
        }

        public override void OnStopClient()
        {
            if (!isServer) WorldEvents.RemoteDecides = false;
            if (!isServer) TransportVehicle.AnyActivated -= OnVehicleActivated;

            // 낙하 그림은 충돌러가 꺼져 있어 남으면 지면을 뚫고 영원히 떨어진다.
            if (!isServer && MeteorSpawner != null) MeteorSpawner.ClearNetworkMeteors();
        }

        public override void OnStopServer()
        {
            // 남겨 두면 다음 매치에서 이미 사라진 오브젝트를 향해 사건이 날아간다.
            WorldEvents.WeldGrown -= RpcWeldGrown;
            WorldEvents.WeldShrunk -= RpcWeldShrunk;
            TransportVehicle.AnyActivated -= OnVehicleActivated;
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

            if (isServer) SendFromServer();
            else FollowOnClient();
        }

        void SendFromServer()
        {
            sendTimer += Time.deltaTime;
            clampKeepaliveTimer += Time.deltaTime;
            dropKeepaliveTimer += Time.deltaTime;
            toolKeepaliveTimer += Time.deltaTime;
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

            // ── 운석 예고 ────────────────────────────────────────
            SendDrops();

            // ── 도구 휴대 ────────────────────────────────────────
            if (sendTools.Length != tools.Count) sendTools = new bool[tools.Count];
            for (int i = 0; i < tools.Count; i++)
                sendTools[i] = tools[i] != null && tools[i].IsEquipped;

            // 도구를 쥐고 놓는 것은 몇 초에 한 번이다 — 고정쇠와 같은 이유로 바뀔 때만
            // 보내고, 신뢰 없는 채널이라 잃을 수 있으므로 조용해도 가끔 다시 보낸다.
            if (!SameTools(sendTools, lastSentTools) || toolKeepaliveTimer >= ClampKeepalive)
            {
                RpcApplyTools(sendSeq, sendTools);
                toolKeepaliveTimer = 0f;

                if (lastSentTools.Length != sendTools.Length)
                    lastSentTools = new bool[sendTools.Length];
                Array.Copy(sendTools, lastSentTools, sendTools.Length);
            }

            // ── 낙하 운석 그림 ──────────────────────────────────
            SendMeteors();
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
        /// </summary>
        Metagame.Gameplay.Hazards.MeteorSpawner MeteorSpawner
        {
            get
            {
                if (meteorSpawnerCached == null && !meteorSpawnerLooked)
                {
                    meteorSpawnerLooked = true;
                    meteorSpawnerCached =
                        FindAnyObjectByType<Metagame.Gameplay.Hazards.MeteorSpawner>();
                }
                return meteorSpawnerCached;
            }
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

            if (recvDrops == null || recvDrops.Count != drops.Length)
            {
                recvDrops = new List<Metagame.Gameplay.Hazards.PendingMeteorDrop>(drops.Length);
                for (int i = 0; i < drops.Length; i++) recvDrops.Add(default);
            }
            for (int i = 0; i < drops.Length; i++)
                recvDrops[i] = new Metagame.Gameplay.Hazards.PendingMeteorDrop(
                    drops[i].Point, drops[i].Remaining, drops[i].Warning, drops[i].Radius);

            spawner.ApplyNetworkPending(recvDrops);
        }

        static bool SameTools(bool[] a, bool[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;

            return true;
        }

        /// <summary>
        /// 어느 도구가 <b>지금 누군가의 손에 있는가</b>를 맞춘다 (S15P21B202-352).
        ///
        /// <para>판정은 서버가 하고 여기서는 그림만 따라간다 —
        /// <see cref="ToolBase.ApplyRemoteHeld"/> 가 소유권을 옮기지 않는 이유를 적어 두었다.</para>
        ///
        /// <para>개수가 다르면 <b>손대지 않는다.</b> 고정쇠와 같다 — 억지로 맞추면 남의
        /// 도구에 남의 상태가 들어간다.</para>
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyTools(uint seq, bool[] held)
        {
            if (isServer) return;               // 호스트는 자기 판정이 정답이다
            if (held == null || !ready) return;
            if (!IsNewer(seq, lastToolSeq, everTool)) return;
            lastToolSeq = seq;
            everTool = true;

            if (held.Length != tools.Count)
            {
                if (!warnedToolMismatch)
                {
                    warnedToolMismatch = true;
                    Debug.LogWarning($"[WorldSync] 도구 개수가 다르다 — 서버 {held.Length} · 여기 {tools.Count}");
                }
                return;
            }
            warnedToolMismatch = false;

            for (int i = 0; i < tools.Count; i++)
                if (tools[i] != null) tools[i].ApplyRemoteHeld(held[i]);
        }

        /// <summary>낙하 자세 하나의 바이트 수 — 자세 28 + 속도 12.</summary>
        const int MeteorPoseBytes = PoseBytes + 12;

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
            int n = live != null ? live.Count : 0;

            int cap = UnreliableMeteorCapacity;
            if (n > cap)
            {
                if (!warnedMeteorCap)
                {
                    warnedMeteorCap = true;
                    Debug.LogWarning($"[WorldSync] 운석이 한 묶음에 안 들어간다 — {n}개 중 {cap}개만 보낸다");
                }
                n = cap;
            }

            // **0개일 때 빈 묶음을 계속 보내지 않는다** (AI 리뷰 지적, 2026-09-08).
            // 운석은 대부분의 시간에 0개다. 그런데도 매 송신 틱마다 RPC 를 내면 사람 수만큼
            // 빈 패킷이 상시 나간다 — 이 MR 이 문제로 든 그 업링크 비용이다.
            //
            // **다만 한 번만 보내면 안 된다**(AI 리뷰 2차 지적). "0개" 는 참가자의 그림을
            // 지우는 신호인데 이 채널은 신뢰가 없다. 그 한 패킷이 유실되면 그림은 충돌러가
            // 꺼진 채 중력만 받아 **지면을 뚫고 영원히 떨어진다** — 경기 중이라
            // OnStopClient 정리도 오지 않는다.
            //
            // 그래서 0이 된 뒤 ZeroRepeats 번은 계속 보내고 그다음에 조용해진다.
            // sendRate 30 에서 15번이면 0.5초 — 고정쇠 keepalive 와 같은 창이다.
            if (n == 0)
            {
                if (zeroSends >= ZeroRepeats) return;
                zeroSends++;
            }
            else zeroSends = 0;

            if (sendMeteors.Length != n) sendMeteors = new MeteorPose[n];
            for (int i = 0; i < n; i++)
            {
                var meteor = live[i];
                if (meteor == null) { sendMeteors[i] = default; continue; }

                // 보내는 쪽도 받는 쪽도 `body` 로 읽고 쓴다 — 운석은 보간이 걸려 있어
                // `transform.position` 은 **표시 위치**다(FollowOnClient 의 주석과 같은 이유).
                var body = meteor.GetComponent<Rigidbody>();
                sendMeteors[i] = body != null
                    ? new MeteorPose
                    {
                        Position = body.position,
                        Rotation = body.rotation,
                        Velocity = body.linearVelocity,
                    }
                    : new MeteorPose
                    {
                        Position = meteor.transform.position,
                        Rotation = meteor.transform.rotation,
                        Velocity = Vector3.zero,
                    };
            }

            RpcApplyMeteors(sendSeq, sendMeteors);
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcApplyMeteors(uint seq, MeteorPose[] poses)
        {
            if (isServer) return;               // 호스트에는 진짜 운석이 있다
            if (!ready) return;
            if (!IsNewer(seq, lastMeteorSeq, everMeteor)) return;
            lastMeteorSeq = seq;
            everMeteor = true;

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
