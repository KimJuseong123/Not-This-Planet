using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>One local overlay owned by the persistent settings object, never a network player.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameSettingsRuntime))]
    public sealed class FpsOverlay : MonoBehaviour
    {
        readonly FpsCounter _counter = new FpsCounter();
        GameSettingsRuntime _settings;
        GUIStyle _style;
        string _label;

        public static void Ensure(GameSettingsRuntime settings)
        {
            if (settings == null || Application.isBatchMode
                || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (settings.GetComponent<FpsOverlay>() == null)
                settings.gameObject.AddComponent<FpsOverlay>();
        }

        void Awake()
        {
            _settings = GetComponent<GameSettingsRuntime>();
            // Keep this subscription while hidden so the settings toggle can re-enable us.
            _settings.Changed += ApplySettings;
            ApplySettings(_settings.Current);
        }

        void ApplySettings(GameSettingsData settings)
        {
            enabled = settings != null && settings.display != null && settings.display.showFps;
        }

        void OnEnable() => ResetSample();
        void OnDisable() => ResetSample();
        void OnApplicationFocus(bool focused) => ResetSample();
        void OnApplicationPause(bool paused) => ResetSample();

        void ResetSample()
        {
            _counter.Reset();
            _label = null;
        }

        void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= ApplySettings;
        }

        void Update()
        {
            if (_counter.AddFrame(Time.unscaledDeltaTime))
                _label = _counter.FramesPerSecond + " FPS";
        }

        void OnGUI()
        {
            if (!_counter.HasSample || Event.current.type != EventType.Repaint) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    alignment = TextAnchor.UpperRight,
                    wordWrap = false
                };
                _style.normal.textColor = Color.white;
            }

            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -1000;
            using (new UiScale.Scope(UiScale.Value))
            {
                Rect safe = UiScale.SafeArea;
                var rect = new Rect(safe.xMax - 136f, UiScale.Height - safe.yMax + 12f, 120f, 24f);
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), _label, _style);
                GUI.color = Color.white;
                GUI.Label(rect, _label, _style);
            }
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }
    }
}
