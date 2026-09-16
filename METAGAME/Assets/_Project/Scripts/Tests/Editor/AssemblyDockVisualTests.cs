using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class AssemblyDockVisualTests
    {
        [Test]
        public void AuthoredJawsMoveAndStatusChangesWithoutReplacingHullMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Metagame.EditorTools.Stage1MainLaunchpadIntegration.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            try
            {
                var visual = instance.GetComponent<AssemblyDockVisual>();
                var data = new SerializedObject(visual);
                var jaws = data.FindProperty("_jaws");
                Assert.That(jaws.arraySize, Is.EqualTo(4));
                visual.ShowDocked(false, 0);
                for (int i = 0; i < jaws.arraySize; i++)
                {
                    var jaw = (Transform)jaws.GetArrayElementAtIndex(i).objectReferenceValue;
                    var r = jaw.GetComponent<Renderer>();
                    var body = r.sharedMaterials[0];
                    var start = jaw.localPosition;
                    visual.ShowDocked(true, 1);
                    Assert.That(Vector3.Distance(jaw.localPosition, data.FindProperty("_closedPositions").GetArrayElementAtIndex(i).vector3Value), Is.LessThan(.001f));
                    Assert.That(Vector3.Distance(start, jaw.localPosition), Is.GreaterThan(.24f));
                    Assert.That(r.sharedMaterials[0], Is.SameAs(body));
                    Assert.That(r.sharedMaterials[1], Is.SameAs(data.FindProperty("_lockedMaterial").objectReferenceValue));
                    visual.ShowDocked(false, 1);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void PrefabProvidesFlatEngineSupportAndClearRocketColumn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Metagame.EditorTools.Stage1MainLaunchpadIntegration.PrefabPath);
            var instance = Object.Instantiate(prefab, new Vector3(1000, 100, 1000), Quaternion.Euler(0, 45, 0));
            try
            {
                UnityEngine.Physics.SyncTransforms();
                var colliders = instance.GetComponentsInChildren<Collider>();
                Assert.That(colliders.Length, Is.EqualTo(8));
                foreach (float x in new[] { -.8f, 0f, .8f }) foreach (float z in new[] { -.8f, 0f, .8f })
                {
                    var ray = new Ray(instance.transform.position + new Vector3(x, 10, z), Vector3.down);
                    bool supported = false;
                    foreach (var c in colliders)
                    {
                        Assert.That(c.isTrigger, Is.False);
                        Assert.That(c.attachedRigidbody, Is.Null);
                        if (!c.Raycast(ray, out var hit, 11)) continue;
                        Assert.That(hit.point.y, Is.EqualTo(100).Within(.005f), c.name);
                        supported = true;
                    }
                    Assert.That(supported, Is.True);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
