using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>AI 가 한 일이 <b>QA 뒤에도 남는가</b> (S15P21B202-708).</summary>
    public sealed class AiStatusLogTests
    {
        /// <summary>
        /// 진행 표시가 <b>로그를 덮지 않는다</b> (S15P21B202-708).
        ///
        /// <para><c>모델 데우는 중 42초</c> 는 0.5초마다 바뀐다. 매번 적으면 900초 동안 1,800줄이
        /// 쌓여 <b>정작 읽어야 할 줄이 묻힌다</b> — 빨간 것이 늘 있으면 없는 것과 같다.</para>
        ///
        /// <para>그렇다고 안 적으면 진행이 안 보인다. 그래서 <b>같은 종류는 뜸하게, 다른 종류는
        /// 곧바로</b>다.</para>
        /// </summary>
        [Test] public void ProgressIsQuietButFailuresAreNot()
        {
            var log = new AiStatusLog();

            Assert.That(log.Due("AI 준비 중 · 모델 데우는 중 3초", 0f), Is.True, "첫 줄은 적어야 한다");
            Assert.That(log.Due("AI 준비 중 · 모델 데우는 중 4초", 1f), Is.False,
                "같은 종류를 매번 적는다 — 0.5초마다 바뀌는 값이라 로그가 이것만 남는다");
            Assert.That(log.Due("AI 준비 중 · 모델 데우는 중 40초", 37f), Is.True,
                "같은 종류를 영영 안 적는다 — 진행이 보이지 않는다");

            // 다른 종류는 뜸할 이유가 없다. 이것이 바로 읽어야 할 줄이다.
            Assert.That(log.Due("추가팩이 종료됐습니다", 37.1f), Is.True,
                "종류가 달라졌는데 앞 줄의 뜸함에 걸려 실패가 묻힌다");
        }

        [Test] public void TheKindIgnoresOnlyTheNumbers()
        {
            Assert.That(AiStatusLog.Kind("모델 데우는 중 42초"), Is.EqualTo(AiStatusLog.Kind("모델 데우는 중 7초")));
            Assert.That(AiStatusLog.Kind("다운로드 45% · 1024 / 2260 MB"),
                Is.EqualTo(AiStatusLog.Kind("다운로드 90% · 2048 / 2260 MB")));
            Assert.That(AiStatusLog.Kind("추가팩이 종료됐습니다"),
                Is.Not.EqualTo(AiStatusLog.Kind("추가팩 연결을 잃었습니다")),
                "서로 다른 실패가 한 종류로 묶이면 뒤엣것이 묻힌다");
            Assert.That(AiStatusLog.Kind(null), Is.EqualTo(""));
        }

        /// <summary>
        /// 계획이 <b>무엇을 시켰는지</b> 말한다 (S15P21B202-708).
        ///
        /// <para>실행 요약은 결과만 말한다. 엉뚱하게 수행됐을 때 <b>잘못 들었는지 · 잘못
        /// 계획했는지 · 계획대로 됐는데 결과가 다른지</b>를 가르려면 이 줄이 있어야 한다.</para>
        /// </summary>
        [Test] public void ThePlanLineSaysWhoDoesWhat()
        {
            var orders = new[]
            {
                new AiCommandAssignment { recipients = "green", steps = new[]
                {
                    new AiCommandStep { action = "pull_pin", destination = "default" },
                    new AiCommandStep { action = "follow", destination = "speaker" }
                }},
                new AiCommandAssignment { recipients = "all", steps = new[]
                {
                    new AiCommandStep { action = "cheer", approach = true, destination = "default" }
                }}
            };

            string line = AiCommandPlan.Describe(orders);
            Assert.That(line, Does.Contain("green"));
            Assert.That(line, Does.Contain("pull_pin"));
            Assert.That(line, Does.Contain("follow@speaker"), "기본이 아닌 목적지가 빠졌다");
            Assert.That(line, Does.Contain("+접근"), "접근 여부가 빠졌다 — 사교 행동은 그것으로 갈린다");
            Assert.That(line, Does.Not.Contain("@default"), "기본 목적지까지 적으면 읽는 눈이 그 자리를 지나친다");
        }

        /// <summary>
        /// <b>잘못된 계획일수록</b> 무엇이 왔는지 알아야 한다 (S15P21B202-708).
        ///
        /// <para>검증을 통과하지 못한 계획에서도 이 줄을 적는다. 여기서 터지면 <b>가장 알고
        /// 싶은 순간에 로그가 사라진다.</b></para>
        /// </summary>
        [Test] public void ABrokenPlanStillDescribesItself()
        {
            Assert.That(AiCommandPlan.Describe(null), Is.EqualTo("(없음)"));
            Assert.That(AiCommandPlan.Describe(new AiCommandAssignment[0]), Is.EqualTo("(없음)"));
            Assert.DoesNotThrow(() => AiCommandPlan.Describe(new AiCommandAssignment[] { null }));
            Assert.DoesNotThrow(() => AiCommandPlan.Describe(new[]
                { new AiCommandAssignment { recipients = null, steps = null } }));
            Assert.DoesNotThrow(() => AiCommandPlan.Describe(new[]
                { new AiCommandAssignment { recipients = "one", steps = new AiCommandStep[] { null } } }));
        }

        /// <summary>
        /// 워커가 보내는 <b>전사를 버리지 않는다</b> (S15P21B202-708).
        ///
        /// <para>워커의 응답에는 <c>text</c> 가 처음부터 들어 있었는데, C# 쪽이 그 필드를
        /// 선언하지 않아 <c>JsonUtility</c> 가 <b>조용히 버렸다.</b> 무엇을 들었는지가 없으면
        /// 잘못 들은 것과 잘못 계획한 것을 가를 수 없다.</para>
        /// </summary>
        [Test] public void TheTranscriptSurvivesParsing()
        {
            var parsed = UnityEngine.JsonUtility.FromJson<AiLocalRuntime.PlanDecision>(
                "{\"status\":\"ok\",\"text\":\"초록색 애한테 고정못 뽑으라고 해\",\"elapsed_ms\":1820,\"orders\":[]}");

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.text, Is.EqualTo("초록색 애한테 고정못 뽑으라고 해"),
                "전사가 파싱에서 사라진다 — 워커는 보내고 있는데 C# 이 받지 않는다");
            Assert.That(parsed.elapsed_ms, Is.EqualTo(1820f));
        }
    }
}
