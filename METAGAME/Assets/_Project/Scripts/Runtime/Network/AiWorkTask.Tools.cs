using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Network
{
    public sealed partial class AiWorkTask
    {
        ToolBase tool;
        Grabbable toolGrab;
        Collider toolCollider;
        Rigidbody practice;
        uint swingBefore;
        float nextThreat, lastWeldProgress;
        Vector3 weldPoint;
        Transform weldSurface;
        int beadsBefore;
        SpaceshipPart weldPartner;
        float pairStrengthBefore;
        bool ToolMatches(ToolBase candidate) => candidate != null &&
            ((action == "pickup_tool" || action == "bring_tool") ||
             (action == "pickup_welder" || action == "bring_welder" || action == "weld" || action == "cut_weld"
                ? candidate is PlasmaWeldingTool : candidate is MeteorBat));
        static bool ToolFree(Grabbable item)
        {
            if (item == null || !item.CanGrab || !item.isActiveAndEnabled || !item.IsTool || item.HandCount != 0) return false;
            var value = item.GetComponent<ToolBase>() ?? item.GetComponentInChildren<ToolBase>();
            return value != null && !value.IsEquipped && !value.IsHeldForView;
        }
        bool BeginTool()
        {
            PhaseAfterDelivery = false;
            if (action == "drop_tool") { Phase(1); return true; }
            practice = null; weldSurface = null; weldPartner = null;
            if (action == "hit_target")
            {
                float distance = 30f * 30f;
                foreach (var target in Object.FindObjectsByType<PracticeMeteorTarget>(FindObjectsSortMode.None))
                {
                    float d = (target.transform.position - speaker.FootPosition).sqrMagnitude;
                    if (target.gameObject.scene == self.gameObject.scene && d < distance)
                    { practice = target.GetComponent<Rigidbody>(); distance = d; }
                }
                if (practice == null) { Finish(false, "타격 가능한 연습 대상 없음"); return false; }
            }
            if (action == "weld" || action == "cut_weld")
            {
                part = NearestPart();
                if (part == null || !FindWeldPoint()) { Finish(false, "접근 가능한 접합 표면 없음"); return false; }
                if (!Claim(part)) { Finish(false, "다른 AI가 이 파츠 용접 중"); return false; }
                if (action == "weld" && !Claim(weldPartner)) { Finish(false, "다른 AI가 상대 파츠 작업 중"); return false; }
                beadsBefore = part.GetComponent<WeldBead>()?.BlobCount ?? 0;
                pairStrengthBefore = part.GetComponent<WeldBead>()?.StrengthTo(weldPartner) ?? 0;
            }
            tool = ToolMatches(self.ActiveTool) ? self.ActiveTool : null;
            toolGrab = tool != null ? self.HeldTool : null;
            float nearest = 40f * 40f;
            if (tool == null)
            foreach (var item in Grabbable.All)
            {
                if (item == null || item.gameObject.scene != self.gameObject.scene || !ToolFree(item)) continue;
                var candidate = item.GetComponent<ToolBase>() ?? item.GetComponentInChildren<ToolBase>();
                float distance = (item.transform.position - speaker.FootPosition).sqrMagnitude;
                if (!ToolMatches(candidate) || distance >= nearest || (claims.TryGetValue(item, out var other) && other != null && other != self)) continue;
                if (!StandNear(item.transform.position, .6f, out var spot)) continue;
                tool = candidate; toolGrab = item; goal = spot; nearest = distance;
            }
            if (tool == null || !Claim(toolGrab)) { Finish(false, "사용 가능한 도구 없음"); return false; }
            toolCollider = null; float closest = float.PositiveInfinity;
            foreach (var collider in toolGrab.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                float d = (collider.ClosestPoint(self.Position) - self.Position).sqrMagnitude;
                if (d < closest) { closest = d; toolCollider = collider; }
            }
            Phase(self.HeldTool == toolGrab ? 4 : self.HeldTool != null ? 1 : 2);
            return true;
        }

        InputFrame TickTool()
        {
            var input = new InputFrame();
            if (PhaseAfterDelivery)
            {
                if (self.HeldTool == null) Finish(true, "도구 운반·내려놓기 완료");
                else if (Time.time - phaseAt > 2f) Finish(false, "운반 후 내려놓기 실패");
                return input;
            }
            if (phase == 1)
            {
                if (self.HeldTool == null)
                { if (action == "drop_tool") Finish(true, "도구 내려놓기 완료"); else Phase(2); return input; }
                if (!pulse) { input.ToolUtility = true; pulse = true; }
                if (Time.time - phaseAt > 2f) Finish(false, "도구 내려놓기 실패");
                return input;
            }
            if (tool == null || toolGrab == null) { Finish(false, "대상 도구 사라짐"); return input; }
            bool held = self.HeldTool == toolGrab;
            if (!held && !ToolFree(toolGrab)) { Finish(false, "다른 플레이어가 도구 사용 중"); return input; }
            if (phase == 2 || phase == 3)
            {
                if (held) { Phase(4); return input; }
                if (phase == 2)
                {
                    Status = "도구로 이동 중";
                    if (!Navigate(goal, .15f, ref input)) return input;
                    Phase(3);
                }
                input = Aim(toolCollider != null ? toolCollider.bounds.center : toolGrab.transform.position);
                if (self.CurrentInteractionTarget.Grabbable == toolGrab && self.CurrentInteractionTarget.CanInteract) input.GrabLeft = true;
                Status = "도구 잡는 중";
                if (Time.time - phaseAt > 5f) Finish(false, "도구에 손이 닿지 않음");
                return input;
            }
            if (!held) { Finish(false, "도구를 놓쳐 작업 중단"); return input; }
            if (phase == 4)
            {
                // The pickup click must be released before the ordinary tool action can fire.
                if (action.StartsWith("pickup_")) { Finish(true, "도구 집기 완료"); return input; }
                if (action.StartsWith("bring_")) goal = destinationPoint;
                else if (action == "hit_target")
                {
                    if (practice == null || !StandNear(practice.position, 1.2f, out goal))
                    { Finish(false, "타격 대상 접근 불가"); return input; }
                }
                else if (action == "weld" || action == "cut_weld")
                {
                    if (weldSurface == null || !StandNear(weldSurface.TransformPoint(weldPoint), .65f, out goal))
                    { Finish(false, "용접 지점 접근 불가"); return input; }
                }
                else goal = destinationPoint;
                Phase(5); return input;
            }
            if (phase == 5)
            {
                Status = "작업 위치로 이동 중";
                if (!Navigate(goal, .2f, ref input)) return input;
                Phase(6); lastWeldProgress = Time.time;
                swingBefore = tool is MeteorBat readyBat ? readyBat.RhythmSequence : 0;
                return input;
            }
            if (action.StartsWith("bring_"))
            {
                if (!pulse) { input.ToolUtility = true; pulse = true; }
                // Check success before the generic 'lost tool' check on the following tick.
                PhaseAfterDelivery = true;
                return input;
            }
            if (action == "weld" || action == "cut_weld") return TickWelding();
            var bat = tool as MeteorBat;
            if (bat == null) { Finish(false, "방망이 필요"); return input; }
            if (action == "hit_target")
            {
                if (practice == null) { Finish(false, "타격 대상 사라짐"); return input; }
                input = Aim(practice.worldCenterOfMass);
                if (bat.RhythmSequence != swingBefore)
                { if (!bat.IsSwinging) Finish(bat.LastPushed == practice, bat.LastPushed == practice ? "타격 완료" : "타격 빗나감"); return input; }
                if (bat.CanSwing && !FriendlyInFront(practice.position - self.Position)) input.ToolPrimary = true;
                Status = "연습 대상 타격"; return input;
            }
            Status = "운석 방어 중";
            if (Time.time < nextThreat || !bat.CanSwing || bat.IsSwinging) return input;
            nextThreat = Time.time + .1f;
            var meteor = MeteorBat.FindTarget(self.Position);
            if (meteor == null || meteor.WasDeflected || !meteor.TryPredictImpact(out var seconds, out var impact)
                || seconds > meteor.BatSwingWindowSeconds * .65f) return input;
            var direction = impact - self.Position; direction.y = 0;
            if (direction.sqrMagnitude < .01f) direction = self.FacingDirection;
            input = Aim(self.AimOrigin + direction.normalized * 5);
            if (!FriendlyInFront(direction)) input.ToolPrimary = true;
            return input;
        }
        bool PhaseAfterDelivery;
        bool FriendlyInFront(Vector3 direction)
        {
            direction.y = 0;
            foreach (var other in PlayerRig.All)
            {
                if (other == null || other == self) continue;
                var delta = other.Position - self.Position; delta.y = 0;
                if (delta.magnitude < PhysicsScale.BatStrikeReach && Vector3.Dot(delta, direction) > 0) return true;
            }
            return false;
        }

        bool FindWeldPoint()
        {
            bool cut = action == "cut_weld";
            // Inspect real collider surfaces; arbitrary nearby points are not valid weld contacts.
            foreach (var surface in part.GetComponentsInChildren<Collider>())
            {
                if (!surface.enabled || surface.isTrigger || surface.GetComponentInParent<PartClamp>() != null) continue;
                var bounds = surface.bounds;
                for (int side = 0; side < 8; side++)
                for (int height = 0; height < 9; height++)
                {
                    var outward = Quaternion.Euler(0, side * 45f, 0) * Vector3.forward;
                    var center = bounds.center; center.y = Mathf.Lerp(bounds.min.y, bounds.max.y, height / 8f);
                    var origin = center + outward * (bounds.extents.magnitude + .3f);
                    if (!surface.Raycast(new Ray(origin, -outward), out var hit, bounds.size.magnitude + 1f)) continue;
                    if (!WeldBead.CanApplyAt(hit, cut) || (!cut && !WeldBead.CanStartJoiningAt(hit))) continue;
                    var partner = cut ? null : WeldBead.JoiningTargetAt(hit);
                    if (!cut && (partner == null || (weldPartner != null && partner != weldPartner))) continue;
                    if (!StandNear(hit.point, .65f, out var stand) || Mathf.Abs(hit.point.y - (stand.y + 1f)) > 1.1f) continue;
                    if (!cut) weldPartner = partner;
                    weldSurface = surface.transform; weldPoint = weldSurface.InverseTransformPoint(hit.point); return true;
                }
            }
            return false;
        }
        InputFrame TickWelding()
        {
            if (part == null || weldSurface == null || !(tool is PlasmaWeldingTool welder))
            { Finish(false, "용접 대상 사라짐"); return default; }
            bool cut = action == "cut_weld";
            if (!cut && weldPartner == null) { Finish(false, "연결할 상대 파츠 사라짐"); return default; }
            var bead = part.GetComponent<WeldBead>();
            if (cut ? (bead == null || bead.BlobCount == 0) : (bead != null && bead.HasLiveBridge
                && bead.StrengthTo(weldPartner) >= Mathf.Min(1f, Mathf.Max(.25f, pairStrengthBefore + .15f))))
            { Finish(true, cut ? "용접 비드 제거 완료" : "용접 연결 확인"); return default; }
            var frame = Aim(weldSurface.TransformPoint(weldPoint));
            if (welder.TryGetBeamHit(self.AimForward, out var hit) && hit.collider.GetComponentInParent<SpaceshipPart>() == part
                && WeldBead.CanApplyAt(hit, cut)
                && (cut || (Vector3.Distance(hit.point, weldSurface.TransformPoint(weldPoint)) < .25f
                    && WeldBead.CanJoinTargetAt(hit, weldPartner))))
            { frame.ToolPrimary = !cut; frame.ToolSecondary = cut; }
            if (cut ? welder.CuttingProgressed : welder.WeldingProgressed) lastWeldProgress = Time.time;
            if (Time.time - lastWeldProgress > 4f)
            {
                if (FindWeldPoint()) { Phase(4); lastWeldProgress = Time.time; }
                else Finish(false, "접합 가능한 지점 없음 · 부분 작업 종료");
            }
            Status = cut ? "기존 용접건으로 절단 중" : "기존 용접건으로 용접 중";
            return frame;
        }
    }
}
