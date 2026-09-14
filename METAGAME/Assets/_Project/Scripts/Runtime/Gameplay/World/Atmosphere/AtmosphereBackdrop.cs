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

        /// <summary>
        /// 별 하나. 메시를 만들기 전의 <b>순수한 값</b>이다 (S15P21B202-530).
        ///
        /// <para>예전에는 방향·밝기·크기를 정하는 일이 메시를 짜는 루프 안에만 있어
        /// <b>"별이 하늘에 고르게 있나" 를 물어볼 수가 없었다.</b> 그래서 하늘을 재는
        /// 검증이 0개였다.</para>
        /// </summary>
        public readonly struct StarPoint
        {
            public readonly Vector3 Direction;
            public readonly float Brightness;
            public readonly float Size;
            public readonly bool InBand;

            public StarPoint(Vector3 direction, float brightness, float size, bool inBand)
            {
                Direction = direction;
                Brightness = brightness;
                Size = size;
                InBand = inBand;
            }
        }

        /// <summary>은하수 띠가 놓이는 대원의 축. 위(+Y)에서 30.8도 기울어 있다.</summary>
        public static readonly Vector3 BandAxis = new Vector3(0.32f, 0.86f, -0.40f).normalized;

        /// <summary>
        /// 별들의 방향·밝기·크기를 정한다. 메시를 만들지 않으므로 검증이 그대로 잴 수 있다.
        /// </summary>
        public static StarPoint[] BuildStarPoints(AtmosphereProfile.StarSettings settings,
                                                  int starCount, int bandCount)
        {
            int total = Mathf.Max(0, starCount) + Mathf.Max(0, bandCount);
            var points = new StarPoint[total];
            if (total == 0) return points;

            var random = new System.Random(settings.seed);
            Vector3 bandU = Vector3.Normalize(Vector3.Cross(BandAxis, Vector3.right));
            Vector3 bandV = Vector3.Cross(BandAxis, bandU);

            for (int index = 0; index < total; index++)
            {
                bool inBand = index >= starCount;
                Vector3 direction;
                if (inBand)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float spread = ((float)random.NextDouble() - 0.5f) * settings.bandSpread;
                    direction = (bandU * Mathf.Cos(angle) + bandV * Mathf.Sin(angle) + BandAxis * spread).normalized;
                }
                else
                {
                    // 구면 균등 표집: z 를 고르게 뽑고 반지름을 √(1-z²) 로 맞춘다.
                    float z = (float)random.NextDouble() * 2f - 1f;
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                    direction = new Vector3(radial * Mathf.Cos(angle), z, radial * Mathf.Sin(angle));
                }

                float brightness = Mathf.Pow((float)random.NextDouble(), inBand ? 3.2f : 2.4f);
                brightness = Mathf.Lerp(settings.minimumBrightness, settings.maximumBrightness, brightness);
                float size = Mathf.Lerp(settings.minimumSize, settings.maximumSize, brightness);
                random.NextDouble();        // 색온도 — 자리를 지켜 씨앗이 같으면 같은 하늘이 나온다

                points[index] = new StarPoint(direction, brightness, size, inBand);
            }
            return points;
        }

        /// <summary>
        /// 별 사각형이 카메라를 마주 보게 하는 두 축.
        ///
        /// <para>극 근처에서 <c>Vector3.up</c> 과 나란해지면 외적이 0 이 되므로 그때만
        /// <c>Vector3.right</c> 로 바꾼다.</para>
        /// </summary>
        public static void StarQuadAxes(Vector3 direction, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Normalize(Vector3.Cross(direction,
                Mathf.Abs(direction.y) > 0.95f ? Vector3.right : Vector3.up));
            up = Vector3.Cross(right, direction);
        }

        /// <summary>
        /// 별 사각형의 꼭짓점 넷. 카메라를 마주 보는 정사각형이다.
        /// </summary>
        public static void StarQuadVertices(Vector3 direction, float radius, float size,
                                            Vector3[] vertices, int offset)
        {
            Vector3 position = direction * radius;
            StarQuadAxes(direction, out var right, out var up);

            vertices[offset + 0] = position - right * size - up * size;
            vertices[offset + 1] = position - right * size + up * size;
            vertices[offset + 2] = position + right * size + up * size;
            vertices[offset + 3] = position + right * size - up * size;
        }

        /// <summary>
        /// 별 사각형의 삼각형 둘. <b>감는 방향이 곧 면의 앞뒤다</b> (S15P21B202-530).
        ///
        /// <para>예전 순서(<c>0,1,2 / 0,2,3</c>)는 면이 하늘 <b>바깥</b>을 보게 만들었다.
        /// 그래도 별이 보였던 것은 <c>FlatUnlit.shader</c> 가 <c>Cull Off</c> 였기 때문이고,
        /// <b>재질을 바꾸는 순간 별이 통째로 사라진다.</b></para>
        ///
        /// <para><b>따로 뺀 이유</b>는 검증이 제품과 같은 순서를 쓰게 하기 위해서다 —
        /// 검증이 자기 사각형을 따로 만들면 제품의 감김을 되돌려도 통과한다.</para>
        /// </summary>
        public static void StarQuadTriangles(int vertexBase, int[] triangles, int triangleBase)
        {
            triangles[triangleBase + 0] = vertexBase + 0;
            triangles[triangleBase + 1] = vertexBase + 2;
            triangles[triangleBase + 2] = vertexBase + 1;
            triangles[triangleBase + 3] = vertexBase + 0;
            triangles[triangleBase + 4] = vertexBase + 3;
            triangles[triangleBase + 5] = vertexBase + 2;
        }

        /// <summary>
        /// 별 하나가 화면에서 몇 픽셀인가. <b>1 픽셀보다 작으면 사실상 안 보인다.</b>
        ///
        /// <para>조건문 안에만 두면 검증이 닿지 못한다 — 이 프로젝트에서 여러 번 겪은
        /// 자리라 처음부터 빼 둔다.</para>
        /// </summary>
        public static float StarPixelSize(float size, float radius,
                                          float verticalFovDegrees, float verticalPixels)
        {
            if (radius <= 0f || verticalFovDegrees <= 0f || verticalPixels <= 0f) return 0f;

            float degrees = 2f * size / radius * Mathf.Rad2Deg;
            return degrees / (verticalFovDegrees / verticalPixels);
        }

        void BuildStars(AtmosphereProfile.StarSettings settings, float effectScale)
        {
            int starCount = Mathf.RoundToInt(settings.starCount * effectScale);
            int bandCount = Mathf.RoundToInt(settings.bandCount * effectScale);
            var points = BuildStarPoints(settings, starCount, bandCount);
            int total = points.Length;
            if (total <= 0) return;

            var random = new System.Random(settings.seed ^ 0x5bf03635);
            var vertices = new Vector3[total * 4];
            var colors = new Color[total * 4];
            var triangles = new int[total * 6];

            for (int index = 0; index < total; index++)
            {
                var star = points[index];
                float tintRoll = (float)random.NextDouble();
                Color tint = tintRoll < 0.22f ? new Color(0.72f, 0.82f, 1f)
                           : tintRoll > 0.86f ? new Color(1f, 0.80f, 0.66f)
                                              : new Color(0.96f, 0.97f, 1f);
                Color color = tint * star.Brightness;
                color.a = 1f;

                int vertex = index * 4;
                StarQuadVertices(star.Direction, settings.radius, star.Size, vertices, vertex);
                colors[vertex + 0] = colors[vertex + 1] = colors[vertex + 2] = colors[vertex + 3] = color;
                StarQuadTriangles(vertex, triangles, index * 6);
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
