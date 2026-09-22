using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiVoicePacketTests
    {
        [Test] public void MonoAndStereoProduceBoundedPcm()
        {
            Assert.That(AiVoicePacket.Valid(AiVoicePacket.Encode(new float[16000], 16000, 1)), Is.True);
            var converted = AiVoicePacket.Encode(new float[48000 * 2], 48000, 2);
            Assert.That(AiVoicePacket.Valid(converted), Is.True);
            Assert.That(converted.Length, Is.EqualTo(32044));
        }
        [Test] public void LongClipIsCapped()
            => Assert.That(AiVoicePacket.Encode(new float[16000 * 12], 16000, 1).Length, Is.EqualTo(AiVoicePacket.MaxBytes));
        [Test] public void ShortMalformedAndWrongRateAreRejected()
        {
            Assert.That(AiVoicePacket.Valid(null), Is.False);
            Assert.That(AiVoicePacket.Valid(new byte[44000]), Is.False);
            Assert.That(AiVoicePacket.Valid(AiVoicePacket.Encode(new float[100], 16000, 1)), Is.False);
            var bytes = AiVoicePacket.Encode(new float[16000], 16000, 1);
            bytes[24] = 0; Assert.That(AiVoicePacket.Valid(bytes), Is.False);
        }
    }
}
