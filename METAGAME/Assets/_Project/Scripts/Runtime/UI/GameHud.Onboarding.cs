using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    public partial class GameHud
    {
        /// <summary>
        /// 필요한 순간의 한 줄 (S15P21B202-665).
        ///
        /// <para><b>화면 가운데 아래에 그린다.</b> 1인칭이라 눈이 화면 가운데에 있다 —
        /// 왼쪽 아래 키캡을 처음 하는 사람이 아무도 못 본 것이 그 증거다. 크로스헤드
        /// 바로 아래라 시선을 거의 안 옮겨도 읽힌다.</para>
        ///
        /// <para>글은 <see cref="OnboardingNotices"/> 가 정한다. 여기는 그리기만 한다.</para>
        /// </summary>
        void DrawOnboardingNotice(float width, float height)
        {
            var session = GameSession.Instance;
            if (!IsKeyGuideOwner || session == null) return;

            // 기절·컷씬·태블릿을 연 동안에는 안 띄운다. 읽을 상황이 아니거나
            // 태블릿에 같은 내용이 이미 펴져 있다.
            var exploration = session.Exploration;
            if (exploration == null || exploration.TabletOpen) return;
            if (session.InputSuppressed || session.ScriptedInput || session.EndingActive
                || session.Cleared || Local == null || Local.Downed) return;

            var text = exploration.Notices.Text;
            if (string.IsNullOrEmpty(text)) return;

            _onboardingContent.text = text;
            float textWidth = Mathf.Min(width - 80f, mid.CalcSize(_onboardingContent).x + 44f);
            float boxHeight = 44f;
            // 크로스헤드(화면 한가운데) 아래. 키캡 줄과 겹치지 않게 아래쪽에서 띄운다.
            float y = Mathf.Min(height * 0.5f + 92f, height - 168f);
            var box = new Rect((width - textWidth) * 0.5f, y, textWidth, boxHeight);

            UiTheme.DrawPanel(box, true, false);
            L(box, text, mid, UiTheme.Text);
        }

        readonly GUIContent _onboardingContent = new GUIContent();
    }
}
