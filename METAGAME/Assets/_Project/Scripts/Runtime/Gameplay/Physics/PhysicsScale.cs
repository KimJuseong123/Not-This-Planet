using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 퍼즐·상호작용에 쓰는 물리 기준값. 1 unit = 1 meter.
    ///
    /// 세계와 캐릭터 값은 여기서 정의하지 않고 <see cref="Tuning"/> 을 그대로 쓴다.
    /// 중력·틱·몸무게가 두 곳에 따로 있으면 서로 어긋나기 때문이다.
    /// 이 클래스가 정의하는 것은 파츠와 견인 도구처럼 퍼즐 전용인 값뿐이다.
    ///
    /// 치수와 질량은 캐릭터를 기준자로 삼아 비율로 정했다.
    /// 캐릭터가 1.33m / 35kg 이므로 파츠는 그 2.8배 높이에 약 14배 질량이다.
    /// </summary>
    public static class PhysicsScale
    {
        // --- 단위 ---
        public const float MetersPerUnit = 1f;

        // --- 우주선 파츠 ---

        /// <summary>
        /// 형태를 따로 정하지 않은 파츠의 기본 크기. <b>실제 스테이지1 파츠는 이 값을 쓰지 않는다</b> —
        /// 네 파츠는 각자의 실측 형태를 <see cref="Parts.PartSpec"/> 에 갖고 있다.
        /// 이 값은 테스트가 만드는 일반 파츠와, 명세가 없는 파츠의 폴백으로만 남는다.
        /// </summary>
        public const float PartWidth = 3f;
        public const float PartHeight = 3.7f;

        /// <summary>
        /// 형태를 따로 정하지 않은 파츠의 질량. 위와 같은 이유로 폴백이다.
        /// 실제 파츠 질량은 <see cref="Parts.PartSpec.Mass"/> 가 부피에서 계산한다.
        /// </summary>
        public const float PartMass = 500f;

        /// <summary>
        /// 파츠 밀도(kg/m³). 파츠 질량을 결정하는 <b>유일한 손잡이</b>다.
        ///
        /// 질량을 파츠마다 손으로 적지 않는 이유는 두 가지다. 모델이 바뀌면 네 값을 따로
        /// 고쳐야 하고, 고치는 사이 서로의 비율이 슬그머니 어긋난다. 부피에 밀도를 곱하면
        /// 형태가 바뀔 때 질량이 저절로 따라온다.
        ///
        /// 70 은 네 파츠의 평균 질량이 예전 고정값 <see cref="PartMass"/>(500kg) 근처에
        /// 오도록 잡은 값이다. 기획서 용어로 <b>초기 테스트값</b>이며 플레이 테스트로 조정한다.
        /// </summary>
        public const float PartDensity = 70f;

        /// <summary>
        /// 견인 중인 파츠에 적용하는 취급 질량.
        ///
        /// 파츠는 견인 중에도 플레이어와 충돌한다. 동료를 툭 치며 방해하는 것이
        /// 협동 게임의 재미이기 때문이다. 다만 놓인 상태의 질량 그대로면 플레이어를
        /// 짓눌러 버리므로 견인 중에만 이 값으로 낮춘다. 내려놓는 순간 <see cref="PartMass"/> 로 돌린다.
        ///
        /// 사람 한 명 무게로 잡았다. 밀칠 수는 있지만 깔아뭉갤 수는 없는 값이다.
        /// 이보다 크게 낮추면 운반 속도로 부딪혀도 플레이어가 몇 cm 밀릴 뿐이라
        /// 방해가 성립하지 않는다. 충돌 위력이 질량에 비례하기 때문이다.
        ///
        /// "무거운 것을 빔으로 들면 가벼운 느낌"은 이 질량이 아니라 견인 빔의 제어 게인으로
        /// 만든다. 캐릭터의 손 잡기가 이미 같은 방식을 쓴다
        /// (<see cref="Tuning.CarryMassScale"/>, <see cref="Tuning.CarrySupport"/>).
        /// </summary>
        public const float CarriedPartMass = Tuning.BodyMass;

        /// <summary>
        /// 견인 중 대형 파츠의 회전 폭주를 막기 위한 per-Rigidbody 제한값.
        /// 전역 최대 각속도는 소품·운석까지 함께 묶이므로 건드리지 않고
        /// Part 프리팹의 Rigidbody.maxAngularVelocity 에만 적용한다.
        /// 견인 구현 티켓에서 실측 후 조정한다.
        /// </summary>
        public const float PartMaxAngularSpeed = 5f;

        // --- 견인 도구 ---
        /// <summary>비행접시형 운반 도구. 파츠에 끌려다니지 않을 정도의 무게.</summary>
        public const float CarrierMass = 25f;

        // --- 소켓 배치 ---
        /// <summary>두 소켓이 이 거리 안에 있으면 스냅 후보가 된다.</summary>
        public const float SnapDistance = 1.5f;

        /// <summary>두 접합면이 마주보는 오차가 이 각도 안이면 스냅 후보가 된다.</summary>
        public const float SnapAngleDegrees = 30f;

        /// <summary>
        /// 용접 전 파츠를 잡아 두는 조인트가 끊어지는 힘.
        ///
        /// 운석 충격에는 끊어지고 일반 조작에는 끊어지지 않아야 하는 값이다.
        /// S15P21B202-70 에서 운석을 실제로 떨어뜨려 확정했다.
        /// 60kg 운석이 30m 에서 떨어질 때 이 값은 끊어지고 WeldedBreakForce 는 버틴다.
        /// 탑은 스스로 무너지지 않는다.
        /// </summary>
        public const float UnweldedBreakForce = 8000f;

        /// <summary>용접 전 조인트가 끊어지는 토크. 위 항목과 함께 S15P21B202-70 에서 확정했다.</summary>
        public const float UnweldedBreakTorque = 5000f;

        // --- 용접 ---
        /// <summary>용접점 하나를 채우는 데 걸리는 시간.</summary>
        public const float WeldSecondsPerPoint = 3f;

        /// <summary>
        /// 용접이 끝난 접합이 버티는 힘. 미접합의 25배다.
        ///
        /// 무한으로 두지 않았다. 기획서가 "운석 충격에도 쉽게 분리되지 않는다" 라고
        /// 표현하고 있어, 극단적인 충격에는 떨어질 여지를 남기는 편이 의도에 맞다.
        /// </summary>
        public const float WeldedBreakForce = 200000f;

        /// <summary>용접이 끝난 접합이 버티는 토크.</summary>
        public const float WeldedBreakTorque = 150000f;

        // --- 견인 광선 ---
        /// <summary>빔이 파츠를 붙잡을 수 있는 최대 거리.</summary>
        public const float BeamRange = 12f;

        /// <summary>붙잡은 파츠를 빔 기준점 앞 몇 미터에 두는지.</summary>
        public const float BeamHoldDistance = 6f;

        /// <summary>
        /// 목표점까지의 거리에 곱하는 가속 계수(1/s²). 스프링의 k 에 해당한다.
        /// </summary>
        public const float BeamAccel = 25f;

        /// <summary>
        /// 속도에 곱하는 감쇠 계수(1/s). 스프링-댐퍼가 임계 감쇠가 되는 조건은 d = 2*sqrt(k) 이고
        /// sqrt(25) * 2 = 10 이다. 이 값을 낮추면 파츠가 목표점을 지나쳐 출렁인다.
        /// </summary>
        public const float BeamDamp = 10f;

        /// <summary>
        /// 가속 상한(m/s²). 중력 보상분(2.8)보다 충분히 커야 파츠가 처지지 않는다.
        /// </summary>
        public const float BeamAccelMax = 30f;

        /// <summary>
        /// 중력을 얼마나 대신 들어주는지. 1 이면 완전 무중력처럼 뜬다.
        /// 빔을 놓으면 보상이 사라져 그대로 떨어진다.
        /// </summary>
        public const float BeamGravitySupport = 1f;

        /// <summary>
        /// 목표 회전으로 돌리는 각속도 계수(1/s). 결과는 PartMaxAngularSpeed 로 잘린다.
        /// </summary>
        public const float BeamAngularGain = 6f;

        // --- 운석 ---
        /// <summary>
        /// 운석 질량. 이 값이 이 시스템의 조정 대상이다.
        ///
        /// 착탄 충격이 미접합 조인트(UnweldedBreakForce)는 끊고 용접된 조인트
        /// (WeldedBreakForce)는 끊지 못하는 구간에 들어와야 한다.
        /// 계산으로는 정할 수 없다. 접촉 지속 시간에 따라 실제로 걸리는 힘이 달라지므로
        /// 미접합 탑과 용접 탑에 실제로 떨어뜨려 스윕으로 잡았다.
        ///
        /// 낙하 고도 30m, 저중력 -2.8 기준 실측 결과:
        ///   20 ~ 140kg  미접합만 끊어진다 (의도한 구간)
        ///   150kg 이상  용접한 접합까지 끊어진다
        /// 안전 구간의 중앙 부근인 60kg 으로 정했다. 위아래로 여유가 있다.
        ///
        /// 실측 방법은 S15P21B202-70 기록 문서에 있다.
        /// </summary>
        public const float MeteorMass = 60f;

        /// <summary>작은 운석의 반지름. 파츠(3m 폭)보다 확실히 작아야 "작은 운석" 으로 읽힌다.</summary>
        public const float MeteorRadius = 0.45f;

        /// <summary>스폰 고도. 저중력에서 이 높이를 떨어지면 착탄 속도가 정해진다.</summary>
        public const float MeteorDropHeight = 30f;

        /// <summary>
        /// 예고부터 낙하까지의 시간. 플레이어가 한 걸음 비킬 수 있어야 한다.
        /// 기획의 미정 사항이던 "피할 수 있는 반응 시간" 을 이 값으로 정했다.
        /// </summary>
        public const float MeteorWarningSeconds = 1.8f;

        /// <summary>착탄 시 넉백이 미치는 반경.</summary>
        public const float MeteorImpactRadius = 4f;

        /// <summary>착탄 시 주변에 주는 폭발력.</summary>
        public const float MeteorImpactForce = 900f;

        /// <summary>착탄 후 사라지기까지의 시간.</summary>
        public const float MeteorDespawnSeconds = 6f;

        /// <summary>
        /// 착탄 여부와 무관한 수명 상한.
        ///
        /// 착탄 처리는 물리 충돌 콜백에 달려 있는데, 운석이 플레이 공간 밖으로 떨어지거나
        /// 콜백을 놓치면 영구히 남는다. 상한을 두어 어떤 경우에도 사라지게 한다.
        /// 낙하 시간(30m, 저중력에서 약 4.6초)보다 충분히 길어야 한다.
        /// </summary>
        public const float MeteorMaxLifeSeconds = 20f;

        // --- 운석 난이도 곡선 ---
        /// <summary>초반 낙하 간격. 조작과 핵심 루프를 익힐 여유를 준다.</summary>
        public const float MeteorIntervalStart = 8f;

        /// <summary>후반 낙하 간격.</summary>
        public const float MeteorIntervalMin = 2f;

        /// <summary>
        /// 간격이 최소까지 줄어드는 데 걸리는 시간.
        /// 1장의 목표 플레이 시간이 미정이라 임시값이다. 기획 확정 후 맞춘다.
        /// </summary>
        public const float MeteorRampSeconds = 180f;

        // --- 전역 물리 설정 기록값 (ProjectSettings/DynamicsManager.asset) ---
        // Unity 6 기본값이 이미 목표치 이상이라 변경하지 않았다. 회귀 감시용으로만 기록한다.
        public const int SolverIterations = 14;
        public const int SolverVelocityIterations = 4;
        public const float MaxAngularSpeed = 28f;
    }
}
