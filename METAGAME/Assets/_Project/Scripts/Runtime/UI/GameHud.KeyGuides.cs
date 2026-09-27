using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    public partial class GameHud
    {
        public KeyGuideHolder KeyGuides { get; } = new KeyGuideHolder();
        int _keyGuideRevision = -1;

        void InitializeKeyGuides()
        {
            // **항상 보인다** (S15P21B202-657). 예전에는 안내를 끝낸 뒤에만 띄웠는데,
            // 태블릿은 진행도를 다시 볼 유일한 길이라 늘 보여야 한다.
            KeyGuides.Register("tablet", "태블릿", () => Controls.Tablet,
                () => IsKeyGuideOwner && GameSession.Instance.CanToggleTablet);
            // "던지기" 만 적혀 있어 **버리는 방법인 줄 몰랐다.** 짧게 누르면 버리기,
            // 길게 누르면 던지기라 둘 다 적는다.
            KeyGuides.Register("throw", "버리기 · 길게 던지기", () => Controls.ToolAction, CanShowThrowGuide);
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

        /// <summary>
        /// 왼쪽 아래 상시 키 안내. <b>상호작용 안내와 같은 모양</b>이다
        /// (S15P21B202-679) — 예전에는 여기만 네모 키캡이라 화면에 안내 문법이
        /// 둘이었다. 키는 <c>[Tab]</c> 꼴로 적는다.
        /// </summary>
        void DrawKeyGuides(float width, float height)
        {
            if (!IsKeyGuideOwner || GameSession.Instance.InputSuppressed
                || GameSession.Instance.ScriptedInput) return;
            int previousDepth = GUI.depth;
            GUI.depth = -90; // 태블릿이 열려 있어도 닫기 키는 보인다.
            const float margin = 18f, gap = 12f;
            float x = margin, y = height - margin - PromptHeight;
            for (int i = 0; i < KeyGuides.Entries.Count; i++)
            {
                var entry = KeyGuides.Entries[i];
                if (!entry.Visible) continue;
                var prompt = new HudPrompt(TabletHelpContent.Cap(entry.KeyText), entry.Label);
                float itemWidth = PromptWidth(prompt);
                if (x > margin && x + itemWidth > width - margin)
                {
                    x = margin;
                    y -= PromptHeight + gap;
                }
                DrawPromptAt(x, y, prompt);
                x += itemWidth + gap;
            }
            GUI.depth = previousDepth;
        }
    }
}
