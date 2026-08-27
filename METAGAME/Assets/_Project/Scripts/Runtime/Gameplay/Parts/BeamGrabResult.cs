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
    }
}
