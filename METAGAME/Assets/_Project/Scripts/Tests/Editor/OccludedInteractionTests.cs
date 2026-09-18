using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Metagame.Gameplay.Tests
{
    public sealed class OccludedInteractionTests
    {
        Scene _scene;
        readonly List<Object> _objects = new();
        [SetUp] public void SetUp() { _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); _scene.name = "Stage1_Astra"; }
        [TearDown] public void TearDown()
        {
            foreach (var item in _objects) if (item != null) Object.DestroyImmediate(item);
            _objects.Clear();
            EditorSceneManager.CloseScene(_scene, true);
        }

        GameObject Cube(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, _scene);
            _objects.Add(go);
            go.transform.position = position;
            go.AddComponent<Grabbable>();
            return go;
        }

        Camera Camera()
        {
            var go = new GameObject("Outline test camera");
            _objects.Add(go);
            return go.AddComponent<Camera>();
        }

        [TestCase(0, 0, 5, true)]
        [TestCase(100, 0, 5, false)]
        [TestCase(0, 0, -5, false)]
        public void CollectRespectsCameraFrustum(float x, float y, float z, bool expected)
        {
            Cube(new Vector3(x, y, z)).AddComponent<MeteorBat>();
            Assert.AreEqual(expected, new OccludedInteractionTargets().Collect(Camera()));
        }

        [Test] public void CameraLayerMaskAndHiddenRendererAreRespected()
        {
            var go = Cube(Vector3.forward * 5);
            go.AddComponent<MeteorBat>();
            var camera = Camera();
            camera.cullingMask = 0;
            Assert.IsFalse(new OccludedInteractionTargets().Collect(camera));
            camera.cullingMask = -1;
            go.GetComponent<Renderer>().enabled = false;
            Assert.IsFalse(new OccludedInteractionTargets().Collect(camera));
        }

        [Test] public void DisabledRemoteToolIsVisibleButHeldToolIsExcluded()
        {
            var go = Cube(Vector3.forward * 5);
            var tool = go.AddComponent<MeteorBat>();
            tool.enabled = false;
            Assert.IsTrue(OccludedInteractionTargets.IsEligible(tool));
            tool.ApplyRemoteHeld(true);
            Assert.IsFalse(OccludedInteractionTargets.IsEligible(tool));
            Assert.IsFalse(OccludedInteractionTargets.IsEligible(go.GetComponent<Grabbable>()));
            tool.ApplyRemoteHeld(false);
            Assert.IsTrue(OccludedInteractionTargets.IsEligible(tool));
        }

        [Test] public void DuplicateInteractionComponentsDrawMeshOnlyOnce()
        {
            var go = Cube(Vector3.forward * 5);
            go.AddComponent<MeteorBat>();
            var targets = new OccludedInteractionTargets();
            Assert.IsTrue(targets.Collect(Camera()));
            Assert.AreEqual(1, targets.Targets.Count);
        }


        [TestCase("Part_1", true)]
        [TestCase("Part_2", true)]
        [TestCase("Part_3", true)]
        [TestCase("Part_4", true)]
        [TestCase("Part_5", false)]
        [TestCase("Environment", false)]
        public void OnlyCorePartsAreCollected(string name, bool expected)
        {
            var go = Cube(Vector3.forward * 5);
            go.name = name;
            go.AddComponent<SpaceshipPart>();
            Assert.AreEqual(expected, new OccludedInteractionTargets().Collect(Camera()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GrabbablesAndClampsAreExcluded(bool clamp)
        {
            var go = Cube(Vector3.forward * 5);
            if (clamp) go.AddComponent<PartClamp>();
            Assert.IsFalse(new OccludedInteractionTargets().Collect(Camera()));
        }

        [Test] public void ClampUnderCorePartIsExcluded()
        {
            var part = Cube(Vector3.forward * 5);
            part.name = "Part_1";
            part.AddComponent<SpaceshipPart>();
            var clamp = Cube(Vector3.forward * 5);
            clamp.AddComponent<PartClamp>();
            clamp.transform.SetParent(part.transform);
            var targets = new OccludedInteractionTargets();
            Assert.IsTrue(targets.Collect(Camera()));
            Assert.AreEqual(1, targets.Targets.Count);
            Assert.AreSame(part.GetComponent<Renderer>(), targets.Targets[0].Renderer);
        }

        [Test] public void WeldingToolIsCollected()
        {
            Cube(Vector3.forward * 5).AddComponent<PlasmaWeldingTool>();
            Assert.IsTrue(new OccludedInteractionTargets().Collect(Camera()));
        }

        [Category("Heavy")]
        [Test]
        public void AuthoredStageOutlinesOnlyToolsAndCoreParts()
        {
            _scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity", OpenSceneMode.Single);
            var tools = Object.FindObjectsByType<ToolBase>(FindObjectsSortMode.None);
            var parts = Object.FindObjectsByType<Metagame.Gameplay.Parts.SpaceshipPart>(FindObjectsSortMode.None);
            var clamps = Object.FindObjectsByType<Metagame.Gameplay.Parts.PartClamp>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(tools.Length, 2);
            Assert.GreaterOrEqual(parts.Length, 4);
            Assert.GreaterOrEqual(clamps.Length, 16);
            var camera = Camera();
            camera.farClipPlane = 2000;
            var roots = new List<Component>();
            roots.AddRange(tools);
            roots.AddRange(parts);
            roots.AddRange(clamps);
            foreach (var root in roots)
            {
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.IsNotEmpty(renderers, root.name);
                var renderer = System.Linq.Enumerable.FirstOrDefault(renderers,
                    item => item.enabled && item.gameObject.activeInHierarchy && !item.forceRenderingOff &&
                            (root is PartClamp || item.GetComponentInParent<PartClamp>() == null) &&
                            item.GetComponent<MeshFilter>() != null &&
                            item.GetComponent<MeshFilter>().sharedMesh != null);
                Assert.NotNull(renderer, root.name + " must have an active visual mesh.");
                camera.transform.position = renderer.bounds.center - Vector3.forward *
                    Mathf.Max(3, renderer.bounds.size.magnitude * 2);
                camera.transform.rotation = Quaternion.identity;
                var targets = new OccludedInteractionTargets();
                targets.Collect(camera);
                Assert.AreEqual(root is MeteorBat || root is PlasmaWeldingTool ||
                    (root is SpaceshipPart part && PartSpec.Find(part.name) != null),
                    System.Linq.Enumerable.Any(targets.Targets,
                    target => target.Renderer == renderer), root.name);
            }
        }

        [TestCase("OccludedInteractionMask")]
        [TestCase("OccludedInteractionOutline")]
        public void ShadersAreIncludedAndCompile(string resource)
        {
            var shader = Resources.Load<Shader>(resource);
            Assert.NotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
        }

        [TestCase(10f, true)]
        [TestCase(5f, false)]
        [TestCase(4f, false)]
        public void OutlineAppearsOnlyBehindSceneDepth(float objectDepth, bool expected)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires GPU shader rendering.");
            var mask = new Texture2D(32, 32, TextureFormat.RGFloat, false, true);
            var sceneDepth = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
            var result = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
            var material = new Material(Resources.Load<Shader>("OccludedInteractionOutline"));
            var rt = new RenderTexture(32, 32, 0);
            _objects.AddRange(new Object[] { mask, sceneDepth, result, material, rt });
            var pixels = new Color[32 * 32];
            for (var y = 8; y < 24; y++)
                for (var x = 8; x < 24; x++) pixels[y * 32 + x] = new Color(objectDepth, 1, 0, 0);
            mask.SetPixels(pixels);
            mask.Apply();
            sceneDepth.SetPixel(0, 0, Color.white);
            sceneDepth.Apply();
            material.SetTexture("_InteractionMask", mask);
            material.SetTexture("_InteractionSceneDepth", sceneDepth);
            material.SetColor("_OutlineColor", Color.yellow);
            material.SetFloat("_OutlineThickness", 2);
            var oldDepth = Shader.GetGlobalVector("_ZBufferParams");
            var oldScreen = Shader.GetGlobalVector("_ScaledScreenParams");
            var oldOrtho = Shader.GetGlobalVector("unity_OrthoParams");
            var previous = RenderTexture.active;
            try
            {
                Shader.SetGlobalVector("_ZBufferParams", new Vector4(0, 0, 0, 0.2f));
                Shader.SetGlobalVector("_ScaledScreenParams", new Vector4(32, 32, 0, 0));
                Shader.SetGlobalVector("unity_OrthoParams", Vector4.zero);
                using (var command = new CommandBuffer())
                {
                    command.SetRenderTarget(rt);
                    command.ClearRenderTarget(false, true, Color.black);
                    command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    Graphics.ExecuteCommandBuffer(command);
                }
                RenderTexture.active = rt;
                result.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                result.Apply();
                Assert.AreEqual(expected, result.GetPixel(8, 16).r > 0.5f);
                Assert.Less(result.GetPixel(16, 16).r, 0.05f, "Do not fill the silhouette.");
                Assert.Less(result.GetPixel(2, 16).r, 0.05f, "Do not draw outside the object.");
            }
            finally
            {
                RenderTexture.active = previous;
                Shader.SetGlobalVector("_ZBufferParams", oldDepth);
                Shader.SetGlobalVector("_ScaledScreenParams", oldScreen);
                Shader.SetGlobalVector("unity_OrthoParams", oldOrtho);
                rt.Release();
            }
        }
    }
}
