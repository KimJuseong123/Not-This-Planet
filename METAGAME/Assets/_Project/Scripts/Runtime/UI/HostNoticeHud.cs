using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 방장과의 연결 상태 안내를 화면 위쪽 가운데에 띄운다 (S15P21B202-385).
    ///
    /// <b>왜 필요한가.</b> 방장이 나가면 화면이 마지막으로 받은 자세에 멈춘 채
    /// <b>10초 동안 아무 말이 없었다</b>(KCP 전송의 <c>Timeout</c> 이 10000ms). 사용자에게는
    /// 게임이 죽은 것으로 보였다.
    ///
    /// <b>표현만 한다.</b> 무엇을 띄울지는 <see cref="HostLinkWatch"/> 가 정하고 이 컴포넌트는
    /// 그리기만 한다. <see cref="TeamPingHud"/> 와 같은 규칙이다.
    /// </summary>
    public sealed class HostNoticeHud : MonoBehaviour
    {
        GUIStyle style;

        void EnsureStyle()
        {
            if (style != null) return;

            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
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

            string notice = HostLinkWatch.Notice;
            if (string.IsNullOrEmpty(notice)) return;

            EnsureStyle();

            var rect = new Rect(0f, UiScale.Height * 0.12f, UiScale.Width, 34f);
            Color previous = GUI.color;

            // 밝은 지형 위에서도 읽히게 한 픽셀 그림자를 먼저 깐다.
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), notice, style);

            GUI.color = new Color(1f, 0.82f, 0.40f, 1f);
            GUI.Label(rect, notice, style);

            GUI.color = previous;
        }
    }
}
