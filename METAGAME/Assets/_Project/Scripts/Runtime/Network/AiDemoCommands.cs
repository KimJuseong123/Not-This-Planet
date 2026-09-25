namespace Metagame.Network
{
    /// <summary>
    /// 라이브 시연에서 AI 동료가 받는 음성 명령은 <b>넷뿐이다</b> (S15P21B202-746).
    ///
    /// <para><b>왜.</b> 2026-09-25 QA: 받아쓰기는 조용한 곳에서 어느 정도 되지만 그 뒤의 행동이
    /// 무너졌다 — 고정못 하나를 제대로 못 뽑았다. 서른 개가 넘는 행동을 조금씩 하는 것보다 넷을
    /// 제대로 하는 것이 시연에 맞다.</para>
    ///
    /// <para><b>게임 쪽에서 좁힌다.</b> 판단기(워커)가 무엇을 내든 여기서 넷 중 가장 가까운 것으로
    /// 옮기고, 옮길 곳이 없으면 거절한다. 그래서 이미 깔린 팩(판단기가 서른 개를 아는)으로도 바로
    /// 넷만 한다. 판단기가 <c>pickup_bat</c> 를 내도 "방망이 주워서 운석 쳐내기" 로 간다.</para>
    /// </summary>
    public static class AiDemoCommands
    {
        /// <summary>끄면 예전처럼 판단기의 행동을 그대로 쓴다 — 시연이 끝나면 되돌릴 자리.</summary>
        public static bool Enabled = true;

        public const string Defend = "defend_meteors", Revive = "revive", Pin = "pull_pin", Come = "come";

        /// <summary>시연에서 받는 네 행동. 이 순서로 화면과 문서가 읽는다.</summary>
        public static readonly string[] Allowed = { Defend, Revive, Pin, Come };

        public const string Refusal = "시연에서는 운석 쳐내기 · 살리기 · 고정못 뽑기 · 이쪽으로 와 만 할 수 있어요";

        /// <summary>
        /// 판단기의 행동을 넷 중 하나로 옮긴다. 옮길 곳이 없으면 null. <b>순수 함수</b>다.
        ///
        /// <para>옮기는 것은 <b>뜻이 같은 쪽</b>만이다 — 방망이·운석은 쳐내기로, 못은 뽑기로,
        /// "나에게 와" 류는 오기로. <c>goto</c> 는 말한 사람 쪽일 때만 오기다. 조립대·저기로
        /// 가라는 말을 "나에게 와" 로 바꾸면 시킨 것과 다른 일을 한다.</para>
        /// </summary>
        public static string Map(string action, string destination = "default")
        {
            switch (action)
            {
                case Defend: case "pickup_bat": case "bring_bat": case "hit_target": return Defend;
                case Revive: return Revive;
                case Pin: case "pull_all_pins": return Pin;
                case Come: case "follow": case "gather": return Come;
                case "goto": return destination == "speaker" || destination == "default" || string.IsNullOrEmpty(destination) ? Come : null;
                default: return null;
            }
        }
    }
}
