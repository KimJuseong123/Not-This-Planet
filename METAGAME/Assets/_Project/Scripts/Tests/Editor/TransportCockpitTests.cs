using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 탑승 중 시점과 탑승자 숨김을 검증한다.
    ///
    /// 처음에는 장치 위 10m 에서 내려다보는 <b>탑다운</b>이었다 — 탑승자가 기체 꼭대기에
    /// 걸터앉은 것처럼 보여 무엇에 타고 있는지가 전달되지 않았다. 그래서 조종석
    /// 1인칭으로 바꿨고(S15P21B202-113), 다시 <b>뒤에서 따라가는 3인칭</b>으로 바꿨다
    /// (S15P21B202-441) — 다른 게임이 차·탈것에서 쓰는 방식이다.
    ///
    /// <b>탑다운으로 되돌아가지 않는 것이 이 파일의 요지다.</b> 여기서 보는 것은 —
    /// 3인칭으로 물러나는지, 초점이 기체에 머무는지, 껍데기가 계속 보이는지,
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

        /// <summary>
        /// 둘러봐도 <b>초점</b>이 조종석에 머무른다 (S15P21B202-441).
        ///
        /// <para>예전에는 카메라가 조종석 자리에 <b>붙어</b> 있었고(1인칭) 이 검증이 그것을
        /// 잠갔다. 3인칭으로 바뀐 뒤에는 그 자리를 <b>중심으로 돈다</b> — 그래서 눈
        /// 위치가 아니라 "무엇을 중심으로 도는가" 를 잠근다. 초점이 흔들리면 기체가
        /// 화면에서 미끄러진다.</para>
        /// </summary>
        [Test]
        public void LookAround_KeepsFocusOnTheCockpit()
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

                // 거리는 0 에서 서서히 벌어진다(탑승 순간 튀지 않게). 수렴할 틈을 준다.
                for (var i = 0; i < 60; i++) camera.Tick();

                // <b>사적 필드를 보지 않는다.</b> "초점을 중심으로 돈다" 는 두 가지로
                // 드러난다 — 물러나 있고, 그 초점이 화면 정중앙에 있다.
                var anchor = vehicle.CockpitEye.position
                             + Vector3.up * PhysicsScale.VehicleCamTargetHeight;
                var toAnchor = anchor - camera.transform.position;

                Assert.Greater(toAnchor.magnitude, 0.5f,
                    $"yaw {yaw}도에서 물러나지 않았다 — 3인칭이 아니다");
                Assert.Greater(Vector3.Dot(toAnchor.normalized, camera.transform.forward), 0.99f,
                    $"yaw {yaw}도에서 초점이 화면 중심을 벗어났다");
            }
        }

        /// <summary>
        /// 탑승하면 <b>3인칭</b>으로 물러나고, 하차하면 원래 설정으로 돌아온다
        /// (S15P21B202-441).
        ///
        /// <para><b>S15P21B202-113 의 결정을 뒤집은 자리다.</b> 그때는 조종석 1인칭이었고
        /// 이 검증이 그것을 잠갔다. 다른 게임이 차·탈것에서 쓰는 방식으로 바꾼다 —
        /// 뒤에서 따라가면 무엇을 타고 있는지가 화면에 남는다.</para>
        ///
        /// <para>되돌리는 쪽은 그대로 잠근다. 탑승 전 시점 설정으로 정확히 돌아오지
        /// 않으면 걷기 화면이 망가진다.</para>
        /// </summary>
        [Test]
        public void Board_SwitchesToThirdPerson_AndExitRestores()
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
            Assert.IsFalse(camera.FirstPerson, "탑승 시 3인칭으로 물러나야 한다.");
            Assert.AreEqual(PhysicsScale.VehicleCamDistance, camera.Distance, 0.01f,
                "기체 크기에 맞춘 거리를 써야 한다.");

            vehicle.ForceReset();

            Assert.IsFalse(camera.VehicleView, "하차하면 탑승 시점에서 나와야 한다.");
            Assert.IsFalse(camera.FirstPerson, "하차하면 원래 시점 설정으로 돌아와야 한다.");
            Assert.AreEqual(Tuning.CamDistance, camera.Distance, 0.01f,
                "하차하면 걷기 거리로 돌아와야 한다.");
        }

        [Test]
        public void Board_HidesRider_AndExitShowsAgain()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            Assert.IsTrue(player.Doll.Visible, "시작할 때는 보여야 한다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.IsFalse(player.Doll.Visible, "탑승 중에는 감춰야 한다 — 기체 밖으로 튀어나온 몸이 보이면 안 된다.");
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
        /// 탑승해도 기체 껍데기는 <b>계속 보인다</b> (S15P21B202-441).
        ///
        /// <para><b>이 검증도 뒤집혔다.</b> 조종석 1인칭이던 동안에는 껍데기를 껐다 —
        /// 카메라가 메시 안에 있어 접시형 테두리가 화면 위·아래에 얇은 곡선 조각으로
        /// 남았고, 그 자리를 채울 내부 벽이 임시 대체물에는 없었다.</para>
        ///
        /// <para>3인칭에서는 반대가 된다. 껍데기를 끄면 <b>기체가 안 보이고 조종대만
        /// 떠 있는 화면</b>이 된다 — 무엇을 타고 있는지가 사라지므로, 걷어낸 탑다운과
        /// 같은 실패다.</para>
        /// </summary>
        [Test]
        public void Board_KeepsCraftHullVisible()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 0f), true);
            var player = MakePlayer(new Vector3(0f, TransportVehicle.GroundY, 1.5f));

            var hull = FindHullRenderer(vehicle);
            Assert.IsNotNull(hull, "이 검증은 기체 껍데기가 있어야 의미가 있다.");
            Assert.IsTrue(hull.enabled, "시작할 때는 껍데기가 보여야 한다.");

            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.IsTrue(hull.enabled,
                "탑승 중에도 껍데기가 보여야 한다 — 3인칭에서 끄면 기체가 사라진다.");

            vehicle.ForceReset();

            Assert.IsTrue(hull.enabled, "하차해도 껍데기는 그대로 보인다.");
        }

        /// <summary>
        /// 시점을 돌려도 <b>보이는 기체가 빔 위에 머문다</b> (S15P21B202-441).
        ///
        /// <para><b>실측으로 드러난 어긋남이다.</b> 3인칭으로 바꾼 뒤 "운반선과 견인빔이
        /// 어긋난다 · 고개를 돌리면 기체가 같이 움직인다" 가 나왔다.</para>
        ///
        /// <para>원인은 <c>SetFacingDirection</c> 이 <c>visual.rotation</c> 만 바꾼 것이었다.
        /// 회전이 Visual 자신의 피벗을 중심으로 일어나는데, 스킨은 경계 중심을 루트에
        /// 맞춰 붙이므로(S15P21B202-84) 피벗이 루트와 어긋나 있다. 1인칭이던 동안에는
        /// 껍데기를 껐고 카메라가 기체 안이라 보이지 않았다.</para>
        /// </summary>
        [Test]
        public void TurningTheView_KeepsTheHullOverTheBeam()
        {
            var vehicle = MakeVehicle(new Vector3(0f, 5f, 0f), true);

            // 피벗을 일부러 어긋나게 둔다 — 스킨이 경계 중심을 루트에 맞추면 이렇게 된다.
            var visual = vehicle.transform.Find("Visual");
            Assert.IsNotNull(visual, "이 검증은 Visual 이 있어야 의미가 있다.");
            visual.position = vehicle.transform.position + new Vector3(0.8f, 0f, 0.4f);

            var player = MakePlayer(new Vector3(0f, 5f, 1.5f));
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            spawned.Add(camera.gameObject);
            Assert.IsTrue(vehicle.TryBoard(player));

            // 처음 기준을 잡게 한 번 돌려 둔다.
            vehicle.SetFacingDirection(Vector3.forward);
            var first = HullCenterOffset(vehicle);

            foreach (var dir in new[] { Vector3.right, Vector3.back, Vector3.left, Vector3.forward })
            {
                vehicle.SetFacingDirection(dir);
                var now = HullCenterOffset(vehicle);

                Assert.Less((now - first).magnitude, 0.05f,
                    $"{dir} 을 볼 때 기체가 빔에서 {(now - first).magnitude:0.00}m 벗어났다");
            }
        }

        /// <summary>
        /// 빔(루트)에서 본 껍데기 중심까지의 수평 거리. 돌려도 이 값이 변하지 않아야 한다.
        ///
        /// <para>회전한 메시의 월드 AABB 는 모양 자체가 달라지므로 중심을 그대로 비교하면
        /// 안 된다. <b>루트에서의 거리</b>로만 본다 — 리그하게 돌면 거리는 보존된다.</para>
        /// </summary>
        static Vector3 HullCenterOffset(TransportVehicle vehicle)
        {
            var visual = vehicle.transform.Find("Visual");
            var offset = visual.position - vehicle.transform.position;
            return new Vector3(offset.magnitude, 0f, 0f);
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
