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

        TransportVehicle CreateFlyingPart(Vector3 position, float roll = 0f)
        {
            var part = CreatePart(out var activator, out _, out _, out _, out var clamps, position);
            part.transform.rotation = Quaternion.Euler(0f, 25f, roll);
            for (var i = 0; i < clamps.Count; i++)
            {
                clamps[i].transform.position = new Vector3(50f + i * 2f, 0f, 0f);
                clamps[i].Release();
            }
            var player = PlayerRig.Create("Pilot", position + Vector3.right * 2f,
                Quaternion.identity, Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            Assert.IsTrue(activator.Vehicle.TryBoard(player));
            return activator.Vehicle;
        }

        [TestCase(0f)]
        [TestCase(75f)]
        [TestCase(180f)]
        public void Boarding_UsesSeatFrameForPilotPositionAndRotation(float roll)
        {
            var vehicle = CreateFlyingPart(Vector3.up * 3f, roll);
            var expected = vehicle.SeatFootPosition
                + vehicle.SeatRotation * (Vector3.up * RagdollBuilder.HipsY);
            Assert.That(Vector3.Distance(expected, vehicle.Rider.Hips.position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(vehicle.SeatRotation, vehicle.Rider.Hips.rotation), Is.LessThan(0.01f));
        }

        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        public void FlightBoundary_ContainsMeteorMomentumAndAllowsReturn(float x, float z)
        {
            var outward = new Vector3(x, 0f, z);
            var vehicle = CreateFlyingPart(outward * 20.75f + Vector3.up * 3f);
            SimulatePart(vehicle, 4f);
            vehicle.TakeDirectMeteorImpact(vehicle.Body.worldCenterOfMass + Vector3.up, outward * 15f);
            for (var i = 0; i < 100; i++)
            {
                SimulatePart(vehicle, 0.02f);
                Assert.LessOrEqual(Vector3.Dot(vehicle.Body.position, outward), 21.01f);
            }
            var edge = Vector3.Dot(vehicle.Body.position, outward);
            vehicle.SetDriveVector(-outward);
            SimulatePart(vehicle, 2f);
            Assert.Less(Vector3.Dot(vehicle.Body.position, outward), edge - 1f);
        }

        [Test]
        public void FlightBoundary_UsesAuthoredMapPositionAndSize()
        {
            var mapObject = new GameObject("Offset map");
            spawned.Add(mapObject);
            var map = mapObject.AddComponent<AuthoredMapSettings>();
            mapObject.transform.position = new Vector3(30f, 0f, -20f);
            map.flightHalfSize = new Vector2(6f, 9f);
            var vehicle = CreateFlyingPart(new Vector3(35.75f, 3f, -20f));
            SimulatePart(vehicle, 4f);
            vehicle.TakeDirectMeteorImpact(vehicle.Body.worldCenterOfMass + Vector3.up, Vector3.right * 15f);
            for (var i = 0; i < 100; i++)
            {
                SimulatePart(vehicle, 0.02f);
                Assert.LessOrEqual(vehicle.Body.position.x, map.Bounds.max.x + 0.01f);
            }
        }

        [TestCase(0f)]
        [TestCase(75f)]
        [TestCase(180f)]
        public void FreePart_BoardsWithoutTeleportAndRecoversFromAirborneImpact(float roll)
        {
            var part = CreatePart(out var activator, out var hull, out _, out _, out var clamps,
                new Vector3(0f, 3f, 0f));
            part.transform.rotation = Quaternion.Euler(0f, 25f, roll);
            var initialPose = part.transform.rotation;
            var initialColliders = part.GetComponents<Collider>();
            for (var i = 0; i < clamps.Count; i++)
                clamps[i].transform.position = new Vector3(20f + i * 2f, 0f, 0f);
            foreach (var clamp in clamps) clamp.Release();
            var vehicle = activator.Vehicle;
            Assert.That(Quaternion.Angle(initialPose, part.transform.rotation), Is.LessThan(0.01f));
            CollectionAssert.AreEquivalent(initialColliders, part.GetComponentsInChildren<Collider>());
            Assert.AreEqual(RigidbodyConstraints.None, vehicle.Body.constraints);
            vehicle.Body.angularVelocity = Vector3.right * 2f;
            SimulatePart(vehicle, 0.15f);
            Assert.Greater(Quaternion.Angle(initialPose, part.transform.rotation), 5f,
                "탑승하지 않은 파츠는 회전 잠금 없이 굴러야 한다.");
            initialPose = part.transform.rotation;
            var player = PlayerRig.Create("Pilot", part.transform.position + Vector3.right * 2f,
                Quaternion.identity, Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            var position = part.transform.position;
            Assert.IsTrue(vehicle.TryBoard(player));
            Assert.That(Vector3.Distance(position, part.transform.position), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(initialPose, part.transform.rotation), Is.LessThan(0.01f));
            SimulatePart(vehicle, 5f);
            Assert.Greater(Vector3.Dot(part.transform.up, Vector3.up), 0.95f);
            Assert.Greater(part.transform.position.y, position.y + 1f);
            var beforeImpact = part.transform.rotation;
            vehicle.TakeDirectMeteorImpact(vehicle.Body.worldCenterOfMass + Vector3.right,
                Vector3.down * 15f);
            SimulatePart(vehicle, 0.3f);
            Assert.IsTrue(vehicle.Tumbling);
            Assert.Greater(Quaternion.Angle(beforeImpact, part.transform.rotation), 5f);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, vehicle.CameraTilt), 12.01f);
            SimulatePart(vehicle, 6f);
            Assert.IsFalse(vehicle.Tumbling);
            Assert.Greater(Vector3.Dot(part.transform.up, Vector3.up), 0.95f);
            Assert.IsTrue(hull.enabled);
            Assert.IsTrue(player.IsMounted);
            vehicle.BeginDismount();
            Assert.AreEqual(RigidbodyConstraints.None, vehicle.Body.constraints);
        }

        static void SimulatePart(TransportVehicle vehicle, float seconds)
        {
            var saved = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Physics.SyncTransforms();
                for (var i = 0; i < Mathf.CeilToInt(seconds / 0.02f); i++)
                {
                    vehicle.Tick(0.02f);
                    Physics.Simulate(0.02f);
                }
            }
            finally { Physics.simulationMode = saved; }
        }

        [Test]
        public void PartFlight_RespectsCeilingAndLandsAfterDismount()
        {
            var part = CreatePart(out var activator, out var hull, out _, out _, out var clamps,
                new Vector3(0f, 2f, 0f));
            for (var i = 0; i < clamps.Count; i++)
            {
                clamps[i].transform.position = new Vector3(20f + i * 2f, 0f, 0f);
                clamps[i].Release();
            }
            var floorObject = new GameObject("Floor") { layer = GameLayers.Ground };
            spawned.Add(floorObject);
            floorObject.transform.position = Vector3.down * 0.5f;
            floorObject.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);
            var ceiling = new GameObject("Ceiling") { layer = GameLayers.Ground };
            spawned.Add(ceiling);
            ceiling.transform.position = new Vector3(0f, 5f, 0f);
            var roof = ceiling.AddComponent<BoxCollider>();
            roof.size = new Vector3(30f, 0.5f, 30f);
            var player = PlayerRig.Create("Pilot", part.transform.position + Vector3.right * 2f,
                Quaternion.identity, Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            var vehicle = activator.Vehicle;
            Assert.IsTrue(vehicle.TryBoard(player));
            SimulatePart(vehicle, 5f);
            Assert.Less(hull.bounds.max.y, roof.bounds.min.y + 0.05f);
            vehicle.Field.SetActive(true);
            vehicle.BeginDismount();
            Assert.IsFalse(vehicle.Field.IsActive);
            SimulatePart(vehicle, 3f);
            Assert.IsTrue(vehicle.Body.useGravity);
            Assert.Less(vehicle.Body.position.y, 1f);
            Assert.Greater(hull.bounds.min.y, -0.1f);
        }

        [Test]
        public void PlacedPart_CannotStartIndependentFlight()
        {
            var part = CreatePart(out var activator, out _, out _, out _, out var clamps);
            foreach (var clamp in clamps) clamp.Release();
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);
            var player = PlayerRig.Create("Pilot", Vector3.right, Quaternion.identity,
                Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            Assert.IsFalse(activator.Vehicle.CanBoardFrom(player));
            Assert.IsFalse(activator.Vehicle.TryBoard(player));
        }

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
            Assert.IsTrue(bodyCollider.enabled, "기존 몸체 충돌체를 유지한다.");
            Assert.IsNull(vehicle.transform.Find("VehiclePlatform"));
            Assert.AreSame(bodyCollider, part.GetComponent<BoxCollider>());
            Assert.IsFalse(push.enabled, "전환 뒤 일반 파츠 밀기 입력은 남으면 안 된다.");
            Assert.IsTrue(socket.enabled, "견인 기능을 겸해도 엔진 소켓은 조립에 필요하다.");
            Assert.IsFalse(part.GetComponent<Grabbable>().enabled,
                "전환 뒤 Part_1 루트가 일반 손잡이 대상으로 남으면 안 된다.");
            Assert.IsNull(vehicle.transform.Find("VehiclePlatform"));
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
            var hull = source;
            Assert.That(Vector3.Distance(center, hull.transform.TransformPoint(hull.center)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rotation, hull.transform.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(size, Vector3.Scale(hull.size, hull.transform.lossyScale)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(-part.transform.up, activator.Vehicle.Beam.BeamOrigin.forward), Is.LessThan(0.01f));
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
            var hull = part.GetComponent<BoxCollider>();
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
            var lower = vehicle.GetComponents<BoxCollider>()[0];
            var upper = vehicle.GetComponents<BoxCollider>()[1];
            Assert.IsNull(vehicle.transform.Find("VehiclePlatform"));
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

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        public void ActivatedPart1_FocusAndBoardingFollowOffsetHull(float roll)
        {
            var part = CreatePart(out var activator, out var hull, out _, out _, out var clamps,
                new Vector3(0f, 2f, 0f));
            hull.center = Vector3.right * 10f;
            hull.size = new Vector3(4f, 2f, 2f);
            part.transform.rotation = Quaternion.Euler(0f, 0f, roll);
            var center = part.transform.TransformPoint(hull.center);
            var player = PlayerRig.Create("Pilot", center - Vector3.forward * 3f,
                Quaternion.identity, Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, false);
            spawned.Add(camera.gameObject);
            player.Cam = camera;
            camera.Yaw = 0f;
            camera.Pitch = 0f;
            camera.transform.position = center - Vector3.forward * 3f;
            foreach (var clamp in clamps) clamp.Release();
            Physics.SyncTransforms();

            Assert.IsTrue(activator.Vehicle.CanBoardFrom(player),
                "원점이 멀어도 몸체 표면 가까이에서는 탑승할 수 있어야 한다.");
            player.ConfigureTransportInteractionTargets(new[] { activator.Vehicle });
            Assert.AreEqual(PlayerInteractionKind.Transport, player.CurrentInteractionTarget.Kind);
            Assert.Less(Vector3.Angle(player.AimForward,
                player.CurrentInteractionTarget.Point - player.AimOrigin), 0.01f);

            camera.Yaw = 47f;
            Assert.IsFalse(hull.Raycast(new Ray(player.AimOrigin, player.AimForward), out _, 34f));
            player.RefreshInteractionFocus();
            Assert.AreNotEqual(PlayerInteractionKind.Transport, player.CurrentInteractionTarget.Kind,
                "몸체를 빗나간 조준은 가까워도 탑승 대상으로 보정하지 않는다.");
            camera.Yaw = 0f;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = center - Vector3.forward * 2f;
            wall.transform.localScale = new Vector3(5f, 5f, 0.2f);
            Physics.SyncTransforms();
            player.RefreshInteractionFocus();
            Assert.AreNotEqual(PlayerInteractionKind.Transport, player.CurrentInteractionTarget.Kind,
                "조준선 앞에 벽이 있으면 뒤의 기체를 선택하지 않는다.");
            wall.SetActive(false);
            Physics.SyncTransforms();

            player.Doll.Teleport(center - Vector3.forward * 10f, Quaternion.identity);
            camera.transform.position = center - Vector3.forward * 10f;
            Physics.SyncTransforms();
            player.RefreshInteractionFocus();
            Assert.AreSame(activator.Vehicle, player.CurrentInteractionTarget.Transport,
                "멀어도 실제로 조준한 기체는 유지하고 탑승 가능 여부만 거리로 제한한다.");
            Assert.IsFalse(player.CurrentInteractionTarget.CanInteract);
            Assert.IsFalse(activator.Vehicle.CanBoardFrom(player));
            Assert.IsFalse(activator.Vehicle.TryBoard(player));
            player.Doll.Teleport(center + Vector3.up * 10f, Quaternion.identity);
            Assert.IsFalse(activator.Vehicle.CanBoardFrom(player), "높이 차이도 실제 거리로 제한한다.");
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
