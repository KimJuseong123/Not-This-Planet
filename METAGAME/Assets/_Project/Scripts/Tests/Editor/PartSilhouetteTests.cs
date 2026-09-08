using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>굳힌 단면 표가 실제 에셋 형태를 담고 있는지</b> 검증한다
    /// (기획 §11.1, S15P21B202-345).
    ///
    /// 표는 <c>Editor/PartSilhouetteProbe.cs</c> 가 프리팹 메시를 40구간으로 잘라 재서
    /// 만든 것이다. 여기서 다시 재지는 않는다 — EditMode 에서 프리팹을 불러 재려면
    /// 에디터 어셈블리를 참조해야 하고, 그러면 검증이 측정 도구와 같은 것을 두 번 한다.
    ///
    /// 대신 <b>표가 형태로 성립하는지</b>와 <b>파츠를 구분할 수 있는지</b>를 본다. 형태를
    /// 보고 어느 파츠인지 알아볼 수 없으면 태블릿이 상태표로 쓸모가 없다.
    /// </summary>
    public sealed class PartSilhouetteTests
    {
        [Test]
        public void EveryPartHasASilhouette()
        {
            for (var i = 0; i < PartSpec.All.Length; i++)
            {
                var spec = PartSpec.All[i];
                Assert.IsNotNull(PartSilhouette.Find(spec.Name),
                    $"{spec.DisplayName}({spec.Name}) 의 단면이 없다. "
                    + "태블릿이 그 층을 빈칸으로 그린다.");
            }
        }

        [Test]
        public void TheTableIsInTheSameOrderAsTheAssemblyTable()
        {
            Assert.AreEqual(PartSpec.All.Length, PartSilhouette.All.Length,
                "단면 표와 파츠 표의 개수가 다르면 층이 어긋난다.");

            for (var i = 0; i < PartSilhouette.All.Length; i++)
                Assert.AreEqual(PartSpec.All[i].Name, PartSilhouette.All[i].PartName,
                    $"{i + 1}층의 단면이 다른 파츠의 것이다. 기획 §7.1 의 순서를 따라야 한다.");
        }

        /// <summary>
        /// 좌우가 뒤집히면 도형이 꼬여 메시가 안팎으로 접힌다.
        /// </summary>
        [Test]
        public void TheLeftEdgeIsNeverRightOfTheRightEdge()
        {
            foreach (var silhouette in PartSilhouette.All)
                for (var b = 0; b < PartSilhouette.Bands; b++)
                    Assert.LessOrEqual(silhouette.Left[b], silhouette.Right[b],
                        $"{silhouette.PartName} 의 {b}번 구간에서 왼쪽이 오른쪽보다 오른쪽에 있다.");
        }

        [Test]
        public void BothEdgesHaveOneValuePerBand()
        {
            foreach (var silhouette in PartSilhouette.All)
            {
                Assert.AreEqual(PartSilhouette.Bands, silhouette.Left.Length,
                    $"{silhouette.PartName} 의 왼쪽 구간 수가 다르다.");
                Assert.AreEqual(PartSilhouette.Bands, silhouette.Right.Length,
                    $"{silhouette.PartName} 의 오른쪽 구간 수가 다르다.");
            }
        }

        /// <summary>
        /// 정규화의 뜻이다 — 가장 넓은 구간의 폭이 1 이다. 이 값이 파츠마다 다르면
        /// 태블릿이 폭을 미터로 환산할 때 비율이 어긋난다.
        /// </summary>
        [Test]
        public void TheWidestBandIsNormalizedToOne()
        {
            foreach (var silhouette in PartSilhouette.All)
                Assert.AreEqual(1f, silhouette.NormalizedWidth, 0.01f,
                    $"{silhouette.PartName} 의 가장 넓은 구간이 1 이 아니다.");
        }

        /// <summary>
        /// <b>여기가 핵심이다.</b> 콜라이더 상자로 그리던 예전 실루엣은 파츠당 2~4단이라
        /// 계단으로 보였다. 40구간이 실제로 형태를 담고 있다면 <b>폭이 여러 번 변해야</b>
        /// 한다 — 상자 개수보다 많은 단계가 나오는 것이 그 증거다.
        /// </summary>
        [Test]
        public void TheOutlineHasMoreDetailThanTheColliderBoxes()
        {
            foreach (var silhouette in PartSilhouette.All)
            {
                var spec = PartSpec.Find(silhouette.PartName);
                Assert.IsNotNull(spec);

                var steps = 0;
                for (var b = 1; b < PartSilhouette.Bands; b++)
                {
                    var before = silhouette.Right[b - 1] - silhouette.Left[b - 1];
                    var after = silhouette.Right[b] - silhouette.Left[b];
                    if (Mathf.Abs(after - before) > 0.005f) steps++;
                }

                Assert.Greater(steps, spec.Boxes.Length,
                    $"{spec.DisplayName} 의 단면이 콜라이더 상자 {spec.Boxes.Length}개보다 "
                    + "단순하다. 그러면 상자로 그리던 것과 다를 것이 없다.");
            }
        }

        /// <summary>
        /// 상단 조립 모듈은 첨탑이 있다 — 꼭대기가 가장 넓은 곳의 절반도 되지 않아야
        /// 한다. 이것이 무너지면 표가 실제 에셋에서 온 것이 아니다.
        /// </summary>
        [Test]
        public void TheTopModuleKeepsItsSpire()
        {
            var silhouette = PartSilhouette.Find("Part_4");
            Assert.IsNotNull(silhouette);

            var top = silhouette.Right[PartSilhouette.Bands - 1]
                      - silhouette.Left[PartSilhouette.Bands - 1];

            Assert.Less(top, 0.5f,
                $"상단 조립 모듈 꼭대기 폭이 {top:0.00} 이다. 첨탑이 사라졌다.");
        }

        /// <summary>
        /// 주 추진 엔진 모듈은 <b>아래가 좁은 받침</b>이다(<see cref="PartSpec"/> 주석의
        /// "아래가 좁은 받침, 가운데가 가장 넓고, 위로 좁아진다").
        /// </summary>
        [Test]
        public void TheEngineModuleKeepsItsNarrowFoot()
        {
            var silhouette = PartSilhouette.Find("Part_1");
            Assert.IsNotNull(silhouette);

            var foot = silhouette.Right[0] - silhouette.Left[0];
            var widest = 0f;
            for (var b = 0; b < PartSilhouette.Bands; b++)
                widest = Mathf.Max(widest, silhouette.Right[b] - silhouette.Left[b]);

            Assert.Less(foot, widest * 0.6f,
                $"엔진 모듈 바닥 폭이 {foot:0.00} 로 가장 넓은 {widest:0.00} 에 가깝다. "
                + "좁은 받침이 사라졌다.");
        }

        /// <summary>
        /// 네 파츠의 형태가 서로 달라야 태블릿에서 구분된다. 폭 변화의 흐름을 비교한다.
        /// </summary>
        [Test]
        public void TheFourPartsLookDifferentFromEachOther()
        {
            for (var a = 0; a < PartSilhouette.All.Length; a++)
            for (var b = a + 1; b < PartSilhouette.All.Length; b++)
            {
                var left = PartSilhouette.All[a];
                var right = PartSilhouette.All[b];

                var difference = 0f;
                for (var band = 0; band < PartSilhouette.Bands; band++)
                {
                    var one = left.Right[band] - left.Left[band];
                    var other = right.Right[band] - right.Left[band];
                    difference += Mathf.Abs(one - other);
                }

                difference /= PartSilhouette.Bands;

                Assert.Greater(difference, 0.03f,
                    $"{left.PartName} 과 {right.PartName} 의 형태가 거의 같다"
                    + $"(평균 폭 차이 {difference:0.000}). 태블릿에서 구분되지 않는다.");
            }
        }

        /// <summary>
        /// 미터는 <see cref="PartSpec"/> 이 갖는다. 단면 표에 크기가 섞여 들면 모델이
        /// 바뀔 때 두 값이 어긋난다.
        /// </summary>
        [Test]
        public void SizeStillComesFromTheSpecTable()
        {
            Assert.Greater(PartSpec.StackedHeight, 7f,
                "네 층 전체 높이가 기획 §7.1 의 약 8.2m 와 크게 다르다.");

            for (var i = 0; i < PartSpec.All.Length; i++)
            {
                Assert.Greater(PartSpec.All[i].Height, 0f);
                Assert.Greater(PartSpec.All[i].Footprint.x, 0f);
            }
        }
    }
}
