using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Interaction;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 화면 안내의 <b>말과 표시 방식</b>을 지킨다 (S15P21B202-679).
    ///
    /// <para>화면에 안내 문법이 셋이었다 — 태블릿은 네모 점 줄과 글자 점 줄을 함께
    /// 띄웠고(둘이 서로 다른 쪽을 가리켰다), HUD 는 왼쪽 아래만 네모 키캡을 썼다.
    /// 여기서 지키는 것은 <b>키는 늘 <c>[E]</c> 꼴</b>이고 <b>쪽 표시는 하나</b>라는
    /// 것이다.</para>
    /// </summary>
    public sealed class HudNoticeStyleTests
    {
        // --- 키는 언제나 [E] 꼴 -------------------------------------------------------

        /// <summary>
        /// <b>키를 글자로 적을 때는</b> 늘 <c>[E]</c> 꼴이다.
        ///
        /// <para>예전에는 "모든 안내에 키 글자가 있다" 로 적었는데, S15P21B202-696 에서
        /// 잡기가 <b>마우스 그림</b>으로 바뀌면서 그 전제가 깨졌다. 재려던 것은 애초에
        /// "글자로 적을 때 문법이 하나인가" 였다.</para>
        /// </summary>
        [Test]
        public void EveryPromptKeyThatIsTextIsBracketed()
        {
            foreach (var prompt in AllPrompts())
            {
                if (prompt.Glyph != HudGlyph.None) continue;
                Assert.That(prompt.Key, Does.StartWith("[").And.EndWith("]"),
                    $"'{prompt.Label}' 의 키가 [E] 꼴이 아니다 — 화면마다 다른 문법이 된다.");
            }
        }

        [Test]
        public void TheKeyNameMatchesTheTablet()
        {
            // 손으로 적으면 키를 바꾼 사람에게 거짓말이 된다(S15P21B202-657).
            // **탑승으로 잰다** — 잡기는 마우스라 키 이름이 없다(S15P21B202-696).
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Transport);
            Assert.That(prompt.Key, Is.EqualTo("[" + TabletHelpContent.Name(Controls.TransportAction) + "]"));
        }

        [Test]
        public void RebindingChangesTheHint()
        {
            // 키를 바꾼 사람에게 안내가 거짓말을 하면 안 된다 — 이것이 키 이름을
            // Controls 에서 읽는 이유다(S15P21B202-657).
            var original = Controls.TransportAction;
            try
            {
                Controls.TransportAction = UnityEngine.KeyCode.F7;
                // 탑승으로 잰다 — 잡기는 바꿀 수 없는 마우스 버튼이라 그림이다.
                var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Transport);
                Assert.That(prompt.Key, Is.EqualTo("[F7]"));
            }
            finally { Controls.TransportAction = original; }
        }

        [Test]
        public void NumberKeysAreWrittenAsTheNumber()
        {
            // KeyCode.Alpha1 의 이름은 "Alpha1" 이지만 키캡에 적힌 글자는 "1" 이다.
            Assert.That(TabletHelpContent.Name(UnityEngine.KeyCode.Alpha1), Is.EqualTo("1"));
            Assert.That(TabletHelpContent.Cap(UnityEngine.KeyCode.Alpha1), Is.EqualTo("[1]"));
        }

        // --- 말 -----------------------------------------------------------------------

        [Test]
        public void GrabbingSaysHoldNotPickUp()
        {
            // "줍기" 는 집어서 주머니에 넣는 말로 읽힌다. 실제로는 손으로 쥔다.
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Grabbable);
            Assert.That(prompt.Label, Is.EqualTo("잡기"));
        }

        [Test]
        public void AGlyphIsOnlyUsedWhereTheInputCannotBeRebound()
        {
            // **키보드 키를 그림으로 박으면 키를 바꾼 사람에게 거짓말이 된다.**
            // 마우스 버튼은 바꿀 수 없어서 그림이 안전하다.
            foreach (var prompt in AllPrompts())
                if (prompt.Glyph != HudGlyph.None)
                    Assert.That(prompt.Key, Is.Null.Or.Empty,
                        "그림과 키 글자를 같이 넣었다 — 둘 중 하나만 있어야 한다.");
        }

        [Test]
        public void BeingStuckOutranksEverythingElse()
        {
            // 끼었을 때는 다른 무엇도 할 수 없다.
            var prompt = GameHud.PromptFor(true, true, true, PlayerInteractionKind.Transport);
            Assert.That(prompt.Key, Is.EqualTo(TabletHelpContent.Cap(Controls.StuckEscape)));
        }

        [Test]
        public void HoldingSomethingSilencesTheHint()
        {
            var prompt = GameHud.PromptFor(false, true, true, PlayerInteractionKind.Grabbable);
            Assert.That(prompt.Exists, Is.False, "도구를 들고 있는데 줍기 안내가 뜬다.");
        }

        [Test]
        public void NothingInReachDrawsNothing()
        {
            var prompt = GameHud.PromptFor(false, false, false, PlayerInteractionKind.Grabbable);
            Assert.That(prompt.Exists, Is.False);
        }

        // --- 안내가 그 동작의 **진짜 입력**을 가리킨다 (S15P21B202-696) ----------------
        //
        // 예전 검사는 "안내에 그 키가 있나" 만 봤다. 그래서 **잡기 안내가 [E] 로 뜨는
        // 것을 세 이슈가 지나가도록 놔뒀다** — E 로는 아무것도 못 줍는다.
        // 여기서 재는 것은 "있나" 가 아니라 "맞나" 다.

        [Test]
        public void GrabbingIsTaughtAsLeftClickNotAsAKey()
        {
            // Controls.GrabLeft = Input.GetMouseButton(0) 이다. E(TransportAction)는
            // 태블릿 단축키와 운반 장치 탑승·하차에만 쓰인다.
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Grabbable);
            Assert.That(prompt.Glyph, Is.EqualTo(HudGlyph.MouseLeft),
                "잡기 안내가 마우스 왼쪽 클릭 그림이 아니다.");
            Assert.That(prompt.Key ?? string.Empty,
                Does.Not.Contain(TabletHelpContent.Name(Controls.TransportAction)),
                "잡기 안내가 아직 E 를 가르친다 — E 로는 아무것도 못 줍는다.");
        }

        [Test]
        public void GrabbingTheClampIsAlsoLeftClick()
        {
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Clamp);
            Assert.That(prompt.Glyph, Is.EqualTo(HudGlyph.MouseLeft));
        }

        [Test]
        public void RidingIsStillTaughtAsTheTransportKey()
        {
            // **이쪽은 원래 맞았다.** 고치면서 같이 바꾸면 안 된다.
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Transport);
            Assert.That(prompt.Glyph, Is.EqualTo(HudGlyph.None));
            Assert.That(prompt.Key, Is.EqualTo(TabletHelpContent.Cap(Controls.TransportAction)));
        }

        [Test]
        public void TheTabletTeachesTheSameInputAsTheHud()
        {
            // 두 화면이 서로 다른 키를 가르치는 것이 S15P21B202-657 이 고치겠다고 한
            // 문제다. 줍기는 그 뒤로도 살아남았다.
            var e = TabletHelpContent.Name(Controls.TransportAction);
            foreach (var entry in AllHelpEntries())
            {
                if (entry.Keys != e) continue;
                Assert.That(entry.Description, Does.Not.Contain("줍기"),
                    "태블릿이 아직 E 로 줍는다고 가르친다.");
                Assert.That(entry.Description, Does.Not.Contain("잡기"),
                    "태블릿이 아직 E 로 잡는다고 가르친다.");
            }
        }

        [Test]
        public void TheTabletTeachesGrabbingSomewhere()
        {
            // 떼어내기만 하고 안 적으면 처음 하는 사람은 영영 못 찾는다.
            var grab = AllHelpEntries()
                .Where(e => e.Keys.Contains("좌클릭"))
                .Any(e => e.Description.Contains("잡기"));
            Assert.That(grab, Is.True, "태블릿 어디에도 '좌클릭 … 잡기' 가 없다.");
        }

        static System.Collections.Generic.IEnumerable<TabletHelpContent.Entry> AllHelpEntries()
        {
            for (var page = 1; page <= 3; page++)
                foreach (var entry in TabletHelpContent.Page(page))
                    yield return entry;
        }

        // --- 쪽 표시는 하나이고, 지금 있는 자리를 가리킨다 -----------------------------

        static int LitIndex(string strip)
        {
            var dots = strip.Where(c => c == '●' || c == '○').ToArray();
            return System.Array.IndexOf(dots, '●');
        }

        [Test]
        public void LeavingTheGuideMovesTheLightOutOfTheGuideGroup()
        {
            var inGuide = TabletHelpContent.PageStrip(true, TabletGuideState.PageCount - 1, 0);
            var left = TabletHelpContent.PageStrip(false, TabletGuideState.PageCount - 1, 0);

            Assert.That(LitIndex(inGuide), Is.LessThan(TabletGuideState.PageCount));
            Assert.That(LitIndex(left), Is.EqualTo(TabletGuideState.PageCount),
                "안내를 나왔는데 불이 아직 안내 칸에 있다.");
        }

        [Test]
        public void PagingRightThroughTheHelpMovesTheLightRight()
        {
            // 이것이 관측된 결함이다 — "오른쪽으로 가는데 오른쪽 불이 안 들어온다".
            // 예전에는 부르는 쪽이 늘 inGuide: true 를 넘겨 여기가 통째로 안 움직였다.
            var previous = -1;
            for (var page = 0; page < Stage1Exploration.TabletPageCount; page++)
            {
                var lit = LitIndex(TabletHelpContent.PageStrip(false, 0, page));
                Assert.That(lit, Is.GreaterThan(previous),
                    $"{page} 쪽으로 넘겼는데 불이 오른쪽으로 안 갔다.");
                previous = lit;
            }
        }

        [Test]
        public void TheStripIsTheOnlyPlaceThatSaysWhichPage()
        {
            // 네모 점 줄을 지웠다. 남은 한 줄에 쪽 수만큼의 점이 있어야 한다.
            var strip = TabletHelpContent.PageStrip(false, 0, 1);
            Assert.That(strip.Count(c => c == '●' || c == '○'),
                Is.EqualTo(TabletGuideState.PageCount + Stage1Exploration.TabletPageCount));
            Assert.That(strip.Count(c => c == '●'), Is.EqualTo(1), "불이 둘 이상이다.");
        }

        [Test]
        public void TheStripSurvivesAPageNumberOutOfRange()
        {
            Assert.That(LitIndex(TabletHelpContent.PageStrip(false, 0, 99)),
                Is.EqualTo(TabletGuideState.PageCount + Stage1Exploration.TabletPageCount - 1));
            Assert.That(LitIndex(TabletHelpContent.PageStrip(false, 0, -3)),
                Is.EqualTo(TabletGuideState.PageCount));
        }

        // --- 마우스 그림의 모양 (S15P21B202-696) ---------------------------------------
        //
        // 텍스처는 Apply(makeNoLongerReadable: true) 라 되읽을 수 없다. 그래서 픽셀
        // 계산을 따로 빼 두었고, 여기서 **그 결과를 직접 잰다.**

        /// <summary>
        /// <paramref name="v"/> 는 <b>위가 1</b> 이다. 텍스처 배열은 아래가 0행이라
        /// (Unity 규약) 그대로 곱하면 된다 — 처음에 <c>1 - v</c> 로 뒤집어 읽어서
        /// 위를 잰다고 해 놓고 아래를 재고 있었다.
        /// </summary>
        static Color At(Color[] pixels, int size, float u, float v) =>
            pixels[Mathf.Clamp(Mathf.RoundToInt(v * (size - 1)), 0, size - 1) * size
                   + Mathf.Clamp(Mathf.RoundToInt(u * (size - 1)), 0, size - 1)];

        [Test]
        public void TheMouseGlyphLightsTheLeftButtonAndNotTheRight()
        {
            // 이것이 "왼쪽 클릭" 을 말하는 전부다. 좌우가 바뀌면 거짓말이 된다.
            const int size = UiTheme.MouseGlyphSize;
            var pixels = UiTheme.MouseLeftPixels(size);

            var left = At(pixels, size, 0.40f, 0.72f);
            var right = At(pixels, size, 0.60f, 0.72f);
            Assert.That(left.a, Is.GreaterThan(0.95f), "왼쪽 버튼이 비어 있다.");
            Assert.That(left.grayscale, Is.GreaterThan(right.grayscale + 0.3f),
                "왼쪽 버튼이 오른쪽보다 밝지 않다 — 어느 쪽을 누르라는 건지 모른다.");
        }

        [Test]
        public void TheMouseGlyphHasABodyAndEmptyCorners()
        {
            const int size = UiTheme.MouseGlyphSize;
            var pixels = UiTheme.MouseLeftPixels(size);

            Assert.That(At(pixels, size, 0.5f, 0.5f).a, Is.GreaterThan(0.5f), "몸통이 비어 있다.");
            // 둥근 모서리 바깥은 투명해야 한다 — 알약 안에서 네모로 떠 보이면 안 된다.
            Assert.That(At(pixels, size, 0.06f, 0.96f).a, Is.LessThan(0.1f), "모서리가 안 둥글다.");
            Assert.That(At(pixels, size, 0.94f, 0.04f).a, Is.LessThan(0.1f), "모서리가 안 둥글다.");
        }

        [Test]
        public void TheMouseGlyphSurvivesBeingDrawnSmall()
        {
            // 화면에서는 24px 로 줄여 쓴다. 그 크기에서도 좌우가 갈려야 한다.
            const int size = 24;
            var pixels = UiTheme.MouseLeftPixels(size);
            var left = At(pixels, size, 0.40f, 0.72f);
            var right = At(pixels, size, 0.62f, 0.72f);
            Assert.That(left.grayscale, Is.GreaterThan(right.grayscale + 0.2f),
                "24px 에서 왼쪽·오른쪽이 안 갈린다.");
        }

        // --- 폰트 -------------------------------------------------------------------

        [Test]
        public void EveryNoticeGlyphIsCollectedForTheFont()
        {
            // 안내에 쓰는 글자를 폰트에 미리 굽지 않으면 **네모로 나온다.**
            // S15P21B202-657 이 같은 함정에 한 번 걸렸다.
            var collected = new StringBuilder();
            OnboardingNotices.AppendGlyphs(collected);
            GameHud.AppendPromptGlyphs(collected);
            var set = new System.Collections.Generic.HashSet<char>(collected.ToString());

            foreach (OnboardingCue cue in System.Enum.GetValues(typeof(OnboardingCue)))
                foreach (var c in OnboardingNotices.TextFor(cue))
                    Assert.That(set, Does.Contain(c), $"안내의 '{c}' 가 폰트 목록에 없다.");

            foreach (var prompt in AllPrompts())
                foreach (var c in prompt.Key + prompt.Label)
                    Assert.That(set, Does.Contain(c), $"안내의 '{c}' 가 폰트 목록에 없다.");
        }

        [Test]
        public void EveryPageStripGlyphIsCollectedForTheFont()
        {
            // 쪽 표시 줄은 두 화면(월드 태블릿 · 전체 화면 태블릿)이 함께 쓴다.
            var collected = new StringBuilder();
            TabletHelpContent.AppendGlyphs(collected);
            var set = new System.Collections.Generic.HashSet<char>(collected.ToString());
            foreach (var c in TabletHelpContent.PageStrip(true, 0, 0)
                              + TabletHelpContent.PageStrip(false, 0, 2))
                Assert.That(set, Does.Contain(c), $"쪽 표시의 '{c}' 가 폰트 목록에 없다.");
        }

        static System.Collections.Generic.IEnumerable<HudPrompt> AllPrompts()
        {
            yield return GameHud.PromptFor(true, false, false, PlayerInteractionKind.None);
            foreach (PlayerInteractionKind kind in System.Enum.GetValues(typeof(PlayerInteractionKind)))
            {
                var prompt = GameHud.PromptFor(false, false, true, kind);
                if (prompt.Exists) yield return prompt;
            }
        }
    }
}
