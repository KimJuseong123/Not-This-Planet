using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>임시 검증용이다 — 이 커밋 다음 커밋에서 지운다.</b>
    ///
    /// <para><c>S15P21B202-347</c> 이 넣은 <b>플래키 재실행 경로</b>가 실제로 도는지 확인한다.
    /// 그 경로는 <b>새 실패가 있을 때만</b> 실행되므로, 정상적인 MR 에서는 한 번도 돌지 않는다 —
    /// 그래서 오프라인 테스트로는 덮을 수 없고 미검증인 채로 머지될 뻔했다(<c>!272</c> 4차
    /// AI 리뷰가 잡았다).</para>
    ///
    /// <para><b>무엇을 보려는가.</b> 잡 로그에서 이 세 줄을 확인한다.</para>
    /// <code>
    /// 새 실패 3건 — 그 테스트만 골라 2회 다시 돌린다
    /// 재실행 unity-recheck-1.xml: 대상 3/3건 · 이 회차 통과 0건
    /// 재실행 판별: 플래키 0건 · 계속 실패 3건 · 확인 못함 0건
    /// </code>
    ///
    /// <para><b>`대상 3/3건` 이 핵심이다.</b> 0 이면 <c>-testFilter</c> 가 이름을 못 잡은
    /// 것이고, 그러면 전원 '확인 못함' → 회귀가 되어 그 장치가 무력화된다. 세미콜론 구분자가
    /// 이 Unity 버전에서 통하는지는 문서로 확인할 수 없어 이렇게 재 봐야 한다.</para>
    ///
    /// <para><b>괄호가 든 이름을 일부러 섞었다.</b> <c>-testFilter</c> 는 이름을 정규식으로
    /// 해석하므로 <c>Probe_Parameterized(1,2)</c> 같은 이름은 이스케이프 없이는 매칭되지
    /// 않는다. <c>testfilter_value()</c> 가 <c>re.escape</c> 로 감싸는데, 그것이 실제
    /// Unity 에서도 통하는지가 여기서 갈린다.</para>
    ///
    /// <para>이 테스트들은 <b>결정적으로 실패한다</b> — 재실행에서도 실패하므로 판정은
    /// <c>1</c>(막는다)이 되어야 맞다. 그것도 확인 대상이다: 재실행이 붙었다고 해서
    /// 진짜 실패가 통과하면 안 된다.</para>
    /// </summary>
    public sealed class TempRecheckProbeTests
    {
        /// <summary>평범한 이름 — 세미콜론 목록의 한 항목이 잡히는지.</summary>
        [Test]
        public void Probe_Plain()
        {
            Assert.Fail("재실행 경로 검증용 — 일부러 실패한다 (S15P21B202-347)");
        }

        /// <summary>또 하나. 목록이 둘 이상일 때 구분자가 통하는지.</summary>
        [Test]
        public void Probe_Second()
        {
            Assert.Fail("재실행 경로 검증용 — 일부러 실패한다 (S15P21B202-347)");
        }

        /// <summary>
        /// 괄호·쉼표가 든 이름 — 정규식 이스케이프가 없으면 이 항목만 매칭에서 빠진다.
        /// 그러면 로그의 `대상 N/M건` 에서 N &lt; M 으로 드러난다.
        /// </summary>
        [TestCase(1, 2)]
        public void Probe_Parameterized(int a, int b)
        {
            Assert.Fail($"재실행 경로 검증용 — 일부러 실패한다 ({a},{b})");
        }
    }
}
