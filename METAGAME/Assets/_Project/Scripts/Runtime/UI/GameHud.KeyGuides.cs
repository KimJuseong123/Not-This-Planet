using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    public partial class GameHud
    {
        public KeyGuideHolder KeyGuides { get; } = new KeyGuideHolder();
        readonly GUIContent _keyGuideContent = new GUIContent();
        int _keyGuideRevision = -1;

        void InitializeKeyGuides()
        {
            KeyGuides.Register("tablet", "태블릿", () => Controls.Tablet, () =>
                IsKeyGuideOwner && GameSession.Instance.CanToggleTablet
                && (GameSession.Instance.Exploration.Guide.Completed
                    || !GameSession.Instance.Exploration.Guide.Enabled
                    || GameSession.Instance.Exploration.TabletOpen));
            KeyGuides.Register("throw", "던지기", () => Controls.ToolAction, CanShowThrowGuide);
        }

        bool IsKeyGuideOwner => Local != null && GameSession.Instance != null
            && GameSession.Instance.LocalPlayer == Local;

        public bool CanShowThrowGuide()
        {
            if (!IsKeyGuideOwner) return false;
            var session = GameSession.Instance;
            if (session.InputSuppressed || session.ScriptedInput || session.EndingActive
                || !session.CursorLocked || Local.Downed || Local.IsMounted || Local.IsRiding
                || (session.Exploration != null && session.Exploration.TabletOpen)) return false;
            // 참가자의 서버 동기화 도구는 물리 HeldTool 대신 PresentationTool에 연결된다.
            return Local.HeldTool != null
                || (Local.PresentationTool != null && Local.PresentationTool.IsHeldForView);
        }

        void DrawKeyGuides(float width, float height)
        {
            if (!IsKeyGuideOwner || GameSession.Instance.InputSuppressed
                || GameSession.Instance.ScriptedInput) return;
            int previousDepth = GUI.depth;
            GUI.depth = -90; // 태블릿이 열려 있어도 닫기 키는 보인다.
            const float margin = 18f, gap = 12f, rowHeight = 46f;
            float x = margin, y = height - margin - rowHeight;
            for (int i = 0; i < KeyGuides.Entries.Count; i++)
            {
                var entry = KeyGuides.Entries[i];
                if (!entry.Visible) continue;
                _keyGuideContent.text = entry.KeyText;
                float keyWidth = Mathf.Max(48f, center.CalcSize(_keyGuideContent).x + 24f);
                _keyGuideContent.text = entry.Label;
                float labelWidth = small.CalcSize(_keyGuideContent).x;
                float itemWidth = keyWidth + labelWidth + 26f;
                if (x > margin && x + itemWidth > width - margin)
                {
                    x = margin;
                    y -= rowHeight + gap;
                }
                UiTheme.DrawPanel(new Rect(x, y, itemWidth, rowHeight), true, false);
                UiTheme.DrawKeyCap(new Rect(x + 5f, y + 5f, keyWidth, rowHeight - 10f));
                L(new Rect(x + 5f, y + 5f, keyWidth, rowHeight - 10f), entry.KeyText, center, UiTheme.CyanSoft);
                L(new Rect(x + keyWidth + 14f, y + 13f, labelWidth, 24f), entry.Label, small, UiTheme.Text);
                x += itemWidth + gap;
            }
            GUI.depth = previousDepth;
        }
    }
}
