using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 견인 조종 모듈의 무인 호버와 충돌 반응을 검증한다
    /// (기획 S15P21B202-107 §14.1 · §14.2).
    ///
    /// 예전에는 하차하면 <c>body.useGravity = true</c> 로 기체가 <b>떨어졌다</b>.
    /// 그러면 §14.2 의 여섯 항목이 모두 성립하지 않고, 무엇보다 §14.5 의
    /// "조종사가 저중력장을 유지해 아래의 용접 담당자를 띄운다" 가 불가능하다 —
    /// 조종사가 내리는 순간 장이 사라진다. 1인승 기체로 협동을 만드는 유일한 장치다.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 를 검증 본문에서 켜는 이유는
    /// <c>TransportAltitudeTests</c> 와 같다 — <c>PlayerRig.Create</c> 경로의
    /// <c>Prim.Decor</c> 가 EditMode 에서 에러 로그를 남긴다.
    /// </summary>
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

        // --- 무인 호버 (기획 §14.2) -----------------------------------------

        /// <summary>
        /// <b>이 이슈의 핵심이다.</b> 기획 §14.2 — "저중력장을 켠 채 하차해도 기체가
        /// 마지막 위치와 높이에서 자동 호버한다".
        /// </summary>
        [Test]
        public void DismountWithFieldOn_KeepsHovering()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.SetVerticalInput(0f);
            Run(rig.Vehicle, 1f);

            var height = rig.Vehicle.transform.position.y;
            Assert.Greater(height, 1f, "먼저 떠 있어야 한다.");

            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 3f);

            Assert.IsTrue(rig.Vehicle.Unmanned, "조종사 없이 호버를 유지해야 한다.");
            Assert.AreEqual(height, rig.Vehicle.transform.position.y, 0.6f,
                $"마지막 높이에서 떠 있어야 한다. {height:0.00}m → {rig.Vehicle.transform.position.y:0.00}m");
        }

        /// <summary>기획 §14.2 — "저중력장도 켜진 상태를 유지한다".</summary>
        [Test]
        public void DismountWithFieldOn_KeepsTheFieldOn()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 2f);

            Assert.IsTrue(rig.Vehicle.Field.IsActive,
                "장이 꺼지면 아래에서 용접하던 사람이 떨어진다(기획 §14.5).");
        }

        /// <summary>
        /// 저중력장이 꺼져 있으면 예전대로 내려앉는다.
        /// 기획 §14.2 는 <b>켠 채</b> 하차한 경우만 말한다.
        /// </summary>
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

        // --- 조종 이어받기 (기획 §14.2) --------------------------------------

        /// <summary>
        /// 기획 §14.2 — "다른 플레이어가 … 탑승해 조종을 이어받을 수 있다.
        /// 누가 탑승하든 조종 권한을 즉시 획득한다".
        ///
        /// 이어받는 것이므로 <b>고도를 초기화하지 않는다.</b> 예전 코드는 탑승할 때마다
        /// 목표 고도를 5m 로 되돌렸다 — 새로 탄 사람이 기체를 끌어올리게 된다.
        /// </summary>
        [Test]
        public void BoardingUnmannedCraft_TakesOverAtCurrentAltitude()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2.5f);
            rig.Vehicle.SetVerticalInput(0f);
            Run(rig.Vehicle, 1f);

            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 2f);

            var height = rig.Vehicle.transform.position.y;
            Assert.IsTrue(rig.Vehicle.Unmanned, "먼저 무인 호버여야 한다.");
            Assert.Greater(height, TransportVehicle.HoverRootY + 0.5f,
                "이 검증은 기본 호버 높이보다 위에 있어야 의미가 있다.");

            var second = MakePlayer(rig.Vehicle.transform.position);
            Assert.IsTrue(rig.Vehicle.TryBoard(second), "무인 호버 중인 기체에 탈 수 있어야 한다.");

            Assert.IsFalse(rig.Vehicle.Unmanned, "탑승하면 무인 상태가 풀린다.");
            Assert.AreEqual(height, rig.Vehicle.TargetAltitude, 0.6f,
                $"이어받은 고도를 지켜야 한다. {height:0.00}m 인데 목표가 {rig.Vehicle.TargetAltitude:0.00}m 다.");
        }

        // --- 충돌 반응 (기획 §14.1 · §14.2) ----------------------------------

        /// <summary>
        /// 기획 §14.2 — "호버 중 운석이나 파츠에 맞으면 기체와 저중력장 위치가 함께
        /// 밀린다 … 호버 위치를 자동으로 되찾지는 않는다".
        /// </summary>
        [Test]
        public void ImpactPushesTheCraft_AndItDoesNotReturn()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 2f);

            var before = rig.Vehicle.transform.position;
            var body = rig.Vehicle.GetComponent<Rigidbody>();
            body.linearVelocity += new Vector3(4f, 0f, 0f);

            Run(rig.Vehicle, 3f);

            var after = rig.Vehicle.transform.position;
            var moved = Vector3.Distance(new Vector3(before.x, 0f, before.z),
                                         new Vector3(after.x, 0f, after.z));

            Assert.Greater(moved, 0.5f, $"충격이 전달되어야 한다. {moved:0.00}m 만 밀렸다.");
            Assert.IsTrue(rig.Vehicle.Unmanned, "밀린 뒤에도 호버를 유지한다.");
        }

        /// <summary>저중력장은 기체에 붙어 있으므로 함께 밀린다(기획 §14.2).</summary>
        [Test]
        public void ImpactMovesTheFieldWithTheCraft()
        {
            var rig = MakeRig();
            rig.Vehicle.Field.SetActive(true);

            rig.Vehicle.SetVerticalInput(1f);
            Run(rig.Vehicle, 2f);
            rig.Vehicle.BeginDismount();
            Run(rig.Vehicle, 1f);

            var body = rig.Vehicle.GetComponent<Rigidbody>();
            body.linearVelocity += new Vector3(4f, 0f, 0f);
            Run(rig.Vehicle, 2f);

            var craft = rig.Vehicle.transform.position;
            var origin = rig.Vehicle.Field.Origin.position;

            Assert.AreEqual(craft.x, origin.x, 0.01f, "장이 기체를 따라와야 한다.");
            Assert.AreEqual(craft.z, origin.z, 0.01f, "장이 기체를 따라와야 한다.");
        }

        /// <summary>
        /// 기획 §14.1 — "충돌 후 기체는 자동으로 수평을 회복한다".
        ///
        /// <b>이미 성립한다 — 기체가 애초에 기울지 않기 때문이다.</b>
        /// <c>EnsurePhysics</c> 가 <c>RigidbodyConstraints.FreezeRotation</c> 을 건다.
        /// 자세 복구 코드를 넣어 봤지만 회전이 잠겨 있어 아무 일도 하지 않았다.
        ///
        /// 실제로 기울었다 돌아오게 하려면 잠금을 풀어야 하는데, 그러면 탑승자가 서 있는
        /// 플랫폼이 함께 기울고 조인트로 붙은 인형이 흔들린다. 별 결정이므로 이 이슈에서는
        /// 하지 않았다. 이 검증은 <b>플레이어가 보는 보장</b>을 잠근다 — 충격을 받아도
        /// 기체는 수평이다.
        /// </summary>
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
