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
            [Min(0.05f)] public float lifetime = 3f;
            [Min(0f)] public float speed = 18f;
            [Min(0f)] public float spread = 20f;
            public Color color = new Color(0.65f, 0.82f, 1f, 0.75f);
            [Min(0.001f)] public float startSize = 0.08f;
            [Min(0.001f)] public float endSize = 0.01f;
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
        public PostProcessingSettings postProcessing = new PostProcessingSettings();

        [Header("Visual-only effects")]
        public MeteorShowerSettings meteorShower = new MeteorShowerSettings();

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
            if (postProcessing == null) postProcessing = new PostProcessingSettings();
            if (meteorShower == null) meteorShower = new MeteorShowerSettings();

            stars.starCount = Mathf.Max(0, stars.starCount);
            stars.bandCount = Mathf.Max(0, stars.bandCount);
            stars.radius = Mathf.Max(1f, stars.radius);
            celestialBodies.planetDiameter = Mathf.Max(0.01f, celestialBodies.planetDiameter);
            celestialBodies.moonDiameter = Mathf.Max(0.01f, celestialBodies.moonDiameter);
            fog.endDistance = Mathf.Max(fog.startDistance + 0.01f, fog.endDistance);
            haze.endDistance = Mathf.Max(haze.startDistance + 0.01f, haze.endDistance);
            meteorShower.maxParticles = Mathf.Max(0, meteorShower.maxParticles);
            meteorShower.lifetime = Mathf.Max(0.05f, meteorShower.lifetime);
            mobileEffectScale = Mathf.Clamp(mobileEffectScale, 0.1f, 1f);
        }

        void OnValidate() { Validate(); }
    }
}
