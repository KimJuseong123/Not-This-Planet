using System.Linq;
using System.Text;
using NUnit.Framework;
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

        [Test]
        public void EveryPromptKeyIsBracketed()
        {
            foreach (var prompt in AllPrompts())
                Assert.That(prompt.Key, Does.StartWith("[").And.EndWith("]"),
                    $"'{prompt.Label}' 의 키가 [E] 꼴이 아니다 — 화면마다 다른 문법이 된다.");
        }

        [Test]
        public void TheKeyNameMatchesTheTablet()
        {
            // 손으로 적으면 키를 바꾼 사람에게 거짓말이 된다(S15P21B202-657).
            var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Grabbable);
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
                var prompt = GameHud.PromptFor(false, false, true, PlayerInteractionKind.Grabbable);
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
