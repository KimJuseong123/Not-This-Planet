using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠별 물리 명세를 검증한다.
    ///
    /// 예전에는 네 파츠가 모두 같은 3 x 3.7 x 3m 상자와 같은 질량(500kg)을 썼다. 상세 기획
    /// (S15P21B202-107 §7.1 · §25.3)이 그것을 금지한다. 같은 상자를 쓰면 비뚤어진 적층이
    /// 만들어지지 않고, 그러면 §22.3 발사 하중 시험과 §22.4 붕괴가 일어날 수 없다.
    ///
    /// 여기서 지키는 것은 네 가지다 — 파츠마다 크기와 질량이 다른가, 상자들이 실제 모델을
    /// 빈틈없이 따르는가, 합산 높이가 기획값과 맞는가, 중심 질량이 외형을 따르는가.
    /// </summary>
    public sealed class PartSpecTests
    {
        /// <summary>기획 §7.1 이 적은 네 파츠의 합산 높이.</summary>
        const float PlannedStackHeight = 8.2f;

        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        [Test]
        public void EveryPartHasItsOwnSizeAndMass()
        {
            Assert.AreEqual(4, PartSpec.All.Length, "기획 §7.1 은 파츠를 네 개로 정한다.");

            var heights = new HashSet<float>();
            var masses = new HashSet<float>();

            foreach (var spec in PartSpec.All)
            {
                heights.Add(Mathf.Round(spec.Height * 100f));
                masses.Add(Mathf.Round(spec.Mass));

                Assert.Greater(spec.Height, 0.5f, $"{spec.Name} 높이가 비정상이다.");
                Assert.Greater(spec.Mass, 1f, $"{spec.Name} 질량이 비정상이다.");
            }

            Assert.AreEqual(4, heights.Count,
                "네 파츠의 높이가 모두 달라야 한다. 같으면 예전의 공용 상자로 되돌아간 것이다.");
            Assert.AreEqual(4, masses.Count,
                "네 파츠의 질량이 모두 달라야 한다. 같으면 PartMass 고정값으로 되돌아간 것이다.");
        }

        /// <summary>
        /// 상자들이 바닥부터 꼭대기까지 빈틈이나 겹침 없이 이어져야 한다. 어긋나면 콜라이더에
        /// 구멍이 생기거나 부피가 부풀어 질량이 틀어진다.
        /// </summary>
        [Test]
        public void BoxesFormAContinuousStack()
        {
            foreach (var spec in PartSpec.All)
            {
                Assert.Greater(spec.Boxes.Length, 0, $"{spec.Name} 에 상자가 없다.");
                Assert.AreEqual(0f, spec.Boxes[0].Bottom, 0.001f,
                    $"{spec.Name} 의 첫 상자는 바닥(0)에서 시작해야 한다.");

                for (var i = 0; i < spec.Boxes.Length; i++)
                {
                    var box = spec.Boxes[i];
                    Assert.Greater(box.Height, 0f, $"{spec.Name} 상자 {i} 의 높이가 0 이하다.");
                    Assert.Greater(box.Width, 0f, $"{spec.Name} 상자 {i} 의 가로가 0 이하다.");
                    Assert.Greater(box.Depth, 0f, $"{spec.Name} 상자 {i} 의 세로가 0 이하다.");

                    if (i == 0) continue;

                    Assert.AreEqual(spec.Boxes[i - 1].Top, box.Bottom, 0.001f,
                        $"{spec.Name} 상자 {i - 1} 의 윗면과 상자 {i} 의 밑면이 어긋난다.");
                }
            }
        }

        [Test]
        public void StackedHeightMatchesPlan()
        {
            Assert.AreEqual(PlannedStackHeight, PartSpec.StackedHeight, 0.2f,
                $"네 파츠를 쌓은 높이가 {PartSpec.StackedHeight:0.00}m 다. "
                + "기획 §7.1 은 약 8.2m 라고 적었다.");
        }

        [Test]
        public void MassComesFromVolumeAndDensity()
        {
            foreach (var spec in PartSpec.All)
                Assert.AreEqual(spec.Volume * PhysicsScale.PartDensity, spec.Mass, 0.01f,
                    $"{spec.Name} 의 질량이 부피 x 밀도가 아니다. 손으로 적은 값이 섞이면 "
                    + "모델이 바뀔 때 서로의 비율이 어긋난다.");
        }

        /// <summary>
        /// 기획 §25.3 은 중심 질량이 외형에 맞게 작동해야 한다고 정한다.
        ///
        /// 값을 손으로 넣지 않고 Unity 가 콜라이더에서 계산하게 두었으므로, 실제로 그렇게
        /// 되는지 확인한다. 견인 조종 모듈은 접시가 아래에 있고 위로 갈수록 가늘어지므로
        /// 중심 질량이 기하학적 한가운데보다 <b>아래</b>에 있어야 한다.
        /// </summary>
        [Test]
        public void CenterOfMassFollowsShape()
        {
            var spec = PartSpec.Find("Part_4");
            Assert.IsNotNull(spec, "견인 조종 모듈 명세가 있어야 한다.");

            var body = BuildBody(spec);

            Assert.Less(body.centerOfMass.y, -0.15f,
                $"중심 질량이 {body.centerOfMass.y:0.000} 다. 파츠 중심(0)보다 뚜렷하게 아래여야 "
                + "접시형 외형을 따른 것이다. 0 근처면 상자 하나짜리 콜라이더로 되돌아간 것이다.");
        }

        /// <summary>납작한 파츠와 높은 파츠의 중심 질량이 서로 달라야 한다.</summary>
        [Test]
        public void CenterOfMassDiffersBetweenParts()
        {
            var craft = BuildBody(PartSpec.Find("Part_4")).centerOfMass.y;
            var pod = BuildBody(PartSpec.Find("Part_2")).centerOfMass.y;

            Assert.AreNotEqual(Mathf.Round(craft * 100f), Mathf.Round(pod * 100f),
                "형태가 다른 두 파츠의 중심 질량이 같다 — 콜라이더가 실제 형태를 따르지 않는다.");
        }

        /// <summary>명세의 상자들로 실제 강체를 만든다. 중심 질량은 Unity 가 계산한다.</summary>
        Rigidbody BuildBody(PartSpec spec)
        {
            var root = new GameObject(spec.Name);
            spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body");
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.CenterFromPartCenter(spec.Height);

                var collider = child.AddComponent<BoxCollider>();
                collider.size = box.Size;
            }

            var body = root.AddComponent<Rigidbody>();
            body.mass = spec.Mass;
            return body;
        }
    }
}
