using System.Collections.Generic;
using System.Reflection;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class PlasmaWeldingToolTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        [Test]
        public void ToolInputFrame_ExposesSemanticHeldAndEdgeStates()
        {
            var input = new ToolInputFrame
            {
                PrimaryHeld = true,
                PrimaryPressed = true,
                PrimaryReleased = false,
                SecondaryHeld = false,
                SecondaryPressed = false,
                SecondaryReleased = true,
                UtilityHeld = true,
                UtilityPressed = true,
                UtilityReleased = false
            };

            Assert.IsTrue(input.PrimaryHeld && input.PrimaryPressed);
            Assert.IsTrue(input.SecondaryReleased);
            Assert.IsTrue(input.UtilityHeld && input.UtilityPressed);
        }

        [Test]
        public void PlasmaTool_PrimaryAndSecondarySelectDifferentBeamModes()
        {
            var tool = CreateTool(out _);

            tool.SetInput(new ToolInputFrame { PrimaryHeld = true },
                          new ToolAimContext(null, null, Vector3.forward));
            Assert.AreEqual(PlasmaBeamMode.Weld, tool.BeamMode);
            Assert.IsTrue(tool.BeamActive);

            tool.SetInput(new ToolInputFrame { SecondaryHeld = true },
                          new ToolAimContext(null, null, Vector3.forward));
            Assert.AreEqual(PlasmaBeamMode.Cut, tool.BeamMode);
            Assert.IsTrue(tool.BeamActive);

            tool.SetInput(new ToolInputFrame { PrimaryHeld = true, SecondaryHeld = true },
                          new ToolAimContext(null, null, Vector3.forward));
            Assert.AreEqual(PlasmaBeamMode.None, tool.BeamMode);
            Assert.IsFalse(tool.BeamActive);
        }

        [Test]
        public void PlasmaTool_BeamStopsAtFirstExternalColliderAndHidesOnRelease()
        {
            var tool = CreateTool(out var muzzle);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "BeamWall";
            wall.transform.position = new Vector3(0f, 0f, 0.42f);
            wall.transform.localScale = new Vector3(0.4f, 0.4f, 0.08f);
            spawned.Add(wall);
            Physics.SyncTransforms();

            var aim = new ToolAimContext(null, null, Vector3.forward);
            tool.SetInput(new ToolInputFrame { PrimaryHeld = true }, aim);
            InvokeLateUpdate(tool);

            var line = tool.GetComponent<LineRenderer>();
            Assert.IsTrue(line.enabled);
            Assert.That(tool.BeamEnd.z, Is.EqualTo(wall.transform.position.z - 0.04f).Within(0.01f));
            Assert.That(tool.BeamEnd.z, Is.LessThan(muzzle.position.z + 0.8f));

            tool.SetInput(new ToolInputFrame(), aim);
            InvokeLateUpdate(tool);
            Assert.IsFalse(line.enabled);
        }

        [Test]
        public void PlasmaTool_CutBeamUsesDifferentColorWithoutPhysicsMutation()
        {
            var tool = CreateTool(out _);
            var body = tool.GetComponent<Rigidbody>();
            Vector3 beforeVelocity = body.linearVelocity;
            bool beforeKinematic = body.isKinematic;

            tool.SetInput(new ToolInputFrame { SecondaryHeld = true },
                          new ToolAimContext(null, null, Vector3.forward));
            InvokeLateUpdate(tool);

            var line = tool.GetComponent<LineRenderer>();
            Assert.AreEqual(PlasmaBeamMode.Cut, tool.BeamMode);
            Assert.AreEqual((Color32)tool.CurrentBeamColor, (Color32)line.startColor);
            Assert.AreEqual(beforeVelocity, body.linearVelocity);
            Assert.AreEqual(beforeKinematic, body.isKinematic);
        }

        [Test]
        public void PlasmaTool_DisablesBodyCollisionsOnlyWhileEquipped()
        {
            var tool = CreateTool(out _);
            var body = tool.GetComponent<Rigidbody>();

            Assert.IsFalse(body.detectCollisions);

            tool.Unequip();

            Assert.IsTrue(body.detectCollisions);
        }

        [Test]
        public void PlasmaTool_AppliesHeldVisualPoseAndRestoresDroppedPose()
        {
            var tool = CreateTool(out _);
            var visual = tool.transform.Find(
                "AttachPoints/ReplacementModel/SF_Stage1_PlasmaWeldingGun");
            Assert.IsNotNull(visual);

            Assert.That(visual.localPosition, Is.EqualTo(new Vector3(0.18f, -0.14f, 0.12f)));
            Assert.That(Quaternion.Angle(
                visual.localRotation,
                Quaternion.Euler(-90f, -80f, 10f)), Is.LessThan(0.01f));
            Assert.That(visual.localScale, Is.EqualTo(Vector3.one * 0.55f));

            tool.Unequip();

            Assert.That(visual.localPosition, Is.EqualTo(new Vector3(0f, -0.5f, 0.25f)));
            Assert.That(visual.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void PlasmaTool_LocalFirstPersonVisualFollowsCameraWithoutPhysicsLag()
        {
            var player = PlayerRig.Create("용접건 화면 표시 테스트", Vector3.zero,
                                          Quaternion.identity, Color.white, Color.gray,
                                          useModel: false);
            spawned.Add(player.gameObject);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            spawned.Add(camera.gameObject);
            player.Cam = camera;

            var tool = CreateTool(out _, player);
            var visual = tool.transform.Find(
                "AttachPoints/ReplacementModel/SF_Stage1_PlasmaWeldingGun");
            Assert.IsNotNull(visual, "월드 모델은 도구 계층에 남아야 한다.");
            var worldVisual = visual;
            visual = tool.FirstPersonModel;
            Assert.IsNotNull(visual, "1인칭 표시 복사본이 생성되지 않았다.");
            Assert.AreNotSame(worldVisual, visual);
            Assert.AreSame(camera.transform, visual.parent);
            Assert.That(visual.localPosition,
                        Is.EqualTo(new Vector3(0.20f, -0.17f, 0.52f)));

            Vector3 before = visual.localPosition;
            tool.GetComponent<Rigidbody>().position = new Vector3(20f, 5f, -10f);
            InvokeLateUpdate(tool);
            Assert.That(visual.localPosition, Is.EqualTo(before),
                        "물리 본체 이동이 화면 모델 위치를 늦게 끌고 갔다.");

            tool.Unequip();
            Assert.AreSame(tool.transform.Find("AttachPoints/ReplacementModel"), worldVisual.parent);
            Assert.IsFalse(visual.gameObject.activeSelf, "내려놓으면 화면 복사본을 숨겨야 한다.");
        }


        [Test]
        public void RayPreviewAndWeld_UseSameSurface_AndMissDoesNotConsume()
        {
            var part = WeldingBody();
            var tool = CreateTool(out _);
            Physics.SyncTransforms();
            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var hit));
            Assert.AreSame(part, hit.collider.GetComponentInParent<SpaceshipPart>());
            Assert.IsTrue(WeldBead.CanApplyAt(hit, false));
            Assert.IsTrue(tool.WeldTick(0.5f, PlasmaBeamMode.Weld));
            Assert.That(Vector3.Distance(tool.GrowingBead.TipPosition(0), hit.point), Is.LessThan(0.001f));
            Assert.That(tool.RemainingFraction, Is.EqualTo(1f - 0.5f / 60f).Within(0.0001f));
            tool.SetInput(new ToolInputFrame { PrimaryHeld = true }, new ToolAimContext(null, null, Vector3.left));
            Assert.IsFalse(tool.WeldingProgressed);
            Assert.That(tool.RemainingFraction, Is.EqualTo(1f - 0.5f / 60f).Within(0.0001f));
        }

        [Test]
        public void FirstObstructionAndRange_BlockWelding()
        {
            WeldingBody();
            var tool = CreateTool(out _);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(wall);
            wall.transform.position = new Vector3(0, 0, 0.3f);
            wall.transform.localScale = new Vector3(1, 1, 0.05f);
            Physics.SyncTransforms();
            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var hit));
            Assert.AreSame(wall.GetComponent<Collider>(), hit.collider);
            Assert.IsFalse(WeldBead.CanApplyAt(hit, false));
            Assert.IsFalse(tool.WeldTick(0.1f, PlasmaBeamMode.Weld));
            wall.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms();
            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out hit));
            Assert.IsTrue(WeldBead.CanApplyAt(hit, false), "트리거는 가리지 않는다.");
            tool.transform.position = Vector3.back * 2f;
            Physics.SyncTransforms();
            Assert.IsFalse(tool.TryGetBeamHit(Vector3.forward, out _));
            Assert.AreEqual(1f, tool.RemainingFraction);
        }

        [Test]
        public void Material_BelongsToGun_ReplicatesWhenDropped_AndEmptyStillCuts()
        {
            WeldingBody();
            var tool = CreateTool(out _);
            Physics.SyncTransforms();
            Assert.IsTrue(tool.WeldTick(0.1f, PlasmaBeamMode.Weld));
            float remaining = tool.RemainingFraction;
            tool.Unequip();
            Assert.AreEqual(remaining, tool.ViewProgress);
            tool.Equip(null, tool.GetComponent<Rigidbody>());
            Assert.AreEqual(remaining, tool.RemainingFraction);
            var other = CreateTool(out _);
            Assert.AreEqual(1f, other.RemainingFraction);
            other.Unequip();
            other.ApplyRemotePresentation(null, 0, tool.ViewProgress, Vector3.forward);
            Assert.That(other.RemainingFraction, Is.EqualTo(remaining).Within(0.0001f));
            tool.Unequip();
            tool.ApplyRemotePresentation(null, 0, 0f, Vector3.forward);
            tool.Equip(null, tool.GetComponent<Rigidbody>());
            Assert.IsFalse(tool.WeldTick(0.1f, PlasmaBeamMode.Weld));
            Physics.SyncTransforms();
            Assert.IsTrue(tool.CutTick(0.1f, PlasmaBeamMode.Cut));
            Assert.That(tool.RemainingFraction, Is.EqualTo(0.1f / 60f).Within(0.0001f));
            Assert.That(other.RemainingFraction, Is.EqualTo(remaining).Within(0.0001f),
                "절단한 용접건만 충전되어야 한다.");
        }

        [Test]
        public void Cutting_RefillsGradually_ClampsAtFull_AndMissDoesNotRefill()
        {
            WeldingBody();
            var tool = CreateTool(out _);
            Physics.SyncTransforms();
            Assert.IsTrue(tool.WeldTick(0.5f, PlasmaBeamMode.Weld));
            Physics.SyncTransforms();
            Assert.IsTrue(tool.CutTick(0.1f, PlasmaBeamMode.Cut));
            Assert.That(tool.RemainingFraction, Is.EqualTo(1f - 0.4f / 60f).Within(0.0001f));
            Physics.SyncTransforms();
            Assert.IsFalse(tool.CutTick(0.1f, PlasmaBeamMode.Cut));
            Assert.That(tool.RemainingFraction, Is.EqualTo(1f - 0.4f / 60f).Within(0.0001f));
            Assert.IsTrue(tool.WeldTick(0.1f, PlasmaBeamMode.Weld));
            Physics.SyncTransforms();
            Assert.IsTrue(tool.CutTick(1f, PlasmaBeamMode.Cut));
            Assert.AreEqual(1f, tool.RemainingFraction);
        }

        [Test]
        public void RemoteClient_CannotSpendMaterialOrCreateBeads()
        {
            var part = WeldingBody();
            var tool = CreateTool(out _);
            Physics.SyncTransforms();
            bool previous = WorldEvents.RemoteDecides;
            try
            {
                WorldEvents.RemoteDecides = true;
                Assert.IsFalse(tool.WeldTick(1f, PlasmaBeamMode.Weld));
                Assert.AreEqual(1f, tool.RemainingFraction);
                Assert.IsNull(part.GetComponent<WeldBead>());
            }
            finally { WorldEvents.RemoteDecides = previous; }
        }

        [Test]
        public void Material_ClampsLastSpend_AndCannotWeldAfterExhaustion()
        {
            WeldingBody();
            var tool = CreateTool(out _);
            Physics.SyncTransforms();
            Assert.IsTrue(tool.WeldTick(100f, PlasmaBeamMode.Weld));
            Assert.AreEqual(0f, tool.RemainingFraction);
            Assert.IsFalse(tool.WeldTick(1f, PlasmaBeamMode.Weld));
            Assert.AreEqual(0f, tool.RemainingFraction);
        }

        [Test]
        public void CrosshairContact_DrivesPreviewBeadAndBeam_WhenMuzzleIsOffset()
        {
            WeldingBody();
            var tool = CreateTool(out var muzzle);
            tool.transform.position = Vector3.right * 0.25f;
            var cameraObject = new GameObject("Crosshair camera");
            spawned.Add(cameraObject);
            var camera = cameraObject.AddComponent<OrbitCamera>();
            camera.transform.position = Vector3.back;
            var aim = new ToolAimContext(null, camera, Vector3.forward);
            tool.SetInput(new ToolInputFrame(), aim);
            Physics.SyncTransforms();

            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var preview));
            Assert.That(preview.point.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.IsTrue(tool.WeldTick(0.1f, PlasmaBeamMode.Weld));
            Assert.That(Vector3.Distance(tool.GrowingBead.TipPosition(0), preview.point), Is.LessThan(0.001f));

            tool.SetInput(new ToolInputFrame { PrimaryHeld = true }, aim);
            InvokeLateUpdate(tool);
            var line = tool.GetComponent<LineRenderer>();
            Assert.IsTrue(line.enabled);
            Assert.That(Vector3.Distance(line.GetPosition(0), muzzle.position), Is.LessThan(0.001f));
            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var beamContact));
            Assert.That(Vector3.Distance(tool.BeamEnd, beamContact.point), Is.LessThan(0.001f));
            Assert.That(tool.BeamEnd.x, Is.EqualTo(0f).Within(0.0001f));

            tool.transform.position = Vector3.right * 2f;
            Physics.SyncTransforms();
            Assert.IsFalse(tool.TryGetBeamHit(Vector3.forward, out _), "카메라가 볼 수 있어도 총구 사거리 밖은 작업할 수 없다.");
        }

        [Test]
        public void IdleGuide_ProjectsOnSurface_AndHidesOnUnequip()
        {
            WeldingBody();
            var player = PlayerRig.Create("Guide owner", Vector3.left * 4f,
                Quaternion.identity, Color.white, Color.gray, useModel: false);
            spawned.Add(player.gameObject);
            var cameraObject = new GameObject("Guide camera");
            spawned.Add(cameraObject);
            var camera = cameraObject.AddComponent<OrbitCamera>();
            var tool = CreateTool(out _, player);
            Physics.SyncTransforms();
            tool.SetInput(new ToolInputFrame(), new ToolAimContext(player, camera, Vector3.forward));
            InvokeLateUpdate(tool);
            Assert.IsTrue(tool.GuideVisible);
            var guide = tool.transform.Find("Welding surface guide");
            Assert.IsNotNull(guide);
            var mesh = guide.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 0);
            Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var hit));
            foreach (var vertex in mesh.vertices)
            {
                var point = guide.TransformPoint(vertex);
                Assert.That(Vector3.Dot(point - hit.point, hit.normal), Is.EqualTo(0.02f).Within(0.0005f));
            }
            Assert.IsNull(hit.collider.GetComponentInParent<WeldBead>(), "예고만으로 비드를 만들지 않는다.");
            tool.Unequip();
            Assert.IsFalse(tool.GuideVisible);
            Assert.IsFalse(guide.gameObject.activeSelf);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void JointRecommendation_FirstBeadActuallyJoins_AndDistantSurfaceDoesNot(bool meshHull)
        {
            var a = WeldingBody();
            var b = WeldingBody();
            if (meshHull)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var shape = cube.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cube);
                foreach (var part in new[] { a, b })
                {
                    Object.DestroyImmediate(part.GetComponent<BoxCollider>());
                    var hull = part.gameObject.AddComponent<MeshCollider>();
                    hull.sharedMesh = shape;
                    hull.convex = true;
                }
            }
            b.transform.position += Vector3.right;
            Physics.SyncTransforms();
            var collider = a.GetComponent<Collider>();
            Assert.IsTrue(collider.Raycast(new Ray(new Vector3(0.49f, 0f, 0f), Vector3.forward), out var seam, 2f));
            Assert.IsTrue(WeldBead.CanStartJoiningAt(seam));
            Assert.IsNull(a.GetComponent<WeldBead>(), "표시 판정은 상태를 변경하지 않는다.");
            Assert.IsTrue(collider.Raycast(new Ray(Vector3.zero, Vector3.forward), out var middle, 2f));
            Assert.IsFalse(WeldBead.CanStartJoiningAt(middle));
            Assert.IsTrue(WeldBead.TryApplyAt(seam, 0.02f, false, out var bead));
            CollectionAssert.Contains(new List<SpaceshipPart>(bead.BridgedParts), b);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void JointRecommendation_LocalHeldOnly_InSoloAndRemotePresentation(bool remote)
        {
            WeldingBody();
            var b = WeldingBody();
            b.transform.position += Vector3.right;
            var player = PlayerRig.Create("Joint guide owner", Vector3.left * 4f,
                Quaternion.identity, Color.white, Color.gray, useModel: false);
            spawned.Add(player.gameObject);
            var tool = CreateTool(out _, remote ? null : player);
            if (remote)
            {
                tool.Unequip();
                tool.ApplyRemoteHeld(true);
                tool.ApplyRemotePresentation(player, 0, 1f, Vector3.forward);
            }
            Physics.SyncTransforms();
            tool.SetInput(default, new ToolAimContext(player, null, Vector3.forward));
            InvokeLateUpdate(tool);
            Assert.IsTrue(tool.JointGuideVisible);
            var recommendation = GameObject.Find("Welding joint recommendations");
            Assert.IsNotNull(recommendation);
            Assert.IsNull(recommendation.GetComponent<Collider>());
            Assert.AreEqual(3000, recommendation.GetComponent<MeshRenderer>().sharedMaterial.renderQueue);
            bool frontSurfaceFound = false;
            foreach (var vertex in recommendation.GetComponent<MeshFilter>().sharedMesh.vertices)
                if (Mathf.Abs(vertex.z - 0.538f) < 0.0001f) frontSurfaceFound = true;
            Assert.IsTrue(frontSurfaceFound, "앞면 콜라이더(z=0.55)에서 1.2cm 띄워 그린다.");
            b.transform.position += Vector3.right * 2f;
            Physics.SyncTransforms();
            InvokeLateUpdate(tool);
            Assert.IsFalse(tool.JointGuideVisible, "접합부가 떨어지면 추천을 즉시 지운다.");
            b.transform.position -= Vector3.right * 2f;
            Physics.SyncTransforms();
            InvokeLateUpdate(tool);
            Assert.IsTrue(tool.JointGuideVisible);
            player.IsLocal = false;
            InvokeLateUpdate(tool);
            Assert.IsFalse(tool.JointGuideVisible);
            player.IsLocal = true;
            InvokeLateUpdate(tool);
            Assert.IsTrue(tool.JointGuideVisible);
            if (remote) tool.ApplyRemoteHeld(false); else tool.Unequip();
            Assert.IsFalse(tool.JointGuideVisible);
        }

        SpaceshipPart WeldingBody()
        {
            var objects = new List<GameObject>();
            var part = SocketTestRig.Placed(objects, new Vector3(0f, 0f, 0.6f));
            foreach (var go in objects) spawned.Add(go);
            part.transform.localScale = new Vector3(1f, 1f, 0.1f);
            return part;
        }

        PlasmaWeldingTool CreateTool(out Transform muzzle, PlayerRig owner = null)
        {
            var root = new GameObject("PlasmaTool");
            spawned.Add(root);
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            var collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.15f;
            var grabbable = root.AddComponent<Grabbable>();
            grabbable.Role = GrabbableRole.Tool;

            var attachPoints = new GameObject("AttachPoints").transform;
            attachPoints.SetParent(root.transform, false);
            var replacement = new GameObject("ReplacementModel").transform;
            replacement.SetParent(attachPoints, false);
            var visual = new GameObject("SF_Stage1_PlasmaWeldingGun").transform;
            visual.SetParent(replacement, false);
            visual.localPosition = new Vector3(0f, -0.5f, 0.25f);

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(attachPoints, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.10f);

            var tool = root.AddComponent<PlasmaWeldingTool>();
            tool.Equip(owner, body);
            return tool;
        }

        static void InvokeLateUpdate(PlasmaWeldingTool tool)
        {
            var method = typeof(ToolBase).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(tool, null);
        }
    }
}
