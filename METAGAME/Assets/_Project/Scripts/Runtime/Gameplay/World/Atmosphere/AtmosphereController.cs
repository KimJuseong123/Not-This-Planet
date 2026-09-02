using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Applies a reusable atmosphere profile without owning stage gameplay.
    /// It can be placed in any stage and is intentionally inert until Apply is called.
    /// </summary>
    public sealed class AtmosphereController : MonoBehaviour
    {
        public const string HazeEnabledId = "_MetagameAtmosphereHazeEnabled";
        public const string HazeColorId = "_MetagameAtmosphereHazeColor";
        public const string HazeDensityId = "_MetagameAtmosphereHazeDensity";
        public const string HazeStartId = "_MetagameAtmosphereHazeStart";
        public const string HazeEndId = "_MetagameAtmosphereHazeEnd";
        public const string GroundFogEnabledId = "_MetagameAtmosphereGroundFogEnabled";
        public const string GroundFogColorId = "_MetagameAtmosphereGroundFogColor";
        public const string GroundFogDensityId = "_MetagameAtmosphereGroundFogDensity";
        public const string GroundFogBaseHeightId = "_MetagameAtmosphereGroundFogBaseHeight";
        public const string GroundFogHeightFalloffId = "_MetagameAtmosphereGroundFogHeightFalloff";
        public const string GroundFogStartId = "_MetagameAtmosphereGroundFogStart";
        public const string GroundFogEndId = "_MetagameAtmosphereGroundFogEnd";
        public const string GroundFogNoiseScaleId = "_MetagameAtmosphereGroundFogNoiseScale";
        public const string GroundFogNoiseOffsetId = "_MetagameAtmosphereGroundFogNoiseOffset";
        public const string GroundFogNoiseStrengthId = "_MetagameAtmosphereGroundFogNoiseStrength";
        public const string MeteorIntensityId = "_MetagameAtmosphereMeteorShowerIntensity";

        [SerializeField] AtmosphereProfile startupProfile;
        [SerializeField] Camera targetCamera;
        [SerializeField] bool applyOnEnable = true;

        AtmosphereProfile activeProfile;
        Volume runtimeVolume;
        VolumeProfile runtimeVolumeProfile;
        Light primaryLight;
        Light fillLight;
        bool capturedRenderSettings;
        bool capturedCamera;
        RenderSettingsSnapshot renderSettingsSnapshot;
        CameraSnapshot cameraSnapshot;
        float alertIntensity;
        float meteorIntensity = 1f;
        Vector3 lastGroundFogCameraPosition;
        Vector2 groundFogNoiseOffset;
        bool groundFogCameraPositionCaptured;

        struct RenderSettingsSnapshot
        {
            public Material skybox;
            public AmbientMode ambientMode;
            public Color ambientSky;
            public Color ambientEquator;
            public Color ambientGround;
            public float ambientIntensity;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogDensity;
            public float fogStart;
            public float fogEnd;
            public Light sun;
        }

        struct CameraSnapshot
        {
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
        }

        void OnEnable()
        {
            if (Application.isPlaying && applyOnEnable && startupProfile != null)
                Apply(startupProfile);
        }

        void OnDisable()
        {
            if (Application.isPlaying)
                ResetToDefaults();
        }

        void LateUpdate()
        {
            if (activeProfile == null || targetCamera == null ||
                activeProfile.groundFog == null || !activeProfile.groundFog.enabled)
                return;

            Vector3 currentPosition = targetCamera.transform.position;
            if (!groundFogCameraPositionCaptured)
            {
                lastGroundFogCameraPosition = currentPosition;
                groundFogCameraPositionCaptured = true;
                return;
            }

            Vector3 movement = currentPosition - lastGroundFogCameraPosition;
            lastGroundFogCameraPosition = currentPosition;
            movement.y = 0f;
            if (movement.sqrMagnitude < 0.000001f) return;

            // The noise is allowed to drift only while the player/camera moves.
            // A stationary player therefore sees a stable fog boundary.
            groundFogNoiseOffset += activeProfile.groundFog.noiseSpeed * Time.deltaTime;
            Shader.SetGlobalVector(GroundFogNoiseOffsetId, groundFogNoiseOffset);
        }

        public void BindCamera(Camera camera)
        {
            targetCamera = camera;
            groundFogCameraPositionCaptured = false;
            if (Application.isPlaying && activeProfile != null)
                ApplyCamera(activeProfile);
        }

        public void Apply(AtmosphereProfile profile)
        {
            if (profile == null)
            {
                ResetToDefaults();
                return;
            }

            profile.Validate();
            CaptureRenderSettings();
            CaptureCamera();

            activeProfile = profile;
            ApplyLighting(profile.lighting);
            ApplyFog(profile.fog);
            ApplyHaze(profile.haze);
            ApplyGroundFog(profile.groundFog);
            ApplyVolume(profile.postProcessing);
            ApplyCamera(profile);
            ApplyVisualComponents(profile);
            ApplyMeteorIntensity();
        }

        public void SetAlertIntensity(float value)
        {
            alertIntensity = Mathf.Clamp01(value);
            if (activeProfile == null) return;

            // Keep the baseline fog distance and density stable. Meteor alerts may
            // affect the depth haze, post-processing, and meteor visuals, but they
            // must not make the world fog appear to move toward the player.
            ApplyHaze(activeProfile.haze);
            ApplyVolume(activeProfile.postProcessing);
            ApplyMeteorIntensity();
        }

        public void SetMeteorShowerIntensity(float value)
        {
            meteorIntensity = Mathf.Clamp01(value);
            ApplyMeteorIntensity();
        }

        public void ResetToDefaults()
        {
            if (capturedRenderSettings)
            {
                RenderSettings.skybox = renderSettingsSnapshot.skybox;
                RenderSettings.ambientMode = renderSettingsSnapshot.ambientMode;
                RenderSettings.ambientSkyColor = renderSettingsSnapshot.ambientSky;
                RenderSettings.ambientEquatorColor = renderSettingsSnapshot.ambientEquator;
                RenderSettings.ambientGroundColor = renderSettingsSnapshot.ambientGround;
                RenderSettings.ambientIntensity = renderSettingsSnapshot.ambientIntensity;
                RenderSettings.fog = renderSettingsSnapshot.fog;
                RenderSettings.fogMode = renderSettingsSnapshot.fogMode;
                RenderSettings.fogColor = renderSettingsSnapshot.fogColor;
                RenderSettings.fogDensity = renderSettingsSnapshot.fogDensity;
                RenderSettings.fogStartDistance = renderSettingsSnapshot.fogStart;
                RenderSettings.fogEndDistance = renderSettingsSnapshot.fogEnd;
                RenderSettings.sun = renderSettingsSnapshot.sun;
            }

            if (capturedCamera && targetCamera != null)
            {
                targetCamera.clearFlags = cameraSnapshot.clearFlags;
                targetCamera.backgroundColor = cameraSnapshot.backgroundColor;
            }

            Shader.SetGlobalFloat(HazeEnabledId, 0f);
            Shader.SetGlobalFloat(GroundFogEnabledId, 0f);
            Shader.SetGlobalVector(GroundFogNoiseOffsetId, Vector4.zero);
            Shader.SetGlobalFloat(MeteorIntensityId, 0f);
            if (runtimeVolume != null) runtimeVolume.enabled = false;

            var backdrop = GetComponentInChildren<AtmosphereBackdrop>();
            if (backdrop != null) backdrop.Clear();
            var shower = GetComponentInChildren<AtmosphereMeteorShower>();
            if (shower != null) shower.Clear();
            var dustGust = GetComponentInChildren<AtmosphereDustGust>();
            if (dustGust != null) dustGust.Clear();

            activeProfile = null;
            DestroyRuntimeObjects();
            capturedRenderSettings = false;
            capturedCamera = false;
            alertIntensity = 0f;
            meteorIntensity = 1f;
        }

        void CaptureRenderSettings()
        {
            if (capturedRenderSettings) return;
            renderSettingsSnapshot = new RenderSettingsSnapshot
            {
                skybox = RenderSettings.skybox,
                ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                sun = RenderSettings.sun
            };
            capturedRenderSettings = true;
        }

        void CaptureCamera()
        {
            if (capturedCamera || targetCamera == null) return;
            cameraSnapshot = new CameraSnapshot
            {
                clearFlags = targetCamera.clearFlags,
                backgroundColor = targetCamera.backgroundColor
            };
            capturedCamera = true;
        }

        void ApplyCamera(AtmosphereProfile profile)
        {
            if (targetCamera == null) return;
            targetCamera.clearFlags = CameraClearFlags.Skybox;
            targetCamera.backgroundColor = profile.sky.horizonColor;
        }

        void ApplyLighting(AtmosphereProfile.LightingSettings settings)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = settings.ambientSky;
            RenderSettings.ambientEquatorColor = settings.ambientEquator;
            RenderSettings.ambientGroundColor = settings.ambientGround;
            RenderSettings.ambientIntensity = settings.ambientIntensity;

            primaryLight = EnsureLight(primaryLight, "Atmosphere Primary Light");
            primaryLight.enabled = settings.primaryLightEnabled;
            primaryLight.type = LightType.Directional;
            primaryLight.color = settings.primaryLightColor;
            primaryLight.intensity = settings.primaryLightIntensity;
            primaryLight.shadows = settings.primaryLightEnabled ? LightShadows.Soft : LightShadows.None;
            primaryLight.shadowStrength = settings.primaryShadowStrength;
            primaryLight.transform.rotation = Quaternion.Euler(settings.primaryLightAngles);

            fillLight = EnsureLight(fillLight, "Atmosphere Fill Light");
            fillLight.enabled = settings.fillLightEnabled;
            fillLight.type = LightType.Directional;
            fillLight.color = settings.fillLightColor;
            fillLight.intensity = settings.fillLightIntensity;
            fillLight.shadows = LightShadows.None;
            fillLight.transform.rotation = Quaternion.Euler(settings.fillLightAngles);
            RenderSettings.sun = settings.primaryLightEnabled ? primaryLight : null;
        }

        void ApplyFog(AtmosphereProfile.FogSettings settings)
        {
            bool enabled = settings.enabled && (!Application.isMobilePlatform || GetEffectScale() > 0f);
            RenderSettings.fog = enabled;
            RenderSettings.fogMode = settings.mode;
            RenderSettings.fogColor = settings.color;
            RenderSettings.fogDensity = enabled ? settings.density : 0f;
            RenderSettings.fogStartDistance = settings.startDistance;
            RenderSettings.fogEndDistance = settings.endDistance;
        }

        void ApplyHaze(AtmosphereProfile.HazeSettings settings)
        {
            float effectScale = GetEffectScale();
            bool enabled = settings.enabled && settings.requiresDepth && effectScale > 0f;
            Shader.SetGlobalFloat(HazeEnabledId, enabled ? 1f : 0f);
            Shader.SetGlobalColor(HazeColorId, settings.color);
            Shader.SetGlobalFloat(HazeDensityId, settings.density * effectScale * (1f + alertIntensity * 0.5f));
            Shader.SetGlobalFloat(HazeStartId, settings.startDistance);
            Shader.SetGlobalFloat(HazeEndId, settings.endDistance);
        }

        void ApplyGroundFog(AtmosphereProfile.GroundFogSettings settings)
        {
            float effectScale = GetEffectScale();
            bool enabled = settings.enabled && effectScale > 0f;
            groundFogNoiseOffset = Vector2.zero;
            groundFogCameraPositionCaptured = false;
            Shader.SetGlobalFloat(GroundFogEnabledId, enabled ? 1f : 0f);
            Shader.SetGlobalColor(GroundFogColorId, settings.color);
            Shader.SetGlobalFloat(GroundFogDensityId, settings.density * effectScale);
            Shader.SetGlobalFloat(GroundFogBaseHeightId, settings.baseHeight);
            Shader.SetGlobalFloat(GroundFogHeightFalloffId, settings.heightFalloff);
            Shader.SetGlobalFloat(GroundFogStartId, settings.startDistance);
            Shader.SetGlobalFloat(GroundFogEndId, settings.endDistance);
            Shader.SetGlobalFloat(GroundFogNoiseScaleId, settings.noiseScale);
            Shader.SetGlobalVector(GroundFogNoiseOffsetId, Vector4.zero);
            Shader.SetGlobalFloat(GroundFogNoiseStrengthId, settings.noiseStrength);
        }

        void ApplyVolume(AtmosphereProfile.PostProcessingSettings settings)
        {
            EnsureRuntimeVolume();
            runtimeVolume.enabled = true;

            var tonemapping = GetOrAdd<Tonemapping>();
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = settings.tonemapping;

            var bloom = GetOrAdd<Bloom>();
            bool enableMobilePostEffects = !Application.isMobilePlatform || GetEffectScale() > 0f;
            bloom.active = settings.bloomEnabled && enableMobilePostEffects;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = settings.bloomThreshold;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = settings.bloomIntensity * GetEffectScale() * (1f + alertIntensity);
            bloom.tint.overrideState = true;
            bloom.tint.value = settings.bloomTint;

            var colorAdjustments = GetOrAdd<ColorAdjustments>();
            colorAdjustments.active = settings.colorAdjustmentsEnabled && enableMobilePostEffects;
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = settings.postExposure;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = settings.contrast;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = settings.colorFilter;

            var vignette = GetOrAdd<Vignette>();
            vignette.active = settings.vignetteEnabled && enableMobilePostEffects;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = settings.vignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = settings.vignetteSmoothness;
        }

        void ApplyMeteorIntensity()
        {
            Shader.SetGlobalFloat(MeteorIntensityId, activeProfile == null ? 0f : meteorIntensity * GetEffectScale());
            var shower = GetComponentInChildren<AtmosphereMeteorShower>();
            if (shower != null)
            {
                shower.SetIntensity(meteorIntensity);
                shower.SetAlertIntensity(alertIntensity);
            }
        }

        void ApplyVisualComponents(AtmosphereProfile profile)
        {
            var backdrop = GetComponentInChildren<AtmosphereBackdrop>();
            if (backdrop != null)
                backdrop.Apply(profile, targetCamera);

            var shower = GetComponentInChildren<AtmosphereMeteorShower>();
            if (shower != null)
            {
                shower.BindCamera(targetCamera);
                shower.Apply(profile);
            }

            var dustGust = GetComponentInChildren<AtmosphereDustGust>();
            if (dustGust == null) dustGust = gameObject.AddComponent<AtmosphereDustGust>();
            dustGust.BindCamera(targetCamera);
            dustGust.Apply(profile);
        }

        float GetEffectScale()
        {
            if (!Application.isMobilePlatform) return 1f;
            if (activeProfile == null || !activeProfile.enableOnMobile) return 0f;
            return Mathf.Clamp01(activeProfile.mobileEffectScale);
        }

        void EnsureRuntimeVolume()
        {
            if (runtimeVolume == null)
            {
                var go = new GameObject("Atmosphere Volume (Runtime)");
                go.transform.SetParent(transform, false);
                go.hideFlags = HideFlags.DontSave;
                runtimeVolume = go.AddComponent<Volume>();
                runtimeVolume.isGlobal = true;
                runtimeVolume.priority = 100f;
                runtimeVolume.weight = 1f;
            }

            if (runtimeVolumeProfile == null)
            {
                runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                runtimeVolumeProfile.name = "Atmosphere Runtime Volume";
                runtimeVolume.profile = runtimeVolumeProfile;
            }
        }

        T GetOrAdd<T>() where T : VolumeComponent
        {
            if (runtimeVolumeProfile.TryGet<T>(out var component)) return component;
            return runtimeVolumeProfile.Add<T>(true);
        }

        Light EnsureLight(Light existing, string lightName)
        {
            if (existing != null) return existing;
            var go = new GameObject(lightName);
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;
            return go.AddComponent<Light>();
        }

        void DestroyRuntimeObjects()
        {
            if (runtimeVolumeProfile != null)
            {
                DestroyRuntimeObject(runtimeVolumeProfile);
                runtimeVolumeProfile = null;
            }
            if (runtimeVolume != null)
            {
                DestroyRuntimeObject(runtimeVolume.gameObject);
                runtimeVolume = null;
            }
            if (primaryLight != null)
            {
                DestroyRuntimeObject(primaryLight.gameObject);
                primaryLight = null;
            }
            if (fillLight != null)
            {
                DestroyRuntimeObject(fillLight.gameObject);
                fillLight = null;
            }
        }

        static void DestroyRuntimeObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
