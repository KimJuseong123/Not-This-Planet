using Metagame.Bootstrap;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 판이 <b>어느 국면</b>에 있었는지 (S15P21B202-627).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이벤트에 있던 것은 <c>assembly</c>(층 상태)와
    /// <c>blocked</c>(발목) 뿐이었다. 그래서 한 판이 <b>컷신에서 나갔는지, 조립하다
    /// 나갔는지, 발사하고 나갔는지</b> 알 수 없었다 — <c>-625</c> 가 "그만뒀다" 를
    /// 답해도 "어디서" 가 비어 있으면 기획에 쓸 수 없다.</para>
    ///
    /// <para>실행 로그에는 <c>[Stage1Cutscene] Entry</c> 처럼 찍히고 있었다. 사람이 읽는
    /// 줄로만 남고 기계가 세는 줄로는 남지 않은 것이 문제였다.</para>
    /// </summary>
    public sealed class PlayPhaseTests
    {
        [Test]
        public void ThePlainCaseIsPlaying()
        {
            Assert.That(PlayObserver.PhaseOf(endingActive: false, inCutscene: false),
                        Is.EqualTo("play"));
        }

        [Test]
        public void TheCutsceneIsItsOwnPhase()
        {
            Assert.That(PlayObserver.PhaseOf(endingActive: false, inCutscene: true),
                        Is.EqualTo("cutscene"),
                        "컷신에서 나간 판이 조립에서 나간 판과 같아진다");
        }

        /// <summary>
        /// <b>연출이 먼저다.</b> 연출은 발사 뒤이고 컷신은 입장이라, 둘이 함께 참으로
        /// 보이면 뒤의 것이 맞다.
        /// </summary>
        [Test]
        public void TheEndingWinsOverTheCutscene()
        {
            Assert.That(PlayObserver.PhaseOf(endingActive: true, inCutscene: false),
                        Is.EqualTo("ending"));

            Assert.That(PlayObserver.PhaseOf(endingActive: true, inCutscene: true),
                        Is.EqualTo("ending"),
                        "발사 뒤 연출을 입장 컷신으로 읽는다");
        }

        /// <summary>
        /// <b>국면은 호스트만 내는 값이 아니다.</b> 값 자체는 모두에게 같지만, 답해야
        /// 하는 질문이 <i>"이 사람이 어디서 그만뒀나"</i> 라 <b>떠난 사람의 파일에</b>
        /// 있어야 한다. 호스트만 내면 참가자 파일에는 국면이 없고, 그 둘을 잇는
        /// <c>match</c> 열쇠는 아직 없다 — 참가자의 이탈이 분석 불가가 된다.
        ///
        /// <para>자리로만 지켜지는 규칙이라 자리를 본다. <c>PlayEvents.Write("phase", …)</c>
        /// 를 글자로 찾을 수는 없다 — <see cref="TestSource.Blank"/> 가 리터럴을 지운다.
        /// 그래서 <c>PhaseOf</c> 호출이 <c>authority</c> 검사보다 <b>앞</b>인지로 본다.</para>
        /// </summary>
        [Test]
        public void EveryoneRecordsTheirOwnPhaseNotJustTheHost()
        {
            var body = TestSource.Method(TestSource.Read("Runtime/Bootstrap/PlayObserver.cs"),
                                         "void Update()");

            int phaseAt = body.IndexOf("PhaseOf(", System.StringComparison.Ordinal);
            int hostOnlyAt = body.IndexOf("if (!authority) return;", System.StringComparison.Ordinal);

            Assert.That(phaseAt, Is.GreaterThanOrEqualTo(0), "국면을 아예 보지 않는다");
            Assert.That(hostOnlyAt, Is.GreaterThanOrEqualTo(0),
                        "호스트 전용 구간이 사라졌다 — 이 검사의 기준점이 없어졌다");

            Assert.That(phaseAt, Is.LessThan(hostOnlyAt),
                        "국면이 호스트 전용 구간으로 들어갔다 — 참가자 파일에 국면이 없어진다");
        }

        /// <summary>
        /// 연출 안의 세부(<c>DemoEndingStage</c>)도 실려야 한다. 아홉 단계가 이미 정의돼
        /// 있으므로 칸 하나로 공짜로 얻는 값인데, 빠지면 <c>ending</c> 안에서 어디였는지가
        /// 사라진다.
        /// </summary>
        [Test]
        public void TheEndingCarriesItsOwnStage()
        {
            var body = TestSource.Method(TestSource.Read("Runtime/Bootstrap/PlayObserver.cs"),
                                         "void Update()");

            Assert.That(body, Does.Contain("session.Ending.Stage"),
                        "연출의 세부 단계를 읽지 않는다");

            // 칸 이름은 리터럴이라 `Blank` 가 지운다 — **대입되는 쪽**으로 본다.
            Assert.That(body, Does.Contain("] = stage;"),
                        "읽어 놓고 칸에 넣지 않는다 — ending 안에서 어디였는지가 사라진다");
        }

        /// <summary>
        /// 새 판은 국면 기억이 비어 있어야 한다. 남으면 <b>두 번째 판의 첫 국면이 안
        /// 찍힌다</b> — 같은 관찰자로 두 번째 판을 여는 길이 실제로 있다
        /// (<c>SpaceBootstrap</c> 이 재사용한다).
        /// </summary>
        [Test]
        public void ANewPlayForgetsTheLastPhase()
        {
            var begin = TestSource.Method(TestSource.Read("Runtime/Bootstrap/PlayObserver.cs"),
                                          "public void Begin(GameSession gameSession, bool hostAuthority, IDictionary<string, object> header)");

            Assert.That(begin, Does.Contain("phase = string.Empty"),
                        "앞 판의 국면 기억이 남는다 — 두 번째 판의 첫 국면이 안 찍힌다");
        }
    }
}
