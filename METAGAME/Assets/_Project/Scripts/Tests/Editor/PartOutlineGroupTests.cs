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
                new[] { Link(a, b, intact: true, weldRatio: 1f) }, groups);

            Assert.AreEqual(1, count, "용접으로 묶였으면 한 덩어리다.");
            Assert.AreEqual(groups[a], groups[b],
                "같은 번호라야 둘 사이에 선이 사라져 하나의 윤곽이 된다.");
        }

        /// <summary>
        /// <b>조금만 용접해도 합쳐진다.</b>
        ///
        /// <para>처음에는 <c>IsFullyWelded</c>(비율 1)를 요구했는데, 그 1 은 비드 세 덩이를
        /// 다 채워야 나오는 값이라 <b>사람이 두 파츠를 이어 붙인 정도로는 한참 모자랐다</b> —
        /// 용접을 해도 테두리가 안 합쳐졌다(실측). 비율은 강도이지 "이어졌나" 가 아니다.</para>
        /// </summary>
        [Test]
        public void EvenATackWeldMerges()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: true, weldRatio: 0.15f) }, groups);

            Assert.AreEqual(groups[a], groups[b], "조금이라도 용접으로 이어졌으면 한 덩어리다.");
        }

        /// <summary>
        /// <b>얹어만 둔 것은 아직 따로다.</b> 소켓에 맞물리면 접합 자체는 생기지만
        /// (<c>PartAssembler</c>), 요구는 "용접건으로 결합했으면" 이다. 그래서
        /// <c>IsIntact</c> 만으로 가르지 않고 비드가 닿았는지를 본다.
        /// </summary>
        [Test]
        public void APlacedButUnweldedLinkDoesNotMerge()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: true, weldRatio: 0f) }, groups);

            Assert.AreNotEqual(groups[a], groups[b], "용접하지 않았으면 아직 둘이다.");
        }

        /// <summary>절단해 링크가 끊기면 다시 갈라져야 한다.</summary>
        [Test]
        public void ACutLinkSplitsThemAgain()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();

            PartOutlineGroups.Assign(new[] { a, b },
                new[] { Link(a, b, intact: false, weldRatio: 1f) }, groups);

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
                new[] { Link(a, b, true, 1f), Link(b, c, true, 1f) }, groups);

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
            var links = new[] { Link(a, c, true, 1f) };

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

        // ── 혼자인 파츠는 테두리가 없다 (S15P21B202-562) ──────

        /// <summary>
        /// <b>테두리는 "붙었다" 를 보여 주는 표시다.</b> 아직 혼자 굴러다니는 파츠에까지
        /// 두르면 온 화면이 테두리가 되어, 무엇이 결합된 것인지 오히려 안 보인다.
        /// </summary>
        [Test]
        public void ALonePartGetsNoOutline()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var groups = new Dictionary<SpaceshipPart, int>();
            PartOutlineGroups.Assign(new[] { a, b }, null, groups);

            var kept = PartOutlineGroups.KeepMergedOnly(groups);

            Assert.AreEqual(0, kept, "붙은 것이 없으면 그릴 덩어리도 없다.");
            Assert.IsEmpty(groups, "혼자인 파츠는 목록에서 빠져야 한다.");
        }

        [Test]
        public void AWeldedPairSurvivesAndTheLonePartDoesNot()
        {
            var a = Part("Part_1");
            var b = Part("Part_2");
            var lone = Part("Part_3");
            var groups = new Dictionary<SpaceshipPart, int>();
            PartOutlineGroups.Assign(new[] { a, b, lone },
                new[] { Link(a, b, true, 1f) }, groups);

            var kept = PartOutlineGroups.KeepMergedOnly(groups);

            Assert.AreEqual(1, kept, "묶인 덩어리 하나만 남는다.");
            Assert.IsTrue(groups.ContainsKey(a), "용접된 쪽은 남아야 한다.");
            Assert.IsTrue(groups.ContainsKey(b));
            Assert.IsFalse(groups.ContainsKey(lone), "혼자인 파츠는 빠져야 한다.");
        }

        /// <summary>
        /// 남은 번호를 다시 매기지 않는다. 마스크는 번호가 <b>다른지만</b> 보므로 빈 번호가
        /// 생겨도 되고, 다시 매기면 그때마다 값이 흔들려 테두리가 깜빡인다.
        /// </summary>
        [Test]
        public void TheSurvivingNumbersDoNotShift()
        {
            var a = Part("Part_1");
            var lone = Part("Part_2");
            var c = Part("Part_3");
            var groups = new Dictionary<SpaceshipPart, int>();
            PartOutlineGroups.Assign(new[] { a, lone, c },
                new[] { Link(a, c, true, 1f) }, groups);
            var before = groups[a];

            PartOutlineGroups.KeepMergedOnly(groups);

            Assert.AreEqual(before, groups[a], "살아남은 번호가 바뀌면 테두리가 깜빡인다.");
            Assert.AreEqual(groups[a], groups[c]);
        }

        [Test]
        public void KeepMergedOnlyHandlesEmptyInput()
        {
            Assert.AreEqual(0, PartOutlineGroups.KeepMergedOnly(null));
            Assert.AreEqual(0, PartOutlineGroups.KeepMergedOnly(new Dictionary<SpaceshipPart, int>()));
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
        PartAssemblyLink Link(SpaceshipPart moving, SpaceshipPart target,
                              bool intact, float weldRatio)
        {
            var joint = moving.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = target.GetComponent<Rigidbody>();

            var link = moving.gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(moving, target, joint);
            link.SetBeadWeldRatio(weldRatio);
            if (!intact) link.Release();
            return link;
        }
    }
}
