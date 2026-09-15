using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 별이 반짝이게 한 것을 지킨다 (S15P21B202-546).
    ///
    /// <para>반짝임은 <b>셰이더</b>가 한다. CPU 로 매 프레임 정점 색을 다시 쓰면 별 4400개
    /// = 정점 17600개를 매 프레임 올려야 해서, 화면에 보이는 것에 비해 너무 비싸다.</para>
    ///
    /// <para>별마다 다른 위상이 필요한데 <b>쓸 자리가 이미 있었다</b> — 정점 색의 알파는
    /// 불투명 패스라 버려지므로 지금까지 1 이 들어가던 빈칸이었다.</para>
    /// </summary>
    public sealed class StarTwinkleTests
    {
        const string ProfilePath = "Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset";

        static AtmosphereProfile ShippedProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
            Assert.NotNull(profile, "배포되는 분위기 프로파일이 없다");
            profile.Validate();
            return profile;
        }

        // ── 위상 ──────────────────────────────────────────────────────

        [Test]
        public void EveryPhaseIsInsideOneTurn()
        {
            for (int i = 0; i < 5000; i++)
            {
                float phase = AtmosphereBackdrop.TwinklePhaseAt(i);
                Assert.GreaterOrEqual(phase, 0f, $"#{i} 의 위상이 음수다");
                Assert.Less(phase, 1f, $"#{i} 의 위상이 한 바퀴를 넘었다");
            }
        }

        [Test]
        public void NeighbouringStarsDoNotBlinkTogether()
        {
            // 이웃한 번호가 이웃한 위상을 가지면 하늘에 물결무늬가 생긴다.
            float closest = 1f;
            for (int i = 0; i < 2000; i++)
            {
                float a = AtmosphereBackdrop.TwinklePhaseAt(i);
                float b = AtmosphereBackdrop.TwinklePhaseAt(i + 1);
                float gap = Mathf.Abs(a - b);
                closest = Mathf.Min(closest, Mathf.Min(gap, 1f - gap));
            }
            Assert.Greater(closest, 0.2f, $"이웃한 두 별의 위상이 {closest:F3} 밖에 안 떨어져 있다");
        }

        [Test]
        public void ThePhasesFillTheWholeTurn()
        {
            // 한쪽에 몰리면 하늘의 절반이 같은 박자로 깜빡인다.
            var buckets = new int[10];
            for (int i = 0; i < 4400; i++)
                buckets[Mathf.Clamp((int)(AtmosphereBackdrop.TwinklePhaseAt(i) * 10f), 0, 9)]++;

            Assert.Greater(buckets.Min(), 4400 / 10 * 0.8f,
                $"위상이 고르지 않다 — 열 칸에 {string.Join(" ", buckets)}");
        }

        // ── 별빛은 한 톨도 건드리지 않았는가 (!444 에서 저지른 실수) ────────

        [Test]
        public void AddingTwinkleDidNotReRollASingleStar()
        {
            // **이 값은 develop 에서 실제로 재서 옮긴 것이다** (2026-09-15).
            // 위상을 난수열에서 뽑았다면 그 뒤의 별이 전부 밀려 이 값이 달라진다.
            // !444 에서 색을 다른 흐름으로 옮기며 별빛을 조용히 다시 굴린 적이 있다.
            var settings = ShippedProfile().stars;
            var stars = AtmosphereBackdrop.BuildStarPoints(settings, settings.starCount, settings.bandCount);

            Assert.AreEqual(4400, stars.Length);

            void Same(int index, float x, float y, float z, float brightness, float tint)
            {
                var s = stars[index];
                Assert.AreEqual(x, s.Direction.x, 1e-4f, $"#{index} 의 방향 x 가 달라졌다");
                Assert.AreEqual(y, s.Direction.y, 1e-4f, $"#{index} 의 방향 y 가 달라졌다");
                Assert.AreEqual(z, s.Direction.z, 1e-4f, $"#{index} 의 방향 z 가 달라졌다");
                Assert.AreEqual(brightness, s.Brightness, 1e-4f, $"#{index} 의 밝기가 달라졌다");
                Assert.AreEqual(tint, s.TintRoll, 1e-4f, $"#{index} 의 색이 달라졌다");
            }

            Same(0, 0.860971f, 0.185199f, 0.473742f, 0.105571f, 0.422240f);
            Same(2, -0.112793f, -0.211567f, -0.970833f, 0.354389f, 0.216072f);
            Same(1499, 0.869335f, 0.491004f, 0.056318f, 0.100152f, 0.434078f);
            Same(3500, 0.956757f, -0.238978f, 0.165848f, 0.716256f, 0.115432f);
            Same(4399, -0.607140f, -0.235398f, -0.758926f, 0.100001f, 0.204628f);
        }

        // ── 메시까지 실제로 실려 가는가 ────────────────────────────────

        static Mesh BuiltStarMesh(GameObject host)
        {
            foreach (var filter in host.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.sharedMesh.name == "Atmosphere Stars")
                    return filter.sharedMesh;
            return null;
        }

        [Test]
        public void TheMeshCarriesADifferentPhaseForEachStar()
        {
            var host = new GameObject("~twinkle test");
            var savedSkybox = RenderSettings.skybox;
            try
            {
                var backdrop = host.AddComponent<AtmosphereBackdrop>();
                backdrop.ApplyStarsOnly(ShippedProfile());

                var mesh = BuiltStarMesh(host);
                Assert.NotNull(mesh, "별 메시가 만들어지지 않았다");

                var colors = mesh.colors;
                Assert.Greater(colors.Length, 0, "정점 색이 없다");

                // 한 별의 네 꼭짓점은 같은 위상이어야 한다 — 다르면 사각형 안에서 번진다.
                for (int vertex = 0; vertex + 3 < colors.Length; vertex += 4)
                    for (int k = 1; k < 4; k++)
                        Assert.AreEqual(colors[vertex].a, colors[vertex + k].a, 1e-5f,
                            $"별 하나의 꼭짓점끼리 위상이 다르다 (정점 {vertex})");

                // 별끼리는 달라야 한다.
                var distinct = new System.Collections.Generic.HashSet<float>();
                for (int vertex = 0; vertex < colors.Length; vertex += 4)
                    distinct.Add(Mathf.Round(colors[vertex].a * 1000f));
                Assert.Greater(distinct.Count, 500,
                    $"위상이 {distinct.Count}가지뿐이다 — 별이 무리지어 깜빡인다");

                // 예전에는 여기에 1 이 들어갔다. 전부 1 이면 아무도 안 반짝인다.
                Assert.Less(distinct.Count(v => Mathf.Approximately(v, 1000f)), 10,
                    "알파가 아직 1 로 채워져 있다 — 위상이 실리지 않았다");
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheStarMaterialAsksForTheProfileAmount()
        {
            var host = new GameObject("~twinkle material test");
            var savedSkybox = RenderSettings.skybox;
            try
            {
                var profile = ShippedProfile();
                var backdrop = host.AddComponent<AtmosphereBackdrop>();
                backdrop.ApplyStarsOnly(profile);

                var renderer = host.GetComponentsInChildren<MeshRenderer>(true)
                    .FirstOrDefault(r => r.GetComponent<MeshFilter>() != null
                                      && r.GetComponent<MeshFilter>().sharedMesh != null
                                      && r.GetComponent<MeshFilter>().sharedMesh.name == "Atmosphere Stars");
                Assert.NotNull(renderer, "별 렌더러를 못 찾았다");

                var material = renderer.sharedMaterial;
                Assert.IsTrue(material.HasProperty("_TwinkleAmount"),
                    "별 머티리얼의 셰이더에 반짝임이 없다 — 대체 셰이더로 떨어졌는지 본다");
                Assert.AreEqual(profile.stars.twinkleAmount, material.GetFloat("_TwinkleAmount"), 1e-4f,
                    "프로파일의 반짝임 깊이가 머티리얼에 전달되지 않았다");
                Assert.AreEqual(profile.stars.twinkleSpeed, material.GetFloat("_TwinkleSpeed"), 1e-4f,
                    "프로파일의 반짝임 빠르기가 머티리얼에 전달되지 않았다");
                Assert.Greater(profile.stars.twinkleAmount, 0f,
                    "배포 설정의 반짝임이 0 이다 — 별이 안 반짝인다");
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheSharedShaderStaysStillForEverybodyElse()
        {
            // FlatUnlit 은 별뿐 아니라 "distant markers" 에도 쓰인다. 기본값이 0 이어야
            // 그쪽이 한 픽셀도 바뀌지 않는다.
            var shader = Resources.Load<Shader>("FlatUnlit");
            Assert.NotNull(shader, "FlatUnlit 셰이더를 못 찾았다");

            var material = new Material(shader);
            try
            {
                Assert.AreEqual(0f, material.GetFloat("_TwinkleAmount"), 1e-6f,
                    "반짝임 기본값이 0 이 아니다 — 별이 아닌 것들까지 깜빡인다");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        // ── 별만 만든다는 약속 ─────────────────────────────────────────

        [Test]
        public void StarsOnlyLeavesTheAuthoredSkyAlone()
        {
            var host = new GameObject("~stars only test");
            var marker = new Material(Shader.Find("Unlit/Color")) { name = "수제 하늘" };
            var savedSkybox = RenderSettings.skybox;
            try
            {
                RenderSettings.skybox = marker;
                host.AddComponent<AtmosphereBackdrop>().ApplyStarsOnly(ShippedProfile());

                Assert.AreSame(marker, RenderSettings.skybox,
                    "별만 얹겠다고 해 놓고 스카이박스를 덮었다 — 아트가 맞춘 하늘색이 사라진다");
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void StarsOnlyDoesNotAddASecondPlanet()
        {
            var host = new GameObject("~no planet test");
            var savedSkybox = RenderSettings.skybox;
            try
            {
                host.AddComponent<AtmosphereBackdrop>().ApplyStarsOnly(ShippedProfile());

                var names = host.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                CollectionAssert.DoesNotContain(names, "Atmosphere Planet",
                    "수제 맵에 이미 천체 아트가 있을 수 있다 — 겹치면 두 개로 보인다");
                CollectionAssert.DoesNotContain(names, "Atmosphere Moon");
                CollectionAssert.Contains(names, "Atmosphere Stars", "별이 안 만들어졌다");
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheFullApplyStillBuildsEverything()
        {
            // 별만 만드는 길을 내면서 원래 길을 부수지 않았는지 본다.
            var host = new GameObject("~full apply test");
            var savedSkybox = RenderSettings.skybox;
            try
            {
                var backdrop = host.AddComponent<AtmosphereBackdrop>();
                backdrop.Apply(ShippedProfile());

                var names = host.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                CollectionAssert.Contains(names, "Atmosphere Stars");
                CollectionAssert.Contains(names, "Atmosphere Planet");
                Assert.AreNotSame(savedSkybox, RenderSettings.skybox,
                    "전체 적용인데 하늘을 안 만들었다");

                backdrop.Clear();
                Assert.AreSame(savedSkybox, RenderSettings.skybox,
                    "Clear 가 원래 하늘을 되돌리지 않았다");
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                Object.DestroyImmediate(host);
            }
        }
    }
}
