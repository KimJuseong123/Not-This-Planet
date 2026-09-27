using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Metagame.Network;
using Metagame.UI;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 준비 중 화면 글자가 <b>방장의 말로만</b>되어 있는가 (S15P21B202-713).
    ///
    /// <para><b>왜 값으로 보나.</b> <c>TestSource</c> 는 문자열을 지우고 본다 — 소스를 훑어서는
    /// 화면 글자를 볼 수 없다. 모든 단계 × 모든 실패를 실제로 그려 보고 그 글자를 검사한다.
    /// 새 단계나 사유가 생겨 <c>default</c> 로 떨어져도 여기서 함께 그려진다.</para>
    /// </summary>
    public sealed class AiPrepTextTests
    {
        /// <summary>화면에 나오면 안 되는 말. 방장에게는 속사정이고, 겁만 준다.</summary>
        internal static readonly string[] Banned =
        {
            "모델", "데우", "첫 실행", "warm", "cold", "슬롯", "slot", "캐시", "cache",
            "llm", "워커", "worker", ".zip", "압축", "추가팩", "/", "\\",
        };

        /// <summary>
        /// 어떤 상태를 그려도 <b>속사정이 새지 않는다</b> (S15P21B202-713).
        ///
        /// <para>전에는 로그 줄이 곧 화면 줄이었다 — "모델 데우는 중 · 첫 실행은 몇 분 걸려요",
        /// "검증 및 압축 해제 중 · runtime.zip", "추가팩 없음: C:\…". 사유 원문과 파일 이름을
        /// 일부러 채워 넣고 그려도 화면에는 나오지 않아야 한다.</para>
        /// </summary>
        [Test] public void NothingOnScreenSpeaksInternals()
        {
            int drawn = 0;
            foreach (var s in Every())
            {
                string text = AiPrepProgress.Display(s);
                Assert.That(text, Is.Not.Null.And.Not.Empty, Describe(s) + " 에서 화면 글자가 비었다");
                string lower = text.ToLowerInvariant();
                foreach (string word in Banned)
                    Assert.That(lower, Does.Not.Contain(word),
                        Describe(s) + " 의 화면 글자에 \"" + word + "\" 가 나온다: " + text);
                drawn++;
            }
            Assert.That(drawn, Is.GreaterThan(Enum.GetValues(typeof(AiPrepPhase)).Length * Enum.GetValues(typeof(AiPrepFailure)).Length),
                "모든 단계 × 모든 실패를 그리지 않았다");
        }

        /// <summary>
        /// 남은 시간을 <b>알면 말하고, 모르면 숫자를 대지 않는다</b> (S15P21B202-713).
        ///
        /// <para>모르는데 초를 대면 그 숫자는 곧 틀린다. 틀리게 늘어나는 남은 시간은 멈춘 것보다
        /// 나쁘다 — 그래서 "계산 중" · "멈췄어요" · "조금 더 걸리고 있어요" 로만 말한다.</para>
        /// </summary>
        [Test] public void TheScreenSaysTheRemainingTimeOnlyWhenItKnowsIt()
        {
            var figure = new Regex(@"\d+\s*(초|분|시간)");
            foreach (var s in Every())
            {
                string text = AiPrepProgress.Display(s);
                double? left = AiPrepProgress.Remaining(s);
                bool working = s.Phase != AiPrepPhase.Done && s.Phase != AiPrepPhase.Failed && s.Phase != AiPrepPhase.Cancelled;
                if (working && left.HasValue)
                    Assert.That(text, Does.Contain(AiPrepProgress.FormatRemaining(left)),
                        Describe(s) + " 에서 남은 시간을 아는데 말하지 않는다: " + text);
                else
                    Assert.That(figure.IsMatch(text), Is.False,
                        Describe(s) + " 에서 남은 시간을 모르는데 숫자를 댄다: " + text);
                Assert.That(text, Does.Not.Contain("초"), Describe(s) + " 에서 초를 센다 — 화면은 경과가 아니라 남은 시간이다: " + text);
            }
        }

        /// <summary>
        /// <b>"완료" 는 끝에서만</b> 나온다 (S15P21B202-713).
        ///
        /// <para>전에는 설치를 마치자마자 "추가팩 설치 완료 · AI 준비 중" 이라고 했다. 방장에게
        /// 남은 것은 몇 분짜리 데우기였는데 화면은 끝났다고 했다.</para>
        /// </summary>
        [Test] public void OnlyTheEndSaysDone()
        {
            foreach (var s in Every())
            {
                string text = AiPrepProgress.Display(s);
                if (s.Phase == AiPrepPhase.Done) Assert.That(text, Is.EqualTo("완료되었습니다"));
                else Assert.That(text, Does.Not.Contain("완료"), Describe(s) + " 에서 끝나기 전에 완료라고 한다: " + text);
            }
        }

        /// <summary>
        /// 로그에는 <b>전부</b> 남는다 (S15P21B202-713). 화면에서 뺀 것이 로그에서도 빠지면
        /// QA 뒤 <c>Player.log</c> 로 원인을 찾을 수 없다(S15P21B202-708).
        /// </summary>
        [Test] public void TheLogKeepsWhatTheScreenLeavesOut()
        {
            var s = Sample(AiPrepPhase.Failed, AiPrepFailure.Corrupt, true, false, false, 0);
            string log = AiPrepProgress.Log(s);
            Assert.That(log, Does.Contain("models.zip"), "어느 파일인지가 빠졌다");
            Assert.That(log, Does.Contain(s.Detail), "사유 원문이 빠졌다");
            Assert.That(log, Does.Contain(nameof(AiPrepFailure.Corrupt)), "실패 이름이 빠졌다");
            Assert.That(log, Does.Contain("MB"), "얼마나 받았는지가 빠졌다");

            var stalled = Sample(AiPrepPhase.Fetch, AiPrepFailure.None, true, true, false, 0);
            Assert.That(AiPrepProgress.Log(stalled), Does.Contain("응답 없음"), "멈춘 받기가 로그에서 흐르는 받기와 같다");

            var space = Sample(AiPrepPhase.Failed, AiPrepFailure.NoSpace, true, false, false, 3L * 1073741824);
            Assert.That(AiPrepProgress.Display(space), Does.Contain("3.0 GB"),
                "공간이 모자란데 얼마를 비워야 하는지 말하지 않는다 — 방장이 할 수 있는 것이 없다");
        }

        /// <summary>
        /// 설치 창과 방 화면의 상태 줄도 <b>방장의 말로만</b> 되어 있다 (S15P21B202-713). 창의 제목·버튼·본문,
        /// 방장 AI 줄의 남은 시간, 상태 줄이 낼 수 있는 모든 글자를 그려 본다.
        ///
        /// <para>상태 줄은 전에 실행기·설치기의 로그 글자를 그대로 보였다 — "추가팩 로딩 중", "LLM 판단",
        /// "추가팩 없음: C:\…". 이제 그 글자는 로그에만 간다.</para>
        /// </summary>
        [Test] public void NothingInTheInstallWindowSpeaksInternals()
        {
            int drawn = 0;
            foreach (string text in WindowTexts())
            {
                Assert.That(text, Is.Not.Null.And.Not.Empty, "창이나 상태 줄의 글자가 비었다");
                string lower = text.ToLowerInvariant();
                foreach (string word in Banned)
                    Assert.That(lower, Does.Not.Contain(word), "창·상태 줄 글자에 \"" + word + "\" 가 나온다: " + text);
                drawn++;
            }
            Assert.That(drawn, Is.GreaterThan(Enum.GetValues(typeof(AiPrepPhase)).Length * Enum.GetValues(typeof(AiPrepFailure)).Length),
                "창이 낼 수 있는 글자를 다 그리지 않았다");

            // "완료" 는 끝에서만 — 설치가 끝났을 뿐인데 끝났다고 하면 방장은 AI 가 왜 안 오는지 모른다.
            foreach (AiAddStep step in Enum.GetValues(typeof(AiAddStep)))
            {
                if (step == AiAddStep.Done) continue;
                Assert.That(AiAddFlow.Title(step), Does.Not.Contain("완료"), step + " 에서 끝나기 전에 완료라고 한다");
                foreach (AiPrepFailure failure in Enum.GetValues(typeof(AiPrepFailure)))
                    Assert.That(AiAddFlow.Buttons(step, failure), Has.No.Member(AiAddButton.Finish), step + " 에 완료 버튼이 있다");
            }
        }

        /// <summary>
        /// 설치 창과 방 화면이 낼 수 있는 글자 전부 — 금지어 검사와 글꼴 예열 검사(<c>AiAddFlowTests</c>)가 함께 쓴다.
        /// 창의 본문은 흐름을 실제로 돌려 얻는다.
        /// </summary>
        internal static IEnumerable<string> WindowTexts()
        {
            foreach (AiAddStep step in Enum.GetValues(typeof(AiAddStep)))
            {
                if (step != AiAddStep.Idle) yield return AiAddFlow.Title(step);
                foreach (AiPrepFailure failure in Enum.GetValues(typeof(AiPrepFailure)))
                    foreach (AiAddButton button in AiAddFlow.Buttons(step, failure))
                        yield return AiAddFlow.Label(button);
            }
            yield return AiAddFlow.RetryRuntimeLabel;
            foreach (AiRoomState state in Enum.GetValues(typeof(AiRoomState)))
                yield return AiAddFlow.HostStatus(state, false, AiRuntimeState.Ready, default);
            foreach (var s in Every())
            {
                yield return AiAddFlow.HostStatus(AiRoomState.Off, true, AiRuntimeState.Missing, s);
                yield return AiAddFlow.HostStatus(AiRoomState.Loading, false, AiRuntimeState.Loading, s);
                yield return AiAddFlow.RemainingText(s);
                if (s.Phase == AiPrepPhase.Start)
                    yield return AiPrepProgress.Display(AiAddFlow.Warming(s, s.Elapsed, s.WarmupElapsed, s.WarmupExpected, s.Progress));
            }
            foreach (string body in AiAddFlowTests.EveryBody()) yield return body;
        }

        internal static IEnumerable<AiPrepSnapshot> Every()
        {
            foreach (AiPrepPhase phase in Enum.GetValues(typeof(AiPrepPhase)))
            foreach (AiPrepFailure failure in Enum.GetValues(typeof(AiPrepFailure)))
            foreach (bool known in new[] { true, false })
            foreach (bool stalled in new[] { false, true })
            foreach (bool overrun in new[] { false, true })
            foreach (long needed in new[] { 0L, 3L * 1073741824 })
                yield return Sample(phase, failure, known, stalled, overrun, needed);
        }

        static AiPrepSnapshot Sample(AiPrepPhase phase, AiPrepFailure failure, bool known, bool stalled, bool overrun, long needed)
        {
            const long MB = ByteRate.Megabyte;
            bool fetched = phase >= AiPrepPhase.Unpack;
            return new AiPrepSnapshot
            {
                Phase = phase, Failure = failure,
                // 화면에 새면 안 되는 것들을 일부러 채운다.
                Archive = "models.zip",
                Detail = "IOException: C:\\Users\\qa\\AppData\\LocalLow\\AI\\.download\\ab.zip · 모델 worker cache LLM",
                FetchTotal = 2105 * MB, FetchDone = fetched ? 2105 * MB : 900 * MB,
                FetchRate = known ? 3.5 * MB : (double?)null,
                Idle = stalled ? AiPackInstaller.StallNotice + 3 : 0,
                HashTotal = 2233 * MB, HashDone = fetched ? 2233 * MB : 128 * MB,
                HashRate = known ? 400.0 * MB : (double?)null,
                ExtractTotal = 2500 * MB, ExtractDone = fetched ? 2500 * MB : 400 * MB,
                ExtractRate = known ? 150.0 * MB : (double?)null,
                WarmupElapsed = overrun ? 400 : 40, WarmupExpected = 227,
                Elapsed = 700, Progress = .45f, NeededBytes = needed,
            };
        }

        static string Describe(AiPrepSnapshot s)
            => s.Phase + "/" + s.Failure + (s.FetchRate.HasValue ? " 속도앎" : " 속도모름")
               + (AiPrepProgress.Stalled(s) ? " 멈춤" : "") + " 데우기" + s.WarmupElapsed + "초";
    }
}
