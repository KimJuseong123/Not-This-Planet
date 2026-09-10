using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 기절한 동료를 <b>일으키는 진행도</b>를 그린다 (S15P21B202-390).
    ///
    /// <para><b>왜 필요했나.</b> 2026-09-10 3인 QA: "사람 살리는 과정이 게이지로
    /// 표시되어야 하는 거 같음. 아직 안 되는 거 같음." 게이지가 없던 것만이 아니라
    /// <see cref="Gameplay.DownedRescue"/> 의 홀드가 <b>한 번도 시작되지 않았다</b> —
    /// 부르는 곳이 없었다. 배선은 <c>PlayerRig.TickRescue</c> 가 하고, 이 파일은
    /// 그것을 <b>보이게만</b> 한다.</para>
    ///
    /// <para><b>새 파일로 둔다.</b> <c>UI/</c> 는 다른 사람 파트이므로 기존
    /// <c>GameHud</c> 를 고치지 않는다 — <c>TeamPingHud</c>·<c>TeamNameplateHud</c> 가
    /// 같은 선례다. 판단은 <c>Gameplay</c>, 그리기는 여기다.</para>
    /// </summary>
    public sealed class DownedRescueHud : MonoBehaviour
    {
        const float BarWidth = 260f;
        const float BarHeight = 14f;
        const float BarBottomMargin = 150f;
        const float LabelHeight = 22f;

        GameSession session;
        Texture2D white;
        GUIStyle labelStyle;

        public void Initialize(GameSession gameSession)
        {
            session = gameSession;
        }

        void Awake()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void OnDestroy()
        {
            if (white != null) Destroy(white);
        }

        void EnsureStyle()
        {
            if (labelStyle != null) return;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyle();

            if (session == null) session = GameSession.Instance;
            var me = session != null ? session.LocalPlayer : null;
            if (me == null || me.Cam == null) return;

            // 내가 기절해 있으면 남을 일으킬 수 없다 — 그때는 아무것도 그리지 않는다.
            if (me.Downed) return;

            var target = me.RescueCandidate;
            if (target == null) return;

            float progress = me.RescueProgress01;
            var center = new Vector2(Screen.width * 0.5f, Screen.height - BarBottomMargin);

            // 진행분이 없으면 안내만 그린다.
            if (progress <= 0f)
            {
                DrawLabel(center, $"[{Controls.RescueTeammate}] {target.Nickname} 일으키기");
                return;
            }

            // **누르지 않아도 막대를 그린다.** 앞사람이 남긴 진행분이 있으면 그것이
            // 보여야 이어받는다는 것을 알 수 있다(기획 §12.1 과 같은 규칙 —
            // `DownedRescue.Release` 가 누른 만큼을 남긴다).
            string label = me.RescueHolding
                ? $"{target.Nickname} 일으키는 중…"
                : $"[{Controls.RescueTeammate}] {target.Nickname} 이어서 일으키기";

            DrawLabel(new Vector2(center.x, center.y - LabelHeight), label);
            DrawBar(center, progress);
        }

        void DrawLabel(Vector2 center, string text)
        {
            var rect = new Rect(center.x - BarWidth * 0.5f, center.y - LabelHeight * 0.5f,
                                BarWidth, LabelHeight);

            // 배경 없이 흰 글씨만 두면 밝은 지형에서 읽히지 않는다. 얇게 깔아 준다.
            Tint(new Color(0f, 0f, 0f, 0.45f));
            GUI.DrawTexture(rect, white);

            Tint(Color.white);
            GUI.Label(rect, text, labelStyle);
        }

        void DrawBar(Vector2 center, float progress)
        {
            var frame = new Rect(center.x - BarWidth * 0.5f, center.y, BarWidth, BarHeight);

            Tint(new Color(0f, 0f, 0f, 0.55f));
            GUI.DrawTexture(frame, white);

            // 채우는 색은 팀 색이 아니라 고정색이다 — 이 막대는 "누가" 가 아니라
            // "얼마나" 를 말하고, 팀 색으로 칠하면 구조자와 대상 중 누구의 색인지
            // 헷갈린다.
            var fill = new Rect(frame.x + 2f, frame.y + 2f,
                                (frame.width - 4f) * Mathf.Clamp01(progress), frame.height - 4f);
            Tint(new Color(0.45f, 0.85f, 0.45f, 0.95f));
            GUI.DrawTexture(fill, white);

            Tint(Color.white);
        }

        static void Tint(Color color) => GUI.color = color;
    }
}
