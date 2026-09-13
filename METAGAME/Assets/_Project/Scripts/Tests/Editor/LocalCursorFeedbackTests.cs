using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class LocalCursorFeedbackTests
    {
        [Test]
        public void PingCursor_UsesReadableAlphaAndPlayerTint_WithoutChangingSource()
        {
            var source = Resources.Load<Texture2D>("PingMarker");
            Assert.That(source, Is.Not.Null);
            Assert.That(source.isReadable, Is.True);
            var before = source.GetPixels32();
            var texture = LocalCursorFeedback.CreateCursorTexture(source, Color.red, false);
            try
            {
                Assert.That(texture.isReadable, Is.True);
                Assert.That(texture.width, Is.EqualTo(40));
                int red = 0, clear = 0;
                foreach (Color pixel in texture.GetPixels())
                {
                    if (pixel.a < 0.01f) clear++;
                    if (pixel.a > 0.9f && pixel.r > 0.9f && pixel.g < 0.01f) red++;
                }
                Assert.That(red, Is.GreaterThan(50));
                Assert.That(clear, Is.GreaterThan(300));
                CollectionAssert.AreEqual(before, source.GetPixels32());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void PressedCursor_IsSmallerAndBrighter()
        {
            var source = Resources.Load<Texture2D>("PingMarker");
            var normal = LocalCursorFeedback.CreateCursorTexture(source, Color.blue, false);
            var pressed = LocalCursorFeedback.CreateCursorTexture(source, Color.blue, true);
            try
            {
                int normalArea = 0, pressedArea = 0, highlights = 0;
                foreach (Color pixel in normal.GetPixels()) if (pixel.a > 0.5f) normalArea++;
                foreach (Color pixel in pressed.GetPixels())
                {
                    if (pixel.a > 0.5f) pressedArea++;
                    if (pixel.a > 0.9f && pixel.b > 0.9f && pixel.r > 0.3f) highlights++;
                }
                Assert.That(pressedArea, Is.LessThan(normalArea * 0.85f));
                Assert.That(highlights, Is.GreaterThan(25));
            }
            finally { Object.DestroyImmediate(normal); Object.DestroyImmediate(pressed); }
        }

        [Test]
        public void ClickSound_IsShortNonClippingAndFadesToSilence()
        {
            var clip = LocalCursorFeedback.CreateClickClip();
            try
            {
                Assert.That(clip.length, Is.EqualTo(0.1f).Within(0.001f));
                Assert.That(clip.channels, Is.EqualTo(1));
                var samples = new float[clip.samples];
                Assert.That(clip.GetData(samples, 0), Is.True);
                float peak = 0f;
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                Assert.That(peak, Is.InRange(0.1f, 0.5f));
                Assert.That(samples[0], Is.Zero);
                Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(clip); }
        }
    }
}
