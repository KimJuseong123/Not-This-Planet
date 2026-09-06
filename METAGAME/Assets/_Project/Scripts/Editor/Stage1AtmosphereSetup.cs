using Metagame.Bootstrap;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Creates the Stage1 atmosphere profile once and wires it to Main.
    /// Existing profile values are intentionally preserved on subsequent runs so that
    /// art direction can be tuned in the Inspector without the full setup overwriting it.
    /// </summary>
    public static class Stage1AtmosphereSetup
    {
        const string ProfilePath = "Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset";
        const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/둥실둥실 우주 대탈출/Main 씬에 1장 분위기 적용", false, 4)]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Stage1 분위기", "Play Mode를 종료한 뒤 실행해 주세요.", "확인");
                return;
            }

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // The storm profile references the generated, external-material debris prefabs.
            // Build those first so this menu is also safe to run independently of the full setup.
            Stage1EnvironmentAssets.Build();
            var profile = EnsureProfileAsset();
            ConfigureDustStorm(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindAnyObjectByType<SpaceBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[Stage1Atmosphere] Main 씬에서 SpaceBootstrap을 찾지 못했습니다.");
                return;
            }

            var serialized = new SerializedObject(bootstrap);
            var profileProperty = serialized.FindProperty("atmosphereProfile");
            if (profileProperty == null)
            {
                Debug.LogError("[Stage1Atmosphere] SpaceBootstrap.atmosphereProfile 필드를 찾지 못했습니다.");
                return;
            }

            Undo.RecordObject(bootstrap, "Apply Stage1 Atmosphere");
            profileProperty.objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Stage1Atmosphere] Stage1 프로필을 Main 씬에 연결했습니다: " + ProfilePath);
        }

        static AtmosphereProfile EnsureProfileAsset()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
            ConfigureDefaultProfile(profile);
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return profile;
        }

        static void ConfigureDustStorm(AtmosphereProfile profile)
        {
            if (profile == null || profile.dustStorm == null) return;

            bool unconfigured = !profile.dustStorm.enabled
                && profile.dustStorm.largeDebrisPrefab == null
                && profile.dustStorm.mediumDebrisPrefab == null
                && profile.dustStorm.smallDebrisPrefab == null;
            if (unconfigured)
            {
                profile.dustStorm.enabled = true;
                profile.dustStorm.minimumInterval = 8f;
                profile.dustStorm.maximumInterval = 14f;
                profile.dustStorm.speed = 6f;
                profile.dustStorm.halfExtents = new Vector3(12f, 2.5f, 5f);
                profile.dustStorm.directionConeDegrees = 60f;
                profile.dustStorm.spawnMargin = 6f;
                profile.dustStorm.seed = 20260903;
                profile.dustStorm.groundClearance = 0.08f;
                profile.dustStorm.showDebugBoundary = false;
                profile.dustStorm.debugBoundaryColor = Color.red;
                profile.dustStorm.debugBoundaryWidth = 0.04f;
                profile.dustStorm.debugSkyMarkerHeight = 80f;
                profile.dustStorm.windForce = 6f;
                profile.dustStorm.maximumAffectedMass = 40f;
                profile.dustStorm.maxPhysicalFragments = 3;
                profile.dustStorm.fragmentDamage = 5f;
                profile.dustStorm.fragmentRepeatCooldown = 1f;
                profile.dustStorm.fragmentLifetime = 2.5f;
                profile.dustStorm.fragmentSpeed = 5f;
                profile.dustStorm.maxVisualFragments = 12;
                profile.dustStorm.maxDustParticles = 128;
                profile.dustStorm.dustEmissionRate = 56f;
                profile.dustStorm.dustLifetime = 2.4f;
                profile.dustStorm.dustStartSize = 0.28f;
                profile.dustStorm.dustEndSize = 0.10f;
                profile.dustStorm.dustColor = new Color(0.72f, 0.52f, 0.34f, 0.46f);
            }

            string largePath = Stage1EnvironmentAssets.PrefabPath("MeteorDebrisLarge");
            string mediumPath = Stage1EnvironmentAssets.PrefabPath("MeteorDebrisMedium");
            string smallPath = Stage1EnvironmentAssets.PrefabPath("MeteorDebrisSmall");
            var large = AssetDatabase.LoadAssetAtPath<GameObject>(largePath);
            var medium = AssetDatabase.LoadAssetAtPath<GameObject>(mediumPath);
            var small = AssetDatabase.LoadAssetAtPath<GameObject>(smallPath);
            if (large != null && (profile.dustStorm.largeDebrisPrefab == null
                || IsModelAsset(profile.dustStorm.largeDebrisPrefab)))
                profile.dustStorm.largeDebrisPrefab = large;
            if (medium != null && (profile.dustStorm.mediumDebrisPrefab == null
                || IsModelAsset(profile.dustStorm.mediumDebrisPrefab)))
                profile.dustStorm.mediumDebrisPrefab = medium;
            if (small != null && (profile.dustStorm.smallDebrisPrefab == null
                || IsModelAsset(profile.dustStorm.smallDebrisPrefab)))
                profile.dustStorm.smallDebrisPrefab = small;

            profile.Validate();
        }

        static bool IsModelAsset(GameObject asset)
        {
            return asset != null
                && AssetDatabase.GetAssetPath(asset).EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        static void ConfigureDefaultProfile(AtmosphereProfile profile)
        {
            profile.sky.zenithColor = new Color(0.008f, 0.012f, 0.035f, 1f);
            profile.sky.horizonColor = new Color(0.34f, 0.12f, 0.10f, 1f);
            profile.sky.nadirColor = new Color(0.012f, 0.008f, 0.020f, 1f);
            profile.sky.horizonHeight = 0f;
            profile.sky.horizonExponent = 1.6f;

            profile.stars.starCount = 1500;
            profile.stars.bandCount = 900;
            profile.stars.radius = 300f;
            profile.stars.seed = 20260825;
            profile.stars.minimumBrightness = 0.1f;
            profile.stars.maximumBrightness = 1f;
            profile.stars.minimumSize = 0.1f;
            profile.stars.maximumSize = 0.55f;

            profile.celestialBodies.planetEnabled = true;
            profile.celestialBodies.planetColor = new Color(0.20f, 0.40f, 0.72f, 1f);
            profile.celestialBodies.planetDirection = new Vector3(-0.46f, 0.60f, 0.66f);
            profile.celestialBodies.planetDiameter = 78f;
            profile.celestialBodies.moonEnabled = true;
            profile.celestialBodies.moonColor = new Color(0.72f, 0.75f, 0.84f, 1f);
            profile.celestialBodies.moonDirection = new Vector3(0.45f, 0.25f, 0.86f);
            profile.celestialBodies.moonDiameter = 24f;

            profile.lighting.primaryLightEnabled = true;
            profile.lighting.primaryLightColor = new Color(1f, 0.78f, 0.64f, 1f);
            profile.lighting.primaryLightIntensity = 1.35f;
            profile.lighting.primaryLightAngles = new Vector3(34f, -46f, 0f);
            profile.lighting.primaryShadowStrength = 0.88f;
            profile.lighting.fillLightEnabled = true;
            profile.lighting.fillLightColor = new Color(0.42f, 0.56f, 0.86f, 1f);
            profile.lighting.fillLightIntensity = 0.20f;
            profile.lighting.fillLightAngles = new Vector3(-24f, 128f, 0f);
            profile.lighting.ambientSky = new Color(0.04f, 0.05f, 0.10f, 1f);
            profile.lighting.ambientEquator = new Color(0.09f, 0.09f, 0.13f, 1f);
            profile.lighting.ambientGround = new Color(0.18f, 0.16f, 0.15f, 1f);
            profile.lighting.ambientIntensity = 0.90f;

            profile.fog.enabled = true;
            profile.fog.mode = FogMode.ExponentialSquared;
            profile.fog.color = new Color(0.10f, 0.07f, 0.18f, 1f);
            profile.fog.density = 0.006f;
            profile.fog.startDistance = 10f;
            profile.fog.endDistance = 180f;

            profile.haze.enabled = true;
            profile.haze.requiresDepth = true;
            profile.haze.color = new Color(0.22f, 0.15f, 0.32f, 1f);
            profile.haze.density = 0.10f;
            profile.haze.startDistance = 15f;
            profile.haze.endDistance = 180f;

            profile.groundFog.enabled = true;
            profile.groundFog.color = new Color(0.20f, 0.15f, 0.32f, 1f);
            profile.groundFog.density = 0.10f;
            profile.groundFog.baseHeight = 0f;
            profile.groundFog.heightFalloff = 0.20f;
            profile.groundFog.startDistance = 2f;
            profile.groundFog.endDistance = 80f;
            profile.groundFog.noiseScale = 0.05f;
            profile.groundFog.noiseSpeed = new Vector2(0.006f, -0.004f);
            profile.groundFog.noiseStrength = 0.22f;

            profile.postProcessing.tonemapping = TonemappingMode.ACES;
            profile.postProcessing.bloomEnabled = true;
            profile.postProcessing.bloomIntensity = 0.15f;
            profile.postProcessing.bloomThreshold = 0.9f;
            profile.postProcessing.bloomTint = new Color(1f, 0.55f, 0.35f, 1f);
            profile.postProcessing.colorAdjustmentsEnabled = true;
            profile.postProcessing.postExposure = -0.10f;
            profile.postProcessing.contrast = 5f;
            profile.postProcessing.colorFilter = new Color(0.92f, 0.88f, 1f, 1f);
            profile.postProcessing.vignetteEnabled = true;
            profile.postProcessing.vignetteIntensity = 0.12f;
            profile.postProcessing.vignetteSmoothness = 0.30f;

            profile.meteorShower.enabled = true;
            profile.meteorShower.maxParticles = 64;
            profile.meteorShower.emissionRate = 4f;
            profile.meteorShower.alertEmissionMultiplier = 1.8f;
            profile.meteorShower.lifetime = 3f;
            profile.meteorShower.speed = 18f;
            profile.meteorShower.spread = 20f;
            profile.meteorShower.color = new Color(0.85f, 0.70f, 0.55f, 0.85f);
            profile.meteorShower.startSize = 0.10f;
            profile.meteorShower.endSize = 0.01f;

            profile.dustGust.enabled = true;
            profile.dustGust.direction = new Vector3(1f, 0f, 0.18f);
            profile.dustGust.minimumInterval = 8f;
            profile.dustGust.maximumInterval = 18f;
            profile.dustGust.duration = 4f;
            profile.dustGust.speed = 3.5f;
            profile.dustGust.color = new Color(0.68f, 0.50f, 0.36f, 0.22f);
            profile.dustGust.startSize = 0.22f;
            profile.dustGust.endSize = 0.06f;
            profile.dustGust.burstCount = 18;
            profile.dustGust.maxParticles = 72;
            profile.dustGust.spawnRadius = 10f;
            profile.dustGust.verticalSpread = 3f;

            ConfigureDustStorm(profile);

            profile.enableOnMobile = false;
            profile.mobileEffectScale = 0.5f;
            profile.Validate();
        }
    }
}
