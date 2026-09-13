using System;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Audio
{
    /// <summary>
    /// Stage 1의 효과음 런타임. 기본은 발생 위치를 기준으로 감쇠하는 3D 소리이며,
    /// 전역 연출과 로컬 UI 신호는 사운드 슬롯의 라우팅 값으로 2D 재생한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoundRuntime : MonoBehaviour
    {
        const int OneShotPoolSize = 32;
        const float RegistryRefreshSeconds = 0.5f;
        const float DefaultFadeInSeconds = 0.005f;
        const float DefaultFadeOutSeconds = 0.04f;
        const float ProgressWindow = 0.14f;

        readonly List<AudioSource> oneShotPool = new List<AudioSource>(OneShotPoolSize);
        readonly List<AudioSource> noisePool = new List<AudioSource>(OneShotPoolSize);
        readonly Dictionary<AudioSource, OneShotState> oneShotStates =
            new Dictionary<AudioSource, OneShotState>();
        readonly Dictionary<SoundEventId, float> lastOneShotTimes =
            new Dictionary<SoundEventId, float>();
        readonly Dictionary<TransportVehicle, LoopState> vehicleLoops =
            new Dictionary<TransportVehicle, LoopState>();
        readonly Dictionary<PlasmaWeldingTool, LoopState> toolLoops =
            new Dictionary<PlasmaWeldingTool, LoopState>();
        readonly Dictionary<PlasmaWeldingTool, float> weldProgressTimes =
            new Dictionary<PlasmaWeldingTool, float>();
        readonly Dictionary<PlasmaWeldingTool, float> cutProgressTimes =
            new Dictionary<PlasmaWeldingTool, float>();
        readonly List<TransportVehicle> vehicles = new List<TransportVehicle>();
        readonly List<PlasmaWeldingTool> tools = new List<PlasmaWeldingTool>();
        readonly List<TransportVehicle> deadVehicles = new List<TransportVehicle>();
        readonly List<PlasmaWeldingTool> deadTools = new List<PlasmaWeldingTool>();

        AudioManager audioManager;
        Stage1SoundBank bank;
        SoundStyle globalStyle = SoundStyle.Stylized;
        bool worldAuthority;
        bool networked;
        bool audioAvailable;
        float userSfxVolume = 1f;
        GameSettingsRuntime settingsRuntime;
        float registryRefresh;
        Transform audioRoot;
        int lastGrabSuccessFrame = -1;
        Grabbable lastGrabSuccess;
        int lastReleaseFrame = -1;
        Grabbable lastReleased;

        sealed class LoopState
        {
            public AudioSource source;
            public AudioSource noiseSource;
            public SoundEventId eventId = SoundEventId.Count;
            public SoundEventId suppressedEventId = SoundEventId.Count;
            public float volume;
            public float noiseVolume;
        }

        sealed class OneShotState
        {
            public AudioSource source;
            public AudioSource noiseSource;
            public SoundEventId eventId;
            public float baseTargetVolume;
            public float baseTargetNoiseVolume;
            public float targetVolume;
            public float targetNoiseVolume;
            public float fadeInSeconds;
            public float fadeOutSeconds;
            public float startedAt;
            public float duration;
        }

        public event Action<SoundEventId, Vector3, float> WorldSoundRequested;

        public void Configure(AudioManager manager, bool authority, bool networkedMode = false)
        {
            audioManager = manager;
            bank = manager != null ? manager.SoundBank : null;
            globalStyle = manager != null ? manager.GlobalStyle : SoundStyle.Stylized;
            worldAuthority = authority;
            networked = networkedMode;
            BindSettings();
            RefreshRegistries();
            ArmRigidbodies();
        }

        public void Configure(Stage1SoundBank soundBank, SoundStyle style, bool authority,
                              bool networkedMode = false)
        {
            audioManager = null;
            bank = soundBank;
            globalStyle = style;
            worldAuthority = authority;
            networked = networkedMode;
            BindSettings();
            RefreshRegistries();
            ArmRigidbodies();
        }

        public void SetWorldAuthority(bool authority) => worldAuthority = authority;

        public void SetNetworked(bool enabled) => networked = enabled;

        void BindSettings()
        {
            var current = GameSettingsRuntime.Instance;
            if (settingsRuntime == current)
            {
                if (current != null && current.Current != null)
                    ApplyUserAudio(current.Current);
                return;
            }

            if (settingsRuntime != null)
                settingsRuntime.Changed -= OnSettingsChanged;

            settingsRuntime = current;
            if (settingsRuntime == null) return;

            settingsRuntime.Changed += OnSettingsChanged;
            ApplyUserAudio(settingsRuntime.Current);
        }

        void OnSettingsChanged(GameSettingsData settings)
        {
            ApplyUserAudio(settings);
        }

        void ApplyUserAudio(GameSettingsData settings)
        {
            float previousVolume = userSfxVolume;
            userSfxVolume = settings != null && settings.audio != null
                ? Mathf.Clamp01(settings.audio.sfxVolume) : 1f;
            float volumeRatio = previousVolume > 0.0001f
                ? userSfxVolume / previousVolume : 0f;

            foreach (var state in oneShotStates.Values)
            {
                if (state == null) continue;
                state.targetVolume = state.baseTargetVolume * userSfxVolume;
                state.targetNoiseVolume = state.baseTargetNoiseVolume * userSfxVolume;
                if (state.source != null && state.source.isPlaying)
                {
                    float elapsed = Mathf.Max(0f, Time.unscaledTime - state.startedAt);
                    float envelope = FadeEnvelope(elapsed, state.duration,
                                                  state.fadeInSeconds, state.fadeOutSeconds);
                    state.source.volume = state.targetVolume * envelope;
                    if (state.noiseSource != null)
                        state.noiseSource.volume = state.targetNoiseVolume * envelope;
                }
            }

            foreach (var state in vehicleLoops.Values)
            {
                if (state == null) continue;
                state.volume = Mathf.Clamp(state.volume * volumeRatio, 0f, 2f);
                state.noiseVolume = Mathf.Clamp(state.noiseVolume * volumeRatio, 0f, 2f);
                ApplyLoopVolumes(state);
            }

            foreach (var state in toolLoops.Values)
            {
                if (state == null) continue;
                state.volume = Mathf.Clamp(state.volume * volumeRatio, 0f, 2f);
                state.noiseVolume = Mathf.Clamp(state.noiseVolume * volumeRatio, 0f, 2f);
                ApplyLoopVolumes(state);
            }
        }

        void OnEnable()
        {
            BindSettings();
            PlayerRig.AnyHandGrabAttempted += OnHandGrabAttempted;
            PlayerRig.AnyHandReleased += OnHandReleased;
            PlayerRig.AnyFootstep += OnFootstep;
            Grabbable.AnyGrabbed += OnGrabbed;
            PartClamp.AnyProgressed += OnClampProgressed;
            Meteor.AnyImpacted += OnMeteorImpacted;
            PartAssemblyLink.AnyWelded += OnLinkWelded;
            PartAssemblyLink.AnyReleased += OnLinkReleased;
            TransportVehicle.AnyBoarded += OnTransportBoarded;
            TransportVehicle.AnyDismounted += OnTransportDismounted;
            PlasmaWeldingTool.AnyWeldProgressed += OnWeldProgressed;
            PlasmaWeldingTool.AnyCutProgressed += OnCutProgressed;
        }

        void OnDisable()
        {
            if (settingsRuntime != null)
                settingsRuntime.Changed -= OnSettingsChanged;
            settingsRuntime = null;

            PlayerRig.AnyHandGrabAttempted -= OnHandGrabAttempted;
            PlayerRig.AnyHandReleased -= OnHandReleased;
            PlayerRig.AnyFootstep -= OnFootstep;
            Grabbable.AnyGrabbed -= OnGrabbed;
            PartClamp.AnyProgressed -= OnClampProgressed;
            Meteor.AnyImpacted -= OnMeteorImpacted;
            PartAssemblyLink.AnyWelded -= OnLinkWelded;
            PartAssemblyLink.AnyReleased -= OnLinkReleased;
            TransportVehicle.AnyBoarded -= OnTransportBoarded;
            TransportVehicle.AnyDismounted -= OnTransportDismounted;
            PlasmaWeldingTool.AnyWeldProgressed -= OnWeldProgressed;
            PlasmaWeldingTool.AnyCutProgressed -= OnCutProgressed;

            for (var i = 0; i < oneShotPool.Count; i++) StopOneShot(oneShotPool[i]);
            foreach (var state in vehicleLoops.Values) StopLoopSources(state);
            foreach (var state in toolLoops.Values) StopLoopSources(state);
            oneShotStates.Clear();
        }

        void Update()
        {
            if (bank == null) return;

            audioAvailable = !Application.isBatchMode
                             && FindAnyObjectByType<AudioListener>() != null;
            UpdateOneShots();

            registryRefresh -= Time.unscaledDeltaTime;
            if (registryRefresh <= 0f)
            {
                registryRefresh = RegistryRefreshSeconds;
                RefreshRegistries();
                ArmRigidbodies();
                CleanupDeadLoops();
            }

            for (var i = 0; i < vehicles.Count; i++)
                if (vehicles[i] != null) UpdateVehicleLoop(vehicles[i]);
            for (var i = 0; i < tools.Count; i++)
                if (tools[i] != null) UpdateToolLoop(tools[i]);
        }

        void RefreshRegistries()
        {
            vehicles.Clear();
            vehicles.AddRange(FindObjectsByType<TransportVehicle>(FindObjectsSortMode.None));
            tools.Clear();
            tools.AddRange(FindObjectsByType<PlasmaWeldingTool>(FindObjectsSortMode.None));
        }

        void ArmRigidbodies()
        {
            var bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null || body.GetComponent<SoundCollisionEmitter>() != null) continue;
                body.gameObject.AddComponent<SoundCollisionEmitter>().Configure(this);
            }
        }

        void CleanupDeadLoops()
        {
            deadVehicles.Clear();
            foreach (var pair in vehicleLoops)
                if (pair.Key == null) deadVehicles.Add(pair.Key);
            for (var i = 0; i < deadVehicles.Count; i++) vehicleLoops.Remove(deadVehicles[i]);

            deadTools.Clear();
            foreach (var pair in toolLoops)
                if (pair.Key == null) deadTools.Add(pair.Key);
            for (var i = 0; i < deadTools.Count; i++) toolLoops.Remove(deadTools[i]);
            for (var i = 0; i < deadTools.Count; i++)
            {
                weldProgressTimes.Remove(deadTools[i]);
                cutProgressTimes.Remove(deadTools[i]);
            }
        }

        public void EmitWorldOneShot(SoundEventId eventId, Vector3 position, float intensity = 1f)
        {
            if (!worldAuthority) return;
            PlaySpatial(eventId, position, intensity);
            WorldSoundRequested?.Invoke(eventId, position, Mathf.Clamp01(intensity));
        }

        public void ReceiveNetworkWorldEvent(SoundEventId eventId, Vector3 position, float intensity)
        {
            PlaySpatial(eventId, position, intensity);
        }

        public void EmitLocalOneShot(SoundEventId eventId, Vector3 position, float intensity = 1f)
        {
            PlaySpatial(eventId, position, intensity);
        }

        void PlaySpatial(SoundEventId eventId, Vector3 position, float intensity)
        {
            if (!CanCreateAudio()) return;

            var clip = Resolve(eventId, out var slot);
            if (clip == null || slot == null) return;
            CleanupFinishedOneShots();
            if (!TryPrepareOneShot(eventId)) return;

            var source = GetOneShotSource(oneShotPool, "OneShot");
            StopOneShot(source);
            source.transform.position = position;
            ConfigureSource(source, eventId, slot, Mathf.Clamp01(intensity), false);
            source.clip = clip;
            source.loop = false;

            var targetVolume = source.volume;
            var noiseClip = ResolveNoise(eventId, out _);
            AudioSource noiseSource = null;
            var targetNoiseVolume = 0f;
            if (noiseClip != null && slot.noiseVolume > 0f && NoiseVolumeScale(eventId) > 0f)
            {
                noiseSource = GetOneShotSource(noisePool, "Noise");
                StopOneShot(noiseSource);
                noiseSource.transform.position = position;
                ConfigureSource(noiseSource, eventId, slot, Mathf.Clamp01(intensity), false);
                noiseSource.clip = noiseClip;
                noiseSource.loop = true;
                noiseSource.pitch = Mathf.Clamp(noiseSource.pitch * slot.noisePitch *
                                                NoisePitchScale(eventId), 0.1f, 3f);
                targetNoiseVolume = Mathf.Clamp(targetVolume * slot.noiseVolume *
                                                 NoiseVolumeScale(eventId), 0f, 2f);
                noiseSource.volume = 0f;
                noiseSource.Play();
            }

            source.volume = 0f;
            source.Play();
            oneShotStates[source] = new OneShotState
            {
                source = source,
                noiseSource = noiseSource,
                eventId = eventId,
                baseTargetVolume = userSfxVolume > 0.0001f
                    ? targetVolume / userSfxVolume : 0f,
                baseTargetNoiseVolume = userSfxVolume > 0.0001f
                    ? targetNoiseVolume / userSfxVolume : 0f,
                targetVolume = targetVolume,
                targetNoiseVolume = targetNoiseVolume,
                fadeInSeconds = FadeInSeconds(eventId),
                fadeOutSeconds = FadeOutSeconds(eventId),
                startedAt = Time.unscaledTime,
                duration = clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch))
            };
            lastOneShotTimes[eventId] = Time.unscaledTime;
        }

        AudioSource GetOneShotSource(List<AudioSource> pool, string prefix)
        {
            for (var i = 0; i < pool.Count; i++)
                if (!pool[i].isPlaying) return pool[i];

            if (pool.Count < OneShotPoolSize)
            {
                var source = CreateSource(prefix + " " + pool.Count, null);
                pool.Add(source);
                return source;
            }

            return pool[0];
        }

        bool TryPrepareOneShot(SoundEventId eventId)
        {
            if (MinRetriggerInterval(eventId) > 0f &&
                lastOneShotTimes.TryGetValue(eventId, out var lastTime) &&
                Time.unscaledTime - lastTime < MinRetriggerInterval(eventId))
                return false;

            var activeCount = 0;
            AudioSource oldestSource = null;
            var oldestTime = float.MaxValue;
            for (var i = 0; i < oneShotPool.Count; i++)
            {
                var source = oneShotPool[i];
                if (source == null || !oneShotStates.TryGetValue(source, out var state) ||
                    state.eventId != eventId || !source.isPlaying) continue;

                activeCount++;
                if (state.startedAt < oldestTime)
                {
                    oldestTime = state.startedAt;
                    oldestSource = source;
                }
            }

            var policy = ConcurrencyPolicy(eventId);
            if (policy == SoundConcurrencyPolicy.IgnoreWhilePlaying && activeCount > 0)
                return false;

            if (policy == SoundConcurrencyPolicy.RestartExisting && activeCount > 0)
            {
                for (var i = 0; i < oneShotPool.Count; i++)
                {
                    var source = oneShotPool[i];
                    if (source != null && oneShotStates.TryGetValue(source, out var state) &&
                        state.eventId == eventId)
                        StopOneShot(source);
                }

                activeCount = 0;
            }

            if (activeCount >= MaxInstances(eventId))
            {
                if (policy != SoundConcurrencyPolicy.StealOldestWhenFull || oldestSource == null)
                    return false;
                StopOneShot(oldestSource);
            }

            return true;
        }

        void CleanupFinishedOneShots()
        {
            for (var i = 0; i < oneShotPool.Count; i++)
            {
                var source = oneShotPool[i];
                if (source != null && oneShotStates.ContainsKey(source) && !source.isPlaying)
                    StopOneShot(source);
            }
        }

        bool CanCreateAudio() => audioAvailable;

        AudioClip Resolve(SoundEventId eventId, out Stage1SoundSlot slot)
        {
            if (audioManager != null) return audioManager.Resolve(eventId, out slot);
            if (bank == null)
            {
                slot = null;
                return null;
            }

            return bank.Resolve(eventId, globalStyle, out slot);
        }

        AudioClip ResolveNoise(SoundEventId eventId, out Stage1SoundSlot slot)
        {
            if (audioManager != null) return audioManager.ResolveNoise(eventId, out slot);
            if (bank == null)
            {
                slot = null;
                return null;
            }

            slot = bank.GetSlot(eventId);
            return slot != null ? slot.noise : null;
        }

        float MasterVolume => (audioManager != null ? audioManager.MasterVolume : 1f)
                              * userSfxVolume;
        float VolumeScale(SoundEventId eventId) =>
            audioManager != null ? audioManager.VolumeScale(eventId) : 1f;
        float PitchScale(SoundEventId eventId) =>
            audioManager != null ? audioManager.PitchScale(eventId) : 1f;
        float MaxDistanceScale(SoundEventId eventId) =>
            audioManager != null ? audioManager.MaxDistanceScale(eventId) : 1f;
        float FadeInSeconds(SoundEventId eventId) =>
            audioManager != null ? audioManager.FadeInSeconds(eventId) : DefaultFadeInSeconds;
        float FadeOutSeconds(SoundEventId eventId) =>
            audioManager != null ? audioManager.FadeOutSeconds(eventId) : DefaultFadeOutSeconds;
        float NoiseVolumeScale(SoundEventId eventId) =>
            audioManager != null ? audioManager.NoiseVolumeScale(eventId) : 1f;
        float NoisePitchScale(SoundEventId eventId) =>
            audioManager != null ? audioManager.NoisePitchScale(eventId) : 1f;
        bool IsLooping(SoundEventId eventId, Stage1SoundSlot slot) =>
            audioManager != null ? audioManager.IsLooping(eventId, slot) : slot != null && slot.loop;
        SoundConcurrencyPolicy ConcurrencyPolicy(SoundEventId eventId) =>
            audioManager != null
                ? audioManager.ConcurrencyPolicy(eventId)
                : SoundConcurrencyPolicy.AllowOverlapUpToLimit;
        int MaxInstances(SoundEventId eventId) =>
            audioManager != null ? audioManager.MaxInstances(eventId) : OneShotPoolSize;
        float MinRetriggerInterval(SoundEventId eventId) =>
            audioManager != null ? audioManager.MinRetriggerInterval(eventId) : 0f;
        float SpatialMinDistance => audioManager != null ? audioManager.SpatialMinDistance : 1.2f;

        float GetSlotNoiseVolume(SoundEventId eventId)
        {
            var slot = bank != null ? bank.GetSlot(eventId) : null;
            return slot != null ? Mathf.Clamp(slot.noiseVolume, 0f, 2f) : 1f;
        }

        AudioSource CreateSource(string name, Transform parent)
        {
            if (audioRoot == null)
            {
                var root = new GameObject("Spatial SFX");
                root.transform.SetParent(transform, false);
                audioRoot = root.transform;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : audioRoot, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.2f;
            source.dopplerLevel = 0f;
            return source;
        }

        void ConfigureSource(AudioSource source, SoundEventId eventId, Stage1SoundSlot slot,
                             float intensity, bool loop)
        {
            source.loop = loop;
            source.volume = Mathf.Clamp(slot.volume * VolumeScale(eventId) *
                                        MasterVolume * intensity, 0f, 2f);
            var pitchScale = loop ? 1f : Mathf.Lerp(0.82f, 1.18f, Mathf.Clamp01(intensity));
            source.pitch = Mathf.Clamp(slot.pitch * PitchScale(eventId) * pitchScale, 0.1f, 3f);
            source.maxDistance = Mathf.Max(0.1f, slot.maxDistance * MaxDistanceScale(eventId));
            var spatial = slot.routing == SoundRouting.Spatial3D;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = spatial ? SpatialMinDistance : 1f;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = audioManager != null ? audioManager.OutputMixerGroup : null;
        }

        void UpdateVehicleLoop(TransportVehicle vehicle)
        {
            var desired = SoundEventId.Count;
            if (vehicle.IsReturning)
                desired = SoundEventId.TractorDescendLoop;
            else if (vehicle.IsOccupied && vehicle.HorizontalSpeed > 0.12f)
                desired = vehicle.Beam != null && vehicle.Beam.IsHolding
                    ? SoundEventId.TractorTowLoop
                    : SoundEventId.TractorMoveLoop;

            UpdateLoop(vehicleLoops, vehicle, desired, 0.72f);
        }

        void UpdateToolLoop(PlasmaWeldingTool tool)
        {
            var desired = SoundEventId.Count;
            if (tool.BeamActive)
            {
                if (tool.BeamMode == PlasmaBeamMode.Weld)
                {
                    if (!tool.BeamHasContact) desired = SoundEventId.WeldBeamAirLoop;
                    else if (tool.GrowingBead != null && tool.GrowingBead.HasLiveBridge)
                        desired = SoundEventId.WeldBridgeGrowLoop;
                    else if (weldProgressTimes.TryGetValue(tool, out var weldTime)
                             && Time.unscaledTime - weldTime <= ProgressWindow)
                        desired = SoundEventId.WeldBeadGrowLoop;
                    else desired = SoundEventId.WeldBeamContactLoop;
                }
                else if (tool.BeamMode == PlasmaBeamMode.Cut)
                {
                    if (!tool.BeamHasContact) desired = SoundEventId.CutBeamAirLoop;
                    else if (cutProgressTimes.TryGetValue(tool, out var cutTime)
                             && Time.unscaledTime - cutTime <= ProgressWindow)
                        desired = SoundEventId.WeldBeadRemoveLoop;
                    else desired = SoundEventId.CutBeamContactLoop;
                }
            }

            UpdateLoop(toolLoops, tool, desired, 0.62f);
        }

        void UpdateLoop<T>(Dictionary<T, LoopState> states, T target,
                           SoundEventId desired, float targetVolume) where T : Component
        {
            if (!states.TryGetValue(target, out var state))
            {
                state = new LoopState();
                states.Add(target, state);
            }

            if (!CanCreateAudio())
            {
                StopLoopSources(state);
                return;
            }

            if (state.source == null)
            {
                state.source = CreateSource("Loop " + target.name, target.transform);
                state.source.transform.localPosition = Vector3.zero;
            }

            if (desired != state.suppressedEventId) state.suppressedEventId = SoundEventId.Count;
            if (state.eventId == SoundEventId.Count)
            {
                if (desired != SoundEventId.Count && desired != state.suppressedEventId)
                    StartLoop(state, target, desired);
                return;
            }

            if (desired != state.eventId)
            {
                state.volume = MoveFade(state.volume, 0f, FadeOutSeconds(state.eventId));
                state.noiseVolume = MoveFade(state.noiseVolume, 0f,
                                              FadeOutSeconds(state.eventId));
                ApplyLoopVolumes(state);

                if (state.volume <= 0.001f && state.noiseVolume <= 0.001f)
                {
                    StopLoopSources(state);
                    if (desired != SoundEventId.Count) StartLoop(state, target, desired);
                }

                return;
            }

            if (state.source == null || !state.source.isPlaying)
            {
                StartLoop(state, target, desired);
                return;
            }

            var wanted = targetVolume * MasterVolume * VolumeScale(state.eventId);
            var noiseWanted = wanted * GetSlotNoiseVolume(state.eventId) *
                              NoiseVolumeScale(state.eventId);
            state.volume = MoveFade(state.volume, wanted,
                                    wanted >= state.volume
                                        ? FadeInSeconds(state.eventId)
                                        : FadeOutSeconds(state.eventId));
            state.noiseVolume = MoveFade(state.noiseVolume, noiseWanted,
                                          noiseWanted >= state.noiseVolume
                                              ? FadeInSeconds(state.eventId)
                                              : FadeOutSeconds(state.eventId));
            ApplyLoopVolumes(state);
        }

        void StartLoop(LoopState state, Component target, SoundEventId eventId)
        {
            var clip = Resolve(eventId, out var slot);
            if (clip == null || slot == null)
            {
                state.eventId = SoundEventId.Count;
                return;
            }

            if (!IsLooping(eventId, slot))
            {
                PlaySpatial(eventId, target.transform.position, 1f);
                state.eventId = SoundEventId.Count;
                state.suppressedEventId = eventId;
                return;
            }

            // AudioSource.loop keeps the internal seam continuous. Fade-out is only
            // applied when the owning gameplay state leaves this loop.
            ConfigureSource(state.source, eventId, slot, 1f, true);
            state.source.clip = clip;
            state.source.volume = 0f;
            state.source.Play();
            state.eventId = eventId;
            state.volume = 0f;
            state.noiseVolume = 0f;

            var noiseClip = ResolveNoise(eventId, out _);
            if (noiseClip != null && slot.noiseVolume > 0f && NoiseVolumeScale(eventId) > 0f)
            {
                if (state.noiseSource == null)
                {
                    state.noiseSource = CreateSource("Loop Noise " + target.name,
                                                     target.transform);
                    state.noiseSource.transform.localPosition = Vector3.zero;
                }

                ConfigureSource(state.noiseSource, eventId, slot, 1f, true);
                state.noiseSource.clip = noiseClip;
                state.noiseSource.pitch = Mathf.Clamp(state.noiseSource.pitch * slot.noisePitch *
                                                      NoisePitchScale(eventId), 0.1f, 3f);
                state.noiseSource.volume = 0f;
                state.noiseSource.Play();
            }
            else if (state.noiseSource != null)
            {
                state.noiseSource.Stop();
            }
        }

        void ApplyLoopVolumes(LoopState state)
        {
            if (state.source != null) state.source.volume = state.volume;
            if (state.noiseSource != null) state.noiseSource.volume = state.noiseVolume;
        }

        void StopLoopSources(LoopState state)
        {
            if (state.source != null)
            {
                state.source.Stop();
                state.source.volume = 0f;
            }

            if (state.noiseSource != null)
            {
                state.noiseSource.Stop();
                state.noiseSource.volume = 0f;
            }

            state.eventId = SoundEventId.Count;
            state.suppressedEventId = SoundEventId.Count;
            state.volume = 0f;
            state.noiseVolume = 0f;
        }

        void UpdateOneShots()
        {
            for (var i = 0; i < oneShotPool.Count; i++)
            {
                var source = oneShotPool[i];
                if (source == null || !oneShotStates.TryGetValue(source, out var state)) continue;
                if (!source.isPlaying)
                {
                    StopOneShot(source);
                    continue;
                }

                var elapsed = Mathf.Max(0f, Time.unscaledTime - state.startedAt);
                var envelope = FadeEnvelope(elapsed, state.duration,
                                             state.fadeInSeconds, state.fadeOutSeconds);
                source.volume = state.targetVolume * envelope;
                if (state.noiseSource != null)
                    state.noiseSource.volume = state.targetNoiseVolume * envelope;
            }
        }

        void StopOneShot(AudioSource source)
        {
            if (source == null) return;

            AudioSource ownerSource = null;
            OneShotState owner = null;
            foreach (var pair in oneShotStates)
            {
                if (pair.Key == source || (pair.Value != null && pair.Value.noiseSource == source))
                {
                    ownerSource = pair.Key;
                    owner = pair.Value;
                    break;
                }
            }

            if (ownerSource != null)
            {
                ownerSource.Stop();
                if (owner != null && owner.noiseSource != null) owner.noiseSource.Stop();
                oneShotStates.Remove(ownerSource);
                return;
            }

            source.Stop();
        }

        static float FadeEnvelope(float elapsed, float duration,
                                  float fadeInSeconds, float fadeOutSeconds)
        {
            var envelope = 1f;
            if (fadeInSeconds > 0f)
                envelope = Mathf.Min(envelope, Mathf.Clamp01(elapsed / fadeInSeconds));

            if (fadeOutSeconds > 0f)
            {
                var fadeStart = Mathf.Max(0f, duration - fadeOutSeconds);
                if (elapsed >= fadeStart)
                    envelope = Mathf.Min(envelope,
                        Mathf.Clamp01((duration - elapsed) / fadeOutSeconds));
            }

            return Mathf.Clamp01(envelope);
        }

        static float MoveFade(float current, float target, float seconds)
        {
            if (seconds <= 0f) return target;
            var step = Time.unscaledDeltaTime / seconds *
                       Mathf.Max(1f, Mathf.Abs(target - current));
            return Mathf.MoveTowards(current, target, step);
        }

        void OnHandGrabAttempted(PlayerRig rig, Hand hand, Grabbable target)
        {
            if (rig == null || !rig.IsLocal || hand == null) return;
            EmitLocalOneShot(SoundEventId.HandGrabAttempt,
                hand.transform.position, 0.8f);
        }

        void OnFootstep(PlayerRig rig, Vector3 position, float intensity)
        {
            if (rig == null) return;

            // 네트워크에서는 서버가 물리 보행을 담당한다. 클라이언트가 즉시 재생하면
            // 서버가 보낸 같은 발걸음과 겹치므로, 네트워크 모드에서는 권한 이벤트만 쓴다.
            if (networked)
            {
                if (worldAuthority)
                    EmitWorldOneShot(SoundEventId.Footstep, position, intensity);
                return;
            }

            EmitLocalOneShot(SoundEventId.Footstep, position, intensity);
        }

        void OnGrabbed(Grabbable target, Hand hand)
        {
            var rig = hand != null ? hand.GetComponentInParent<PlayerRig>() : null;
            if (rig == null || !rig.IsLocal || target == null) return;
            if (lastGrabSuccess == target && lastGrabSuccessFrame == Time.frameCount) return;
            lastGrabSuccess = target;
            lastGrabSuccessFrame = Time.frameCount;
            EmitLocalOneShot(SoundEventId.HandGrabSuccess, target.transform.position, 0.9f);
        }

        void OnHandReleased(PlayerRig rig, Hand hand, Grabbable target,
                            PlayerRig.HandReleaseReason reason)
        {
            if (rig == null || !rig.IsLocal || target == null) return;
            if (lastReleased == target && lastReleaseFrame == Time.frameCount) return;
            lastReleased = target;
            lastReleaseFrame = Time.frameCount;

            var eventId = reason == PlayerRig.HandReleaseReason.Throw
                ? SoundEventId.HandThrow : SoundEventId.HandRelease;
            EmitLocalOneShot(eventId, target.transform.position, 0.9f);
        }

        void OnClampProgressed(PartClamp clamp, int bucket)
        {
            if (clamp == null || bucket <= 0) return;
            EmitWorldOneShot(SoundEventId.ClampPullProgress, clamp.transform.position, 0.72f);
        }

        void OnMeteorImpacted(Meteor meteor, Vector3 point)
        {
            var eventId = meteor != null && meteor.Size == MeteorSize.Medium
                ? SoundEventId.MeteorImpactMedium : SoundEventId.MeteorImpactSmall;
            EmitWorldOneShot(eventId, point, meteor != null && meteor.Size == MeteorSize.Medium ? 1f : 0.72f);
        }

        void OnLinkWelded(PartAssemblyLink link)
        {
            if (link != null) EmitWorldOneShot(SoundEventId.WeldConfirm, link.transform.position, 0.9f);
        }

        void OnLinkReleased(PartAssemblyLink link)
        {
            if (link != null) EmitWorldOneShot(SoundEventId.UnweldConfirm, link.transform.position, 0.9f);
        }

        void OnTransportBoarded(TransportVehicle vehicle, PlayerRig player)
        {
            if (vehicle != null) EmitWorldOneShot(SoundEventId.TractorBoard, vehicle.transform.position, 0.8f);
        }

        void OnTransportDismounted(TransportVehicle vehicle, PlayerRig player)
        {
            if (vehicle != null) EmitWorldOneShot(SoundEventId.TractorDismount, vehicle.transform.position, 0.9f);
        }

        void OnWeldProgressed(PlasmaWeldingTool tool)
        {
            if (tool != null) weldProgressTimes[tool] = Time.unscaledTime;
        }

        void OnCutProgressed(PlasmaWeldingTool tool)
        {
            if (tool != null) cutProgressTimes[tool] = Time.unscaledTime;
        }
    }
}
