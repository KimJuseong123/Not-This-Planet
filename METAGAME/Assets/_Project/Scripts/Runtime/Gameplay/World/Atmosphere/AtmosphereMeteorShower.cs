using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// A capped, visual-only meteor shower. It deliberately has no collider,
    /// Rigidbody, damage source, or gameplay event authority.
    /// </summary>
    public sealed class AtmosphereMeteorShower : MonoBehaviour
    {
        [SerializeField] AtmosphereProfile profile;
        [SerializeField] ParticleSystem particleSystem;
        [SerializeField] bool playOnEnable = true;

        Material particleMaterial;
        float intensity = 1f;

        void OnEnable()
        {
            if (Application.isPlaying && playOnEnable && profile != null)
                Apply(profile);
        }

        void OnDisable()
        {
            if (particleSystem != null) particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Apply(AtmosphereProfile atmosphereProfile)
        {
            if (atmosphereProfile == null)
            {
                Clear();
                return;
            }

            profile = atmosphereProfile;
            var settings = profile.meteorShower;
            EnsureParticleSystem();
            Configure(settings);
            SetIntensity(intensity);

            if (settings.enabled && GetEffectScale() > 0f && Application.isPlaying)
                particleSystem.Play();
            else
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            if (particleSystem == null || profile == null) return;

            var emission = particleSystem.emission;
            emission.rateOverTime = profile.meteorShower.emissionRate * GetEffectScale() * intensity;
            if (intensity <= 0.001f)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else if (Application.isPlaying && profile.meteorShower.enabled && !particleSystem.isPlaying)
                particleSystem.Play();
        }

        public void Clear()
        {
            if (particleSystem != null)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            DestroyRuntimeMaterial();
        }

        void EnsureParticleSystem()
        {
            if (particleSystem == null)
            {
                var go = new GameObject("Atmosphere Meteor Shower");
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
                var shader = Shader.Find("Metagame/Atmosphere/Meteor");
                if (shader == null)
                {
                    Debug.LogError("[Atmosphere] Meteor 셰이더를 찾지 못했습니다.");
                    return;
                }
                particleMaterial = new Material(shader) { name = "Atmosphere Meteor (Runtime)" };
            }

            // Re-apply after a particle system/renderer is recreated so the runtime
            // material cannot silently fall back to the default particle shader.
            renderer.sharedMaterial = particleMaterial;
        }

        void Configure(AtmosphereProfile.MeteorShowerSettings settings)
        {
            float effectScale = GetEffectScale();
            var main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(settings.maxParticles * effectScale), 0, 512);
            main.startLifetime = settings.lifetime;
            main.startSpeed = settings.speed;
            main.startSize = settings.startSize;
            main.startColor = settings.color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particleSystem.emission;
            emission.enabled = settings.enabled && effectScale > 0f;
            emission.rateOverTime = settings.emissionRate * effectScale;

            var shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 24f;

            var velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-settings.spread, settings.spread);
            velocity.y = new ParticleSystem.MinMaxCurve(-settings.spread, settings.spread);
            velocity.z = new ParticleSystem.MinMaxCurve(-settings.speed, -settings.speed * 0.5f);

            var colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(settings.color, 0f), new GradientColorKey(settings.color, 1f) },
                new[] { new GradientAlphaKey(settings.color.a, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
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
