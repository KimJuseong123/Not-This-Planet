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
        /// <summary>
        /// 막대와 문구의 폭. 2026-09-10 QA 에서 <b>문구가 두 줄로 넘쳐 잘렸다</b> —
        /// 닉네임이 길면(`졸다일어난우주인송승우`) 260 으로는 한 줄에 안 들어간다.
        /// 넓히고, 그래도 넘치면 <see cref="LabelHeight"/> 대신 <b>실제로 필요한
        /// 높이를 재서</b> 그린다.
        /// </summary>
        const float BarWidth = 360f;

        const float BarHeight = 14f;
        const float BarBottomMargin = 150f;

        /// <summary>문구 칸의 <b>최소</b> 높이. 두 줄이면 이보다 커진다.</summary>
        const float LabelHeight = 22f;

        /// <summary>문구와 막대 사이 · 문구 좌우 여백.</summary>
        const float Gap = 4f;

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

                // 켜 두지 않으면 긴 닉네임이 칸을 넘어가 잘린다. 켜면 두 줄이 되고,
                // 그 높이는 CalcHeight 로 재서 칸을 늘린다(2026-09-10 QA 신고).
                wordWrap = true,
            };
        }

        void OnGUI()
        {
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        void DrawScaledGui()
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
            var center = new Vector2(UiScale.Width * 0.5f, UiScale.Height - BarBottomMargin);

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

            // **문구를 막대 위에 올린다.** 두 줄이 되면 그만큼 더 올라가야 한다 —
            // 고정 높이로 두면 잘린다(2026-09-10 QA 신고).
            float labelHeight = LabelHeight;
            if (labelStyle != null)
            {
                labelHeight = Mathf.Max(
                    LabelHeight,
                    labelStyle.CalcHeight(new GUIContent(label), BarWidth - Gap * 2f));
            }

            DrawLabel(new Vector2(center.x, center.y - Gap - labelHeight * 0.5f), label,
                      labelHeight);
            DrawBar(center, progress);
        }

        void DrawLabel(Vector2 center, string text, float height = 0f)
        {
            if (height <= 0f)
            {
                height = labelStyle != null
                    ? Mathf.Max(LabelHeight,
                                labelStyle.CalcHeight(new GUIContent(text), BarWidth - Gap * 2f))
                    : LabelHeight;
            }

            var rect = new Rect(center.x - BarWidth * 0.5f, center.y - height * 0.5f,
                                BarWidth, height);

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
