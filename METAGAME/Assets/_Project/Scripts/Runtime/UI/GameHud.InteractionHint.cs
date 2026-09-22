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
        ///
        /// <para><b>모양.</b> 눌러야 하는 것이므로 둥근 테두리 안에 담는다
        /// (S15P21B202-679). 그냥 읽기만 하면 되는 안내(<see cref="DrawOnboardingNotice"/>)
        /// 는 반대로 테두리가 없다 — 테두리의 있고 없음이 "누르라"와 "읽으라"를 가른다.</para>
        /// </summary>
        void DrawInteractionHint(float width, float height)
        {
            var session = GameSession.Instance;
            if (!IsKeyGuideOwner || session == null) return;
            if (session.InputSuppressed || session.ScriptedInput || session.EndingActive
                || session.Cleared || !session.CursorLocked) return;
            if (Local == null || Local.Downed || Local.IsMounted) return;
            if (session.Exploration != null && session.Exploration.TabletOpen) return;

            var target = Local.CurrentInteractionTarget;
            var prompt = PromptFor(
                session.Escape != null && session.Escape.LooksStuck,
                Local.ActiveTool != null || Local.ClampGrabbed,
                target.CanInteract, target.Kind);
            if (!prompt.Exists) return;

            // **작은 창에서도 겹치지 않게 한다.** S15P21B202-665 의 안내 줄은
            // Min(h*0.5 + 92, h - 168) 이라 창이 520px 보다 낮으면 위로 올라온다 —
            // 그때 이 줄이 고정 자리에 있으면 두 줄이 포개진다(mr-overlap 이 잡았다).
            // 같은 방식으로 눌러 18px 을 남긴다: (h - 168) - 38 - 18 = h - 224.
            float y = Mathf.Min(height * 0.5f + 40f, height - 224f);
            DrawPromptCentered(width * 0.5f, y, prompt);
        }

        /// <summary>
        /// 그 순간에 쓸 수 있는 것 하나만 고른다. <b>다 띄우면 다시 안 보이게 된다.</b>
        ///
        /// <para>끼었을 때가 먼저다 — 그때는 다른 무엇도 할 수 없다.</para>
        ///
        /// <para>화면 없이 검사할 수 있도록 상태만 받는다.</para>
        /// </summary>
        public static HudPrompt PromptFor(bool stuck, bool handsBusy, bool canInteract,
                                          PlayerInteractionKind kind)
        {
            // 끼임은 판을 못 이어 가는 상태라 다른 안내보다 앞선다.
            if (stuck)
                return new HudPrompt(TabletHelpContent.Cap(Controls.StuckEscape), "꾹 눌러 빠져나오기");

            // 도구를 들고 있으면 도구 조준이 우선이다 — 크로스헤드와 같은 규칙이다.
            if (handsBusy || !canInteract) return default;

            return kind switch
            {
                PlayerInteractionKind.Clamp =>
                    new HudPrompt(TabletHelpContent.Cap("좌클릭 유지"), "고정못을 잡고 뒤로 당기기"),
                // "줍기" 는 집어서 주머니에 넣는 말로 읽힌다. 실제로는 손으로 쥔다.
                // 고정물·운반물·동료·도구 모두 GrabLeft/GrabRight 경로다. E는 탑승용이다.
                PlayerInteractionKind.Grabbable =>
                    new HudPrompt(TabletHelpContent.Cap("좌클릭"), "잡기"),
                PlayerInteractionKind.Transport =>
                    new HudPrompt(TabletHelpContent.Cap(Controls.TransportAction), "타기"),
                _ => default,
            };
        }

        /// <summary>안내에 쓰는 모든 글자. 폰트가 미리 굽지 않으면 네모로 나온다.</summary>
        public static void AppendPromptGlyphs(System.Text.StringBuilder text)
        {
            text.Append(TabletHelpContent.Cap(Controls.StuckEscape)).Append("꾹 눌러 빠져나오기");
            text.Append(TabletHelpContent.Cap("좌클릭 유지")).Append("고정못을 잡고 뒤로 당기기");
            text.Append(TabletHelpContent.Cap("좌클릭")).Append("잡기");
            text.Append(TabletHelpContent.Cap(Controls.TransportAction)).Append("타기");
        }
    }
}
