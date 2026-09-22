using Metagame.Core;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class ProximityVoiceTests
    {
        [Test] public void HearingIncludesBoundaryAndHeight()
        {
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.right * 20), Is.True);
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.right * 20.01f), Is.False);
            Assert.That(VoiceRules.Audible(Vector3.zero, Vector3.up * 21), Is.False);
            Assert.That(VoiceRules.Audible(Vector3.zero, new Vector3(float.NaN, 0, 0)), Is.False);
        }
        [Test] public void CodecRoundTripsAndClamps()
        {
            foreach (float value in new[] { -1f, -.5f, -.01f, 0, .01f, .5f, 1f })
                Assert.That(VoiceRules.Decode(VoiceRules.Encode(value)), Is.EqualTo(value).Within(.025f));
            Assert.That(VoiceRules.Encode(2), Is.EqualTo(VoiceRules.Encode(1)));
        }
        [Test] public void SilenceDoesNotTransmitAndSpeechDoes()
        {
            var filter = new VoiceFilter(); var samples = new float[VoiceRules.Frame];
            Assert.That(filter.Process(samples, 1), Is.False);
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * .2f) * .1f;
            Assert.That(filter.Process(samples, 1), Is.True);
            for (int n = 0; n < 10; n++) { System.Array.Clear(samples, 0, samples.Length); filter.Process(samples, 1); }
            Assert.That(filter.Process(samples, 1), Is.False);
        }
        [Test] public void MuteSurvivesSettingsClone()
        {
            var settings = new GameSettingsData(); settings.voice.microphoneMuted = true;
            Assert.That(settings.Clone().voice.microphoneMuted, Is.True);
        }
    }
}
