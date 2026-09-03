using UnityEngine;
using UnityEngine.Rendering;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// World-space presentation of the deterministic Stage1 dust storm.
    /// It owns only particles, art debris, and the optional test boundary.
    /// </summary>
    public sealed class AtmosphereDustStorm : MonoBehaviour
    {
        [SerializeField] AtmosphereProfile profile;
        [SerializeField] ParticleSystem dustParticles;
        [SerializeField] DustStormBoundaryRenderer boundary;

        readonly ParticleSystem[] debrisParticles = new ParticleSystem[3];
        readonly bool[] debrisSourceWarnings = new bool[3];
        AtmosphereProfile.DustStormSettings settings;
        DustStormSchedule schedule;
        Bounds mapBounds;
        bool hasMapBounds;
        int activeCycle = -1;
        Material dustMaterial;

        public void BindMapBounds(Bounds bounds)
        {
            mapBounds = bounds;
            hasMapBounds = bounds.size.x > 0.01f && bounds.size.z > 0.01f;
        }

        public void Apply(AtmosphereProfile atmosphereProfile)
        {
            if (atmosphereProfile == null)
            {
                Clear();
                return;
            }

            profile = atmosphereProfile;
            profile.Validate();
            settings = profile.dustStorm;
            schedule = new DustStormSchedule(settings);
            if (!hasMapBounds) BindMapBounds(DustStormBounds.FindOrFallback());

            EnsureDustParticles();
            EnsureBoundary();
            ConfigureDustParticles();
            ConfigureDebrisParticles();
            boundary.SetStyle(settings.debugBoundaryColor,
                              settings.debugBoundaryWidth,
                              settings.debugSkyMarkerHeight);
            activeCycle = -1;
        }

        public void Tick(double simulationTime)
        {
            if (!Application.isPlaying || schedule == null || settings == null)
                return;

            if (!settings.enabled || GetEffectScale() <= 0f)
            {
                StopVisuals();
                return;
            }

            var pose = schedule.Evaluate(simulationTime, mapBounds);
            if (!pose.IsActive)
            {
                StopVisuals();
                return;
            }

            UpdatePose(pose);
            if (activeCycle == pose.CycleIndex) return;

            activeCycle = pose.CycleIndex;
            ConfigureDustDirection(pose.Direction);
            ConfigureDebrisDirection(pose.Direction);
            dustParticles.randomSeed = unchecked((uint)(settings.seed + pose.CycleIndex * 7919 + 1));
            dustParticles.useAutoRandomSeed = false;
            dustParticles.Clear(true);
            dustParticles.Play(true);
            EmitDebris(pose.CycleIndex);
        }

        public void Clear()
        {
            StopVisuals();
            schedule = null;
            settings = null;
            DestroyDustMaterial();
        }

        void OnDisable()
        {
            if (Application.isPlaying) Clear();
        }

        void EnsureDustParticles()
        {
            if (dustParticles == null)
            {
                var go = new GameObject("Atmosphere Dust Storm");
                go.transform.SetParent(transform, false);
                dustParticles = go.AddComponent<ParticleSystem>();
                go.AddComponent<ParticleSystemRenderer>();
            }

            var renderer = dustParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) renderer = dustParticles.gameObject.AddComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (dustMaterial == null)
            {
                var shader = Shader.Find("Metagame/Atmosphere/Dust");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null)
                {
                    dustMaterial = new Material(shader) { name = "Atmosphere Dust Storm (Runtime)" };
                    dustMaterial.hideFlags = HideFlags.DontSave;
                }
            }

            renderer.sharedMaterial = dustMaterial;
        }

        void EnsureBoundary()
        {
            if (boundary != null)
            {
                if (!boundary.gameObject.activeSelf)
                    boundary.gameObject.SetActive(true);
                return;
            }

            var go = new GameObject("Dust Storm Debug Boundary");
            go.transform.SetParent(transform, false);
            boundary = go.AddComponent<DustStormBoundaryRenderer>();
        }

        void ConfigureDustParticles()
        {
            if (dustParticles == null || settings == null) return;

            var main = dustParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(settings.maxDustParticles * GetEffectScale()), 0, 128);
            main.startLifetime = settings.dustLifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                settings.dustStartSize * 0.65f, settings.dustStartSize * 1.35f);
            main.startColor = settings.dustColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = dustParticles.emission;
            emission.enabled = settings.enabled && GetEffectScale() > 0f;
            emission.rateOverTime = settings.dustEmissionRate * GetEffectScale();

            var shape = dustParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = settings.halfExtents * 2f;

            var size = dustParticles.sizeOverLifetime;
            size.enabled = true;
            float endRatio = settings.dustStartSize <= 0.001f
                ? 0.01f
                : Mathf.Clamp01(settings.dustEndSize / settings.dustStartSize);
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, endRatio));

            var colors = dustParticles.colorOverLifetime;
            colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(settings.dustColor, 0f),
                        new GradientColorKey(settings.dustColor, 1f) },
                new[] { new GradientAlphaKey(settings.dustColor.a, 0f),
                        new GradientAlphaKey(0f, 1f) });
            colors.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        void ConfigureDebrisParticles()
        {
            ConfigureDebrisParticle(0, "Large", settings.largeDebrisPrefab, 2);
            ConfigureDebrisParticle(1, "Medium", settings.mediumDebrisPrefab, 2);
            ConfigureDebrisParticle(2, "Small", settings.smallDebrisPrefab, 2);
        }

        void ConfigureDebrisParticle(int index, string label, GameObject prefab, int maxParticles)
        {
            bool sourceWasAssigned = !ReferenceEquals(prefab, null);
            if (!TryResolveDebrisVisual(prefab, out var mesh, out var material))
            {
                StopDebrisParticle(index);
                if (sourceWasAssigned && !debrisSourceWarnings[index])
                {
                    debrisSourceWarnings[index] = true;
                    Debug.LogWarning("[AtmosphereDustStorm] " + label
                        + " debris source is unavailable; the storm will continue without this visual fragment.");
                }
                return;
            }

            if (debrisParticles[index] == null)
            {
                var go = new GameObject("Dust Storm Debris " + label);
                go.transform.SetParent(transform, false);
                debrisParticles[index] = go.AddComponent<ParticleSystem>();
                go.AddComponent<ParticleSystemRenderer>();
            }

            var system = debrisParticles[index];
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.mesh = mesh;
            renderer.sharedMaterial = material;

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.startLifetime = settings.fragmentLifetime;
            main.startSpeed = 0f;
            main.startSize = index == 0 ? 0.32f : index == 1 ? 0.24f : 0.16f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = settings.halfExtents * 2f;

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-2f, 2f);
            rotation.y = new ParticleSystem.MinMaxCurve(-2f, 2f);
            rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        }

        void ConfigureDustDirection(Vector3 direction)
        {
            var velocity = dustParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            Vector3 drift = direction * (settings.speed * 0.25f);
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z);
        }

        void ConfigureDebrisDirection(Vector3 direction)
        {
            for (int i = 0; i < debrisParticles.Length; i++)
            {
                var system = debrisParticles[i];
                if (system == null) continue;

                var velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                Vector3 drift = direction * (settings.fragmentSpeed * 0.6f);
                velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
                velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
                velocity.z = new ParticleSystem.MinMaxCurve(drift.z);
            }
        }

        void EmitDebris(int cycle)
        {
            int budget = settings.maxVisualFragments;
            for (int i = 0; i < debrisParticles.Length && budget > 0; i++)
            {
                var system = debrisParticles[i];
                if (system == null || system.GetComponent<ParticleSystemRenderer>().mesh == null) continue;

                int wanted = i == 0 ? 1 : i == 1 ? 2 : 3;
                int count = Mathf.Min(wanted, budget);
                system.randomSeed = unchecked((uint)(settings.seed + cycle * 6151 + i * 97 + 11));
                system.useAutoRandomSeed = false;
                system.transform.position = transform.position;
                system.transform.rotation = transform.rotation;
                system.Clear(true);
                system.Emit(count);
                budget -= count;
            }
        }

        void UpdatePose(StormPose pose)
        {
            var rotation = pose.Rotation;
            transform.SetPositionAndRotation(pose.Center, rotation);
            dustParticles.transform.SetPositionAndRotation(pose.Center, rotation);
            for (int i = 0; i < debrisParticles.Length; i++)
                if (debrisParticles[i] != null)
                    debrisParticles[i].transform.SetPositionAndRotation(pose.Center, rotation);

            boundary.SetStyle(settings.debugBoundaryColor,
                              settings.debugBoundaryWidth,
                              settings.debugSkyMarkerHeight);
            boundary.SetPose(pose);
            boundary.SetVisible(settings.showDebugBoundary);
        }

        void StopVisuals()
        {
            activeCycle = -1;
            if (dustParticles != null)
                dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < debrisParticles.Length; i++)
                if (debrisParticles[i] != null)
                    debrisParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (boundary != null) boundary.Clear();
        }

        void StopDebrisParticle(int index)
        {
            var system = debrisParticles[index];
            if (system == null) return;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            renderer.mesh = null;
            renderer.sharedMaterial = null;
        }

        /// <summary>
        /// FBX/프리팹 에셋 루트를 직접 순회하지 않고 임시 인스턴스에서 시각 리소스를
        /// 추출한다. FBX 메인 GameObject는 재임포트·도메인 리로드 시 파괴된 Unity
        /// 래퍼가 남을 수 있어 GetComponentInChildren 호출 대상이 되면 안 된다.
        /// </summary>
        static bool TryResolveDebrisVisual(GameObject prefab, out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;
            if (!IsAlive(prefab)) return false;

            GameObject sample = null;
            Object clone = null;
            try
            {
                clone = Object.Instantiate((Object)prefab);
                sample = clone as GameObject;
                if (sample == null)
                {
                    var component = clone as Component;
                    if (component != null) sample = component.gameObject;
                }

                if (!IsAlive(sample)) return false;
                sample.SetActive(false);
                MeshVisual.EnsureRenderers(sample);

                var filter = sample.GetComponentInChildren<MeshFilter>(true);
                if (filter != null) mesh = filter.sharedMesh;

                var renderer = sample.GetComponentInChildren<Renderer>(true);
                if (renderer != null) material = renderer.sharedMaterial;
                return IsAlive(mesh);
            }
            catch (MissingReferenceException)
            {
                mesh = null;
                material = null;
                return false;
            }
            finally
            {
                if (sample != null)
                {
                    if (Application.isPlaying) Object.Destroy(sample);
                    else Object.DestroyImmediate(sample);
                }
                else if (clone != null)
                {
                    if (Application.isPlaying) Object.Destroy(clone);
                    else Object.DestroyImmediate(clone);
                }
            }
        }

        static bool IsAlive(Object target) =>
            !ReferenceEquals(target, null) && target != null;

        float GetEffectScale()
        {
            if (!Application.isMobilePlatform) return 1f;
            if (profile == null || !profile.enableOnMobile) return 0f;
            return Mathf.Clamp01(profile.mobileEffectScale);
        }

        void DestroyDustMaterial()
        {
            if (dustMaterial == null) return;
            if (Application.isPlaying) Destroy(dustMaterial);
            else DestroyImmediate(dustMaterial);
            dustMaterial = null;
        }
    }
}
