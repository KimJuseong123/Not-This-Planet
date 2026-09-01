using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 탑 판정이 소켓 사슬이 아니라 <b>접합 그래프</b>를 본다는 것을 검증한다
    /// (S15P21B202-229).
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>비드로만 이은 탑이 완성 판정을 통과한다.</item>
    /// <item>조립 순서(기획 §7.1)는 <b>높이</b>로 판정하며 그대로 강제된다.</item>
    /// <item>하중 시험이 그래프의 <b>모든</b> 접합을 본다.</item>
    /// <item>파츠 하나가 여러 쌍과 이어질 수 있다.</item>
    /// </list>
    ///
    /// 이 스위트가 새로 다루는 것만 여기 둔다. 소켓으로 쌓은 탑의 완성·탑승·하중은
    /// <see cref="ShipBoardingTests"/> · <see cref="LaunchTestTests"/> 가 이미 지키고,
    /// 그 둘이 통과하는 것이 이 이슈의 회귀 방어선이다.
    /// </summary>
    public sealed class PartGraphTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => ClearRegistries();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            ClearRegistries();
        }

        static void ClearRegistries()
        {
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();
            LowGravityField.All.Clear();
        }

        // --- 그래프 탐색 ---------------------------------------------------

        [Test]
        public void ABeadOnlyTower_IsCollectedBottomUp()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var tower = new List<SpaceshipPart>();
            PartGraph.Collect(parts[0], tower);

            Assert.AreEqual(PartSpec.All.Length, tower.Count, "비드로 이은 탑 전체를 모아야 한다.");

            for (var i = 0; i < tower.Count; i++)
                Assert.AreEqual(PartSpec.All[i].Name, tower[i].name,
                    $"{i + 1}번째가 아래에서 위로 정렬돼야 한다.");
        }

        /// <summary>
        /// 소켓으로 쌓은 탑도 같은 결과여야 한다. 예전 사슬 탐색과 결과가 달라지면
        /// 195·200·204 가 세운 것이 무너진다.
        /// </summary>
        [Test]
        public void ASocketTower_IsCollectedTheSameWay()
        {
            var parts = AssembledTower();

            var tower = new List<SpaceshipPart>();
            PartGraph.Collect(parts[0], tower);

            Assert.AreEqual(PartSpec.All.Length, tower.Count);
            for (var i = 0; i < tower.Count; i++)
                Assert.AreEqual(PartSpec.All[i].Name, tower[i].name);
        }

        [Test]
        public void AnUnjoinedPart_IsNotInTheTower()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            // 옆에 아무것에도 잇지 않은 파츠를 하나 세운다.
            var stray = MakePart(PartSpec.All[1], new Vector3(12f, PartSpec.All[1].Height * 0.5f, 0f));
            stray.name = "StrayPart";

            var tower = new List<SpaceshipPart>();
            PartGraph.Collect(parts[0], tower);

            Assert.IsFalse(tower.Contains(stray), "접합이 없는 파츠는 탑이 아니다.");
        }

        // --- 완성 판정 -----------------------------------------------------

        /// <summary>
        /// <b>이 이슈의 목표다.</b> S15P21B202-228 까지는 비드로 다 이어 놓아도 발사
        /// 버튼이 열리지 않았다.
        /// </summary>
        [Test]
        public void ABeadOnlyTower_Boards()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var assembly = MakeAssembly(Vector3.zero);
            assembly.Evaluate();

            Assert.IsTrue(assembly.IsBoardable, $"탑승 가능해야 한다. 막은 이유: {assembly.Blocker}");
            Assert.AreEqual(parts[0], assembly.Engine);
            Assert.AreEqual(PartSpec.All.Length, assembly.Tower.Count);
        }

        /// <summary>
        /// 조립 순서는 그대로 강제된다. 비드는 <b>붙이는 수단</b>이지 §7.1 을 우회하는
        /// 길이 아니다 — 재는 방법만 사슬에서 높이로 바뀌었다.
        /// </summary>
        [Test]
        public void ABeadTowerInTheWrongOrder_DoesNotBoard()
        {
            // 2층과 3층을 맞바꿔 쌓는다.
            var order = new[] { 0, 2, 1, 3 };
            var parts = SeparatedTower(order);
            BridgeAll(parts);

            var assembly = MakeAssembly(Vector3.zero);
            assembly.Evaluate();

            Assert.IsFalse(assembly.IsBoardable, "순서가 틀리면 탑승할 수 없어야 한다.");
            StringAssert.Contains("층이", assembly.Blocker);
        }

        [Test]
        public void TheTopModule_IsTheHighestPart()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var assembly = MakeAssembly(Vector3.zero);
            assembly.Evaluate();

            var top = assembly.Tower[assembly.Tower.Count - 1];

            Assert.AreEqual(PartSpec.All[PartSpec.All.Length - 1].Name, top.name,
                "최상단이 견인 조종 모듈이어야 한다(기획 §22.4 의 탑승 위치).");

            for (var i = 0; i < assembly.Tower.Count; i++)
                Assert.LessOrEqual(assembly.Tower[i].transform.position.y,
                                   top.transform.position.y + 1e-3f,
                                   "최상단보다 높은 파츠가 있으면 안 된다.");
        }

        // --- 하중 시험이 모든 변을 본다 -----------------------------------------

        /// <summary>
        /// 소켓 사슬을 쓸 때는 "위에 올라가는 파츠의 접합" 하나씩이면 충분했다. 그래프에서는
        /// 옆으로 이은 접합도 하중을 받으므로 빠뜨리면 <b>그 자리가 실제로는 끊어지는데
        /// 시험은 통과한다.</b>
        /// </summary>
        [Test]
        public void TheLoadTest_SeesEveryEdgeOfTheGraph()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var tower = new List<SpaceshipPart>();
            PartGraph.Collect(parts[0], tower);

            var links = new List<PartAssemblyLink>();
            PartGraph.CollectLinks(tower, links);

            Assert.AreEqual(PartSpec.All.Length - 1, links.Count,
                "4층 탑이면 접합이 3개여야 한다.");

            for (var i = 0; i < links.Count; i++)
                Assert.IsTrue(links[i].IsIntact);
        }

        [Test]
        public void LinksLeavingTheTower_AreNotCounted()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            // 최하단 옆에 파츠를 세우고 비드로 잇는다 — 탑이 아니라 옆에 붙은 것이다.
            var side = MakePart(
                PartSpec.All[1],
                new Vector3(SideGap(PartSpec.All[0], PartSpec.All[1]), PartSpec.All[1].Height * 0.5f, 0f));
            side.name = "SidePart";

            GrowAt(SideNozzle(parts[0], PartSpec.All[0], Vector3.right, parts[0].transform.position.y));
            Assert.IsNotNull(PartAssemblyLink.FindBetween(parts[0], side), "옆 파츠와 이어져야 한다.");

            // 탑은 최하단에서 이어진 것을 전부 모으므로 옆 파츠까지 들어온다.
            // 발사 판정이 그것을 거르는 것은 완성 판정(층 수·순서)의 몫이다.
            // 여기서 보는 것은 <b>덩어리 밖으로 나가는 접합</b>을 세지 않는 것이다.
            var tower = new List<SpaceshipPart> { parts[0], parts[1], parts[2], parts[3] };
            var links = new List<PartAssemblyLink>();
            PartGraph.CollectLinks(tower, links);

            Assert.AreEqual(PartSpec.All.Length - 1, links.Count,
                "덩어리 안의 접합만 세어야 한다.");
        }

        // --- 파츠 하나에 접합 여럿 -------------------------------------------

        /// <summary>
        /// S15P21B202-228 이 <c>APartAlreadyLinked_TakesNoSecondBeadJoint</c> 로 기록해 둔
        /// 경계가 여기서 사라진다. 그 검증은 지우지 않고 뒤집어 놓았다.
        /// </summary>
        [Test]
        public void OnePart_CanHoldSeveralLinks()
        {
            var middle = MakePart(PartSpec.All[0], Vector3.zero);
            var right = MakePart(PartSpec.All[1],
                new Vector3(SideGap(PartSpec.All[0], PartSpec.All[1]), 0f, 0f));
            var left = MakePart(PartSpec.All[2],
                new Vector3(-SideGap(PartSpec.All[0], PartSpec.All[2]), 0f, 0f));
            right.name = "RightPart";
            left.name = "LeftPart";

            Place(middle);
            Place(right);
            Place(left);

            GrowAt(SideNozzle(middle, PartSpec.All[0], Vector3.right, 0f));
            GrowAt(SideNozzle(middle, PartSpec.All[0], Vector3.left, 0f));

            Assert.IsNotNull(PartAssemblyLink.FindBetween(middle, right));
            Assert.IsNotNull(PartAssemblyLink.FindBetween(middle, left));
            Assert.AreEqual(2, middle.GetComponents<PartAssemblyLink>().Length,
                "파츠 하나가 두 쌍과 이어질 수 있어야 한다.");

            var links = new List<PartAssemblyLink>();
            PartAssemblyLink.CollectLinksOf(middle, links);
            Assert.AreEqual(2, links.Count);
        }

        /// <summary>같은 두 파츠 사이에는 여전히 접합이 하나여야 한다.</summary>
        [Test]
        public void OnePair_StillHoldsOnlyOneLink()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var before = parts[0].GetComponents<PartAssemblyLink>().Length;

            // 이미 이은 쌍에 다른 자리로 한 번 더 잇는다.
            GrowAt(TopNozzle(parts[0], PartSpec.All[0]) + Vector3.right * 0.9f);

            Assert.AreEqual(before, parts[0].GetComponents<PartAssemblyLink>().Length,
                "같은 쌍에 접합이 둘이면 강도가 두 배가 되고 하중 시험이 같은 자리를 두 번 센다.");
        }

        // --- 소켓 접합과 비드 접합을 가른다 --------------------------------------

        /// <summary>
        /// 소켓 접합이 풀려도 <b>옆에 비드로 이어 둔 접합은 남아야 한다.</b>
        ///
        /// 예전에는 원형 접합선을 완전 절단해 이 상황을 만들었다. 접합선이 사라진
        /// 뒤에는(S15P21B202-230) 운석에 끊어지거나 비드를 다 깎는 것이 그 자리를
        /// 대신하므로, 접합을 직접 해제해 같은 상황을 만든다.
        /// </summary>
        [Test]
        public void ReleasingTheSocketLink_KeepsBeadLinksOnOtherPairs()
        {
            var (parts, side, beadLink) = TowerWithSideBead();

            PartAssemblyLink.SocketLinkOf(parts[1]).Release();

            Assert.IsNull(PartAssemblyLink.SocketLinkOf(parts[1]), "소켓 접합은 풀려야 한다.");
            Assert.IsTrue(beadLink != null && beadLink.IsIntact, "비드 접합은 남아야 한다.");
            Assert.AreSame(beadLink, PartAssemblyLink.FindBetween(parts[1], side));
        }

        /// <summary>
        /// 떼어낸 파츠를 다시 올려놓아도 옆의 비드 접합이 살아 있어야 한다
        /// (기획 §16.3 "절단한 구간은 … 재조립").
        ///
        /// <b>먼저 떼어내야 다시 올릴 수 있다.</b> <see cref="PartAssembler.FindMatch"/> 가
        /// 밑면 소켓이 이미 맞물린 파츠를 건너뛰기 때문이다 — 붙어 있는 파츠를 그대로
        /// 다시 조립하는 경로는 없다. 처음에 그것을 모르고 검증을 썼다가
        /// <c>FindMatch</c> 가 후보를 못 찾는 것으로 알았다.
        /// </summary>
        [Test]
        public void ReassemblingAfterARelease_KeepsBeadLinksOnOtherPairs()
        {
            var (parts, side, beadLink) = TowerWithSideBead();

            PartAssemblyLink.SocketLinkOf(parts[1]).Release();

            parts[1].TryTransitionTo(PartState.Carried);
            var match = PartAssembler.FindMatch(parts[1]);
            Assert.IsTrue(match.HasValue, "떼어낸 뒤에는 접합 후보가 있어야 한다.");
            Assert.IsNotNull(PartAssembler.Assemble(match), "재조립이 되어야 한다.");

            Assert.IsTrue(beadLink != null && beadLink.IsIntact,
                "재조립이 옆의 비드 접합을 지우면 안 된다.");
            Assert.AreSame(beadLink, PartAssemblyLink.FindBetween(parts[1], side));
            Assert.IsNotNull(PartAssemblyLink.SocketLinkOf(parts[1]), "소켓 접합이 새로 생겨야 한다.");
        }

        /// <summary>
        /// 조인트가 끊어졌다가 다시 조립되면 <b>남아 있는 비드의 강도를 되찾아야 한다.</b>
        ///
        /// 운석에 끊어지는 경우가 그렇다. 비드는 파츠에 그대로 있는데 새 접합의 강도가
        /// 0 에서 시작하면 <b>눈에 보이는 비드와 실제 강도가 어긋난다</b>.
        /// </summary>
        [Test]
        public void ReassemblingWithBeadsStillThere_RecoversTheirStrength()
        {
            var parts = SeparatedTower();
            BridgeAll(parts);

            var link = PartAssemblyLink.FindBetween(parts[0], parts[1]);
            var before = link.BeadWeldRatio;
            Assert.Greater(before, 0f);

            // 접합만 끊는다. 비드는 그대로 남는다.
            link.Release();
            Assert.IsNull(PartAssemblyLink.FindBetween(parts[0], parts[1]));

            // 소켓으로 붙을 수 있는 자리로 옮겨 다시 조립한다.
            parts[1].TryTransitionTo(PartState.Carried);
            parts[1].transform.position = new Vector3(
                0f, PartSpec.All[0].Height + PartSpec.All[1].Height * 0.5f, 0f);
            UnityEngine.Physics.SyncTransforms();

            var match = PartAssembler.FindMatch(parts[1]);
            Assert.IsTrue(match.HasValue, "다시 올릴 자리가 있어야 한다.");

            var relink = PartAssembler.Assemble(match);
            Assert.IsNotNull(relink);
            Assert.Greater(relink.BeadWeldRatio, 0f,
                "남아 있는 비드의 강도를 되찾아야 한다.");
        }

        // --- 리그 ---------------------------------------------------------

        static float Step => Time.fixedDeltaTime;

        /// <summary>파츠 사이의 틈(m). 소켓 접합 허용치보다 넓어 저절로 붙지 않는다.</summary>
        static float Clearance => PhysicsScale.WeldGapTolerance * 1.25f;

        /// <summary>노즐을 표면에서 띄우는 거리(m). 틈의 절반보다 작아야 이쪽 몸체에 씨앗이 내린다.</summary>
        const float NozzleStandoff = 0.08f;

        static float FullChainSeconds =>
            PhysicsScale.WeldBeadBlobSeconds * (PhysicsScale.WeldBeadBlobsPerChain + 2);

        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            _spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body") { layer = GameLayers.Part };
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.CenterFromPartCenter(spec.Height);
                child.AddComponent<BoxCollider>().size = box.Size;
            }

            root.AddComponent<Rigidbody>();
            var part = root.AddComponent<SpaceshipPart>();

            foreach (var role in new[] { SocketRole.Base, SocketRole.Top })
            {
                var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;
                var socket = new GameObject(role.ToString());
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = direction * (spec.Height * 0.5f);
                socket.transform.localRotation = Quaternion.LookRotation(direction, Vector3.forward);
                socket.AddComponent<PartSocket>().SetRole(role);
            }

            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            part.RefreshSockets();
            UnityEngine.Physics.SyncTransforms();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        static void Place(SpaceshipPart part)
        {
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);
        }

        /// <summary>
        /// 서로 <b>닿지 않게</b> 층마다 틈을 두고 쌓은 탑. 소켓으로는 붙지 않는다.
        /// </summary>
        SpaceshipPart[] SeparatedTower() => SeparatedTower(new[] { 0, 1, 2, 3 });

        /// <param name="order">아래에서 위로 놓을 <see cref="PartSpec.All"/> 인덱스.</param>
        SpaceshipPart[] SeparatedTower(int[] order)
        {
            var parts = new SpaceshipPart[order.Length];
            var y = 0f;

            for (var i = 0; i < order.Length; i++)
            {
                var spec = PartSpec.All[order[i]];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                Place(parts[i]);
                y += spec.Height + Clearance;
            }

            UnityEngine.Physics.SyncTransforms();
            return parts;
        }

        /// <summary>소켓으로 정상 조립한 탑.</summary>
        SpaceshipPart[] AssembledTower()
        {
            var parts = new SpaceshipPart[PartSpec.All.Length];
            var y = 0f;

            for (var i = 0; i < parts.Length; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            Place(parts[0]);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < parts.Length; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);
                var match = PartAssembler.FindMatch(parts[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}층 접합 후보가 없다.");
                Assert.IsNotNull(PartAssembler.Assemble(match), $"{i + 1}층 조립이 실패했다.");
            }

            UnityEngine.Physics.SyncTransforms();
            return parts;
        }

        /// <summary>층마다 아래 파츠 윗면에서 비드를 뻗어 위 파츠에 잇는다.</summary>
        void BridgeAll(SpaceshipPart[] parts)
        {
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var spec = PartSpec.Find(parts[i].name);
                GrowAt(TopNozzle(parts[i], spec));

                Assert.IsNotNull(PartAssemblyLink.FindBetween(parts[i], parts[i + 1]),
                    $"{i + 1}층과 {i + 2}층이 이어져야 한다.");
            }
        }

        static Vector3 TopNozzle(SpaceshipPart part, PartSpec spec) =>
            part.transform.position + Vector3.up * (spec.Height * 0.5f + NozzleStandoff);

        static Vector3 SideNozzle(SpaceshipPart part, PartSpec spec, Vector3 direction, float y)
        {
            var origin = part.transform.position;
            return new Vector3(origin.x, y, origin.z) +
                   direction * (HalfWidthOf(spec) + NozzleStandoff);
        }

        /// <summary>
        /// 파츠의 가장 넓은 지점의 반폭(m).
        ///
        /// <see cref="PartSpec"/> 은 폭을 직접 갖지 않는다 — 실측 상자 여럿으로 형태를
        /// 표현하기 때문이다(S15P21B202-166). 옆면을 겨누려면 가장 튀어나온 상자를 봐야 한다.
        /// </summary>
        /// <summary>두 파츠를 옆으로 <see cref="Clearance"/> 만큼 띄워 놓을 때의 중심 간 거리(m).</summary>
        static float SideGap(PartSpec left, PartSpec right) =>
            HalfWidthOf(left) + HalfWidthOf(right) + Clearance;

        static float HalfWidthOf(PartSpec spec)
        {
            var half = 0f;
            for (var i = 0; i < spec.Boxes.Length; i++)
                half = Mathf.Max(half, Mathf.Max(spec.Boxes[i].Width, spec.Boxes[i].Depth) * 0.5f);

            return half;
        }

        /// <summary>소켓으로 쌓은 탑 + 2층 옆에 비드로 이어 붙인 파츠 하나.</summary>
        (SpaceshipPart[] Parts, SpaceshipPart Side, PartAssemblyLink BeadLink) TowerWithSideBead()
        {
            var parts = AssembledTower();

            // <b>이름을 바꾼다.</b> MakePart 는 명세 이름을 그대로 쓰므로 탑에 이미 있는
            // 파츠와 이름이 겹치고, 완성 판정과 높이 정렬의 동순위 판정이 둘 다 이름을 본다.
            var side = MakePart(
                PartSpec.All[2],
                new Vector3(SideGap(PartSpec.All[1], PartSpec.All[2]),
                            parts[1].transform.position.y, 0f));
            side.name = "SidePart";
            Place(side);

            GrowAt(SideNozzle(parts[1], PartSpec.All[1], Vector3.right,
                              parts[1].transform.position.y));

            var beadLink = PartAssemblyLink.FindBetween(parts[1], side);
            Assert.IsNotNull(beadLink, "비드 접합이 있어야 의미가 있다.");
            Assert.IsFalse(beadLink.HasSockets);

            return (parts, side, beadLink);
        }

        WeldBead GrowAt(Vector3 nozzle) => GrowAt(nozzle, FullChainSeconds);

        WeldBead GrowAt(Vector3 nozzle, float seconds)
        {
            WeldBead bead = null;
            var steps = Mathf.CeilToInt(seconds / Step);

            for (var i = 0; i < steps; i++)
            {
                WeldBead.TryGrowNear(nozzle, Step, out var grown);
                if (grown != null) bead = grown;
            }

            UnityEngine.Physics.SyncTransforms();
            return bead;
        }

        ShipAssembly MakeAssembly(Vector3 padPosition)
        {
            var pad = new GameObject("LaunchPad");
            pad.transform.position = padPosition;
            _spawned.Add(pad);

            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);
            return assembly;
        }
    }
}
