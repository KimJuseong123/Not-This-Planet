using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>Chooses an approach, then drives the same grab/backward inputs as a player.</summary>
    public sealed class AiPinTask
    {
        static readonly Dictionary<PartClamp, PlayerRig> reservations = new Dictionary<PartClamp, PlayerRig>();
        readonly List<PartClamp> candidates = new List<PartClamp>();
        readonly AiNavigationRoute route = new AiNavigationRoute();
        readonly RaycastHit[] hits = new RaycastHit[24];
        NavMeshPath probe;
        PlayerRig owner, speaker;
        PartClamp target;
        Vector3 approach, pinPosition;
        float started, progressAt, bestDistance, grabAt, bestProgress;
        bool approaching, selectedTarget;
        SpaceshipPart requestedPart;
        readonly HashSet<PartClamp> excluded = new HashSet<PartClamp>();
        public bool Active { get; private set; }
        public bool Succeeded { get; private set; }
        public string Status { get; private set; } = "대기";
        public PartClamp Target => target;
        public bool WantsMovement => Active && (target == null || approaching);
        public Vector3? MovementGoal => target != null ? approach : (Vector3?)null;

        public void SuspendMovement()
        {
            if (!Active) return;
            // Keep target/reservation and completed pulling progress, not the previous approach timer.
            if (owner != null && owner.ClampGrabbed) owner.DropEquipment();
            started += Time.fixedDeltaTime;
            progressAt = grabAt = Time.time; bestDistance = float.PositiveInfinity;
            route.Reset(); approaching = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetReservations() => reservations.Clear();

        public void Begin(PlayerRig self, PlayerRig requester, SpaceshipPart part = null, IEnumerable<PartClamp> skip = null)
        {
            Cancel(); owner = self; speaker = requester;
            requestedPart = part; excluded.Clear();
            if (skip != null) foreach (var item in skip) excluded.Add(item);
            Active = true; started = Time.time; Status = "가까운 고정못 탐색";
        }

        public void DetachDriver()
        {
            if (!ReferenceEquals(target, null) && reservations.TryGetValue(target, out var reserved) && reserved == owner)
                reservations.Remove(target);
            owner = null;
            Cancel();
        }

        public void Cancel(string status = "대기")
        {
            Succeeded = false;
            if (owner != null)
            {
                owner.SetInput(default);
                // We enter this task only with empty hands. Never alter pin progress or release state.
                if (owner.ClampGrabbed) owner.DropEquipment();
            }
            if (!ReferenceEquals(target, null) && reservations.TryGetValue(target, out var reserved) && reserved == owner)
                reservations.Remove(target);
            target = null; owner = null; speaker = null; route.Reset();
            Active = false; approaching = selectedTarget = false; grabAt = 0; Status = status;
        }

        public InputFrame Tick(bool allowed)
        {
            if (!Active) return default;
            if (!allowed || owner == null || speaker == null || owner.Downed || owner.IsRiding || speaker.Downed)
            { Cancel("못 뽑기 중단"); return default; }
            if (Time.time - started > 90f) { Cancel("못 작업 시간 초과"); return default; }
            if (target != null && target.IsReleased)
            { Cancel("고정못 해제 확인 · 완료"); Succeeded = true; return default; }
            if (owner.HeldTool != null) { Cancel("도구를 내려놓은 뒤 지시해 주세요"); return default; }
            var world = GameSession.Instance != null ? GameSession.Instance.GetComponent<AiNavigationWorld>() : null;
            if (world == null || !world.Ready) { Status = "못 작업 · 이동 경로 준비 중"; return default; }
            if (target == null)
            {
                if (selectedTarget) { Cancel("대상 고정못이 사라짐"); return default; }
                if (!Select(world)) { Cancel("접근 가능한 빈 고정못 없음"); return default; }
                approaching = true; progressAt = Time.time; bestDistance = float.PositiveInfinity;
            }
            if (!target.isActiveAndEnabled) { Cancel("대상 고정못 비활성화"); return default; }
            if (owner.ClampGrabbed && owner.ClampGrabTarget != target)
            { Cancel("다른 물체를 잡아 작업 중단"); return default; }
            if (owner.ClampGrabTarget == target && owner.ClampGrabbed)
            {
                if (target.Progress > bestProgress + .01f) { bestProgress = target.Progress; progressAt = Time.time; }
                if (Time.time - progressAt > 5f) { Cancel("못 당기기 진행 불가"); return default; }
                var pull = Aim(); pull.GrabLeft = pull.GrabRight = true;
                var backward = owner.FootPosition - target.transform.position; backward.y = 0;
                if (AiFollowController.SafeStep(owner, owner.FootPosition, backward.normalized, hits)) pull.Move = Vector2.down;
                Status = $"당기는 중: {Label()} · {target.Progress:P0}";
                return pull;
            }
            if (target.Pullers > 0) { Cancel("다른 플레이어가 작업 중인 못"); return default; }
            if ((target.transform.position - pinPosition).sqrMagnitude > .09f)
            {
                if (!FindApproach(world, target, out approach)) { Cancel("이동한 못에 접근 불가"); return default; }
                pinPosition = target.transform.position; route.Reset(); approaching = true; grabAt = 0;
            }
            if (approaching)
            {
                var offset = approach - owner.FootPosition;
                bool atApproach = new Vector2(offset.x, offset.z).magnitude <= .14f && Mathf.Abs(offset.y) <= .35f;
                if (!route.Steer(world, owner.FootPosition, approach, .12f, out var direction) && !atApproach)
                {
                    if (Time.time - progressAt > 6f) Cancel("못으로 가는 경로가 막힘");
                    else Status = "못 접근 경로 재탐색";
                    return default;
                }
                if (!route.Arrived && !atApproach)
                {
                    if (route.Remaining < bestDistance - .15f) { bestDistance = route.Remaining; progressAt = Time.time; }
                    if (Time.time - progressAt > 6f) { Cancel("못으로 이동 불가"); return default; }
                    Status = "못으로 이동: " + Label();
                    // Final approach must not stop a full walking stride before the hand-reach point.
                    bool precision = route.Remaining < 1.5f;
                    if (!AiFollowController.SafeStep(owner, owner.FootPosition, direction, hits, precision ? .12f : .6f)) return default;
                    return new InputFrame { AimYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                        Move = Vector2.up * (precision ? .35f : 1f) };
                }
                approaching = false; grabAt = progressAt = Time.time; bestProgress = target.Progress;
            }
            if (Time.time - grabAt > 4f) { Cancel("고정못에 손이 닿지 않음"); return default; }
            var frame = Aim();
            // Give existing focus selection a frame to use our aim before pressing either grab button.
            var focus = owner.CurrentInteractionTarget;
            if (focus.Clamp == target && focus.CanInteract) frame.GrabLeft = frame.GrabRight = true;
            Status = "고정못 잡기: " + Label();
            return frame;
        }

        string Label() => (target.Part != null ? target.Part.name + "/" : "") + target.name;

        InputFrame Aim()
        {
            var point = target.transform.position;
            target.TryGetClosestColliderPoint(owner.Doll.Hips.position, out point);
            var d = point - owner.AimOrigin;
            return new InputFrame { AimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg,
                AimPitch = -Mathf.Atan2(d.y, new Vector2(d.x,d.z).magnitude) * Mathf.Rad2Deg };
        }

        bool Select(AiNavigationWorld world)
        {
            if (owner.LeftHand.Gripping || owner.RightHand.Gripping || owner.Carrying) return false;
            candidates.Clear();
            foreach (var pin in PartClamp.All)
                if (pin != null && pin.isActiveAndEnabled && pin.IsRequired && !pin.IsReleased && pin.Pullers == 0
                    && (requestedPart == null || pin.Part == requestedPart) && !excluded.Contains(pin)
                    && (!reservations.TryGetValue(pin, out var other) || other == null || !other.isActiveAndEnabled)
                    && (pin.transform.position - speaker.FootPosition).sqrMagnitude < 30f * 30f)
                    candidates.Add(pin);
            candidates.Sort((a,b) => (a.transform.position-speaker.FootPosition).sqrMagnitude.CompareTo(
                (b.transform.position-speaker.FootPosition).sqrMagnitude));
            foreach (var pin in candidates)
            {
                if (!FindApproach(world, pin, out approach)) continue;
                target = pin; selectedTarget = true; reservations[pin] = owner; pinPosition = pin.transform.position; route.Reset();
                return true;
            }
            return false;
        }

        bool FindApproach(AiNavigationWorld world, PartClamp pin, out Vector3 result)
        {
            probe ??= new NavMeshPath();
            Vector3 away = pin.Part != null ? pin.transform.position - pin.Part.transform.position : owner.FootPosition - pin.transform.position;
            away.y = 0; if (away.sqrMagnitude < .01f) away = Vector3.forward; away.Normalize();
            for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < 8; i++)
            {
                var direction = Quaternion.Euler(0, i * 45, 0) * away;
                var point = pin.transform.position + direction * (.45f + ring * .15f);
                if (!Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out var ground, 3f,
                        GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore) || ground.normal.y < .77f
                    || ground.collider.GetComponentInParent<SpaceshipPart>() != null) continue;
                if (!NavMesh.SamplePosition(ground.point, out var nav, .25f, NavMesh.AllAreas)
                    || Mathf.Abs(nav.position.y-ground.point.y) > .2f) continue;
                point = nav.position;
                if (!pin.TryGetClosestColliderPoint(point + Vector3.up * RagdollBuilder.HipsY, out var grip)
                    || Vector3.Distance(point + Vector3.up * RagdollBuilder.HipsY, grip) > .68f) continue;
                if (Physics.Linecast(point + Vector3.up * 1.4f, grip, GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                var back = point - pin.transform.position; back.y = 0; back.Normalize();
                if (!AiFollowController.SafeStep(owner, point, back, hits)) continue;
                if (!world.CalculatePath(owner.FootPosition, point, probe)) continue;
                result = point; return true;
            }
            result = default; return false;
        }
    }
}
