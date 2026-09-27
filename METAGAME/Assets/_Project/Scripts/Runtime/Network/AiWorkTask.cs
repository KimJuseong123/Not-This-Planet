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
        public bool Rescuing => Active && (action == "revive" || Standing(action));
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
            "pull_pin", "come", "pull_all_pins", "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "bring_tool",
            "bring_bat", "bring_welder", "hit_target", "defend_meteors", "grab_part", "release_part",
            "carry_part", "weld", "cut_weld", "board_tractor", "leave_tractor", "move_tractor",
            "lift_player", "lift_part", "transport_part", "hold_tractor", "stack_part", "goto", "revive"
        };

        /// <summary>
        /// 일으키러 갈 동료를 찾는 반경(m) (S15P21B202-716). 이보다 멀면 "없다" 로 본다 —
        /// 맵 반대편으로 혼자 보내는 것은 도움이 아니다.
        /// </summary>
        public const float ReviveSearchRange = 150f;   // 판 전체 — 2026-09-26 QA: 40m 밖의 기절자를 "없음" 으로 봤다

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

        /// <summary>
        /// <c>come</c>(이쪽으로 와)는 말한 사람과 이만큼 가까워지면 멈춘다(m, 평면) (S15P21B202-746).
        /// 몸통 둘이 부딪히지 않고, 말한 사람이 "왔다" 고 느끼는 거리.
        /// </summary>
        public const float ComeStopDistance = 2f;

        /// <summary>이보다 멀면 뛰어서 온다(m). 부른 사람을 오래 기다리게 하지 않는다.</summary>
        public const float ComeRunDistance = 6f;

        /// <summary>
        /// 고정못 하나를 못 뽑으면 <b>다음으로 가까운 못</b>을 이만큼 더 해 본다 (S15P21B202-746).
        /// 한 번 어긋났다고 명령 전체를 버리면 시연에서 "못 알아들었다" 로 보인다.
        /// </summary>
        public const int PinRetries = 2;

        /// <summary>
        /// 멈추지 않는 명령인가 (S15P21B202-746). 고정못은 없을 때까지, 운석은 계속 — 시킨 사람이 쓰러져도
        /// 이어 간다(살리기와 같다). 동료가 쓰러졌다 일어나면 <see cref="AiCompanionController"/> 가 다시 건다.
        /// </summary>
        public static bool Standing(string value) => value == "pull_pin" || value == "defend_meteors";

        /// <summary>시킨 일이 더 할 것 없이 끝났다 — 다시 걸 필요가 없다(뽑을 못이 없다).</summary>
        public bool Exhausted { get; private set; }
        int pinsPulled;
        bool pinsRetried;

        /// <summary>작업이 끝날 때마다 하나씩 는다 — 화면이 "새 결과가 났다" 를 안다 (S15P21B202-746).</summary>
        public int OutcomeSeq { get; private set; }

        /// <summary>이번 작업에서 해낸 수 — 뽑은 못 · 쳐낸 운석. 화면이 한 건마다 띄운다.</summary>
        public int Tally { get; private set; }
        public static bool Supports(string value) => System.Array.IndexOf(Actions, value) >= 0;
        public static bool VehicleOrder(string value) => value == "board_tractor" || value == "leave_tractor"
            || value == "move_tractor" || value == "lift_player" || value == "lift_part"
            || value == "transport_part" || value == "hold_tractor" || value == "stack_part";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetClaims() => claims.Clear();

        /// <summary>다른 AI 가 맡은 대상인가 (S15P21B202-712). <see cref="Claim"/> 과 같은 판정을 맡지 않고 묻는다.</summary>
        bool ClaimedByOther(Object target)
            => target != null && claims.TryGetValue(target, out var owner) && owner != null && owner != self && owner.isActiveAndEnabled;

        bool Claim(Object target)
        {
            if (target == null) return false;
            if (claims.TryGetValue(target, out var owner) && owner != null && owner != self && owner.isActiveAndEnabled) return false;
            claims[target] = self; ownedClaims.Add(target); return true;
        }

        public bool Begin(string command, PlayerRig actor, PlayerRig requester, string to = "default")
        {
            if (!Supports(command) || actor == null || requester == null) return false;
            Cancel("새 명령"); self = actor; speaker = requester; action = command; destination = to; Exhausted = false; Tally = 0;
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
            if (command == "come") { comeGoal = speaker.FootPosition; return true; }
            if (command == "pull_pin")
            {
                // 명령받은 동료에게서 가장 가까운 못 (S15P21B202-746). 손에 든 도구는 먼저 내려놓는다 —
                // 전에는 "도구를 내려놓은 뒤 지시해 주세요" 로 끝났고, 로그에도 남지 않았다.
                attemptedPins.Clear(); pinCount = 0; pinTarget = null; part = null; pinsPulled = 0; pinsRetried = false;
                Phase(self.HeldTool != null ? 0 : 1);
                if (phase == 1) pin.Begin(self, speaker, null, attemptedPins);
                return true;
            }
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
            if (self == null || speaker == null || self.Downed || (speaker.Downed && action != "revive" && !Standing(action)))
            { Finish(false, "플레이어 상태 변경으로 중단"); return default; }
            // goto 는 도착한 뒤 그 자리를 지키는 지속 작업이라 시간으로 끊지 않는다
            // (S15P21B202-717). 못 가는 경우는 Navigate 가 "이동 막힘" 으로 따로 끝낸다.
            if (Time.time - started > 180f && action != "defend_meteors" && action != "grab_part"
                && action != "hold_tractor" && action != "goto" && !Standing(action))
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
            if (action == "come") return TickCome();
            if (action == "pull_pin") return TickPin();
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
            ReleaseChase();
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
            Outcome = outcome; Reason = reason; OutcomeSeq++;
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
            if (downed.Count == 0) Debug.Log("[AI] 살릴 사람 찾기 · " + DownedCensus());
            Finish(false, downed.Count == 0 ? "기절한 동료가 없음"
                                            : "쓰러진 동료마다 이미 다른 동료가 가고 있음");
            return false;
        }

        /// <summary>
        /// 살릴 사람을 못 찾았을 때 로그에 남길 한 줄 — 누가 어디서 무슨 상태인지 (S15P21B202-746). 2026-09-26 QA 에서
        /// 말한 사람이 기절한 채 "살려" 를 했는데 "기절한 동료가 없음" 이 나왔고, 로그로는 까닭을 가릴 수 없었다.
        /// </summary>
        string DownedCensus()
        {
            var line = new System.Text.StringBuilder();
            foreach (var player in NetPlayer.AllServerPlayers)
            {
                var rig = player != null ? player.VisualRig : null;
                if (rig == null || rig == self) continue;
                line.Append(player.TeamName).Append(rig.Downed ? " 기절" : rig.Ragdolled ? " 넘어짐" : " 서 있음")
                    .Append(' ').Append((rig.FootPosition - self.FootPosition).magnitude.ToString("0")).Append('m')
                    .Append(rig.gameObject.scene != self.gameObject.scene ? " (다른 씬)" : "").Append(" · ");
            }
            return line.Length > 0 ? line.ToString() : "플레이어 없음";
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

        Vector3 comeGoal;
        PartClamp pinTarget;

        /// <summary>
        /// 말한 사람에게 온다 (S15P21B202-746). 사람이 움직이면 목적지도 따라간다 — 전에는
        /// <c>goto</c> 가 말한 순간의 자리로 가서 그 자리를 지켰다.
        /// </summary>
        InputFrame TickCome()
        {
            var input = default(InputFrame);
            var to = speaker.FootPosition;
            var offset = to - self.FootPosition; offset.y = 0;
            float distance = offset.magnitude;
            // Finish 가 speaker 를 비우므로 돌아볼 방향을 먼저 잰다.
            if (distance <= ComeStopDistance) { var face = AiCompanionController.Face(self, speaker); Finish(true, "말한 사람 곁에 왔다"); return face; }
            // 사람이 1m 넘게 움직였으면 "나아지고 있나" 를 새로 잰다 — 달아나는 사람을 쫓는 것은 막힘이 아니다.
            if ((to - comeGoal).sqrMagnitude > 1f) { comeGoal = to; bestDistance = float.PositiveInfinity; movedAt = Time.time; }
            Status = "오는 중 · " + distance.ToString("0.0") + "m";
            if (Navigate(to, ComeStopDistance - .2f, ref input))
            { var face = AiCompanionController.Face(self, speaker); Finish(true, "말한 사람 곁에 왔다"); return face; }
            if (!Active) return input;   // Navigate 가 막힘으로 끝냈다
            input.Run = distance > ComeRunDistance;
            return input;
        }

        /// <summary>
        /// 가장 가까운 고정못 하나를 뽑는다 (S15P21B202-746). 못 뽑으면 다음 못을 <see cref="PinRetries"/>
        /// 번 더 해 보고, 뽑을 못이 없으면 끝낸다 — 그러면 기본 행동으로 돌아간다.
        /// </summary>
        InputFrame TickPin()
        {
            var input = default(InputFrame);
            if (phase == 0)
            {
                if (self.HeldTool == null) { Phase(1); pin.Begin(self, speaker, null, attemptedPins); return input; }
                if (!pulse) { input.ToolUtility = true; pulse = true; }
                Status = "들고 있던 도구 내려놓는 중";
                if (Time.time - phaseAt > 2f) Finish(false, "들고 있던 도구를 내려놓지 못함");
                return input;
            }
            input = pin.Tick(true);
            if (pin.Target != null) pinTarget = pin.Target;
            Status = pin.Status;
            WantsMovement = pin.WantsMovement;
            if (pin.MovementGoal.HasValue) goal = pin.MovementGoal.Value;
            if (pin.Active) return input;
            // **못이 없을 때까지 이어 간다** (S15P21B202-746). 하나 뽑으면 다음으로 가까운 못으로 간다.
            if (pin.Succeeded || (pinTarget != null && pinTarget.IsReleased))
            {
                pinsPulled++; pinCount = 0; pinTarget = null; Tally = pinsPulled;
                Debug.Log("[AI] 고정못 " + pinsPulled + "개째 뽑음 · 다음 못으로");
                pin.Begin(self, speaker, null, attemptedPins);
                return input;
            }
            if (pinTarget == null && attemptedPins.Count > 0 && !pinsRetried)
            {
                // 다른 못을 다 뽑았으면 **포기했던 못을 한 번 더** (2026-09-26 검사: 처음에 막혔던 3m 앞의 못이 나중에는
                // 설 자리가 났는데 포기 목록에 남아 그냥 끝났다 — 파츠가 풀리며 길이 바뀐다).
                pinsRetried = true; attemptedPins.Clear(); pinCount = 0;
                Debug.Log("[AI] 포기했던 고정못을 다시 본다");
                pin.Begin(self, speaker, null, attemptedPins);
                return input;
            }
            if (pinTarget == null)
            {
                // 뽑을 못이 더 없다 — 할 일을 다 했다. 뽑은 것이 없으면 처음부터 없던 것이다.
                Exhausted = true;
                // "더 없다" 는 **걸어서 닿을 수 있는 못**이 더 없다는 뜻이다 — 높은 곳에 놓인 파츠의 못은 길이 없다.
                Finish(pinsPulled > 0, pinsPulled > 0 ? "닿을 수 있는 고정못을 다 뽑았다 · " + pinsPulled + "개" : pin.Status);
                return input;
            }
            attemptedPins.Add(pinTarget);
            if (++pinCount > PinRetries)
            {
                // 이 못은 버리고 다른 못으로 — 한 못이 안 된다고 명령 전체를 끝내지 않는다.
                Debug.Log("[AI] 고정못 " + pinTarget.name + " 포기 · " + pin.Status + " · 다른 못으로");
                pinCount = 0;
            }
            else Debug.Log("[AI] 고정못 다시 시도 " + pinCount + "/" + PinRetries + " · " + pin.Status);
            pinTarget = null;
            pin.Begin(self, speaker, null, attemptedPins);
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
            if (!AiFollowController.SteerAround(self, self.FootPosition, ref direction, stepHits, .18f)) return false;
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
        /// <summary>
        /// 말한 사람에게서 가장 가까운 파츠. <paramref name="accept"/> 가 있으면 <b>쓸 수 있는 것 중에서</b>
        /// 고른다 (S15P21B202-712) — 전에는 가장 가까운 것이 용접·고정·다른 AI 몫이면 명령 전체가 실패했다.
        /// 말한 사람이 바라보는 파츠도 조건을 통과해야 고른다.
        /// </summary>
        SpaceshipPart NearestPart(bool pins = false, System.Predicate<SpaceshipPart> accept = null)
        {
            SpaceshipPart chosen = null; float best = 40f * 40f;
            // Selection happens once at acceptance, not on every physics tick.
            var focus = speaker.CurrentInteractionTarget.Grabbable;
            var focusedPart = focus != null ? focus.GetComponentInParent<SpaceshipPart>() : null;
            foreach (var item in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
            {
                if (!item.isActiveAndEnabled || item.gameObject.scene != self.gameObject.scene) continue;
                if (pins ? item.RemainingRequiredClamps == 0 : item.GetComponent<TransportVehicle>() != null) continue;
                if (accept != null && !accept(item)) continue;
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
