using System.Collections.Generic;
using System.Text;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Metagame.UI
{
    /// <summary>
    /// 기존 태블릿 정보를 모델 화면 위의 World Space Canvas에 직접 그린다.
    /// 중간 RenderTexture를 거치지 않아 비스듬한 휴대 포즈에서도 글자를 선명하게 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Stage1TabletScreen : MonoBehaviour
    {
        const int UiLayer = 0;
        const float CanvasWidth = 1024f;
        const float CanvasHeight = 512f;

        Stage1Exploration exploration;
        GameObject uiRoot;
        Text title;
        Text body;
        Text footer;
        Text discoverySummary;
        GameObject partsRoot;
        GameObject helpRoot;
        Text previousArrow;
        Text nextArrow;
        readonly TowerFloorView[] towerFloors =
            new TowerFloorView[Stage1Exploration.RequiredPartCount];

        ShipAssembly assembly;
        readonly Image[] discoveryDots = new Image[Stage1Exploration.RequiredPartCount];
        readonly Image[] pageDots = new Image[Stage1Exploration.TabletPageCount];
        readonly HelpCardView[] helpCards = new HelpCardView[9];
        Font font;
        bool ownsFont;
        string lastTitle;
        string lastBody;
        string lastFooter;

        /// <summary>
        /// 탑 한 층. 실루엣 채움·외곽선과 층 이름을 함께 갖는다(S15P21B202-345).
        ///
        /// 채움과 외곽선이 따로인 이유는 기획 §11.1 의 단계가 <b>둘을 다르게</b> 쓰기
        /// 때문이다 — 탑에 올라간 것은 외곽선만, 용접까지 끝난 것은 안까지 채운다.
        /// </summary>
        sealed class TowerFloorView
        {
            public PartSilhouetteGraphic Fill;
            public PartSilhouetteGraphic Outline;
            public Text Name;
        }

        sealed class HelpCardView
        {
            public GameObject Root;
            public RectTransform Rect;
            public Text Keys;
            public Text Description;
        }

        readonly struct HelpEntry
        {
            public readonly string Keys;
            public readonly string Description;

            public HelpEntry(string keys, string description)
            {
                Keys = keys;
                Description = description;
            }
        }

        static readonly HelpEntry[] MovementHelp =
        {
            new("WASD", "이동"),
            new("Shift", "달리기"),
            new("Space", "점프"),
            new("마우스", "시점과 캐릭터 정면·조준 기준"),
            new("Alt", "이동·조준·캐릭터 정면은 카메라를 따르고 시점 자유롭게 돌리기"),
        };

        static readonly HelpEntry[] InteractionHelp =
        {
            new("좌·우클릭", "고정쇠에 손을 결합하고 반대 방향으로 뒤로 이동"),
            new("Ctrl", "팔을 위로 올려 레일·케이블 잡기"),
            new("W + 잡기", "잡은 레일·케이블을 따라 기어오르기"),
            new("Q 짧게", "현재 손 장비 내려놓기"),
            new("Q 길게", "충전 후 바라보는 방향으로 장비 투척"),
        };

        static readonly HelpEntry[] TransportHelp =
        {
            new("E", "장비 줍기·교환, 동료 구조, 운반 장치 탑승·하차"),
            new("WASD", "탑승 중 운반 장치 운전"),
            new("좌·우클릭", "탑승 중 견인 빔 사용"),
            new("Space / Ctrl", "탑승 중 상승 / 하강"),
            new("T", "그 자리에서 다시 서기"),
            new("R", "처음부터 다시 시작"),
            new("Tab", "태블릿 열기·닫기"),
            new("← → / A D", "이전·다음 태블릿 페이지"),
            new("Esc", "커서 잠금 풀기"),
        };

        public void Initialize(Stage1Exploration state, Renderer screenRenderer)
        {
            exploration = state;
            if (exploration == null || screenRenderer == null) return;

            CreateCanvas(screenRenderer);
            Refresh(true);
        }

        // 기획 §11.1 의 단계별 색. 표의 네 줄을 그대로 옮기고 두 줄을 더했다 —
        // 사용자 스케치의 "제대로 쌓이고 용접되면 초록, 뭔가 잘못되면 빨강" 이다.
        //
        // §11.1 은 빨강을 "다음으로 쌓아야 함" 에 썼다. 한 화면에서 빨강이 안내와 경고를
        // 겸할 수 없으므로 안내는 점멸로 옮겼다. "초록" 은 §11.1 이 직접 지정한 청록으로
        // 읽는다.
        static readonly Color Undiscovered = new(0.16f, 0.22f, 0.26f, 1f);
        static readonly Color Discovered = new(0.55f, 0.66f, 0.72f, 1f);
        static readonly Color NextUp = new(0.86f, 0.94f, 0.98f, 1f);
        static readonly Color Stacked = new(0.26f, 0.90f, 0.82f, 1f);
        static readonly Color Wrong = new(1f, 0.30f, 0.28f, 1f);

        void Update() => Tick();

        /// <summary>
        /// 화면을 갱신한다.
        ///
        /// <c>Update</c> 와 갈라 둔 이유는 EditMode 검증에서 <c>Update</c> 가 돌지 않기
        /// 때문이다 — 이 저장소가 <c>Initialize()</c> · <c>Tick()</c> 로 맞춰 온 방식이다.
        /// </summary>
        public void Tick()
        {
            if (exploration == null) return;

            if (uiRoot != null && uiRoot.activeSelf != exploration.TabletOpen)
            {
                uiRoot.SetActive(exploration.TabletOpen);

                // <b>열 때마다 탐색 예산을 되돌린다.</b> 상한만 두면 소진한 뒤 영구히
                // 안 찾는다 — 조립대가 늦게 만들어지거나 씬을 다시 들어오면 탑이 통째로
                // 빈칸이 되고 아무 오류도 나지 않는다. 막으려던 S15P21B202-275 형
                // 결함과 같은 모양이다.
                if (exploration.TabletOpen) assemblySearches = 0;
            }

            Refresh(false);
        }

        void CreateCanvas(Renderer screenRenderer)
        {
            uiRoot = new GameObject("태블릿 화면 UI", typeof(RectTransform));
            uiRoot.layer = UiLayer;
            var screenTransform = screenRenderer.transform;
            var rootRect = (RectTransform)uiRoot.transform;
            // 화면판 자체의 자식으로 두어 사용자가 화면 위치·회전·크기를 바꾸면
            // UI도 별도 계산 없이 정확히 같은 면을 따라간다.
            rootRect.SetParent(screenTransform, false);
            rootRect.localPosition = new Vector3(0f, 0f, -0.0006f);
            rootRect.localRotation = Quaternion.identity;
            rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            rootRect.localScale = new Vector3(
                1f / CanvasWidth,
                1f / CanvasHeight,
                1f);

            var canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 20;
            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            // World Space의 동적 글꼴을 높은 밀도로 래스터화해 가까이 보거나
            // 화면을 기울여도 한글 획이 뭉개지지 않게 한다.
            scaler.dynamicPixelsPerUnit = 10f;

            var background = Panel(uiRoot.transform, "Background",
                new Color(0.035f, 0.085f, 0.115f, 1f), Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            Panel(background.transform, "TopAccent", new Color(0.26f, 0.86f, 1f, 1f),
                new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -6f), Vector2.zero);

            font = CreateKoreanFont(out ownsFont);
            title = Label(background.transform, "Title", 38, TextAnchor.MiddleLeft,
                new Color(0.76f, 0.96f, 1f, 1f),
                new Vector2(0f, 0.84f), new Vector2(1f, 1f),
                new Vector2(42f, 0f), new Vector2(-42f, -10f));
            title.fontStyle = FontStyle.Bold;
            discoverySummary = Label(background.transform, "DiscoverySummary", 23,
                TextAnchor.MiddleRight, new Color(0.45f, 1f, 0.70f, 1f),
                new Vector2(0.68f, 0.84f), new Vector2(0.96f, 1f),
                Vector2.zero, new Vector2(0f, -10f));
            body = Label(background.transform, "Body", 27, TextAnchor.UpperLeft,
                new Color(0.88f, 0.94f, 0.97f, 1f),
                new Vector2(0f, 0.12f), new Vector2(1f, 0.84f),
                new Vector2(48f, 18f), new Vector2(-48f, -12f));
            body.fontStyle = FontStyle.Bold;
            body.lineSpacing = 1.18f;
            CreatePartsPage(background.transform);
            CreateHelpPage(background.transform);
            CreateNavigation(background.transform);
            footer = Label(background.transform, "Footer", 20, TextAnchor.MiddleCenter,
                new Color(0.48f, 0.85f, 0.94f, 1f),
                Vector2.zero, new Vector2(1f, 0.12f),
                new Vector2(28f, 6f), new Vector2(-28f, 0f));
            footer.gameObject.SetActive(false);
            uiRoot.SetActive(exploration.TabletOpen);
        }

        void Refresh(bool force)
        {
            if (exploration == null || title == null || body == null || footer == null) return;

            var page = Mathf.Clamp(exploration.TabletPage, 0, Stage1Exploration.TabletPageCount - 1);
            var nextTitle = PageTitle(page);
            var showingParts = page == 0;
            var nextBody = showingParts ? string.Empty : HelpText(page);
            var dots = new StringBuilder();
            for (var i = 0; i < Stage1Exploration.TabletPageCount; i++)
                dots.Append(i == page ? "●" : "○").Append(' ');
            var nextFooter = dots + "     A / D · ← / →  페이지 이동     Tab  닫기";

            partsRoot.SetActive(showingParts);
            helpRoot.SetActive(!showingParts);
            body.gameObject.SetActive(false);
            discoverySummary.gameObject.SetActive(false);
            if (showingParts) RefreshParts();
            else RefreshHelp(page);
            RefreshNavigation(page);

            if (force || nextTitle != lastTitle) title.text = lastTitle = nextTitle;
            if (force || nextBody != lastBody) body.text = lastBody = nextBody;
            if (force || nextFooter != lastFooter) footer.text = lastFooter = nextFooter;
        }

        /// <summary>
        /// 0페이지 — <b>실루엣 탑</b>. 왼쪽에 층으로 쌓인 파츠 형태, 오른쪽에 층 이름이다
        /// (기획 §11.1 "우주선 조립 상태표", S15P21B202-345).
        ///
        /// <b>탑은 실제 비율이다.</b> 층 높이는 <see cref="PartSpec.Height"/>, 폭은
        /// <see cref="PartSpec.Footprint"/> 를 쓴다 — 둘 다 프리팹 메시를 재서 얻은 값이라
        /// 부양 모듈은 납작하고 상단 모듈은 첨탑이 솟는다. 형태를 보고 어느 파츠인지
        /// 알아볼 수 있어야 태블릿이 상태표로 쓸모가 있다.
        ///
        /// <b>위치·회전은 그리지 않는다.</b> §11 이 정확한 위치·중심점·회전값·자동 정렬
        /// 가이드를 금지한다. 여기 있는 것은 <b>순서와 상태</b>뿐이다.
        /// </summary>
        void CreatePartsPage(Transform parent)
        {
            partsRoot = Panel(parent, "PartsPage", Color.clear,
                new Vector2(0f, 0.12f), new Vector2(1f, 0.84f),
                new Vector2(32f, 14f), new Vector2(-32f, -10f));

            // 파츠 중 가장 넓은 폭을 1 로 두고 나머지를 그 비율로 좁힌다.
            var widest = 0f;
            for (var i = 0; i < PartSpec.All.Length; i++)
                widest = Mathf.Max(widest, PartSpec.All[i].Footprint.x);

            var stacked = PartSpec.StackedHeight;
            var bottom = 0f;

            for (var i = 0; i < towerFloors.Length && i < PartSpec.All.Length; i++)
            {
                var spec = PartSpec.All[i];
                var share = stacked > 0f ? spec.Height / stacked : 0.25f;
                var top = bottom + share;
                var halfWidth = widest > 0f ? spec.Footprint.x / widest * 0.5f : 0.5f;

                towerFloors[i] = CreateTowerFloor(partsRoot.transform, i, spec,
                    TowerCenterX - halfWidth * TowerWidth,
                    TowerCenterX + halfWidth * TowerWidth,
                    bottom, top);
                bottom = top;
            }
        }

        /// <summary>실루엣 탑이 차지하는 가로 폭(0~1). 오른쪽은 층 이름 자리다.</summary>
        const float TowerWidth = 0.36f;

        const float TowerCenterX = 0.20f;

        TowerFloorView CreateTowerFloor(Transform parent, int index, PartSpec spec,
                                        float xMin, float xMax, float yMin, float yMax)
        {
            var view = new TowerFloorView
            {
                Fill = Silhouette(parent, $"Floor{index + 1}_Fill",
                                  PartSilhouetteGraphic.DrawMode.Fill,
                                  xMin, xMax, yMin, yMax),
                Outline = Silhouette(parent, $"Floor{index + 1}_Outline",
                                     PartSilhouetteGraphic.DrawMode.Outline,
                                     xMin, xMax, yMin, yMax),
                Name = Label(parent, $"Floor{index + 1}_Name", 25, TextAnchor.MiddleLeft,
                             Discovered,
                             new Vector2(0.44f, yMin), new Vector2(1f, yMax),
                             Vector2.zero, Vector2.zero),
            };

            view.Name.fontStyle = FontStyle.Bold;
            view.Name.text = $"{index + 1}층   {spec.DisplayName}";
            return view;
        }

        PartSilhouetteGraphic Silhouette(Transform parent, string name,
                                         PartSilhouetteGraphic.DrawMode mode,
                                         float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // <b>어트리뷰트에만 맡기지 않는다.</b> 한 줄로 확실해지는 것을 어트리뷰트
            // 상속 규칙에 걸어 둘 이유가 없다.
            go.AddComponent<CanvasRenderer>();

            var graphic = go.AddComponent<PartSilhouetteGraphic>();
            graphic.Mode = mode;
            graphic.raycastTarget = false;
            return graphic;
        }

        void CreateHelpPage(Transform parent)
        {
            helpRoot = Panel(parent, "HelpPage", Color.clear,
                new Vector2(0f, 0.12f), new Vector2(1f, 0.84f),
                new Vector2(32f, 14f), new Vector2(-32f, -10f));
            for (var i = 0; i < helpCards.Length; i++)
            {
                var root = Panel(helpRoot.transform, $"HelpCard_{i + 1}",
                    new Color(0.08f, 0.15f, 0.20f, 1f),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Panel(root.transform, "Accent", new Color(0.30f, 0.84f, 1f, 0.9f),
                    new Vector2(0f, 0.96f), Vector2.one, Vector2.zero, Vector2.zero);
                Panel(root.transform, "KeyBackground", new Color(0.04f, 0.09f, 0.13f, 1f),
                    new Vector2(0.025f, 0.22f), new Vector2(0.34f, 0.78f),
                    Vector2.zero, Vector2.zero);
                helpCards[i] = new HelpCardView
                {
                    Root = root,
                    Rect = root.GetComponent<RectTransform>(),
                    Keys = Label(root.transform, "Keys", 20, TextAnchor.MiddleLeft,
                        new Color(0.65f, 0.95f, 1f, 1f),
                        new Vector2(0.05f, 0.20f), new Vector2(0.32f, 0.80f),
                        Vector2.zero, Vector2.zero),
                    Description = Label(root.transform, "Description", 18, TextAnchor.MiddleLeft,
                        new Color(0.86f, 0.91f, 0.95f, 1f),
                        new Vector2(0.38f, 0.12f), new Vector2(0.97f, 0.88f),
                        Vector2.zero, Vector2.zero),
                };
            }
        }

        void CreateNavigation(Transform parent)
        {
            previousArrow = Label(parent, "PreviousPage", 31, TextAnchor.MiddleCenter,
                new Color(0.24f, 0.34f, 0.39f, 0.45f),
                new Vector2(0f, 0.42f), new Vector2(0.055f, 0.58f),
                Vector2.zero, Vector2.zero);
            previousArrow.text = "◀";
            nextArrow = Label(parent, "NextPage", 31, TextAnchor.MiddleCenter,
                new Color(0.48f, 0.92f, 1f, 0.95f),
                new Vector2(0.945f, 0.42f), new Vector2(1f, 0.58f),
                Vector2.zero, Vector2.zero);
            nextArrow.text = "▶";
            for (var i = 0; i < pageDots.Length; i++)
            {
                var x = 0.475f + i * 0.017f;
                pageDots[i] = Panel(parent, $"PageDot_{i + 1}",
                    new Color(0.28f, 0.42f, 0.50f, 0.95f),
                    new Vector2(x, 0.055f), new Vector2(x + 0.008f, 0.071f),
                    Vector2.zero, Vector2.zero).GetComponent<Image>();
            }
            for (var i = 0; i < discoveryDots.Length; i++)
            {
                var x = 0.82f + i * 0.032f;
                discoveryDots[i] = Panel(parent, $"DiscoveryDot_{i + 1}",
                    new Color(0.24f, 0.36f, 0.42f, 0.95f),
                    new Vector2(x, 0.91f), new Vector2(x + 0.014f, 0.932f),
                    Vector2.zero, Vector2.zero).GetComponent<Image>();
            }
        }

        /// <summary>
        /// 층별로 색을 칠한다(기획 §11.1, S15P21B202-345).
        ///
        /// <b>발견 여부는 <see cref="Stage1Exploration"/>, 쌓임 여부는
        /// <see cref="ShipAssembly"/> 가 안다.</b> 둘을 합쳐야 §11.1 의 단계가 나온다 —
        /// 발견은 탐사의 결과이고 쌓임은 조립의 결과다.
        /// </summary>
        void RefreshParts()
        {
            ResolveAssembly();

            for (var i = 0; i < discoveryDots.Length; i++)
            {
                discoveryDots[i].gameObject.SetActive(true);
                discoveryDots[i].color = i < exploration.DiscoveredCount
                    ? Stacked
                    : Undiscovered;
            }

            var floors = assembly != null ? assembly.Floors : null;
            var nextFloor = assembly != null ? assembly.NextFloor : 0;

            // <b>잘못된 층이 있으면 "다음으로 쌓아야 함" 점멸을 끈다.</b>
            // <see cref="ShipAssembly.NextFloor"/> 는 첫 빈 층을 사실대로 알려 주므로
            // 아래층이 틀렸어도 위층을 가리킨다. 그대로 그리면 화면이 "3층을 치워라"
            // 와 "4층을 올려라" 를 동시에 말한다 — 지금 할 일은 치우는 쪽이다.
            if (HasWrongFloor(floors)) nextFloor = -1;

            // 점멸. "다음으로 쌓아야 함" 에만 쓴다(§11.1).
            var blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));

            for (var i = 0; i < towerFloors.Length; i++)
            {
                var view = towerFloors[i];
                if (view == null) continue;

                var spec = i < PartSpec.All.Length ? PartSpec.All[i] : null;
                var silhouette = spec != null ? PartSilhouette.Find(spec.Name) : null;
                if (silhouette != null && !view.Outline.HasProfile)
                {
                    view.Fill.SetProfile(silhouette.Left, silhouette.Right);
                    view.Outline.SetProfile(silhouette.Left, silhouette.Right);
                }

                var info = i < exploration.Parts.Count ? exploration.Parts[i] : null;
                var discovered = info != null && info.IsDiscovered;
                var floor = floors != null && i < floors.Count
                    ? floors[i]
                    : ShipAssembly.FloorState.Empty;

                Paint(view, floor, discovered, i == nextFloor, blink);
            }
        }

        /// <summary>
        /// 한 층의 색을 정한다. 기획 §11.1 의 표를 그대로 옮긴 자리다.
        ///
        /// <b>순서가 규칙이다.</b> 잘못된 것이 먼저다 — 3층이 틀렸는데 "다음으로 쌓아야 함"
        /// 점멸이 이기면 플레이어는 잘못을 못 본다.
        /// </summary>
        static void Paint(TowerFloorView view, ShipAssembly.FloorState floor,
                          bool discovered, bool isNext, float blink)
        {
            Color outline;
            var fill = Color.clear;

            switch (floor)
            {
                case ShipAssembly.FloorState.WrongPart:
                case ShipAssembly.FloorState.Tilted:
                    outline = Wrong;
                    fill = Fade(Wrong, 0.22f);
                    break;

                case ShipAssembly.FloorState.Welded:
                    outline = Stacked;
                    fill = Fade(Stacked, 0.70f);
                    break;

                case ShipAssembly.FloorState.Stacked:
                    outline = Stacked;
                    fill = Fade(Stacked, 0.16f);
                    break;

                default:
                    if (!discovered)
                    {
                        // 미발견은 어두운 회색 실루엣이다 — 채워서 형태만 보인다(§11.1).
                        outline = Undiscovered;
                        fill = Fade(Undiscovered, 0.55f);
                    }
                    else if (isNext)
                    {
                        outline = Fade(NextUp, blink);
                    }
                    else
                    {
                        outline = Discovered;
                    }

                    break;
            }

            view.Outline.color = outline;
            view.Fill.color = fill;
            view.Name.color = outline;
        }

        static bool HasWrongFloor(IReadOnlyList<ShipAssembly.FloorState> floors)
        {
            if (floors == null) return false;

            for (var i = 0; i < floors.Count; i++)
                if (floors[i] is ShipAssembly.FloorState.WrongPart
                                or ShipAssembly.FloorState.Tilted)
                    return true;

            return false;
        }

        static Color Fade(Color color, float alpha) =>
            new(color.r, color.g, color.b, alpha);

        /// <summary>
        /// 조립대를 찾는다.
        ///
        /// <b>한 번 실패로 포기할 수 없다.</b> 늦게 만들어지는 경우가 있고, 못 찾으면
        /// 탑이 통째로 빈칸이 되면서 아무 오류도 나지 않는다(S15P21B202-275 가 그렇게
        /// 사라졌다).
        ///
        /// <b>그렇다고 영원히 뒤질 수도 없다.</b> 조립대가 없는 씬에서 태블릿을 열어 두면
        /// 매 프레임 씬 전체 탐색이 돈다. <b>한 번 열려 있는 동안</b>의 횟수로 묶고,
        /// 다시 열면 예산을 되돌린다(<see cref="Tick"/>).
        ///
        /// <b>시계로 묶지 않는 이유.</b> EditMode 에서 <c>Time.unscaledTime</c> 은
        /// 나아가지 않으므로 간격을 두면 첫 실패 뒤 영구히 막힌다 — 검증이 조립대를
        /// 영원히 못 찾는다.
        /// </summary>
        void ResolveAssembly()
        {
            if (assembly != null || assemblySearches >= MaxAssemblySearches) return;

            assemblySearches++;
            assembly = FindAnyObjectByType<ShipAssembly>();
        }

        /// <summary>약 2초(60fps) 분량이면 씬이 다 세워진다.</summary>
        const int MaxAssemblySearches = 120;

        int assemblySearches;

        void RefreshHelp(int page)
        {
            foreach (var dot in discoveryDots) dot.gameObject.SetActive(false);
            var entries = page switch
            {
                1 => MovementHelp,
                2 => InteractionHelp,
                3 => TransportHelp,
                _ => MovementHelp,
            };
            var rows = Mathf.CeilToInt(entries.Length / 2f);
            const float columnGap = 0.02f;
            const float rowGap = 0.035f;
            var cardWidth = (1f - columnGap) * 0.5f;
            var cardHeight = (1f - rowGap * (rows - 1)) / rows;
            for (var i = 0; i < helpCards.Length; i++)
            {
                var card = helpCards[i];
                var visible = i < entries.Length;
                card.Root.SetActive(visible);
                if (!visible) continue;
                var column = i % 2;
                var row = i / 2;
                var xMin = column * (cardWidth + columnGap);
                var yMax = 1f - row * (cardHeight + rowGap);
                card.Rect.anchorMin = new Vector2(xMin, yMax - cardHeight);
                card.Rect.anchorMax = new Vector2(xMin + cardWidth, yMax);
                card.Rect.offsetMin = Vector2.zero;
                card.Rect.offsetMax = Vector2.zero;
                card.Keys.text = entries[i].Keys;
                card.Description.text = entries[i].Description;
            }
        }

        void RefreshNavigation(int page)
        {
            previousArrow.color = page > 0
                ? new Color(0.48f, 0.92f, 1f, 0.95f)
                : new Color(0.24f, 0.34f, 0.39f, 0.45f);
            nextArrow.color = page < Stage1Exploration.TabletPageCount - 1
                ? new Color(0.48f, 0.92f, 1f, 0.95f)
                : new Color(0.24f, 0.34f, 0.39f, 0.45f);
            for (var i = 0; i < pageDots.Length; i++)
                pageDots[i].color = i == page
                    ? new Color(0.42f, 0.96f, 1f, 0.95f)
                    : new Color(0.28f, 0.42f, 0.50f, 0.95f);
        }

        static string HelpText(int page)
        {
            return page switch
            {
                1 => "WASD          카메라 기준 이동\n\nShift             달리기\n\nSpace           점프\n\n마우스          1인칭 시점·정면\n\nAlt                현재 카메라 기준으로 자유 시점",
                2 => "좌·우클릭     고정쇠에 손 결합 / 도구 사용\n\nCtrl               팔 올리기\n\nW + 잡기       레일·케이블 오르기\n\nQ 짧게          장비 내려놓기\n\nQ 길게          바라보는 방향으로 장비 던지기",
                3 => "E                  장비 줍기·교환 / 탑승·구조\n\nWASD          운반 장치 운전\n\nSpace / Ctrl  상승 / 하강\n\nT                  그 자리에서 다시 서기\n\nR                  처음부터 다시 시작",
                _ => string.Empty,
            };
        }

        static string PageTitle(int page)
        {
            return page switch
            {
                0 => "탐사 태블릿 · 파츠 상태",
                1 => "조작 안내 · 이동과 시점",
                2 => "조작 안내 · 손과 도구",
                3 => "조작 안내 · 운반과 복구",
                _ => "탐사 태블릿",
            };
        }

        static GameObject Panel(Transform parent, string name, Color color,
                                Vector2 anchorMin, Vector2 anchorMax,
                                Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = UiLayer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        Text Label(Transform parent, string name, int size, TextAnchor alignment, Color color,
                   Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = UiLayer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        static Font CreateKoreanFont(out bool ownsFont)
        {
            ownsFont = false;
            // 프로젝트에 포함한 OFL 한글 폰트를 우선 사용해 모든 PC와 빌드에서
            // 동일한 한글 모양과 글꼴 데이터를 보장한다.
            var packaged = Resources.Load<Font>("Fonts/NotoSansKR-Variable");
            if (packaged != null) return packaged;

            var wanted = new[] { "Malgun Gothic", "맑은 고딕", "NanumGothic", "Nanum Gothic", "Gulim", "굴림" };
            var installed = Font.GetOSInstalledFontNames();
            if (installed != null)
                foreach (var candidate in wanted)
                    foreach (var name in installed)
                        if (string.Equals(candidate, name, System.StringComparison.OrdinalIgnoreCase))
                        {
                            ownsFont = true;
                            return Font.CreateDynamicFontFromOSFont(name, 32);
                        }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void OnDestroy()
        {
            if (uiRoot != null) Destroy(uiRoot);
            if (ownsFont && font != null) Destroy(font);
        }
    }
}
