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
            Assert.That(AiCommandPlan.Valid(new[] { new AiCommandAssignment { recipients = "remaining", steps = new[] { Step("wait") } } }), Is.False);
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
