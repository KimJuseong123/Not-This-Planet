using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;

namespace Metagame.UI
{
    /// <summary>
    /// 1장 탐험용 태블릿 화면.
    ///
    /// 기존 HUD를 교체하지 않고 별도 오버레이로 붙인다. 태블릿이 열려 있을 때만
    /// 카탈로그의 네 우주선 파츠와 수동 도움말을 카드와 단순화한 실루엣으로 보여 준다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1ExplorationHud : MonoBehaviour
    {
        const int SizeSmall = 15;
        const int SizeMid = 20;
        const int SizeTitle = 30;
        const FontStyle Style = FontStyle.Normal;

        struct HelpEntry
        {
            public readonly string Keys;
            public readonly string Description;

            public HelpEntry(string keys, string description)
            {
                Keys = keys;
                Description = description;
            }
        }

        static readonly HelpEntry[] MovementHelp =
        {
            new HelpEntry("WASD", "이동"),
            new HelpEntry("Shift", "달리기"),
            new HelpEntry("Space", "점프"),
            new HelpEntry("마우스", "시점과 캐릭터 정면·조준 기준"),
            new HelpEntry("Alt", "이동·조준·캐릭터 정면은 카메라를 따르고 시점 자유롭게 돌리기"),
        };

        static readonly HelpEntry[] InteractionHelp =
        {
            new HelpEntry("좌·우클릭", "고정쇠에 손을 결합하고 반대 방향으로 뒤로 이동"),
            new HelpEntry("Ctrl", "팔을 위로 올려 레일·케이블 잡기"),
            new HelpEntry("W + 잡기", "잡은 레일·케이블을 따라 기어오르기"),
            new HelpEntry("Q 짧게", "현재 손 장비 내려놓기"),
            new HelpEntry("Q 길게", "충전 후 바라보는 방향으로 장비 투척"),
        };

        static readonly HelpEntry[] TransportHelp =
        {
            new HelpEntry("E", "장비 줍기·교환, 동료 구조, 운반 장치 탑승·하차"),
            new HelpEntry("WASD", "탑승 중 운반 장치 운전"),
            new HelpEntry("좌·우클릭", "탑승 중 견인 빔 사용"),
            new HelpEntry("Space / Ctrl", "탑승 중 상승 / 하강"),
            new HelpEntry("T", "그 자리에서 다시 서기"),
            new HelpEntry("R", "처음부터 다시 시작"),
            new HelpEntry("Tab", "태블릿 열기·닫기"),
            new HelpEntry("← → / A D", "이전·다음 태블릿 페이지"),
            new HelpEntry("Esc", "커서 잠금 풀기"),
        };

        static readonly Dictionary<int, Font> Fonts = new();

        Stage1Exploration _exploration;
        GameSession _session;
        GUIStyle _small;
        GUIStyle _mid;
        GUIStyle _title;
        GUIStyle _body;
        Texture2D _white;
        Texture2D _arrowLeft;
        Texture2D _arrowRight;
        Stage1TabletView _tabletView;
        bool _warmed;

        /// <summary>부트스트랩이 기존 HUD와 함께 명시적으로 연결한다.</summary>
        public void Initialize(Stage1Exploration exploration)
        {
            Initialize(exploration, GameSession.Instance);
        }

        /// <summary>탐험 상태와 현재 단계 안내를 태블릿에 연결한다.</summary>
        public void Initialize(Stage1Exploration exploration, GameSession session)
        {
            _exploration = exploration;
            _session = session;
        }

        /// <summary>현재 플레이어 카메라에 태블릿 3D 모델을 연결한다.</summary>
        public void AttachTablet(Camera camera)
        {
            if (_tabletView != null) Destroy(_tabletView.gameObject);
            _tabletView = Stage1TabletView.Create(camera, _exploration);
        }

        void Awake()
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            _arrowLeft = CreateArrowTexture(false);
            _arrowRight = CreateArrowTexture(true);
        }

        void Update()
        {
            if (!_warmed && _exploration != null) WarmFont();
        }

        void OnGUI()
        {
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        void DrawScaledGui()
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout
                && Event.current.type != EventType.MouseDown && Event.current.type != EventType.MouseUp)
                return;

            if (_exploration == null) return;

            EnsureStyles();

            var exploration = _exploration;
            if (exploration.TabletOpen)
                DrawTablet(exploration);
        }

        void DrawTablet(Stage1Exploration exploration)
        {
            if (_tabletView != null)
                return;

            var width = UiScale.Width;
            var height = UiScale.Height;

            Box(new Rect(0f, 0f, width, height), new Color(0.02f, 0.04f, 0.07f, 0.82f));

            var panelWidth = Mathf.Min(1080f, width - 48f);
            var panelHeight = Mathf.Min(700f, height - 48f);
            var panel = new Rect((width - panelWidth) * 0.5f, (height - panelHeight) * 0.5f,
                                 panelWidth, panelHeight);

            DrawTabletPanel(panel, exploration);
        }

        void DrawTabletPanel(Rect panel, Stage1Exploration exploration)
        {

            Box(panel, new Color(0.07f, 0.12f, 0.17f, 0.98f));
            Box(new Rect(panel.x, panel.y, panel.width, 5f), new Color(0.28f, 0.82f, 1f, 0.95f));

            var page = Mathf.Clamp(exploration.TabletPage, 0, Stage1Exploration.TabletPageCount - 1);
            var title = page == 0 ? "탐사 태블릿 · 파츠 상태" : PageTitle(page);
            L(new Rect(panel.x + 30f, panel.y + 24f, panel.width - 260f, 42f),
              title, _title, new Color(0.80f, 0.95f, 1f));

            if (page == 0)
                DrawDiscoveryDots(new Rect(panel.x + panel.width - 150f, panel.y + 38f, 120f, 12f),
                                  exploration.DiscoveredCount);
            else
                L(new Rect(panel.x + panel.width - 190f, panel.y + 32f, 160f, 28f),
                  "DEBUG", _mid, new Color(0.55f, 1f, 0.70f));

            if (page == 0)
                DrawPartsPage(panel, exploration);
            else
                DrawHelpPage(panel, page);

            DrawPageControls(panel, page, exploration);
        }

        void DrawPartsPage(Rect panel, Stage1Exploration exploration)
        {
            var gap = 18f;
            var cardWidth = (panel.width - 60f - gap) * 0.5f;
            var cardHeight = Mathf.Max(120f, (panel.height - 176f - gap) * 0.5f);
            for (var i = 0; i < exploration.Parts.Count; i++)
            {
                var column = i % 2;
                var row = i / 2;
                var card = new Rect(panel.x + 30f + column * (cardWidth + gap),
                                    panel.y + 112f + row * (cardHeight + gap),
                                    cardWidth, cardHeight);
                DrawPartCard(card, exploration.Parts[i]);
            }
        }

        void DrawPartCard(Rect card, Stage1Exploration.PartInfo info)
        {
            var discovered = info != null && info.IsDiscovered;
            var accent = PartAccent(info);

            Box(card, discovered
                ? new Color(0.09f, 0.19f, 0.20f, 0.98f)
                : new Color(0.09f, 0.13f, 0.18f, 0.98f));
            Box(new Rect(card.x, card.y, card.width, 4f), accent);

            if (info == null)
            {
                DrawStatusGlyph(new Rect(card.center.x - 20f, card.center.y - 20f, 40f, 40f), 4,
                                new Color(1f, 0.36f, 0.30f, 0.95f));
                return;
            }

            DrawSilhouette(new Rect(card.x + 18f, card.y + 24f, 140f, 124f), info.Kind, discovered);
            L(new Rect(card.x + 178f, card.y + 28f, card.width - 196f, 32f),
              info.Number.ToString("00"), _title, new Color(0.72f, 0.90f, 0.96f));

            var stateLevel = StateLevel(info);
            DrawStatusGlyph(new Rect(card.x + 178f, card.y + 82f, 116f, 26f), stateLevel, accent);
        }

        void DrawHelpPage(Rect panel, int page)
        {
            var entries = HelpForPage(page);
            var contentTop = panel.y + 112f;
            if (page == 3 && _session != null && !string.IsNullOrEmpty(_session.StageHint))
            {
                L(new Rect(panel.x + 30f, contentTop, panel.width - 60f, 46f),
                  "현재 단계 · " + _session.StageHint, _body, new Color(1f, 0.96f, 0.80f));
                contentTop += 60f;
            }

            var gap = 14f;
            var columns = 2;
            var rows = Mathf.CeilToInt(entries.Length / (float)columns);
            var contentHeight = panel.y + panel.height - 68f - contentTop;
            var cardWidth = (panel.width - 60f - gap) / columns;
            var cardHeight = Mathf.Max(58f, (contentHeight - gap * (rows - 1)) / rows);

            for (var i = 0; i < entries.Length; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var card = new Rect(panel.x + 30f + column * (cardWidth + gap),
                                    contentTop + row * (cardHeight + gap),
                                    cardWidth, cardHeight);
                DrawHelpCard(card, entries[i]);
            }
        }

        void DrawHelpCard(Rect card, HelpEntry entry)
        {
            Box(card, new Color(0.08f, 0.15f, 0.20f, 0.98f));
            Box(new Rect(card.x, card.y, card.width, 3f), new Color(0.30f, 0.84f, 1f, 0.90f));
            var keyWidth = Mathf.Min(132f, card.width * 0.34f);
            Box(new Rect(card.x + 12f, card.y + 14f, keyWidth, 34f),
                new Color(0.04f, 0.09f, 0.13f, 0.98f));
            L(new Rect(card.x + 20f, card.y + 19f, keyWidth - 16f, 24f),
              entry.Keys, _mid, new Color(0.65f, 0.95f, 1f));
            L(new Rect(card.x + keyWidth + 28f, card.y + 14f,
                      card.width - keyWidth - 40f, card.height - 24f),
              entry.Description, _body, new Color(0.86f, 0.91f, 0.95f));
        }

        void DrawPageControls(Rect panel, int page, Stage1Exploration exploration)
        {
            var left = new Rect(panel.x + 10f, panel.center.y - 32f, 44f, 64f);
            var right = new Rect(panel.x + panel.width - 54f, panel.center.y - 32f, 44f, 64f);
            var canPrevious = page > 0;
            var canNext = page < Stage1Exploration.TabletPageCount - 1;

            DrawArrow(left, _arrowLeft, canPrevious);
            DrawArrow(right, _arrowRight, canNext);

            // The arrow image is also a transparent IMGUI button so mouse clicks
            // and keyboard navigation share the same page state and boundaries.
            if (canPrevious && UiInteraction.Button(left, GUIContent.none, GUIStyle.none))
                exploration.PreviousTabletPage();
            if (canNext && UiInteraction.Button(right, GUIContent.none, GUIStyle.none))
                exploration.NextTabletPage();

            var width = Stage1Exploration.TabletPageCount * 12f;
            var start = panel.center.x - width * 0.5f;
            for (var i = 0; i < Stage1Exploration.TabletPageCount; i++)
                Box(new Rect(start + i * 12f, panel.y + panel.height - 34f, 7f, 7f),
                    i == page ? new Color(0.42f, 0.96f, 1f, 0.95f)
                              : new Color(0.28f, 0.42f, 0.50f, 0.95f));
        }

        void DrawDiscoveryDots(Rect area, int count)
        {
            for (var i = 0; i < Stage1Exploration.RequiredPartCount; i++)
                Box(new Rect(area.x + i * 24f, area.y, 12f, area.height),
                    i < count ? new Color(0.45f, 1f, 0.70f, 0.95f)
                              : new Color(0.24f, 0.36f, 0.42f, 0.95f));
        }

        void DrawStatusGlyph(Rect area, int level, Color accent)
        {
            var segments = 4;
            var gap = 5f;
            var segmentWidth = (area.width - gap * (segments - 1)) / segments;
            for (var i = 0; i < segments; i++)
                Box(new Rect(area.x + i * (segmentWidth + gap), area.y, segmentWidth, area.height),
                    i < level ? accent : new Color(0.12f, 0.20f, 0.25f, 0.98f));
        }

        static int StateLevel(Stage1Exploration.PartInfo info)
        {
            if (info == null || !info.IsDiscovered) return 0;
            if (info.Part == null) return 1;

            switch (info.Part.State)
            {
                case PartState.Locked: return 1;
                case PartState.Free: return 2;
                case PartState.Carried: return 3;
                case PartState.Placed:
                case PartState.Welded: return 4;
                default: return 1;
            }
        }

        static Color PartAccent(Stage1Exploration.PartInfo info)
        {
            if (info == null) return new Color(1f, 0.36f, 0.30f, 0.95f);
            if (!info.IsDiscovered) return new Color(0.30f, 0.42f, 0.52f, 0.95f);
            if (info.Part == null) return new Color(0.62f, 0.78f, 0.84f, 0.95f);

            return info.Part.State switch
            {
                PartState.Placed or PartState.Welded => new Color(0.32f, 1f, 0.64f, 0.95f),
                PartState.Free or PartState.Carried => new Color(0.38f, 0.88f, 1f, 0.95f),
                _ => new Color(0.62f, 0.78f, 0.84f, 0.95f),
            };
        }

        void DrawArrow(Rect area, Texture2D texture, bool enabled)
        {
            if (texture == null) return;

            var previous = GUI.color;
            GUI.color = enabled ? new Color(0.48f, 0.92f, 1f, 0.95f)
                                : new Color(0.24f, 0.34f, 0.39f, 0.45f);
            GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        static Texture2D CreateArrowTexture(bool right)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = right ? "TabletArrowRight" : "TabletArrowLeft",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                var distance = Mathf.Abs(y - center) / center;
                var boundary = Mathf.RoundToInt(Mathf.Lerp(4f, size - 5f, distance));
                for (var x = 0; x < size; x++)
                {
                    var filled = right ? x >= boundary : x <= size - 1 - boundary;
                    pixels[y * size + x] = filled ? Color.white : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static string PageTitle(int page)
        {
            switch (page)
            {
                case 1: return "조작 안내 · 이동과 시점";
                case 2: return "조작 안내 · 손과 도구";
                case 3: return "조작 안내 · 운반과 복구";
                default: return "탐사 태블릿";
            }
        }

        static HelpEntry[] HelpForPage(int page)
        {
            switch (page)
            {
                case 1: return MovementHelp;
                case 2: return InteractionHelp;
                case 3: return TransportHelp;
                default: return MovementHelp;
            }
        }

        static void AppendHelpText(StringBuilder text, HelpEntry[] entries)
        {
            for (var i = 0; i < entries.Length; i++)
                text.Append(entries[i].Keys).Append(entries[i].Description);
        }

        void DrawSilhouette(Rect area, Stage1Exploration.PartKind kind, bool discovered)
        {
            var color = discovered
                ? new Color(0.48f, 1f, 0.72f, 0.78f)
                : new Color(0.42f, 0.57f, 0.67f, 0.68f);
            var dark = new Color(0.06f, 0.13f, 0.17f, 0.90f);

            Box(area, dark);
            Box(new Rect(area.x, area.y + area.height - 2f, area.width, 2f), color);

            var cx = area.x + area.width * 0.5f;
            var cy = area.y + area.height * 0.5f;
            switch (kind)
            {
                case Stage1Exploration.PartKind.HoverPod:
                    Box(new Rect(cx - 34f, cy - 10f, 68f, 20f), color);
                    Box(new Rect(cx - 22f, cy - 24f, 44f, 14f), color);
                    Box(new Rect(cx - 48f, cy + 10f, 96f, 6f), color);
                    break;

                case Stage1Exploration.PartKind.Robot:
                    Box(new Rect(cx - 20f, cy - 28f, 40f, 22f), color);
                    Box(new Rect(cx - 28f, cy - 4f, 56f, 36f), color);
                    Box(new Rect(cx - 42f, cy - 1f, 14f, 7f), color);
                    Box(new Rect(cx + 28f, cy - 1f, 14f, 7f), color);
                    Box(new Rect(cx - 20f, cy + 32f, 12f, 18f), color);
                    Box(new Rect(cx + 8f, cy + 32f, 12f, 18f), color);
                    break;

                case Stage1Exploration.PartKind.Capsule:
                    Box(new Rect(cx - 28f, cy - 34f, 56f, 68f), color);
                    Box(new Rect(cx - 38f, cy - 4f, 76f, 8f), color);
                    Box(new Rect(cx - 12f, cy - 46f, 24f, 12f), color);
                    break;

                case Stage1Exploration.PartKind.TractorCraft:
                    Box(new Rect(cx - 42f, cy - 8f, 84f, 16f), color);
                    Box(new Rect(cx - 18f, cy - 22f, 36f, 14f), color);
                    Box(new Rect(cx - 8f, cy + 8f, 16f, 15f), color);
                    break;
            }
        }

        void EnsureStyles()
        {
            if (_title != null) return;

            _title = MakeStyle(SizeTitle, TextAnchor.MiddleLeft, false);
            _mid = MakeStyle(SizeMid, TextAnchor.UpperLeft, false);
            _small = MakeStyle(SizeSmall, TextAnchor.UpperLeft, false);
            _body = MakeStyle(SizeSmall, TextAnchor.UpperLeft, true);
        }

        void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }

        void L(Rect rect, string text, GUIStyle style, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.60f * color.a);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        void WarmFont()
        {
            _warmed = true;
            var fontName = KoreanFontName;
            if (string.IsNullOrEmpty(fontName)) return;

            var text = new StringBuilder();
            text.Append("0123456789./· 1장 탐험 태블릿 파츠 상태 조작 안내 이동과 시점 손과 도구 운반과 복구 DEBUG 현재 단계");
            text.Append("파츠 발견 미발견 상태 씬 배치 확인 고정 해제 운반 중 배치됨 조립 완료");
            text.Append("발견됩니다 이전 다음");
            AppendHelpText(text, MovementHelp);
            AppendHelpText(text, InteractionHelp);
            AppendHelpText(text, TransportHelp);
            if (_session != null) text.Append(_session.StageHint);

            foreach (var size in new[] { SizeSmall, SizeMid, SizeTitle })
            {
                var font = FontAt(size, fontName);
                if (font != null) font.RequestCharactersInTexture(text.ToString(), size, Style);
            }
        }

        static GUIStyle MakeStyle(int size, TextAnchor anchor, bool wordWrap)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = Style,
                alignment = anchor,
                wordWrap = wordWrap,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            var font = FontAt(size, KoreanFontName);
            if (font != null)
            {
                style.font = font;
                style.fontSize = 0;
            }

            return style;
        }

        static Font FontAt(int size, string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return null;
            if (Fonts.TryGetValue(size, out var cached) && cached != null) return cached;

            var font = Font.CreateDynamicFontFromOSFont(fontName, size);
            Fonts[size] = font;
            return font;
        }

        static string KoreanFontName
        {
            get
            {
                var wanted = new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic", "Gulim", "굴림" };
                var installed = Font.GetOSInstalledFontNames();
                if (installed == null) return null;

                foreach (var candidate in wanted)
                    foreach (var name in installed)
                        if (string.Equals(candidate, name, System.StringComparison.OrdinalIgnoreCase))
                            return name;

                foreach (var name in installed)
                    if (name.IndexOf("Gothic", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return name;

                return null;
            }
        }
    }
}
