using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 태블릿 안내가 진행도를 가리지 않는지, 그리고 안내가 실제 키와 맞는지 지킨다
    /// (S15P21B202-657).
    ///
    /// <para>처음 하는 사람에게 시켜 보니 여섯 가지를 못 했고 <b>다섯 가지의 원인이
    /// 하나</b>였다 — 진행도와 조작 안내가 안내 4쪽 뒤에 갇혀 있었다. 확인 키를 따로
    /// 누르기 전까지 그려지지도 않았다.</para>
    /// </summary>
    public sealed class TabletOnboardingTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        Stage1Exploration NewExploration()
        {
            var go = new GameObject("exploration");
            _spawned.Add(go);
            var state = go.AddComponent<Stage1Exploration>();
            state.Guide.Enable();
            return state;
        }

        // --- A. 안내가 막다른 길이 아니다 ---------------------------------------------

        [Test]
        public void PagingPastTheLastGuidePageLeavesTheGuide()
        {
            var guide = new TabletGuideState();
            guide.Enable();
            guide.Open();

            for (var i = 0; i < TabletGuideState.PageCount - 1; i++)
                Assert.That(guide.Move(1), Is.False, $"{i} 쪽에서 벌써 안내를 벗어났다.");

            // 마지막 쪽에서 한 번 더 넘기면 **나간다**. 예전에는 여기가 막다른 길이라
            // 확인 키를 모르는 사람은 진행도를 영영 못 봤다.
            Assert.That(guide.Move(1), Is.True, "마지막 쪽에서 넘겨도 안내를 안 벗어난다.");
            Assert.That(guide.Visible, Is.False);
            Assert.That(guide.Completed, Is.True, "다음에 열 때 또 안내부터 뜬다.");
        }

        [Test]
        public void TheProgressPageIsReachableWithoutTheConfirmKey()
        {
            var state = NewExploration();
            state.SetTabletOpen(true);
            Assert.That(state.Guide.Visible, Is.True, "처음 열면 안내부터 떠야 한다.");

            // 방향키만 쓴다. 확인 키(ActivateTabletGuide)는 한 번도 안 부른다.
            for (var i = 0; i < TabletGuideState.PageCount; i++) state.NextTabletPage();

            Assert.That(state.Guide.Visible, Is.False, "안내에 갇혔다.");
            Assert.That(state.TabletPage, Is.Zero, "안내를 나오면 진행도여야 한다.");
        }

        [Test]
        public void EveryHelpPageIsReachableByPagingOnward()
        {
            var state = NewExploration();
            state.SetTabletOpen(true);
            var seen = new HashSet<int>();
            for (var i = 0; i < TabletGuideState.PageCount + Stage1Exploration.TabletPageCount + 2; i++)
            {
                if (!state.Guide.Visible) seen.Add(state.TabletPage);
                state.NextTabletPage();
            }
            for (var page = 0; page < Stage1Exploration.TabletPageCount; page++)
                Assert.That(seen, Does.Contain(page), $"{page} 쪽에 넘겨서 닿을 수 없다.");
        }

        [Test]
        public void PagingBackFromProgressReturnsToTheGuide()
        {
            var state = NewExploration();
            state.SetTabletOpen(true);
            for (var i = 0; i < TabletGuideState.PageCount; i++) state.NextTabletPage();
            Assert.That(state.Guide.Visible, Is.False);

            // 진행도에서 앞으로 더 가면 안내로 되돌아온다 — **다시 보려고 판을 새로
            // 시작할 필요가 없어야 한다.**
            state.PreviousTabletPage();
            Assert.That(state.Guide.Visible, Is.True, "진행도에서 안내로 못 돌아간다.");
            Assert.That(state.Guide.Page, Is.EqualTo(TabletGuideState.PageCount - 1));
        }

        [Test]
        public void TheSecondTimeItOpensOnProgress()
        {
            var state = NewExploration();
            state.SetTabletOpen(true);
            for (var i = 0; i < TabletGuideState.PageCount; i++) state.NextTabletPage();
            state.SetTabletOpen(false);

            state.SetTabletOpen(true);
            Assert.That(state.Guide.Visible, Is.False, "한 번 다 봤는데 안내가 또 뜬다.");
        }

        // --- B. 안내가 실제 키와 맞는다 -----------------------------------------------

        static IEnumerable<TabletHelpContent.Entry> AllEntries()
        {
            for (var page = 1; page <= 3; page++)
                foreach (var entry in TabletHelpContent.Page(page))
                    yield return entry;
        }

        /// <summary>
        /// 화면에 안내가 있어야 하는 키들. <b>이 목록이 이 검사의 값이다</b> — 기능을
        /// 넣고 안내를 안 고치면 여기서 걸린다. 616 의 엎드리기가 그렇게 빠져 있었다.
        /// </summary>
        static readonly (string Label, KeyCode Key)[] MustBeDocumented =
        {
            ("점프", Controls.Jump),
            ("달리기", Controls.Run),
            ("팔 올리기", Controls.ArmsUp),
            ("엎드리기", Controls.Prone),
            ("저중력장", Controls.FieldToggle),
            ("자유 시점", Controls.FreeLook),
            ("버리기·던지기", Controls.ToolAction),
            // **줍기가 여기 붙어 있었다** (S15P21B202-696). E 로는 아무것도 못 줍는데
            // 이 목록이 그 짝을 잠그고 있어서, 안내가 틀린 채로 세 이슈를 지나갔다.
            ("운반 장치 탑승", Controls.TransportAction),
            ("동료 구조", Controls.RescueTeammate),
            ("끼임 탈출", Controls.StuckEscape),
            ("다시 시작", Controls.Restart),
            ("태블릿", Controls.Tablet),
            ("커서 풀기", Controls.FreeCursor),
        };

        [Test]
        public void EveryKeyThePlayerNeedsIsInTheHelp()
        {
            var keys = AllEntries().Select(e => e.Keys).ToArray();
            foreach (var (label, key) in MustBeDocumented)
            {
                var name = TabletHelpContent.Name(key);
                Assert.That(keys.Any(k => k.Contains(name)), Is.True,
                    $"{label}({name}) 가 조작 안내에 없다 — 기능만 넣고 안내를 빠뜨렸다.");
            }
        }

        [Test]
        public void RescueIsTaughtWithItsOwnKeyNotTheInteractKey()
        {
            // 예전에는 "E … 장비 줍기·교환, 동료 구조" 였다. 실제 키는 G 라서
            // **태블릿과 HUD 가 서로 다른 키를 가르쳤다.**
            var interact = AllEntries().First(e => e.Keys == TabletHelpContent.Name(Controls.TransportAction));
            Assert.That(interact.Description, Does.Not.Contain("구조"),
                "탑승 줄이 아직 구조까지 떠안고 있다.");

            var rescue = AllEntries().FirstOrDefault(
                e => e.Keys == TabletHelpContent.Name(Controls.RescueTeammate));
            Assert.That(rescue.Description, Is.Not.Null.And.Contains("일으"),
                "구조가 제 키로 안 적혀 있다.");
        }

        [Test]
        public void ProneSaysWhyYouWouldUseIt()
        {
            // 키만 적으면 쓸 이유를 모른다 — 폭풍을 피할 수 있다는 것이 존재 이유다.
            var prone = AllEntries().First(e => e.Keys == TabletHelpContent.Name(Controls.Prone));
            Assert.That(prone.Description, Does.Contain("폭풍"), "엎드리는 이유가 안 적혀 있다.");
        }

        [Test]
        public void DroppingSaysDropNotJustThrow()
        {
            var drop = AllEntries().First(e => e.Keys.Contains("짧게"));
            Assert.That(drop.Description, Does.Contain("버리"),
                "'내려놓기' 로는 버리는 방법인 줄 모른다.");
        }

        [Test]
        public void KeyNamesStayReadable()
        {
            // Controls 에서 읽되 ToString 을 그대로 쓰면 "LeftShift"·"Escape" 가 되어
            // 지금보다 읽기 나빠진다.
            Assert.That(TabletHelpContent.Name(KeyCode.LeftShift), Is.EqualTo("Shift"));
            Assert.That(TabletHelpContent.Name(KeyCode.LeftControl), Is.EqualTo("Ctrl"));
            Assert.That(TabletHelpContent.Name(KeyCode.LeftAlt), Is.EqualTo("Alt"));
            Assert.That(TabletHelpContent.Name(KeyCode.Escape), Is.EqualTo("Esc"));
            Assert.That(TabletHelpContent.Name(KeyCode.G), Is.EqualTo("G"));
        }

        [Test]
        public void NoHelpPageOverflowsTheCards()
        {
            for (var page = 1; page <= 3; page++)
                Assert.That(TabletHelpContent.Page(page).Length,
                    Is.LessThanOrEqualTo(TabletHelpContent.MaxRows),
                    $"{page} 쪽이 카드 수를 넘는다 — 넘친 줄은 **조용히 안 그려진다**.");
        }

        [Test]
        public void EveryHelpGlyphIsCollectedForTheFont()
        {
            // 안내에 쓰는 글자가 폰트 요청 목록에 없으면 그 글자가 네모로 뜬다.
            var text = new System.Text.StringBuilder();
            TabletHelpContent.AppendGlyphs(text);
            var collected = new HashSet<char>(text.ToString());
            foreach (var entry in AllEntries())
                foreach (var c in entry.Keys + entry.Description)
                    Assert.That(collected, Does.Contain(c), $"'{c}' 가 폰트 글자 목록에 없다.");
        }

        // --- 쪽 표시 -------------------------------------------------------------------

        [Test]
        public void ThePageDotsShowThreeGroups()
        {
            var dots = TabletHelpContent.PageDots(true, 0);
            Assert.That(dots.Count(c => c == '●' || c == '○'),
                Is.EqualTo(TabletGuideState.PageCount + Stage1Exploration.TabletPageCount),
                "점 개수가 쪽 수와 다르다.");
            Assert.That(dots.Count(c => c == '|'), Is.EqualTo(2), "세 덩어리로 안 끊긴다.");
            Assert.That(dots.Count(c => c == '●'), Is.EqualTo(1), "지금 쪽이 하나여야 한다.");

            // 안내에 있을 때와 진행도에 있을 때 채워지는 자리가 달라야 한다.
            Assert.That(TabletHelpContent.PageDots(false, 0),
                Is.Not.EqualTo(TabletHelpContent.PageDots(true, 0)));
        }
    }
}
