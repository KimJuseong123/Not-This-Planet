using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
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

            var aim = new ToolAimContext(null, null, Vector3.forward);
            tool.SetInput(new ToolInputFrame { PrimaryHeld = true }, aim);
            tool.gameObject.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);

            var line = tool.GetComponent<LineRenderer>();
            Assert.IsTrue(line.enabled);
            Assert.That(tool.BeamEnd.z, Is.EqualTo(wall.transform.position.z - 0.04f).Within(0.01f));
            Assert.That(tool.BeamEnd.z, Is.LessThan(muzzle.position.z + 0.8f));

            tool.SetInput(new ToolInputFrame(), aim);
            tool.gameObject.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
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
            tool.gameObject.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);

            var line = tool.GetComponent<LineRenderer>();
            Assert.AreEqual(PlasmaBeamMode.Cut, tool.BeamMode);
            Assert.AreEqual(tool.CurrentBeamColor, line.startColor);
            Assert.AreEqual(beforeVelocity, body.linearVelocity);
            Assert.AreEqual(beforeKinematic, body.isKinematic);
        }

        PlasmaWeldingTool CreateTool(out Transform muzzle)
        {
            var root = new GameObject("PlasmaTool");
            spawned.Add(root);
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            var collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.15f;
            var grabbable = root.AddComponent<Grabbable>();
            grabbable.Role = GrabbableRole.Tool;

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.10f);

            var tool = root.AddComponent<PlasmaWeldingTool>();
            tool.Equip(null, body);
            return tool;
        }
    }
}
