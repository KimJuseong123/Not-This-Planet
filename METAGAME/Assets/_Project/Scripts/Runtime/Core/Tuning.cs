using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 물리 상수를 한 곳에. 이 프로토타입의 성격은 세 덩어리가 정한다.
    ///
    ///  1) 중력이 약하다 (지구의 약 1/3.5). 점프가 높고 낙하가 느리다 —
    ///     그래서 실수해도 허공에서 허우적거릴 시간이 있다.
    ///  2) 인형이 작고 머리가 크다 (2등신). 관성이 작아 조인트 스프링도 전부 작다.
    ///  3) 잡기가 매우 관대하다. 손 근처 반경이 크다. 가벼운 도구는 양손으로
    ///     크로스헤드 앞에 고정하고, 무거운 물체는 운반하지 않고 고정물처럼 짚는다.
    /// </summary>
    public static class Tuning
    {
        // ── 세계 ────────────────────────────────────────────────
        // 중력 트릭 행성은 아니므로 기본 조작감을 확보하되, 지구보다 약하게 두어 외계 행성의 부유감을 남긴다.
        public const float Gravity = -5f;
        // 100Hz. 조인트가 많아서 촘촘하게 밟는다. **60Hz 로 낮추지 않는다** (S15P21B202-746, 2026-09-26 검사): 걷기·뛰기
        // 속도는 같았지만 고정못 당기기와 풀린 파츠의 움직임이 달라져, 한 번 시킨 고정못이 8개 → 2~5개로 줄고 첫 못도
        // 11초 → 20초로 늦어졌다. 물리 + FixedUpdate 스크립트(약 6ms)는 다른 길로 줄인다.
        public const float FixedStep = 0.01f;
        public const float MaxCatchUp = FixedStep * 5f;   // 한 프레임에 따라잡는 물리 — 5단계 (S15P21B202-746)
        public const float DeathHeight = -30f;      // 이 아래로 떨어지면 체크포인트로 되돌린다

        // ── 몸 ──────────────────────────────────────────────────
        // 귀 끝까지 1.33m, 헬멧 지름 0.636m → 약 2.1등신. 몸무게는 우주복 포함 목표치.
        // 토끼는 다리가 짧아 골반이 낮다 — 이 값은 RagdollBuilder.HipsY 와 반드시 같아야 한다.
        public const float BodyMass = 35f;          // 전체 합계 목표치 (셀프테스트가 확인한다)
        public const float RideHeight = 0.30f;      // 골반이 떠 있어야 하는 높이
        public const float RideSpring = 2600f;      // 서 있게 만드는 스프링. 낮추면 무릎이 꺾인다
        public const float RideDamp = 400f;
        public const float RideMaxForce = 1500f;
        public const float GroundProbe = 0.66f;     // 이 거리까지 바닥을 찾는다

        // ── 이동 ────────────────────────────────────────────────
        public const float WalkSpeed = 2.9f;
        public const float RunSpeed = 4.6f;
        public const float GroundAcceleration = 22f; // 일반적인 캐릭터처럼 즉시 반응하되 미세한 가속은 남긴다
        public const float GroundDeceleration = 30f;
        public const float AirAcceleration = 8f;
        public const float Accel = 330f;            // N per (m/s)
        public const float AccelMax = 900f;
        // 진공에는 공기가 없다 — 대신 우주복 등짐의 자세제어 분사기(RCS)로 버틴다는 설정.
        // 그래서 공중 제어가 지구 게임보다 후하다.
        public const float AirControl = 0.42f;
        public const float JumpSpeed = 3.4f;        // 최고점 v²/2g = 1.156m, 체공 1.36초
        public const float StepPeriod = 0.80f;      // 걷기 주기(초). 저중력이라 성큼성큼 느리게
        public const float SideStepSwing = 18f;    // 좌우 걸음에서 허벅지가 옆으로 기울어지는 최대 각도
        public const float SideStepKneeSwing = 10f; // 좌우 걸음의 무릎 보정

        // ── 체력 · 피해 ────────────────────────────────────────
        // 일반 점프는 안전하게 유지하고, 높은 곳에서 빠르게 착지했을 때만 낙하 피해를 준다.
        public const float MaxHealth = 100f;

        // 빨간 테두리는 피해를 받은 순간 번쩍였다가 식는다 (S15P21B202-539).
        // 예전처럼 남은 체력에 정비례하면, 구조가 체력을 절반만 돌려주는데
        // (PhysicsScale.RescueHealthRatio) 되돌릴 수단이 없어 테두리가 판 끝까지 남는다.
        //
        // 0.45초는 "맞았다" 가 읽히면서 조작을 가리지 않는 길이다. 운석 직격(25)이
        // 게인 2.2 로 0.55 가 되어 화면 짧은 변의 4.4% 두께로 뜬다 — 눈에 걸리되
        // 시야를 막지는 않는다.
        public const float DamageFlashSeconds = 0.45f;
        public const float DamageFlashGain = 2.2f;
        // 낙하 피해 상수를 걷어냈다(S15P21B202-334). 기획 §20.2 는 낙하를 피해가 아니라
        // 자세로 처리한다 — 단계와 경계는 PhysicsScale.FallStagger/Knock/StunHeight 에 있다.

        // ── 균형 ────────────────────────────────────────────────
        // 골반을 세우는 일은 "세계에 매인 보이지 않는 조인트" 가 한다.
        // AddTorque 로는 안 된다 — 다리 조인트 드라이브는 솔버 안에서 풀리고,
        // 밖에서 넣은 토크는 그 앞에서 그냥 밀려난다.
        // 머리(헬멧)가 무거워 위쪽이 무겁다. 지구 버전보다 상대적으로 세게 잡아준다.
        public const float UprightSpring = 1800f;
        public const float UprightDamp = 190f;
        public const float UprightMaxTorque = 3200f;
        public const float RagdollBlend = 0.10f;    // 자빠졌을 때 남기는 균형 힘

        // ── 팔 ──────────────────────────────────────────────────
        public const float ArmReach = 0.31f;        // 어깨에서 손까지 뻗는 거리 (팔 최대 0.338 보다 짧게)
        public const float HandForce = 620f;        // 빈손을 목표점으로 끌어당기는 힘
        public const float HandDamp = 30f;
        public const float HandMaxForce = 420f;

        // ── 잡기 — 여기가 이 게임의 관대한 부분이다 ─────────────
        /// <summary>손 주위 이 반경 안에 잡을 것이 있으면 붙는다. 장갑 반지름의 6배.</summary>
        public const float GrabRadius = 0.50f;
        /// <summary>뻗은 손끝 근처 이 거리 안의 잡을 것으로 손이 저절로 휘어 간다 (자석 손).</summary>
        public const float GrabAssistRange = 1.10f;
        /// <summary>조준 방향에서 이 각도 안에 있는 것만 자석이 먹는다.</summary>
        public const float GrabAssistAngle = 75f;
        /// <summary>
        /// 도구처럼 작고 중요한 특수 물체를 화면 중심 주변에서 놓치지 않게 하는 보정 반경.
        /// 일반 운반물에는 적용하지 않는다.
        /// </summary>
        public const float SpecialGrabAssistRadius = 0.18f;
        /// <summary>특수 물체에 허용하는 좁은 화면 중심 보정 각도.</summary>
        public const float SpecialGrabAssistAngle = 28f;
        /// <summary>Clamp의 실제 손 결합에만 더하는 좁은 팔 도달 여유(m).</summary>
        public const float ClampGrabAssistReach = 0.16f;
        public const float GripBreakForce = 4000f;  // 저중력이라 사실상 놓치지 않는다
        public const float GripBreakTorque = 2600f;
        /// <summary>손을 뗀 뒤 다시 잡을 수 있게 되기까지. 짧을수록 관대하다.</summary>
        public const float RegrabLock = 0.06f;

        // ── 들기 · 나르기 ───────────────────────────────────────
        // 가벼운 운반 물체는 PlayerRig가 Kinematic 상태로 크로스헤드 위치를
        // 직접 추적하므로 아래의 기존 동적 운반 보정값은 현재 경로에서 사용하지 않는다.
        // 외부/향후 참조 호환성을 위해 값은 유지한다.
        public const float CarryAccel = 42f;
        public const float CarryDamp = 8f;
        public const float CarryAccelMax = 26f;
        /// <summary>잡은 물체의 무게를 이만큼 대신 들어준다 (1 = 완전 무중력처럼).</summary>
        public const float CarrySupport = 0.85f;
        /// <summary>물체를 당긴 반작용 중 몸이 실제로 받는 비율.</summary>
        public const float CarryReaction = 0.16f;
        /// <summary>두 손으로 같은 것을 잡으면 이만큼 더 세게 다룬다.</summary>
        public const float TwoHandBonus = 1.6f;
        /// <summary>기존 고정형 물체 Joint가 동적 연결 대상을 사용할 때의 질량 보정값.</summary>
        public const float CarryMassScale = 0.35f;
        /// <summary>이 질량 이상인 물체는 들어 올리지 않고 고정물을 짚듯 잡는다.</summary>
        public const float FixedGripMass = 40f;

        // ── 동료 던지기 (기획 §20.4 · S15P21B202-519) ───────────
        /// <summary>양손으로 잡은 동료를 위로 내던질 때의 위쪽 속도(m/s).</summary>
        public const float TossUpSpeed = 8.5f;
        /// <summary>같은 던지기의 앞쪽 속도. 위보다 작다 — "높으면서 살짝 앞으로".</summary>
        public const float TossForwardSpeed = 3.2f;
        /// <summary>이만큼 충전해야 던진다. 못 미치면 그냥 놓는다(도구와 같은 규칙).</summary>
        public const float TossChargeThreshold = 0.10f;
        /// <summary>
        /// 던져진 사람이 최소한 이만큼은 날아가는 것으로 친다.
        ///
        /// 발이 땅에서 떨어지기 전에 <c>Grounded</c> 를 읽으면 던진 그 틱에 곧바로
        /// 끝나 버린다 — 시야가 깜빡이고 아무 일도 없던 것처럼 보인다.
        /// </summary>
        public const float TossedMinSeconds = 0.45f;

        /// <summary>
        /// 던져진 몸이 넘어가는 빠르기(라디안/초). 꽉 채워 던졌을 때의 값이고,
        /// 약하게 던지면 <see cref="Metagame.Gameplay.PlayerRig.TossStrength"/> 만큼 줄어든다
        /// (S15P21B202-551).
        ///
        /// <para><b>실측으로 고른 값이다.</b> 균형 조인트의 감쇠를 걷은 뒤 각속도를 바꿔
        /// 가며 1.2초 동안 골반이 얼마나 넘어가는지 쟀다.</para>
        ///
        /// <list type="table">
        /// <item><description>4 rad/s → 62도 · 완전히 뒤집히지는 않는다</description></item>
        /// <item><description>8 rad/s → 124도 · 한 번 뒤집힌다</description></item>
        /// <item><description><b>12 rad/s → 180도</b></description></item>
        /// <item><description>20 rad/s → 180도 · 더 올려도 늘지 않는다</description></item>
        /// </list>
        /// </summary>
        public const float TossSpinSpeed = 12f;

        // ── 도구 투척 ──────────────────────────────────────────
        public const float ToolChargeDuration = 2.0f;
        public const float ToolThrowSpeed = 14f;
        public const float ToolDropChargeThreshold = 0.08f;
        public const float ToolThrowRegrabLock = 0.30f;
        public const float ToolHoldDistance = 0.55f;
        public const float ToolBeamRange = 0.80f;
        /// <summary>운반물 목표 위치를 따라가는 보간 속도.</summary>
        public const float CarryPositionSmoothing = 18f;
        /// <summary>전방 고정 도구의 회전 보간 속도.</summary>
        public const float CarryRotationSmoothing = 18f;

        // ── 기어오르기 ──────────────────────────────────────────
        public const float ClimbPull = 150f;        // 매달린 채 위로 기어오르는 보조력
        public const float HangPull = 0.85f;        // 매달렸을 때 팔 힘이 몸을 끌어당기는 비율

        // ── 카메라 ──────────────────────────────────────────────
        public const float CamDistance = 3.6f;
        public const float CamHeight = 0.55f;

        // ── 팀 핑 ───────────────────────────────────────────────
        /// <summary>핑이 생성된 뒤 서버에 남아 있는 시간(초).</summary>
        public const float PingLifetimeSeconds = 3f;
        /// <summary>표면을 찾지 못했을 때 핑을 놓을 가상 끝점까지의 거리(m).</summary>
        public const float PingMaxDistance = 200f;
    }
}
