using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>탑승 비행과 하차 후 무인 착륙을 검증한다 (2026-09-10 조작 변경).</summary>
    public sealed class CraftHoverTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;
        bool savedIgnore;

        [SetUp]
        public void SetUp()
        {
            savedMode = Physics.simulationMode;
            savedIgnore = LogAssert.ignoreFailingMessages;
            LowGravityField.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = savedMode;
            LogAssert.ignoreFailingMessages = savedIgnore;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            LowGravityField.All.Clear();
        }

        // --- 하차 후 착륙 (2026-09-10 요청) -----------------------------------------

        [Test]
        public void DismountWithFieldOn_ReleasesRiderAndLands()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);
            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            Assert.Greater(rig.Vehicle.transform.position.y, 1f);
            rig.Vehicle.BeginDismount();
            Assert.IsFalse(rig.Vehicle.IsOccupied, "하차 요청 즉시 조종사를 놓아야 한다.");
            Run(rig.Vehicle, 6f);
            Assert.IsFalse(rig.Vehicle.Unmanned);
            Assert.Less(rig.Vehicle.transform.position.y, 1.5f);
        }

        [Test]
        public void DismountWithFieldOff_StillLands()
        {
            var rig = MakeRig();

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            Assert.IsFalse(rig.Vehicle.Field.IsActive, "이 검증은 장이 꺼져 있어야 한다.");

            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 6f);

            Assert.IsFalse(rig.Vehicle.Unmanned, "장이 꺼져 있으면 무인 호버로 가지 않는다.");
            Assert.Less(rig.Vehicle.transform.position.y, 1.5f, "내려앉아야 한다.");
        }

        // --- 착륙 후 재탑승 --------------------------------------

        [Test]
        public void BoardingAfterLanding_StartsANewFlight()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);
            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 6f);
            var second = MakePlayer(rig.Vehicle.transform.position);
            Assert.IsTrue(rig.Vehicle.TryBoard(second));
            Assert.IsFalse(rig.Vehicle.Unmanned);
            Assert.IsFalse(rig.Vehicle.Field.IsActive);
            Run(rig.Vehicle, 2f);
            Assert.Greater(rig.Vehicle.transform.position.y, 1.5f);
        }

        // --- 충돌 반응 (기획 §14.1 · §14.2) ----------------------------------

        [Test]
        public void ImpactPushesPilotedCraft_WithoutPositionSnapping()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.SetVerticalInput(0f);
            Run(rig.Vehicle, 2f);

            var before = rig.Vehicle.transform.position;
            var body = rig.Vehicle.GetComponent<Rigidbody>();
            body.linearVelocity += new Vector3(4f, 0f, 0f);

            Run(rig.Vehicle, 3f);

            var after = rig.Vehicle.transform.position;
            var moved = Vector3.Distance(new Vector3(before.x, 0f, before.z),
                                         new Vector3(after.x, 0f, after.z));

            Assert.Greater(moved, 0.5f, $"충격이 전달되어야 한다. {moved:0.00}m 만 밀렸다.");
            Assert.IsFalse(rig.Vehicle.Unmanned, "하차한 기체는 무인 호버로 돌아가지 않는다.");
        }

        [Test]
        public void ImpactedCraft_StaysLevel()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 1f);

            var craft = rig.Vehicle.transform;
            var body = rig.Vehicle.GetComponent<Rigidbody>();

            Assert.AreEqual(RigidbodyConstraints.FreezeRotation, body.constraints,
                "회전이 잠겨 있어야 이 보장이 성립한다. 풀면 자세 복구를 만들어야 한다.");

            // 옆에서 세게 밀고 비트는 충격을 함께 준다.
            body.linearVelocity += new Vector3(6f, 0f, 0f);
            body.AddTorque(new Vector3(0f, 0f, 400f), ForceMode.Impulse);

            Run(rig.Vehicle, 3f);

            // 이 기체는 로컬 forward 가 아래를 향할 때가 수평이다(루트가
            // LookRotation(down, forward) 로 세워져 있다).
            var tilt = Vector3.Angle(-craft.forward, Vector3.up);
            Assert.Less(tilt, 5f, $"충격을 받아도 기체는 수평이어야 한다. {tilt:0.0}도 기울었다.");
        }

        // --- 속도 (기획 §14.1) ----------------------------------------------

        /// <summary>기획 §14.1 — 일반 8m/s · 가속 12m/s.</summary>
        [Test]
        public void Boost_RaisesTopSpeed()
        {
            var rig = MakeRig();

            rig.Vehicle.SetBoost(false);
            var normal = rig.Vehicle.CurrentMaxSpeed;

            rig.Vehicle.SetBoost(true);
            var boosted = rig.Vehicle.CurrentMaxSpeed;

            Assert.AreEqual(8f, normal, 0.01f, "일반 속도는 기획의 8m/s 다.");
            Assert.AreEqual(PhysicsScale.CraftBoostSpeed, boosted, 0.01f, "가속은 12m/s 다.");
        }

        /// <summary>기획 §14.1 — "파츠 견인 중 최고 속도는 약 20% 감소한다".</summary>
        [Test]
        public void Towing_SlowsTheCraft()
        {
            Assert.AreEqual(0.8f, PhysicsScale.CraftTowSpeedFactor, 0.001f,
                "기획의 20% 감소는 0.8 배다.");

            var rig = MakeRig();
            var free = rig.Vehicle.CurrentMaxSpeed;

            // 빔이 파츠를 잡으면 견인 중이다.
            var cargo = SocketTestRig.Freed(
                spawned, rig.Vehicle.transform.position + Vector3.down * 3f);

            Assert.AreEqual(BeamGrabResult.Grabbed, rig.Vehicle.Beam.TryGrab(cargo),
                "빔이 잡아야 이 검증이 성립한다.");
            Assert.IsTrue(rig.Vehicle.Beam.IsHolding);

            Assert.AreEqual(free * PhysicsScale.CraftTowSpeedFactor,
                            rig.Vehicle.CurrentMaxSpeed, 0.01f,
                "견인 중에는 최고 속도가 20% 낮아야 한다.");
        }

        // --- 헬퍼 ----------------------------------------------------------

        struct Rig
        {
            public TransportVehicle Vehicle;
            public PlayerRig Player;
        }

        Rig MakeRig()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var position = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(
                position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            spawned.Add(go);

            var player = MakePlayer(position + Vector3.up * 0.5f);
            Assert.IsTrue(vehicle.TryBoard(player), "먼저 탑승해야 한다.");

            return new Rig { Vehicle = vehicle, Player = player };
        }

        PlayerRig MakePlayer(Vector3 position)
        {
            // PlayerRig.Create 경로의 Prim.Decor 가 EditMode 에서 에러 로그를 남긴다.
            // [SetUp] 에서 켜면 프레임워크가 본문 시작 전에 되돌리므로 여기서 켠다.
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", position, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        void MakeSurface(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = 0 };
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            spawned.Add(go);
        }

        void Run(TransportVehicle vehicle, float seconds)
        {
            Physics.simulationMode = SimulationMode.Script;
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                vehicle.Tick(Step);
                Physics.Simulate(Step);
            }
        }
    }
}
