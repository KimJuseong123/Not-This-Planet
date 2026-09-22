using System;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 우주비행사의 이동과 물리 상호작용을 연결한다.
    ///
    ///  · 골반에 매달린 보이지 않는 스프링이 몸무게를 떠받쳐 서 있게 한다 (다리는 거들 뿐)
    ///  · 골반을 카메라 기준 정면으로 세우고, 이동 방향과 독립적으로 유지한다
    ///  · 다리는 걷기 주기에 맞춰 조인트 목표 자세를 흔든다 (애니메이션 클립 없음)
    ///  · 팔은 카메라가 보는 쪽으로 뻗되, 근처에 잡을 것이 있으면 그쪽으로 휘어 간다
    ///  · GrabbableRole.Tool 도구는 양손 논리 고정 그립으로 잡고 중력과 누적 속도를 제거해 전방에 고정된다
    ///  · 일정 질량 이상인 동적 물체는 들어 올리지 않고 고정물을 짚듯 잡는다
    ///
    /// 이동 자체는 목표 속도를 직접 따라가 안정적으로 조종하고, 잡기·등반·충돌 반응은 물리에 맡긴다.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        const float ClampInteractionRange = 3f;
        const float ClampCrosshairAssistRadius = 0.14f;
        const int ClampHitBufferSize = 32;
        readonly Collider[] _vegetationHits = new Collider[32];

        [SerializeField, Min(0f)]
        [Tooltip("직접 조준한 고정못의 실제 Collider 표면까지 플레이어 골반에서 클릭할 수 있는 최대 거리(m). 고정못은 특수 보정으로 처리됨")]
        // Clamp가 본체 아래에 있어 어깨 높이 때문에 막히지 않도록 특수 보정으로 처리한다.
        float clampGrabDistanceFromPlayer = 0.72f;

        /// <summary>구조 상호작용 트리거의 반지름(m). 쓰러진 몸 옆에 서면 닿는다.</summary>
        const float RescueTriggerRadius = 1.1f;
        const float ClampInteractionHalfAngle = 60f;

        public Doll Doll;
        /// <summary>
        /// 이 리그를 보는 카메라. <b>원격 플레이어는 null 이다</b> — 그래서 1인칭 숨김이
        /// 제 캐릭터에만 적용되고 남의 캐릭터는 그대로 보인다.
        /// </summary>
        public OrbitCamera Cam
        {
            get => cam;
            set { cam = value; RefreshDollVisibility(); }
        }

        OrbitCamera cam;
        public bool IsLocal = true;
        public string Nickname = "비행사";
        /// <summary>이 플레이어의 액센트 색. 핑과 다른 팀 표시가 같은 색을 쓴다.</summary>
        public Color TeamColor { get; private set; }

        /// <summary>
        /// 이 몸의 위치를 서버가 정하는가. 켜져 있으면 물리는 여기서 돌지 않고,
        /// 서버가 보낸 자리를 그리기만 한다(<c>Metagame.Network.RemoteAvatar</c>).
        ///
        /// 이 파일의 물리 코드는 이 값을 보지 않는다 — 부활·낙사처럼 "위치를 정하는"
        /// 판단을 하는 쪽(GameSession)이 대신 물러나라는 표시다. 혼자 돌릴 때는
        /// 계속 꺼져 있으므로 기존 흐름은 그대로다.
        /// </summary>
        public bool NetworkDriven;

        /// <summary>
        /// 이 몸의 <b>판정을 이 기계가 하는가</b> (S15P21B202-390).
        ///
        /// <para><b>기본이 참이고 클라이언트가 내려놓는다.</b> 그 자리는
        /// <c>RemoteAvatar.Attach</c> 다 — 거기서 이미 <see cref="NetworkDriven"/> 을
        /// 켜고 <see cref="ClampInteractor"/> 를 끈다("고정쇠 잡기가 클라에서도 돌아
        /// 서버와 이중 판정이 된다", <c>!275</c> AI 리뷰). 같은 이유의 같은 자리라
        /// 새 불변식을 만들지 않는다.</para>
        ///
        /// <para><b>처음에는 기본을 거짓으로 두고 서버가 켜게 했다.</b> 그러자 에디터
        /// 테스트와 혼자 실행이 깨졌다 — 거기에는 <c>NetPlayer</c> 가 없어서 아무도
        /// 켜 주지 않는다(<c>DownedRescueTests</c> 2건이 그것을 잡았다).</para>
        ///
        /// <para><b>그리고 서버는 다시 집는다</b> (S15P21B202-414). 기본값만으로는
        /// 부족하다 — 내려놓은 뒤 되돌아올 길이 없으면 <b>호스트를 이어받은 기계가
        /// 영원히 거짓</b>으로 남는다(<c>HostMigration</c> 은 <c>RemoteAvatar</c> 를
        /// 떼지 않는다). 그래서 <c>NetPlayer</c> 의 서버 루프가 매 스텝 참으로 둔다.
        /// 값을 쓰는 곳이 둘인 것은 의도다: <b>거짓은 클라이언트가, 참은 서버가</b>
        /// 말하고, 두 말은 같은 기계에서 겹치지 않는다.</para>
        ///
        /// <para><see cref="NetworkDriven"/> 으로는 구별할 수 없다. 그 값은 호스트가
        /// 넘겨받은 자기 몸에도 참이라서, 서버와 클라이언트를 가르지 못한다.</para>
        /// </summary>
        public bool ServerAuthority = true;
        public bool WorkstationMovementLocked { get; set; }

        /// <summary>조준 기준점. 카메라가 없는 서버에서도 겨냥이 가능하게 한다.</summary>
        Transform aimPivot;

        // ── 상태 ────────────────────────────────────────────────
        public bool Grounded { get; private set; }
        public float GroundDistance { get; private set; }
        public Vector3 GroundVelocity { get; private set; }
        public float Speed { get; private set; }
        public float MaxHealth { get { return Tuning.MaxHealth; } }

        float health;

        /// <summary>
        /// 지금 체력.
        ///
        /// <para><b>왜 대입을 세터로 모았나</b>(S15P21B202-539). 빨간 테두리가
        /// <see cref="DamageFlash01"/> 를 읽게 바뀌면서 "이번에 얼마나 깎였나" 를
        /// 알아야 하는데, 깎는 자리가 <see cref="ApplyDamage"/> · <see cref="GoDown"/> ·
        /// <see cref="SetRemoteHealth"/> 로 흩어져 있다. 세 곳에 같은 줄을 넣으면
        /// <b>한 곳이 빠진다</b> — <c>-513</c> 이 참가자 기계에서 정확히 그렇게 빠졌다.
        /// 세터에 두면 어느 경로로 깎여도 번쩍인다.</para>
        ///
        /// <para>늘어나는 것은 번쩍이지 않는다 — 구조(<see cref="Revive"/>)와 판 초기화
        /// (<see cref="ResetHealth"/>)가 그 경로다.</para>
        /// </summary>
        public float Health
        {
            get { return health; }
            private set
            {
                float lost = health - value;
                health = value;

                if (lost <= 0f || MaxHealth <= 0f) return;

                // 겹쳐 맞으면 더 센 쪽이 남는다. 합치면 연달아 맞을 때 1 에 붙어
                // 버려서 "얼마나 세게 맞았나" 가 사라진다.
                DamageFlash01 = Mathf.Max(DamageFlash01,
                                          Mathf.Clamp01(lost / MaxHealth * Tuning.DamageFlashGain));
            }
        }

        public float Health01 { get { return MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / MaxHealth); } }
        public bool IsDead { get { return Health <= 0f; } }

        /// <summary>
        /// 피해를 받은 직후의 번쩍임 0~1. <c>GameHud</c> 의 빨간 테두리가 이것을 읽는다
        /// (S15P21B202-539).
        ///
        /// <para><b>왜 체력이 아니라 이것인가.</b> 예전 테두리는 <see cref="Health01"/> 에
        /// 정비례하는 상시 표시였다. 그런데 구조는 체력을 절반만 돌려주고
        /// (<c>PhysicsScale.RescueHealthRatio</c>) 게임 안에 체력을 되돌리는 수단이
        /// 없다 — <b>한 번 구조되면 판이 끝날 때까지 테두리가 남았다.</b></para>
        ///
        /// <para>밸런스가 아니라 표시 방식을 고친다. <c>RescueHealthRatio</c> 0.5 는
        /// 의도된 값이다(<c>PhysicsScale</c> 의 주석).</para>
        /// </summary>
        public float DamageFlash01 { get; private set; }

        /// <summary>
        /// 기절해 쓰러져 있는가(기획 §26-13). 동료가 일으켜야 다시 움직인다.
        ///
        /// <b>예전에는 이 상태가 없었다.</b> 체력이 0이 되면 <c>GameSession</c> 이
        /// 곧바로 <c>Respawn()</c> 을 불러 스폰 지점에 되살렸다 — 예전 NotThisPlanet 코스의
        /// 낙사 처리를 그대로 쓴 것이다. 팀원이 도우러 갈 틈도 이유도 없었다.
        /// </summary>
        public bool Downed { get; private set; }

        /// <summary>
        /// 남이 나를 양손으로 들어 <b>내던져서 날아가는 중</b>인가 (기획 §20.4 ·
        /// S15P21B202-519).
        ///
        /// <para><b>기절과 다르다.</b> 기절은 체력이 다한 상태이고 이것은 잠깐 날아가는
        /// 것뿐이다. 땅에 닿으면 스스로 풀린다. 그런데 <b>시야는 같은 것이 필요하다</b> —
        /// 몸이 구르는 대로 화면이 함께 넘어갔다가 3인칭으로 물러나는 것
        /// (<see cref="OrbitCamera.SetDownedView"/>). 그래서 카메라는 이 둘을 합쳐
        /// 판단한다(<see cref="BodyViewActive"/>).</para>
        /// </summary>
        public bool Tossed { get; private set; }

        /// <summary>던져진 뒤 지난 시간(초). 착지 판정을 너무 이르게 하지 않기 위한 것.</summary>
        public float TossedSeconds { get; private set; }

        /// <summary>
        /// 내가 동료를 던진 횟수. <b>참가자에게 던지는 동작을 알리는 값</b>이다.
        ///
        /// <para>던지기는 한 순간의 사건이라 "지금 던지는 중" 이라는 상태가 없다. 그래서
        /// 이벤트(<c>ClientRpc</c>) 로 보내고 싶어지는데, 그러면 <b>패킷 하나를 잃으면
        /// 그 동작은 영영 안 보인다.</b> 대신 <b>세어서 보내면</b> 받는 쪽이 "내가
        /// 마지막으로 본 수보다 크다" 로 판단하므로 한 번쯤 늦게 와도 복구된다.</para>
        /// </summary>
        public uint TossSeq { get; private set; }

        /// <summary>
        /// 시야가 몸을 따라가야 하는가. 기절이든 던져졌든 같은 화면이 필요하다.
        /// </summary>
        public bool BodyViewActive => BodyOutOfControl;

        /// <summary>
        /// 몸이 내 조종을 벗어난 상태 — 기절했거나(<c>-259</c>) 던져져 날아가는
        /// 중이다(<c>-519</c> · <c>-550</c>).
        ///
        /// <para><b>왜 하나로 묶나.</b> 둘은 같은 것을 뜻한다 — 넉백은 받되 입력은 없고,
        /// 몸을 세우지 않으며, 시야는 몸을 따라 넘어갔다가 3인칭으로 물러난다.
        /// <c>Tick</c> 의 조기 반환과 <see cref="BodyViewActive"/> 가 같은 조건을 따로
        /// 적고 있으면 한쪽만 고치는 날이 온다.</para>
        /// </summary>
        public bool BodyOutOfControl => Downed || Tossed;

        public TransportVehicle MountedVehicle { get; private set; }
        public bool IsMounted { get { return MountedVehicle != null || RemoteRiding; } }

        /// <summary>
        /// 몸이 조인트로 붙어 있는 강체. 운반 장치 탑승도, 우주선 탑승도 이것을 채운다.
        ///
        /// 예전에는 탑승 상태가 <see cref="MountedVehicle"/> 하나였고 타입이
        /// <see cref="TransportVehicle"/> 로 못박혀 있었다. 그래서 우주선에는 탈 수 없었고,
        /// S15P21B202-200 의 탑승은 인원을 세는 것뿐이었다 — 발사하면 발사대에 서서
        /// 우주선이 올라가는 것을 구경했다.
        ///
        /// 운반 장치 고유의 처리(조종 입력, 조종석 시야, 하차 발판 탐색)는
        /// <see cref="MountedVehicle"/> 이 그대로 갖는다. 여기 올라오는 것은 "몸이 붙어
        /// 있으니 보행 물리를 돌리지 않는다" 는 공통 사실뿐이다.
        /// </summary>
        public Rigidbody RidingBody { get; private set; }

        /// <summary>무엇에든 붙어 실려 가는 중인가.</summary>
        public bool IsRiding { get { return RidingBody != null || RemoteRiding; } }

        /// <summary>
        /// 서버가 확정한 견인장치 탑승 상태. 참가자 본인에게도 적용한다.
        /// 클라이언트에서는 좌석 조인트를 만들지 않으므로 RidingBody가 없어도
        /// 보행·부유·상호작용 판정은 탑승 상태를 사용해야 한다.
        /// </summary>
        public bool RemoteRiding { get; private set; }

        /// <summary>
        /// 남이 보는 이 몸의 탑승 여부를 갱신한다(S15P21B202-305).
        ///
        /// <b>몸을 지우거나 비활성화하지 않는다.</b> 자세 동기화가 계속 돌아야 하차한
        /// 자리에 서 있고, 기절 판정과 구조 상호작용(§20)이 몸에 붙어 있다. 감추는
        /// 방법은 1인칭·기절 3인칭에서 쓰던 것과 같은 <see cref="Doll.SetVisible"/> 하나로
        /// 둔다 — 새 방법을 만들면 상태가 두 갈래가 된다.
        /// </summary>
        public void SetRemoteRiding(bool riding)
        {
            // <b>인형이 아직 없을 때를 같은 값으로 걸러내면 안 된다.</b> 그러면 플래그만
            // 참으로 남고, 인형이 생긴 뒤 같은 값이 다시 와도 여기서 돌아나가 렌더러가
            // 끝내 꺼지지 않는다 — <b>원래 증상이 조용히 되돌아온다.</b>
            //
            // 지금 경로에서는 PlayerRig.Create 가 인형을 만들고 나서 돌려주므로 닿지
            // 않지만, Doll 은 public 필드라 불변식이 없다.
            if (RemoteRiding == riding && Doll != null) return;

            RemoteRiding = riding;
            RefreshDollVisibility();
        }
        /// <summary>
        /// 남의 화면에서 이 몸이 <b>기절한 것으로 보이고 후보로 잡히게</b> 한다
        /// (S15P21B202-378).
        ///
        /// <para><b>왜 필요한가.</b> 기절은 서버가 판정한다(<see cref="GoDown"/> 를 부르는
        /// 곳은 서버 물리뿐이다). 그래서 참가자 기계에서 원격 플레이어의
        /// <see cref="Downed"/> 는 <b>언제나 false</b> 였고,
        /// <see cref="FindDownedTeammate"/> 가 <c>!other.Downed</c> 로 건너뛰어
        /// <b>참가자는 동료를 일으킬 수 없었다.</b> 기획 §26-13 이 호스트에서만
        /// 성립하고 있었다.</para>
        ///
        /// <para><b>표시와 후보 자격만 맞춘다.</b> <see cref="GoDown"/> 을 그대로 부르면
        /// 안 된다 — 그 함수는 넉백·장비 해제·레이어 변경·카메라 전환까지 하고 그것들은
        /// 서버 권위다(S15P21B202-145). 특히 레이어는 건드리지 않는다:
        /// <c>RemoteAvatar</c> 가 원격 몸을 <c>RemotePlayer</c> 레이어로 옮겨 두었고
        /// 여기서 <c>PlayerDowned</c> 로 바꾸면 그 의도가 깨진다. 후보 판정은 레이어를
        /// 보지 않고 <see cref="All"/> 을 훑으므로 바꿀 이유도 없다.</para>
        ///
        /// <para><see cref="RefreshCarryTarget"/> 을 부르는 이유: 그 함수는
        /// <see cref="GoDown"/>·<see cref="Revive"/> 안에서만 불린다. 클라이언트에서는
        /// 둘 다 돌지 않으므로 <see cref="CarryTarget"/> 이 비어 있고, 그러면
        /// <b>플래그만 맞춰도 후보가 되지 않는다</b>(잡을 지점이 없다).</para>
        /// </summary>
        /// <summary>
        /// 서버가 보낸 체력을 받는다 (S15P21B202-513).
        ///
        /// <para><b>왜 필요한가.</b> 빨간 테두리(<c>GameHud.HealthFrame</c>)는
        /// <see cref="Health01"/> 을 읽는데, 참가자 기계에는 서버가 깎은 값이 오지
        /// 않았다 — 실측으로 <c>NetPlayer</c> 와 <c>WorldSync</c> 둘 다에서
        /// <c>health</c> 가 <b>0회</b>였다. 그래서 참가자에게 체력은 사실상
        /// <b>"가득" 아니면 "기절" 둘뿐</b>이었다(운석 직격은 <c>ApplyDamage</c> 를
        /// 거치지 않고 곧바로 <see cref="GoDown"/> 이고 <c>downed</c> 는 전송된다).</para>
        ///
        /// <para><b>0~1 이 아니라 절댓값을 받는다.</b> <see cref="IsDead"/> 가
        /// <c>Health &lt;= 0</c> 이라, 비율만 받으면 그 값이 여전히 로컬로 남아
        /// 어긋난다 — <c>DamageSource</c> 와 <c>Stage1DustStormFragment</c> 가
        /// <c>IsDead</c> 로 피해를 거르므로, 서버가 기절시킨 사람을 참가자 기계에서
        /// 계속 때리게 된다.</para>
        ///
        /// <para><b>권위가 있는 몸은 무시한다.</b> 호스트·혼자 실행에서는 자기 판정이
        /// 정답이고, 받은 값으로 덮으면 값이 두 곳에서 오게 된다
        /// (<c>-383</c>). <see cref="SetRemoteBeingRescued"/> 와 같은 규칙이다.</para>
        /// </summary>
        public void SetRemoteHealth(float nowHealth)
        {
            if (ServerAuthority) return;

            Health = Mathf.Clamp(nowHealth, 0f, MaxHealth);
        }

        /// <summary>
        /// 서버가 "너는 지금 던져져 날아가는 중" 이라고 알려 준 것을 받는다
        /// (S15P21B202-519).
        ///
        /// <para><b>권위가 있는 몸은 무시한다.</b> <see cref="SetRemoteHealth"/> 와 같은
        /// 규칙이다 — 호스트는 자기 판정이 정답이고, 받은 값으로 덮으면 두 곳에서 정하는
        /// 것이 되어 갈라진다(<c>-383</c> · <c>-513</c>).</para>
        ///
        /// <para><b>카메라까지 여기서 바꾼다.</b> <c>-511</c> 이 기절에서 겪은 것과 같은
        /// 자리다 — 값은 내려오는데 받는 쪽이 플래그만 세우고 카메라를 안 바꾸면
        /// "호스트만 3인칭" 이 된다(QA 유형 M).</para>
        /// </summary>
        public void SetRemoteTossed(bool nowTossed)
        {
            if (ServerAuthority) return;
            if (Tossed == nowTossed && Doll != null) return;

            Tossed = nowTossed;
            if (!nowTossed) TossedSeconds = 0f;

            RefreshDownedCamera();
            RefreshDollVisibility();
        }

        /// <summary>
        /// 남의 몸에 자세를 반영한다 (S15P21B202-616).
        ///
        /// <para><b>서버에서는 하지 않는다</b> — <see cref="SetRemoteDowned"/> 와 같은
        /// 이유다. 서버의 몸이 진짜 상태이고, 한 틱 늦은 SyncVar 값이 방금 바뀐
        /// 상태를 지울 수 있다.</para>
        ///
        /// <para>이게 없으면 <b>남의 화면에서 그 사람의 <see cref="Balance"/> 와
        /// <see cref="Hover"/> 가 선 자세를 목표로 잡는다.</b> 골반 위치는 서버가
        /// 보내 주지만 팔다리는 각자 기계의 조인트가 잡으므로 자세가 어긋난다.</para>
        /// </summary>
        public void SetRemoteProne(bool value)
        {
            Prone = value;
            proneHeldLast = false;
        }

        public void SetRemoteDowned(bool nowDowned)
        {
            // 인형이 아직 없을 때를 같은 값으로 걸러내면 안 된다 — 플래그만 남고
            // 인형이 생긴 뒤 같은 값이 다시 와도 여기서 돌아나가 잡을 지점이 끝내
            // 만들어지지 않는다. SetRemoteRiding 이 같은 함정을 적어 두고 있다.
            if (Downed == nowDowned && Doll != null) return;

            Downed = nowDowned;

            RefreshRescueTarget();
            RefreshCarryTarget();

            // **참가자 본인의 기절 카메라 전환**(S15P21B202-511). 예전에는 여기에
            // 없어서 참가자는 죽어도 1인칭 그대로였다 — 실측으로 `Downed=True` 인데
            // `DownedView=False` 였다. 값은 잘 내려오고 구조 지점·잡을 지점도 켜지는데
            // 카메라만 안 바뀌었다.
            //
            // S15P21B202-378 이 "넉백·장비 해제·레이어·카메라는 여기서 하지 않는다 —
            // 서버 권위다" 로 넷을 묶었는데, **셋은 맞고 하나는 틀리다.** 카메라는
            // 각자 화면의 일이라 남이 볼 일도 서버가 알 일도 없다. 같은 문장에 들어
            // 있어서 함께 빠졌고, 그 주석 스스로 "따로 봐야 한다" 고 적어 두었다.
            RefreshDownedCamera();

            RefreshDollVisibility();

            // **이 기계가 남의 기절을 받았는지 남긴다** (S15P21B202-414).
            //
            // 2026-09-10 16:00 QA 의 "부활 게이지가 안 보였다" 를 로그로 판정할 수
            // 없었다. `[NetPlayer] 조준` 진단은 **로컬 플레이어**만 찍어서(기절O/X 도
            // 자기 값이다) 참가자가 동료의 기절을 아는지가 어디에도 남지 않았다 —
            // 후보가 없던 것인지 누르지 않은 것인지 구별할 근거가 없었다.
            //
            // 값이 **바뀔 때만** 오는 함수이므로 로그가 늘지 않는다.
            // **구조 지점이 실제로 생겼는지 함께 남긴다** (S15P21B202-433).
            //
            // 2026-09-11 4인 QA 에서 참가자가 "살리기 G 표시가 안 뜬다 · 게이지가 안
            // 보인다" 를 신고했다. 이 줄은 `원격 기절 O` 를 참가자에서 8~11번 찍었고
            // 호스트에는 `[구조] 시작 → 완료` 가 있는데 참가자에는 `시작` 이 **한 번도
            // 없다** — 거기까지는 보인다. 그런데 그것이
            //   ① 구조 지점이 아예 없어서인지
            //   ② 지점은 있는데 인터랙터가 후보로 잡지 못해서인지
            //   ③ 표시가 없어 사람이 누르지 않아서인지
            // **구별할 근거가 로그에 없었다.**
            //
            // `RefreshRescueTarget` 은 `Doll.Hips` 가 없으면 아무것도 만들지 않고
            // 돌아간다. 그 경우 플래그만 남고 잡을 지점이 끝내 생기지 않으므로, 이
            // 사실이 남아야 ①을 그 자리에서 가릴 수 있다. 값이 바뀔 때만 오는
            // 함수이므로 줄이 늘지 않는다.
            string point = RescueTarget == null
                ? "구조지점없음(인형이 아직 없다)"
                : RescueTarget.gameObject.activeSelf ? "구조지점켬" : "구조지점끔";

            Debug.Log($"[NotThisPlanet][구조] 원격 기절 {(nowDowned ? "O" : "X")}"
                      + $" · 대상={Nickname} · {point}");
        }

        public bool Hanging { get { return LeftHand.Hanging || RightHand.Hanging; } }
        public bool Carrying { get { return LeftHand.Carrying || RightHand.Carrying; } }
        public bool Ragdolled { get; private set; }      // 크게 넘어져 조종이 잠깐 풀린 상태
        public float StepPhase { get; private set; }
        /// <summary>현재 캐릭터가 유지하는 수평 정면. 이동 방향과 독립적으로 카메라를 따른다.</summary>
        public Vector3 FacingDirection { get { return faceDir; } }

        /// <summary>
        /// 제 발로 엎드려 있는가 (S15P21B202-616).
        ///
        /// <para><b><see cref="Ragdolled"/> 와 다르다.</b> 저쪽은 "자빠졌다" 이고
        /// 이쪽은 "엎드리기로 했다" 다. 엎드리면 머리가 골반보다 낮아져
        /// <see cref="Ragdolled"/> 가 <b>저절로 켜지는데</b>, 그러면 조종을 잃어
        /// 기어갈 수가 없다 — 그래서 그 계산에서 이 상태를 뺀다.</para>
        ///
        /// <para>폭풍 면제의 근거이기도 하다. 물리는 서버가 도므로
        /// (S15P21B202-145) 이 값을 서버가 갖고 있어야 판정이 갈리지 않는다 —
        /// 그래서 토글 처리는 <see cref="Tick"/>(양쪽에서 도는 자리)에서 하고,
        /// 누름이 아니라 <b>눌린 상태</b>를 받아 가장자리를 여기서 잡는다.</para>
        /// </summary>
        public bool Prone { get; private set; }
        bool proneHeldLast;

        /// <summary>엎드린 채 기어가는 속도(m/s). 걷기 2.9 의 31% — 일반적인 게임의 포복 속도.</summary>
        public const float CrawlSpeed = 0.9f;

        /// <summary>엎드렸을 때 골반이 떠 있는 높이(m). 선 자세는 <see cref="Tuning.RideHeight"/> 0.30 이다.</summary>
        public const float ProneRideHeight = 0.13f;

        /// <summary>엎드린 몸통이 앞으로 기운 각도. 90 이면 완전히 수평이다.</summary>
        const float PronePitch = 84f;

        /// <summary>
        /// 춤 때문에 몸이 시점을 안 따라가는 상태인가 (S15P21B202-596).
        ///
        /// <para><b>춤 클립은 뿌리를 돌리지 않는다</b>(<c>applyRootMotion = false</c>).
        /// 허리·척추를 최대 ±76도 비틀 뿐이고, 캐릭터의 세계 방향은 전부
        /// <see cref="faceDir"/> 다. 그 <see cref="faceDir"/> 이 춤 중에도 시정수
        /// 1/7초로 카메라를 쫓으면 비틀린 상체 위에 골반까지 돌아 <b>미리보기와 다른
        /// 그림</b>이 된다 — 실제로 그 지적을 받았다.</para>
        ///
        /// <para><b>물리는 서버가 돈다</b>(S15P21B202-145, <c>RemoteAvatar</c>).
        /// 남의 화면에 보이는 골반 회전은 서버가 보낸 것이므로, 이 판단이 서버에서도
        /// 같아야 한다. 그래서 카메라가 아니라 <b>몸이 실제로 춤 클립을 돌리고
        /// 있는가</b>를 본다 — 서버도 <c>CmdPlayDance</c> 에서 같은 <c>Suit</c> 로
        /// 춤을 켜므로 양쪽이 같은 답을 낸다.</para>
        /// </summary>
        public bool DanceHolding
        {
            get { return Doll != null && Doll.Suit != null && Doll.Suit.DanceActive; }
        }
        /// <summary>현재 실제 수평 이동을 캐릭터 정면 기준 좌우·전후로 나타낸 값.</summary>
        public Vector2 MovementDirectionLocal { get { return movementDirectionLocal; } }

        /// <summary>
        /// 이번 틱의 <b>이동 의도</b>(월드 수평, 최대 길이 1). 실제 속도가 아니다.
        ///
        /// 파츠 밀기(기획 §13.1 "이동 방향을 검사한다")가 쓴다. 500kg 짜리 파츠를 밀 때는
        /// 제자리걸음이라 속도로는 의도를 알 수 없다(S15P21B202-250).
        /// </summary>
        public Vector3 MoveWish { get; private set; }

        /// <summary>
        /// 셋 이상이 모여 파츠를 <b>실제로 밀고 있는가</b>(기획 §13 · §10.2).
        /// 밀기 카메라가 이 값으로 3인칭 전환을 판단한다.
        ///
        /// <b>서버와 참가자가 채우는 경로가 다르다.</b> 파츠 물리는 서버에서만 도므로
        /// (참가자 화면의 파츠는 kinematic 이고 <c>PartPush.CanPush</c> 가 false 다)
        /// 참가자는 이 값을 스스로 알 수 없다. 서버는 <see cref="Tick"/> 에서
        /// <c>PartPush</c> 를 직접 조회하고, 참가자는 <see cref="NetPlayer"/> 가
        /// SyncVar 로 받아 <see cref="SetHaulState"/> 로 넣어 준다.
        /// </summary>
        public bool Hauling { get; private set; }

        /// <summary>미는 방향(수평 단위). 밀고 있지 않으면 0 이다.</summary>
        public Vector3 HaulDirection { get; private set; }

        /// <summary>
        /// 지금 미는 파츠가 낼 수 있는 속도(m/s) (S15P21B202-667). 미는 사람은 여기에
        /// 맞춰 걷는다 — <b>고정 배율로 하면 넷이 밀 때 전원이 뒤처진다</b>(파츠는
        /// 1.5m/s 인데 사람은 0.73m/s 라 접촉이 끊긴다).
        ///
        /// <para>참가자 화면에는 <c>NetPlayer.haulTopSpeed</c> 로 서버가 계산한 값이
        /// 그대로 온다. <b>0 이면 상한을 안 건다</b> — 모를 때 1인 값을 넣으면 그것이
        /// 바닥이 아니라 천장이 되어 넷이 밀 때 참가자만 뒤처진다.</para>
        /// </summary>
        public float HaulTopSpeed { get; private set; }

        /// <summary>파츠 표면에서 이 사람이 잡거나 짚은 지점(월드).</summary>
        public Vector3 HaulGrasp { get; private set; }

        /// <summary>
        /// 참가자 화면에서 서버가 보낸 밀기 상태를 넣는다.
        /// 서버·혼자 실행에서는 <see cref="Tick"/> 이 직접 채우므로 부르지 않는다.
        /// </summary>
        /// <param name="topSpeed">
        /// 그 파츠가 낼 수 있는 속도(m/s). <b>0 이면 걷기 상한을 안 건다</b> —
        /// 모르는 값을 1인 속도로 채우면 그것이 바닥이 아니라 천장이 된다.
        /// </param>
        public void SetHaulState(bool hauling, Vector3 direction, Vector3 grasp,
                                 float topSpeed = 0f)
        {
            ApplyHaulState(hauling, direction, grasp, topSpeed);
        }

        // 밀기 중 이동 기준. **시점이 아니라 밀기를 시작한 방향이다** — §10.2
        // "마우스로 제한적인 자유 시점이 가능하되 캐릭터는 기존 밀기 방향을 유지한다".
        //
        // 시작 순간에 한 번 정하고 밀기가 끝날 때까지 붙든다. 매 틱 HaulDirection 을
        // 따라가게 하면 순환이 된다 — HaulDirection 은 사람들의 MoveWish 합이고
        // MoveWish 는 이 기준으로 만들어지므로, A 만 눌러도 기준이 계속 왼쪽으로
        // 돌아 제자리에서 빙빙 도는 조작이 된다.
        float haulBasisYaw;
        bool haulBasisSet;

        /// <summary>밀기 중 이동·조작의 기준 방향(도). 밀고 있지 않으면 의미가 없다.</summary>
        public float HaulBasisYaw => haulBasisYaw;

        void ApplyHaulState(bool hauling, Vector3 direction, Vector3 grasp, float topSpeed)
        {
            // **0 이면 상한을 안 건다.** 예전에는 모를 때 1인 값을 넣었는데, 걷기
            // 상한이 Min 이라 그것이 바닥이 아니라 **천장**이 됐다 — 넷이 미는데
            // 참가자만 0.69m/s 로 묶여 파츠를 놓쳤다. 모르면 안 누르는 쪽이 낫다.
            HaulTopSpeed = topSpeed;
            var flat = new Vector3(direction.x, 0f, direction.z);

            Hauling = hauling && flat.sqrMagnitude > 1e-6f;
            HaulDirection = Hauling ? flat.normalized : Vector3.zero;
            HaulGrasp = grasp;

            if (Hauling)
            {
                // 기준은 **처음 한 번만** 정한다. 위 주석의 순환을 막는 자리다.
                if (!haulBasisSet)
                {
                    haulBasisYaw = Mathf.Atan2(HaulDirection.x, HaulDirection.z)
                                   * Mathf.Rad2Deg;
                    haulBasisSet = true;
                }
            }
            else
            {
                haulBasisSet = false;
            }
        }

        /// <summary>
        /// 밀기 상태를 내 카메라에 반영한다. 소유자가 <b>매 프레임</b> 부른다.
        ///
        /// <b>이 로직이 GameSession 안에 있으면 검증이 잠그지 못한다.</b> 처음에는
        /// 거기에 두었는데, 검증 하네스가 같은 두 줄을 흉내내는 꼴이 되어
        /// <b>전환 조건을 <c>true</c> 로 바꿔도 22개가 전부 통과했다</b> — 배선이
        /// 검증 경로 밖이었다(S15P21B202-400). 그래서 여기로 옮겼다.
        ///
        /// 0.5초 복귀 유예를 여기서 세지 않는다 — 위 <see cref="RefreshHaulState"/>
        /// 주석에 이유를 적었다.
        /// </summary>
        public void SyncPushCamera(float deltaTime)
        {
            if (Cam == null || deltaTime <= 0f) return;

            // 조건이 Hauling 인 이유는 그 값이 **실제로 밀리는 중**만 참이기 때문이다.
            // 한두 명이 붙어 꿈쩍하지 않을 때 시점이 바뀌면 "시점은 바뀌었는데 아무
            // 일도 안 일어나는" 화면이 된다(기획 §10.2).
            Cam.SetPushView(Hauling, HaulDirection);
            Cam.TickPushView(HaulGrasp, HaulDirection, deltaTime);
        }

        /// <summary>
        /// 서버·혼자 실행에서 파츠 밀기 상태를 읽어 온다.
        ///
        /// 0.5초 유예를 여기서 따로 세지 않는다 — <c>PartPush.Gather</c> 가 이미
        /// <see cref="PhysicsScale.PartPushReleaseSeconds"/> 동안 인원을 유지하므로,
        /// 걸음을 멈춰도 <c>IsMoving</c> 이 0.5초간 참이다. 기획 §10.2 의 "밀기를
        /// 멈추거나 접촉이 끊기면 0.5초 뒤 1인칭으로 복귀한다" 가 그대로 나온다.
        /// </summary>
        void RefreshHaulState()
        {
            // 참가자 화면에서는 서버가 보낸 값을 쓴다. 여기서 조회하면 파츠가
            // kinematic 이라 늘 "밀고 있지 않다" 가 되어 서버 값을 덮어쓴다.
            if (WorldEvents.RemoteDecides) return;

            var push = Parts.PartPush.MovingPartFor(this);
            if (push == null)
            {
                ApplyHaulState(false, Vector3.zero, Vector3.zero, 0f);
                return;
            }

            ApplyHaulState(true, push.HaulDirection, push.GraspPointFor(this),
                           Parts.PartPushTable.SpeedFor(push.HaulerCount));
        }
        /// <summary>공중에서 자세제어 분사기를 쓰고 있는가 (HUD·연기 표시용).</summary>
        public bool Thrusting { get; private set; }
        public float AirTime { get; private set; }

        /// <summary>
        /// 골반이 실제로 오르내리는 속도(m/s). 위가 양수 (S15P21B202-628).
        ///
        /// <para><b>리지드바디 속도를 쓰지 않는다.</b> 남의 몸은
        /// <c>RemoteAvatar</c> 가 <c>MovePosition</c> 으로 옮기므로 그 값이 물리
        /// 결과가 아니다 — 원격에서는 엉뚱한 수가 나온다. 골반이 <b>실제로 움직인
        /// 거리</b>를 재면 내 몸이든 남의 몸이든 같은 방법으로 같은 답이 나오고,
        /// 새로 동기화할 것도 권위 문제도 없다.</para>
        ///
        /// <para>공중 자세가 이 값 하나로 정해진다 — 그래서 난간에서 떨어질 때도
        /// 점프할 때와 같은 자세가 그대로 쓰인다.</para>
        /// </summary>
        public float VerticalSpeed { get; private set; }
        float lastHipsY;
        bool hipsYKnown;

        /// <summary>제 발로 뛴 점프인가 (S15P21B202-628). 떨어진 것과 구별한다 — 최고점 표현이 다르다.</summary>
        public bool JumpedUp { get; private set; }

        /// <summary>
        /// 광역 저중력장 안에서 떠 있는가.
        ///
        /// 파츠는 저중력장이 힘으로 끌어당기지만 플레이어는 그러지 않는다. 기획
        /// (S15P21B202-107 §14.3)이 "플레이어는 중심으로 강제 견인되지 않고 직접 움직인다"
        /// 고 정했기 때문이다. 그래서 저중력장은 "여기가 영역이다" 만 알려 주고
        /// (<see cref="Parts.LowGravityField.FindContaining"/>) 실제 부유는 여기서 만든다.
        ///
        /// 인형에 힘을 주지 않고 속도를 직접 넣는 이유도 같다. 래그돌의 강체를 하나씩
        /// 띄우면 팔다리가 따로 떠서 자세가 무너진다. <see cref="DriveMovementVelocity"/> 가
        /// 이미 수평을 그렇게 다루고 있어 세로도 같은 방식으로 맞췄다.
        /// </summary>
        public bool Floating { get; private set; }
        public bool Swimming { get; private set; }
        public bool InWater => water != null;
        DrainageWater water;
        public Grabbable HeldTool { get { return toolHeld; } }
        // 서버의 장착/입력 권한과 별개인 화면 표시 연결. 참가자에게도 설정된다.
        public ToolBase PresentationTool { get; internal set; }
        /// <summary>현재 장착된 도구 동작 컴포넌트. 도구가 없거나 구형 Tool이면 null이다.</summary>
        public ToolBase ActiveTool { get { return activeTool; } }
        public float ToolCharge { get { return toolCharge; } }
        public bool ChargingTool { get { return chargingTool; } }
        /// <summary>현재 조준 중인 고정쇠를 찾고 해제 홀드를 진행하는 상호작용 주체.</summary>
        public Interactor ClampInteractor { get; private set; }

        /// <summary>
        /// 기절한 동료를 <b>일으키는</b> 홀드를 도는 인터랙터 (S15P21B202-390).
        ///
        /// <para><b>왜 따로 두는가.</b> <see cref="ClampInteractor"/> 와 나누지 않으면
        /// 둘이 서로의 대상을 점유한다 — 한 인터랙터는 홀드 하나만 들 수 있다
        /// (<c>InteractorState.Holding</c>). 고정쇠를 잡고 있는 동안 동료를 일으킬 수
        /// 없게 되는 것도 이유다.</para>
        ///
        /// <para><see cref="Interactor.Accepts"/> 로 <see cref="DownedRescue"/> 만 받게
        /// 해 둔다. 그러지 않으면 <b>고정쇠를 점유해 버린다</b> — 후보 탐색이
        /// <c>Interactable</c> 레이어의 아무 대상이나 잡고, <c>PartClamp</c> 는
        /// <c>HoldSeconds</c> 가 0 이라 그 자리에서 완료로 처리된다.</para>
        /// </summary>
        public Interactor RescueInteractor { get; private set; }

        /// <summary>
        /// 지금 겨냥한 사람을 다 일으키기까지의 진행도 0~1. HUD 가 이 값을 그린다.
        ///
        /// <para><b>인터랙터의 <c>HoldProgress</c> 가 아니라 <see cref="DownedRescue"/>
        /// 의 값이다</b> (<c>!297</c> AI 리뷰). 앞사람이 남긴 진행분이 포함돼야
        /// 이어받는 화면에서 게이지가 0 부터 다시 차 보이지 않는다. 세는 곳은 한
        /// 곳이어야 한다 — <c>-383</c> 에서 겪은 것이다.</para>
        /// </summary>
        public float RescueProgress01
        {
            get
            {
                var rescue = RescueFocus;
                return rescue != null ? rescue.Progress : 0f;
            }
        }

        /// <summary>지금 누르고 있는가. HUD 문구가 "일으키기" 와 "일으키는 중" 을 가른다.</summary>
        public bool RescueHolding =>
            RescueInteractor != null && RescueInteractor.State == InteractorState.Holding;

        /// <summary>지금 일으킬 수 있는 사람. 없으면 <c>null</c> — HUD 가 문구를 띄운다.</summary>
        public PlayerRig RescueCandidate => RescueFocus != null ? RescueFocus.Target : null;

        /// <summary>
        /// <b>남이 나를</b> 일으키고 있는 진행도 0~1 (S15P21B202-448).
        ///
        /// <para><see cref="RescueProgress01"/> 과 방향이 반대다 — 그쪽은 "내가 남을",
        /// 이쪽은 "남이 나를" 이다. 쓰러진 사람 화면에 게이지를 그리려면 이 값이
        /// 필요하고, <b>그 기계에는 그 값을 만들 방법이 없었다.</b> 진행분은 구조자의
        /// 인터랙터가 대상의 <see cref="DownedRescue"/> 에 쌓는데 그 일은 서버에서만
        /// 일어나고, 예전에는 그것을 실어 보내지 않았다.</para>
        ///
        /// <para>그래서 <b>서버가 계산하고 참가자는 받는다</b> — <see cref="Downed"/> 와
        /// 같은 처리다(<c>NetPlayer.rescueProgress</c>).</para>
        /// </summary>
        public float BeingRescuedProgress01 { get; private set; }

        /// <summary>
        /// 서버가 보낸 "남이 나를 일으키는 진행도" 를 받는다.
        ///
        /// <b>권위가 있는 몸에서는 무시한다.</b> 호스트·혼자 실행에서는
        /// <see cref="RefreshBeingRescued"/> 가 자기 <see cref="RescueTarget"/> 에서 직접
        /// 읽으므로, 여기서 덮으면 값이 두 곳에서 오게 된다 — <c>-383</c> 의
        /// "판정을 두 곳에 두면 반드시 갈라진다" 가 그대로 적용된다.
        /// </summary>
        public void SetRemoteBeingRescued(float progress01)
        {
            if (ServerAuthority) return;
            BeingRescuedProgress01 = Mathf.Clamp01(progress01);
        }

        /// <summary>
        /// 권위가 있는 몸에서 내 구조 진행도를 갱신한다.
        ///
        /// <b>지금 누가 붙들고 있을 때만 값을 낸다</b>(!356 AI 리뷰).
        ///
        /// <para><see cref="DownedRescue.Progress"/> 는 구조자가 손을 떼도 <b>남는다</b> —
        /// 다른 사람이 이어받게 하려고 일부러 그렇게 되어 있다(<c>Release</c>). 그 값을
        /// 그대로 내보내면 아무도 오지 않는 동안에도 쓰러진 사람 화면에
        /// "일으켜지는 중…" 이 계속 떠서, <b>이 이슈가 피하려던 거짓 표시</b>가 된다.</para>
        ///
        /// <para>그래서 이 값의 뜻을 <b>"지금 일으켜지고 있는 진행도"</b> 하나로 좁혔다.
        /// 남은 진행분은 <b>구조자 쪽</b>이 알아야 하는 정보고 그쪽 화면은 이미 그것을
        /// "이어서 일으키기" 로 보여준다 — 쓰러진 사람이 그것으로 할 수 있는 일은 없다.</para>
        /// </summary>
        void RefreshBeingRescued()
        {
            if (!ServerAuthority) return;   // 참가자는 SetRemoteBeingRescued 로 받는다

            BeingRescuedProgress01 = Downed && RescueTarget != null && SomeoneIsRescuingMe
                ? RescueTarget.Progress
                : 0f;
        }

        /// <summary>지금 누군가가 내 구조 지점을 붙들고 있는가.</summary>
        bool SomeoneIsRescuingMe =>
            RescueTarget != null
            && RescueTarget.Occupant != null
            && RescueTarget.Occupant.State == InteractorState.Holding;

        /// <summary>
        /// 지금 겨냥한(또는 붙들고 있는) 구조 지점. 홀드 중에는 <c>Focus</c> 가 아니라
        /// <c>Held</c> 를 봐야 한다 — 인터랙터는 홀드 중에 포커스를 갱신하지 않는다.
        /// </summary>
        DownedRescue RescueFocus
        {
            get
            {
                if (RescueInteractor == null) return null;

                var focus = RescueInteractor.State == InteractorState.Holding
                    ? RescueInteractor.Held : RescueInteractor.Focus;
                return focus as DownedRescue;
            }
        }
        /// <summary>
        /// 검증이 "고정못을 잡은 상태" 를 만들 때 쓴다 (S15P21B202-667 · -669).
        /// 조건이 손·대상·해제 여부까지 걸려 있어 조작 전체를 흉내내기 어렵다.
        /// </summary>
        public bool ClampGrabbedForTests { get; set; }

        /// <summary>실제로 한 손이 고정쇠에 결합된 상태.</summary>
        public bool ClampGrabbed
        {
            get
            {
                if (ClampGrabbedForTests) return true;
                return clampGrabbed && clampGrabHand != null && clampGrabTarget != null
                       && !clampGrabTarget.IsReleased
                       && clampGrabHand.Gripping
                       && clampGrabHand.Held == clampGrabTarget.GripTarget;
            }
        }

        /// <summary>HUD와 입력 처리가 공유하는 현재 화면 중심 상호작용 대상.</summary>
        public PlayerInteractionTarget CurrentInteractionTarget { get; private set; } =
            PlayerInteractionTarget.None;

        /// <summary>
        /// 지금 잡고 있는 고정못. 없으면 null. 연출과 검증이 쓴다.
        /// </summary>
        public PartClamp ClampGrabTarget { get { return clampGrabTarget; } }

        /// <summary>이번 틱에 고정못을 실제로 뒤로 당겼는가. 낑낑대는 연출이 붙을 신호다.</summary>
        public bool ClampPulling { get; private set; }
        /// <summary>
        /// 예전 조작의 호환용 이름. 현재는 실제 손 결합 여부를 반환한다.
        /// </summary>
        public bool ClampLiftReady { get { return ClampGrabbed; } }
        /// <summary>
        /// 잡고 있는 고정못이 얼마나 뽑혔는가 0~1.
        ///
        /// 예전에는 "마우스를 위로 얼마나 들었는가" 였다. 그 단계가 없어졌으므로
        /// 고정못의 진행분을 그대로 돌려준다.
        /// </summary>
        public float ClampLiftProgress
        {
            get { return clampGrabTarget != null ? clampGrabTarget.Progress : 0f; }
        }
        /// <summary>실제로 결합된 손의 버튼을 계속 누르고 있는가.</summary>
        public bool ClampReleaseHolding
        {
            get
            {
                if (!ClampGrabbed) return false;
                return clampGrabHand == LeftHand ? input.GrabLeft : input.GrabRight;
            }
        }

        public Hand LeftHand { get { return Doll.Left.Hand; } }
        public Hand RightHand { get { return Doll.Right.Hand; } }
        public Rigidbody Hips { get { return Doll.Hips; } }
        public Vector3 Position { get { return Doll.Hips.position; } }

        /// <summary>
        /// 살아 있는 플레이어. <see cref="Parts.PartSocket.All"/> 과 같은 방식이다.
        ///
        /// 방망이(<see cref="MeteorBat"/>)가 때릴 동료를 여기서 찾는다(기획 §18.3).
        /// 콜라이더를 훑지 않는 이유는 인형 하나가 <b>Rigidbody 여럿</b>이기 때문이다 —
        /// 몸통·팔·다리가 각자 콜라이더라 같은 사람을 여러 번 찾게 되고, 어느 부위가
        /// 맞았는지는 §18.3 이 구분하지 않는다.
        /// </summary>
        public static readonly List<PlayerRig> All = new();

        public enum HandReleaseReason
        {
            Release,
            Throw
        }

        public static event Action<PlayerRig, Hand, Grabbable> AnyHandGrabAttempted;
        public static event Action<PlayerRig, Hand, Grabbable, HandReleaseReason> AnyHandReleased;
        public static event Action<PlayerRig, Vector3, float> AnyFootstep;
        public static event Action<PlayerRig, Vector3> AnyJumped;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable() => All.Remove(this);
        public Vector3 FootPosition { get { return Doll.Hips.position - Vector3.up * RagdollBuilder.HipsY; } }

        InputFrame input;
        Vector3 faceDir = Vector3.forward;
        Vector2 movementDirectionLocal;
        float jumpLock;
        float regrabLock;
        float uprightBlend = 1f;
        float thrustPuff;
        float fallPeakFootHeight;

        /// <summary>부유하느라 인형의 중력을 꺼 둔 상태인가. 켜고 끄는 시점을 한 번씩만 만든다.</summary>
        bool floatingGravityOff;
        float floatingLiftTarget;
        bool floatingAutoLift;
        Grabbable toolHeld;
        float toolCharge;
        bool toolActionHeld;
        bool chargingTool;
        float toolThrowRegrabLock;
        Rigidbody toolBody;
        bool toolBodyHadGravity;
        ToolBase activeTool;
        bool previousToolPrimary;
        bool previousToolSecondary;
        bool previousToolUtility;
        bool suppressToolPrimary;
        bool suppressToolSecondary;
        bool suppressToolUtility;
        bool previousGrabLeft;
        bool previousGrabRight;
        bool clampGrabbed;
        PartClamp clampGrabTarget;
        Hand clampGrabHand;
        TransportVehicle[] interactionVehicles = System.Array.Empty<TransportVehicle>();
        Rigidbody pendingThrowBody;
        Vector3 pendingThrowVelocity;
        Rigidbody groundBody;
        // ── 좌석 붙잡기 (S15P21B202-434) ───────────────────────────
        //
        // 탑승 중 몸은 물리에서 빠져 탈것에 대한 상대 자세로만 움직인다.
        // 조인트를 걸지 않는 이유와 되돌리는 이유는 AttachTo·EndSeatHold 의 주석에 있다.
        bool seatHeld;
        Vector3[] seatLocalPositions;
        Quaternion[] seatLocalRotations;
        bool[] seatWasKinematic;
        bool[] seatWasColliding;
        Rigidbody ignoredHeldBodyA;
        Rigidbody ignoredHeldBodyB;
        readonly Dictionary<Rigidbody, CarriedBodyState> carriedBodies = new Dictionary<Rigidbody, CarriedBodyState>(4);
        readonly List<CollisionPair> heldObjectCollisionPairs = new List<CollisionPair>(24);

        /// <summary>
        /// 고정못을 잡고 있는 동안 파츠와 무시하는 충돌 쌍 (S15P21B202-667).
        /// <see cref="RefreshAnchoredPartCollision"/> 참고.
        /// </summary>
        readonly List<CollisionPair> anchoredPartCollisionPairs = new List<CollisionPair>(24);

        /// <summary>무시 쌍을 이미 지었는가. 매 틱 다시 만들지 않으려고 둔다.</summary>
        bool anchoredPartsBuilt;
        readonly RaycastHit[] rayHits = new RaycastHit[8];
        readonly Collider[] interactionCandidates = new Collider[32];
        readonly RaycastHit[] clampHits = new RaycastHit[ClampHitBufferSize];

        struct CarriedBodyState
        {
            public bool WasKinematic;
            public bool HadGravity;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public CollisionDetectionMode CollisionDetectionMode;
            public RigidbodyInterpolation Interpolation;
            public RigidbodyConstraints Constraints;
            public Vector3 FollowPosition;
            public bool FollowInitialized;
        }

        struct CollisionPair
        {
            public Collider Player;
            public Collider Held;
        }

        public static PlayerRig Create(string nickname, Vector3 footPos, Quaternion facing,
                                       Color suit, Color accent, bool useModel = true)
        {
            var go = new GameObject("플레이어 " + nickname);
            var rig = go.AddComponent<PlayerRig>();
            rig.Nickname = nickname;
            rig.TeamColor = accent;
            bool model = useModel && SpaceSuit.Available;
            if (!All.Contains(rig)) All.Add(rig);   // EditMode 에서는 OnEnable 이 돌지 않는다
            rig.Doll = RagdollBuilder.Build(nickname, footPos, facing, suit, accent, !model);
            rig.Doll.Root.SetParent(go.transform, true);

            // 잡을 지점을 태어날 때 만든다(S15P21B202-442). 예전에는 GoDown·Revive·
            // SetRemoteDowned 안에서만 만들어져서, 한 번도 기절한 적 없는 사람에게는
            // 지점이 아예 없었다 — 참가자 화면의 남의 몸도 마찬가지다.
            rig.RefreshCarryTarget();
            rig.ClampInteractor = go.AddComponent<Interactor>();
            rig.RescueInteractor = go.AddComponent<Interactor>();

            // 조준 기준점. 머리를 그대로 쓰면 안 된다 — 래그돌 머리는 물리로 흔들려서
            // 어디를 겨냥하는지가 사람의 의도와 무관해진다. 서버의 참가자 몸에는 카메라가
            // 없어 이 자리가 곧 조준이 되므로, 그때 아무 데도 겨냥할 수 없었다
            // (2026-09-02 실측 — 참가자만 상호작용이 되지 않았다).
            // 이 기준점은 AimOrigin·AimRotation 을 따라간다(카메라가 있으면 카메라, 없으면
            // 클라이언트가 보낸 각도). 내 화면에서는 SpaceBootstrap 이 카메라로 바꿔 끼운다.
            var aimPivot = new GameObject("~조준").transform;
            aimPivot.SetParent(go.transform, false);
            rig.aimPivot = aimPivot;
            rig.SyncAimPivot();
            rig.ConfigureClampInteraction(aimPivot);
            rig.ResetHealth();
            rig.fallPeakFootHeight = footPos.y;
            rig.faceDir = facing * Vector3.forward;
            return rig;
        }

        void OnDestroy()
        {
            All.Remove(this);

            if (activeTool != null) activeTool.Unequip();
            RestoreAllCarriedBodies();
            ClearHeldObjectOwnerCollision();

            // 잡고 있던 고정쇠의 당기기 자리를 반납한다 (S15P21B202-383).
            //
            // 이것이 없어서 접속이 끊기면 Pullers 가 줄지 않았다. 기획 §12.2 로
            // 고정쇠 하나에 최대 2명이므로 **한 번 새면 그 고정쇠는 딱 한 명만 더
            // 잡을 수 있고, 두 번 새면 아무도 못 잡는다.** 그리고 그 상태는
            // 되돌아오지 않는다 — 레벨을 다시 시작해야 한다.
            //
            // RestoreAllCarriedBodies() 옆이 자리다: 둘 다 "죽을 때 남에게 남긴
            // 상태를 되돌린다" 다. 진행분은 지우지 않는다(§12.1).
            ResetClampGrab();
        }

        public void SetInput(InputFrame frame)
        {
            input = frame;
        }

        /// <summary>카메라가 만들어진 뒤 고정쇠 조준 기준을 갱신한다.</summary>
        public void ConfigureClampInteraction(Transform aim)
        {
            if (ClampInteractor == null)
                ClampInteractor = gameObject.AddComponent<Interactor>();

            ClampInteractor.Configure(aim, ClampInteractionRange, ClampInteractionHalfAngle);

            // 살리기는 운반 후보를 고르는 것과 같은 사거리·시야각을 쓴다
            // (S15P21B202-390). 같은 사람을 겨냥하는 두 동작이므로 기준이 달라지면
            // "옮기기는 되는데 일으키기는 안 되는" 자리가 생긴다.
            if (RescueInteractor == null)
                RescueInteractor = gameObject.AddComponent<Interactor>();
            RescueInteractor.Configure(aim, PhysicsScale.DownedCarryReach,
                                       PhysicsScale.DownedCarryAngle);
            RescueInteractor.Accepts = OnlyDownedRescue;

            // **물리 질의만으로는 참가자 화면에서 아무도 못 찾는다**(S15P21B202-448).
            // 이유는 Interactor.ExtraCandidates 에 적었다. 잡기가 -249 이후로 목록을
            // 훑는 것과 같은 처리다.
            RescueInteractor.ExtraCandidates = CollectRescueCandidates;
        }

        /// <summary>운반 장치 후보를 단일 현장 포커스에 등록한다.</summary>
        public void ConfigureTransportInteractionTargets(TransportVehicle[] vehicles)
        {
            interactionVehicles = vehicles ?? System.Array.Empty<TransportVehicle>();
            RefreshInteractionFocus();
        }

        /// <summary>
        /// 현재 카메라 중심에서 실제로 조작할 수 있는 대상을 하나만 고른다.
        /// HUD가 별도 대상을 찾지 않도록 입력 직전에 갱신하고, 고정쇠를 이미 잡은 동안에는
        /// 그 대상을 잠근다.
        /// </summary>
        public void RefreshInteractionFocus()
        {
            if (Doll == null || Doll.Head == null || IsRiding || Downed)
            {
                CurrentInteractionTarget = PlayerInteractionTarget.None;
                return;
            }

            if (clampGrabbed)
            {
                if (ClampGrabbed)
                {
                    CurrentInteractionTarget = MakeTarget(
                        PlayerInteractionKind.Clamp, clampGrabTarget, null, null,
                        clampGrabTarget.transform.position, 0f,
                        Vector3.Distance(InteractionOrigin(), clampGrabTarget.transform.position));
                    return;
                }

                ResetClampGrab();
            }

            var origin = InteractionOrigin();
            var forward = InteractionForward();
            var best = PlayerInteractionTarget.None;

            // 직접 조준 우선 처리를 하기 전에도 Interactor의 포커스 상태를 갱신한다.
            // 그래야 직접 조준을 벗어난 다음 프레임에 이전 Clamp 포커스가 남지 않는다.
            ClampInteractor?.Tick(0f);

            // 파츠 탑승은 실제 크로스헤드 명중을 먼저 확정한다. 멀리 있는 기체도
            // 조준 대상은 유지하고 거리·연결 조건은 E 표시와 탑승 허용에만 사용한다.
            // 가장 앞의 충돌만 보므로 벽 뒤 기체나 주변의 조준 보정 후보로 넘어가지 않는다.
            if (Physics.Raycast(origin, forward, out var transportHit,
                TransportVehicle.InteractionFocusRange, RagdollBuilder.WorldMask,
                QueryTriggerInteraction.Ignore))
            {
                var vehicle = transportHit.collider.GetComponentInParent<TransportVehicle>();
                if (vehicle != null && vehicle.UsesPartBody && vehicle.isActiveAndEnabled
                    && !vehicle.IsReturning && System.Array.IndexOf(interactionVehicles, vehicle) >= 0)
                {
                    CurrentInteractionTarget = MakeTarget(PlayerInteractionKind.Transport,
                        null, null, vehicle, transportHit.point, 0f, transportHit.distance,
                        !vehicle.IsOccupied && vehicle.CanBoardFrom(this));
                    return;
                }
            }

            // 크로스헤드가 Clamp Collider를 직접 맞히면, 팔 주변의 일반 Grabbable 보정이
            // 옆 본체를 대신 고르지 못하게 한다. 실제로 잡을 수 없는 거리여도 후보를
            // Clamp로 고정해 노란 조준점은 유지하고, 클릭 문구만 숨긴다.
            var crosshairClamp = FindCrosshairClamp(origin, forward, out var crosshairPoint,
                                                    out var crosshairDistance);
            if (crosshairClamp != null)
            {
                var canInteract = crosshairClamp.GetAvailability(ClampInteractor).IsAllowed
                                  && CanReachClamp(crosshairClamp);
                CurrentInteractionTarget = MakeTarget(
                    PlayerInteractionKind.Clamp, crosshairClamp, null, null,
                    crosshairPoint, 0f, crosshairDistance, canInteract);
                return;
            }

            // Interactor의 고정쇠 탐색 규칙은 기존 테스트와의 호환을 위해 그대로 재사용한다.
            var clamp = ClampInteractor != null ? ClampInteractor.Focus as PartClamp : null;
            if (clamp != null && !clamp.IsReleased
                && (ClampInteractor == null || clamp.GetAvailability(ClampInteractor).IsAllowed))
            {
                if (!clamp.TryGetClosestColliderPoint(origin, out var point))
                    point = clamp.transform.position;

                Consider(ref best, MakeTarget(PlayerInteractionKind.Clamp, clamp, null, null,
                                              point, AngleFrom(origin, forward, point),
                                              Vector3.Distance(origin, point),
                                              CanReachClamp(clamp)));
            }

            FindCrosshairGrabbable(origin, forward, ref best);
            FindTeammateGrabPoint(origin, forward, ref best);

            // 카메라가 있을 때만 보던 자리다. 그러면 서버에 선 참가자의 몸은 카메라가 없어
            // 운반체를 영영 후보로 잡지 못하고, 참가자만 탑승이 되지 않는다(2026-09-03 실측).
            // 아래는 origin·forward 만 쓰므로 카메라가 없어도 그대로 성립한다.
            {
                for (var i = 0; i < interactionVehicles.Length; i++)
                {
                    var vehicle = interactionVehicles[i];
                    if (vehicle == null || vehicle.UsesPartBody
                        || !vehicle.isActiveAndEnabled || vehicle.IsReturning
                        || !vehicle.CanBoardFrom(this)) continue;

                    var point = vehicle.transform.position
                                + Vector3.up * TransportVehicle.InteractionFocusHeight;
                    var distance = Vector3.Distance(origin, point);
                    if (distance > TransportVehicle.InteractionFocusRange) continue;

                    var angle = AngleFrom(origin, forward, point);
                    if (angle > TransportVehicle.InteractionFocusHalfAngle) continue;

                    Consider(ref best, MakeTarget(PlayerInteractionKind.Transport, null, null,
                                                  vehicle, point, angle, distance));
                }
            }

            CurrentInteractionTarget = best;
        }

        // 조준 창구를 쓴다(S15P21B202-149). 카메라가 있으면 지금까지와 같은 값이고,
        // 카메라가 없는 서버에서는 클라이언트가 입력에 실어 보낸 각도가 답을 낸다.
        // 머리 방향으로 대신하면 서버에서 엉뚱한 것을 조준한다 — 머리는 조준을
        // 뒤늦게 따라오는 관절이라 실제로 보는 곳과 어긋난다.
        Vector3 InteractionOrigin() => AimOrigin;

        Vector3 InteractionForward() => AimForward;

        /// <summary>
        /// 화면 중심 광선이 실제 Clamp Collider를 맞혔는지 확인한다.
        ///
        /// 넓은 OverlapSphere 탐색은 크로스헤드 색상과 후보 발견을 위한 보정으로 남긴다.
        /// 반면 이 광선에 맞은 Clamp는 플레이어가 명시적으로 바라본 대상으로 취급해
        /// 주변 본체 Grabbable이 거리 우선으로 끼어들지 못하게 한다.
        /// </summary>
        PartClamp FindCrosshairClamp(Vector3 origin, Vector3 forward,
                                     out Vector3 point, out float distance)
        {
            point = Vector3.zero;
            distance = float.MaxValue;

            if (forward.sqrMagnitude < 1e-6f) return null;

            var hitCount = Physics.SphereCastNonAlloc(
                origin, ClampCrosshairAssistRadius, forward.normalized, clampHits,
                ClampInteractionRange, GameLayers.InteractableMask,
                QueryTriggerInteraction.Collide);

            PartClamp bestClamp = null;
            var bestDistance = float.MaxValue;
            var bestPoint = Vector3.zero;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = clampHits[i];
                var candidate = hit.collider != null
                    ? hit.collider.GetComponentInParent<PartClamp>() : null;
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsReleased)
                    continue;

                if (hit.distance >= bestDistance) continue;

                bestClamp = candidate;
                bestDistance = hit.distance;
                bestPoint = hit.point;
            }

            if (bestClamp == null)
                return null;

            point = bestPoint;
            distance = bestDistance;
            return bestClamp;
        }

        void FindCrosshairGrabbable(Vector3 origin, Vector3 forward,
                                    ref PlayerInteractionTarget best)
        {
            var center = origin + forward * Tuning.ArmReach;
            var count = Physics.OverlapSphereNonAlloc(center, Tuning.GrabAssistRange,
                                                       interactionCandidates,
                                                       RagdollBuilder.WorldMask,
                                                       QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var collider = interactionCandidates[i];
                if (collider == null) continue;

                var grabbable = collider.GetComponentInParent<Grabbable>();
                if (grabbable == null || !grabbable.isActiveAndEnabled || !grabbable.CanGrab
                    || grabbable == toolHeld)
                    continue;

                // 도구 보정 반경은 손끝 중심의 작은 구가 아니라 조준선 주위의 폭이다.
                // 기존 구 판정은 정면의 도구도 카메라에서 약 0.49m를 넘으면 탈락시켰다.
                var isSpecial = grabbable.IsTool;
                var probePoint = center;
                if (isSpecial)
                {
                    var depth = Mathf.Clamp(Vector3.Dot(collider.bounds.center - origin, forward),
                                            0f, Tuning.ArmReach + Tuning.GrabAssistRange);
                    probePoint = origin + forward * depth;
                }
                var pointReach = isSpecial ? Tuning.SpecialGrabAssistRadius : Tuning.GrabAssistRange;
                if (!Hand.TryGetGripPoint(collider, probePoint, pointReach, out var point)) continue;
                var centerDistance = Vector3.Distance(center, point);
                var distance = Vector3.Distance(origin, point);
                var angle = AngleFrom(origin, forward, point);
                var assistRadius = isSpecial ? Tuning.SpecialGrabAssistRadius
                                              : Tuning.GrabAssistRange;
                var assistAngle = isSpecial ? Tuning.SpecialGrabAssistAngle
                                             : Tuning.GrabAssistAngle;
                var assistDistance = isSpecial ? Vector3.Distance(probePoint, point) : centerDistance;
                if (assistDistance > assistRadius || angle > assistAngle) continue;
                if (isSpecial && distance > Tuning.ArmReach + Tuning.GrabAssistRange) continue;

                Consider(ref best, MakeTarget(PlayerInteractionKind.Grabbable, null, grabbable,
                                              null, point, angle, distance));
            }
        }

        static PlayerInteractionTarget MakeTarget(PlayerInteractionKind kind, PartClamp clamp,
                                                   Grabbable grabbable, TransportVehicle vehicle,
                                                   Vector3 point, float angle, float distance,
                                                   bool canInteract = true) =>
            new PlayerInteractionTarget(kind, clamp, grabbable, vehicle, point, angle, distance,
                                        canInteract);

        /// <summary>
        /// 탐색 포커스가 된 고정못을 실제로 클릭할 수 있는지 판정한다.
        ///
        /// Interactor의 넓은 반경/각도 보정은 탐색 피드백용으로만 남긴다. Clamp는
        /// 본체 아래에 배치되어 어깨-Collider 직선 거리로만 검사하면 실제 손이 닿을
        /// 수 있는 위치도 탈락할 수 있으므로, 직접 조준된 Clamp에 한해 골반에서 실제
        /// Collider 표면까지의 직렬화된 거리만 클릭 보정으로 사용한다.
        /// </summary>
        bool CanReachClamp(PartClamp clamp)
        {
            if (clamp == null || Doll == null) return false;

            var left = Doll.Left;
            var right = Doll.Right;
            return left != null && CanReachClampHand(clamp, left, left.Hand, out _)
                   || right != null && CanReachClampHand(clamp, right, right.Hand, out _);
        }

        bool CanReachClampHand(PartClamp clamp, Arm arm, Hand hand, out Vector3 point)
        {
            point = Vector3.zero;
            if (clamp == null || arm == null || hand == null || hand.Gripping || hand.Carrying)
                return false;

            var shoulder = arm.ShoulderAnchor != null
                ? arm.ShoulderAnchor.position
                : Doll != null && Doll.Chest != null ? Doll.Chest.position : Vector3.positiveInfinity;
            var handOrigin = hand.Body != null ? hand.Body.position : shoulder;
            if (!IsFinite(handOrigin)) return false;

            // 실제 손 위치에서 가장 가까운 Collider 표면을 그립 지점으로 사용한다.
            // Clamp는 일반 Hand.Search(트리거 무시) 대신 이 명시적 경로로만 결합된다.
            if (!clamp.TryGetClosestColliderPoint(handOrigin, out point)) return false;

            if (Doll == null || Doll.Hips == null) return false;

            // Position은 플레이어 골반(RagdollBuilder.HipsY) 기준이다. Inspector에서 이
            // 값을 줄이면 접근을 더 엄격하게, 늘리면 플레이어 중심 기준 제한을 완화할 수 있다.
            var playerDistance = point - Doll.Hips.position;
            var maxPlayerDistance = Mathf.Max(0f, clampGrabDistanceFromPlayer);
            if (playerDistance.sqrMagnitude > maxPlayerDistance * maxPlayerDistance)
                return false;

            // 이 지점까지는 직접 조준한 실제 Clamp Collider와 플레이어 거리로 이미
            // 검증했으므로, 어깨 기준 팔 길이 검사를 다시 적용하지 않는다. 본체 아래의
            // Clamp를 잡을 때 어깨 높이/각도 때문에 클릭이 막히는 것을 방지한다.
            return true;
        }

        static float AngleFrom(Vector3 origin, Vector3 forward, Vector3 point)
        {
            var offset = point - origin;
            return offset.sqrMagnitude < 1e-6f ? 0f : Vector3.Angle(forward, offset);
        }

        static void Consider(ref PlayerInteractionTarget current,
                             PlayerInteractionTarget candidate)
        {
            if (!candidate.IsValid) return;
            if (current.Kind == PlayerInteractionKind.None || IsBetter(candidate, current))
                current = candidate;
        }

        static bool IsBetter(PlayerInteractionTarget candidate, PlayerInteractionTarget current)
        {
            const float epsilon = 0.01f;
            if (candidate.Angle < current.Angle - epsilon) return true;
            if (candidate.Angle > current.Angle + epsilon) return false;
            if (candidate.Distance < current.Distance - epsilon) return true;
            if (candidate.Distance > current.Distance + epsilon) return false;

            var comparison = ComparePosition(candidate.Point, current.Point);
            if (comparison != 0) return comparison < 0;
            return candidate.Kind < current.Kind;
        }

        static int ComparePosition(Vector3 left, Vector3 right)
        {
            var comparison = left.x.CompareTo(right.x);
            if (comparison != 0) return comparison;

            comparison = left.y.CompareTo(right.y);
            return comparison != 0 ? comparison : left.z.CompareTo(right.z);
        }

        static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        public InputFrame CurrentInput { get { return input; } }

        /// <summary>체크포인트 부활이나 새 라운드에서 체력을 초기화한다.</summary>
        public void ResetHealth()
        {
            Health = MaxHealth;
        }

        /// <summary>
        /// 피해 번쩍임을 식힌다 (S15P21B202-539).
        ///
        /// <para>매 고정틱 <c>Time.fixedDeltaTime</c> 을 적산한다 — <see cref="TickRescue"/> ·
        /// <see cref="TickTossed"/> 와 같은 규칙이다. 시간을 인수로 받지 않는 이유도 같다.</para>
        ///
        /// <para>0 에 닿으면 <c>GameHud.HealthFrameStrength</c> 가 테두리를 그리지 않는다.
        /// <see cref="Mathf.MoveTowards"/> 라 0 을 지나치지 않는다 — 음수가 되면
        /// 테두리 두께 보간이 뒤집힌다.</para>
        /// </summary>
        void TickDamageFlash()
        {
            if (DamageFlash01 <= 0f) return;

            DamageFlash01 = Mathf.MoveTowards(DamageFlash01, 0f,
                                              Time.fixedDeltaTime / Tuning.DamageFlashSeconds);
        }

        /// <summary>
        /// 외부 피해 오브젝트와 낙하 판정이 공통으로 사용하는 피해 진입점.
        ///
        /// <para><b>판정은 서버가 한다</b>(S15P21B202-513). 예전에는 권한 게이트가 없어서
        /// 참가자 기계의 트리거(<c>DamageSource</c> · <c>Stage1DustStormFragment</c>)가
        /// 자기 몸에 닿으면 <b>로컬에서만 체력이 깎였다</b> — 실측으로
        /// <c>ServerAuthority=False</c> 인데 <c>Health01</c> 이 1.000 → 0.700 이 됐다.</para>
        ///
        /// <para>체력을 전송하기 시작하면 그 값과 <b>싸운다</b> — 서버 값이 내려오는데
        /// 로컬에서도 깎으면 화면이 깜빡인다. <c>-383</c> 의 "판정을 두 곳에 두면 반드시
        /// 갈라진다" 이고, <b>전송과 이 게이트는 짝이다.</b></para>
        ///
        /// <para>혼자 실행·호스트에서는 <see cref="ServerAuthority"/> 가 참이라 그대로
        /// 돈다.</para>
        /// </summary>
        public void ApplyDamage(float amount, string source = "피해")
        {
            if (!ServerAuthority) return;
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (IsDead || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;

            Health = Mathf.Clamp(Health - amount, 0f, MaxHealth);
            if (Health > 0f) return;

            GoDown(source);
        }

        /// <summary>
        /// 그 자리에 쓰러진다. 순간이동하지 않는다.
        ///
        /// <see cref="GameLayers.PlayerDowned"/> 로 옮긴다. 그 레이어는 이미 만들어져
        /// 있었고 <see cref="RagdollBuilder"/> 와 <see cref="Parts.TransportVehicle"/> 의
        /// 충돌 마스크가 그것을 전제한다 — 다운을 그 레이어로 표현하는 것이 설계 의도다.
        ///
        /// 손에 든 것과 잡고 있던 것을 모두 놓는다. 쓰러진 사람이 고정못을 계속 잡고
        /// 있으면 다른 사람이 그 고정못에 붙을 수 없다(정원 §12.2).
        /// </summary>
        public void GoDown(string source = "기절")
        {
            if (Downed) return;

            Downed = true;
            Health = 0f;
            Ragdolled = true;
            uprightBlend = 0f;

            DropEquipment();

            SetDollLayer(GameLayers.PlayerDowned);
            RefreshRescueTarget();
            RefreshCarryTarget();

            // 시야가 몸과 함께 넘어간 뒤 3인칭으로 물러난다(기획 §10.1 · §10.3,
            // S15P21B202-259). 규칙은 RefreshDownedCamera 한 곳에 있다.
            RefreshDownedCamera();

            // 시점이 바뀌었으니 몸을 그릴지 다시 정한다(S15P21B202-267).
            RefreshDollVisibility();

            Debug.Log("[NotThisPlanet] " + Nickname + " 기절 (" + source + ")");
        }

        /// <summary>
        /// 들고 있던 것과 잡고 있던 것을 모두 놓는다.
        ///
        /// <b>기절과 분리해 둔다.</b> 기획 §20.1 의 "강한 충격: 크게 날아가고 들고 있던
        /// 장비를 떨어뜨림" 은 기절이 아니다 — 예전에는 이 다섯 줄이
        /// <see cref="GoDown"/> 안에만 있어서 <b>기절하지 않으면 장비를 떨어뜨릴 수가
        /// 없었다</b>(S15P21B202-247).
        ///
        /// 고정못은 손만 뗀다. <see cref="ResetClampGrab"/> 이 진행분을 남기므로
        /// §12.1 "운석에 맞아도 빠진 정도는 유지된다" 가 지켜진다.
        /// </summary>
        public void DropEquipment()
        {
            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            ClampInteractor?.Cancel();
            ResetClampGrab();
            ResetToolState();
        }

        /// <summary>
        /// 다시 일어난다. 동료 구조와 전원 다운 복귀가 함께 쓴다.
        /// </summary>
        public void Revive(float health)
        {
            if (!Downed) return;

            Downed = false;
            Health = Mathf.Clamp(health, 1f, MaxHealth);
            Ragdolled = true;      // 곧 Balance 가 일으켜 세운다

            SetDollLayer(GameLayers.Player);
            RefreshRescueTarget();
            RefreshCarryTarget();

            // 기획 §10.3 "구조되면 1인칭으로 돌아온다".
            RefreshDownedCamera();

            RefreshDollVisibility();
        }

        /// <summary>
        /// 구조 상호작용 대상. 다운한 동안만 켜진다.
        ///
        /// 인형의 몸은 Player/PlayerDowned 레이어라 <see cref="Interactor"/> 의 탐색
        /// (Interactable 레이어)에 걸리지 않는다. 그래서 전용 트리거를 하나 둔다 —
        /// 몸의 레이어를 바꾸면 충돌 매트릭스가 함께 흔들린다.
        /// </summary>
        public DownedRescue RescueTarget { get; private set; }

        void RefreshRescueTarget()
        {
            if (Doll == null || Doll.Hips == null) return;

            if (RescueTarget == null)
            {
                var go = new GameObject("구조 지점") { layer = GameLayers.Interactable };
                go.transform.SetParent(Doll.Hips.transform, false);

                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = RescueTriggerRadius;
                sphere.isTrigger = true;

                RescueTarget = go.AddComponent<DownedRescue>();
                RescueTarget.Configure(this);
            }

            if (!Downed) RescueTarget.ResetProgress();
            RescueTarget.gameObject.SetActive(Downed);
        }

        /// <summary>
        /// 운반 대상(기획 §20.4). 다운한 동안만 켜진다.
        ///
        /// <see cref="RescueTarget"/> 과 같은 방식이다 — 인형의 몸을 건드리지 않고 자식
        /// 하나를 둔다. "몸의 레이어를 바꾸면 충돌 매트릭스가 함께 흔들린다".
        ///
        /// <b>콜라이더를 두지 않는다.</b> 찾는 것은 물리 질의가 아니라
        /// <see cref="FindDownedTeammate"/> 가 <see cref="All"/> 을 훑어서 한다 —
        /// 조준 탐색 마스크(<see cref="RagdollBuilder.WorldMask"/>)는 Player ·
        /// PlayerDowned · Interactable 을 <b>모두</b> 빼고 트리거도 무시하므로 콜라이더로는
        /// 어차피 걸리지 않는다. 마스크를 열면 고정못(파츠의 자식이다)이 파츠의 잡기
        /// 지점으로 딸려 들어온다(S15P21B202-249).
        /// </summary>
        public Grabbable CarryTarget { get; private set; }

        /// <summary>
        /// 지금 누군가가 나를 잡고 있는가.
        ///
        /// <see cref="Grabbable.HandCount"/> 를 그대로 쓴다 — 내 잡을 지점을 잡을 수
        /// 있는 것은 <b>남뿐이다</b>(<see cref="FindTeammateGrabPoint"/> 가
        /// <c>other == this</c> 를 거른다). 따로 세어 둘 이유가 없다.
        /// </summary>
        public bool GrabbedByOthers =>
            CarryTarget != null && CarryTarget.isActiveAndEnabled && CarryTarget.HandCount > 0;

        void RefreshCarryTarget()
        {
            if (Doll == null || Doll.Hips == null) return;

            if (CarryTarget == null)
            {
                var go = new GameObject("잡을 지점") { layer = GameLayers.Interactable };
                go.transform.SetParent(Doll.Hips.transform, false);

                CarryTarget = go.AddComponent<Grabbable>();
                CarryTarget.Role = GrabbableRole.Transport;
                CarryTarget.PhysicsGrip = true;   // 도구처럼 몸 앞에 붙지 않게 (§20.4)
            }

            // 이름만 상태를 따라간다. HUD 가 이 문구를 그대로 보여준다.
            CarryTarget.Label = Downed ? "기절한 동료" : "동료";

            // **항상 켜 둔다**(S15P21B202-442). 예전에는 Downed 일 때만 켰다 —
            // §20.4 의 제목이 "기절한 동료 운반" 이라 그 범위로 읽었기 때문이다.
            // 살아있는 사람도 잡을 수 있어야 한다: 세계의 다른 물건은 전부 잡아서
            // 밀고 당길 수 있는데 사람만 안 되는 것은 규칙으로 설명되지 않는다.
            //
            // 일어나는 순간 손을 놓게 하던 것도 뺐다 — 잡은 채로 일어나는 것은 이제
            // 정상이다. 그 정리가 있던 이유는 "멀쩡한 사람은 못 잡는다" 였다.
            CarryTarget.gameObject.SetActive(true);
        }

        /// <summary>
        /// 이 대상을 잡고 있는 모든 손을 놓게 한다.
        ///
        /// <b>컴포넌트를 끄는 것만으로는 이미 만들어진 <c>FixedJoint</c> 가 사라지지
        /// 않는다.</b> 몸이 갑자기 다른 자리로 가는 경로(순간이동·탑승)에서 불러야
        /// 한다 — 그러지 않으면 조인트가 남아 몸을 도로 끌어당긴다.
        /// </summary>
        static void ReleaseCarriersOf(Grabbable target)
        {
            if (target == null || target.HandCount <= 0) return;

            for (var i = 0; i < All.Count; i++)
            {
                var rig = All[i];
                if (rig == null || rig.Doll == null) continue;   // LeftHand 가 Doll 을 탄다

                if (rig.LeftHand != null && rig.LeftHand.Held == target) rig.ReleaseHand(rig.LeftHand);
                if (rig.RightHand != null && rig.RightHand.Held == target) rig.ReleaseHand(rig.RightHand);
            }
        }

        /// <summary>
        /// 지금 동료를 잡고 끌고 있는가(기획 §20.4 · S15P21B202-442).
        ///
        /// <b>잡은 것이 사람인지까지 확인한다.</b> <see cref="Grabbable.PhysicsGrip"/>
        /// 만 보면 사람이 아닌 물체에도 참이 되어 이동 감속이 엉뚱한 데 걸린다. 지금은
        /// 그 깃발을 켜는 곳이 잡을 지점 하나뿐이라 증상이 없지만, <b>이름이
        /// "Teammate" 인데 판정은 아무 물체나 받는 상태</b>를 그대로 둘 이유가 없다
        /// (!352 AI 리뷰).
        /// </summary>
        public bool CarryingTeammate =>
            OwnerOfHeldGrabPoint(LeftHand) != null || OwnerOfHeldGrabPoint(RightHand) != null;

        /// <summary>
        /// 그 동료가 <b>기절한</b> 사람인가(기획 §20.4).
        ///
        /// <see cref="CarryingTeammate"/> 와 갈라 둔다 — 살아있는 사람도 잡을 수 있게
        /// 되면서 예전의 <c>CarryingDownedTeammate</c> 하나로는 두 경우를 구분할 수
        /// 없어졌다. 이름이 거짓이 되는 것을 그냥 두지 않는다.
        /// </summary>
        public bool CarryingDownedTeammate =>
            OwnerOfHeldGrabPoint(LeftHand)?.Downed == true
            || OwnerOfHeldGrabPoint(RightHand)?.Downed == true;

        static bool IsCarryPoint(Hand hand) =>
            hand != null && hand.Held != null && hand.Held.PhysicsGrip;

        /// <summary>이 손이 잡고 있는 것이 누구의 잡을 지점인가. 아니면 null.</summary>
        static PlayerRig OwnerOfHeldGrabPoint(Hand hand)
        {
            if (!IsCarryPoint(hand)) return null;

            for (var i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].CarryTarget == hand.Held) return All[i];

            return null;
        }

        // ── 동료 던지기 (기획 §20.4 · S15P21B202-519) ───────────────────────

        /// <summary>
        /// 지금 <b>양손으로</b> 잡고 있어서 던질 수 있는 동료. 아니면 null.
        ///
        /// <para><b>양손이어야 한다.</b> 동작 자체가 양손으로 허리를 잡아 머리 위로
        /// 넘기는 것이라(<c>Rabbit_TossUp</c>) 한 손으로 되면 그림과 규칙이 어긋난다.
        /// 도구도 같은 규칙을 쓴다 — "도구는 반드시 양손이 같은 대상을 잡고 있어야
        /// 한다".</para>
        /// </summary>
        public PlayerRig TossVictim
        {
            get
            {
                var left = OwnerOfHeldGrabPoint(LeftHand);
                return left != null && left == OwnerOfHeldGrabPoint(RightHand) ? left : null;
            }
        }

        /// <summary>
        /// 던지기 충전 진행(0~1). HUD 가 도구 충전과 같은 자리에 그린다.
        ///
        /// <para><b>서버만 이 값을 만든다.</b> 참가자 화면의 값은
        /// <see cref="SetRemoteTossCharge"/> 로 내려온다 — <c>-448</c> 의
        /// <c>rescueProgress</c> 와 같은 판단이다. 충전을 참가자가 스스로 세게 하면
        /// 두 곳에서 정하는 것이 되고(<c>-383</c>), 무엇보다 <b>참가자 기계에 그 조인트가
        /// 있는지에 기대게 된다</b> — 잡기 판정은 서버가 하므로 믿을 근거가 없다.</para>
        /// </summary>
        public float TossCharge01 { get { return tossCharge; } }

        /// <summary>지금 동료를 던지려고 충전 중인가. HUD 가 게이지를 그릴지 정한다.</summary>
        public bool ChargingToss => tossCharge > 0f;

        /// <summary>
        /// 서버가 센 충전 진행을 받는다 (S15P21B202-519). 권위가 있는 몸은 무시한다 —
        /// <see cref="SetRemoteHealth"/> · <see cref="SetRemoteBeingRescued"/> 와 같은 규칙.
        /// </summary>
        public void SetRemoteTossCharge(float nowCharge)
        {
            if (ServerAuthority) return;

            tossCharge = Mathf.Clamp01(nowCharge);
        }

        float tossCharge;
        bool tossActionHeld;
        PlayerRig pendingTossVictim;

        /// <summary>넘어가는 빠르기를 정할 충전량 (S15P21B202-551).</summary>
        float pendingTossCharge = 1f;
        Vector3 pendingTossVelocity;

        /// <summary>
        /// 충전한 만큼의 세기. <b>0 에서 시작하지 않는다</b> — 문턱만 넘겨 툭 던져도
        /// 사람이 떠야 한다. 완전히 비례시키면 짧게 누른 던지기가 제자리 놓기와
        /// 구별되지 않는다.
        /// </summary>
        public static float TossStrength(float charge01) =>
            Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(charge01));

        /// <summary>
        /// 던져지는 속도. <b>위가 크고 앞이 작다</b> — "높으면서 살짝 앞으로".
        ///
        /// <para>앞 성분은 <b>수평으로 눕힌다.</b> 조준을 그대로 쓰면 하늘을 보고 던질 때
        /// 위 성분이 두 번 더해져 천장까지 날아가고, 땅을 보고 던지면 서로 상쇄되어
        /// 제자리에 떨어진다.</para>
        /// </summary>
        public static Vector3 TossVelocity(Vector3 aim, float charge01)
        {
            var forward = aim;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.zero;

            var strength = TossStrength(charge01);
            return Vector3.up * (Tuning.TossUpSpeed * strength)
                   + forward * (Tuning.TossForwardSpeed * strength);
        }

        // ── 동료를 시선으로 끌기 (S15P21B202-555) ──────────────────

        /// <summary>
        /// 지금 들고 있는가(끌기가 아니라). <b>히스테리시스가 있다</b> — 들어가는 문턱과
        /// 나오는 문턱이 다르다.
        ///
        /// <para>문턱이 하나면 그 경계에서 고개가 조금만 흔들려도 몸이 떴다 내렸다 한다.
        /// 저중력장이 <c>-538</c> 에서 겪은 떨림과 원인이 다르지만 보기에는 같다.</para>
        /// </summary>
        public static bool TeammateLifted(float aimUp, bool wasLifted) =>
            wasLifted ? aimUp > PhysicsScale.TeammateLiftExit
                      : aimUp > PhysicsScale.TeammateLiftEnter;

        /// <summary>
        /// 끌어올 높이(m, 잡은 사람의 발 기준). 고개를 내렸으면 땅에 끌리는 높이,
        /// 올렸으면 <b>올려다볼수록 더 높이</b>.
        /// </summary>
        public static float TeammateHoldHeight(float aimUp, bool lifted)
        {
            if (!lifted) return PhysicsScale.TeammateDragHeight;

            var t = Mathf.InverseLerp(PhysicsScale.TeammateLiftEnter, 1f, aimUp);
            return Mathf.Lerp(PhysicsScale.TeammateLiftHeightLow,
                              PhysicsScale.TeammateLiftHeightHigh, t);
        }

        /// <summary>
        /// 끌어올 자리. 잡은 사람의 <b>발</b>에서 조준의 수평 방향으로 떨어진 곳이다.
        ///
        /// <para><b>발을 기준으로 잡는다.</b> 눈이나 골반에서 재면 잡은 사람이 점프하거나
        /// 경사에 서는 것만으로 동료의 높이가 따라 흔들린다 — "고개 높이로 정한다" 는
        /// 규칙이 무너진다.</para>
        /// </summary>
        public static Vector3 TeammateHoldPoint(Vector3 holderFoot, Vector3 aim, float height)
        {
            var forward = aim;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            return holderFoot + forward * PhysicsScale.TeammateHoldDistance
                   + Vector3.up * height;
        }

        /// <summary>
        /// 끌어오는 가속. 스프링-댐퍼이고 상한이 있다.
        ///
        /// <para><c>ForceMode.Acceleration</c> 으로 쓸 값이라 질량과 무관하다 — 저중력장이
        /// 같은 이유로 그렇게 한다. 상한이 없으면 두 몸이 서로 밀며 튄다.</para>
        /// </summary>
        public static Vector3 TeammatePull(Vector3 from, Vector3 to, Vector3 velocity, float scale)
        {
            var accel = (to - from) * PhysicsScale.TeammatePullAccel
                        - velocity * PhysicsScale.TeammatePullDamp;

            return Vector3.ClampMagnitude(accel * scale, PhysicsScale.TeammatePullMaxAccel);
        }

        /// <summary>
        /// 잡힌 쪽이 <b>반대로 버티고</b> 있는가. 옆으로 걷는 정도로는 저항이 아니다.
        /// </summary>
        public static bool TeammateResisting(Vector3 victimWish, Vector3 pullDirection)
        {
            if (victimWish.sqrMagnitude < 0.01f) return false;

            var pull = pullDirection;
            pull.y = 0f;
            if (pull.sqrMagnitude < 1e-4f) return false;

            var wish = victimWish;
            wish.y = 0f;
            if (wish.sqrMagnitude < 1e-4f) return false;

            return Vector3.Dot(wish.normalized, pull.normalized) < PhysicsScale.TeammateResistDot;
        }

        /// <summary>들고 있는가. 표시와 검증이 읽는다.</summary>
        public bool CarryLifted { get; private set; }

        /// <summary>잡힌 동료가 버틴 시간(초). <see cref="PhysicsScale.TeammateResistSeconds"/> 에서 손이 풀린다.</summary>
        public float CarryResistSeconds { get; private set; }

        /// <summary>
        /// 잡은 동료를 조준 방향으로 끌어온다 (S15P21B202-555).
        ///
        /// <para><b>서버만 한다.</b> 잡기 판정이 서버에 있으므로 참가자 기계의 내 손에 그
        /// 조인트가 있다고 믿을 근거가 없다 — 충전 게이지가 <c>-519</c> 에서 같은 판단을
        /// 했다. 조준은 <c>CmdSendInput</c> 으로 이미 올라오므로 <b>추가 전송이 없다</b>.</para>
        /// </summary>
        void PullTeammate(PlayerRig victim, Vector3 aim)
        {
            if (victim == null || victim.Doll == null || victim.Doll.Hips == null) return;

            var aimUp = aim.sqrMagnitude > 1e-4f ? aim.normalized.y : 0f;
            CarryLifted = TeammateLifted(aimUp, CarryLifted);

            var target = TeammateHoldPoint(FootPosition, aim,
                                           TeammateHoldHeight(aimUp, CarryLifted));

            var hips = victim.Doll.Hips;
            var pullDirection = target - hips.position;

            if (TeammateResisting(victim.MoveWish, pullDirection))
                CarryResistSeconds += Time.fixedDeltaTime;
            else
                CarryResistSeconds = 0f;

            // 버티는 데 끝이 있어야 한다. 없으면 잡힌 쪽이 빠져나갈 방법이 사라진다.
            if (CarryResistSeconds >= PhysicsScale.TeammateResistSeconds)
            {
                ReleaseHand(LeftHand, HandReleaseReason.Throw);
                ReleaseHand(RightHand, HandReleaseReason.Throw);
                CarryResistSeconds = 0f;
                CarryLifted = false;
                return;
            }

            var scale = CarryResistSeconds > 0f ? PhysicsScale.TeammateResistPullScale : 1f;
            var pull = TeammatePull(hips.position, target, hips.linearVelocity, scale);
            if (!JointTools.IsFinite(pull)) return;

            // 골반과 가슴에 나눠 준다. 골반에만 주면 상체가 뒤로 끌리며 접힌다 —
            // 잡힌 쪽 이동이 이미 같은 비율(0.6 / 0.4)을 쓴다.
            hips.AddForce(pull * 0.6f, ForceMode.Acceleration);
            if (victim.Doll.Chest != null)
                victim.Doll.Chest.AddForce(pull * 0.4f, ForceMode.Acceleration);
        }

        /// <summary>
        /// 양손으로 동료를 잡고 있는 동안의 Q 처리. 처리했으면 참을 돌려 팔 구동을 넘긴다.
        ///
        /// <para><b>판정은 서버만 한다.</b> 물리는 서버에서만 돌고(<c>-145</c>) 입력은
        /// <c>CmdSendInput</c> 으로 이미 올라간다 — 참가자가 여기서 스스로 던지면 같은
        /// 던지기가 두 번 일어난다(<c>-513</c> 이 체력에서 겪은 것과 같은 갈라짐).</para>
        /// </summary>
        bool ProcessTeammateToss(Vector3 aim)
        {
            var victim = TossVictim;
            if (victim == null)
            {
                // **참가자 화면에서는 지우지 않는다.** 잡기 판정은 서버가 하므로 참가자
                // 기계의 내 손에는 그 조인트가 없을 수 있고, 그러면 여기서 매 틱 0 을
                // 써서 서버가 보낸 게이지를 지운다 — 게이지가 호스트에만 보이게 된다.
                if (ServerAuthority)
                {
                    tossCharge = 0f;
                    tossActionHeld = false;
                    CarryResistSeconds = 0f;
                    CarryLifted = false;
                }
                return false;
            }

            // 잡고 있는 동안 두 팔은 그 대상을 향해 둔다. 도구를 들었을 때와 같다.
            DriveArm(Doll.Left, true, aim);
            DriveArm(Doll.Right, true, aim);

            if (!ServerAuthority) return true;

            // 잡고 있는 동안 계속 끌어온다(S15P21B202-555). 충전·던지기보다 앞에 둔다 —
            // 던진 뒤에는 잡은 것이 없으므로 이 아래 분기들이 먼저 돌면 한 틱이 어긋난다.
            PullTeammate(victim, aim);

            if (GetToolUtilityHeld())
            {
                tossCharge = Mathf.Clamp01(tossCharge + Time.fixedDeltaTime / Tuning.ToolChargeDuration);
                tossActionHeld = true;
                return true;
            }

            if (!tossActionHeld) return true;

            if (tossCharge >= Tuning.TossChargeThreshold) TossTeammate(victim, aim, tossCharge);

            tossCharge = 0f;
            tossActionHeld = false;
            return true;
        }

        void TossTeammate(PlayerRig victim, Vector3 aim, float charge)
        {
            if (!ServerAuthority || victim == null) return;

            ReleaseHand(LeftHand, HandReleaseReason.Throw);
            ReleaseHand(RightHand, HandReleaseReason.Throw);
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;

            var direction = aim.sqrMagnitude > 1e-4f ? aim : Doll.Chest.transform.forward;

            // **한 틱 늦춘다.** 바로 위 도구 투척이 같은 이유로 그렇게 한다 — 놓은
            // 조인트는 다음 틱에 사라지므로, 지금 속도를 넣으면 구속이 먹어 버린다.
            pendingTossVictim = victim;
            pendingTossVelocity = TossVelocity(direction, charge);
            pendingTossCharge = charge;

            TossSeq++;

            // 호스트 화면의 던지는 그림. 참가자 화면은 NetPlayer 가 tossSeq 로 켠다.
            Doll?.Suit?.PlayToss();

            Debug.Log($"[NotThisPlanet][던지기] {Nickname} → {victim.Nickname}"
                      + $" · 충전={charge:F2} · 속도={pendingTossVelocity}");
        }

        /// <summary>
        /// 던져져 날아가기 시작한다. <b>서버에서만</b> 불린다.
        ///
        /// <para>속도는 <see cref="Doll.Bodies"/> 전부에 넣는다. 골반에만 넣으면 나머지
        /// 부위가 조인트에 끌려가며 몸이 늘어나 보인다.</para>
        /// </summary>
        /// <summary>
        /// 던져진 몸이 넘어갈 축과 빠르기 (S15P21B202-551).
        ///
        /// <para><b>앞구르기다.</b> 날아가는 방향을 축으로 삼으면 꼬치처럼 제자리에서 돌고,
        /// 위를 축으로 삼으면 팽이가 된다. 넘어가 보이려면 <b>가는 방향과 위, 둘 다에
        /// 수직인 축</b>이어야 한다.</para>
        ///
        /// <para>빠르기는 던진 세기를 따른다 — 살짝 던지면 조금 기울고 꽉 채우면 한 바퀴
        /// 넘어간다. 위로만 던져 앞 성분이 없으면 <paramref name="fallbackRight"/> 로
        /// 축을 잡는다(몸의 오른쪽). 그것마저 없으면 회전을 주지 않는다 — 아무 축이나
        /// 고르면 던질 때마다 다르게 돈다.</para>
        /// </summary>
        public static Vector3 TossSpin(Vector3 velocity, float charge01, Vector3 fallbackRight)
        {
            var forward = velocity;
            forward.y = 0f;

            var axis = forward.sqrMagnitude > 1e-4f
                ? Vector3.Cross(Vector3.up, forward.normalized)
                : fallbackRight;

            if (axis.sqrMagnitude < 1e-4f) return Vector3.zero;

            return axis.normalized * (Tuning.TossSpinSpeed * TossStrength(charge01));
        }

        public void LaunchTossed(Vector3 velocity) => LaunchTossed(velocity, 1f);

        public void LaunchTossed(Vector3 velocity, float charge01)
        {
            if (!ServerAuthority) return;

            // 나를 잡고 있던 손이 하나라도 남아 있으면 조인트가 도로 끌어당긴다.
            ReleaseCarriersOf(CarryTarget);

            Tossed = true;
            TossedSeconds = 0f;

            if (Doll != null)
            {
                for (var i = 0; i < Doll.Bodies.Count; i++)
                {
                    var body = Doll.Bodies[i];
                    if (body == null || body.isKinematic) continue;

                    var horizontal = body.linearVelocity;
                    horizontal.y = 0f;
                    body.linearVelocity = horizontal + velocity;
                }

                // 넘어가게 한다(S15P21B202-551). 속도와 같은 이유로 **부위 전부**에 넣는다 —
                // 골반에만 넣으면 나머지가 조인트에 끌려가며 몸이 늘어져 보인다.
                var spin = TossSpin(velocity, charge01,
                                    Doll.Hips != null ? Doll.Hips.transform.right : Vector3.right);
                if (spin.sqrMagnitude > 1e-6f)
                {
                    for (var i = 0; i < Doll.Bodies.Count; i++)
                    {
                        var body = Doll.Bodies[i];
                        if (body == null || body.isKinematic) continue;
                        body.angularVelocity = spin;
                    }
                }
            }

            RefreshDownedCamera();
            RefreshDollVisibility();
        }

        /// <summary>
        /// 날아가는 것이 끝났는지 본다. <b>최소 시간을 두는 이유</b>는
        /// <see cref="Tuning.TossedMinSeconds"/> 에 적었다 — 발이 아직 땅에 있는
        /// 그 틱에 <see cref="Grounded"/> 를 읽으면 던지자마자 끝난다.
        /// </summary>
        void TickTossed()
        {
            if (!Tossed) return;

            TossedSeconds += Time.fixedDeltaTime;

            if (!ServerAuthority) return;
            if (TossedSeconds < Tuning.TossedMinSeconds || !Grounded) return;

            Tossed = false;
            TossedSeconds = 0f;
            RefreshDownedCamera();
            RefreshDollVisibility();
        }

        /// <summary>
        /// 조준 앞의 동료를 후보로 넣는다(기획 §20.4 · S15P21B202-442).
        ///
        /// <b>물리 질의를 쓰지 않는다.</b> 이유는 <see cref="CarryTarget"/> 에 적었다 —
        /// 조준 탐색 마스크가 Player 를 통째로 빼므로 몸에 <see cref="Grabbable"/> 을
        /// 붙여도 영영 걸리지 않는다. <see cref="Hazards.MeteorBat"/> 가 때릴 동료를
        /// <see cref="All"/> 로 찾는 것과 같은 방식이다.
        ///
        /// <para><b>예전 이름은 <c>FindDownedTeammate</c> 였다.</b> 기절한 사람만 걸렀기
        /// 때문인데, 이제 살아있는 동료도 후보다 — 이름이 거짓이 되므로 바꿨다.
        /// 무엇이 갈랐는지는 실측이 정확히 말해 준다: 같은 자리·같은 조준에서
        /// <c>GoDown()</c> 만 부르면 후보가 <c>None → Grabbable</c> 로 바뀌었다.
        /// 거리·각도는 이미 전부 맞아 있었고 <c>Downed</c> 조건 하나가 갈랐다.</para>
        /// </summary>
        void FindTeammateGrabPoint(Vector3 origin, Vector3 forward,
                                   ref PlayerInteractionTarget best)
        {
            for (var i = 0; i < All.Count; i++)
            {
                var other = All[i];
                if (other == null || other == this) continue;

                var carry = other.CarryTarget;
                if (carry == null || !carry.isActiveAndEnabled || !carry.CanGrab) continue;

                var point = carry.transform.position;
                var distance = Vector3.Distance(origin, point);
                if (distance > PhysicsScale.DownedCarryReach) continue;

                var angle = AngleFrom(origin, forward, point);
                if (angle > PhysicsScale.DownedCarryAngle) continue;

                Consider(ref best, MakeTarget(PlayerInteractionKind.Grabbable, null, carry,
                                              null, point, angle, distance));
            }
        }

        /// <summary>
        /// 겨냥 안의 기절한 동료를 후보로 댄다(S15P21B202-448).
        ///
        /// <b>거리·각도는 여기서 보지 않는다.</b> <see cref="Interactor"/> 가 질의로 찾은
        /// 후보와 같은 규칙으로 걸러야 "4인이 각자 화면에서 같은 판단을 한다" 가
        /// 성립한다. 여기서 한 번 더 자르면 두 경로의 기준이 갈라진다.
        ///
        /// <para><see cref="FindTeammateGrabPoint"/> 와 같은 방식이다 — 물리 질의를
        /// 쓰지 않고 <see cref="All"/> 을 훑는다. 그쪽이 참가자 화면에서 동작하는
        /// 이유가 이것이고, 살리기만 질의를 쓰고 있어서 호스트에서만 됐다.</para>
        /// </summary>
        void CollectRescueCandidates(List<IInteractable> into)
        {
            // **내가 쓰러져 있으면 아무도 대지 않는다**(!356 AI 리뷰). 막는 곳이
            // DownedRescue.GetAvailability 하나뿐이면, 그 한 줄이 사라져도 아무 검증이
            // 빨간불이 되지 않는다. 후보 자체를 대지 않는 쪽이 뜻도 분명하다 —
            // TickRescue 도 Downed 이면 인터랙터를 취소하고 돌아간다.
            if (Downed) return;

            for (var i = 0; i < All.Count; i++)
            {
                var other = All[i];
                if (other == null || other == this || !other.Downed) continue;

                var rescue = other.RescueTarget;
                if (rescue == null || !rescue.isActiveAndEnabled) continue;

                into.Add(rescue);
            }
        }

        void SetDollLayer(int layer)
        {
            if (Doll == null) return;

            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body != null) body.gameObject.layer = layer;
            }
        }

        // ── 부활 / 순간이동 ─────────────────────────────────────
        public void Teleport(Vector3 footPos, Quaternion facing)
        {
            // 옮긴 거리를 속도로 읽으면 안 된다 (S15P21B202-628).
            hipsYKnown = false;
            JumpedUp = false;
            if (MountedVehicle != null) ForceExitTransportVehicle(MountedVehicle);
            else DisconnectTransportJoint();   // 우주선에 붙어 있었다면 여기서 떨어진다

            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);

            // **나를 잡고 있는 손도 놓게 한다**(S15P21B202-442). 내 손만 놓으면
            // 남이 건 FixedJoint 가 그대로 남아, 순간이동한 몸을 맵 저쪽에서 도로
            // 끌어당긴다. 잡을 지점이 기절 중에만 켜져 있던 동안에는 이 경로가
            // 기절 해제와 겹쳐 가려져 있었다.
            ReleaseCarriersOf(CarryTarget);

            Doll.Teleport(footPos + Vector3.up * RagdollBuilder.HipsY, facing);
            water = null;
            Swimming = false;
            UpdateFloating();
            faceDir = facing * Vector3.forward;
            movementDirectionLocal = Vector2.zero;
            uprightBlend = 1f;
            Ragdolled = false;
            StepPhase = 0f;
            AirTime = 0f;
            fallPeakFootHeight = footPos.y;
            ResetToolState();
            toolThrowRegrabLock = 0f;
            pendingThrowBody = null;
            previousGrabLeft = false;
            previousGrabRight = false;
            ClampInteractor?.Cancel();
            ResetClampGrab();
            if (Cam != null)
            {
                Cam.Yaw = facing.eulerAngles.y;
                Cam.SnapToTarget();
            }
        }

        /// <summary>
        /// 몸을 강체에 붙인다. 붙이는 코드가 여기 한 군데다.
        ///
        /// <b>조인트를 만들지 않는다</b> (S15P21B202-434). 예전에는 골반에
        /// <c>FixedJoint</c> 를 걸었는데, 그러면 탑승자 위치를 정하는 주체가 <b>셋</b>이
        /// 되었다 — 이 조인트 · <c>TransportVehicle.RideAlong</c> 의 속도 주입 ·
        /// 참가자 쪽의 좌석 추종(S15P21B202-423). 한 성질을 세 곳에서 판단하면 반드시
        /// 어긋나고, 실제로 참가자 화면에서 몸이 기체를 벗어났다.
        ///
        /// 이제 <b>좌석 자세가 유일한 진실</b>이다. 몸을 물리에서 빼고(kinematic ·
        /// 충돌 끔) 탈것에 대한 <b>상대 자세</b>를 적어 둔 뒤, 매 물리 스텝마다 그 자리로
        /// 옮긴다(<see cref="HoldSeat"/>). <c>RideAlong</c> 은 kinematic 인 몸을 스스로
        /// 건너뛰므로 속도 주입도 저절로 멈춘다.
        ///
        /// <b>몸을 지우지는 않는다.</b> 이름표(<c>NetPlayer.NameplateAnchor</c> 가
        /// <c>Doll.Head</c> 를 본다) · 기절과 구조(기획 §20) · 하차 복원 ·
        /// <c>NetPlayer</c> 의 위치 전송이 모두 몸에 붙어 있다.
        ///
        /// 탈것 조종감은 바뀌지 않는다 — <c>TransportVehicle</c> 의 질량은 고정 80kg 이라
        /// 탑승자를 물리에서 빼도 계산에 들어가는 값이 없다.
        /// </summary>
        void AttachTo(Rigidbody body)
        {
            RidingBody = body;
            BeginSeatHold(body);
        }

        /// <summary>
        /// 탑승 자세를 탈것 기준으로 적어 두고 몸을 물리에서 뺀다 (S15P21B202-434).
        ///
        /// <para><b>상대 자세로 적는 이유.</b> 탈것은 평소 <c>FreezeRotation</c> 이지만
        /// 운석에 맞으면 돌며 떨어진다(S15P21B202-349·-353). 월드 자세를 적어 두면 그때
        /// 몸이 제자리에 남아 기체만 굴러간다.</para>
        /// </summary>
        void BeginSeatHold(Rigidbody seat)
        {
            if (seat == null || Doll == null) return;

            int count = Doll.Bodies.Count;
            seatLocalPositions = new Vector3[count];
            seatLocalRotations = new Quaternion[count];
            seatWasKinematic = new bool[count];
            seatWasColliding = new bool[count];

            var t = seat.transform;
            for (var i = 0; i < count; i++)
            {
                var part = Doll.Bodies[i];
                if (part == null) continue;

                seatLocalPositions[i] = t.InverseTransformPoint(part.position);
                seatLocalRotations[i] = Quaternion.Inverse(t.rotation) * part.rotation;
                seatWasKinematic[i] = part.isKinematic;
                seatWasColliding[i] = part.detectCollisions;

                part.detectCollisions = false;
                part.isKinematic = true;
            }

            seatHeld = true;
        }

        /// <summary>
        /// 적어 둔 상대 자세로 몸을 옮긴다. 매 물리 스텝에 한 번 (S15P21B202-434).
        ///
        /// <para><c>MovePosition</c>·<c>MoveRotation</c> 을 쓴다 — kinematic 인 몸을
        /// 그렇게 옮기면 보간이 살아 있어 화면이 끊기지 않는다. <c>transform</c> 에 직접
        /// 대입하면 그 보간이 꺼진다(<c>WorldSync.FollowOnClient</c> 의 주석과 같은 사정).</para>
        /// </summary>
        void HoldSeat()
        {
            if (!seatHeld || RidingBody == null || Doll == null) return;

            var t = RidingBody.transform;
            int count = Mathf.Min(Doll.Bodies.Count, seatLocalPositions.Length);
            for (var i = 0; i < count; i++)
            {
                var part = Doll.Bodies[i];
                if (part == null) continue;

                part.MovePosition(t.TransformPoint(seatLocalPositions[i]));
                part.MoveRotation(t.rotation * seatLocalRotations[i]);
            }
        }

        /// <summary>
        /// 몸을 물리로 돌려놓는다 (S15P21B202-434).
        ///
        /// <para><b>되돌리지 않으면 하차한 사람이 공중에 굳는다.</b> 그것이 원래 버그보다
        /// 나쁘다 — 참가자 쪽에서 같은 실수를 막으려고 S15P21B202-423 도 저장·복원을
        /// 짝으로 두었다.</para>
        /// </summary>
        void EndSeatHold()
        {
            if (!seatHeld)
            {
                seatLocalPositions = null;
                seatLocalRotations = null;
                seatWasKinematic = null;
                seatWasColliding = null;
                return;
            }

            if (Doll != null && seatWasKinematic != null)
            {
                int count = Mathf.Min(Doll.Bodies.Count, seatWasKinematic.Length);
                for (var i = 0; i < count; i++)
                {
                    var part = Doll.Bodies[i];
                    if (part == null) continue;

                    part.isKinematic = seatWasKinematic[i];
                    part.detectCollisions = seatWasColliding[i];
                }
            }

            seatHeld = false;
            seatLocalPositions = null;
            seatLocalRotations = null;
            seatWasKinematic = null;
            seatWasColliding = null;
        }

        /// <summary>탑승 직전에 손·도구·보행 상태를 정리한다. 두 탑승 경로가 함께 쓴다.</summary>
        void ClearForRide()
        {
            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);

            // 탑승하는 사람을 붙들고 있을 수는 없다 — 몸이 좌석에 고정되므로
            // 잡은 쪽이 탈것에 끌려다니게 된다(S15P21B202-442).
            ReleaseCarriersOf(CarryTarget);
            ClampInteractor?.Cancel();
            ResetClampGrab();
            ResetToolState();

            Grounded = true;
            GroundDistance = Tuning.RideHeight;
            GroundVelocity = Vector3.zero;
            Speed = 0f;
            movementDirectionLocal = Vector2.zero;
            AirTime = 0f;
            Thrusting = false;
            Ragdolled = false;
        }

        /// <summary>운반 장치에 올라탄다. 고정쇠·도구 상태와 무관하게 탑승을 허용한다.</summary>
        public bool TryEnterTransportVehicle(TransportVehicle vehicle)
        {
            if (vehicle == null || IsRiding || Doll == null || Doll.Hips == null)
                return false;

            ClearForRide();

            var foot = vehicle.SeatFootPosition;
            var seatRotation = vehicle.SeatRotation;
            Doll.Teleport(foot + seatRotation * (Vector3.up * RagdollBuilder.HipsY), seatRotation);
            faceDir = seatRotation * Vector3.forward;

            AttachTo(vehicle.Body);
            MountedVehicle = vehicle;

            // 조종석 안에 있으므로 몸을 감춘다. 1인칭 시야를 제 몸이 가리지 않게 하는 것이
            // 첫 목적이고, 3인칭으로 강제 전환해도 기체 밖으로 튀어나온 몸이 보이지 않는다.
            RefreshDollVisibility();

            if (Cam != null)
            {
                Cam.SetVehicleEye(vehicle.CockpitEye);
                Cam.SetVehicleView(true);
            }
            return true;
        }

        /// <summary>
        /// 우주선에 탑승한다. 붙는 곳은 최상단 견인 조종 모듈이다
        /// (기획 S15P21B202-107 §22.4 "플레이어들이 탑승한 최상단 견인 조종 모듈").
        ///
        /// 운반 장치와 달리 <see cref="MountedVehicle"/> 을 채우지 않는다 — 우주선은
        /// 조종하지 않고 조종석 시야도 쓰지 않는다. 실려 가기만 한다.
        /// </summary>
        public bool TryBoardShip(Rigidbody module, Vector3 seatPosition)
        {
            if (module == null || IsRiding || Doll == null || Doll.Hips == null) return false;

            ClearForRide();

            Doll.Teleport(seatPosition, Doll.Hips.rotation);
            AttachTo(module);

            // 모듈 안에 있는 몸이다. 껍데기를 통과해 보이면 안 된다.
            RefreshDollVisibility();
            return true;
        }

        /// <summary>우주선에서 내린다. 카운트다운을 취소하면 이 경로로 돌아온다.</summary>
        public void LeaveShip(Vector3 footPosition, Quaternion facing)
        {
            if (RidingBody == null || MountedVehicle != null) return;

            DisconnectTransportJoint();
            Doll.Teleport(footPosition + Vector3.up * RagdollBuilder.HipsY, facing);
            faceDir = facing * Vector3.forward;
            RefreshDollVisibility();

            if (Cam == null) return;
            Cam.Yaw = facing.eulerAngles.y;
            Cam.SnapToTarget();
        }

        /// <summary>
        /// 발사 실패로 우주선에서 튀어나온다(기획 §22.4 "모든 플레이어가 사방으로
        /// 튀어나온다 … 잠시 구르지만 기절하지 않고 자동으로 일어난다").
        ///
        /// <b>기절 처리를 새로 만들지 않는다.</b> <see cref="Balance"/> 가 이미 넘어진
        /// 자세를 스스로 세우므로, 조인트를 끊고 충격만 주면 구르다가 일어난다.
        /// 체력도 깎지 않는다 — 기획이 기절하지 않는다고 못박았고, 체력을 깎으면
        /// 운석 두 방 맞은 상태에서 발사 실패가 사망이 된다.
        ///
        /// 위치는 옮기지 않는다. 몸은 이미 모듈 안에 있고, 밖으로 순간이동시키면
        /// 지면이나 잔해 속에 박힐 자리를 다시 찾아야 한다 — 그 탐색이
        /// S15P21B202-132 의 흰 화면을 만든 곳이다. 충격으로 밀어내는 편이 안전하다.
        /// </summary>
        public void EjectFromShip(Vector3 direction)
        {
            if (RidingBody == null) return;

            DisconnectTransportJoint();
            MountedVehicle = null;

            var away = direction;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : faceDir;

            var impulse = away * PhysicsScale.EjectHorizontalSpeed
                          + Vector3.up * PhysicsScale.EjectVerticalSpeed;

            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                body.useGravity = true;
                body.linearVelocity = impulse;
            }

            Grounded = false;
            GroundDistance = float.MaxValue;
            GroundVelocity = Vector3.zero;
            Ragdolled = true;          // Balance 가 다음 틱부터 다시 세운다
            AirTime = 0f;
            faceDir = away;

            RefreshDollVisibility();
        }

        /// <summary>운반 장치가 표면에 닿은 뒤 플레이어를 그 옆에 내려놓는다.</summary>
        public void CompleteTransportVehicleExit(TransportVehicle vehicle)
        {
            if (vehicle == null || MountedVehicle != vehicle) return;
            DisconnectTransportJoint();
            MountedVehicle = null;

            PlaceAfterVehicleExit(vehicle);
        }

        /// <summary>부활·리셋 때 지연 없이 운반 장치에서 내린다.</summary>
        public void ForceExitTransportVehicle(TransportVehicle vehicle)
        {
            if (vehicle == null || MountedVehicle != vehicle) return;
            DisconnectTransportJoint();
            MountedVehicle = null;

            PlaceAfterVehicleExit(vehicle);
        }

        /// <summary>
        /// 제 몸을 그릴지 정한다. 가시성을 바꾸는 곳은 여기 한 군데다.
        ///
        /// <b>1인칭에서는 제 몸을 그리지 않는다.</b> 카메라는 골반 + 0.55m, 즉 발 기준
        /// 0.85m 에 서고 시선 방향으로 0.20m(<see cref="OrbitCamera.FirstPersonForward"/>)
        /// 내민다. 그런데 헬멧이 0.51~1.15m, 반지름 약 0.32m 다 — <b>카메라가 제 헬멧
        /// 안에 있다.</b> 귀는 1.33m 까지 올라가고 모델(<c>useModel</c>)을 씌우면 주둥이와
        /// 꼬리가 더 붙는다. 그래서 시선을 조금만 돌려도 제 몸이 화면을 덮는다.
        ///
        /// 내미는 거리를 키워서 피하는 것은 임시방편이다. 모델이 바뀌면 다시 깨지고,
        /// 카메라를 몸 밖으로 밀어낼수록 시점이 몸에서 떠 보인다. 1인칭에서 제 몸을
        /// 그리지 않는 것이 정석이다.
        ///
        /// 그림자는 남긴다 — 1인칭에서 발밑을 읽는 단서가 제 그림자다.
        ///
        /// 탑승 중에는 그림자까지 감춘다. 기체 안에 있는 몸이고 껍데기도 감춰 둔
        /// 상태(<c>SetHullVisible</c>)라, 그림자만 남으면 허공에 혼자 뜬 그림자가 된다.
        ///
        /// <see cref="Cam"/> 이 null 인 원격 플레이어는 그대로 보인다.
        /// </summary>
        /// <summary>
        /// 기절 시점(1인칭 ↔ 3인칭)을 지금 상태에 맞춘다 (S15P21B202-511).
        ///
        /// <para><b>규칙을 한 곳에 둔다.</b> 예전에는 <see cref="GoDown"/> 과
        /// <see cref="Revive"/> 안에 <c>Cam.SetDownedView(...)</c> 가 각각 인라인으로
        /// 있었고, <see cref="SetRemoteDowned"/> 에는 아무것도 없었다. 그래서 참가자
        /// 기계에서는 — <c>GoDown</c>·<c>Revive</c> 가 돌지 않으므로 — <b>죽어도
        /// 1인칭 그대로였다.</b></para>
        ///
        /// <para><b>남의 몸은 저절로 걸러진다.</b> <see cref="Cam"/> 은
        /// <c>SpaceBootstrap</c> 이 <b>로컬 플레이어 하나에만</b> 붙인다 — 원격 몸의
        /// 카메라는 <c>null</c> 이다. 그래서 "내 것일 때만" 같은 조건을 따로 두지
        /// 않는다.</para>
        ///
        /// <para><b>여러 번 불려도 안전하다.</b>
        /// <see cref="OrbitCamera.SetDownedView"/> 의 첫 줄이
        /// <c>if (on == downedView) return;</c> 이다.</para>
        ///
        /// <para>머리 본을 넘기는 이유는 <c>OrbitCamera</c> 쪽에 적었다 — 그 회전을
        /// 따라가야 시야가 몸과 함께 넘어간다(S15P21B202-259).</para>
        /// </summary>
        void RefreshDownedCamera()
        {
            if (Cam == null) return;

            // 기절과 던져짐을 함께 본다(S15P21B202-519). 둘 다 "몸이 구르는 대로 화면이
            // 넘어갔다가 3인칭으로 물러난다" 로 같고, 카메라 쪽에 상태를 하나 더 만들면
            // 저장·복원이 두 벌이 되어 서로의 값을 덮는다 — SetDownedView 안의
            // pushView·vehicleView 처리가 이미 그 문제를 적어 두었다.
            var on = BodyViewActive;
            Cam.SetDownedView(on, on ? Doll?.Head?.transform : null);
        }

        void RefreshDollVisibility()
        {
            if (Doll == null) return;

            // 탑승 중에는 그림자도 남기지 않는다. 조종석 안에 있는 몸의 그림자가 기체
            // 밖 지면에 생기면 몸이 보이는 것보다 더 이상하다 — 1인칭에서 그림자를
            // 남기는 것(§10.1 "발밑을 읽는 단서가 제 그림자다")과는 사정이 다르다.
            if (IsRiding || RemoteRiding) Doll.SetVisible(false);
            else if (Cam != null && Cam.FirstPerson) Doll.SetVisible(false, keepShadow: true);
            else Doll.SetVisible(true);

            lastFirstPerson = Cam != null && Cam.FirstPerson;
        }

        /// <summary>
        /// 마지막으로 몸을 그릴지 정할 때의 시점. 바뀐 것을 알아채는 데만 쓴다.
        /// </summary>
        bool lastFirstPerson;

        /// <summary>
        /// 시점이 바뀌었으면 몸을 그릴지 다시 정한다(S15P21B202-267).
        ///
        /// <b>왜 물어보는가.</b> 시점을 바꾸는 것은 <see cref="OrbitCamera"/> 안에서
        /// 일어난다 — 기절 5초 뒤 3인칭으로 물러나는 것(S15P21B202-259)이 그렇다. 그런데
        /// 인형을 그릴지 정하는 것은 이쪽 일이고, 카메라가 인형을 직접 만지게 하면
        /// 의존 방향이 뒤집힌다(지금은 <c>PlayerRig</c> → <c>OrbitCamera</c> 한쪽이다).
        ///
        /// <b>매 프레임 갱신하지 않는다.</b> <see cref="RefreshDollVisibility"/> 는 인형
        /// 아래 <b>모든</b> 렌더러를 훑는다(장식이 강체의 자식이라 그래야 한다). 바뀔 때만
        /// 부르는 것이 지금 구조가 "사건마다 부르는" 이유이고, 그 구조를 지킨다.
        /// </summary>
        void RefreshDollVisibilityIfViewChanged()
        {
            if (Cam == null) return;

            if (Cam.FirstPerson == lastFirstPerson) return;

            RefreshDollVisibility();
        }

        /// <summary>하차한 탑승자를 지면 위로 살짝 띄우는 여유. 발이 표면에 박히지 않게 한다.</summary>
        const float VehicleExitClearance = 0.06f;

        /// <summary>
        /// 하차 공통 처리. 운반체가 찾아 준 발 위치에 세우고 장치 바깥을 보게 돌린다.
        ///
        /// <b><see cref="Doll.Teleport"/> 의 첫 인수는 발이 아니라 골반 위치다.</b>
        /// 예전에는 발 높이 + 0.04m 를 그대로 넘겨서 골반이 지면 4cm 에 놓였다. 골반은
        /// 0.3m 높이여야 하므로 다리와 발이 지면 26cm 아래에 파묻히고, PhysX 가 그것을
        /// 밀어내면서 탑승자가 천장까지 날아갔다. 탑승할 때는 같은 함수에 HipsY 를 더해
        /// 넘기고 있었는데 하차 경로만 빠져 있었다.
        ///
        /// <c>fallPeakFootHeight</c> 를 내려놓은 높이로 맞추는 것이 중요하다. 구조물 위에서
        /// 내리면 지면보다 높은데, 이 값을 갱신하지 않으면 그 차이가 낙하로 집계돼
        /// HUD 의 추락 횟수가 오른다.
        /// </summary>
        void PlaceAfterVehicleExit(TransportVehicle vehicle)
        {
            var foot = vehicle.GroundExitFootPosition;

            var away = foot - vehicle.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = vehicle.ExitForward;
            away.Normalize();

            // 카메라 설정을 먼저 되돌린다. 1인칭인지 3인칭인지를 보고 서는 방향을 정한다.
            if (Cam != null) Cam.SetVehicleView(false);

            // 시점이 정해진 뒤에 몸을 그릴지 정한다. 순서가 바뀌면 조종석 1인칭 상태를
            // 보고 판단해 3인칭인데도 몸이 감춰진 채로 남는다.
            RefreshDollVisibility();

            // <b>인형의 방향과 카메라의 요를 반드시 맞춘다.</b> 이것이 이 버그의 핵심이었다.
            // 예전에는 인형만 돌리고 카메라 요는 조종석에서 보던 값을 그대로 뒀다. 두 값이
            // 어긋나면 1인칭 카메라가 머리 <b>앞</b> 20cm 가 아니라 머리 <b>뒤</b>로 밀려
            // 제 몸과 꼬리가 화면에 들어온다.
            //
            // 어느 쪽을 보고 설지는 1인칭과 3인칭에서 반대다.
            //
            //  · 1인칭 — 카메라가 머리에 있으므로 기체 <b>바깥</b>을 봐야 눈앞이 트인다.
            //    기체를 마주 보면 1.8m 앞의 기체(1.90 x 2.45m)가 화면을 가득 덮는다.
            //    걸어 나가는 방향이기도 해서 자연스럽다.
            //  · 3인칭 — 카메라가 등 뒤 3.6m 에 서므로 기체를 <b>마주</b> 봐야 한다.
            //    등을 지면 하차 지점 1.8m 에서 껍데기 반지름 0.95m 를 뺀 0.85m 밖에
            //    물러날 자리가 없어 최소 거리 1m 까지 밀려들어오고, 그 자리에서 보이는
            //    것은 제 등과 꼬리뿐이다.
            //
            // 씬의 SpaceBootstrap 은 firstPerson = 1 로 1인칭으로 시작한다. 3인칭 쪽도
            // 맞춰 두는 이유는 그 값이 씬 설정이라 언제든 바뀔 수 있어서다.
            var lookDir = Cam != null && Cam.FirstPerson ? away : -away;
            var facing = Quaternion.LookRotation(lookDir, Vector3.up);

            Doll.Teleport(foot + Vector3.up * (RagdollBuilder.HipsY + VehicleExitClearance), facing);
            faceDir = lookDir;

            if (Cam != null)
            {
                Cam.Yaw = facing.eulerAngles.y;

                // 조종석 자리에서 새 자리로 화면을 가로질러 날아오지 않게.
                Cam.SnapToTarget();
            }
            Grounded = false;
            GroundDistance = 0f;
            GroundVelocity = Vector3.zero;
            Speed = 0f;
            AirTime = 0f;
            fallPeakFootHeight = foot.y;
        }

        /// <summary>
        /// 좌석 조인트를 뗀다. <b>즉시</b> 없애야 한다.
        ///
        /// 예전 구현은 <c>connectedBody = null</c> 로 두고 <c>Destroy</c> 를 불렀다. 두 가지가
        /// 겹쳐 하차하면 캐릭터가 맵 원점으로 빨려갔다.
        ///
        ///   · <c>connectedBody</c> 가 null 이면 <c>connectedAnchor</c> 가 <b>월드 좌표</b>로
        ///     해석된다(Unity 문서). 탑승할 때 넣어 둔 값은 운반체 로컬 좌표, 즉 원점 근처의
        ///     작은 벡터였다. 그래서 조인트가 맵 원점을 앵커로 삼는 스프링이 됐다.
        ///   · <c>Destroy</c> 는 프레임 끝에 처리된다. 그 사이 물리 스텝이 돌면 위 앵커가
        ///     그대로 작동한다. 인형은 이미 하차 지점으로 옮겨 놨으므로 그대로 끌려간다.
        ///
        /// 계측값: 하차 지점 (18.00, 0.36, -12.20) 에서 1초 만에 (0.00, 0.06, -0.73) 로
        /// 21.3m 이동. <c>Exit_RiderStaysWhereItWasPlaced</c> 가 이 값을 지킨다.
        ///
        /// <c>connectedBody</c> 는 건드리지 않고 <c>DestroyImmediate</c> 로 지운다. 앵커를
        /// 옮겨 무해하게 만드는 방법도 있지만, 조인트를 떼는 시점이 인형을 옮기는 시점보다
        /// 앞이라 결국 "옮긴 뒤 원래 자리로 당겨지는" 문제가 남는다. 아예 없애는 편이 맞다.
        /// </summary>
        void DisconnectTransportJoint()
        {
            RidingBody = null;

            // 조인트는 더 이상 만들지 않는다 (S15P21B202-434). 붙잡고 있던 좌석을 놓고
            // 몸을 물리로 돌려놓는 것이 이제 "떼는 것" 이다.
            EndSeatHold();
        }

        void FixedUpdate()
        {
            // 조준 기준점을 먼저 맞춘다 — 이번 스텝의 상호작용이 그것을 본다.
            SyncAimPivot();
            Tick();
        }

        /// <summary>
        /// 탑승체가 돌면 화면도 함께 기울인다(기획 §14.1, S15P21B202-353).
        ///
        /// <b>여기서 하는 이유.</b> <see cref="MountedVehicle"/> 과
        /// <see cref="Cam"/> 을 둘 다 아는 곳이 여기뿐이다. 조종 컨트롤러에 두면 원격
        /// 플레이어나 컨트롤러가 없는 경로에서 빠진다.
        ///
        /// 굴림·복구가 끝나면 항등으로 돌아간다 — 그 두 상태가 반드시 끝나는 것은
        /// <c>TransportVehicle</c> 쪽 검증이 잠근다.
        /// </summary>
        void SyncCameraTilt()
        {
            if (Cam == null) return;

            var craft = MountedVehicle;
            var tilting = craft != null && (craft.Tumbling || craft.Righting);

            Cam.SetViewTilt(tilting ? craft.CameraTilt : Quaternion.identity);
        }

        /// <summary>조준 기준점을 지금 보고 있는 곳에 맞춘다.</summary>
        void SyncAimPivot()
        {
            if (aimPivot == null) return;
            aimPivot.SetPositionAndRotation(AimOrigin, AimRotation);
        }

        /// <summary>
        /// 물리 스텝 한 번. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode 테스트는
        /// <c>Physics.Simulate</c> 와 짝지어 직접 부른다.
        ///
        /// 리포의 다른 물리 컴포넌트(<see cref="Parts.TractorBeam"/>,
        /// <see cref="Parts.TransportVehicle"/>, <see cref="Parts.LowGravityField"/>)가
        /// 모두 같은 방식이다. 시간은 인수로 받지 않는다 — 본문이 여러 곳에서
        /// <c>Time.fixedDeltaTime</c> 을 쓰고 있어 인수로 바꾸면 그 전부를 함께 손봐야 한다.
        /// </summary>
        public void Tick()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (WorkstationMovementLocked)
            {
                input.Move = Vector2.zero;
                input.Jump = input.GrabLeft = input.GrabRight = false;
                input.ToolPrimary = input.ToolSecondary = false;
            }

            // 피해 번쩍임을 식힌다 (S15P21B202-539). 아래 게이지들보다 앞에 두는 이유는
            // 뒤쪽 이른 반환(Doll 이 아직 없거나 Hips 좌표가 망가진 틱) 때문이다 —
            // 그 뒤에 두면 그런 틱에 번쩍임이 식지 않고 화면에 얼어붙는다.
            TickDamageFlash();

            SyncCameraTilt();

            // 파츠 밀기 상태를 먼저 갱신한다 — 아래 Locomotion 이 이 값으로 이동
            // 기준을 정하고(§10.2), 밀기 카메라도 같은 값을 본다.
            RefreshHaulState();
            RefreshAnchoredPartCollision();

            if (Doll == null || Doll.Hips == null) return;
            if (!JointTools.IsFinite(Doll.Hips.position)) { return; }
            ReleaseProneIfTaken();

            RefreshInteractionFocus();

            // 매 고정틱 돌아야 한다. Time.fixedDeltaTime 을 적산하므로 호출 빈도가
            // 곧 게이지 속도다 (!297 AI 리뷰가 첫 판의 자리를 짚었다 — 운반 장치
            // 목록 갱신 함수 안에 들어가 있었다).
            TickRescue();

            // 쓰러진 사람 화면의 게이지가 읽을 값이다(S15P21B202-448). TickRescue 가
            // 진행분을 쌓은 뒤에 읽어야 같은 틱의 값이 나온다.
            RefreshBeingRescued();

            // 던져져 날아가는 중인지 센다(S15P21B202-519). 기절 게이지와 같은 줄에
            // 두는 이유도 같다 — 매 고정틱 돌아야 시간이 맞는다.
            TickTossed();

            // 기절 5초 뒤 3인칭으로 물러나는 것처럼(S15P21B202-259) 카메라가 스스로 시점을
            // 바꾸는 경우가 있다. 기절 중에도 돌아야 하므로 아래 조기 반환보다 앞이다.
            RefreshDollVisibilityIfViewChanged();

            // Release가 예약한 조인트 제거가 끝난 다음 틱에 속도를 넣어야 투척이 구속에 먹히지 않는다.
            if (pendingThrowBody != null)
            {
                if (!pendingThrowBody.isKinematic) pendingThrowBody.linearVelocity = pendingThrowVelocity;
                pendingThrowBody = null;
            }

            // 던져진 동료도 같은 이유로 한 틱 늦춘다(S15P21B202-519). 잡고 있던 손의
            // 조인트가 사라지기 전에 속도를 넣으면 구속이 그것을 먹는다.
            if (pendingTossVictim != null)
            {
                pendingTossVictim.LaunchTossed(pendingTossVelocity, pendingTossCharge);
                pendingTossVictim = null;
            }

            // 몸이 탈것에 붙어 있으면 보행 물리를 돌리지 않는다. 돌리면 부양·이동력이
            // 좌석과 싸운다. 운반 장치는 제 속도를 알려 주고, 우주선은 강체에서 읽는다.
            if (IsRiding)
            {
                UpdateFloating();
                // 적어 둔 상대 자세로 몸을 옮긴다 (S15P21B202-434). 보행 물리를 건너뛰는
                // 이 분기 안에 둔다 — 걷기와 좌석이 같은 스텝에 몸을 두 번 옮기면 안 된다.
                HoldSeat();

                Grounded = true;
                GroundDistance = Tuning.RideHeight;

                if (MountedVehicle != null)
                {
                    GroundVelocity = MountedVehicle.HorizontalVelocity;
                    Speed = MountedVehicle.HorizontalSpeed;
                }
                else if (RidingBody != null)
                {
                    var flat = RidingBody.linearVelocity;
                    flat.y = 0f;
                    GroundVelocity = flat;
                    Speed = flat.magnitude;
                }
                else
                {
                    GroundVelocity = Vector3.zero;
                    Speed = 0f;
                }

                AirTime = 0f;
                Thrusting = false;
                Ragdolled = false;
                return;
            }

            water = DrainageWater.Sample(Doll.Hips.position);
            Swimming = water != null && !Hanging && !WorkstationMovementLocked
                && water.CanSwim(Doll.Hips.position, Swimming);
            if (Swimming) fallPeakFootHeight = FootPosition.y;
            ProbeGround();
            UpdateVerticalSpeed(Time.fixedDeltaTime);

            // 기절·던져짐 중에는 조작이 없다. 넉백은 그대로 받는다 — 운석에 굴러다녀야
            // 한다(기획 §26-13 의 긴장이 거기서 나온다).
            //
            // **던져져 날아가는 동안도 같다**(S15P21B202-550). 예전에는 `Tossed` 가
            // 카메라 플래그 하나였고 몸에는 아무 일도 하지 않았다 — 실측으로 던져진 몸과
            // 그냥 같은 속도로 띄운 몸의 기울기 계열이 완전히 같았고(0.7 1.3 1.5 1.6 1.6
            // 1.5), 공중 가로 이동도 1.804m 로 같았다. 즉 던져진 사람이 선 자세로 날아가며
            // 평소처럼 조종했다.
            //
            // **여기서 고쳐야 한다.** `LaunchTossed` 에서 `Ragdolled = true` 를 한 번 써
            // 봐야 소용없다 — `UpdateSpeed` 가 매 틱 "머리가 골반보다 낮은가" 로 다시
            // 계산해서 지운다. 매 틱 유지되는 자리는 여기뿐이다.
            if (BodyOutOfControl)
            {
                UpdateFloating();
                Speed = 0f;
                Ragdolled = true;
                uprightBlend = 0f;
                Balance();
                return;
            }

            UpdatePosture();
            UpdateFloating();
            UpdateSpeed();
            Hover();
            Locomotion();
            Balance();
            DriveLegs();
            DriveArms();
        }

        /// <summary>
        /// 엎드리기 토글. <b>눌린 상태의 가장자리를 여기서 잡는다</b>(S15P21B202-616).
        ///
        /// <para>몸을 가져가는 것들이 끼어들면 자세가 풀린다 — 기절·던져짐·탑승·
        /// 매달림·부유. 이걸 안 풀면 기절해서 누운 사람이 폭풍 면제를 계속 받는다.</para>
        /// </summary>
        /// <summary>
        /// 몸을 가져가는 것이 끼어들면 자세를 푼다.
        ///
        /// <para><b><see cref="Tick"/> 의 맨 앞에서 부른다.</b> 보행 계산 옆에 두면
        /// 기절·탑승 분기가 그 전에 <c>return</c> 해서 <see cref="Prone"/> 이 켜진 채
        /// 남는다 — 기절한 사람이 폭풍 면제를 계속 받는다(검증이 잡은 자리).</para>
        /// </summary>
        void ReleaseProneIfTaken()
        {
            if (!Prone) return;
            if (BodyOutOfControl || IsRiding || Hanging || Floating || MountedVehicle != null)
                Prone = false;
        }

        void UpdatePosture()
        {
            bool held = input.ProneHeld;
            bool pressed = held && !proneHeldLast;
            proneHeldLast = held;

            if (!pressed) return;
            if (Prone) { Prone = false; return; }
            // 땅에 붙어 있을 때만 엎드린다. 공중에서 엎드리면 떨어지는 동안 면제가 붙는다.
            if (Grounded && !BodyOutOfControl && !IsRiding && !Hanging && !Floating
                && MountedVehicle == null) Prone = true;
        }

        // ── 바닥 ────────────────────────────────────────────────
        /// <summary>
        /// 골반의 세로 이동을 속도로 바꾼다. <see cref="VerticalSpeed"/> 참조.
        ///
        /// <para>첫 틱에는 이전 값이 없으므로 0 으로 둔다 — 안 그러면 태어나는
        /// 순간 엄청난 속도가 한 번 튀고, 그 프레임의 공중 자세가 튄다.</para>
        /// </summary>
        void UpdateVerticalSpeed(float dt)
        {
            float y = Doll.Hips.position.y;
            VerticalSpeed = hipsYKnown && dt > 0f ? (y - lastHipsY) / dt : 0f;
            lastHipsY = y;
            hipsYKnown = true;
            if (Grounded) JumpedUp = false;
        }

        void ProbeGround()
        {
            bool wasGrounded = Grounded;
            float fallSpeed = Mathf.Max(0f, -Doll.Hips.linearVelocity.y);

            groundBody = null;
            GroundVelocity = Vector3.zero;
            GroundDistance = float.MaxValue;

            Vector3 origin = Doll.Hips.position;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, Tuning.GroundProbe,
                                            RagdollBuilder.WorldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (rayHits[i].distance >= GroundDistance) continue;
                GroundDistance = rayHits[i].distance;
                groundBody = rayHits[i].collider.attachedRigidbody;
            }

            Grounded = GroundDistance < Tuning.RideHeight + 0.22f && jumpLock <= 0f;
            if (groundBody != null) GroundVelocity = groundBody.linearVelocity;

            float footHeight = FootPosition.y;
            if (!Grounded)
            {
                fallPeakFootHeight = Mathf.Max(fallPeakFootHeight, footHeight);
            }
            else if (!wasGrounded)
            {
                float fallHeight = Mathf.Max(0f, fallPeakFootHeight - footHeight);
                ApplyFallImpact(fallHeight);
                fallPeakFootHeight = footHeight;
            }
            else
            {
                fallPeakFootHeight = footHeight;
            }

            if (Grounded) AirTime = 0f; else AirTime += Time.fixedDeltaTime;

            // 착지 먼지 — 진공이라 흩어지지 않고 그대로 포물선을 그린다
            if (Grounded && !wasGrounded && fallSpeed > 1.6f)
                Puff.Regolith(FootPosition, Mathf.Clamp01(fallSpeed / 7f));
        }

        /// <summary>
        /// 착지를 <b>자세</b>로 처리한다(기획 §20.2, S15P21B202-334).
        ///
        /// <code>
        /// 낮음        정상 착지
        /// 중간        잠시 휘청인다
        /// 높음        넘어지고 1~2초 뒤 스스로 일어난다
        /// 매우 높음   + <b>머리부터</b> 충돌이면 기절할 수 있다
        /// </code>
        ///
        /// <b>체력을 깎지 않는다.</b> 예전에는 2.75m·3.8m/s 를 넘으면
        /// <see cref="ApplyDamage"/> 로 체력을 깎았고, 누적되면 <see cref="GoDown"/> 까지
        /// 갔다. §20.1 은 "체력, 사망과 영구 탈락은 없다" 이고 §20.2 는 기절 조건을
        /// <b>"매우 높은 곳에서 머리부터"</b> 하나로 못박는다.
        ///
        /// 저중력에서 이 차이가 크다. 우주선 4층 접합부가 5.71m 에 있어 공중 용접
        /// (§14.3)을 하다 저중력장을 벗어나면 그 높이에서 떨어지는데, 예전 규칙에서는
        /// 그것이 상시 체력 소모였다.
        ///
        /// <b>넘어짐은 <see cref="KnockDown"/> 을 쓴다.</b> 그 메서드가 이미 "기절시키지
        /// 않고 체력도 깎지 않는다" 를 §12.2·§18.3·§22.4 세 곳에 제공하고 있으므로,
        /// 낙하가 네 번째 호출자가 된다 — 넘어지는 규칙을 두 번 쓰지 않는다.
        /// </summary>
        void ApplyFallImpact(float fallHeight)
        {
            if (Downed) return;
            if (fallHeight < PhysicsScale.FallStaggerHeight) return;

            if (fallHeight < PhysicsScale.FallKnockHeight)
            {
                Stagger(PhysicsScale.FallStaggerSeconds);
                return;
            }

            // 매우 높은 곳에서 <b>머리부터</b> 충돌한 경우만 기절이다(§20.2). 높이만으로는
            // 기절하지 않는다 — "일반 몸통 충돌만으로 기절하지 않는다" 가 같은 절에 있다.
            if (fallHeight >= PhysicsScale.FallStunHeight && LandedHeadFirst())
            {
                GoDown("높은 곳에서 머리부터 떨어졌다");
                return;
            }

            KnockDown(-faceDir, PhysicsScale.FallKnockSpeed);
        }

        /// <summary>
        /// 착지 순간 머리가 골반보다 낮았는가.
        ///
        /// <see cref="UpdateSpeed"/> 가 자빠짐을 판정하는 식과 <b>같은 것을 쓴다</b> —
        /// "머리가 골반보다 낮으면 자빠진 것" 이라는 규칙을 두 번 적지 않는다.
        /// </summary>
        bool LandedHeadFirst() =>
            Doll != null && Doll.Head != null && Doll.Hips != null
            && Doll.Head.position.y < Doll.Hips.position.y + 0.12f;

        /// <summary>
        /// 잠시 휘청이게 한다(기획 §20.2 "중간 높이에서는 잠시 휘청인다").
        ///
        /// <b>조종을 잃지 않는다.</b> 그것이 넘어짐(<see cref="KnockDown"/>)과 휘청임을
        /// 가르는 것이다 — <see cref="Ragdolled"/> 를 켜지 않으므로 점프도 걷기도 된다.
        /// 세우는 힘만 잠깐 약해져 몸이 흔들린다.
        /// </summary>
        public void Stagger(float seconds)
        {
            if (Downed || seconds <= 0f) return;

            staggerSeconds = Mathf.Max(staggerSeconds, seconds);
        }

        /// <summary>휘청임이 남은 시간(초). 0 보다 크면 세우는 힘이 약하다.</summary>
        public bool Staggering => staggerSeconds > 0f;

        float staggerSeconds;

        void UpdateSpeed()
        {
            Vector3 v = Doll.Hips.linearVelocity;
            v.y = 0f;
            Speed = v.magnitude;

            if (jumpLock > 0f) jumpLock -= Time.fixedDeltaTime;
            if (regrabLock > 0f) regrabLock -= Time.fixedDeltaTime;

            if (staggerSeconds > 0f) staggerSeconds -= Time.fixedDeltaTime;

            // 머리가 골반보다 낮으면 완전히 자빠진 것 — 잠깐 조종을 놓았다가 천천히 일어난다
            // 엎드리면 머리가 골반보다 낮아 이 검사가 **저절로 켜진다.** 그러면
            // Blocked 에 걸려 기어가기가 죽는다 — 매달림과 같은 이유로 뺀다.
            bool down = Doll.Head.position.y < Doll.Hips.position.y + 0.12f;
            Ragdolled = down && !Hanging && !Prone;

            // 휘청임은 그 사이 단계다(§20.2, S15P21B202-334). 세우는 힘만 약해지고
            // Ragdolled 는 켜지 않으므로 걷기와 점프가 유지된다.
            float want = Ragdolled ? 0.18f
                       : Staggering ? PhysicsScale.FallStaggerUpright
                       : 1f;
            uprightBlend = Mathf.MoveTowards(uprightBlend, want, Time.fixedDeltaTime * (want > uprightBlend ? 0.8f : 4f));
        }

        // ── 서 있기 ─────────────────────────────────────────────
        /// <summary>
        /// 저중력장 안인지 보고 부유 상태를 갱신한다. 부유 중이면 세로 속도를 직접 준다.
        ///
        /// <b>부유하는 동안 인형의 중력을 끈다.</b> 처음에는 세로 속도만 덮어쓰면 될 줄 알았는데,
        /// 상승 가속(2m/s ÷ 0.5초 = 4m/s²)에서 행성 중력(2.8m/s²)을 빼면 순 상승이 1.2m/s² 뿐이라
        /// 관절과 접촉 저항에 먹혀 아예 뜨지 않았다(계측: 2초에 -0.01m). 중력을 끄면
        /// 기획이 정한 "0.5초 관성" 도 그대로 성립한다.
        ///
        /// 영역을 벗어나거나 저중력장이 꺼지면 중력을 다시 켜므로 그 즉시 떨어진다(기획 §14.3).
        ///
        /// 접지를 강제로 끄는 이유는 <see cref="Hover"/> 때문이다. 지면 가까이에서 떠 있으면
        /// 접지로 잡혀 지면 스프링이 부유를 밀어 올린다.
        ///
        /// 낙하 기준 높이도 계속 갱신한다. 떠 있는 것은 떨어지는 것이 아니므로,
        /// 그냥 두면 부유했다 내려올 때 그 높이차가 추락으로 집계된다.
        /// </summary>
        void UpdateFloating()
        {
            var field = Parts.LowGravityField.FindContaining(Doll.Hips.position);
            if (IsRiding) { water = null; Swimming = false; }
            var wants = (field != null || Swimming) && !Hanging && !IsRiding;

            // 들고 날 때 인형의 중력을 끄고 켠다. 상태가 바뀔 때만 건드려 매 스텝 강체를
            // 훑지 않는다.
            if (wants != floatingGravityOff)
            {
                floatingAutoLift = wants && !Swimming;
                if (wants && field != null)
                    floatingLiftTarget = Mathf.Min(Doll.Hips.position.y + 1.5f,
                        field.Origin.position.y - 1.2f);
                for (int i = 0; i < Doll.Bodies.Count; i++)
                    if (Doll.Bodies[i] != null)
                        Doll.Bodies[i].useGravity = !wants;

                floatingGravityOff = wants;
            }

            Floating = wants;
            if (!Floating) return;

            Grounded = false;
            GroundDistance = float.MaxValue;
            GroundVelocity = Vector3.zero;
            AirTime = 0f;
            fallPeakFootHeight = Doll.Hips.position.y - RagdollBuilder.HipsY;

            // Space 로 올라가고 Ctrl 로 내려간다. 탑승 중 고도 조작과 같은 두 키다.
            float wish = BodyOutOfControl ? 0 : (input.Jump ? 1f : 0f) - (input.ArmsUp ? 1f : 0f);
            if (wish != 0f) floatingAutoLift = false;
            float target = wish * PhysicsScale.FieldFloatVerticalSpeed;
            if (floatingAutoLift)
                target = Mathf.Clamp((floatingLiftTarget - Doll.Hips.position.y) * 3f,
                    0f, PhysicsScale.FieldFloatVerticalSpeed);
            float rate = PhysicsScale.FieldFloatVerticalSpeed / PhysicsScale.FieldFloatStopSeconds;
            if (Swimming)
            {
                floatingAutoLift = false;
                target = water.VerticalTarget(Doll.Hips.position.y, wish);
                rate = 4f;
            }

            for (int i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                Vector3 v = body.linearVelocity;
                if (Swimming) v.y = Mathf.Clamp(v.y, -2f, 2f);
                v.y = Mathf.MoveTowards(v.y, target, rate * Time.fixedDeltaTime);
                body.linearVelocity = v;
            }
        }

        void Hover()
        {
            if (!Grounded || Hanging) return;

            // 엎드리면 낮게 뜬다. **세우는 힘(uprightBlend)은 그대로 둔다** —
            // 그걸 0 으로 하면 래그돌로 무너져서 방향을 못 잡는다.
            float ride = Prone ? ProneRideHeight : Tuning.RideHeight;
            float offset = ride - GroundDistance;
            if (offset < -0.2f) return;

            float vy = Doll.Hips.linearVelocity.y - (groundBody != null ? groundBody.linearVelocity.y : 0f);
            float f = offset * Tuning.RideSpring - vy * Tuning.RideDamp;
            f = Mathf.Clamp(f, 0f, Tuning.RideMaxForce) * uprightBlend;

            Doll.Hips.AddForce(Vector3.up * f, ForceMode.Force);
            if (groundBody != null)
                groundBody.AddForceAtPosition(Vector3.down * f, Doll.Hips.position, ForceMode.Force);
        }

        /// <summary>
        /// 조작의 기준이 되는 시선. 내 화면에서는 카메라가 기준이고, 카메라가 없는
        /// 서버에서는 클라이언트가 입력에 실어 보낸 각도가 기준이다(-147).
        /// OrbitCamera 의 ViewRotation 과 같은 식이라 양쪽 결과가 어긋나지 않는다.
        /// </summary>
        Quaternion AimRotation
        {
            get
            {
                return Cam != null
                    ? Quaternion.Euler(Cam.Pitch, Cam.Yaw, 0f)
                    : Quaternion.Euler(input.AimPitch, input.AimYaw, 0f);
            }
        }

        /// <summary>수평면에 눕힌다. 길이가 없으면 대신 쓸 방향을 준다.</summary>
        static Vector3 Flatten(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
        }

        // ── 조준 (카메라가 없어도 쓸 수 있는 창구) ──────────────
        //
        // 상호작용 코드가 카메라를 직접 들여다보면 서버에서 돌지 않는다 — 카메라는
        // 각자 PC 에만 있기 때문이다(S15P21B202-149). 그래서 "어디를 보고 있나" 를
        // 여기 한 곳으로 모은다. 내 화면에서는 카메라가, 서버에서는 클라이언트가
        // 보낸 각도가 답을 낸다.

        /// <summary>조준이 시작되는 지점. 카메라가 있으면 눈, 없으면 머리다.</summary>
        public Vector3 AimOrigin
        {
            get
            {
                if (Cam != null) return Cam.transform.position;

                // 카메라가 없는 서버에서는 클라이언트가 보낸 시작점을 쓴다. 그래야 그 사람의
                // 화면과 서버가 같은 곳을 겨냥한다 — 머리로 대신하면 카메라와 몇 미터
                // 어긋나 화면에 보이는 대상을 잡지 못한다.
                //
                // 다만 그대로 믿지는 않는다. 클라이언트가 보내는 값이라 몸에서 너무 멀면
                // 엉뚱한 곳에서 집는 셈이 된다. 상식 밖이면 머리로 되돌린다.
                Vector3 head = Doll != null && Doll.Head != null ? Doll.Head.position : Position;
                if (input.AimFrom == Vector3.zero) return head;

                return (input.AimFrom - head).sqrMagnitude <= MaxAimOffset * MaxAimOffset
                    ? input.AimFrom : head;
            }
        }

        /// <summary>조준 시작점이 몸에서 떨어져 있어도 되는 한계(미터). 카메라가 뒤로 물러난 거리보다 넉넉하게.</summary>
        const float MaxAimOffset = 8f;

        /// <summary>조준 방향(위아래 포함).</summary>
        public Vector3 AimForward { get { return AimRotation * Vector3.forward; } }

        /// <summary>바닥에 눕힌 앞 방향. 이동·정렬 기준이다.</summary>
        public Vector3 PlanarForward { get { return Flatten(AimRotation * Vector3.forward, Vector3.forward); } }

        /// <summary>바닥에 눕힌 오른쪽 방향.</summary>
        public Vector3 PlanarRight { get { return Flatten(AimRotation * Vector3.right, Vector3.right); } }

        /// <summary>WASD 입력을 지금 보는 방향 기준의 이동 벡터로 바꾼다.</summary>
        public Vector3 RelativeMove(Vector2 move)
        {
            if (Cam != null) return Cam.CameraRelativeMove(move);

            if (move.sqrMagnitude > 1f) move.Normalize();
            Vector3 result = PlanarRight * move.x + PlanarForward * move.y;
            return result.sqrMagnitude > 1f ? result.normalized : result;
        }

        void Locomotion()
        {
            Vector3 fwd, right, wish;

            // 파츠를 밀고 있으면 **시점이 아니라 밀기 방향**이 이동 기준이다
            // (기획 §10.2 "캐릭터는 기존 밀기 방향을 유지한다"). 그래서 3인칭으로
            // 물러난 카메라를 좌우로 돌려도 미는 방향이 흔들리지 않는다.
            //
            // 서버에도 카메라가 없으므로 이 분기가 양쪽에서 같은 결과를 낸다.
            if (haulBasisSet)
            {
                var basis = Quaternion.Euler(0f, haulBasisYaw, 0f);
                fwd = Flatten(basis * Vector3.forward, Vector3.forward);
                right = Flatten(basis * Vector3.right, Vector3.right);

                Vector2 pushMove = input.Move.sqrMagnitude > 1f
                    ? input.Move.normalized : input.Move;
                wish = right * pushMove.x + fwd * pushMove.y;
            }
            else if (Cam != null)
            {
                fwd = Cam.PlanarForward;
                right = Cam.PlanarRight;
                wish = Cam.CameraRelativeMove(input.Move);
            }
            else
            {
                // 서버에는 카메라가 없다. 클라이언트가 함께 보낸 시선으로 같은 기준을
                // 세운다(-147). 이것이 없으면 W 를 눌러도 늘 월드 +Z 로만 걷는다.
                fwd = Flatten(AimRotation * Vector3.forward, Vector3.forward);
                right = Flatten(AimRotation * Vector3.right, Vector3.right);

                Vector2 move = input.Move.sqrMagnitude > 1f ? input.Move.normalized : input.Move;
                wish = right * move.x + fwd * move.y;
            }
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // 파츠 밀기(기획 §13)가 이 값을 본다. MovementDirectionLocal 은 실제 속도를
            // 우선하는 로컬 좌표라 쓸 수 없다 — 500kg 짜리 파츠에 막혀 제자리걸음일 때도
            // "파츠 쪽으로 밀고 있다" 는 것을 알아야 한다.
            MoveWish = wish;

            float top = Floating
                ? PhysicsScale.FieldFloatSpeed
                : (input.Run ? Tuning.RunSpeed : Tuning.WalkSpeed);

            // 동료를 끌고 가는 동안 느려진다(기획 §20.4). 34.9kg 을 저중력에서 끌고
            // 가는 것이라 그냥 걷는 것과 같으면 안 된다.
            //
            // 기절한 사람뿐 아니라 <b>살아있는 사람을 끌 때도</b> 적용한다
            // (S15P21B202-442) — 무게는 같고, 이것이 없으면 잡은 쪽이 일방적으로
            // 이겨서 잡힌 쪽이 빠져나갈 방법이 없다.
            if (CarryingTeammate) top *= PhysicsScale.DownedCarrySpeedScale;
            // 파츠를 미는 동안 느려진다 (S15P21B202-667). 이것이 없으면 파츠는 0.6m/s 로
            // 제한되는데 사람은 2.9m/s 로 걸어 **두고 앞서 간다** — 손으로 잡아도
            // Hand.BraceTo 는 조인트를 만들지 않아 붙잡아 주지 않는다.
            // 파츠를 미는 동안 느려진다 (S15P21B202-667). **파츠가 낼 수 있는 속도에
            // 맞춘다** — 고정 배율이면 넷이 밀 때(1.5m/s) 전원이 뒤처져 접촉이 끊긴다.
            // 여유를 조금 둬야 따라붙으면서 밀 수 있다.
            if (Hauling && HaulTopSpeed > 0f)
                top = Mathf.Min(top, HaulTopSpeed * PhysicsScale.PartPushKeepUpMargin);
            // 엎드리면 기어간다. 다른 감속과 곱하지 않고 **상한으로** 둔다 —
            // 곱하면 달리기 키를 누른 채 기어가는 것이 더 빨라진다.
            if (Prone) top = Mathf.Min(top, CrawlSpeed);
            // S15P21B202-577: tall flora uses 0.45 of target speed (55% reduction),
            // including floating/carrying; overlapping plants do not stack.
            // Acceptance procedure: docs/records/2026-09-17-577-review-resolution.md.
            top *= PlantRustle.SampleMovementMultiplier(Doll.Hips.position, _vegetationHits);
            if (Swimming) top = Mathf.Min(top, DrainageWater.SwimSpeed);
            else if (water != null) top *= water.MovementScale(Doll.Hips.position);
            Vector3 targetVel = wish * top;
            if (water != null) targetVel += water.Current * (Swimming ? 1 : .35f);
            targetVel.x += GroundVelocity.x;
            targetVel.z += GroundVelocity.z;

            // **남이 나를 잡고 있으면 매달린 것과 같다**(S15P21B202-442).
            //
            // DriveMovementVelocity 는 인형의 모든 몸에 `linearVelocity = ...` 를
            // <b>직접 써 넣는다.</b> 그러면 조인트가 끌어온 속도가 매 스텝 지워진다 —
            // 입력이 없으면 목표 속도가 0 이므로 <b>제자리에 못을 박는 것</b>과 같다.
            //
            // 실측(2초 동안 끌기): 이 분기가 없으면 잡힌 사람이 <b>0.0785m</b> 움직인다.
            // 잡은 사람이 오히려 끌려간다. 기절한 동료가 끌려오는 이유는 래그돌이라
            // 이 코드가 돌지 않아서였다 — 살아있는 몸에는 그대로 적용된다.
            //
            // 매달릴 때 이미 같은 문제를 풀어 두었다("손 조인트가 이동을 결정해야
            // 하므로"). 같은 길을 쓴다.
            if (Hanging || GrabbedByOthers)
            {
                // 매달린 동안에는 손 조인트가 이동을 결정해야 하므로 기존 물리 제어를 유지한다.
                //
                // 잡힌 쪽에는 공중 제어가 아니라 지상 제어를 준다. 땅에 발을 딛고
                // 버티는 것이라 공중만큼 무력하면 줄다리기가 성립하지 않는다.
                float control = Hanging || !Grounded
                    ? Tuning.AirControl
                    : PhysicsScale.GrabbedGroundControl;

                // **잡힌 채 가만히 있으면 힘을 아예 넣지 않는다.**
                //
                // 목표 속도 0 을 향해 힘을 주는 것은 "가만히 있는 것" 이 아니라
                // "버티는 것" 이다. 그러면 손을 놓고 서 있기만 해도 끌려오지 않는다 —
                // 실측으로 2초에 0.2771m 밖에 안 움직였다. 버티기는 <b>실제로 반대
                // 방향을 입력했을 때만</b> 해야 줄다리기가 의도대로 성립한다.
                //
                // 매달릴 때는 예전 그대로다. 그쪽은 손 조인트에 체중이 걸려 있어
                // 감속까지 힘으로 잡아 주지 않으면 몸이 계속 흔들린다.
                var resisting = Hanging || wish.sqrMagnitude > 0.01f;
                if (resisting)
                {
                    Vector3 v = Doll.Hips.linearVelocity;
                    Vector3 dv = new Vector3(targetVel.x - v.x, 0f, targetVel.z - v.z);
                    Vector3 force = JointTools.Clamp(dv * Tuning.Accel * control, Tuning.AccelMax);
                    if (JointTools.IsFinite(force))
                    {
                        Doll.Hips.AddForce(force * 0.6f, ForceMode.Force);
                        Doll.Chest.AddForce(force * 0.4f, ForceMode.Force);
                    }
                }
            }
            else
            {
                DriveMovementVelocity(targetVel);
            }

            UpdateMovementDirection(wish, right, fwd);

            // 등짐의 자세제어 분사기가 공중에서 방향을 잡아준다는 설정
            Thrusting = !InWater && !Grounded && !Hanging && wish.sqrMagnitude > 0.04f;
            if (Thrusting)
            {
                thrustPuff -= Time.fixedDeltaTime;
                if (thrustPuff <= 0f)
                {
                    thrustPuff = 0.09f;
                    Puff.Thruster(Doll.Chest.position - Doll.Chest.transform.forward * 0.26f, -wish);
                }
            }

            // 고정못을 잡고 있으면 그쪽을 계속 본다(기획 §12.1 "몸을 뒤로 젖히고").
            // 이동 입력 쪽으로 돌면 뒤로 당기는 동안 고정못을 등지게 되고, 조준이 벗어나
            // 놓았을 때 다시 잡을 수 없다.
            var facingClamp = Vector3.zero;
            if (clampGrabbed && clampGrabTarget != null && Doll.Hips != null)
            {
                facingClamp = clampGrabTarget.transform.position - Doll.Hips.position;
                facingClamp.y = 0f;
            }

            Vector3 cameraFacing = fwd;
            if (facingClamp.sqrMagnitude > 0.02f)
                cameraFacing = facingClamp.normalized;

            // 춤 중에는 시점을 따라 돌지 않는다 (S15P21B202-596). 카메라는 그대로 돈다 —
            // 막는 것은 **몸**이지 둘러보는 것이 아니다.
            //
            // 고정못을 잡고 있으면 그쪽이 먼저다(위 §12.1). 그건 조작이 아니라 기획이
            // 요구하는 자세라서, 춤보다 앞선다.
            else if (DanceHolding)
                cameraFacing = Vector3.zero;

            if (cameraFacing.sqrMagnitude > 0.02f)
                faceDir = Vector3.Slerp(faceDir, cameraFacing,
                                        1f - Mathf.Exp(-7f * Time.fixedDeltaTime));

            // 점프 — 몸 전체에 같은 속도를 얹어야 인형이 통째로 뜬다
            if (input.Jump && Grounded && !Ragdolled && jumpLock <= 0f)
            {
                for (int i = 0; i < Doll.Bodies.Count; i++)
                {
                    var b = Doll.Bodies[i];
                    var bv = b.linearVelocity;
                    bv.y = Mathf.Max(bv.y, 0f) + Tuning.JumpSpeed;
                    b.linearVelocity = bv;
                }
                jumpLock = 0.30f;
                Grounded = false;
                JumpedUp = true;
                AnyJumped?.Invoke(this, FootPosition);
                Puff.Regolith(FootPosition, 0.45f);
            }
        }

        /// <summary>
        /// 실제 수평 속도를 카메라 기준 좌우·전후 성분으로 바꿔 보행 포즈에 전달한다.
        /// 속도가 아직 가속 중인 첫 프레임에는 입력 방향을 사용해 애니메이션이 늦게
        /// 시작하지 않게 하고, 키를 놓은 뒤에는 남은 물리 관성도 자연스럽게 반영한다.
        /// </summary>
        void UpdateMovementDirection(Vector3 wish, Vector3 right, Vector3 forward)
        {
            Vector3 actual = Doll != null && Doll.Hips != null ? Doll.Hips.linearVelocity : Vector3.zero;
            actual.y = 0f;

            Vector3 movement = actual.sqrMagnitude > 0.0025f ? actual : wish;
            if (movement.sqrMagnitude < 0.0001f)
            {
                movementDirectionLocal = Vector2.zero;
                return;
            }

            movementDirectionLocal = new Vector2(Vector3.Dot(movement, right),
                                                 Vector3.Dot(movement, forward));
            movementDirectionLocal.Normalize();
        }

        /// <summary>
        /// 모든 몸 부위의 수평 속도를 같은 목표로 맞춰 이동 중 몸통이 뒤처지거나
        /// 관성 때문에 미끄러지는 문제를 막는다. 회전과 수직 속도는 물리 조인트에 맡긴다.
        /// </summary>
        void DriveMovementVelocity(Vector3 targetVelocity)
        {
            Vector3 target = new Vector3(targetVelocity.x, 0f, targetVelocity.z);

            // 부유 중에는 기획이 정한 관성으로 선다 — "입력을 놓으면 약 0.5초 관성 후 정지".
            float floatRate = PhysicsScale.FieldFloatSpeed / PhysicsScale.FieldFloatStopSeconds;

            float acceleration = Floating ? floatRate
                : (Grounded ? Tuning.GroundAcceleration : Tuning.AirAcceleration);
            float deceleration = Floating ? floatRate
                : (Grounded ? Tuning.GroundDeceleration : Tuning.AirAcceleration);

            for (int i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null) continue;

                Vector3 current = body.linearVelocity;
                Vector3 planar = new Vector3(current.x, 0f, current.z);
                float rate = target.sqrMagnitude < planar.sqrMagnitude ? deceleration : acceleration;
                planar = Vector3.MoveTowards(planar, target, rate * Time.fixedDeltaTime);
                body.linearVelocity = new Vector3(planar.x, current.y, planar.z);
            }
        }

        /// <summary>
        /// 균형 조인트의 감쇠 계수. 스프링과 달리 <b>바닥이 있다</b> — 그리고 그 바닥을
        /// 언제 걷느냐가 "구르는가" 를 가른다 (S15P21B202-551).
        ///
        /// <para><b>왜 바닥이 있었나.</b> 감쇠가 0 이면 한 번 흔들린 몸이 멈추지 않는다.
        /// 휘청임(<see cref="Staggering"/>)과 평소 공중(<c>k = 0.26</c>)은 조종이 살아
        /// 있는 상태라 그 바닥이 필요하다.</para>
        ///
        /// <para><b>왜 걷어야 하나.</b> 균형 조인트는 <c>connectedBody = null</c> 이라
        /// <b>세계</b>를 기준으로 붙잡는다. 그래서 이 감쇠는 세계에 대한 회전을 통째로
        /// 먹는다 — 스프링이 0 이라 "세우지는" 않으면서 "구르지도 못하게" 한다.
        /// 실측(각속도 12 rad/s, 1.2초, 골반 기울기): 지금 그대로 8.5도 ·
        /// 강체 각저항을 0 으로 해도 8.7도 · <b>이 감쇠를 걷으면 179.6도</b>.
        /// 강체의 각저항(0.70)은 범인이 아니었다.</para>
        ///
        /// <para>그래서 <b>조종을 잃었을 때만</b> 걷는다. 기절과 던져짐이 함께 해당된다 —
        /// 둘 다 "몸이 내 것이 아닌" 상태이고 <c>-550</c> 이 이미 한 조건으로 묶었다.</para>
        /// </summary>
        float DampFloor(float k) => BodyOutOfControl ? k : Mathf.Max(k, 0.25f);

        void Balance()
        {
            // 엎드린 자세는 **목표 회전**으로 만든다(S15P21B202-616). 세우는 힘을
            // 끄는 쪽이 싸지만 그건 곧 래그돌이고, 그러면 기어가는 방향을 못 잡는다.
            Quaternion desired = Quaternion.LookRotation(faceDir, Vector3.up);
            if (Prone) desired *= Quaternion.AngleAxis(PronePitch, Vector3.right);

            // 공중에서는 힘을 빼야 팔다리가 허우적거린다. 저중력이라 그 시간이 길다.
            float k = (Grounded ? 1f : 0.26f) * uprightBlend;
            if (Hanging) k = 0.40f;

            JointTools.SetDrive(Doll.Balance,
                                Tuning.UprightSpring * k,
                                Tuning.UprightDamp * DampFloor(k),
                                Tuning.UprightMaxTorque);
            JointTools.SetTargetLocalRotation(Doll.Balance, desired, Quaternion.identity);

            // 달릴수록 앞으로 기운다
            float lean = Mathf.Clamp(Speed / Tuning.RunSpeed, 0f, 1f) * 11f;
            JointTools.SetTargetLocalRotation(Doll.ChestJoint,
                                              Quaternion.AngleAxis(lean, Vector3.right),
                                              Quaternion.identity);

            // 고개는 카메라 피치를 조금 따라간다 (서버에서는 클라이언트가 보낸 피치)
            float pitch = Mathf.Clamp(Cam != null ? Cam.Pitch : input.AimPitch, -26f, 26f);
            var sequence = GameSession.Instance != null ? GameSession.Instance.Sequence : null;
            if (sequence != null) pitch = Mathf.Lerp(pitch, -45f, sequence.LookUpWeight);
            JointTools.SetTargetLocalRotation(Doll.HeadJoint,
                                              Quaternion.AngleAxis(pitch * 0.5f, Vector3.right),
                                              Quaternion.identity);
        }

        // ── 걷기 ────────────────────────────────────────────────
        void DriveLegs()
        {
            bool walking = Grounded && !Ragdolled && Speed > 0.40f;
            var previousPhase = StepPhase;

            if (walking)
            {
                float rate = Mathf.Clamp(Speed / Tuning.WalkSpeed, 0.55f, 1.8f);
                StepPhase += Time.fixedDeltaTime * (Mathf.PI * 2f / Tuning.StepPeriod) * rate;
                if (StepPhase > Mathf.PI * 2f) StepPhase -= Mathf.PI * 2f;

                if (StepPhase < previousPhase)
                    EmitFootstep(Doll.LeftLeg != null ? Doll.LeftLeg.Foot : null);
                if (previousPhase < Mathf.PI && StepPhase >= Mathf.PI)
                    EmitFootstep(Doll.RightLeg != null ? Doll.RightLeg.Foot : null);
            }
            else
            {
                StepPhase = Mathf.MoveTowardsAngle(StepPhase * Mathf.Rad2Deg, 0f,
                                                   240f * Time.fixedDeltaTime) * Mathf.Deg2Rad;
            }

            float swing = walking ? Mathf.Lerp(22f, 40f, Mathf.Clamp01(Speed / Tuning.RunSpeed)) : 0f;
            PoseLeg(Doll.LeftLeg, StepPhase, swing, walking);
            PoseLeg(Doll.RightLeg, StepPhase + Mathf.PI, swing, walking);
        }

        void EmitFootstep(Rigidbody foot)
        {
            if (foot == null) return;
            AnyFootstep?.Invoke(this, foot.position,
                                Mathf.Clamp01(Speed / Mathf.Max(0.01f, Tuning.RunSpeed)));
        }

        void PoseLeg(Leg leg, float phase, float swing, bool walking)
        {
            float thigh, knee;
            float lateral;

            if (!Grounded && !walking)
            {
                // 저중력에서 오래 떠 있다 — 다리를 살짝 웅크리면 '둥실' 하는 그림이 된다
                thigh = -14f; knee = 34f; lateral = 0f;
            }
            else if (!walking)
            {
                thigh = 0f; knee = 4f; lateral = 0f;             // 서 있을 땐 곧게
            }
            else
            {
                float forward = Mathf.Clamp(movementDirectionLocal.y, -1f, 1f);
                float side = Mathf.Clamp(movementDirectionLocal.x, -1f, 1f);

                // 전후 걸음과 좌우 걸음을 같은 주기로 합성한다. S는 전진 스텝을
                // 반전하고, A/D는 고관절의 Z축과 발목 보정으로 옆걸음 그림을 만든다.
                thigh = -Mathf.Sin(phase) * swing * forward;
                lateral = Mathf.Sin(phase) * Tuning.SideStepSwing * side;
                knee = Mathf.Max(0f, Mathf.Sin(phase + 1.1f))
                       * (swing * 1.7f * Mathf.Abs(forward)
                          + Tuning.SideStepKneeSwing * Mathf.Abs(side));
            }

            if (Ragdolled) { thigh *= 0.2f; knee *= 0.2f; lateral *= 0.2f; }

            Quaternion hipPose = Quaternion.AngleAxis(lateral, Vector3.forward)
                                 * Quaternion.AngleAxis(thigh, Vector3.right);
            JointTools.SetTargetLocalRotation(leg.Hip, hipPose, leg.HipStart);
            JointTools.SetTargetLocalRotation(leg.Knee, Quaternion.AngleAxis(knee, Vector3.right), leg.KneeStart);
            Quaternion anklePose = Quaternion.AngleAxis(
                                       Mathf.Clamp(-thigh * 0.35f, -20f, 20f), Vector3.right)
                                   * Quaternion.AngleAxis(
                                       Mathf.Clamp(-lateral * 0.45f, -12f, 12f), Vector3.forward);
            JointTools.SetTargetLocalRotation(leg.Ankle, anklePose, leg.AnkleStart);
        }

        // ── 팔 ──────────────────────────────────────────────────
        void DriveArms()
        {
            // 서버에는 카메라가 없다 — 클라이언트가 보낸 시선을 쓴다. 팔이 향하는
            // 곳이 곧 부품을 잡는 방향이라, 여기가 어긋나면 엉뚱한 것을 집는다.
            Vector3 aim = AimRotation * Vector3.forward;
            if (input.ArmsUp) aim = Vector3.Slerp(aim, Vector3.up, 0.65f).normalized;

            bool leftPressed = input.GrabLeft && !previousGrabLeft;
            bool rightPressed = input.GrabRight && !previousGrabRight;
            previousGrabLeft = input.GrabLeft;
            previousGrabRight = input.GrabRight;

            if (leftPressed)
                AnyHandGrabAttempted?.Invoke(this, LeftHand, LeftHand != null ? LeftHand.Aimed : null);
            if (rightPressed)
                AnyHandGrabAttempted?.Invoke(this, RightHand, RightHand != null ? RightHand.Aimed : null);

            if (toolThrowRegrabLock > 0f) toolThrowRegrabLock -= Time.fixedDeltaTime;

            // 도구는 반드시 양손이 같은 대상을 잡고 있어야 한다. 조인트가 하나라도 끊기면
            // 남은 손도 함께 놓아 다음 클릭에서 다시 양손으로 잡게 한다.
            if (toolHeld != null && (LeftHand.Held != toolHeld || RightHand.Held != toolHeld))
            {
                ReleaseHand(LeftHand);
                ReleaseHand(RightHand);
                ResetToolState();
            }

            if (toolHeld != null)
            {
                // Q를 놓는 순간에만 놓기/투척을 처리하고, 충전 중에는 두 손의 운반 자세를 유지한다.
                if (ProcessToolAction(aim))
                {
                    activeTool?.SetInput(new ToolInputFrame(), CreateToolAimContext(aim));
                    SyncHeldObjectOwnerCollision();
                    return;
                }
                SetActiveToolInput(aim, GetToolUtilityHeld());

                // 도구가 자세를 흔들 수 있게 한다(S15P21B202-243). 방망이는 휘두르는 동안
                // 조준선에서 벗어난 방향을 돌려주고, 그러면 도구가 카메라 주위로 호를
                // 그린다 — HoldToolPose 가 방향 하나로 위치와 회전을 정하기 때문이다.
                //
                // <b>입력보다 뒤에 묻는다.</b> SetActiveToolInput 이 이번 틱의 클릭을
                // 넘겨 스윙을 시작시키므로, 그 앞에서 물으면 한 틱 늦게 움직인다.
                Vector3 carryAim = activeTool != null ? activeTool.CarryAim(aim) : aim;

                HoldToolPose(carryAim);
                DriveArm(Doll.Left, true, carryAim);
                DriveArm(Doll.Right, true, carryAim);
                SyncHeldObjectOwnerCollision();
                return;
            }

            // 여기까지 왔으면 도구를 들고 있지 않다. 도구 충전 상태를 **던지기 분기보다
            // 먼저** 내린다 — 뒤에 두면 던지기가 early return 하면서 이 초기화를 건너뛰고,
            // HUD 가 ChargingTool 분기를 먼저 타 던지기 게이지를 가린다(!436 AI 리뷰).
            chargingTool = false;
            toolCharge = 0f;
            toolActionHeld = false;

            // 양손으로 동료를 잡고 있으면 그쪽이 Q 를 가져간다(S15P21B202-519).
            // 도구 경로 바로 뒤가 자리다 — 규칙이 같고(양손·Q·충전), 아래의 일반 손
            // 탐색으로 내려가면 잡은 채로 다른 것을 다시 잡으려 든다.
            if (ProcessTeammateToss(aim))
            {
                SyncHeldObjectOwnerCollision();
                return;
            }

            // 고정쇠가 조준 중이면 일반 손잡이/도구 탐색보다 우선한다.
            // 단순히 클릭한 상태가 아니라 실제 한 손이 고정쇠에 결합되어야 시작한다.
            bool clampFocused = CurrentInteractionTarget.Kind == PlayerInteractionKind.Clamp
                                && CurrentInteractionTarget.Clamp != null;
            bool clampInteractionActive = ProcessClampInteraction();
            if (clampInteractionActive || clampFocused)
            {
                DriveArm(Doll.Left, clampGrabHand == LeftHand && input.GrabLeft,
                         aim, null, false);
                DriveArm(Doll.Right, clampGrabHand == RightHand && input.GrabRight,
                         aim, null, false);
                if ((leftPressed || rightPressed) && !clampInteractionActive)
                {
                    LogClampRefusal(leftPressed, rightPressed);
                }
                SyncHeldObjectOwnerCollision();
                return;
            }

            var focusedGrabbable = CurrentInteractionTarget.Kind == PlayerInteractionKind.Grabbable
                ? CurrentInteractionTarget.Grabbable : null;
            bool hasFocusedInteraction = focusedGrabbable != null;

            // 좌클릭과 우클릭 중 어느 쪽이든 현재 단일 포커스가 Tool 역할이면 양손으로 함께 잡는다.
            if ((input.GrabLeft || input.GrabRight) && toolThrowRegrabLock <= 0f
                && TryGrabTool(aim, CurrentInteractionTarget))
            {
                DriveArm(Doll.Left, true, aim);
                DriveArm(Doll.Right, true, aim);
                if (leftPressed || rightPressed)
                    LogInteraction("양손 도구 집기", leftPressed, rightPressed, toolHeld, toolBody);
                SyncHeldObjectOwnerCollision();
                return;
            }

            DriveArm(Doll.Left, input.GrabLeft, aim, focusedGrabbable, hasFocusedInteraction);
            DriveArm(Doll.Right, input.GrabRight, aim, focusedGrabbable, hasFocusedInteraction);
            ClimbAssist();
            if (leftPressed) LogHandInteraction("좌클릭", LeftHand);
            if (rightPressed) LogHandInteraction("우클릭", RightHand);
            SyncHeldObjectOwnerCollision();
        }

        /// <summary>
        /// 고정쇠를 클릭하면 실제 손을 결합하고, 결합에 성공한 뒤에만 뒤로 이동 입력으로
        /// 뽑는 진행을 시작한다.
        /// </summary>
        bool ProcessClampInteraction()
        {
            if (ClampInteractor == null) return false;

            bool buttonHeld = input.GrabLeft || input.GrabRight;

            if (!buttonHeld)
            {
                ResetClampGrab();
                return false;
            }

            if (clampGrabTarget != null && clampGrabTarget.IsReleased)
            {
                ResetClampGrab();
                return false;
            }

            if (clampGrabbed)
            {
                bool activeButtonHeld = clampGrabHand == LeftHand
                    ? input.GrabLeft : clampGrabHand == RightHand && input.GrabRight;
                if (!activeButtonHeld) ResetClampGrab();
            }

            if (clampGrabbed)
            {
                PullGrabbedClamp();
                return true;
            }

            // 여러 후보 중 PlayerRig가 고른 하나만 고정쇠 입력 대상이다. 먼저 손을
            // 실제 고정쇠 지점에 결합하고, 결합 성공 뒤에만 puller를 등록한다.
            var target = CurrentInteractionTarget;
            if (target.Kind != PlayerInteractionKind.Clamp || target.Clamp == null
                || target.Clamp.IsReleased || !target.CanInteract) return false;

            var focus = target.Clamp;
            var hand = TryAttachClampHand(focus);
            if (hand == null) return false;

            if (!focus.TryJoinPull())
            {
                ReleaseHand(hand);
                return false;
            }

            clampGrabbed = true;
            clampGrabTarget = focus;
            clampGrabHand = hand;

            PullGrabbedClamp();
            return true;
        }

        Hand TryAttachClampHand(PartClamp target)
        {
            if (target == null || target.GripTarget == null) return null;

            var body = target.PartBody;
            var left = Doll != null ? Doll.Left : null;
            var right = Doll != null ? Doll.Right : null;

            // 양쪽을 동시에 누르면 왼손을 우선한다. 한 손이 이미 다른 것을 잡고
            // 있으면 오른손을 시도하되, 어느 손도 결합되지 않으면 pull을 시작하지 않는다.
            if (input.GrabLeft && left != null
                && CanReachClampHand(target, left, left.Hand, out var leftPoint)
                && left.Hand.GripToFixedAtPoint(target.GripTarget, body, leftPoint))
                return left.Hand;

            if (input.GrabRight && right != null
                && CanReachClampHand(target, right, right.Hand, out var rightPoint)
                && right.Hand.GripToFixedAtPoint(target.GripTarget, body, rightPoint))
                return right.Hand;

            return null;
        }

        /// <summary>
        /// 잡은 고정못을 뒤로 당긴다(기획 §12.1 "고정못 반대 방향으로 뒤로 이동해 뽑는다").
        ///
        /// <b>이동 입력의 방향을 본다. 실제로 움직인 거리를 보지 않는다.</b> 고정못이
        /// 몸을 붙들고 있으므로 아무리 밀어도 거의 제자리다 — 기획의 "발을 구르며
        /// 낑낑대는" 그림이 그것이다. 거리로 재면 진행이 사실상 멈춘다.
        ///
        /// 제자리에서 버튼만 누르고 있으면 진행되지 않는다. 예전 방식(마우스를 위로
        /// 16도)은 한 번 들어올린 뒤에는 가만히 있어도 시간이 흘렀다.
        /// </summary>
        void PullGrabbedClamp()
        {
            ClampPulling = false;
            // PartClamp.Pull() 은 마지막 고정못을 해제하면서 SpaceshipPart의 상태 이벤트를
            // 발생시킨다. Part_1이 견인 장치로 전환되면 컨트롤러가 포커스를 갱신하고,
            // 그 재진입 경로에서 ResetClampGrab()이 필드를 비울 수 있다. 한 틱의 작업
            // 대상은 로컬 참조로 고정해 Pull() 복귀 직후의 null 역참조를 막는다.
            var target = clampGrabTarget;
            if (target == null || Doll == null || Doll.Hips == null) return;

            // 카메라가 없다고 월드 축(+Z·+X)으로 대신하면 어디를 보든 같은 방향으로 끌게 된다
            // — 서버가 입력과 무관하게 +Z 로만 걷던 것과 같은 실수다(-147).
            // 조준 창구는 카메라가 있으면 카메라를, 없으면 클라이언트가 보낸 각도를 쓴다.
            var fwd = PlanarForward;
            var right = PlanarRight;
            var wish = RelativeMove(input.Move);
            wish.y = 0f;
            if (wish.sqrMagnitude < 0.0001f) return;

            // 고정못에서 몸을 향하는 수평 방향이 "뒤" 다.
            var away = Doll.Hips.position - target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;

            // 너무 멀어졌으면 손을 뗀다. 진행분은 남는다(기획 §12.1).
            // 잡은 뒤 범위를 다시 보지 않으면 고정못을 잡은 채 걸어서 멀어지면서
            // 계속 뽑히게 된다.
            if (away.magnitude > ClampInteractionRange)
            {
                ResetClampGrab();
                return;
            }

            var alignment = Vector3.Dot(wish.normalized, away.normalized);
            if (alignment < PhysicsScale.ClampPullAlignment) return;

            var wasLast = target.Part != null
                          && target.Part.RemainingRequiredClamps == 1;

            ClampPulling = target.Pull(Time.fixedDeltaTime);
            if (ClampPulling) TetherToClamp(away.normalized);

            if (!target.IsReleased) return;

            // 기획 §12.2 — "마지막 고정못이 빠지면 플레이어가 뒤로 넘어질 수 있다".
            // 뒤로 당기던 힘이 갑자기 풀리는 것이다. 넘어짐은 Balance 가 스스로 세운다.
            if (wasLast) SnapBackFromClamp(away.normalized);

            ResetClampGrab();
        }

        /// <summary>
        /// 당기는 동안 뒤로 밀려나는 속도를 묶는다.
        ///
        /// 기획 §12.1 의 "몸을 뒤로 젖히고 발을 구르며 낑낑대는" 은 뒤로 밀지만 거의
        /// 제자리라는 뜻이다. 고정못이 몸을 붙들고 있기 때문이다.
        ///
        /// 옆·앞 방향은 건드리지 않는다. 뽑는 것과 무관한 이동을 막으면 조작이 굳는다.
        /// </summary>
        void TetherToClamp(Vector3 away)
        {
            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                var velocity = body.linearVelocity;
                var outward = Vector3.Dot(velocity, away);
                if (outward <= PhysicsScale.ClampPullTetherSpeed) continue;

                body.linearVelocity = velocity - away * (outward - PhysicsScale.ClampPullTetherSpeed);
            }
        }

        /// <summary>마지막 고정못이 빠져 뒤로 넘어진다(기획 §12.2).</summary>
        void SnapBackFromClamp(Vector3 away) =>
            KnockDown(away, PhysicsScale.ClampSnapBackSpeed);

        /// <summary>
        /// 넘어뜨린다. <b>기절시키지 않는다.</b>
        ///
        /// 기절 처리를 새로 만들지 않는다 — <see cref="Balance"/> 가 이미 넘어진 자세를
        /// 스스로 세우므로, 충격만 주고 <see cref="Ragdolled"/> 를 켜면 구르다가 일어난다.
        /// 체력도 깎지 않는다. 기획이 §12.2("뒤로 넘어질 수 있다")와 §18.3("넘어지지만
        /// 기절하지는 않는다") 두 곳에서 기절과 구분하기 때문이다.
        ///
        /// <b>세 곳이 같은 것을 필요로 해서 여기로 모았다</b> — 마지막 고정못(§12.2),
        /// 방망이 직접 타격(§18.3), 그리고 발사 실패 사출(§22.4)이다. 사출은 조인트를
        /// 끊고 중력을 켜고 <c>faceDir</c> 까지 돌리므로 <see cref="EjectFromShip"/> 에
        /// 그대로 남겼다 — 하는 일이 이것보다 넓다.
        /// </summary>
        /// <summary>
        /// 탑승체가 받은 충격을 인형에도 <b>강체처럼</b> 같이 준다
        /// (S15P21B202-349).
        ///
        /// <b>이것이 없으면 탑승체가 돌지 못한다.</b> 조인트는 골반 하나만 묶으므로
        /// 나머지 뼈는 정지한 채 매달려 있고, 인형이 진자가 되어 회전 에너지를 먹는다.
        /// 측정값 — 무인 기체는 각속도 3.25 를 0.8초 동안 3.14 로 유지했는데, 탑승
        /// 중에는 <b>3.25 에서 0.03 으로</b> 죽었다.
        ///
        /// <b><see cref="KnockDown"/> 과 다르다.</b> 저것은 뼈마다 다른 힘을 주어 몸을
        /// 흩는 것이 목적이고, 이것은 <c>v = ω × r</c> 로 <b>한 덩어리로</b> 움직인다.
        /// </summary>
        /// <param name="velocityChange">덩어리 전체의 속도 변화(m/s).</param>
        /// <param name="spin">덩어리 전체의 각속도 변화(rad/s).</param>
        /// <param name="pivot">회전 중심. 탑승체의 무게중심을 넣는다.</param>
        public void RideAlong(Vector3 velocityChange, Vector3 spin, Vector3 pivot)
        {
            if (Doll == null) return;

            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                var arm = body.worldCenterOfMass - pivot;
                body.linearVelocity += velocityChange + Vector3.Cross(spin, arm);
                body.angularVelocity += spin;
            }
        }

        public void KnockDown(Vector3 direction, float speed)
        {
            if (!Shove(direction, speed)) return;

            Ragdolled = true;
        }

        /// <summary>
        /// 몸 전체를 한 방향으로 민다. <b>조종을 빼앗지 않는다.</b>
        ///
        /// <see cref="KnockDown"/> 에서 속도만 떼어낸 것이다. 둘을 가르는 것은
        /// <see cref="Ragdolled"/> 한 줄뿐이고, 운석의 약한 충격파(기획 §20.1
        /// "밀려나지만 바로 행동 가능")가 그 한 줄 없는 쪽을 쓴다.
        ///
        /// <b>폭발력으로는 안 된다</b>(2026-09-18). <c>AddExplosionForce</c> 는
        /// <c>ForceMode.Force</c> 라 한 물리 스텝에만 작용하고, <see cref="Balance"/> 가
        /// 그 다음 스텝에 되세워 버린다 — 기체가 폭발력으로 움직이지 않아
        /// <see cref="PhysicsScale.CraftImpactSpeed"/> 를 직접 주는 것과 같은 이유다.
        /// 눈에 보이게 하려면 속도를 얹어야 한다.
        ///
        /// <b>뼈마다 같은 속도를 준다.</b> 래그돌이 한 덩어리로 밀린다 —
        /// <see cref="RideAlong"/> 처럼 회전까지 맞추지는 않으므로 관절이 흔들리고,
        /// 그 흔들림이 "충격을 맞았다" 로 보인다.
        /// </summary>
        /// <returns>실제로 밀었으면 true. 기절 중이거나 인형이 없으면 false.</returns>
        public bool Shove(Vector3 direction, float speed)
        {
            if (Downed || Doll == null) return false;

            var away = direction;
            if (away.sqrMagnitude < 1e-6f) away = -faceDir;
            away.Normalize();

            for (var i = 0; i < Doll.Bodies.Count; i++)
            {
                var body = Doll.Bodies[i];
                if (body == null || body.isKinematic) continue;

                body.linearVelocity += away * speed;
            }

            return true;
        }

        /// <summary>
        /// 고정못에서 손을 뗀다. <b>진행분은 지우지 않는다</b> — 기획 §12.1 "좌클릭을
        /// 놓거나 운석에 맞아도 빠진 정도는 유지된다".
        /// </summary>
        /// <summary><see cref="RescueInteractor"/> 가 받는 것 — 구조 지점뿐이다.</summary>
        static bool OnlyDownedRescue(IInteractable candidate) => candidate is DownedRescue;

        /// <summary>
        /// 누르고 있는 동안 동료를 일으킨다 (S15P21B202-390).
        ///
        /// <para><b>이 함수가 없어서 기능이 돌지 않았다.</b> <see cref="DownedRescue"/> 는
        /// 홀드·진행분 누적·이어받기까지 완성돼 있었는데 <b>부르는 곳이 없었다</b> —
        /// 2026-09-10 QA 의 "사람 살리는 과정이 게이지로 표시되어야 하는 거 같다" 가
        /// 그것이다. 게이지가 없었던 것이 아니라 상호작용이 한 번도 시작되지 않았다.</para>
        ///
        /// <para><b>운반과 다른 버튼이다.</b> 대상은 같은 사람 하나이고, 좌클릭은
        /// <c>CarryTarget</c>(들어 옮기기), 이 키는 <c>구조 지점</c>(일으키기)에
        /// 작용한다. 그래서 <see cref="CurrentInteractionTarget"/> 을 쪼개지 않았다 —
        /// "대상 후보를 여러 시스템이 각각 고르면 화면에는 하나가 보여도 다른 대상이
        /// 작동한다" 는 <c>PlayerInteractionTarget</c> 의 경고가 그대로 적용된다.</para>
        /// </summary>
        void TickRescue()
        {
            if (RescueInteractor == null) return;

            ReportRescueCandidate();

            // 기절한 사람은 남을 일으킬 수 없다. 홀드 중이었으면 놓는다 —
            // 진행분은 남으므로 다른 사람이 이어받는다(DownedRescue.Release).
            if (Downed)
            {
                if (RescueInteractor.State == InteractorState.Holding)
                    LogRescue("중단", "내가 기절했다");
                RescueInteractor.Cancel();
                return;
            }

            bool wasHolding = RescueHolding;

            // **로그에 쓸 값을 미리 잡아 둔다** (S15P21B202-390). 홀드가 끝나면
            // 인터랙터가 Idle 로 돌아가고 포커스가 **다른 기절자**로 옮겨갈 수 있다 —
            // 그러면 "완료 · 대상=X · 진행=0.75" 처럼 **방금 일으킨 사람이 아닌** 쪽을
            // 보고한다. 2026-09-10 QA 로그에 실제로 그렇게 찍혔고, 기본 닉네임이 모두
            // `비행사` 라 사람 눈으로는 구별도 되지 않았다. -383 과 같은 계열이다.
            string heldName = wasHolding ? RescueTargetName() : null;
            float heldProgress = wasHolding ? RescueProgress01 : 0f;

            if (!input.RescueHeld)
            {
                if (wasHolding)
                {
                    LogHeldRescue("중단", heldName, heldProgress,
                                  "손을 뗐다 · 진행분은 남는다");
                    RescueInteractor.Cancel();
                }
                waitingForServerRescue = false;
                RescueInteractor.Tick(Time.fixedDeltaTime);   // 후보 갱신
                return;
            }

            // **클라이언트는 예측만 한다.** 완료를 서버가 하므로(-145 · DownedRescue
            // 의 권위 가드), 다 찬 것처럼 보이는 데서 진행을 멈추고 `downed` SyncVar
            // 가 내려오기를 기다린다(-378). 그러지 않으면 인터랙터가 그 프레임에
            // 완료로 넘어가 아무 일도 일어나지 않는 완료를 매 틱 되풀이한다.
            if (wasHolding && !ServerAuthority
                && RescueInteractor.HoldProgress >= RescuePredictionCap)
            {
                if (!waitingForServerRescue)
                {
                    waitingForServerRescue = true;
                    LogRescue("대기", "다 찼다 · 서버 판정을 기다린다");
                }
                return;
            }

            RescueInteractor.Tick(Time.fixedDeltaTime);

            if (RescueHolding) return;

            // 손을 떼지 않았는데 홀드가 끝났다 = 다 채웠다. Interactor 가 대상의
            // OnCompleted 를 부르고 Idle 로 돌아가는 것이 완료의 신호다.
            if (wasHolding)
            {
                LogHeldRescue("완료", heldName, heldProgress, "일어났다");
                waitingForServerRescue = false;
                return;
            }

            if (RescueInteractor.TryBegin())
            {
                waitingForServerRescue = false;
                LogRescue("시작", "누르고 있는다");
            }
        }

        /// <summary>마지막으로 남긴 구조 후보. 바뀔 때만 찍으려고 들고 있는다.</summary>
        string lastRescueCandidate;

        /// <summary>
        /// 구조 후보가 잡히는지 남긴다 (S15P21B202-433).
        ///
        /// <para><b>이것이 없어서 QA 신고를 가릴 수 없었다.</b> <c>DownedRescueHud</c> 는
        /// <see cref="RescueCandidate"/> 가 있을 때만 그린다. 그래서 "G 표시가 안 뜬다"
        /// 는 곧 "후보가 없다" 인데, 후보가 없었던 것인지 사람이 누르지 않은 것인지
        /// 로그로 갈릴 수 없었다 — 참가자 로그에 <c>[구조] 시작</c> 이 없는 것만
        /// 보였다.</para>
        ///
        /// <para><b>바뀔 때만 찍는다.</b> 매 틱 찍으면 2026-09-11 의 kinematic 경고처럼
        /// 로그를 덮는다 — 그 QA 로그는 109,643줄 중 106,652줄이 한 줄이었다.</para>
        /// </summary>
        void ReportRescueCandidate()
        {
            var candidate = RescueCandidate;
            string now = candidate != null ? candidate.Nickname : null;
            if (now == lastRescueCandidate) return;

            lastRescueCandidate = now;
            Debug.Log("[NotThisPlanet][구조] 후보 "
                      + (now != null ? $"O · 대상={now}" : "X · 겨냥 안에 기절한 동료가 없다"));
        }

        /// <summary>
        /// 클라이언트의 예측이 멈추는 자리. 1 에 닿으면 인터랙터가 완료로 넘어가므로
        /// 그 앞에서 세운다 — 화면은 거의 다 찬 것으로 보이고, 실제로 일어나는 것은
        /// 서버가 정한다.
        /// </summary>
        public const float RescuePredictionCap = 0.98f;

        bool waitingForServerRescue;

        string RescueTargetName()
        {
            var target = RescueCandidate;
            return target != null ? target.Nickname : "모름";
        }

        /// <summary>
        /// 살리기 과정을 남긴다 (S15P21B202-390).
        ///
        /// <para>이 기능에 계측이 <b>0건</b>이어서 QA 신고를 로그로 확인할 수 없었다 —
        /// 3인 로그 64,000줄에 구조·부활 흔적이 하나도 없었다. <c>-383</c>·<c>-354</c>·
        /// <c>-361</c> 과 같은 계열을 만들지 않으려면 <b>무엇이 왜 안 됐는지</b>가
        /// 남아야 한다.</para>
        /// </summary>
        /// <summary>
        /// <b>붙들고 있던</b> 대상으로 남긴다 (S15P21B202-390). 홀드가 끝난 뒤에는
        /// <see cref="RescueCandidate"/> 가 이미 다른 사람을 가리킬 수 있으므로,
        /// 완료·중단은 이쪽을 쓴다.
        /// </summary>
        static void LogHeldRescue(string what, string who, float progress, string detail)
        {
            Debug.Log($"[NotThisPlanet][구조] {what} · 대상={who ?? "모름"} · {detail}"
                      + $" · 채운진행={progress:0.00}");
        }

        void LogRescue(string what, string detail)
        {
            Debug.Log($"[NotThisPlanet][구조] {what} · 대상={RescueTargetName()} · {detail}"
                      + $" · 진행={RescueProgress01:0.00}");
        }

        void ResetClampGrab()
        {
            if (clampGrabHand != null)
                ReleaseHand(clampGrabHand);
            if (clampGrabbed && clampGrabTarget != null) clampGrabTarget.LeavePull();

            clampGrabbed = false;
            clampGrabTarget = null;
            clampGrabHand = null;
            ClampPulling = false;
        }

        void LogHeldToolInteraction(bool leftPressed, bool rightPressed)
        {
            LogInteraction("도구 운반 중·양손 유지", leftPressed, rightPressed, toolHeld, toolBody);
        }

        void LogHandInteraction(string button, Hand hand)
        {
            if ((hand.Gripping || hand.Carrying) && hand.Held != null)
            {
                string branch = hand.HeldBody != null ? "개별 물체 운반" : "고정형 물체 짚기";
                LogInteraction(branch, button == "좌클릭", button == "우클릭", hand.Held, hand.HeldBody);
                return;
            }

            if (hand.Aimed != null)
            {
                LogInteraction("대상 탐색됨·잡기 실패", button == "좌클릭", button == "우클릭", hand.Aimed, null);
                return;
            }

            LogInteraction("대상 없음", button == "좌클릭", button == "우클릭", null, null);
        }

        /// <summary>
        /// 고정쇠를 못 잡은 <b>이유를 갈라 적는다</b> (S15P21B202-383).
        ///
        /// <para><b>왜 필요했나.</b> 예전에는 두 갈래뿐이었고 대상도 버렸다.</para>
        /// <code>
        /// string branch = CurrentInteractionTarget.CanInteract
        ///     ? "고정못 잡기 실패" : "고정못 접근 불가";
        /// LogInteraction(branch, leftPressed, rightPressed, null, null);   // ← null
        /// </code>
        /// <para>그런데 <c>CanInteract</c> 는 <b>점유·해제됨·거리를 한 불리언으로 합친
        /// 값</b>이다(<c>GetAvailability().IsAllowed &amp;&amp; CanReachClamp()</c>). 그래서
        /// "점유돼서 못 잡았다" 와 "너무 멀어서 못 잡았다" 가 <b>같은 문구</b>로 찍혔고,
        /// 대상까지 <c>null</c> 로 버려서 어느 고정쇠였는지도 남지 않았다.</para>
        ///
        /// <para>2026-09-10 QA 에서 "한 사람은 되고 다른 사람은 안 된다" 는 신고가 왔을 때
        /// 로그에 이 줄이 57건·90건 있었지만 <b>그중 무엇이 점유 때문인지 알 수 없었다.</b>
        /// 그래서 로그로 진단하지 못하고 코드를 읽어야 했다. <c>-354</c>(각주가 사유를
        /// 하드코딩) · <c>-361</c>(카드가 실패한 잡을 못 찾음) 과 같은 계열이다 —
        /// <b>말은 하는데 쓸모가 없는 로그.</b></para>
        ///
        /// <para><b>사유는 <see cref="PartClamp.GetAvailability"/> 에게 묻는다.</b>
        /// 이 함수의 첫 판은 <c>IsReleased</c>·<c>Pullers</c> 를 직접 보고 사유를
        /// 다시 짜맞췄는데, <c>InteractableBase.GetAvailability</c> 가 단일
        /// <c>Occupant</c> 잠금으로도 <c>Occupied</c> 를 내므로 그 경우가
        /// <b>"너무 멀다" 로 찍혔다.</b> 판정을 두 곳에 두면 반드시 갈라진다.</para>
        /// </summary>
        void LogClampRefusal(bool leftPressed, bool rightPressed)
        {
            string buttons = leftPressed && rightPressed ? "좌+우클릭"
                : leftPressed ? "좌클릭" : "우클릭";

            var clamp = CurrentInteractionTarget.Clamp;
            if (clamp == null)
            {
                Debug.Log($"[NotThisPlanet][상호작용] {buttons} → 고정못 거부 (조준 대상 없음)");
                return;
            }

            // 사유를 손으로 다시 짜맞추지 않고 규칙의 주인에게 묻는다.
            //
            // 처음에는 여기서 IsReleased·Pullers 를 직접 보고 사유를 만들었는데,
            // base.GetAvailability 가 **Pullers 와 다른 규칙**(단일 Occupant 잠금)으로도
            // Occupied 를 낸다. 그 경우가 "너무 멀다" 로 찍혔다 — 고치려던 실수를
            // 같은 함수 안에서 되풀이한 것이다. 판정은 한 곳에만 둔다.
            var availability = clamp.GetAvailability(ClampInteractor);
            bool reachable = CanReachClamp(clamp);

            string reason = !availability.IsAllowed
                ? RefusalReason(availability.Failure, clamp.Pullers)
                : reachable ? "닿는데 결합 안 됨" : "너무 멀다";

            // 사유와 무관하게 점유·거리를 함께 적는다. 다음 신고 때 로그 한 줄로
            // 끝나야 한다 — 두 값을 따로 찾아 맞춰 보게 만들면 또 코드를 읽는다.
            Debug.Log($"[NotThisPlanet][상호작용] {buttons} → 고정못 거부 ({reason}) · {clamp.name}"
                      + $" · 당기는 사람 {clamp.Pullers}/{PhysicsScale.ClampMaxPullers}"
                      + $" · 닿음={(reachable ? "O" : "X")}");
        }

        /// <summary>
        /// 거부 <b>사유 하나를 사람 말로</b> 옮긴다 (S15P21B202-383).
        ///
        /// <para>순수 함수로 떼어 둔 이유는 <b>값으로 검증되게</b> 하려는 것이다.
        /// 사유가 새로 생겼는데 여기에 없으면 <c>모르는 사유(...)</c> 로 찍히고,
        /// <c>ClampOccupancyTests</c> 가 그것을 실패로 잡는다. 문구가 한 갈래로
        /// 뭉치는 것도 같은 테스트가 막는다.</para>
        ///
        /// <para><b><see cref="InteractionFailure.Occupied"/> 는 두 규칙이 겹쳐 있다.</b>
        /// <c>InteractableBase</c> 의 단일 <c>Occupant</c> 잠금과 기획 §12.2 의
        /// "최대 2명" 이 같은 값을 낸다. 인원으로 갈라 적어야 "두 명이 이미 붙어서"
        /// 인지 "다른 사람이 먼저 집어서" 인지 구별된다.</para>
        /// </summary>
        public static string RefusalReason(InteractionFailure failure, int pullers)
        {
            switch (failure)
            {
                case InteractionFailure.Occupied:
                    return pullers >= PhysicsScale.ClampMaxPullers
                        ? $"점유 {pullers}/{PhysicsScale.ClampMaxPullers}명"
                        : "다른 사람이 선점";
                case InteractionFailure.NotReady: return "이미 풀림";
                case InteractionFailure.OutOfRange: return "사거리·시야각 밖";
                case InteractionFailure.Busy: return "내가 다른 상호작용 중";
                case InteractionFailure.NoTarget: return "조준 대상 없음";

                // 허용인데 거부 분기로 들어온 것이므로 부르는 쪽이 잘못됐다.
                case InteractionFailure.None: return "허용인데 거부됨";
                default: return $"모르는 사유({failure})";
            }
        }

        static void LogInteraction(string branch, bool left, bool right, Grabbable target, Rigidbody body)
        {
            string buttons = left && right ? "좌+우클릭" : left ? "좌클릭" : "우클릭";
            string label = target != null && !string.IsNullOrEmpty(target.Label) ? target.Label : "없음";
            string mass = body != null ? body.mass.ToString("0.0") + "kg" : "고정";
            Debug.Log("[NotThisPlanet][상호작용] " + buttons + " → " + branch
                      + " | 대상=" + label + " | 질량=" + mass);
        }

        bool TryGrabTool(Vector3 aim, PlayerInteractionTarget focus)
        {
            if (LeftHand.Gripping || LeftHand.Carrying || RightHand.Gripping || RightHand.Carrying)
                return false;

            var targetGrabbable = focus.Kind == PlayerInteractionKind.Grabbable
                ? focus.Grabbable : null;
            var body = BodyOf(targetGrabbable);
            if (!IsUsableTool(targetGrabbable, body)) return false;

            LeftHand.Aimed = targetGrabbable;
            LeftHand.AimedPoint = focus.Point;
            RightHand.Aimed = targetGrabbable;
            RightHand.AimedPoint = focus.Point;

            // 도구는 역할에 따라 양손 논리 고정 그립으로 잡는다. 잡은 직후 Rigidbody를
            // 키네마틱으로 전환하고 포즈를 직접 제어하므로 양손 FixedJoint 반작용을 만들지 않는다.
            bool left = LeftHand.BraceTo(targetGrabbable, body);
            bool right = RightHand.BraceTo(targetGrabbable, body);
            if (left && right)
            {
                toolHeld = targetGrabbable;
                toolBody = body;
                toolBodyHadGravity = body.useGravity;
                activeTool = targetGrabbable.GetComponent<ToolBase>();
                if (activeTool == null)
                    activeTool = targetGrabbable.GetComponentInChildren<ToolBase>(true);

                ResetToolInputState();
                // The click that picked up the tool is consumed until that button is
                // released, so picking up with the primary button cannot also fire it.
                suppressToolPrimary = GetToolPrimaryHeld();
                suppressToolSecondary = GetToolSecondaryHeld();
                suppressToolUtility = GetToolUtilityHeld();

                BeginCarriedBody(body);
                activeTool?.Equip(this, body);
                HoldToolPose(aim);
                SyncHeldObjectOwnerCollision();
                return true;
            }

            if (left) ReleaseHand(LeftHand);
            if (right) ReleaseHand(RightHand);
            return false;
        }

        ToolAimContext CreateToolAimContext(Vector3 aim)
        {
            return new ToolAimContext(this, Cam, aim);
        }

        bool GetToolPrimaryHeld()
        {
            // Controls.Read fills both fields. The fallback preserves scripted callers
            // that still populate the legacy grab field directly.
            return input.ToolPrimary || input.GrabLeft;
        }

        bool GetToolSecondaryHeld()
        {
            return input.ToolSecondary || input.GrabRight;
        }

        bool GetToolUtilityHeld()
        {
            return input.ToolUtility || input.ToolAction;
        }

        void SetActiveToolInput(Vector3 aim, bool blockActions)
        {
            bool primaryHeld = GetToolPrimaryHeld();
            bool secondaryHeld = GetToolSecondaryHeld();
            bool utilityHeld = GetToolUtilityHeld();

            if (suppressToolPrimary && !primaryHeld) suppressToolPrimary = false;
            if (suppressToolSecondary && !secondaryHeld) suppressToolSecondary = false;
            if (suppressToolUtility && !utilityHeld) suppressToolUtility = false;

            bool primary = primaryHeld && !suppressToolPrimary && !blockActions;
            bool secondary = secondaryHeld && !suppressToolSecondary && !blockActions;
            bool utility = utilityHeld && !suppressToolUtility;

            var toolInput = new ToolInputFrame
            {
                PrimaryHeld = primary,
                SecondaryHeld = secondary,
                UtilityHeld = utility,
                PrimaryPressed = primary && !previousToolPrimary,
                SecondaryPressed = secondary && !previousToolSecondary,
                UtilityPressed = utility && !previousToolUtility,
                PrimaryReleased = !primary && previousToolPrimary,
                SecondaryReleased = !secondary && previousToolSecondary,
                UtilityReleased = !utility && previousToolUtility
            };

            previousToolPrimary = primary;
            previousToolSecondary = secondary;
            previousToolUtility = utility;

            if (activeTool != null)
                activeTool.SetInput(toolInput, CreateToolAimContext(aim));
        }

        void ResetToolInputState()
        {
            previousToolPrimary = false;
            previousToolSecondary = false;
            previousToolUtility = false;
            suppressToolPrimary = false;
            suppressToolSecondary = false;
            suppressToolUtility = false;
        }

        static bool IsUsableTool(Grabbable target, Rigidbody body)
        {
            // 고정 레일·케이블은 기존의 한 손 매달리기를 유지한다. 도구 역할로 지정된
            // 가벼운 동적 물체만 양손 고정 그립과 전방 조준 자세를 사용한다.
            return target != null && target.IsTool && !target.Climbable
                   && body != null && !body.isKinematic && body.mass < Tuning.FixedGripMass;
        }

        bool ProcessToolAction(Vector3 aim)
        {
            if (GetToolUtilityHeld())
            {
                toolCharge = Mathf.Clamp01(toolCharge + Time.fixedDeltaTime / Tuning.ToolChargeDuration);
                toolActionHeld = true;
                chargingTool = true;
                return false;
            }

            if (!toolActionHeld) { chargingTool = false; return false; }

            if (toolCharge >= Tuning.ToolDropChargeThreshold)
                ThrowTool(aim, toolCharge);
            else
                DropTool();
            return true;
        }

        void DropTool()
        {
            ReleaseHand(LeftHand);
            ReleaseHand(RightHand);
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;
        }

        void ThrowTool(Vector3 aim, float charge)
        {
            var target = toolHeld;
            Rigidbody body = LeftHand.Held == target ? LeftHand.HeldBody : RightHand.HeldBody;
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;

            ReleaseHand(LeftHand, HandReleaseReason.Throw);
            ReleaseHand(RightHand, HandReleaseReason.Throw);
            ResetToolState();
            toolThrowRegrabLock = Tuning.ToolThrowRegrabLock;

            if (body != null && !body.isKinematic)
            {
                pendingThrowBody = body;
                pendingThrowVelocity = velocity + direction * (Tuning.ToolThrowSpeed * Mathf.Clamp01(charge));
            }
        }

        void ResetToolState()
        {
            ClearHeldObjectOwnerCollision();
            var releasedTool = activeTool;
            releasedTool?.Unequip();
            activeTool = null;
            RestoreCarriedBodyIfUnused(toolBody);
            if (toolBody != null)
            {
                // A dropped meteor bat is always a world prop. The generic cache keeps
                // the old state for ordinary tools, but a bat must not remain floating
                // or rotation-locked after a player releases it.
                if (releasedTool is MeteorBat)
                {
                    toolBody.isKinematic = false;
                    toolBody.useGravity = true;
                    toolBody.constraints = RigidbodyConstraints.None;
                }
                else
                {
                    toolBody.useGravity = toolBodyHadGravity;
                }
            }
            toolBody = null;
            toolBodyHadGravity = false;
            toolHeld = null;
            toolCharge = 0f;
            toolActionHeld = false;
            chargingTool = false;
            ResetToolInputState();
        }

        /// <summary>
        /// 현재 PlayerRig의 콜라이더와 현재 들고 있는 물체의 콜라이더 쌍만 충돌을 끈다.
        /// Carrying과 Gripping을 모두 포함하므로 운반체와 Tool에 공통 적용된다.
        /// 레이어 충돌 매트릭스는 건드리지 않으므로 다른 플레이어와 도구의 충돌은 유지된다.
        /// </summary>
        void SyncHeldObjectOwnerCollision()
        {
            // 논리 운반 상태와 도구의 논리 고정 그립 상태 모두 소유자-물체 충돌 무시 규칙을 탄다.
            Rigidbody left = GetHeldBody(LeftHand);
            Rigidbody right = GetHeldBody(RightHand);

            if (left == null)
            {
                left = right;
                right = null;
            }
            else if (left == right)
            {
                right = null;
            }

            if (left == ignoredHeldBodyA && right == ignoredHeldBodyB)
            {
                // A moving/reenabled collider can rebuild its contact state. Reassert
                // the explicit owner-held pairs even when the held-body cache is unchanged.
                IgnoreHeldObjectFromOwner(left);
                IgnoreHeldObjectFromOwner(right);
                return;
            }

            ClearHeldObjectOwnerCollision();
            ignoredHeldBodyA = left;
            ignoredHeldBodyB = right;
            IgnoreHeldObjectFromOwner(left);
            IgnoreHeldObjectFromOwner(right);
        }

        /// <summary>
        /// 고정못을 잡고 있는 동안에는 <b>몸으로 파츠를 밀지 못한다</b> (S15P21B202-667).
        ///
        /// <para>고정못을 잡으면 손이 <b>파츠 강체에 FixedJoint</b> 로 묶이고
        /// (<c>TryAttachClampHand</c>), 박혀 있는 파츠는 kinematic 이다. 그래서 잡은
        /// 사람은 <b>밀리지 않는 벽</b>이 된다 — 그것은 의도된 것이다(기획 §12.1 의
        /// "발을 구르며 낑낑대는" 그림).</para>
        ///
        /// <para><b>문제는 그 벽이 다른 파츠에 닿을 때다.</b> 힘이 한쪽으로만 가서
        /// 파츠는 밀리는데 사람은 안 밀린다 — 무한 질량으로 미는 셈이다. 고정못을
        /// 잡은 채 자유 파츠에 몸을 비비면 아주 쉽게 밀려 나간다.</para>
        ///
        /// <para>밀 수 있는 파츠(<c>Free</c>)와만 무시한다. 박힌·놓인 파츠까지 통과시키면
        /// 조립된 우주선 안으로 걸어 들어가게 된다.</para>
        /// </summary>
        void RefreshAnchoredPartCollision()
        {
            if (!ClampGrabbed)
            {
                if (anchoredPartCollisionPairs.Count > 0) ClearAnchoredPartCollision();
                anchoredPartsBuilt = false;
                return;
            }
            if (Doll == null) return;

            // **더 이상 밀 수 없게 된 것은 즉시 되돌린다.** 잡고 있는 사이에 그 파츠가
            // Placed/Welded 로 넘어가면(남이 조립하면) 무시가 남아 **조립된 우주선
            // 안으로 걸어 들어갈 수 있다** — 이 함수가 막겠다고 한 바로 그것이다.
            for (int i = anchoredPartCollisionPairs.Count - 1; i >= 0; i--)
            {
                var pair = anchoredPartCollisionPairs[i];
                if (pair.Player != null && pair.Held != null && StillPushable(pair.Held)) continue;
                if (pair.Player != null && pair.Held != null)
                    Physics.IgnoreCollision(pair.Player, pair.Held, false);
                anchoredPartCollisionPairs.RemoveAt(i);
            }

            // **매 틱 다시 만들지 않는다.** 예전에는 잡고 있는 내내 파츠마다
            // GetComponentsInChildren 을 돌려 배열을 새로 할당했다. 잡기 시작한
            // 프레임에 한 번만 짓고, 위의 해제만 매 틱 본다.
            if (anchoredPartsBuilt) return;
            anchoredPartsBuilt = true;

            var parts = Parts.PartPush.All;
            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                if (part == null || !part.CanPush) continue;

                var partColliders = part.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < Doll.Colliders.Count; i++)
                {
                    var mine = Doll.Colliders[i];
                    if (mine == null || !mine.enabled || !mine.gameObject.activeInHierarchy) continue;

                    for (int k = 0; k < partColliders.Length; k++)
                    {
                        var theirs = partColliders[k];
                        if (theirs == null || theirs == mine || theirs.isTrigger
                            || !theirs.enabled || !theirs.gameObject.activeInHierarchy) continue;
                        if (Physics.GetIgnoreCollision(mine, theirs)) continue;

                        Physics.IgnoreCollision(mine, theirs, true);
                        anchoredPartCollisionPairs.Add(new CollisionPair { Player = mine, Held = theirs });
                    }
                }
            }
        }

        /// <summary>그 콜라이더가 아직 밀 수 있는 파츠의 것인가.</summary>
        static bool StillPushable(Collider collider)
        {
            var push = collider.GetComponentInParent<Parts.PartPush>();
            return push != null && push.CanPush;
        }

        /// <summary>고정못을 놓으면 파츠와 다시 부딪힌다.</summary>
        void ClearAnchoredPartCollision()
        {
            for (int i = anchoredPartCollisionPairs.Count - 1; i >= 0; i--)
            {
                var pair = anchoredPartCollisionPairs[i];
                if (pair.Player != null && pair.Held != null)
                    Physics.IgnoreCollision(pair.Player, pair.Held, false);
            }
            anchoredPartCollisionPairs.Clear();
            anchoredPartsBuilt = false;
        }

        void IgnoreHeldObjectFromOwner(Rigidbody body)
        {
            if (body == null || Doll == null) return;

            // Include inactive child colliders too. If a tool model enables a collider
            // after pickup, the next physics tick will still restore the owner-only ignore.
            var heldColliders = body.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < Doll.Colliders.Count; i++)
            {
                var playerCollider = Doll.Colliders[i];
                if (playerCollider == null || !playerCollider.enabled || !playerCollider.gameObject.activeInHierarchy)
                    continue;

                for (int k = 0; k < heldColliders.Length; k++)
                {
                    var heldCollider = heldColliders[k];
                    if (heldCollider == null || playerCollider == heldCollider
                        || !heldCollider.enabled || !heldCollider.gameObject.activeInHierarchy)
                        continue;
                    if (Physics.GetIgnoreCollision(playerCollider, heldCollider)) continue;

                    Physics.IgnoreCollision(playerCollider, heldCollider, true);
                    if (!HasTrackedHeldObjectPair(playerCollider, heldCollider))
                    {
                        heldObjectCollisionPairs.Add(new CollisionPair
                        {
                            Player = playerCollider,
                            Held = heldCollider
                        });
                    }
                }
            }
        }

        bool HasTrackedHeldObjectPair(Collider playerCollider, Collider heldCollider)
        {
            for (int i = 0; i < heldObjectCollisionPairs.Count; i++)
            {
                var pair = heldObjectCollisionPairs[i];
                if (pair.Player == playerCollider && pair.Held == heldCollider)
                    return true;
            }
            return false;
        }

        void ClearHeldObjectOwnerCollision()
        {
            for (int i = heldObjectCollisionPairs.Count - 1; i >= 0; i--)
            {
                var pair = heldObjectCollisionPairs[i];
                if (pair.Player != null && pair.Held != null)
                    Physics.IgnoreCollision(pair.Player, pair.Held, false);
            }
            heldObjectCollisionPairs.Clear();
            ignoredHeldBodyA = null;
            ignoredHeldBodyB = null;
        }

        static Rigidbody GetHeldBody(Hand hand)
        {
            if (hand == null || hand.HeldBody == null) return null;
            return hand.Carrying || hand.Gripping ? hand.HeldBody : null;
        }

        void BeginCarriedBody(Rigidbody body)
        {
            if (body == null) return;

            if (!carriedBodies.ContainsKey(body))
            {
                carriedBodies.Add(body, new CarriedBodyState
                {
                    WasKinematic = body.isKinematic,
                    HadGravity = body.useGravity,
                    LinearVelocity = body.linearVelocity,
                    AngularVelocity = body.angularVelocity,
                    CollisionDetectionMode = body.collisionDetectionMode,
                    Interpolation = body.interpolation,
                    Constraints = body.constraints,
                    FollowPosition = body.position,
                    FollowInitialized = false
                });
            }

            // 속도를 **먼저** 비운다 (S15P21B202-433) — kinematic 으로 바꾼 뒤에 쓰면
            // Unity 가 거부하고 경고만 남는다. 여기서 비우는 목적은 남은 운동량을
            // 없애는 것이고, 그것은 동적인 동안에만 가능하다.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void MoveCarriedBody(Rigidbody body, Vector3 aim)
        {
            if (body == null) return;
            if (!carriedBodies.ContainsKey(body)) BeginCarriedBody(body);

            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;
            // 조준 창구를 쓴다. 가슴으로 대신하면 서버에서 든 물체가 그 사람 화면과
            // 다른 자리에 떠 있게 된다.
            Vector3 holdPoint = AimOrigin + direction * Tuning.ToolHoldDistance;

            // **이미 kinematic 이면 속도를 쓰지 않는다** (S15P21B202-433).
            //
            // 순서가 거꾸로였다. kinematic 강체에 속도를 대입하면 Unity 가 거부하면서
            // 경고를 찍는데, 이 함수는 운반 중 **매 틱 · 운반물마다** 돈다. 2026-09-11
            // QA 로그 109,643줄 중 **106,652줄(97%)** 이 그 경고 한 줄이었다(참가자도 96%).
            //
            // 프레임에는 영향이 없었다 — 5초 구간별 실측으로 경고 있는 구간 58.7fps ·
            // 없는 구간 58.8fps 로 차이가 없다. 문제는 **로그가 묻힌 것**이다. `-390` 에서
            // "계측이 0건이어서 QA 신고를 확인할 수 없었다" 를 고쳤는데, 이번에는 반대로
            // 다른 진단이 이 한 줄에 파묻혀 QA 로그가 쓸모를 잃었다.
            //
            // kinematic 강체의 속도는 뜻이 없다(물리가 읽지 않는다). 그래서 동적인
            // 동안에만 비우고, 그 뒤 kinematic 으로 바꾼다.
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            body.useGravity = false;

            CarriedBodyState state = carriedBodies[body];
            Vector3 desiredPosition = body.position + holdPoint - body.worldCenterOfMass;
            if (!state.FollowInitialized)
            {
                // Pickup is responsive, while every following physics step is smoothed.
                state.FollowPosition = desiredPosition;
                state.FollowInitialized = true;
            }
            else
            {
                float blend = 1f - Mathf.Exp(-Tuning.CarryPositionSmoothing * Time.fixedDeltaTime);
                state.FollowPosition = Vector3.Lerp(state.FollowPosition, desiredPosition, blend);
            }

            carriedBodies[body] = state;
            body.MovePosition(state.FollowPosition);
        }

        void HoldToolPose(Vector3 aim)
        {
            if (toolBody == null) return;

            MoveCarriedBody(toolBody, aim);

            Vector3 direction = aim.sqrMagnitude > 1e-4f ? aim.normalized : Doll.Chest.transform.forward;
            Vector3 up = Cam != null ? Cam.transform.up : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.98f)
                up = Cam != null ? Cam.transform.right : Vector3.right;

            Quaternion targetRotation = Quaternion.LookRotation(direction, up);
            float blend = 1f - Mathf.Exp(-Tuning.CarryRotationSmoothing * Time.fixedDeltaTime);
            toolBody.MoveRotation(Quaternion.Slerp(toolBody.rotation, targetRotation, blend));
        }

        /// <summary>
        /// 잡고 있던 파츠에서 손이 <b>너무 멀어졌을 때</b> 놓는다 (S15P21B202-686).
        ///
        /// <para><see cref="PartPush"/> 가 거리를 재고 여기로 돌려보낸다. 손을 실제로
        /// 놓아야 한다 — 밀기 집계에서 빼기만 하면 손은 영영 붙어 있는 것으로 남아
        /// 다시 잡을 수도, 다른 것을 잡을 수도 없다.</para>
        /// </summary>
        internal void ReleaseHaulGrip(Hand hand) => ReleaseHand(hand);

        void ReleaseHand(Hand hand, HandReleaseReason reason = HandReleaseReason.Release)
        {
            if (hand == null) return;

            var target = hand.Held;
            Rigidbody body = hand.HeldBody;
            if (target != null)
                AnyHandReleased?.Invoke(this, hand, target, reason);
            hand.Release();
            RestoreCarriedBodyIfUnused(body);
        }

        // Hand owns the Unity joint-break callback, but the global audio/event contract
        // belongs to PlayerRig. This bridge keeps an unexpected physics release visible
        // without routing it back through ReleaseHand after the hand has already cleared.
        internal void NotifyHandReleasedByPhysics(Hand hand, Grabbable target)
        {
            if (hand != null && target != null)
                AnyHandReleased?.Invoke(this, hand, target, HandReleaseReason.Release);
        }

        void RestoreCarriedBodyIfUnused(Rigidbody body)
        {
            if (body == null ||
                (LeftHand.HeldBody == body && (LeftHand.Carrying || LeftHand.Gripping)) ||
                (RightHand.HeldBody == body && (RightHand.Carrying || RightHand.Gripping)))
                return;

            CarriedBodyState state;
            if (!carriedBodies.TryGetValue(body, out state)) return;

            body.isKinematic = state.WasKinematic;
            body.useGravity = state.HadGravity;
            body.collisionDetectionMode = state.CollisionDetectionMode;
            body.interpolation = state.Interpolation;
            body.constraints = state.Constraints;
            body.linearVelocity = state.LinearVelocity;
            body.angularVelocity = state.AngularVelocity;
            carriedBodies.Remove(body);
        }

        void RestoreAllCarriedBodies()
        {
            foreach (var pair in carriedBodies)
            {
                Rigidbody body = pair.Key;
                if (body == null) continue;

                CarriedBodyState state = pair.Value;
                body.isKinematic = state.WasKinematic;
                body.useGravity = state.HadGravity;
                body.collisionDetectionMode = state.CollisionDetectionMode;
                body.interpolation = state.Interpolation;
                body.constraints = state.Constraints;
                body.linearVelocity = state.LinearVelocity;
                body.angularVelocity = state.AngularVelocity;
            }
            carriedBodies.Clear();
        }

        void DriveArm(Arm arm, bool reaching, Vector3 aim,
                      Grabbable focusedTarget = null, bool allowGeneralGrip = true)
        {
            var hand = arm.Hand;

            if (!reaching)
            {
                if (hand.Gripping || hand.Carrying)
                {
                    ReleaseHand(hand);
                    regrabLock = Tuning.RegrabLock;
                }
                if (!allowGeneralGrip)
                {
                    hand.Aimed = null;
                    hand.AimedPoint = hand.Body != null ? hand.Body.position : Vector3.zero;
                }
                else if (focusedTarget != null)
                {
                    hand.Aimed = focusedTarget;
                    hand.AimedPoint = CurrentInteractionTarget.Point;
                }
                else
                {
                    UpdateAimedTarget(arm, hand, aim);
                }
                JointTools.SetDrive(arm.Shoulder, 26f, 4f);
                JointTools.SetDrive(arm.Elbow, 18f, 3f);
                JointTools.SetTargetLocalRotation(arm.Shoulder, Quaternion.identity, arm.ShoulderStart);
                JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);
                return;
            }

            // 어깨를 굳혀 팔을 조준 방향으로 세운다
            JointTools.SetDrive(arm.Shoulder, 220f, 18f);
            JointTools.SetDrive(arm.Elbow, 90f, 9f);

            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : Doll.Chest.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            bool toolTarget = false;

            // ── 자석 손 ────────────────────────────────────────
            // 뻗은 손끝 근처에 잡을 것이 있으면 손 목표를 그쪽으로 옮긴다.
            // 목표를 팔 길이 안으로 자르는 것이 중요하다 — 넘기면 팔이 뻗다 못해 뜯긴다.
            if (!hand.Gripping && !hand.Carrying)
            {
                Vector3 p; Rigidbody b;
                Grabbable g;
                if (!allowGeneralGrip)
                {
                    g = null;
                    p = target;
                    b = null;
                }
                else if (focusedTarget != null)
                {
                    g = focusedTarget;
                    p = CurrentInteractionTarget.Point;
                    b = BodyOf(focusedTarget);
                }
                else
                {
                    g = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim,
                                    Tuning.GrabAssistAngle, out p, out b);
                }
                hand.Aimed = g;
                hand.AimedPoint = p;
                toolTarget = IsUsableTool(g, b);
                if (g != null)
                {
                    Vector3 dir = p - shoulder;
                    if (dir.sqrMagnitude > 1e-4f)
                        target = shoulder + dir.normalized * Mathf.Min(dir.magnitude, Tuning.ArmReach);
                }
            }
            else
            {
                hand.Aimed = hand.Held;
                hand.AimedPoint = hand.Body.position;

                // **파츠를 잡고 있으면 팔을 잡은 지점으로 뻗는다** (S15P21B202-686).
                // 조준 방향(카메라 앞)으로만 뻗으면 옆이나 뒤로 걸을 때 손이 파츠를
                // 떠난다 — 3초를 끄는 동안 손과 표면이 0.16 → 0.83m 로 벌어졌다(실측).
                // 파츠는 여전히 §13.2 의 표가 움직인다. 여기서 정하는 것은 **팔이
                // 어디를 향하는가**뿐이다.
                if (Hauling && HeldPartGrasp(hand, out var grasp))
                {
                    hand.AimedPoint = grasp;
                    target = HaulArmTarget(shoulder, grasp, aim);
                }
            }

            Vector3 armDir = target - shoulder;
            if (armDir.sqrMagnitude < 1e-4f) armDir = aim;
            armDir.Normalize();

            Quaternion aimWorld = Quaternion.FromToRotation(Vector3.down, armDir);
            Quaternion aimLocal = Quaternion.Inverse(Doll.Chest.rotation) * aimWorld;
            JointTools.SetTargetLocalRotation(arm.Shoulder, aimLocal, arm.ShoulderStart);
            JointTools.SetTargetLocalRotation(arm.Elbow, Quaternion.identity, arm.ElbowStart);

            // Tool은 양손 고정 그립 상태지만 위치·회전은 PlayerRig가 직접 제어한다.
            // 손에 다시 힘을 가하면 도구 포즈 제어와 팔 물리가 서로 당겨 플레이어를 날린다.
            if (toolHeld != null && hand.Held == toolHeld)
                return;

            // 고정쇠 경로는 FixedJoint가 손 위치를 책임진다. 다른 손의 자동 탐색이나
            // 손끝 힘이 별도 대상과 경쟁하지 않도록 이 프레임의 일반 그립을 막는다.
            if (!allowGeneralGrip)
                return;

            if (hand.Carrying)
            {
                if (toolHeld != null && hand.Held == toolHeld) CarryTool(aim);
                else Carry(hand, aim);
                return;
            }

            Vector3 dp = target - hand.Body.position;
            Vector3 dv = Doll.Chest.linearVelocity - hand.Body.linearVelocity;
            Vector3 f = JointTools.Clamp(dp * Tuning.HandForce + dv * Tuning.HandDamp, Tuning.HandMaxForce);
            if (!JointTools.IsFinite(f)) return;

            if (hand.Hanging)
            {
                // 고정물을 잡은 손은 이미 붙어 있다 — 힘을 몸쪽에 실어야 몸이 딸려 올라간다
                Doll.Chest.AddForce(-f * Tuning.HangPull, ForceMode.Force);
            }
            else
            {
                hand.Body.AddForce(f, ForceMode.Force);
                Doll.Chest.AddForce(-f * 0.25f, ForceMode.Force);
                // Transport 역할의 가벼운 대상은 한 손 운반 경로로 잡는다.
                bool gripped = TryGripTarget(hand, focusedTarget);
                if (regrabLock <= 0f && !toolTarget && gripped && hand.Carrying)
                    BeginCarriedBody(hand.HeldBody);
            }
        }

        /// <summary>
        /// 파츠를 짚은 팔이 향할 지점 (S15P21B202-686).
        ///
        /// <para><b>팔 길이 안으로 자른다.</b> 넘기면 팔이 뻗다 못해 뜯긴다 — 자석 손이
        /// 같은 이유로 같은 것을 한다.</para>
        ///
        /// <para>잡은 지점이 어깨와 겹치면 조준 방향으로 돌아간다. 방향이 0 이면
        /// <c>Quaternion.FromToRotation</c> 이 쓰레기 회전을 내놓는다.</para>
        /// </summary>
        public static Vector3 HaulArmTarget(Vector3 shoulder, Vector3 grasp, Vector3 aim)
        {
            Vector3 toGrasp = grasp - shoulder;
            if (toGrasp.sqrMagnitude <= 1e-4f) return shoulder + aim * Tuning.ArmReach;
            return shoulder + toGrasp.normalized
                   * Mathf.Min(toGrasp.magnitude, Tuning.ArmReach);
        }

        /// <summary>
        /// 이 손이 <b>지금 밀고 있는 파츠</b>를 잡고 있으면 그 잡은 지점을 돌려준다.
        /// 지점은 <see cref="HaulGrasp"/> 라 호스트와 참가자가 같은 값을 쓴다.
        /// </summary>
        bool HeldPartGrasp(Hand hand, out Vector3 grasp)
        {
            grasp = HaulGrasp;
            if (HaulGrasp.sqrMagnitude <= 1e-6f) return false;
            return hand.Held != null && hand.Held.GetComponentInParent<PartPush>() != null;
        }

        bool TryGripTarget(Hand hand, Grabbable focusedTarget)
        {
            var target = focusedTarget != null ? focusedTarget : hand.Aimed;
            var partPush = target != null ? target.GetComponentInParent<PartPush>() : null;

            // 파츠는 일반 운반 경로가 아니라 협동 견인 집계 경로를 사용한다.
            // PartPush가 비활성/고정 상태면 여기서 거부해 Locked 파츠를 손으로
            // 세계에 고정하는 기존 FixedJoint 경로로 우회하지 않는다.
            if (partPush != null)
                return partPush.TryGrab(this, hand);

            return focusedTarget != null
                ? hand.GripTo(focusedTarget, BodyOf(focusedTarget))
                : hand.TryGrip();
        }

        static Rigidbody BodyOf(Grabbable target)
        {
            if (target == null) return null;
            var body = target.GetComponent<Rigidbody>();
            return body != null ? body : target.GetComponentInParent<Rigidbody>();
        }

        void UpdateAimedTarget(Arm arm, Hand hand, Vector3 aim)
        {
            if (hand == null || hand.Body == null)
            {
                if (hand != null) hand.Aimed = null;
                return;
            }

            Vector3 shoulder = arm.ShoulderAnchor != null ? arm.ShoulderAnchor.position : Doll.Chest.position;
            Vector3 target = shoulder + aim * Tuning.ArmReach;
            Vector3 point;
            Rigidbody body;
            hand.Aimed = Hand.Search(target, Tuning.GrabAssistRange, shoulder, aim,
                                     Tuning.GrabAssistAngle, out point, out body);
            hand.AimedPoint = point;
        }

        /// <summary>
        /// Tool 역할의 도구를 중력과 누적 속도의 영향 없이 크로스헤드 앞에 고정하고,
        /// 모델의 +Z 전방이 조준 방향을 향하도록 회전한다.
        /// </summary>
        void CarryTool(Vector3 aim)
        {
            MoveCarriedBody(toolBody, aim);
        }

        void Carry(Hand hand, Vector3 aim)
        {
            MoveCarriedBody(hand != null ? hand.HeldBody : null, aim);
        }

        void ClimbAssist()
        {
            if (!Hanging) return;

            float best = float.MinValue;
            if (LeftHand.Hanging) best = Mathf.Max(best, LeftHand.Body.position.y);
            if (RightHand.Hanging) best = Mathf.Max(best, RightHand.Body.position.y);

            // 손이 골반보다 위에 있을 때만 기어오를 수 있다
            float over = best - (Doll.Hips.position.y + 0.16f);
            if (over <= 0f) return;

            float pull = Tuning.ClimbPull * Mathf.Clamp01(over / 0.35f);
            if (input.ArmsUp || input.Move.y > 0.1f) pull *= 2.2f;

            Doll.Chest.AddForce(Vector3.up * pull, ForceMode.Force);
            Doll.Hips.AddForce(Vector3.up * pull * 0.7f, ForceMode.Force);
        }
    }
}
