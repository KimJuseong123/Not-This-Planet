namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 우주선 파츠가 거치는 상태. 상태마다 물리 표현(레이어·질량·kinematic)이 다르다.
    /// </summary>
    public enum PartState
    {
        /// <summary>고정쇠로 바닥에 박혀 있다. 운석에 맞아도 움직이지 않는다.</summary>
        Locked = 0,

        /// <summary>고정쇠가 모두 풀렸다. 아직 한 번도 들리지 않았다.</summary>
        Free,

        /// <summary>견인 빔에 들려 있다. 이 동안만 가볍게 다뤄진다.</summary>
        Carried,

        /// <summary>내려놓였다. 아직 접합되지 않아 충격에 흔들리거나 무너질 수 있다.</summary>
        Placed,

        /// <summary>용접이 끝났다. 되돌아가지 않는 마지막 상태다.</summary>
        Welded,
    }
}
