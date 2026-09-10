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
        GUIStyle style;

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

        void DrawPlate(Camera camera, NetPlayer player)
        {
            Vector3 world = player.NameplateAnchor;

            // 카메라 뒤는 그리지 않는다. WorldToScreenPoint 는 뒤쪽도 좌표를 주므로
            // z 를 보지 않으면 등 뒤 사람의 이름이 화면에 뒤집혀 뜬다.
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return;

            if (screen.x < 0f || screen.x > Screen.width
                || screen.y < 0f || screen.y > Screen.height) return;

            float distance = Vector3.Distance(camera.transform.position, world);
            if (distance > FadeEnd) return;

            float alpha = distance <= FadeStart
                ? 1f
                : 1f - Mathf.InverseLerp(FadeStart, FadeEnd, distance);

            string label = string.IsNullOrEmpty(player.nickname) ? "비행사" : player.nickname;

            var rect = new Rect(screen.x - PlateWidth * 0.5f,
                                Screen.height - screen.y - PlateHeight,
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
