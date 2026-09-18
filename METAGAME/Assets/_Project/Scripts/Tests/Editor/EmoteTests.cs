using NUnit.Framework;
using Metagame.Core;
using Metagame.UI;
using UnityEngine;
using UnityEditor;

namespace Metagame.Gameplay.Tests
{
    public sealed class EmoteTests
    {
        [TestCase(0)] [TestCase(7)] [TestCase(255)]
        public void InvalidIdNeverChangesState(int id)
        {
            var state = new EmoteState();
            Assert.IsFalse(state.TryShow((byte)id, 1));
            Assert.AreEqual(0, state.Id);
        }

        [Test]
        public void CooldownAndExpiryAreServerClockBased()
        {
            var state = new EmoteState();
            Assert.IsTrue(state.TryShow(1, 10));
            Assert.IsFalse(state.TryShow(2, 10.79));
            Assert.AreEqual(1, state.Id);
            Assert.IsTrue(state.TryShow(2, 10.8));
            Assert.IsTrue(EmoteState.IsActive(state.Id, state.ExpiresAt, 14.29));
            Assert.IsFalse(EmoteState.IsActive(state.Id, state.ExpiresAt, 14.3));
        }

        [TestCase(0, 0, -1)] [TestCase(0, 100, 0)]
        [TestCase(100, 50, 1)] [TestCase(100, -50, 2)]
        [TestCase(0, -100, 3)] [TestCase(-100, -50, 4)] [TestCase(-100, 50, 5)]
        public void RadialSelectionMatchesVisibleSlots(float x, float y, int expected) =>
            Assert.AreEqual(expected, EmoteHud.Selection(new Vector2(x, y)));

        [TestCase("01-question")] [TestCase("02-exclamation")] [TestCase("03-thumbsup")]
        [TestCase("04-heart")] [TestCase("05-crying")] [TestCase("06-angry")]
        public void IconsAreAvailableToBuildWithoutSceneReferences(string name)
        {
            var texture = Resources.Load<Texture2D>("Emotes/" + name);
            Assert.IsNotNull(texture);
            Assert.AreEqual(512, texture.width);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            Assert.IsTrue(importer.DoesSourceTextureHaveAlpha());
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.IsFalse(importer.mipmapEnabled);
        }

        [Test]
        public void ClosingEmoteDoesNotReleasePauseInputSuppression()
        {
            var go = new GameObject("Emote session test");
            var session = go.AddComponent<GameSession>();
            try
            {
                session.SetEmoteWheelOpen(true);
                session.SetInputSuppressed(true);
                session.SetCursor(false);
                session.SetEmoteWheelOpen(false);
                Assert.IsTrue(session.InputSuppressed);
                Assert.IsFalse(session.CursorLocked);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
