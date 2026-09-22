using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using Metagame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>태블릿이 조립 상태를 실제로 보여주는지</b> 검증한다(기획 §11.1,
    /// S15P21B202-345).
    ///
    /// <b>이 스위트가 있는 이유.</b> S15P21B202-275 에서 <c>MeteorWarningView</c> 를
    /// 아무도 만들지 않아 예고 원이 조용히 사라진 일이 있었다. 태블릿도 같은 모양의
    /// 위험을 갖는다 — <see cref="ShipAssembly"/> 를 찾지 못하면 탑이 통째로 빈칸이
    /// 되면서 아무 오류도 나지 않는다.
    ///
    /// 그래서 <b>만들어졌는가</b> · <b>형태가 붙었는가</b> · <b>상태가 색으로 나오는가</b>
    /// 를 각각 본다.
    /// </summary>
    public sealed class TabletTowerTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            CleanScene();

            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }


        /// <summary>
        /// 씬과 정적 목록에 남는 것을 치운다.
        ///
        /// <b>목록만 비우면 안 된다.</b> S15P21B202-334 에서 씬에 남은 인형이 뒤에 도는
        /// 스위트의 물리 질의에 걸려 상승 검증을 넘어뜨렸다. 여기서는 조립까지 하므로
        /// <c>PartAssemblyLink.All</c> 과 <c>WeldBead.All</c> 도 남는다 — 그것이 남으면
        /// 뒤 스위트의 탑 판정이 이 스위트의 접합을 함께 본다.
        /// </summary>
        void CleanScene()
        {
            DestroyAll<SpaceshipPart>();
            DestroyAll<ShipAssembly>();
            DestroyAll<PlayerRig>();
            DestroyAll<LowGravityField>();

            PartSocket.All.Clear();
            PartAssemblyLink.All.Clear();
            WeldBead.All.Clear();
            PartClamp.All.Clear();
            PartPush.All.Clear();
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();
        }

        static void DestroyAll<T>() where T : Component
        {
            foreach (var found in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (found != null)
                    Object.DestroyImmediate(found.gameObject);
        }

        // --- 만들어지는가 -----------------------------------------------------

        [Test]
        public void TheTowerHasOneSilhouettePerFloor()
        {
            var screen = MakeScreen();

            var graphics = screen.GetComponentsInChildren<PartSilhouetteGraphic>(true);

            // 층마다 채움과 외곽선 하나씩이다.
            Assert.AreEqual(PartSpec.All.Length * 2, graphics.Length,
                "층별 실루엣이 층 수와 맞지 않는다.");
        }

        /// <summary>
        /// <b>275 형 결함을 막는 검증이다.</b> 형태가 붙지 않으면 그릴 것이 없어 탑이
        /// 통째로 사라지고, 오류는 나지 않는다.
        /// </summary>
        [Test]
        public void EverySilhouetteGetsItsShape()
        {
            var screen = MakeScreen();

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
                Assert.IsTrue(graphic.HasProfile,
                    $"{graphic.name} 에 형태가 붙지 않았다. 태블릿이 빈칸으로 보인다.");
        }

        /// <summary>
        /// <b>이 검증이 처음에 헛돌았다.</b> <c>BuildMesh</c> 를 직접 불러 "삼각형이
        /// 나오는가" 만 봤는데, 그 경로는 <see cref="CanvasRenderer"/> 를 지나지 않는다.
        /// 실제로는 <c>CanvasRenderer</c> 가 없어 <b>메시는 만들어지는데 화면에는 아무것도
        /// 안 나오고 있었다</b> — 인게임 스크린샷으로 발견했다.
        ///
        /// 원인은 <see cref="RequireComponent"/> 가 <b>상속되지 않는 것</b>이다.
        /// <see cref="Image"/> 와 <see cref="Text"/> 는 각자 다시 붙여 두었다.
        /// </summary>
        [Test]
        public void EverySilhouetteHasACanvasRenderer()
        {
            var screen = MakeScreen();

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
                Assert.IsNotNull(graphic.GetComponent<CanvasRenderer>(),
                    $"{graphic.name} 에 CanvasRenderer 가 없다. "
                    + "메시는 만들어지지만 화면에는 아무것도 안 나온다.");
        }

        /// <summary>
        /// 실제로 그려지는 경로를 지나간다. <c>Rebuild</c> 는 <c>CanvasRenderer</c> 에
        /// 메시와 재질을 넣으므로, 그것이 없으면 여기서 예외가 난다.
        /// </summary>
        [Test]
        public void EverySilhouetteSurvivesARealRebuild()
        {
            var screen = MakeScreen();

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
            {
                // Explicitly exercise the fallback; the normal illustrated page hides it.
                graphic.gameObject.SetActive(true);
                Assert.DoesNotThrow(() => graphic.Rebuild(CanvasUpdate.PreRender),
                    $"{graphic.name} 을 실제로 그리려 하면 터진다.");

                Assert.Greater(graphic.canvasRenderer.materialCount, 0,
                    $"{graphic.name} 에 재질이 들어가지 않았다.");
            }
        }

        /// <summary>
        /// <b>삼각형 개수로는 부족하다.</b> <c>Quad</c> 는 크기가 0 이어도 삼각형을
        /// 만들므로, 정점이 <see cref="RectTransform"/> 을 실제로 채우는지 봐야 한다.
        /// </summary>
        [Test]
        public void EverySilhouetteFillsItsRect()
        {
            var screen = MakeScreen();

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
            {
                var rect = ((RectTransform)graphic.transform).rect;
                Assert.Greater(rect.width, 1f, $"{graphic.name} 의 가로가 없다.");
                Assert.Greater(rect.height, 1f, $"{graphic.name} 의 세로가 없다.");

                var helper = new VertexHelper();
                var verts = new List<UIVertex>();
                try
                {
                    graphic.BuildMesh(helper);
                    helper.GetUIVertexStream(verts);
                }
                finally
                {
                    helper.Dispose();
                }

                Assert.Greater(verts.Count, 0, $"{graphic.name} 이 정점을 만들지 않는다.");

                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var v in verts)
                {
                    min = Vector2.Min(min, v.position);
                    max = Vector2.Max(max, v.position);
                }

                // 세로는 rect 를 꽉 채워야 한다. 가로는 파츠가 좁아지는 구간이 있으므로
                // 가장 넓은 곳이 rect 폭에 닿는 것으로 본다.
                Assert.AreEqual(rect.height, max.y - min.y, rect.height * 0.1f,
                    $"{graphic.name} 의 정점이 세로를 채우지 않는다.");
                Assert.Greater(max.x - min.x, rect.width * 0.8f,
                    $"{graphic.name} 의 정점이 가로를 거의 채우지 않는다.");
            }
        }

        /// <summary>
        /// 층 이름은 기획 §7.1 의 표에서 온다. 순서를 알려주는 것이 태블릿의 일이고,
        /// 그 순서는 §7.1 이 이미 공개한 것이다.
        /// </summary>
        [Test]
        public void EachFloorIsLabelledWithItsFloorNumberAndName()
        {
            var screen = MakeScreen();

            var labels = new List<string>();
            foreach (var text in screen.GetComponentsInChildren<Text>(true))
                if (text.name.StartsWith("Floor"))
                    labels.Add(text.text);

            for (var i = 0; i < PartSpec.All.Length; i++)
            {
                var expected = $"{i + 1}층";
                var name = PartSpec.All[i].DisplayName;
                Assert.IsTrue(labels.Exists(l => l.Contains(expected) && l.Contains(name)),
                    $"'{expected} {name}' 이 태블릿에 없다. 실제 이름: {string.Join(" / ", labels)}");
            }
        }

        // --- 실제 비율인가 ----------------------------------------------------

        /// <summary>
        /// 탑이 실제 비율이어야 형태를 보고 파츠를 알아볼 수 있다. 부양 모듈은 1.33m 로
        /// 납작하고 상단 모듈은 2.45m 로 가장 높다.
        /// </summary>
        [Test]
        public void FloorHeightsFollowTheMeasuredPartHeights()
        {
            var screen = MakeScreen();

            var outlines = FindOutlines(screen);

            for (var i = 0; i < PartSpec.All.Length; i++)
            {
                var rect = (RectTransform)outlines[i].transform;
                var share = rect.anchorMax.y - rect.anchorMin.y;
                var expected = PartSpec.All[i].Height / PartSpec.StackedHeight;

                Assert.AreEqual(expected, share, 0.005f,
                    $"{i + 1}층의 높이 비율이 실측과 다르다. "
                    + $"{PartSpec.All[i].DisplayName} 은 {PartSpec.All[i].Height:0.00}m 다.");
            }
        }

        [Test]
        public void TheFloorsAreStackedBottomToTop()
        {
            var screen = MakeScreen();

            var outlines = FindOutlines(screen);

            for (var i = 1; i < outlines.Length; i++)
            {
                var below = (RectTransform)outlines[i - 1].transform;
                var above = (RectTransform)outlines[i].transform;

                Assert.AreEqual(below.anchorMax.y, above.anchorMin.y, 0.001f,
                    $"{i}층과 {i + 1}층 사이가 붙어 있지 않다.");
            }
        }

        [Test]
        public void WiderPartsAreDrawnWider()
        {
            var screen = MakeScreen();

            var outlines = FindOutlines(screen);

            // 상단 조립 모듈(1.90m)이 부양·안정화 모듈(2.30m)보다 좁아야 한다.
            var hover = (RectTransform)outlines[1].transform;
            var top = (RectTransform)outlines[3].transform;

            Assert.Less(top.anchorMax.x - top.anchorMin.x,
                        hover.anchorMax.x - hover.anchorMin.x,
                        "폭이 실측 비율을 따르지 않는다.");
        }

        // --- 상태가 색으로 나오는가 -------------------------------------------

        [Test]
        public void AnUndiscoveredFloorIsDrawnDark()
        {
            var screen = MakeScreen();
            screen.Tick();

            var outline = FindOutlines(screen)[3];

            Assert.Less(Brightness(outline.color), 0.35f,
                $"미발견 층이 어둡지 않다({outline.color}). §11.1 은 어두운 회색이라고 정했다.");
        }

        /// <summary>
        /// 사용자 스케치 2·3 — 제대로 쌓이고 용접되면 초록(§11.1 의 청록)이다.
        /// </summary>
        [Test]
        public void AWeldedFloorTurnsTealAndFillsIn()
        {
            Stack(PartSpec.All.Length, weld: true);
            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, PartSpec.All.Length);
            Assembly().Evaluate();
            screen.Tick();

            // 2층을 본다 — 1층은 아래에 용접할 상대가 없어 놓인 순간 완료로 보고되므로
            // 실제로 용접한 층을 확인해야 한다.
            var fill = FindFills(screen)[1];
            var outline = FindOutlines(screen)[1];

            Assert.Greater(outline.color.g, outline.color.r + 0.3f,
                $"용접된 층의 외곽선이 청록이 아니다({outline.color}).");
            Assert.Greater(fill.color.a, 0.4f,
                $"용접된 층이 채워지지 않았다(알파 {fill.color.a:0.00}). "
                + "스케치 2·3 은 안까지 초록으로 칠했다.");
        }

        /// <summary>
        /// 사용자 스케치 4 — 뭔가 잘못되면 빨강이다.
        /// </summary>
        [Test]
        public void AWrongFloorTurnsRed()
        {
            var parts = Stack(PartSpec.All.Length, weld: false);
            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, PartSpec.All.Length);

            parts[2].transform.rotation =
                Quaternion.Euler(PhysicsScale.BoardingMaxTilt + 10f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();
            Assembly().Evaluate();
            screen.Tick();

            var outlines = FindOutlines(screen);

            Assert.Greater(outlines[2].color.r, outlines[2].color.g + 0.3f,
                $"기울어진 3층이 빨갛지 않다({outlines[2].color}).");
            Assert.Greater(outlines[0].color.g, outlines[0].color.r + 0.3f,
                $"멀쩡한 1층까지 빨갛다({outlines[0].color}). 어디를 고쳐야 할지 알 수 없다.");
        }

        /// <summary>
        /// §15.1 이 오류 문구를 금지한다. <see cref="ShipAssembly.Blocker"/> 문장이
        /// 화면에 나오면 안 된다.
        /// </summary>
        [Test]
        public void TheBlockerSentenceNeverReachesTheScreen()
        {
            Stack(2, weld: false);
            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, 2);

            var assembly = Assembly();
            assembly.Evaluate();
            screen.Tick();

            Assert.IsNotEmpty(assembly.Blocker, "먼저 막는 이유가 있어야 하는 상태다.");

            foreach (var text in screen.GetComponentsInChildren<Text>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                Assert.IsFalse(text.text.Contains(assembly.Blocker),
                    $"막는 이유가 '{text.name}' 에 그대로 떠 있다. §15.1 이 금지한다.");
            }
        }

        /// <summary>
        /// 발사 실패 이유도 태블릿에 두지 않는다. 사용자가 그렇게 정했고, §22.4 는
        /// 실패를 물리로 보이라고 정했다.
        /// </summary>
        [Test]
        public void NoFloorLabelMentionsWeldRatioOrScore()
        {
            var screen = MakeScreen();

            string[] banned = { "용접률", "점수", "%", "정렬", "오차" };

            foreach (var text in screen.GetComponentsInChildren<Text>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                if (!text.name.StartsWith("Floor")) continue;

                foreach (var word in banned)
                    Assert.IsFalse(text.text.Contains(word),
                        $"'{word}' 가 층 이름에 있다. §3.4 · §11 이 금지한다. — {text.text}");
            }
        }

        /// <summary>
        /// <b>화면이 두 가지를 동시에 말하면 안 된다.</b>
        /// <see cref="ShipAssembly.NextFloor"/> 는 첫 빈 층을 사실대로 알려 주므로 아래층이
        /// 틀렸어도 위층을 가리킨다. 그대로 점멸시키면 "3층을 치워라" 와 "4층을 올려라" 가
        /// 함께 뜬다 — 지금 할 일은 치우는 쪽이다.
        /// </summary>
        [Test]
        public void TheNextFloorHintGoesQuietWhileSomethingIsWrong()
        {
            var parts = Stack(3, weld: false);

            // <b>4층을 발견 상태로 두어야 점멸을 잴 수 있다.</b> 미발견이면 어두운 회색이
            // 먼저 이기므로, 점멸을 끄지 않아도 이 검증이 통과해 버린다.
            MakePart(PartSpec.All[3], new Vector3(60f, PartSpec.All[3].Height * 0.5f, 60f));

            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, PartSpec.All.Length);

            parts[2].transform.rotation =
                Quaternion.Euler(PhysicsScale.BoardingMaxTilt + 10f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();

            var assembly = Assembly();
            assembly.Evaluate();
            Assert.AreEqual(3, assembly.NextFloor,
                "전제가 깨졌다 — 4층이 아직 다음 차례여야 한다.");

            screen.Tick();

            var outlines = FindOutlines(screen);

            Assert.Greater(outlines[2].color.r, outlines[2].color.g + 0.3f,
                $"기울어진 3층이 빨갛지 않다({outlines[2].color}).");

            // 안내 색은 거의 흰빛(r 0.86)이고 발견 색은 차가운 회색(r 0.55)이다.
            // 점멸은 알파만 바꾸므로 r 로 갈라진다.
            var hint = outlines[3].color;
            Assert.Less(hint.r, 0.7f,
                $"잘못된 층이 있는데 4층이 여전히 안내로 점멸한다({hint}).");
        }

        // --- 조립대가 없어도 무너지지 않는가 ----------------------------------

        /// <summary>
        /// <b>275 형 결함의 두 번째 얼굴이다.</b> 씬에 조립대가 없는 상황에서도 태블릿은
        /// 형태를 보여줘야 한다 — 조용히 빈 화면이 되는 것이 가장 나쁘다.
        /// </summary>
        [Test]
        public void TheTowerStillDrawsWhenThereIsNoAssembly()
        {
            var screen = MakeScreen();
            screen.Tick();

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
                Assert.IsTrue(graphic.HasProfile,
                    $"{graphic.name} 이 조립대 없이는 아무것도 그리지 않는다.");
        }

        /// <summary>
        /// <b>탐색 상한이 영구 포기가 되면 안 된다.</b> 매 프레임 씬 전체를 뒤지지 않으려고
        /// 횟수를 묶었는데, 소진한 뒤 되돌리지 않으면 늦게 만들어진 조립대를 영원히
        /// 못 찾는다 — 탑이 통째로 빈칸이 되고 아무 오류도 나지 않는다. 막으려던
        /// S15P21B202-275 형 결함과 같은 모양이다.
        /// </summary>
        [Test]
        public void ReopeningTheTabletLooksForTheAssemblyAgain()
        {
            var screen = MakeScreen(out var exploration);

            // 조립대 없이 예산을 넉넉히 소진시킨다.
            for (var i = 0; i < 200; i++) screen.Tick();

            // <b>두 층으로 족하다.</b> 2층이 청록이 되는지만 보므로 용접도 4층도 필요
            // 없다. 이 스위트가 씬에 남기는 물리 이력이 작을수록 좋다 —
            // ShipAscentTests 와 TowerCollapseTests 가 그 이력에 민감하다.
            Stack(2, weld: false);

            // 화면을 먼저 만들었으므로 파츠를 다시 물려 준다 — Configure 는 참조로
            // 짝을 맺으므로 그 전에 만든 상태에는 파츠가 없다.
            exploration.Configure(
                Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
            DiscoverEverything(exploration, 2);
            Assembly().Evaluate();

            screen.Tick();

            // 예산이 소진됐으면 조립대를 못 찾아 2층이 "발견했지만 탑에 없음" 회색이다.
            // 어두운 색이 아니라 회색인 것에 주의한다 — 파츠는 발견된 상태다.
            var before = FindOutlines(screen)[1].color;
            Assert.Less(before.g, before.r + 0.3f,
                $"전제가 깨졌다 — 예산이 소진돼 아직 조립대를 못 찾은 상태여야 한다({before}).");

            exploration.SetTabletOpen(false);
            screen.Tick();
            exploration.SetTabletOpen(true);
            screen.Tick();

            Assembly().Evaluate();
            screen.Tick();

            var outline = FindOutlines(screen)[1];
            Assert.Greater(outline.color.g, outline.color.r + 0.3f,
                $"다시 열었는데도 조립대를 못 찾아 2층이 회색이다({outline.color}).");
        }

        // The tests above retain coverage of the resource-missing silhouette fallback.
        // These checks assert the visible illustrated path, not the hidden fallback.
        [Test]
        public void ApprovedArtworkIsVisibleAndMissingPartsAreDim()
        {
            var screen = MakeScreen();
            for (var i = 1; i <= 4; i++)
            {
                var art = FindNamed<RawImage>(screen, $"AssemblyPart{i}");
                Assert.IsTrue(art.gameObject.activeInHierarchy);
                Assert.IsNotNull(art.texture);
                Assert.AreEqual("Metagame/UI/Tablet Chalk Ink", art.material.shader.name);
                Assert.Less(art.color.r, 0.5f);
            }
            Assert.AreEqual("배치 0 / 4", FindNamed<Text>(screen, "AssemblyCount").text);
            Assert.AreEqual("파츠를 가져와 주세요", FindNamed<Text>(screen, "AssemblyAction").text);
        }

        [Test]
        public void PlacedArtBrightensButStillAsksForWelding()
        {
            Stack(4, weld: false);
            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, 4);
            Assembly().Evaluate();
            screen.Tick();
            Assert.Greater(FindNamed<RawImage>(screen, "AssemblyPart2").color.r, 0.7f);
            StringAssert.Contains("용접해 주세요", FindNamed<Text>(screen, "AssemblyAction").text);
            Assert.AreEqual("배치 4 / 4 · 용접 대기", FindNamed<Text>(screen, "AssemblyCount").text);
            Assert.IsEmpty(FindNamed<Text>(screen, "AssemblyCheck2").text);
            Assert.AreEqual("✓", FindNamed<Text>(screen, "AssemblyStepCheck1").text);
            Assert.IsEmpty(FindNamed<Text>(screen, "AssemblyStepCheck2").text);
        }

        [Test]
        public void WrongArtOverridesWeldHintAndCompletionClearsIt()
        {
            var parts = Stack(4, weld: true);
            var screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, 4);
            Assembly().Evaluate();
            screen.Tick();
            Assert.AreEqual("조립 완료!", FindNamed<Text>(screen, "AssemblyAction").text);
            Assert.AreEqual("✓", FindNamed<Text>(screen, "AssemblyCheck2").text);
            parts[2].transform.rotation = Quaternion.Euler(PhysicsScale.BoardingMaxTilt + 10f, 0, 0);
            UnityEngine.Physics.SyncTransforms();
            Assembly().Evaluate();
            screen.Tick();
            Assert.AreEqual("파츠를 다시 놓아 주세요", FindNamed<Text>(screen, "AssemblyAction").text);
            Assert.Greater(FindNamed<RawImage>(screen, "AssemblyPart3").color.r,
                FindNamed<RawImage>(screen, "AssemblyPart3").color.g);
        }

        [Test]
        public void TabletTextHasReadableMinimumSizeAndBoundedRasterDensity()
        {
            var screen = MakeScreen();
            foreach (var label in screen.GetComponentsInChildren<Text>(true))
            {
                Assert.GreaterOrEqual(label.fontSize, 24, label.name);
                Assert.AreSame(Resources.Load<Font>("Fonts/SUIT-Bold"), label.font, label.name);
            }
            Assert.AreEqual(2f, screen.GetComponentInChildren<CanvasScaler>(true).dynamicPixelsPerUnit);
        }

        [Test]
        public void IllustrationImportPreservesSizeAndSmoothMinification()
        {
            foreach (var name in new[] { "Ship", "Actions" })
            {
                var texture = Resources.Load<Texture2D>("TabletAssembly/" + name);
                Assert.IsNotNull(texture);
                Assert.Greater(texture.mipmapCount, 1);
                Assert.AreEqual(FilterMode.Trilinear, texture.filterMode);
                Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapMode);
            }
            var ship = Resources.Load<Texture2D>("TabletAssembly/Ship");
            Assert.AreEqual(1774, ship.width);
            Assert.AreEqual(887, ship.height);
        }

        [Test]
        public void CompactFooterPreservesCurrentPageAndControlKeys()
        {
            var text = TabletHelpContent.PageStrip(false, 0, 2, compact: true);
            StringAssert.Contains(TabletHelpContent.PageDots(false, 2).Replace(" ", ""), text);
            StringAssert.Contains("A/D", text);
            StringAssert.Contains(TabletHelpContent.Cap(Controls.Tablet), text);
        }

        [Test, Explicit("Graphics-device visual QA only")]
        public void CaptureAssemblyArtworkPreview()
        {
            var screen = MakeScreen();
            Capture(screen, "tablet-empty");
            for (var page = 1; page <= 3; page++)
            {
                var state = Object.FindAnyObjectByType<Stage1Exploration>();
                state.NextTabletPage();
                screen.Tick();
                Capture(screen, $"tablet-help-{page}");
            }
            Object.DestroyImmediate(screen.gameObject);
            Stack(4, weld: false);
            screen = MakeScreen(out var exploration);
            DiscoverEverything(exploration, 4);
            Assembly().Evaluate();
            screen.Tick();
            Capture(screen, "tablet-placed");
        }

        void Capture(Stage1TabletScreen screen, string name)
        {
            var canvas = screen.GetComponentInChildren<Canvas>(true);
            canvas.transform.localPosition = Vector3.zero;
            canvas.transform.localRotation = Quaternion.identity;
            canvas.transform.localScale = Vector3.one;
            foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var cameraObject = new GameObject("Tablet QA camera");
            _spawned.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 256;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            foreach (var width in new[] { 2048, 1024 })
            {
                var target = new RenderTexture(width, width / 2, 24);
                var image = new Texture2D(width, width / 2, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, width / 2), 0, 0);
                    image.Apply();
                    System.IO.File.WriteAllBytes($"Logs/{name}-{width}.png", image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(image);
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
        }

        static T FindNamed<T>(Component screen, string name) where T : Component
        {
            foreach (var item in screen.GetComponentsInChildren<T>(true))
                if (item.name == name) return item;
            Assert.Fail($"Missing visible UI: {name}");
            return null;
        }

        // --- 하니스 -----------------------------------------------------------

        Stage1TabletScreen MakeScreen() => MakeScreen(out _);

        Stage1TabletScreen MakeScreen(out Stage1Exploration exploration)
        {
            var stateGo = new GameObject("Exploration");
            _spawned.Add(stateGo);
            exploration = stateGo.AddComponent<Stage1Exploration>();

            // <b>씬의 파츠로 Configure 해야 발견이 성립한다.</b> null 을 넘기면
            // PartInfo.Part 가 비어 있고, TryDiscover 는 그 참조로 짝을 찾으므로
            // 무엇을 발견해도 미발견으로 남는다 — 색 검증이 통째로 헛돌았다.
            exploration.Configure(
                Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None));
            exploration.SetTabletOpen(true);

            var screenGo = new GameObject("ScreenSurface");
            _spawned.Add(screenGo);
            var renderer = screenGo.AddComponent<MeshRenderer>();

            var screen = screenGo.AddComponent<Stage1TabletScreen>();
            screen.Initialize(exploration, renderer);
            return screen;
        }

        /// <summary>
        /// 씬의 파츠를 모두 발견 처리한다.
        ///
        /// <b>발견됐는지 확인한다.</b> 발견이 성립하지 않으면 미발견 색이 먼저 이기므로
        /// 색 검증이 조용히 헛돈다.
        /// </summary>
        static void DiscoverEverything(Stage1Exploration exploration, int expected)
        {
            foreach (var part in Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None))
                exploration.TryDiscover(part);

            Assert.AreEqual(expected, exploration.DiscoveredCount,
                "하니스가 파츠를 발견 처리하지 못했다. 색 검증이 무의미해진다.");
        }

        ShipAssembly Assembly()
        {
            var existing = Object.FindAnyObjectByType<ShipAssembly>();
            if (existing != null) return existing;

            var pad = new GameObject("LaunchPad");
            _spawned.Add(pad);
            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);
            return assembly;
        }

        SpaceshipPart[] Stack(int floors, bool weld)
        {
            MakeGround();
            Assembly();

            var parts = new SpaceshipPart[floors];
            var y = 0f;

            for (var i = 0; i < floors; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < floors; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                if (!match.HasValue) continue;

                var link = PartAssembler.Assemble(match);
                if (link == null) continue;
                if (weld) link.ForceWeldComplete();
            }

            return parts;
        }

        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            _spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body");
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.CenterFromPartCenter(spec.Height);
                child.AddComponent<BoxCollider>().size = box.Size;
            }

            root.AddComponent<Rigidbody>();
            var part = root.AddComponent<SpaceshipPart>();

            foreach (var role in new[] { SocketRole.Base, SocketRole.Top })
            {
                var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;
                var socket = new GameObject(role.ToString());
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = direction * (spec.Height * 0.5f);
                socket.transform.localRotation =
                    Quaternion.LookRotation(direction, Vector3.forward);
                socket.AddComponent<PartSocket>().SetRole(role);
            }

            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            part.RefreshSockets();
            UnityEngine.Physics.SyncTransforms();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            _spawned.Add(go);
        }

        static PartSilhouetteGraphic[] FindOutlines(Component screen) =>
            Pick(screen, "_Outline");

        static PartSilhouetteGraphic[] FindFills(Component screen) =>
            Pick(screen, "_Fill");

        /// <summary>층 순서(1층이 0번)로 골라 준다.</summary>
        static PartSilhouetteGraphic[] Pick(Component screen, string suffix)
        {
            var found = new PartSilhouetteGraphic[PartSpec.All.Length];

            foreach (var graphic in screen.GetComponentsInChildren<PartSilhouetteGraphic>(true))
            {
                if (!graphic.name.EndsWith(suffix)) continue;

                for (var i = 0; i < found.Length; i++)
                    if (graphic.name.StartsWith($"Floor{i + 1}_"))
                        found[i] = graphic;
            }

            for (var i = 0; i < found.Length; i++)
                Assert.IsNotNull(found[i], $"{i + 1}층의 {suffix} 를 찾지 못했다.");

            return found;
        }

        static float Brightness(Color color) =>
            (color.r + color.g + color.b) / 3f * Mathf.Max(color.a, 0.001f);
    }
}
