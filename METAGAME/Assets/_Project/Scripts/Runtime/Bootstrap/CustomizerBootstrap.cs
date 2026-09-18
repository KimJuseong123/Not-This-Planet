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
            [InspectorName("트랙 색")] public Color trackColor = new Color32(0x16, 0x18, 0x2A, 0xE6);
            [InspectorName("손잡이 색")] public Color thumbColor = new Color32(0x9B, 0x83, 0xE6, 0xFF);
            [InspectorName("마우스 오버 색")] public Color thumbHoverColor = new Color32(0xB5, 0xA3, 0xF2, 0xFF);
            [InspectorName("누를 때 색")] public Color thumbPressedColor = new Color32(0x80, 0x67, 0xC7, 0xFF);
            [InspectorName("너비"), Range(6, 18)] public int width = 9;
            [InspectorName("모서리 둥글기"), Range(0, 12)] public int cornerRadius = 5;
        }

        [Serializable]
        sealed class SuitColorSettings
        {
            [InspectorName("기본 (White)"), ColorUsage(false)]
            public Color white = Color.white;
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
                return new[] { white, yellow, lavender, pink, sky };
            }
        }

        [Serializable]
        sealed class TintSwatchVisualSettings
        {
            [InspectorName("원 크기"), Range(32f, 64f)] public float diameter = 42f;
            [InspectorName("원 사이 간격"), Range(4f, 24f)] public float spacing = 12f;
            [InspectorName("마우스 오버 확대"), Range(1f, 1.2f)] public float hoverScale = 1.08f;
            [InspectorName("선택 테두리 색")] public Color selectedRingColor = new Color32(0xB5, 0xA3, 0xF2, 0xFF);
            [InspectorName("흰색 원 테두리 색")] public Color whiteBorderColor = new Color32(0x78, 0x79, 0x8C, 0xFF);
        }

        [Header("미리보기")]
        [SerializeField, Range(0.05f, 2f)] float dragRotationSensitivity = 0.35f;
        [SerializeField, Range(1f, 2.5f)] float previewBrightness = 1.65f;

        [Header("미리보기 줌")]
        [SerializeField, Range(0.4f, 1f)] float zoomNear = 0.62f;
        [SerializeField, Range(1f, 2.2f)] float zoomFar = 1.75f;
        [SerializeField, Range(0.02f, 0.4f)] float zoomStep = 0.09f;

        [Header("미리보기 배경")]
        [SerializeField] Texture2D previewBackground;
        [SerializeField] Color previewBackgroundTint = Color.white;
        [SerializeField, Range(0f, 0.25f)] float previewBackgroundParallax = 0.08f;

        [Header("오른쪽 패널")]
        [SerializeField, InspectorName("배경 색")]
        Color panelBackgroundColor = new Color(0.018f, 0.026f, 0.065f, 0.98f);

        [Header("슈트 컬러")]
        [SerializeField] SuitColorSettings suitColors = new SuitColorSettings();

        [Header("슈트 컬러 버튼")]
        [SerializeField] TintSwatchVisualSettings tintSwatches = new TintSwatchVisualSettings();

        [Header("스크롤")]
        [SerializeField] ScrollVisualSettings scrollStyle = new ScrollVisualSettings();

        [Header("선택 버튼")]
        [SerializeField] ButtonVisualSettings choiceButton = new ButtonVisualSettings(
            new Color32(0x17, 0x1A, 0x2D, 0xFF), new Color32(0x39, 0x34, 0x52, 0xFF),
            new Color32(0x62, 0x57, 0x83, 0xFF), new Color32(0x80, 0x67, 0xC7, 0xFF),
            new Color32(0xB5, 0xA3, 0xF2, 0xFF), new Color32(0xD8, 0xD3, 0xE8, 0xFF), 10, 15);

        [Header("시작 버튼")]
        [SerializeField] ButtonVisualSettings startButton = new ButtonVisualSettings(
            new Color32(0x9B, 0x83, 0xE6, 0xFF), new Color32(0xB5, 0xA3, 0xF2, 0xFF),
            new Color32(0x80, 0x67, 0xC7, 0xFF), new Color32(0x9B, 0x83, 0xE6, 0xFF),
            new Color32(0xB5, 0xA3, 0xF2, 0xFF), new Color32(0x17, 0x12, 0x21, 0xFF), 12, 18);

        [Header("기본 외형 버튼")]
        [SerializeField] ButtonVisualSettings resetButton = new ButtonVisualSettings(
            new Color32(0x17, 0x1A, 0x2D, 0xFF), new Color32(0x39, 0x34, 0x52, 0xFF),
            new Color32(0x62, 0x57, 0x83, 0xFF), new Color32(0x17, 0x1A, 0x2D, 0xFF),
            new Color32(0x39, 0x34, 0x52, 0xFF), new Color32(0xD8, 0xD3, 0xE8, 0xFF), 10, 15);

        [Header("로비 버튼")]
        [SerializeField] ButtonVisualSettings lobbyButton = new ButtonVisualSettings(
            new Color32(0x17, 0x1A, 0x2D, 0xFF), new Color32(0x39, 0x34, 0x52, 0xFF),
            new Color32(0x62, 0x57, 0x83, 0xFF), new Color32(0x17, 0x1A, 0x2D, 0xFF),
            new Color32(0x39, 0x34, 0x52, 0xFF), new Color32(0xD8, 0xD3, 0xE8, 0xFF), 10, 15);

        CustomizationData draft;
        Doll preview;
        Camera previewCamera;
        GameObject previewBackgroundObject;
        Material previewBackgroundMaterial;
        GameObject lightObject, fillLightObject;
        GameObject beamObject, poolObject, lampObject;
        Material glowMaterial;
        Mesh beamMesh, discMesh;
        float zoom = 1f;
        GUIStyle title, sectionTitle, option, swatchCheck, swatchTooltip;
        GUIStyle primaryButton, secondaryButton, quietButton, errorText;
        GUIStyle scrollTrack, scrollThumb, scrollButton;
        Texture2D panelTexture, swatchCircle;
        readonly List<Texture2D> generatedButtonTextures = new List<Texture2D>();
        Vector2 scroll;
        float yaw = 25f;
        string error;
        bool completed;
        bool stylesDirty = true;

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
            CreatePreviewBackground();
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

            // **실제 Light 컴포넌트는 두지 않는다** (S15P21B202-631).
            //
            // 이 씬에서는 광원이 캐릭터 표면에 전혀 기여하지 않는다 — 위의 키 3.5 와
            // 필 2.5 를 포함해 광원을 전부 꺼도 렌더가 한 화소도 안 바뀌었다(실측).
            // 스포트라이트를 하나 더 넣어 봐도 켬/끔 차이가 0.000 이었다.
            //
            // 그리고 애초에 Unity 의 광원은 공기 중의 빛줄기를 그리지 않는다. 무대
            // 조명처럼 보이려면 빛기둥·바닥 웅덩이·광원 원반을 **직접 그려야** 한다.
            BuildSpotlightGlow();
            LobbyTransition.Ensure().Failed += OnFailure;
        }

        /// <summary>미리보기가 바라보는 지점. 카메라와 스포트라이트가 같은 곳을 본다.</summary>
        static Vector3 PreviewFocus => new Vector3(0f, 0.66f, 0f);

        /// <summary>
        /// 빛이 나오는 자리. <b>캐릭터 바로 위</b>다 (S15P21B202-631).
        ///
        /// <para>처음에는 앞옆으로 비스듬히 뒀는데, 그러면 빛기둥이 한쪽으로
        /// 치우쳐 보인다 — 정면에서는 왼쪽으로, 옆에서는 뒤로 쏠렸다.
        /// <b>광원과 캐릭터의 세로축이 같아야</b> 머리 위 고리와 귀에 딱 맞는다.</para>
        /// </summary>
        static Vector3 SpotOrigin => new Vector3(0f, 1.98f, 0f);

        /// <summary>
        /// 스포트라이트의 <b>보이는 부분</b>을 만든다 (S15P21B202-631).
        ///
        /// <para><b>Light 컴포넌트는 이걸 안 그린다.</b> 광원은 표면을 밝힐 뿐이고
        /// 공기 중의 빛줄기는 URP 기본으로 렌더되지 않는다. 무대 조명처럼 보이려면
        /// 기둥·바닥 웅덩이·광원 원반을 <b>직접 그려야</b> 한다.</para>
        ///
        /// <para>셋 다 가산 합성이라 조명 계산과 무관하게 항상 보인다 — 이 씬에서
        /// 광원이 표면에 기여하지 않는 것(실측: 광원을 다 꺼도 그림이 동일)과
        /// 상관없이 그림이 나온다.</para>
        /// </summary>
        void BuildSpotlightGlow()
        {
            var shader = Resources.Load<Shader>("SpotlightGlow");
            if (shader == null)
            {
                Debug.LogError("[Customizer] SpotlightGlow 셰이더를 찾지 못했습니다.");
                return;
            }
            glowMaterial = new Material(shader) { name = "Customizer Spotlight Glow" };

            Vector3 apex = SpotOrigin;
            // 축을 세운다. 바닥 중심이 광원 바로 아래여야 원뿔이 기울지 않는다.
            Vector3 floorCentre = new Vector3(apex.x, 0.01f, apex.z);
            float height = apex.y - floorCentre.y;
            float topRadius = 0.085f;
            float bottomRadius = 0.78f;

            beamMesh = BuildCone(topRadius, bottomRadius, height);
            beamObject = NewGlow("Customizer Spot Beam", beamMesh, 0f, 0.30f);
            beamObject.transform.position = apex;
            beamObject.transform.rotation =
                Quaternion.FromToRotation(Vector3.down, (floorCentre - apex).normalized);

            discMesh = BuildDisc(1f);
            poolObject = NewGlow("Customizer Spot Pool", discMesh, 1f, 0.85f);
            poolObject.transform.position = floorCentre;
            poolObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            poolObject.transform.localScale = new Vector3(bottomRadius * 1.9f, bottomRadius * 1.35f, 1f);

            lampObject = NewGlow("Customizer Spot Lamp", discMesh, 1f, 1f);
            lampObject.transform.position = apex;
            lampObject.transform.localScale = Vector3.one * (topRadius * 4.2f);
        }

        GameObject NewGlow(string name, Mesh mesh, float radialMix, float strength)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(glowMaterial) { name = name };
            material.SetFloat("_RadialMix", radialMix);
            material.SetFloat("_Strength", strength);
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>끝이 열린 원뿔. uv.y 는 위에서 0, 아래에서 1 이다(셰이더가 그렇게 읽는다).</summary>
        static Mesh BuildCone(float topRadius, float bottomRadius, float height)
        {
            const int segments = 40;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * topRadius;
                vertices[i * 2 + 1] = dir * bottomRadius + Vector3.down * height;
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
                colors[i * 2] = Color.white;
                colors[i * 2 + 1] = new Color(1f, 1f, 1f, 0.55f);
            }
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, q = i * 6;
                triangles[q] = v; triangles[q + 1] = v + 1; triangles[q + 2] = v + 2;
                triangles[q + 3] = v + 2; triangles[q + 4] = v + 1; triangles[q + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Spotlight Cone" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>가운데가 밝은 원반. 셰이더가 중심에서 방사로 옅어지게 한다.</summary>
        static Mesh BuildDisc(float radius)
        {
            const int segments = 48;
            var vertices = new Vector3[segments + 2];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            colors[0] = Color.white;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices[i + 1] = new Vector3(dir.x, dir.y, 0f) * radius;
                uv[i + 1] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.y * 0.5f);
                colors[i + 1] = new Color(1f, 1f, 1f, 0f);
            }
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            var mesh = new Mesh { name = "Spotlight Disc" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        void LateUpdate()
        {
            if (completed || previewCamera == null) return;
            previewCamera.rect = new Rect(0f, 0f, 0.56f, 1f);
            Vector3 focus = PreviewFocus;
            // 줌은 **거리에만** 건다. 시야각을 건드리면 원근이 바뀌어 얼굴 비율이 왜곡된다.
            float distance = Mathf.Max(2.55f, 1.48f / Mathf.Max(0.3f, previewCamera.aspect)) * zoom;
            previewCamera.transform.position = focus + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.15f, distance);
            previewCamera.transform.LookAt(focus);
            if (lampObject != null)
                lampObject.transform.rotation = previewCamera.transform.rotation;
            UpdatePreviewBackground();
        }

        void CreatePreviewBackground()
        {
            if (previewBackground == null)
                previewBackground = Resources.Load<Texture2D>("UI/Customizer/T_Customizer_Stage1Background");
            if (previewBackground == null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogWarning("[Customizer] 미리보기 배경용 Unlit 셰이더를 찾을 수 없습니다.", this);
                return;
            }

            previewBackgroundObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            previewBackgroundObject.name = "Customizer Space Background";
            previewBackgroundObject.transform.SetParent(previewCamera.transform, false);

            var collider = previewBackgroundObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = previewBackgroundObject.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            previewBackgroundMaterial = new Material(shader)
            {
                name = "~Customizer Space Background",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = previewBackground,
                color = previewBackgroundTint
            };
            renderer.sharedMaterial = previewBackgroundMaterial;
            UpdatePreviewBackground();
        }

        void UpdatePreviewBackground()
        {
            if (previewBackgroundObject == null || previewCamera == null) return;

            const float distance = 10f;
            const float overscan = 1.12f;
            float height = 2f * distance * Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * overscan;
            float parallax = Mathf.Sin(yaw * Mathf.Deg2Rad) * previewBackgroundParallax;
            previewBackgroundObject.transform.localPosition = new Vector3(parallax, 0f, distance);
            previewBackgroundObject.transform.localRotation = Quaternion.identity;
            previewBackgroundObject.transform.localScale = new Vector3(height * previewCamera.aspect, height, 1f);

            if (previewBackgroundMaterial != null)
            {
                previewBackgroundMaterial.color = previewBackgroundTint;
                ApplyBackgroundCrop(previewCamera.aspect);
            }
        }

        void ApplyBackgroundCrop(float viewAspect)
        {
            if (previewBackgroundMaterial == null || previewBackground == null) return;

            float textureAspect = previewBackground.width / (float)Mathf.Max(1, previewBackground.height);
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;
            if (textureAspect > viewAspect)
            {
                scale.x = viewAspect / textureAspect;
                offset.x = (1f - scale.x) * 0.5f;
            }
            else
            {
                scale.y = textureAspect / Mathf.Max(0.01f, viewAspect);
                offset.y = (1f - scale.y) * 0.5f;
            }

            previewBackgroundMaterial.mainTextureScale = scale;
            previewBackgroundMaterial.mainTextureOffset = offset;
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
            draft.Tint = TintChoice(draft.Tint);
            draft.Helmet = Choice("헬멧 장식", CustomizationData.HelmetNames, draft.Helmet, 3);
            draft.Badge = Choice("가슴 장식", CustomizationData.BadgeNames, draft.Badge, 3);
            draft.Aura = Choice("아우라", CustomizationData.AuraNames, draft.Aura, 2);
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

        int TintChoice(int selected)
        {
            GUILayout.Label("슈트 컬러", sectionTitle);
            GUILayout.Space(8f);

            int count = CustomizationData.TintNames.Length;
            float diameter = tintSwatches.diameter;
            float spacing = tintSwatches.spacing;
            float totalWidth = count * diameter + Mathf.Max(0, count - 1) * spacing;
            Rect row = GUILayoutUtility.GetRect(0f, diameter + 30f, GUILayout.ExpandWidth(true));
            float startX = row.x + Mathf.Max(0f, (row.width - totalWidth) * 0.5f);
            float centerY = row.y + diameter * 0.5f + 5f;
            string hoveredName = null;

            for (int i = 0; i < count; i++)
            {
                float centerX = startX + diameter * 0.5f + i * (diameter + spacing);
                Rect hitRect = CenteredRect(centerX, centerY, diameter + 10f);
                bool hovered = hitRect.Contains(Event.current.mousePosition);
                float visibleDiameter = diameter * (hovered ? tintSwatches.hoverScale : 1f);

                if (i == selected)
                    DrawTintCircle(CenteredRect(centerX, centerY, visibleDiameter + 8f), tintSwatches.selectedRingColor);

                if (i == 0)
                {
                    DrawTintCircle(CenteredRect(centerX, centerY, visibleDiameter), tintSwatches.whiteBorderColor);
                    DrawTintCircle(CenteredRect(centerX, centerY, visibleDiameter - 4f), CustomizationData.TintColor(i));
                }
                else
                {
                    DrawTintCircle(CenteredRect(centerX, centerY, visibleDiameter), CustomizationData.TintColor(i));
                }

                if (i == selected)
                {
                    Color previous = GUI.contentColor;
                    GUI.contentColor = CustomizationData.TintColor(i).grayscale > 0.72f
                        ? new Color32(0x17, 0x12, 0x21, 0xFF)
                        : Color.white;
                    GUI.Label(CenteredRect(centerX, centerY, visibleDiameter), "✓", swatchCheck);
                    GUI.contentColor = previous;
                }

                if (GUI.Button(hitRect, new GUIContent(string.Empty, CustomizationData.TintNames[i]), GUIStyle.none))
                    selected = i;
                if (hovered)
                    hoveredName = CustomizationData.TintNames[i];
            }

            if (!string.IsNullOrEmpty(hoveredName))
                GUI.Label(new Rect(startX, row.y + diameter + 9f, totalWidth, 18f), hoveredName, swatchTooltip);

            GUILayout.Space(20f);
            return selected;
        }

        static Rect CenteredRect(float centerX, float centerY, float size)
        {
            return new Rect(centerX - size * 0.5f, centerY - size * 0.5f, size, size);
        }

        void DrawTintCircle(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, swatchCircle, ScaleMode.StretchToFill, true);
            GUI.color = previous;
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
                // 줌 (S15P21B202-631). **회전과 같은 자리에서 받는다** — 미리보기
                // 영역 안에서만 먹어야 오른쪽 선택 패널을 스크롤할 때 캐릭터가
                // 같이 딸려오지 않는다. 이 프로젝트의 첫 휠 입력이다.
                case EventType.ScrollWheel:
                    if (!previewRect.Contains(current.mousePosition)) break;
                    zoom = Mathf.Clamp(zoom + current.delta.y * zoomStep, zoomNear, zoomFar);
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
            UiTheme.DrawPanel(new Rect(panelX + 12f, 12f, width - panelX - 24f, height - 24f));
        }

        void EnsureStyles()
        {
            if (title != null && !stylesDirty) return;

            ReleaseUiTextures();

            var fallbackFont = Resources.Load<Font>("Fonts/NotoSansKR-Variable");
            var titleFont = Resources.Load<Font>("Fonts/SUIT-Bold") ?? fallbackFont;
            var uiFont = Resources.Load<Font>("Fonts/SUIT-Medium") ?? fallbackFont;
            panelTexture = Solid(panelBackgroundColor);

            UiTheme.Ensure();
            title = Label(titleFont, 34, FontStyle.Normal, UiTheme.Text);
            sectionTitle = Label(titleFont, 20, FontStyle.Normal, UiTheme.CyanSoft);
            errorText = Label(uiFont, 14, FontStyle.Normal, new Color(1f, 0.48f, 0.52f));

            option = UiTheme.ButtonStyle(uiFont, 15);
            option.richText = true;
            option.padding = new RectOffset(10, 10, 10, 10);
            option.margin = new RectOffset(3, 3, 3, 3);

            swatchCircle = Rounded(Color.white, 32);
            generatedButtonTextures.Add(swatchCircle);
            swatchCheck = Label(uiFont, 18, FontStyle.Bold, Color.white);
            swatchCheck.alignment = TextAnchor.MiddleCenter;
            swatchTooltip = Label(uiFont, 13, FontStyle.Normal, Color.white);
            swatchTooltip.alignment = TextAnchor.MiddleCenter;

            primaryButton = UiTheme.ButtonStyle(uiFont, 18, true);
            secondaryButton = UiTheme.ButtonStyle(uiFont, 15);
            quietButton = UiTheme.ButtonStyle(uiFont, 15, false, true);

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
            if (beamObject != null) beamObject.SetActive(false);
            if (poolObject != null) poolObject.SetActive(false);
            if (lampObject != null) lampObject.SetActive(false);
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
            if (previewBackgroundObject != null) Destroy(previewBackgroundObject);
            if (previewBackgroundMaterial != null) Destroy(previewBackgroundMaterial);
            if (previewCamera != null) Destroy(previewCamera.gameObject);
            if (lightObject != null) Destroy(lightObject);
            if (fillLightObject != null) Destroy(fillLightObject);
            if (beamObject != null) Destroy(beamObject);
            if (poolObject != null) Destroy(poolObject);
            if (lampObject != null) Destroy(lampObject);
            if (glowMaterial != null) Destroy(glowMaterial);
            if (beamMesh != null) Destroy(beamMesh);
            if (discMesh != null) Destroy(discMesh);
            ReleaseUiTextures();
        }

        void ReleaseUiTextures()
        {
            DestroyTexture(panelTexture);
            panelTexture = null;
            foreach (var texture in generatedButtonTextures) DestroyTexture(texture);
            generatedButtonTextures.Clear();
            swatchCircle = null;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
        }
    }
}
