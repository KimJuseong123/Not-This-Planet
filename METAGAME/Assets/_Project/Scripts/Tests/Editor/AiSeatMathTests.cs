using System.Linq;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// AI 자리 수 셈을 값으로 고정한다 (S15P21B202-713).
    ///
    /// <para>자리 모양은 글자 하나가 한 자리다 — <c>H</c> 사람 · <c>h</c> 사람이 받아 둔 빈 자리(끊겨
    /// 돌아올 사람, AI 를 띄워도 된다고 나온다) · <c>A</c> AI(떠 있거나 뜨는 중) · <c>.</c> 비어 있고
    /// 띄워도 됨 · <c>x</c> 비어 있지만 아직 띄울 때가 아님.</para>
    /// </summary>
    public sealed class AiSeatMathTests
    {
        /// <summary>
        /// 고른 수만큼만, <b>높은 번호부터</b> 채운다.
        ///
        /// <para>예전처럼 빈 자리를 전부 채우면 첫 줄이 <c>3,2,1</c> 이 되고, 낮은 번호부터 채우면
        /// <c>1</c> 이 된다 — 늦게 붙는 사람(가장 낮은 빈 자리를 받는다)과 부딪쳐 AI 가 밀려났다가
        /// 다른 색으로 다시 뜬다.</para>
        /// </summary>
        [TestCase("H...", 1, "3")]
        [TestCase("H...", 3, "3,2,1")]
        [TestCase("H..A", 2, "2")]         // 뜨는 중인 AI 도 센다 — 안 세면 2,1 이 되어 하나 더 뜬다
        [TestCase("HH..", 3, "3,2")]       // 사람 둘이면 AI 는 둘까지
        [TestCase("H...", 0, "")]
        [TestCase("HHHH", 3, "")]
        [TestCase("H..h", 3, "2,1")]       // 사람이 받아 둔 자리는 띄워도 된다고 나와도 돌려주지 않는다
        [TestCase("H.x.", 3, "3,1")]       // 아직 때가 아닌 자리는 건너뛴다
        [TestCase("HAAA", 3, "")]          // 이미 다 찼다
        [TestCase("HAA.", 2, "")]
        public void FillsOnlyTheRequestedSeatsFromTheTop(string seats, int requested, string expected)
        {
            Assert.That(Fill(seats, requested), Is.EqualTo(Slots(expected)));
        }

        /// <summary>사람이 잡은 자리는 어떤 수를 요청해도 돌려주지 않는다.</summary>
        [Test]
        public void NeverReturnsAHumanSeat()
        {
            foreach (string seats in new[] { "Hh..", "hhhh", "H.h.", "hH.." })
                for (int requested = 0; requested <= 5; requested++)
                    foreach (int slot in Fill(seats, requested))
                        Assert.That(char.ToUpperInvariant(seats[slot]), Is.Not.EqualTo('H'),
                            $"{seats} · {requested}명 요청에서 사람 자리 {slot} 을 돌려줬다");
        }

        /// <summary>받을 칸이 모자라면 칸만큼만 적는다 — 넘쳐 던지지 않는다.</summary>
        [Test]
        public void WritesNoMoreThanTheBufferHolds()
        {
            ParseSeats("H...", out bool[] human, out bool[] ai, out bool[] eligible);
            var into = new int[1];

            Assert.That(AiSeatMath.SlotsToFill(human, ai, eligible, 3, into), Is.EqualTo(1));
            Assert.That(into[0], Is.EqualTo(3));
        }

        /// <summary>백엔드 <c>Room.tryJoin</c> 과 같은 규칙 — 넘치는 만큼만 줄인다.</summary>
        [TestCase(3, 2, 2)]
        [TestCase(1, 4, 0)]
        [TestCase(1, 3, 1)]
        [TestCase(0, 2, 0)]
        [TestCase(2, 2, 2)]
        public void AHumanJoiningDropsOnlyWhatOverflows(int requested, int humans, int expected)
        {
            Assert.That(AiSeatMath.AfterHumanJoined(requested, humans), Is.EqualTo(expected));
        }

        /// <summary>사람은 적어도 1(방장)로 센다 — 목록이 비어 있어도 AI 는 셋까지다.</summary>
        [TestCase(0, 3)]
        [TestCase(1, 3)]
        [TestCase(2, 2)]
        [TestCase(3, 1)]
        [TestCase(4, 0)]
        [TestCase(5, 0)]
        public void MaxAiLeavesTheHostASeat(int humans, int expected)
        {
            Assert.That(AiSeatMath.MaxAi(humans), Is.EqualTo(expected));
        }

        [TestCase(9, 1, 3)]
        [TestCase(-1, 1, 0)]
        [TestCase(2, 2, 2)]
        [TestCase(3, 2, 2)]
        public void ClampsLikeTheServer(int wanted, int humans, int expected)
        {
            Assert.That(AiSeatMath.ClampRequested(wanted, humans), Is.EqualTo(expected));
        }

        [TestCase(0, 1, true)]
        [TestCase(2, 1, true)]
        [TestCase(3, 1, false)]
        [TestCase(1, 3, false)]
        [TestCase(0, 4, false)]
        public void CanAddOnlyWhileASeatIsFree(int requested, int humans, bool expected)
        {
            Assert.That(AiSeatMath.CanAdd(requested, humans), Is.EqualTo(expected));
        }

        /// <summary>대기 AI 시험은 예전처럼 빈 자리를 전부 채운다 — 개발용 동작을 바꾸지 않는다.</summary>
        [Test]
        public void IdleTestStillFillsEverySeat()
        {
            Assert.That(AiSeatMath.EffectiveRequest(0, idleTest: true), Is.EqualTo(TeamSlots.Count));
            Assert.That(AiSeatMath.EffectiveRequest(2, idleTest: false), Is.EqualTo(2));
        }

        [TestCase(3, 2, 2)]
        [TestCase(1, 1, 1)]
        [TestCase(3, 4, 0)]
        [TestCase(2, 5, 0)]
        public void SeatTargetNeverGoesNegative(int requested, int humanSeats, int expected)
        {
            Assert.That(AiSeatMath.SeatTarget(requested, humanSeats), Is.EqualTo(expected));
        }

        /// <summary>넘치는 AI 는 세기만 한다(경고용). 정상 흐름에서는 0 이다.</summary>
        [TestCase("HAAA", 1, 2)]
        [TestCase("HA..", 1, 0)]
        [TestCase("HhAA", 2, 0)]
        [TestCase("HHAA", 3, 0)]
        public void ExcessCountsOnlyAiBeyondTheTarget(string seats, int requested, int expected)
        {
            ParseSeats(seats, out bool[] human, out bool[] ai, out _);
            Assert.That(AiSeatMath.Excess(human, ai, requested), Is.EqualTo(expected));
        }

        /// <summary>
        /// 서버의 자리 관리자가 이 셈을 <b>실제로 쓴다</b>.
        ///
        /// <para>값 테스트만으로는 <c>TeamSeats.Update</c> 가 예전처럼 빈 자리를 전부 채우는 줄로
        /// 돌아가도 모른다 — 그 길은 네트워크 서버가 떠야만 돌아서 MR 에서는 Heavy 테스트가 빠진다.
        /// 그래서 배선을 글자로 못박는다. 부르는 줄은 <c>;</c> 까지 넣어 찾는다.</para>
        /// </summary>
        [Test]
        public void TeamSeatsFillsThroughTheCount()
        {
            string update = TestSource.Method(TestSource.Read("Runtime/Network/TeamSeats.cs"), "void Update()");

            Assert.That(update, Does.Contain("AiSeatMath.SlotsToFill(humanSeat, aiSeat, fillEligible, requested, fillSlots);"),
                "자리 관리자가 수를 보지 않는다 — 방장이 고른 수와 상관없이 AI 가 뜬다");
            Assert.That(update, Does.Not.Contain("if (NeedsAi(slot)) SpawnAi(slot);"),
                "빈 자리를 전부 채우던 줄이 돌아왔다");
            Assert.That(update, Does.Contain("AiHostSettings.RequestedCount"),
                "방이 정한 수를 읽지 않는다");

            // NeedsAi 는 오래 매달린 AiPending 을 치운다. 먼저, 한 번만 불러야 아래에서 이미 버린
            // AI 를 세지 않는다.
            Assert.That(TestSource.Count(update, "NeedsAi("), Is.EqualTo(1), "NeedsAi 를 한 자리에서만 부르지 않는다");
            Assert.That(update.IndexOf("NeedsAi(slot);", System.StringComparison.Ordinal),
                Is.LessThan(update.IndexOf("aiSeat[slot] =", System.StringComparison.Ordinal)),
                "AI 를 센 뒤에 NeedsAi 를 부른다 — 방금 치운 AI 를 센다");
        }

        static int[] Fill(string seats, int requested)
        {
            ParseSeats(seats, out bool[] human, out bool[] ai, out bool[] eligible);
            var into = new int[seats.Length];
            int count = AiSeatMath.SlotsToFill(human, ai, eligible, requested, into);
            return into.Take(count).ToArray();
        }

        static void ParseSeats(string seats, out bool[] human, out bool[] ai, out bool[] eligible)
        {
            human = new bool[seats.Length];
            ai = new bool[seats.Length];
            eligible = new bool[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                char c = seats[i];
                human[i] = c == 'H' || c == 'h';
                ai[i] = c == 'A';
                eligible[i] = c == '.' || c == 'h';
            }
        }

        static int[] Slots(string expected)
            => expected.Length == 0 ? new int[0] : expected.Split(',').Select(int.Parse).ToArray();
    }
}
