using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 자리(색) 배정 규칙을 고정한다 (S15P21B202-375).
    ///
    /// <b>왜 이 규칙이 필요한가.</b> 예전에는 우주복 색을 <c>userId</c> 에서 뽑았다.
    /// 그 값은 DB 자동 증가라 같은 사람이 판마다 다른 색이었고, 채도 0.35 · 명도 0.95 라
    /// 네 명이 다 비슷한 흰색으로 보였다.
    /// </summary>
    public sealed class TeamSlotsTests
    {
        [SetUp]
        public void SetUp() => TeamSlots.Clear();

        [TearDown]
        public void TearDown() => TeamSlots.Clear();

        [Test]
        public void FourPlayersGetFourDifferentSeats()
        {
            var seats = new[]
            {
                TeamSlots.Claim(11L), TeamSlots.Claim(22L),
                TeamSlots.Claim(33L), TeamSlots.Claim(44L),
            };

            Assert.That(seats, Is.Unique, "네 명이 서로 다른 자리를 받아야 한다");
            Assert.That(seats, Is.All.InRange(0, TeamSlots.Count - 1));
        }

        /// <summary>끊겼다 돌아와도 색이 바뀌지 않아야 한다 (S15P21B202-150 과 짝).</summary>
        [Test]
        public void TheSamePersonKeepsTheirSeat()
        {
            int first = TeamSlots.Claim(77L);
            TeamSlots.Claim(88L);

            Assert.That(TeamSlots.Claim(77L), Is.EqualTo(first));
        }

        /// <summary>자리를 비운 사람의 번호는 다음 사람이 쓴다.</summary>
        [Test]
        public void ANewMatchStartsFromTheFirstSeat()
        {
            TeamSlots.Claim(1L);
            TeamSlots.Claim(2L);

            TeamSlots.Clear();

            Assert.That(TeamSlots.Claim(9L), Is.EqualTo(0),
                "새 판이면 첫 자리부터 다시 나가야 한다");
        }

        /// <summary>
        /// 색이 흰색처럼 보이지 않아야 한다 — 이 이슈의 출발점이다.
        /// 예전 값은 채도 0.35 였다.
        /// </summary>
        [Test]
        public void SuitColorsAreSaturatedEnoughToTellApart()
        {
            for (int slot = 0; slot < TeamSlots.Count; slot++)
            {
                Color.RGBToHSV(TeamSlots.Suit(slot), out _, out float s, out _);
                Assert.That(s, Is.GreaterThan(0.5f),
                    $"{slot}번({TeamSlots.Name(slot)}) 채도가 낮아 흰색처럼 보인다");
            }
        }

        /// <summary>네 색이 색상환에서 충분히 떨어져 있어야 한다.</summary>
        [Test]
        public void SuitColorsAreSpreadAroundTheWheel()
        {
            var hues = new float[TeamSlots.Count];
            for (int slot = 0; slot < TeamSlots.Count; slot++)
                Color.RGBToHSV(TeamSlots.Suit(slot), out hues[slot], out _, out _);

            for (int a = 0; a < hues.Length; a++)
            {
                for (int b = a + 1; b < hues.Length; b++)
                {
                    float gap = Mathf.Abs(hues[a] - hues[b]);
                    gap = Mathf.Min(gap, 1f - gap);          // 색상환은 둥글다
                    Assert.That(gap, Is.GreaterThan(0.08f),
                        $"{TeamSlots.Name(a)}·{TeamSlots.Name(b)} 색이 너무 가깝다");
                }
            }
        }

        /// <summary>글씨용 색은 어두운 화면에서도 읽히도록 밝아야 한다.</summary>
        [Test]
        public void AccentColorsStayBrightForText()
        {
            for (int slot = 0; slot < TeamSlots.Count; slot++)
            {
                Color.RGBToHSV(TeamSlots.Accent(slot), out _, out _, out float v);
                Assert.That(v, Is.GreaterThanOrEqualTo(0.95f));
            }
        }

        /// <summary>범위를 넘는 자리 번호에도 색을 준다 — 그리는 쪽이 죽지 않아야 한다.</summary>
        [Test]
        public void OutOfRangeSeatsWrapInsteadOfThrowing()
        {
            Assert.That(TeamSlots.Suit(TeamSlots.Count), Is.EqualTo(TeamSlots.Suit(0)));
            Assert.That(TeamSlots.Name(-1), Is.EqualTo(TeamSlots.Name(TeamSlots.Count - 1)));
        }
    }
}
