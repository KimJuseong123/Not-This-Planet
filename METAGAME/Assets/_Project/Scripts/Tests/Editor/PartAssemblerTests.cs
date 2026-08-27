using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 스냅 후보 판정과 실제 조립을 검증한다.
    ///
    /// 파츠 높이는 PhysicsScale.PartHeight 이고 소켓은 밑면·윗면 가운데에 있으므로,
    /// 두 파츠가 정확히 맞물리면 중심 간 거리가 정확히 PartHeight 가 된다.
    /// </summary>
    public sealed class PartAssemblerTests
    {
        readonly List<GameObject> _spawned = new();

        SimulationMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            _previousMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _previousMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
        }

        // --- 후보 판정 -------------------------------------------------------

        [Test]
        public void FindMatch_ReturnsNone_WhenNothingIsNearby()
        {
            var moving = SocketTestRig.Freed(_spawned, new Vector3(0f, 50f, 0f));
            SocketTestRig.Placed(_spawned, Vector3.zero);

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue);
        }

        [Test]
        public void FindMatch_ReturnsNone_WhenTargetIsNotInAssemblyYet()
        {
            // 아래 파츠가 아직 Free 일 뿐이라면 그 위에 쌓을 수 없다.
            var lower = SocketTestRig.CreatePart(_spawned, Vector3.zero);
            lower.TryTransitionTo(PartState.Free);

            var moving = SocketTestRig.Freed(_spawned, Vector3.up * PhysicsScale.PartHeight);

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue);
        }

        [Test]
        public void FindMatch_FindsTopSocketOfPlacedPart()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.4f));

            var match = PartAssembler.FindMatch(moving);

            Assert.IsTrue(match.HasValue);
            Assert.AreSame(SocketTestRig.TopSocketOf(lower), match.TargetSocket);
            Assert.AreSame(SocketTestRig.BaseSocketOf(moving), match.MovingSocket);
        }

        [Test]
        public void FindMatch_RejectsBeyondSnapDistance()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + PhysicsScale.SnapDistance + 1f));

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue);
        }

        [Test]
        public void FindMatch_RejectsBeyondSnapAngle()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * PhysicsScale.PartHeight);

            // 파츠를 눕혀 밑면이 아래를 향하지 않게 만든다.
            moving.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue,
                "접합면이 마주보지 않으면 후보가 되어서는 안 된다.");
        }

        [Test]
        public void FindMatch_PrefersNearerSocket()
        {
            var near = SocketTestRig.Placed(_spawned, Vector3.zero);
            var far = SocketTestRig.Placed(_spawned, new Vector3(1.2f, 0f, 0f));

            // 가까운 쪽 소켓 바로 위에 둔다.
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.1f));

            var match = PartAssembler.FindMatch(moving);

            Assert.IsTrue(match.HasValue);
            Assert.AreSame(SocketTestRig.TopSocketOf(near), match.TargetSocket,
                $"더 가까운 소켓이 선택되어야 한다. 선택된 소유자는 " +
                $"{(match.TargetSocket.Owner == far ? "먼 쪽" : "가까운 쪽")}");
        }

        [Test]
        public void FindMatch_IsRepeatable()
        {
            SocketTestRig.Placed(_spawned, new Vector3(-0.6f, 0f, 0f));
            SocketTestRig.Placed(_spawned, new Vector3(0.6f, 0f, 0f));
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.2f));

            var first = PartAssembler.FindMatch(moving);
            for (var i = 0; i < 5; i++)
            {
                var again = PartAssembler.FindMatch(moving);
                Assert.AreSame(first.TargetSocket, again.TargetSocket,
                    "같은 상황에서는 항상 같은 후보가 나와야 한다.");
            }
        }

        [Test]
        public void FindMatch_SkipsOccupiedSocket()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var other = SocketTestRig.Placed(_spawned, Vector3.up * 40f);
            SocketTestRig.BaseSocketOf(other).Connect(SocketTestRig.TopSocketOf(lower));

            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.2f));

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue);
        }

        // --- 조립 ------------------------------------------------------------

        [Test]
        public void Assemble_AlignsSocketsExactly()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.4f));
            moving.transform.rotation = Quaternion.Euler(0f, 12f, 4f);
            UnityEngine.Physics.SyncTransforms();

            moving.TryTransitionTo(PartState.Carried);

            var match = PartAssembler.FindMatch(moving);
            Assert.IsTrue(match.HasValue);
            Assert.IsNotNull(PartAssembler.Assemble(match));

            var gap = Vector3.Distance(
                SocketTestRig.BaseSocketOf(moving).transform.position,
                SocketTestRig.TopSocketOf(lower).transform.position);

            Assert.Less(gap, 0.001f, $"두 소켓이 정확히 겹쳐야 한다. 오차 {gap:F5}m");
            Assert.AreEqual(PhysicsScale.PartHeight,
                Vector3.Distance(moving.transform.position, lower.transform.position), 0.001f);
        }

        [Test]
        public void Assemble_MarksBothSocketsOccupiedAndPlacesPart()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));
            moving.TryTransitionTo(PartState.Carried);

            PartAssembler.Assemble(PartAssembler.FindMatch(moving));

            Assert.AreEqual(PartState.Placed, moving.State);
            Assert.IsTrue(SocketTestRig.TopSocketOf(lower).IsOccupied);
            Assert.IsTrue(SocketTestRig.BaseSocketOf(moving).IsOccupied);
        }

        [Test]
        public void Assemble_CreatesBreakableJoint()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));

            moving.TryTransitionTo(PartState.Carried);
            PartAssembler.Assemble(PartAssembler.FindMatch(moving));

            var joint = moving.GetComponent<FixedJoint>();
            Assert.IsNotNull(joint, "용접 전에는 끊어질 수 있는 조인트로만 잡아야 한다.");
            Assert.AreEqual(PhysicsScale.UnweldedBreakForce, joint.breakForce, 0.1f);
            Assert.AreEqual(PhysicsScale.UnweldedBreakTorque, joint.breakTorque, 0.1f);
        }

        [Test]
        public void Link_ReleaseFreesTheSocketAgain()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));
            moving.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));
            link.Release();

            Assert.IsFalse(SocketTestRig.TopSocketOf(lower).IsOccupied);
            Assert.IsTrue(SocketTestRig.TopSocketOf(lower).CanReceive);
            Assert.IsNull(moving.GetComponent<FixedJoint>());
        }

        [Test]
        public void Link_TickDetectsBrokenJoint()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));
            moving.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));

            // 물리 콜백이 오지 않는 상황에서도 안전망이 동작해야 한다.
            Object.DestroyImmediate(moving.GetComponent<FixedJoint>());
            link.Tick();

            Assert.IsFalse(SocketTestRig.TopSocketOf(lower).IsOccupied,
                "조인트가 사라졌으면 그 자리에 다시 쌓을 수 있어야 한다.");
        }

        [Test]
        public void AssembledPair_HoldsTogetherUnderGravity()
        {
            // 이 씬에는 지면이 없어 조립체가 통째로 자유낙하한다. 그것은 정상이다.
            // 확인할 것은 두 파츠의 상대 위치가 유지되는지, 즉 조인트가 버티는지다.
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var upper = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));
            upper.TryTransitionTo(PartState.Carried);

            PartAssembler.Assemble(PartAssembler.FindMatch(upper));

            for (var i = 0; i < Mathf.RoundToInt(5f / Tuning.FixedStep); i++)
                UnityEngine.Physics.Simulate(Tuning.FixedStep);

            Assert.IsNotNull(upper.GetComponent<FixedJoint>(),
                "스스로는 조인트가 끊어지지 않아야 한다.");

            var offset = upper.transform.position - lower.transform.position;
            Assert.AreEqual(PhysicsScale.PartHeight, offset.magnitude, 0.1f,
                $"두 파츠 간격이 벌어졌다. {offset.magnitude:F3}m");
            Assert.Greater(offset.y, 0f, "위 파츠가 아래로 내려가면 안 된다.");
        }

        [Test]
        public void Assemble_RefusesPartThatIsNotCarriedOrPlaced()
        {
            // Free 파츠를 그냥 붙이면 상태가 Placed 로 올라가지 못해
            // 그 위에 다음 층을 쌓을 수 없는 상태가 된다. 조용히 통과시키지 않는다.
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + 0.3f));

            var match = PartAssembler.FindMatch(moving);
            Assert.IsTrue(match.HasValue);

            Assert.IsNull(PartAssembler.Assemble(match),
                "Free 상태의 파츠는 조립 대상이 아니다.");
            Assert.AreEqual(PartState.Free, moving.State);
            Assert.IsNull(moving.GetComponent<FixedJoint>());
        }

        [Test]
        public void ThreeTierTower_CanBeStackedOneAfterAnother()
        {
            var tiers = new SpaceshipPart[3];
            tiers[0] = SocketTestRig.Placed(_spawned, Vector3.zero);

            for (var i = 1; i < 3; i++)
            {
                tiers[i] = SocketTestRig.Freed(
                    _spawned, Vector3.up * (PhysicsScale.PartHeight * i + 0.3f));
                tiers[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(tiers[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}단 스냅 후보가 없다.");
                Assert.IsNotNull(PartAssembler.Assemble(match), $"{i + 1}단 조립이 실패했다.");
                Assert.AreEqual(PartState.Placed, tiers[i].State,
                    $"{i + 1}단이 Placed 가 되지 않으면 그 위에 쌓을 수 없다.");
            }

            for (var i = 1; i < 3; i++)
                Assert.AreEqual(PhysicsScale.PartHeight,
                    Vector3.Distance(tiers[i - 1].transform.position, tiers[i].transform.position), 0.01f);
        }

        // --- 빔 연동 ---------------------------------------------------------

        [Test]
        public void Beam_AlignsTargetRotationToMatch()
        {
            SocketTestRig.Placed(_spawned, new Vector3(0f, 0f, 8f));
            var moving = SocketTestRig.Freed(
                _spawned, new Vector3(0f, PhysicsScale.PartHeight + 0.3f, 8f));
            moving.transform.rotation = Quaternion.Euler(0f, 20f, 6f);
            UnityEngine.Physics.SyncTransforms();

            var beam = CreateBeam(moving.transform.position);
            Assert.AreEqual(BeamGrabResult.Grabbed, beam.TryGrab());

            beam.Tick(Tuning.FixedStep);

            Assert.IsTrue(beam.CurrentMatch.HasValue, "다가가는 동안 후보가 잡혀야 한다.");
            Assert.AreEqual(0f, Quaternion.Angle(beam.TargetRotation, beam.CurrentMatch.SnappedRotation), 0.01f,
                "목표 자세가 정렬 회전으로 맞춰져야 한다.");
        }

        [Test]
        public void Beam_AssemblesOnRelease_WhenMatchExists()
        {
            var lower = SocketTestRig.Placed(_spawned, new Vector3(0f, 0f, 8f));
            var moving = SocketTestRig.Freed(
                _spawned, new Vector3(0f, PhysicsScale.PartHeight + 0.3f, 8f));

            var beam = CreateBeam(moving.transform.position);
            beam.TryGrab();
            beam.Tick(Tuning.FixedStep);

            PartAssemblyLink assembled = null;
            beam.Assembled += link => assembled = link;

            beam.Release();

            Assert.IsNotNull(assembled, "후보가 있으면 정렬 배치로 조립되어야 한다.");
            Assert.AreEqual(PartState.Placed, moving.State);
            Assert.IsTrue(SocketTestRig.TopSocketOf(lower).IsOccupied);
        }

        [Test]
        public void Beam_PlacesFreely_WhenNoMatchExists()
        {
            var moving = SocketTestRig.Freed(_spawned, new Vector3(0f, 20f, 8f));

            var beam = CreateBeam(moving.transform.position);
            beam.TryGrab();
            beam.Tick(Tuning.FixedStep);

            PartAssemblyLink assembled = null;
            beam.Assembled += link => assembled = link;

            beam.Release();

            Assert.IsNull(assembled);
            Assert.AreEqual(PartState.Placed, moving.State);
            Assert.IsNull(moving.GetComponent<FixedJoint>());
        }

        TractorBeam CreateBeam(Vector3 aimAt)
        {
            // 대상 바로 위에서 아래를 겨눈다.
            var go = new GameObject("Beam");
            go.transform.SetPositionAndRotation(
                aimAt + Vector3.up * 8f, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            var beam = go.AddComponent<TractorBeam>();
            beam.Configure(go.transform, PhysicsScale.BeamRange, 8f);

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return beam;
        }
    }
}
