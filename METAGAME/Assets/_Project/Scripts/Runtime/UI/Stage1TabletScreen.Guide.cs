using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Metagame.UI
{
    public sealed partial class Stage1TabletScreen
    {
        GameObject guideRoot;
        RawImage guideArt;
        Text guideCaption;
        Text guideAction;
        Image guideActionBackground;
        Text guideKeys;
        Camera guideCamera;
        int guideRenderedPage = -1;
        RectTransform pressedGuideControl;

        void CreateGuidePage(Transform parent)
        {
            guideCamera = GetComponentInParent<Camera>();
            guideRoot = Panel(parent, "FirstUseGuide", new Color(0.035f, 0.085f, 0.115f),
                new Vector2(0.065f, 0.13f), new Vector2(0.935f, 0.84f), Vector2.zero, Vector2.zero);
            var artFrame = Panel(guideRoot.transform, "IllustrationFrame", Color.clear,
                new Vector2(0, 0.19f), Vector2.one, Vector2.zero, Vector2.zero);
            var artObject = new GameObject("Illustration", typeof(RectTransform));
            artObject.transform.SetParent(artFrame.transform, false);
            guideArt = artObject.AddComponent<RawImage>();
            guideArt.raycastTarget = false;
            var fit = artObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 3f;
            guideCaption = Label(guideRoot.transform, "Caption", 26, TextAnchor.MiddleCenter,
                new Color(0.80f, 0.96f, 1f), Vector2.zero, new Vector2(1, 0.20f), Vector2.zero, Vector2.zero);
            guideCaption.fontStyle = FontStyle.Bold;
            guideActionBackground = Panel(parent, "GuideActionBackground", new Color(0.10f, 0.25f, 0.30f),
                new Vector2(0.65f, 0.018f), new Vector2(0.98f, 0.11f), Vector2.zero, Vector2.zero).GetComponent<Image>();
            guideAction = Label(parent, "GuideAction", 23, TextAnchor.MiddleCenter,
                new Color(0.65f, 0.96f, 1f), new Vector2(0.65f, 0.018f), new Vector2(0.98f, 0.11f), Vector2.zero, Vector2.zero);
            guideAction.fontStyle = FontStyle.Bold;
            // 네모 점 줄이 쓰던 자리(0.44~0.53)까지 넓힌다 — 그것을 지웠으므로
            // 이제 쪽 표시와 키 안내가 한 줄에 들어간다 (S15P21B202-679).
            guideKeys = Label(parent, "GuideKeys", 21, TextAnchor.MiddleLeft,
                new Color(0.48f, 0.85f, 0.94f), new Vector2(0.055f, 0.018f), new Vector2(0.62f, 0.11f), Vector2.zero, Vector2.zero);
            guideKeys.fontStyle = FontStyle.Bold;
        }

        bool RefreshGuidePage()
        {
            if (guideRoot == null) return false;
            var guide = exploration.Guide;
            guideRoot.SetActive(guide.Visible);
            guideAction.gameObject.SetActive(guide.Enabled);
            guideActionBackground.gameObject.SetActive(guide.Enabled && (!guide.Visible || guide.CanConfirm));
            guideKeys.gameObject.SetActive(guide.Enabled);
            // **쪽 표시는 지금 있는 자리를 가리켜야 한다** (S15P21B202-679).
            // 어느 칸에 불이 들어오는지는 TabletHelpContent 가 정한다.
            guideKeys.text = TabletHelpContent.PageStrip(
                guide.Visible, guide.Page, exploration.TabletPage);
            // **관문이 아니라 지름길이다** (S15P21B202-657). 넘기다 보면 저절로
            // 빠져나오므로 "확인" 이라는 말을 쓰지 않는다 — 눌러야 열리는 줄 알았다.
            guideAction.text = TabletHelpContent.Cap(Controls.TransportAction)
                + (guide.Visible ? "  진행도로 건너뛰기" : "  사용 가이드");
            if (!guide.Visible)
            {
                if (guideRenderedPage >= 0) { lastTitle = null; guideRenderedPage = -1; }
                return false;
            }
            partsRoot.SetActive(false);
            helpRoot.SetActive(false);
            body.gameObject.SetActive(false);
            discoverySummary.gameObject.SetActive(false);
            foreach (var dot in discoveryDots) dot.gameObject.SetActive(false);
            if (guideRenderedPage != guide.Page)
            {
                guideRenderedPage = guide.Page;
                title.text = TabletGuideContent.Title(guide.Page);
                guideCaption.text = TabletGuideContent.Caption(guide.Page);
                guideArt.texture = TabletGuideContent.Illustration(guide.Page);
                guideArt.enabled = guideArt.texture != null;
            }
            RefreshNavigation(guide.Page, TabletGuideState.PageCount);
            return true;
        }

        // 기존 태블릿은 EventSystem 없이 동작한다. 화면 면의 실제 Rect에만 클릭을 받는다.
        // 새 전역 EventSystem을 설치하거나 게임의 입력/커서 소유권을 바꾸지 않는다.
        void HandleGuidePointer()
        {
            var session = GameSession.Instance;
            if (exploration == null || !exploration.TabletOpen || !exploration.Guide.Enabled
                || guideCamera == null || session == null || session.Exploration != exploration
                || session.InputSuppressed || session.ScriptedInput || session.EndingActive)
            { pressedGuideControl = null; return; }
            if (Input.GetMouseButtonDown(0))
            {
                pressedGuideControl = UnderPointer(guideAction.rectTransform) ? guideAction.rectTransform
                    : UnderPointer(previousArrow.rectTransform) ? previousArrow.rectTransform
                    : UnderPointer(nextArrow.rectTransform) ? nextArrow.rectTransform : null;
            }
            if (!Input.GetMouseButtonUp(0)) return;
            var control = pressedGuideControl;
            pressedGuideControl = null;
            if (control == null || !UnderPointer(control)) return;
            if (control == guideAction.rectTransform)
            {
                if (exploration.Guide.Visible && !exploration.Guide.CanConfirm) return;
                exploration.ActivateTabletGuide();
            }
            else if (control == previousArrow.rectTransform) exploration.PreviousTabletPage();
            else exploration.NextTabletPage();
            LocalCursorFeedback.NotifyPress();
        }

        bool UnderPointer(RectTransform rect) => rect != null && rect.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, guideCamera);
    }
}
