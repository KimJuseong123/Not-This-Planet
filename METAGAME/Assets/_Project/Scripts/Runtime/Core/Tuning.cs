using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 물리 상수를 한 곳에. 이 프로토타입의 성격은 세 덩어리가 정한다.
    ///
    ///  1) 중력이 약하다 (지구의 약 1/3.5). 점프가 높고 낙하가 느리다 —
    ///     그래서 실수해도 허공에서 허우적거릴 시간이 있다.
    ///  2) 인형이 작고 머리가 크다 (2등신). 관성이 작아 조인트 스프링도 전부 작다.
    ///  3) 잡기가 매우 관대하다. 손 근처 반경이 크고, 잡은 물체는 질량을 무시하고 끌려온다.
    /// </summary>
    public static class Tuning
    {
        // ── 세계 ────────────────────────────────────────────────
        // 달(1.62)보다 조금 무겁고 화성(3.71)보다 가볍다. 조종 가능한 선에서 가장 둥실거리는 값.
        public const float Gravity = -2.8f;
        public const float FixedStep = 0.01f;       // 100Hz. 조인트가 많아서 촘촘하게 밟는다
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
        public const float JumpSpeed = 3.4f;        // 최고점 v²/2g = 2.06m, 체공 2.4초
        public const float StepPeriod = 0.80f;      // 걷기 주기(초). 저중력이라 성큼성큼 느리게

        // ── 체력 · 피해 ────────────────────────────────────────
        // 일반 점프는 안전하게 유지하고, 높은 곳에서 빠르게 착지했을 때만 낙하 피해를 준다.
        public const float MaxHealth = 100f;
        public const float FallDamageMinHeight = 2.75f;
        public const float FallDamageMinSpeed = 3.8f;
        public const float FallDamageBase = 10f;
        public const float FallDamagePerSpeed = 15f;

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
        public const float GripBreakForce = 4000f;  // 저중력이라 사실상 놓치지 않는다
        public const float GripBreakTorque = 2600f;
        /// <summary>손을 뗀 뒤 다시 잡을 수 있게 되기까지. 짧을수록 관대하다.</summary>
        public const float RegrabLock = 0.06f;

        // ── 들기 · 나르기 ───────────────────────────────────────
        // 잡은 물체는 ForceMode.Acceleration 으로 끌어온다 → 무게와 상관없이 같은 속도로 온다.
        // 55kg 궤짝이 5kg 통조림처럼 다뤄지는 이유가 이 두 줄이다.
        public const float CarryAccel = 42f;        // 1/s² — 목표점까지의 거리에 곱한다
        public const float CarryDamp = 8f;          // 1/s
        public const float CarryAccelMax = 26f;     // m/s²
        /// <summary>잡은 물체의 무게를 이만큼 대신 들어준다 (1 = 완전 무중력처럼).</summary>
        public const float CarrySupport = 0.85f;
        /// <summary>물체를 당긴 반작용 중 몸이 실제로 받는 비율. 크면 무거운 걸 잡을 때 딸려간다.</summary>
        public const float CarryReaction = 0.16f;
        /// <summary>두 손으로 같은 것을 잡으면 이만큼 더 세게 다룬다.</summary>
        public const float TwoHandBonus = 1.6f;
        /// <summary>조인트의 connectedMassScale — 낮을수록 팔이 물체 무게를 덜 느낀다.</summary>
        public const float CarryMassScale = 0.35f;

        // ── 도구 투척 ──────────────────────────────────────────
        public const float ToolChargeDuration = 2.0f;
        public const float ToolThrowSpeed = 14f;
        public const float ToolDropChargeThreshold = 0.08f;
        public const float ToolThrowRegrabLock = 0.30f;

        // ── 기어오르기 ──────────────────────────────────────────
        public const float ClimbPull = 150f;        // 매달린 채 위로 기어오르는 보조력
        public const float HangPull = 0.85f;        // 매달렸을 때 팔 힘이 몸을 끌어당기는 비율

        // ── 카메라 ──────────────────────────────────────────────
        public const float CamDistance = 3.6f;
        public const float CamHeight = 0.55f;
    }
}
