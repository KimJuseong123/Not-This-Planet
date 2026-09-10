using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>Editor controls for the runtime fog; slider changes are not persisted.</summary>
    public sealed class Stage1FogCompareWindow : EditorWindow
    {
        private float _start = 5, _end = 55, _maximum = .65f, _desaturation = .7f;
        private Color _color = new Color(.27f, .32f, .43f);
        private bool _mistEnabled = true;
        private float _mistStrength = .35f, _mistBase = 0, _mistHeight = 5, _mistSpeed = .08f;
        [MenuItem("Metagame/Rendering/Fog Compare")]
        private static void Open()
        {
            var window = GetWindow<Stage1FogCompareWindow>("Fog Compare");
            window.minSize = new Vector2(390, 560);
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            GUILayout.Label("Stage1 안개 비교", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("1스테이지 실행 시 빌드에서도 자동 적용됩니다. 이 창은 비교·조절용이며 변경값은 저장하지 않습니다.", MessageType.Info);
            bool available = EditorApplication.isPlaying
                && SceneManager.GetActiveScene().path == Stage1AstraIntegration.ScenePath;
            using (new EditorGUI.DisabledScope(!available))
            {
                GUILayout.Label("현재: " + (Stage1FogTestSession.ShaderEnabled ? "셰이더 안개" : RenderSettings.fog ? "기본 안개" : "안개 없음"));
                if (GUILayout.Button("1 · 안개 없음", GUILayout.Height(32))) Stage1FogTestSession.SetMode(0);
                if (GUILayout.Button("2 · 기존 기본 안개 (15 ~ 90m)", GUILayout.Height(32))) Stage1FogTestSession.SetMode(1);
                if (GUILayout.Button("3 · 셰이더 안개 (최대 강도 제한)", GUILayout.Height(40))) Stage1FogTestSession.SetMode(2);
                if (GUILayout.Button("추천값 · 원근감 강화", GUILayout.Height(28)))
                {
                    _start = 5; _end = 55; _maximum = .65f; _desaturation = .7f;
                    _color = new Color(.27f, .32f, .43f);
                    Stage1FogTestSession.SetMode(2);
                }
                if (GUILayout.Button("추천값 · 공간 안개 (안개판 없음)", GUILayout.Height(28)))
                {
                    _mistEnabled = true;
                    _mistStrength = .35f; _mistBase = 0; _mistHeight = 5; _mistSpeed = .08f;
                    Stage1FogTestSession.SetMode(2);
                }
                _start = EditorGUILayout.Slider("시작 거리", _start, 0, 50);
                _end = EditorGUILayout.Slider("끝 거리", _end, _start + 1, 160);
                _maximum = EditorGUILayout.Slider("최대 강도", _maximum, 0, .8f);
                _desaturation = EditorGUILayout.Slider("먼 곳 채도 낮추기", _desaturation, 0, 1);
                _color = EditorGUILayout.ColorField("안개 색", _color);
                Stage1FogTestSession.Configure(_start, _end, _maximum, _color, _desaturation);
                EditorGUILayout.Space();
                _mistEnabled = EditorGUILayout.Toggle("공간 안개 · 32단계 적분", _mistEnabled);
                _mistStrength = EditorGUILayout.Slider("공간 안개 밀도", _mistStrength, 0, .7f);
                _mistBase = EditorGUILayout.Slider("안개 기준 높이 (월드 Y)", _mistBase, -2, 5);
                _mistHeight = EditorGUILayout.Slider("안개 층 두께", _mistHeight, .5f, 8);
                _mistSpeed = EditorGUILayout.Slider("안개 흐르는 속도", _mistSpeed, 0, .5f);
                Stage1FogTestSession.ConfigureMist(_mistEnabled ? _mistStrength : 0, _mistBase, _mistHeight, _mistSpeed);
            }
            GUILayout.Label("셰이더: 하늘·수직 유도 빛 보호 · 가까운 곳부터 거리감 강화");
        }
    }
}
