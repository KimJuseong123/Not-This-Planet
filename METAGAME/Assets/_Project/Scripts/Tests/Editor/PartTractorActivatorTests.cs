using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>Part_1이 고정못 해제 전에는 일반 파츠이고, 해제 후에는 유일한 견인 장치가 되는지 검증한다.</summary>
    public sealed class PartTractorActivatorTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartClamp.All.Clear();
            PartSocket.All.Clear();
        }

        [Test]
        public void Part1_ActivatesOnlyAfterAllRequiredClampsAreReleased()
        {
            var part = CreatePart(out var activator, out var bodyCollider, out var push,
                                  out var socket, out var clamps);

            Assert.AreEqual(PartState.Locked, part.State);
            Assert.IsFalse(activator.IsActivated);
            Assert.IsNull(part.GetComponent<TransportVehicle>());

            for (var i = 0; i < clamps.Count - 1; i++)
            {
                clamps[i].Release();

                Assert.AreEqual(PartState.Locked, part.State,
                    "고정못이 하나라도 남아 있으면 Part_1은 견인 장치가 되면 안 된다.");
                Assert.IsFalse(activator.IsActivated);
                Assert.IsNull(part.GetComponent<TransportVehicle>());
            }

            clamps[clamps.Count - 1].Release();

            var vehicle = activator.Vehicle;
            Assert.AreEqual(PartState.Free, part.State);
            Assert.IsTrue(activator.IsActivated);
            Assert.IsNotNull(vehicle);
            Assert.IsNotNull(vehicle.Beam);
            Assert.IsNotNull(part.GetComponent<TransportVehicle>());
            Assert.IsFalse(bodyCollider.enabled, "원본은 끄고 동일한 형상을 견인 외형 좌표계에 유지한다.");
            Assert.IsFalse(vehicle.transform.Find("VehiclePlatform").GetComponent<Collider>().enabled);
            Assert.IsTrue(vehicle.transform.Find("Visual/VehicleHull_0").GetComponent<Collider>().enabled);
            Assert.IsFalse(push.enabled, "전환 뒤 일반 파츠 밀기 입력은 남으면 안 된다.");
            Assert.IsFalse(socket.enabled, "전환 뒤 조립용 소켓 판정은 남으면 안 된다.");
            Assert.IsFalse(part.GetComponent<Grabbable>().enabled,
                "전환 뒤 Part_1 루트가 일반 손잡이 대상으로 남으면 안 된다.");
            Assert.IsNotNull(vehicle.transform.Find("VehiclePlatform"));
            Assert.IsNotNull(vehicle.transform.Find("VehicleSeat"));
            Assert.AreEqual(GameLayers.Prop, vehicle.gameObject.layer);
        }

        [Test]
        public void ConversionAndDismount_PreserveCompoundHullAndEmptySpace()
        {
            var part = CreatePart(out var activator, out var lower, out _, out _, out var clamps);
            lower.center = new Vector3(0f, 0.5f, 0f);
            var upper = part.gameObject.AddComponent<BoxCollider>();
            upper.center = new Vector3(0f, 1.5f, 0f);
            upper.size = new Vector3(2f, 1f, 1f);
            foreach (var clamp in clamps) clamp.Release();
            var vehicle = activator.Vehicle;
            AssertCompoundShape(vehicle);

            var player = PlayerRig.Create("Hull rider", vehicle.transform.position + Vector3.forward * 1.5f,
                Quaternion.identity, Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            Assert.IsTrue(vehicle.TryBoard(player));
            AssertCompoundShape(vehicle);
            vehicle.BeginDismount();
            AssertCompoundShape(vehicle);
            vehicle.ForceReset();
            AssertCompoundShape(vehicle);
        }

        static void AssertCompoundShape(TransportVehicle vehicle)
        {
            Assert.IsNotNull(vehicle, "고정못 해제 이벤트가 견인 전환을 실행해야 한다.");
            Physics.SyncTransforms();
            var lower = vehicle.transform.Find("Visual/VehicleHull_0").GetComponent<BoxCollider>();
            var upper = vehicle.transform.Find("Visual/VehicleHull_1").GetComponent<BoxCollider>();
            Assert.IsFalse(vehicle.transform.Find("VehiclePlatform").GetComponent<Collider>().enabled);
            Assert.AreEqual(Vector3.one, lower.size);
            Assert.AreEqual(new Vector3(2f, 1f, 1f), upper.size);
            var bottom = lower.bounds.min.y;
            var center = lower.bounds.center;
            // Wide upper body must collide, but the empty space beside its narrow foot must not.
            Assert.IsTrue(upper.Raycast(new Ray(new Vector3(center.x + 0.9f, bottom + 1.5f,
                center.z - 3f), Vector3.forward), out _, 6f));
            Assert.IsFalse(lower.Raycast(new Ray(new Vector3(center.x + 0.9f, bottom + 0.5f,
                center.z - 3f), Vector3.forward), out _, 6f));
        }

        [Test]
        public void ActivatedPart1_IsRegisteredAsTransportInteractionTarget()
        {
            var part = CreatePart(out var activator, out _, out _, out _, out var clamps,
                                  new Vector3(0f, 1f, 3.2f));
            var player = PlayerRig.Create("테스트", Vector3.zero, Quaternion.identity,
                                         Color.white, Color.gray, false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            player.Cam = camera;
            camera.Yaw = 0f;
            camera.Pitch = 0f;
            camera.SnapToTarget();
            spawned.Add(player.gameObject);
            spawned.Add(camera.gameObject);

            var controllerObject = new GameObject("TransportController");
            var controller = controllerObject.AddComponent<TractorBeamController>();
            controller.Configure(player, System.Array.Empty<TractorBeam>(), false);
            spawned.Add(controllerObject);

            for (var i = 0; i < clamps.Count; i++) clamps[i].Release();

            Assert.IsTrue(activator.IsActivated);
            Assert.AreSame(activator.Vehicle, controller.FocusedVehicle,
                "해제된 Part_1이 별도 장치 없이 컨트롤러의 유일한 운반 대상이 되어야 한다.");
            Assert.AreEqual(PlayerInteractionKind.Transport,
                            player.CurrentInteractionTarget.Kind);
            Assert.AreSame(activator.Vehicle, player.CurrentInteractionTarget.Transport);
            Assert.AreSame(part.GetComponent<TransportVehicle>(), activator.Vehicle);
        }

        SpaceshipPart CreatePart(out PartTractorActivator activator,
                                 out BoxCollider bodyCollider,
                                 out PartPush push,
                                 out PartSocket socket,
                                 out List<PartClamp> clamps,
                                 Vector3 position = default)
        {
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = position;
            spawned.Add(go);

            go.AddComponent<Rigidbody>();
            bodyCollider = go.AddComponent<BoxCollider>();
            bodyCollider.size = Vector3.one;

            var part = go.AddComponent<SpaceshipPart>();
            push = go.AddComponent<PartPush>();

            var socketObject = new GameObject("Socket_Top");
            socketObject.transform.SetParent(go.transform, false);
            socket = socketObject.AddComponent<PartSocket>();
            socket.SetRole(SocketRole.Top);
            spawned.Add(socketObject);

            clamps = new List<PartClamp>(4);
            for (var i = 0; i < 4; i++)
            {
                var clampObject = new GameObject($"Clamp_{i}")
                    { layer = GameLayers.Interactable };
                clampObject.transform.SetParent(go.transform, false);
                clampObject.AddComponent<SphereCollider>().isTrigger = true;
                var clamp = clampObject.AddComponent<PartClamp>();
                clamps.Add(clamp);
                spawned.Add(clampObject);
            }

            part.RefreshClamps();
            part.RefreshSockets();
            activator = go.AddComponent<PartTractorActivator>();
            activator.Initialize(); // EditMode does not invoke MonoBehaviour.OnEnable.
            return part;
        }
    }
}
