using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 발사 하중 시험을 검증한다(기획 S15P21B202-107 §22.3 · §22.4).
    ///
    /// 하중을 계산해 접합부에 나눠 주지 않는다. 최하단에 추력을 주면 위 접합부가 자기가
    /// 지고 있는 질량과 가속만큼의 힘을 물리로 받는다. 그래서 여기서 보는 것은
    /// "용접한 탑은 견디고 안 한 탑은 못 견디는가" 와 "결과가 결정론적인가" 다.
    ///
    /// 탑을 쌓는 방법과 붕괴 경계는 S15P21B202-194 에서 실측해 고정했다
    /// (docs/records/S15P21B202-194-tower-collapse.md). 그 위에 얹는다.
    /// </summary>
    public sealed class LaunchTestTests
    {
        const float Step = 0.02f;

        /// <summary>층마다 이만큼 밀면 탑이 선다(194 실측: 0.30 까지 선다).</summary>
        const float StableOffset = 0.20f;

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

        /// <summary>
        /// <see cref="PartSpec"/> 의 실측 형태·질량으로 파츠 하나를 만든다.
        /// 소켓은 <see cref="SpaceshipPart"/> 뒤에 붙이고 <c>RefreshSockets</c> 를 불러야
        /// 목록에 잡힌다(S15P21B202-194 에서 이것 때문에 접합 후보가 아예 안 나왔다).
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

        /// <summary>탑을 쌓고 최하단을 돌려준다.</summary>
        SpaceshipPart BuildTower(float lateralPerTier, bool welded)
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
                Assert.IsTrue(match.HasValue, $"{i + 1}층 접합 후보가 없다.");

                var link = PartAssembler.Assemble(match);
                Assert.IsNotNull(link, $"{i + 1}층 조립이 실패했다.");
                links.Add(link);

                if (welded)
                {
                    link.ForceWeldComplete();
                }

                link.Tick();
            }

            // 쌓은 직후의 흔들림을 가라앉힌다. 그러지 않으면 시작 기울기 검사가
            // 조립 순간의 미세한 진동을 실패로 읽는다.
            Settle(links, 0.5f);
            return parts[0];
        }

        void Settle(List<PartAssemblyLink> links, float seconds)
        {
            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++)
            {
                foreach (var link in links)
                    if (link != null) link.Tick();

                UnityEngine.Physics.Simulate(Step);
            }
        }

        /// <summary>시험을 걸고 결론이 날 때까지 돌린다.</summary>
        LaunchTest RunLaunch(SpaceshipPart engine, float seconds = 12f)
        {
            var host = new GameObject("LaunchTest");
            spawned.Add(host);

            var test = host.AddComponent<LaunchTest>();
            test.Begin(engine);

            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++)
            {
                // S15P21B202-204 부터 하중 시험 통과는 Success 가 아니라 Ascending 이다.
                // 판정이 끝나는 지점은 여기다 — 그 뒤 9초 상승은 §22.5 의 연출이고
                // 이 규칙이 볼 것이 아니다.
                if (test.State is LaunchState.Ascending or LaunchState.Success
                    or LaunchState.Failed) break;

                test.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }

            return test;
        }

        // --- 성공 ----------------------------------------------------------

        /// <summary>
        /// 기획 §16.2 — "파츠를 전부 쌓기만 하면 발사를 시도할 수 있으며 발사 하중으로
        /// 품질을 검증한다". 잘 쌓고 용접한 탑은 통과해야 한다.
        /// </summary>
        [Test]
        public void WeldedTower_PassesTheLoadTest()
        {
            var engine = BuildTower(StableOffset, welded: true);
            var test = RunLaunch(engine);

            Assert.AreEqual(LaunchState.Ascending, test.State,
                $"용접한 탑이 하중 시험을 통과해야 한다. 실패 이유: {test.FailureReason}");
            Assert.GreaterOrEqual(test.Altitude, PhysicsScale.LaunchLiftOffHeight,
                "지면에서 완전히 떠올라야 통과다.");
        }

        /// <summary>
        /// 기획 §22.3 — "성공 상태 이후 상승 연출 중에는 접합부가 파손되지 않는다".
        /// 봉인이 <c>ApplyJointStrength</c> 의 재계산을 막는지 확인한다.
        /// </summary>
        [Test]
        public void SuccessfulLaunch_SealsJointsAgainstFurtherBreaking()
        {
            var engine = BuildTower(StableOffset, welded: true);
            var test = RunLaunch(engine);

            Assert.AreEqual(LaunchState.Ascending, test.State,
                "먼저 하중 시험을 통과해야 의미가 있다 — 봉인은 그때 걸린다.");

            foreach (var part in test.Tower)
            {
                var link = part.GetComponent<PartAssemblyLink>();
                if (link == null) continue;

                var joint = part.GetComponent<FixedJoint>();
                Assert.IsNotNull(joint, $"{part.name} 의 접합이 사라졌다.");
                Assert.IsTrue(float.IsPositiveInfinity(joint.breakForce),
                    $"{part.name} 의 접합이 봉인되지 않았다. 상승 중 파손될 수 있다.");
            }
        }

        // --- 실패 ----------------------------------------------------------

        /// <summary>용접하지 않은 탑은 하중을 버티지 못한다. 용접의 값이 여기서 나온다.</summary>
        [Test]
        public void UnweldedTower_FailsTheLoadTest()
        {
            var engine = BuildTower(StableOffset, welded: false);
            var test = RunLaunch(engine);

            Assert.AreEqual(LaunchState.Failed, test.State,
                "용접하지 않은 탑은 하중 시험에서 실패해야 한다.");
            Assert.IsNotEmpty(test.FailureReason, "실패 이유를 남겨야 한다.");
        }

        /// <summary>
        /// 기획 §22.4 — 실패해도 접합부가 전부 끊어지지는 않는다.
        /// "끊어지지 않은 접합부는 붙은 덩어리로 남는다".
        /// </summary>
        [Test]
        public void FailedLaunch_LeavesSomeJointsIntact()
        {
            var engine = BuildTower(StableOffset, welded: false);
            var test = RunLaunch(engine);

            Assert.AreEqual(LaunchState.Failed, test.State, "먼저 실패해야 의미가 있다.");

            var intact = 0;
            foreach (var part in test.Tower)
            {
                var link = part.GetComponent<PartAssemblyLink>();
                if (link != null && link.IsIntact) intact++;
            }

            Assert.Greater(intact, 0,
                "접합이 전부 끊어졌다. 약한 곳부터 끊어지고 나머지는 붙은 덩어리로 남아야 한다.");
        }

        /// <summary>
        /// 기획 §22.3 — "우주선이 자기 무게로 서 있는지 먼저 확인한다".
        /// 추력이 넘어짐을 가려 버리면 플레이어가 왜 실패했는지 알 수 없다.
        /// </summary>
        [Test]
        public void TiltedShip_FailsBeforeThrustBegins()
        {
            var engine = BuildTower(StableOffset, welded: true);

            engine.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            UnityEngine.Physics.SyncTransforms();

            var host = new GameObject("LaunchTest");
            spawned.Add(host);

            var test = host.AddComponent<LaunchTest>();
            test.Begin(engine);

            Assert.AreEqual(LaunchState.Failed, test.State,
                "기울어 있으면 추력을 주기 전에 실패해야 한다.");
            Assert.AreEqual(0f, test.Output, 0.001f, "출력이 오르기 전에 판정해야 한다.");
        }

        // --- 구조와 결정론 --------------------------------------------------

        /// <summary>소켓 사슬을 따라 네 파츠를 모두 찾아야 한다.</summary>
        [Test]
        public void Begin_CollectsTheWholeTower()
        {
            var engine = BuildTower(StableOffset, welded: true);

            var host = new GameObject("LaunchTest");
            spawned.Add(host);

            var test = host.AddComponent<LaunchTest>();
            test.Begin(engine);

            Assert.AreEqual(PartSpec.All.Length, test.Tower.Count,
                "소켓 사슬을 따라 탑 전체를 모아야 한다.");
            Assert.AreSame(engine, test.Tower[0], "최하단이 첫 번째여야 한다.");
        }

        /// <summary>
        /// 기획 §22.3 — "같은 배치와 용접 상태에서는 항상 같은 결과가 나와야 하며 무작위
        /// 실패를 사용하지 않는다". 각도까지 같기를 요구하지 않는다 — PhysX 가 비트 단위로
        /// 결정론적이지 않기 때문이다. 성공/실패라는 결과가 같으면 된다.
        /// </summary>
        [Test]
        public void Result_IsDeterministic()
        {
            var first = RunLaunch(BuildTower(StableOffset, welded: true)).State;
            TearDown();
            SetUp();
            var second = RunLaunch(BuildTower(StableOffset, welded: true)).State;

            Assert.AreEqual(first, second,
                $"같은 배치·용접인데 결과가 {first} 와 {second} 로 다르다. 무작위가 섞였다.");
        }

        /// <summary>출력은 5초에 걸쳐 0에서 1까지 오른다(기획 §22.3).</summary>
        [Test]
        public void Output_RampsOverFiveSeconds()
        {
            var engine = BuildTower(StableOffset, welded: true);

            var host = new GameObject("LaunchTest");
            spawned.Add(host);

            var test = host.AddComponent<LaunchTest>();
            test.Begin(engine);
            Assert.AreEqual(LaunchState.Ramping, test.State, "시작하면 출력을 올려야 한다.");

            var half = Mathf.RoundToInt(PhysicsScale.LaunchRampSeconds * 0.5f / Step);
            for (var s = 0; s < half; s++)
            {
                test.Tick(Step);
                UnityEngine.Physics.Simulate(Step);
            }

            Assert.AreEqual(0.5f, test.Output, 0.05f,
                $"절반 시점의 출력이 {test.Output:0.00} 다. 5초에 걸쳐 선형으로 올라야 한다.");
        }
    }
}
