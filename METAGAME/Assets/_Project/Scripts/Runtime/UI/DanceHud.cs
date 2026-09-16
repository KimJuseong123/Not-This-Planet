using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// F2 로 춤을 고른다 (S15P21B202-563).
    ///
    /// <para><b>조작은 <see cref="EmoteHud"/> 와 똑같다</b> — 누른 채 방향을 가리키고
    /// 떼면 결정된다. 이미 손에 익은 것을 그대로 쓰려고 그렇게 했다. 다른 점은 고른
    /// 것이 <b>화면에 뜨는 그림이 아니라 몸이 추는 춤</b>이라는 것뿐이다.</para>
    ///
    /// <para>그래서 나르는 방식도 다르다 — 이모티콘은 만료 시각을 나누지만 춤은
    /// <c>NetPlayer.danceSeq</c> 순번만 보낸다(<see cref="DanceCatalog"/>).</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DanceHud : MonoBehaviour
    {
        GameSession _session;
        Vector2 _wheelOrigin;
        int _selection = -1;
        bool _open;
        bool _releaseNextFrame;
        GUIStyle _label;

        public void Initialize(GameSession session) => _session = session;

        void OnDisable() => Close();
        void OnApplicationFocus(bool focused) { if (!focused) Close(); }

        bool Available => _session != null && _session.LocalPlayer != null
            && !_session.LocalPlayer.Downed && !_session.ScriptedInput
            && !_session.InputSuppressed && !_session.EndingActive
            && (_session.Exploration == null || !_session.Exploration.TabletOpen);

        void Update()
        {
            if (_releaseNextFrame) { _releaseNextFrame = false; Close(); }
            if (!Available) { Close(); return; }
            if (!_open && Input.GetKeyDown(Controls.DanceWheel) && _session.CursorLocked)
            {
                _open = true;
                _selection = -1;
                _session.SetDanceWheelOpen(true);
                _wheelOrigin = new Vector2(Screen.width * .5f, Screen.height * .5f);
            }
            if (!_open) return;
            if (Input.GetKeyDown(Controls.FreeCursor) || Input.GetMouseButtonDown(1))
            {
                _releaseNextFrame = true;
                _open = false;
                return;
            }
            Vector2 delta = (Vector2)Input.mousePosition - _wheelOrigin;
            _selection = Selection(delta / UiScale.Value, DanceCatalog.Count);
            if (Input.GetKeyUp(Controls.DanceWheel))
            {
                if (_selection >= 0) Play((byte)(_selection + 1));
                // 선택을 끝낸 프레임의 버튼이 게임 도구 입력으로 새지 않게 한다.
                _releaseNextFrame = true;
                _open = false;
            }
        }

        void Close()
        {
            if ((_open || _releaseNextFrame || (_session != null && _session.DanceWheelOpen))
                && _session != null) _session.SetDanceWheelOpen(false);
            _open = false;
            _releaseNextFrame = false;
            _selection = -1;
        }

        /// <summary>
        /// 마우스가 가리키는 칸. 가운데 가까이면 -1(고르지 않음)이다.
        ///
        /// <para><see cref="EmoteHud.Selection"/> 은 여섯 칸에 붙박이인데 춤은 개수가
        /// 늘어난다. 그래서 칸 수를 받는다 — <b>칸이 하나뿐일 때도 방향으로만 고르게
        /// 두면 아무 데나 가리켜도 그 하나가 골라진다</b>(그게 맞다).</para>
        /// </summary>
        public static int Selection(Vector2 delta, int count)
        {
            if (count <= 0) return -1;
            if (delta.sqrMagnitude < 24f * 24f) return -1;
            if (count == 1) return 0;
            float step = 360f / count;
            float angle = Mathf.Repeat(Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + step * .5f, 360f);
            return Mathf.Min(count - 1, Mathf.FloorToInt(angle / step));
        }

        void Play(byte id)
        {
            if (!DanceCatalog.IsValid(id)) return;
            var players = NetPlayer.AllClientPlayers;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].isLocalPlayer)
                { players[i].RequestDance(id); return; }

            // 혼자 돌릴 때는 네트워크를 거치지 않는다. 연결 중인 클라이언트가 로컬
            // 재생으로 성공한 것처럼 보이지 않게 한다 — EmoteHud 와 같은 이유다.
            if (!NetworkClient.active && !NetworkServer.active)
                _session?.LocalPlayer?.Doll?.Suit?.PlayDance();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _session == null
                || _session.LocalPlayer == null || _session.EndingActive) return;
            using (new UiScale.Scope(UiScale.Value))
            {
                if (_label == null) _label = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                if (_open) DrawWheel();
                else if (Available) GUI.Label(new Rect(UiScale.Width - 185, UiScale.Height - 66, 175, 28),
                    "Hold F2 : DANCE", _label);
            }
        }

        void DrawWheel()
        {
            Vector2 center = new Vector2(UiScale.Width * .5f, UiScale.Height * .5f);
            GUI.Box(new Rect(center.x - 195, center.y - 195, 390, 390), GUIContent.none);
            GUI.Label(new Rect(center.x - 90, center.y - 24, 180, 48),
                "F2\nRelease to dance", _label);
            int count = DanceCatalog.Count;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Vector2 position = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 122f;
                if (_selection == i)
                {
                    Color color = GUI.color;
                    GUI.color = new Color(.55f, .8f, 1f, .95f);
                    GUI.Box(new Rect(position.x - 56, position.y - 30, 112, 60), GUIContent.none);
                    GUI.color = color;
                }
                GUI.Label(new Rect(position.x - 56, position.y - 16, 112, 32),
                    DanceCatalog.NameOf((byte)(i + 1)), _label);
            }
        }
    }
}
