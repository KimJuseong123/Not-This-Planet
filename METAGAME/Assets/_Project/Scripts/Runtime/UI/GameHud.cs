using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;

namespace Metagame.UI
{
    /// <summary>
    /// IMGUI HUD.
    ///
    /// 한글은 내장 폰트에 글리프가 없어 네모로 나온다 → OS 폰트를 크기별로 만들어 쓴다.
    /// 글리프 요청(RequestCharactersInTexture)은 반드시 OnGUI 밖에서 한다 —
    /// 그리는 도중에 아틀라스가 다시 만들어지면 이미 찍은 글자의 UV 가 어긋나
    /// 엉뚱한 글자가 나온다. clipping = Overflow 도 필수(받침이 잘린다).
    /// </summary>
    public partial class GameHud : MonoBehaviour
    {
        public enum InteractionReticleState
        {
            Dot,
            OpenRabbitHand,
            GrippingRabbitHand
        }

        public PlayerRig Local;
        public TractorBeamController Transport;
        public Stage1Exploration Exploration;

        const int SizeSmall = 15;
        const int SizeMid = 18;
        const int SizeCenter = 22;
        const int SizeBig = 34;
        const FontStyle Style = FontStyle.Normal;

        static string koFontName;
        static bool koFontSearched;
        static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        GUIStyle small, mid, center, big, weldingTick;
        Texture2D white;
        Texture2D rabbitHandCrosshairSheet;
        bool warmed;
        WeldingGaugeTexture weldingGaugeTexture;

        [Header("용접 재료 게이지")]
        [Tooltip("화면 크기 대비 게이지 중심 위치입니다. (0, 0)은 왼쪽 위, (1, 1)은 오른쪽 아래입니다.")]
        [SerializeField] Vector2 weldingGaugeViewportAnchor = new Vector2(0.14f, 0.76f);
        [Tooltip("화면 앵커에서 게이지 중심을 추가로 옮길 픽셀 값입니다.")]
        [SerializeField] Vector2 weldingGaugeScreenOffset = Vector2.zero;
        [Min(40f)] [SerializeField] float weldingGaugeMaxRadius = 82f;

        [Header("상호작용 크로스헤드")]
        [Tooltip("상호작용 가능한 물체를 바라볼 때 표시할 토끼손 아이콘 크기입니다.")]
        [Range(32f, 96f)] [SerializeField] float rabbitHandReticleSize = 58f;

        void Awake()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            rabbitHandCrosshairSheet = Resources.Load<Texture2D>("UI/Crosshair/T_RabbitHandCrosshairSheet");
            InitializeKeyGuides();
        }

        void OnEnable() { Font.textureRebuilt += OnFontRebuilt; }
        void OnDisable() { Font.textureRebuilt -= OnFontRebuilt; }

        void OnDestroy()
        {
            weldingGaugeTexture?.Dispose();
            if (white != null)
            {
                if (Application.isPlaying) Destroy(white);
                else DestroyImmediate(white);
            }
        }

        void OnFontRebuilt(Font f)
        {
            foreach (var kv in Fonts) if (kv.Value == f) { warmed = false; return; }
        }

        void Update()
        {
            if (_keyGuideRevision != KeyGuides.Revision)
            {
                _keyGuideRevision = KeyGuides.Revision;
                warmed = false;
            }
            if (!warmed) WarmFont();
            UpdateMeteorRhythm();

            if (Local == null && GameSession.Instance != null) Local = GameSession.Instance.LocalPlayer;
            if (Transport == null && GameSession.Instance != null) Transport = GameSession.Instance.Transport;
            KeyGuides.Refresh();
        }

        // ── 폰트 ────────────────────────────────────────────────
        static string KoreanFontName
        {
            get
            {
                if (koFontSearched) return koFontName;
                koFontSearched = true;

                string[] wanted = { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic",
                                    "Gulim", "굴림", "Dotum", "돋움", "Batang", "바탕" };
                var installed = Font.GetOSInstalledFontNames();
                if (installed == null) return null;

                foreach (var w in wanted)
                    foreach (var n in installed)
                        if (string.Equals(n, w, System.StringComparison.OrdinalIgnoreCase))
                            return koFontName = n;

                foreach (var n in installed)
                    if (n.IndexOf("Gothic", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return koFontName = n;

                return koFontName;
            }
        }

        static Font FontAt(int size)
        {
            Font f;
            if (Fonts.TryGetValue(size, out f) && f != null) return f;
            string name = KoreanFontName;
            if (string.IsNullOrEmpty(name)) return null;
            f = Font.CreateDynamicFontFromOSFont(name, size);
            Fonts[size] = f;
            return f;
        }

        void WarmFont()
        {
            warmed = true;
            if (string.IsNullOrEmpty(KoreanFontName)) return;

            var sb = new StringBuilder();
            sb.Append("0123456789.,:/%()[]·—+-!?초회중력 ");
            sb.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz 태블릿 던지기");
            for (int i = 0; i < KeyGuides.Entries.Count; i++)
                sb.Append(KeyGuides.Entries[i].Label);
            sb.Append("경과 시간 표지 추락 도착 기록 남은 구간 진행 상태 목표 지구의");
            sb.Append("서 있음 공중 매달림 자빠짐 걷는 중 달리는 중 미끄러짐 착지 분사 중 나르는 중");
            sb.Append("왼팔 오른팔 뻗음 잡음 비어 있음 놓침 잡은 것 없음 닿는 곳");
            sb.Append("핸드레일 케이블 화물 상자 궤짝 산소통 돌덩이 널빤지 배터리 셀 널 소켓 안테나");
            sb.Append("착륙장 고원 절벽 위 균열 건너 화물판 앞 에어록 앞 에어록 통과 골 구조 로켓 탑승");
            sb.Append("해치 열림 닫힘 전원 없음 들어옴 꽂아라 기다렸다 올라탄다 조심 저중력 진공");
            sb.Append("처음부터 다시 그 자리에서 서기 도움말 커서 풀기 이동 달리기 점프 시점 기어오르기");
            sb.Append("자유 이동 방향 카메라 기준 Alt 떼도 현재 시점 유지 토끼 귀");
            sb.Append("팔을 위로 누르고 있으면 손이 잡는다 가벼운 도구는 크로스헤드에 고정된다 무거운 물체는 고정물처럼 짚는다");
            sb.Append("도구 양손 투척 충전 놓기 운석 타격 준비 지금 클릭 좌클릭 노란 구간에 맞춰 휘두르기 다시 준비 너무 일찍 실패 GOOD GREAT PERFECT MISS");
            sb.Append("운반 장치 탑승 하차 운전 견인 부유");
            sb.Append("1장 탐험 태블릿 파츠 발견 미발견 상태 씬 배치 확인 고정 해제 운반 중 배치됨 조립 완료");
            for (int i = 0; i < WorldLabel.All.Count; i++)
                if (WorldLabel.All[i] != null) sb.Append(WorldLabel.All[i].Text);
            for (int i = 0; i < Grabbable.All.Count; i++)
                if (Grabbable.All[i] != null) sb.Append(Grabbable.All[i].Label);

            string all = sb.ToString();
            int[] sizes = { SizeSmall, SizeMid, SizeCenter, SizeBig };
            for (int i = 0; i < sizes.Length; i++)
            {
                var f = FontAt(sizes[i]);
                if (f != null) f.RequestCharactersInTexture(all, sizes[i], Style);
            }
        }

        static GUIStyle MakeStyle(int size, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = size;
            s.fontStyle = Style;
            s.alignment = anchor;
            s.wordWrap = false;
            s.clipping = TextClipping.Overflow;      // 한글 받침이 잘리면 다른 글자로 보인다
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);

            var f = FontAt(size);
            if (f != null) { s.font = f; s.fontSize = 0; }
            return s;
        }

        void EnsureStyles()
        {
            if (big != null) return;
            big = MakeStyle(SizeBig, TextAnchor.MiddleCenter);
            center = MakeStyle(SizeCenter, TextAnchor.MiddleCenter);
            mid = MakeStyle(SizeMid, TextAnchor.UpperLeft);
            small = MakeStyle(SizeSmall, TextAnchor.UpperLeft);
            weldingTick = MakeStyle(SizeSmall, TextAnchor.MiddleRight);
        }

        // ── 그리기 도구 ─────────────────────────────────────────
        void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void L(Rect r, string text, GUIStyle s, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f * c.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, s);
            GUI.color = c;
            GUI.Label(r, text, s);
            GUI.color = old;
        }

        // ── HUD ─────────────────────────────────────────────────
        void OnGUI()
        {
            using (new UiScale.Scope(UiScale.Value)) DrawScaledGui();
        }

        static readonly string[] WeldingTicks = { "0%", "25%", "50%", "75%", "100%" };

        void WeldingMaterialGauge(float width, float height)
        {
            var tool = Local != null ? Local.PresentationTool as PlasmaWeldingTool : null;
            if (tool == null || !tool.IsHeldForView || Local.Downed || Local.IsRiding) return;

            // 무기 모델이 움직여도 읽기 쉽도록 화면 왼쪽의 고정 앵커에 배치한다.
            float radius = Mathf.Min(weldingGaugeMaxRadius, height * 0.16f);
            float thickness = radius * 0.19f;
            var origin = new Vector2(
                Mathf.Clamp(width * weldingGaugeViewportAnchor.x + weldingGaugeScreenOffset.x,
                    radius + 70f, width - radius - 60f),
                Mathf.Clamp(height * weldingGaugeViewportAnchor.y + weldingGaugeScreenOffset.y,
                    radius + 30f, height - radius - 30f));
            Color yellow = new Color(1f, 0.85f, 0.08f, 0.95f);
            if (weldingGaugeTexture == null) weldingGaugeTexture = new WeldingGaugeTexture();
            weldingGaugeTexture.SetFraction(tool.RemainingFraction);
            float extent = radius * WeldingGaugeTexture.OuterRadiusRatio;
            var previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(origin.x - extent, origin.y - extent, extent * 2f, extent * 2f),
                weldingGaugeTexture.Texture, ScaleMode.StretchToFill, true);
            GUI.color = previousColor;
            const int segments = 64;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1f) / segments;
                GaugeLine(GaugePoint(origin, radius - thickness * 0.5f, a),
                    GaugePoint(origin, radius - thickness * 0.5f, b), 1.5f, yellow);
                GaugeLine(GaugePoint(origin, radius + thickness * 0.5f, a),
                    GaugePoint(origin, radius + thickness * 0.5f, b), 1.5f, yellow);
            }
            for (int i = 0; i < WeldingTicks.Length; i++)
            {
                float fraction = i * 0.25f;
                GaugeLine(GaugePoint(origin, radius - thickness * 0.5f, fraction),
                    GaugePoint(origin, radius + thickness * 0.5f + 4f, fraction), 2f, Color.white);
                var label = GaugePoint(origin, radius + thickness * 0.5f + 10f, fraction);
                L(new Rect(label.x - 58f, label.y - 11f, 52f, 22f),
                    WeldingTicks[i], weldingTick, Color.white);
            }
        }

        static Vector2 GaugePoint(Vector2 origin, float radius, float fraction)
        {
            float angle = Mathf.Lerp(18f, -48f, fraction) * Mathf.Deg2Rad;
            return origin + new Vector2(-Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        void GaugeLine(Vector2 start, Vector2 end, float thickness, Color color)
        {
            var matrix = GUI.matrix;
            var delta = end - start;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            Box(new Rect(start.x, start.y - thickness * 0.5f, delta.magnitude + 0.5f, thickness), color);
            GUI.matrix = matrix;
        }

        void DrawScaledGui()
        {
            if (GameSession.Instance != null && GameSession.Instance.EndingActive) return;
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
            EnsureStyles();

            var s = GameSession.Instance;
            float w = UiScale.Width, h = UiScale.Height;

            Reticle(w, h);
            WeldingMaterialGauge(w, h);
            DrawMeteorRhythm(w, h);
            DrawKeyGuides(w, h);

            // 도착
            if (s != null && s.Cleared)
            {
                Box(new Rect(0f, h * 0.5f - 78f, w, 156f), new Color(0.04f, 0.10f, 0.14f, 0.76f));
                L(new Rect(0f, h * 0.5f - 44f, w, 48f), "구조 로켓 탑승!", big, new Color(0.60f, 1f, 0.70f));
                L(new Rect(0f, h * 0.5f + 6f, w, 30f),
                  "기록 " + s.ClearTime.ToString("0.0") + "초 · 추락 " + s.Falls + "회", center, Color.white);
                L(new Rect(0f, h * 0.5f + 40f, w, 30f), "로켓 탈출 성공", center, new Color(0.85f, 0.9f, 0.95f));
            }

            if (Local != null)
                HealthFrame(w, h);
        }

        /// <summary>
        /// 빨간 테두리의 진하기 0~1. 0 이면 그리지 않는다 (S15P21B202-539).
        ///
        /// <para><b>무엇이 바뀌었나.</b> 예전에는 남은 체력에 정비례하는 <b>상시 표시</b>
        /// 였다(<c>1 - health01</c>, <c>-513</c>). 그런데 구조는 체력을 절반만 돌려주고
        /// (<c>PhysicsScale.RescueHealthRatio</c>) 게임 안에 체력을 되돌리는 수단이
        /// 없다 — <b>한 번 구조되면 판이 끝날 때까지 테두리가 남았다.</b> 이제는
        /// 피해를 받은 순간 번쩍였다가 식는 값(<c>PlayerRig.DamageFlash01</c>)을 읽는다.
        /// 구조 직후에는 피해 이벤트가 없으므로 화면이 깨끗하다.</para>
        ///
        /// <para><b>값을 그대로 쓴다.</b> 예전의 <c>1 - x</c> 뒤집기가 없다 — 번쩍임은
        /// 이미 "얼마나 세게 맞았나" 라서 크면 짙은 쪽이 맞다. 뒤집으면 다 식은 0 이
        /// 가장 짙어진다.</para>
        ///
        /// <para><b>왜 순수 메서드로 빼는가.</b> 이 판단이 <see cref="OnGUI"/> 경로 안의
        /// 조건문으로만 있으면 검증이 닿지 못한다 — <c>-438</c>·<c>-448</c>·<c>-489</c>
        /// 에서 세 번 걸린 자리다. 밖에서 부를 수 있게 두면 대조가 실제 경로를 지난다.</para>
        ///
        /// <para>임계값이 있는 이유는 부동소수 찌꺼기다 — 다 식었는데 1e-7 만큼 남아
        /// 테두리가 깜빡이는 것을 막는다.</para>
        /// </summary>
        public static float HealthFrameStrength(float damageFlash01) =>
            Mathf.Clamp01(damageFlash01) <= 0.001f ? 0f : Mathf.Clamp01(damageFlash01);

        void HealthFrame(float width, float height)
        {
            float damage = HealthFrameStrength(Local.DamageFlash01);
            if (damage <= 0f) return;

            float thickness = Mathf.Lerp(0f, Mathf.Min(width, height) * 0.08f, damage);
            float alpha = Mathf.Lerp(0.10f, 0.78f, damage);
            var color = new Color(1f, 0.10f, 0.08f, alpha);

            // 이 패널은 태블릿 위에서도 보여야 하는 로컬 상태 피드백이다.
            int previousDepth = GUI.depth;
            GUI.depth = -100;
            Box(new Rect(0f, 0f, width, thickness), color);
            Box(new Rect(0f, height - thickness, width, thickness), color);
            Box(new Rect(0f, thickness, thickness, height - thickness * 2f), color);
            Box(new Rect(width - thickness, thickness, thickness, height - thickness * 2f), color);
            GUI.depth = previousDepth;
        }

        /// <summary>조준점과 현재 단일 상호작용 키워드만 표시한다.</summary>
        void Reticle(float w, float h)
        {
            var focus = Local != null ? Local.CurrentInteractionTarget : PlayerInteractionTarget.None;
            // 탐색 포커스와 클릭 가능 상태는 분리한다. Clamp는 멀리서도 색상 포커스는
            // 보여 주지만, 팔이 실제 Collider에 닿을 때만 "클릭" 문구를 활성화한다.
            bool hasTarget = focus.CanInteract && Local != null && !Local.IsMounted;
            bool clampGrabbed = Local != null && Local.ClampGrabbed;
            bool releasingClamp = Local != null && Local.ClampReleaseHolding;
            bool mountedVehicle = Local != null && Local.IsMounted;

            float cx = w * 0.5f, cy = h * 0.5f;
            bool interactionPressed = Local != null
                && (Local.CurrentInput.GrabLeft || Local.CurrentInput.GrabRight);
            var reticleState = ResolveInteractionReticleState(hasTarget, interactionPressed);
            Color c = releasingClamp ? new Color(0.45f, 1f, 0.60f, 0.95f)
                    : focus.Kind == PlayerInteractionKind.Clamp ? new Color(1f, 0.78f, 0.28f, 0.95f)
                    : hasTarget || mountedVehicle ? new Color(0.45f, 0.90f, 1f, 0.95f)
                    : new Color(1f, 1f, 1f, 0.55f);

            if (reticleState == InteractionReticleState.Dot || rabbitHandCrosshairSheet == null)
            {
                // 아무것도 가리키지 않을 때에는 시야를 가리지 않는 작은 점만 남긴다.
                Box(new Rect(cx - 2f, cy - 2f, 4f, 4f), c);
            }
            else
            {
                DrawRabbitHandReticle(cx, cy, reticleState);
            }

            if (Local != null && clampGrabbed)
                ClampGauge(cx, cy,
                           Local.ClampLiftProgress,
                           releasingClamp);
            else if (Local != null && Local.ChargingTool)
                ChargeGauge(cx, cy, Local.ToolCharge);
            // 동료를 던지는 충전도 같은 게이지를 쓴다 (S15P21B202-519). 조작이 같으니
            // (양손으로 잡고 Q 를 누르고 있다가 놓는다) 화면도 같은 것이 맞다.
            else if (Local != null && Local.ChargingToss)
                ChargeGauge(cx, cy, Local.TossCharge01);
        }

        /// <summary>
        /// 상호작용 조준점 상태를 입력과 분리해 EditMode에서도 전이를 검증할 수 있게 한다.
        /// 클릭을 누르고 있는 동안만 손을 오므리고, 버튼을 놓으면 다시 펼친 손으로 돌아간다.
        /// </summary>
        public static InteractionReticleState ResolveInteractionReticleState(bool canInteract, bool interactionPressed)
        {
            if (!canInteract) return InteractionReticleState.Dot;
            return interactionPressed
                ? InteractionReticleState.GrippingRabbitHand
                : InteractionReticleState.OpenRabbitHand;
        }

        void DrawRabbitHandReticle(float cx, float cy, InteractionReticleState state)
        {
            // 한 장에 나란히 만든 두 아이콘을 UV로 잘라 사용한다. 원본은 투명 PNG다.
            Rect uv = state == InteractionReticleState.GrippingRabbitHand
                ? new Rect(0.50f, 0.22f, 0.46f, 0.46f)
                : new Rect(0.03f, 0.21f, 0.55f, 0.55f);
            float size = rabbitHandReticleSize;
            var rect = new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);
            GUI.DrawTextureWithTexCoords(rect, rabbitHandCrosshairSheet, uv, true);
        }

        void ClampGauge(float cx, float cy, float progress, bool releaseHolding)
        {
            float ratio = Mathf.Clamp01(progress);
            float width = 180f;
            float height = 8f;
            float x = cx - width * 0.5f;
            float y = cy - 46f;
            Color fill = Color.Lerp(new Color(1f, 0.65f, 0.16f), new Color(0.35f, 1f, 0.55f), ratio);

            Box(new Rect(x, y, width, height), new Color(0f, 0f, 0f, 0.70f));
            Box(new Rect(x, y, width * ratio, height), fill);
        }

        void ChargeGauge(float cx, float cy, float charge)
        {
            float ratio = Mathf.Clamp01(charge);
            float width = 150f;
            float height = 8f;
            float x = cx - width * 0.5f;
            float y = cy - 46f;
            Color fill = Color.Lerp(new Color(1f, 0.64f, 0.18f), new Color(0.35f, 1f, 0.55f), ratio);

            Box(new Rect(x, y, width, height), new Color(0f, 0f, 0f, 0.70f));
            Box(new Rect(x, y, width * ratio, height), fill);
        }

    }
}
