using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 견인 광선의 붙잡기 조건, 상태 연동, 추적 안정성을 검증한다.
    ///
    /// TractorBeam 이 Tick(deltaTime) 을 받기 때문에 Physics.Simulate 와 짝지어
    /// PlayMode 없이 실제 물리 거동까지 확인할 수 있다.
    /// </summary>
    public sealed class TractorBeamTests
    {
        readonly List<GameObject> _spawned = new();

        SimulationMode _previousMode;

        [SetUp]
        public void SetUp()
        {
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
        }

        // --- 붙잡기 조건 -----------------------------------------------------

        [Test]
        public void TryGrab_FailsWithNoTarget()
        {
            var beam = CreateBeam();

            Assert.AreEqual(BeamGrabResult.NoTarget, beam.TryGrab());
            Assert.IsFalse(beam.IsHolding);
        }

        [Test]
        public void TryGrab_RefusesClampedPart()
        {
            var part = CreatePart(new Vector3(0f, 0f, 8f));   // Locked 로 시작한다
            var beam = CreateBeam();

            Assert.AreEqual(BeamGrabResult.Clamped, beam.TryGrab());
            Assert.AreEqual(PartState.Locked, part.State);
        }

        [Test]
        public void TryGrab_RefusesWeldedPart()
        {
            var part = CreatePart(new Vector3(0f, 0f, 8f));
            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);
            part.TryTransitionTo(PartState.Welded);

            var beam = CreateBeam();

            Assert.AreEqual(BeamGrabResult.Welded, beam.TryGrab());
        }

        [Test]
        public void TryGrab_MovesFreePartToCarried()
        {
            var part = Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();

            Assert.AreEqual(BeamGrabResult.Grabbed, beam.TryGrab());
            Assert.AreSame(part, beam.Held);
            Assert.AreEqual(PartState.Carried, part.State);
            Assert.AreEqual(GameLayers.PartCarried, part.gameObject.layer);
            Assert.AreEqual(PhysicsScale.CarriedPartMass, part.GetComponent<Rigidbody>().mass, 0.001f);
        }

        [Test]
        public void TryGrab_FailsWhenBeamIsAlreadyHolding()
        {
            Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();

            Assert.AreEqual(BeamGrabResult.Grabbed, beam.TryGrab());
            Assert.AreEqual(BeamGrabResult.BeamBusy, beam.TryGrab());
        }

        [Test]
        public void TryGrab_RefusesPartHeldByAnotherBeam()
        {
            Freed(new Vector3(0f, 0f, 8f));
            var first = CreateBeam();
            var second = CreateBeam();

            Assert.AreEqual(BeamGrabResult.Grabbed, first.TryGrab());
            Assert.AreEqual(BeamGrabResult.AlreadyCarried, second.TryGrab());
            Assert.IsFalse(second.IsHolding);
        }

        // --- 놓기 ------------------------------------------------------------

        [Test]
        public void Release_ReturnsPartToPlacedAndRestoresMass()
        {
            var part = Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();
            beam.TryGrab();

            beam.Release();

            Assert.IsFalse(beam.IsHolding);
            Assert.AreEqual(PartState.Placed, part.State);
            Assert.AreEqual(GameLayers.Part, part.gameObject.layer);
            Assert.AreEqual(PhysicsScale.PartMass, part.GetComponent<Rigidbody>().mass, 0.001f);
        }

        [Test]
        public void Release_RaisesEventOnce()
        {
            Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();
            beam.TryGrab();

            var raised = 0;
            beam.ReleasedPart += _ => raised++;

            beam.Release();
            beam.Release();

            Assert.AreEqual(1, raised);
        }

        // --- 추적 거동 -------------------------------------------------------

        [Test]
        public void Beam_PullsPartTowardHoldPoint()
        {
            var part = Freed(new Vector3(0f, 0f, 11f));
            var beam = CreateBeam();
            beam.TryGrab();

            var before = Vector3.Distance(part.transform.position, beam.HoldPoint);
            Simulate(beam, 1f);
            var after = Vector3.Distance(part.transform.position, beam.HoldPoint);

            Assert.Less(after, before * 0.5f, $"1초 안에 절반 이상 다가와야 한다. {before:F2} -> {after:F2}");
        }

        [Test]
        public void Beam_SettlesAtHoldPointWithoutOscillating()
        {
            var part = Freed(new Vector3(0f, 0f, 11f));
            var beam = CreateBeam();
            beam.TryGrab();

            Simulate(beam, 10f);

            var offset = Vector3.Distance(part.transform.position, beam.HoldPoint);
            var speed = part.GetComponent<Rigidbody>().linearVelocity.magnitude;

            Assert.Less(offset, 0.2f, $"목표점에 정착해야 한다. 오차 {offset:F3}m");
            Assert.Less(speed, 0.5f, $"정착 후 출렁이면 안 된다. 속도 {speed:F3}m/s");
        }

        [Test]
        public void Beam_HoldsPartAgainstGravity()
        {
            // 중력 보상이 없으면 파츠가 아래로 처진다.
            var part = Freed(new Vector3(0f, 0f, 6f));
            var beam = CreateBeam();
            beam.TryGrab();

            Simulate(beam, 5f);

            Assert.Less(Mathf.Abs(part.transform.position.y - beam.HoldPoint.y), 0.2f,
                $"y 가 처졌다. {part.transform.position.y:F3} vs {beam.HoldPoint.y:F3}");
        }

        [Test]
        public void ReleasedPart_FallsAgain()
        {
            var part = Freed(new Vector3(0f, 0f, 6f));
            var beam = CreateBeam();
            beam.TryGrab();
            Simulate(beam, 3f);

            var heightWhileHeld = part.transform.position.y;
            beam.Release();
            Simulate(beam, 2f);

            Assert.Less(part.transform.position.y, heightWhileHeld - 1f,
                "놓으면 중력 보상이 사라져 떨어져야 한다.");
        }

        [Test]
        public void Beam_ReleasesWhenPartLeavesRange()
        {
            var part = Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();
            beam.TryGrab();

            // 기준점을 파츠에서 멀리 옮긴다.
            beam.transform.position = new Vector3(0f, 0f, -100f);
            UnityEngine.Physics.SyncTransforms();

            beam.Tick(Tuning.FixedStep);

            Assert.IsFalse(beam.IsHolding);
            Assert.AreEqual(PartState.Placed, part.State);
        }

        [Test]
        public void Beam_KeepsAngularSpeedWithinPartLimit()
        {
            var part = Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();
            beam.TryGrab();

            // 자세를 크게 어긋나게 만들어 회전 보정이 최대로 걸리게 한다.
            part.transform.rotation = Quaternion.Euler(0f, 179f, 0f);
            UnityEngine.Physics.SyncTransforms();

            beam.Tick(Tuning.FixedStep);

            var angularSpeed = part.GetComponent<Rigidbody>().angularVelocity.magnitude;
            Assert.LessOrEqual(angularSpeed, PhysicsScale.PartMaxAngularSpeed + 0.001f,
                $"각속도 상한을 넘었다. {angularSpeed:F3} rad/s");
        }

        [Test]
        public void Beam_ReleasesWhenPartStateChangesExternally()
        {
            var part = Freed(new Vector3(0f, 0f, 8f));
            var beam = CreateBeam();
            beam.TryGrab();

            // 다른 시스템이 상태를 바꾼 상황.
            part.TryTransitionTo(PartState.Placed);
            beam.Tick(Tuning.FixedStep);

            Assert.IsFalse(beam.IsHolding);
        }

        // --- 헬퍼 ------------------------------------------------------------

        static void Simulate(TractorBeam beam, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Tuning.FixedStep);
            for (var i = 0; i < steps; i++)
            {
                beam.Tick(Tuning.FixedStep);
                UnityEngine.Physics.Simulate(Tuning.FixedStep);
            }
        }

        SpaceshipPart CreatePart(Vector3 position)
        {
            var go = new GameObject("Part");
            go.transform.position = position;
            go.transform.localScale = new Vector3(
                PhysicsScale.PartWidth, PhysicsScale.PartHeight, PhysicsScale.PartWidth);
            go.AddComponent<BoxCollider>();

            var part = go.AddComponent<SpaceshipPart>();
            _spawned.Add(go);
            part.Initialize();
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        /// <summary>고정쇠가 없어 바로 Free 로 넘어간 파츠.</summary>
        SpaceshipPart Freed(Vector3 position)
        {
            var part = CreatePart(position);
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        TractorBeam CreateBeam()
        {
            var go = new GameObject("Beam");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward));

            var beam = go.AddComponent<TractorBeam>();
            beam.Configure(go.transform, PhysicsScale.BeamRange, PhysicsScale.BeamHoldDistance);

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return beam;
        }
    }
}
