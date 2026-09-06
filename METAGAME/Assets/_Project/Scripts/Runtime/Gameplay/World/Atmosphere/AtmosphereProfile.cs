using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay
{
    /// <summary>
    /// A stage-independent visual description of a world atmosphere.
    /// Stage scenes own profile assets; this object never owns gameplay state.
    /// </summary>
    [CreateAssetMenu(fileName = "AtmosphereProfile", menuName = "Metagame/Rendering/Atmosphere Profile")]
    public sealed class AtmosphereProfile : ScriptableObject
    {
        [Serializable]
        public sealed class SkySettings
        {
            public Color zenithColor = new Color(0.015f, 0.017f, 0.032f, 1f);
            public Color horizonColor = new Color(0.12f, 0.08f, 0.18f, 1f);
            public Color nadirColor = new Color(0.005f, 0.006f, 0.012f, 1f);
            [Range(-1f, 1f)] public float horizonHeight;
            [Range(0.1f, 8f)] public float horizonExponent = 1.5f;
        }

        [Serializable]
        public sealed class StarSettings
        {
            [Min(0)] public int starCount = 1500;
            [Min(0)] public int bandCount = 900;
            [Min(1f)] public float radius = 300f;
            public int seed = 20260825;
            [Min(0f)] public float minimumBrightness = 0.1f;
            [Min(0f)] public float maximumBrightness = 1f;
            [Min(0.001f)] public float minimumSize = 0.1f;
            [Min(0.001f)] public float maximumSize = 0.55f;
        }

        [Serializable]
        public sealed class CelestialBodySettings
        {
            public bool planetEnabled = true;
            public Color planetColor = new Color(0.20f, 0.40f, 0.72f, 1f);
            public Vector3 planetDirection = new Vector3(-0.46f, 0.60f, 0.66f);
            [Min(0.01f)] public float planetDiameter = 78f;
            public bool moonEnabled;
            public Color moonColor = new Color(0.72f, 0.75f, 0.84f, 1f);
            public Vector3 moonDirection = new Vector3(0.45f, 0.25f, 0.86f);
            [Min(0.01f)] public float moonDiameter = 24f;
        }

        [Serializable]
        public sealed class LightingSettings
        {
            public bool primaryLightEnabled = true;
            public Color primaryLightColor = new Color(1f, 0.99f, 0.96f, 1f);
            [Range(0f, 8f)] public float primaryLightIntensity = 1.75f;
            public Vector3 primaryLightAngles = new Vector3(34f, -46f, 0f);
            [Range(0f, 1f)] public float primaryShadowStrength = 0.94f;
            public bool fillLightEnabled = true;
            public Color fillLightColor = new Color(0.42f, 0.56f, 0.86f, 1f);
            [Range(0f, 4f)] public float fillLightIntensity = 0.16f;
            public Vector3 fillLightAngles = new Vector3(-24f, 128f, 0f);
            public Color ambientSky = new Color(0.05f, 0.06f, 0.10f, 1f);
            public Color ambientEquator = new Color(0.11f, 0.11f, 0.13f, 1f);
            public Color ambientGround = new Color(0.26f, 0.23f, 0.19f, 1f);
            [Range(0f, 2f)] public float ambientIntensity = 1f;
        }

        [Serializable]
        public sealed class FogSettings
        {
            public bool enabled;
            public FogMode mode = FogMode.ExponentialSquared;
            public Color color = new Color(0.08f, 0.08f, 0.14f, 1f);
            [Min(0f)] public float density = 0.01f;
            [Min(0f)] public float startDistance = 10f;
            [Min(0.01f)] public float endDistance = 150f;
        }

        [Serializable]
        public sealed class HazeSettings
        {
            public bool enabled;
            public bool requiresDepth = true;
            public Color color = new Color(0.25f, 0.20f, 0.38f, 1f);
            [Range(0f, 1f)] public float density = 0.12f;
            [Min(0f)] public float startDistance = 15f;
            [Min(0.01f)] public float endDistance = 180f;
        }

        [Serializable]
        public sealed class GroundFogSettings
        {
            public bool enabled;
            public Color color = new Color(0.20f, 0.15f, 0.32f, 1f);
            [Range(0f, 1f)] public float density = 0.10f;
            public float baseHeight;
            [Min(0.001f)] public float heightFalloff = 0.20f;
            [Min(0f)] public float startDistance = 2f;
            [Min(0.01f)] public float endDistance = 80f;
            [Min(0.001f)] public float noiseScale = 0.05f;
            public Vector2 noiseSpeed = new Vector2(0.006f, -0.004f);
            [Range(0f, 1f)] public float noiseStrength = 0.22f;
        }

        [Serializable]
        public sealed class PostProcessingSettings
        {
            public TonemappingMode tonemapping = TonemappingMode.ACES;
            public bool bloomEnabled;
            [Range(0f, 10f)] public float bloomIntensity = 0.2f;
            [Range(0f, 1f)] public float bloomThreshold = 1f;
            public Color bloomTint = Color.white;
            public bool colorAdjustmentsEnabled;
            [Range(-5f, 5f)] public float postExposure;
            [Range(-100f, 100f)] public float contrast;
            public Color colorFilter = Color.white;
            public bool vignetteEnabled;
            [Range(0f, 1f)] public float vignetteIntensity;
            [Range(0.01f, 1f)] public float vignetteSmoothness = 0.2f;
        }

        [Serializable]
        public sealed class MeteorShowerSettings
        {
            public bool enabled;
            [Min(0)] public int maxParticles = 64;
            [Min(0f)] public float emissionRate = 4f;
            [Min(1f)] public float alertEmissionMultiplier = 1.8f;
            [Min(0.05f)] public float lifetime = 3f;
            [Min(0f)] public float speed = 18f;
            [Min(0f)] public float spread = 20f;
            public Color color = new Color(0.65f, 0.82f, 1f, 0.75f);
            [Min(0.001f)] public float startSize = 0.08f;
            [Min(0.001f)] public float endSize = 0.01f;
        }

        [Serializable]
        public sealed class DustGustSettings
        {
            public bool enabled;
            public Vector3 direction = new Vector3(1f, 0f, 0.18f);
            [Min(0.01f)] public float minimumInterval = 8f;
            [Min(0.01f)] public float maximumInterval = 18f;
            [Min(0.05f)] public float duration = 4f;
            [Min(0f)] public float speed = 3.5f;
            public Color color = new Color(0.68f, 0.50f, 0.36f, 0.22f);
            [Min(0.001f)] public float startSize = 0.22f;
            [Min(0.001f)] public float endSize = 0.06f;
            [Min(0)] public int burstCount = 18;
            [Min(0)] public int maxParticles = 72;
            [Min(0.1f)] public float spawnRadius = 10f;
            [Min(0.1f)] public float verticalSpread = 3f;
        }

        [Serializable]
        public sealed class DustStormSettings
        {
            public bool enabled;
            [Min(0.1f)] public float minimumInterval = 8f;
            [Min(0.1f)] public float maximumInterval = 14f;
            [Min(0.01f)] public float speed = 6f;
            // Local X is the travel axis, Y is vertical, and Z is the lateral axis.
            public Vector3 halfExtents = new Vector3(12f, 2.5f, 5f);
            [Range(0f, 180f)] public float directionConeDegrees = 60f;
            [Min(0f)] public float spawnMargin = 6f;
            public int seed = 20260903;

            [Header("Grounding")]
            [Min(0f)] public float groundClearance = 0.08f;

            [Header("Test visualization")]
            public bool showDebugBoundary;
            public Color debugBoundaryColor = Color.red;
            [Min(0.001f)] public float debugBoundaryWidth = 0.04f;
            [Min(10f)] public float debugSkyMarkerHeight = 80f;

            [Header("Gameplay")]
            [Min(0f)] public float windForce = 6f;
            [Min(0f)] public float maximumAffectedMass = 40f;
            [Min(0)] public int maxPhysicalFragments = 3;
            [Min(0f)] public float fragmentDamage = 5f;
            [Min(0.01f)] public float fragmentRepeatCooldown = 1f;
            [Min(0.1f)] public float fragmentLifetime = 2.5f;
            [Min(0.01f)] public float fragmentSpeed = 5f;
            public int affectedLayers = ~0;

            [Header("Visual debris")]
            [Min(0)] public int maxVisualFragments = 6;
            [Min(0)] public int maxDustParticles = 64;
            [Min(0f)] public float dustEmissionRate = 20f;
            [Min(0.05f)] public float dustLifetime = 3.5f;
            [Min(0.001f)] public float dustStartSize = 0.35f;
            [Min(0.001f)] public float dustEndSize = 0.08f;
            public Color dustColor = new Color(0.68f, 0.50f, 0.36f, 0.30f);

            // These are art-only prefabs generated from the Stage1 meteor debris FBX files.
            // The hazard adds its own physics components at runtime.
            public GameObject largeDebrisPrefab;
            public GameObject mediumDebrisPrefab;
            public GameObject smallDebrisPrefab;
        }

        [Header("Sky and distant space")]
        public SkySettings sky = new SkySettings();
        public StarSettings stars = new StarSettings();
        public CelestialBodySettings celestialBodies = new CelestialBodySettings();

        [Header("World lighting")]
        public LightingSettings lighting = new LightingSettings();

        [Header("Depth and screen atmosphere")]
        public FogSettings fog = new FogSettings();
        public HazeSettings haze = new HazeSettings();
        public GroundFogSettings groundFog = new GroundFogSettings();
        public PostProcessingSettings postProcessing = new PostProcessingSettings();

        [Header("Visual-only effects")]
        public MeteorShowerSettings meteorShower = new MeteorShowerSettings();
        public DustGustSettings dustGust = new DustGustSettings();

        [Header("Stage1 moving dust storm")]
        public DustStormSettings dustStorm = new DustStormSettings();

        [Header("Quality")]
        public bool enableOnMobile;
        [Range(0.1f, 1f)] public float mobileEffectScale = 0.5f;

        public void Validate()
        {
            if (sky == null) sky = new SkySettings();
            if (stars == null) stars = new StarSettings();
            if (celestialBodies == null) celestialBodies = new CelestialBodySettings();
            if (lighting == null) lighting = new LightingSettings();
            if (fog == null) fog = new FogSettings();
            if (haze == null) haze = new HazeSettings();
            if (groundFog == null) groundFog = new GroundFogSettings();
            if (postProcessing == null) postProcessing = new PostProcessingSettings();
            if (meteorShower == null) meteorShower = new MeteorShowerSettings();
            if (dustGust == null) dustGust = new DustGustSettings();
            if (dustStorm == null) dustStorm = new DustStormSettings();

            stars.starCount = Mathf.Max(0, stars.starCount);
            stars.bandCount = Mathf.Max(0, stars.bandCount);
            stars.radius = Mathf.Max(1f, stars.radius);
            celestialBodies.planetDiameter = Mathf.Max(0.01f, celestialBodies.planetDiameter);
            celestialBodies.moonDiameter = Mathf.Max(0.01f, celestialBodies.moonDiameter);
            fog.endDistance = Mathf.Max(fog.startDistance + 0.01f, fog.endDistance);
            haze.endDistance = Mathf.Max(haze.startDistance + 0.01f, haze.endDistance);
            groundFog.density = Mathf.Clamp01(groundFog.density);
            groundFog.startDistance = Mathf.Max(0f, groundFog.startDistance);
            groundFog.endDistance = Mathf.Max(groundFog.startDistance + 0.01f, groundFog.endDistance);
            groundFog.heightFalloff = Mathf.Max(0.001f, groundFog.heightFalloff);
            groundFog.noiseScale = Mathf.Max(0.001f, groundFog.noiseScale);
            groundFog.noiseStrength = Mathf.Clamp01(groundFog.noiseStrength);
            meteorShower.maxParticles = Mathf.Clamp(meteorShower.maxParticles, 0, 64);
            meteorShower.alertEmissionMultiplier = Mathf.Max(1f, meteorShower.alertEmissionMultiplier);
            meteorShower.lifetime = Mathf.Max(0.05f, meteorShower.lifetime);
            if (dustGust.direction.sqrMagnitude < 0.0001f)
                dustGust.direction = Vector3.right;
            else
                dustGust.direction.Normalize();
            dustGust.minimumInterval = Mathf.Max(0.01f, dustGust.minimumInterval);
            dustGust.maximumInterval = Mathf.Max(dustGust.minimumInterval, dustGust.maximumInterval);
            dustGust.duration = Mathf.Max(0.05f, dustGust.duration);
            dustGust.speed = Mathf.Max(0f, dustGust.speed);
            dustGust.startSize = Mathf.Max(0.001f, dustGust.startSize);
            dustGust.endSize = Mathf.Max(0.001f, dustGust.endSize);
            dustGust.maxParticles = Mathf.Clamp(dustGust.maxParticles, 0, 128);
            dustGust.burstCount = Mathf.Clamp(dustGust.burstCount, 0, dustGust.maxParticles);
            dustGust.spawnRadius = Mathf.Max(0.1f, dustGust.spawnRadius);
            dustGust.verticalSpread = Mathf.Max(0.1f, dustGust.verticalSpread);

            dustStorm.minimumInterval = Mathf.Max(0.1f, dustStorm.minimumInterval);
            dustStorm.maximumInterval = Mathf.Max(dustStorm.minimumInterval, dustStorm.maximumInterval);
            dustStorm.speed = Mathf.Max(0.01f, dustStorm.speed);
            dustStorm.halfExtents = new Vector3(
                Mathf.Max(0.1f, Mathf.Abs(dustStorm.halfExtents.x)),
                Mathf.Max(0.1f, Mathf.Abs(dustStorm.halfExtents.y)),
                Mathf.Max(0.1f, Mathf.Abs(dustStorm.halfExtents.z)));
            dustStorm.directionConeDegrees = Mathf.Clamp(dustStorm.directionConeDegrees, 0f, 180f);
            dustStorm.spawnMargin = Mathf.Max(0f, dustStorm.spawnMargin);
            dustStorm.groundClearance = Mathf.Max(0f, dustStorm.groundClearance);
            dustStorm.debugBoundaryWidth = Mathf.Max(0.001f, dustStorm.debugBoundaryWidth);
            dustStorm.debugSkyMarkerHeight = Mathf.Max(10f, dustStorm.debugSkyMarkerHeight);
            dustStorm.windForce = Mathf.Max(0f, dustStorm.windForce);
            dustStorm.maximumAffectedMass = Mathf.Max(0f, dustStorm.maximumAffectedMass);
            dustStorm.maxPhysicalFragments = Mathf.Clamp(dustStorm.maxPhysicalFragments, 0, 3);
            dustStorm.fragmentDamage = Mathf.Max(0f, dustStorm.fragmentDamage);
            dustStorm.fragmentRepeatCooldown = Mathf.Max(0.01f, dustStorm.fragmentRepeatCooldown);
            dustStorm.fragmentLifetime = Mathf.Max(0.1f, dustStorm.fragmentLifetime);
            dustStorm.fragmentSpeed = Mathf.Max(0.01f, dustStorm.fragmentSpeed);
            dustStorm.maxVisualFragments = Mathf.Clamp(dustStorm.maxVisualFragments, 0, 12);
            dustStorm.maxDustParticles = Mathf.Clamp(dustStorm.maxDustParticles, 0, 128);
            dustStorm.dustEmissionRate = Mathf.Max(0f, dustStorm.dustEmissionRate);
            dustStorm.dustLifetime = Mathf.Max(0.05f, dustStorm.dustLifetime);
            dustStorm.dustStartSize = Mathf.Max(0.001f, dustStorm.dustStartSize);
            dustStorm.dustEndSize = Mathf.Max(0.001f, dustStorm.dustEndSize);
            mobileEffectScale = Mathf.Clamp(mobileEffectScale, 0.1f, 1f);
        }

        void OnValidate() { Validate(); }
    }
}
