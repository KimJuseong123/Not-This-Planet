namespace Metagame.Network
{
    /// <summary>
    /// AI 동료 머리 위에 띄우는 "지금 하는 일" (S15P21B202-746).
    ///
    /// <para>2026-09-26 QA: 명령 결과가 화면 아래 작은 글씨로만 나와 플레이어도 시연을 보는 사람도 읽지 못했다.
    /// 동료가 무엇을 하는지는 <b>그 동료 위에</b> 크게 보여야 한다. 서버가 바이트 하나로 동기화하고, 화면은 이 표로
    /// 글자를 고른다 — 서버의 상태 문구(거리 숫자가 매 틱 바뀐다)를 그대로 보내지 않는다.</para>
    /// </summary>
    public static class AiBadges
    {
        public const byte None = 0, Defend = 1, Revive = 2, Pin = 3, Come = 4, Dodge = 5;

        /// <summary>지금 하는 일. 기본 행동 중이면 없음. <b>순수 함수</b>다.</summary>
        public static byte Of(string command, bool dodging)
        {
            if (dodging) return Dodge;
            switch (command)
            {
                case "defend_meteors": return Defend;
                case "revive": return Revive;
                case "pull_pin": return Pin;
                case "come": return Come;
                default: return None;
            }
        }

        public static string Label(byte badge)
        {
            switch (badge)
            {
                case Defend: return "운석 막는 중";
                case Revive: return "살리러 가는 중";
                case Pin: return "고정못 뽑는 중";
                case Come: return "가는 중";
                case Dodge: return "운석 피하는 중!";
                default: return null;
            }
        }

        /// <summary>한 건 해낼 때마다 띄우는 말 — 고정못 N개째 · 운석 N개 쳐냄.</summary>
        public static string Tally(string command, int count)
            => command == "pull_pin" ? "고정못 " + count + "개째!" : command == "defend_meteors" ? "운석 " + count + "개 쳐냄!" : null;

        /// <summary>작업이 끝났을 때 띄우는 말. 성공은 짧게, 실패는 까닭까지.</summary>
        public static string Finished(string command, bool succeeded, string reason)
        {
            if (succeeded)
                return command == "revive" ? "살렸다!" : command == "come" ? "도착!" : command == "pull_pin" ? "고정못 다 뽑음!" : "완료!";
            return "못 함 · " + (string.IsNullOrEmpty(reason) ? "까닭 모름" : reason);
        }

        /// <summary>결과를 이만큼 보여 준다(초).</summary>
        public const float NoteSeconds = 3.5f;
    }
}
