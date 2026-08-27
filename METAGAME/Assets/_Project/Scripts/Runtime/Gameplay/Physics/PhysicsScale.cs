namespace Metagame.Gameplay
{
    /// <summary>
    /// 1스테이지 물리 기준값. 1 unit = 1 meter.
    /// 파츠·플레이어·도구 프리팹의 Rigidbody/Collider 수치는 이 값을 기준으로 설정한다.
    /// </summary>
    public static class PhysicsScale
    {
        // --- 단위 ---
        public const float MetersPerUnit = 1f;
        public const float Gravity = -9.81f;

        /// <summary>50Hz. ProjectSettings/TimeManager.asset 의 Fixed Timestep 과 일치한다.</summary>
        public const float FixedTimestep = 0.02f;

        // --- 플레이어 ---
        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.3f;
        public const float PlayerMass = 70f;

        // --- 우주선 파츠 ---
        public const float PartWidth = 4f;
        public const float PartHeight = 5f;
        public const float PartMass = 1000f;

        /// <summary>
        /// 견인 중인 파츠에 적용하는 취급 질량.
        ///
        /// 파츠는 견인 중에도 플레이어와 충돌해야 한다. 동료를 툭 치며 방해하는 것이
        /// 협동 게임의 재미이기 때문이다. 다만 1000kg 그대로 두면 플레이어를 압사시키므로
        /// 견인 상태 동안만 이 값으로 낮춘다. 내려놓는 순간 PartMass 로 되돌린다.
        ///
        /// 사람 한 명 무게로 잡았다. 밀칠 수는 있지만 깔아뭉갤 수는 없는 값이다.
        /// 더 낮추면(예: 10kg) 운반 속도로 부딪혀도 플레이어가 2cm 남짓 밀릴 뿐이라
        /// "방해"가 성립하지 않는다. 운동량이 질량에 비례하기 때문이다.
        ///
        /// "1000kg을 빔으로 들면 가벼운 느낌"은 이 질량이 아니라 견인 빔의 제어 게인으로
        /// 만든다. 취급감과 충돌 위력은 분리한다.
        ///
        /// 기절한 플레이어(PlayerDowned)는 레이어 매트릭스에서 계속 제외한다.
        /// 스스로 빠져나올 수 없어 파츠에 눌리면 구조가 불가능해지기 때문이다.
        /// </summary>
        public const float CarriedPartMass = PlayerMass;

        /// <summary>
        /// 견인 중 대형 파츠의 회전 폭주를 막기 위한 per-Rigidbody 제한값.
        /// 전역 m_DefaultMaxAngularSpeed 는 소품·운석까지 함께 묶이므로 건드리지 않고,
        /// Part 프리팹의 Rigidbody.maxAngularVelocity 에만 적용한다.
        /// 견인 구현 티켓에서 실측 후 조정한다.
        /// </summary>
        public const float PartMaxAngularSpeed = 5f;

        // --- 견인 도구 ---
        public const float CarrierMass = 50f;

        // --- 전역 물리 설정 기록값 (ProjectSettings/DynamicsManager.asset) ---
        // Unity 6 기본값이 이미 목표치 이상이라 변경하지 않았다. 회귀 감시용으로만 기록한다.
        public const int SolverIterations = 14;
        public const int SolverVelocityIterations = 4;
        public const float MaxAngularSpeed = 28f;
    }
}
