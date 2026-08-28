using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운반 장치의 하차 판정과 하차 지점을 검증한다.
    ///
    /// 예전 구현은 "루트가 절대 높이 <see cref="TransportVehicle.GroundY"/> 에 도달"로
    /// 하차를 판정하고, 탑승자를 운반체 중심 좌표에 내려놓았다. 그래서 구조물 위에서는
    /// 하차가 완료되지 않았고, 평지에서도 플랫폼 콜라이더와 겹쳐 탑승자가 튕겨 나갔다.
    /// 여기서 검증하는 것은 그 두 가지가 다시 절대 높이·중심 좌표로 돌아가지 않는다는 것이다.
    ///
    /// EditMode 에서는 FixedUpdate 가 불리지 않으므로, 하강을 보는 테스트는
    /// <see cref="TransportVehicle.Tick"/> 를 직접 부르고 <c>Physics.Simulate</c> 로 물리를
    /// 한 스텝씩 돌린다. 나머지 테스트는 판정에 쓰는 질의(접지·하차 지점)만 확인한다.
    /// </summary>
    public sealed class TransportDismountTests
    {
        const float PlatformHalfDepth = 1.1f;   // TransportVehicle 의 플랫폼 크기 2.4 x 0.34 x 2.2

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

        /// <summary>넓은 바닥판. 지면과 절벽 상단을 대신한다.</summary>
        GameObject MakeSurface(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = 0 };   // Default — SurfaceMask 에 포함된다
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            spawned.Add(go);
            return go;
        }

        TransportVehicle MakeVehicle(Vector3 position, float visualYaw = 0f)
        {
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            // 요를 들고 있는 Visual 자식. 실제 프리팹과 같은 이름이어야 Initialize 가 찾는다.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.rotation = Quaternion.Euler(0f, visualYaw, 0f);

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            spawned.Add(go);
            return vehicle;
        }

        [Test]
        public void ExitFoot_LandsOutsidePlatformFootprint()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));

            Assert.IsTrue(vehicle.TryFindExitFoot(out var foot), "평지에서는 하차 지점을 찾아야 한다.");

            var flat = new Vector2(foot.x - vehicle.transform.position.x, foot.z - vehicle.transform.position.z);
            Assert.Greater(flat.magnitude, PlatformHalfDepth,
                "하차 지점이 플랫폼 콜라이더 밖이어야 한다. 중심에 내려놓으면 탑승자가 튕겨 나간다.");
            Assert.AreEqual(0f, foot.y, 0.05f, "평지에서는 지면 높이에 서야 한다.");
        }

        [Test]
        public void ExitFoot_FollowsStructureTopInsteadOfAbsoluteGroundHeight()
        {
            // 지면과, 그보다 2.5m 높은 구조물 상단.
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            MakeSurface("Structure", new Vector3(0f, 1.25f, 0f), new Vector3(12f, 2.5f, 12f));

            var vehicle = MakeVehicle(new Vector3(0f, 2.5f + TransportVehicle.GroundY, 0f));

            Assert.IsTrue(vehicle.TryFindExitFoot(out var foot), "구조물 위에서도 하차 지점을 찾아야 한다.");
            Assert.AreEqual(2.5f, foot.y, 0.05f,
                "구조물 상단에 서야 한다. 절대 높이(GroundY)로 돌아가면 이 값이 0 근처가 된다.");
        }

        [Test]
        public void TouchingSurface_TrueOnStructureTop_FalseWhileHovering()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            MakeSurface("Structure", new Vector3(0f, 1.25f, 0f), new Vector3(12f, 2.5f, 12f));

            var landed = MakeVehicle(new Vector3(0f, 2.5f + TransportVehicle.GroundY, 0f));
            Assert.IsTrue(landed.IsTouchingSurface,
                "구조물 상단에 내려앉으면 접지로 판정해야 한다. 여기서 실패하면 구조물 위에서 하차가 안 된다.");

            var hovering = MakeVehicle(new Vector3(20f, TransportVehicle.HoverRootY, 20f));
            Assert.IsFalse(hovering.IsTouchingSurface, "부유 높이에서는 접지가 아니다.");
        }

        /// <summary>
        /// 이 버그의 회귀 테스트. 구조물 위에서 하차를 걸고 물리를 돌려 실제로 완료되는지 본다.
        ///
        /// 예전 구현은 루트가 절대 높이 0.06m 에 닿아야 완료됐으므로, 2.5m 구조물 위에서는
        /// 아무리 돌려도 <c>IsReturning</c> 이 풀리지 않는다.
        ///
        /// <c>LogAssert.ignoreFailingMessages</c> 를 켜는 이유는 이 테스트와 무관하다 —
        /// <c>PlayerRig.Create</c> 경로의 <c>Prim.Decor</c> 가 edit mode 에서 <c>Object.Destroy</c> 를
        /// 불러 에러 로그를 남긴다(develop 에 이미 있는 문제, 이 MR 범위 아님).
        /// </summary>
        [Test]
        public void Dismount_CompletesOnStructureTop()
        {
            LogAssert.ignoreFailingMessages = true;

            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            MakeSurface("Structure", new Vector3(0f, 1.25f, 0f), new Vector3(12f, 2.5f, 12f));

            var vehicle = MakeVehicle(new Vector3(0f, 4.2f, 0f));

            var player = PlayerRig.Create("테스트", new Vector3(0f, 2.5f, 1.5f), Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);

            Assert.IsTrue(vehicle.TryBoard(player), "탑승이 되어야 이어서 하차를 볼 수 있다.");
            vehicle.BeginDismount();
            Assert.IsTrue(vehicle.IsReturning);

            Physics.simulationMode = SimulationMode.Script;
            const float step = 0.02f;
            for (var i = 0; i < 250 && vehicle.IsReturning; i++)   // 최대 5초
            {
                vehicle.Tick(step);
                Physics.Simulate(step);
            }

            Assert.IsFalse(vehicle.IsReturning,
                "구조물 위에서도 하차가 완료되어야 한다. 절대 높이로 판정하면 여기서 영원히 참이다.");
            Assert.IsFalse(vehicle.IsOccupied, "하차가 끝나면 탑승자가 없어야 한다.");
            Assert.IsNull(player.MountedVehicle, "플레이어 쪽 탑승 상태도 풀려야 한다.");

            Assert.Greater(vehicle.transform.position.y, 2.0f,
                "운반체가 구조물 위에 남아야 한다. 지면까지 파고들면 이 값이 0 근처가 된다.");
        }

        [Test]
        public void ExitFoot_PicksAnotherDirectionWhenFrontIsBlocked()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f));
            var forward = vehicle.ExitForward;

            // 정면 바로 앞을 벽으로 막는다. 사람이 설 공간이 없어야 한다.
            MakeSurface("Wall", vehicle.transform.position + forward * 1.8f + Vector3.up * 1.5f,
                        new Vector3(4f, 3f, 1.2f));

            Assert.IsTrue(vehicle.TryFindExitFoot(out var foot), "정면이 막혀도 다른 방향으로 내려야 한다.");

            var toFoot = foot - vehicle.transform.position;
            toFoot.y = 0f;
            Assert.Less(Vector3.Dot(toFoot.normalized, forward), 0.7f,
                "정면이 막혔으면 정면이 아닌 방향을 골라야 한다.");
        }
    }
}
