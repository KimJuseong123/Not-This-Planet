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
        private GUIStyle primaryButtonStyle;
        private GUIStyle quietButtonStyle;
        private GUIStyle kickerStyle;
        private GUIStyle hintStyle;

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

            // 대기/컷씬의 이동 차단은 Esc와 로비 나가기까지 차단하지 않는다.
            if (session == null || session.ScriptedInput || titleTransitionStarted)
                return;

            // 설정 화면은 기존 LobbyHud가 Esc를 키 재지정 취소 입력으로 사용할 수 있다.
            // 그 화면이 열려 있는 동안에는 게임 메뉴 토글을 소비하지 않는다.
            if (SettingsIsOpen)
                return;

            if (Input.GetKeyDown(Controls.FreeCursor))
                TryToggle();
        }

        public bool TryToggle()
        {
            if (session == null || session.ScriptedInput || session.EndingActive || titleTransitionStarted || SettingsIsOpen)
                return false;
            if (isOpen)
                CloseMenu();
            else
                OpenMenu();
            return true;
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
            float panelHeight = 366f * scale;
            float x = (UiScale.Width - panelWidth) * 0.5f;
            float y = (UiScale.Height - panelHeight) * 0.5f;
            float buttonWidth = 320f * scale;
            float buttonHeight = 52f * scale;
            float buttonX = (UiScale.Width - buttonWidth) * 0.5f;
            float firstButtonY = y + 116f * scale;
            float gap = 12f * scale;

            int previousDepth = GUI.depth;
            GUI.depth = -100;

            UiTheme.DrawDimmer(new Rect(0f, 0f, UiScale.Width, UiScale.Height));
            UiTheme.DrawPanel(new Rect(x, y, panelWidth, panelHeight));
            DrawLabel(new Rect(x, y + 25f * scale, panelWidth, 24f * scale),
                      "MISSION PAUSED", kickerStyle, UiTheme.Cyan);
            DrawLabel(new Rect(x, y + 49f * scale, panelWidth, 48f * scale),
                      "일시 메뉴", titleStyle, UiTheme.Text);

            if (UiInteraction.Button(new Rect(buttonX, firstButtonY, buttonWidth, buttonHeight),
                           "계속하기", primaryButtonStyle))
                CloseMenu();

            if (UiInteraction.Button(new Rect(buttonX, firstButtonY + (buttonHeight + gap),
                                    buttonWidth, buttonHeight), "설정", buttonStyle))
                OpenSettings();

            if (UiInteraction.Button(new Rect(buttonX, firstButtonY + (buttonHeight + gap) * 2f,
                                    buttonWidth, buttonHeight), "타이틀로 나가기", quietButtonStyle))
                ReturnToTitle();

            DrawLabel(new Rect(x, y + panelHeight - 28f * scale, panelWidth, 22f * scale),
                      "Esc 닫기", hintStyle, UiTheme.MutedText);

            GUI.depth = previousDepth;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            UiTheme.Ensure();

            titleStyle = MakeLabelStyle(TitleSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            kickerStyle = MakeLabelStyle(HintSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            hintStyle = MakeLabelStyle(HintSize, TextAnchor.MiddleCenter, FontStyle.Normal);
            Font font = FontAt(ButtonSize);
            buttonStyle = UiTheme.ButtonStyle(font, ButtonSize);
            primaryButtonStyle = UiTheme.ButtonStyle(font, ButtonSize, true);
            quietButtonStyle = UiTheme.ButtonStyle(font, ButtonSize, false, true);
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

        private static void DrawLabel(Rect area, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(area, text, style);
            GUI.color = previous;
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

        }
    }
}
