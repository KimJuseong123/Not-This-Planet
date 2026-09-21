using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 필요한 순간의 한 줄 안내를 지킨다 (S15P21B202-665).
    ///
    /// <para>S15P21B202-657 이 태블릿에서 정보를 꺼낼 수 있게 했지만 <b>꺼낼 생각을
    /// 해야</b> 닿았다. 이 안내는 찾아가지 않아도 오게 한다 — 그래서 <b>제때 오는가</b>
    /// 와 <b>거슬리지 않는가</b> 둘 다 지켜야 한다.</para>
    /// </summary>
    public sealed class OnboardingNoticeTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        static OnboardingNotices Fresh()
        {
            var notices = new OnboardingNotices();
            notices.ResetRun();
            return notices;
        }

        // --- 제때 온다 -----------------------------------------------------------------

        [Test]
        public void TheFirstNoticeTellsYouToPressTheKey()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);

            Assert.That(notices.Text, Is.Not.Null.And.Contains(Controls.Tablet.ToString()),
                "첫 안내에 눌러야 할 키가 없다.");
            Assert.That(notices.Text, Does.Contain("여십시오"),
                "누르라는 **동사**가 없다 — 'TAB' 글자만 띄웠을 때 아무도 안 눌렀다.");
        }

        [Test]
        public void TheFirstNoticeWaitsUntilYouActuallyOpenTheTablet()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);

            // 시간이 아무리 지나도 안 사라진다. **한 번 눌러 본 키는 기억한다.**
            for (var i = 0; i < 100; i++) notices.Tick(1f);
            Assert.That(notices.Text, Is.Not.Null, "첫 안내가 누르기도 전에 사라졌다.");
            Assert.That(notices.HoldingForTablet, Is.True);

            notices.OnTabletOpened();
            Assert.That(notices.Text, Is.Null, "태블릿을 열었는데도 남아 있다.");
        }

        [Test]
        public void LaterNoticesFadeOnTheirOwn()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);
            notices.OnTabletOpened();

            notices.Raise(OnboardingCue.PartFound);
            Assert.That(notices.Text, Is.Not.Null);

            notices.Tick(OnboardingNotices.VisibleSeconds - 0.1f);
            Assert.That(notices.Text, Is.Not.Null, "너무 빨리 사라진다.");
            notices.Tick(0.2f);
            Assert.That(notices.Text, Is.Null, "사라지지 않는다 — 화면에 눌러앉는다.");
        }

        [Test]
        public void EveryLaterNoticeSaysHowToSeeItAgain()
        {
            foreach (var cue in new[] { OnboardingCue.PartFound, OnboardingCue.PartStacked })
                Assert.That(OnboardingNotices.TextFor(cue), Does.Contain("다시 보기"),
                    $"{cue} 에 '다시 보기' 꼬리표가 없다 — 같은 글자가 반복돼야 학습된다.");
        }

        [Test]
        public void EveryNoticeFitsOneLine()
        {
            foreach (OnboardingCue cue in System.Enum.GetValues(typeof(OnboardingCue)))
            {
                var text = OnboardingNotices.TextFor(cue);
                Assert.That(text, Does.Not.Contain("\n"), $"{cue} 가 두 줄이다.");
                Assert.That(text.Length, Is.LessThan(60),
                    $"{cue} 가 {text.Length}자다 — 운석이 떨어지는 중에는 못 읽는다.");
            }
        }

        // --- 거슬리지 않는다 -----------------------------------------------------------

        [Test]
        public void TheSameNoticeNeverComesTwiceInARun()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);
            notices.OnTabletOpened();
            Assert.That(notices.Text, Is.Null);

            // 두 번째부터 거슬리는 순간 아무도 안 읽는다.
            notices.Raise(OnboardingCue.Start);
            Assert.That(notices.Text, Is.Null, "같은 안내가 또 떴다.");
        }

        [Test]
        public void ANoticeDoesNotOverwriteOneYouAreStillReading()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);
            notices.OnTabletOpened();

            notices.Raise(OnboardingCue.PartFound);
            var first = notices.Text;
            notices.Raise(OnboardingCue.PartStacked);
            Assert.That(notices.Text, Is.EqualTo(first), "읽고 있는 줄을 덮어썼다.");

            // 줄을 세웠으니 앞의 것이 끝나면 나온다 — 버리지 않는다.
            notices.Tick(OnboardingNotices.VisibleSeconds + 0.1f);
            Assert.That(notices.Text, Is.EqualTo(OnboardingNotices.TextFor(OnboardingCue.PartStacked)),
                "줄 세운 안내가 사라졌다.");
        }

        [Test]
        public void StartingANewRunForgetsWhatWasShown()
        {
            var notices = Fresh();
            notices.Raise(OnboardingCue.Start);
            notices.OnTabletOpened();
            notices.ResetRun();

            notices.Raise(OnboardingCue.Start);
            Assert.That(notices.Text, Is.Not.Null, "새 판인데 안내가 안 뜬다.");
        }

        // --- 태블릿의 해당 쪽이 펴진다 --------------------------------------------------

        [Test]
        public void EachCuePointsAtItsOwnGuidePage()
        {
            Assert.That(OnboardingNotices.GuidePageFor(OnboardingCue.Start), Is.Zero, "조립");
            Assert.That(OnboardingNotices.GuidePageFor(OnboardingCue.PartFound), Is.EqualTo(1), "고정못");
            Assert.That(OnboardingNotices.GuidePageFor(OnboardingCue.PartStacked), Is.EqualTo(3), "용접");

            foreach (OnboardingCue cue in System.Enum.GetValues(typeof(OnboardingCue)))
                Assert.That(OnboardingNotices.GuidePageFor(cue),
                    Is.InRange(0, TabletGuideState.PageCount - 1), $"{cue} 가 없는 쪽을 가리킨다.");
        }

        Stage1Exploration NewExploration()
        {
            var go = new GameObject("exploration");
            _spawned.Add(go);
            var state = go.AddComponent<Stage1Exploration>();
            state.Guide.Enable();
            return state;
        }

        [Test]
        public void OpeningTheTabletShowsThePageForTheMoment()
        {
            var state = NewExploration();
            state.Notices.Raise(OnboardingCue.Start);
            state.Notices.OnTabletOpened();
            state.Notices.Raise(OnboardingCue.PartStacked);

            state.SetTabletOpen(true);
            Assert.That(state.Guide.Visible, Is.True, "그 순간의 안내가 안 펴졌다.");
            Assert.That(state.Guide.Page, Is.EqualTo(3), "용접 쪽이 아니다.");
        }

        [Test]
        public void TheTabletNeverOpensByItself()
        {
            var state = NewExploration();
            state.Notices.Raise(OnboardingCue.Start);
            state.Notices.Raise(OnboardingCue.PartFound);

            // 1인칭 화면을 가로채면 운석에 맞는다. 여는 것은 플레이어다.
            for (var i = 0; i < 50; i++) state.TickOnboarding(0.1f);
            Assert.That(state.TabletOpen, Is.False, "태블릿이 저절로 열렸다.");
        }

        [Test]
        public void TheNoticeDoesNotRunDownWhileTheTabletIsOpen()
        {
            var state = NewExploration();
            state.TickOnboarding(0.1f);                 // 시작 안내
            state.SetTabletOpen(true);                  // 첫 안내는 여기서 놓인다
            state.Notices.Raise(OnboardingCue.PartFound);

            // 태블릿을 열어 둔 사이에 사라지면 그 안내는 아무도 못 본 것이 된다.
            for (var i = 0; i < 100; i++) state.TickOnboarding(1f);
            Assert.That(state.Notices.Text, Is.Not.Null, "태블릿을 보는 동안 안내가 흘러갔다.");
        }

        [Test]
        public void FindingAPartTellsYouWhatToDoNext()
        {
            var state = NewExploration();
            state.TickOnboarding(0.1f);
            state.SetTabletOpen(true);
            state.SetTabletOpen(false);

            state.Notices.Raise(OnboardingCue.PartFound);
            Assert.That(state.Notices.Text, Does.Contain("고정못"), "찾은 다음에 할 일을 안 알려 준다.");
        }
    }
}
