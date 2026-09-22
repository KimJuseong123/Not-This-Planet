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
    public readonly struct HudPrompt
    {
        /// <summary><c>[E]</c> 처럼 대괄호까지 포함한다. 없을 수도 있다.</summary>
        public readonly string Key;
        public readonly string Label;

        public HudPrompt(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public bool Exists => !string.IsNullOrEmpty(Key) || !string.IsNullOrEmpty(Label);
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

        public float PromptWidth(HudPrompt prompt)
        {
            float key = Measure(prompt.Key);
            float label = Measure(prompt.Label);
            float gap = key > 0f && label > 0f ? PromptGap : 0f;
            return key + gap + label + PromptPadding * 2f;
        }

        /// <summary>왼쪽 끝을 기준으로 그린다. 왼쪽 아래 키 안내가 줄을 세울 때 쓴다.</summary>
        void DrawPromptAt(float x, float y, HudPrompt prompt)
        {
            float keyWidth = Measure(prompt.Key);
            float labelWidth = Measure(prompt.Label);
            float gap = keyWidth > 0f && labelWidth > 0f ? PromptGap : 0f;
            float width = keyWidth + gap + labelWidth + PromptPadding * 2f;

            UiTheme.DrawPrompt(new Rect(x, y, width, PromptHeight));

            float textHeight = mid.lineHeight > 0f ? mid.lineHeight : 22f;
            float textY = y + (PromptHeight - textHeight) * 0.5f;
            float cursor = x + PromptPadding;
            // 키는 청록, 말은 흰색이다. 눈이 키를 먼저 잡아야 한다.
            if (keyWidth > 0f)
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
