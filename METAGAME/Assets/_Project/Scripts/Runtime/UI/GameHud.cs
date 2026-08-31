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
    public class GameHud : MonoBehaviour
    {
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

        GUIStyle small, mid, center, big;
        Texture2D white;
        bool warmed;
        string nearbySign = "";

        void Awake()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        void OnEnable() { Font.textureRebuilt += OnFontRebuilt; }
        void OnDisable() { Font.textureRebuilt -= OnFontRebuilt; }

        void OnFontRebuilt(Font f)
        {
            foreach (var kv in Fonts) if (kv.Value == f) { warmed = false; return; }
        }

        void Update()
        {
            if (!warmed) WarmFont();

            if (Local == null && GameSession.Instance != null) Local = GameSession.Instance.LocalPlayer;
            if (Transport == null && GameSession.Instance != null) Transport = GameSession.Instance.Transport;

            nearbySign = "";
            if (Local != null)
            {
                float d;
                var label = WorldLabel.Nearest(Local.Position, out d);
                if (label != null) nearbySign = label.Text;
            }
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
            sb.Append("경과 시간 표지 추락 도착 기록 남은 구간 진행 상태 목표 지구의");
            sb.Append("서 있음 공중 매달림 자빠짐 걷는 중 달리는 중 미끄러짐 착지 분사 중 나르는 중");
            sb.Append("왼팔 오른팔 뻗음 잡음 비어 있음 놓침 잡은 것 없음 닿는 곳");
            sb.Append("핸드레일 케이블 화물 상자 궤짝 산소통 돌덩이 널빤지 배터리 셀 널 소켓 안테나");
            sb.Append("착륙장 고원 절벽 위 균열 건너 화물판 앞 에어록 앞 에어록 통과 골 구조 로켓 탑승");
            sb.Append("해치 열림 닫힘 전원 없음 들어옴 꽂아라 기다렸다 올라탄다 조심 저중력 진공");
            sb.Append("처음부터 다시 그 자리에서 서기 도움말 커서 풀기 이동 달리기 점프 시점 기어오르기");
            sb.Append("자유 이동 방향 고정 떼면 원래 시점으로 돌아온다 토끼 귀");
            sb.Append("팔을 위로 누르고 있으면 손이 잡는다 가벼운 도구는 크로스헤드에 고정된다 무거운 물체는 고정물처럼 짚는다");
            sb.Append("도구 양손 투척 충전 놓기");
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
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
            EnsureStyles();

            var s = GameSession.Instance;
            float w = Screen.width, h = Screen.height;

            Reticle(w, h);

            // 왼쪽 위 — 진행 상황
            Box(new Rect(14f, 12f, 268f, 142f), new Color(0f, 0f, 0f, 0.38f));
            if (s != null)
            {
                L(new Rect(26f, 20f, 260f, 24f),
                  "경과 " + s.Elapsed.ToString("0.0") + "초", mid, Color.white);
                L(new Rect(26f, 44f, 260f, 24f),
                  "표지 " + s.CheckpointIndex + " / 6", mid, new Color(0.72f, 0.90f, 1f));
                L(new Rect(26f, 68f, 120f, 24f),
                  "추락 " + s.Falls + "회", small, new Color(1f, 0.78f, 0.70f));
                bool powered = PowerSocket.Instance != null && PowerSocket.Instance.Powered;
                L(new Rect(26f, 90f, 120f, 24f),
                  "전원 " + (powered ? "들어옴" : "없음"), small,
                  powered ? new Color(0.55f, 1f, 0.6f) : new Color(0.9f, 0.7f, 0.6f));
                L(new Rect(150f, 90f, 130f, 24f),
                  "해치 " + (SlidingDoor.Instance != null && SlidingDoor.Instance.IsOpen ? "열림" : "닫힘"),
                  small, new Color(0.85f, 0.85f, 0.85f));
                L(new Rect(26f, 114f, 250f, 24f),
                  "중력 " + Mathf.Abs(Physics.gravity.y).ToString("0.0") + " m/s² · 지구의 "
                  + (Mathf.Abs(Physics.gravity.y) / 9.81f * 100f).ToString("0") + "%",
                  small, new Color(0.72f, 0.82f, 1f));
            }

            // 오른쪽 위 — 몸 상태
            if (Local != null)
            {
                Box(new Rect(w - 266f, 12f, 252f, 142f), new Color(0f, 0f, 0f, 0.38f));
                bool free = Local.Cam != null && Local.Cam.FreeLook;
                string state = Local.IsMounted ? "운반 장치 탑승 중"
                             : free ? "시점 자유"
                             : Local.Ragdolled ? "자빠짐"
                             : Local.Hanging ? "매달림"
                             : Local.Grounded ? (Local.Speed > 0.5f ? "걷는 중" : "서 있음")
                             : (Local.Thrusting ? "분사 중" : "공중");
                L(new Rect(w - 254f, 20f, 240f, 24f), state, mid, new Color(0.95f, 0.95f, 0.80f));
                L(new Rect(w - 254f, 44f, 240f, 24f), HandLine("왼팔", Local.LeftHand), small,
                  Local.LeftHand.Gripping ? new Color(0.55f, 1f, 0.6f) : new Color(0.8f, 0.8f, 0.8f));
                L(new Rect(w - 254f, 66f, 240f, 24f), HandLine("오른팔", Local.RightHand), small,
                  Local.RightHand.Gripping ? new Color(0.55f, 1f, 0.6f) : new Color(0.8f, 0.8f, 0.8f));
                L(new Rect(w - 254f, 88f, 240f, 24f),
                  "속도 " + Local.Speed.ToString("0.0") + " m/s", small, new Color(0.75f, 0.8f, 0.9f));
                L(new Rect(w - 140f, 88f, 240f, 24f),
                  Local.Grounded ? "" : "체공 " + Local.AirTime.ToString("0.0") + "초", small,
                  new Color(0.80f, 0.90f, 1f));
                if (Transport != null && Transport.IsControlling)
                    L(new Rect(w - 254f, 112f, 240f, 24f),
                      Transport.ActiveBeam != null && Transport.ActiveBeam.IsHolding ? "견인 중" : "운전 중",
                      small, new Color(0.50f, 0.92f, 1f));
            }

            // 표지판
            if ((Exploration == null || !Exploration.TabletOpen) && !string.IsNullOrEmpty(nearbySign))
                L(new Rect(w * 0.5f - 400f, h * 0.5f + 100f, 800f, 30f), nearbySign, center,
                  new Color(0.90f, 0.98f, 1f));

            // 도착
            if (s != null && s.Cleared)
            {
                Box(new Rect(0f, h * 0.5f - 78f, w, 156f), new Color(0.04f, 0.10f, 0.14f, 0.76f));
                L(new Rect(0f, h * 0.5f - 44f, w, 48f), "구조 로켓 탑승!", big, new Color(0.60f, 1f, 0.70f));
                L(new Rect(0f, h * 0.5f + 6f, w, 30f),
                  "기록 " + s.ClearTime.ToString("0.0") + "초 · 추락 " + s.Falls + "회", center, Color.white);
                L(new Rect(0f, h * 0.5f + 40f, w, 30f), "R 처음부터 다시", center, new Color(0.85f, 0.9f, 0.95f));
            }

            if (Local != null)
                HealthFrame(w, h);
        }

        void HealthFrame(float width, float height)
        {
            float damage = Mathf.Clamp01(1f - Local.Health01);
            if (damage <= 0.001f) return;

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

        /// <summary>
        /// 조준점. 자석 손이 노리고 있는 것이 있으면 초록으로 벌어지고 이름을 띄운다 —
        /// "잡기가 쉽다" 는 걸 화면으로 알려주는 부분.
        /// </summary>
        void Reticle(float w, float h)
        {
            Grabbable aimed = null;
            bool holding = false;
            bool holdingTool = false;
            PartClamp focusedClamp = null;
            bool clampGrabbed = false;
            bool clampLiftReady = false;
            bool releasingClamp = false;
            TransportVehicle focusedVehicle = null;
            bool mountedVehicle = false;
            bool toolTarget = false;
            if (Local != null)
            {
                if (Local.LeftHand.Gripping || Local.RightHand.Gripping) holding = true;
                aimed = Local.RightHand.Aimed != null ? Local.RightHand.Aimed : Local.LeftHand.Aimed;
                toolTarget = aimed != null && aimed.IsTool;
                holdingTool = Local.HeldTool != null && aimed == Local.HeldTool;

                if (Local.ClampInteractor != null)
                {
                    focusedClamp = Local.ClampInteractor.Focus as PartClamp;
                    if (focusedClamp != null && focusedClamp.IsReleased) focusedClamp = null;
                    clampGrabbed = Local.ClampGrabbed;
                    clampLiftReady = Local.ClampLiftReady;
                    releasingClamp = Local.ClampReleaseHolding;
                }

                if (Transport != null)
                {
                    focusedVehicle = Transport.FocusedVehicle;
                    mountedVehicle = Transport.IsControlling;
                }
            }

            float cx = w * 0.5f, cy = h * 0.5f;
            bool clampTarget = focusedClamp != null;
            bool beamTarget = focusedVehicle != null;
            Color c = releasingClamp ? new Color(0.45f, 1f, 0.60f, 0.95f)
                    : clampTarget ? new Color(1f, 0.78f, 0.28f, 0.95f)
                    : mountedVehicle || beamTarget ? new Color(0.45f, 0.90f, 1f, 0.95f)
                    : holding ? new Color(0.45f, 1f, 0.60f, 0.95f)
                    : aimed != null ? new Color(0.60f, 1f, 0.75f, 0.90f)
                                    : new Color(1f, 1f, 1f, 0.55f);

            float gap = clampTarget || beamTarget || aimed != null || holding ? 7f : 3f;
            float len = clampTarget || beamTarget || aimed != null || holding ? 7f : 3f;
            Box(new Rect(cx - gap - len, cy - 1f, len, 2f), c);
            Box(new Rect(cx + gap, cy - 1f, len, 2f), c);
            Box(new Rect(cx - 1f, cy - gap - len, 2f, len), c);
            Box(new Rect(cx - 1f, cy + gap, 2f, len), c);

            if (aimed != null && !string.IsNullOrEmpty(aimed.Label))
            {
                string toolLabel = string.IsNullOrEmpty(aimed.Label) ? "도구" : aimed.Label;
                string aimedText = toolTarget
                    ? (holdingTool ? toolLabel + " · 전방 고정 중" : toolLabel + " · 좌/우클릭으로 잡기")
                    : (holding ? aimed.Label : "닿는 곳: " + aimed.Label);
                L(new Rect(cx - 200f, cy + 26f, 400f, 24f),
                  aimedText, center, c);
            }

            if (clampTarget)
                L(new Rect(cx - 260f, cy + 26f, 520f, 24f),
                  releasingClamp ? "고정쇠 해제 유지 중 · 좌/우클릭 유지"
                  : clampGrabbed && clampLiftReady ? "고정쇠 고정 완료 · 좌/우클릭 유지"
                  : clampGrabbed ? "고정쇠를 위로 들어 올리는 중"
                  : "고정쇠 · 좌/우클릭으로 잡기",
                  center, c);
            else if (Transport != null && mountedVehicle)
                L(new Rect(cx - 300f, cy + 26f, 600f, 24f),
                  focusedVehicle != null && focusedVehicle.Beam != null && focusedVehicle.Beam.IsHolding
                    ? "견인 중 · 좌/우클릭으로 내려놓기 · E 하차"
                    : "탑승 중 · WASD 운전 · 파츠 위에서 좌/우클릭으로 견인 · E 하차",
                  center, c);
            else if (Transport != null && beamTarget)
                L(new Rect(cx - 300f, cy + 26f, 600f, 24f),
                  "운반 장치 · 가까이서 E로 탑승", center, c);

            if (Transport != null && !string.IsNullOrEmpty(Transport.StatusMessage))
                L(new Rect(cx - 340f, cy + 52f, 680f, 24f),
                  Transport.StatusMessage, small, new Color(0.72f, 0.92f, 1f));

            if (Local != null && clampGrabbed)
                ClampGauge(cx, cy,
                           releasingClamp ? Local.ClampInteractor.HoldProgress : Local.ClampLiftProgress,
                           releasingClamp);
            else if (Local != null && Local.ChargingTool)
                ChargeGauge(cx, cy, Local.ToolCharge);
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
            L(new Rect(cx - 150f, y - 22f, 300f, 20f),
              (releaseHolding ? "고정쇠 해제 유지 " : "고정쇠 들어 올리기 ")
              + (ratio * 100f).ToString("0") + "%", small, fill);
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
            L(new Rect(cx - 120f, y - 22f, 240f, 20f),
              "Q 충전 " + (charge * Tuning.ToolChargeDuration).ToString("0.0")
              + " / " + Tuning.ToolChargeDuration.ToString("0.0") + "초",
              small, fill);
        }

        static string HandLine(string name, Hand hand)
        {
            if (hand == null) return name + " —";
            if (hand.Gripping)
                return name + " 잡음 : " + (hand.Held != null && hand.Held.Label != "" ? hand.Held.Label : "무언가");
            if (hand.Aimed != null) return name + " 뻗음 → " + hand.Aimed.Label;
            return name + " 비어 있음";
        }
    }
}
