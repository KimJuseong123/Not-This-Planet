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
        Vector3 carryFrom;
        float carryReplanAt, settleSince;
        int carryRetries, pushTries;

        /// <summary>
        /// 잡기 · 운반을 처음(접근)부터 다시 해 보는 횟수 (S15P21B202-712). 전에는 한 번 놓치거나
        /// 5초 안에 못 잡으면 명령이 그대로 끝났다.
        /// </summary>
        public const int CarryRetries = 2;

        /// <summary>파츠가 이만큼(m) 움직이면 서는 자리를 다시 고른다 — 밀려난 파츠 옆 빈자리로 걸어가지 않게.</summary>
        public const float CarryReplanDistance = .5f;

        /// <summary>
        /// 서는 자리까지 이만큼(m) 붙으면 도착. 서는 자리는 파츠 표면에서 0.4m 라, 전의 0.12m 는
        /// 몸통이 못 닿아 8초 뒤 "이동 막힘" 이 났다.
        /// </summary>
        public const float CarryStandReach = .3f;

        /// <summary>운반이 끝났다고 보려면 손을 놓은 파츠가 이 속도(m/s) 아래로 이만큼(초) 머물러야 한다.</summary>
        public const float CarrySettleSpeed = .4f, CarrySettleSeconds = .8f;

        /// <summary>몸이 먼저 목적지에 닿으면 파츠는 뒤에 끌려온다 — 이만큼(m) 너머까지 더 끈다.</summary>
        public const float CarryOvershoot = 1.2f;

        bool BeginCarry()
        {
            if (action == "release_part") return true;
            carryRetries = pushTries = 0;
            part = NearestPart(false, Carriable);
            if (part == null) { Finish(false, "고정되지 않은 파츠가 필요함"); return false; }
            carry = GrabbableOf(part);
            if (carry == null || self.HeldTool != null) { Finish(false, "빈손과 잡을 수 있는 파츠 필요"); return false; }
            // Multiple helpers may intentionally pull the same part. Do not reserve it exclusively.
            if (!PickCarrySurface()) { Finish(false, "파츠 접근 불가"); return false; }
            return true;
        }

        static Grabbable GrabbableOf(SpaceshipPart item)
            => item.GetComponent<Grabbable>() ?? item.GetComponentInChildren<Grabbable>();

        /// <summary>
        /// 걸어서 잡을 수 있는 파츠인가 (S15P21B202-712). 전에는 가장 가까운 것 하나만 보고, 그것이
        /// 용접·고정·다른 AI 의 견인 중이면 가까이에 쓸 수 있는 파츠가 있어도 명령 전체가 실패했다.
        /// </summary>
        bool Carriable(SpaceshipPart item)
            => !item.IsDockedToPad && item.RemainingRequiredClamps == 0 && item.State != PartState.Welded
               && GrabbableOf(item) != null && !ClaimedByOther(item);

        /// <summary>파츠에서 지금 가장 가까운 잡을 면과 그 앞의 서는 자리. 파츠가 움직이면 다시 부른다.</summary>
        bool PickCarrySurface()
        {
            carrySurface = null; float best = float.PositiveInfinity;
            foreach (var collider in part.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<PartClamp>() != null) continue;
                var point = collider.ClosestPoint(self.Position);
                float d = (point - self.Position).sqrMagnitude;
                if (d < best && StandNear(point, .4f, out var stand))
                { best = d; carrySurface = collider; goal = stand; }
            }
            carryFrom = part.transform.position; carryReplanAt = Time.time + 1f;
            return carrySurface != null;
        }

        /// <summary>
        /// 한 번 더 해 볼 수 있으면 접근부터 다시 한다. 아니면 그 이유로 실패한다 (S15P21B202-712).
        /// </summary>
        void RetryCarry(string why)
        {
            if (carryRetries >= CarryRetries || !PickCarrySurface()) { Finish(false, why); return; }
            carryRetries++; Phase(0);
            Status = why + " · 다시 시도 " + carryRetries + "/" + CarryRetries;
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
            if (part == null || carry == null || carrySurface == null)
            { Finish(false, "파츠 상태 변경"); return default; }
            // 조립대로 옮기다 붙었으면 그것이 목적이다 — 전에는 "파츠 상태 변경" 실패였다.
            if (part.IsDockedToPad && action == "carry_part" && destination == "assembly")
            { Finish(true, "파츠가 조립대에 붙음"); return default; }
            if (part.IsDockedToPad || part.RemainingRequiredClamps != 0 || part.State == PartState.Welded)
            { Finish(false, "파츠 상태 변경"); return default; }
            if (phase == 3) return TickCarrySettle();
            var input = new InputFrame();
            if (phase == 0)
            {
                if (carryRetries == 0) Status = "파츠로 이동 중";
                if (Time.time >= carryReplanAt
                    && (part.transform.position - carryFrom).sqrMagnitude > CarryReplanDistance * CarryReplanDistance)
                {
                    if (!PickCarrySurface()) { Finish(false, "파츠 접근 불가"); return default; }
                    route.Reset();
                }
                // 이미 손이 닿으면 서는 자리까지 가지 않고 바로 잡는다.
                bool reachable = self.CurrentInteractionTarget.Grabbable == carry && self.CurrentInteractionTarget.CanInteract;
                if (!reachable && !Navigate(goal, CarryStandReach, ref input)) return input;
                if (!Active) return default;
                Phase(1);
            }
            var gripPoint = carrySurface.ClosestPoint(self.Position);
            input = Aim(gripPoint);
            bool held = self.LeftHand.Held == carry || self.RightHand.Held == carry;
            if (!held)
            {
                if (phase > 1) { RetryCarry("파츠를 놓침"); return default; }
                if (self.CurrentInteractionTarget.Grabbable == carry && self.CurrentInteractionTarget.CanInteract)
                    input.GrabLeft = input.GrabRight = true;
                if (Time.time - phaseAt > 5f) { RetryCarry("파츠 잡기 실패"); return default; }
                return input;
            }
            if (phase == 1) Phase(2);
            input.GrabLeft = input.GrabRight = true;
            Status = action == "grab_part" ? "파츠 잡고 유지 중" : "파츠 운반 보조 중";
            if (action == "grab_part") return input;
            var offset = destinationPoint - part.transform.position; offset.y = 0;
            if (offset.magnitude < 1.5f)
            {
                // 손을 놓고 멈추는지 본다 — 미끄러지는 중에 "완료" 를 내지 않는다.
                Phase(3); settleSince = -1; Status = "파츠 내려놓고 멈추기 확인 중";
                return default;
            }
            // Follow the same grounded route as a player; never drag through geometry by setting a transform.
            // 몸이 먼저 닿으면 파츠는 뒤에 끌려온다 — 목적지 너머까지 끈다(한 번 닿을 때마다 조금 더).
            var push = destinationPoint + offset.normalized * (CarryOvershoot * (1 + pushTries));
            var movement = new InputFrame();
            if (Navigate(push, .7f, ref movement))
            {
                if (++pushTries > 2) { Finish(false, "몸은 도착했지만 파츠 이동 미완료"); return default; }
                route.Reset(); movedAt = Time.time; bestDistance = float.PositiveInfinity;
                return input;
            }
            if (!Active) return default;
            var worldMove = Quaternion.Euler(0, movement.AimYaw, 0) * new Vector3(movement.Move.x, 0, movement.Move.y);
            var local = Quaternion.Euler(0, -(self.Hauling ? self.HaulBasisYaw : input.AimYaw), 0) * worldMove;
            input.Move = new Vector2(local.x, local.z) * .5f;
            return input;
        }

        /// <summary>
        /// 손을 놓은 파츠가 멈추기를 기다린다 (S15P21B202-712). 멈추면 완료, 4초가 지나도 미끄러지면
        /// 그 사실을 적고 완료로 둔다 — 목적지 안에는 들어왔다.
        /// </summary>
        InputFrame TickCarrySettle()
        {
            var body = part.GetComponent<Rigidbody>();
            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            if (speed > CarrySettleSpeed) settleSince = -1;
            else if (settleSince < 0) settleSince = Time.time;
            if (settleSince >= 0 && Time.time - settleSince >= CarrySettleSeconds)
            { Finish(true, "파츠 운반 완료"); return default; }
            if (Time.time - phaseAt > 4f) Finish(true, "파츠 운반 완료 · 아직 미끄러지는 중");
            return default;
        }
    }
}
