using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;

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

        const int SizeSmall = 15;
        const int SizeMid = 18;
        const int SizeCenter = 22;
        const int SizeBig = 34;
        const FontStyle Style = FontStyle.Normal;

        static readonly string[] HelpLines =
        {
            "WASD 이동   ·   Shift 달리기   ·   Space 점프 (저중력이라 2m 넘게 뜬다)   ·   마우스 시점",
            "좌클릭 (꾹) 왼팔 뻗기 / 잡기      우클릭 (꾹) 오른팔 뻗기 / 잡기",
            "가벼운 도구는 크로스헤드에 고정된다. 무거운 물체는 고정물처럼 짚는다",
            "Ctrl 팔을 위로 — 레일·케이블을 잡을 때. 잡은 채로 W 를 누르면 기어오른다",
            "좌·우클릭 도구는 양손으로 든다   ·   Q 짧게 놓기   ·   Q 꾹 2초 충전 후 놓으면 투척",
            "Alt (꾹) 시점만 자유롭게 — 이동 방향은 고정된다. 떼면 원래 시점으로 돌아온다",
            "T 그 자리에서 다시 서기   ·   R 처음부터   ·   Tab 도움말   ·   Esc 커서 풀기",
        };

        static string koFontName;
        static bool koFontSearched;
        static readonly Dictionary<int, Font> Fonts = new Dictionary<int, Font>();

        GUIStyle small, mid, center, big;
        Texture2D white;
        bool help = true;
        bool warmed;
        float helpHideAt;
        string nearbySign = "";

        void Awake()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            helpHideAt = Time.time + 20f;
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

            if (Input.GetKeyDown(Controls.Help)) { help = !help; helpHideAt = float.MaxValue; }
            if (help && Time.time > helpHideAt) help = false;

            if (Local == null && GameSession.Instance != null) Local = GameSession.Instance.LocalPlayer;

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
            sb.Append("체력");
            sb.Append("착륙장 고원 절벽 위 균열 건너 화물판 앞 에어록 앞 에어록 통과 골 구조 로켓 탑승");
            sb.Append("해치 열림 닫힘 전원 없음 들어옴 꽂아라 기다렸다 올라탄다 조심 저중력 진공");
            sb.Append("처음부터 다시 그 자리에서 서기 도움말 커서 풀기 이동 달리기 점프 시점 기어오르기");
            sb.Append("자유 이동 방향 고정 떼면 원래 시점으로 돌아온다 토끼 귀");
            sb.Append("팔을 위로 누르고 있으면 손이 잡는다 가벼운 도구는 크로스헤드에 고정된다 무거운 물체는 고정물처럼 짚는다");
            sb.Append("도구 양손 투척 충전 놓기");
            for (int i = 0; i < HelpLines.Length; i++) sb.Append(HelpLines[i]);
            if (GameSession.Instance != null) sb.Append(GameSession.Instance.StageHint);
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

            // 왼쪽 위 — 플레이어 체력. 진행 상황과 분리해 항상 같은 위치에 둔다.
            if (Local != null)
                HealthBar(new Rect(14f, 12f, 268f, 62f));

            // 왼쪽 위 — 진행 상황
            Box(new Rect(14f, 82f, 268f, 142f), new Color(0f, 0f, 0f, 0.38f));
            if (s != null)
            {
                L(new Rect(26f, 90f, 260f, 24f),
                  "경과 " + s.Elapsed.ToString("0.0") + "초", mid, Color.white);
                L(new Rect(26f, 114f, 260f, 24f),
                  "표지 " + s.CheckpointIndex + " / 6", mid, new Color(0.72f, 0.90f, 1f));
                L(new Rect(26f, 138f, 120f, 24f),
                  "추락 " + s.Falls + "회", small, new Color(1f, 0.78f, 0.70f));
                bool powered = PowerSocket.Instance != null && PowerSocket.Instance.Powered;
                L(new Rect(26f, 160f, 120f, 24f),
                  "전원 " + (powered ? "들어옴" : "없음"), small,
                  powered ? new Color(0.55f, 1f, 0.6f) : new Color(0.9f, 0.7f, 0.6f));
                L(new Rect(150f, 160f, 130f, 24f),
                  "해치 " + (SlidingDoor.Instance != null && SlidingDoor.Instance.IsOpen ? "열림" : "닫힘"),
                  small, new Color(0.85f, 0.85f, 0.85f));
                L(new Rect(26f, 184f, 250f, 24f),
                  "중력 " + Mathf.Abs(Physics.gravity.y).ToString("0.0") + " m/s² · 지구의 "
                  + (Mathf.Abs(Physics.gravity.y) / 9.81f * 100f).ToString("0") + "%",
                  small, new Color(0.72f, 0.82f, 1f));
            }

            // 오른쪽 위 — 몸 상태
            if (Local != null)
            {
                Box(new Rect(w - 266f, 12f, 252f, 118f), new Color(0f, 0f, 0f, 0.38f));
                bool free = Local.Cam != null && Local.Cam.FreeLook;
                string state = free ? "시점 자유"
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
            }

            // 가운데 위 — 지금 할 일
            if (s != null && !string.IsNullOrEmpty(s.StageHint))
                L(new Rect(w * 0.5f - 400f, 22f, 800f, 30f), s.StageHint, center, new Color(1f, 0.96f, 0.80f));

            // 표지판
            if (!string.IsNullOrEmpty(nearbySign))
                L(new Rect(w * 0.5f - 400f, h * 0.5f + 100f, 800f, 30f), nearbySign, center,
                  new Color(0.90f, 0.98f, 1f));

            // 도움말
            if (help)
            {
                float y = h - 24f - HelpLines.Length * 22f;
                Box(new Rect(14f, y - 12f, 780f, HelpLines.Length * 22f + 20f), new Color(0f, 0f, 0f, 0.38f));
                for (int i = 0; i < HelpLines.Length; i++)
                    L(new Rect(26f, y + i * 22f, 760f, 22f), HelpLines[i], small, new Color(0.90f, 0.90f, 0.92f));
            }

            // 도착
            if (s != null && s.Cleared)
            {
                Box(new Rect(0f, h * 0.5f - 78f, w, 156f), new Color(0.04f, 0.10f, 0.14f, 0.76f));
                L(new Rect(0f, h * 0.5f - 44f, w, 48f), "구조 로켓 탑승!", big, new Color(0.60f, 1f, 0.70f));
                L(new Rect(0f, h * 0.5f + 6f, w, 30f),
                  "기록 " + s.ClearTime.ToString("0.0") + "초 · 추락 " + s.Falls + "회", center, Color.white);
                L(new Rect(0f, h * 0.5f + 40f, w, 30f), "R 처음부터 다시", center, new Color(0.85f, 0.9f, 0.95f));
            }
        }

        void HealthBar(Rect area)
        {
            Box(area, new Color(0f, 0f, 0f, 0.48f));

            float ratio = Local == null ? 0f : Local.Health01;
            Color fill = ratio > 0.5f
                ? new Color(0.28f, 0.90f, 0.42f)
                : ratio > 0.25f
                    ? new Color(1f, 0.72f, 0.20f)
                    : new Color(1f, 0.24f, 0.22f);

            L(new Rect(area.x + 12f, area.y + 7f, area.width - 24f, 20f),
              "체력 " + Local.Health.ToString("0") + " / " + Local.MaxHealth.ToString("0"),
              small, Color.white);
            Box(new Rect(area.x + 12f, area.y + 32f, area.width - 24f, 16f),
                new Color(0.08f, 0.10f, 0.12f, 0.92f));
            Box(new Rect(area.x + 12f, area.y + 32f, (area.width - 24f) * ratio, 16f), fill);
        }

        /// <summary>
        /// 조준점. 자석 손이 노리고 있는 것이 있으면 초록으로 벌어지고 이름을 띄운다 —
        /// "잡기가 쉽다" 는 걸 화면으로 알려주는 부분.
        /// </summary>
        void Reticle(float w, float h)
        {
            Grabbable aimed = null;
            bool holding = false;
            if (Local != null)
            {
                if (Local.LeftHand.Gripping || Local.RightHand.Gripping) holding = true;
                aimed = Local.RightHand.Aimed != null ? Local.RightHand.Aimed : Local.LeftHand.Aimed;
            }

            float cx = w * 0.5f, cy = h * 0.5f;
            Color c = holding ? new Color(0.45f, 1f, 0.60f, 0.95f)
                    : aimed != null ? new Color(0.60f, 1f, 0.75f, 0.90f)
                                    : new Color(1f, 1f, 1f, 0.55f);

            float gap = aimed != null || holding ? 7f : 3f;
            float len = aimed != null || holding ? 7f : 3f;
            Box(new Rect(cx - gap - len, cy - 1f, len, 2f), c);
            Box(new Rect(cx + gap, cy - 1f, len, 2f), c);
            Box(new Rect(cx - 1f, cy - gap - len, 2f, len), c);
            Box(new Rect(cx - 1f, cy + gap, 2f, len), c);

            if (aimed != null && !string.IsNullOrEmpty(aimed.Label))
                L(new Rect(cx - 200f, cy + 26f, 400f, 24f),
                  (holding ? "" : "닿는 곳: ") + aimed.Label, center, c);

            if (Local != null && Local.ChargingTool)
                ChargeGauge(cx, cy, Local.ToolCharge);
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
