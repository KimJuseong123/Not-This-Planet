using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.Network
{
    /// <summary>
    /// 네트워크로 오가는 플레이어의 뿌리. 위치와 신원만 들고 있고, 몸은 각자 만든다.
    ///
    /// PlayerRig 는 프리팹이 아니라 런타임에 조인트로 조립하는 래그돌이라(RagdollBuilder)
    /// Mirror 의 스폰 목록에 등록할 수 없다. 그래서 NetworkIdentity 만 가진 얇은 프리팹을
    /// 스폰하고, 몸은 서버와 클라이언트가 각자 만든다.
    ///
    /// <b>물리는 서버에서만 돈다</b>(S15P21B202-145·-147). 클라이언트는 누른 키를
    /// [Command] 로 보내고, 서버의 PlayerRig 가 그 입력으로 힘을 가한다. 결과인 골반
    /// 위치가 이 오브젝트에 실려 NetworkTransform 으로 모두에게 간다. 내 캐릭터도
    /// 예외가 아니다 — 각자 계산하면 두 부품을 함께 옮길 때 결과가 갈린다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class NetPlayer : NetworkBehaviour
    {
        /// <summary>
        /// 입력을 보내는 최소 간격. 물리는 100Hz 로 돌지만 그 속도로 보낼 이유는 없다 —
        /// 사람이 키를 100분의 1초 단위로 바꾸지 않는다. 바뀌면 바로 보내고,
        /// 안 바뀌어도 이 간격마다 한 번은 보내 서버가 연결이 살아 있음을 안다.
        /// </summary>
        const float SendInterval = 1f / 20f;

        // ── 내 캐릭터 예측 (P1) · S15P21B202-363 ────────────────────────
        //
        // 참가자는 자기 캐릭터조차 서버가 보낸 과거 자세를 보간해 그렸다. 그래서 왕복
        // 78ms + 보간 버퍼 83ms 가 **둘 다 내 몸에** 붙어 점프가 ~166ms 늦게 보였다
        // (2026-09-09 3인 QA 실측). 이제 참가자도 자기 몸은 로컬 물리로 즉시 돌리고,
        // 서버 자세와의 **오차만 부드럽게 얹는다**(되감기·재시뮬레이션은 하지 않는다 —
        // 래그돌 전체 상태를 저장·복원해야 하고, 우리는 판정이 없어 그만한 정확도가
        // 필요하지 않다. docs/network-sync-policy.md §2).

        /// <summary>서버가 "여기까지 적용했다" 를 돌려주는 주기. 매 물리 스텝(100Hz)일 이유가 없다.</summary>
        const float EchoInterval = 1f / 30f;

        /// <summary>보낸 입력의 자세를 몇 개까지 기억하나. 30Hz 로 왕복 2초분이면 넉넉하다.</summary>
        const int HistorySize = 64;

        /// <summary>보낸 입력의 순번과 그때 내 골반이 있던 자리.</summary>
        struct SentPose
        {
            public uint Seq;
            public Vector3 Position;
        }

        readonly SentPose[] history = new SentPose[HistorySize];
        int historyCount;
        uint nextSeq;

        /// <summary>서버가 마지막으로 적용한 입력의 순번. 그 결과 자세와 함께 돌려준다.</summary>
        uint appliedSeq;
        float echoTimer;

        /// <summary>
        /// 마지막으로 받아들인 echo 의 순번 (`!275` AI 리뷰).
        ///
        /// <para>신뢰 없는 채널이라 <b>순서가 뒤집혀 도착할 수 있다.</b> 늦게 온 옛 echo 를
        /// 그대로 받으면 이미 갚은 오차를 다시 얹어 <b>되돌린다</b> — 가만히 서 있는데
        /// 미끄러지는 증상이 된다. 그래서 뒤로 가는 순번은 버린다.</para>
        /// </summary>
        uint lastEchoSeq;

        /// 서버가 정하고 모두에게 전달된다. 접속을 확인할 때 Spring 이 알려 준 값이다.
        [SyncVar(hook = nameof(OnIdentityChanged))]
        public string nickname = string.Empty;

        [SyncVar]
        public long userId;

        [SyncVar(hook = nameof(OnAppearanceChanged))]
        int appearance;

        public override void OnStartLocalPlayer()
        {
            CmdSetAppearance(CustomizationData.Load().Packed);
        }

        [Command]
        void CmdSetAppearance(int value)
        {
            appearance = CustomizationData.Decode(value).Packed;
            ApplyAppearance();
        }

        void OnAppearanceChanged(int previous, int current)
        {
            if (body != null) PlayerAppearance.Apply(body.Doll, current);
        }

        void ApplyAppearance()
        {
            if (body != null) PlayerAppearance.Apply(body.Doll, appearance);
        }

        /// <summary>
        /// 지금 운반 장치를 타고 있는가. 서버가 정하고 모두에게 전달된다(-149).
        /// 클라이언트의 화면에는 조작 결과가 자세로만 오므로, "저 사람이 탔다" 는
        /// 사실 자체를 따로 알려 줘야 한다.
        ///
        /// 남의 몸을 감추고, 참가자 본인의 보행 예측을 탑승 추종으로 전환한다.
        /// HUD와 상호작용도 PlayerRig의 공통 탑승 상태를 읽는다.
        /// </summary>
        [SyncVar(hook = nameof(OnRidingChanged))]
        public bool riding;

        /// <summary>
        /// 지금 타고 있는 탈것의 짝짓기 번호. 안 타고 있으면 <b>-1</b> (S15P21B202-434).
        ///
        /// <para><b><see cref="riding"/> 만으로는 부족했다.</b> 그 값은 bool 이라 "타고
        /// 있다" 는 알려도 <b>무엇을 타는지</b>는 말해 주지 않는다. 그래서 참가자는
        /// 조종석 시점 앵커(<c>OrbitCamera.SetVehicleEye</c>)를 세울 수 없었고,
        /// <c>vehicleView</c> 만 켜진 채 앵커가 비어 있어 카메라가 <b>래그돌을 계속
        /// 따라갔다</b> — 참가자만 시점이 어긋나던 자리다. 호스트는
        /// <c>TryEnterTransportVehicle</c> 안에서 앵커를 받으므로 멀쩡했다.</para>
        ///
        /// <para>번호는 <see cref="WorldSync"/> 가 이미 쓰는 짝짓기 순서다. 여기서 탈것만
        /// 따로 줄을 세우면 -315 가 기록한 사고를 새로 만든다.</para>
        /// </summary>
        [SyncVar(hook = nameof(OnRideVehicleChanged))]
        public int ridingVehicle = -1;

        /// <summary>
        /// 이 사람이 <b>기절해 쓰러져 있는가</b> (S15P21B202-378).
        ///
        /// <para>기절 판정은 서버가 한다. 이 값이 없던 동안 참가자 기계에서는 원격
        /// 플레이어의 <c>PlayerRig.Downed</c> 가 언제나 false 였고, 그래서
        /// <b>참가자는 동료를 일으킬 수 없었다</b> — 후보 탐색이 그 플래그로 걸러진다.
        /// 2026-09-10 QA 에서 호스트가 기절했는데 참가자가 아무 반응을 못 얻었다.</para>
        ///
        /// <para>훅은 <b>표시와 후보 자격만</b> 맞춘다(<c>PlayerRig.SetRemoteDowned</c>).
        /// 물리·카메라·장비 해제는 서버가 한다.</para>
        /// </summary>
        [SyncVar(hook = nameof(OnDownedChanged))]
        public bool downed;

        /// <summary>
        /// 셋 이상이 모여 파츠를 실제로 밀고 있는가 (기획 §13 · §10.2).
        ///
        /// <b>참가자는 이 값을 스스로 알 수 없어서 실어 보낸다.</b> 파츠 물리는 서버에서만
        /// 돌고 참가자 화면의 파츠는 kinematic 이라 <c>PartPush.CanPush</c> 가 false 다 —
        /// 그쪽에서 조회하면 늘 "밀고 있지 않다" 가 나온다. 그러면 방장만 3인칭으로
        /// 물러나고 참가자는 1인칭에 갇혀, 셋이 함께 미는 장면이 참가자 화면에서는
        /// 그냥 걷는 화면이 된다(S15P21B202-400).
        ///
        /// <b>파츠를 짝지어 보내지 않는다.</b> 카메라가 필요한 것은 파츠가 무엇인지가
        /// 아니라 <b>어느 지점을 어느 쪽으로</b> 밀고 있는가뿐이다. 그래서 사람당 세
        /// 값으로 끝나고 <see cref="WorldSync"/> 의 번호 짝짓기를 건드리지 않는다.
        /// </summary>
        [SyncVar]
        public bool hauling;

        /// <summary>미는 방향(수평 단위). 카메라의 자유 시점 기준이 된다.</summary>
        [SyncVar]
        public Vector3 haulDirection;

        /// <summary>파츠 표면에서 이 사람이 잡거나 짚은 지점(월드). 카메라 초점이 쓴다.</summary>
        [SyncVar]
        public Vector3 haulGrasp;

        /// <summary>
        /// <b>남이 이 사람을 일으키는 진행도 0~1</b> (S15P21B202-448).
        ///
        /// <para><b>왜 보내야 하나.</b> 진행분은 구조자의 인터랙터가 대상의
        /// <c>DownedRescue</c> 에 쌓고, 그 일은 <b>서버에서만</b> 일어난다 — 입력이
        /// <see cref="CmdSendInput"/> 으로 와서 서버의 <c>PlayerRig</c> 가 실행하기
        /// 때문이다. 그래서 <b>쓰러진 당사자의 기계에는 그 값이 존재한 적이 없다</b>
        /// (실측 2026-09-11: 대상 쪽 <c>DownedRescue.Progress</c> 가 0.000).
        /// 화면만 열면 빈 막대가 그려진다.</para>
        ///
        /// <para><b>진행도만 보낸다.</b> 누가 일으키고 있는지는 보내지 않는다 — 쓰러진
        /// 사람 화면에 필요한 것은 "얼마나 찼는가" 뿐이고, 사람을 실으면
        /// <c>WorldSync</c> 의 번호 짝짓기를 건드리게 된다. <c>hauling</c> 세 값이 같은
        /// 판단을 한 자리다.</para>
        ///
        /// <para>기본값 0 이 "아무도 일으키지 않는다" 와 같아서 초기 상태가 안전하다.</para>
        /// </summary>
        [SyncVar]
        public float rescueProgress;

        /// <summary>
        /// 방 안의 자리 번호(0~3). 색이 여기서 나온다 (S15P21B202-375).
        ///
        /// <para><b>서버가 <see cref="OnStartServer"/> 에서 정한다.</b> 그 시점에 넣으면
        /// 스폰 메시지에 실려 가므로, 클라이언트가 몸을 세울 때 이미 값이 있다. 몸을
        /// 만든 뒤에 색을 바꾸는 경로를 만들 필요가 없다(<c>PlayerRig</c> 에 recolor 가
        /// 없다).</para>
        ///
        /// <para>예전에는 색을 <c>userId</c> 에서 뽑았다 — DB 자동 증가라 같은 사람이
        /// 판마다 다른 색이었다.</para>
        /// </summary>
        [SyncVar]
        public byte teamSlot;

        /// <summary>이 사람의 팀 색(우주복).</summary>
        public Color TeamSuitColor { get { return TeamSlots.Suit(teamSlot); } }

        /// <summary>이 사람의 팀 색(글씨·아이콘용). 어두운 화면에서도 읽힌다.</summary>
        public Color TeamAccentColor { get { return TeamSlots.Accent(teamSlot); } }

        /// <summary>"빨강" 처럼 말로 부를 수 있는 자리 이름.</summary>
        public string TeamName { get { return TeamSlots.Name(teamSlot); } }

        /// <summary>
        /// 이름표를 띄울 자리 — 헬멧 위쪽 (S15P21B202-375).
        ///
        /// <para>몸이 아직 없으면(스폰 직후·전용 서버) 이 오브젝트의 위치를 준다.
        /// 쓰러져 있으면 머리가 땅에 있으므로 이름표도 낮게 뜬다 — 그게 맞다.</para>
        /// </summary>
        public Vector3 NameplateAnchor
        {
            get
            {
                if (body == null || body.Doll == null || body.Doll.Head == null)
                {
                    return transform.position + Vector3.up * 0.45f;
                }

                return body.Doll.Head.position + Vector3.up * 0.34f;
            }
        }

        /// <summary>몸이 서 있는가. 이름표는 몸이 있을 때만 그린다.</summary>
        public bool HasBody { get { return body != null; } }
        public PlayerRig VisualRig => body;
        public static uint NetIdOfRig(PlayerRig rig)
        {
            if (rig == null) return 0;
            foreach (var player in ServerPlayers)
                if (player != null && player.body == rig) return player.netId;
            return 0;
        }

        /// <summary>서버가 확정한 이 플레이어의 현재 팀 핑.</summary>
        [SyncVar]
        public bool pingActive;

        [SyncVar]
        public Vector3 pingPosition;

        [SyncVar]
        public double pingExpiresAt;

        /// <summary>화면에 그릴 수 있는, 서버 시각 기준의 활성 핑인가.</summary>
        public bool HasActivePing
        {
            get { return pingActive && NetworkTime.time < pingExpiresAt; }
        }

        public Vector3 PingPosition { get { return pingPosition; } }
        public Color PingColor { get { return TeamAccentColor; } }

        // 이동 판정을 복제하지 않고, 표시용 걷기/달리기 상태만 함께 전달한다.
        [SyncVar]
        RabbitLocomotion.State _locomotionState;

        /// <summary>서버에서 물리를 도는 진짜 몸. 클라이언트에서는 그림용 몸이다.</summary>
        PlayerRig body;
        RemoteAvatar remoteAvatar;

        void LateUpdate()
        {
            if (body == null || body.Doll == null || body.Hips == null) return;

            // 서버가 보낸 밀기 상태를 몸에 넣는다 (S15P21B202-400).
            //
            // **SyncVar hook 으로는 안 된다.** hook 은 그 변수가 바뀔 때만 불리는데
            // 방향·접촉 지점은 매 틱 조금씩 움직인다 — hauling 하나에만 hook 을 걸면
            // 켜지는 순간의 값으로 굳어 카메라 초점이 파츠를 따라가지 않는다.
            //
            // 방장 자신의 몸에는 넣지 않는다. 그쪽은 PlayerRig.Tick 이 파츠를 직접
            // 조회해 이미 채웠고, 여기서 덮으면 한 프레임 늦은 값으로 되돌린다.
            //
            // **내 몸에만 넣는다.** 이 LateUpdate 는 모든 NetPlayer 에서 돌고, 남의
            // 몸도 PlayerRig 다(BuildRemoteBody 가 만든다). 카메라는 내 것만 쓰므로
            // 남의 몸에 넣는 것은 무의미하고, 그 값이 Locomotion 의 이동 기준
            // (haulBasisSet)에 섞일 자리를 남긴다. 지금은 RemoteAvatar 가
            // `rig.enabled = false` 로 조종을 멈춰 증상이 없지만, 그 전제가 바뀌면
            // 조용히 깨진다(!312 AI 리뷰가 이 자리를 물었다).
            //
            // 여기서 isLocalPlayer 를 쓰는 것은 안전하다. 그것을 피하라는 주의
            // (IsHostOwnPlayer 주석)는 **서버 쪽 몸 세우기**의 스폰 경쟁 이야기고,
            // 이 자리는 body 가 이미 있는지 확인한 뒤이며 한 프레임 늦어도 무해하다.
            if (!isServer && isLocalPlayer)
                body.SetHaulState(hauling, haulDirection, haulGrasp);

            // **내 몸에만 넣는다.** 이 값을 그리는 곳은 쓰러진 당사자의 화면뿐이고,
            // 남의 몸(그림)에 넣어 봐야 읽는 데가 없다. 서버에서는 PlayerRig 가 자기
            // RescueTarget 에서 직접 읽으므로 넣지 않는다 — SetRemoteBeingRescued 가
            // 권위가 있는 몸에서 스스로 무시하지만, 부르지 않는 것이 뜻이 분명하다.
            if (!isServer && isLocalPlayer)
                body.SetRemoteBeingRescued(rescueProgress);

            if (isServer) _locomotionState = RabbitLocomotion.ReadState(body);
            if (body.Doll.Suit != null)
                body.Doll.Suit.NetworkLocomotionState = _locomotionState;
            if (remoteAvatar != null)
                remoteAvatar.SetArmIntent(_locomotionState.LeftArmBusy,
                                          _locomotionState.RightArmBusy);
        }

        /// <summary>이 사람의 운반 장치 조작. 서버에만 있다(-149).</summary>
        TractorBeamController transport;

        /// <summary>게임 씬이 만든 몸을 넘겨받았는가. 그렇다면 여기서 없애지 않는다(-263).</summary>
        bool adoptedBody;

        /// <summary>몸을 못 세운 채 지나간 물리 스텝 수. 진단 로그를 한 번만 남기려고 센다.</summary>
        int waitedSteps;

        /// <summary>쓰러져 있은 시간. 전원 기절 구제를 재는 데 쓴다(-55).</summary>
        float downedSeconds;

        /// <summary>서버가 들고 있는 접속자 목록. "전원 기절" 의 '전원' 이 이것이다.</summary>
        static readonly List<NetPlayer> ServerPlayers = new List<NetPlayer>();

        /// <summary>현재 클라이언트가 화면에 표시할 네트워크 플레이어 목록.</summary>
        static readonly List<NetPlayer> ClientPlayers = new List<NetPlayer>();
        public static IReadOnlyList<NetPlayer> AllClientPlayers
        {
            get { return ClientPlayers; }
        }

        // ── 서버 ────────────────────────────────────────────────
        InputFrame pending;          // 이 사람이 마지막으로 보낸 입력
        bool everReceived;           // 서버가 이 사람 입력을 한 번이라도 받았나(진단용)
        string lastAim;              // 마지막으로 남긴 조준 상태(바뀔 때만 찍는다)
        bool previousPingPressed;

        // ── 클라이언트(내 것) ───────────────────────────────────
        InputFrame lastSent;
        bool respawnWasPressed;
        double nextManualRespawnTime;
        float sendTimer;
        bool everSent;

        public override void OnStartServer()
        {
            // 접속 확인을 통과한 연결에는 신원이 붙어 있다(MatchAuthenticator).
            // 여기서 채우면 SyncVar 를 타고 모든 클라이언트에 전달된다.
            // 인증자 없이 띄운 검증용 실행에서는 없을 수 있어 그때는 연결 번호로 대신한다.
            var identity = connectionToClient != null
                ? connectionToClient.authenticationData as MatchIdentity
                : null;

            if (identity != null)
            {
                userId = identity.UserId;
                nickname = identity.Nickname;
            }
            else if (string.IsNullOrEmpty(nickname))
            {
                userId = connectionToClient != null ? connectionToClient.connectionId : 0;
                nickname = "비행사 " + userId;
            }

            // 자리(색)를 정한다 (S15P21B202-375). userId 가 확정된 뒤여야 한다 —
            // 같은 사람이 끊겼다 돌아오면 같은 자리를 다시 받는다.
            teamSlot = (byte)TeamSlots.Claim(userId);

            name = "플레이어 " + nickname;

            // 같은 사람이 이미 붙어 있으면 낡은 쪽을 먼저 걷어낸다 (S15P21B202-373).
            // 이 줄이 없어서 재접속할 때마다 이전 캐릭터가 유령으로 남았다.
            EvictStaleDuplicates();

            ServerPlayers.Add(this);

            // 몸은 여기서 만들지 않는다. 호스트 모드에서는 게임 씬이 다 뜨기 전에
            // 스폰이 먼저 오기 때문이다(S15P21B202-263 실측) — 그때 만들면 로비 씬에
            // 래그돌이 생기고, 방장의 몸을 넘겨받을 기회도 놓친다.
            // 준비되면 FixedUpdate 가 만든다.
            Debug.Log($"[NetPlayer] 서버 스폰 · {nickname}(userId={userId}) · 접속 인원 {NetworkServer.connections.Count}명");
        }

        /// <summary>
        /// 같은 <c>userId</c> 의 낡은 접속을 걷어낸다 (S15P21B202-373).
        ///
        /// <b>왜 필요한가.</b> Mirror 는 연결이 죽어도 그것을 <b>바로</b> 알지 못한다 —
        /// 전송 타임아웃까지 그 사람의 플레이어 오브젝트가 살아 있다. 그 사이에 같은
        /// 사람이 다시 붙으면(백엔드가 재접속 유예로 자리를 지켜 주므로 붙는다,
        /// S15P21B202-150) <b>한 사람에 대해 NetPlayer 가 둘이 된다.</b> 그러면 옛
        /// 몸이 그 자리에 그대로 서 있어 유령처럼 보인다.
        ///
        /// 2026-09-10 4인 QA 의 참가자 로그에서 <c>userId=19</c> 에 대해
        /// "다른 플레이어가 보인다" 가 두 번 찍혔다 — 클라이언트 로그이므로 서버가
        /// 같은 사람을 두 번 들였다는 뜻이다.
        ///
        /// <b>지우는 순서가 중요하다.</b> 먼저 오브젝트를 지워야 그 자리에서
        /// <see cref="OnStopServer"/> 가 돌아 <see cref="RejoinMemory"/> 에 자리를 적는다.
        /// 새 몸은 <c>FixedUpdate</c> 가 나중에 세우므로 그때 <c>TryTake</c> 가 그 값을
        /// 받는다 — 순서가 뒤집히면 돌아온 사람이 스폰 지점에 선다.
        ///
        /// <b>누구를 걷어낼지는 <see cref="RejoinMemory.IsStaleDuplicate"/> 가 정한다.</b>
        /// 이 클래스는 <c>NetworkBehaviour</c> 라 EditMode 테스트에서 건드리면 테스트
        /// 어셈블리가 Mirror 를 참조해야 한다(2026-09-10 실측: <c>error CS0012</c> 5건).
        /// 판정만 Mirror 와 무관한 곳에 두면 규칙을 테스트가 지킬 수 있다.
        /// </summary>
        void EvictStaleDuplicates()
        {
            // 걷어낼 대상을 먼저 모은다. OnStopServer 가 ServerPlayers 를 건드리므로
            // 순회 중에 지우면 목록이 흐트러진다.
            var stale = new List<NetPlayer>();
            for (int i = 0; i < ServerPlayers.Count; i++)
            {
                var other = ServerPlayers[i];
                if (other == null || other == this) continue;
                if (RejoinMemory.IsStaleDuplicate(userId, other.userId, other.adoptedBody)) stale.Add(other);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                var old = stale[i];
                var conn = old.connectionToClient;

                Debug.Log($"[NetPlayer] 같은 사람의 낡은 접속을 걷어낸다 · "
                          + $"{nickname}(userId={userId}) · 낡은 연결 "
                          + (conn != null ? conn.connectionId.ToString() : "없음"));

                // 오브젝트를 먼저 — OnStopServer 가 여기서 돌아 자리를 적는다.
                if (old != null && old.gameObject != null) NetworkServer.Destroy(old.gameObject);

                // 죽은 연결이 목록에 남아 입력을 보내지 못하게 끊는다.
                if (conn != null) conn.Disconnect();
            }
        }

        public override void OnStartClient()
        {
            if (!ClientPlayers.Contains(this)) ClientPlayers.Add(this);
            PerfLog.Players = ClientPlayers.Count;

            // 내 역할을 로그 앞쪽에 한 줄로 남긴다 (S15P21B202-380). 2026-09-09 에
            // "누가 호스트였는지" 를 잘못 읽어 분석을 한 번 되돌린 적이 있다.
            if (isLocalPlayer)
            {
                Debug.Log($"[RUN] 역할={(isServer ? "호스트" : "참가자")} · {nickname}(userId={userId})");
            }

            // 서버 겸 클라이언트(호스트 모드)라면 몸은 이미 서버 쪽에서 만들었다.
            if (isServer) return;

            // 내 몸은 게임 씬이 이미 세워 뒀다. 아직이면 Update 가 계속 시도한다.
            if (isLocalPlayer) AdoptLocalBody();
            else BuildRemoteBody();
        }

        // ── 서버: 진짜 몸 ───────────────────────────────────────

        /// <summary>
        /// 물리가 도는 몸을 세운다. 이 몸이 부품을 밀고 지형을 밟는 유일한 실체다.
        /// </summary>
        /// <returns>몸을 세웠으면 참. 게임 씬이 아직 준비되지 않았으면 거짓.</returns>
        /// <summary>
        /// 이 플레이어가 방장 자신인가 (호스트 모드에서 서버 겸 클라이언트인 그 사람).
        ///
        /// <c>isLocalPlayer</c> 로 판단하면 안 된다 — 그 값은 <b>클라이언트 쪽</b> 스폰 처리가
        /// 끝나야 참이 되는데, 서버의 첫 <c>FixedUpdate</c> 가 그보다 먼저 도는 경우가 있다.
        /// 그러면 방장을 못 알아보고 몸을 하나 더 만들어 버린다(2026-09-02 실측).
        /// 연결이 서버 자신의 로컬 연결인지는 서버 쪽에서 바로 알 수 있어 이런 틈이 없다.
        /// </summary>
        bool IsHostOwnPlayer =>
            NetworkServer.active
            && NetworkServer.localConnection != null
            && connectionToClient == NetworkServer.localConnection;

        bool BuildServerBody()
        {
            var session = FindAnyObjectByType<GameSession>();

            // 게임 씬이 아직이다. 다음 물리 스텝에 다시 본다.
            if (session == null) return false;

            // 호스트 모드(방장의 PC 가 서버 겸 클라이언트)에서 방장 자신의 몸은
            // 게임 씬이 이미 세웠다 — 카메라·HUD·태블릿이 전부 그 몸에 붙어 있다.
            // 여기서 하나 더 만들면 방장만 몸이 두 벌이 되고, 카메라가 따라갈 대상이
            // 모호해진다(S15P21B202-263).
            if (IsHostOwnPlayer)
            {
                // 게임 씬이 아직 내 몸을 안 세웠으면 기다린다. 먼저 만들면 두 벌이 된다.
                if (session.LocalPlayer == null) return false;

                body = session.LocalPlayer;
                ApplyAppearance();
                adoptedBody = true;

                // 진행 판정(낙사·전원 기절)은 서버가 한다. 호스트에서는 내가 곧 서버라
                // GameSession 이 로컬로 한 번 더 부활시키면 두 번 일어난다.
                body.NetworkDriven = true;

                AttachTransport();
                transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
                ApplyRiding(riding);
                ApplyDowned(downed);
                Debug.Log($"[NetPlayer] 방장의 몸을 넘겨받았다 · {nickname}(userId={userId})");
                return true;
            }

            // 끊겼다 돌아온 사람이면 그 자리에 세운다 (S15P21B202-150).
            // 없으면(첫 입장·유예 초과) 스폰 지점이다.
            Vector3 foot;
            Quaternion facing;
            if (RejoinMemory.TryTake(userId, out Vector3 lastFoot, out Vector3 lastForward))
            {
                foot = lastFoot;
                facing = Quaternion.LookRotation(lastForward, Vector3.up);
                Debug.Log($"[NetPlayer] 끊긴 자리로 복귀 · {nickname}(userId={userId}) · {foot}");
            }
            else
            {
                foot = session != null ? session.SpawnFoot : transform.position;
                facing = Quaternion.LookRotation(Vector3.right, Vector3.up);
            }

            // 헤드리스 서버에는 모델을 씌우지 않는다. 그릴 일이 없고 로딩만 든다.
            body = PlayerRig.Create(
                string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                foot + Vector3.up * 0.10f, facing,
                TeamSuitColor, TeamAccentColor,
                useModel: !Application.isBatchMode);

            ApplyAppearance();

            AttachTransport();
            transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
            ApplyRiding(riding);
            ApplyDowned(downed);
            return true;
        }

        /// <summary>
        /// 운반 장치 조작을 이 사람 몫으로 하나 붙인다(S15P21B202-149).
        /// 컨트롤러가 1인용이라 접속자마다 하나씩 둔다 — 장치 자체는 씬에 하나뿐이고
        /// 여럿이 나눠 쓴다. 배치는 이미 끝나 있으므로 여기서 다시 섞지 않는다.
        /// </summary>
        void AttachTransport()
        {
            transport = body.gameObject.AddComponent<TractorBeamController>();
            transport.Configure(body, FindObjectsByType<TractorBeam>(FindObjectsSortMode.None),
                                randomizeVehicles: false);
        }

        /// <summary>
        /// 서버에서만 돈다. 마지막으로 받은 입력을 넣고, 물리가 낸 결과를 이 오브젝트에
        /// 옮겨 싣는다. NetworkTransform 이 그 값을 모두에게 보낸다.
        ///
        /// PlayerRig 자신의 FixedUpdate 가 물리를 돌리므로 여기서는 넣고 꺼내기만 한다 —
        /// 물리 코드는 건드리지 않는다.
        /// </summary>
        [ServerCallback]
        void FixedUpdate()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            // 게임 씬이 준비될 때까지 기다렸다 몸을 세운다.
            if (body == null)
            {
                if (BuildServerBody()) { waitedSteps = 0; }
                else
                {
                    // 오래 기다리면 왜 못 세우는지 한 번 남긴다. 이 화면이 그대로 멈춘
                    // 것처럼 보이므로, 로그가 없으면 원인을 찾을 실마리가 없다.
                    if (++waitedSteps == 250)
                    {
                        var session = FindAnyObjectByType<GameSession>();
                        Debug.LogWarning(
                            $"[NetPlayer] 5초째 몸을 못 세우고 있다 · {nickname}(userId={userId}) · " +
                            $"게임씬={(session != null ? "있음" : "없음")} · " +
                            $"방장자신={IsHostOwnPlayer} · " +
                            $"씬이세운내몸={(session != null && session.LocalPlayer != null ? "있음" : "없음")}");
                    }
                    return;
                }
            }

            // **먼저 돌려준다** (S15P21B202-363). 지금 골반 자세는 `직전에 적용한 입력`
            // 까지의 결과다 — 이번 입력은 아래에서 넣고 물리는 그 뒤에 돈다. 그래서
            // 짝지을 순번은 `appliedSeq`(직전 것)이고, 이 순서를 바꾸면 한 스텝씩 어긋난다.
            echoTimer += Time.fixedDeltaTime;
            if (echoTimer >= EchoInterval && appliedSeq != 0 && !IsHostOwnPlayer
                && body.Hips != null)
            {
                echoTimer = 0f;
                TargetEcho(appliedSeq, body.Hips.position, body.IsRiding);
            }

            // 방장 자신의 몸은 게임 씬(GameSession)이 매 프레임 직접 입력을 넣는다.
            // 그 몸에 pending 을 덮어쓰면 안 된다 — Update 가 body.CurrentInput 을 읽어
            // 다시 보내므로 "내가 쓴 값을 내가 되읽는" 고리가 생기고, 입력이 처음 값에서
            // 영원히 갱신되지 않는다. 그래서 방장만 움직이지 못했다(2026-09-02 실측).
            // FixedUpdate 는 Update 보다 먼저 도니까 순서로도 막을 수 없다.
            InputFrame frame = adoptedBody ? body.CurrentInput : pending;

            if (transport != null)
            {
                transport.SetInput(frame);

                // 탑승이 실제로 서버에서 일어났는지, 그때 탈것이 움직이는지 남긴다.
                // 참가자가 탄 탈것이 방장 화면에서 어긋나는데, 서버가 태우지 않은 것인지
                // 태우고도 안 움직이는 것인지 구분할 방법이 없었다(2026-09-03).
                if (transport.IsControlling != riding)
                {
                    var vehicle = transport.FocusedVehicle;
                    Debug.Log($"[NetPlayer] {(transport.IsControlling ? "탑승" : "하차")} · "
                              + $"{nickname}(userId={userId}) · "
                              + $"탈것={(vehicle != null ? vehicle.name : "모름")}");
                }
                riding = transport.IsControlling;

                // 탑승 중에는 몸이 운반체와 싸우지 않게 한다. 이동은 운반체가 처리하고
                // 몸은 좌석에 앉아 있기만 한다 — 혼자 실행할 때 GameSession 이 하는 것과
                // 같은 정리다(그쪽은 클라이언트, 여기는 서버라 각자 한 번씩 필요하다).
                if (transport.IsControlling)
                {
                    frame.Move = Vector2.zero;
                    frame.Jump = false;
                    frame.Run = false;
                    frame.GrabLeft = false;
                    frame.GrabRight = false;
                    frame.ToolAction = false;
                    frame.ToolPrimary = false;
                    frame.ToolSecondary = false;
                    frame.ToolUtility = false;
                    frame.ArmsUp = false;
                }
            }

            // 넘겨받은 몸(방장 자신)에는 넣지 않는다 — 위에서 적은 고리 때문이다.
            if (!adoptedBody) body.SetInput(frame);

            // 이번에 적용한 순번을 기억한다. 다음 스텝의 echo 가 이 값을 쓴다.
            if (frame.Seq != 0) appliedSeq = frame.Seq;

            HandlePing(frame);

            ReportAim();
            JudgeProgress(Time.fixedDeltaTime);

            // 기절 여부를 실어 보낸다 (S15P21B202-378).
            //
            // **transport 블록 안에 두면 안 된다.** 처음에는 riding 대입 옆에 두었는데
            // 그 자리는 `if (transport != null)` 안이다. 몸을 세운 두 경로가 모두 곧바로
            // AttachTransport() 를 부르므로 지금은 non-null 이고 증상이 나지 않았지만,
            // **기절은 탑승과 아무 상관이 없다** — 그 블록의 조건에 손대는 다음 사람이
            // 복제를 조용히 끊을 수 있다(!287 AI 리뷰가 이 배치를 짚었다).
            //
            // 그래서 몸의 결과를 옮겨 싣는 이 자리에 둔다. 위의 SetPositionAndRotation
            // 과 같은 일이다 — 물리가 낸 결과를 SyncVar·NetworkTransform 에 넘긴다.
            downed = body.Downed;

            // **이 기계가 서버라고 몸에 알린다** (S15P21B202-414).
            //
            // 이 메서드는 [ServerCallback] 이다 — 여기 있다는 사실 자체가 근거다.
            // PlayerRig 안에서는 계산할 수 없다: NetworkServer.active 를 봐야 하고
            // Metagame.Gameplay 는 Mirror 를 참조하지 않는다.
            //
            // **-390 에서 이 대입을 지운 것이 잘못이었다.** 그때 기본값을 참으로
            // 바꾸면서 "쓰는 곳을 하나로" 라며 함께 지웠는데, 그러자 이 필드를
            // **거짓으로 쓰는 곳만 남았다**(RemoteAvatar.Attach 두 자리). 되돌아올
            // 길이 없어져서, **한 번 참가자였던 기계는 호스트를 이어받은 뒤에도
            // 영원히 거짓**이었다 — TickRescue 가 예측 상한(0.98)에서 자기 자신인
            // 서버를 기다리며 멈췄고 동료는 일어나지 않았다(2026-09-10 16:00 QA,
            // 방장 49 → 47 이전 회차).
            //
            // downed·hauling 과 같은 자리에 둔다. 그리고 위 둘과 같은 이유로
            // `if (transport != null)` 안에 두지 않는다 — 권위는 탑승과 무관하다(-378).
            body.ServerAuthority = true;

            // 밀기 상태도 같은 자리에서 실어 보낸다 (S15P21B202-400). 위 downed 와 같은
            // 이유로 transport 블록 안에 두지 않는다 — 밀기는 탑승과 아무 상관이 없다.
            hauling = body.Hauling;
            haulDirection = body.HaulDirection;
            haulGrasp = body.HaulGrasp;

            // 쓰러진 사람 화면의 게이지가 읽을 값이다 (S15P21B202-448). 위 downed 와
            // 같은 자리에 둔다 — 같은 몸의 같은 상태이고, 탑승과 무관하다(-378).
            rescueProgress = body.BeingRescuedProgress01;

            // 어느 탈것을 타는지도 같은 자리에서 실어 보낸다 (S15P21B202-434).
            //
            // 위 downed·hauling 과 같은 이유로 `if (transport != null)` 안에 두지 않는다.
            // 그리고 <b>몸에게 묻는다</b> — transport.FocusedVehicle 은 "지금 보고 있는
            // 것" 이라 타지 않은 탈것을 가리킬 수 있다. MountedVehicle 은 실제로 탄 것이다.
            ridingVehicle = IndexOfVehicle(body.MountedVehicle);

            transform.SetPositionAndRotation(body.Hips.position, body.Hips.rotation);
        }

        /// <summary>
        /// 서버만 핑을 확정한다. 입력의 조준 원점·방향으로 월드 표면을 찾고,
        /// 새 핑은 이전 핑을 같은 SyncVar 슬롯에서 즉시 대체한다.
        /// </summary>
        [Server]
        void HandlePing(InputFrame frame)
        {
            double now = NetworkTime.time;
            if (pingActive && now >= pingExpiresAt)
            {
                pingActive = false;
                pingExpiresAt = 0d;
            }

            bool pressed = frame.PingPressed;
            if (pressed && !previousPingPressed && body != null && !body.Downed)
            {
                if (PingPlacement.TryResolve(body.AimOrigin, body.AimForward, body,
                                              out var point, out _))
                {
                    pingPosition = point;
                    pingExpiresAt = now + Tuning.PingLifetimeSeconds;
                    pingActive = true;
                }
            }

            previousPingPressed = pressed;
        }

        /// <summary>
        /// 서버가 이 사람의 조준을 어떻게 보고 있는지 남긴다. 바뀔 때만 찍는다.
        ///
        /// 참가자만 상호작용이 되지 않는 일이 있었는데, 서버가 대상을 못 잡는 것인지
        /// 잡고도 실행이 막힌 것인지 구분할 방법이 없었다. 대상·탑승·기절을 함께 남기면
        /// 그 자리에서 갈린다 — RefreshInteractionFocus 는 탑승·기절이면 대상을 비운다.
        /// </summary>
        void ReportAim()
        {
            if (body == null) return;

            bool holding = (body.LeftHand != null && body.LeftHand.Held != null)
                        || (body.RightHand != null && body.RightHand.Held != null);

            // 무거운 부품은 손으로 쥐는 대신 "운반" 으로 간다. 둘은 다른 경로라
            // 쥐기만 보면 운반이 되는 중인지 알 수 없다.
            bool carrying = (body.LeftHand != null && body.LeftHand.Carrying)
                         || (body.RightHand != null && body.RightHand.Carrying);

            string now = $"{body.CurrentInteractionTarget.Kind}"
                       + $"·탑승{(body.IsRiding ? "O" : "X")}"
                       + $"·기절{(body.Downed ? "O" : "X")}"
                       + $"·잡음{(holding ? "O" : "X")}"
                       + $"·운반{(carrying ? "O" : "X")}";
            if (now == lastAim) return;

            lastAim = now;
            Debug.Log($"[NetPlayer] 조준 · {nickname}(userId={userId}) · {now}");
        }

        /// <summary>
        /// 낙사와 전원 기절을 서버가 판정한다(S15P21B202-55).
        ///
        /// 혼자 실행할 때는 GameSession 이 하지만, 전용 서버에는 "내 캐릭터" 가 없어
        /// 그 경로가 돌지 않는다(SpaceBootstrap 이 Session.Begin(null, ...) 로 시작한다).
        /// 그대로 두면 맵 밖으로 나간 사람이 영원히 떨어진다.
        /// </summary>
        [Server]
        void JudgeProgress(float deltaTime)
        {
            var session = GameSession.Instance;
            if (session == null || (session.Recovery != null && session.Recovery.RecoverySuspended)) return;

            if (GameSession.HasFallen(body))
            {
                session.RecoverFallenPlayer(body, transport);
                downedSeconds = 0f;
                return;
            }

            // 전원이 기절하면 아무도 일으킬 수 없다. 시간이 지나면 되살아난다
            // (기획 §26-13). 혼자일 때는 "전원" 이 곧 나 하나다.
            if (!body.Downed)
            {
                downedSeconds = 0f;
                return;
            }

            downedSeconds += deltaTime;
            if (downedSeconds < PhysicsScale.AllDownedRespawnSeconds) return;
            if (!EveryoneDowned()) return;

            downedSeconds = 0f;
            session.RespawnPlayer(body, transport);
        }

        /// <summary>접속한 사람이 모두 쓰러져 있는가.</summary>
        static bool EveryoneDowned()
        {
            foreach (var other in ServerPlayers)
                if (other != null && other.body != null && !other.body.Downed)
                    return false;
            return true;
        }

        /// <summary>
        /// 소유자가 요청한 개인 복구. 반복 요청은 서버에서 1초 간격으로 제한한다.
        /// </summary>
        [Command]
        void CmdRequestRespawn()
        {
            var session = GameSession.Instance;
            if (body == null || session == null || session.Cleared
                || NetworkTime.time < nextManualRespawnTime
                || (session.Recovery != null && session.Recovery.RecoverySuspended)) return;
            nextManualRespawnTime = NetworkTime.time + 1.0;
            session.RespawnPlayer(body, transport);
            downedSeconds = 0f;
        }

        /// <summary>입력을 보관하고 다음 물리 스텝에 적용한다.</summary>
        [Command]
        void CmdSendInput(InputFrame frame)
        {
            if (!everReceived)
            {
                everReceived = true;
                Debug.Log($"[NetPlayer] 서버가 입력을 받기 시작 · {nickname}(userId={userId})");
            }
            pending = frame;
        }

        // ── 클라이언트: 내 몸 ───────────────────────────────────

        /// <summary>
        /// 게임 씬(SpaceBootstrap)이 이미 세워 둔 내 몸을 넘겨받아, 스스로 움직이는 대신
        /// 서버가 보낸 자리를 따라가게 한다.
        ///
        /// 새로 만들지 않는 이유는 카메라·HUD·태블릿이 모두 그 몸에 붙어 있어서다.
        /// 여기서 한 벌 더 만들면 카메라가 붙을 대상이 둘이 된다.
        /// </summary>
        /// <returns>넘겨받았으면 참. 게임 씬이 아직 준비되지 않았으면 거짓.</returns>
        bool AdoptLocalBody()
        {
            var session = FindAnyObjectByType<GameSession>();
            body = session != null ? session.LocalPlayer : null;
            if (body == null) return false;

            ApplyAppearance();
            remoteAvatar = RemoteAvatar.Attach(body, transform, mine: true);
            ApplyRiding(riding);
            ApplyDowned(downed);
            Debug.Log($"[NetPlayer] 내 캐릭터를 서버에 맡겼다 · {nickname}(userId={userId})");
            return true;
        }

        /// <summary>
        /// 내가 누른 키를 서버로 보낸다. 카메라는 로컬에 남으므로 조준 방향은
        /// 입력(<see cref="InputFrame.Look"/>)에 실려 함께 간다.
        /// </summary>
        void Update()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (!isLocalPlayer) return;

            // 스폰 메시지가 게임 씬이 몸을 세우기 전에 도착할 수 있다. 준비될 때까지 기다린다.
            if (body == null && !AdoptLocalBody()) return;

            // 조종석 앵커를 아직 못 물렸으면 계속 시도한다 (S15P21B202-434).
            // WorldSync 가 준비되는 시점이 스폰보다 늦을 수 있다.
            if (riding && !rideCameraBound) ApplyRideCamera();

            sendTimer += Time.deltaTime;

            // 카메라는 내 PC 에만 있다. 서버가 "앞" 이 어느 쪽인지 알 수 있도록
            // 지금 보고 있는 각도를 입력에 실어 함께 보낸다.
            InputFrame now = body.CurrentInput;
            // 일회성 복구는 신뢰성 있는 Command로 보낸다. 이동 패킷 반복으로 재발동하지 않는다.
            if (now.Restart && !respawnWasPressed) CmdRequestRespawn();
            respawnWasPressed = now.Restart;
            if (body.Cam != null)
            {
                now.AimYaw = body.Cam.Yaw;
                now.AimPitch = body.Cam.Pitch;

                // 조준을 어디서 시작하는지도 보낸다. 각도만 보내면 서버는 몸에서 재게 되어
                // 내 화면과 몇 미터 어긋난다 — 참가자가 눈앞의 부품을 못 잡던 이유다.
                now.AimFrom = body.Cam.transform.position;
            }

            bool changed = !everSent || !Same(now, lastSent);
            if (!changed && sendTimer < SendInterval) return;

            if (!everSent)
                Debug.Log($"[NetPlayer] 입력 전송 시작 · {nickname}(userId={userId}) · " +
                          $"isServer={isServer} isLocalPlayer={isLocalPlayer}");

            // 순번을 붙이고 **그때 내 골반이 있던 자리를 적어 둔다** (S15P21B202-363).
            // 서버가 이 순번의 결과를 돌려주면 이 자리와 견주어 오차를 뽑는다.
            // 방장 자신의 몸은 서버가 곧 정답이라 필요 없다.
            if (!isServer)
            {
                now.Seq = ++nextSeq;
                Remember(now.Seq, body.Hips != null ? body.Hips.position : transform.position);
            }

            CmdSendInput(now);
            lastSent = now;
            everSent = true;
            sendTimer = 0f;
        }

        /// <summary>보낸 순번과 그때의 자세를 고리 버퍼에 적는다.</summary>
        void Remember(uint seq, Vector3 position)
        {
            history[historyCount % HistorySize] = new SentPose { Seq = seq, Position = position };
            historyCount++;
        }

        /// <summary>그 순번을 보낼 때 내가 있던 자리. 너무 오래돼 밀려났으면 거짓.</summary>
        bool TryRecall(uint seq, out Vector3 position)
        {
            int have = Mathf.Min(historyCount, HistorySize);
            for (int i = 0; i < have; i++)
            {
                if (history[i].Seq != seq) continue;
                position = history[i].Position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 서버가 "순번 <paramref name="seq"/> 까지 적용한 결과는 이 자세다" 를 소유자에게만
        /// 돌려준다. 신뢰 없는 채널이다 — 하나 빠져도 다음 것이 오고, 신뢰 채널에 실으면
        /// 재전송 대기가 지연으로 쌓인다(S15P21B202-328 에서 겪은 것과 같은 종류).
        /// </summary>
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetEcho(uint seq, Vector3 position, bool serverRiding)
        {
            // 방장은 자기가 정답이라 보정할 것이 없다. 원격 아바타도 이 경로를 타지 않는다.
            if (isServer || !isLocalPlayer) return;
            if (seq == 0 || remoteAvatar == null || body == null) return;

            // 순서가 뒤집혀 온 옛 소식은 버린다. 받아들이면 이미 갚은 오차를 되돌린다.
            if (seq <= lastEchoSeq) return;
            lastEchoSeq = seq;

            // 탑승 자세는 NetworkTransform을 따른다. 보행 오차로 갚으면 좌석에서 밀린다.
            // 이전 탑승 상태의 지연된 echo도 새 보행 상태에 섞지 않는다.
            if (serverRiding || body.IsRiding) return;

            // 오차는 **같은 순번끼리** 견줘야 한다. 지금 자세와 견주면 왕복 시간만큼
            // 뒤로 끌린다 — 가만히 서 있어도 계속 뒤로 밀린다.
            if (!TryRecall(seq, out Vector3 mine)) return;

            Vector3 error = position - mine;

            // 임계값은 `PredictionCorrection` 한 곳에만 둔다 (`!275` AI 리뷰). 여기 따로
            // 상수를 두면 한쪽만 바꿔도 테스트는 통과하는데 런타임이 어긋난다.
            if (!PredictionCorrection.CanConverge(error))
            {
                // 물리로 이어질 수 없는 이동이다(리스폰·순간이동). 그 자리로 옮긴다.
                body.Doll?.Teleport(position, body.Hips != null ? body.Hips.rotation : transform.rotation);
                remoteAvatar.ClearCorrection();
                return;
            }

            remoteAvatar.AddCorrection(error);
        }

        /// <summary>
        /// 두 입력이 사실상 같은가. 마우스와 시선은 늘 미세하게 흔들려 그대로
        /// 비교하면 매 프레임 "바뀜" 이 되고, 제한을 둔 뜻이 사라진다.
        /// 시선은 1도까지는 같은 것으로 본다 — 그 차이는 화면에서 보이지 않는다.
        /// </summary>
        static bool Same(InputFrame a, InputFrame b)
        {
            return (a.Move - b.Move).sqrMagnitude < 0.0001f
                && (a.Look - b.Look).sqrMagnitude < 0.0001f
                && Mathf.Abs(Mathf.DeltaAngle(a.AimYaw, b.AimYaw)) < 1f
                && Mathf.Abs(a.AimPitch - b.AimPitch) < 1f
                // 조준 시작점도 늘 조금씩 흔들린다. 10cm 까지는 같은 것으로 본다 —
                // 잡기 판정(보조 반경 1.1m)에 영향을 주지 않는 차이다.
                && (a.AimFrom - b.AimFrom).sqrMagnitude < 0.01f
                && a.Jump == b.Jump && a.Run == b.Run
                && a.GrabLeft == b.GrabLeft && a.GrabRight == b.GrabRight
                && a.ToolAction == b.ToolAction
                && a.ToolPrimary == b.ToolPrimary && a.ToolSecondary == b.ToolSecondary
                && a.ToolUtility == b.ToolUtility
                && a.TransportAction == b.TransportAction
                && a.ArmsUp == b.ArmsUp && a.FreeLook == b.FreeLook
                && a.TabletToggle == b.TabletToggle && a.FieldToggle == b.FieldToggle
                && a.TabletPreviousPage == b.TabletPreviousPage
                && a.TabletNextPage == b.TabletNextPage
                && a.Restart == b.Restart
                && a.PingPressed == b.PingPressed
                // 여기 빠지면 눌러도 서버가 모른다 (S15P21B202-390). Same() 이 같다고
                // 보면 그 프레임을 보내지 않는다 — StuckEscapeHeld 가 실제로 그 상태다.
                && a.RescueHeld == b.RescueHeld;
        }

        // ── 클라이언트: 남의 몸 ─────────────────────────────────

        /// <summary>다른 사람의 몸. 판정에 관여하지 않는 그림이다.</summary>
        void BuildRemoteBody()
        {
            if (body != null) return;

            body = PlayerRig.Create(
                string.IsNullOrEmpty(nickname) ? "비행사" : nickname,
                transform.position,
                transform.rotation,
                TeamSuitColor,
                TeamAccentColor);

            ApplyAppearance();

            // 자식으로 붙이지 않는 이유는 RemoteAvatar 가 골반을 MovePosition 으로
            // 직접 옮기기 때문이다 — 부모를 따라 끌려가면 조인트가 그 이동을
            // 순간이동으로 받아들여 몸이 튄다.
            remoteAvatar = RemoteAvatar.Attach(body, transform);

            // 이미 타고 있는 사람이라면 지금 바로 감춘다. 훅은 값이 <b>바뀔 때만</b>
            // 돌므로, 탄 채로 접속한 사람에게는 오지 않는다(S15P21B202-305).
            ApplyRiding(riding);
            ApplyDowned(downed);
            Debug.Log($"[NetPlayer] 다른 플레이어가 보인다 · {nickname}(userId={userId})");
        }

        void OnIdentityChanged(string _, string newNickname)
        {
            if (body == null) return;
            body.Nickname = string.IsNullOrEmpty(newNickname) ? "비행사" : newNickname;
        }

        /// <summary>
        /// 탑승 여부가 바뀌면 <b>남의 화면에 있는 몸</b>을 감추거나 되돌린다
        /// (S15P21B202-305).
        ///
        /// <b><see cref="riding"/> 은 값을 쓰기만 하고 아무도 읽지 않았다.</b> 서버가
        /// 매 물리 스텝 채워 모든 클라이언트에 보내고 있었는데, 받는 쪽에서 쓰는 곳이
        /// 없어 남의 몸이 계속 그려졌다 — 조종석에 묻힌 몸통과 달리 <b>귀가 기체 밖으로
        /// 튀어나왔다.</b>
        ///
        /// <b>필드가 아니라 인수를 쓴다.</b> 훅이 도는 시점에 필드가 이미 갱신되었는지는
        /// Mirror 버전에 달려 있고, 인수는 어느 버전에서나 새 값이다.
        /// </summary>
        void OnRidingChanged(bool _, bool nowRiding) => ApplyRiding(nowRiding);

        void OnDownedChanged(bool _, bool nowDowned) => ApplyDowned(nowDowned);

        /// <summary>
        /// 남의 화면의 기절 표시를 맞춘다 (S15P21B202-378).
        ///
        /// <para><b>서버에서는 하지 않는다.</b> 서버의 몸이 진짜 상태이고, SyncVar 값으로
        /// 되쓰면 권위가 뒤집힌다 — 한 틱 늦은 값이 방금 바뀐 상태를 지울 수 있다.</para>
        ///
        /// <para>몸을 만드는 자리에서도 부른다. 그러지 않으면 <b>이미 기절한 채로 접속한
        /// 사람이 멀쩡해 보인다</b> — <c>ApplyRiding</c> 이 같은 이유로 그렇게 되어 있다.</para>
        ///
        /// <para><b><c>isLocalPlayer</c> 를 빼지 않는다</b>(!287 AI 리뷰가 물은 것).
        /// 참가자 기계에서 자기 자신의 몸에도 이 값이 선다. 그것이 맞다 — 그 전에는
        /// 참가자 본인의 <c>PlayerRig.Downed</c> 가 <b>서버와 의견이 달랐다.</b>
        /// 기절 판정은 서버가 하는데 로컬 게이트들(<c>RefreshInteractionFocus</c> ·
        /// <c>ApplyFallImpact</c> · <c>Stagger</c> · <c>KnockDown</c>)은 false 를 보고
        /// 계속 돌았다. 특히 기절한 참가자의 화면이 <b>여전히 고정쇠를 조준 대상으로
        /// 잡아 줬다.</b> 이제 호스트(<c>GoDown</c> 이 도는 쪽)와 같게 닫힌다.</para>
        ///
        /// <para>넉백·장비 해제·레이어·카메라는 여기서 하지 않는다. 그것들은 서버
        /// 권위이고 <c>SetRemoteDowned</c> 도 손대지 않는다. <b>참가자 본인의 기절
        /// 카메라 전환은 이 이슈 범위가 아니다</b> — 원래도 없었고 따로 봐야 한다.</para>
        /// </summary>
        void ApplyDowned(bool nowDowned)
        {
            if (body == null || isServer) return;

            body.SetRemoteDowned(nowDowned);
        }

        /// <summary>
        /// 탑승 여부를 몸에 적용한다.
        ///
        /// <b>몸이 아직 없을 때 훅이 먼저 올 수 있다.</b> 스폰 메시지가 몸을 세우기 전에
        /// 도착하는 경우가 있어(<c>Update</c> 의 주석이 같은 상황을 적어 두었다), 몸을
        /// 만드는 자리에서도 한 번 더 부른다. 그러지 않으면 <b>탄 채로 접속한 사람이
        /// 계속 보인다.</b>
        /// </summary>
        void ApplyRiding(bool nowRiding)
        {
            if (body == null || isServer) return;

            if (body.RemoteRiding != nowRiding)
            {
                historyCount = 0;
                remoteAvatar?.ClearCorrection();
            }
            body.SetRemoteRiding(nowRiding);
            remoteAvatar?.ApplyRidingState();
            ApplyRideCamera();
        }

        void OnRideVehicleChanged(int _, int __) => ApplyRideCamera();

        /// <summary>
        /// 내 카메라를 조종석에 물린다 (S15P21B202-434).
        ///
        /// <para><b>이것이 없으면 참가자만 시점이 어긋난다.</b> 조종석 앵커는
        /// <c>PlayerRig.TryEnterTransportVehicle</c> 안에서만 세우는데 그 함수는 탑승을
        /// 직접 수행한 기계에서만 돈다 — 참가자는 서버가 태우므로 타지 않는다.
        /// <c>OrbitCamera</c> 는 <c>vehicleView</c> 와 앵커가 <b>둘 다</b> 있어야
        /// 조종석으로 붙으므로, 앵커가 비면 그 분기를 건너뛰고 래그돌을 따라간다.</para>
        ///
        /// <para><b>될 때까지 다시 물어본다.</b> 스폰과 씬 준비 순서가 고정이 아니라
        /// (<c>WorldSync.OnStartClient</c> 의 주석) 훅이 도는 시점에 목록이 비어 있을 수
        /// 있다. 그때 한 번 실패하고 끝내면 그 판 내내 앵커가 없다 —
        /// <see cref="Update"/> 가 탑승 중이고 아직 못 물렸으면 계속 시도한다.</para>
        /// </summary>
        void ApplyRideCamera()
        {
            if (isServer || !isLocalPlayer || body == null) return;

            var cam = body.Cam;
            if (cam == null) return;

            if (!riding || ridingVehicle < 0)
            {
                // 하차했다. 앵커를 비워 두면 다음에 다른 탈것을 타도 옛 조종석을 본다.
                cam.SetVehicleEye(null);
                rideCameraBound = false;
                return;
            }

            var sync = WorldSync.Current;
            var vehicle = sync != null ? sync.VehicleAt(ridingVehicle) : null;
            if (vehicle == null) return;             // 아직 준비되지 않았다 — 다시 물어본다

            cam.SetVehicleEye(vehicle.CockpitEye);
            rideCameraBound = true;
        }

        /// <summary>조종석 앵커를 물렸는가. 못 물렸으면 <see cref="Update"/> 가 다시 시도한다.</summary>
        bool rideCameraBound;

        /// <summary>
        /// 이 탈것의 짝짓기 번호. 안 탔거나 목록에 없으면 -1 (S15P21B202-434).
        ///
        /// <para><see cref="WorldSync"/> 가 아직 없으면 -1 이다. 그러면 그 프레임은
        /// "안 탄 것" 으로 나가는데, 다음 프레임에 준비되면 제 번호가 실린다 —
        /// SyncVar 는 바뀔 때마다 가므로 늦게 채워져도 도착한다.</para>
        /// </summary>
        static int IndexOfVehicle(Metagame.Gameplay.Parts.TransportVehicle vehicle)
        {
            if (vehicle == null) return -1;

            var sync = WorldSync.Current;
            if (sync == null) return -1;

            return sync.IndexOfTracked(vehicle.Body);
        }

        public override void OnStopClient()
        {
            // 내가 이어받는 중이면 이 사람이 서 있던 자리를 적어 둔다 (S15P21B202-389).
            //
            // <b>적지 않으면 팀원 전원이 시작 지점으로 되돌아간다.</b> 탑·용접은 씬에
            // 남지만 사람은 그렇지 않다 — 남의 몸은 서버가 스폰한 것이라 아래
            // <see cref="RemoveBody"/> 가 지우고, 내가 서버가 된 뒤 새로 만든다.
            // 그때 <c>RejoinMemory</c> 에 자리가 없으면 스폰 지점이 된다.
            //
            // 원래 이 기록은 서버가 <see cref="OnStopServer"/> 에서 남긴다(S15P21B202-150).
            // 그런데 이어받기에서는 <b>그 서버가 이미 죽어</b> 아무것도 남기지 못하므로,
            // 마지막으로 받은 자세를 본 사람이 대신 적는다.
            //
            // 내 몸은 적지 않는다. 게임 씬 것이라 <see cref="RemoveBody"/> 가 남겨 두고,
            // 서버가 된 뒤 <see cref="OnStartServer"/> 가 그것을 그대로 넘겨받는다.
            if (HostMigration.Taking && body != null && !isLocalPlayer)
            {
                RejoinMemory.Remember(userId, body.FootPosition, body.PlanarForward);
            }

            // 쓰던 색도 물려받는다 (S15P21B202-396). <b>내 것까지 적는다</b> — 자리 기록과
            // 달리 내 색도 방장이 정해 준 것이라, 안 적으면 내 색이 바뀐다.
            if (HostMigration.Taking)
            {
                TeamSlots.Remember(userId, teamSlot);
            }

            ClientPlayers.Remove(this);
            PerfLog.Players = ClientPlayers.Count;
            RemoveBody();
        }
        public override void OnStopServer()
        {
            // 어디에 서 있었는지 적어 둔다 (S15P21B202-150). 이것이 없으면 다시 붙었을 때
            // 새 몸이 스폰 지점에 서서, 멀리까지 걸어간 사람이 처음으로 되돌아간다.
            //
            // 넘겨받은 몸(방장 자기 몸)은 적지 않는다 — 게임 씬이 만든 것이고 호스트가
            // 끊기면 매치 자체가 끝난다.
            if (body != null && !adoptedBody)
            {
                RejoinMemory.Remember(userId, body.FootPosition, body.PlanarForward);
            }

            ServerPlayers.Remove(this);
            ClientPlayers.Remove(this);
            RemoveBody();
        }

        /// <summary>
        /// 몸을 치운다. 내 몸은 게임 씬이 만든 것이라 여기서 없애지 않는다 —
        /// 카메라와 HUD 가 아직 그것을 보고 있다.
        /// </summary>
        void RemoveBody()
        {
            if (body == null) return;

            // 로컬 리그는 씬에 남는다. 연결 종료/호스트 전환 때 탑승용 물리를
            // 복원하고 이전 서버를 따르던 컴포넌트를 제거한다.
            if (remoteAvatar != null)
            {
                remoteAvatar.enabled = false;
                body.SetRemoteRiding(false);
                Destroy(remoteAvatar);
            }

            // 넘겨받은 몸은 게임 씬 것이다 — 카메라와 HUD 가 아직 보고 있다.
            if (!adoptedBody && (!isLocalPlayer || isServer)) Destroy(body.gameObject);

            body = null;
            remoteAvatar = null;
            adoptedBody = false;
        }
    }
}
