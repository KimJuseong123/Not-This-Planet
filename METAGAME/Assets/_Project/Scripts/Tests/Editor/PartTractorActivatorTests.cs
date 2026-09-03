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
            Assert.IsFalse(bodyCollider.enabled, "기존 Part_1 몸체 충돌은 운반 플랫폼과 함께 남으면 안 된다.");
            Assert.IsFalse(push.enabled, "전환 뒤 일반 파츠 밀기 입력은 남으면 안 된다.");
            Assert.IsFalse(socket.enabled, "전환 뒤 조립용 소켓 판정은 남으면 안 된다.");
            Assert.IsFalse(part.GetComponent<Grabbable>().enabled,
                "전환 뒤 Part_1 루트가 일반 손잡이 대상으로 남으면 안 된다.");
            Assert.IsNotNull(vehicle.transform.Find("VehiclePlatform"));
            Assert.IsNotNull(vehicle.transform.Find("VehicleSeat"));
            Assert.AreEqual(GameLayers.Prop, vehicle.gameObject.layer);
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
            return part;
        }
    }
}
