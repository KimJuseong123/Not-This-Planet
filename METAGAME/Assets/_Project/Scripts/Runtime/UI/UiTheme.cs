using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 태블릿과 우주선 외장의 시각 언어를 모든 IMGUI 화면에 공유한다.
    /// 화면의 기능과 배경은 그대로 두고 패널, 버튼, 입력창과 HUD 크롬만 통일한다.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color PanelFill = new Color32(0x08, 0x10, 0x29, 0xF5);
        public static readonly Color PanelFillSoft = new Color32(0x0E, 0x19, 0x35, 0xE8);
        public static readonly Color Surface = new Color32(0x20, 0x31, 0x59, 0xE8);
        public static readonly Color SurfaceHover = new Color32(0x3D, 0x3A, 0x74, 0xF5);
        public static readonly Color Violet = new Color32(0x70, 0x58, 0xA6, 0xFF);
        public static readonly Color Cyan = new Color32(0x70, 0xE6, 0xFF, 0xFF);
        public static readonly Color CyanSoft = new Color32(0xB8, 0xF5, 0xFF, 0xFF);
        public static readonly Color Lime = new Color32(0xDF, 0xFF, 0x73, 0xFF);
        public static readonly Color Text = new Color32(0xF4, 0xF7, 0xFF, 0xFF);
        public static readonly Color MutedText = new Color32(0xAE, 0xBB, 0xD2, 0xFF);
        public static readonly Color FrameWhite = new Color32(0xD9, 0xDC, 0xEC, 0xE8);
        public static readonly Color Dimmer = new Color(0.01f, 0.015f, 0.045f, 0.66f);
        /// <summary>안내 한 줄의 바탕. 지면이 밝아도 글자가 읽히게 어둡고, 뒤가 비친다.</summary>
        public static readonly Color PromptFill = new Color32(0x05, 0x0D, 0x20, 0xD2);

        static Texture2D white;
        static Texture2D panel;
        static Texture2D panelSoft;
        static Texture2D button;
        static Texture2D buttonHover;
        static Texture2D buttonActive;
        static Texture2D primary;
        static Texture2D primaryHover;
        static Texture2D quiet;
        static Texture2D input;
        static Texture2D inputFocused;
        static Texture2D prompt;
        static Texture2D circleWhite;
        static Texture2D circleViolet;
        static Texture2D circleCyan;
        static GUIStyle panelStyle;
        static GUIStyle softPanelStyle;
        static GUIStyle promptStyle;

        public static void Ensure()
        {
            if (white != null) return;

            white = Solid(Color.white, "~UI Theme White");
            panel = RoundedLayered(PanelFill, 14, FrameWhite, Violet, Cyan);
            panelSoft = RoundedLayered(PanelFillSoft, 14, new Color(FrameWhite.r, FrameWhite.g,
                FrameWhite.b, 0.56f), new Color(Violet.r, Violet.g, Violet.b, 0.70f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f));
            button = RoundedLayered(Surface, 11, new Color(FrameWhite.r, FrameWhite.g,
                FrameWhite.b, 0.22f), new Color(Violet.r, Violet.g, Violet.b, 0.55f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            buttonHover = RoundedLayered(SurfaceHover, 11, FrameWhite, Violet, Cyan);
            buttonActive = RoundedLayered(new Color32(0x2B, 0x55, 0x78, 0xFF), 11,
                FrameWhite, Violet, Cyan);
            primary = RoundedLayered(new Color32(0x66, 0xCF, 0xF0, 0xFF), 11,
                new Color32(0xE6, 0xFA, 0xFF, 0xFF), new Color32(0x7E, 0x89, 0xD7, 0xFF),
                CyanSoft);
            primaryHover = RoundedLayered(new Color32(0x86, 0xDB, 0xF5, 0xFF), 11,
                Color.white, Violet, CyanSoft);
            quiet = RoundedLayered(new Color32(0x08, 0x10, 0x29, 0xB8), 11,
                new Color(FrameWhite.r, FrameWhite.g, FrameWhite.b, 0.20f),
                new Color(Violet.r, Violet.g, Violet.b, 0.40f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f));
            input = RoundedLayered(new Color32(0x03, 0x08, 0x1A, 0xF2), 10,
                new Color(FrameWhite.r, FrameWhite.g, FrameWhite.b, 0.26f),
                new Color(Violet.r, Violet.g, Violet.b, 0.55f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.14f));
            inputFocused = RoundedLayered(new Color32(0x06, 0x0D, 0x22, 0xFF), 10,
                FrameWhite, Violet, Cyan);
            // 테두리만 청록이고 안은 그냥 어둡다 — 창이 아니라 버튼으로 읽혀야 한다.
            prompt = RoundedLayered(PromptFill, 16,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.90f), PromptFill, PromptFill);
            circleWhite = Circle(FrameWhite);
            circleViolet = Circle(Violet);
            circleCyan = Circle(Cyan);

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(16, 16, 16, 16),
                normal = { background = panel }
            };
            softPanelStyle = new GUIStyle(panelStyle);
            softPanelStyle.normal.background = panelSoft;
            promptStyle = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(18, 18, 18, 18),
                normal = { background = prompt }
            };
        }

        public static GUIStyle ButtonStyle(Font font, int size, bool isPrimary = false,
                                           bool isQuiet = false)
        {
            Ensure();
            var style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                font = font,
                fontSize = font != null && font.dynamic ? 0 : size,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(14, 14, 7, 7),
                border = new RectOffset(12, 12, 12, 12)
            };

            if (isPrimary)
            {
                style.normal.background = primary;
                style.hover.background = primaryHover;
                style.focused.background = primaryHover;
                style.active.background = buttonActive;
                style.onNormal.background = primaryHover;
                style.onHover.background = primaryHover;
                style.onFocused.background = primaryHover;
                style.onActive.background = primary;
                style.normal.textColor = new Color32(0x07, 0x10, 0x1E, 0xFF);
                style.hover.textColor = style.normal.textColor;
                style.focused.textColor = style.normal.textColor;
                style.active.textColor = Color.white;
                style.onNormal.textColor = style.normal.textColor;
                style.onHover.textColor = style.normal.textColor;
                style.onFocused.textColor = style.normal.textColor;
                style.onActive.textColor = style.normal.textColor;
            }
            else
            {
                style.normal.background = isQuiet ? quiet : button;
                style.hover.background = buttonHover;
                style.focused.background = buttonHover;
                style.active.background = buttonActive;
                style.onNormal.background = buttonActive;
                style.onHover.background = primaryHover;
                style.onFocused.background = primaryHover;
                style.onActive.background = primary;
                style.normal.textColor = isQuiet ? MutedText : Text;
                style.hover.textColor = Text;
                style.focused.textColor = Text;
                style.active.textColor = Text;
                style.onNormal.textColor = Text;
                style.onHover.textColor = new Color32(0x07, 0x10, 0x1E, 0xFF);
                style.onFocused.textColor = style.onHover.textColor;
                style.onActive.textColor = style.onHover.textColor;
            }

            return style;
        }

        public static GUIStyle TextFieldStyle(Font font, int size)
        {
            Ensure();
            var style = new GUIStyle(GUI.skin.textField)
            {
                font = font,
                fontSize = font != null && font.dynamic ? 0 : size,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 14, 7, 7),
                border = new RectOffset(12, 12, 12, 12),
                clipping = TextClipping.Clip
            };
            style.normal.background = input;
            style.hover.background = inputFocused;
            style.focused.background = inputFocused;
            style.active.background = inputFocused;
            style.normal.textColor = Text;
            style.hover.textColor = Text;
            style.focused.textColor = Text;
            style.active.textColor = Text;
            return style;
        }

        public static void DrawPanel(Rect area, bool soft = false, bool lens = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure();
            (soft ? softPanelStyle : panelStyle).Draw(area, GUIContent.none, false, false, false, false);
            DrawCornerMarks(area);
            if (lens) DrawLens(area);
        }

        /// <summary>
        /// 안내 한 줄을 담는 둥근 테두리 (S15P21B202-679).
        ///
        /// <para><see cref="DrawPanel"/> 와 달리 <b>모서리 표식도 렌즈도 없다.</b> 그것은
        /// 창의 장식이라, 한 줄짜리 안내에 붙이면 읽을 것이 아니라 열어 볼 것처럼
        /// 보인다. 여기 필요한 것은 "지금 이 키를 누르면 된다" 하나뿐이다.</para>
        /// </summary>
        public static void DrawPrompt(Rect area)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure();
            promptStyle.Draw(area, GUIContent.none, false, false, false, false);
        }

        public static void DrawDimmer(Rect area)
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawSolid(area, Dimmer);
        }

        public static void DrawSection(Rect area)
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawSolid(area, new Color(0.10f, 0.22f, 0.38f, 0.82f));
            DrawSolid(new Rect(area.x, area.yMax - 2f, area.width, 2f), Cyan);
        }

        public static void DrawSolid(Rect area, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure();
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, white);
            GUI.color = previous;
        }

        static void DrawCornerMarks(Rect area)
        {
            const float inset = 12f;
            const float length = 22f;
            const float thickness = 2f;
            var color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f);
            DrawSolid(new Rect(area.x + inset, area.y + inset, length, thickness), color);
            DrawSolid(new Rect(area.x + inset, area.y + inset, thickness, length), color);
            DrawSolid(new Rect(area.xMax - inset - length, area.y + inset, length, thickness), color);
            DrawSolid(new Rect(area.xMax - inset - thickness, area.y + inset, thickness, length), color);
            DrawSolid(new Rect(area.x + inset, area.yMax - inset - thickness, length, thickness), color);
            DrawSolid(new Rect(area.x + inset, area.yMax - inset - length, thickness, length), color);
            DrawSolid(new Rect(area.xMax - inset - length, area.yMax - inset - thickness, length, thickness), color);
            DrawSolid(new Rect(area.xMax - inset - thickness, area.yMax - inset - length, thickness, length), color);
        }

        static void DrawLens(Rect area)
        {
            var center = new Vector2(area.xMax - 24f, area.y + 24f);
            GUI.DrawTexture(new Rect(center.x - 8f, center.y - 8f, 16f, 16f), circleWhite);
            GUI.DrawTexture(new Rect(center.x - 6f, center.y - 6f, 12f, 12f), circleViolet);
            GUI.DrawTexture(new Rect(center.x - 3f, center.y - 3f, 6f, 6f), circleCyan);
        }

        static Texture2D RoundedLayered(Color fill, int radius, Color outer,
                                        Color middle, Color inner)
        {
            const int size = 64;
            var texture = NewTexture(size, size, "~UI Theme Rounded");
            var pixels = new Color[size * size];
            var half = (size - 1f) * 0.5f;
            var core = half - radius;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var qx = Mathf.Abs(x - half) - core;
                var qy = Mathf.Abs(y - half) - core;
                var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                var inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                var signedDistance = outside + inside - radius;
                var alpha = Mathf.Clamp01(0.75f - signedDistance);
                var depth = -signedDistance;
                var color = depth < 2.2f ? outer : depth < 5.4f ? middle : depth < 7.2f ? inner : fill;
                color.a *= alpha;
                pixels[y * size + x] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D Circle(Color color)
        {
            const int size = 32;
            var texture = NewTexture(size, size, "~UI Theme Circle");
            var pixels = new Color[size * size];
            var center = (size - 1f) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var pixel = color;
                pixel.a *= Mathf.Clamp01(center - distance + 0.75f);
                pixels[y * size + x] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D Solid(Color color, string name)
        {
            var texture = NewTexture(2, 2, name);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
