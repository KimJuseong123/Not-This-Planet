using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>
    /// Lobby 전용 IMGUI. 현재 프로젝트가 HUD를 IMGUI로 그리므로 별도 UI 패키지 없이
    /// 로그인·방 상태를 표현한다.
    /// </summary>
    public sealed class LobbyHud : MonoBehaviour
    {
        private NetworkSession network;
        private string nickname;
        private string serverAddress;
        private string deviceIdOverride;
        private string roomIdInput = string.Empty;
        private string error = string.Empty;
        private bool subscribed;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private Font koreanFont;

        public void Initialize(NetworkSession session, string defaultNickname, string deviceOverride,
            string defaultServerAddress = null)
        {
            Unsubscribe();
            network = session;
            nickname = string.IsNullOrWhiteSpace(defaultNickname) ? "비행사" : defaultNickname;
            serverAddress = NetworkSession.ResolveServerAddress(defaultServerAddress);
            deviceIdOverride = deviceOverride;
            Subscribe();
        }

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        void Update()
        {
            if (network == null) return;
            if (network.State == NetworkConnectionState.Connected &&
                (network.CurrentRoom == null || network.CurrentRoom.IsWaiting))
                error = error == "서버 연결이 끊어졌습니다." ? string.Empty : error;
        }

        private void Subscribe()
        {
            if (subscribed || network == null) return;
            network.ErrorReceived += OnError;
            network.ConnectionClosed += OnClosed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || network == null) return;
            network.ErrorReceived -= OnError;
            network.ConnectionClosed -= OnClosed;
            subscribed = false;
        }

        private void OnError(string message) => error = message ?? string.Empty;

        private void OnClosed()
        {
            if (network != null && network.CurrentRoom == null)
                error = "서버 연결이 끊어졌습니다.";
        }

        void OnGUI()
        {
            if (network == null) return;
            EnsureStyles();

            float width = Mathf.Min(620f, Screen.width - 40f);
            float height = Mathf.Min(620f, Screen.height - 40f);
            float x = (Screen.width - width) * 0.5f;
            float y = (Screen.height - height) * 0.5f;
            var panel = new Rect(x, y, width, height);

            Color previous = GUI.color;
            GUI.color = new Color(0.02f, 0.05f, 0.09f, 0.94f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = previous;

            Label(new Rect(x + 32f, y + 24f, width - 64f, 44f), "둥실둥실 우주 대탈출", titleStyle);
            Label(new Rect(x + 32f, y + 72f, width - 64f, 24f), "온라인 로비", smallStyle);

            if (!network.IsLoggedIn)
                DrawLogin(x, y, width);
            else if (!network.IsConnected)
                DrawConnecting(x, y, width);
            else if (network.CurrentRoom == null)
                DrawRoomEntry(x, y, width);
            else
                DrawRoom(x, y, width, network.CurrentRoom);

            if (!string.IsNullOrEmpty(error))
                Label(new Rect(x + 32f, y + height - 56f, width - 64f, 32f), error, smallStyle, new Color(1f, 0.55f, 0.45f));
        }

        private void DrawLogin(float x, float y, float width)
        {
            Label(new Rect(x + 42f, y + 142f, 180f, 30f), "닉네임", labelStyle);
            nickname = GUI.TextField(new Rect(x + 230f, y + 138f, width - 272f, 36f), nickname ?? string.Empty, 12);

            Label(new Rect(x + 42f, y + 190f, 180f, 30f), "서버 주소", labelStyle);
            serverAddress = GUI.TextField(new Rect(x + 230f, y + 186f, width - 272f, 36f), serverAddress ?? string.Empty, 64);

            Label(new Rect(x + 42f, y + 238f, width - 84f, 52f),
                "방장 PC의 주소를 입력하세요 (예: 192.168.0.15:8080). 같은 와이파이에 있어야 합니다.", smallStyle);

            bool canLogin = network.State != NetworkConnectionState.LoggingIn;
            GUI.enabled = canLogin;
            if (GUI.Button(new Rect(x + 42f, y + 310f, width - 84f, 48f), "게스트 로그인", buttonStyle))
            {
                error = string.Empty;
                string addressError;
                if (!network.ApplyServerAddress(serverAddress, out addressError))
                    error = addressError;
                else
                    network.LoginGuest(nickname, NetworkSession.ResolveDeviceId(deviceIdOverride));
            }
            GUI.enabled = true;
            if (network.State == NetworkConnectionState.LoggingIn)
                Label(new Rect(x + 42f, y + 374f, width - 84f, 30f), "로그인 중...", smallStyle);
        }

        private void DrawConnecting(float x, float y, float width)
        {
            Label(new Rect(x + 42f, y + 152f, width - 84f, 34f),
                network.State == NetworkConnectionState.Failed ? "연결에 실패했습니다." : "서버에 연결 중입니다...", labelStyle);
            Label(new Rect(x + 42f, y + 202f, width - 84f, 30f),
                "로그인: " + network.Nickname + " · 서버: " + network.ServerAddress, smallStyle);

            if (network.State == NetworkConnectionState.Failed &&
                GUI.Button(new Rect(x + 42f, y + 270f, width - 84f, 48f), "다시 연결", buttonStyle))
            {
                error = string.Empty;
                network.Connect();
            }
        }

        private void DrawRoomEntry(float x, float y, float width)
        {
            Label(new Rect(x + 42f, y + 136f, width - 84f, 30f), "로그인 완료: " + network.Nickname, labelStyle);

            if (GUI.Button(new Rect(x + 42f, y + 198f, width - 84f, 48f), "새 방 만들기", buttonStyle))
            {
                error = string.Empty;
                network.CreateRoom();
            }

            Label(new Rect(x + 42f, y + 284f, 150f, 30f), "방 ID로 입장", labelStyle);
            roomIdInput = GUI.TextField(new Rect(x + 205f, y + 280f, width - 247f, 38f), roomIdInput ?? string.Empty, 8);
            if (GUI.Button(new Rect(x + 42f, y + 342f, width - 84f, 48f), "방 입장", buttonStyle))
            {
                error = string.Empty;
                network.JoinRoom(roomIdInput);
            }
        }

        private void DrawRoom(float x, float y, float width, RoomSnapshot room)
        {
            Label(new Rect(x + 42f, y + 132f, width - 84f, 32f), "방 " + room.RoomId, labelStyle);
            Label(new Rect(x + 42f, y + 168f, width - 84f, 26f),
                "상태: " + room.Status + " · " + (room.HostUserId == network.UserId ? "방장" : "참가자"), smallStyle);

            float rowY = y + 214f;
            for (int i = 0; i < room.Players.Count; i++)
            {
                PlayerSnapshot player = room.Players[i];
                string role = player.UserId == room.HostUserId ? "방장" : "참가자";
                string state = player.Disconnected
                    ? "연결 끊김"
                    : player.Ready ? "준비 완료" : "준비 중";
                Color color = player.Disconnected
                    ? new Color(1f, 0.75f, 0.45f)
                    : player.Ready ? new Color(0.55f, 1f, 0.65f) : Color.white;
                Label(new Rect(x + 52f, rowY + i * 30f, width - 104f, 26f),
                    player.Nickname + (player.UserId == network.UserId ? " (나)" : "") + " · " + role + " · " + state,
                    smallStyle, color);
            }

            float controlsY = rowY + Mathf.Max(1, room.Players.Count) * 30f + 16f;
            if (GUI.Button(new Rect(x + 42f, controlsY, width * 0.42f, 44f),
                network.IsLocalReady ? "준비 취소" : "Ready", buttonStyle))
                network.SetReady(!network.IsLocalReady);

            bool canStart = room.HostUserId == network.UserId && room.IsWaiting && room.AllReady;
            GUI.enabled = canStart;
            if (GUI.Button(new Rect(x + width * 0.48f, controlsY, width * 0.42f, 44f), "게임 시작", buttonStyle))
                network.StartGame();
            GUI.enabled = true;

            if (GUI.Button(new Rect(x + 42f, controlsY + 60f, width - 84f, 40f), "방 나가기", buttonStyle))
            {
                error = string.Empty;
                network.LeaveRoom();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            koreanFont = FindKoreanFont();
            titleStyle = MakeStyle(28, TextAnchor.MiddleCenter, FontStyle.Bold);
            labelStyle = MakeStyle(19, TextAnchor.MiddleLeft, FontStyle.Normal);
            smallStyle = MakeStyle(15, TextAnchor.MiddleLeft, FontStyle.Normal);
            buttonStyle = MakeStyle(18, TextAnchor.MiddleCenter, FontStyle.Normal);
        }

        private GUIStyle MakeStyle(int size, TextAnchor anchor, FontStyle fontStyle)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = anchor,
                fontStyle = fontStyle,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(4, 4, 2, 2)
            };
            if (koreanFont != null) style.font = koreanFont;
            return style;
        }

        private static Font FindKoreanFont()
        {
            string[] wanted = { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic", "Gulim", "굴림" };
            string[] installed = Font.GetOSInstalledFontNames();
            if (installed == null) return null;

            for (int i = 0; i < wanted.Length; i++)
                for (int j = 0; j < installed.Length; j++)
                    if (string.Equals(wanted[i], installed[j], System.StringComparison.OrdinalIgnoreCase))
                        return Font.CreateDynamicFontFromOSFont(installed[j], 28);
            return null;
        }

        private static void Label(Rect area, string text, GUIStyle style, Color? color = null)
        {
            Color previous = GUI.color;
            if (color.HasValue) GUI.color = color.Value;
            GUI.Label(area, text ?? string.Empty, style);
            GUI.color = previous;
        }
    }
}
