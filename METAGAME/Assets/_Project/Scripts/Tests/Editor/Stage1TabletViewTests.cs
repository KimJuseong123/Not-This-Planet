using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage1TabletViewTests
    {
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
    }
}
