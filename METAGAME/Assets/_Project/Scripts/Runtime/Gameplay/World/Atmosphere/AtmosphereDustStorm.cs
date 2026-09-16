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
        int? matchSeedOverride;
        Material dustMaterial;
        MagneticStormVisual magneticVisual;

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
            if (matchSeedOverride.HasValue)
                settings = settings.WithSeed(matchSeedOverride.Value);
            schedule = new DustStormSchedule(settings);
            if (!hasMapBounds) BindMapBounds(DustStormBounds.FindOrFallback());

            EnsureDustParticles();
            EnsureBoundary();
            EnsureMagneticVisual();
            ConfigureDustParticles();
            ConfigureDebrisParticles();
            boundary.SetStyle(settings.debugBoundaryColor,
                              settings.debugBoundaryWidth,
                              settings.debugSkyMarkerHeight);
            activeCycle = -1;
        }

        /// <summary>
        /// Applies the server-selected match seed to a runtime copy of the profile.
        /// The shared ScriptableObject remains unchanged for the next match.
        /// </summary>
        public void SetMatchSeed(int matchSeed)
        {
            matchSeedOverride = matchSeed;
            if (profile == null || profile.dustStorm == null)
                return;

            if (settings != null && settings.seed == matchSeed)
                return;

            settings = profile.dustStorm.WithSeed(matchSeed);
            schedule = new DustStormSchedule(settings);
            activeCycle = -1;
            StopVisuals();
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
            if (magneticVisual != null)
            {
                if (!magneticVisual.gameObject.activeSelf) magneticVisual.gameObject.SetActive(true);
                magneticVisual.Evaluate(simulationTime, GetEffectScale());
            }
            if (activeCycle == pose.CycleIndex) return;

            activeCycle = pose.CycleIndex;
            ConfigureDustDirection(pose.Direction);
            ConfigureDebrisDirection(pose.Direction);
            dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            dustParticles.useAutoRandomSeed = false;
            dustParticles.randomSeed = unchecked((uint)(settings.seed + pose.CycleIndex * 7919 + 1));
            dustParticles.Clear(true);
            if (magneticVisual == null) dustParticles.Play(true);
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
            }

            var renderer = dustParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) renderer = dustParticles.gameObject.AddComponent<ParticleSystemRenderer>();
            // Stretch the billboard along the wind direction. A static round billboard
            // reads as floating dots; the stretched pass gives the effect a clear sand
            // flow even when the storm is seen from the side.
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.lengthScale = 1.6f;
            renderer.velocityScale = 0.45f;
            renderer.cameraVelocityScale = 0.05f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (dustMaterial == null)
            {
                var shader = Shader.Find("Metagame/Atmosphere/Dust");
                if (shader == null || !shader.isSupported)
                    shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null || !shader.isSupported) return;

                dustMaterial = new Material(shader) { name = "Atmosphere Dust Storm (Runtime)" };
                dustMaterial.hideFlags = HideFlags.DontSave;
            }

            renderer.sharedMaterial = dustMaterial;
        }

        void EnsureMagneticVisual()
        {
            if (!settings.enabled || GetEffectScale() <= 0f || !SystemInfo.supports3DTextures) return;
            if (magneticVisual == null)
            {
                var prefab = Resources.Load<GameObject>(MagneticStormVisual.ResourcePath);
                if (prefab == null) return; // Older content packs retain the original dust presentation.
                var instance = Instantiate(prefab, transform, false);
                magneticVisual = instance.GetComponent<MagneticStormVisual>();
                if (magneticVisual == null)
                {
                    Destroy(instance);
                    return;
                }
                magneticVisual.SetExternallyDriven();
                instance.SetActive(false);
            }
            magneticVisual.SetExtents(settings.halfExtents);
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
            // The custom shader multiplies its material color by the particle color.
            // Keep the particle white so the profile alpha is not multiplied twice.
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            if (dustMaterial != null)
            {
                if (dustMaterial.HasProperty("_Color"))
                    dustMaterial.SetColor("_Color", settings.dustColor);
                if (dustMaterial.HasProperty("_BaseColor"))
                    dustMaterial.SetColor("_BaseColor", settings.dustColor);
            }

            var emission = dustParticles.emission;
            emission.enabled = magneticVisual == null && settings.enabled && GetEffectScale() > 0f;
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
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f) });
            colors.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        void ConfigureDebrisParticles()
        {
            ConfigureDebrisParticle(0, "Large", settings.largeDebrisPrefab);
            ConfigureDebrisParticle(1, "Medium", settings.mediumDebrisPrefab);
            ConfigureDebrisParticle(2, "Small", settings.smallDebrisPrefab);
        }

        void ConfigureDebrisParticle(int index, string label, GameObject prefab)
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
            }

            var system = debrisParticles[index];
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.mesh = mesh;
            renderer.sharedMaterial = IsAlive(material)
                ? material
                : Prim.Dough(new Color(0.38f, 0.25f, 0.16f, 1f), 0.8f);

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = index == 0 ? 2 : index == 1 ? 4 : 8;
            main.startLifetime = settings.fragmentLifetime;
            main.startSpeed = 0f;
            main.startSize = index == 0 ? 0.72f : index == 1 ? 0.42f : 0.24f;
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
            Vector3 drift = direction * (settings.speed * 0.65f);
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x);
            velocity.y = new ParticleSystem.MinMaxCurve(drift.y);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z);

            var noise = dustParticles.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = 0.55f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.22f;
            noise.octaveCount = 2;
            noise.damping = true;
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

                int wanted = i == 0 ? 1 : i == 1 ? 3 : 6;
                int count = Mathf.Min(wanted, budget);
                system.transform.position = transform.position;
                system.transform.rotation = transform.rotation;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.useAutoRandomSeed = false;
                system.randomSeed = unchecked((uint)(settings.seed + cycle * 6151 + i * 97 + 11));
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
            if (magneticVisual != null && magneticVisual.gameObject.activeSelf)
                magneticVisual.gameObject.SetActive(false);
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
            if (!IsAlive(prefab)) return TryCreateFallbackDebrisVisual(out mesh, out material);

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

                if (!IsAlive(sample))
                    return TryCreateFallbackDebrisVisual(out mesh, out material);
                sample.SetActive(false);
                MeshVisual.EnsureRenderers(sample);

                var filter = sample.GetComponentInChildren<MeshFilter>(true);
                if (filter != null) mesh = filter.sharedMesh;
                if (!IsAlive(mesh))
                {
                    var skinned = sample.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (skinned != null) mesh = skinned.sharedMesh;
                }

                var renderer = sample.GetComponentInChildren<Renderer>(true);
                if (renderer != null) material = renderer.sharedMaterial;
                if (IsAlive(mesh) && !IsSafeDebrisMaterial(material))
                    material = Prim.Dough(new Color(0.38f, 0.25f, 0.16f, 1f), 0.8f);
                return IsAlive(mesh)
                    || TryCreateFallbackDebrisVisual(out mesh, out material);
            }
            catch (MissingReferenceException)
            {
                mesh = null;
                material = null;
                return TryCreateFallbackDebrisVisual(out mesh, out material);
            }
            catch (System.InvalidCastException)
            {
                mesh = null;
                material = null;
                return TryCreateFallbackDebrisVisual(out mesh, out material);
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

        static bool TryCreateFallbackDebrisVisual(out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var filter = fallback.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
                material = Prim.Dough(new Color(0.38f, 0.25f, 0.16f, 1f), 0.8f);
                return IsAlive(mesh) && IsAlive(material);
            }
            finally
            {
                if (Application.isPlaying) Destroy(fallback);
                else DestroyImmediate(fallback);
            }
        }

        static bool IsAlive(Object target) =>
            !ReferenceEquals(target, null) && target != null;

        /// <summary>
        /// FBX debris can still carry a Built-in/failed shader when the editor-side
        /// material extraction step has not been run. Passing that material to the
        /// particle renderer produces the URP magenta error shader in a player.
        /// Keep the imported mesh, but use the project's guaranteed URP surface for
        /// the visual fallback instead of mutating the source asset at runtime.
        /// </summary>
        static bool IsSafeDebrisMaterial(Material candidate)
        {
            if (!IsAlive(candidate) || !IsAlive(candidate.shader) || !candidate.shader.isSupported)
                return false;

            string shaderName = candidate.shader.name;
            return shaderName != "Standard"
                && shaderName != "Diffuse"
                && shaderName != "Legacy Shaders/Diffuse"
                && shaderName != "Legacy Shaders/Specular"
                && shaderName != "Hidden/InternalErrorShader";
        }

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
