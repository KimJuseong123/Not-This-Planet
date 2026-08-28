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
            Assert.Greater(camera.ViewPitch, 70f, "탑승 시 카메라는 캐릭터 위에서 내려다봐야 한다.");

            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsTrue(controller.IsControlling, "하차 요청 직후에는 장치가 바닥으로 내려가는 동안 탑승 상태를 유지한다.");
            Assert.IsTrue(vehicle.IsReturning);
            Assert.IsTrue(player.IsMounted);
        }
    }
}
