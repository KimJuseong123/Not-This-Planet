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
        /// <summary>
        /// 이 명령을 지금 받을 수 없다면 <b>왜 못 받는지</b>. 받을 수 있으면 <c>null</c>
        /// (S15P21B202-715).
        ///
        /// <para>전에는 이 판단 전체가 <c>bool</c> 하나였다. 그래서 화면에 남는 것은
        /// <c>0/1명</c> 이라는 숫자뿐이었고, <b>왜 거절했는지는 아무 데도 없었다</b> —
        /// 고치려면 매번 추측해야 했다.</para>
        ///
        /// <para><b>값으로 받는다.</b> 씬도 네트워크도 없이 확인할 수 있어야, 다음에 조건이
        /// 늘어도 이 표가 실제 동작과 같은지를 시험이 말해 준다.</para>
        ///
        /// <para><c>cancel</c> 은 거의 모든 것을 통과한다. <b>멈추라는 말은 언제나 들어야</b>
        /// 하기 때문이다 — 기절했든 연출 중이든 진행 중인 작업은 멈출 수 있어야 한다.</para>
        /// </summary>
        public static string OrderRefusal(string action, bool aiSeat, bool roomActive, bool downed,
                                          bool ridingBlocks, bool cutscene, bool danceBlocked,
                                          bool speakerDowned = false)
        {
            if (!aiSeat) return "AI 자리가 아니거나 몸이 없음";
            if (action == "cancel") return null;
            // **쓰러진 사람의 말도 듣는다 — 다만 뜻을 잃는 것은 여기서 거절한다**
            // (S15P21B202-730). `TickCore` 와 `AiWorkTask.Tick` 이 말한 사람이 쓰러지면
            // 살리기만 빼고 작업을 끊으므로, 막지 않으면 **시작했다가 곧바로 죽는다** —
            // 화면에는 `follow 3/3명` 이 뜨고 동료는 가만히 있는다. 그쪽이 더 나쁘다.
            if (speakerDowned && action != "revive") return "말한 사람이 기절해 그 명령은 뜻을 잃음";
            if (!roomActive) return "방이 명령을 받는 상태가 아님";
            if (downed) return "기절해 있음";
            if (ridingBlocks) return "탑승 중이라 그 명령은 못 함";
            if (cutscene) return "연출·엔딩 중";
            if (danceBlocked) return "춤을 아직 못 춤 (쿨다운이거나 동작이 없음)";
            return null;
        }

        /// <summary>
        /// <b>말한 사람 쪽이 아닌 곳으로 모이라는 것은 <c>goto</c> 와 같은 일이다</b>
        /// (S15P21B202-729).
        ///
        /// <para><c>gather</c> 는 말한 사람을 따라가는 것으로 끝나고 <c>destination</c> 을
        /// 아예 보지 않았다. 그래서 <c>다들 조립대로 모여</c> 가 <b>말한 사람 앞으로</b>
        /// 모였다 — 거절도 오류도 아니라 화면에는 <c>gather 4/4명</c> 이라고 떴다.
        /// <b>시킨 대로 된 것처럼 보이는 것</b>이 이 버그의 전부다.</para>
        ///
        /// <para>규칙을 두 곳이 각자 알면 또 갈린다. 여기 한 번 적고
        /// <see cref="AiCompanionController.Apply"/> 와
        /// <see cref="AiGroupOrders.ExecutePlan"/> 이 같이 읽는다.</para>
        /// </summary>
        public static bool GathersAwayFromSpeaker(string action, string destination)
            => action == "gather" && destination != "default" && destination != "speaker";

        /// <summary>
        /// 행동을 <b>사람 말로</b> (S15P21B202-736).
        ///
        /// <para>명령 뒤 화면에 돌아가던 것은 <c>AI 동료 1명이 명령을 받았어요</c> 뿐이라
        /// <b>무엇을 시킨 것인지가 없었다.</b> 2026-09-23 QA 에서 헛말이 춤으로 실행됐는데
        /// 플레이어는 동료가 왜 춤추는지 알 방법이 없었다.</para>
        ///
        /// <para>모르는 것은 <b>그대로 돌려준다.</b> 빈 문자열로 만들면 새 행동이 생겼을 때
        /// 화면에서 조용히 사라진다 — 영어라도 보이는 편이 낫다.</para>
        /// </summary>
        public static string ActionName(string action)
        {
            switch (action)
            {
                case "follow": return "따라오기";
                case "wait": return "기다리기";
                case "gather": return "모이기";
                case "goto": return "그곳으로 가기";
                case "cancel": return "하던 일 그만두기";
                case "revive": return "쓰러진 동료 일으키기";
                case "pull_pin": return "고정못 뽑기";
                case "pull_all_pins": return "고정못 모두 뽑기";
                case "pickup_tool": return "도구 집기";
                case "pickup_bat": return "방망이 집기";
                case "pickup_welder": return "용접건 집기";
                case "drop_tool": return "도구 내려놓기";
                case "bring_tool": return "도구 가져다주기";
                case "bring_bat": return "방망이 가져다주기";
                case "bring_welder": return "용접건 가져다주기";
                case "hit_target": return "연습공 때리기";
                case "defend_meteors": return "운석 막기";
                case "grab_part": return "파츠 잡고 있기";
                case "release_part": return "파츠 놓기";
                case "carry_part": return "파츠 옮기기";
                case "weld": return "용접하기";
                case "cut_weld": return "용접 떼기";
                case "board_tractor": return "견인 장치 타기";
                case "leave_tractor": return "견인 장치에서 내리기";
                case "move_tractor": return "견인 장치 옮기기";
                case "hold_tractor": return "견인 장치 자리 지키기";
                case "lift_player": return "동료 올려주기";
                case "lift_part": return "파츠 들어 올리기";
                case "transport_part": return "파츠 운반하기";
                case "stack_part": return "파츠 위에 쌓기";
                case "dance": return "춤추기";
                case "greet": return "인사하기";
                case "heart": return "하트";
                case "cheer": return "응원하기";
                default: return action;
            }
        }

        /// <summary>대상을 사람 말로. 색은 <see cref="TeamSlots.Names"/> 와 같아야 한다.</summary>
        public static string RecipientName(string recipients)
        {
            switch (recipients)
            {
                case "all": return "모두";
                case "one": return "한 명";
                case "two": return "둘";
                case "remaining": return "나머지";
                case "red": return "빨강";
                case "blue": return "파랑";
                case "yellow": return "노랑";
                case "green": return "초록";
                default: return "가까운 동료";
            }
        }

        /// <summary>
        /// 말한 사람에게 돌려줄 한 줄 — <b>무엇을 누구에게 시켰는가</b> (S15P21B202-736).
        ///
        /// <para>계획이 비었으면 <c>null</c>. 부르는 쪽이 들은 말과 이어 붙인다.</para>
        /// </summary>
        public static string Spoken(AiCommandAssignment[] orders)
        {
            if (orders == null || orders.Length == 0) return null;
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < orders.Length; i++)
            {
                var order = orders[i];
                if (order == null || order.steps == null || order.steps.Length == 0) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(RecipientName(order.recipients)).Append("에게 ");
                for (int s = 0; s < order.steps.Length; s++)
                {
                    if (s > 0) text.Append(" 다음 ");
                    text.Append(ActionName(order.steps[s] != null ? order.steps[s].action : null));
                }
            }
            return text.Length > 0 ? text.ToString() : null;
        }

        /// <summary>
        /// 자리 번호와 계획의 대상 이름. <see cref="TeamSlots"/> 의 순서와 같다.
        ///
        /// <para>한 곳에만 둔다 — <see cref="AiGroupOrders.Select"/> 가 색을 자리로 풀고
        /// <see cref="ForTarget"/> 가 자리를 색으로 되돌린다. 두 곳이 각자 표를 가지면
        /// 한쪽만 고쳐졌을 때 <b>버튼으로 고른 동료와 다른 동료가 움직인다.</b></para>
        /// </summary>
        static readonly string[] SlotRecipients = { "red", "blue", "yellow", "green" };

        /// <summary>색 대상의 자리 번호. 색이 아니면 <c>-1</c>.</summary>
        public static int SlotOf(string recipients) => Array.IndexOf(SlotRecipients, recipients);

        /// <summary>자리 번호의 색 대상. 범위 밖이면 <c>null</c>.</summary>
        public static string RecipientOf(int slot)
            => slot >= 0 && slot < SlotRecipients.Length ? SlotRecipients[slot] : null;

        /// <summary>
        /// <b>버튼으로 고른 동료에게 보내는 계획</b>으로 바꾼다 (S15P21B202-738).
        ///
        /// <para><b>왜.</b> 명령 실패가 두 곳에서 나는데 둘 다 "누구에게" 였다. 받아쓰기는
        /// 문장 첫머리의 색을 가장 많이 틀리고(<c>노랑</c>↔<c>너랑</c> 자모 거리 1), LLM 은
        /// 색을 제대로 들어도 대상을 틀린다(<c>초록아 방망이 잡아</c> → <c>one</c>).
        /// 버튼으로 고르면 <b>둘 다 통째로 사라진다</b> — 말에는 행동만 남는다.</para>
        ///
        /// <para><b>버튼이 말을 덮는다.</b> 말 속의 대상은 무시하고 버튼의 색으로 고친다.
        /// 여러 지시가 왔으면 <b>그 색에게 한 지시</b>를 고르고, 없으면 첫 지시를 쓴다 —
        /// <c>나머지는 따라와</c> 처럼 버튼과 안 맞는 말도 버튼의 동료가 한다.</para>
        ///
        /// <para><c>default</c> 로 두지 않고 <b>색으로</b> 적는 이유: 화면에 돌아가는 한 줄
        /// (<see cref="Spoken"/>)이 <c>가까운 동료에게</c> 가 아니라 <c>빨강에게</c> 가 되고,
        /// 로그(<see cref="Describe"/>)에도 누구에게 갔는지가 남는다.</para>
        ///
        /// <para><b>고르지 않았으면(<see cref="AiTalkKeys.Anyone"/>) 계획을 그대로 돌려준다.</b>
        /// 부르는 쪽이 "골랐을 때만 부른다" 를 따로 적지 않게 한다 — 그 가드가 빠지면 V 로 한
        /// 명령이 <b>전부</b> 잘못된 계획이 되는데, 글자 검사로는 그것을 못 잡는다(리뷰).</para>
        ///
        /// <para>계획이 실행할 수 없는 꼴이거나 자리가 범위 밖이면 <c>null</c>.</para>
        /// </summary>
        public static AiCommandAssignment[] ForTarget(AiCommandAssignment[] orders, int slot)
        {
            if (!Valid(orders)) return null;
            if (slot == AiTalkKeys.Anyone) return orders;
            string target = RecipientOf(slot);
            if (target == null) return null;
            var picked = orders[0];
            foreach (var order in orders)
                if (order.recipients == target) { picked = order; break; }
            return new[] { new AiCommandAssignment { recipients = target, steps = picked.steps } };
        }

        public static bool Action(string action) => AiCompanionController.IsSocial(action) || AiWorkTask.Supports(action)
            || action == "follow" || action == "gather" || action == "wait" || action == "cancel" || action == "pull_pin";
        public static bool Recipients(string value) => value == "default" || value == "all" || value == "one" || value == "two"
            || value == "remaining" || value == "red" || value == "blue" || value == "yellow" || value == "green";
        public static bool Destination(string value) => value == "default" || value == "speaker" || value == "aim" || value == "assembly" || value == "high";
        public static bool Continuous(string action) => action == "follow" || action == "wait" || action == "defend_meteors"
            || action == "grab_part" || action == "hold_tractor" || action == "lift_part" || action == "goto";
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
