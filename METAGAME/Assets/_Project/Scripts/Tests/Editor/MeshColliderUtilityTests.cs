using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class MeshColliderUtilityTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        [Test]
        public void EnsureMeshColliders_ConnectsTheMeshUsedByAStaticVisual()
        {
            _root = new GameObject("Static Visual");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(_root.transform, false);
            var mesh = child.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(child.GetComponent<Collider>());

            var added = MeshColliderUtility.EnsureMeshColliders(_root, false);
            var collider = child.GetComponent<MeshCollider>();

            Assert.AreEqual(1, added);
            Assert.IsNotNull(collider);
            Assert.AreSame(mesh, collider.sharedMesh);
            Assert.IsFalse(collider.convex);
        }

        [Test]
        public void EnsureMeshColliders_PreservesAnExistingSimpleCollider()
        {
            _root = new GameObject("Authored Collider Visual");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(_root.transform, false);

            var added = MeshColliderUtility.EnsureMeshColliders(_root, false);

            Assert.AreEqual(0, added);
            Assert.IsNull(child.GetComponent<MeshCollider>());
            Assert.IsNotNull(child.GetComponent<BoxCollider>());
        }

        [Test]
        public void EnsureMeshColliders_SkipsNonConvexGeometryBelowARigidbody()
        {
            _root = new GameObject("Dynamic Visual");
            _root.AddComponent<Rigidbody>();
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(_root.transform, false);
            Object.DestroyImmediate(child.GetComponent<Collider>());

            var added = MeshColliderUtility.EnsureMeshColliders(_root, false);

            Assert.AreEqual(0, added);
            Assert.IsNull(child.GetComponent<MeshCollider>());
        }

        [Test]
        public void EnsureMeshColliders_CanExplicitlyCreateAConvexDynamicCollider()
        {
            _root = new GameObject("Opted In Dynamic Visual");
            _root.AddComponent<Rigidbody>();
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(_root.transform, false);
            Object.DestroyImmediate(child.GetComponent<Collider>());

            var added = MeshColliderUtility.EnsureMeshColliders(_root, true);
            var collider = child.GetComponent<MeshCollider>();

            Assert.AreEqual(1, added);
            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.convex);
        }

        [Test]
        public void EnsureStage1EnvironmentColliders_RepairsAPlacedStaticInstance()
        {
            _root = new GameObject("Environment");
            var group = new GameObject("Cliffs");
            group.transform.SetParent(_root.transform, false);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(group.transform, false);
            Object.DestroyImmediate(child.GetComponent<Collider>());

            MeshColliderUtility.EnsureStage1EnvironmentColliders();

            Assert.IsNotNull(child.GetComponent<MeshCollider>());
            Assert.IsFalse(child.GetComponent<MeshCollider>().convex);
        }
    }
}
