using System;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    public sealed partial class NetPlayer
    {
        byte[] voiceUpload;
        int voiceReceived;
        double voiceDeadline, nextVoiceAt;
        GameSession voiceSession;
        AiGroupOrders hearingGroup;
        double hearingDeadline;
        public void BeginAiListening() { if (isLocalPlayer) CmdStartAiListening(); }
        public void CancelAiListening() { if (isLocalPlayer) CmdCancelAiListening(); }
        [Command] void CmdCancelAiListening() { hearingGroup = null; voiceUpload = null; }
        [Command] void CmdStartAiListening()
        {
            hearingGroup = null;
            if (body == null || body.Downed || IsAi || AiHostSettings.ServerState != AiRoomState.Active) return;
            hearingGroup = AiGroupOrders.Capture(AiRecipients.All, null, body, VoiceRules.Range);
            hearingDeadline = NetworkTime.time + AiVoicePacket.Seconds + 5;
            voiceSession = GameSession.Instance;
            if (hearingGroup.Count == 0) ReportAiVoice("주변에 명령을 들을 AI가 없어요");
        }
        public string AiVoiceMessage { get; private set; }
        public float AiVoiceMessageUntil { get; private set; }

        public void SubmitAiVoice(byte[] wav)
        {
            if (!isLocalPlayer || !AiVoicePacket.Valid(wav)) return;
            CmdBeginAiVoice(wav.Length);
            for (int offset = 0; offset < wav.Length; offset += 8192)
            {
                var chunk = new byte[Math.Min(8192, wav.Length - offset)];
                Buffer.BlockCopy(wav, offset, chunk, 0, chunk.Length);
                CmdAiVoiceChunk(chunk);
            }
            CmdEndAiVoice();
        }
        // Default Mirror authority requires the sending connection to own this player.
        [Command] void CmdBeginAiVoice(int length)
        {
            voiceUpload = null; voiceReceived = 0;
            if (NetworkTime.time < nextVoiceAt) return;
            nextVoiceAt = NetworkTime.time + 2;
            if (length < 44 || length > AiVoicePacket.MaxBytes || IsAi || body == null || body.Downed
                || GameSession.Instance == null || GameSession.Instance.CinematicInputBlocked
                || AiHostSettings.ServerState != AiRoomState.Active) { ReportAiVoice("지금은 AI 명령을 사용할 수 없어요"); return; }
            if (AiLocalRuntime.Current == null || AiLocalRuntime.Current.Busy)
            { ReportAiVoice("다른 명령을 처리 중이에요. 잠시 후 말해 주세요"); return; }
            if (hearingGroup == null || hearingGroup.Count == 0 || NetworkTime.time > hearingDeadline || voiceSession != GameSession.Instance)
            { ReportAiVoice("주변에 명령을 들을 AI가 없어요"); return; }
            voiceUpload = new byte[length]; voiceDeadline = NetworkTime.time + 12;
            voiceSession = GameSession.Instance;
        }
        [Command] void CmdAiVoiceChunk(byte[] chunk)
        {
            if (voiceUpload == null) return;
            if (NetworkTime.time > voiceDeadline || chunk == null || chunk.Length == 0 || chunk.Length > 8192
                || voiceReceived + chunk.Length > voiceUpload.Length)
            { voiceUpload = null; return; }
            Buffer.BlockCopy(chunk, 0, voiceUpload, voiceReceived, chunk.Length); voiceReceived += chunk.Length;
        }
        [Command] void CmdEndAiVoice()
        {
            var wav = voiceUpload; voiceUpload = null;
            if (wav == null) return;
            if (voiceReceived != wav.Length || NetworkTime.time > voiceDeadline || voiceSession != GameSession.Instance
                || AiLocalRuntime.Current == null || !AiLocalRuntime.Current.SendVoice(wav, this, hearingGroup))
                ReportAiVoice("명령을 받지 못했어요. 잠시 후 다시 말해 주세요");
            hearingGroup = null;
        }
        [Server] public void ReportAiVoice(string message)
        {
            if (connectionToClient != null) TargetAiVoiceResult(message);
        }
        [TargetRpc] void TargetAiVoiceResult(string message)
        { AiVoiceMessage = message; AiVoiceMessageUntil = Time.unscaledTime + 4f; }
    }

    public static class AiVoicePacket
    {
        public const int Rate = 16000, Seconds = 8, MaxBytes = 44 + Rate * Seconds * 2;
        public static bool Valid(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 44 + Rate / 5 * 2 || bytes.Length > MaxBytes) return false;
            return BitConverter.ToUInt32(bytes, 0) == 0x46464952 && BitConverter.ToUInt32(bytes, 8) == 0x45564157
                && BitConverter.ToUInt32(bytes, 12) == 0x20746d66 && BitConverter.ToInt32(bytes, 16) == 16
                && BitConverter.ToInt16(bytes, 20) == 1 && BitConverter.ToInt16(bytes, 22) == 1
                && BitConverter.ToInt32(bytes, 24) == Rate && BitConverter.ToInt16(bytes, 34) == 16
                && BitConverter.ToUInt32(bytes, 36) == 0x61746164 && BitConverter.ToInt32(bytes, 40) == bytes.Length - 44;
        }
        public static byte[] Encode(float[] samples, int rate, int channels, float gain = 1f)
        {
            int count = Math.Min(Rate * Seconds, (int)((long)samples.Length * Rate / (rate * channels)));
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write(0x46464952); writer.Write(36 + count * 2); writer.Write(0x45564157);
                writer.Write(0x20746d66); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(0x61746164); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    int at = Math.Min(samples.Length / channels - 1, (int)((long)i * rate / Rate));
                    float sample = 0;
                    for (int c = 0; c < channels; c++) sample += samples[at * channels + c];
                    writer.Write((short)(Mathf.Clamp(sample / channels * gain, -1f, 1f) * 32767));
                }
                return stream.ToArray();
            }
        }
    }
}
