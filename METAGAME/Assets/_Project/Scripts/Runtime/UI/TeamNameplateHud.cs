using Metagame.Gameplay;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 팀원 머리 위에 닉네임을 그 사람의 팀 색으로 띄운다 (S15P21B202-375).
    ///
    /// <b>왜 필요한가.</b> 닉네임은 <c>NetPlayer.nickname</c> 으로 이미 모두에게 오고
    /// 있었는데 <b>게임 화면에 그리는 코드가 없었다</b> — 로비에서만 썼다. 그래서 4인
    /// 협동인데 누가 누군지 몰라 "네가 그거 잡아" 가 성립하지 않았다.
    ///
    /// <b>표현만 한다.</b> 이름과 색은 서버가 정한 값(<c>nickname</c> · <c>teamSlot</c>)을
    /// 읽을 뿐이고 판단하지 않는다. <see cref="TeamPingHud"/> 와 같은 규칙이다.
    ///
    /// <b>씬·프리팹을 건드리지 않는다.</b> <c>SpaceBootstrap</c> 이 코드로 붙이고
    /// <c>OnGUI</c> 에서 카메라 투영으로 그린다. 머리마다 3D 오브젝트를 매다는 방식이
    /// 아니므로 프리팹 편집이 필요 없다.
    /// </summary>
    public sealed class TeamNameplateHud : MonoBehaviour
    {
        /// <summary>이 거리부터 흐려지기 시작한다(m).</summary>
        const float FadeStart = 18f;

        /// <summary>이 거리를 넘으면 그리지 않는다(m). 맵 반대편 이름이 붙어 있지 않게.</summary>
        const float FadeEnd = 45f;

        const float PlateWidth = 160f;
        const float PlateHeight = 22f;

        GameSession session;
        GUIStyle style, badgeStyle;

        /// <summary>
        /// 머리 위 표시의 글자 크기 — 1080p 기준 픽셀, 화면 높이에 비례 (S15P21B202-746). 2026-09-26 QA: 동료가 무엇을
        /// 하는지 시연을 보는 사람도 읽을 수 있어야 한다.
        /// </summary>
        public const int BadgeFont = 26;

        /// <summary>표시는 이 거리까지 그린다(m) — 이름표보다 멀리. 멀리서 일하는 동료가 보여야 한다.</summary>
        const float BadgeEnd = 70f;

        public void Initialize(GameSession gameSession)
        {
            session = gameSession;
        }

        void EnsureStyle()
        {
            if (style != null) return;

            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
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
            if (session == null || session.LocalPlayer == null || session.LocalPlayer.Cam == null)
                return;

            Camera camera = session.LocalPlayer.Cam.Cam;
            if (camera == null) return;

            var players = NetPlayer.AllClientPlayers;
            if (players == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || !player.HasBody) continue;

                // 내 이름표는 내 화면에 그리지 않는다 — 시야를 가린다.
                if (player.isLocalPlayer) continue;

                DrawPlate(camera, player);
            }
        }

        /// <summary>
        /// AI 동료가 지금 하는 일 · 방금 해낸 것을 이름표 위에 크게 그린다. 하는 일은 팀 색 바탕, 해낸 것은 초록, 못 한 것은
        /// 빨강 — 글자를 읽지 않아도 색으로 안다. 멀어도 줄이지 않는다(작으면 안 보인다).
        /// </summary>
        void DrawBadge(Vector3 screen, float distance, NetPlayer player)
        {
            if (distance > BadgeEnd) return;
            // **하는 중인 일은 띄우지 않는다** (2026-09-26). 목표는 AI 동료가 사람과 크게 구분되지 않는 것이다 —
            // 사람 머리 위에는 "고정못 뽑는 중" 이 없다. 방금 해낸 것 · 못 한 것만 잠깐 띄운다.
            bool note = player.AiBadgeNoteVisible;
            if (!note) return;
            string text = player.AiBadgeNote;
            if (string.IsNullOrEmpty(text)) return;
            float k = Screen.height / 1080f / Mathf.Max(.5f, UiScale.Value);
            if (badgeStyle == null)
                badgeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            badgeStyle.fontSize = Mathf.RoundToInt(BadgeFont * k);
            var size = badgeStyle.CalcSize(new GUIContent(text));
            float w = size.x + 24f * k, h = size.y + 8f * k;
            var rect = new Rect(screen.x - w * .5f, UiScale.Height - screen.y - PlateHeight - h - 6f * k, w, h);
            Color back = note ? (player.AiBadgeNoteGood ? new Color(.18f, .62f, .28f, .92f) : new Color(.75f, .2f, .2f, .92f))
                              : WithAlpha(player.TeamAccentColor, .88f);
            Color previous = GUI.color;
            GUI.color = back;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = note ? Color.white : Color.black;
            GUI.Label(rect, text, badgeStyle);
            GUI.color = previous;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void DrawPlate(Camera camera, NetPlayer player)
        {
            Vector3 world = player.NameplateAnchor;

            // 카메라 뒤는 그리지 않는다. WorldToScreenPoint 는 뒤쪽도 좌표를 주므로
            // z 를 보지 않으면 등 뒤 사람의 이름이 화면에 뒤집혀 뜬다.
            Vector3 screen = UiScale.ScreenPoint(camera.WorldToScreenPoint(world));
            if (screen.z <= 0f) return;

            if (screen.x < 0f || screen.x > UiScale.Width
                || screen.y < 0f || screen.y > UiScale.Height) return;

            float distance = Vector3.Distance(camera.transform.position, world);
            if (distance > FadeEnd)
            {
                if (player.IsAiDriver) DrawBadge(screen, distance, player);
                return;
            }

            float alpha = distance <= FadeStart
                ? 1f
                : 1f - Mathf.InverseLerp(FadeStart, FadeEnd, distance);

            if (player.IsAiDriver) DrawBadge(screen, distance, player);

            string label = string.IsNullOrEmpty(player.nickname) ? "비행사" : player.nickname;

            var rect = new Rect(screen.x - PlateWidth * 0.5f,
                                UiScale.Height - screen.y - PlateHeight,
                                PlateWidth, PlateHeight);

            // 글씨만 올리면 밝은 지형 위에서 읽히지 않는다. 한 픽셀 그림자를 먼저 깐다.
            Color previous = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, alpha * 0.75f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, style);

            Color team = player.TeamAccentColor;
            GUI.color = new Color(team.r, team.g, team.b, alpha);
            GUI.Label(rect, label, style);

            GUI.color = previous;
        }
    }
}
