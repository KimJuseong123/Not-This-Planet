using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>Server-owned jobs. Only generates normal player input; never completes physics tasks by fiat.</summary>
    public sealed partial class AiWorkTask
    {
        readonly AiNavigationRoute route = new AiNavigationRoute();
        readonly AiPinTask pin = new AiPinTask();
        readonly HashSet<PartClamp> attemptedPins = new HashSet<PartClamp>();
        readonly RaycastHit[] stepHits = new RaycastHit[24];
        static readonly Dictionary<Object, PlayerRig> claims = new Dictionary<Object, PlayerRig>();
        readonly List<Object> ownedClaims = new List<Object>();
        PlayerRig self, speaker;
        SpaceshipPart part;
        string action, destination;
        Vector3 goal, destinationPoint;
        float started, phaseAt, movedAt, bestDistance;
        int phase, pinCount;
        bool pulse;
        public bool Active { get; private set; }
        public bool Succeeded { get; private set; }
        public string Status { get; private set; } = "대기";
        public bool WantsMovement { get; private set; }
        public Vector3? MovementGoal => WantsMovement ? goal : (Vector3?)null;
        public static readonly string[] Actions = {
            "pull_all_pins", "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "bring_tool",
            "bring_bat", "bring_welder", "hit_target", "defend_meteors", "grab_part", "release_part",
            "carry_part", "weld", "cut_weld", "board_tractor", "leave_tractor", "move_tractor",
            "lift_player", "lift_part", "transport_part", "hold_tractor", "stack_part"
        };
        public static bool Supports(string value) => System.Array.IndexOf(Actions, value) >= 0;
        public static bool VehicleOrder(string value) => value == "board_tractor" || value == "leave_tractor"
            || value == "move_tractor" || value == "lift_player" || value == "lift_part"
            || value == "transport_part" || value == "hold_tractor" || value == "stack_part";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetClaims() => claims.Clear();

        bool Claim(Object target)
        {
            if (target == null) return false;
            if (claims.TryGetValue(target, out var owner) && owner != null && owner != self && owner.isActiveAndEnabled) return false;
            claims[target] = self; ownedClaims.Add(target); return true;
        }

        public bool Begin(string command, PlayerRig actor, PlayerRig requester, string to = "default")
        {
            if (!Supports(command) || actor == null || requester == null) return false;
            Cancel(); self = actor; speaker = requester; action = command; destination = to;
            Active = true; Succeeded = false; started = Time.time; Phase(0);
            Status = command + " · 준비";
            // Snapshot destinations, not a moving camera. Actors and objects remain live references.
            if (!ResolveDestination(to, out destinationPoint)) { Finish(false, "목적지 표면/조립대를 찾지 못함"); return false; }
            if (command == "pull_all_pins")
            {
                part = NearestPart(true); attemptedPins.Clear(); pinCount = 0;
                if (part == null) { Finish(false, "못이 남은 파츠 없음"); return false; }
                pin.Begin(self, speaker, part, attemptedPins); return true;
            }
            if (VehicleOrder(command)) return BeginVehicle();
            if (command == "grab_part" || command == "carry_part" || command == "release_part") return BeginCarry();
            return BeginTool();
        }

        public InputFrame Tick()
        {
            WantsMovement = false;
            if (!Active) return default;
            if (self == null || speaker == null || self.Downed || speaker.Downed)
            { Finish(false, "플레이어 상태 변경으로 중단"); return default; }
            if (Time.time - started > 180f && action != "defend_meteors" && action != "grab_part" && action != "hold_tractor")
            { Finish(false, "작업 시간 초과"); return default; }
            if (action == "pull_all_pins")
            {
                var previous = pin.Target;
                var input = pin.Tick(true);
                if (previous != null) attemptedPins.Add(previous);
                Status = $"파츠 못 해제 {pinCount}개 · {pin.Status}";
                WantsMovement = pin.WantsMovement;
                if (pin.MovementGoal.HasValue) goal = pin.MovementGoal.Value;
                if (!pin.Active)
                {
                    if (previous != null && previous.IsReleased) pinCount++;
                    if (part == null) Finish(false, "대상 파츠 사라짐");
                    else if (part.RemainingRequiredClamps == 0) Finish(true, "파츠의 모든 못 해제 완료");
                    else if (previous == null) Finish(false, "접근 가능하거나 비어 있는 못 없음 · 일부 미완료");
                    else pin.Begin(self, speaker, part, attemptedPins);
                }
                return input;
            }
            if (VehicleOrder(action)) return TickVehicle();
            if (action == "grab_part" || action == "carry_part" || action == "release_part") return TickCarry();
            return TickTool();
        }

        public void SuspendMovement()
        {
            pin.SuspendMovement(); route.Reset(); movedAt = phaseAt = Time.time;
            started += Time.fixedDeltaTime;
        }
        public void Cancel()
        {
            pin.Cancel(); ReleaseClaims(); route.Reset(); StopVehicle();
            Active = false; WantsMovement = false; Succeeded = false;
            self = speaker = null; part = null; tool = null; toolGrab = null; carry = null; vehicle = null;
        }
        public void DetachDriver()
        {
            pin.DetachDriver();
            ReleaseClaims();
            // No vehicle or grab changes: a human now owns this same body.
            self = speaker = null;
            vehicle = null;
            Active = false;
            WantsMovement = false;
            route.Reset();
        }
        void Finish(bool success, string message)
        {
            Cancel(); Succeeded = success; Status = message;
        }
        void ReleaseClaims()
        {
            foreach (var item in ownedClaims)
                if (!ReferenceEquals(item, null) && claims.TryGetValue(item, out var owner) && owner == self) claims.Remove(item);
            ownedClaims.Clear();
        }
        void Phase(int value)
        { phase = value; phaseAt = movedAt = Time.time; bestDistance = float.PositiveInfinity; pulse = false; route.Reset(); }
        InputFrame Aim(Vector3 point)
        {
            var delta = point - self.AimOrigin;
            return new InputFrame { AimYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg,
                AimPitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg };
        }
        bool Navigate(Vector3 point, float stop, ref InputFrame input)
        {
            goal = point; WantsMovement = true;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (!route.Steer(world, self.FootPosition, point, stop, out var direction))
            { if (Time.time - movedAt > 8f) Finish(false, "목적지 경로 없음"); return false; }
            if (route.Arrived) { WantsMovement = false; return true; }
            if (route.Remaining < bestDistance - .1f) { bestDistance = route.Remaining; movedAt = Time.time; }
            if (Time.time - movedAt > 8f) { Finish(false, "이동 막힘"); return false; }
            if (!AiFollowController.SafeStep(self, self.FootPosition, direction, stepHits, .18f)) return false;
            input = Aim(self.AimOrigin + direction);
            input.Move = Vector2.up * (route.Remaining < 1.2f ? .4f : 1f); return false;
        }
        bool StandNear(Vector3 point, float radius, out Vector3 stand)
        {
            stand = default;
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) return false;
            var path = new NavMeshPath(); float best = float.PositiveInfinity;
            for (int i = 0; i < 16; i++)
            {
                var candidate = point + Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward * radius;
                if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas)
                    || !world.CalculatePath(self.FootPosition, hit.position, path)) continue;
                float d = Vector3.Distance(self.FootPosition, hit.position);
                if (d < best) { best = d; stand = hit.position; }
            }
            return !float.IsInfinity(best);
        }
        SpaceshipPart NearestPart(bool pins = false)
        {
            SpaceshipPart chosen = null; float best = 40f * 40f;
            // Selection happens once at acceptance, not on every physics tick.
            var focus = speaker.CurrentInteractionTarget.Grabbable;
            var focusedPart = focus != null ? focus.GetComponentInParent<SpaceshipPart>() : null;
            foreach (var item in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
            {
                if (!item.isActiveAndEnabled || item.gameObject.scene != self.gameObject.scene) continue;
                if (pins ? item.RemainingRequiredClamps == 0 : item.GetComponent<TransportVehicle>() != null) continue;
                float distance = (item.transform.position - speaker.FootPosition).sqrMagnitude;
                if (item == focusedPart) return item;
                if (distance < best) { chosen = item; best = distance; }
            }
            return chosen;
        }
        bool ResolveDestination(string to, out Vector3 result)
        {
            result = default;
            if (to == "assembly")
            {
                var pad = Object.FindFirstObjectByType<ShipAssembly>();
                if (pad != null) { result = pad.LaunchPad.position + Vector3.right * 5f; return true; }
                return false;
            }
            if (to == "aim" || to == "high")
            {
                if (Physics.Raycast(speaker.AimOrigin, speaker.AimForward, out var hit, 80f, GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > .7f) { result = hit.point; return true; }
                return false;
            }
            result = speaker.FootPosition + speaker.PlanarRight * 2f; return true;
        }
    }
}
