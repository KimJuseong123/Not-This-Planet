using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 접합부가 끊어진 뒤 <b>공중에서 부서지는지</b> 검증한다(기획 §22.4,
    /// S15P21B202-340).
    ///
    /// <b>예전에는 "터진다" 가 아니라 "주저앉는다" 로 보였다.</b> 접합이 끊어지는
    /// 프레임에 <c>Fail()</c> 이 상태만 바꿨고, <c>Tick</c> 첫 줄이
    /// <c>State != Ramping</c> 이면 돌아가므로 <b>엔진이 그 자리에서 꺼졌다.</b>
    ///
    /// 끊어질 때 우주선은 이미 공중에 있다 — 추력이 무게의 3.2배라 출력 31%(램프 5초의
    /// 약 1.6초)에서 무게를 이기고, 접합은 그보다 높은 출력에서 끊어진다. 그래서 추력만
    /// 잠깐 남기면 아래쪽은 올라가고 위쪽은 떨어지며 찢어진다.
    ///
    /// <b><c>ShipHatch</c> 를 세우지 않는다.</b> 탑승·카운트다운 전 과정은
    /// <c>ShipAscentTests</c> 가 이미 보고 있고, 이 이슈가 바꾼 것은
    /// <see cref="LaunchTest"/> 의 상태 기계다. <see cref="LaunchTest.Begin"/> 이 최하단
    /// 파츠만 받으므로 탑만 세워 직접 발사한다.
    /// </summary>
    public sealed class LaunchBreakupTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();
            LowGravityField.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // --- 끊어지면 부서지는 상태로 간다 -------------------------------------

        [Test]
        public void AWeakJoint_EntersBreakingNotFailed()
        {
            var rig = Launch(weldRatio: 0f);

            RunUntilLeavingRamp(rig);

            Assert.AreEqual(LaunchState.Breaking, rig.Launch.State,
                $"끊어진 직후에는 부서지는 중이어야 한다. 사유: {rig.Launch.FailureReason}");
        }

        [Test]
        public void TheReasonIsSetWhenItStartsBreaking()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);

            Assert.IsNotEmpty(rig.Launch.FailureReason);
            StringAssert.Contains("접합부", rig.Launch.FailureReason);
        }

        /// <summary>
        /// <b>이것이 이 이슈의 본체다.</b> 끊어진 뒤에도 추력이 남아야 위쪽이 떨어져
        /// 나가는 것이 보인다.
        ///
        /// <b>속도가 오르는지로 재지 않는다.</b> 추력을 남은 덩어리의 무게 대비
        /// <see cref="PhysicsScale.LaunchBreakupThrustRatio"/>(1.2배)로 묶었고, 아래
        /// 접합이 붙어 있으면 엔진이 그만큼을 함께 지므로 오히려 느려질 수 있다.
        /// <b>자유낙하보다 덜 떨어지는지</b>가 추력의 존재를 보는 정확한 척도다.
        /// </summary>
        [Test]
        public void ThrustKeepsPushingWhileBreaking()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);
            Assert.AreEqual(LaunchState.Breaking, rig.Launch.State);

            var engine = rig.Engine.GetComponent<Rigidbody>();
            var before = engine.linearVelocity.y;

            const float Span = 0.2f;
            Run(rig, Span);

            var freeFall = before - Mathf.Abs(UnityEngine.Physics.gravity.y) * Span;

            Assert.Greater(engine.linearVelocity.y, freeFall,
                "부서지는 동안 추력이 걸려 자유낙하보다 덜 떨어져야 한다.");
        }

        // --- 반드시 빠져나온다 (소프트락 방지) ---------------------------------

        /// <summary>
        /// <b>갇히면 원래 버그보다 나쁘다.</b> <c>ShipHatch.WatchLaunch</c> 는
        /// <see cref="LaunchState.Failed"/> 하나만 보고 그때부터 착지를 기다려 플레이어를
        /// 사출한다(§22.4). 부서지는 상태에 머물면 그 사출이 영원히 오지 않는다.
        /// </summary>
        [Test]
        public void BreakingAlwaysReachesFailed()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);

            Run(rig, PhysicsScale.LaunchBreakupSeconds + 0.2f);

            Assert.AreEqual(LaunchState.Failed, rig.Launch.State,
                "상한 시간이 지나면 반드시 실패로 확정되어야 한다.");
        }

        [Test]
        public void LosingTheEngineWhileBreaking_ReachesFailed()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);

            Object.DestroyImmediate(rig.Engine.gameObject);
            rig.Launch.Tick(Step);

            Assert.AreEqual(LaunchState.Failed, rig.Launch.State,
                "엔진이 사라져도 갇히지 않아야 한다.");
        }

        [Test]
        public void OnceFailed_ItStaysFailed()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);
            Run(rig, PhysicsScale.LaunchBreakupSeconds + 0.2f);

            Run(rig, 1f);

            Assert.AreEqual(LaunchState.Failed, rig.Launch.State);
        }

        // --- 발사 전 실패는 즉시 실패다 ----------------------------------------

        /// <summary>
        /// §22.3 은 "우주선이 자기 무게로 서 있는지 <b>먼저</b> 확인한다" 로 추력 전
        /// 판정을 정했다. 공중에서 일어나는 일이 아니므로 부서지는 연출을 태우지 않는다.
        /// </summary>
        [Test]
        public void ATiltedTower_FailsImmediatelyWithoutBreaking()
        {
            var rig = Build(weldRatio: 1f);

            rig.Engine.transform.rotation =
                Quaternion.Euler(PhysicsScale.LaunchMaxStandingTilt + 15f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();

            rig.Launch.Begin(rig.Engine);

            Assert.AreEqual(LaunchState.Failed, rig.Launch.State,
                "추력을 주기 전 판정은 즉시 실패다.");
        }

        [Test]
        public void NoEngine_FailsImmediatelyWithoutBreaking()
        {
            var rig = Build(weldRatio: 1f);

            rig.Launch.Begin(null);

            Assert.AreEqual(LaunchState.Failed, rig.Launch.State);
        }

        // --- 성공 경로는 그대로다 ----------------------------------------------

        /// <summary>
        /// <b>과잉 교정 방어선.</b> 부서지는 상태를 넣으면서 성공 경로를 건드리면
        /// 잘 만든 탑이 안 날아간다.
        /// </summary>
        [Test]
        public void AFullyWeldedStack_StillPassesAndAscends()
        {
            var rig = Launch(weldRatio: 1f);

            RunUntilLeavingRamp(rig);

            Assert.AreEqual(LaunchState.Ascending, rig.Launch.State,
                $"완전 용접한 스택은 통과해야 한다. 사유: {rig.Launch.FailureReason}");
        }

        [Test]
        public void ANewLaunchClearsTheBreakingState()
        {
            var rig = Launch(weldRatio: 0f);
            RunUntilLeavingRamp(rig);
            Assert.AreEqual(LaunchState.Breaking, rig.Launch.State);

            rig.Launch.Begin(rig.Engine);

            Assert.AreNotEqual(LaunchState.Breaking, rig.Launch.State,
                "다시 시도하면 부서지던 상태가 남아 있으면 안 된다(§22.4 재시도).");
        }

        // --- 리그 -------------------------------------------------------------

        readonly struct Rig
        {
            public Rig(LaunchTest launch, SpaceshipPart engine)
            {
                Launch = launch;
                Engine = engine;
            }

            public LaunchTest Launch { get; }
            public SpaceshipPart Engine { get; }
        }

        Rig Launch(float weldRatio)
        {
            var rig = Build(weldRatio);
            rig.Launch.Begin(rig.Engine);

            Assert.AreEqual(LaunchState.Ramping, rig.Launch.State,
                $"발사가 시작되어야 한다. 사유: {rig.Launch.FailureReason}");

            return rig;
        }

        /// <summary>
        /// 4층 탑. <see cref="LaunchTest"/> 는 <c>ShipAssembly</c> 를 거치지 않고 최하단
        /// 파츠만 받으므로 <c>ShipHatch</c> 는 세우지 않는다.
        ///
        /// <b>2층으로는 끊어지지 않는다.</b> 최하단 접합이 위 파츠 하나(약 500kg)만
        /// 지므로 하중이 <see cref="PhysicsScale.UnweldedBreakForce"/>(8,000N)에 못
        /// 미치고, 용접을 아예 안 해도 통과한다. S15P21B202-331 의 측정이 4층에서
        /// 용접률 0% 가 파손된다는 것을 보였다.
        /// </summary>
        Rig Build(float weldRatio)
        {
            MakeGround();

            var parts = new SpaceshipPart[PartSpec.All.Length];
            var y = 0f;

            for (var i = 0; i < parts.Length; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < parts.Length; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}층 접합 후보가 있어야 한다.");

                var link = PartAssembler.Assemble(match);
                Assert.IsNotNull(link, $"{i + 1}층 조립이 되어야 한다.");

                if (weldRatio >= 1f) link.ForceWeldComplete();
                else link.SetBeadWeldRatio(weldRatio);
            }

            var pad = new GameObject("LaunchPad");
            _spawned.Add(pad);

            return new Rig(pad.AddComponent<LaunchTest>(), parts[0]);
        }

        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            _spawned.Add(root);

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
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            _spawned.Add(go);
        }

        void Run(Rig rig, float seconds)
        {
            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++) Step1(rig);
        }

        /// <summary>램프를 벗어날 때까지 돌린다 — 통과했거나 부서지기 시작했거나.</summary>
        void RunUntilLeavingRamp(Rig rig)
        {
            for (var s = 0; s < 900 && rig.Launch.State == LaunchState.Ramping; s++)
                Step1(rig);

            Assert.AreNotEqual(LaunchState.Ramping, rig.Launch.State,
                "램프가 끝나지 않았다.");
        }

        void Step1(Rig rig)
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                var link = go.GetComponent<PartAssemblyLink>();
                if (link != null) link.Tick();
            }

            rig.Launch.Tick(Step);
            UnityEngine.Physics.Simulate(Step);
        }
    }
}
