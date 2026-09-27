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
        /// <summary>버튼으로 고른 자리 (S15P21B202-738). 주변에 말하면 <see cref="AiTalkKeys.Anyone"/>.</summary>
        int hearingTarget = AiTalkKeys.Anyone;
        /// <summary>
        /// 듣기를 열 때 못 붙잡은 이유. 업로드를 거절할 때 <b>같은 말</b>을 다시 쓴다 —
        /// 안 그러면 "빨강 동료가 기절해 있어요" 가 곧바로 "주변에 명령을 들을 AI가
        /// 없어요" 로 덮인다.
        /// </summary>
        string hearingRefusal;
        double hearingDeadline;
        readonly AiVoiceTransfer voiceTransfer = new();
        uint voiceUploadId;
        GameSession localVoiceSession;
        /// <summary>
        /// 이 클라이언트가 연 듣기의 번호 (S15P21B202-738). 서버가 듣기를 거절할 때 이 번호를
        /// 같이 돌려준다 — 번호 없이 거절만 오면, 빨리 다시 누른 <b>다음</b> 녹음이 앞 녹음의
        /// 거절에 끊긴다.
        /// </summary>
        public uint AiListenId { get; private set; }

        /// <summary>서버가 마지막으로 거절한 듣기 번호와 그 이유. 녹음하는 쪽이 읽고 곧바로 멈춘다.</summary>
        public uint AiListenRefused { get; private set; }
        public string AiListenRefusal { get; private set; }

        /// <summary>
        /// 앞 명령을 아직 올리는 중인가 (S15P21B202-738 리뷰). 이때 새로 들으면
        /// <see cref="BeginAiListening"/> 이 전송을 비우고 서버가 받던 것을 버려서,
        /// <b>앞 명령이 아무 말 없이 사라진다.</b>
        /// </summary>
        public bool AiVoiceSending => voiceTransfer.Active;

        public void BeginAiListening(int target = AiTalkKeys.Anyone)
        { if (isLocalPlayer) { voiceTransfer.Clear(); CmdStartAiListening(target, ++AiListenId); } }
        public void CancelAiListening() { if (isLocalPlayer) { voiceTransfer.Clear(); CmdCancelAiListening(); } }
        [Command] void CmdCancelAiListening() { hearingGroup = null; hearingTarget = AiTalkKeys.Anyone; hearingRefusal = null; voiceUpload = null; }
        [Command] void CmdStartAiListening(int target, uint listen)
        {
            hearingGroup = null; voiceUpload = null; hearingTarget = AiTalkKeys.Anyone; hearingRefusal = null;
            // **쓰러져도 말은 할 수 있다** (S15P21B202-730). 전에는 여기서 그냥 돌아가
            // 아무 말도 안 남겼다 — 바로 아래 "주변에 AI 가 없어요" 는 말해 주는데
            // 기절만 조용했다. 그래서 마이크 고장과 구별되지 않았다.
            if (body == null || IsAi || AiHostSettings.ServerState != AiRoomState.Active) return;
            hearingDeadline = NetworkTime.time + AiVoicePacket.Seconds + 5;
            voiceSession = GameSession.Instance;
            // **버튼으로 골랐으면 그 한 명만 듣는다** (S15P21B202-738). 거리를 보지 않으므로
            // 멀리 있는 동료에게도 시킬 수 있다. 못 붙잡은 이유는 그 자리의 이름으로 돌려준다
            // — "주변에 AI가 없어요" 는 주변을 고르지 않은 사람에게 틀린 말이다.
            if (target != AiTalkKeys.Anyone)
            {
                hearingGroup = AiGroupOrders.CaptureSlot(target, body, out hearingRefusal);
                hearingTarget = target;
                if (hearingRefusal != null) TargetAiListenRefused(listen, hearingRefusal);
                return;
            }
            hearingGroup = AiGroupOrders.Capture(AiRecipients.All, null, body, VoiceRules.Range);
            if (hearingGroup.Count == 0) TargetAiListenRefused(listen, "주변에 명령을 들을 AI가 없어요");
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
            { RejectAiVoice(id, hearingRefusal ?? "주변에 명령을 들을 AI가 없어요"); return; }
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
                || AiLocalRuntime.Current == null || !AiLocalRuntime.Current.SendVoice(wav, this, hearingGroup, hearingTarget))
                ReportAiVoice("명령을 받지 못했어요. 잠시 후 다시 말해 주세요");
            hearingGroup = null; hearingTarget = AiTalkKeys.Anyone; hearingRefusal = null;
        }
        /// <summary>
        /// <b>듣기를 열자마자 거절됐다</b> — 녹음을 곧바로 멈추게 한다 (S15P21B202-738 리뷰).
        ///
        /// <para>전에는 <see cref="ReportAiVoice"/> 로 보냈는데, 녹음 중인 화면은 "듣고 있어요"
        /// 를 그리느라 그 말을 가렸다. 그래서 기절한 동료의 키를 누른 사람이 <b>8초를 다 말하고
        /// 나서야</b> 안 된다는 것을 알았다 — 기다리게 하고 결국 실패하는 것이 QA 에서 가장
        /// 화가 난다고 한 그것이다.</para>
        /// </summary>
        [TargetRpc] void TargetAiListenRefused(uint listen, string message)
        { AiListenRefused = listen; AiListenRefusal = message; }

        // ── 결과 배너 (S15P21B202-746) ───────────────────────────────────────────
        // 2026-09-26 QA: 결과 줄이 너무 길었다. 보여 줄 것은 둘 — 들은 말 · 그래서 한 명령 — 과 누가 받았는지(아이콘).
        public string AiVoiceHeard { get; private set; }
        public string AiVoiceCommand { get; private set; }
        public int AiVoiceSlots { get; private set; }
        public bool AiVoiceOk { get; private set; }
        public float AiVoiceOutcomeUntil { get; private set; }

        [Server] public void ReportAiVoiceOutcome(string heard, string command, int slots, bool ok)
        {
            if (connectionToClient != null) TargetAiVoiceOutcome(heard ?? "", command ?? "", slots, ok);
        }
        [TargetRpc] void TargetAiVoiceOutcome(string heard, string command, int slots, bool ok)
        {
            AiVoiceHeard = heard; AiVoiceCommand = command; AiVoiceSlots = slots; AiVoiceOk = ok;
            AiVoiceOutcomeUntil = Time.unscaledTime + 4f;
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
