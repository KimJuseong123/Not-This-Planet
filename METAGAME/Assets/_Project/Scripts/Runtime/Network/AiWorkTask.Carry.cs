using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Network
{
    public sealed partial class AiWorkTask
    {
        Grabbable carry;
        Collider carrySurface;
        bool BeginCarry()
        {
            if (action == "release_part") return true;
            part = NearestPart();
            if (part == null || part.IsDockedToPad || part.RemainingRequiredClamps != 0 || part.State == PartState.Welded)
            { Finish(false, "고정되지 않은 파츠가 필요함"); return false; }
            carry = part.GetComponent<Grabbable>() ?? part.GetComponentInChildren<Grabbable>();
            if (carry == null || self.HeldTool != null) { Finish(false, "빈손과 잡을 수 있는 파츠 필요"); return false; }
            // Multiple helpers may intentionally pull the same part. Do not reserve it exclusively.
            carrySurface = null; float best = float.PositiveInfinity;
            foreach (var collider in part.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<PartClamp>() != null) continue;
                var point = collider.ClosestPoint(self.Position);
                float d = (point - self.Position).sqrMagnitude;
                if (d < best && StandNear(point, .4f, out var stand))
                { best = d; carrySurface = collider; goal = stand; }
            }
            if (carrySurface == null) { Finish(false, "파츠 접근 불가"); return false; }
            return true;
        }
        InputFrame TickCarry()
        {
            if (action == "release_part")
            {
                // Releasing both grab inputs uses the player's ordinary hand-release path.
                if (!self.LeftHand.Gripping && !self.RightHand.Gripping) Finish(true, "파츠 놓기 완료");
                else if (Time.time - started > 2f) Finish(false, "파츠 놓기 실패");
                return default;
            }
            if (part == null || carry == null || carrySurface == null || part.IsDockedToPad || part.RemainingRequiredClamps != 0)
            { Finish(false, "파츠 상태 변경"); return default; }
            var input = new InputFrame();
            if (phase == 0)
            {
                Status = "파츠로 이동 중";
                if (!Navigate(goal, .12f, ref input)) return input;
                Phase(1);
            }
            var gripPoint = carrySurface.ClosestPoint(self.Position);
            input = Aim(gripPoint);
            bool held = self.LeftHand.Held == carry || self.RightHand.Held == carry;
            if (!held)
            {
                if (phase > 1) { Finish(false, "파츠를 놓침"); return default; }
                if (self.CurrentInteractionTarget.Grabbable == carry && self.CurrentInteractionTarget.CanInteract)
                    input.GrabLeft = input.GrabRight = true;
                if (Time.time - phaseAt > 5f) Finish(false, "파츠 잡기 실패");
                return input;
            }
            if (phase == 1) Phase(2);
            input.GrabLeft = input.GrabRight = true;
            Status = action == "grab_part" ? "파츠 잡고 유지 중" : "파츠 운반 보조 중";
            if (action == "grab_part") return input;
            var offset = destinationPoint - part.transform.position; offset.y = 0;
            if (offset.magnitude < 1.5f) { Finish(true, "파츠 운반 완료"); return default; }
            // Follow the same grounded route as a player; never drag through geometry by setting a transform.
            var movement = new InputFrame();
            if (Navigate(destinationPoint, .7f, ref movement)) { Finish(false, "몸은 도착했지만 파츠 이동 미완료"); return default; }
            if (!Active) return default;
            var worldMove = Quaternion.Euler(0, movement.AimYaw, 0) * new Vector3(movement.Move.x, 0, movement.Move.y);
            var local = Quaternion.Euler(0, -(self.Hauling ? self.HaulBasisYaw : input.AimYaw), 0) * worldMove;
            input.Move = new Vector2(local.x, local.z) * .5f;
            return input;
        }
    }
}
