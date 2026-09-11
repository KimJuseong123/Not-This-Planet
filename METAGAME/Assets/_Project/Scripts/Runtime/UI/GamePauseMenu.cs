using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;

namespace Metagame.UI
{
    /// <summary>
    /// 게임 씬에서 각 로컬 플레이어가 여는 메뉴.
    ///
    /// 기획상 메뉴를 연 사람의 입력만 막고 세계 시간은 멈추지 않는다. 설정 화면은
    /// 로비의 기존 설정 UI를 그대로 재사용해 게임 씬에서도 같은 저장·미리보기 흐름을 쓴다.
    /// </summary>
    public sealed class GamePauseMenu : MonoBehaviour
    {
        private const int TitleSize = 34;
        private const int ButtonSize = 20;
        private const int HintSize = 15;

        private static string koreanFontName;
        private static bool koreanFontSearched;
        private static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        private GameSession session;
        private LobbyHud settingsView;
        private LobbyTransition transition;
        private bool isOpen;
        private bool titleTransitionStarted;
        private bool fontWarmed;

        private GUIStyle titleStyle;
        private GUIStyle buttonStyle;
        private GUIStyle hintStyle;
        private Texture2D whiteTexture;
        private Texture2D buttonNormalTexture;
        private Texture2D buttonHoverTexture;
        private Texture2D buttonActiveTexture;

        public bool IsOpen { get { return isOpen; } }

        public void Initialize(GameSession gameSession)
        {
            session = gameSession;
            isOpen = false;
            titleTransitionStarted = false;
            WarmFont();
        }

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnFontRebuilt;
        }

        private bool SettingsIsOpen
        {
            get { return settingsView != null && settingsView.IsSettingsOnlyOpen; }
        }

        private void Update()
        {
            if (!fontWarmed)
                WarmFont();

            if (session == null)
                session = GameSession.Instance;

            if (session != null && session.EndingActive)
            {
                isOpen = false;
                if (settingsView != null) settingsView.enabled = false;
                return;
            }

            if (session == null || session.ScriptedInput || titleTransitionStarted)
                return;

            // 설정 화면은 기존 LobbyHud가 Esc를 키 재지정 취소 입력으로 사용할 수 있다.
            // 그 화면이 열려 있는 동안에는 게임 메뉴 토글을 소비하지 않는다.
            if (SettingsIsOpen)
                return;

            if (Input.GetKeyDown(Controls.FreeCursor))
                Toggle();
        }

        private void Toggle()
        {
            if (isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        private void OpenMenu()
        {
            isOpen = true;
            session.SetInputSuppressed(true);
            session.SetCursor(false);
        }

        private void CloseMenu()
        {
            isOpen = false;
            session.SetInputSuppressed(false);

            bool tabletOpen = session.Exploration != null && session.Exploration.TabletOpen;
            session.SetCursor(!tabletOpen);
        }

        private void OpenSettings()
        {
            if (settingsView == null)
                settingsView = gameObject.AddComponent<LobbyHud>();

            settingsView.InitializeSettingsOnly(GameSettingsRuntime.Ensure());
        }

        private void ReturnToTitle()
        {
            if (titleTransitionStarted)
                return;

            transition = LobbyTransition.Ensure();
            transition.Failed -= OnTitleTransitionFailed;
            transition.Failed += OnTitleTransitionFailed;

            titleTransitionStarted = transition.BeginTitle();
            if (!titleTransitionStarted)
                return;

            session.SetInputSuppressed(true);
            session.SetCursor(false);
        }

        private void OnTitleTransitionFailed(string message)
        {
            titleTransitionStarted = false;
            if (session == null)
                return;

            session.SetInputSuppressed(false);
            session.SetCursor(false);
        }

        private void OnGUI()
        {
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        void DrawScaledGui()
        {
            if (!isOpen || titleTransitionStarted || SettingsIsOpen)
                return;

            EnsureStyles();

            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f),
                                      0.78f, 1.25f);
            float panelWidth = 430f * scale;
            float panelHeight = 330f * scale;
            float x = (UiScale.Width - panelWidth) * 0.5f;
            float y = (UiScale.Height - panelHeight) * 0.5f;
            float buttonWidth = 320f * scale;
            float buttonHeight = 52f * scale;
            float buttonX = (UiScale.Width - buttonWidth) * 0.5f;
            float firstButtonY = y + 96f * scale;
            float gap = 12f * scale;

            int previousDepth = GUI.depth;
            GUI.depth = -100;

            DrawBox(new Rect(0f, 0f, UiScale.Width, UiScale.Height),
                    new Color(0f, 0.01f, 0.04f, 0.62f));
            DrawBox(new Rect(x, y, panelWidth, panelHeight),
                    new Color(0.01f, 0.04f, 0.11f, 0.96f));
            DrawLabel(new Rect(x, y + 26f * scale, panelWidth, 48f * scale),
                      "일시 메뉴", titleStyle, Color.white);

            if (GUI.Button(new Rect(buttonX, firstButtonY, buttonWidth, buttonHeight),
                           "계속하기", buttonStyle))
                CloseMenu();

            if (GUI.Button(new Rect(buttonX, firstButtonY + (buttonHeight + gap),
                                    buttonWidth, buttonHeight), "설정", buttonStyle))
                OpenSettings();

            if (GUI.Button(new Rect(buttonX, firstButtonY + (buttonHeight + gap) * 2f,
                                    buttonWidth, buttonHeight), "타이틀로 나가기", buttonStyle))
                ReturnToTitle();

            DrawLabel(new Rect(x, y + panelHeight - 28f * scale, panelWidth, 22f * scale),
                      "Esc 닫기", hintStyle, new Color(0.70f, 0.84f, 0.94f));

            GUI.depth = previousDepth;
        }

        private void EnsureStyles()
        {
            if (whiteTexture != null)
                return;

            whiteTexture = MakeTexture(Color.white);
            buttonNormalTexture = MakeTexture(new Color(0.02f, 0.10f, 0.22f, 0.88f));
            buttonHoverTexture = MakeTexture(new Color(0.08f, 0.52f, 0.84f, 0.94f));
            buttonActiveTexture = MakeTexture(new Color(0.04f, 0.28f, 0.62f, 0.98f));

            titleStyle = MakeLabelStyle(TitleSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            hintStyle = MakeLabelStyle(HintSize, TextAnchor.MiddleCenter, FontStyle.Normal);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                padding = new RectOffset(12, 12, 5, 5),
                border = new RectOffset(2, 2, 2, 2)
            };
            buttonStyle.normal.background = buttonNormalTexture;
            buttonStyle.hover.background = buttonHoverTexture;
            buttonStyle.focused.background = buttonHoverTexture;
            buttonStyle.active.background = buttonActiveTexture;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.focused.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;

            Font font = FontAt(ButtonSize);
            if (font != null)
            {
                buttonStyle.font = font;
                buttonStyle.fontSize = 0;
            }
            else
            {
                buttonStyle.fontSize = ButtonSize;
            }
        }

        private void WarmFont()
        {
            fontWarmed = true;
            const string characters = "일시 메뉴 계속하기 설정 타이틀로 나가기 Esc 닫기";
            int[] sizes = { TitleSize, ButtonSize, HintSize };
            for (int i = 0; i < sizes.Length; i++)
            {
                Font font = FontAt(sizes[i]);
                if (font != null)
                {
                    font.RequestCharactersInTexture(characters, sizes[i], FontStyle.Normal);
                    font.RequestCharactersInTexture(characters, sizes[i], FontStyle.Bold);
                }
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

        private static GUIStyle MakeLabelStyle(int size, TextAnchor anchor, FontStyle fontStyle)
        {
            var result = new GUIStyle(GUI.skin.label)
            {
                alignment = anchor,
                fontStyle = fontStyle,
                wordWrap = false,
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
            GUI.Label(area, text, style);
            GUI.color = previous;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "~게임 메뉴 UI 색상"
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

        private void OnDestroy()
        {
            if (transition != null)
                transition.Failed -= OnTitleTransitionFailed;

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
