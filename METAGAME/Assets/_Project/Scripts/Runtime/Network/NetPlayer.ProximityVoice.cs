using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    public sealed partial class NetPlayer
    {
        double voiceBudgetAt;
        float voiceBudget = 5;
        int serverVoiceSequence;
        ProximityVoicePlayback voicePlayback;
        public bool VoiceSpeaking => voicePlayback != null && voicePlayback.Speaking;
        public void SendProximityVoice(byte[] bytes, int sequence)
        { if (isLocalPlayer) CmdProximityVoice(bytes, sequence); }

        [Command(channel = Channels.Unreliable)]
        void CmdProximityVoice(byte[] bytes, int sequence)
        {
            if (IsAi || body == null || body.Downed || bytes == null || bytes.Length != VoiceRules.Frame || sequence <= serverVoiceSequence) return;
            double now = NetworkTime.time;
            voiceBudget = Mathf.Min(5, voiceBudget + (float)(now - voiceBudgetAt) * 30); voiceBudgetAt = now;
            if (voiceBudget < 1) return;
            voiceBudget--; serverVoiceSequence = sequence;
            foreach (var peer in AllServerPlayers)
                if (peer != null && peer != this && !peer.IsAi && peer.body != null && peer.connectionToClient != null
                    && VoiceRules.Audible(body.FootPosition, peer.body.FootPosition))
                    TargetProximityVoice(peer.connectionToClient, bytes, sequence);
        }
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetProximityVoice(NetworkConnectionToClient target, byte[] bytes, int sequence)
        {
            if (isLocalPlayer || bytes == null || bytes.Length != VoiceRules.Frame) return;
            if (voicePlayback == null)
            {
                var host = new GameObject("Proximity voice"); host.transform.SetParent(transform, false);
                voicePlayback = host.AddComponent<ProximityVoicePlayback>();
            }
            voicePlayback.Receive(this, bytes, sequence);
        }
    }
}
