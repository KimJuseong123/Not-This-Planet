using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 탑승 중 조종석 시야와 탑승자 숨김을 검증한다.
    ///
    /// 예전에는 탑승하면 장치 위 10m 에서 내려다보는 3인칭이었고 탑승자가 기체 꼭대기에
    /// 걸터앉은 것처럼 보였다. 여기서 보는 것은 세 가지다 — 1인칭으로 바뀌는지,
    /// 탑승자가 감춰지고 하차하면 다시 보이는지, 좌석이 기체 안에 들어가는지.
    /// </summary>
    public sealed class TransportCockpitTests
    {
        readonly List<GameObject> spawned = new();
        bool savedIgnoreFailingMessages;

        [SetUp]
        public void SetUp()
        {
            savedIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
        }

        [TearDown]
        public void TearDown()
        {
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

        /// <summary>
        /// 기체 모델을 흉내낸 Visual. 실제 TractorCraft 는 1.90 x 2.45 x 1.85m 이고
        /// S15P21B202-84 스킨이 경계 중심을 운반체 루트에 맞춰 붙인다.
        /// </summary>
        Transform MakeVisual(Transform parent, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Visual";
            Object.DestroyImmediate(go.GetComponent<Collider>());   // 기체 콜라이더는 플랫폼이 담당한다
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.transform.localScale = size;
            return go.transform;
        }

        TransportVehicle MakeVehicle(Vector3 position, bool withVisual)
        {
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            if (withVisual) MakeVisual(go.transform, position, new Vector3(1.90f, 2.45f, 1.85f));

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            spawned.Add(go);
            return vehicle;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            // Prim.Decor 가 만드는 로그는 이 테스트와 무관하다.
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        [Test]
        public void LookAround_KeepsEyeAtCockpitOrigin()
        {
            var vehicle = MakeVehicle(new Vector3(0f, 5f, 0f), true);
            var player = MakePlayer(new Vector3(0f, 5f, 1.5f));
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            spawned.Add(camera.gameObject);
            Assert.IsTrue(vehicle.TryBoard(player));

            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                camera.Yaw = yaw;
                camera.Pitch = 35f;
                vehicle.SetFacingDirection(camera.PlanarForward);
                camera.Tick();
                Assert.Less(Vector3.Distance(camera.transform.position, vehicle.CockpitEye.position), 0.001f);
                Assert.Less(vehicle.CockpitEye.InverseTransformPoint(camera.transform.position).magnitude, 0.001f);
            }
        }

        [Test]
        public void Board_SwitchesToFirstPerson_AndExitRestores()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            player.Cam = camera;
            spawned.Add(camera.gameObject);

            Assert.IsFalse(camera.FirstPerson, "이 검증은 3인칭에서 시작해야 의미가 있다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.IsTrue(camera.VehicleView, "탑승 시 탑승 시점으로 들어가야 한다.");
            Assert.IsTrue(camera.FirstPerson, "탑승 시 조종석 1인칭이어야 한다.");

            vehicle.ForceReset();

            Assert.IsFalse(camera.VehicleView, "하차하면 탑승 시점에서 나와야 한다.");
            Assert.IsFalse(camera.FirstPerson, "하차하면 원래 시점 설정으로 돌아와야 한다.");
        }

        [Test]
        public void Board_HidesRider_AndExitShowsAgain()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            Assert.IsTrue(player.Doll.Visible, "시작할 때는 보여야 한다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.IsFalse(player.Doll.Visible, "탑승 중에는 감춰야 한다 — 1인칭 시야를 제 몸이 가린다.");
            Assert.IsFalse(AnyRendererEnabled(player), "렌더러가 실제로 꺼져 있어야 한다.");

            vehicle.ForceReset();

            Assert.IsTrue(player.Doll.Visible, "하차하면 다시 보여야 한다.");
            Assert.IsTrue(AnyRendererEnabled(player), "렌더러가 실제로 켜져 있어야 한다.");
        }

        /// <summary>
        /// 이 버그의 회귀 테스트.
        ///
        /// Doll.Teleport 의 첫 인수는 발이 아니라 골반 위치다. 하차 경로가 발 높이를 그대로
        /// 넘겨서 골반이 지면 4cm 에 놓였고, 다리와 발이 지면 26cm 아래에 파묻혀
        /// PhysX 가 밀어내며 탑승자가 천장까지 날아갔다.
        /// </summary>
        [Test]
        public void Exit_PlacesHipsAboveGround_NotBuriedInIt()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            Assert.IsTrue(vehicle.TryBoard(player));
            vehicle.ForceReset();

            // 발이 놓일 표면 높이. 지면 판의 윗면이 y=0 이다.
            var hips = player.Doll.Hips.position.y;

            Assert.Greater(hips, RagdollBuilder.HipsY * 0.8f,
                "골반이 골반 높이(0.3m) 근처여야 한다. 발 높이를 그대로 넘기면 0.04m 가 되어 "
                + "다리가 지면에 파묻히고 탑승자가 튕겨 나간다.");

            var lowest = float.MaxValue;
            foreach (var body in player.Doll.Bodies)
                if (body != null) lowest = Mathf.Min(lowest, body.position.y);

            Assert.Greater(lowest, -0.15f, "어느 부위도 지면 아래로 크게 파묻히지 않아야 한다.");
        }

        /// <summary>
        /// 탑승하면 기체 껍데기를 감춘다.
        ///
        /// 카메라가 기체 메시 안에 있으면 접시형 기체의 테두리가 화면 위·아래에 얇은 곡선
        /// 조각으로 남는다. 껍데기를 끄지 않으면 그 조각이 계속 보인다.
        /// </summary>
        [Test]
        public void Board_HidesCraftHull_AndExitShowsAgain()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            var hull = FindHullRenderer(vehicle);
            Assert.IsNotNull(hull, "이 검증은 기체 껍데기가 있어야 의미가 있다.");
            Assert.IsTrue(hull.enabled, "시작할 때는 껍데기가 보여야 한다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.IsFalse(hull.enabled, "탑승 중에는 껍데기를 감춰야 한다.");

            vehicle.ForceReset();

            Assert.IsTrue(hull.enabled, "하차하면 껍데기가 다시 보여야 한다.");
        }

        /// <summary>
        /// 하차하면 카메라가 기체 반대편의 빈 공간에 서야 한다.
        ///
        /// 예전에는 탑승자를 기체 바깥을 보게 돌렸다. 3인칭 카메라는 캐릭터 뒤에 서므로
        /// 등 뒤에 기체가 왔고, 하차 지점 1.8m 에서 껍데기 반지름 0.95m 를 빼면 카메라가
        /// 물러날 수 있는 거리는 0.85m 뿐이다. 카메라 거리 3.6m 가 최소 1m 까지 밀려들어와
        /// 화면에 제 등과 꼬리만 남았다.
        ///
        /// 여기서 보는 것은 두 가지다 — 탑승자가 기체를 마주 보는지, 카메라가 기체
        /// 반대편에 서는지. 실제로 밀려들어오는 거리는 시간에 따라 수렴하므로
        /// EditMode 에서는 측정할 수 없다. 방향만 고정한다.
        /// </summary>
        [Test]
        public void Exit_CameraStandsOnFarSideFromCraft()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var spot = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var vehicle = MakeVehicle(spot, true);
            var player = MakePlayer(spot + new Vector3(0f, 0f, 1.5f));

            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            player.Cam = camera;
            spawned.Add(camera.gameObject);

            Assert.IsTrue(vehicle.TryBoard(player));
            vehicle.ForceReset();

            var hips = player.Doll.Hips.position;

            var toCraft = vehicle.transform.position - hips;
            toCraft.y = 0f;
            Assert.Greater(toCraft.magnitude, 0.5f,
                "이 검증은 탑승자가 기체 밖에 내려야 의미가 있다.");
            toCraft.Normalize();

            var facing = player.Doll.Hips.transform.forward;
            facing.y = 0f;
            Assert.Greater(Vector3.Dot(facing.normalized, toCraft), 0.7f,
                "하차하면 기체를 마주 봐야 한다. 등을 지면 카메라가 기체에 막힌다.");

            var toCamera = camera.transform.position - hips;
            toCamera.y = 0f;
            Assert.Greater(toCamera.magnitude, 0.01f, "카메라가 골반과 같은 자리면 안 된다.");

            Assert.Less(Vector3.Dot(toCamera.normalized, toCraft), -0.7f,
                "카메라가 기체 반대편에 서야 한다. 기체 쪽에 서면 시야가 막혀 "
                + "제 등과 꼬리만 보인다.");
        }

        /// <summary>
        /// 1인칭에서 하차하면 기체 바깥을 보고, 카메라 요가 인형의 방향과 맞아야 한다.
        ///
        /// 씬의 SpaceBootstrap 은 firstPerson = 1 이므로 실제 게임이 타는 경로다.
        ///
        /// 예전에는 인형만 돌리고 카메라 요는 조종석에서 보던 값을 그대로 뒀다. 1인칭
        /// 카메라는 머리에서 시선 방향으로 20cm 내민 자리에 서므로, 두 값이 어긋나면
        /// 카메라가 머리 뒤로 밀려 제 몸과 꼬리가 화면에 들어온다.
        /// </summary>
        [Test]
        public void Exit_FirstPersonLooksAwayFromCraft()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var spot = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var vehicle = MakeVehicle(spot, true);
            var player = MakePlayer(spot + new Vector3(0f, 0f, 1.5f));

            // 실제 게임과 같은 1인칭으로 만든다.
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            spawned.Add(camera.gameObject);

            Assert.IsTrue(camera.FirstPerson, "이 검증은 1인칭에서 해야 의미가 있다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            vehicle.ForceReset();

            Assert.IsTrue(camera.FirstPerson, "1인칭으로 시작했으면 하차해도 1인칭이어야 한다.");

            var hips = player.Doll.Hips.position;

            var fromCraft = hips - vehicle.transform.position;
            fromCraft.y = 0f;
            Assert.Greater(fromCraft.magnitude, 0.5f, "이 검증은 탑승자가 기체 밖에 내려야 의미가 있다.");
            fromCraft.Normalize();

            var facing = player.Doll.Hips.transform.forward;
            facing.y = 0f;
            Assert.Greater(Vector3.Dot(facing.normalized, fromCraft), 0.7f,
                "1인칭에서는 기체 바깥을 봐야 한다. 마주 보면 1.8m 앞의 기체가 화면을 덮는다.");

            var view = camera.transform.forward;
            view.y = 0f;
            Assert.Greater(Vector3.Dot(view.normalized, facing.normalized), 0.9f,
                "카메라 요가 인형의 방향과 맞아야 한다. 어긋나면 카메라가 머리 뒤로 밀려 "
                + "제 몸과 꼬리가 화면에 들어온다.");

            // 머리에서 시선 방향으로 내민 자리이므로 기체에서 더 멀어져야 한다.
            var camFlat = new Vector2(camera.transform.position.x - vehicle.transform.position.x,
                                      camera.transform.position.z - vehicle.transform.position.z).magnitude;
            var hipsFlat = new Vector2(hips.x - vehicle.transform.position.x,
                                       hips.z - vehicle.transform.position.z).magnitude;
            Assert.Greater(camFlat, hipsFlat,
                "카메라가 머리 앞으로 나가야 한다. 뒤로 밀리면 이 값이 작아진다.");
        }

        /// <summary>Visual 자식의 껍데기 렌더러. 조종석은 Visual 아래에 있으므로 제외한다.</summary>
        static Renderer FindHullRenderer(TransportVehicle vehicle)
        {
            foreach (var t in vehicle.GetComponentsInChildren<Transform>(true))
            {
                if (t == vehicle.transform || t.name != "Visual") continue;

                var renderer = t.GetComponent<Renderer>();
                if (renderer != null) return renderer;
            }

            return null;
        }

        [Test]
        public void Seat_SitsInsideCraftBounds()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var position = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var vehicle = MakeVehicle(position, true);

            // 기체 경계 — MakeVehicle 이 붙인 Visual 과 같은 값이다.
            var top = position.y + 2.45f * 0.5f;
            var bottom = position.y - 2.45f * 0.5f;

            var foot = vehicle.SeatFootPosition;
            var head = foot.y + RagdollBuilder.TotalHeight;

            Assert.Less(head, top,
                "머리가 기체 위로 튀어나오면 꼭대기에 걸터앉은 것처럼 보인다.");
            Assert.GreaterOrEqual(foot.y, bottom,
                "발이 기체 아래로 빠지면 안 된다.");
        }

        [Test]
        public void Seat_FallsBackToConstantWithoutVisual()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var position = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var vehicle = MakeVehicle(position, false);   // Visual 없음

            Assert.Greater(vehicle.SeatFootPosition.y, position.y,
                "기체 모델이 없으면 예전 좌석 높이를 그대로 써야 한다 — 좌석이 사라지면 안 된다.");
        }

        static bool AnyRendererEnabled(PlayerRig player)
        {
            var renderers = player.Doll.Root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
                if (renderers[i].enabled) return true;

            return false;
        }
    }
}
