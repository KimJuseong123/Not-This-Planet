using System;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.UI;

namespace Metagame.Bootstrap
{
    /// <summary>외형 선택 후 솔로는 Stage1, 멀티는 이 씬의 방 UI를 거쳐 Mirror에 접속한다.</summary>
    public sealed class CustomizerBootstrap : MonoBehaviour
    {
        [Serializable]
        sealed class ButtonVisualSettings
        {
            [InspectorName("기본 색")]
            public Color normalColor;
            [InspectorName("마우스 오버 색")]
            public Color hoverColor;
            [InspectorName("누를 때 색")]
            public Color pressedColor;
            [InspectorName("선택된 색")]
            public Color selectedColor;
            [InspectorName("선택 + 마우스 오버 색")]
            public Color selectedHoverColor;
            [InspectorName("글자 색")]
            public Color textColor;
            [InspectorName("모서리 둥글기"), Range(0, 24)] public int cornerRadius = 10;
            [InspectorName("글자 크기"), Range(12, 28)] public int fontSize = 15;

            public ButtonVisualSettings(Color normal, Color hover, Color pressed, Color selected,
                Color selectedHover, Color text, int radius, int size)
            {
                normalColor = normal;
                hoverColor = hover;
                pressedColor = pressed;
                selectedColor = selected;
                selectedHoverColor = selectedHover;
                textColor = text;
                cornerRadius = radius;
                fontSize = size;
            }
        }

        [Serializable]
        sealed class ScrollVisualSettings
        {
            [InspectorName("트랙 색")] public Color trackColor = new Color(0.08f, 0.11f, 0.18f, 0.55f);
            [InspectorName("손잡이 색")] public Color thumbColor = new Color(0.24f, 0.73f, 0.78f, 0.78f);
            [InspectorName("마우스 오버 색")] public Color thumbHoverColor = new Color(0.38f, 0.91f, 0.94f, 0.95f);
            [InspectorName("누를 때 색")] public Color thumbPressedColor = new Color(0.51f, 0.96f, 1f, 1f);
            [InspectorName("너비"), Range(6, 18)] public int width = 9;
            [InspectorName("모서리 둥글기"), Range(0, 12)] public int cornerRadius = 5;
        }

        [Serializable]
        sealed class SuitColorSettings
        {
            [InspectorName("Yellow (#F2FF9F)"), ColorUsage(false)]
            public Color yellow = new Color32(0xF2, 0xFF, 0x9F, 0xFF);
            [InspectorName("Lavender (#DEC6FF)"), ColorUsage(false)]
            public Color lavender = new Color32(0xDE, 0xC6, 0xFF, 0xFF);
            [InspectorName("Pink (#FFACCD)"), ColorUsage(false)]
            public Color pink = new Color32(0xFF, 0xAC, 0xCD, 0xFF);
            [InspectorName("Sky (#A1D9FF)"), ColorUsage(false)]
            public Color sky = new Color32(0xA1, 0xD9, 0xFF, 0xFF);

            public Color[] ToArray()
            {
                return new[] { yellow, lavender, pink, sky };
            }
        }

        [Header("미리보기")]
        [SerializeField, Range(0.05f, 2f)] float dragRotationSensitivity = 0.35f;
        [SerializeField, Range(1f, 2.5f)] float previewBrightness = 1.65f;

        [Header("슈트 컬러")]
        [SerializeField] SuitColorSettings suitColors = new SuitColorSettings();

        [Header("스크롤")]
        [SerializeField] ScrollVisualSettings scrollStyle = new ScrollVisualSettings();

        [Header("선택 버튼")]
        [SerializeField] ButtonVisualSettings choiceButton = new ButtonVisualSettings(
            new Color(0.075f, 0.095f, 0.16f, 0.92f), new Color(0.105f, 0.14f, 0.22f, 0.98f),
            new Color(0.15f, 0.44f, 0.52f, 1f), new Color(0.12f, 0.34f, 0.42f, 1f),
            new Color(0.15f, 0.44f, 0.52f, 1f), new Color(0.78f, 0.84f, 0.92f), 10, 15);

        [Header("시작 버튼")]
        [SerializeField] ButtonVisualSettings startButton = new ButtonVisualSettings(
            new Color(0.16f, 0.68f, 0.76f, 1f), new Color(0.24f, 0.82f, 0.88f, 1f),
            new Color(0.15f, 0.44f, 0.52f, 1f), new Color(0.16f, 0.68f, 0.76f, 1f),
            new Color(0.24f, 0.82f, 0.88f, 1f), new Color(0.01f, 0.06f, 0.09f), 12, 18);

        [Header("기본 외형 버튼")]
        [SerializeField] ButtonVisualSettings resetButton = new ButtonVisualSettings(
            new Color(0.075f, 0.095f, 0.16f, 0.92f), new Color(0.105f, 0.14f, 0.22f, 0.98f),
            new Color(0.12f, 0.34f, 0.42f, 1f), new Color(0.075f, 0.095f, 0.16f, 0.92f),
            new Color(0.105f, 0.14f, 0.22f, 0.98f), new Color(0.77f, 0.94f, 1f), 10, 15);

        [Header("로비 버튼")]
        [SerializeField] ButtonVisualSettings lobbyButton = new ButtonVisualSettings(
            new Color(0.055f, 0.07f, 0.12f, 0.94f), new Color(0.10f, 0.13f, 0.20f, 1f),
            new Color(0.075f, 0.095f, 0.16f, 0.92f), new Color(0.055f, 0.07f, 0.12f, 0.94f),
            new Color(0.10f, 0.13f, 0.20f, 1f), new Color(0.70f, 0.77f, 0.86f), 10, 15);

        CustomizationData draft;
        Doll preview;
        Camera previewCamera;
        GameObject lightObject, fillLightObject;
        GUIStyle title, sectionTitle, option;
        GUIStyle primaryButton, secondaryButton, quietButton, errorText;
        GUIStyle scrollTrack, scrollThumb, scrollButton;
        Texture2D panelTexture;
        readonly List<Texture2D> generatedButtonTextures = new List<Texture2D>();
        Vector2 scroll;
        float yaw = 25f;
        string error;
        bool completed;
        bool stylesDirty = true;

        static readonly Color Cyan = new Color(0.30f, 0.93f, 1f);
        static readonly Color Lavender = new Color(0.68f, 0.48f, 1f);

        void Start()
        {
            GameSettingsRuntime.Ensure();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ApplySuitColorSettings();
            draft = CustomizationData.Load();
            preview = RagdollBuilder.Build("외형 미리보기", Vector3.zero, Quaternion.identity,
                RagdollBuilder.SuitWhite, RagdollBuilder.Accent, !SpaceSuit.Available);
            foreach (var bodyPart in preview.Root.GetComponentsInChildren<Rigidbody>())
            {
                bodyPart.isKinematic = true;
                bodyPart.detectCollisions = false;
            }
            PlayerAppearance.Apply(preview, draft.Packed);
            SetPreviewBrightness();
            previewCamera = new GameObject("Customizer Preview Camera").AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.018f, 0.028f, 0.06f);
            previewCamera.fieldOfView = 32f;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.depth = 1f;
            lightObject = new GameObject("Customizer Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 3.5f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            fillLightObject = new GameObject("Customizer Fill Light");
            var fill = fillLightObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = Color.white;
            fill.intensity = 2.5f;
            fill.transform.rotation = Quaternion.Euler(18f, 145f, 0f);
            LobbyTransition.Ensure().Failed += OnFailure;
        }

        void LateUpdate()
        {
            if (completed || previewCamera == null) return;
            previewCamera.rect = new Rect(0f, 0f, 0.56f, 1f);
            Vector3 focus = new Vector3(0f, 0.66f, 0f);
            float distance = Mathf.Max(2.55f, 1.48f / Mathf.Max(0.3f, previewCamera.aspect));
            previewCamera.transform.position = focus + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.15f, distance);
            previewCamera.transform.LookAt(focus);
        }

        void OnGUI()
        {
            if (completed || preview == null) return;
            using (new UiScale.Scope(UiScale.Value)) Draw();
        }

        void Draw()
        {
            EnsureStyles();
            float width = UiScale.Width;
            float height = UiScale.Height;
            float panelX = width * 0.56f;

            DrawBackdrop(width, height, panelX);
            HandlePreviewRotation(new Rect(0f, 0f, panelX, height));

            bool wasEnabled = GUI.enabled;
            GUI.enabled = !LobbyTransition.Ensure().IsBusy;

            float contentX = panelX + 38f;
            float contentWidth = width - contentX - 38f;
            GUI.Label(new Rect(contentX, 34f, contentWidth, 46f), "탐사대원 준비", title);

            GUILayout.BeginArea(new Rect(contentX, 98f, contentWidth, Mathf.Max(220f, height - 262f)));
            var skin = GUI.skin;
            GUIStyle previousThumb = skin.verticalScrollbarThumb;
            GUIStyle previousUp = skin.verticalScrollbarUpButton;
            GUIStyle previousDown = skin.verticalScrollbarDownButton;
            skin.verticalScrollbarThumb = scrollThumb;
            skin.verticalScrollbarUpButton = scrollButton;
            skin.verticalScrollbarDownButton = scrollButton;
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, scrollTrack, GUIStyle.none);

            int old = draft.Packed;
            draft.Tint = Choice("슈트 컬러", TintButtonLabels(), draft.Tint, 2);
            draft.Helmet = Choice("헬멧 장식", CustomizationData.HelmetNames, draft.Helmet, 3);
            draft.Badge = Choice("가슴 장식", CustomizationData.BadgeNames, draft.Badge, 3);
            if (old != draft.Packed) PlayerAppearance.Apply(preview, draft.Packed);
            GUILayout.EndScrollView();
            skin.verticalScrollbarThumb = previousThumb;
            skin.verticalScrollbarUpButton = previousUp;
            skin.verticalScrollbarDownButton = previousDown;
            GUILayout.EndArea();

            float footerY = height - 140f;
            DrawLine(new Rect(contentX, footerY - 18f, contentWidth, 1f), new Color(0.37f, 0.56f, 0.75f, 0.24f));
            if (UiInteraction.Button(new Rect(contentX, footerY, contentWidth, 56f),
                    CustomizerFlow.Multiplayer ? "저장하고 팀 합류하기" : "저장하고 탐사 시작", primaryButton))
                Continue();

            float half = (contentWidth - 12f) * 0.5f;
            if (UiInteraction.Button(new Rect(contentX, footerY + 68f, half, 44f), "기본 외형", secondaryButton))
            {
                draft = default;
                PlayerAppearance.Apply(preview, draft.Packed);
            }

            if (UiInteraction.Button(new Rect(contentX + half + 12f, footerY + 68f, half, 44f),
                    "로비로 돌아가기", quietButton))
                LobbyTransition.Ensure().BeginTitle();

            if (!string.IsNullOrEmpty(error))
                GUI.Label(new Rect(contentX, footerY - 48f, contentWidth, 28f), error, errorText);
            GUI.enabled = wasEnabled;
        }

        int Choice(string heading, string[] options, int selected, int columns)
        {
            GUILayout.Label(heading, sectionTitle);
            GUILayout.Space(8f);

            int rows = Mathf.CeilToInt(options.Length / (float)columns);
            selected = UiInteraction.SelectionGrid(selected, options, columns, option, GUILayout.Height(rows * 49f));
            GUILayout.Space(20f);
            return selected;
        }

        string[] TintButtonLabels()
        {
            var labels = new string[CustomizationData.TintNames.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                string hex = ColorUtility.ToHtmlStringRGB(CustomizationData.TintColor(i));
                labels[i] = $"<color=#{hex}>{CustomizationData.TintNames[i]}</color>";
            }
            return labels;
        }

        void HandlePreviewRotation(Rect previewRect)
        {
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(0x43555354, FocusType.Passive, previewRect);
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button != 0 || !previewRect.Contains(current.mousePosition)) break;
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    yaw += current.delta.x * dragRotationSensitivity;
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId || current.button != 0) break;
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
            }
        }

        void SetPreviewBrightness()
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in preview.Root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !materials[i].HasProperty("_PreviewBrightness")) continue;
                    block.Clear();
                    renderer.GetPropertyBlock(block, i);
                    block.SetFloat("_PreviewBrightness", previewBrightness);
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }

        void ApplySuitColorSettings()
        {
            if (suitColors != null)
                CustomizationData.ConfigureTintColors(suitColors.ToArray());
        }

        void RefreshPreviewTint()
        {
            if (preview == null || preview.Root == null) return;
            Renderer[] renderers = preview.Suit != null
                ? preview.Suit.GetComponentsInChildren<Renderer>(true)
                : preview.Root.GetComponentsInChildren<Renderer>(true);
            PlayerAppearance.Tint(renderers, CustomizationData.TintColor(draft.Tint));
        }

        void DrawBackdrop(float width, float height, float panelX)
        {
            GUI.DrawTexture(new Rect(panelX, 0f, width - panelX, height), panelTexture);
            DrawLine(new Rect(panelX, 0f, 1f, height), new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f));
            DrawLine(new Rect(panelX + 4f, 0f, 1f, height), new Color(Lavender.r, Lavender.g, Lavender.b, 0.16f));
            DrawLine(new Rect(0f, 0f, panelX, 3f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f));
        }

        void EnsureStyles()
        {
            if (title != null && !stylesDirty) return;

            ReleaseUiTextures();

            var fallbackFont = Resources.Load<Font>("Fonts/NotoSansKR-Variable");
            var titleFont = Resources.Load<Font>("Fonts/SUIT-Bold") ?? fallbackFont;
            var uiFont = Resources.Load<Font>("Fonts/SUIT-Medium") ?? fallbackFont;
            panelTexture = Solid(new Color(0.018f, 0.026f, 0.065f, 0.98f));

            title = Label(titleFont, 34, FontStyle.Normal, Color.white);
            sectionTitle = Label(titleFont, 20, FontStyle.Normal, Color.white);
            errorText = Label(uiFont, 14, FontStyle.Normal, new Color(1f, 0.48f, 0.52f));

            option = Button(uiFont, choiceButton, true);
            option.richText = true;
            option.padding = new RectOffset(10, 10, 10, 10);
            option.margin = new RectOffset(3, 3, 3, 3);

            primaryButton = Button(uiFont, startButton, false);
            secondaryButton = Button(uiFont, resetButton, false);
            quietButton = Button(uiFont, lobbyButton, false);

            scrollTrack = new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = scrollStyle.width,
                border = new RectOffset(scrollStyle.cornerRadius, scrollStyle.cornerRadius,
                    scrollStyle.cornerRadius, scrollStyle.cornerRadius),
                margin = new RectOffset(8, 0, 2, 2),
                padding = new RectOffset(1, 1, 1, 1)
            };
            scrollTrack.normal.background = Rounded(scrollStyle.trackColor, scrollStyle.cornerRadius);

            scrollThumb = new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = scrollStyle.width,
                border = new RectOffset(scrollStyle.cornerRadius, scrollStyle.cornerRadius,
                    scrollStyle.cornerRadius, scrollStyle.cornerRadius),
                margin = new RectOffset(1, 1, 1, 1)
            };
            scrollThumb.normal.background = Rounded(scrollStyle.thumbColor, scrollStyle.cornerRadius);
            scrollThumb.hover.background = Rounded(scrollStyle.thumbHoverColor, scrollStyle.cornerRadius);
            scrollThumb.active.background = Rounded(scrollStyle.thumbPressedColor, scrollStyle.cornerRadius);

            Texture2D clear = Rounded(Color.clear, 0);
            scrollButton = new GUIStyle(GUI.skin.verticalScrollbarUpButton)
            {
                fixedWidth = scrollStyle.width,
                fixedHeight = 1f,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            scrollButton.normal.background = clear;
            scrollButton.hover.background = clear;
            scrollButton.active.background = clear;
            stylesDirty = false;
        }

        static GUIStyle Label(Font font, int size, FontStyle style, Color color)
        {
            var result = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                fontStyle = style,
                alignment = TextAnchor.MiddleLeft
            };
            result.normal.textColor = color;
            return result;
        }

        GUIStyle Button(Font font, ButtonVisualSettings settings, bool selectable)
        {
            var result = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = settings.fontSize,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                border = new RectOffset(settings.cornerRadius, settings.cornerRadius,
                    settings.cornerRadius, settings.cornerRadius)
            };
            result.normal.background = Rounded(settings.normalColor, settings.cornerRadius);
            result.hover.background = Rounded(settings.hoverColor, settings.cornerRadius);
            result.active.background = Rounded(settings.pressedColor, settings.cornerRadius);
            result.normal.textColor = settings.textColor;
            result.hover.textColor = settings.textColor;
            result.active.textColor = settings.textColor;
            if (selectable)
            {
                result.onNormal.background = Rounded(settings.selectedColor, settings.cornerRadius);
                result.onHover.background = Rounded(settings.selectedHoverColor, settings.cornerRadius);
                result.onActive.background = Rounded(settings.pressedColor, settings.cornerRadius);
                result.onNormal.textColor = settings.textColor;
                result.onHover.textColor = settings.textColor;
                result.onActive.textColor = settings.textColor;
            }
            return result;
        }

        Texture2D Rounded(Color color, int radius)
        {
            const int size = 64;
            radius = Mathf.Clamp(radius, 0, size / 2);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            float edge = radius - 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x, edge, size - 1f - edge);
                float nearestY = Mathf.Clamp(y, edge, size - 1f - edge);
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(nearestX, nearestY));
                Color pixel = color;
                pixel.a *= radius == 0 ? 1f : Mathf.Clamp01(edge + 1f - distance);
                pixels[y * size + x] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            generatedButtonTextures.Add(texture);
            return texture;
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply(false, true);
            return texture;
        }

        static void DrawLine(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
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
            fillLightObject.SetActive(false);
            // 기존 로비 연결/인증/준비/시작과 오류 처리를 그대로 재사용한다.
            gameObject.AddComponent<LobbyBootstrap>();
            GetComponent<LobbyHud>().OpenMultiplayer();
        }

        void OnFailure(string message) { error = message; }
        void OnValidate()
        {
            stylesDirty = true;
            ApplySuitColorSettings();
            if (Application.isPlaying && preview != null && preview.Root != null)
            {
                RefreshPreviewTint();
                SetPreviewBrightness();
            }
        }

        void OnDestroy()
        {
            if (LobbyTransition.Instance != null) LobbyTransition.Instance.Failed -= OnFailure;
            if (preview != null && preview.Root != null) Destroy(preview.Root.gameObject);
            if (previewCamera != null) Destroy(previewCamera.gameObject);
            if (lightObject != null) Destroy(lightObject);
            if (fillLightObject != null) Destroy(fillLightObject);
            ReleaseUiTextures();
        }

        void ReleaseUiTextures()
        {
            DestroyTexture(panelTexture);
            panelTexture = null;
            foreach (var texture in generatedButtonTextures) DestroyTexture(texture);
            generatedButtonTextures.Clear();
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
        }
    }
}
