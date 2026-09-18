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

            /// <summary>
            /// 은하수 띠의 두께(대원에서 벗어나는 정도). 0 이면 실 한 줄, 1 이면 거의 온 하늘
            /// (S15P21B202-530).
            ///
            /// <para>예전에는 <c>0.22</c> 가 메시를 짜는 루프 안에 박혀 있었다. 별에 관한
            /// 다른 수는 전부 여기 있는데 이것만 코드에 있었다.</para>
            ///
            /// <para><b>기본값은 그대로 둔다.</b> 넓히면 별이 고르게 퍼질 것 같지만 실측은
            /// 아니었다 — 0.22 → 0.45 로 넓혀도 방향별 편차가 1.790 → 1.768 로 1.2%만
            /// 움직였다. 몰림은 띠 두께가 아니라 <b>띠가 없는 쪽이 비어 있어서</b>였다
            /// (S15P21B202-530).</para>
            /// </summary>
            [Min(0f)] public float bandSpread = 0.22f;

            /// <summary>
            /// 별이 반짝이는 깊이. 0 이면 안 반짝이고, 1 이면 완전히 꺼졌다 켜진다
            /// (S15P21B202-546).
            ///
            /// <para>밝기를 <c>1-amount</c> 와 <c>1</c> 사이에서 흔든다. 별마다 위상이
            /// 다르므로 하늘 전체가 한꺼번에 밝아졌다 어두워지지는 않는다.</para>
            ///
            /// <para><b>눈으로 맞춘 값이다.</b> 별은 화면에서 1~3픽셀이라(`-530` 실측)
            /// 숫자로는 정할 수 없다. 너무 얕으면 안 보이고, 너무 깊으면 반짝임이 아니라
            /// 지글거리는 잡티가 된다.</para>
            /// </summary>
            [Range(0f, 1f)] public float twinkleAmount = 0.55f;

            /// <summary>반짝이는 빠르기(라디안/초). 별 하나가 한 번 밝아졌다 어두워지는 데
            /// <c>2π/speed</c> 초 걸린다 — 2.4 면 약 2.6초다.</summary>
            [Min(0f)] public float twinkleSpeed = 2.4f;
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
            public Color secondaryColor = new Color(1f, 1f, 1f, 0.95f);
            [Min(0.001f)] public float startSize = 0.08f;
            [Min(0.001f)] public float endSize = 0.01f;
        }

        [Serializable]
        public sealed class GalaxySettings
        {
            public bool enabled = true;
            [Tooltip("은하수 성운 텍스처. 비어 있으면 절차적 광운만 사용합니다.")]
            public Texture2D texture;
            [Range(0.01f, 1f)] public float width = 0.28f;
            [Range(0f, 2f)] public float intensity = 0.55f;
            public Color coreColor = new Color(0.72f, 0.66f, 1f, 0.42f);
            public Color edgeColor = new Color(0.24f, 0.42f, 0.82f, 0f);
            [Range(32, 256)] public int segments = 128;
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
            [Tooltip("반크기: X=쓸어가는 너비, Y=높이, Z=진행 방향 두께")]
            public Vector3 halfExtents = new Vector3(12f, 2.5f, 2.5f);
            [Range(0f, 180f)] public float directionConeDegrees = 60f;
            [Min(0f)] public float spawnMargin = 6f;
            public int seed = 20260903;

            [Header("Magnetic storm wake")]
            [Tooltip("후면이 지나간 자리에 잔운이 남는 시간(초). 0이면 끕니다. 최대 한 폭풍 주기까지 유지됩니다.")]
            [Range(0f, 30f)] public float wakeDuration = 4f;
            [Tooltip("잔운의 연기 불투명도 배율. 0이면 보이지 않지만 영향 배율이 있으면 힘은 적용됩니다.")]
            [Range(0f, 1f)] public float wakeOpacity = .35f;
            [Tooltip("잔운이 주는 충격의 배율. 본체의 힘에 곱하며 시간이 지날수록 0으로 감소합니다. 0이면 시각 효과만 남습니다.")]
            [Range(0f, 1f)] public float wakeInfluence = .18f;

            [Header("Fixed height above map base")]
            [Min(0f)] public float groundClearance = 0.08f;

            [Header("Test visualization")]
            public bool showDebugBoundary;
            public Color debugBoundaryColor = Color.red;
            [Min(0.001f)] public float debugBoundaryWidth = 0.04f;
            [Min(10f)] public float debugSkyMarkerHeight = 80f;

            [Header("Gameplay")]
            [Tooltip("같은 대상에 다시 충격을 주기까지의 시간(초). 진입 시 즉시 1회 적용합니다.")]
            [Min(0.05f)] public float impulseInterval = 1f;
            [Tooltip("진행 방향 순간 속도 증가량(m/s). 질량과 무관하게 플레이어도 강하게 밀립니다.")]
            [Min(0f)] public float impulseSpeed = 18f;
            [Tooltip("타격 시 위쪽 속도 증가량(m/s). 0이면 수평 방향으로만 밀립니다.")]
            [Min(0f)] public float impulseLift = 4f;
            [Min(0f)] public float playerImpulseMultiplier = 1f;
            [Min(0f)] public float objectImpulseMultiplier = 1f;
            [Tooltip("타격 대상을 찾을 레이어 마스크입니다.")]
            public int affectedLayers = ~0;
            [Tooltip("폭풍이 적용하는 기준 힘의 크기입니다. 단위는 Newton이며 FixedUpdate마다 지속 적용됩니다.")]
            [HideInInspector, Min(0f)] public float windForce = 6f;
            [Tooltip("플레이어 골반에 적용할 기준 힘의 배율입니다. 낮출수록 플레이어는 덜 밀립니다.")]
            [HideInInspector, Min(0f)] public float playerWindForceMultiplier = 0.45f;
            [Tooltip("일반 Rigidbody에 적용할 기준 힘의 배율입니다. 플레이어보다 크게 설정하면 가벼운 물체가 더 잘 밀립니다.")]
            [HideInInspector, Min(0f)] public float objectWindForceMultiplier = 1.25f;
            [Tooltip("이 질량을 초과하는 플레이어 또는 일반 Rigidbody에는 폭풍 힘을 적용하지 않습니다.")]
            [HideInInspector, Min(0f)] public float maximumAffectedMass = 40f;
            [HideInInspector, Min(0)] public int maxPhysicalFragments = 3;
            [HideInInspector, Min(0f)] public float fragmentDamage = 5f;
            [HideInInspector, Min(0.01f)] public float fragmentRepeatCooldown = 1f;
            [HideInInspector, Min(0.1f)] public float fragmentLifetime = 2.5f;
            [HideInInspector, Min(0.01f)] public float fragmentSpeed = 5f;

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

            /// <summary>
            /// Creates a runtime copy so a match can override only the seed without
            /// mutating the shared ScriptableObject asset.
            /// </summary>
            public DustStormSettings WithSeed(int matchSeed)
            {
                var copy = (DustStormSettings)MemberwiseClone();
                copy.seed = matchSeed;
                return copy;
            }
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
        public GalaxySettings galaxy = new GalaxySettings();
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
            if (galaxy == null) galaxy = new GalaxySettings();
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
            galaxy.width = Mathf.Clamp(galaxy.width, 0.01f, 1f);
            galaxy.intensity = Mathf.Clamp(galaxy.intensity, 0f, 2f);
            galaxy.segments = Mathf.Clamp(galaxy.segments, 32, 256);
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
            dustStorm.wakeDuration = Mathf.Clamp(dustStorm.wakeDuration, 0f, 30f);
            dustStorm.wakeOpacity = Mathf.Clamp01(dustStorm.wakeOpacity);
            dustStorm.wakeInfluence = Mathf.Clamp01(dustStorm.wakeInfluence);
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
            dustStorm.impulseInterval = Mathf.Max(.05f, dustStorm.impulseInterval);
            dustStorm.impulseSpeed = Mathf.Max(0f, dustStorm.impulseSpeed);
            dustStorm.impulseLift = Mathf.Max(0f, dustStorm.impulseLift);
            dustStorm.playerImpulseMultiplier = Mathf.Max(0f, dustStorm.playerImpulseMultiplier);
            dustStorm.objectImpulseMultiplier = Mathf.Max(0f, dustStorm.objectImpulseMultiplier);
            dustStorm.playerWindForceMultiplier = Mathf.Max(0f, dustStorm.playerWindForceMultiplier);
            dustStorm.objectWindForceMultiplier = Mathf.Max(0f, dustStorm.objectWindForceMultiplier);
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
