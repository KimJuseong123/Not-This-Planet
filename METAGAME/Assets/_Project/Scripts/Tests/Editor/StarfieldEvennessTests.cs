using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 하늘의 별이 고르게 있는지 검증한다 (S15P21B202-530).
    ///
    /// <para><b>무엇이 문제였나.</b> "별이 한쪽에만 몰려있다" 를 실측했더니 화면 하나에
    /// 들어오는 별이 방위각에 따라 <b>397개에서 136개</b>로 변했다(2.9배). 그런데
    /// <b>차이는 전부 은하수 띠에서 나왔다</b> — 일반별은 126~174로 고르다.</para>
    ///
    /// <para><b>띠는 결함이 아니다.</b> 축이 위에서 30.8도 기울어 있어 대원이 지평선 위로
    /// 올라갔다 내려간다 — 실제 은하수가 그렇다. <b>문제는 띠가 없는 쪽에 남는 136개가
    /// 대부분 1픽셀이라 검은 하늘로 보이는 것</b>이다.</para>
    ///
    /// <para>그래서 고친 방향은 <b>천장을 낮추는 것이 아니라 바닥을 올리는 것</b>이다 —
    /// 일반별을 늘리고 최소 크기를 키웠다.</para>
    ///
    /// <para><b>이전에는 하늘을 재는 검증이 0개였다.</b> 방향·밝기·크기를 정하는 일이
    /// 메시를 짜는 루프 안에만 있어 물어볼 방법이 없었다.</para>
    /// </summary>
    public sealed class StarfieldEvennessTests
    {
        const string ProfilePath = "Assets/_Project/Data/Atmosphere/Stage1AtmosphereProfile.asset";

        static AtmosphereProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
            Assert.IsNotNull(profile, "Stage1 분위기 프로필이 없다: " + ProfilePath);
            return profile;
        }

        static AtmosphereBackdrop.StarPoint[] Stars()
        {
            var settings = Profile().stars;
            return AtmosphereBackdrop.BuildStarPoints(settings, settings.starCount, settings.bandCount);
        }

        /// <summary>화면 하나(가로 90도·세로 50도, 20도 올려봄)에 들어오는 별 수.</summary>
        static int OnScreen(AtmosphereBackdrop.StarPoint[] stars, float yawDegrees)
        {
            const float halfWidth = 45f, halfHeight = 25f, pitchDegrees = 20f;
            float yaw = yawDegrees * Mathf.Deg2Rad, pitch = pitchDegrees * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), Mathf.Sin(pitch),
                                          Mathf.Cos(pitch) * Mathf.Sin(yaw));
            Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.up, forward));
            Vector3 up = Vector3.Cross(forward, right);

            var count = 0;
            foreach (var star in stars)
            {
                if (Vector3.Dot(star.Direction, forward) <= 0f) continue;
                float x = Mathf.Atan2(Vector3.Dot(star.Direction, right),
                                      Vector3.Dot(star.Direction, forward)) * Mathf.Rad2Deg;
                float y = Mathf.Atan2(Vector3.Dot(star.Direction, up),
                                      Vector3.Dot(star.Direction, forward)) * Mathf.Rad2Deg;
                if (Mathf.Abs(x) <= halfWidth && Mathf.Abs(y) <= halfHeight) count++;
            }
            return count;
        }

        // ── 고르게 있는가 ───────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b></summary>
        [Test]
        public void NoDirectionOfTheSkyIsTwiceAsEmptyAsAnother()
        {
            var stars = Stars();
            var perScreen = Enumerable.Range(0, 8).Select(i => OnScreen(stars, i * 45f)).ToArray();

            var ratio = (float)perScreen.Max() / perScreen.Min();
            Assert.LessOrEqual(ratio, 2.0f,
                $"방향에 따라 별 수가 {ratio:F2}배 차이 난다 — 한쪽이 비어 보인다. "
                + "화면별: " + string.Join(" ", perScreen));
        }

        /// <summary>
        /// 가장 빈 화면에도 별이 충분히 있어야 한다. <b>비율만 보면 전부 비어도 통과한다.</b>
        /// </summary>
        [Test]
        public void EvenTheEmptiestScreenHasEnoughStars()
        {
            var stars = Stars();
            var emptiest = Enumerable.Range(0, 8).Select(i => OnScreen(stars, i * 45f)).Min();

            Assert.GreaterOrEqual(emptiest, 250,
                $"가장 빈 화면에 별이 {emptiest}개뿐이다 — 1080p 화면에서 검은 하늘로 보인다.");
        }

        /// <summary>
        /// 은하수 띠는 <b>남겨야 한다</b>. 고르게 만든다고 띠를 없애면 하늘이 밋밋해진다.
        /// </summary>
        [Test]
        public void TheMilkyWayIsStillThere()
        {
            var stars = Stars();
            var band = stars.Where(s => s.InBand).ToArray();

            Assert.Greater(band.Length, 0, "띠가 사라지면 하늘이 밋밋해진다.");

            // 띠는 대원 근처에 몰려 있어야 한다 — 축과의 각이 90도 언저리다.
            var offAxis = band.Select(s => Mathf.Abs(90f - Vector3.Angle(s.Direction, AtmosphereBackdrop.BandAxis)))
                              .Average();
            Assert.Less(offAxis, 20f, $"띠가 대원에서 평균 {offAxis:F1}도나 벗어났다 — 띠로 안 보인다.");
        }

        // ── 보이는가 ────────────────────────────────────────────

        /// <summary>
        /// <b>가장 어두운 별도 한 픽셀보다 커야 한다.</b> 작으면 래스터에 안 걸려 깜빡이거나
        /// 아예 안 나온다 — 실측에서 절반 이상이 1.0~1.5픽셀이었다.
        /// </summary>
        [Test]
        public void TheFaintestStarIsStillBiggerThanAPixel()
        {
            var settings = Profile().stars;
            var smallest = Stars().Min(s => s.Size);

            var pixels = AtmosphereBackdrop.StarPixelSize(smallest, settings.radius, 60f, 1080f);
            Assert.GreaterOrEqual(pixels, 1.2f,
                $"가장 어두운 별이 {pixels:F2}픽셀이다 — 1픽셀 언저리는 사실상 안 보인다.");
        }

        [Test]
        public void ABiggerStarTakesMorePixels()
        {
            var small = AtmosphereBackdrop.StarPixelSize(0.1f, 300f, 60f, 1080f);
            var large = AtmosphereBackdrop.StarPixelSize(0.5f, 300f, 60f, 1080f);

            Assert.Greater(large, small);
            Assert.Zero(AtmosphereBackdrop.StarPixelSize(0.3f, 0f, 60f, 1080f), "반지름이 0이면 셈이 없다.");
        }

        /// <summary>멀리 두면 같은 별도 작아진다 — 반지름을 바꿀 때 크기도 따라가야 한다.</summary>
        [Test]
        public void TheSameStarShrinksWhenTheSkyIsFurther()
        {
            var near = AtmosphereBackdrop.StarPixelSize(0.2f, 300f, 60f, 1080f);
            var far = AtmosphereBackdrop.StarPixelSize(0.2f, 600f, 60f, 1080f);

            Assert.AreEqual(near * 0.5f, far, 1e-3f);
        }

        // ── 사각형이 카메라를 보는가 ────────────────────────────

        /// <summary>
        /// <b>별 사각형이 카메라를 마주 봐야 한다.</b>
        ///
        /// <para>예전 감김 순서는 면이 하늘 <b>바깥</b>을 보게 만들었다. 그래도 보였던 것은
        /// <c>FlatUnlit.shader</c> 가 <c>Cull Off</c> 였기 때문이다 — <b>재질을 바꾸는
        /// 순간 별이 통째로 사라지는데 아무도 막고 있지 않았다.</b></para>
        ///
        /// <para>유니티 자신의 규약(<c>RecalculateNormals</c>)으로 잰다.</para>
        /// </summary>
        [Test]
        public void TheStarQuadFacesTheCamera()
        {
            Vector3[] samples =
            {
                Vector3.forward, Vector3.right, Vector3.back, Vector3.left,
                new Vector3(0f, 0.99f, 0.14f).normalized,
                new Vector3(0f, -0.99f, 0.14f).normalized,
                new Vector3(0.5f, 0.6f, 0.6f).normalized,
            };

            foreach (var direction in samples)
            {
                var mesh = BuildOneQuad(direction, 300f, 0.3f);
                try
                {
                    mesh.RecalculateNormals();
                    var normal = mesh.normals[0];
                    Assert.Greater(Vector3.Dot(normal, -direction), 0.9f,
                        $"방향 {direction} 의 사각형이 카메라를 등지고 있다 — "
                        + "지금은 Cull Off 라 보이지만 재질을 바꾸면 사라진다.");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        /// <summary>사각형은 시선에 <b>수직</b>이어야 한다 — 비스듬하면 별이 타원으로 늘어난다.</summary>
        [Test]
        public void TheStarQuadIsSquareOnToTheView()
        {
            foreach (var direction in new[] { Vector3.forward, Vector3.up, new Vector3(0.3f, -0.8f, 0.5f).normalized })
            {
                AtmosphereBackdrop.StarQuadAxes(direction, out var right, out var up);

                Assert.AreEqual(0f, Vector3.Dot(right, direction), 1e-4f, "가로축이 시선에 수직이어야 한다.");
                Assert.AreEqual(0f, Vector3.Dot(up, direction), 1e-4f, "세로축이 시선에 수직이어야 한다.");
                Assert.AreEqual(0f, Vector3.Dot(right, up), 1e-4f, "두 축이 서로 수직이어야 한다.");
                Assert.AreEqual(1f, right.magnitude, 1e-3f);
                Assert.AreEqual(1f, up.magnitude, 1e-3f);
            }
        }

        /// <summary>극 근처에서도 축이 무너지지 않는다 — 외적이 0 이 되는 자리다.</summary>
        [Test]
        public void ItSurvivesLookingStraightUp()
        {
            AtmosphereBackdrop.StarQuadAxes(Vector3.up, out var right, out var up);

            Assert.AreEqual(1f, right.magnitude, 1e-3f, "바로 위에서도 가로축이 살아 있어야 한다.");
            Assert.AreEqual(1f, up.magnitude, 1e-3f);
        }

        // ── 만드는 쪽 ───────────────────────────────────────────

        [Test]
        public void TheSameSeedMakesTheSameSky()
        {
            var settings = Profile().stars;
            var first = AtmosphereBackdrop.BuildStarPoints(settings, 200, 100);
            var second = AtmosphereBackdrop.BuildStarPoints(settings, 200, 100);

            for (int i = 0; i < first.Length; i++)
                Assert.AreEqual(0f, Vector3.Distance(first[i].Direction, second[i].Direction), 1e-5f,
                    "같은 씨앗이면 같은 하늘이어야 한다 — 켤 때마다 별자리가 바뀌면 안 된다.");
        }

        /// <summary>
        /// <b>씨앗 말고는 아무것도 하늘을 바꾸지 못한다.</b>
        ///
        /// <para>위의 검증은 <b>한 실행 안에서</b> 두 번 부른 결과만 견준다. 그래서
        /// 씨앗에 시각 같은 것을 더해도 같은 밀리초 안에서는 통과한다 — 대조 실험에서
        /// 실제로 통과했다. 켤 때마다 별자리가 바뀌면 <b>스크린샷·영상이 매번 달라지고</b>
        /// 회귀를 눈으로 비교할 수 없다.</para>
        ///
        /// <para>그래서 <b>값을 못 박는다.</b> 생성 방식을 일부러 바꿀 때는 이 수도 함께
        /// 고쳐야 하고, 그때는 그것이 의도한 변경이라는 뜻이다.</para>
        /// </summary>
        [Test]
        public void TheSkyIsTheSameEveryRun()
        {
            var settings = Profile().stars;
            var stars = AtmosphereBackdrop.BuildStarPoints(settings, 3, 0);

            Vector3[] expected =
            {
                new Vector3(0.860971f, 0.185199f, 0.473742f),
                new Vector3(0.716988f, 0.538833f, 0.442253f),
                new Vector3(-0.112793f, -0.211567f, -0.970833f),
            };

            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(0f, Vector3.Distance(expected[i], stars[i].Direction), 1e-5f,
                    $"씨앗 {settings.seed} 의 {i}번째 별이 달라졌다 — 켤 때마다 하늘이 바뀐다.");
        }

        [Test]
        public void NoStarsMeansNoWork()
        {
            Assert.IsEmpty(AtmosphereBackdrop.BuildStarPoints(Profile().stars, 0, 0));
        }

        /// <summary>일반별은 구면에 고르게 뿌려져야 한다 — 띠와 달리 몰리면 안 된다.</summary>
        [Test]
        public void TheOrdinaryStarsAreSpreadOverTheWholeSphere()
        {
            var general = Stars().Where(s => !s.InBand).ToArray();

            var byOctant = new int[8];
            foreach (var star in general)
            {
                int octant = (star.Direction.x > 0f ? 1 : 0)
                           | (star.Direction.y > 0f ? 2 : 0)
                           | (star.Direction.z > 0f ? 4 : 0);
                byOctant[octant]++;
            }

            var ratio = (float)byOctant.Max() / byOctant.Min();
            Assert.LessOrEqual(ratio, 1.35f,
                "일반별이 8분면에 고르지 않다 — 구면 균등 표집이 깨졌다. " + string.Join(" ", byOctant));
        }

        /// <summary>
        /// <b>제품이 쓰는 함수로 사각형을 만든다.</b> 검증이 자기 순서를 따로 적으면
        /// 제품의 감김을 되돌려도 통과한다 — 그러면 아무것도 잠그지 못한다.
        /// </summary>
        static Mesh BuildOneQuad(Vector3 direction, float radius, float size)
        {
            var vertices = new Vector3[4];
            var triangles = new int[6];
            AtmosphereBackdrop.StarQuadVertices(direction, radius, size, vertices, 0);
            AtmosphereBackdrop.StarQuadTriangles(0, triangles, 0);

            return new Mesh { vertices = vertices, triangles = triangles };
        }
    }
}
