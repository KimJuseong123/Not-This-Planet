using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Audio
{
    /// <summary>씬 소유의 로컬 2D BGM. 기존 곡은 대기 후 반복하고 상황별 곡은 공백 없이 재생한다.</summary>
    [DisallowMultipleComponent]
    public sealed class GameMusicPlayer : MonoBehaviour
    {
        AudioManager manager;
        AudioSource source;
        AudioSource outgoing;
        GameSettingsRuntime settings;
        bool playing;
        double restartAt;
        double transitionAt;
        float outgoingGain;
        float incomingGain;
        double cueTime;
        const float TransitionSeconds = .35f;
        public MusicCue Cue { get; private set; } = MusicCue.Default;

        public void Configure(AudioManager configuration)
        {
            manager = configuration;
            settings = GameSettingsRuntime.Ensure();
            if (source == null)
            {
                source = CreateSource();
                outgoing = CreateSource();
            }
            source.Stop();
            outgoing.Stop();
            outgoing.clip = null;
            source.loop = false;
            source.clip = manager != null ? manager.MusicClip : null;
            Cue = MusicCue.Default;
            playing = false;
            incomingGain = outgoingGain = 0f;
            restartAt = AudioSettings.dspTime;
            if (source.clip == null)
                Debug.LogWarning("[BGM] AudioManager에 Music Clip이 지정되지 않았습니다.", this);
        }

        AudioSource CreateSource()
        {
            var result = gameObject.AddComponent<AudioSource>();
            result.playOnAwake = false;
            result.spatialBlend = 0f;
            result.priority = 128;
            result.volume = 0f;
            return result;
        }

        /// <summary>같은 곡은 재시작하지 않는다. elapsedSeconds는 늦참을 포함한 공통 연출 시각이다.</summary>
        public void PlayCue(MusicCue cue, double elapsedSeconds = 0d)
        {
            if (source == null || manager == null) return;
            cueTime = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d : System.Math.Max(0d, elapsedSeconds);
            if (Cue == cue && playing) return;
            if (Cue == cue && cue == MusicCue.Default) return;

            if (Cue != cue)
            {
                outgoing.Stop();
                var previous = source;
                source = outgoing;
                outgoing = previous;
                outgoingGain = incomingGain;
                source.volume = 0f;
                incomingGain = 0f;
                Cue = cue;
            }
            transitionAt = AudioSettings.dspTime;
            source.clip = manager.GetMusicClip(cue);
            source.loop = cue == MusicCue.Assault || cue == MusicCue.Stage2 || cue == MusicCue.Practice;
            playing = cue != MusicCue.Default;
            restartAt = AudioSettings.dspTime;
            if (source.clip == null || source.clip.length <= 0f)
            {
                if (cue != MusicCue.Silence)
                    Debug.LogWarning($"[BGM] {cue} 클립이 지정되지 않았습니다.", this);
                return;
            }
            if (cue == MusicCue.Default) return;
            if (!source.loop && cueTime >= source.clip.length) return;
            source.time = (float)(cueTime % source.clip.length);
            source.Play();
        }

        void Update()
        {
            if (source == null || manager == null) return;
            // Listener pause freezes playback and must not be mistaken for the end of a song.
            if (AudioListener.pause) return;
            float userVolume = settings != null && settings.Current != null
                ? settings.Current.audio.musicVolume : 1f;
            float volume = manager.MusicBaseVolume * userVolume;
            float transition = Mathf.Clamp01((float)(AudioSettings.dspTime - transitionAt) / TransitionSeconds);
            if (outgoing != null && outgoing.clip != null)
            {
                outgoing.volume = volume * outgoingGain * (1f - transition);
                if (transition >= 1f) { outgoing.Stop(); outgoing.clip = null; }
            }
            if (source.clip == null) return;
            if (Cue != MusicCue.Default)
            {
                // 컷씬은 공유 시각을 따라 seek한다. 프레임마다 seek하여 스트리밍을 끊지 않는다.
                if (!source.loop)
                {
                    if (cueTime >= source.clip.length) { source.Stop(); source.volume = 0f; return; }
                    if (source.isPlaying && System.Math.Abs(source.time - cueTime) > .75d)
                        source.time = (float)cueTime;
                }
                incomingGain = transition;
                source.volume = volume * incomingGain;
                return;
            }
            if (!playing)
            {
                if (AudioSettings.dspTime < restartAt) return;
                source.volume = 0f;
                source.Play();
                playing = true;
            }
            else if (!source.isPlaying)
            {
                source.volume = 0f;
                playing = false;
                restartAt = AudioSettings.dspTime + manager.MusicRepeatDelaySeconds;
                return;
            }

            float elapsed = (float)source.timeSamples / source.clip.frequency;
            float duration = (float)source.clip.samples / source.clip.frequency;
            float fadeIn = manager.MusicFadeInSeconds <= 0f ? 1f
                : Mathf.Clamp01(elapsed / manager.MusicFadeInSeconds);
            float fadeOut = manager.MusicFadeOutSeconds <= 0f ? 1f
                : Mathf.Clamp01((duration - elapsed) / manager.MusicFadeOutSeconds);
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
            // Master volume is already applied by AudioListener; SFX gain must not affect BGM.
            incomingGain = envelope;
            source.volume = volume * envelope;
        }

        void OnDisable()
        {
            if (source != null) source.Stop();
            if (outgoing != null) { outgoing.Stop(); outgoing.clip = null; }
            playing = false;
            restartAt = AudioSettings.dspTime;
        }

        void OnDestroy()
        {
            if (source != null) Destroy(source);
            if (outgoing != null) Destroy(outgoing);
        }
    }
}
