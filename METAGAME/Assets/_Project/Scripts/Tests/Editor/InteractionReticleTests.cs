using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class InteractionReticleTests
    {
        [Test]
        public void EmptySpace_ShowsDot_EvenWhileMouseIsPressed()
        {
            Assert.AreEqual(GameHud.InteractionReticleState.Dot,
                GameHud.ResolveInteractionReticleState(false, false));
            Assert.AreEqual(GameHud.InteractionReticleState.Dot,
                GameHud.ResolveInteractionReticleState(false, true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FocusedButUnavailableClamp_AlwaysShowsDot(bool interactionPressed)
        {
            // 고정못이 보이더라도 거리 부족·다른 플레이어 선점·인원 제한으로
            // 못 뽑는 상태는 CanInteract=false로 전달된다.
            Assert.AreEqual(GameHud.InteractionReticleState.Dot,
                GameHud.ResolveInteractionReticleState(
                    canInteract: false,
                    interactionPressed: interactionPressed));
        }

        [Test]
        public void Interactable_ChangesFromOpenToGrippingRabbitHand()
        {
            Assert.AreEqual(GameHud.InteractionReticleState.OpenRabbitHand,
                GameHud.ResolveInteractionReticleState(true, false));
            Assert.AreEqual(GameHud.InteractionReticleState.GrippingRabbitHand,
                GameHud.ResolveInteractionReticleState(true, true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EquippedTool_AlwaysShowsDot(bool interactionPressed)
        {
            Assert.AreEqual(GameHud.InteractionReticleState.Dot,
                GameHud.ResolveInteractionReticleState(
                    canInteract: true,
                    interactionPressed: interactionPressed,
                    toolEquipped: true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PullingClamp_AlwaysShowsDot(bool interactionPressed)
        {
            Assert.AreEqual(GameHud.InteractionReticleState.Dot,
                GameHud.ResolveInteractionReticleState(
                    canInteract: true,
                    interactionPressed: interactionPressed,
                    pullingClamp: true));
        }

        [Test]
        public void ReleasedClamp_ChangesBackToOpenHandWhenTargetCanBeGrabbed()
        {
            Assert.AreEqual(GameHud.InteractionReticleState.OpenRabbitHand,
                GameHud.ResolveInteractionReticleState(
                    canInteract: true,
                    interactionPressed: false,
                    pullingClamp: false));
        }

        [Test]
        public void RabbitHandTexture_IsAvailableFromResources()
        {
            Assert.NotNull(Resources.Load<Texture2D>("UI/Crosshair/T_RabbitHandCrosshairSheet"));
        }
    }
}
