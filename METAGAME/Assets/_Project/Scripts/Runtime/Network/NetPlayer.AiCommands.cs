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
        public static IReadOnlyList<NetPlayer> AllServerPlayers => ServerPlayers;
        readonly AiCompanionController aiFollow = new AiCompanionController();
        public string AiActivity => aiFollow.Status;
        public bool AiBusy => aiFollow.Busy;
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
        {
            if (!isServer || !IsAi || body == null) return false;
            if (action != "cancel" && AiHostSettings.ServerState != AiRoomState.Active) return false;
            if (action != "cancel" && (body.Downed || (body.IsRiding && !AiWorkTask.VehicleOrder(action) && action != "wait")
                || GameSession.Instance == null || GameSession.Instance.CinematicInputBlocked || GameSession.Instance.EndingActive)) return false;
            if (action == "dance" && (body.Doll?.Suit?.Locomotion == null
                || !body.Doll.Suit.Locomotion.HasDance(1) || NetworkTime.time < _nextDanceAllowed)) return false;
            bool applied = aiFollow.Apply(action, speaker, body, approach, destination);
            if (applied)
            {
                AiOrderRevision++;
                StopAiSocialPresentation();
                if (action == "cancel" || action == "wait") body.SetInput(default);
            }
            return applied;
        }

        public bool TryApplyAiPlan(AiCommandStep[] steps, PlayerRig speaker)
        {
            if (!AiCommandPlan.ValidSteps(steps)
                || !TryApplyAiOrder(steps[0].action, speaker, steps[0].approach, steps[0].destination)) return false;
            aiFollow.QueueAfterCurrent(steps, speaker);
            return true;
        }

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
