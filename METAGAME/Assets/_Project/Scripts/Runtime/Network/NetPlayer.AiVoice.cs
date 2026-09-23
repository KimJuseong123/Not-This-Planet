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
        readonly AiVoiceTransfer voiceTransfer = new();
        uint voiceUploadId;
        GameSession localVoiceSession;
        public void BeginAiListening() { if (isLocalPlayer) { voiceTransfer.Clear(); CmdStartAiListening(); } }
        public void CancelAiListening() { if (isLocalPlayer) { voiceTransfer.Clear(); CmdCancelAiListening(); } }
        [Command] void CmdCancelAiListening() { hearingGroup = null; voiceUpload = null; }
        [Command] void CmdStartAiListening()
        {
            hearingGroup = null; voiceUpload = null;
            // **쓰러져도 말은 할 수 있다** (S15P21B202-730). 전에는 여기서 그냥 돌아가
            // 아무 말도 안 남겼다 — 바로 아래 "주변에 AI 가 없어요" 는 말해 주는데
            // 기절만 조용했다. 그래서 마이크 고장과 구별되지 않았다.
            if (body == null || IsAi || AiHostSettings.ServerState != AiRoomState.Active) return;
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
            localVoiceSession = GameSession.Instance;
            var id = voiceTransfer.Begin(wav, Time.unscaledTimeAsDouble);
            CmdBeginAiVoice(id, wav.Length);
        }

        void TickAiVoiceTransfer()
        {
            if (isServer && voiceUpload != null && NetworkTime.time >= voiceDeadline)
            {
                voiceUpload = null;
                RejectAiVoice(voiceUploadId, "음성 전송 시간이 초과됐어요. 다시 말해 주세요");
            }
            if (!isLocalPlayer || !voiceTransfer.Active) return;
            if (GameSession.Instance != localVoiceSession || localVoiceSession == null
                || localVoiceSession.CinematicInputBlocked || localVoiceSession.EndingActive || body == null)
            { CancelAiListening(); return; }
            if (voiceTransfer.Expire(Time.unscaledTimeAsDouble))
            {
                CmdCancelAiListening();
                AiVoiceMessage = "음성 전송 시간이 초과됐어요. 다시 말해 주세요";
                AiVoiceMessageUntil = Time.unscaledTime + 4;
                return;
            }
            if (!voiceTransfer.TryNext(Time.unscaledTimeAsDouble, out var chunk, out var last)) return;
            CmdAiVoiceChunk(voiceTransfer.Id, chunk);
            if (last) CmdEndAiVoice(voiceTransfer.Id);
        }
        // Default Mirror authority requires the sending connection to own this player.
        [Command] void CmdBeginAiVoice(uint id, int length)
        {
            voiceUpload = null; voiceReceived = 0;
            if (NetworkTime.time < nextVoiceAt) { RejectAiVoice(id, "잠시 후 다시 말해 주세요"); return; }
            nextVoiceAt = NetworkTime.time + 2;
            // 여기도 기절을 뺀다 (S15P21B202-730). 듣기는 열어 놓고 업로드만 막으면
            // "지금은 AI 명령을 사용할 수 없어요" 로 끝나 **살려 달라는 말을 못 한다.**
            if (length < 44 || length > AiVoicePacket.MaxBytes || IsAi || body == null
                || GameSession.Instance == null || GameSession.Instance.CinematicInputBlocked
                || AiHostSettings.ServerState != AiRoomState.Active) { RejectAiVoice(id, "지금은 AI 명령을 사용할 수 없어요"); return; }
            if (AiLocalRuntime.Current == null || AiLocalRuntime.Current.Busy)
            { RejectAiVoice(id, "다른 명령을 처리 중이에요. 잠시 후 말해 주세요"); return; }
            if (hearingGroup == null || hearingGroup.Count == 0 || NetworkTime.time > hearingDeadline || voiceSession != GameSession.Instance)
            { RejectAiVoice(id, "주변에 명령을 들을 AI가 없어요"); return; }
            voiceUpload = new byte[length]; voiceDeadline = NetworkTime.time + 12;
            voiceSession = GameSession.Instance;
            voiceUploadId = id;
            TargetAiVoiceAccepted(id, true);
        }
        void RejectAiVoice(uint id, string message)
        { TargetAiVoiceAccepted(id, false); ReportAiVoice(message); }

        [TargetRpc] void TargetAiVoiceAccepted(uint id, bool accepted)
            => voiceTransfer.Approve(id, accepted, Time.unscaledTimeAsDouble);

        [Command] void CmdAiVoiceChunk(uint id, ArraySegment<byte> chunk)
        {
            if (voiceUpload == null || id != voiceUploadId) return;
            if (NetworkTime.time > voiceDeadline || chunk.Array == null || chunk.Count == 0 || chunk.Count > AiVoiceTransfer.ChunkBytes
                || voiceReceived + chunk.Count > voiceUpload.Length)
            { voiceUpload = null; RejectAiVoice(id, "음성 전송이 중단됐어요. 다시 말해 주세요"); return; }
            Buffer.BlockCopy(chunk.Array, chunk.Offset, voiceUpload, voiceReceived, chunk.Count); voiceReceived += chunk.Count;
        }
        [Command] void CmdEndAiVoice(uint id)
        {
            if (id != voiceUploadId) return;
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
        public static byte[] Encode(System.Collections.Generic.IReadOnlyList<float> samples, int rate, int channels, float gain = 1f)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (rate <= 0 || channels <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            int count = (int)Math.Min(Rate * Seconds, (long)(samples.Count / channels) * Rate / rate);
            var bytes = new byte[44 + count * 2];
            using (var stream = new System.IO.MemoryStream(bytes))
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write(0x46464952); writer.Write(36 + count * 2); writer.Write(0x45564157);
                writer.Write(0x20746d66); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(0x61746164); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    int at = Math.Min(samples.Count / channels - 1, (int)((long)i * rate / Rate));
                    float sample = 0;
                    for (int c = 0; c < channels; c++) sample += samples[at * channels + c];
                    writer.Write((short)(Mathf.Clamp(sample / channels * gain, -1f, 1f) * 32767));
                }
                return bytes;
            }
        }
    }
}
