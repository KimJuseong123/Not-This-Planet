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
        Mesh galaxyMesh;
        Material galaxyMaterial;
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

        public void Apply(AtmosphereProfile atmosphereProfile, Camera camera = null) =>
            Build(atmosphereProfile, camera, includeSky: true, includeCelestialBodies: true);

        /// <summary>
        /// <b>별만</b> 만든다 — 스카이박스도 조명도 건드리지 않는다 (S15P21B202-546).
        ///
        /// <para>수제 맵(<see cref="AuthoredMapSettings.preserveSceneLighting"/>)용이다.
        /// 아트가 맞춘 하늘색·조명은 그대로 두고 별만 얹는다. <see cref="BuildSky"/> 를
        /// 건너뛰므로 <c>RenderSettings.skybox</c> 를 덮지 않는다 — 그것이
        /// <see cref="Apply"/> 를 그대로 쓸 수 없는 이유다.</para>
        ///
        /// <para>행성·달도 만들지 않는다. 수제 맵에는 이미 아트가 그린 천체가 있을 수
        /// 있고, 겹치면 두 개로 보인다.</para>
        /// </summary>
        public void ApplyStarsOnly(AtmosphereProfile atmosphereProfile, Camera camera = null) =>
            Build(atmosphereProfile, camera, includeSky: false, includeCelestialBodies: false);

        void Build(AtmosphereProfile atmosphereProfile, Camera camera,
                   bool includeSky, bool includeCelestialBodies)
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

            // 하늘을 안 만들면 스카이박스를 건드리지 않으므로 되돌릴 것도 없다.
            // 여기서 기억해 두면 Clear 가 남의 하늘을 자기 것으로 알고 되돌린다.
            if (includeSky && !capturedSkybox)
            {
                previousSkybox = RenderSettings.skybox;
                capturedSkybox = true;
            }

            generatedRoot = new GameObject("~Atmosphere Backdrop").transform;
            generatedRoot.SetParent(transform, false);
            generatedRoot.position = followCamera != null ? followCamera.transform.position : transform.position;

            if (includeSky) BuildSky(profile.sky);
            float effectScale = GetEffectScale(profile);
            BuildGalaxy(profile.galaxy, profile.stars.radius, effectScale);
            BuildStars(profile.stars, effectScale);
            if (includeCelestialBodies)
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

            /// <summary>
            /// 색온도 뽑기(0~1). <b>값이 아니라 뽑은 수를 나른다</b> — 색을 정하는 규칙은
            /// 그리는 쪽에 두고, 난수열은 하나로 유지한다 (!444 AI 리뷰).
            /// </summary>
            public readonly float TintRoll;

            /// <summary>
            /// 반짝임의 위상(0~1). 별마다 다르므로 하늘이 한꺼번에 밝아지지 않는다
            /// (S15P21B202-546).
            ///
            /// <para><b>난수열에서 뽑지 않는다.</b> 여기서 한 번 더 뽑으면 그 뒤의 모든
            /// 별이 밀려 별자리가 통째로 바뀐다 — !444 에서 색을 다른 흐름으로 옮겼다가
            /// 별빛을 조용히 다시 굴린 적이 있다. 대신 번호에서 <b>결정적으로</b> 만든다.</para>
            /// </summary>
            public readonly float TwinklePhase;

            public StarPoint(Vector3 direction, float brightness, float size, bool inBand,
                             float tintRoll, float twinklePhase)
            {
                Direction = direction;
                Brightness = brightness;
                Size = size;
                InBand = inBand;
                TintRoll = tintRoll;
                TwinklePhase = twinklePhase;
            }
        }

        /// <summary>
        /// 번호 하나에서 0~1 의 위상을 만든다 (S15P21B202-546).
        ///
        /// <para>황금비의 소수부를 더해 나간다. 이웃한 번호가 이웃한 위상을 갖지 않으면서도
        /// 난수를 쓰지 않으므로, <b>별의 자리·밝기·색을 한 톨도 건드리지 않는다.</b></para>
        /// </summary>
        public static float TwinklePhaseAt(int index) =>
            index * 0.6180339887f - Mathf.Floor(index * 0.6180339887f);

        static readonly int TwinkleAmountId = Shader.PropertyToID("_TwinkleAmount");
        static readonly int TwinkleSpeedId = Shader.PropertyToID("_TwinkleSpeed");

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
                float tintRoll = (float)random.NextDouble();

                points[index] = new StarPoint(direction, brightness, size, inBand, tintRoll,
                                              TwinklePhaseAt(index));
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

        /// <summary>별의 색온도 — 푸른 별, 흰 별, 붉은 별.</summary>
        public static Color StarTint(float roll) =>
            roll < 0.22f ? new Color(0.72f, 0.82f, 1f)
          : roll > 0.86f ? new Color(1f, 0.80f, 0.66f)
                         : new Color(0.96f, 0.97f, 1f);

        void BuildStars(AtmosphereProfile.StarSettings settings, float effectScale)
        {
            int starCount = Mathf.RoundToInt(settings.starCount * effectScale);
            int bandCount = Mathf.RoundToInt(settings.bandCount * effectScale);
            var points = BuildStarPoints(settings, starCount, bandCount);
            int total = points.Length;
            if (total <= 0) return;

            var vertices = new Vector3[total * 4];
            var colors = new Color[total * 4];
            var triangles = new int[total * 6];

            for (int index = 0; index < total; index++)
            {
                var star = points[index];
                Color color = StarTint(star.TintRoll) * star.Brightness;

                // **알파에 반짝임의 위상을 싣는다**(S15P21B202-546). 이 셰이더는 불투명
                // 패스라 출력 알파가 버려진다 — 그래서 여기는 지금까지 1 이 들어가던
                // 빈자리였다. 이 채널을 쓰면 정점도 메모리도 늘지 않는다.
                color.a = star.TwinklePhase;

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

            // 셰이더가 없는 환경(대체 셰이더로 떨어진 경우)에서도 조용히 넘어간다.
            // 값을 넣지 않으면 기본값 0 이라 반짝이지 않을 뿐 그림은 그대로다.
            if (starMaterial.HasProperty(TwinkleAmountId))
                starMaterial.SetFloat(TwinkleAmountId, settings.twinkleAmount);
            if (starMaterial.HasProperty(TwinkleSpeedId))
                starMaterial.SetFloat(TwinkleSpeedId, settings.twinkleSpeed);

            renderer.sharedMaterial = starMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void BuildGalaxy(AtmosphereProfile.GalaxySettings settings, float radius, float effectScale)
        {
            if (settings == null || !settings.enabled || effectScale <= 0f) return;

            int segments = Mathf.Clamp(settings.segments, 32, 256);
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            Vector3 bandU = Vector3.Normalize(Vector3.Cross(BandAxis, Vector3.right));
            Vector3 bandV = Vector3.Cross(BandAxis, bandU);
            float halfWidth = settings.width * 0.5f;
            float distance = radius * 0.985f;

            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float angle = u * Mathf.PI * 2f;
                Vector3 centre = bandU * Mathf.Cos(angle) + bandV * Mathf.Sin(angle);
                vertices[i * 2] = (centre * Mathf.Cos(halfWidth) - BandAxis * Mathf.Sin(halfWidth)).normalized * distance;
                vertices[i * 2 + 1] = (centre * Mathf.Cos(halfWidth) + BandAxis * Mathf.Sin(halfWidth)).normalized * distance;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);

                if (i == segments) continue;
                int vertex = i * 2;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }

            galaxyMesh = new Mesh { name = "Atmosphere Galaxy" };
            galaxyMesh.vertices = vertices;
            galaxyMesh.uv = uvs;
            galaxyMesh.triangles = triangles;
            galaxyMesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 4f);

            var galaxy = new GameObject("Atmosphere Galaxy");
            galaxy.transform.SetParent(generatedRoot, false);
            galaxy.AddComponent<MeshFilter>().sharedMesh = galaxyMesh;
            var renderer = galaxy.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Metagame/Atmosphere/Galaxy");
            if (shader == null || !shader.isSupported)
            {
                DestroyRuntimeObject(galaxy);
                return;
            }

            galaxyMaterial = new Material(shader) { name = "Atmosphere Galaxy (Runtime)" };
            galaxyMaterial.SetColor("_CoreColor", settings.coreColor);
            galaxyMaterial.SetColor("_EdgeColor", settings.edgeColor);
            galaxyMaterial.SetFloat("_Intensity", settings.intensity * effectScale);
            if (settings.texture != null)
                galaxyMaterial.SetTexture("_MainTex", settings.texture);
            renderer.sharedMaterial = galaxyMaterial;
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
            if (galaxyMesh != null)
            {
                DestroyRuntimeObject(galaxyMesh);
                galaxyMesh = null;
            }
            if (galaxyMaterial != null)
            {
                DestroyRuntimeObject(galaxyMaterial);
                galaxyMaterial = null;
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
