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
        PlayerRig rescueTarget;
        readonly List<PlayerRig> downed = new List<PlayerRig>(3);
        float rescueBest, rescueAt;
        SpaceshipPart part;
        string action, destination;
        Vector3 goal, destinationPoint;
        float started, phaseAt, movedAt, bestDistance;
        int phase, pinCount;
        bool pulse;
        public bool Active { get; private set; }

        /// <summary>
        /// 지금 동료를 일으키는 중인가 (S15P21B202-716).
        ///
        /// <para><see cref="AiCompanionController"/> 가 <b>말한 사람이 쓰러졌을 때 멈출지</b>
        /// 를 이 값으로 가른다. 다른 작업은 시킨 사람이 쓰러지면 뜻을 잃지만 살리기는
        /// <b>대상이 따로</b> 있다.</para>
        /// </summary>
        public bool Rescuing => Active && action == "revive";
        public bool Succeeded { get; private set; }
        public string Status { get; private set; } = "대기";

        /// <summary>
        /// 마지막으로 받은 작업이 어떻게 끝났는가 (S15P21B202-703). 끝난 뒤에도 남는다 — 다음
        /// 작업을 시작할 때 <see cref="AiTaskOutcome.Running"/> 으로 바뀐다.
        /// </summary>
        public AiTaskOutcome Outcome { get; private set; }

        /// <summary>끝난 이유. 완료 · 실패는 <c>Finish</c> 의 문구, 취소는 누가 멈췄는지.</summary>
        public string Reason { get; private set; }

        /// <summary>받은 행동 이름. <c>lift_part</c> 는 들어 올린 뒤 <c>hold_tractor</c> 로 바뀌므로 따로 쥔다.</summary>
        public string Command { get; private set; }
        public bool WantsMovement { get; private set; }
        public Vector3? MovementGoal => WantsMovement ? goal : (Vector3?)null;
        public static readonly string[] Actions = {
            "pull_all_pins", "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "bring_tool",
            "bring_bat", "bring_welder", "hit_target", "defend_meteors", "grab_part", "release_part",
            "carry_part", "weld", "cut_weld", "board_tractor", "leave_tractor", "move_tractor",
            "lift_player", "lift_part", "transport_part", "hold_tractor", "stack_part", "goto", "revive"
        };

        /// <summary>
        /// 일으키러 갈 동료를 찾는 반경(m) (S15P21B202-716). 이보다 멀면 "없다" 로 본다 —
        /// 맵 반대편으로 혼자 보내는 것은 도움이 아니다.
        /// </summary>
        public const float ReviveSearchRange = 40f;

        /// <summary>
        /// 이만큼 붙어야 일으키기가 걸린다(m).
        ///
        /// <para><see cref="PhysicsScale.DownedRescueNearReach"/> 1.2m 안에서는 게임이
        /// <b>시야각을 묻지 않는다</b> — 쓰러진 사람의 구조 지점은 바닥(0.30m)에 있어서
        /// 가까이 갈수록 내려다보는 각이 35도를 넘기 때문이다. 그 여유를 3D 거리로 쓰려면
        /// 평면 거리는 더 짧아야 하므로 <c>.9</c> 로 둔다.</para>
        /// </summary>
        public const float ReviveReach = .9f;

        /// <summary>
        /// <c>goto</c> 로 자리를 잡은 뒤 이만큼 밀려나면 다시 간다 (S15P21B202-717).
        ///
        /// <para>지시는 "거기 가 있어" 이지 "거기를 한 번 밟아" 가 아니다. 운석이나 파츠에
        /// 밀려 자리를 떠도 아무 일이 없으면, 방장이 미리 잡아 둔 자리가 <b>조용히
        /// 비어 버린다.</b> 멈춰 서는 거리(<c>.6f</c>)보다 넉넉히 커야 제자리에서
        /// 들썩이지 않는다.</para>
        /// </summary>
        public const float GotoDriftLimit = 2.5f;
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
            Cancel("새 명령"); self = actor; speaker = requester; action = command; destination = to;
            Active = true; Succeeded = false; started = Time.time; Phase(0);
            Outcome = AiTaskOutcome.Running; Reason = null; Command = command;
            Status = command + " · 준비";
            // Snapshot destinations, not a moving camera. Actors and objects remain live references.
            if (!ResolveDestination(to, out destinationPoint)) { Finish(false, "목적지 표면/조립대를 찾지 못함"); return false; }
            if (command == "pull_all_pins")
            {
                part = NearestPart(true); attemptedPins.Clear(); pinCount = 0;
                if (part == null) { Finish(false, "못이 남은 파츠 없음"); return false; }
                pin.Begin(self, speaker, part, attemptedPins); return true;
            }
            // goto 는 목적지 말고는 준비할 것이 없다 — 위에서 이미 풀었다.
            if (command == "goto") return true;
            if (command == "revive") return BeginRevive();
            if (VehicleOrder(command)) return BeginVehicle();
            if (command == "grab_part" || command == "carry_part" || command == "release_part") return BeginCarry();
            return BeginTool();
        }

        public InputFrame Tick()
        {
            WantsMovement = false;
            if (!Active) return default;
            // **살리기는 말한 사람이 쓰러져도 이어 간다** (S15P21B202-716). 다른 작업은
            // 시킨 사람이 쓰러지면 뜻을 잃지만, 살리기는 **대상이 따로 있다.** 여기서 끊으면
            // 둘이 연달아 쓰러졌을 때 AI 가 가던 길을 되돌아온다 — 가장 필요한 순간에.
            if (self == null || speaker == null || self.Downed || (speaker.Downed && action != "revive"))
            { Finish(false, "플레이어 상태 변경으로 중단"); return default; }
            // goto 는 도착한 뒤 그 자리를 지키는 지속 작업이라 시간으로 끊지 않는다
            // (S15P21B202-717). 못 가는 경우는 Navigate 가 "이동 막힘" 으로 따로 끝낸다.
            if (Time.time - started > 180f && action != "defend_meteors" && action != "grab_part"
                && action != "hold_tractor" && action != "goto")
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
            if (action == "goto") return TickGoto();
            if (action == "revive") return TickRevive();
            if (VehicleOrder(action)) return TickVehicle();
            if (action == "grab_part" || action == "carry_part" || action == "release_part") return TickCarry();
            return TickTool();
        }

        public void SuspendMovement()
        {
            pin.SuspendMovement(); route.Reset(); movedAt = phaseAt = Time.time;
            started += Time.fixedDeltaTime;
        }
        public void Cancel() => Cancel("취소");

        /// <summary>
        /// 멈춘다. 돌던 작업이면 <b>취소로 남기고 이유를 적는다</b> (S15P21B202-703) — 전에는
        /// 이유 없이 사라지고 화면은 따라다니기의 "대기" 로 덮였다.
        /// </summary>
        public void Cancel(string reason)
        {
            Settle(AiTaskOutcome.Cancelled, reason);
            pin.Cancel(); ReleaseClaims(); route.Reset(); StopVehicle();
            Active = false; WantsMovement = false; Succeeded = false;
            self = speaker = null; part = null; tool = null; toolGrab = null; carry = null; vehicle = null;
            rescueTarget = null;
        }
        public void DetachDriver()
        {
            Settle(AiTaskOutcome.Cancelled, "사람이 몸을 넘겨받음");
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
            // 결과를 먼저 적는다 — Cancel 이 먼저 돌면 "취소" 로 남는다.
            Settle(success ? AiTaskOutcome.Succeeded : AiTaskOutcome.Failed, message);
            Cancel(); Succeeded = success; Status = message;
        }

        /// <summary>
        /// 돌던 작업의 끝을 한 번만 적고 로그에 남긴다 (S15P21B202-703). 돌고 있지 않았으면
        /// 아무것도 안 한다 — 쉬는 동안 매 틱 불리는 <c>Stop</c> 이 로그를 채우지 않게.
        /// </summary>
        void Settle(AiTaskOutcome outcome, string reason)
        {
            if (Outcome != AiTaskOutcome.Running) return;
            Outcome = outcome; Reason = reason;
            Debug.Log(AiTaskOutcomes.LogLine(Command, outcome, reason, Time.time - started));
        }
        void ReleaseClaims()
        {
            foreach (var item in ownedClaims)
                if (!ReferenceEquals(item, null) && claims.TryGetValue(item, out var owner) && owner == self) claims.Remove(item);
            ownedClaims.Clear();
        }
        void Phase(int value)
        { phase = value; phaseAt = movedAt = Time.time; bestDistance = float.PositiveInfinity; pulse = false; route.Reset(); }

        /// <summary>
        /// 말한 곳으로 가서 <b>거기 있는다</b> (S15P21B202-717).
        ///
        /// <para>전에는 이동 명령이 <c>follow</c> 와 <c>gather</c> 둘뿐이라 <b>AI 를
        /// "나에게" 만 부를 수 있었다.</b> 자리를 미리 잡아 두려면 사람이 그 자리까지 가서
        /// <c>여기서 기다려</c> 를 말해야 했다.</para>
        ///
        /// <para>목적지는 기존 <c>destination</c> 칸을 그대로 쓴다 — 조립대 · 보고 있는 곳 ·
        /// 생략하면 말한 사람 옆. 길이 없거나 막히면 <see cref="Navigate"/> 가 사유를 남기고
        /// 끝낸다(<c>S15P21B202-715</c>).</para>
        /// </summary>
        /// <summary>
        /// 쓰러진 동료에게 가서 <b>사람과 같은 길로</b> 일으킨다 (S15P21B202-716).
        ///
        /// <para>새로 만든 것은 대상 고르기와 다가가기뿐이다. 일으키는 것 자체는
        /// <c>InputFrame.RescueHeld</c> 를 채우는 것 — <b>G 키가 채우는 바로 그 칸</b>이다.
        /// 완료 판정도 서버의 <see cref="Metagame.Gameplay.DownedRescue"/> 가 한다.</para>
        ///
        /// <para>대상은 <see cref="Claim"/> 으로 잡는다. 안 그러면 동료 셋에게 "살려줘" 를
        /// 말했을 때 <b>셋이 같은 사람에게 몰린다.</b></para>
        /// </summary>
        bool BeginRevive()
        {
            // **가까운 순서로 훑으며 아직 아무도 안 잡은 첫 대상을 고른다** (`!615` AI 리뷰).
            //
            // 전에는 최근접 하나만 보고 그것이 이미 잡혀 있으면 바로 포기했다. 그래서
            // 둘이 쓰러진 방에서 `다들 살려줘` 를 하면 **두 번째 동료가 남은 사람을 두고
            // 멈췄다** — 분담하라고 넣은 Claim 이 오히려 한 명을 놀게 만들었다.
            CollectDowned();
            foreach (var rig in downed)
                if (Claim(rig)) { rescueTarget = rig; rescueBest = 0f; rescueAt = Time.time; return true; }
            Finish(false, downed.Count == 0 ? "기절한 동료가 없음"
                                            : "쓰러진 동료마다 이미 다른 동료가 가고 있음");
            return false;
        }

        /// <summary>쓰러진 동료를 <b>가까운 순서로</b> 모은다. 범위 밖은 없는 것으로 본다.</summary>
        void CollectDowned()
        {
            downed.Clear();
            foreach (var player in NetPlayer.AllServerPlayers)
            {
                var rig = player != null ? player.VisualRig : null;
                if (rig == null || rig == self || !rig.Downed) continue;
                if (rig.gameObject.scene != self.gameObject.scene) continue;
                if ((rig.FootPosition - self.FootPosition).sqrMagnitude > ReviveSearchRange * ReviveSearchRange) continue;
                downed.Add(rig);
            }
            var from = self.FootPosition;
            downed.Sort((a, b) => (a.FootPosition - from).sqrMagnitude
                                  .CompareTo((b.FootPosition - from).sqrMagnitude));
        }

        InputFrame TickRevive()
        {
            if (rescueTarget == null) { Finish(false, "대상 동료가 사라짐"); return default; }
            if (!rescueTarget.Downed) { Finish(true, "동료를 일으켰다"); return default; }
            var input = default(InputFrame);
            float distance = Vector3.Distance(self.FootPosition, rescueTarget.FootPosition);
            if (distance > ReviveReach)
            {
                Status = "일으키러 가는 중 · " + distance.ToString("0.0") + "m";
                rescueAt = Time.time;
                if (!Navigate(rescueTarget.FootPosition, ReviveReach, ref input)) return input;
            }
            // **닿았는데 안 차는 경우를 따로 끝낸다.** 인터랙터가 대상을 못 잡으면
            // RescueHeld 를 아무리 눌러도 진행도가 0 인 채로 180초를 버틴다 — 그동안
            // 이 동료는 다른 일을 못 한다. 그것은 도움이 아니라 인원 손실이다.
            if (self.RescueProgress01 > rescueBest + .01f)
            { rescueBest = self.RescueProgress01; rescueAt = Time.time; }
            if (Time.time - rescueAt > 6f)
            { Finish(false, "일으키기가 진행되지 않음 · 대상을 잡지 못함"); return default; }
            Status = "일으키는 중 " + (self.RescueProgress01 * 100f).ToString("0") + "%";
            input = Aim(rescueTarget.FootPosition);
            input.RescueHeld = true;
            return input;
        }

        InputFrame TickGoto()
        {
            var input = default(InputFrame);
            float distance = Vector3.Distance(self.FootPosition, destinationPoint);
            if (phase == 0)
            {
                Status = "이동 중 · " + distance.ToString("0.0") + "m";
                if (!Navigate(destinationPoint, .6f, ref input)) return input;
                Phase(1);
            }
            Status = "자리 지키는 중";
            if (distance > GotoDriftLimit) Phase(0);
            return input;
        }
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
