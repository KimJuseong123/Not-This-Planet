using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운반 장치의 탑승·하차 요청과 플레이어 탑승 상태 연결을 검증한다.
    ///
    /// <para><b>오브젝트를 만들거나 옮긴 뒤에는 <see cref="UnityEngine.Physics.SyncTransforms"/>
    /// 를 부른다.</b> 이 프로젝트는 <c>autoSyncTransforms</c> 가 꺼져 있어, Transform 을
    /// 바꿔도 <b>다음 물리 스텝 전까지 PhysX 는 옛 자리를 들고 있다.</b> EditMode 에는 그
    /// 스텝이 없으므로 직접 부르지 않으면 질의가 원점의 유령을 본다 — 그러면 후보가
    /// 0개가 되어 "상호작용 대상이 None" 같은, 원인과 전혀 닮지 않은 실패가 난다
    /// (S15P21B202-463). 같은 함정과 처방이 <c>InteractorTests.cs:362-368</c> 에 있다.</para>
    /// </summary>
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
            Physics.SyncTransforms();
            return player;
        }

        TransportVehicle MakeVehicle(Vector3 position)
        {
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            var beam = go.AddComponent<TractorBeam>();
            beam.Configure(go.transform, PhysicsScale.BeamRange, TransportVehicle.BeamHoldDistance);
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            vehicle.PlaceOnGround(position, 0f);
            spawned.Add(go);
            Physics.SyncTransforms();
            return vehicle;
        }

        Grabbable MakeGrabbable(Vector3 position, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.25f;
            var grabbable = Grabbable.Mark(go, name);
            grabbable.CanGrab = true;
            spawned.Add(go);
            Physics.SyncTransforms();
            return grabbable;
        }

        [Test]
        public void InteractionFocus_SelectsOnlyTheNearestCameraCenterCandidate()
        {
            var player = MakePlayerWithCamera();
            var center = player.Cam.transform.position + player.Cam.transform.forward * Tuning.ArmReach;
            var centered = MakeGrabbable(center, "Centered");
            var offCenter = MakeGrabbable(center + player.Cam.transform.right * 0.65f, "OffCenter");

            player.RefreshInteractionFocus();

            Assert.AreEqual(PlayerInteractionKind.Grabbable,
                player.CurrentInteractionTarget.Kind);
            Assert.AreSame(centered, player.CurrentInteractionTarget.Grabbable,
                "화면 중심에 더 가까운 하나만 현재 상호작용 대상이어야 한다.");

            player.SetInput(new InputFrame { GrabLeft = true });
            player.Tick();

            Assert.AreSame(centered, player.LeftHand.Held,
                "선택된 물체만 클릭으로 잡혀야 한다.");
            Assert.AreNotSame(offCenter, player.LeftHand.Held,
                "선택되지 않은 물체는 같은 클릭에 반응하면 안 된다.");
        }

        [Test]
        public void VehicleOutsideBoardRadius_HasNoFocusAndCannotBoard()
        {
            var player = MakePlayerWithCamera();
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY,
                                                   TransportVehicle.BoardRadius + 2f));

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, new[] { vehicle.Beam }, false);
            spawned.Add(controllerObject);

            Assert.AreEqual(PlayerInteractionKind.None,
                player.CurrentInteractionTarget.Kind,
                "탑승 반경 밖 운반 장치는 HUD/입력 대상이 아니어야 한다.");

            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsFalse(controller.IsControlling);
            Assert.IsFalse(player.IsMounted);
        }

        [Test]
        public void CenterGrabbable_BlocksVehicleEInputWhenVehicleIsAlsoVisible()
        {
            var player = MakePlayerWithCamera();
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 3.2f));
            var center = player.Cam.transform.position + player.Cam.transform.forward * Tuning.ArmReach;
            MakeGrabbable(center, "CenteredObject");

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, new[] { vehicle.Beam }, false);
            spawned.Add(controllerObject);

            Assert.AreEqual(PlayerInteractionKind.Grabbable,
                player.CurrentInteractionTarget.Kind);
            Assert.IsNull(controller.FocusedVehicle,
                "화면 중심의 일반 물체가 선택되면 운반 장치는 E 대상이 아니어야 한다.");

            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsFalse(controller.IsControlling);
            Assert.IsFalse(player.IsMounted);
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

            // S15P21B202-113 은 조종석 1인칭이었다. S15P21B202-441 에서 3인칭으로
            // 바꿨다 — 다른 게임이 차·탈것에서 쓰는 방식이고, 뒤에서 따라가면 무엇을
            // 타고 있는지가 화면에 남는다. 그 결정의 근거는 PhysicsScale 쪽 주석에 있다.
            Assert.IsFalse(camera.FirstPerson, "탑승 시 3인칭으로 물러나야 한다 (S15P21B202-441).");

            // **키를 뗐다 다시 누른다.** 예전에는 true 를 두 번 넣어 탑승→하차를
            // 기대했는데, 그것은 실제 입력이 아니라 이 결함의 모습이었다
            // (S15P21B202-680) — 서버가 마지막 입력을 매 틱 다시 적용하므로
            // 한 번 눌러도 true 가 두 틱 들어온다.
            controller.SetInput(new InputFrame());
            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsFalse(controller.IsControlling, "하차 요청 즉시 플레이어 조작으로 복귀한다.");
            Assert.IsTrue(vehicle.IsReturning);
            Assert.IsFalse(player.IsMounted);
        }

        /// <summary>
        /// <b>한 번 눌러 탄 뒤 바로 내리지 않는다</b>(S15P21B202-680).
        ///
        /// <para>멀티에서 탑승 버튼을 누르면 즉시 내려졌다. 서버는 참가자가 마지막으로
        /// 보낸 입력(<c>NetPlayer.pending</c>)을 새 프레임이 올 때까지 <b>매 물리 스텝
        /// 다시 적용</b>한다 — <c>GetKeyDown</c> 으로 한 프레임만 참이던 값이 서버에서는
        /// 여러 틱 참이고, 첫 틱에 타고 둘째 틱에 내렸다.</para>
        ///
        /// <para>잡기(<c>GrabLeft</c>)는 이미 누른 순간만 보고 있었고 탑승만 빠져 있었다.</para>
        /// </summary>
        [Test]
        public void HoldingTheBoardKey_DoesNotBoardAndDismountInOneGo()
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

            // 서버가 같은 입력을 다시 적용하는 상황. 키를 뗀 적이 없다.
            controller.SetInput(new InputFrame { TransportAction = true });
            controller.SetInput(new InputFrame { TransportAction = true });
            controller.SetInput(new InputFrame { TransportAction = true });

            Assert.IsTrue(controller.IsControlling, "한 번 눌렀는데 내려졌다.");
            Assert.IsTrue(player.IsMounted);
            Assert.IsFalse(vehicle.IsReturning, "하차가 시작되면 안 된다.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MountedClickAndF_ToggleLowGravityOnlyOncePerFrame(bool boost)
        {
            var player = MakePlayerWithCamera();
            var vehicle = MakeVehicle(new Vector3(0f, TransportVehicle.GroundY, 3.2f));

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, new[] { vehicle.Beam }, false);
            spawned.Add(controllerObject);

            controller.SetInput(new InputFrame { TransportAction = true });
            Assert.IsTrue(controller.IsControlling);
            Assert.IsFalse(vehicle.Field.IsActive);

            controller.SetInput(new InputFrame
            {
                GrabLeft = true,
                GrabRight = true,
                FieldToggle = true,
                Run = boost
            });
            controller.Tick(Time.fixedDeltaTime);
            Assert.IsTrue(vehicle.Field.IsActive,
                "탑승 중 클릭과 F는 저중력장 토글 경로를 사용해야 한다.");
            Assert.IsFalse(vehicle.Beam.IsHolding, "Shift를 눌러도 단일 파츠 회수 모드로 바뀌지 않는다.");

            controller.SetInput(new InputFrame());
            controller.SetInput(new InputFrame { FieldToggle = true, Run = boost });
            controller.Tick(Time.fixedDeltaTime);
            Assert.IsFalse(vehicle.Field.IsActive,
                "같은 프레임의 좌·우클릭·F가 토글을 여러 번 뒤집으면 안 된다.");
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
            vehicle.Tick(Time.fixedDeltaTime);
            Vector3 initialFacing = vehicle.ExitForward;

            var sideInput = Vector2.left;
            controller.SetInput(new InputFrame { Move = sideInput });
            controller.Tick(Time.fixedDeltaTime);

            // **컨트롤러는 기체에 입력만 넣는다**(SetDriveVector·SetFacingDirection).
            // 기체를 실제로 움직이는 것은 `TransportVehicle.Tick` 이고, 게임에서는
            // `FixedUpdate` 가 그것을 부른다 — EditMode 에는 `FixedUpdate` 가 없으므로
            // 여기서 직접 부르지 않으면 속도가 영원히 0 이다 (S15P21B202-463).
            vehicle.Tick(Time.fixedDeltaTime);

            Assert.Greater(Vector3.Dot(vehicle.HorizontalVelocity,
                                       player.Cam.CameraRelativeMove(sideInput)), 0.01f,
                           "A/D는 카메라 기준 선체 이동 벡터로 전달되어야 한다.");
            Assert.Less(Vector3.Angle(initialFacing, vehicle.ExitForward), 1f,
                        "A/D 이동은 운반선 정면을 회전시키지 않아야 한다.");

            player.Cam.Steer(new Vector2(30f, 0f));
            controller.SetInput(new InputFrame());
            controller.Tick(Time.fixedDeltaTime);
            vehicle.Tick(Time.fixedDeltaTime);
            Assert.Greater(Vector3.Dot(vehicle.ExitForward, player.Cam.PlanarForward), 0.99f,
                           "카메라 yaw가 바뀌면 정지 중에도 운반선 정면이 갱신되어야 한다.");
        }
    }
}
