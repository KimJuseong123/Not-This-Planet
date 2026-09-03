using UnityEditor;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.EditorTools
{
    /// <summary>실제 게임 씬을 열거나 저장하지 않는 캐릭터 전용 미리보기.</summary>
    public sealed class RabbitLocomotionPreview : EditorWindow
    {
        PreviewRenderUtility _preview;
        GameObject _rabbit;
        AnimationClip _walk, _run;
        double _previousTime;
        float _time;
        bool _running, _playing = true;

        [MenuItem("Tools/Metagame/캐릭터/걷기·달리기 미리보기")]
        public static void Open()
        {
            GetWindow<RabbitLocomotionPreview>("토끼 걷기·달리기").Show();
        }

        void OnEnable()
        {
            _preview = new PreviewRenderUtility();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab");
            if (prefab != null)
            {
                _rabbit = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _preview.camera.gameObject.scene);
                _rabbit.transform.localScale = Vector3.one * SpaceSuit.ModelScale;
                _preview.AddSingleGO(_rabbit);
            }
            _walk = Resources.Load<AnimationClip>(RabbitLocomotion.WalkResource);
            _run = Resources.Load<AnimationClip>(RabbitLocomotion.RunResource);
            _previousTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Refresh;
        }

        void OnDisable()
        {
            EditorApplication.update -= Refresh;
            if (_rabbit != null) DestroyImmediate(_rabbit);
            _preview?.Cleanup();
            _preview = null;
        }

        void Refresh()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_playing) _time += (float)(now - _previousTime);
            _previousTime = now;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("캐릭터만 보여주는 미리보기입니다. 맵이나 현재 씬은 변경하지 않습니다.", MessageType.Info);
            _running = GUILayout.Toolbar(_running ? 1 : 0, new[] { "걷기 · Standard Walk", "달리기 · Running" }) == 1;
            _playing = GUILayout.Toggle(_playing, "반복 재생");
            if (_rabbit == null || _walk == null || _run == null)
            {
                EditorGUILayout.HelpBox("캐릭터 또는 변환한 애니메이션 클립을 찾을 수 없습니다.", MessageType.Warning);
                return;
            }
            var clip = _running ? _run : _walk;
            _time = EditorGUILayout.Slider("시간", Mathf.Repeat(_time, clip.length), 0, clip.length);
            clip.SampleAnimation(_rabbit, _time);
            Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint) return;
            _preview.BeginPreview(rect, GUIStyle.none);
            var camera = _preview.camera;
            camera.transform.position = new Vector3(1.7f, 1.1f, 2.3f);
            camera.transform.LookAt(new Vector3(0, 0.64f, 0));
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.fieldOfView = 36f;
            _preview.lights[0].intensity = 1.2f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(35, -35, 0);
            _preview.lights[1].intensity = 0.7f;
            _preview.ambientColor = Color.gray;
            _preview.Render(true);
            GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
        }
    }
}
