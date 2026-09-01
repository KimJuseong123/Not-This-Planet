using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>운반 장치의 탑승·하차 요청과 플레이어 탑승 상태 연결을 검증한다.</summary>
    public sealed class TractorBeamControllerTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        PlayerRig MakePlayerWithCamera(Vector3 foot = default)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                         Color.white, Color.gray, false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            player.Cam = camera;
            camera.Yaw = 0f;
            camera.Pitch = 0f;
            camera.SnapToTarget();
            spawned.Add(player.gameObject);
            spawned.Add(camera.gameObject);
            return player;
        }

        [Test]
        public void CameraRelativeMove_FollowsCurrentViewDuringFreeLook()
        {
            var player = MakePlayerWithCamera();
            var camera = player.Cam;

            Vector3 before = camera.PlanarForward;
            camera.SetFreeLook(true);
            camera.Steer(new Vector2(40f, 0f));

            Assert.Greater(Vector3.Angle(before, camera.PlanarForward), 45f,
                           "Alt 중에도 현재 카메라 yaw가 이동 기준을 바꿔야 한다.");
            Assert.Greater(Vector3.Dot(camera.CameraRelativeMove(Vector2.up).normalized,
                                       camera.PlanarForward), 0.99f);
            Assert.Greater(Vector3.Dot(camera.CameraRelativeMove(Vector2.right).normalized,
                                       camera.PlanarRight), 0.99f);
            Assert.Greater(Vector3.Dot(camera.CameraRelativeMove(Vector2.down).normalized,
                                       -camera.PlanarForward), 0.99f);
            Assert.Greater(Vector3.Dot(camera.CameraRelativeMove(Vector2.left).normalized,
                                       -camera.PlanarRight), 0.99f);

            camera.SetFreeLook(false);
            Assert.Less(Vector3.Angle(camera.AimDirection, camera.PlanarForward), 1f,
                        "Alt를 놓아도 조준 기준이 이전 방향으로 되돌아가면 안 된다.");
        }

        [Test]
        public void PlayerFacing_StaysOnCameraForwardDuringSideStep()
        {
            var player = MakePlayerWithCamera();
            var camera = player.Cam;
            camera.Yaw = 90f;
            camera.Pitch = 0f;
            camera.SnapToTarget();

            player.SetInput(new InputFrame { Move = new Vector2(-1f, 0f) });
            for (int i = 0; i < 100; i++) player.Tick();

            Assert.Greater(Vector3.Dot(player.FacingDirection.normalized,
                                       camera.PlanarForward), 0.98f,
                           "A 이동 중에도 캐릭터 정면은 이동 방향이 아니라 카메라 정면이어야 한다.");
            Assert.Less(player.MovementDirectionLocal.x, -0.45f,
                        "A 입력은 캐릭터 기준 좌우 보행 성분으로 전달되어야 한다.");
            Assert.Less(Mathf.Abs(player.MovementDirectionLocal.y), 0.45f);

            player.Teleport(Vector3.zero, Quaternion.identity);
            camera.Yaw = 90f;
            camera.Pitch = 0f;
            camera.SnapToTarget();
            player.SetInput(new InputFrame { Move = new Vector2(0f, -1f) });
            for (int i = 0; i < 100; i++) player.Tick();

            Assert.Greater(Vector3.Dot(player.FacingDirection.normalized,
                                       camera.PlanarForward), 0.98f,
                           "S 이동 중에도 캐릭터가 뒤돌지 않고 카메라 정면을 유지해야 한다.");
            Assert.Less(player.MovementDirectionLocal.y, -0.45f,
                        "S 입력은 뒷걸음 보행 성분으로 전달되어야 한다.");
        }

        [Test]
        public void TransportAction_BoardsAndStartsDismountWithoutClampState()
        {
            var beamObject = new GameObject("TractorBeam");
            beamObject.transform.SetPositionAndRotation(
                new Vector3(0f, TransportVehicle.GroundY, 3.2f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var beam = beamObject.AddComponent<TractorBeam>();
            beam.Configure(beamObject.transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);
            var vehicle = beamObject.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            vehicle.PlaceOnGround(beamObject.transform.position, 0f);
            spawned.Add(beamObject);

            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                         Color.white, Color.gray, false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            player.Cam = camera;
            camera.Yaw = 0f;
            camera.SnapToTarget();
            spawned.Add(player.gameObject);
            spawned.Add(camera.gameObject);

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, new[] { beam }, false);
            spawned.Add(controllerObject);

            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsTrue(controller.IsControlling);
            Assert.IsTrue(player.IsMounted);
            Assert.AreSame(vehicle, controller.MountedVehicle);
            Assert.AreSame(vehicle, player.MountedVehicle);
            Assert.IsTrue(camera.VehicleView);
            Assert.IsTrue(camera.FirstPerson, "탑승 시 조종석 1인칭으로 전환해야 한다 (S15P21B202-113).");

            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsTrue(controller.IsControlling, "하차 요청 직후에는 장치가 바닥으로 내려가는 동안 탑승 상태를 유지한다.");
            Assert.IsTrue(vehicle.IsReturning);
            Assert.IsTrue(player.IsMounted);
        }

        [Test]
        public void VehicleDrive_UsesCameraFacingSeparateFromSidewaysMove()
        {
            var beamObject = new GameObject("TractorBeam");
            beamObject.transform.SetPositionAndRotation(
                new Vector3(0f, TransportVehicle.GroundY, 3.2f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var beam = beamObject.AddComponent<TractorBeam>();
            beam.Configure(beamObject.transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);
            var vehicle = beamObject.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            vehicle.PlaceOnGround(beamObject.transform.position, 0f);
            spawned.Add(beamObject);

            var player = MakePlayerWithCamera();
            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, new[] { beam }, false);
            spawned.Add(controllerObject);

            controller.SetInput(new InputFrame { TransportAction = true });
            Assert.IsTrue(controller.IsControlling);

            controller.Tick(Time.fixedDeltaTime);
            Vector3 initialFacing = vehicle.ExitForward;

            var sideInput = Vector2.left;
            controller.SetInput(new InputFrame { Move = sideInput });
            controller.Tick(Time.fixedDeltaTime);
            Assert.Greater(Vector3.Dot(vehicle.HorizontalVelocity,
                                       player.Cam.CameraRelativeMove(sideInput)), 0.01f,
                           "A/D는 카메라 기준 선체 이동 벡터로 전달되어야 한다.");
            Assert.Less(Vector3.Angle(initialFacing, vehicle.ExitForward), 1f,
                        "A/D 이동은 운반선 정면을 회전시키지 않아야 한다.");

            player.Cam.Steer(new Vector2(30f, 0f));
            controller.SetInput(new InputFrame());
            controller.Tick(Time.fixedDeltaTime);
            Assert.Greater(Vector3.Dot(vehicle.ExitForward, player.Cam.PlanarForward), 0.99f,
                           "카메라 yaw가 바뀌면 정지 중에도 운반선 정면이 갱신되어야 한다.");
        }
    }
}
