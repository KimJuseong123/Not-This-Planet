namespace Metagame.Gameplay.Interaction
{
    /// <summary>
    /// 특정 Interactor 가 지금 이 대상과 상호작용할 수 있는지와, 안 되는 경우의 이유.
    /// </summary>
    public readonly struct InteractionAvailability
    {
        InteractionAvailability(bool isAllowed, InteractionFailure failure)
        {
            IsAllowed = isAllowed;
            Failure = failure;
        }

        public bool IsAllowed { get; }
        public InteractionFailure Failure { get; }

        public static InteractionAvailability Allowed => new(true, InteractionFailure.None);

        public static InteractionAvailability Denied(InteractionFailure failure) => new(false, failure);
    }
}
