using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Metagame.Gameplay.Audio
{
    public enum SoundLoopPolicy
    {
        BankDefault = 0,
        ForceOneShot = 1,
        ForceLoop = 2
    }

    public enum SoundConcurrencyPolicy
    {
        AllowOverlapUpToLimit = 0,
        IgnoreWhilePlaying = 1,
        RestartExisting = 2,
        StealOldestWhenFull = 3
    }

    [Serializable]
    public sealed class AudioManagerEventOverride
    {
        public SoundEventId eventId = SoundEventId.Count;
        public bool enabled = true;
        [Tooltip("지정하면 이 이벤트만 이 클립으로 교체한다. 비워 두면 Stage1SoundBank의 클립을 사용한다.")]
        public AudioClip clipOverride;
        [Tooltip("선택하면 본음과 함께 재생할 노이즈 레이어를 교체한다. 비워 두면 사운드뱅크의 노이즈를 사용한다.")]
        public AudioClip noiseClipOverride;
        [Range(0f, 2f)] public float volumeScale = 1f;
        [Range(0.1f, 3f)] public float pitchScale = 1f;
        [Min(0.1f)] public float maxDistanceScale = 1f;
        [Tooltip("AudioManager의 기본 페이드 인 시간에 곱하는 이벤트별 배율이다.")]
        [Min(0f)] public float fadeInScale = 1f;
        [Tooltip("AudioManager의 기본 페이드 아웃 시간에 곱하는 이벤트별 배율이다.")]
        [Min(0f)] public float fadeOutScale = 1f;
        [Tooltip("이 이벤트의 노이즈 레이어 음량에 곱하는 배율이다. 0이면 이 이벤트의 노이즈를 끈다.")]
        [Range(0f, 2f)] public float noiseVolumeScale = 1f;
        [Tooltip("이 이벤트의 노이즈 레이어 피치에 곱하는 배율이다.")]
        [Range(0.1f, 3f)] public float noisePitchScale = 1f;
        [Tooltip("관리되는 루프 상태에서 이 이벤트를 사운드뱅크 설정대로 재생할지, 원샷 또는 루프로 강제할지 정한다. 직접 호출되는 원샷 이벤트는 호출 방식이 우선된다.")]
        public SoundLoopPolicy loopPolicy = SoundLoopPolicy.BankDefault;
        [Tooltip("재생 중 같은 이벤트가 다시 발생했을 때의 처리 방식이다.")]
        public SoundConcurrencyPolicy concurrencyPolicy = SoundConcurrencyPolicy.AllowOverlapUpToLimit;
        [Tooltip("이 이벤트가 동시에 재생될 수 있는 최대 원샷 수이다.")]
        [Range(1, 32)] public int maxInstances = 8;
        [Tooltip("같은 이벤트를 다시 재생하기까지 필요한 최소 간격(초)이다. 0이면 제한하지 않는다.")]
        [Min(0f)] public float minRetriggerInterval;
    }

    /// <summary>
    /// 개발자가 Inspector에서 Stage 1 효과음의 공통 재생값과 이벤트별 보정값을
    /// 한 곳에서 조정하는 중앙 오디오 설정 자산이다.
    /// </summary>
    [CreateAssetMenu(menuName = "Metagame/Audio/Audio Manager", fileName = "Stage1AudioManager")]
    public sealed class AudioManager : ScriptableObject
    {
        [Header("클립 뱅크")]
        [SerializeField] Stage1SoundBank soundBank;
        [SerializeField] SoundStyle globalStyle = SoundStyle.Stylized;

        [Header("전체 재생")]
        [Range(0f, 2f)] [SerializeField] float masterVolume = 1f;
        [SerializeField] bool muted;
        [SerializeField] AudioMixerGroup outputMixerGroup;

        [Header("인게임 BGM")]
        [SerializeField] AudioClip musicClip;
        [Range(0f, 1f)] [SerializeField] float musicBaseVolume = 0.18f;
        [Min(0f)] [SerializeField] float musicFadeInSeconds = 4f;
        [Min(0f)] [SerializeField] float musicFadeOutSeconds = 6f;
        [Min(0f)] [SerializeField] float musicRepeatDelaySeconds = 5f;

        public AudioClip MusicClip => musicClip;
        public float MusicBaseVolume => Mathf.Clamp01(musicBaseVolume);
        public float MusicFadeInSeconds => Mathf.Max(0f, musicFadeInSeconds);
        public float MusicFadeOutSeconds => Mathf.Max(0f, musicFadeOutSeconds);
        public float MusicRepeatDelaySeconds => Mathf.Max(0f, musicRepeatDelaySeconds);

        [Header("공간음 기본값")]
        [Tooltip("모든 신규 효과음은 3D로 재생되며, 이 값은 청취 거리 감쇠 시작점이다.")]
        [Min(0.1f)] [SerializeField] float spatialMinDistance = 1.2f;

        [Header("페이드·노이즈 기본값")]
        [Tooltip("원샷과 루프가 시작할 때 적용되는 기본 페이드 인 시간(초)이다. 이벤트별 배율로 조정할 수 있다.")]
        [Min(0f)] [SerializeField] float defaultFadeInSeconds = 0.005f;
        [Tooltip("원샷과 루프가 끝날 때 적용되는 기본 페이드 아웃 시간(초)이다. 이벤트별 배율로 조정할 수 있다.")]
        [Min(0f)] [SerializeField] float defaultFadeOutSeconds = 0.04f;
        [Tooltip("모든 이벤트의 노이즈 레이어 음량 배율이다. 0이면 노이즈 레이어를 끈다.")]
        [Range(0f, 2f)] [SerializeField] float noiseMasterVolume = 1f;
        [Tooltip("모든 이벤트의 노이즈 레이어 피치 배율이다.")]
        [Range(0.1f, 3f)] [SerializeField] float noiseMasterPitch = 1f;

        [Header("이벤트별 보정")]
        [Tooltip("이벤트를 끄거나 클립·볼륨·피치·페이드·노이즈·루프·중복 재생 정책을 개별 조정한다. 클립을 비워 두면 뱅크의 클립을 사용한다.")]
        [SerializeField] List<AudioManagerEventOverride> eventOverrides =
            new List<AudioManagerEventOverride>();

        public Stage1SoundBank SoundBank => soundBank;
        public SoundStyle GlobalStyle => globalStyle;
        public float MasterVolume => muted ? 0f : Mathf.Clamp(masterVolume, 0f, 2f);
        public bool Muted => muted;
        public AudioMixerGroup OutputMixerGroup => outputMixerGroup;
        public float SpatialMinDistance => Mathf.Max(0.1f, spatialMinDistance);
        public float DefaultFadeInSeconds => Mathf.Max(0f, defaultFadeInSeconds);
        public float DefaultFadeOutSeconds => Mathf.Max(0f, defaultFadeOutSeconds);
        public IReadOnlyList<AudioManagerEventOverride> EventOverrides => eventOverrides;

        public AudioClip Resolve(SoundEventId eventId, out Stage1SoundSlot slot)
        {
            slot = null;
            var settings = GetOverride(eventId);
            if (settings != null && !settings.enabled) return null;
            if (soundBank == null) return null;

            slot = soundBank.GetSlot(eventId);
            if (slot == null) return null;
            return settings != null && settings.clipOverride != null
                ? settings.clipOverride
                : slot.Resolve(globalStyle);
        }

        public AudioClip ResolveNoise(SoundEventId eventId, out Stage1SoundSlot slot)
        {
            slot = null;
            var settings = GetOverride(eventId);
            if (settings != null && !settings.enabled) return null;
            if (soundBank == null) return null;

            slot = soundBank.GetSlot(eventId);
            if (slot == null) return null;
            return settings != null && settings.noiseClipOverride != null
                ? settings.noiseClipOverride
                : slot.noise;
        }

        public bool HasAllEventSlots() => soundBank != null && soundBank.HasAllEventSlots();

        public bool HasAllEventOverrides()
        {
            for (var i = 0; i < (int)SoundEventId.Count; i++)
                if (GetOverride((SoundEventId)i) == null) return false;
            return true;
        }

        public float VolumeScale(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? Mathf.Clamp(settings.volumeScale, 0f, 2f) : 1f;
        }

        public float PitchScale(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? Mathf.Clamp(settings.pitchScale, 0.1f, 3f) : 1f;
        }

        public float MaxDistanceScale(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? Mathf.Max(0.1f, settings.maxDistanceScale) : 1f;
        }

        public float FadeInSeconds(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            var scale = settings != null ? Mathf.Max(0f, settings.fadeInScale) : 1f;
            return DefaultFadeInSeconds * scale;
        }

        public float FadeOutSeconds(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            var scale = settings != null ? Mathf.Max(0f, settings.fadeOutScale) : 1f;
            return DefaultFadeOutSeconds * scale;
        }

        public float NoiseVolumeScale(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            var eventScale = settings != null ? Mathf.Clamp(settings.noiseVolumeScale, 0f, 2f) : 1f;
            return Mathf.Clamp(noiseMasterVolume * eventScale, 0f, 2f);
        }

        public float NoisePitchScale(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            var eventScale = settings != null ? Mathf.Clamp(settings.noisePitchScale, 0.1f, 3f) : 1f;
            return Mathf.Clamp(noiseMasterPitch * eventScale, 0.1f, 3f);
        }

        public SoundLoopPolicy LoopPolicy(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? settings.loopPolicy : SoundLoopPolicy.BankDefault;
        }

        public bool IsLooping(SoundEventId eventId, Stage1SoundSlot slot)
        {
            var policy = LoopPolicy(eventId);
            if (policy == SoundLoopPolicy.ForceLoop) return true;
            if (policy == SoundLoopPolicy.ForceOneShot) return false;
            return slot != null && slot.loop;
        }

        public SoundConcurrencyPolicy ConcurrencyPolicy(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null
                ? settings.concurrencyPolicy
                : SoundConcurrencyPolicy.AllowOverlapUpToLimit;
        }

        public int MaxInstances(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? Mathf.Clamp(settings.maxInstances, 1, 32) : 8;
        }

        public float MinRetriggerInterval(SoundEventId eventId)
        {
            var settings = GetOverride(eventId);
            return settings != null ? Mathf.Max(0f, settings.minRetriggerInterval) : 0f;
        }

        AudioManagerEventOverride GetOverride(SoundEventId eventId)
        {
            if (eventOverrides == null) return null;
            for (var i = 0; i < eventOverrides.Count; i++)
            {
                var settings = eventOverrides[i];
                if (settings != null && settings.eventId == eventId) return settings;
            }

            return null;
        }
    }
}
