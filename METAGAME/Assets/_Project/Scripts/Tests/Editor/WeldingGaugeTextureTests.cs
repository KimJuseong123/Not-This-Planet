using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public class WeldingGaugeTextureTests
    {
        [Test]
        public void FullHalfEmptyAndRefill_ChangeVisibleYellowArea()
        {
            using (var gauge = new WeldingGaugeTexture())
            {
                int full = CountYellow(gauge.Texture);
                Assert.Greater(full, 2000, "가득 찬 상태에 충분한 면적의 노란색 채움이 있어야 한다.");
                gauge.SetFraction(0.5f);
                int half = CountYellow(gauge.Texture);
                Assert.That(half / (float)full, Is.EqualTo(0.5f).Within(0.02f));
                gauge.SetFraction(0f);
                Assert.AreEqual(0, CountYellow(gauge.Texture));
                gauge.SetFraction(1f);
                Assert.AreEqual(full, CountYellow(gauge.Texture));
            }
        }

        [Test]
        public void HalfFill_StaysAtBottom_AndCenterRemainsTransparent()
        {
            using (var gauge = new WeldingGaugeTexture())
            {
                gauge.SetFraction(0.5f);
                Assert.IsTrue(IsYellow(AtAngle(gauge.Texture, -10f)));
                Assert.IsFalse(IsYellow(AtAngle(gauge.Texture, 40f)));
                Assert.AreEqual(0f, gauge.Texture.GetPixel(128, 128).a);
            }
        }

        static Color AtAngle(Texture2D texture, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return texture.GetPixel(Mathf.RoundToInt(128 + 115 * Mathf.Cos(angle)),
                Mathf.RoundToInt(128 + 115 * Mathf.Sin(angle)));
        }

        static bool IsYellow(Color color) => color.r > 0.9f && color.g > 0.7f && color.b < 0.2f;

        static int CountYellow(Texture2D texture)
        {
            int count = 0;
            foreach (var pixel in texture.GetPixels32())
                if (IsYellow(pixel)) count++;
            return count;
        }
    }
}
