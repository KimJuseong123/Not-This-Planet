using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.UI;

namespace Metagame.Bootstrap
{
    /// <summary>외형 선택 후 솔로는 Stage1, 멀티는 이 씬의 방 UI를 거쳐 Mirror에 접속한다.</summary>
    public sealed class CustomizerBootstrap : MonoBehaviour
    {
        CustomizationData draft;
        Doll preview;
        Camera previewCamera;
        GameObject lightObject;
        GUIStyle title, label, button;
        Vector2 scroll;
        float yaw = 25f;
        string error;
        bool completed;

        void Start()
        {
            GameSettingsRuntime.Ensure();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            draft = CustomizationData.Load();
            preview = RagdollBuilder.Build("외형 미리보기", Vector3.zero, Quaternion.identity,
                RagdollBuilder.SuitWhite, RagdollBuilder.Accent, !SpaceSuit.Available);
            foreach (var body in preview.Root.GetComponentsInChildren<Rigidbody>())
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            PlayerAppearance.Apply(preview, draft.Packed);
            previewCamera = new GameObject("Customizer Preview Camera").AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.025f, 0.045f, 0.075f);
            previewCamera.fieldOfView = 35;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.depth = 1;
            lightObject = new GameObject("Customizer Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -35, 0);
            LobbyTransition.Ensure().Failed += OnFailure;
        }

        void LateUpdate()
        {
            if (completed || previewCamera == null) return;
            previewCamera.rect = new Rect(0, 0.12f, 0.52f, 0.78f);
            Vector3 focus = new Vector3(0, 0.66f, 0);
            float distance = Mathf.Max(2.7f, 1.55f / Mathf.Max(0.3f, previewCamera.aspect));
            previewCamera.transform.position = focus + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.15f, distance);
            previewCamera.transform.LookAt(focus);
        }

        void OnGUI()
        {
            if (completed || preview == null) return;
            using (new UiScale.Scope(UiScale.Value)) Draw();
        }

        void Draw()
        {
            if (title == null)
            {
                var font = Resources.Load<Font>("Fonts/NotoSansKR-Variable");
                title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 30, wordWrap = true };
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18, wordWrap = true };
            }
            GUI.Label(new Rect(26, 18, UiScale.Width - 52, 60), "탐사대원 커스터마이징", title);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !LobbyTransition.Ensure().IsBusy;
            float x = UiScale.Width * 0.53f;
            GUILayout.BeginArea(new Rect(x, 90, UiScale.Width - x - 24, UiScale.Height - 112));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(CustomizerFlow.Multiplayer ? "함께 하기 · 출발 전 준비" : "혼자 하기 · 출발 전 준비", label);
            GUILayout.Space(18);
            int old = draft.Packed;
            draft.Tint = Choice("전체 색조", CustomizationData.TintNames, draft.Tint);
            draft.Helmet = Choice("헬멧 장식", CustomizationData.HelmetNames, draft.Helmet);
            draft.Badge = Choice("가슴 장식", CustomizationData.BadgeNames, draft.Badge);
            if (old != draft.Packed) PlayerAppearance.Apply(preview, draft.Packed);
            GUILayout.Label("외형만 변경됩니다. 이동·점프·장비 성능은 같습니다.", label);
            GUILayout.EndScrollView();
            GUILayout.Space(16);
            if (UiInteraction.LayoutButton("기본 외형으로", button, GUILayout.MinHeight(42)))
            {
                draft = default;
                PlayerAppearance.Apply(preview, draft.Packed);
            }
            if (UiInteraction.LayoutButton(CustomizerFlow.Multiplayer ? "저장하고 방 만들기 / 참가" : "저장하고 출발", button, GUILayout.MinHeight(54)))
                Continue();
            if (UiInteraction.LayoutButton("취소 · 로비로", button, GUILayout.MinHeight(42)))
                LobbyTransition.Ensure().BeginTitle();
            if (!string.IsNullOrEmpty(error)) GUILayout.Label(error, label);
            GUILayout.EndArea();
            if (UiInteraction.Button(new Rect(26, UiScale.Height - 65, 85, 42), "◀ 회전", button)) yaw -= 30;
            if (UiInteraction.Button(new Rect(124, UiScale.Height - 65, 85, 42), "회전 ▶", button)) yaw += 30;
            GUI.enabled = wasEnabled;
        }

        int Choice(string heading, string[] options, int selected)
        {
            GUILayout.Label(heading, label);
            int columns = options.Length == 5 ? 2 : 1;
            selected = UiInteraction.SelectionGrid(selected, options, columns, button,
                GUILayout.MinHeight(Mathf.CeilToInt(options.Length / (float)columns) * 38));
            GUILayout.Space(14);
            return selected;
        }

        void Continue()
        {
            draft.Save();
            if (!CustomizerFlow.Multiplayer)
            {
                LobbyTransition.Ensure().BeginSolo();
                return;
            }
            completed = true;
            preview.Root.gameObject.SetActive(false);
            previewCamera.gameObject.SetActive(false);
            lightObject.SetActive(false);
            // 기존 로비 연결/인증/준비/시작과 오류 처리를 그대로 재사용한다.
            gameObject.AddComponent<LobbyBootstrap>();
            GetComponent<LobbyHud>().OpenMultiplayer();
        }

        void OnFailure(string message) { error = message; }
        void OnDestroy()
        {
            if (LobbyTransition.Instance != null) LobbyTransition.Instance.Failed -= OnFailure;
            if (preview != null && preview.Root != null) Destroy(preview.Root.gameObject);
            if (previewCamera != null) Destroy(previewCamera.gameObject);
            if (lightObject != null) Destroy(lightObject);
        }
    }
}
