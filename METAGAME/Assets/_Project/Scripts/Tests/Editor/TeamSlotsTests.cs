using Metagame.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

        // --- 이어받을 때 색 물려받기 (S15P21B202-396) -----------------------

        /// <summary>
        /// 쓰던 자리를 물려받으면 <see cref="TeamSlots.Claim"/> 이 그 자리를 돌려준다.
        ///
        /// <para>색은 방장이 배정해 SyncVar 로 내려가고 참가자는 받기만 한다 — 자기 기계에는
        /// 기록이 없다. 그래서 이어받은 새 방장이 그냥 다시 배정하면 접속 순서대로 색이
        /// 정해져 사람마다 색이 바뀐다.</para>
        /// </summary>
        [Test]
        public void RememberedSeatsSurviveTheTakeover()
        {
            // 옛 방장이 배정해 둔 그림: 11번이 2번 자리, 22번이 0번 자리.
            TeamSlots.Remember(11L, 2);
            TeamSlots.Remember(22L, 0);

            Assert.That(TeamSlots.Claim(11L), Is.EqualTo(2), "물려받은 자리가 아니다");
            Assert.That(TeamSlots.Claim(22L), Is.EqualTo(0));
        }

        /// <summary>물려받지 않은 사람은 남은 자리를 받는다 — 겹치지 않는다.</summary>
        [Test]
        public void SomeoneWithoutARememberedSeatTakesAFreeOne()
        {
            TeamSlots.Remember(11L, 0);
            TeamSlots.Remember(22L, 1);

            int fresh = TeamSlots.Claim(33L);

            Assert.That(fresh, Is.Not.EqualTo(0));
            Assert.That(fresh, Is.Not.EqualTo(1));
            Assert.That(fresh, Is.InRange(0, TeamSlots.Count - 1));
        }

        /// <summary>
        /// 남이 쓰는 자리는 덮지 않는다 — 두 사람이 같은 색이면 구분이 안 된다.
        /// </summary>
        [Test]
        public void RememberDoesNotStealAnotherPersonsSeat()
        {
            TeamSlots.Remember(11L, 3);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("TeamSlots"));
            TeamSlots.Remember(22L, 3);

            Assert.That(TeamSlots.Claim(11L), Is.EqualTo(3), "먼저 쓰던 사람의 자리가 바뀌었다");
            Assert.That(TeamSlots.Claim(22L), Is.Not.EqualTo(3), "같은 자리를 둘이 가졌다");
        }

        /// <summary>같은 사람에게 같은 자리를 두 번 적어도 문제가 없다.</summary>
        [Test]
        public void RememberingTheSameSeatTwiceIsHarmless()
        {
            TeamSlots.Remember(11L, 1);
            TeamSlots.Remember(11L, 1);

            Assert.That(TeamSlots.Claim(11L), Is.EqualTo(1));
        }

        /// <summary>범위를 벗어난 자리는 감싼다 — 던지지 않는다.</summary>
        [Test]
        public void RememberWrapsOutOfRangeSeats()
        {
            Assert.DoesNotThrow(() => TeamSlots.Remember(11L, TeamSlots.Count + 1));
            Assert.That(TeamSlots.Claim(11L), Is.InRange(0, TeamSlots.Count - 1));
        }

        /// <summary>새 판이면 물려받은 것도 버린다 — 이어받기가 아니다.</summary>
        [Test]
        public void ANewMatchDropsRememberedSeats()
        {
            TeamSlots.Remember(11L, 3);
            TeamSlots.Clear();

            Assert.That(TeamSlots.Claim(11L), Is.EqualTo(0), "새 판은 첫 자리부터다");
        }

        // --- 사람이 받아 둔 자리 (S15P21B202-713) ---------------------------

        /// <summary>
        /// 받아 둔 자리는 AI 가 앉지 않는 자리다. 배정(Claim)과 물려받기(Remember) 둘 다 센다 —
        /// 끊겨 돌아올 사람과 이어받기에서 물려받은 사람이 여기 남는다.
        /// </summary>
        [Test]
        public void ClaimedAndRememberedSeatsCountAsTaken()
        {
            Assert.That(TeamSlots.Claim(11L), Is.EqualTo(0));
            TeamSlots.Remember(22L, 3);

            Assert.That(TeamSlots.IsAssigned(0), Is.True, "배정한 자리를 빈 자리로 본다");
            Assert.That(TeamSlots.IsAssigned(3), Is.True, "물려받은 자리를 빈 자리로 본다");
            Assert.That(TeamSlots.IsAssigned(1), Is.False);
            Assert.That(TeamSlots.IsAssigned(2), Is.False);
        }

        [Test]
        public void ANewMatchFreesEverySeat()
        {
            TeamSlots.Claim(11L);
            TeamSlots.Remember(22L, 3);

            TeamSlots.Clear();

            for (int slot = 0; slot < TeamSlots.Count; slot++)
                Assert.That(TeamSlots.IsAssigned(slot), Is.False, $"{slot}번 자리가 새 판에도 남았다");
        }

        /// <summary>
        /// "그 자리가 찼나" 는 감싸지 않는다 — 색과 달리 5번을 1번으로 읽으면 엉뚱한 자리를 막는다.
        /// </summary>
        [Test]
        public void OutOfRangeSeatsAreNeverTaken()
        {
            TeamSlots.Claim(11L);   // 0번

            Assert.That(TeamSlots.IsAssigned(TeamSlots.Count), Is.False);
            Assert.That(TeamSlots.IsAssigned(-TeamSlots.Count), Is.False);
            Assert.That(TeamSlots.IsAssigned(-1), Is.False);
        }

        /// <summary>
        /// 자리 수와 방 정원은 같은 수다 (S15P21B202-713). 사람 + AI 가 정원을 넘지 않는다는 규칙이
        /// 자리 네 개에 그대로 맞아야 한다 — 한쪽만 바뀌면 AI 가 앉을 자리가 없거나 남는다.
        /// </summary>
        [Test]
        public void SeatCountMatchesTheRoomCapacity()
        {
            Assert.That(TeamSlots.Count, Is.EqualTo(NetworkSession.MaxRoomPlayers));
        }
    }
}
