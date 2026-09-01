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
        readonly PartCardView[] partCards = new PartCardView[Stage1Exploration.RequiredPartCount];
        readonly Image[] discoveryDots = new Image[Stage1Exploration.RequiredPartCount];
        readonly Image[] pageDots = new Image[Stage1Exploration.TabletPageCount];
        readonly HelpCardView[] helpCards = new HelpCardView[9];
        Font font;
        bool ownsFont;
        string lastTitle;
        string lastBody;
        string lastFooter;

        sealed class PartCardView
        {
            public Image Background;
            public Image Accent;
            public Text Number;
            public Text Name;
            public Image[] StateSegments;
            public Image[] SilhouetteShapes;
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
            new("좌·우클릭", "고정쇠를 잡고 위로 들어 올린 뒤 유지"),
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

        void Update()
        {
            if (exploration == null) return;
            if (uiRoot != null && uiRoot.activeSelf != exploration.TabletOpen)
                uiRoot.SetActive(exploration.TabletOpen);
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

        void CreatePartsPage(Transform parent)
        {
            partsRoot = Panel(parent, "PartsPage", Color.clear,
                new Vector2(0f, 0.12f), new Vector2(1f, 0.84f),
                new Vector2(32f, 14f), new Vector2(-32f, -10f));

            for (var i = 0; i < partCards.Length; i++)
            {
                var column = i % 2;
                var row = i / 2;
                var xMin = column == 0 ? 0f : 0.51f;
                var xMax = column == 0 ? 0.49f : 1f;
                var yMin = row == 0 ? 0.52f : 0f;
                var yMax = row == 0 ? 1f : 0.48f;
                var info = i < exploration.Parts.Count ? exploration.Parts[i] : null;
                partCards[i] = CreatePartCard(partsRoot.transform, i,
                    new Vector2(xMin, yMin), new Vector2(xMax, yMax), info?.Kind);
            }
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

        PartCardView CreatePartCard(Transform parent, int index, Vector2 anchorMin,
                                    Vector2 anchorMax, Stage1Exploration.PartKind? kind)
        {
            var root = Panel(parent, $"PartCard_{index + 1}",
                new Color(0.09f, 0.13f, 0.18f, 1f), anchorMin, anchorMax,
                Vector2.zero, Vector2.zero);
            var view = new PartCardView
            {
                Background = root.GetComponent<Image>(),
                Accent = Panel(root.transform, "Accent", Color.gray,
                    new Vector2(0f, 0.965f), Vector2.one, Vector2.zero, Vector2.zero)
                    .GetComponent<Image>(),
                Number = Label(root.transform, "Number", 29, TextAnchor.MiddleLeft,
                    new Color(0.72f, 0.90f, 0.96f, 1f),
                    new Vector2(0.43f, 0.60f), new Vector2(0.94f, 0.88f),
                    Vector2.zero, Vector2.zero),
                Name = Label(root.transform, "Name", 21, TextAnchor.MiddleLeft,
                    new Color(0.80f, 0.90f, 0.94f, 1f),
                    new Vector2(0.43f, 0.38f), new Vector2(0.94f, 0.62f),
                    Vector2.zero, Vector2.zero),
                StateSegments = new Image[4],
            };
            view.Number.fontStyle = FontStyle.Bold;
            view.Name.gameObject.SetActive(false);

            var silhouette = Panel(root.transform, "Silhouette",
                new Color(0.04f, 0.10f, 0.14f, 1f),
                new Vector2(0.035f, 0.14f), new Vector2(0.385f, 0.84f),
                Vector2.zero, Vector2.zero);
            view.SilhouetteShapes = CreateSilhouette(silhouette.transform,
                kind ?? Stage1Exploration.PartKind.Capsule);

            for (var i = 0; i < view.StateSegments.Length; i++)
            {
                var xMin = i * 0.255f;
                view.StateSegments[i] = Panel(root.transform, $"State_{i + 1}",
                    new Color(0.12f, 0.20f, 0.25f, 1f),
                    new Vector2(0.43f + xMin * 0.51f, 0.17f),
                    new Vector2(0.43f + (xMin + 0.20f) * 0.51f, 0.31f),
                    Vector2.zero, Vector2.zero).GetComponent<Image>();
            }
            return view;
        }

        static Image[] CreateSilhouette(Transform parent, Stage1Exploration.PartKind kind)
        {
            return kind switch
            {
                Stage1Exploration.PartKind.HoverPod => new[]
                {
                    Shape(parent, "Body", .25f, .38f, .75f, .58f),
                    Shape(parent, "Top", .34f, .58f, .66f, .74f),
                    Shape(parent, "Wing", .12f, .31f, .88f, .36f),
                },
                Stage1Exploration.PartKind.Robot => new[]
                {
                    Shape(parent, "Head", .38f, .60f, .62f, .82f),
                    Shape(parent, "Body", .27f, .30f, .73f, .60f),
                    Shape(parent, "ArmL", .12f, .42f, .27f, .50f),
                    Shape(parent, "ArmR", .73f, .42f, .88f, .50f),
                    Shape(parent, "LegL", .31f, .12f, .43f, .30f),
                    Shape(parent, "LegR", .57f, .12f, .69f, .30f),
                },
                Stage1Exploration.PartKind.TractorCraft => new[]
                {
                    Shape(parent, "Body", .18f, .40f, .82f, .54f),
                    Shape(parent, "Canopy", .36f, .54f, .64f, .70f),
                    Shape(parent, "Stem", .45f, .25f, .55f, .40f),
                },
                _ => new[]
                {
                    Shape(parent, "Body", .30f, .25f, .70f, .75f),
                    Shape(parent, "Belt", .20f, .47f, .80f, .54f),
                    Shape(parent, "Top", .42f, .75f, .58f, .88f),
                },
            };
        }

        static Image Shape(Transform parent, string name, float xMin, float yMin,
                           float xMax, float yMax)
        {
            return Panel(parent, name, Color.gray,
                new Vector2(xMin, yMin), new Vector2(xMax, yMax),
                Vector2.zero, Vector2.zero).GetComponent<Image>();
        }

        void RefreshParts()
        {
            for (var i = 0; i < discoveryDots.Length; i++)
            {
                discoveryDots[i].gameObject.SetActive(true);
                discoveryDots[i].color = i < exploration.DiscoveredCount
                    ? new Color(0.45f, 1f, 0.70f, 0.95f)
                    : new Color(0.24f, 0.36f, 0.42f, 0.95f);
            }
            for (var i = 0; i < partCards.Length; i++)
            {
                var view = partCards[i];
                var info = i < exploration.Parts.Count ? exploration.Parts[i] : null;
                var discovered = info != null && info.IsDiscovered;
                var accent = PartAccent(info);
                view.Background.color = discovered
                    ? new Color(0.07f, 0.18f, 0.19f, 1f)
                    : new Color(0.08f, 0.12f, 0.17f, 1f);
                view.Accent.color = accent;
                view.Number.text = info == null ? "--" : info.Number.ToString("00");
                view.Name.text = discovered ? info.DisplayName : "미발견 파츠";

                var level = StateLevel(info);
                for (var segment = 0; segment < view.StateSegments.Length; segment++)
                    view.StateSegments[segment].color = segment < level
                        ? accent : new Color(0.12f, 0.20f, 0.25f, 1f);
                var silhouetteColor = discovered
                    ? new Color(0.48f, 1f, 0.72f, 0.82f)
                    : new Color(0.38f, 0.52f, 0.62f, 0.72f);
                foreach (var shape in view.SilhouetteShapes) shape.color = silhouetteColor;
            }
        }

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

        string PartsText()
        {
            var text = new StringBuilder(512);
            text.Append("발견한 파츠  ").Append(exploration.DiscoveredCount)
                .Append(" / ").Append(Stage1Exploration.RequiredPartCount).AppendLine().AppendLine();

            foreach (var info in exploration.Parts)
            {
                if (info == null) continue;
                var level = StateLevel(info);
                var color = level >= 4 ? "#55FFA6" : info.IsDiscovered ? "#70DFFF" : "#71818C";
                text.Append("<color=").Append(color).Append(">")
                    .Append(info.Number.ToString("00")).Append("   ")
                    .Append(info.IsDiscovered ? info.DisplayName : "미발견 파츠")
                    .Append("     ");
                for (var i = 0; i < 4; i++) text.Append(i < level ? "■" : "□");
                text.Append("</color>").AppendLine().AppendLine();
            }
            return text.ToString();
        }

        static string HelpText(int page)
        {
            return page switch
            {
                1 => "WASD          카메라 기준 이동\n\nShift             달리기\n\nSpace           점프\n\n마우스          1인칭 시점·정면\n\nAlt                현재 카메라 기준으로 자유 시점",
                2 => "좌·우클릭     고정쇠 잡기 / 도구 사용\n\nCtrl               팔 올리기\n\nW + 잡기       레일·케이블 오르기\n\nQ 짧게          장비 내려놓기\n\nQ 길게          바라보는 방향으로 장비 던지기",
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

        static int StateLevel(Stage1Exploration.PartInfo info)
        {
            if (info == null || !info.IsDiscovered) return 0;
            if (info.Part == null) return 1;
            return info.Part.State switch
            {
                PartState.Locked => 1,
                PartState.Free => 2,
                PartState.Carried => 3,
                PartState.Placed or PartState.Welded => 4,
                _ => 1,
            };
        }

        static Color PartAccent(Stage1Exploration.PartInfo info)
        {
            if (info == null) return new Color(1f, 0.36f, 0.30f, 0.95f);
            if (!info.IsDiscovered) return new Color(0.30f, 0.42f, 0.52f, 0.95f);
            if (info.Part == null) return new Color(0.62f, 0.78f, 0.84f, 0.95f);
            return info.Part.State switch
            {
                PartState.Placed or PartState.Welded => new Color(0.32f, 1f, 0.64f, 0.95f),
                PartState.Free or PartState.Carried => new Color(0.38f, 0.88f, 1f, 0.95f),
                _ => new Color(0.62f, 0.78f, 0.84f, 0.95f),
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
