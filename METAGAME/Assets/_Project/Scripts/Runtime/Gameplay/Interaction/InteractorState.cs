namespace Metagame.Gameplay.Interaction
{
    public enum InteractorState
    {
        /// <summary>바라보는 대상이 없다.</summary>
        Idle = 0,

        /// <summary>대상을 바라보고 있지만 아직 시작하지 않았다.</summary>
        Focused,

        /// <summary>홀드 상호작용을 진행 중이다.</summary>
        Holding,
    }
}
