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

        [Test]
        public void Interactable_ChangesFromOpenToGrippingRabbitHand()
        {
            Assert.AreEqual(GameHud.InteractionReticleState.OpenRabbitHand,
                GameHud.ResolveInteractionReticleState(true, false));
            Assert.AreEqual(GameHud.InteractionReticleState.GrippingRabbitHand,
                GameHud.ResolveInteractionReticleState(true, true));
        }

        [Test]
        public void RabbitHandTexture_IsAvailableFromResources()
        {
            Assert.NotNull(Resources.Load<Texture2D>("UI/Crosshair/T_RabbitHandCrosshairSheet"));
        }
    }
}
