using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 탑승 중 고도 조작을 검증한다.
    ///
    /// 예전에는 목표 고도가 <see cref="TransportVehicle.HoverRootY"/>(5m) 하나로 고정이라
    /// 스프링이 항상 그 높이로 되돌렸다. 여기서 보는 것은 네 가지다 — 올라가는지,
    /// 내려가는지, 손을 떼면 그 고도를 유지하는지, 그리고 상·하한이 지켜지는지.
    ///
    /// <c>MakeRiddenVehicle</c> 이 <c>LogAssert.ignoreFailingMessages</c> 를 켜는 이유는
    /// 이 테스트와 무관하다 — <c>PlayerRig.Create</c> 경로의 <c>Prim.Decor</c> 가 edit mode 에서
    /// <c>Object.Destroy</c> 를 불러 에러 로그를 남긴다(develop 에 이미 있는 문제).
    /// </summary>
    public sealed class TransportAltitudeTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedSimulationMode;
        bool savedIgnoreFailingMessages;

        [SetUp]
        public void SetUp()
        {
            savedSimulationMode = Physics.simulationMode;
            savedIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = savedSimulationMode;

            // 켜 둔 채로 끝내면 같은 세션의 다른 테스트가 에러 로그를 놓친다.
            LogAssert.ignoreFailingMessages = savedIgnoreFailingMessages;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        GameObject MakeSurface(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = 0 };
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            spawned.Add(go);
            return go;
        }

        /// <summary>탑승자를 태운 운반체. 고도 조작은 탑승 중에만 동작한다.</summary>
        TransportVehicle MakeRiddenVehicle(Vector3 position)
        {
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            spawned.Add(go);

            // PlayerRig.Create 경로의 Prim.Decor 가 edit mode 에서 Object.Destroy 를 불러
            // 에러 로그를 남긴다(develop 에 이미 있는 문제, 이 MR 범위 아님).
            // [SetUp] 에서 켜면 테스트 프레임워크가 본문 시작 전에 되돌리므로 여기서 켠다.
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", position + Vector3.up * 0.5f, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);

            Assert.IsTrue(vehicle.TryBoard(player), "고도 조작은 탑승 중에만 동작한다.");
            return vehicle;
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

        [Test]
        public void Board_StartsAtDefaultHoverAltitude()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            Assert.AreEqual(TransportVehicle.HoverRootY, vehicle.TargetAltitude, 0.01f,
                "탑승 직후 목표 고도는 기존 부양 높이여야 한다.");
        }

        [Test]
        public void Ascend_RaisesTargetAltitude_AndHoldsAfterRelease()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            vehicle.SetVerticalInput(1f);
            Run(vehicle, 0.5f);

            var raised = vehicle.TargetAltitude;
            Assert.Greater(raised, TransportVehicle.HoverRootY + 0.2f, "Space 입력이면 목표 고도가 올라야 한다.");

            // 손을 뗀다. 예전 구현은 여기서 5m 로 되돌아갔다.
            vehicle.SetVerticalInput(0f);
            Run(vehicle, 1.0f);

            Assert.AreEqual(raised, vehicle.TargetAltitude, 0.01f,
                "입력을 떼면 그 고도를 유지해야 한다. 고정 높이로 되돌아가면 실패한다.");
        }

        [Test]
        public void Descend_LowersTargetAltitude()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            Run(vehicle, 1.5f);                       // 먼저 기본 고도까지 올라간다
            vehicle.SetVerticalInput(-1f);
            Run(vehicle, 0.5f);

            Assert.Less(vehicle.TargetAltitude, TransportVehicle.HoverRootY - 0.2f,
                "Ctrl 입력이면 목표 고도가 내려가야 한다.");
        }

        [Test]
        public void Ascend_StopsAtMaxAltitude()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            vehicle.SetVerticalInput(1f);
            Run(vehicle, 8f);                         // 상한을 훨씬 넘길 만큼 쥐고 있는다

            Assert.LessOrEqual(vehicle.TargetAltitude, 8.01f,
                "상한을 넘으면 경계 벽 콜라이더(9m)를 넘어 아레나를 빠져나갈 수 있다.");
        }

        [Test]
        public void Descend_StopsOnStructureTopInsteadOfSinking()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            MakeSurface("Structure", new Vector3(0f, 1.25f, 0f), new Vector3(12f, 2.5f, 12f));

            var vehicle = MakeRiddenVehicle(new Vector3(0f, 2.5f + TransportVehicle.GroundY, 0f));

            Run(vehicle, 1.5f);                       // 기본 고도까지 부양
            vehicle.SetVerticalInput(-1f);
            Run(vehicle, 4f);                         // 계속 내리려 한다

            Assert.Greater(vehicle.TargetAltitude, 2.0f,
                "구조물 상단에서 멈춰야 한다. 계속 내리면 스프링이 표면을 파고들도록 밀어붙인다.");
            Assert.Greater(vehicle.transform.position.y, 2.0f,
                "실제 위치도 구조물 위에 남아야 한다.");
        }

        [Test]
        public void Hovering_TrueAtNonDefaultAltitude()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            vehicle.SetVerticalInput(1f);
            Run(vehicle, 0.6f);
            vehicle.SetVerticalInput(0f);
            Run(vehicle, 2f);                         // 새 고도에서 안정될 시간

            Assert.AreNotEqual(TransportVehicle.HoverRootY, vehicle.TargetAltitude,
                "이 검증은 기본 고도가 아닌 곳에서 해야 의미가 있다.");
            Assert.IsTrue(vehicle.IsHovering,
                "기본 고도가 아니어도 부양 안정으로 봐야 한다. 고정 높이를 보면 견인이 영구히 막힌다.");
        }

        [Test]
        public void CeilingOnlyBlocksRise_WhileDriveAndDismountRemainAvailable()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            // The underside is above the vehicle platform but below the normal hover target.
            MakeSurface("Ceiling", new Vector3(0f, 1.25f, 0f), new Vector3(12f, 0.2f, 12f));
            var vehicle = MakeRiddenVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            vehicle.SetVerticalInput(1f);
            Run(vehicle, 0.8f);

            Assert.IsTrue(vehicle.VerticalBlocked, "천장에 막힌 동안 상승 제한 상태를 알려야 한다.");

            var beforeX = vehicle.transform.position.x;
            vehicle.SetVerticalInput(0f);
            vehicle.SetDriveVector(Vector3.right);
            Run(vehicle, 0.6f);
            Assert.Greater(vehicle.transform.position.x, beforeX + 0.1f,
                "천장 때문에 수평 조작까지 잠기면 안 된다.");

            vehicle.BeginDismount();
            Assert.IsTrue(vehicle.IsReturning, "막힘 상태에서도 하차 요청은 받아야 한다.");
            Run(vehicle, 2f);
            Assert.IsFalse(vehicle.IsOccupied, "하차 요청 후 탑승 상태가 남아 조작 불능이 되면 안 된다.");
        }
    }
}
