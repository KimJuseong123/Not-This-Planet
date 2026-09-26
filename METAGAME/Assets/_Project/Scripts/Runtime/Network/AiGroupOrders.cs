using System.Collections.Generic;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    public enum AiRecipients { Selected, OneIdle, TwoIdle, All }

    /// <summary>
    /// 누구를 왜 거절했는지 모아 한 줄로 만든다 (S15P21B202-715).
    ///
    /// <para>전에 남던 것은 <c>0/1명</c> 이라는 숫자뿐이었다. 거절 갈래가 전부
    /// <c>continue</c> 였기 때문에 <b>집계에서 빠질 뿐 사유가 없었고</b>, 고치려면 매번
    /// 추측해야 했다. 실제로 2026-09-22 QA 에서 <c>move_tractor 0/1명</c> 하나를 두고
    /// 아무 것도 알 수 없었다.</para>
    ///
    /// <para><b>같은 사유는 묶는다.</b> 동료 셋이 같은 이유로 거절되면 세 번 적히는 대신
    /// <c>기절해 있음 ×3</c> 이 된다 — 줄이 길어지면 사람이 안 읽는다.</para>
    ///
    /// <para>이 요약은 <c>Message</c> 를 타고 <see cref="AiStatusLog"/> 로 흘러가므로
    /// 화면과 <c>Player.log</c> 가 <b>같은 글자</b>를 본다.</para>
    /// </summary>
    public sealed class AiRefusalTally
    {
        readonly List<string> reasons = new List<string>();

        public void Add(string reason)
            => reasons.Add(string.IsNullOrEmpty(reason) ? "알 수 없음" : reason);

        /// <summary>값으로 확인할 수 있게 떼어 둔다 — 씬 없이 시험한다.</summary>
        public static string Summarize(IReadOnlyList<string> reasons)
        {
            if (reasons == null || reasons.Count == 0) return "";
            var text = new System.Text.StringBuilder(" · 거절 ").Append(reasons.Count).Append(": ");
            var seen = new List<string>();
            var counts = new List<int>();
            foreach (string reason in reasons)
            {
                int at = seen.IndexOf(reason);
                if (at < 0) { seen.Add(reason); counts.Add(1); } else counts[at]++;
            }
            for (int i = 0; i < seen.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(seen[i]);
                if (counts[i] > 1) text.Append(" ×").Append(counts[i]);
            }
            return text.ToString();
        }

        public string Summary() => Summarize(reasons);

        /// <summary>
        /// 첫 사유. <b>말한 사람에게 돌려줄 한 마디</b>다 (S15P21B202-730).
        ///
        /// <para>전체 요약은 방장 화면과 로그의 것이라 기술적이다. 명령을 말한 사람에게는
        /// <c>지금 수행할 수 있는 AI 동료가 없어요</c> 대신 <b>왜 안 됐는지</b>를 준다 —
        /// 쓰러진 채 말했을 때 아무 말도 없던 것이 이 이슈의 시작이다.</para>
        /// </summary>
        public string First => reasons.Count > 0 ? reasons[0] : null;
    }

    /// <summary>Snapshot recipients before async classification; never substitute a newly joined actor.</summary>
    public sealed class AiGroupOrders
    {
        readonly List<NetPlayer> actors = new List<NetPlayer>(3);
        readonly List<uint> revisions = new List<uint>(3);
        readonly bool automatic;
        public int Count => actors.Count;
        public NetPlayer Nearest => actors.Count > 0 ? actors[0] : null;
        public string Names { get; private set; }
        public int Requested { get; private set; }
        public int LastAccepted { get; private set; }

        /// <summary>받은 동료의 자리 비트(1 &lt;&lt; teamSlot) — 결과 배너가 누가 받았는지 아이콘으로 그린다 (S15P21B202-746).</summary>
        public int LastAcceptedSlots { get; private set; }

        /// <summary>아무도 안 받았을 때 <b>왜</b> (S15P21B202-730). 받았으면 <c>null</c>.</summary>
        public string LastRefusal { get; private set; }
        AiGroupOrders(bool auto) { automatic = auto; }

        public AiGroupOrders Select(string recipients, AiRecipients fallback, NetPlayer selected)
        {
            int slot = AiCommandPlan.SlotOf(recipients);
            var mode = recipients == "default" ? fallback : recipients == "all" ? AiRecipients.All
                : recipients == "one" ? AiRecipients.OneIdle : recipients == "two" ? AiRecipients.TwoIdle : AiRecipients.Selected;
            var result = new AiGroupOrders(mode == AiRecipients.OneIdle || mode == AiRecipients.TwoIdle);
            result.Requested = mode == AiRecipients.All ? Count : mode == AiRecipients.TwoIdle ? 2 : 1;
            for (int i=0; i<actors.Count && result.Count<result.Requested; i++)
            {
                var ai=actors[i];
                if(ai==null || ai.AiOrderRevision!=revisions[i] || (result.automatic && ai.AiBusy)) continue;
                if(mode==AiRecipients.Selected && (slot>=0 ? ai.teamSlot!=slot : recipients!="default" || ai!=selected)) continue;
                result.actors.Add(ai); result.revisions.Add(revisions[i]);
                result.Names += (result.Names==null ? "" : ", ") + ai.nickname;
            }
            return result;
        }

        public static AiGroupOrders Capture(AiRecipients mode, NetPlayer selected, PlayerRig speaker, float range = float.PositiveInfinity)
        {
            var order = new AiGroupOrders(mode == AiRecipients.OneIdle || mode == AiRecipients.TwoIdle);
            order.Requested = mode == AiRecipients.TwoIdle ? 2 : mode == AiRecipients.All ? 3 : 1;
            if (!NetworkServer.active || speaker == null) return order;
            foreach (var ai in NetPlayer.AllServerPlayers)
            {
                if (ai == null || !ai.IsAi || !ai.HasBody || ai.VisualRig.Downed) continue;
                if (!((ai.VisualRig.FootPosition - speaker.FootPosition).sqrMagnitude <= range * range)) continue;
                if (mode == AiRecipients.Selected && ai != selected) continue;
                if (order.automatic && ai.AiBusy) continue;
                order.actors.Add(ai);
            }
            order.actors.Sort((a,b) => {
                int d = (a.VisualRig.FootPosition-speaker.FootPosition).sqrMagnitude.CompareTo((b.VisualRig.FootPosition-speaker.FootPosition).sqrMagnitude);
                return d != 0 ? d : a.teamSlot.CompareTo(b.teamSlot);
            });
            if (order.actors.Count > order.Requested) order.actors.RemoveRange(order.Requested, order.actors.Count-order.Requested);
            if (mode == AiRecipients.All) order.Requested = order.Count;
            foreach (var ai in order.actors) { order.revisions.Add(ai.AiOrderRevision); order.Names += (order.Names == null ? "" : ", ") + ai.nickname; }
            return order;
        }

        /// <summary>
        /// <b>버튼으로 고른 자리의 동료 한 명</b>을 붙잡는다 (S15P21B202-738).
        ///
        /// <para><see cref="Capture"/> 와 달리 <b>거리를 보지 않는다.</b> 멀리 있거나 벽 뒤에
        /// 있는 동료에게 시킬 수 있다는 것이 버튼으로 고르는 이유 중 하나다 — 겨냥으로
        /// 고르는 안(B)이 못 하던 것이다.</para>
        ///
        /// <para>못 붙잡으면 <b>왜인지</b>를 <paramref name="refusal"/> 로 준다. 빈 목록만
        /// 돌려주면 부르는 쪽은 "주변에 명령을 들을 AI가 없어요" 밖에 할 말이 없는데,
        /// 버튼을 누른 사람에게 그것은 틀린 말이다 — 주변을 고른 것이 아니다.</para>
        /// </summary>
        public static AiGroupOrders CaptureSlot(int slot, PlayerRig speaker, out string refusal)
        {
            var order = new AiGroupOrders(false) { Requested = 1 };
            refusal = null;
            string color = AiCommandPlan.RecipientOf(slot) != null ? TeamSlots.Name(slot) : null;
            if (color == null) { refusal = "없는 자리예요"; return order; }
            if (!NetworkServer.active || speaker == null) { refusal = "지금은 AI 명령을 사용할 수 없어요"; return order; }
            foreach (var ai in NetPlayer.AllServerPlayers)
            {
                if (ai == null || ai.teamSlot != slot) continue;
                if (!ai.IsAi) { refusal = color + "은 AI 동료가 아니에요"; return order; }
                if (!ai.HasBody) { refusal = color + " 동료가 아직 준비되지 않았어요"; return order; }
                if (ai.VisualRig.Downed) { refusal = color + " 동료가 기절해 있어요"; return order; }
                order.actors.Add(ai); order.revisions.Add(ai.AiOrderRevision); order.Names = ai.nickname;
                return order;
            }
            refusal = color + " 자리에 AI 동료가 없어요";
            return order;
        }

        public string Execute(string action, PlayerRig speaker, bool approach = false)
        {
            if (!NetworkServer.active || speaker == null) return "방장만 지시할 수 있습니다";
            int applied = 0; var used = new List<Vector3>(3);
            var refusals = new AiRefusalTally();
            for (int i = 0; i < actors.Count; i++)
            {
                var ai = actors[i];
                if (ai == null || !ai.IsAi || !ai.HasBody) { refusals.Add("동료가 없거나 몸이 없음"); continue; }
                if (ai.AiOrderRevision != revisions[i]) { refusals.Add("그 사이 다른 명령을 받음"); continue; }
                if (automatic && ai.AiBusy) { refusals.Add("이미 작업 중"); continue; }
                Vector3 offset = default;
                bool place = action == "gather" || (action == "follow" && Count > 1) || (approach && AiCompanionController.IsSocial(action));
                if (place && !FindPlace(ai, speaker, used, out offset)) { refusals.Add("설 자리를 못 찾음"); continue; }
                if (!ai.TryApplyAiOrder(action, speaker, out string refusal, approach)) { refusals.Add(refusal); continue; }
                if (place) { ai.SetAiFormation(offset); used.Add(speaker.FootPosition + offset); }
                applied++;
            }
            return action + " → " + applied + "/" + Requested + "명 (" + (Names ?? "대상 없음") + ")"
                + refusals.Summary();
        }

        public string ExecutePlan(AiCommandAssignment[] assignments, AiRecipients fallback, NetPlayer selected, PlayerRig speaker)
        {
            LastAccepted = 0; LastRefusal = null; LastAcceptedSlots = 0;
            if (!AiCommandPlan.Valid(assignments) || !NetworkServer.active || speaker == null) return "잘못된 계획 · 기존 행동 유지";
            var usedActors = new HashSet<NetPlayer>();
            var reservedPlaces = new List<Vector3>();
            var report = new System.Text.StringBuilder();
            foreach (var assignment in assignments)
            {
                var available = new AiGroupOrders(false);
                for (int i = 0; i < actors.Count; i++)
                    if (!usedActors.Contains(actors[i])) { available.actors.Add(actors[i]); available.revisions.Add(revisions[i]); }
                var group = available.Select(assignment.recipients == "remaining" ? "all" : assignment.recipients, fallback, selected);
                int applied = 0;
                var refusals = new AiRefusalTally();
                foreach (var ai in group.actors)
                {
                    if (ai == null) continue;
                    var first = assignment.steps[0];
                    // **먼 곳으로 모이는 것은 말한 사람 옆자리를 잡지 않는다**
                    // (S15P21B202-729). 그러지 않으면 조립대로 보내면서 말한 사람 주변에
                    // 설 자리를 찾고, 그 자리가 없으면 **가 보지도 못하고 거절**된다.
                    bool place = (first.action == "gather" && !AiCommandPlan.GathersAwayFromSpeaker(first.action, first.destination))
                                 || (first.action == "follow" && group.Count > 1) || first.approach;
                    Vector3 offset = default;
                    if (place && !FindPlace(ai, speaker, reservedPlaces, out offset)) { refusals.Add("설 자리를 못 찾음"); continue; }
                    if (!ai.TryApplyAiPlan(assignment.steps, speaker, out string refusal)) { refusals.Add(refusal); continue; }
                    if (place) { ai.SetAiFormation(offset); reservedPlaces.Add(speaker.FootPosition + offset); }
                    usedActors.Add(ai); applied++; LastAccepted++; LastAcceptedSlots |= 1 << ai.teamSlot;
                }
                if (report.Length > 0) report.Append(" / ");
                report.Append(assignment.recipients).Append(": ").Append(assignment.steps[0].action)
                    .Append(" ").Append(applied).Append('/').Append(group.Requested).Append("명")
                    .Append(assignment.steps.Length > 1 ? " · 순차 작업 예약" : "")
                    .Append(refusals.Summary());
                if (LastRefusal == null) LastRefusal = refusals.First;
            }
            return report.ToString();
        }

        static bool FindPlace(NetPlayer ai, PlayerRig speaker, List<Vector3> used, out Vector3 offset)
        {
            offset = default;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) return false;
            var path = new NavMeshPath();
            for (int i = 0; i < 12; i++)
            {
                var candidate = speaker.FootPosition + Quaternion.Euler(0, ai.teamSlot * 120 + i * 30, 0) * Vector3.forward * 2.8f;
                if (!NavMesh.SamplePosition(candidate, out var hit, .6f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y-candidate.y) > .5f) continue;
                bool occupied = false;
                foreach (var point in used) if (Vector3.Distance(point, hit.position) < 1.6f) occupied = true;
                if (occupied || !world.CalculatePath(ai.VisualRig.FootPosition, hit.position, path)) continue;
                offset = hit.position - speaker.FootPosition; return true;
            }
            return false;
        }
    }
}
