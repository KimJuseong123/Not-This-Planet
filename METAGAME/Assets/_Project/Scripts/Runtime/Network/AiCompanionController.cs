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
        public string SocialPerforming => performing ? social : null;
        public bool Busy => work.Active || pin.Active || follow.Active || social != null || pending.Count > 0;
        public static bool IsSocial(string action) => action == "dance" || action == "greet" || action == "heart" || action == "cheer";
        public string Status => recoveryStatus ?? (workOrder ? work.Status : approaching ? "다가가는 중" : social != null && !performing ? "말한 사람 바라보는 중" : social != null ?
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
            Stop();
            requester = speaker;
            if (AiWorkTask.Supports(action))
            {
                workOrder = true;
                if (work.Begin(action, self, speaker, destination)) return true;
                refusal = "작업 대상을 못 찾음"; return false;
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
            if (advance && pending.Count > 0)
            {
                var next = pending.Dequeue(); var rest = pending.ToArray(); var source = planSpeaker;
                pending.Clear();
                if (!Apply(next.action, source, self, next.approach, next.destination)) { pending.Clear(); return default; }
                planSpeaker = source; foreach (var step in rest) pending.Enqueue(step); advance = false;
            }
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
            if (!allowed || self == null || self.Downed || (self.IsRiding && !workOrder)
                || requester == null || (requester.Downed && !rescuing))
            { Stop(); return default; }
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
        public void Stop()
        {
            work.Cancel(); workOrder = false; pending.Clear(); planSpeaker = null; advance = false;
            pin.Cancel(); follow.Stop(); recovery.Reset(); requester = null; recoveryStatus = null; pinOrder = false;
            social = null; gather = false; approaching=false; performing=false; watchRequester=false;
        }
        public void DetachDriver()
        {
            work.DetachDriver();
            pin.DetachDriver();
            follow.Stop();
            pending.Clear();
            planSpeaker = requester = null;
        }
    }
}
