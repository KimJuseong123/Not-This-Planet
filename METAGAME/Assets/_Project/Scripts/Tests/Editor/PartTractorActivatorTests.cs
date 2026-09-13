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
            WeldBead.All.Clear();
            WorldEvents.RemoteDecides = false;
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
            Assert.IsTrue(socket.enabled, "견인 기능을 겸해도 엔진 소켓은 조립에 필요하다.");
            Assert.IsFalse(part.GetComponent<Grabbable>().enabled,
                "전환 뒤 Part_1 루트가 일반 손잡이 대상으로 남으면 안 된다.");
            Assert.IsNotNull(vehicle.transform.Find("VehiclePlatform"));
            Assert.IsNotNull(vehicle.transform.Find("VehicleSeat"));
            Assert.AreEqual(GameLayers.Part, vehicle.gameObject.layer);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Conversion_KeepsAuthoredSurfaceAndBeadsEditable(bool remote, bool existingBead)
        {
            WorldEvents.RemoteDecides = remote;
            var part = CreatePart(out var activator, out var legacy, out _, out _, out var clamps,
                new Vector3(7f, 3f, -4f));
            legacy.enabled = false;
            var bodyObject = new GameObject("Body_0") { layer = GameLayers.Part };
            bodyObject.transform.SetParent(part.transform, false);
            var surface = bodyObject.AddComponent<BoxCollider>();
            surface.size = Vector3.one * 2f;
            Physics.SyncTransforms();
            var ray = new Ray(part.transform.position + Vector3.up * 2f, Vector3.down);
            Assert.IsTrue(surface.Raycast(ray, out var hit, 3f));
            WeldBead bead = null;
            Collider blob = null;
            var blobPosition = Vector3.zero;
            if (existingBead)
            {
                Assert.IsTrue(WeldBead.TryApplyAt(hit, 0.02f, false, out bead));
                blob = part.GetComponentInChildren<SphereCollider>();
                // Clamp triggers also use spheres; the bead has a solid sphere.
                foreach (var sphere in part.GetComponentsInChildren<SphereCollider>())
                    if (!sphere.isTrigger) blob = sphere;
                blobPosition = blob.transform.position;
            }
            foreach (var clamp in clamps)
            {
                if (remote) clamp.ApplyNetworkState(1f, true);
                else clamp.Release();
            }
            Physics.SyncTransforms();
            Assert.IsTrue(surface.enabled, "원래 몸체 충돌체를 계속 사용해야 한다.");
            Assert.AreSame(part.GetComponent<Rigidbody>(), surface.attachedRigidbody);
            Assert.IsNull(part.transform.Find("Visual/VehicleHull_0"), "몸체 복제본을 만들면 안 된다.");
            Assert.IsTrue(surface.Raycast(ray, out hit, 3f));
            Assert.IsTrue(WeldBead.TryApplyAt(hit, 0.02f, false, out var after));
            if (existingBead)
            {
                Assert.AreSame(bead, after);
                Assert.IsTrue(blob.enabled);
                Assert.That(Vector3.Distance(blobPosition, blob.transform.position), Is.LessThan(0.0001f));
            }
            else
            {
                bead = after;
                foreach (var sphere in part.GetComponentsInChildren<SphereCollider>())
                    if (!sphere.isTrigger) { blob = sphere; break; }
            }

            // Steering rotates the visual independently of the beam root; beads must follow it.
            var visual = surface.transform.parent;
            var localBlobPosition = visual.InverseTransformPoint(blob.transform.position);
            visual.Rotate(Vector3.up, 65f, Space.World);
            Physics.SyncTransforms();
            Assert.That(Vector3.Distance(visual.TransformPoint(localBlobPosition), blob.transform.position),
                Is.LessThan(0.0001f));
            var cutRay = new Ray(blob.bounds.center + Vector3.up, Vector3.down);
            Assert.IsTrue(blob.Raycast(cutRay, out var cutHit, 2f));
            Assert.IsTrue(WeldBead.CanApplyAt(cutHit, true));
            Assert.IsTrue(WeldBead.TryApplyAt(cutHit, 0.02f, true, out var cutBead));
            Assert.AreSame(bead, cutBead);
            Assert.IsTrue(activator.IsActivated);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LastClampRelease_PreservesWorldPoseAndCollisionVolume(bool remote)
        {
            WorldEvents.RemoteDecides = remote;
            var part = CreatePart(out var activator, out var source, out _, out var socket,
                out var clamps, new Vector3(7f, 3f, -4f));
            part.transform.rotation = Quaternion.Euler(12f, 37f, -8f);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(part.transform, false);
            visual.localPosition = new Vector3(0.3f, 0.6f, -0.2f);
            source.center = new Vector3(0.1f, 0.2f, -0.1f);
            source.size = new Vector3(2f, 1f, 1.5f);
            for (var i = 0; i < 3; i++)
            {
                if (remote) clamps[i].ApplyNetworkState(1f, true);
                else clamps[i].Release();
            }
            Physics.SyncTransforms();
            var rootPosition = part.transform.position;
            var visualPosition = visual.position;
            var visualRotation = visual.rotation;
            var socketPosition = socket.transform.position;
            var socketRotation = socket.transform.rotation;
            var center = source.transform.TransformPoint(source.center);
            var rotation = source.transform.rotation;
            var size = Vector3.Scale(source.size, source.transform.lossyScale);
            if (remote) clamps[3].ApplyNetworkState(1f, true);
            else clamps[3].Release();
            Physics.SyncTransforms();

            Assert.That(Vector3.Distance(rootPosition, part.transform.position), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(visualPosition, visual.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(visualRotation, visual.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(socketPosition, socket.transform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(socketRotation, socket.transform.rotation), Is.LessThan(0.01f));
            var hull = visual.Find("VehicleHull_0").GetComponent<BoxCollider>();
            Assert.That(Vector3.Distance(center, hull.transform.TransformPoint(hull.center)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rotation, hull.transform.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(size, Vector3.Scale(hull.size, hull.transform.lossyScale)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(Vector3.down, part.transform.forward), Is.LessThan(0.01f));
            Assert.AreEqual(remote, part.GetComponent<Rigidbody>().isKinematic);
            Assert.IsTrue(activator.IsActivated);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleasedEngine_RemainsWeldableOnAuthorityAndReplica(bool remote)
        {
            WorldEvents.RemoteDecides = remote;
            var part = CreatePart(out var activator, out _, out _, out var socket, out var clamps);
            foreach (var clamp in clamps)
            {
                if (remote) clamp.ApplyNetworkState(1f, true);
                else clamp.Release();
            }
            AssertWeldable(part, activator.Vehicle);
            Assert.IsTrue(socket.enabled);
            Assert.Contains(socket, PartSocket.All);
            Assert.IsTrue(socket.CanReceive);
            Assert.AreEqual(BeamGrabResult.NoTarget, activator.Vehicle.Beam.TryGrab(part),
                "Keeping the Part layer must not let the tractor grab itself.");
            var body = part.GetComponent<Rigidbody>();
            part.SetExtraMass(2f);
            part.RefreshRepresentation();
            Assert.AreEqual(remote, body.isKinematic);
            Assert.AreEqual(82f, body.mass, 0.001f);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Engine_RemainsWeldableAndLaunchableAfterRiderChanges(int rides)
        {
            var part = CreatePart(out var activator, out _, out _, out var top, out var clamps);
            foreach (var clamp in clamps) clamp.Release();
            var vehicle = activator.Vehicle;
            for (var i = 0; i < rides; i++)
            {
                var player = PlayerRig.Create("Rider " + i,
                    vehicle.transform.position + Vector3.forward * 1.5f,
                    Quaternion.identity, Color.white, Color.gray, false);
                spawned.Add(player.gameObject);
                Assert.IsTrue(vehicle.TryBoard(player));
                Assert.IsFalse(top.CanReceive);
                vehicle.BeginDismount();
                AssertWeldable(part, vehicle);
                vehicle.ForceReset();
            }
            AssertWeldable(part, vehicle);
            Assert.IsTrue(top.CanReceive);
            Assert.Less(Vector3.Angle(top.transform.forward, Vector3.up), 0.01f);

            var baseSocket = new GameObject("Socket_Base");
            baseSocket.transform.SetParent(part.transform, false);
            baseSocket.AddComponent<PartSocket>().SetRole(SocketRole.Base);
            part.RefreshSockets();

            // Assemble all remaining floors onto the converted engine through real socket matching.
            var previousTop = top;
            for (var i = 1; i < PartSpec.All.Length; i++)
            {
                var go = new GameObject(PartSpec.All[i].Name) { layer = GameLayers.Part };
                spawned.Add(go);
                go.transform.position = previousTop.transform.position + Vector3.up * 0.5f;
                go.AddComponent<BoxCollider>();
                var next = go.AddComponent<SpaceshipPart>();
                foreach (var role in new[] { SocketRole.Base, SocketRole.Top })
                {
                    var child = new GameObject(role.ToString());
                    child.transform.SetParent(go.transform, false);
                    var direction = role == SocketRole.Top ? Vector3.up : Vector3.down;
                    child.transform.localPosition = direction * 0.5f;
                    child.transform.rotation = Quaternion.LookRotation(direction, Vector3.forward);
                    var socket = child.AddComponent<PartSocket>();
                    socket.SetRole(role);
                    if (role == SocketRole.Top) previousTop = socket;
                }
                next.Initialize();
                next.TryTransitionTo(PartState.Free);
                next.TryTransitionTo(PartState.Carried);
                var match = PartAssembler.FindMatch(next);
                Assert.IsTrue(match.HasValue, "Missing socket match for floor " + i);
                var link = PartAssembler.Assemble(match);
                Assert.IsNotNull(link);
                link.ForceWeldComplete();
                link.Tick();
            }
            var pad = new GameObject("LaunchPad");
            spawned.Add(pad);
            pad.transform.position = part.transform.position;
            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Evaluate();
            Assert.IsTrue(assembly.IsBoardable, assembly.Blocker);
            var launch = pad.AddComponent<LaunchTest>();
            launch.Begin(assembly.Engine);
            Assert.AreEqual(LaunchState.Ramping, launch.State, launch.FailureReason);
        }

        static void AssertWeldable(SpaceshipPart part, TransportVehicle vehicle)
        {
            Physics.SyncTransforms();
            var hull = vehicle.transform.Find("Visual/VehicleHull_0").GetComponent<BoxCollider>();
            var nozzle = hull.bounds.center + Vector3.right * (hull.bounds.extents.x + 0.05f);
            Assert.IsTrue(WeldBead.TryGrowNear(nozzle, 0.02f, out var bead),
                "Converted engine hull must remain a weld target.");
            Assert.AreSame(part, bead.Owner);
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
            socketObject.transform.localPosition = Vector3.up * 0.5f;
            socketObject.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
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
