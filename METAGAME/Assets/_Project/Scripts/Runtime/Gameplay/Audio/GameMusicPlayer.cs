using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Audio
{
    /// <summary>게임 씬에 속하는 로컬 2D BGM. 곡 양끝을 페이드하고 종료 후 쉬었다 재생한다.</summary>
    [DisallowMultipleComponent]
    public sealed class GameMusicPlayer : MonoBehaviour
    {
        AudioManager manager;
        AudioSource source;
        GameSettingsRuntime settings;
        bool playing;
        double restartAt;

        public void Configure(AudioManager configuration)
        {
            manager = configuration;
            settings = GameSettingsRuntime.Ensure();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.priority = 128;
                source.volume = 0f;
            }
            source.Stop();
            source.clip = manager != null ? manager.MusicClip : null;
            playing = false;
            restartAt = AudioSettings.dspTime;
            if (source.clip == null)
                Debug.LogWarning("[BGM] AudioManager에 Music Clip이 지정되지 않았습니다.", this);
        }

        void Update()
        {
            if (source == null || source.clip == null || manager == null) return;
            // Listener pause freezes playback and must not be mistaken for the end of a song.
            if (AudioListener.pause) return;
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
            float userVolume = settings != null && settings.Current != null
                ? settings.Current.audio.musicVolume : 1f;
            // Master volume is already applied by AudioListener; SFX gain must not affect BGM.
            source.volume = manager.MusicBaseVolume * userVolume * envelope;
        }

        void OnDisable()
        {
            if (source != null) source.Stop();
            playing = false;
            restartAt = AudioSettings.dspTime;
        }

        void OnDestroy()
        {
            if (source != null) Destroy(source);
        }
    }
}
