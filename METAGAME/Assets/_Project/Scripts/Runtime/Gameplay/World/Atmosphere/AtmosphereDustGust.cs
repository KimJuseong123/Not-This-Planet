using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Intermittent, directional dust visible around the camera. It has no collider,
    /// Rigidbody, gameplay damage, or network state.
    /// </summary>
    public sealed class AtmosphereDustGust : MonoBehaviour
    {
        [SerializeField] AtmosphereProfile profile;
        [SerializeField] ParticleSystem particleSystem;
        [SerializeField] bool playOnEnable = true;

        Material particleMaterial;
        Camera followCamera;
        float nextBurstAt;
        uint randomState = 0x9E3779B9u;

        void OnEnable()
        {
            if (Application.isPlaying && playOnEnable && profile != null)
                Apply(profile);
        }

        void OnDisable()
        {
            if (particleSystem != null)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void Update()
        {
            if (!Application.isPlaying || profile == null || particleSystem == null)
                return;

            var settings = profile.dustGust;
            if (!settings.enabled || GetEffectScale() <= 0f || settings.burstCount <= 0)
            {
                if (particleSystem.isPlaying)
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            if (nextBurstAt <= 0f)
                nextBurstAt = Time.time + NextInterval(settings);
            if (Time.time < nextBurstAt) return;

            var count = Mathf.Min(settings.burstCount, particleSystem.main.maxParticles);
            if (count > 0)
            {
                // The system is deliberately non-looping so each gust is a discrete
                // burst. Restart it when the previous burst's lifetime has elapsed.
                if (!particleSystem.isPlaying) particleSystem.Play();
                particleSystem.Emit(count);
            }
            nextBurstAt = Time.time + NextInterval(settings);
        }

        void LateUpdate()
        {
            if (particleSystem == null) return;
            if (followCamera == null) followCamera = Camera.main;
            if (followCamera != null)
                particleSystem.transform.position = followCamera.transform.position;
        }

        public void BindCamera(Camera camera)
        {
            followCamera = camera;
            if (particleSystem != null && followCamera != null)
                particleSystem.transform.position = followCamera.transform.position;
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
            EnsureParticleSystem();
            if (particleMaterial != null)
                particleMaterial.SetColor("_Color", profile.dustGust.color);

            Configure(profile.dustGust);
            nextBurstAt = Application.isPlaying
                ? Time.time + NextInterval(profile.dustGust)
                : 0f;

            if (profile.dustGust.enabled && GetEffectScale() > 0f && Application.isPlaying)
                particleSystem.Play();
            else
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Clear()
        {
            if (particleSystem != null)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            nextBurstAt = 0f;
            DestroyRuntimeMaterial();
        }

        void EnsureParticleSystem()
        {
            if (particleSystem == null)
            {
                var go = new GameObject("Atmosphere Dust Gust");
                go.transform.SetParent(transform, false);
                particleSystem = go.AddComponent<ParticleSystem>();
                go.AddComponent<ParticleSystemRenderer>();
            }

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) renderer = particleSystem.gameObject.AddComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (particleMaterial == null)
            {
                var shader = Shader.Find("Metagame/Atmosphere/Dust");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                {
                    Debug.LogError("[Atmosphere] Dust 셰이더를 찾지 못했습니다.");
                    return;
                }
                particleMaterial = new Material(shader) { name = "Atmosphere Dust (Runtime)" };
            }

            renderer.sharedMaterial = particleMaterial;
        }

        void Configure(AtmosphereProfile.DustGustSettings settings)
        {
            var effectScale = GetEffectScale();
            var main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(settings.maxParticles * effectScale), 0, 128);
            main.startLifetime = settings.duration;
            main.startSpeed = 0f;
            main.startSize = settings.startSize;
            main.startColor = settings.color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(settings.spawnRadius * 2f,
                                      settings.verticalSpread,
                                      settings.spawnRadius * 2f);

            var velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            var direction = settings.direction.normalized * settings.speed;
            velocity.x = new ParticleSystem.MinMaxCurve(direction.x);
            velocity.y = new ParticleSystem.MinMaxCurve(direction.y);
            velocity.z = new ParticleSystem.MinMaxCurve(direction.z);

            var sizeOverLifetime = particleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var endRatio = settings.startSize <= 0.001f
                ? 0.01f
                : Mathf.Clamp01(settings.endSize / settings.startSize);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, endRatio));

            var colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(settings.color, 0f),
                        new GradientColorKey(settings.color, 1f) },
                new[] { new GradientAlphaKey(settings.color.a, 0f),
                        new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        float NextInterval(AtmosphereProfile.DustGustSettings settings)
        {
            return Mathf.Lerp(settings.minimumInterval, settings.maximumInterval, Next01());
        }

        float Next01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00FFFFFFu) / 16777216f;
        }

        float GetEffectScale()
        {
            if (!Application.isMobilePlatform) return 1f;
            if (profile == null || !profile.enableOnMobile) return 0f;
            return Mathf.Clamp01(profile.mobileEffectScale);
        }

        void DestroyRuntimeMaterial()
        {
            if (particleMaterial == null) return;
            if (Application.isPlaying) Destroy(particleMaterial);
            else DestroyImmediate(particleMaterial);
            particleMaterial = null;
        }
    }
}
