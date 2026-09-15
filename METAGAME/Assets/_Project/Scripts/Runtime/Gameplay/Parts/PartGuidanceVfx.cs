using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay.Parts
{
    /// <summary>Plasma optics on the authored part-location beacon, independent of part state.</summary>
    public sealed class PartGuidanceVfx : MonoBehaviour
    {
        Renderer beacon;
        Material originalMaterial, coreMaterial, haloMaterial;
        LineRenderer ring, halo;
        ParticleSystem sparks;
        Color tint;
        readonly Vector3[] circle = new Vector3[48];

        public void Configure(Renderer target)
        {
            if (beacon != null || target == null || target.sharedMaterial == null) return;
            beacon = target;
            originalMaterial = target.sharedMaterial;
            var route = originalMaterial;
            tint = route.HasProperty("_BaseColor") ? route.GetColor("_BaseColor") : route.color;
            tint.a = 1f;
            // Reuse the welder's soft additive optics, with independent HDR material instances.
            var shader = Resources.Load<Shader>("WeldingParticle");
            var coreShader = Resources.Load<Shader>("GuidanceNoFog");
            if (shader == null || coreShader == null) { enabled = false; return; }
            coreMaterial = new Material(coreShader) { name = "Part guidance HDR core" };
            coreMaterial.SetColor("_BaseColor", tint);
            coreMaterial.SetColor("_EmissionColor", tint);
            coreMaterial.SetFloat("_EmissionIntensity", 2f);
            // Override the route shader's intentional LDR cap only on this private material.
            coreMaterial.SetFloat("_MaxBrightness", 3f);
            haloMaterial = new Material(shader) { name = "Part guidance plasma halo" };
            haloMaterial.SetColor("_Color", new Color(tint.r * 4f, tint.g * 4f, tint.b * 4f, .65f));
            beacon.sharedMaterial = coreMaterial;
            ring = CreateLine("Locator ring", coreMaterial, .07f, circle.Length, true);
            halo = CreateLine("Plasma glow", haloMaterial, .85f, 2, false);
            var sparkObject = new GameObject("Plasma motes");
            sparkObject.transform.SetParent(transform, false);
            sparks = sparkObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 24;
            main.startLifetime = 1.2f;
            main.startSpeed = .65f;
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .16f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = sparks.emission;
            emission.rateOverTime = 8f;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = .3f;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = haloMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Refresh(0f);
        }

        LineRenderer CreateLine(string label, Material material, float width, int count, bool loop)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            var line = obj.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = count;
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void LateUpdate() => Refresh(Time.time);

        public void Refresh(float time)
        {
            if (halo == null) return;
            bool visible = isActiveAndEnabled && beacon != null && beacon.enabled && beacon.gameObject.activeInHierarchy;
            ring.enabled = halo.enabled = visible;
            if (!visible)
            {
                if (sparks.isPlaying) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }
            // Use the existing beam's full world-space extent, including non-uniform scale.
            var bounds = beacon.bounds;
            var origin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            halo.SetPosition(0, origin);
            halo.SetPosition(1, top);
            float pulse = .85f + .15f * Mathf.Sin(time * 2f);
            coreMaterial.SetFloat("_EmissionIntensity", 1.5f + pulse);
            float radius = .8f + .12f * pulse;
            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / circle.Length;
                circle[i] = origin + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            }
            ring.SetPositions(circle);
            sparks.transform.SetPositionAndRotation(top, Quaternion.Euler(-90, 0, 0));
            if (Application.isPlaying && !sparks.isPlaying) sparks.Play();
        }

        void OnEnable()
        {
            if (beacon != null && coreMaterial != null) beacon.sharedMaterial = coreMaterial;
        }

        void OnDisable()
        {
            if (halo != null) ring.enabled = halo.enabled = false;
            if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            RestoreBeacon();
        }

        void OnDestroy()
        {
            RestoreBeacon();
            Release(coreMaterial);
            Release(haloMaterial);
        }

        void RestoreBeacon()
        {
            if (beacon != null && beacon.sharedMaterial == coreMaterial)
                beacon.sharedMaterial = originalMaterial;
        }

        static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
