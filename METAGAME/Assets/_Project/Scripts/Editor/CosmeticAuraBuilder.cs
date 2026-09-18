using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.EditorTools
{
    public static class CosmeticAuraBuilder
    {
        const string Root = "Assets/_Project/VFX/Cosmetics609";
        const string Prefabs = "Assets/_Project/Prefabs/Resources/Cosmetics609";

        [MenuItem("Metagame/Art/Rebuild 609 Auras")]
        public static void Build()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh();
            var shader = Resources.Load<Shader>("CharacterAura");
            if (shader == null) throw new System.InvalidOperationException("CharacterAura shader missing");
            Color[] colors = { new Color(1f, .68f, .19f), new Color(.18f, .8f, 1f), new Color(.74f, .38f, 1f) };
            for (int index = 1; index <= 3; index++)
            {
                string path = $"{Root}/Aura{index:00}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetFloat("_Intensity", index == 3 ? 2f : 2.5f);
                material.SetFloat("_Star", index == 3 ? 0f : 1f);
                EditorUtility.SetDirty(material);
                var root = new GameObject($"Aura{index:00}");
                try
                {
                    AddParticles(root.transform, material, colors[index - 1], index, 0);
                    if (index == 2) AddParticles(root.transform, material, new Color(.4f, 1f, .85f), index, 1);
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/Aura{index:00}.prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CosmeticAura] Built golden stardust, cyan orbit and violet fireflies.");
        }

        static void AddParticles(Transform parent, Material material, Color color, int variant, int band)
        {
            var go = new GameObject("Sparkles " + band);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, variant == 2 ? (band == 0 ? .25f : -.2f) : .12f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 4f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            main.maxParticles = variant == 2 ? 32 : 64;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.4f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(variant == 3 ? .035f : .055f, variant == 3 ? .085f : .13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, .4f));
            var emission = ps.emission;
            emission.rateOverTime = variant == 2 ? 9f : 16f;
            var shape = ps.shape;
            shape.shapeType = variant == 2 ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = variant == 2 ? .55f : .58f;
            shape.radiusThickness = .12f;
            shape.scale = variant == 2 ? Vector3.one : new Vector3(1, 1.1f, .85f);
            if (variant == 2) shape.rotation = new Vector3(90, 0, band == 0 ? 15 : -15);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = variant == 1 ? .08f : 0f;
            velocity.orbitalY = variant == 2 ? (band == 0 ? .65f : -.5f) : .12f;
            var noise = ps.noise;
            noise.enabled = variant == 3;
            noise.strength = .055f;
            noise.frequency = .65f;
            noise.scrollSpeed = .25f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.85f, .2f), new GradientAlphaKey(.25f, .42f),
                    new GradientAlphaKey(.9f, .62f), new GradientAlphaKey(0, 1) });
            var lifetime = ps.colorOverLifetime;
            lifetime.enabled = true;
            lifetime.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .2f), new Keyframe(.25f, 1f), new Keyframe(.55f, .5f), new Keyframe(.75f, 1f), new Keyframe(1, 0)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
