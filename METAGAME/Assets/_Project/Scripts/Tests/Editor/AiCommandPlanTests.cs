using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiCommandPlanTests
    {
        static AiCommandStep Step(string action) => new AiCommandStep { action = action, destination = "default" };
        [Test] public void WorkCatalogHasExecutors()
        {
            foreach (var action in AiWorkTask.Actions)
                Assert.That(AiCommandPlan.ValidSteps(new[] { Step(action) }), Is.True, action);
        }
        [Test] public void PickupThenDefenseIsValid()
            => Assert.That(AiCommandPlan.ValidSteps(new[] { Step("pickup_bat"), Step("defend_meteors") }), Is.True);
        [Test] public void ContinuousJobCannotHideLaterSteps()
            => Assert.That(AiCommandPlan.ValidSteps(new[] { Step("follow"), Step("dance") }), Is.False);
        [Test] public void MalformedPlanNeverReachesGameplay()
        {
            Assert.That(AiCommandPlan.Valid(null), Is.False);
            Assert.That(AiCommandPlan.ValidSteps(new[] { Step("teleport") }), Is.False);
            Assert.That(AiCommandPlan.ValidSteps(new[] { Step("dance"), Step("dance"), Step("dance"), Step("dance"), Step("dance") }), Is.False);
            Assert.That(AiCommandPlan.Valid(new[] { new AiCommandAssignment { recipients = "nobody", steps = new[] { Step("wait") } } }), Is.False);
        }

        /// <summary>
        /// <c>나머지는 고정못 뽑아</c> — 지시가 하나뿐이어도 명령이다 (S15P21B202-727).
        ///
        /// <para>전에는 여기서 막았고, 그래서 <b>한 글자도 안 틀리게 받아쓴 명령이 조용히
        /// 사라졌다.</b> <see cref="AiGroupOrders.ExecutePlan"/> 은 <c>remaining</c> 을
        /// "앞선 지시가 안 쓴 동료" 로 푸는데 첫 지시에서는 그것이 곧 전원이다.</para>
        /// </summary>
        [Test] public void RemainingAloneIsACommand()
            => Assert.That(AiCommandPlan.Valid(new[] {
                new AiCommandAssignment { recipients = "remaining", steps = new[] { Step("pull_pin") } } }), Is.True);
        /// <summary>
        /// 거절마다 <b>이름이 있다</b> (S15P21B202-715). 전에는 전부 <c>bool</c> 하나였다.
        ///
        /// <para><c>cancel</c> 만은 거의 모든 것을 통과한다 — <b>멈추라는 말은 언제나
        /// 들어야</b> 하기 때문이다. 기절했든 연출 중이든 진행 중인 작업은 멈출 수 있다.</para>
        /// </summary>
        [Test] public void EveryRefusalHasAName()
        {
            Assert.That(AiCommandPlan.OrderRefusal("follow", true, true, false, false, false, false), Is.Null);
            Assert.That(AiCommandPlan.OrderRefusal("follow", false, true, false, false, false, false),
                        Is.EqualTo("AI 자리가 아니거나 몸이 없음"));
            Assert.That(AiCommandPlan.OrderRefusal("follow", true, false, false, false, false, false),
                        Is.EqualTo("방이 명령을 받는 상태가 아님"));
            Assert.That(AiCommandPlan.OrderRefusal("follow", true, true, true, false, false, false),
                        Is.EqualTo("기절해 있음"));
            Assert.That(AiCommandPlan.OrderRefusal("weld", true, true, false, true, false, false),
                        Is.EqualTo("탑승 중이라 그 명령은 못 함"));
            Assert.That(AiCommandPlan.OrderRefusal("follow", true, true, false, false, true, false),
                        Is.EqualTo("연출·엔딩 중"));
            Assert.That(AiCommandPlan.OrderRefusal("dance", true, true, false, false, false, true),
                        Is.EqualTo("춤을 아직 못 춤 (쿨다운이거나 동작이 없음)"));
        }

        /// <summary>
        /// <b>쓰러진 사람의 말도 듣되, 뜻을 잃는 것은 이름을 붙여 거절한다</b>
        /// (S15P21B202-730).
        ///
        /// <para>막지 않으면 <c>TickCore</c> 가 곧바로 멈추므로 화면에는
        /// <c>follow 3/3명</c> 이 뜨고 동료는 가만히 있는다 — 거절보다 나쁘다.
        /// <c>revive</c> 와 <c>cancel</c> 은 통해야 한다.</para>
        /// </summary>
        [TestCase("revive", null)]
        [TestCase("cancel", null)]
        [TestCase("follow", "말한 사람이 기절해 그 명령은 뜻을 잃음")]
        [TestCase("weld", "말한 사람이 기절해 그 명령은 뜻을 잃음")]
        public void ADownedSpeakerCanStillAskForHelp(string action, string expected)
            => Assert.That(AiCommandPlan.OrderRefusal(action, true, true, false, false, false, false,
                                                      speakerDowned: true), Is.EqualTo(expected));

        [Test] public void StopIsAlwaysHeard()
        {
            Assert.That(AiCommandPlan.OrderRefusal("cancel", true, false, true, true, true, true), Is.Null);
            // 자리 자체가 없으면 멈출 대상도 없다.
            Assert.That(AiCommandPlan.OrderRefusal("cancel", false, true, false, false, false, false),
                        Is.EqualTo("AI 자리가 아니거나 몸이 없음"));
        }

        [Test] public void RolesCanUseRemainingActors()
            => Assert.That(AiCommandPlan.Valid(new[] {
                new AiCommandAssignment { recipients = "two", steps = new[] { Step("pull_all_pins") } },
                new AiCommandAssignment { recipients = "remaining", steps = new[] { Step("defend_meteors") } } }), Is.True);
        /// <summary>
        /// <c>goto</c> 는 목적지를 고를 수 있는 <b>유일한 이동 명령</b>이다 (S15P21B202-717).
        ///
        /// <para><c>follow</c>·<c>gather</c> 는 말한 사람 쪽으로만 온다. 도착한 뒤 그 자리를
        /// 지키는 지속 작업이므로 <b>뒤에 다른 단계가 올 수 없다.</b></para>
        /// </summary>
        [Test] public void GoToIsAPlaceableContinuousMove()
        {
            var there = new AiCommandStep { action = "goto", destination = "assembly" };
            Assert.That(AiCommandPlan.ValidSteps(new[] { there }), Is.True);
            Assert.That(AiCommandPlan.Continuous("goto"), Is.True);
            Assert.That(AiCommandPlan.ValidSteps(new[] { there, Step("dance") }), Is.False);
            Assert.That(AiCommandPlan.ValidSteps(new[] { Step("pickup_bat"), there }), Is.True);
        }

        /// <summary>
        /// <c>revive</c> 는 <b>끝나는 일</b>이다 (S15P21B202-716).
        ///
        /// <para>일으키고 나면 할 일이 남으므로 지속 작업이 아니다 — <c>쟤 살리고 따라와</c>
        /// 가 서야 한다. 목적지 칸은 쓰지 않는다(대상이 어디 쓰러져 있는지는 게임이 안다).</para>
        /// </summary>
        [Test] public void ReviveFinishesSoItCanLeadIntoTheNextStep()
        {
            Assert.That(AiCommandPlan.ValidSteps(new[] { Step("revive") }), Is.True);
            Assert.That(AiCommandPlan.Continuous("revive"), Is.False);
            Assert.That(AiCommandPlan.ValidSteps(new[] { Step("revive"), Step("follow") }), Is.True);
        }

        /// <summary>
        /// <c>다들 조립대로 모여</c> 는 <b>조립대로</b> 가야 한다 (S15P21B202-729).
        ///
        /// <para><c>gather</c> 가 <c>destination</c> 을 안 봐서 말한 사람 앞으로 모였고,
        /// 화면에는 <c>gather 4/4명</c> 이라 <b>시킨 대로 된 것처럼 보였다.</b>
        /// <c>다들 모여</c>(목적지 생략)와 <c>여기로 모여</c> 는 그대로여야 한다.</para>
        /// </summary>
        [TestCase("gather", "assembly", true)]
        [TestCase("gather", "aim", true)]
        [TestCase("gather", "high", true)]
        [TestCase("gather", "default", false)]
        [TestCase("gather", "speaker", false)]
        [TestCase("follow", "assembly", false)]
        [TestCase("goto", "assembly", false)]
        public void GatheringSomewhereElseIsAMove(string action, string destination, bool expected)
            => Assert.That(AiCommandPlan.GathersAwayFromSpeaker(action, destination), Is.EqualTo(expected));

        /// <summary>
        /// <b>무엇을 누구에게 시켰는지 사람 말로</b> (S15P21B202-736).
        ///
        /// <para>명령 뒤 화면에 돌아가던 것은 <c>AI 동료 1명이 명령을 받았어요</c> 뿐이었다.
        /// 그래서 헛말이 춤으로 실행돼도 플레이어는 동료가 왜 춤추는지 알 수 없었다.</para>
        /// </summary>
        [Test] public void TheSpokenResultNamesWhatWasOrdered()
        {
            Assert.That(AiCommandPlan.Spoken(new[] {
                new AiCommandAssignment { recipients = "green", steps = new[] { Step("pull_pin") } } }),
                Is.EqualTo("초록에게 고정못 뽑기"));

            Assert.That(AiCommandPlan.Spoken(new[] {
                new AiCommandAssignment { recipients = "all", steps = new[] { Step("pickup_bat"), Step("defend_meteors") } } }),
                Is.EqualTo("모두에게 방망이 집기 다음 운석 막기"));

            Assert.That(AiCommandPlan.Spoken(null), Is.Null, "빈 계획인데 할 말을 지어낸다");
            Assert.That(AiCommandPlan.Spoken(new AiCommandAssignment[0]), Is.Null);
        }

        /// <summary>
        /// <b>모르는 행동은 그대로 돌려준다.</b> 빈 문자열로 만들면 새 행동이 생겼을 때
        /// 화면에서 조용히 사라진다 — 영어라도 보이는 편이 낫다.
        /// </summary>
        [Test] public void EveryActionHasAHumanName()
        {
            foreach (var action in AiWorkTask.Actions)
                Assert.That(AiCommandPlan.ActionName(action), Is.Not.EqualTo(action),
                    $"{action} 의 사람 말 이름이 없다 — 화면에 영어가 나온다");
            foreach (var action in new[] { "follow", "wait", "gather", "cancel", "pull_pin" })
                Assert.That(AiCommandPlan.ActionName(action), Is.Not.EqualTo(action), action);
            Assert.That(AiCommandPlan.ActionName("newly_added"), Is.EqualTo("newly_added"),
                "모르는 행동을 빈 문자열로 만들면 화면에서 조용히 사라진다");
        }

        static AiCommandAssignment To(string recipients, string action)
            => new AiCommandAssignment { recipients = recipients, steps = new[] { Step(action) } };

        /// <summary>
        /// <b>버튼이 말을 덮는다</b> (S15P21B202-738).
        ///
        /// <para>2026-09-23 실측에서 받아쓰기가 <b>맞았는데도</b> LLM 이 대상을 틀렸다 —
        /// <c>초록아 방망이 잡아</c> → <c>one</c>. 버튼으로 골랐으면 말 속의 대상은 무엇이든
        /// 버튼의 색이 된다.</para>
        /// </summary>
        [Test] public void TheButtonOverridesWhoTheSpeechNamed()
        {
            var misheard = AiCommandPlan.ForTarget(new[] { To("one", "pickup_bat") }, 3);
            Assert.That(misheard, Has.Length.EqualTo(1));
            Assert.That(misheard[0].recipients, Is.EqualTo("green"), "버튼으로 초록을 골랐는데 말 속 대상을 따른다");
            Assert.That(misheard[0].steps[0].action, Is.EqualTo("pickup_bat"), "행동까지 바꿨다");
            Assert.That(AiCommandPlan.Valid(misheard), Is.True, "덮은 계획이 실행할 수 없는 꼴이다");

            // 다른 색을 들었어도 버튼의 색이다 — 색 인식이 실패의 주범이다.
            Assert.That(AiCommandPlan.ForTarget(new[] { To("yellow", "follow") }, 1)[0].recipients, Is.EqualTo("blue"));

            // 화면에 돌아가는 한 줄이 "가까운 동료에게" 가 아니라 그 색이어야 한다.
            Assert.That(AiCommandPlan.Spoken(misheard), Is.EqualTo("초록에게 방망이 집기"));
        }

        /// <summary>
        /// 여러 지시가 오면 <b>버튼의 색에게 한 지시</b>를 고른다. 없으면 첫 지시다
        /// (S15P21B202-738). 버튼의 동료 한 명만 듣고 있으므로 나머지 지시는 받을 사람이 없다.
        /// </summary>
        [Test] public void AMultiPartPlanKeepsThePartForTheButton()
        {
            var plan = new[] { To("red", "pull_pin"), To("blue", "weld") };
            var forBlue = AiCommandPlan.ForTarget(plan, 1);
            Assert.That(forBlue, Has.Length.EqualTo(1));
            Assert.That(forBlue[0].steps[0].action, Is.EqualTo("weld"), "파랑에게 한 지시가 아니라 다른 것을 골랐다");

            var forGreen = AiCommandPlan.ForTarget(plan, 3);
            Assert.That(forGreen[0].steps[0].action, Is.EqualTo("pull_pin"), "맞는 색이 없을 때 첫 지시가 아니다");
            Assert.That(forGreen[0].recipients, Is.EqualTo("green"));

            Assert.That(plan[0].recipients, Is.EqualTo("red"), "원래 계획을 고쳐 썼다 — 로그가 들은 것과 달라진다");
        }

        [Test] public void AnUnusablePlanOrSeatGivesNoPlan()
        {
            Assert.That(AiCommandPlan.ForTarget(null, 0), Is.Null);
            Assert.That(AiCommandPlan.ForTarget(new[] { To("red", "teleport") }, 0), Is.Null, "실행할 수 없는 계획을 살렸다");
            Assert.That(AiCommandPlan.ForTarget(new[] { To("red", "follow") }, -1), Is.Null);
            Assert.That(AiCommandPlan.ForTarget(new[] { To("red", "follow") }, TeamSlots.Count), Is.Null);
        }

        /// <summary>
        /// 자리 ↔ 색 표가 <b>한 벌</b>이다 (S15P21B202-738).
        ///
        /// <para>색을 자리로 푸는 쪽(<see cref="AiGroupOrders.Select"/>)과 자리를 색으로 되돌리는
        /// 쪽(<see cref="AiCommandPlan.ForTarget"/>), 화면 이름(<see cref="TeamSlots.Name"/>)이
        /// 갈리면 <b>버튼으로 고른 동료와 다른 동료가 움직인다.</b></para>
        /// </summary>
        [Test] public void SeatsAndColorsRoundTrip()
        {
            for (int slot = 0; slot < TeamSlots.Count; slot++)
            {
                string recipient = AiCommandPlan.RecipientOf(slot);
                Assert.That(AiCommandPlan.SlotOf(recipient), Is.EqualTo(slot), recipient);
                Assert.That(AiCommandPlan.Recipients(recipient), Is.True, recipient + " 가 계획의 대상으로 안 통한다");
                Assert.That(AiCommandPlan.RecipientName(recipient), Is.EqualTo(TeamSlots.Name(slot)),
                    "화면의 색 이름과 계획의 색이 갈렸다");
            }
            Assert.That(AiCommandPlan.SlotOf("all"), Is.EqualTo(-1));
            Assert.That(AiCommandPlan.SlotOf(null), Is.EqualTo(-1));
        }

        [Test] public void NavigationParametersAreAllowlisted()
        {
            Assert.That(AiCommandPlan.Destination("assembly"), Is.True);
            Assert.That(AiCommandPlan.Destination("100,0,100"), Is.False);
            var step = Step("weld"); step.approach = true;
            Assert.That(AiCommandPlan.ValidSteps(new[] { step }), Is.False);
        }
    }
}
