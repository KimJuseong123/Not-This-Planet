using System.Collections.Generic;
using System.Reflection;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AssemblyGuidanceBollardTests
    {
        readonly List<Object> _owned = new();

        T Own<T>(T item) where T : Object { _owned.Add(item); return item; }
        Renderer Renderer(string name) => Own(new GameObject(name)).AddComponent<MeshRenderer>();
        Material Material(Shader shader, Color color)
        {
            var material = Own(new Material(shader));
            material.SetColor("_BaseColor", color);
            return material;
        }

        static void Tick(AssemblyGuidanceLight guide)
        {
            // EditMode does not run this MonoBehaviour. SendMessage enters Unity's
            // ShouldRunBehaviour assertion; invoke the managed update as other EditMode tests do.
            var update = typeof(AssemblyGuidanceLight).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null, "Assembly guidance update method must exist.");
            update.Invoke(guide, null);
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count-1; i >= 0; i--) if (_owned[i]) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(-1)]
        public void AssemblyStateChangesKeepBollardTextureAndUseMatchingSignalColor(int required)
        {
            var assembly = Own(new GameObject("Assembly input")).AddComponent<ShipAssembly>();
            // Inject presentation input only; physics/assembly evaluation is covered separately.
            var floors = (ShipAssembly.FloorState[])assembly.Floors;
            for (int i=0;i<floors.Length;i++) floors[i] = ShipAssembly.FloorState.Welded;
            if (required >= 0) floors[required] = ShipAssembly.FloorState.Empty;
            var guide = Own(new GameObject("Guide")).AddComponent<AssemblyGuidanceLight>();
            var floor = Renderer("Original floor strip");
            var bollard = Renderer("Imported bollard");
            var sourceShader = Shader.Find("Universal Render Pipeline/Lit");
            var signalShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/BollardSignal.shader");
            Assert.That(signalShader, Is.Not.Null);
            var texture = Own(new Texture2D(2,2));
            Color[] colors = { Color.yellow, Color.cyan, Color.magenta, Color.green };
            var floorMaterials = new Material[4]; var bollardMaterials = new Material[4];
            for (int i=0;i<4;i++)
            {
                floorMaterials[i] = Material(sourceShader, colors[i]);
                bollardMaterials[i] = Material(signalShader, colors[i]);
                bollardMaterials[i].SetTexture("_BaseMap", texture);
            }
            var floorReady = Material(sourceShader, Color.white);
            var bollardReady = Material(signalShader, Color.white); bollardReady.SetTexture("_BaseMap", texture);
            guide.Configure(assembly, new[] {floor}, floorMaterials, floorReady);
            guide.ConfigureBollards(new[] {bollard}, bollardMaterials, bollardReady);
            Tick(guide);
            Assert.That(floor.sharedMaterial, Is.SameAs(required < 0 ? floorReady : floorMaterials[required]));
            Assert.That(bollard.sharedMaterial, Is.SameAs(required < 0 ? bollardReady : bollardMaterials[required]));
            Assert.That(bollard.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(texture));
            // A regression from any completed/current state must switch the signal back too.
            floors[0] = ShipAssembly.FloorState.WrongPart;
            Tick(guide);
            Assert.That(bollard.sharedMaterial, Is.SameAs(bollardMaterials[0]));
        }

        [Test]
        public void ExistingScenesWithoutBollardsStillUpdateTheirFloorLights()
        {
            var assembly = Own(new GameObject("Assembly")).AddComponent<ShipAssembly>();
            var guide = Own(new GameObject("Guide")).AddComponent<AssemblyGuidanceLight>();
            var lamp = Renderer("Original lamp");
            var material = Material(Shader.Find("Universal Render Pipeline/Lit"), Color.yellow);
            guide.Configure(assembly, new[] {lamp}, new[] {material}, material);
            Assert.DoesNotThrow(() => Tick(guide));
            Assert.That(lamp.sharedMaterial, Is.SameAs(material));
        }
    }
}
