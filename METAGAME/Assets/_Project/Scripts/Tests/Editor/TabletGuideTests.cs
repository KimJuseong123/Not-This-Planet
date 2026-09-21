using Metagame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Metagame.Gameplay.Tests
{
    public sealed class TabletGuideTests
    {
        [Test]
        public void NewPlayerGetsGuideOnlyWhenTabletOpens()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            Assert.IsFalse(guide.Visible);
            guide.OnTabletOpened();
            Assert.IsTrue(guide.Visible);
            Assert.AreEqual(0, guide.Page);
            Assert.IsTrue(guide.CanConfirm);
        }

        [Test]
        public void ConfirmFromFirstPageReturnsImmediatelyAndFiresOnce()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.OnTabletOpened();
            int confirmed = 0;
            guide.Confirmed += () => confirmed++;
            Assert.IsTrue(guide.Confirm());
            Assert.IsTrue(guide.Completed);
            Assert.IsFalse(guide.Visible);
            Assert.IsFalse(guide.Confirm());
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void ReopeningUnfinishedGuideKeepsItsPage()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.OnTabletOpened();
            guide.Move(1);
            guide.OnTabletOpened();
            Assert.AreEqual(1, guide.Page);
        }

        [Test]
        public void NavigationClampsBackwardsAndLeavesForwards()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.OnTabletOpened();

            // 첫 쪽에서 더 앞으로는 못 간다 — 안내 앞에는 아무것도 없다.
            Assert.IsFalse(guide.Move(-1));
            Assert.AreEqual(0, guide.Page);

            // 마지막 쪽까지는 그냥 넘어간다.
            for (int i = 0; i < TabletGuideState.PageCount - 1; i++) Assert.IsFalse(guide.Move(1));
            Assert.AreEqual(TabletGuideState.PageCount - 1, guide.Page);

            // **여기서 한 번 더 넘기면 안내를 벗어난다** (S15P21B202-657).
            // 예전에는 마지막 쪽에 붙잡혀 있었고, 확인 키를 모르는 사람은 진행도를
            // 영영 못 봤다 — 처음 하는 사람이 실제로 그랬다.
            Assert.IsTrue(guide.Move(1));
            Assert.IsFalse(guide.Visible);
            Assert.IsTrue(guide.Completed);
        }

        [Test]
        public void ReturningPlayerCanManuallyReopenGuide()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.OnTabletOpened();
            Assert.IsTrue(guide.Confirm());
            guide.OnTabletOpened();
            Assert.IsFalse(guide.Visible);
            guide.Open();
            Assert.IsTrue(guide.Visible);
            Assert.AreEqual(0, guide.Page);
        }

        [Test]
        public void ResetRunRestoresFirstOpenGuideEvenAfterConfirmation()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.Open();
            guide.Move(1);
            Assert.IsTrue(guide.Confirm());
            int version = guide.RunVersion;
            guide.ResetRun();
            Assert.IsFalse(guide.Completed);
            Assert.IsFalse(guide.Visible);
            Assert.AreEqual(0, guide.Page);
            Assert.Greater(guide.RunVersion, version);
            guide.OnTabletOpened();
            Assert.IsTrue(guide.Visible);
        }

        [Test]
        public void SeparatePlayersDoNotShareRuntimeProgress()
        {
            var first = new TabletGuideState();
            var second = new TabletGuideState();
            first.Enable();
            second.Enable();
            first.Open();
            for (int i = 1; i < TabletGuideState.PageCount; i++) first.Move(1);
            Assert.IsTrue(first.Confirm());
            Assert.IsFalse(second.Completed);
            Assert.IsFalse(second.Visible);
        }

        [Test]
        public void ExplorationReturnsToProgressAndKeepsTabletOpen()
        {
            var go = new GameObject("GuideStateTest");
            try
            {
                var state = go.AddComponent<Stage1Exploration>();
                state.Configure(null);
                state.NextTabletPage(); // 기존 도움말 화면에 있다가 가이드를 열어도 복귀는 진행도.
                state.Guide.Enable();
                state.SetTabletOpen(true);
                state.ActivateTabletGuide(); // 첫 그림에서도 E로 진행상황판 진입.
                Assert.IsTrue(state.TabletOpen);
                Assert.IsTrue(state.Guide.Completed);
                Assert.AreEqual(0, state.TabletPage);
                state.NextTabletPage();
                Assert.AreEqual(1, state.TabletPage);
                state.SetTabletOpen(false);
                state.SetTabletOpen(true);
                Assert.IsFalse(state.Guide.Visible);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void AllIllustrationsArePackagedResources(int page)
        {
            var art = TabletGuideContent.Illustration(page);
            Assert.IsNotNull(art);
            Assert.GreaterOrEqual(art.width, 1000);
            Assert.IsNotEmpty(TabletGuideContent.Title(page));
            Assert.IsNotEmpty(TabletGuideContent.Caption(page));
        }

        [Test]
        public void HudIgnoresLegacyCompletionPreferenceAndPreservesIt()
        {
            const string legacyKey = "NotThisPlanet.TabletGuide.v1.Completed";
            bool existed = PlayerPrefs.HasKey(legacyKey);
            int previous = PlayerPrefs.GetInt(legacyKey, 0);
            var go = new GameObject("GuideLegacyPreferenceTest");
            try
            {
                PlayerPrefs.SetInt(legacyKey, 1);
                var state = go.AddComponent<Stage1Exploration>();
                state.Configure(null);
                var hud = go.AddComponent<Stage1ExplorationHud>();
                hud.Initialize(state, null);
                Assert.IsFalse(state.Guide.Completed);
                state.SetTabletOpen(true);
                Assert.IsTrue(state.Guide.Visible);
                state.ActivateTabletGuide();
                Assert.IsTrue(state.Guide.Completed);
                hud.Initialize(state, null); // HUD 재연결은 새 게임이 아니다.
                Assert.IsTrue(state.Guide.Completed);
                state.ResetRun();
                Assert.IsFalse(state.Guide.Completed);
                state.SetTabletOpen(true);
                Assert.IsTrue(state.Guide.Visible);
                Assert.AreEqual(1, PlayerPrefs.GetInt(legacyKey));
            }
            finally
            {
                if (existed) PlayerPrefs.SetInt(legacyKey, previous);
                else PlayerPrefs.DeleteKey(legacyKey);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnchorPinPagePrecedesToolsAndUsesDistinctArt()
        {
            Assert.AreEqual(4, TabletGuideState.PageCount);
            StringAssert.Contains("고정못", TabletGuideContent.Title(1));
            StringAssert.Contains("클릭 유지", TabletGuideContent.Caption(1));
            StringAssert.Contains("모두", TabletGuideContent.Caption(1));
            Assert.AreSame(Resources.Load<Texture2D>("TabletGuide/Page4"), TabletGuideContent.Illustration(1));
            Assert.AreSame(Resources.Load<Texture2D>("TabletGuide/Page2"), TabletGuideContent.Illustration(2));
            Assert.AreSame(Resources.Load<Texture2D>("TabletGuide/Page3"), TabletGuideContent.Illustration(3));
        }

        [Test]
        public void ProductionTabletShowsGuideThenRestoresExistingTower()
        {
            var cameraObject = new GameObject("GuideCamera");
            var stateObject = new GameObject("GuideState");
            Stage1TabletView view = null;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var state = stateObject.AddComponent<Stage1Exploration>();
                state.Configure(null);
                state.Guide.Enable();
                state.SetTabletOpen(true);
                view = Stage1TabletView.Create(camera, state);
                Assert.IsNotNull(view);
                var screen = view.GetComponent<Stage1TabletScreen>();
                screen.Tick();
                var art = view.GetComponentInChildren<RawImage>();
                Assert.IsNotNull(art);
                Assert.AreSame(TabletGuideContent.Illustration(0), art.texture);
                CapturePreviewIfRequested(camera, "guide-1");
                state.NextTabletPage();
                screen.Tick();
                CapturePreviewIfRequested(camera, "guide-2");
                state.NextTabletPage();
                screen.Tick();
                Assert.AreSame(TabletGuideContent.Illustration(2), art.texture);
                CapturePreviewIfRequested(camera, "guide-3");
                state.NextTabletPage();
                screen.Tick();
                Assert.AreSame(TabletGuideContent.Illustration(3), art.texture);
                CapturePreviewIfRequested(camera, "guide-4");
                state.ActivateTabletGuide();
                screen.Tick();
                Assert.IsFalse(art.gameObject.activeInHierarchy);
                var silhouette = view.GetComponentInChildren<PartSilhouetteGraphic>();
                Assert.IsNotNull(silhouette);
                CapturePreviewIfRequested(camera, "progress-restored");
            }
            finally
            {
                if (view != null)
                {
                    var canvas = view.GetComponentInChildren<Canvas>(true);
                    if (canvas != null) Object.DestroyImmediate(canvas.gameObject);
                    Object.DestroyImmediate(view.gameObject);
                }
                Object.DestroyImmediate(stateObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        static void CapturePreviewIfRequested(Camera camera, string name)
        {
            // 선택적 로컬 시각 검증 산출물. 일반 테스트/CI에는 파일을 만들지 않는다.
            var args = System.Environment.GetCommandLineArgs();
            int flag = System.Array.IndexOf(args, "-tabletGuidePreviewDir");
            if (flag < 0 || flag + 1 >= args.Length) return;
            string directory = System.IO.Path.GetFullPath(args[flag + 1]);
            System.IO.Directory.CreateDirectory(directory);
            var target = new RenderTexture(1600, 900, 24);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            var oldTarget = camera.targetTexture;
            try
            {
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.01f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.015f, 0.025f, 0.05f);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = active;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
        }
    }
}
