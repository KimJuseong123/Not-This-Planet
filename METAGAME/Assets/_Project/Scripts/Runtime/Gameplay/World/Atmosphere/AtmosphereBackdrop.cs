using System;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Builds a camera-following sky, star mesh, and optional celestial bodies from a profile.
    /// It is visual-only and has no relationship with stage navigation or hazards.
    /// </summary>
    public sealed class AtmosphereBackdrop : MonoBehaviour
    {
        [SerializeField] AtmosphereProfile profile;
        [SerializeField] Camera followCamera;
        [SerializeField] bool buildOnEnable = true;

        Transform generatedRoot;
        Material skyMaterial;
        Mesh starMesh;
        Material starMaterial;
        Material previousSkybox;
        bool capturedSkybox;

        void OnEnable()
        {
            if (Application.isPlaying && buildOnEnable && profile != null)
                Apply(profile, followCamera);
        }

        void LateUpdate()
        {
            if (generatedRoot == null) return;
            var camera = followCamera != null ? followCamera : Camera.main;
            if (camera != null) generatedRoot.position = camera.transform.position;
        }

        public void Apply(AtmosphereProfile atmosphereProfile, Camera camera = null)
        {
            if (atmosphereProfile == null)
            {
                Clear();
                return;
            }

            profile = atmosphereProfile;
            if (camera != null) followCamera = camera;
            profile.Validate();
            ClearGenerated();

            if (!capturedSkybox)
            {
                previousSkybox = RenderSettings.skybox;
                capturedSkybox = true;
            }

            generatedRoot = new GameObject("~Atmosphere Backdrop").transform;
            generatedRoot.SetParent(transform, false);
            generatedRoot.position = followCamera != null ? followCamera.transform.position : transform.position;

            BuildSky(profile.sky);
            float effectScale = GetEffectScale(profile);
            BuildStars(profile.stars, effectScale);
            BuildCelestialBodies(profile.celestialBodies, profile.stars.radius, effectScale);
        }

        public void Clear()
        {
            ClearGenerated();
            if (capturedSkybox)
            {
                RenderSettings.skybox = previousSkybox;
                previousSkybox = null;
                capturedSkybox = false;
            }
        }

        void OnDisable()
        {
            if (Application.isPlaying) Clear();
        }

        void BuildSky(AtmosphereProfile.SkySettings settings)
        {
            var shader = Shader.Find("Metagame/Atmosphere/SkyGradient");
            if (shader == null)
            {
                Debug.LogError("[Atmosphere] SkyGradient 셰이더를 찾지 못했습니다.");
                return;
            }

            skyMaterial = new Material(shader) { name = "Atmosphere Sky Gradient (Runtime)" };
            skyMaterial.SetColor("_ZenithColor", settings.zenithColor);
            skyMaterial.SetColor("_HorizonColor", settings.horizonColor);
            skyMaterial.SetColor("_NadirColor", settings.nadirColor);
            skyMaterial.SetFloat("_HorizonHeight", settings.horizonHeight);
            skyMaterial.SetFloat("_HorizonExponent", settings.horizonExponent);
            RenderSettings.skybox = skyMaterial;
        }

        static float GetEffectScale(AtmosphereProfile atmosphereProfile)
        {
            if (!Application.isMobilePlatform) return 1f;
            if (!atmosphereProfile.enableOnMobile) return 0f;
            return Mathf.Clamp01(atmosphereProfile.mobileEffectScale);
        }

        void BuildStars(AtmosphereProfile.StarSettings settings, float effectScale)
        {
            int starCount = Mathf.RoundToInt(settings.starCount * effectScale);
            int bandCount = Mathf.RoundToInt(settings.bandCount * effectScale);
            int total = starCount + bandCount;
            if (total <= 0) return;

            var random = new System.Random(settings.seed);
            var vertices = new Vector3[total * 4];
            var colors = new Color[total * 4];
            var triangles = new int[total * 6];
            Vector3 bandAxis = new Vector3(0.32f, 0.86f, -0.40f).normalized;
            Vector3 bandU = Vector3.Normalize(Vector3.Cross(bandAxis, Vector3.right));
            Vector3 bandV = Vector3.Cross(bandAxis, bandU);

            for (int index = 0; index < total; index++)
            {
                bool inBand = index >= starCount;
                Vector3 direction;
                if (inBand)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float spread = ((float)random.NextDouble() - 0.5f) * 0.22f;
                    direction = (bandU * Mathf.Cos(angle) + bandV * Mathf.Sin(angle) + bandAxis * spread).normalized;
                }
                else
                {
                    float z = (float)random.NextDouble() * 2f - 1f;
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                    direction = new Vector3(radial * Mathf.Cos(angle), z, radial * Mathf.Sin(angle));
                }

                float brightness = Mathf.Pow((float)random.NextDouble(), inBand ? 3.2f : 2.4f);
                brightness = Mathf.Lerp(settings.minimumBrightness, settings.maximumBrightness, brightness);
                float size = Mathf.Lerp(settings.minimumSize, settings.maximumSize, brightness);
                float tintRoll = (float)random.NextDouble();
                Color tint = tintRoll < 0.22f ? new Color(0.72f, 0.82f, 1f)
                           : tintRoll > 0.86f ? new Color(1f, 0.80f, 0.66f)
                                              : new Color(0.96f, 0.97f, 1f);
                Color color = tint * brightness;
                color.a = 1f;

                Vector3 position = direction * settings.radius;
                Vector3 right = Vector3.Normalize(Vector3.Cross(direction,
                    Mathf.Abs(direction.y) > 0.95f ? Vector3.right : Vector3.up));
                Vector3 up = Vector3.Cross(right, direction);
                int vertex = index * 4;
                vertices[vertex + 0] = position - right * size - up * size;
                vertices[vertex + 1] = position - right * size + up * size;
                vertices[vertex + 2] = position + right * size + up * size;
                vertices[vertex + 3] = position + right * size - up * size;
                colors[vertex + 0] = colors[vertex + 1] = colors[vertex + 2] = colors[vertex + 3] = color;

                int triangle = index * 6;
                triangles[triangle + 0] = vertex + 0;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 0;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            starMesh = new Mesh { name = "Atmosphere Stars" };
            starMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            starMesh.vertices = vertices;
            starMesh.colors = colors;
            starMesh.triangles = triangles;
            starMesh.bounds = new Bounds(Vector3.zero, Vector3.one * settings.radius * 4f);

            var stars = new GameObject("Atmosphere Stars");
            stars.transform.SetParent(generatedRoot, false);
            stars.AddComponent<MeshFilter>().sharedMesh = starMesh;
            var renderer = stars.AddComponent<MeshRenderer>();
            starMaterial = Prim.Flat(Color.white);
            renderer.sharedMaterial = starMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildCelestialBodies(AtmosphereProfile.CelestialBodySettings settings, float radius, float effectScale)
        {
            if (effectScale <= 0f) return;
            if (settings.planetEnabled)
                BuildBody("Atmosphere Planet", settings.planetDirection, radius * 0.82f,
                    settings.planetDiameter * Mathf.Lerp(0.75f, 1f, effectScale), settings.planetColor);
            if (settings.moonEnabled)
                BuildBody("Atmosphere Moon", settings.moonDirection, radius * 0.88f,
                    settings.moonDiameter * Mathf.Lerp(0.75f, 1f, effectScale), settings.moonColor);
        }

        void BuildBody(string name, Vector3 direction, float distance, float diameter, Color color)
        {
            Vector3 position = direction.normalized * distance;
            var body = Prim.Decor(Prim.Ball(name, position, diameter, color, 0.8f, generatedRoot));
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = Prim.Flat(color);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void ClearGenerated()
        {
            if (generatedRoot != null)
            {
                if (Application.isPlaying) Destroy(generatedRoot.gameObject);
                else DestroyImmediate(generatedRoot.gameObject);
                generatedRoot = null;
            }

            if (starMesh != null)
            {
                DestroyRuntimeObject(starMesh);
                starMesh = null;
            }
            if (starMaterial != null)
            {
                DestroyRuntimeObject(starMaterial);
                starMaterial = null;
            }
            if (skyMaterial != null)
            {
                DestroyRuntimeObject(skyMaterial);
                skyMaterial = null;
            }
        }

        static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
