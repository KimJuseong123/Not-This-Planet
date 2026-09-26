using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;
using Unity.Profiling;

namespace Metagame.Network
{
    public sealed class AiCompanionController
    {
        static readonly ProfilerMarker TickMarker = new("AI.Companion.Tick");
        readonly AiFollowController follow = new AiFollowController();
        readonly AiPinTask pin = new AiPinTask();
        readonly AiMovementRecovery recovery = new AiMovementRecovery();
        readonly AiWorkTask work = new AiWorkTask();
        readonly AiMeteorDodge dodge = new AiMeteorDodge();
        static int ambientSeeds;
        /// <summary>명령이 없을 때의 행동 (S15P21B202-746). 동료마다 다른 씨앗이라 셋이 같이 춤추지 않는다.</summary>
        readonly AiAmbient ambient = new AiAmbient(unchecked(System.Environment.TickCount * 31 + ++ambientSeeds * 7919));

        /// <summary>지금 춤을 출 수 있나(쿨다운·동작). 몸을 가진 <c>NetPlayer</c> 가 매 틱 알려 준다.</summary>
        public bool AmbientCanDance { get; set; }

        /// <summary>
        /// 멈추지 않는 명령(고정못 · 운석) — 끊겨도 다시 건다 (S15P21B202-746). 동료가 쓰러졌다 일어나거나 작업이
        /// 한 번 어긋나 끝나도, 새 명령이 오기 전까지는 그 일을 이어 간다. 뽑을 못이 없으면 내려놓는다.
        /// </summary>
        string standing;
        PlayerRig standingSpeaker;
        float standingRetryAt;

        /// <summary>끊긴 명령을 다시 걸기 전에 기다리는 초. 매 틱 걸면 안 되는 일을 계속 시도한다.</summary>
        public const float StandingRetrySeconds = 3f;

        /// <summary>명령 없이 기본 행동 중인가. 화면·로그가 명령 상태와 가른다.</summary>
        public bool Ambient => !Busy && ambient.Current != AiAmbient.Mode.None;
        readonly System.Collections.Generic.Queue<AiCommandStep> pending = new System.Collections.Generic.Queue<AiCommandStep>();
        PlayerRig planSpeaker;
        bool workOrder, advance;
        PlayerRig requester;
        string recoveryStatus;
        bool pinOrder;
        string social;
        float socialUntil;
        bool gather;
        bool approaching, performing;
        float faceStarted, approachDeadline;
        bool watchRequester;
        public string SocialPerforming => performing ? social : !Busy ? ambient.Performing : null;
        public bool Busy => work.Active || pin.Active || follow.Active || social != null || pending.Count > 0;

        /// <summary>마지막 작업이 어떻게 끝났는가 · 왜 (S15P21B202-703). 화면 글자가 아니라 값이다.</summary>
        public AiTaskOutcome WorkOutcome => work.Outcome;
        public string WorkReason => work.Reason;

        /// <summary>머리 위에 띄울 "지금 하는 일" (S15P21B202-746). 끊긴 명령을 다시 걸 참이면 그 명령.</summary>
        public byte Badge => AiBadges.Of(work.Active ? work.Command : standing, dodge.Active);
        public int OutcomeSeq => work.OutcomeSeq;
        public int Tally => work.Tally;
        public string LastCommand => work.Command;
        public static bool IsSocial(string action) => action == "dance" || action == "greet" || action == "heart" || action == "cheer";
        public string Status => dodge.Active ? "운석 피하는 중" : Ambient ? ambient.Status : recoveryStatus ?? (workOrder ? work.Status : approaching ? "다가가는 중" : social != null && !performing ? "말한 사람 바라보는 중" : social != null ?
            (social == "dance" ? "춤추는 중" : social == "greet" ? "인사하는 중" : "감정 표현 중") :
            pinOrder ? pin.Status : follow.Status);
        public bool Apply(string action, PlayerRig speaker, PlayerRig self, bool approach = false, string destination = "default")
            => Apply(action, speaker, self, out _, approach, destination);

        /// <summary>
        /// 시작하지 못하면 <paramref name="refusal"/> 에 <b>왜 못 시작했는지</b>를 담는다
        /// (S15P21B202-715).
        ///
        /// <para>갈래마다 이름이 있어야 한다. 전에는 전부 <c>return false</c> 라서, 화면에는
        /// <c>0/1명</c> 만 남고 <b>손이 차 있어서인지 대상을 못 찾아서인지 알 수 없었다.</b></para>
        /// </summary>
        public bool Apply(string action, PlayerRig speaker, PlayerRig self, out string refusal,
                          bool approach = false, string destination = "default")
        {
            refusal = null;
            if (action != "follow" && action != "gather" && action != "wait" && action != "cancel" && action != "pull_pin" && !IsSocial(action) && !AiWorkTask.Supports(action))
            { refusal = "모르는 행동"; return false; }
            if (action != "wait" && action != "cancel" && (speaker == null || self == null))
            { refusal = "말한 사람을 못 찾음"; return false; }
            if (IsSocial(action) && (Busy || self.HeldTool != null || self.Carrying || self.LeftHand.Gripping || self.RightHand.Gripping || self.GrabbedByOthers))
            { refusal = Busy ? "다른 작업 중이라 감정 표현 불가" : "손에 무언가를 들고 있어 감정 표현 불가"; return false; }
            Stop(action == "cancel" ? "취소 명령" : "새 명령");
            requester = speaker;
            standing = AiWorkTask.Standing(action) ? action : null;
            standingSpeaker = standing != null ? speaker : null;
            if (AiWorkTask.Supports(action))
            {
                workOrder = true;
                if (work.Begin(action, self, speaker, destination)) return true;
                // 못 시작한 **그 이유**를 올린다 (S15P21B202-703). 전에는 뭉뚱그린 한 문구라
                // "견인 가능한 빈 파츠 없음" 인지 "목적지를 찾지 못함" 인지 화면에서 사라졌다.
                refusal = work.Reason ?? "작업 대상을 못 찾음"; return false;
            }
            if (IsSocial(action))
            {
                social = action; approaching = approach; faceStarted = Time.time;
                if(approach) { follow.Apply("follow",speaker); approachDeadline=Time.time+25f; }
                return true;
            }
            if (action == "pull_pin") { pinOrder = true; pin.Begin(self, speaker); return true; }
            if (action == "gather")
            {
                // 말한 사람 쪽이 아니면 `goto` 와 같은 일이다 (S15P21B202-729). 새로 짜지
                // 않고 그쪽에 넘긴다 — 두 곳이 같은 일을 각자 하면 한쪽만 고쳐진다.
                if (AiCommandPlan.GathersAwayFromSpeaker(action, destination))
                {
                    workOrder = true;
                    if (work.Begin("goto", self, speaker, destination)) return true;
                    refusal = work.Reason ?? "모일 자리를 못 찾음"; return false;
                }
                gather = true;
                if (follow.Apply("follow", speaker)) return true;
                refusal = "따라갈 상대를 못 잡음"; return false;
            }
            if (follow.Apply(action, speaker)) return true;
            refusal = "이동 명령을 시작하지 못함";
            return false;
        }
        public void SetFormation(Vector3 offset) => follow.SetFormation(offset, gather || approaching);
        public static InputFrame Face(PlayerRig self, PlayerRig speaker)
        {
            var direction=speaker.FootPosition-self.FootPosition; direction.y=0;
            if(direction.sqrMagnitude<.01f) direction=self.FacingDirection;
            return new InputFrame { AimYaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg };
        }
        public InputFrame Tick(PlayerRig self, bool allowed)
        {
            using var sample = TickMarker.Auto();
            double began = Metagame.Core.FrameBudget.Stamp;
            try { return TickTimed(self, allowed); }
            finally { Metagame.Core.FrameBudget.AddAi(Metagame.Core.FrameBudget.Stamp - began); }
        }

        InputFrame TickTimed(PlayerRig self, bool allowed)
        {
            if (advance && pending.Count > 0)
            {
                var next = pending.Dequeue(); var rest = pending.ToArray(); var source = planSpeaker;
                pending.Clear();
                if (!Apply(next.action, source, self, next.approach, next.destination)) { pending.Clear(); return default; }
                planSpeaker = source; foreach (var step in rest) pending.Enqueue(step); advance = false;
            }
            // 곧 떨어질 운석을 피한다 (S15P21B202-746) — 명령·기본 행동보다 먼저. 방망이를 들고 칠 참이면 피하지 않는다.
            if (dodge.Tick(self, allowed, work.Active && work.Striking, out var escape)) return escape;
            // 멈추지 않는 명령이 끊겼으면 다시 건다. 뽑을 못이 없어 끝났으면 내려놓는다.
            if (!Busy && standing != null)
            {
                if (work.Exhausted || standingSpeaker == null) standing = null;
                else if (allowed && self != null && !self.Downed && !self.IsRiding && Time.time >= standingRetryAt)
                {
                    standingRetryAt = Time.time + StandingRetrySeconds;
                    var keep = standing; var who = standingSpeaker;
                    if (Apply(keep, who, self, out var refused)) UnityEngine.Debug.Log("[AI] 끊긴 명령 다시 · " + keep);
                    standing = keep; standingSpeaker = who;
                }
            }
            // 명령이 없으면 기본 행동이다 (S15P21B202-746). 전에는 여기서 TickCore 가 "말한 사람이
            // 없음" 으로 멈추고 끝이라, 명령을 기다리는 동료가 선 채로 굳어 있었다.
            if (!Busy && AiDemoCommands.Enabled) return ambient.Tick(self, allowed, AmbientCanDance, Time.time);
            return TickCore(self, allowed);
        }
        public void QueueAfterCurrent(AiCommandStep[] steps, PlayerRig speaker)
        {
            pending.Clear();
            planSpeaker = speaker;
            for (int i = 1; i < steps.Length; i++) pending.Enqueue(steps[i]);
        }
        InputFrame TickCore(PlayerRig self, bool allowed)
        {
            recoveryStatus = null;
            // **살리는 중이면 말한 사람이 쓰러져도 멈추지 않는다** (S15P21B202-716).
            //
            // `AiWorkTask.Tick` 에 같은 예외를 두었는데 **여기가 먼저 끊어서 그 코드가
            // 죽어 있었다.** 둘이 연달아 쓰러지면 AI 가 가던 길을 되돌아온다 — 가장
            // 필요한 순간에. 시킨 사람이 쓰러지면 뜻을 잃는 다른 작업과 달리, 살리기는
            // **대상이 따로** 있다.
            bool rescuing = workOrder && work.Rescuing;
            string halt = HaltReason(self, allowed, rescuing);
            if (halt != null) { Stop(halt); return default; }
            if (workOrder)
            {
                if (!self.IsRiding && work.Active && recovery.Tick(self, requester, work.WantsMovement, out var rescued, work.MovementGoal))
                { work.SuspendMovement(); recoveryStatus = recovery.Status; return rescued; }
                var jobInput = work.Tick();
                if (!work.Active) { advance = work.Succeeded; if (!advance) pending.Clear(); }
                return jobInput;
            }
            if (social != null && (self.Ragdolled || self.GrabbedByOthers || self.HeldTool!=null || self.Carrying || self.LeftHand.Gripping || self.RightHand.Gripping))
            { Stop(); return default; }
            if (social != null && !approaching)
            {
                var face=Face(self,requester);
                if(!performing)
                {
                    var direction=Quaternion.Euler(0,face.AimYaw,0)*Vector3.forward;
                    if(Time.time-faceStarted>.15f && Vector3.Angle(self.FacingDirection,direction)<12f)
                    { performing=true; socialUntil=Time.time+(social=="dance" ? 16f : 2.5f); }
                    else if(Time.time-faceStarted>4f) { Stop(); return default; }
                }
                else if(Time.time>=socialUntil) { social = null; performing = false; advance = true; return face; }
                return face;
            }
            bool active = pinOrder ? pin.Active : follow.Active;
            if (active && recovery.Tick(self, requester, pinOrder ? pin.WantsMovement : follow.WantsMovement,
                    out var frame, pinOrder ? pin.MovementGoal : follow.Goal))
            {
                pin.SuspendMovement(); follow.SuspendMovement(); recoveryStatus = recovery.Status;
                return frame;
            }
            var input = pinOrder ? pin.Tick(allowed) : follow.Tick(self, allowed);
            if (pinOrder && !pin.Active) { advance = pin.Succeeded; if (!advance) pending.Clear(); }
            if(approaching)
            {
                if(!follow.Active || Time.time>approachDeadline) { Stop(); recoveryStatus="접근 실패 · 행동 취소"; return default; }
                if(follow.Arrived) { follow.Stop(); approaching=false; faceStarted=Time.time; return Face(self,requester); }
            }
            if (gather && !follow.Active) { gather = false; pending.Clear(); }
            if (gather && follow.Arrived) { follow.Stop(); gather = false; watchRequester=true; advance = true; }
            if(watchRequester) return Face(self,requester);
            return input;
        }
        /// <summary>
        /// 지금 멈춰야 하면 <b>그 이유</b>, 아니면 null (S15P21B202-703). 전에는 한 줄의 or 로
        /// 묶여 있어 무엇 때문에 멈췄는지 남지 않았다.
        /// </summary>
        string HaltReason(PlayerRig self, bool allowed, bool rescuing)
        {
            if (!allowed) return "AI 명령이 막힌 상태";
            if (self == null || self.Downed) return "AI 가 쓰러짐";
            if (self.IsRiding && !workOrder) return "작업 없이 탑승 중";
            if (requester == null) return "말한 사람이 없음";
            if (requester.Downed && !rescuing) return "말한 사람이 쓰러짐";
            return null;
        }

        public void Stop() => Stop("중단");

        public void Stop(string reason)
        {
            work.Cancel(reason); workOrder = false; pending.Clear(); planSpeaker = null; advance = false;
            ambient.Stop();
            pin.Cancel(); follow.Stop(); recovery.Reset(); requester = null; recoveryStatus = null; pinOrder = false;
            social = null; gather = false; approaching=false; performing=false; watchRequester=false;
        }
        public void DetachDriver()
        {
            standing = null; standingSpeaker = null;
            work.DetachDriver();
            pin.DetachDriver();
            follow.Stop();
            pending.Clear();
            planSpeaker = requester = null;
        }
    }
}
