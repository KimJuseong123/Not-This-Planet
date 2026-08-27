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
        public const float PartWidth = 3f;
        public const float PartHeight = 3.7f;

        /// <summary>놓이거나 용접된 상태의 질량. 혼자서는 밀 수 없는 무게다.</summary>
        public const float PartMass = 500f;

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

        // --- 전역 물리 설정 기록값 (ProjectSettings/DynamicsManager.asset) ---
        // Unity 6 기본값이 이미 목표치 이상이라 변경하지 않았다. 회귀 감시용으로만 기록한다.
        public const int SolverIterations = 14;
        public const int SolverVelocityIterations = 4;
        public const float MaxAngularSpeed = 28f;
    }
}
