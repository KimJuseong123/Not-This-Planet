using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Video;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>
    /// 로비 전용 IMGUI. 현재 프로젝트의 HUD가 IMGUI 기반이므로 같은 방식으로
    /// 메인 메뉴와 온라인 방 화면을 구성한다.
    /// </summary>
    public sealed class LobbyHud : MonoBehaviour
    {
        private enum LobbyView
        {
            Main,
            Multiplayer
        }

        private NetworkSession network;
        private LobbyTransition transition;
        private LobbyView view = LobbyView.Main;
        private string nickname = string.Empty;
        private string deviceIdOverride;
        private string roomIdInput = string.Empty;
        private string error = string.Empty;
        private string notice = string.Empty;
        private bool subscribed;
        private bool fontWarmed;

        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private GUIStyle smallButtonStyle;
        private Texture2D whiteTexture;
        private Texture2D buttonNormalTexture;
        private Texture2D buttonHoverTexture;
        private Texture2D buttonActiveTexture;

        private static string koreanFontName;
        private static bool koreanFontSearched;
        private static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        /// <summary>
        /// 기존 호출부와 테스트가 사용할 수 있도록 영상 참조는 선택 인자로 둔다.
        /// </summary>
        public void Initialize(NetworkSession session, string defaultNickname, string deviceOverride,
            string defaultServerAddress = null, Texture2D backgroundImage = null,
            VideoClip backgroundVideo = null)
        {
            Unsubscribe();
            network = session;
            deviceIdOverride = deviceOverride;
            nickname = network != null && network.IsLoggedIn
                ? network.Nickname
                : (string.IsNullOrWhiteSpace(defaultNickname) ? "비행사" : defaultNickname);
            view = LobbyView.Main;
            roomIdInput = string.Empty;
            error = string.Empty;
            notice = string.Empty;

            if (network != null)
            {
                string address = NetworkSession.ResolveServerAddress(defaultServerAddress);
                string addressError;
                if (!network.ApplyServerAddress(address, out addressError))
                    error = addressError;
            }

            var background = GetComponent<LobbyBackground>();
            if (background == null)
                background = gameObject.AddComponent<LobbyBackground>();
            background.Initialize(backgroundImage, backgroundVideo);

            transition = LobbyTransition.Ensure();
            WarmFont();
            Subscribe();
        }

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
            Subscribe();
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            Unsubscribe();
        }

        private void Update()
        {
            if (!fontWarmed)
                WarmFont();
        }

        private void Subscribe()
        {
            if (subscribed || network == null)
                return;

            if (transition == null)
                transition = LobbyTransition.Ensure();

            network.ErrorReceived += OnError;
            network.ConnectionClosed += OnClosed;
            transition.Failed += OnTransitionFailed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (network != null)
            {
                network.ErrorReceived -= OnError;
                network.ConnectionClosed -= OnClosed;
            }

            if (transition != null)
                transition.Failed -= OnTransitionFailed;
            subscribed = false;
        }

        private void OnError(string message)
        {
            error = message ?? string.Empty;
        }

        private void OnClosed()
        {
            if (network != null && network.CurrentRoom == null && network.IsLoggedIn)
                error = "서버 연결이 끊어졌습니다. 다시 연결해 주세요.";
        }

        private void OnTransitionFailed(string message)
        {
            view = LobbyView.Multiplayer;
            error = message ?? "게임 시작에 실패했습니다.";
        }

        private void OnGUI()
        {
            if (network == null)
                return;

            EnsureStyles();
            if (view == LobbyView.Main)
                DrawMainMenu();
            else
                DrawMultiplayer();
        }

        private void DrawMainMenu()
        {
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.75f, 1.35f);
            float buttonWidth = 330f * scale;
            float buttonHeight = 60f * scale;
            float gap = 14f * scale;
            float totalHeight = buttonHeight * 4f + gap * 3f;
            Rect safe = Screen.safeArea;
            float x = Mathf.Clamp(Screen.width * 0.16f, safe.xMin + 20f, safe.xMax - buttonWidth - 20f);
            float y = Mathf.Clamp(Screen.height * 0.54f, safe.yMin + 20f,
                safe.yMax - totalHeight - 20f);

            var panel = new Rect(x - 22f * scale, y - 42f * scale,
                buttonWidth + 44f * scale, totalHeight + 66f * scale);
            DrawBox(panel, new Color(0.01f, 0.03f, 0.10f, 0.48f));
            DrawLabel(new Rect(x, y - 35f * scale, buttonWidth, 26f * scale),
                "MENU", smallStyle, new Color(0.72f, 0.91f, 1f));

            if (GUI.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 0, gap),
                    "혼자 하기", buttonStyle))
                BeginSolo();
            if (GUI.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 1, gap),
                    "함께 하기", buttonStyle))
            {
                view = LobbyView.Multiplayer;
                error = string.Empty;
                notice = string.Empty;
            }
            if (GUI.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 2, gap),
                    "설정", buttonStyle))
            {
                notice = "설정 기능은 준비 중입니다.";
                error = string.Empty;
            }
            if (GUI.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 3, gap),
                    "종료하기", buttonStyle))
                Application.Quit();

            DrawMessage(new Rect(x, y + totalHeight + 10f * scale, buttonWidth, 42f * scale));
        }

        private void BeginSolo()
        {
            if (transition == null)
                transition = LobbyTransition.Ensure();

            error = string.Empty;
            notice = string.Empty;
            network.DisconnectLobby();
            transition.BeginSolo();
        }

        private void DrawMultiplayer()
        {
            float width = Mathf.Min(780f, Screen.width - 40f);
            float height = Mathf.Min(700f, Screen.height - 40f);
            float x = (Screen.width - width) * 0.5f;
            float y = (Screen.height - height) * 0.5f;

            DrawBox(new Rect(x, y, width, height), new Color(0.01f, 0.03f, 0.09f, 0.88f));
            DrawLabel(new Rect(x + 34f, y + 22f, width - 190f, 42f),
                "함께 하기", titleStyle, Color.white);
            DrawLabel(new Rect(x + 36f, y + 68f, width - 72f, 26f),
                "친구에게 방 코드를 공유해 함께 출발하세요", smallStyle,
                new Color(0.72f, 0.86f, 0.98f));

            if (GUI.Button(new Rect(x + width - 142f, y + 24f, 106f, 40f),
                    "메인으로", smallButtonStyle))
            {
                view = LobbyView.Main;
                error = string.Empty;
                notice = string.Empty;
            }

            if (!network.IsLoggedIn)
                DrawLogin(x, y, width);
            else if (!network.IsConnected)
                DrawConnecting(x, y, width);
            else if (network.CurrentRoom == null)
                DrawRoomEntry(x, y, width);
            else
                DrawRoom(x, y, width, network.CurrentRoom);

            DrawMessage(new Rect(x + 36f, y + height - 62f, width - 72f, 38f));
        }

        private void DrawLogin(float x, float y, float width)
        {
            DrawLabel(new Rect(x + 54f, y + 156f, 170f, 30f), "닉네임", labelStyle, Color.white);
            GUI.SetNextControlName("LobbyNickname");
            nickname = GUI.TextField(new Rect(x + 236f, y + 151f, width - 290f, 40f),
                nickname ?? string.Empty, 12);
            DrawLabel(new Rect(x + 54f, y + 208f, width - 108f, 24f),
                "최대 12자 · 게스트로 바로 시작합니다", smallStyle,
                new Color(0.62f, 0.74f, 0.86f));

            bool canLogin = network.State != NetworkConnectionState.LoggingIn
                         && network.State != NetworkConnectionState.Connecting;
            GUI.enabled = canLogin;
            if (GUI.Button(new Rect(x + 54f, y + 270f, width - 108f, 54f),
                    "게스트 로그인", buttonStyle))
            {
                error = string.Empty;
                network.LoginGuest(nickname, NetworkSession.ResolveDeviceId(deviceIdOverride));
            }
            GUI.enabled = true;

            if (network.State == NetworkConnectionState.LoggingIn)
                DrawLabel(new Rect(x + 54f, y + 342f, width - 108f, 30f),
                    "게스트 로그인 중...", smallStyle, new Color(0.75f, 0.90f, 1f));
        }

        private void DrawConnecting(float x, float y, float width)
        {
            string name = string.IsNullOrEmpty(network.Nickname) ? nickname : network.Nickname;
            string state = network.State == NetworkConnectionState.Connecting
                ? "서버에 연결 중입니다..."
                : "서버 연결이 필요합니다.";
            DrawLabel(new Rect(x + 54f, y + 156f, width - 108f, 32f), state,
                labelStyle, Color.white);
            DrawLabel(new Rect(x + 54f, y + 204f, width - 108f, 26f),
                "닉네임: " + name + " · 서버: " + network.ServerAddress,
                smallStyle, new Color(0.72f, 0.84f, 0.94f));

            bool canConnect = network.State != NetworkConnectionState.Connecting;
            GUI.enabled = canConnect;
            if (GUI.Button(new Rect(x + 54f, y + 274f, width - 108f, 52f),
                    "다시 연결", buttonStyle))
            {
                error = string.Empty;
                network.Connect();
            }
            GUI.enabled = true;

            if (GUI.Button(new Rect(x + 54f, y + 348f, width - 108f, 42f),
                    "게스트 로그아웃", smallButtonStyle))
                LogoutGuest();
        }

        private void DrawRoomEntry(float x, float y, float width)
        {
            DrawLabel(new Rect(x + 54f, y + 144f, width - 108f, 30f),
                "로그인 완료: " + network.Nickname + "  (유저 " + network.UserId + ")",
                labelStyle, Color.white);

            if (GUI.Button(new Rect(x + 54f, y + 202f, width - 108f, 54f),
                    "새 방 만들기", buttonStyle))
            {
                error = string.Empty;
                network.CreateRoom();
            }

            DrawLabel(new Rect(x + 54f, y + 302f, 170f, 30f), "방 코드로 입장", labelStyle,
                Color.white);
            GUI.SetNextControlName("LobbyRoomCode");
            roomIdInput = GUI.TextField(new Rect(x + 236f, y + 297f, width - 290f, 40f),
                roomIdInput ?? string.Empty, 6);
            if (GUI.Button(new Rect(x + 54f, y + 365f, width - 108f, 54f),
                    "방 입장", buttonStyle))
            {
                error = string.Empty;
                network.JoinRoom((roomIdInput ?? string.Empty).Trim().ToUpperInvariant());
            }

            if (GUI.Button(new Rect(x + 54f, y + 438f, width - 108f, 42f),
                    "게스트 로그아웃", smallButtonStyle))
                LogoutGuest();
        }

        private void DrawRoom(float x, float y, float width, RoomSnapshot room)
        {
            bool isHost = room.HostUserId == network.UserId;
            DrawLabel(new Rect(x + 54f, y + 130f, width - 350f, 42f),
                "방 " + room.RoomId, titleStyle, new Color(0.75f, 0.95f, 1f));
            DrawLabel(new Rect(x + width - 276f, y + 139f, 132f, 26f),
                isHost ? "방장" : "참가자", smallStyle,
                isHost ? new Color(1f, 0.84f, 0.45f) : new Color(0.72f, 0.84f, 0.95f));
            if (GUI.Button(new Rect(x + width - 132f, y + 130f, 78f, 38f),
                    "복사", smallButtonStyle))
            {
                GUIUtility.systemCopyBuffer = room.RoomId;
                notice = "방 코드가 클립보드에 복사되었습니다.";
                error = string.Empty;
            }

            DrawLabel(new Rect(x + 54f, y + 184f, width - 108f, 26f),
                "상태: " + RoomStatusLabel(room.Status) + " · " + room.Players.Count + "/4명",
                smallStyle, new Color(0.72f, 0.84f, 0.94f));

            float rowY = y + 228f;
            for (int i = 0; i < room.Players.Count; i++)
            {
                PlayerSnapshot player = room.Players[i];
                string role = player.UserId == room.HostUserId ? "방장" : "참가자";
                string state = player.Disconnected
                    ? "연결 끊김"
                    : player.Ready ? "준비 완료" : "준비 중";
                Color color = player.Disconnected
                    ? new Color(1f, 0.72f, 0.42f)
                    : player.Ready ? new Color(0.55f, 1f, 0.68f) : Color.white;
                DrawLabel(new Rect(x + 68f, rowY + i * 34f, width - 136f, 28f),
                    player.Nickname + (player.UserId == network.UserId ? " (나)" : "")
                    + " · " + role + " · " + state, smallStyle, color);
            }

            float controlsY = rowY + Mathf.Max(1, room.Players.Count) * 34f + 24f;
            if (GUI.Button(new Rect(x + 54f, controlsY, width * 0.40f, 48f),
                    network.IsLocalReady ? "준비 취소" : "준비", buttonStyle))
                network.SetReady(!network.IsLocalReady);

            bool canStart = isHost && room.IsWaiting && room.AllReady;
            GUI.enabled = canStart;
            if (GUI.Button(new Rect(x + width * 0.52f, controlsY, width * 0.40f, 48f),
                    "게임 시작", buttonStyle))
                network.StartGame();
            GUI.enabled = true;

            if (GUI.Button(new Rect(x + 54f, controlsY + 64f, width - 108f, 40f),
                    "방 나가기", smallButtonStyle))
            {
                error = string.Empty;
                network.LeaveRoom();
            }

            if (GUI.Button(new Rect(x + 54f, controlsY + 116f, width - 108f, 40f),
                    "게스트 로그아웃", smallButtonStyle))
                LogoutGuest();
        }

        private void LogoutGuest()
        {
            string previousNickname = network != null ? network.Nickname : nickname;
            network?.LogoutGuest();
            nickname = string.IsNullOrWhiteSpace(previousNickname) ? "비행사" : previousNickname;
            roomIdInput = string.Empty;
            notice = string.Empty;
            error = string.Empty;
            view = LobbyView.Multiplayer;
        }

        private static string RoomStatusLabel(string status)
        {
            if (string.Equals(status, "WAITING", System.StringComparison.OrdinalIgnoreCase))
                return "대기 중";
            if (string.Equals(status, "PLAYING", System.StringComparison.OrdinalIgnoreCase))
                return "게임 중";
            return string.IsNullOrEmpty(status) ? "알 수 없음" : status;
        }

        private static Rect ButtonRect(float x, float y, float width, float height,
            int index, float gap)
        {
            return new Rect(x, y + index * (height + gap), width, height);
        }

        private void DrawMessage(Rect area)
        {
            if (!string.IsNullOrEmpty(error))
            {
                DrawLabel(area, error, smallStyle, new Color(1f, 0.58f, 0.48f));
                return;
            }

            if (!string.IsNullOrEmpty(notice))
                DrawLabel(area, notice, smallStyle, new Color(0.58f, 1f, 0.76f));
        }

        private void EnsureStyles()
        {
            if (whiteTexture != null)
                return;

            whiteTexture = MakeTexture(Color.white);
            buttonNormalTexture = MakeTexture(new Color(0.02f, 0.10f, 0.22f, 0.78f));
            buttonHoverTexture = MakeTexture(new Color(0.08f, 0.52f, 0.84f, 0.92f));
            buttonActiveTexture = MakeTexture(new Color(0.04f, 0.28f, 0.62f, 0.98f));

            titleStyle = MakeLabelStyle(29, TextAnchor.MiddleLeft, FontStyle.Bold);
            labelStyle = MakeLabelStyle(19, TextAnchor.MiddleLeft, FontStyle.Normal);
            smallStyle = MakeLabelStyle(15, TextAnchor.MiddleLeft, FontStyle.Normal);
            buttonStyle = MakeButtonStyle(20);
            smallButtonStyle = MakeButtonStyle(16);
        }

        private GUIStyle MakeLabelStyle(int size, TextAnchor anchor, FontStyle style)
        {
            var result = new GUIStyle(GUI.skin.label)
            {
                alignment = anchor,
                fontStyle = style,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(4, 4, 2, 2)
            };
            Font font = FontAt(size);
            if (font != null)
            {
                result.font = font;
                result.fontSize = 0;
            }
            else
            {
                result.fontSize = size;
            }
            return result;
        }

        private GUIStyle MakeButtonStyle(int size)
        {
            var result = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                padding = new RectOffset(12, 12, 5, 5),
                border = new RectOffset(2, 2, 2, 2)
            };
            result.normal.background = buttonNormalTexture;
            result.hover.background = buttonHoverTexture;
            result.focused.background = buttonHoverTexture;
            result.active.background = buttonActiveTexture;
            result.normal.textColor = Color.white;
            result.hover.textColor = Color.white;
            result.focused.textColor = Color.white;
            result.active.textColor = Color.white;

            Font font = FontAt(size);
            if (font != null)
            {
                result.font = font;
                result.fontSize = 0;
            }
            else
            {
                result.fontSize = size;
            }
            return result;
        }

        private void DrawBox(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, whiteTexture);
            GUI.color = previous;
        }

        private static void DrawLabel(Rect area, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(area, text ?? string.Empty, style);
            GUI.color = previous;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "~로비 UI 색상"
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static string KoreanFontName
        {
            get
            {
                if (koreanFontSearched)
                    return koreanFontName;

                koreanFontSearched = true;
                string[] wanted = { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic",
                                    "Gulim", "굴림", "Dotum", "돋움", "Batang", "바탕" };
                string[] installed = Font.GetOSInstalledFontNames();
                if (installed == null)
                    return null;

                for (int i = 0; i < wanted.Length; i++)
                    for (int j = 0; j < installed.Length; j++)
                        if (string.Equals(wanted[i], installed[j], System.StringComparison.OrdinalIgnoreCase))
                            return koreanFontName = installed[j];

                for (int i = 0; i < installed.Length; i++)
                    if (installed[i].IndexOf("Gothic", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return koreanFontName = installed[i];

                return null;
            }
        }

        private static Font FontAt(int size)
        {
            Font font;
            if (Fonts.TryGetValue(size, out font) && font != null)
                return font;

            string fontName = KoreanFontName;
            if (string.IsNullOrEmpty(fontName))
                return null;

            font = Font.CreateDynamicFontFromOSFont(fontName, size);
            Fonts[size] = font;
            return font;
        }

        private void WarmFont()
        {
            fontWarmed = true;
            string fontName = KoreanFontName;
            if (string.IsNullOrEmpty(fontName))
                return;

            var characters = new StringBuilder();
            characters.Append("0123456789 /·()[]+-!?MENU");
            characters.Append("혼자 하기 함께 설정 종료하기 준비 중입니다 게스트 로그인 로그인 중 서버 연결 다시 연결");
            characters.Append("방 코드로 입장 새 방 만들기 방 입장 방장 참가자 준비 취소 게임 시작 방 나가기");
            characters.Append("게스트 로그아웃 복사 클립보드에 복사되었습니다 대기 중 게임 중 알 수 없음");
            characters.Append("친구에게 방 코드를 공유해 함께 출발하세요 최대 12자 바로 시작합니다 로그인 완료 유저 명");
            characters.Append("서버 연결이 끊어졌습니다 설정 기능은 준비 중입니다 게임 서버 연결 시간이 초과되었습니다");

            int[] sizes = { 15, 16, 19, 20, 29 };
            for (int i = 0; i < sizes.Length; i++)
            {
                Font font = FontAt(sizes[i]);
                if (font != null)
                    font.RequestCharactersInTexture(characters.ToString(), sizes[i], FontStyle.Normal);
            }
        }

        private void OnFontRebuilt(Font font)
        {
            foreach (var entry in Fonts)
            {
                if (entry.Value == font)
                {
                    fontWarmed = false;
                    break;
                }
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DestroyTexture(whiteTexture);
            DestroyTexture(buttonNormalTexture);
            DestroyTexture(buttonHoverTexture);
            DestroyTexture(buttonActiveTexture);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
