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
        [Test] public void NavigationParametersAreAllowlisted()
        {
            Assert.That(AiCommandPlan.Destination("assembly"), Is.True);
            Assert.That(AiCommandPlan.Destination("100,0,100"), Is.False);
            var step = Step("weld"); step.approach = true;
            Assert.That(AiCommandPlan.ValidSteps(new[] { step }), Is.False);
        }
    }
}
