using UnityEngine;

namespace Metagame.UI
{
    /// <summary>기존 IMGUI 입력 처리를 보존하면서 활성 컨트롤의 누름만 로컬로 알린다.</summary>
    public static class UiInteraction
    {
        static bool IsPress(Rect rect)
        {
            var current = Event.current;
            return GUI.enabled && current.type == EventType.MouseDown && current.button == 0
                && rect.Contains(current.mousePosition);
        }

        public static bool Button(Rect rect, string text, GUIStyle style = null)
        {
            bool pressed = IsPress(rect);
            var result = GUI.Button(rect, text, style ?? GUI.skin.button);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static bool Button(Rect rect, GUIContent content, GUIStyle style)
        {
            bool pressed = IsPress(rect);
            var result = GUI.Button(rect, content, style);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static bool Toggle(Rect rect, bool value, string text, GUIStyle style = null)
        {
            bool pressed = IsPress(rect);
            var result = GUI.Toggle(rect, value, text, style ?? GUI.skin.toggle);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static float HorizontalSlider(Rect rect, float value, float left, float right)
        {
            bool pressed = IsPress(rect);
            var result = GUI.HorizontalSlider(rect, value, left, right);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static string TextField(Rect rect, string text, int maxLength)
        {
            bool pressed = IsPress(rect);
            var result = GUI.TextField(rect, text, maxLength);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static string TextField(Rect rect, string text, int maxLength, GUIStyle style)
        {
            bool pressed = IsPress(rect);
            var result = GUI.TextField(rect, text, maxLength, style ?? GUI.skin.textField);
            if (pressed && Event.current.type == EventType.Used) LocalCursorFeedback.NotifyPress();
            return result;
        }

        public static bool LayoutButton(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(text), style, options);
            return Button(rect, text, style);
        }

        public static int SelectionGrid(int selected, string[] texts, int columns,
            GUIStyle style, params GUILayoutOption[] options)
        {
            // GUILayout owns the grid geometry; only react if it actually consumes a pointer press.
            bool pointerDown = GUI.enabled && Event.current.type == EventType.MouseDown
                               && Event.current.button == 0;
            int result = GUILayout.SelectionGrid(selected, texts, columns, style, options);
            if (pointerDown && Event.current.type == EventType.Used)
                LocalCursorFeedback.NotifyPress();
            return result;
        }
    }
}
