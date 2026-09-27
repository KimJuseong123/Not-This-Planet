using Metagame.UI;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    public sealed class FpsCounterTests
    {
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void MeasuresFramesPerElapsedSecond(int fps)
        {
            var counter = new FpsCounter();
            int updates = 0;
            for (int i = 0; i < fps; i++)
                if (counter.AddFrame(1f / fps)) updates++;
            Assert.That(counter.FramesPerSecond, Is.EqualTo(fps));
            Assert.That(updates, Is.EqualTo(2));
        }

        [Test]
        public void SlowFrameIsIncludedInElapsedAverage()
        {
            var counter = new FpsCounter();
            counter.AddFrame(0.125f);
            counter.AddFrame(0.125f);
            Assert.That(counter.AddFrame(0.75f), Is.True);
            Assert.That(counter.FramesPerSecond, Is.EqualTo(3));
        }

        [Test]
        public void ResetDiscardsOldFramesBeforeReenable()
        {
            var counter = new FpsCounter();
            counter.AddFrame(0.4f);
            counter.Reset();
            Assert.That(counter.HasSample, Is.False);
            Assert.That(counter.AddFrame(0.25f), Is.False);
            Assert.That(counter.AddFrame(0.25f), Is.True);
            Assert.That(counter.FramesPerSecond, Is.EqualTo(4));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDeltaDoesNotPoisonLaterSamples(float delta)
        {
            var counter = new FpsCounter();
            Assert.That(counter.AddFrame(delta), Is.False);
            Assert.That(counter.AddFrame(0.5f), Is.True);
            Assert.That(counter.FramesPerSecond, Is.EqualTo(2));
        }
    }
}
