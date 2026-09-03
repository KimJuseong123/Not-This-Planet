using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class MeshVisualTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        [Test]
        public void EnsureRenderer_AddsRendererToAUsableMeshFilter()
        {
            _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var filter = _root.GetComponent<MeshFilter>();
            Object.DestroyImmediate(_root.GetComponent<MeshRenderer>());

            var renderer = MeshVisual.EnsureRenderer(_root);

            Assert.IsNotNull(filter.sharedMesh);
            Assert.IsNotNull(renderer);
            Assert.AreSame(renderer, _root.GetComponent<MeshRenderer>());
        }

        [Test]
        public void EnsureRenderers_RepairsNestedMeshVisuals()
        {
            _root = new GameObject("Mesh Visual Root");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(_root.transform, false);
            Object.DestroyImmediate(child.GetComponent<MeshRenderer>());

            var added = MeshVisual.EnsureRenderers(_root);

            Assert.AreEqual(1, added);
            Assert.IsNotNull(child.GetComponent<MeshRenderer>());
        }

        [Test]
        public void EnsureRenderer_DoesNotInventRendererWithoutMesh()
        {
            _root = new GameObject("Empty Mesh Holder");
            _root.AddComponent<MeshFilter>();

            var renderer = MeshVisual.EnsureRenderer(_root);

            Assert.IsNull(renderer);
            Assert.IsNull(_root.GetComponent<MeshRenderer>());
        }
    }
}
