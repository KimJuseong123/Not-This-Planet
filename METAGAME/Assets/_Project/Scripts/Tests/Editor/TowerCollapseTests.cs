using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 미용접 탑이 서는 조건과 무너지는 조건을 고정한다.
    ///
    /// 기획(S15P21B202-107)은 두 가지를 동시에 요구한다 — §15.1 "잘 올리면 용접 전에도 서
    /// 있을 수 있고 잘못 올리면 기울거나 떨어진다", §22.4 "약한 접합부부터 끊어지고
    /// 끊어지지 않은 접합부는 붙은 덩어리로 남는다".
    ///
    /// 조인트 강도(UnweldedBreakForce 8000 · WeldedBreakForce 200000)는 S15P21B202-70 에서
    /// 균일 500kg 파츠와 완벽 정렬을 기준으로 잡은 값이다. 그 전제가 S15P21B202-166(파츠별
    /// 질량 406~745kg)과 S15P21B202-172(정렬 제거)로 모두 바뀌었으므로, 이 테스트가
    /// 그 조합에서도 규칙이 유지되는지 지킨다.
    ///
    /// 실측 근거는 docs/records/S15P21B202-194-tower-collapse.md 에 있다.
    /// </summary>
    public sealed class TowerCollapseTests
    {
        const float Step = 0.02f;

        /// <summary>층마다 이만큼 옆으로 밀면 탑이 선다. 실측 경계는 0.30~0.35m 사이다.</summary>
        const float StableOffset = 0.20f;

        /// <summary>층마다 이만큼 밀면 무너진다.</summary>
        const float UnstableOffset = 0.35f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartSocket.All.Clear();
        }

        /// <summary>탑 하나를 쌓고 돌린 결과.</summary>
        readonly struct Outcome
        {
            public Outcome(int broken, float tilt) { Broken = broken; Tilt = tilt; }

            /// <summary>끊어진 접합 수.</summary>
            public int Broken { get; }

            /// <summary>최상단 파츠가 기울어진 각도.</summary>
            public float Tilt { get; }
        }

        /// <summary>
        /// <see cref="PartSpec"/> 의 실측 형태·질량으로 파츠 하나를 만든다.
        ///
        /// 소켓을 <see cref="SpaceshipPart"/> 뒤에 붙이고 <c>RefreshSockets</c> 를 부르는 순서가
        /// 중요하다. 앞에 붙이면 목록에 잡히지 않아 접합 후보가 아예 나오지 않는다
        /// (<see cref="SocketTestRig"/> 가 같은 순서를 쓴다).
        /// </summary>
        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body");
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

            // 원형 용접선 하나(기획 §16.1). 예전에는 용접점 4개를 놓았다.
            var seam = new GameObject("WeldSeam");
            seam.transform.SetParent(root.transform, false);
            seam.transform.localPosition = Vector3.down * (spec.Height * 0.5f);
            seam.AddComponent<WeldSeam>();

            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            part.RefreshSockets();
            UnityEngine.Physics.SyncTransforms();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, 60f);
            spawned.Add(go);
        }

        /// <summary>
        /// 네 파츠를 쌓고 <paramref name="seconds"/> 만큼 돌린다.
        /// </summary>
        /// <param name="lateralPerTier">층마다 옆으로 미는 거리. 0 이면 반듯하다.</param>
        /// <param name="welded">쌓은 뒤 용접점을 모두 채울지.</param>
        /// <param name="meteorMass">0 보다 크면 최상단 위로 운석을 떨어뜨린다.</param>
        Outcome RunTower(float lateralPerTier, bool welded = false, float meteorMass = 0f,
                         float seconds = 6f)
        {
            MakeGround();

            var parts = new SpaceshipPart[PartSpec.All.Length];
            var links = new List<PartAssemblyLink>();

            var y = 0f;
            for (var i = 0; i < PartSpec.All.Length; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(lateralPerTier * i, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < parts.Length; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                Assert.IsTrue(match.HasValue,
                    $"{i + 1}층 접합 후보가 없다. 옆밀기 {lateralPerTier:0.00}m 가 허용 오차를 넘었다.");

                var link = PartAssembler.Assemble(match);
                Assert.IsNotNull(link, $"{i + 1}층 조립이 실패했다.");
                links.Add(link);

                // 강도는 PartAssemblyLink 가 WeldRatio 로 정한다. Assemble 인수로는 바꿀 수 없다.
                if (welded)
                {
                    link.ForceWeldComplete();
                }

                link.Tick();
            }

            if (meteorMass > 0f) DropMeteor(parts[^1], meteorMass);

            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++)
            {
                foreach (var link in links)
                    if (link != null) link.Tick();

                UnityEngine.Physics.Simulate(Step);
            }

            var broken = 0;
            foreach (var link in links)
                if (link == null || link.GetComponent<FixedJoint>() == null) broken++;

            return new Outcome(broken, Vector3.Angle(parts[^1].transform.up, Vector3.up));
        }

        void DropMeteor(SpaceshipPart top, float mass)
        {
            var meteor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            meteor.layer = GameLayers.Meteor;
            meteor.transform.position = top.transform.position
                                        + Vector3.up * PhysicsScale.MeteorDropHeight;
            meteor.transform.localScale = Vector3.one * (PhysicsScale.MeteorRadius * 2f);
            spawned.Add(meteor);

            var body = meteor.AddComponent<Rigidbody>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            UnityEngine.Physics.SyncTransforms();
        }

        // --- 용접 전 -------------------------------------------------------

        /// <summary>기획 §15.1 — "잘 올리면 용접 전에도 서 있을 수 있고".</summary>
        [Test]
        public void WellStackedTower_StandsWithoutWelding()
        {
            var outcome = RunTower(StableOffset);

            Assert.AreEqual(0, outcome.Broken, "잘 쌓은 탑은 용접 전에도 접합이 끊어지지 않아야 한다.");
            Assert.Less(outcome.Tilt, 5f, $"최상단이 {outcome.Tilt:0.0}도 기울었다. 서 있어야 한다.");
        }

        /// <summary>기획 §15.1 — "잘못 올리면 기울거나 떨어진다".</summary>
        [Test]
        public void BadlyStackedTower_CollapsesWithoutWelding()
        {
            var outcome = RunTower(UnstableOffset);

            Assert.Greater(outcome.Broken, 0, "비뚤게 쌓은 탑은 무너져야 한다.");
            Assert.Greater(outcome.Tilt, 30f, $"최상단이 {outcome.Tilt:0.0}도 밖에 기울지 않았다.");
        }

        // --- 운석 ----------------------------------------------------------

        /// <summary>
        /// 운석이 미용접 접합을 끊는다. 이 규칙이 S15P21B202-70 에서 조인트 강도와 운석
        /// 질량을 정한 근거이므로, 파츠 질량이 바뀐 뒤에도 유지되는지 지켜야 한다.
        /// </summary>
        [Test]
        public void Meteor_BreaksUnweldedTower()
        {
            var outcome = RunTower(0f, meteorMass: PhysicsScale.MeteorMass, seconds: 12f);

            Assert.Greater(outcome.Broken, 0,
                "운석 한 발이 미용접 탑의 접합을 끊어야 한다. 안 끊기면 운석이 위협이 아니다.");
        }

        /// <summary>같은 운석이 용접된 접합은 끊지 못한다. 용접의 값이 여기서 나온다.</summary>
        [Test]
        public void Meteor_DoesNotBreakWeldedTower()
        {
            var outcome = RunTower(0f, welded: true, meteorMass: PhysicsScale.MeteorMass, seconds: 12f);

            Assert.AreEqual(0, outcome.Broken,
                "용접한 접합은 운석 한 발을 버텨야 한다. 끊기면 용접에 의미가 없다.");
        }

        /// <summary>
        /// 기획 §22.4 — "약한 접합부부터 끊어지고 끊어지지 않은 접합부는 붙은 덩어리로 남는다".
        /// 용접했어도 무게중심이 나쁘면 무너진다(§16.2).
        /// </summary>
        [Test]
        public void WeldedTower_StillCollapsesWhenBadlyStacked()
        {
            // 용접된 탑은 하나의 덩어리로 천천히 기운다. 미용접 탑보다 시간이 더 걸린다.
            var outcome = RunTower(UnstableOffset, welded: true, seconds: 12f);

            Assert.Greater(outcome.Broken, 0, "용접해도 무게중심이 나쁘면 무너져야 한다.");
            Assert.Less(outcome.Broken, 3,
                $"접합 셋이 모두 끊어졌다({outcome.Broken}). 약한 곳부터 끊어지고 나머지는 "
                + "붙은 덩어리로 남아야 한다.");
        }

        // --- 결정론 --------------------------------------------------------

        /// <summary>
        /// 기획 §22.3 — "같은 배치와 용접 상태에서는 항상 같은 결과가 나와야 하며 무작위
        /// 실패를 사용하지 않는다". 각도까지 같기를 요구하지는 않는다 — PhysX 가 비트 단위로
        /// 결정론적이지 않기 때문이다. 무너지는가 서는가 하는 <b>결과</b>가 같으면 된다.
        /// </summary>
        [Test]
        public void Collapse_IsDeterministic()
        {
            var first = RunTower(UnstableOffset);
            TearDown();
            SetUp();
            var second = RunTower(UnstableOffset);

            Assert.AreEqual(first.Broken, second.Broken,
                $"같은 배치인데 끊어진 접합 수가 {first.Broken} 과 {second.Broken} 로 다르다.");
            Assert.AreEqual(first.Tilt, second.Tilt, 5f,
                $"같은 배치인데 기울기가 {first.Tilt:0.0} 과 {second.Tilt:0.0} 로 크게 다르다.");
        }
    }
}
