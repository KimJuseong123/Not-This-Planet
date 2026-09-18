using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class PartOutlineRenderTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<Object> spawned = new();
        bool previousRemote;

        [SetUp]
        public void SetUp() => previousRemote = WorldEvents.RemoteDecides;

        [TearDown]
        public void TearDown()
        {
            WorldEvents.RemoteDecides = previousRemote;
            for (var i = spawned.Count - 1; i >= 0; i--)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
            CollectNow();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DockedPartsTouchingWithoutBeadsDoNotActivateOutline(bool remote)
        {
            WorldEvents.RemoteDecides = remote;
            var lower = Part(0, Vector3.zero);
            lower.ApplyDockedToPad(true);
            var upper = Part(1, Vector3.up);
            Physics.SyncTransforms();
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper));
            Assert.IsFalse(CollectNow());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WeldedDockedPartsCanRecordMaskCommands(bool remote)
        {
            WorldEvents.RemoteDecides = remote;
            var lower = Part(0, Vector3.zero);
            lower.ApplyDockedToPad(true);
            var upper = Part(1, Vector3.up);
            var joint = upper.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = lower.GetComponent<Rigidbody>();
            var link = upper.gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(upper, lower, joint);
            link.SetBeadWeldRatio(0.1f);
            Assert.IsTrue(CollectNow());

            var feature = Keep(ScriptableObject.CreateInstance<PartOutlineRendererFeature>());
            var mask = Keep(new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/_Project/Shaders/Rendering/PartOutlineMask.shader")));
            feature.MaskMaterial = mask;
            feature.OutlineMaterial = Keep(new Material(AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/_Project/Shaders/Rendering/PartOutline.shader")));
            var pass = (ScriptableRenderPass)typeof(PartOutlineRendererFeature)
                .GetField("pass", PrivateInstance).GetValue(feature);
            pass.GetType().GetMethod("Setup").Invoke(pass, new object[] { mask, feature.OutlineMaterial });

            using var commands = new CommandBuffer();
            using var frame = new ContextContainer();
            var graph = new RenderGraph("Part outline regression");
            try
            {
                graph.BeginRecording(new RenderGraphParameters { commandBuffer = commands });
                var resources = frame.Create<UniversalResourceData>();
                // URP normally opens resource access when it starts a camera frame.
                typeof(UniversalResourceDataBase).GetMethod("InitFrame", PrivateInstance)
                    .Invoke(resources, null);
                resources.cameraColor = graph.CreateTexture(new TextureDesc(64, 64)
                    { colorFormat = GraphicsFormat.R8G8B8A8_UNorm });
                resources.cameraDepth = graph.CreateTexture(new TextureDesc(64, 64)
                    { depthBufferBits = DepthBits.Depth24 });
                pass.RecordRenderGraph(graph, frame);
                var recorded = (IList)typeof(RenderGraph).GetField("m_RenderPasses", PrivateInstance)
                    .GetValue(graph);
                Assert.AreEqual(2, recorded.Count);
                // Use Unity's real command validation against the production-recorded pass.
                var raster = (RasterCommandBuffer)Activator.CreateInstance(typeof(RasterCommandBuffer),
                    PrivateInstance, null, new[] { commands, recorded[0], (object)false }, null);
                var draw = pass.GetType().GetMethod("DrawMask", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.DoesNotThrow(() => draw.Invoke(null, new object[] { raster, mask }));
                Assert.Greater(commands.sizeInBytes, 0);
            }
            finally
            {
                // This test records commands without submitting a GPU frame.
                var state = typeof(RenderGraph).GetField("m_RenderGraphState", PrivateInstance);
                state.SetValue(graph, Enum.Parse(state.FieldType, "Idle"));
                graph.Cleanup();
            }
        }

        [Test]
        public void RemoteExistingBeadCanActivateOutlineWhenAnotherPartMovesIntoContact()
        {
            WorldEvents.RemoteDecides = true;
            var lower = Part(0, Vector3.zero);
            lower.ApplyDockedToPad(true);
            var upper = Part(1, Vector3.up * 2f);
            Physics.SyncTransforms();
            var bead = WeldBead.Attach(lower);
            var snapshot = new[] { new WeldBead.ChainState
            {
                Origin = Vector3.up * 0.5f, Direction = Vector3.up,
                BlobCount = 1, TipRadius = PhysicsScale.WeldBeadMinRadius
            } };
            bead.RestoreChains(snapshot, 0, 1);
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper));
            Assert.IsFalse(CollectNow());

            upper.transform.position = Vector3.up;
            Physics.SyncTransforms();
            // WorldSync repeats this snapshot even without a new welding input.
            bead.RestoreChains(snapshot, 0, 1);
            Assert.IsTrue(PartOutlineGroups.IsWeldJoined(PartAssemblyLink.FindBetween(lower, upper)));
            Assert.IsTrue(CollectNow());
        }

        SpaceshipPart Part(int index, Vector3 position)
        {
            var go = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.name = PartSpec.All[index].Name;
            go.transform.position = position;
            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            part.RefreshRepresentation();
            return part;
        }

        static bool CollectNow() => (bool)typeof(PartOutlineTargets)
            .GetMethod("CollectNow", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        T Keep<T>(T value) where T : Object { spawned.Add(value); return value; }
    }
}
