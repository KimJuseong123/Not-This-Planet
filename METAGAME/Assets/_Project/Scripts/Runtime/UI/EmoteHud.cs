using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 기존 핑/이름표처럼 각 카메라 화면에 정면 PNG를 투영한다.
    /// 월드 회전이나 카메라 방향은 네트워크로 전송하지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class EmoteHud : MonoBehaviour
    {
        static readonly string[] Names = { "01-question", "02-exclamation", "03-thumbsup",
            "04-heart", "05-crying", "06-angry" };
        readonly Texture2D[] _icons = new Texture2D[EmoteState.Count];
        readonly EmoteState _solo = new EmoteState();
        GameSession _session;
        Vector2 _wheelOrigin;
        int _selection = -1;
        bool _open;
        bool _releaseNextFrame;
        GUIStyle _label;

        public void Initialize(GameSession session) => _session = session;

        void Awake()
        {
            for (int i = 0; i < Names.Length; i++)
                _icons[i] = Resources.Load<Texture2D>("Emotes/" + Names[i]);
        }

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
            if (!_open && Input.GetKeyDown(Controls.EmoteWheel) && _session.CursorLocked)
            {
                _open = true;
                _selection = -1;
                _session.SetEmoteWheelOpen(true);
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
            _selection = Selection(delta / UiScale.Value);
            if (Input.GetKeyUp(Controls.EmoteWheel))
            {
                if (_selection >= 0) Show((byte)(_selection + 1));
                // 선택을 끝낸 프레임의 버튼이 게임 도구 입력으로 새지 않게 한다.
                _releaseNextFrame = true;
                _open = false;
            }
        }

        void Close()
        {
            if ((_open || _releaseNextFrame || (_session != null && _session.EmoteWheelOpen))
                && _session != null) _session.SetEmoteWheelOpen(false);
            _open = false;
            _releaseNextFrame = false;
            _selection = -1;
        }

        public static int Selection(Vector2 delta)
        {
            if (delta.sqrMagnitude < 24f * 24f) return -1;
            float angle = Mathf.Repeat(Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg + 30f, 360f);
            return Mathf.FloorToInt(angle / 60f);
        }

        void Show(byte id)
        {
            var players = NetPlayer.AllClientPlayers;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].isLocalPlayer)
                { players[i].RequestEmote(id); return; }
            // 연결 중인 클라이언트가 로컬 표시로 성공한 것처럼 보이지 않게 한다.
            if (!NetworkClient.active && !NetworkServer.active)
                _solo.TryShow(id, Time.unscaledTimeAsDouble);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _session == null
                || _session.LocalPlayer == null || _session.EndingActive) return;
            using (new UiScale.Scope(UiScale.Value))
            {
                if (_label == null) _label = new GUIStyle(GUI.skin.label)
                { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                var camera = _session.LocalPlayer.Cam != null ? _session.LocalPlayer.Cam.Cam : null;
                if (camera != null) DrawPlayers(camera);
                if (_open) DrawWheel();
                else if (Available) GUI.Label(new Rect(UiScale.Width - 185, UiScale.Height - 40, 175, 28),
                    "Hold F1 : EMOTES", _label);
            }
        }

        void DrawPlayers(Camera camera)
        {
            var players = NetPlayer.AllClientPlayers;
            foreach (var player in players)
            {
                if (player == null || !player.HasBody
                    || !EmoteState.IsActive(player.EmoteId, player.EmoteExpiresAt, NetworkTime.time)) continue;
                float remaining = (float)(player.EmoteExpiresAt - NetworkTime.time);
                if (player.isLocalPlayer)
                    DrawIcon(player.EmoteId, new Vector2(UiScale.Width * .5f, UiScale.Height - 100), 65, remaining);
                else DrawAboveHead(camera, player, remaining);
            }
            if (!NetworkClient.active && !NetworkServer.active
                && EmoteState.IsActive(_solo.Id, _solo.ExpiresAt, Time.unscaledTimeAsDouble))
                DrawIcon(_solo.Id, new Vector2(UiScale.Width * .5f, UiScale.Height - 100), 65,
                    (float)(_solo.ExpiresAt - Time.unscaledTimeAsDouble));
        }

        void DrawAboveHead(Camera camera, NetPlayer player, float remaining)
            => DrawAboveHead(camera, player.NameplateAnchor + Vector3.up * .55f, player.EmoteId, remaining);

        /// <summary>NetPlayer 가 아닌 액터(서버가 모는 AI 동료)의 이모티콘을 같은 규칙으로 그린다.</summary>
        public void DrawWorldEmote(Camera camera, Vector3 anchor, byte id, double expiresAt, double now)
        {
            if (Event.current.type != EventType.Repaint || camera == null
                || !EmoteState.IsActive(id, expiresAt, now)) return;
            using (new UiScale.Scope(UiScale.Value))
                DrawAboveHead(camera, anchor, id, (float)(expiresAt - now));
        }

        void DrawAboveHead(Camera camera, Vector3 anchor, byte id, float remaining)
        {
            Vector3 point = UiScale.ScreenPoint(camera.WorldToScreenPoint(anchor));
            if (point.z <= 0 || point.x < 0 || point.x > UiScale.Width || point.y < 0 || point.y > UiScale.Height) return;
            float distance = Vector3.Distance(camera.transform.position, anchor);
            if (distance > 45f) return;
            // 가까운 벽 너머의 이모티콘으로 동료 위치를 노출하지 않는다.
            int wallMask = Physics.DefaultRaycastLayers & ~(1 << GameLayers.Player);
            if (Physics.Linecast(camera.transform.position, anchor, wallMask,
                QueryTriggerInteraction.Ignore)) return;
            float size = Mathf.Clamp(260f / Mathf.Max(distance, 1f), 32f, 88f);
            DrawIcon(id, new Vector2(point.x, UiScale.Height - point.y), size, remaining);
        }

        void DrawIcon(byte id, Vector2 center, float size, float remaining)
        {
            if (!EmoteState.IsValid(id) || _icons[id - 1] == null) return;
            float age = (float)EmoteState.Lifetime - remaining;
            float pop = 1f + Mathf.Sin(Mathf.Clamp01(age / .22f) * Mathf.PI) * .18f;
            size *= pop;
            Color color = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(remaining / .35f));
            GUI.DrawTexture(new Rect(center.x - size / 2, center.y - size / 2, size, size),
                _icons[id - 1], ScaleMode.ScaleToFit, true);
            GUI.color = color;
        }

        void DrawWheel()
        {
            Vector2 center = new Vector2(UiScale.Width * .5f, UiScale.Height * .5f);
            GUI.Box(new Rect(center.x - 195, center.y - 195, 390, 390), GUIContent.none);
            GUI.Label(new Rect(center.x - 90, center.y - 24, 180, 48),
                "F1\nRelease to send", _label);
            for (int i = 0; i < EmoteState.Count; i++)
            {
                float angle = i * Mathf.PI / 3;
                Vector2 position = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 122f;
                if (_selection == i)
                {
                    Color color = GUI.color;
                    GUI.color = new Color(.55f, .8f, 1f, .95f);
                    GUI.Box(new Rect(position.x - 48, position.y - 48, 96, 96), GUIContent.none);
                    GUI.color = color;
                }
                DrawIcon((byte)(i + 1), position, _selection == i ? 88 : 70, 2f);
            }
        }
    }
}
