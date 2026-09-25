using System;
using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.Network
{
    public static class VoiceRules
    {
        public const int Rate = 16000, Frame = 640;

        /// <summary>
        /// <b>AI 가 누구 말을 듣는가</b>의 반경.
        ///
        /// <para>플레이어끼리의 근접 음성 채팅은 <c>S15P21B202-705</c> 에서 걷어냈지만 이 값은
        /// 남는다 — <see cref="NetPlayer.BeginAiListening"/> 이 명령을 받을 AI 를 고를 때 그대로
        /// 쓴다. "가까이 있는 동료에게 말한다" 는 규칙 자체는 없어지지 않았다.</para>
        /// </summary>
        public const float Range = 20f;
        public static bool Audible(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= Range * Range;
    }

    /// <summary>Basic high-pass / adaptive downward expansion, not acoustic echo cancellation.</summary>
    public sealed class VoiceFilter
    {
        float previous, high, noise = .0015f, gain = 1f;
        int hangover;

        /// <summary>
        /// 방금 프레임의 크기와 <b>바닥 잡음</b> (S15P21B202-735).
        ///
        /// <para>둘 다 <see cref="Process"/> 가 <b>이미 계산하고 있었다</b> — 밖으로
        /// 내보내지 않았을 뿐이다. 화면에서 다시 재면 같은 판단을 두 곳이 각자 하게 되어
        /// 어긋난다.</para>
        ///
        /// <para><b>잡음까지 내보내는 이유.</b> 크기만 보여 주면 "이 정도면 큰 건가" 를
        /// 알 수 없다. 시끄러운 방에서 필요한 것은 <b>내 목소리가 잡음 위에 있는가</b> 이고,
        /// 그 선이 곧 <see cref="Process"/> 가 말/침묵을 가르는 자리다.</para>
        /// </summary>
        public float Level { get; private set; }
        public float NoiseFloor => Mathf.Max(.003f, noise * 2.5f);

        public bool Process(float[] frame, float sensitivity)
        {
            float sum = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                float input = frame[i]; high = .965f * (high + input - previous); previous = input;
                frame[i] = high; sum += high * high;
            }
            float rms = Mathf.Sqrt(sum / frame.Length);
            Level = rms;
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

    /// <summary>
    /// <b>V 를 누르는 동안에만</b> 열리는 마이크. 잡은 소리가 갈 곳은 AI 명령뿐이다.
    /// Main-thread callbacks only.
    ///
    /// <para><b>왜 상시 열어 두지 않는가</b> (S15P21B202-705). 예전에는 오픈 마이크로 늘 열려
    /// 있으면서 프레임마다 근접 음성 패킷을 올렸고, <b>호스트가 그것을 받아 들리는 사람 수만큼
    /// 되뿌렸다</b> — 사람 수의 제곱으로 늘어 호스트만 렉이 심했다. 같은 PC 에서 AI 모델이
    /// CPU 로 도는 판에서는 감당할 수 없었다. 플레이어끼리 말하는 길을 걷어내고 나면 마이크를
    /// 늘 열어 둘 이유도 없다 — OS 의 마이크 표시까지 꺼진다.</para>
    /// </summary>
    public sealed class ProximityMicrophone : MonoBehaviour
    {
        /// <summary>
        /// V 를 놓은 뒤 <b>이만큼 더 받는다</b> (S15P21B202-705).
        ///
        /// <para>상시로 열려 있을 때는 링 버퍼가 매 프레임 비워졌다. 놓는 순간 닫으면
        /// <b>아직 읽지 않은 마지막 조각</b>이 남은 채 버려진다 — AI 명령의 <b>끝 낱말이
        /// 잘린다.</b></para>
        ///
        /// <para><b>꼬리의 주인은 여기 하나다.</b> 보내는 쪽(<c>AiVoiceControls</c>)은 제 시계로
        /// 같은 값을 재지 않고 <see cref="Capturing"/> 이 꺼지기를 기다린다. 양쪽이 각자 재면
        /// 이쪽은 "마지막으로 눌린 프레임"에서 <c>unscaledTime</c> 으로, 저쪽은 "놓인 프레임"
        /// 에서 <c>realtimeSinceStartup</c> 으로 세게 되어 <b>늘 이쪽이 먼저 닫힌다</b> —
        /// 그 틈이 멀쩡한 녹음을 통째로 버리게 만들었다(<c>!596</c> AI 리뷰).</para>
        /// </summary>
        public const float ReleaseTail = .35f;
        public static ProximityMicrophone Current { get; private set; }
        public event Action<float[]> FrameReady;

        /// <summary>
        /// 장치가 <b>실제로 열려 있는가</b>. 3초 물러서기(<c>retryAt</c>) 중에는 V 를 눌러도
        /// 열리지 않는데, 그 사이의 녹음은 <b>한 조각도 못 받고 끝난다</b> — 부르는 쪽이
        /// 이것을 보지 않으면 "조금 더 길게 말해 주세요" 라는 엉뚱한 안내가 나간다.
        /// </summary>
        public bool Capturing => clip != null;
        public bool Speaking { get; private set; }

        /// <summary>
        /// 지금 들어오는 소리의 크기와 <b>말로 치는 문턱</b> (S15P21B202-735).
        ///
        /// <para>화면이 막대를 그리는 데 쓴다. 값은 <see cref="VoiceFilter"/> 가 이미
        /// 재고 있던 것이고, 문턱은 그 필터가 <b>말/침묵을 가르는 바로 그 선</b>이다 —
        /// 다른 숫자를 보여 주면 "막대는 찼는데 왜 안 들었지" 가 된다.</para>
        ///
        /// <para>마이크가 닫혀 있으면 0 이다 — V 를 안 누르는 동안 막대가 움직이면
        /// <b>늘 듣고 있는 것처럼 보인다.</b></para>
        /// </summary>
        public float Level => Capturing ? filter.Level : 0f;
        public float SpeechFloor => filter.NoiseFloor;
        public string Problem { get; private set; }
        AudioClip clip;
        string device;
        int read;
        float retryAt, lastPoll, holdUntil;
        float[] raw, mono = new float[VoiceRules.Frame];
        VoiceFilter filter = new VoiceFilter();
        GameSession session;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || Current != null) return;
            Current = new GameObject("AI command microphone (V)").AddComponent<ProximityMicrophone>();
            DontDestroyOnLoad(Current.gameObject);
        }
        void Update()
        {
            var local = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.GetComponent<NetPlayer>() : null;
            var now = GameSession.Instance;
            var settings = GameSettingsRuntime.Instance?.Current?.voice;
            // 마이크는 V 를 누르는 동안(+ 꼬리)에만 열린다. 고를 수 있는 설정이 아니다 —
            // 오픈 마이크로 갈 곳이 없어졌기 때문이다 (S15P21B202-705).
            if (Input.GetKey(KeyCode.V)) holdUntil = Time.unscaledTime + ReleaseTail;
            bool allowed = Application.isFocused && local != null && now != null && now.LocalPlayer != null
                && !now.LocalPlayer.Downed && now.CursorLocked && !now.CinematicInputBlocked && !now.EndingActive
                && !(settings?.microphoneMuted ?? false)
                && Time.unscaledTime < holdUntil;
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
                // 잡은 소리는 AI 명령으로만 간다. 플레이어끼리 보내는 길은 없다 (S15P21B202-705).
                FrameReady?.Invoke(mono);
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
}
