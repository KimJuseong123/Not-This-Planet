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
