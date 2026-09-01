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

        [Test]
        public void PlaceOnGround_AlignsVisualBoundsAboveTheGround()
        {
            var vehicle = MakeVehicleWithHull(new Vector3(4f, TransportVehicle.GroundY, -3f),
                                               out var hull);

            vehicle.PlaceOnGround(vehicle.transform.position, 0f);

            Assert.GreaterOrEqual(hull.bounds.min.y, TransportVehicle.GroundY + 0.019f,
                "운반 장치 외형의 실제 최저 Y가 바닥 아래로 내려가면 안 된다.");
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

        /// <summary>
        /// 하차 직후 탑승자가 제자리에 머무는지 본다.
        ///
        /// 하차하면 캐릭터가 한 지점으로 빨려가듯 날아가는 증상이 있었다. 내려놓은 자리에서
        /// 물리를 1초 돌려 수평으로 얼마나 밀려나는지 재는 것이 이 버그의 회귀 테스트다.
        /// </summary>
        [Test]
        public void Exit_RiderStaysWhereItWasPlaced()
        {
            LogAssert.ignoreFailingMessages = true;

            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));

            // 원점에서 멀리 둔다. 월드 원점으로 끌려가는 것이 의심 원인이라 원점 근처면
            // 끌려가도 이동 거리가 작아 놓칠 수 있다.
            var spot = new Vector3(18f, TransportVehicle.GroundY, -14f);
            var vehicle = MakeVehicle(spot);

            var player = PlayerRig.Create("테스트", spot + new Vector3(0f, 0f, 1.5f), Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);

            Assert.IsTrue(vehicle.TryBoard(player), "탑승이 되어야 하차를 볼 수 있다.");
            vehicle.ForceReset();

            var placed = player.Doll.Hips.position;

            Physics.simulationMode = SimulationMode.Script;
            const float step = 0.02f;
            for (var i = 0; i < 50; i++) Physics.Simulate(step);   // 1초

            var after = player.Doll.Hips.position;
            var drift = new Vector2(after.x - placed.x, after.z - placed.z).magnitude;

            Assert.Less(drift, 1.0f,
                $"하차 뒤 1초 동안 수평으로 {drift:0.00}m 밀렸다. 내려놓은 자리 {placed} → {after}. "
                + "조인트가 남아 골반을 끌어당기면 이 값이 커진다.");
        }

        /// <summary>
        /// 사방이 막혀 하차 지점 탐색이 실패해도 탑승자를 기체 껍데기 안에 두지 않는다.
        ///
        /// 예전 폴백은 플랫폼 콜라이더 위, 즉 기체 중심이었다. 껍데기가 없던 시절에는
        /// 맞았지만 S15P21B202-84 스킨이 실제 기체 모델을 붙인 뒤로 그 자리는 껍데기 안이다.
        /// 카메라가 탑승자를 따라 함께 갇혀 화면에 접시 테두리만 얇은 곡선으로 남고
        /// 나머지는 눈앞의 제 몸으로 가득 찼다 — 하차 후 흰 화면 증상.
        ///
        /// 여기서는 좁은 돌기둥 꼭대기에 착륙시킨다. 사방 어디에도 밟을 표면이 없으므로
        /// 탐색 2단계까지 실패하고 마지막 수단을 탄다. 그래도 골반이 껍데기 밖이어야 한다.
        /// </summary>
        [Test]
        public void Exit_NeverPlacesRiderInsideCraftHull()
        {
            LogAssert.ignoreFailingMessages = true;

            // 기체만 겨우 올라서는 기둥. 주변에는 아무 표면도 없다.
            MakeSurface("Pillar", new Vector3(0f, -2f, 0f), new Vector3(2f, 4f, 2f));

            var spot = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var vehicle = MakeVehicleWithHull(spot, out var hull);

            var player = PlayerRig.Create("테스트", spot + new Vector3(0f, 0f, 0.2f), Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);

            Assert.IsTrue(vehicle.TryBoard(player), "탑승이 되어야 하차를 볼 수 있다.");
            Assert.IsFalse(vehicle.TryFindExitFoot(out _),
                "이 검증은 탐색이 실패하는 상황에서만 의미가 있다. 성공하면 벽을 더 촘촘히 둘러야 한다.");

            vehicle.ForceReset();

            var hips = player.Doll.Hips.position;
            var bounds = hull.bounds;

            var inside = hips.x > bounds.min.x && hips.x < bounds.max.x
                      && hips.z > bounds.min.z && hips.z < bounds.max.z
                      && hips.y > bounds.min.y && hips.y < bounds.max.y;

            Assert.IsFalse(inside,
                $"골반 {hips} 이 기체 껍데기 {bounds} 안이다. 카메라가 함께 갇혀 화면이 흰색으로 덮인다.");
        }

        /// <summary>렌더러를 가진 껍데기가 붙은 운반체. 껍데기 경계를 재려면 필요하다.</summary>
        TransportVehicle MakeVehicleWithHull(Vector3 position, out Renderer hull)
        {
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            // 실제 TractorCraft 는 1.90 x 2.45 x 1.85m 이고 84 스킨이 경계 중심을 루트에 맞춘다.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, true);
            visual.transform.position = position;
            visual.transform.localScale = new Vector3(1.90f, 2.45f, 1.85f);
            hull = visual.GetComponent<Renderer>();

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            spawned.Add(go);
            return vehicle;
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
