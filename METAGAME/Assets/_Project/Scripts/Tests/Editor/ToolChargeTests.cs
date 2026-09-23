using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class ToolChargeTests
    {
        PlayerRig player;

        [SetUp]
        public void SetUp()
        {
            player = PlayerRig.Create("Tool charge test", Vector3.zero, Quaternion.identity,
                Color.white, Color.gray, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) Object.DestroyImmediate(player.gameObject);
        }

        [Test]
        public void ClientGaugeSurvivesEmptyHandTicksAndClearsOnServerRelease()
        {
            player.ServerAuthority = false;
            Assert.IsNull(player.ActiveTool);
            player.SetRemoteToolCharge(0.6f);

            for (int i = 0; i < 3; i++) player.Tick();

            Assert.IsTrue(player.ChargingTool);
            Assert.AreEqual(0.6f, player.ToolCharge, 0.001f);

            player.SetRemoteToolCharge(0f);
            player.Tick();
            Assert.IsFalse(player.ChargingTool);
            Assert.Zero(player.ToolCharge);
        }

        [Test]
        public void RemoteChargeCannotOverwriteAuthority()
        {
            Assert.IsTrue(player.ServerAuthority);
            player.SetRemoteToolCharge(0.8f);
            Assert.IsFalse(player.ChargingTool);
            Assert.Zero(player.ToolCharge);
        }

        [TestCase(-1f, 0f, false)]
        [TestCase(2f, 1f, true)]
        public void ClientChargeStaysWithinGaugeRange(float received, float expected, bool charging)
        {
            player.ServerAuthority = false;
            player.SetRemoteToolCharge(received);
            Assert.AreEqual(expected, player.ToolCharge);
            Assert.AreEqual(charging, player.ChargingTool);
        }
    }
}
