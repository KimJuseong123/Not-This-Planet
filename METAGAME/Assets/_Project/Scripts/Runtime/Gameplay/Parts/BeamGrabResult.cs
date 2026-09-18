namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 견인 광선이 파츠를 붙잡으려 한 결과. 실패 사유는 UI 피드백 문구를 고르는 데 쓴다.
    /// </summary>
    public enum BeamGrabResult
    {
        /// <summary>붙잡았다.</summary>
        Grabbed = 0,

        /// <summary>조준선에 파츠가 없다.</summary>
        NoTarget,

        /// <summary>고정쇠가 남아 있어 아직 떼어낼 수 없다.</summary>
        Clamped,

        /// <summary>이미 용접되어 떼어낼 수 없다.</summary>
        Welded,

        /// <summary>다른 빔이 이미 들고 있다.</summary>
        AlreadyCarried,

        /// <summary>이 빔이 이미 다른 파츠를 들고 있다.</summary>
        BeamBusy,

        /// <summary>
        /// 사람이 타고 있는 견인선이라 집을 수 없다 (S15P21B202-554).
        ///
        /// <para><see cref="AlreadyCarried"/> 와 섞지 않는다. 그쪽은 "다른 빔이 들고
        /// 있으니 기다리면 된다" 이고, 이쪽은 "타고 있는 사람이 내려야 한다" 다 —
        /// 사람에게 할 말이 다르다.</para>
        /// </summary>
        Occupied,
    }
}
