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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            var match = PartAssembler.FindMatch(moving);

            Assert.IsTrue(match.HasValue);
            Assert.AreSame(SocketTestRig.TopSocketOf(lower), match.TargetSocket);
            Assert.AreSame(SocketTestRig.BaseSocketOf(moving), match.MovingSocket);
        }

        /// <summary>
        /// 위아래로 벌어지면 접합선이 이어지지 않는다. 기획 §15.2 의 "접합면 간격 0.2m" 다.
        /// </summary>
        [Test]
        public void FindMatch_RejectsBeyondGapTolerance()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + PhysicsScale.WeldGapTolerance + 0.2f));

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue,
                "접합면이 허용 간격보다 벌어지면 후보가 되어서는 안 된다.");
        }

        /// <summary>
        /// 옆으로 어긋나는 것은 위아래보다 너그럽다. 기획 §15.2 의 "중심 오차 0.8m" 다.
        /// 예전에는 3차원 거리 하나로 재서 두 방향을 구분하지 못했다.
        /// </summary>
        [Test]
        public void FindMatch_AllowsSidewaysOffsetWithinCenterTolerance()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);

            // 위아래는 딱 맞추고 옆으로만 허용치의 절반 어긋나게 둔다.
            var moving = SocketTestRig.Freed(_spawned, new Vector3(
                PhysicsScale.WeldCenterTolerance * 0.5f, PhysicsScale.PartHeight, 0f));

            Assert.IsTrue(PartAssembler.FindMatch(moving).HasValue,
                "옆으로 어긋난 정도가 허용치 안이면 붙을 수 있어야 한다.");
        }

        [Test]
        public void FindMatch_RejectsBeyondCenterTolerance()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, new Vector3(
                PhysicsScale.WeldCenterTolerance + 0.5f, PhysicsScale.PartHeight, 0f));

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue,
                "옆으로 허용치보다 어긋나면 후보가 되어서는 안 된다.");
        }

        [Test]
        public void FindMatch_RejectsBeyondTiltTolerance()
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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

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

            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            Assert.IsFalse(PartAssembler.FindMatch(moving).HasValue);
        }

        // --- 조립 ------------------------------------------------------------

        /// <summary>
        /// 이 규칙의 핵심. <b>조립해도 파츠를 옮기지 않는다.</b>
        ///
        /// 예전에는 조립할 때 위치와 회전을 정렬해 두 소켓을 정확히 겹쳐 놓았다. 그러면
        /// 비뚤어진 적층이 존재할 수 없고, 기획(S15P21B202-107 §15.1)의 "잘못 올리면 기울거나
        /// 떨어진다" 도, §22.3 발사 하중 시험도, §22.4 붕괴도 성립하지 않는다.
        /// </summary>
        [Test]
        public void Assemble_KeepsPoseExactlyAsPlaced()
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);

            // 허용 범위 안에서 일부러 비뚤게 올린다.
            var moving = SocketTestRig.Freed(_spawned, new Vector3(0.3f, PhysicsScale.PartHeight + SocketTestRig.StackGap, 0.2f));
            moving.transform.rotation = Quaternion.Euler(0f, 12f, 4f);
            UnityEngine.Physics.SyncTransforms();

            moving.TryTransitionTo(PartState.Carried);

            var position = moving.transform.position;
            var rotation = moving.transform.rotation;

            var match = PartAssembler.FindMatch(moving);
            Assert.IsTrue(match.HasValue, "허용 범위 안이므로 붙을 수 있어야 한다.");
            Assert.IsNotNull(PartAssembler.Assemble(match));

            Assert.AreEqual(0f, Vector3.Distance(position, moving.transform.position), 0.001f,
                "조립이 파츠를 옮겼다. 자동 정렬이 되살아났다.");
            Assert.AreEqual(0f, Quaternion.Angle(rotation, moving.transform.rotation), 0.01f,
                "조립이 파츠를 돌렸다. 회전 보정이 되살아났다.");
        }

        /// <summary>비뚤게 붙인 뒤에도 그 어긋남이 남아 있어야 발사 하중 시험이 의미를 갖는다.</summary>
        [Test]
        public void Assemble_LeavesSocketsMisalignedWhenPlacedCrooked()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, new Vector3(0.4f, PhysicsScale.PartHeight + SocketTestRig.StackGap, 0f));
            UnityEngine.Physics.SyncTransforms();

            moving.TryTransitionTo(PartState.Carried);
            Assert.IsNotNull(PartAssembler.Assemble(PartAssembler.FindMatch(moving)));

            var offset = Vector3.Distance(
                SocketTestRig.BaseSocketOf(moving).transform.position,
                SocketTestRig.TopSocketOf(lower).transform.position);

            Assert.Greater(offset, 0.1f,
                $"두 소켓이 {offset:F3}m 만 떨어져 있다. 0 에 가까우면 정렬해 버린 것이다.");
        }

        [Test]
        public void Assemble_MarksBothSocketsOccupiedAndPlacesPart()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
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
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
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
            var upper = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));
            upper.TryTransitionTo(PartState.Carried);

            PartAssembler.Assemble(PartAssembler.FindMatch(upper));

            // 붙인 직후의 상대 위치를 기준으로 삼는다. 정렬을 없앤 뒤로는 올려놓은 자리가
            // 그대로 남으므로, 기대값을 PartHeight 같은 고정값으로 적을 수 없다.
            var placed = upper.transform.position - lower.transform.position;

            for (var i = 0; i < Mathf.RoundToInt(5f / Tuning.FixedStep); i++)
                UnityEngine.Physics.Simulate(Tuning.FixedStep);

            Assert.IsNotNull(upper.GetComponent<FixedJoint>(),
                "스스로는 조인트가 끊어지지 않아야 한다.");

            var offset = upper.transform.position - lower.transform.position;
            Assert.AreEqual(placed.magnitude, offset.magnitude, 0.1f,
                $"두 파츠 간격이 {placed.magnitude:F3}m 에서 {offset.magnitude:F3}m 로 바뀌었다.");
            Assert.Greater(offset.y, 0f, "위 파츠가 아래로 내려가면 안 된다.");
        }

        [Test]
        public void Assemble_RefusesPartThatIsNotCarriedOrPlaced()
        {
            // Free 파츠를 그냥 붙이면 상태가 Placed 로 올라가지 못해
            // 그 위에 다음 층을 쌓을 수 없는 상태가 된다. 조용히 통과시키지 않는다.
            SocketTestRig.Placed(_spawned, Vector3.zero);
            var moving = SocketTestRig.Freed(_spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

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
                    _spawned, Vector3.up * (PhysicsScale.PartHeight * i + SocketTestRig.StackGap));
                tiers[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(tiers[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}단 스냅 후보가 없다.");
                Assert.IsNotNull(PartAssembler.Assemble(match), $"{i + 1}단 조립이 실패했다.");
                Assert.AreEqual(PartState.Placed, tiers[i].State,
                    $"{i + 1}단이 Placed 가 되지 않으면 그 위에 쌓을 수 없다.");
            }

            // 간격을 정확한 값으로 확인하지 않는다. 정렬을 없앤 뒤로는 올려놓은 자리가 그대로
            // 남으므로, 층 간격은 "쌓인 순서대로 위로 간다" 는 것만 보면 된다.
            for (var i = 1; i < 3; i++)
                Assert.Greater(tiers[i].transform.position.y, tiers[i - 1].transform.position.y,
                    $"{i + 1}단이 아래 층보다 위에 있어야 한다.");
        }

        // --- 빔 연동 ---------------------------------------------------------

        /// <summary>
        /// 빔은 후보가 잡혀도 <b>방향을 돌려 주지 않는다.</b>
        ///
        /// 예전에는 후보가 잡히면 목표 회전을 정렬 회전으로 덮어써서, 파츠가 다가가는 동안
        /// 저절로 방향이 맞았다. 기획 §15.1 이 금지하는 회전 보정이다. 방향은 플레이어가
        /// SetTargetRotation 으로 직접 맞춘다.
        /// </summary>
        [Test]
        public void Beam_DoesNotAlignRotationToMatch()
        {
            SocketTestRig.Placed(_spawned, new Vector3(0f, 0f, 8f));
            var moving = SocketTestRig.Freed(
                _spawned, new Vector3(0f, PhysicsScale.PartHeight + SocketTestRig.StackGap, 8f));

            // 허용 각도 안에서 살짝 비틀어 둔다. 빔이 이것을 펴면 안 된다.
            var crooked = Quaternion.Euler(0f, 20f, 6f);
            moving.transform.rotation = crooked;
            UnityEngine.Physics.SyncTransforms();

            var beam = CreateBeam(moving.transform.position);
            Assert.AreEqual(BeamGrabResult.Grabbed, beam.TryGrab());

            beam.Tick(Tuning.FixedStep);

            Assert.IsTrue(beam.CurrentMatch.HasValue, "다가가는 동안 후보가 잡혀야 한다.");
            Assert.AreEqual(0f, Quaternion.Angle(beam.TargetRotation, crooked), 0.01f,
                "빔이 목표 자세를 바꿨다. 회전 보정이 되살아났다.");
        }

        [Test]
        public void Beam_AssemblesOnRelease_WhenMatchExists()
        {
            var lower = SocketTestRig.Placed(_spawned, new Vector3(0f, 0f, 8f));
            var moving = SocketTestRig.Freed(
                _spawned, new Vector3(0f, PhysicsScale.PartHeight + SocketTestRig.StackGap, 8f));

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
