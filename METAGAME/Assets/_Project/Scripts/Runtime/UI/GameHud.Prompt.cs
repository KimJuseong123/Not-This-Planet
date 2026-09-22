using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 화면에 뜨는 한 줄짜리 안내. <b>키 조각과 말 조각</b>으로만 이루어진다
    /// (S15P21B202-679).
    ///
    /// <para>키를 <c>[E]</c> 꼴로 적는 이유는 마우스·패드 그림을 쓰면 <b>키를 바꾼
    /// 사람에게 거짓말</b>이 되기 때문이다 — S15P21B202-657 이 고친 것이 정확히
    /// 그것이다. 글자는 <see cref="TabletHelpContent.Name"/> 를 거치므로 태블릿과
    /// 같은 이름이 나온다.</para>
    /// </summary>
    /// <summary>
    /// 키 자리에 글자 대신 들어가는 그림 (S15P21B202-696).
    ///
    /// <para><b>바인딩이 없는 입력에만 쓴다.</b> 키보드 키는 사람이 바꿀 수 있으므로
    /// <see cref="Controls"/> 에서 읽어 글자로 적어야 한다 — 그림으로 박으면 키를 바꾼
    /// 사람에게 거짓말이 된다. 마우스 버튼은 바꿀 수 없어서 그림이 안전하다.</para>
    /// </summary>
    public enum HudGlyph
    {
        None,
        MouseLeft,
    }

    public readonly struct HudPrompt
    {
        /// <summary><c>[E]</c> 처럼 대괄호까지 포함한다. 없을 수도 있다.</summary>
        public readonly string Key;
        public readonly string Label;

        /// <summary>키 글자 대신 그릴 그림. <see cref="HudGlyph.None"/> 이면 글자를 쓴다.</summary>
        public readonly HudGlyph Glyph;

        public HudPrompt(string key, string label)
        {
            Key = key;
            Label = label;
            Glyph = HudGlyph.None;
        }

        public HudPrompt(HudGlyph glyph, string label)
        {
            Key = null;
            Label = label;
            Glyph = glyph;
        }

        public bool Exists => Glyph != HudGlyph.None
                              || !string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Label);
    }

    public partial class GameHud
    {
        /// <summary>안내 한 줄의 높이. 키캡 줄과 상호작용 줄이 같은 크기여야 한다.</summary>
        public const float PromptHeight = 38f;

        const float PromptPadding = 17f;
        const float PromptGap = 12f;

        readonly GUIContent _promptContent = new GUIContent();

        float Measure(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            _promptContent.text = text;
            return mid.CalcSize(_promptContent).x;
        }

        /// <summary>그림이 차지하는 폭. 높이에 비를 곱해 글자 줄과 키를 맞춘다.</summary>
        float GlyphWidth(HudPrompt prompt) =>
            prompt.Glyph == HudGlyph.None ? 0f : GlyphHeight * UiTheme.MouseGlyphAspect;

        /// <summary>그림 높이. 글자보다 조금 크게 둬야 같은 무게로 읽힌다.</summary>
        const float GlyphHeight = 24f;

        public float PromptWidth(HudPrompt prompt)
        {
            float key = Measure(prompt.Key) + GlyphWidth(prompt);
            float label = Measure(prompt.Label);
            float gap = key > 0f && label > 0f ? PromptGap : 0f;
            return key + gap + label + PromptPadding * 2f;
        }

        /// <summary>왼쪽 끝을 기준으로 그린다. 왼쪽 아래 키 안내가 줄을 세울 때 쓴다.</summary>
        void DrawPromptAt(float x, float y, HudPrompt prompt)
        {
            float glyphWidth = GlyphWidth(prompt);
            float keyWidth = Measure(prompt.Key) + glyphWidth;
            float labelWidth = Measure(prompt.Label);
            float gap = keyWidth > 0f && labelWidth > 0f ? PromptGap : 0f;
            float width = keyWidth + gap + labelWidth + PromptPadding * 2f;

            UiTheme.DrawPrompt(new Rect(x, y, width, PromptHeight));

            float textHeight = mid.lineHeight > 0f ? mid.lineHeight : 22f;
            float textY = y + (PromptHeight - textHeight) * 0.5f;
            float cursor = x + PromptPadding;
            // 키는 청록, 말은 흰색이다. 눈이 키를 먼저 잡아야 한다.
            if (glyphWidth > 0f)
            {
                UiTheme.DrawMouseLeft(new Rect(cursor, y + (PromptHeight - GlyphHeight) * 0.5f,
                                               glyphWidth, GlyphHeight));
                cursor += glyphWidth + gap;
            }
            else if (keyWidth > 0f)
            {
                L(new Rect(cursor, textY, keyWidth, textHeight), prompt.Key, mid, UiTheme.Cyan);
                cursor += keyWidth + gap;
            }
            if (labelWidth > 0f)
                L(new Rect(cursor, textY, labelWidth, textHeight), prompt.Label, mid, UiTheme.Text);
        }

        /// <summary>가운데를 기준으로 그린다. 크로스헤드 아래 안내가 쓴다.</summary>
        void DrawPromptCentered(float centerX, float y, HudPrompt prompt)
        {
            DrawPromptAt(centerX - PromptWidth(prompt) * 0.5f, y, prompt);
        }
    }
}
