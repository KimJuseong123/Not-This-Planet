using System;

namespace Metagame.Network
{
    [Serializable]
    public sealed class AiCommandStep
    {
        public string action;
        public bool approach;
        public string destination = "default";
    }
    [Serializable]
    public sealed class AiCommandAssignment
    {
        public string recipients;
        public AiCommandStep[] steps;
    }
    public static class AiCommandPlan
    {
        public static bool Action(string action) => AiCompanionController.IsSocial(action) || AiWorkTask.Supports(action)
            || action == "follow" || action == "gather" || action == "wait" || action == "cancel" || action == "pull_pin";
        public static bool Recipients(string value) => value == "default" || value == "all" || value == "one" || value == "two"
            || value == "remaining" || value == "red" || value == "blue" || value == "yellow" || value == "green";
        public static bool Destination(string value) => value == "default" || value == "speaker" || value == "aim" || value == "assembly" || value == "high";
        public static bool Continuous(string action) => action == "follow" || action == "wait" || action == "defend_meteors"
            || action == "grab_part" || action == "hold_tractor" || action == "lift_part";
        public static bool ValidSteps(AiCommandStep[] steps)
        {
            if (steps == null || steps.Length == 0 || steps.Length > 4) return false;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step == null || !Action(step.action) || !Destination(step.destination)
                    || (step.approach && !AiCompanionController.IsSocial(step.action))
                    || (i < steps.Length - 1 && (Continuous(step.action) || step.action == "cancel"))) return false;
            }
            return true;
        }
        /// <summary>
        /// 계획을 <b>로그 한 줄로</b> (S15P21B202-708).
        ///
        /// <para>실행 요약은 <b>결과</b>만 말한다. 명령이 엉뚱하게 수행됐을 때 잘못 들은
        /// 것인지 · 잘못 계획한 것인지 · 계획대로 됐는데 결과가 다른 것인지를 가르려면
        /// <b>무엇을 시켰는지</b>가 남아 있어야 한다.</para>
        ///
        /// <para><c>green←pull_pin,follow · all←wait</c> 처럼 적는다. <c>default</c> 목적지는
        /// 적지 않는다 — 늘 붙으면 읽는 눈이 그 자리를 지나치게 된다.</para>
        ///
        /// <para><b>검증 전에도 부를 수 있어야 한다.</b> 잘못된 계획일수록 무엇이 왔는지
        /// 알아야 하므로, <c>null</c> 과 빈 배열을 값으로 다룬다.</para>
        /// </summary>
        public static string Describe(AiCommandAssignment[] orders)
        {
            if (orders == null || orders.Length == 0) return "(없음)";
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < orders.Length; i++)
            {
                if (i > 0) text.Append(" · ");
                var order = orders[i];
                text.Append(order == null || string.IsNullOrEmpty(order.recipients) ? "?" : order.recipients).Append('←');
                if (order == null || order.steps == null || order.steps.Length == 0) { text.Append("(없음)"); continue; }
                for (int s = 0; s < order.steps.Length; s++)
                {
                    if (s > 0) text.Append(',');
                    var step = order.steps[s];
                    if (step == null || string.IsNullOrEmpty(step.action)) { text.Append('?'); continue; }
                    text.Append(step.action);
                    if (step.approach) text.Append("+접근");
                    if (!string.IsNullOrEmpty(step.destination) && step.destination != "default")
                        text.Append('@').Append(step.destination);
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// 게임이 실행할 수 있는 꼴인가.
        ///
        /// <para><b>첫 지시의 <c>remaining</c> 은 막지 않는다</b> (S15P21B202-727). 전에는
        /// 막았고, 그래서 <c>나머지는 고정못 뽑아</c> 처럼 <b>한 글자도 안 틀리게 받아쓴
        /// 명령이 조용히 사라졌다.</b></para>
        ///
        /// <para>막을 이유가 없었다. <see cref="AiGroupOrders.ExecutePlan"/> 은
        /// <c>remaining</c> 을 <b>앞선 지시가 쓰지 않은 동료들</b>로 푸는데, 첫 지시에서는
        /// 그 집합이 곧 전원이라 <c>all</c> 과 같아진다 — 통과시켜도 새로 닿는 곳이 없다.</para>
        /// </summary>
        public static bool Valid(AiCommandAssignment[] orders)
        {
            if (orders == null || orders.Length == 0 || orders.Length > 3) return false;
            for (int i = 0; i < orders.Length; i++)
                if (orders[i] == null || !Recipients(orders[i].recipients) || !ValidSteps(orders[i].steps)) return false;
            return true;
        }
    }
}
