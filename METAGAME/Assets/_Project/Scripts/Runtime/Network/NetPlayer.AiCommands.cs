using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Core;

namespace Metagame.Network
{
    public sealed partial class NetPlayer
    {
        // The seat owns the persistent body; this driver owns only its command state.
        public bool IsAi => IsAiDriver;

        /// <summary>
        /// 이 사람이 지금 AI 에게 말하고 있는가(서버에서만 뜻이 있다). 길찾기가 "명령이 오고 있다" 를
        /// 알고 미리 굽는 데 쓴다 (S15P21B202-737).
        /// </summary>
        public bool AiHearingOpen => hearingGroup != null;
        public static IReadOnlyList<NetPlayer> AllServerPlayers => ServerPlayers;
        readonly AiCompanionController aiFollow = new AiCompanionController();
        public string AiActivity => aiFollow.Status;
        public bool AiBusy => aiFollow.Busy;
        /// <summary>마지막 작업의 끝과 이유 (S15P21B202-703). 서버에서만 뜻이 있다.</summary>
        public AiTaskOutcome AiLastOutcome => aiFollow.WorkOutcome;
        public string AiLastReason => aiFollow.WorkReason;
        public uint AiOrderRevision { get; private set; }
        string aiPresentedSocial;
        [SyncVar] double aiGreetingStarted;
        [SyncVar] bool aiGreeting;
        public bool AiGreetingActive => aiGreeting;

        public void SetAiFormation(Vector3 offset)
        {
            if (isServer && IsAi) aiFollow.SetFormation(offset);
        }

        public bool TryApplyAiOrder(string action, PlayerRig speaker, bool approach = false, string destination = "default")
            => TryApplyAiOrder(action, speaker, out _, approach, destination);

        /// <summary>
        /// 거절하면 <paramref name="refusal"/> 에 <b>왜 거절했는지</b>를 담는다
        /// (S15P21B202-715). 받아들이면 <c>null</c> 이다.
        /// </summary>
        public bool TryApplyAiOrder(string action, PlayerRig speaker, out string refusal,
                                    bool approach = false, string destination = "default")
        {
            // 시연에서는 네 명령뿐이다 (S15P21B202-746). 판단기가 무엇을 내든 넷 중 가까운 것으로 옮기고,
            // 옮길 곳이 없으면 거절한다 — 깔린 팩을 다시 굽지 않아도 넷만 한다.
            if (AiDemoCommands.Enabled)
            {
                var mapped = AiDemoCommands.Map(action, destination);
                if (mapped == null) { refusal = AiDemoCommands.Refusal + " (들은 행동: " + action + ")"; return false; }
                if (mapped != action) { approach = false; destination = "default"; }
                action = mapped;
            }
            var session = GameSession.Instance;
            refusal = AiCommandPlan.OrderRefusal(action,
                aiSeat: isServer && IsAi && body != null,
                roomActive: AiHostSettings.ServerState == AiRoomState.Active,
                downed: body != null && body.Downed,
                ridingBlocks: body != null && body.IsRiding && !AiWorkTask.VehicleOrder(action) && action != "wait",
                cutscene: session == null || session.CinematicInputBlocked || session.EndingActive,
                danceBlocked: action == "dance" && (body?.Doll?.Suit?.Locomotion == null
                    || !body.Doll.Suit.Locomotion.HasDance(1) || NetworkTime.time < _nextDanceAllowed),
                speakerDowned: speaker != null && speaker.Downed);
            if (refusal != null) return false;
            bool applied = aiFollow.Apply(action, speaker, body, out refusal, approach, destination);
            if (applied)
            {
                AiOrderRevision++;
                StopAiSocialPresentation();
                if (action == "cancel" || action == "wait") body.SetInput(default);
            }
            return applied;
        }

        public bool TryApplyAiPlan(AiCommandStep[] steps, PlayerRig speaker)
            => TryApplyAiPlan(steps, speaker, out _);

        /// <summary>계획이 서지 않은 이유를 그대로 올려 보낸다 (S15P21B202-715).</summary>
        public bool TryApplyAiPlan(AiCommandStep[] steps, PlayerRig speaker, out string refusal)
        {
            refusal = null;
            if (!AiCommandPlan.ValidSteps(steps)) { refusal = "계획 형식이 틀림"; return false; }
            // 시연에서는 한 번에 하나다 (S15P21B202-746). 넷 중 하나로 옮길 수 있는 첫 단계만 하고 나머지는
            // 버린다 — "방망이 집어서 운석 막아" 는 두 단계로 오지만 넷 중에서는 한 가지 일이다.
            if (AiDemoCommands.Enabled)
            {
                AiCommandStep first = null;
                foreach (var step in steps)
                    if (step != null && AiDemoCommands.Map(step.action, step.destination) != null) { first = step; break; }
                steps = new[] { first ?? steps[0] };
            }
            if (!TryApplyAiOrder(steps[0].action, speaker, out refusal, steps[0].approach, steps[0].destination)) return false;
            aiFollow.QueueAfterCurrent(steps, speaker);
            return true;
        }

        readonly AiStatusLog aiActivityLog = new AiStatusLog();
        string aiLoggedActivity;

        /// <summary>
        /// 동료가 지금 하는 일이 바뀌면 로그에 남긴다 (S15P21B202-746). 전에는 화면에만 떠서, 2026-09-25 QA 에서
        /// 고정못 명령 세 번이 <b>어디서 멈췄는지 로그로 가릴 수 없었다</b>. 숫자만 바뀌는 줄은 뜸하게 적는다.
        /// </summary>
        void LogAiActivity()
        {
            string now = aiFollow.Status;
            if (now == aiLoggedActivity) return;
            aiLoggedActivity = now;
            aiActivityLog.Write("[AI] " + TeamName + " ·", now);
        }

        /// <summary>기본 행동이 지금 춤을 춰도 되나 — 명령으로 추는 춤과 같은 조건(동작·쿨다운).</summary>
        bool AiCanDanceNow => body?.Doll?.Suit?.Locomotion != null && body.Doll.Suit.Locomotion.HasDance(1)
                              && NetworkTime.time >= _nextDanceAllowed;

        void StopAiSocialPresentation()
        {
            if (body != null && (body.DanceHolding || danceId != 0))
            {
                body.Doll?.Suit?.CancelDance();
                danceId = 0;
                danceSeq++;
            }
            aiGreeting = false;
            aiPresentedSocial = null;
            _emoteExpiresAt = NetworkTime.time;
        }

        void UpdateAiSocialPresentation()
        {
            string action = aiFollow.SocialPerforming;
            if (action == aiPresentedSocial) return;
            StopAiSocialPresentation();
            aiPresentedSocial = action;
            if (action == "dance")
            {
                _nextDanceAllowed = NetworkTime.time + DanceCatalog.Cooldown;
                danceId = 1;
                danceSeq++;
                body.Doll?.Suit?.PlayDance(1);
            }
            else if (action == "greet")
            {
                aiGreeting = true;
                aiGreetingStarted = NetworkTime.time;
            }
            else if ((action == "heart" || action == "cheer")
                && _emoteState.TryShow((byte)(action == "heart" ? 4 : 3), NetworkTime.time))
            {
                _emoteId = _emoteState.Id;
                _emoteExpiresAt = _emoteState.ExpiresAt;
            }
        }
    }
}
