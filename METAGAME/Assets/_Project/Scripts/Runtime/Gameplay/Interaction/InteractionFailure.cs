namespace Metagame.Gameplay.Interaction
{
    /// <summary>
    /// 상호작용이 거절된 이유. UI 피드백 문구를 고르는 데 사용한다.
    /// </summary>
    public enum InteractionFailure
    {
        None = 0,

        /// <summary>바라보는 곳에 대상이 없다.</summary>
        NoTarget,

        /// <summary>대상이 사거리 또는 시야각을 벗어났다.</summary>
        OutOfRange,

        /// <summary>다른 플레이어가 이미 이 대상을 사용 중이다.</summary>
        Occupied,

        /// <summary>대상의 조건이 아직 충족되지 않았다. 예: 고정쇠가 남아 있다.</summary>
        NotReady,

        /// <summary>이 Interactor 가 이미 다른 상호작용을 진행 중이다.</summary>
        Busy,
    }
}
