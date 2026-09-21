using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 리더보드의 계산과 서식 (S15P21B202-656).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 클라이언트가 만든 값은 서버가 다시 검사하지만,
    /// 거절당할 것을 보내면 <b>사용자는 아무것도 얻지 못하고 실패 로그만 남는다.</b>
    /// 여기서 걸러 내는 것이 그 헛걸음이다. 통신에서 갈라 둔 이유도 같다 —
    /// <c>UnityWebRequest</c> 와 섞여 있으면 서버를 세워야 시험할 수 있다.</para>
    /// </summary>
    public sealed class LeaderboardTests
    {
        // --- 표 넷 ------------------------------------------------------------

        /// <summary>1인·2인·3인·4인 네 표가 있고 순서가 곧 탭 순서다.</summary>
        [Test]
        public void ThereAreFourBoardsOnePerCrewSize()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Leaderboard.Boards);
        }

        [Test]
        public void EachBoardIsNamedByItsCrewSize()
        {
            Assert.AreEqual("1인", Leaderboard.DisplayName(1));
            Assert.AreEqual("4인", Leaderboard.DisplayName(4));
        }

        /// <summary>없는 표를 물으면 요청을 만들지 않는다 — 서버가 400 으로 답한다.</summary>
        [Test]
        public void OnlyTheFourBoardsAreValid()
        {
            Assert.IsTrue(Leaderboard.IsValidCrew(1));
            Assert.IsTrue(Leaderboard.IsValidCrew(4));
            Assert.IsFalse(Leaderboard.IsValidCrew(0));
            Assert.IsFalse(Leaderboard.IsValidCrew(5));
        }

        // --- 시간 서식 ------------------------------------------------------

        [Test]
        public void TimeIsShownAsMinutesAndSeconds()
        {
            Assert.AreEqual("04:12", Leaderboard.FormatTime(252_000));
            Assert.AreEqual("00:30", Leaderboard.FormatTime(30_000));
            Assert.AreEqual("59:59", Leaderboard.FormatTime(3_599_000));
        }

        /// <summary>
        /// 한 시간을 넘으면 칸이 하나 는다. 서버가 세 시간까지 받으므로 실제로
        /// 들어올 수 있고, <c>184:12</c> 로 찍히면 읽히지 않는다.
        /// </summary>
        [Test]
        public void PastAnHourTheFormatGrowsAnotherField()
        {
            Assert.AreEqual("1:00:00", Leaderboard.FormatTime(3_600_000));
            Assert.AreEqual("3:04:12", Leaderboard.FormatTime(11_052_000));
        }

        [Test]
        public void ANegativeTimeDoesNotProduceGarbage()
        {
            Assert.AreEqual("00:00", Leaderboard.FormatTime(-1));
        }

        // --- 보낼 것을 만든다 ------------------------------------------------

        [Test]
        public void ANormalRunBecomesASubmission()
        {
            var sub = Leaderboard.BuildSubmission(252.0, new[] { "재현", "수호" }, "v1.0.0");

            Assert.IsNotNull(sub);
            Assert.AreEqual(252_000, sub.clearMillis);
            Assert.AreEqual(2, sub.crewNames.Count);
        }

        /// <summary>
        /// <b>서버가 거절할 값은 보내지 않는다.</b> 보내 봐야 400 이고 실패 로그만 남는다.
        /// </summary>
        [Test]
        public void AnImpossiblyFastRunIsNotEvenBuilt()
        {
            Assert.IsNull(Leaderboard.BuildSubmission(1.0, new[] { "재현" }, null));
            Assert.IsNull(Leaderboard.BuildSubmission(
                (Leaderboard.MinClearMillis - 1) / 1000.0, new[] { "재현" }, null));
        }

        [Test]
        public void AnAbsurdlySlowRunIsNotEvenBuilt()
        {
            Assert.IsNull(Leaderboard.BuildSubmission(
                (Leaderboard.MaxClearMillis + 1000) / 1000.0, new[] { "재현" }, null));
        }

        [Test]
        public void TheBoundariesThemselvesAreBuilt()
        {
            Assert.IsNotNull(Leaderboard.BuildSubmission(
                Leaderboard.MinClearMillis / 1000.0, new[] { "재현" }, null));
            Assert.IsNotNull(Leaderboard.BuildSubmission(
                Leaderboard.MaxClearMillis / 1000.0, new[] { "재현" }, null));
        }

        [Test]
        public void ARunWithNoUsableNameIsNotBuilt()
        {
            Assert.IsNull(Leaderboard.BuildSubmission(252.0, new[] { "  ", "\n" }, null));
            Assert.IsNull(Leaderboard.BuildSubmission(252.0, new string[0], null));
            Assert.IsNull(Leaderboard.BuildSubmission(252.0, null, null));
        }

        // --- 닉네임 다듬기 ---------------------------------------------------

        /// <summary>
        /// 서버가 닉네임을 한 문자열로 이어 붙이므로, 이름에 구분자가 있으면
        /// 읽는 쪽에서 <b>사람이 하나 더 있는 것처럼</b> 보인다.
        /// </summary>
        [Test]
        public void ANameCannotSmuggleTheSeparator()
        {
            var names = Leaderboard.CleanNames(new[] { "재현 · 가짜" });

            Assert.AreEqual(1, names.Count);
            Assert.IsFalse(names[0].Contains(Leaderboard.NameSeparator.Trim()));
        }

        [Test]
        public void ControlCharactersAreStripped()
        {
            Assert.AreEqual("재현", Leaderboard.CleanNames(new[] { "재\n현\t" })[0]);
        }

        /// <summary>팀원 하나의 이름 때문에 판 전체를 버리지 않는다.</summary>
        [Test]
        public void ABadNameDoesNotThrowAwayTheRun()
        {
            var sub = Leaderboard.BuildSubmission(252.0, new[] { "재현", " ", "주성" }, null);

            Assert.IsNotNull(sub);
            CollectionAssert.AreEqual(new List<string> { "재현", "주성" }, sub.crewNames);
        }

        [Test]
        public void MoreNamesThanTheGameAllowsAreCut()
        {
            var names = Leaderboard.CleanNames(new[] { "가", "나", "다", "라", "마" });

            Assert.AreEqual(Leaderboard.MaxCrew, names.Count);
            CollectionAssert.DoesNotContain(names, "마");
        }

        [Test]
        public void ALongNameIsShortenedNotDropped()
        {
            var names = Leaderboard.CleanNames(new[] { new string('가', 40) });

            Assert.AreEqual(1, names.Count);
            Assert.AreEqual(20, names[0].Length);
        }

        // --- 서버 응답 읽기 --------------------------------------------------

        /// <summary>
        /// <c>JsonUtility</c> 는 최상위 배열을 못 읽는다. 감싸서 읽는 것이 실제로
        /// 되는지 본다 — 안 되면 표가 조용히 늘 비어 보인다.
        /// </summary>
        [Test]
        public void AServerArrayIsParsed()
        {
            const string json =
                "[{\"rank\":1,\"crewNames\":\"재현 · 수호\",\"clearMillis\":252000,\"crewSize\":2}," +
                "{\"rank\":2,\"crewNames\":\"주성\",\"clearMillis\":300000,\"crewSize\":1}]";

            var rows = LeaderboardUpload.ParseRows(json);

            Assert.AreEqual(2, rows.Length);
            Assert.AreEqual(1, rows[0].rank);
            Assert.AreEqual("재현 · 수호", rows[0].crewNames);
            Assert.AreEqual(252_000, rows[0].clearMillis);
            Assert.AreEqual(2, rows[0].crewSize);
        }

        [Test]
        public void AnEmptyArrayIsNotAnError()
        {
            Assert.AreEqual(0, LeaderboardUpload.ParseRows("[]").Length);
        }

        /// <summary>표를 못 읽어도 로비가 멈추면 안 된다 — 빈 표로 넘어간다.</summary>
        [Test]
        public void BrokenJsonGivesAnEmptyBoardNotAnException()
        {
            Assert.AreEqual(0, LeaderboardUpload.ParseRows("이건 JSON 이 아니다").Length);
            Assert.AreEqual(0, LeaderboardUpload.ParseRows(string.Empty).Length);
            Assert.AreEqual(0, LeaderboardUpload.ParseRows(null).Length);
        }

        // --- 밀린 기록 --------------------------------------------------------

        /// <summary>
        /// <b>못 올린 기록이 줄을 선다</b>(!558 AI 리뷰).
        ///
        /// 예전에는 파일 하나를 덮어써서, 서버가 닿지 않는 동안 두 판을 깨면 앞 판이
        /// 복구 없이 사라졌다. 판이 인원별로 갈리므로 두 기록이 <b>다른 표</b>에 오를
        /// 수도 있어, 최근 것만 남기는 것은 그냥 기록 하나를 잃는 것이다.
        /// </summary>
        [Test]
        public void PendingRunsQueueUpInsteadOfOverwriting()
        {
            LeaderboardUpload.Forget();
            try
            {
                Assert.IsTrue(LeaderboardUpload.Remember(
                    Leaderboard.BuildSubmission(300.0, new[] { "혼자" }, null)));
                Assert.IsTrue(LeaderboardUpload.Remember(
                    Leaderboard.BuildSubmission(250.0, new[] { "넷", "이", "함께", "간다" }, null)));

                Assert.AreEqual(2, LeaderboardUpload.PendingCount,
                    "앞 판이 덮여 사라지면 안 된다.");
            }
            finally
            {
                LeaderboardUpload.Forget();
            }
        }

        /// <summary>파일이 무한히 자라지 않는다. 넘치면 오래된 것부터 버린다.</summary>
        [Test]
        public void ThePendingQueueHasACap()
        {
            LeaderboardUpload.Forget();
            try
            {
                for (var i = 0; i < 12; i++)
                    LeaderboardUpload.Remember(
                        Leaderboard.BuildSubmission(300.0 + i, new[] { "사람" + i }, null));

                Assert.LessOrEqual(LeaderboardUpload.PendingCount, LeaderboardUpload.MaxPending);
                Assert.Greater(LeaderboardUpload.PendingCount, 0);
            }
            finally
            {
                LeaderboardUpload.Forget();
            }
        }

        [Test]
        public void ForgettingClearsEverything()
        {
            LeaderboardUpload.Remember(Leaderboard.BuildSubmission(300.0, new[] { "가" }, null));
            LeaderboardUpload.Forget();

            Assert.IsFalse(LeaderboardUpload.HasPending);
            Assert.AreEqual(0, LeaderboardUpload.PendingCount);
        }
    }
}
