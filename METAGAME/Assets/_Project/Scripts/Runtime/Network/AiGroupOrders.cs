using System.Collections.Generic;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    public enum AiRecipients { Selected, OneIdle, TwoIdle, All }

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
        AiGroupOrders(bool auto) { automatic = auto; }

        public AiGroupOrders Select(string recipients, AiRecipients fallback, NetPlayer selected)
        {
            int slot = recipients == "red" ? 0 : recipients == "blue" ? 1 : recipients == "yellow" ? 2 : recipients == "green" ? 3 : -1;
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

        public string Execute(string action, PlayerRig speaker, bool approach = false)
        {
            if (!NetworkServer.active || speaker == null) return "방장만 지시할 수 있습니다";
            int applied = 0; var used = new List<Vector3>(3);
            for (int i = 0; i < actors.Count; i++)
            {
                var ai = actors[i];
                if (ai == null || !ai.IsAi || !ai.HasBody || ai.AiOrderRevision != revisions[i] || (automatic && ai.AiBusy)) continue;
                Vector3 offset = default;
                bool place = action == "gather" || (action == "follow" && Count > 1) || (approach && AiCompanionController.IsSocial(action));
                if (place && !FindPlace(ai, speaker, used, out offset)) continue;
                if (!ai.TryApplyAiOrder(action, speaker, approach)) continue;
                if (place) { ai.SetAiFormation(offset); used.Add(speaker.FootPosition + offset); }
                applied++;
            }
            return action + " → " + applied + "/" + Requested + "명 (" + (Names ?? "대상 없음") + ")"
                + (applied < Requested || Requested == 0 ? " · 인원 부족/작업 중/경로 없음/현재 지원하지 않는 명령" : "");
        }

        public string ExecutePlan(AiCommandAssignment[] assignments, AiRecipients fallback, NetPlayer selected, PlayerRig speaker)
        {
            LastAccepted = 0;
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
                foreach (var ai in group.actors)
                {
                    if (ai == null) continue;
                    var first = assignment.steps[0];
                    bool place = first.action == "gather" || (first.action == "follow" && group.Count > 1) || first.approach;
                    Vector3 offset = default;
                    if (place && !FindPlace(ai, speaker, reservedPlaces, out offset)) continue;
                    if (!ai.TryApplyAiPlan(assignment.steps, speaker)) continue;
                    if (place) { ai.SetAiFormation(offset); reservedPlaces.Add(speaker.FootPosition + offset); }
                    usedActors.Add(ai); applied++; LastAccepted++;
                }
                if (report.Length > 0) report.Append(" / ");
                report.Append(assignment.recipients).Append(": ").Append(assignment.steps[0].action)
                    .Append(" ").Append(applied).Append('/').Append(group.Requested).Append("명")
                    .Append(assignment.steps.Length > 1 ? " · 순차 작업 예약" : "");
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
