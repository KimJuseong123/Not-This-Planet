using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 용접으로 묶인 파츠가 <b>같은 덩어리 번호</b>를 받는지 검증한다 (S15P21B202-562).
    ///
    /// <para><b>왜 이것이 기능의 전부인가.</b> 테두리는 마스크의 번호가 바뀌는 자리에만
    /// 생긴다. 그래서 "용접하면 테두리가 하나로 합쳐진다" 는 곧 "용접하면 두 파츠가 같은
    /// 번호를 받는다" 다. 그림을 검증할 수는 없어도 <b>이 판단은 값으로 잠글 수
    /// 있다.</b></para>
    ///
    /// <para>링크는 실물 대신 대역을 쓴다 — <c>PartAssemblyLink</c> 는 조인트와 소켓을
    /// 걸고 만들어지는 물건이라, 번호 매기기만 보려는 검증이 그 준비를 다 할 이유가 없다.
    /// 그래서 <see cref="PartOutlineGroups.Assign"/> 은 목록을 인수로 받는다.</para>
    /// </summary>
    public sealed class PartOutlineGroupTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => PartAssemblyLink.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            // 링크는 스스로 목록에 들어간다. 안 비우면 다음 검증이 남의 링크를 본다.
            PartAssemblyLink.All.Clear();
            WeldBead.All.Clear();
        }

        [Test]
        public void SeparatePartsGetSeparateGroups()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            var count = PartOutlineGroups.Assign(new[] { a, b }, null, groups);

            Assert.AreEqual(2, count, "붙지 않았으면 덩어리가 둘이다.");
            Assert.AreNotEqual(groups[a], groups[b],
                "번호가 같으면 두 파츠 사이에 선이 안 생겨 붙은 것처럼 보인다.");
        }

        /// <summary><b>이 검증이 이 기능 자체다.</b></summary>
        [Test]
        public void FullyWeldedPartsShareOneGroup()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            var count = PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: true, welded: true) }, groups);

            Assert.AreEqual(1, count, "용접으로 묶였으면 한 덩어리다.");
            Assert.AreEqual(groups[a], groups[b],
                "같은 번호라야 둘 사이에 선이 사라져 하나의 윤곽이 된다.");
        }

        /// <summary>
        /// 얹어만 둔 것은 아직 따로 보여야 한다. 기획 §16 의 용접이 끝나야 하나가 된다 —
        /// <c>Placed</c> 는 "충격에 흔들리거나 무너질 수 있다" 인 상태다.
        /// </summary>
        [Test]
        public void APartiallyWeldedLinkDoesNotMerge()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: true, welded: false) }, groups);

            Assert.AreNotEqual(groups[a], groups[b], "용접이 덜 됐으면 아직 둘이다.");
        }

        /// <summary>절단해 링크가 끊기면 다시 갈라져야 한다.</summary>
        [Test]
        public void ACutLinkSplitsThemAgain()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: false, welded: true) }, groups);

            Assert.AreNotEqual(groups[a], groups[b], "끊긴 링크는 묶지 않는다.");
        }

        /// <summary>세 파츠가 사슬로 이어지면 셋이 한 덩어리다 — 유니온-파인드가 옳게 도는가.</summary>
        [Test]
        public void AChainOfWeldsBecomesOneGroup()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var c = Part("Part_3");
            var groups = new Dictionary<SpaceshipPart, int>();

            var count = PartOutlineGroups.Assign(new[] { a, b, c },
                new[] { Link(a, b, true, true), Link(b, c, true, true) }, groups);

            Assert.AreEqual(1, count);
            Assert.AreEqual(groups[a], groups[c], "a-b, b-c 가 붙었으면 a 와 c 도 한 덩어리다.");
        }

        /// <summary>
        /// 같은 구성이면 같은 번호가 나와야 한다. 프레임마다 번호가 뒤바뀌면 테두리가
        /// 깜빡인다.
        /// </summary>
        [Test]
        public void TheSameLayoutGivesTheSameNumbers()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var c = Part("Part_3");
            var parts = new[] { a, b, c };
            var links = new[] { Link(a, c, true, true) };

            var first = new Dictionary<SpaceshipPart, int>();
            var second = new Dictionary<SpaceshipPart, int>();
            PartOutlineGroups.Assign(parts, links, first);
            PartOutlineGroups.Assign(parts, links, second);

            foreach (var part in parts)
                Assert.AreEqual(first[part], second[part], part.name + " 의 번호가 흔들린다.");
        }

        [Test]
        public void NullsAndDuplicatesDoNotBreakIt()
        {
            var a = Part("Part_1");
            var groups = new Dictionary<SpaceshipPart, int>();

            Assert.DoesNotThrow(() =>
                PartOutlineGroups.Assign(new[] { a, null, a }, new PartAssemblyLink[] { null }, groups));
            Assert.AreEqual(1, groups.Count, "같은 파츠를 두 번 세지 않는다.");
        }

        [Test]
        public void NoPartsMeansNoGroups()
        {
            var groups = new Dictionary<SpaceshipPart, int>();
            Assert.AreEqual(0, PartOutlineGroups.Assign(null, null, groups));
            Assert.AreEqual(0, PartOutlineGroups.Assign(new SpaceshipPart[0], null, groups));
        }

        // ── 도구 ────────────────────────────────────────────────

        SpaceshipPart Part(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.AddComponent<Rigidbody>();
            return go.AddComponent<SpaceshipPart>();
        }

        /// <summary>
        /// 실물 링크를 만든다. <c>DemoEndingTests.Join</c> 과 같은 방법이다 — 검증용
        /// 샛길을 프로덕션에 뚫지 않는다.
        /// </summary>
        PartAssemblyLink Link(SpaceshipPart moving, SpaceshipPart target, bool intact, bool welded)
        {
            var joint = moving.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target.GetComponent<Rigidbody>();

            var link = moving.gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(moving, target, joint);
            link.SetBeadWeldRatio(welded ? 1f : 0.4f);
            if (!intact) link.Release();
            return link;
        }
    }
}
