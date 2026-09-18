namespace Metagame.Core
{
    /// <summary>
    /// F2 로 고를 수 있는 춤 목록 (S15P21B202-563).
    ///
    /// <para><see cref="EmoteState"/> 와 나란한 자리다. 다른 점은 <b>지속 시간을 공유하지
    /// 않는다</b>는 것이다 — 이모티콘은 화면에 몇 초 떠 있는 그림이라 만료 시각을 나눠야
    /// 하지만, 춤은 <b>몸이 재생하는 클립</b>이라 시작 신호만 가면 각자 제 시계로 끝까지
    /// 돈다. 그래서 던지기(S15P21B202-519)와 같이 <b>순번</b>으로 나른다.</para>
    ///
    /// <para>춤을 더 넣을 때는 <see cref="Names"/> 에 한 줄, 그리고 그 이름의 클립을
    /// <c>RabbitLocomotion</c> 에 한 줄 더하면 된다.</para>
    /// </summary>
    public static class DanceCatalog
    {
        /// <summary>
        /// 고를 수 있는 춤의 이름. 순서가 곧 번호다(1부터).
        ///
        /// <para>여기 한 줄을 더할 때 <c>RabbitLocomotion</c> 의 클립 상수와 한 번짜리
        /// 슬롯도 같이 늘려야 한다. 어긋나면 고를 수는 있는데 아무 일도 안 일어난다 —
        /// 그래서 검증이 개수를 맞대어 본다.</para>
        /// </summary>
        public static readonly string[] Names = { "뮤직챌린지", "REDRED", "BAD" };

        public static byte Count => (byte)Names.Length;

        /// <summary>
        /// 연달아 고를 수 있는 가장 짧은 간격(초).
        ///
        /// <para>이모티콘(0.8초)보다 길다. 춤은 14초짜리라, 짧게 잡으면 끝나기도 전에
        /// 다시 눌러 처음으로 되돌리는 것을 계속 할 수 있다.</para>
        /// </summary>
        public const double Cooldown = 1.5;

        public static bool IsValid(byte id) => id >= 1 && id <= Count;

        /// <summary>번호에 해당하는 이름. 범위를 벗어나면 빈 문자열이다.</summary>
        public static string NameOf(byte id) => IsValid(id) ? Names[id - 1] : string.Empty;
    }
}
