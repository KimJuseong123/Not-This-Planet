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
        Camera followCamera;
        float intensity = 1f;
        float alertIntensity;

        void OnEnable()
        {
            if (Application.isPlaying && playOnEnable && profile != null)
                Apply(profile);
        }

        void OnDisable()
        {
            if (particleSystem != null) particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void LateUpdate()
        {
            if (particleSystem == null) return;
            if (followCamera == null) followCamera = Camera.main;
            if (followCamera != null)
                particleSystem.transform.position = followCamera.transform.position;
        }

        /// <summary>
        /// 카메라가 재생성되는 Stage에서도 시각 유성우가 플레이 화면을 따라가도록 연결한다.
        /// 이 컴포넌트의 부모 전체를 움직이지 않고 파티클 루트만 이동시켜 조명과 다른
        /// 분위기 런타임 오브젝트에는 영향을 주지 않는다.
        /// </summary>
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
            var settings = profile.meteorShower;
            EnsureParticleSystem();
            if (particleMaterial != null)
                particleMaterial.SetColor("_Color", settings.color);
            Configure(settings);
            SetIntensity(intensity);
            SetAlertIntensity(alertIntensity);

            if (settings.enabled && GetEffectScale() > 0f && Application.isPlaying)
                particleSystem.Play();
            else
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            if (particleSystem == null || profile == null) return;

            ApplyEmission();
            if (intensity <= 0.001f)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else if (Application.isPlaying && profile.meteorShower.enabled && !particleSystem.isPlaying)
                particleSystem.Play();
        }

        /// <summary>
        /// 실제 운석 예고가 진행 중일 때만 시각 효과의 방출량을 키운다.
        /// 운석 스포너의 시간·물리·충돌 상태는 수정하지 않는다.
        /// </summary>
        public void SetAlertIntensity(float value)
        {
            alertIntensity = Mathf.Clamp01(value);
            if (particleSystem == null || profile == null) return;

            ApplyEmission();
            if (Application.isPlaying && profile.meteorShower.enabled && intensity > 0.001f && !particleSystem.isPlaying)
                particleSystem.Play();
        }

        public void Clear()
        {
            if (particleSystem != null)
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            DestroyRuntimeMaterial();
            alertIntensity = 0f;
        }

        void EnsureParticleSystem()
        {
            if (particleSystem == null)
            {
                var go = new GameObject("Atmosphere Meteor Shower");
                go.transform.SetParent(transform, false);
                particleSystem = go.AddComponent<ParticleSystem>();
            }

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) renderer = particleSystem.gameObject.AddComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.trailMaterial = particleMaterial;

            if (particleMaterial == null)
            {
                var shader = Shader.Find("Metagame/Atmosphere/Meteor");
                if (shader == null || !shader.isSupported)
                    shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null || !shader.isSupported)
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
            main.maxParticles = Mathf.Clamp(Mathf.Min(settings.maxParticles, 64), 0, 64);
            main.startLifetime = settings.lifetime;
            main.startSpeed = settings.speed;
            main.startSize = settings.startSize;
            main.startColor = settings.color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var sizeOverLifetime = particleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            float endSizeRatio = settings.startSize <= 0.001f
                ? 0.01f
                : Mathf.Max(0.01f, settings.endSize / settings.startSize);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, endSizeRatio));

            var emission = particleSystem.emission;
            emission.enabled = settings.enabled && effectScale > 0f;
            emission.rateOverTime = settings.emissionRate * effectScale;

            var trails = particleSystem.trails;
            trails.enabled = settings.enabled && effectScale > 0f;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = 0.22f;
            trails.dieWithParticles = true;
            trails.minVertexDistance = 0.05f;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.inheritParticleColor = true;

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.trailMaterial = particleMaterial;
                renderer.minParticleSize = 0f;
                renderer.maxParticleSize = 1f;
            }

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

        void ApplyEmission()
        {
            if (particleSystem == null || profile == null) return;

            float effectScale = GetEffectScale();
            var settings = profile.meteorShower;
            float alertMultiplier = Mathf.Lerp(1f, settings.alertEmissionMultiplier, alertIntensity);
            var emission = particleSystem.emission;
            emission.enabled = settings.enabled && effectScale > 0f;
            emission.rateOverTime = settings.emissionRate * effectScale * intensity * alertMultiplier;
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
