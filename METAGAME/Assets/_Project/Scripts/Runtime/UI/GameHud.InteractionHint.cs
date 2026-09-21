using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.UI
{
    public partial class GameHud
    {
        /// <summary>
        /// 지금 할 수 있는 조작의 키를 크로스헤드 아래에 한 줄로 (S15P21B202-669).
        ///
        /// <para>크로스헤드는 손 아이콘으로 <b>"상호작용할 수 있다" 까지만</b> 말한다
        /// (<see cref="ResolveInteractionReticleState"/>). 어느 키인지는 말하지 않아서,
        /// 처음 하는 사람은 손 모양이 떴는데 뭘 눌러야 하는지 몰랐다.</para>
        ///
        /// <para><b>자리.</b> 크로스헤드 바로 아래(+40)다. 왼쪽 아래 키캡을 처음 하는
        /// 사람이 아무도 못 봤기 때문이다(S15P21B202-657). S15P21B202-665 의 안내
        /// 한 줄은 +92 에서 시작하므로 18px 이 남는다 — 겹치지 않는다.</para>
        /// </summary>
        void DrawInteractionHint(float width, float height)
        {
            var session = GameSession.Instance;
            if (!IsKeyGuideOwner || session == null) return;
            if (session.InputSuppressed || session.ScriptedInput || session.EndingActive
                || session.Cleared || !session.CursorLocked) return;
            if (Local == null || Local.Downed || Local.IsMounted) return;
            if (session.Exploration != null && session.Exploration.TabletOpen) return;

            var text = HintText(session);
            if (string.IsNullOrEmpty(text)) return;

            _hintContent.text = text;
            float boxWidth = Mathf.Min(width - 80f, mid.CalcSize(_hintContent).x + 40f);
            // **작은 창에서도 겹치지 않게 한다.** S15P21B202-665 의 안내 줄은
            // Min(h*0.5 + 92, h - 168) 이라 창이 520px 보다 낮으면 위로 올라온다 —
            // 그때 이 줄이 고정 자리에 있으면 두 줄이 포개진다(mr-overlap 이 잡았다).
            // 같은 방식으로 눌러 18px 을 남긴다: (h - 168) - 34 - 18 = h - 220.
            float y = Mathf.Min(height * 0.5f + 40f, height - 220f);
            var box = new Rect((width - boxWidth) * 0.5f, y, boxWidth, 34f);
            UiTheme.DrawPanel(box, true, false);
            L(box, text, mid, UiTheme.CyanSoft);
        }

        /// <summary>
        /// 그 순간에 쓸 수 있는 것 하나만 고른다. <b>다 띄우면 다시 안 보이게 된다.</b>
        ///
        /// <para>끼었을 때가 먼저다 — 그때는 다른 무엇도 할 수 없다.</para>
        /// </summary>
        string HintText(GameSession session)
        {
            // 끼임은 판을 못 이어 가는 상태라 다른 안내보다 앞선다.
            if (session.Escape != null && session.Escape.LooksStuck)
                return "[" + TabletHelpContent.Name(Controls.StuckEscape) + "]  꾹 눌러 빠져나오기";

            // 도구를 들고 있으면 도구 조준이 우선이다 — 크로스헤드와 같은 규칙이다.
            if (Local.ActiveTool != null || Local.ClampGrabbed) return null;

            var target = Local.CurrentInteractionTarget;
            if (!target.CanInteract) return null;

            return target.Kind switch
            {
                PlayerInteractionKind.Clamp => "[좌클릭 유지]  고정못을 잡고 뒤로 당기기",
                PlayerInteractionKind.Grabbable =>
                    "[" + TabletHelpContent.Name(Controls.TransportAction) + "]  줍기",
                PlayerInteractionKind.Transport =>
                    "[" + TabletHelpContent.Name(Controls.TransportAction) + "]  타기",
                _ => null,
            };
        }

        readonly GUIContent _hintContent = new GUIContent();
    }
}
