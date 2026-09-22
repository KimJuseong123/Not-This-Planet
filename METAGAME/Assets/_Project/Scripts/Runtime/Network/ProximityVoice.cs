using System;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Network
{
    public static class VoiceRules
    {
        public const int Rate = 16000, Frame = 640;
        public const float Range = 20f;
        public static bool Audible(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= Range * Range;
        // G.711 mu-law: independent 40 ms packets, no cross-packet decoder state.
        public static byte Encode(float value)
        {
            int sample = (int)(Mathf.Clamp(value, -1, 1) * 32767), sign = sample < 0 ? 128 : 0;
            sample = Math.Min(Math.Abs(sample), 32635) + 132;
            int exponent = 7;
            for (int mask = 16384; exponent > 0 && (sample & mask) == 0; mask >>= 1) exponent--;
            return (byte)~(sign | exponent << 4 | (sample >> (exponent + 3) & 15));
        }
        public static float Decode(byte value)
        {
            int v = (~value) & 255, sample = (((v & 15) << 3) + 132) << ((v >> 4) & 7);
            return ((v & 128) != 0 ? 132 - sample : sample - 132) / 32768f;
        }
    }

    /// <summary>Basic high-pass / adaptive downward expansion, not acoustic echo cancellation.</summary>
    public sealed class VoiceFilter
    {
        float previous, high, noise = .0015f, gain = 1f;
        int hangover;
        public bool Process(float[] frame, float sensitivity)
        {
            float sum = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                float input = frame[i]; high = .965f * (high + input - previous); previous = input;
                frame[i] = high; sum += high * high;
            }
            float rms = Mathf.Sqrt(sum / frame.Length);
            bool speech = rms > Mathf.Max(.003f, noise * 2.5f);
            if (!speech) noise = Mathf.Lerp(noise, Mathf.Min(rms, .012f), .03f);
            hangover = speech ? 6 : Math.Max(0, hangover - 1);
            float target = speech ? 1 : Mathf.Clamp01(rms / Mathf.Max(.003f, noise * 2.5f));
            target = Mathf.Max(.15f, target * target); // Do not hard-gate the AI recording.
            for (int i = 0; i < frame.Length; i++)
            { gain = Mathf.Lerp(gain, target, .015f); frame[i] = Mathf.Clamp(frame[i] * gain * sensitivity, -1, 1); }
            return hangover > 0 && sensitivity > 0;
        }
    }

    /// <summary>Single microphone owner shared by open voice and V recording. Main-thread callbacks only.</summary>
    public sealed class ProximityMicrophone : MonoBehaviour
    {
        public static ProximityMicrophone Current { get; private set; }
        public event Action<float[]> FrameReady;
        public bool Capturing => clip != null;
        public bool Speaking { get; private set; }
        public string Problem { get; private set; }
        AudioClip clip;
        string device;
        int read, sequence;
        float retryAt, lastPoll;
        float[] raw, mono = new float[VoiceRules.Frame];
        VoiceFilter filter = new VoiceFilter();
        GameSession session;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || Current != null) return;
            Current = new GameObject("Shared proximity microphone").AddComponent<ProximityMicrophone>();
            DontDestroyOnLoad(Current.gameObject);
        }
        void Update()
        {
            var local = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.GetComponent<NetPlayer>() : null;
            var now = GameSession.Instance;
            var settings = GameSettingsRuntime.Instance?.Current?.voice;
            bool allowed = Application.isFocused && local != null && now != null && now.LocalPlayer != null
                && !now.LocalPlayer.Downed && now.CursorLocked && !now.CinematicInputBlocked && !now.EndingActive
                && !(settings?.microphoneMuted ?? false)
                && (!(settings?.pushToTalk ?? false) || Input.GetKey(KeyCode.V));
            if (!allowed) { Stop(); return; }
            string wanted = settings?.microphoneDevice ?? "";
            if (clip != null && (session != now || wanted != device)) Stop();
            if (clip == null)
            {
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 3;
                try
                {
                    if (Microphone.devices.Length == 0) { Problem = "마이크 없음"; return; }
                    string selected = string.IsNullOrEmpty(wanted) || Array.IndexOf(Microphone.devices, wanted) < 0 ? null : wanted;
                    clip = Microphone.Start(selected, true, 2, VoiceRules.Rate);
                    if (clip == null) { Problem = "마이크 시작 실패"; return; }
                    device = wanted; session = now; read = 0; lastPoll = Time.unscaledTime;
                    raw = new float[(int)(clip.frequency * .04f) * clip.channels];
                    filter = new VoiceFilter(); Problem = null;
                }
                catch (Exception) { Stop(); Problem = "마이크 권한 확인 필요"; }
                return;
            }
            string active = string.IsNullOrEmpty(device) || Array.IndexOf(Microphone.devices, device) < 0 ? null : device;
            int position = Microphone.GetPosition(active), frames = raw.Length / clip.channels;
            if (position < 0) { Stop(); return; }
            if (Time.unscaledTime - lastPoll > .5f) { read = position; lastPoll = Time.unscaledTime; return; }
            lastPoll = Time.unscaledTime;
            int available = (position - read + clip.samples) % clip.samples;
            for (int processed = 0; available >= frames && processed < 5; processed++)
            {
                if (!clip.GetData(raw, read)) { Stop(); return; }
                read = (read + frames) % clip.samples; available -= frames;
                for (int i = 0; i < mono.Length; i++)
                {
                    int at = Math.Min(frames - 1, i * frames / mono.Length) * clip.channels;
                    float value = 0; for (int c = 0; c < clip.channels; c++) value += raw[at + c];
                    mono[i] = value / clip.channels;
                }
                Speaking = filter.Process(mono, settings?.microphoneSensitivity ?? 1f);
                FrameReady?.Invoke(mono);
                // V is never speech-gated. Normal chat uses VAD plus a 240 ms release tail.
                if (Speaking || Input.GetKey(KeyCode.V))
                {
                    var packet = new byte[mono.Length];
                    for (int i = 0; i < packet.Length; i++) packet[i] = VoiceRules.Encode(mono[i]);
                    local.SendProximityVoice(packet, ++sequence);
                }
            }
        }
        void Stop()
        {
            Speaking = false;
            if (clip == null) return;
            string active = string.IsNullOrEmpty(device) || Array.IndexOf(Microphone.devices, device) < 0 ? null : device;
            Microphone.End(active); Destroy(clip); clip = null; session = null;
        }
        void OnDisable() => Stop();
        void OnApplicationFocus(bool focused) { if (!focused) Stop(); }
        void OnDestroy() { Stop(); if (Current == this) Current = null; }
    }

    public sealed class ProximityVoicePlayback : MonoBehaviour
    {
        readonly Queue<float> queue = new Queue<float>(VoiceRules.Rate / 2);
        readonly object gate = new object();
        AudioSource source;
        AudioClip clip;
        NetPlayer owner;
        bool primed;
        int lastSequence;
        float lastPacket;
        public bool Speaking => Time.unscaledTime - lastPacket < .3f;
        public void Receive(NetPlayer player, byte[] bytes, int sequence)
        {
            if (source != null && sequence <= lastSequence) return;
            owner = player; lastSequence = sequence; lastPacket = Time.unscaledTime;
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>(); source.spatialBlend = 1;
                source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 3; source.maxDistance = VoiceRules.Range;
                source.dopplerLevel = 0; source.loop = true; source.playOnAwake = false;
                clip = AudioClip.Create("Remote voice (memory only)", VoiceRules.Rate, 1, VoiceRules.Rate, true, Read);
                source.clip = clip; source.Play();
            }
            lock (gate)
            {
                if (queue.Count > VoiceRules.Frame * 5) { queue.Clear(); primed = false; }
                foreach (byte b in bytes) queue.Enqueue(VoiceRules.Decode(b));
            }
        }
        void Read(float[] output)
        {
            lock (gate)
            {
                if (!primed && queue.Count >= VoiceRules.Frame * 2) primed = true;
                for (int i = 0; i < output.Length; i++) output[i] = primed && queue.Count > 0 ? queue.Dequeue() : 0;
                if (queue.Count == 0) primed = false;
            }
        }
        void Update()
        {
            if (source == null) return;
            var session = GameSession.Instance; var settings = GameSettingsRuntime.Instance?.Current;
            bool audible = owner != null && owner.VisualRig != null && session?.LocalPlayer != null
                && VoiceRules.Audible(owner.VisualRig.FootPosition, session.LocalPlayer.FootPosition)
                && !(settings?.voice?.mutedPlayerIds?.Contains(owner.userId.ToString()) ?? false);
            // Master volume is already applied by GameSettingsRuntime to AudioListener.
            source.volume = audible ? (settings?.audio?.voiceVolume ?? 1f) : 0;
            if (owner?.VisualRig != null) transform.position = owner.VisualRig.FootPosition + Vector3.up * 1.3f;
            if (!audible || Time.unscaledTime - lastPacket > .4f) lock (gate) { queue.Clear(); primed = false; }
        }
        void OnDestroy() { if (clip != null) Destroy(clip); }
    }
}
