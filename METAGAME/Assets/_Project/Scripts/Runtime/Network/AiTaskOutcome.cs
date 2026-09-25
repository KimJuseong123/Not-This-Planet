namespace Metagame.Network
{
    /// <summary>
    /// AI 동료가 받은 작업이 어떻게 끝났는가 (S15P21B202-703).
    ///
    /// <para><b>왜 따로 두나.</b> 전에는 <see cref="AiWorkTask.Active"/> 와
    /// <see cref="AiWorkTask.Succeeded"/> 두 개로만 알 수 있었다 — 실패 · 취소 · 시작도 안 함이
    /// 모두 <c>!Active && !Succeeded</c> 로 같아 보였다. 화면 글자(<c>Status</c>)는 사람이 읽는
    /// 문구라 검사도 로그도 그것으로 가를 수 없고, 취소되면 따라다니기의 "대기" 로 덮여 이유가
    /// 사라졌다. "AI 가 이상한 짓을 했다" 를 받아쓰기 · 판단 · 실행 중 어디 탓인지 가르려면
    /// <b>실행의 끝</b>이 값으로 남아야 한다.</para>
    /// </summary>
    public enum AiTaskOutcome : byte { None, Running, Succeeded, Failed, Cancelled }

    public static class AiTaskOutcomes
    {
        public static string Label(AiTaskOutcome outcome)
        {
            switch (outcome)
            {
                case AiTaskOutcome.Running: return "진행 중";
                case AiTaskOutcome.Succeeded: return "완료";
                case AiTaskOutcome.Failed: return "실패";
                case AiTaskOutcome.Cancelled: return "취소";
                default: return "없음";
            }
        }

        public static bool Ended(AiTaskOutcome outcome)
            => outcome == AiTaskOutcome.Succeeded || outcome == AiTaskOutcome.Failed || outcome == AiTaskOutcome.Cancelled;

        /// <summary>
        /// <c>Player.log</c> 에 남기는 한 줄. 판단 로그(<c>[AI] 계획</c>)와 같은 머리를 써서
        /// 한 명령의 "들음 → 계획 → 결과" 가 이어서 읽히게 한다.
        /// </summary>
        public static string LogLine(string task, AiTaskOutcome outcome, string reason, float seconds)
            => "[AI] 작업 결과 " + (string.IsNullOrEmpty(task) ? "?" : task) + " → " + Label(outcome)
               + (string.IsNullOrEmpty(reason) ? "" : " · " + reason)
               + " (" + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "초)";
    }
}
