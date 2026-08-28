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
