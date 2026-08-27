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
