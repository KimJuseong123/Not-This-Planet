using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.UI
{
    /// <summary>
    /// 1장 탐험용 태블릿 화면.
    ///
    /// 기존 HUD를 교체하지 않고 별도 오버레이로 붙인다. 태블릿이 닫혀 있을 때는
    /// 발견 알림만 보여 주고, 열려 있을 때는 카탈로그의 네 우주선 파츠를 카드와
    /// 단순화한 실루엣으로 보여 준다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1ExplorationHud : MonoBehaviour
    {
        const int SizeSmall = 15;
        const int SizeMid = 20;
        const int SizeTitle = 30;
        const FontStyle Style = FontStyle.Normal;

        static readonly Dictionary<int, Font> Fonts = new();

        Stage1Exploration _exploration;
        GUIStyle _small;
        GUIStyle _mid;
        GUIStyle _title;
        GUIStyle _body;
        Texture2D _white;
        bool _warmed;

        /// <summary>부트스트랩이 기존 HUD와 함께 명시적으로 연결한다.</summary>
        public void Initialize(Stage1Exploration exploration)
        {
            _exploration = exploration;
        }

        void Awake()
        {
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        void Update()
        {
            if (!_warmed && _exploration != null) WarmFont();
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;

            if (_exploration == null) return;

            EnsureStyles();

            var exploration = _exploration;
            if (exploration.TabletOpen)
                DrawTablet(exploration);
            else if (exploration.HasActiveNotice)
                DrawDiscoveryNotice(exploration);
        }

        void DrawTablet(Stage1Exploration exploration)
        {
            var width = Screen.width;
            var height = Screen.height;

            Box(new Rect(0f, 0f, width, height), new Color(0.02f, 0.04f, 0.07f, 0.82f));

            var panelWidth = Mathf.Min(1080f, width - 48f);
            var panelHeight = Mathf.Min(700f, height - 48f);
            var panel = new Rect((width - panelWidth) * 0.5f, (height - panelHeight) * 0.5f,
                                 panelWidth, panelHeight);

            Box(panel, new Color(0.07f, 0.12f, 0.17f, 0.98f));
            Box(new Rect(panel.x, panel.y, panel.width, 5f), new Color(0.28f, 0.82f, 1f, 0.95f));

            L(new Rect(panel.x + 30f, panel.y + 24f, panel.width - 260f, 42f),
              "탐사 태블릿  ·  1장 탐험", _title, new Color(0.80f, 0.95f, 1f));
            L(new Rect(panel.x + panel.width - 230f, panel.y + 32f, 200f, 28f),
              "발견 " + exploration.DiscoveredCount + " / " + Stage1Exploration.RequiredPartCount,
              _mid, new Color(0.55f, 1f, 0.70f));
            L(new Rect(panel.x + 30f, panel.y + 70f, panel.width - 60f, 28f),
              "분리된 우주선 실루엣을 단서로 기지의 건물 속 파츠를 찾아보자.",
              _small, new Color(0.72f, 0.80f, 0.88f));

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
                DrawCard(card, exploration.Parts[i]);
            }

            L(new Rect(panel.x + 30f, panel.y + panel.height - 38f, panel.width - 60f, 24f),
              "가까이 다가가면 발견됩니다  ·  Tab 닫기", _small, new Color(0.65f, 0.74f, 0.82f));
        }

        void DrawCard(Rect card, Stage1Exploration.PartInfo info)
        {
            var discovered = info != null && info.IsDiscovered;
            var accent = discovered
                ? new Color(0.32f, 1f, 0.64f, 0.95f)
                : new Color(0.30f, 0.42f, 0.52f, 0.95f);

            Box(card, discovered
                ? new Color(0.09f, 0.19f, 0.20f, 0.98f)
                : new Color(0.09f, 0.13f, 0.18f, 0.98f));
            Box(new Rect(card.x, card.y, card.width, 4f), accent);

            if (info == null)
            {
                L(new Rect(card.x + 18f, card.y + 22f, card.width - 36f, 28f),
                  "파츠 데이터 없음", _mid, new Color(1f, 0.64f, 0.55f));
                return;
            }

            DrawSilhouette(new Rect(card.x + 18f, card.y + 24f, 100f, 92f), info.Kind, discovered);

            L(new Rect(card.x + 132f, card.y + 18f, card.width - 150f, 22f),
              "PART 0" + info.Number, _small, new Color(0.56f, 0.73f, 0.83f));
            L(new Rect(card.x + 132f, card.y + 42f, card.width - 150f, 30f),
              info.DisplayName, _mid, discovered ? Color.white : new Color(0.68f, 0.75f, 0.82f));
            L(new Rect(card.x + 132f, card.y + 74f, card.width - 150f, 24f),
              "상태  ·  " + info.StatusText, _small, accent);

            L(new Rect(card.x + 18f, card.y + 128f, card.width - 36f, 24f),
              info.AssetName, _small, new Color(0.50f, 0.67f, 0.76f));
            var clueHeight = Mathf.Max(0f, card.height - 176f);
            if (clueHeight > 0f)
                L(new Rect(card.x + 18f, card.y + 158f, card.width - 36f, clueHeight),
                  info.Clue, _body, new Color(0.80f, 0.86f, 0.91f));
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

        void DrawDiscoveryNotice(Stage1Exploration exploration)
        {
            var width = Mathf.Min(520f, Screen.width - 40f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 112f, width, 54f);
            Box(rect, new Color(0.04f, 0.15f, 0.16f, 0.88f));
            L(new Rect(rect.x + 18f, rect.y + 13f, rect.width - 36f, 28f),
              exploration.DiscoveryNotice, _mid, new Color(0.58f, 1f, 0.70f));
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
            text.Append("0123456789./· 1장 탐험 태블릿 발견 미발견 상태 씬 배치 확인 고정 해제 운반 중 배치됨 조립 완료");
            text.Append("분리된 우주선 실루엣을 단서로 기지의 건물 속 파츠를 찾아보자 가까이 다가가면 발견됩니다 Tab 닫기");
            text.Append("부양 추진부 로봇 파츠 조종석 캡슐 견인 장치 납작한 추진부 팔을 가진 정비 로봇 둥근 조종석 캡슐 작은 비행접시 모양");

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
