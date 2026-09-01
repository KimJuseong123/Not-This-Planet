using Metagame.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage1TabletViewTests
    {
        const string PrefabPath =
            "Assets/_Project/Prefabs/Resources/PF_Stage1_Tablet.prefab";

        [Test]
        public void ProductionPrefab_UsesApprovedPoseAndScreenAlignment()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var view = prefab.GetComponent<Stage1TabletView>();
            Assert.IsNotNull(view);
            AssertVector(view.OpenLocalPosition, Stage1TabletView.ApprovedOpenLocalPosition);
            AssertVector(view.OpenLocalEuler, Stage1TabletView.ApprovedOpenLocalEuler);
            AssertVector(view.ScreenSize, Stage1TabletView.ApprovedScreenSize);

            var screenAnchor = prefab.transform.Find("ScreenAnchor");
            Assert.IsNotNull(screenAnchor);
            AssertVector(screenAnchor.localPosition,
                Stage1TabletView.ApprovedScreenAnchorLocalPosition);
            Assert.Less(Quaternion.Angle(screenAnchor.localRotation,
                Quaternion.Euler(Stage1TabletView.ApprovedScreenAnchorLocalEuler)), 0.01f);

            var screen = screenAnchor.Find("DynamicScreenSurface");
            Assert.IsNotNull(screen);
            AssertVector(new Vector2(screen.localScale.x, screen.localScale.y),
                Stage1TabletView.ApprovedScreenSize);
        }

        [Test]
        public void OpenTablet_ProjectsModelDisplayIntoGuiRect()
        {
            var cameraObject = new GameObject("Camera");
            var stateObject = new GameObject("Exploration");
            var tabletObject = new GameObject("Tablet");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 60f;

                tabletObject.transform.SetParent(cameraObject.transform, false);
                tabletObject.transform.localPosition = new Vector3(0f, 0f, 1f);
                var screen = new GameObject("ScreenAnchor").transform;
                screen.SetParent(tabletObject.transform, false);

                var exploration = stateObject.AddComponent<Stage1Exploration>();
                exploration.Configure(null);
                exploration.SetTabletOpen(true);

                var view = tabletObject.AddComponent<Stage1TabletView>();
                view.ConfigurePrefab(screen, new Vector2(0.4f, 0.2f));
                view.Initialize(camera, exploration);

                Assert.IsTrue(view.TryGetGuiScreenRect(out var rect));
                Assert.Greater(rect.width, 32f);
                Assert.Greater(rect.height, 24f);

                exploration.SetTabletOpen(false);
                Assert.IsFalse(view.TryGetGuiScreenRect(out _));
            }
            finally
            {
                Object.DestroyImmediate(tabletObject);
                Object.DestroyImmediate(stateObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.Less((actual - expected).sqrMagnitude, 0.000001f,
                $"expected={expected}, actual={actual}");
        }

        static void AssertVector(Vector2 actual, Vector2 expected)
        {
            Assert.Less((actual - expected).sqrMagnitude, 0.000001f,
                $"expected={expected}, actual={actual}");
        }
    }
}
