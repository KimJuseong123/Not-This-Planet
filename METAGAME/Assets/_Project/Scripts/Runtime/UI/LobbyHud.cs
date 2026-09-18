using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Video;
using Metagame.Core;
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
            Multiplayer,
            Settings
        }

        private enum SettingsTab
        {
            Graphics,
            Audio,
            Controls
        }

        private enum BindingAction
        {
            MoveForward,
            MoveBack,
            MoveLeft,
            MoveRight,
            Jump,
            Run,
            ArmsUp,
            Prone,
            FreeLook,
            TransportAction,
            ToolAction,
            Tablet,
            FieldToggle,
            StuckEscape,
            Restart,
            TabletPreviousPage,
            TabletNextPage,
            FreeCursor
        }

        private static readonly BindingAction[] BindingActions =
        {
            BindingAction.MoveForward,
            BindingAction.MoveBack,
            BindingAction.MoveLeft,
            BindingAction.MoveRight,
            BindingAction.Jump,
            BindingAction.Run,
            BindingAction.ArmsUp,
            BindingAction.Prone,
            BindingAction.FreeLook,
            BindingAction.TransportAction,
            BindingAction.ToolAction,
            BindingAction.Tablet,
            BindingAction.FieldToggle,
            BindingAction.StuckEscape,
            BindingAction.Restart,
            BindingAction.TabletPreviousPage,
            BindingAction.TabletNextPage,
            BindingAction.FreeCursor
        };

        private NetworkSession network;
        private GameSettingsRuntime settings;
        private GameSettingsData editingSettings;
        private LobbyTransition transition;
        private LobbyView view = LobbyView.Main;
        private SettingsTab settingsTab = SettingsTab.Graphics;
        private string nickname = string.Empty;
        private string deviceIdOverride;
        private string roomIdInput = string.Empty;
        private string error = string.Empty;
        private string notice = string.Empty;
        private bool subscribed;
        private bool fontWarmed;
        private BindingAction? rebindingAction;
        private Vector2 settingsScroll;
        private readonly List<Vector2Int> resolutionOptions = new List<Vector2Int>();
        private string[] qualityOptions = new string[0];

        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private GUIStyle smallButtonStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle quietButtonStyle;
        private GUIStyle textFieldStyle;
        private Texture2D whiteTexture;

        private static string koreanFontName;
        private static bool koreanFontSearched;
        private static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        /// <summary>게임 씬에서 설정 오버레이로 재사용될 때만 참이다.</summary>
        public bool IsSettingsOnlyOpen
        {
            get { return network == null && view == LobbyView.Settings && editingSettings != null; }
        }

        /// <summary>
        /// 기존 호출부와 테스트가 사용할 수 있도록 영상 참조는 선택 인자로 둔다.
        /// </summary>
        public void Initialize(NetworkSession session, string defaultNickname, string deviceOverride,
            Texture2D backgroundImage = null,
            VideoClip backgroundVideo = null, GameSettingsRuntime settingsRuntime = null)
        {
            Unsubscribe();
            network = session;
            settings = settingsRuntime != null ? settingsRuntime : GameSettingsRuntime.Ensure();
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
                // **씬이 준 주소를 넘기지 않는다** (S15P21B202-584). 여기서 넘기던
                // `defaultServerAddress` 가 빌드 채널을 이겨서, 개발 빌드인데 로비만
                // 배포 서버로 갔다. 이제 `ResolveServerAddress` 가 실행 인자와
                // `BackendEndpoints` 만 본다.
                string address = NetworkSession.ResolveServerAddress();
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

        /// <summary>
        /// 게임 씬의 로컬 메뉴에서 설정 화면만 재사용한다. 네트워크나 로비 배경은 만들지 않는다.
        /// </summary>
        public void InitializeSettingsOnly(GameSettingsRuntime settingsRuntime = null)
        {
            Unsubscribe();
            network = null;
            settings = settingsRuntime != null ? settingsRuntime : GameSettingsRuntime.Ensure();
            editingSettings = settings.Current != null
                ? settings.Current.Clone() : GameSettingsData.CreateDefaults();
            editingSettings.Normalize();
            transition = null;
            view = LobbyView.Settings;
            settingsTab = SettingsTab.Graphics;
            settingsScroll = Vector2.zero;
            rebindingAction = null;
            error = string.Empty;
            notice = string.Empty;
            RefreshSettingsOptions();
            WarmFont();
        }

        private void OnEnable()
        {
            Font.textureRebuilt += OnFontRebuilt;
            Subscribe();
        }

        private void OnDisable()
        {
            if (editingSettings != null && settings != null)
                settings.RestoreSaved();
            editingSettings = null;
            rebindingAction = null;
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
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        void DrawScaledGui()
        {
            if (network == null && !IsSettingsOnlyOpen)
                return;

            EnsureStyles();
            if (view == LobbyView.Main)
                DrawMainMenu();
            else if (view == LobbyView.Multiplayer)
                DrawMultiplayer();
            else
                DrawSettings();
        }

        private void DrawMainMenu()
        {
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.75f, 1.35f);
            float buttonWidth = 330f * scale;
            float buttonHeight = 60f * scale;
            float gap = 14f * scale;
            float totalHeight = buttonHeight * 6f + gap * 5f;   // 테스트 버튼이 늘어 6칸 (S15P21B202-572)
            Rect safe = UiScale.SafeArea;
            float x = Mathf.Clamp(UiScale.Width * 0.16f, safe.xMin + 20f, safe.xMax - buttonWidth - 20f);
            float y = Mathf.Clamp(UiScale.Height * 0.54f, safe.yMin + 20f,
                safe.yMax - totalHeight - 20f);

            var panel = new Rect(x - 22f * scale, y - 42f * scale,
                buttonWidth + 44f * scale, totalHeight + 66f * scale);
            UiTheme.DrawPanel(panel, true, true);
            DrawLabel(new Rect(x, y - 35f * scale, buttonWidth, 26f * scale),
                "EXPLORATION COMMAND", smallStyle, UiTheme.Cyan);

            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 0, gap),
                    "혼자 하기", primaryButtonStyle))
                BeginSolo();
            // 조립을 손으로 하지 않고 바로 시험한다 (S15P21B202-572). 혼자 하기와 같은
            // 경로로 들어가되, 배치만 스폰 앞으로 모은다. 감추지 않는 이유는 팀 내부
            // 도구이기 때문이다 — 감추면 정작 필요할 때 못 쓴다.
            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 1, gap),
                    "테스트 (조립 준비됨)", buttonStyle))
            {
                Metagame.Gameplay.TestArena.Requested = true;
                BeginSolo();
            }
            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 2, gap),
                    "함께 하기", buttonStyle))
            {
                CustomizerFlow.Begin(true);
            }
            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 3, gap),
                    "설정", buttonStyle))
            {
                OpenSettings();
            }
            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 4, gap),
                    "우주선 연습실", buttonStyle))
                LobbyTransition.Ensure().BeginPractice();
            if (UiInteraction.Button(ButtonRect(x, y, buttonWidth, buttonHeight, 5, gap),
                    "종료하기", quietButtonStyle))
                Application.Quit();

            DrawMessage(new Rect(x, y + totalHeight + 10f * scale, buttonWidth, 42f * scale));
        }

        private void OpenSettings()
        {
            settings = settings != null ? settings : GameSettingsRuntime.Ensure();
            editingSettings = settings.Current != null
                ? settings.Current.Clone() : GameSettingsData.CreateDefaults();
            editingSettings.Normalize();
            RefreshSettingsOptions();
            settingsScroll = Vector2.zero;
            rebindingAction = null;
            error = string.Empty;
            notice = string.Empty;
            settingsTab = SettingsTab.Graphics;
            view = LobbyView.Settings;
        }

        private void DrawSettings()
        {
            if (settings == null || editingSettings == null)
            {
                view = LobbyView.Main;
                return;
            }

            CaptureBindingInput();

            float width = Mathf.Min(1040f, UiScale.Width - 36f);
            float height = Mathf.Min(840f, UiScale.Height - 36f);
            float x = (UiScale.Width - width) * 0.5f;
            float y = (UiScale.Height - height) * 0.5f;
            UiTheme.DrawDimmer(new Rect(0f, 0f, UiScale.Width, UiScale.Height));
            UiTheme.DrawPanel(new Rect(x, y, width, height));
            DrawLabel(new Rect(x + 30f, y + 20f, width - 260f, 44f),
                "설정", titleStyle, UiTheme.Text);
            DrawLabel(new Rect(x + 32f, y + 63f, width - 64f, 24f),
                "변경값은 즉시 미리보기로 적용되며 저장 버튼을 눌러야 파일에 기록됩니다.",
                smallStyle, UiTheme.MutedText);

            if (UiInteraction.Button(new Rect(x + width - 226f, y + 24f, 92f, 40f),
                    "기본값", quietButtonStyle))
                ResetSettingsPreview();
            if (UiInteraction.Button(new Rect(x + width - 126f, y + 24f, 92f, 40f),
                    "취소", quietButtonStyle))
            {
                CancelSettings();
                return;
            }

            DrawSettingsTabs(new Rect(x + 32f, y + 92f, width - 64f, 38f));

            Rect viewport = new Rect(x + 24f, y + 142f, width - 48f, height - 212f);
            Rect content = new Rect(0f, 0f, Mathf.Max(900f, viewport.width - 24f),
                settingsTab == SettingsTab.Controls ? 980f : 420f);
            settingsScroll = GUI.BeginScrollView(viewport, settingsScroll, content);

            float left = 22f;
            float rowWidth = content.width - 44f;
            float rowY = 16f;

            if (settingsTab == SettingsTab.Graphics)
            {
                DrawSettingsSection(new Rect(left, rowY, rowWidth, 30f), "그래픽");
                rowY += 38f;
                DrawDisplaySettings(left, ref rowY, rowWidth);
            }
            else if (settingsTab == SettingsTab.Audio)
            {
                DrawSettingsSection(new Rect(left, rowY, rowWidth, 30f), "오디오");
                rowY += 38f;
                DrawAudioSettings(left, ref rowY, rowWidth);
            }
            else
            {
                DrawSettingsSection(new Rect(left, rowY, rowWidth, 30f), "조작");
                rowY += 38f;
                DrawControlSettings(left, ref rowY, rowWidth);
            }

            GUI.EndScrollView();

            if (UiInteraction.Button(new Rect(x + width - 218f, y + height - 56f, 184f, 42f),
                    "저장", primaryButtonStyle))
                SaveSettings();

            if (!string.IsNullOrEmpty(error))
                DrawLabel(new Rect(x + 34f, y + height - 54f, width - 270f, 36f),
                    error, smallStyle, new Color(1f, 0.58f, 0.48f));
        }

        private void DrawSettingsTabs(Rect area)
        {
            float gap = 8f;
            float tabWidth = (area.width - gap * 2f) / 3f;
            DrawSettingsTab(new Rect(area.x, area.y, tabWidth, area.height),
                SettingsTab.Graphics, "그래픽");
            DrawSettingsTab(new Rect(area.x + tabWidth + gap, area.y, tabWidth, area.height),
                SettingsTab.Audio, "오디오");
            DrawSettingsTab(new Rect(area.x + (tabWidth + gap) * 2f, area.y,
                tabWidth, area.height), SettingsTab.Controls, "조작");
        }

        private void DrawSettingsTab(Rect area, SettingsTab tab, string label)
        {
            bool selected = settingsTab == tab;
            if (selected)
                UiTheme.DrawSection(area);

            if (UiInteraction.Button(area, label, selected ? buttonStyle : smallButtonStyle)
                && !selected)
            {
                settingsTab = tab;
                settingsScroll = Vector2.zero;
                error = string.Empty;
            }
        }

        private void DrawDisplaySettings(float x, ref float y, float width)
        {
            DrawLabel(new Rect(x, y, 230f, 30f), "UI 텍스트 크기 조정", labelStyle, Color.white);
            float textScale = UiInteraction.HorizontalSlider(new Rect(x + 250f, y + 7f, 300f, 20f),
                editingSettings.display.uiTextScale, 0.8f, 1.5f);
            textScale = Mathf.Round(textScale * 20f) / 20f;
            DrawLabel(new Rect(x + 570f, y, 90f, 30f),
                Mathf.RoundToInt(textScale * 100f) + "%", smallStyle, Color.white);
            if (!Mathf.Approximately(textScale, editingSettings.display.uiTextScale))
            {
                editingSettings.display.uiTextScale = textScale;
                ApplySettingsPreview();
            }
            y += 38f;

            DrawLabel(new Rect(x, y, 190f, 30f), "해상도", labelStyle, Color.white);
            DrawLabel(new Rect(x + 210f, y, 210f, 30f),
                editingSettings.display.width + " × " + editingSettings.display.height,
                smallStyle, new Color(0.82f, 0.92f, 1f));
            if (UiInteraction.Button(new Rect(x + width - 174f, y, 76f, 30f), "이전", smallButtonStyle))
                CycleResolution(-1);
            if (UiInteraction.Button(new Rect(x + width - 86f, y, 76f, 30f), "다음", smallButtonStyle))
                CycleResolution(1);
            y += 38f;

            DrawLabel(new Rect(x, y, 190f, 30f), "화면 모드", labelStyle, Color.white);
            DrawLabel(new Rect(x + 210f, y, 260f, 30f),
                FullscreenModeLabel(editingSettings.display.fullscreenMode),
                smallStyle, new Color(0.82f, 0.92f, 1f));
            if (UiInteraction.Button(new Rect(x + width - 86f, y, 76f, 30f), "변경", smallButtonStyle))
                CycleFullscreenMode(1);
            y += 38f;

            DrawLabel(new Rect(x, y, 190f, 30f), "품질", labelStyle, Color.white);
            string quality = editingSettings.display.qualityLevel >= 0
                          && editingSettings.display.qualityLevel < qualityOptions.Length
                ? qualityOptions[editingSettings.display.qualityLevel] : "기본값";
            DrawLabel(new Rect(x + 210f, y, 260f, 30f), quality,
                smallStyle, new Color(0.82f, 0.92f, 1f));
            if (UiInteraction.Button(new Rect(x + width - 86f, y, 76f, 30f), "변경", smallButtonStyle))
                CycleQuality(1);
            y += 38f;

            bool vSync = UiInteraction.Toggle(new Rect(x, y, width - 12f, 30f),
                editingSettings.display.vSync, "수직 동기화", labelStyle);
            if (vSync != editingSettings.display.vSync)
            {
                editingSettings.display.vSync = vSync;
                ApplySettingsPreview();
            }
            y += 38f;

            DrawLabel(new Rect(x, y, 190f, 30f), "프레임 제한", labelStyle, Color.white);
            DrawLabel(new Rect(x + 210f, y, 260f, 30f),
                FrameRateLabel(editingSettings.display.frameRateLimit),
                smallStyle, new Color(0.82f, 0.92f, 1f));
            if (UiInteraction.Button(new Rect(x + width - 86f, y, 76f, 30f), "변경", smallButtonStyle))
                CycleFrameRate(1);
            y += 38f;
        }

        private void DrawAudioSettings(float x, ref float y, float width)
        {
            float master = UiInteraction.HorizontalSlider(new Rect(x + 210f, y + 7f, 300f, 20f),
                editingSettings.audio.masterVolume, 0f, 1f);
            DrawLabel(new Rect(x, y, 190f, 30f), "전체 음량", labelStyle, Color.white);
            DrawLabel(new Rect(x + 530f, y, 90f, 30f),
                Mathf.RoundToInt(master * 100f) + "%", smallStyle,
                new Color(0.82f, 0.92f, 1f));
            if (!Mathf.Approximately(master, editingSettings.audio.masterVolume))
            {
                editingSettings.audio.masterVolume = master;
                ApplySettingsPreview();
            }
            y += 38f;

            float sfx = UiInteraction.HorizontalSlider(new Rect(x + 210f, y + 7f, 300f, 20f),
                editingSettings.audio.sfxVolume, 0f, 1f);
            DrawLabel(new Rect(x, y, 190f, 30f), "효과음", labelStyle, Color.white);
            DrawLabel(new Rect(x + 530f, y, 90f, 30f),
                Mathf.RoundToInt(sfx * 100f) + "%", smallStyle,
                new Color(0.82f, 0.92f, 1f));
            if (!Mathf.Approximately(sfx, editingSettings.audio.sfxVolume))
            {
                editingSettings.audio.sfxVolume = sfx;
                ApplySettingsPreview();
            }
            y += 38f;

            float music = UiInteraction.HorizontalSlider(new Rect(x + 210f, y + 7f, 300f, 20f),
                editingSettings.audio.musicVolume, 0f, 1f);
            DrawLabel(new Rect(x, y, 190f, 30f), "음악", labelStyle, Color.white);
            DrawLabel(new Rect(x + 530f, y, 90f, 30f),
                Mathf.RoundToInt(music * 100f) + "%", smallStyle,
                new Color(0.82f, 0.92f, 1f));
            if (!Mathf.Approximately(music, editingSettings.audio.musicVolume))
            {
                editingSettings.audio.musicVolume = music;
                ApplySettingsPreview();
            }
            y += 38f;
            DrawDisabledAudioRow(x, ref y, width, "음성", "음성 채팅 시스템 준비 중");
            DrawDisabledAudioRow(x, ref y, width, "마이크", "음성 채팅 시스템 준비 중");
        }

        private void DrawDisabledAudioRow(float x, ref float y, float width,
                                          string label, string reason)
        {
            DrawLabel(new Rect(x, y, 190f, 30f), label, labelStyle,
                new Color(0.50f, 0.56f, 0.64f));
            bool previous = GUI.enabled;
            GUI.enabled = false;
            UiInteraction.HorizontalSlider(new Rect(x + 210f, y + 7f, 300f, 20f), 1f, 0f, 1f);
            GUI.enabled = previous;
            DrawLabel(new Rect(x + 530f, y, width - 530f, 30f), reason,
                smallStyle, new Color(0.50f, 0.56f, 0.64f));
            y += 38f;
        }

        private void DrawControlSettings(float x, ref float y, float width)
        {
            float sensitivity = UiInteraction.HorizontalSlider(new Rect(x + 210f, y + 7f, 300f, 20f),
                editingSettings.controls.lookSensitivity, 0.1f, 5f);
            DrawLabel(new Rect(x, y, 190f, 30f), "마우스 감도", labelStyle, Color.white);
            DrawLabel(new Rect(x + 530f, y, 90f, 30f), sensitivity.ToString("0.0"),
                smallStyle, new Color(0.82f, 0.92f, 1f));
            if (!Mathf.Approximately(sensitivity, editingSettings.controls.lookSensitivity))
            {
                editingSettings.controls.lookSensitivity = sensitivity;
                ApplySettingsPreview();
            }
            y += 38f;

            bool invert = UiInteraction.Toggle(new Rect(x, y, width - 12f, 30f),
                editingSettings.controls.invertLookY, "Y축 반전", labelStyle);
            if (invert != editingSettings.controls.invertLookY)
            {
                editingSettings.controls.invertLookY = invert;
                ApplySettingsPreview();
            }
            y += 44f;

            DrawLabel(new Rect(x, y, width, 28f),
                "키 재매핑", labelStyle, new Color(0.72f, 0.91f, 1f));
            y += 34f;
            for (int i = 0; i < BindingActions.Length; i++)
            {
                DrawBindingRow(x, ref y, width, BindingActions[i]);
            }
        }

        private void DrawBindingRow(float x, ref float y, float width, BindingAction action)
        {
            DrawLabel(new Rect(x, y, 250f, 30f), BindingLabel(action), smallStyle, Color.white);
            string value = rebindingAction.HasValue && rebindingAction.Value == action
                ? "입력 대기 중 (Esc 취소)" : GetBinding(action).ToString();
            if (UiInteraction.Button(new Rect(x + 270f, y, Mathf.Min(300f, width - 270f), 30f),
                    value, smallButtonStyle))
                rebindingAction = action;
            y += 34f;
        }

        private void CaptureBindingInput()
        {
            if (!rebindingAction.HasValue || Event.current.type != EventType.KeyDown)
                return;

            if (Event.current.keyCode == KeyCode.Escape)
            {
                rebindingAction = null;
                Event.current.Use();
                return;
            }

            KeyCode key = Event.current.keyCode;
            if (key == KeyCode.None) return;

            var candidate = editingSettings.Clone();
            SetBinding(candidate.controls, rebindingAction.Value, key);
            if (Controls.HasDuplicateBindings(candidate.controls, out var duplicate))
            {
                error = "이미 사용 중인 키입니다: " + duplicate;
                Event.current.Use();
                return;
            }

            SetBinding(editingSettings.controls, rebindingAction.Value, key);
            rebindingAction = null;
            error = string.Empty;
            ApplySettingsPreview();
            Event.current.Use();
        }

        private void RefreshSettingsOptions()
        {
            resolutionOptions.Clear();
            Resolution[] resolutions = Screen.resolutions;
            for (int i = 0; i < resolutions.Length; i++)
            {
                var value = new Vector2Int(resolutions[i].width, resolutions[i].height);
                if (!resolutionOptions.Contains(value)) resolutionOptions.Add(value);
            }

            var current = new Vector2Int(editingSettings.display.width, editingSettings.display.height);
            if (resolutionOptions.Count == 0 || !resolutionOptions.Contains(current))
                resolutionOptions.Add(current);
            qualityOptions = QualitySettings.names ?? new string[0];
        }

        private void CycleResolution(int direction)
        {
            if (resolutionOptions.Count == 0) return;
            var current = new Vector2Int(editingSettings.display.width, editingSettings.display.height);
            int index = resolutionOptions.IndexOf(current);
            if (index < 0) index = 0;
            index = (index + direction + resolutionOptions.Count) % resolutionOptions.Count;
            editingSettings.display.width = resolutionOptions[index].x;
            editingSettings.display.height = resolutionOptions[index].y;
            ApplySettingsPreview();
        }

        private void CycleFullscreenMode(int direction)
        {
            int[] modes = { (int)FullScreenMode.Windowed, (int)FullScreenMode.FullScreenWindow,
                            (int)FullScreenMode.ExclusiveFullScreen,
                            (int)FullScreenMode.MaximizedWindow };
            int index = System.Array.IndexOf(modes, editingSettings.display.fullscreenMode);
            if (index < 0) index = 0;
            index = (index + direction + modes.Length) % modes.Length;
            editingSettings.display.fullscreenMode = modes[index];
            ApplySettingsPreview();
        }

        private void CycleQuality(int direction)
        {
            if (qualityOptions.Length == 0) return;
            int index = Mathf.Clamp(editingSettings.display.qualityLevel, 0, qualityOptions.Length - 1);
            index = (index + direction + qualityOptions.Length) % qualityOptions.Length;
            editingSettings.display.qualityLevel = index;
            ApplySettingsPreview();
        }

        private void CycleFrameRate(int direction)
        {
            int[] values = { 0, 30, 60, 120, 144, 240 };
            int index = System.Array.IndexOf(values, editingSettings.display.frameRateLimit);
            if (index < 0) index = 0;
            index = (index + direction + values.Length) % values.Length;
            editingSettings.display.frameRateLimit = values[index];
            ApplySettingsPreview();
        }

        private void ApplySettingsPreview()
        {
            editingSettings.Normalize();
            settings.Preview(editingSettings);
            editingSettings = settings.Current.Clone();
        }

        private void ResetSettingsPreview()
        {
            editingSettings = settings.ResetDefaults();
            RefreshSettingsOptions();
            rebindingAction = null;
            error = string.Empty;
        }

        private void CancelSettings()
        {
            settings.RestoreSaved();
            editingSettings = null;
            rebindingAction = null;
            error = string.Empty;
            notice = string.Empty;
            view = LobbyView.Main;
        }

        private void SaveSettings()
        {
            if (Controls.HasDuplicateBindings(editingSettings.controls, out var duplicate))
            {
                error = "중복된 키를 먼저 변경해 주세요: " + duplicate;
                return;
            }

            if (!settings.Save(editingSettings, out var saveError))
            {
                error = saveError;
                return;
            }

            editingSettings = null;
            rebindingAction = null;
            error = string.Empty;
            notice = "설정을 저장했습니다.";
            view = LobbyView.Main;
        }

        private void DrawSettingsSection(Rect area, string title)
        {
            UiTheme.DrawSection(area);
            DrawLabel(area, title, labelStyle, UiTheme.CyanSoft);
        }

        private static string FullscreenModeLabel(int value)
        {
            switch ((FullScreenMode)value)
            {
                case FullScreenMode.Windowed: return "창 모드";
                case FullScreenMode.ExclusiveFullScreen: return "전체 화면";
                case FullScreenMode.MaximizedWindow: return "최대화 창";
                default: return "테두리 없는 창";
            }
        }

        private static string FrameRateLabel(int value) => value <= 0 ? "제한 없음" : value + " FPS";

        private static string BindingLabel(BindingAction action)
        {
            switch (action)
            {
                case BindingAction.MoveForward: return "앞으로 이동";
                case BindingAction.MoveBack: return "뒤로 이동";
                case BindingAction.MoveLeft: return "왼쪽 이동";
                case BindingAction.MoveRight: return "오른쪽 이동";
                case BindingAction.Jump: return "점프";
                case BindingAction.Run: return "달리기";
                case BindingAction.ArmsUp: return "팔 올리기";
                case BindingAction.Prone: return "엎드리기";
                case BindingAction.FreeLook: return "자유 시점";
                case BindingAction.TransportAction: return "상호작용 / 탑승";
                case BindingAction.ToolAction: return "장비 동작";
                case BindingAction.Tablet: return "태블릿";
                case BindingAction.FieldToggle: return "저중력장";
                case BindingAction.StuckEscape: return "끼임 탈출";
                case BindingAction.Restart: return "개인 리스폰";
                case BindingAction.TabletPreviousPage: return "태블릿 이전";
                case BindingAction.TabletNextPage: return "태블릿 다음";
                default: return "커서 해제";
            }
        }

        private static KeyCode GetBinding(BindingAction action)
        {
            switch (action)
            {
                case BindingAction.MoveForward: return Controls.MoveForward;
                case BindingAction.MoveBack: return Controls.MoveBack;
                case BindingAction.MoveLeft: return Controls.MoveLeft;
                case BindingAction.MoveRight: return Controls.MoveRight;
                case BindingAction.Jump: return Controls.Jump;
                case BindingAction.Run: return Controls.Run;
                case BindingAction.ArmsUp: return Controls.ArmsUp;
                case BindingAction.Prone: return Controls.Prone;
                case BindingAction.FreeLook: return Controls.FreeLook;
                case BindingAction.TransportAction: return Controls.TransportAction;
                case BindingAction.ToolAction: return Controls.ToolAction;
                case BindingAction.Tablet: return Controls.Tablet;
                case BindingAction.FieldToggle: return Controls.FieldToggle;
                case BindingAction.StuckEscape: return Controls.StuckEscape;
                case BindingAction.Restart: return Controls.Restart;
                case BindingAction.TabletPreviousPage: return Controls.TabletPreviousPage;
                case BindingAction.TabletNextPage: return Controls.TabletNextPage;
                default: return Controls.FreeCursor;
            }
        }

        private static void SetBinding(GameSettingsData.ControlSettings settings,
                                       BindingAction action, KeyCode value)
        {
            switch (action)
            {
                case BindingAction.MoveForward: settings.moveForward = value; break;
                case BindingAction.MoveBack: settings.moveBack = value; break;
                case BindingAction.MoveLeft: settings.moveLeft = value; break;
                case BindingAction.MoveRight: settings.moveRight = value; break;
                case BindingAction.Jump: settings.jump = value; break;
                case BindingAction.Run: settings.run = value; break;
                case BindingAction.ArmsUp: settings.armsUp = value; break;
                case BindingAction.Prone: settings.prone = value; break;
                case BindingAction.FreeLook: settings.freeLook = value; break;
                case BindingAction.TransportAction: settings.transportAction = value; break;
                case BindingAction.ToolAction: settings.toolAction = value; break;
                case BindingAction.Tablet: settings.tablet = value; break;
                case BindingAction.FieldToggle: settings.fieldToggle = value; break;
                case BindingAction.StuckEscape: settings.stuckEscape = value; break;
                case BindingAction.Restart: settings.restart = value; break;
                case BindingAction.TabletPreviousPage: settings.tabletPreviousPage = value; break;
                case BindingAction.TabletNextPage: settings.tabletNextPage = value; break;
                case BindingAction.FreeCursor: settings.freeCursor = value; break;
            }
        }

        private void BeginSolo()
        {
            if (transition == null)
                transition = LobbyTransition.Ensure();

            error = string.Empty;
            notice = string.Empty;
            network.DisconnectLobby();
            CustomizerFlow.Begin(false);
        }

        public void OpenMultiplayer() { view = LobbyView.Multiplayer; }

        private void DrawMultiplayer()
        {
            float width = Mathf.Min(780f, UiScale.Width - 40f);
            float height = Mathf.Min(700f, UiScale.Height - 40f);
            float x = (UiScale.Width - width) * 0.5f;
            float y = (UiScale.Height - height) * 0.5f;

            UiTheme.DrawPanel(new Rect(x, y, width, height));
            DrawLabel(new Rect(x + 34f, y + 22f, width - 190f, 42f),
                "함께 하기", titleStyle, UiTheme.Text);
            DrawLabel(new Rect(x + 36f, y + 68f, width - 72f, 26f),
                "친구에게 방 코드를 공유해 함께 출발하세요", smallStyle,
                UiTheme.MutedText);

            if (UiInteraction.Button(new Rect(x + width - 142f, y + 24f, 106f, 40f),
                    "메인으로", smallButtonStyle))
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == CustomizerFlow.SceneName)
                {
                    transition.BeginTitle();
                    return;
                }
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
            nickname = UiInteraction.TextField(new Rect(x + 236f, y + 151f, width - 290f, 40f),
                nickname ?? string.Empty, 12, textFieldStyle);
            DrawLabel(new Rect(x + 54f, y + 208f, width - 108f, 24f),
                "최대 12자 · 게스트로 바로 시작합니다", smallStyle,
                new Color(0.62f, 0.74f, 0.86f));

            bool canLogin = network.State != NetworkConnectionState.LoggingIn
                         && network.State != NetworkConnectionState.Connecting;
            GUI.enabled = canLogin;
            if (UiInteraction.Button(new Rect(x + 54f, y + 270f, width - 108f, 54f),
                    "게스트 로그인", primaryButtonStyle))
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
            if (UiInteraction.Button(new Rect(x + 54f, y + 274f, width - 108f, 52f),
                    "다시 연결", buttonStyle))
            {
                error = string.Empty;
                network.Connect();
            }
            GUI.enabled = true;

            if (UiInteraction.Button(new Rect(x + 54f, y + 348f, width - 108f, 42f),
                    "게스트 로그아웃", smallButtonStyle))
                LogoutGuest();
        }

        private void DrawRoomEntry(float x, float y, float width)
        {
            DrawLabel(new Rect(x + 54f, y + 144f, width - 108f, 30f),
                "로그인 완료: " + network.Nickname + "  (유저 " + network.UserId + ")",
                labelStyle, Color.white);

            if (UiInteraction.Button(new Rect(x + 54f, y + 202f, width - 108f, 54f),
                    "새 방 만들기", primaryButtonStyle))
            {
                error = string.Empty;
                network.CreateRoom();
            }

            DrawLabel(new Rect(x + 54f, y + 302f, 170f, 30f), "방 코드로 입장", labelStyle,
                Color.white);
            GUI.SetNextControlName("LobbyRoomCode");
            roomIdInput = UiInteraction.TextField(new Rect(x + 236f, y + 297f, width - 290f, 40f),
                roomIdInput ?? string.Empty, 6, textFieldStyle);
            if (UiInteraction.Button(new Rect(x + 54f, y + 365f, width - 108f, 54f),
                    "방 입장", primaryButtonStyle))
            {
                error = string.Empty;
                network.JoinRoom((roomIdInput ?? string.Empty).Trim().ToUpperInvariant());
            }

            if (UiInteraction.Button(new Rect(x + 54f, y + 438f, width - 108f, 42f),
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
            if (UiInteraction.Button(new Rect(x + width - 132f, y + 130f, 78f, 38f),
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
            if (UiInteraction.Button(new Rect(x + 54f, controlsY, width * 0.40f, 48f),
                    network.IsLocalReady ? "준비 취소" : "준비", buttonStyle))
                network.SetReady(!network.IsLocalReady);

            bool canStart = isHost && room.IsWaiting && room.AllReady;
            GUI.enabled = canStart;
            if (UiInteraction.Button(new Rect(x + width * 0.52f, controlsY, width * 0.40f, 48f),
                    "게임 시작", primaryButtonStyle))
                network.StartGame();
            GUI.enabled = true;

            // 게임 중인 방에 앉아 있는데 매치로 못 들어간 상태를 설명한다 (S15P21B202-413).
            //
            // <b>버튼이 죽어 있는 것만 보이면 사용자는 고장으로 읽는다.</b> 방장이 사라져
            // 이전이 진행되는 동안 서버는 접속 정보를 주지 않고(S15P21B202-401), 그 사이
            // 이 화면에는 "상태: 게임 중" 과 비활성 시작 버튼만 남는다 — 왜 기다려야 하는지
            // 알 길이 없었다.
            //
            // 방장인지에 따라 할 일이 다르다. 방장이면 이어받기가 곧 시작되므로 기다리면
            // 되고, 참가자라면 새 방장이 이어받을 때까지 기다리는 것이다.
            float waitNotice = 0f;
            if (!room.IsWaiting)
            {
                waitNotice = 30f;
                DrawLabel(new Rect(x + 54f, controlsY + 56f, width - 108f, 26f),
                    isHost
                        ? "게임에 다시 들어가는 중입니다 · 잠시만 기다려 주세요"
                        : "방장이 바뀌는 중입니다 · 잠시만 기다려 주세요",
                    smallStyle, new Color(1f, 0.86f, 0.5f));
            }

            if (UiInteraction.Button(new Rect(x + 54f, controlsY + 64f + waitNotice, width - 108f, 40f),
                    "방 나가기", smallButtonStyle))
            {
                error = string.Empty;
                network.LeaveRoom();
            }

            if (UiInteraction.Button(new Rect(x + 54f, controlsY + 116f + waitNotice, width - 108f, 40f),
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
            UiTheme.Ensure();

            titleStyle = MakeLabelStyle(29, TextAnchor.MiddleLeft, FontStyle.Bold);
            labelStyle = MakeLabelStyle(19, TextAnchor.MiddleLeft, FontStyle.Normal);
            smallStyle = MakeLabelStyle(15, TextAnchor.MiddleLeft, FontStyle.Normal);
            buttonStyle = UiTheme.ButtonStyle(FontAt(20), 20);
            smallButtonStyle = UiTheme.ButtonStyle(FontAt(16), 16);
            primaryButtonStyle = UiTheme.ButtonStyle(FontAt(20), 20, true);
            quietButtonStyle = UiTheme.ButtonStyle(FontAt(16), 16, false, true);
            textFieldStyle = UiTheme.TextFieldStyle(FontAt(16), 16);
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
            return UiTheme.ButtonStyle(FontAt(size), size);
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
            characters.Append("설정 변경값은 즉시 미리보기로 적용되며 저장 버튼을 눌러야 파일에 기록됩니다");
            characters.Append("그래픽 오디오 조작 해상도 화면 모드 품질 수직 동기화 프레임 제한 전체 음량 효과음 음악 음성 마이크");
            characters.Append("음악 시스템 준비 중 음성 채팅 시스템 준비 중 마우스 감도 Y축 반전 키 재매핑 입력 대기 중 취소 저장 기본값");
            characters.Append("앞으로 이동 뒤로 이동 왼쪽 이동 오른쪽 이동 점프 달리기 팔 올리기 자유 시점 상호작용 탑승 장비 동작 태블릿 저중력장 끼임 탈출 재시작 이전 다음 커서 해제");
            characters.Append("이미 사용 중인 키입니다 중복된 키를 먼저 변경해 주세요 제한 없음 창 모드 전체 화면 최대화 창 테두리 없는 창");
            characters.Append("설정을 저장했습니다 설정을 저장하지 못했습니다");
            // 입장 실패 사유와 이전 대기 안내 (S15P21B202-413). 이 목록에 없는 글자는
            // 예열되지 않으므로 새 문구를 넣을 때마다 여기도 더해야 한다.
            characters.Append(".,인원이 다 찼습니다 이미 시작된 방입니다 끝나면 다시 시도해 주세요");
            characters.Append("그런 방이 없습니다 코드를 확인해 주세요 판에 참가했던 사람만 돌아올 수 있습니다");
            characters.Append("게임에 들어가는 중입니다 잠시만 기다려 주세요 방장이 바뀌는 중입니다");

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
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
