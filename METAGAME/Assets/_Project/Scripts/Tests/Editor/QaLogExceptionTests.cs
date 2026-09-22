using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// QA 로그에 <b>예외가 남지 않는다</b> (S15P21B202-693).
    ///
    /// <para>2026-09-22 QA 로그 두 개에 있던 <c>NullReferenceException</c> 전부가 둘이었다 —
    /// <c>NetPlayer.AttachTransport</c> 3건, <c>EdgegapKcpTransport</c> 의 끊김 콜백 2건.
    /// 다른 종류는 없었다.</para>
    ///
    /// <para><b>왜 로그를 지키는 것이 일인가.</b> QA 를 사람 눈이 아니라 로그로 판정하기로
    /// 했다(<c>-654</c>). 그러면 매 판 끝에 찍히는 빨간 줄은 <b>진짜 예외를 찾을 때 섞이는
    /// 잡음</b>이 된다 — <c>-685</c> 에서 정한 "빨간 것이 늘 있으면 없는 것과 같다" 와
    /// 같은 이유다.</para>
    ///
    /// <para><b>왜 글자로 보나.</b> 둘 다 값으로 볼 수 없다. 하나는 "돌려받은 값을 확인하고
    /// 쓰는가" 라는 <b>순서</b>이고(그 값이 <c>null</c> 이 되는 조건을 EditMode 에서 만들 수
    /// 없다 — 파괴 예약을 흉내 내야 한다), 하나는 <b>벤더 파일의 한 글자</b>다. 그래서
    /// <see cref="TestSource"/> 를 쓴다. 남용하지 않는 규칙에 맞는 경우다.</para>
    /// </summary>
    public sealed class QaLogExceptionTests
    {
        const string NetPlayer = "Runtime/Network/NetPlayer.cs";

        /// <summary>
        /// <c>AddComponent</c> 가 <c>null</c> 을 돌려줄 수 있다는 것을 <b>확인한 뒤</b> 쓴다.
        ///
        /// <para><c>TractorBeamController</c> 는 <c>DisallowMultipleComponent</c> 라, 이미
        /// 붙어 있는 것이 <b>파괴 예약 상태</b>면 추가가 거절되고 <c>null</c> 이 온다.
        /// <b>그 위험은 이미 주석으로 적혀 있었다</b>(*"Destroy is deferred, so
        /// destroying/re-adding in the same frame violates DisallowMultipleComponent"*) —
        /// 적어 두고 결과를 보지 않은 것이 결함이었다.</para>
        ///
        /// <para>순서를 본다. <c>null</c> 검사가 <c>transport.enabled</c> 보다 <b>앞에</b>
        /// 있어야 한다. 검사만 있고 뒤에 있으면 아무것도 막지 못한다.</para>
        /// </summary>
        [Test]
        public void TheTransportAttachChecksWhatAddComponentReturned()
        {
            var scope = TestSource.Method(TestSource.Read(NetPlayer), "bool AttachTransport()");

            int add = scope.IndexOf("AddComponent<TractorBeamController>()",
                                    System.StringComparison.Ordinal);
            int guard = scope.IndexOf("if (transport == null)", add + 1,
                                      System.StringComparison.Ordinal);
            int use = scope.IndexOf("transport.enabled", System.StringComparison.Ordinal);

            Assert.That(add, Is.GreaterThanOrEqualTo(0), "조작기를 붙이지 않는다");
            Assert.That(guard, Is.GreaterThan(add),
                "AddComponent 가 돌려준 값을 확인하지 않는다 — DisallowMultipleComponent 가 "
                + "거절하면 null 이 오고 다음 줄에서 NRE 가 난다(2026-09-22 QA 3건)");
            Assert.That(use, Is.GreaterThan(guard),
                "확인보다 먼저 쓴다 — 검사가 있어도 아무것도 막지 못한다");
        }

        /// <summary>
        /// 못 붙었으면 <b>다시 시도한다</b> — 조용히 견인빔 없이 계속 돌지 않는다.
        ///
        /// <para>거절 원인이 "같은 프레임의 파괴 예약" 이라 다음 스텝에는 붙는다. 그런데
        /// <c>body</c> 가 이미 채워져 있어 <c>BuildServerBody</c> 는 다시 돌지 않으므로,
        /// 다시 붙이는 자리가 따로 있어야 한다. 없으면 입력 경로의
        /// <c>if (transport != null)</c> 가드 때문에 <b>그 사람의 견인빔이 판이 끝날 때까지
        /// 아무 말 없이 없는 채로</b> 남는다.</para>
        ///
        /// <para><c>transportWanted</c> 로 가르는 것도 함께 본다. 그 값이 없으면 붙일 일이
        /// 없는 기계에서도 조작기를 새로 만든다.</para>
        /// </summary>
        [Test]
        public void AFailedAttachIsRetriedInsteadOfLostSilently()
        {
            var scope = TestSource.Method(TestSource.Read(NetPlayer), "void FixedUpdate()");

            Assert.That(scope, Does.Contain("transportWanted && transport == null"),
                "못 붙은 것을 다시 시도하지 않는다 — 그 사람의 견인빔이 판이 끝날 때까지 "
                + "조용히 없는 채로 남는다");
            Assert.That(scope, Does.Contain("RetryAttachTransport()"),
                "다시 붙이는 호출이 없다");
        }

        /// <summary>
        /// 다시 붙이기에 <b>상한이 있고, 그만두는 것을 말한다</b> (S15P21B202-693).
        ///
        /// <para><b>이 검사가 막는 것은 이 MR 이 만들 수 있었던 새 침묵이다</b>(`!586` AI
        /// 리뷰 지적). 거절 원인이 "같은 프레임의 파괴 예약" 이 아니면 재시도가 <b>매 물리
        /// 스텝</b> 돌고, <c>AddComponent</c> 는 거절될 때마다 <b>Unity 가 제 오류를 찍는다</b>
        /// — 초당 50줄이면 그 줄들이 로그를 못 읽게 만든다. 고치려던 모양을 새 코드로 다시
        /// 만드는 것이다.</para>
        ///
        /// <para>그리고 <b>그만두는 것을 조용히 하지 않는다.</b> 그만둔 판에서 그 사람의
        /// 견인빔은 동작하지 않으므로, 그 사실이 로그에 있어야 다음 QA 의 "나만 장치가
        /// 안 된다" 를 여기로 이을 수 있다.</para>
        /// </summary>
        [Test]
        public void TheRetryStopsAndSaysSoInsteadOfHammeringForever()
        {
            var src = TestSource.Read(NetPlayer);
            var scope = TestSource.Method(src, "void RetryAttachTransport()");

            Assert.That(scope, Does.Contain("TransportRetryMaxSteps"),
                "재시도에 상한이 없다 — 붙을 때까지 매 스텝 AddComponent 가 돌고, 거절될 "
                + "때마다 Unity 가 오류를 찍어 로그를 못 읽게 만든다");
            Assert.That(scope, Does.Contain("Debug.LogError"),
                "그만두는 것을 조용히 한다 — 그 판에서 그 사람의 견인빔이 동작하지 않는데 "
                + "로그에 근거가 남지 않는다");

            // 상한 뒤에 **붙이기를 부르지 않는다.** 남기기만 하고 그대로 계속 부르면
            // 상한이 아무것도 막지 못한다.
            int stop = scope.IndexOf("TransportRetryMaxSteps", System.StringComparison.Ordinal);
            int call = scope.IndexOf("AttachTransport()", System.StringComparison.Ordinal);
            Assert.That(call, Is.GreaterThan(stop),
                "상한 검사보다 먼저 붙이기를 부른다 — 상한이 아무것도 막지 못한다");

            // 몸을 새로 세우면 셈을 되돌린다. 안 되돌리면 새 몸이 첫 시도부터 상한을
            // 넘은 것으로 취급된다.
            Assert.That(TestSource.Method(src, "bool BuildServerBody()"),
                Does.Contain("transportRetrySteps = 0"),
                "몸을 새로 세울 때 셈을 되돌리지 않는다 — 새 몸이 첫 시도부터 포기한다");
        }

        /// <summary>
        /// 벤더의 <b>서버 끊김 콜백</b>이 <c>?.</c> 로 남아 있다 (S15P21B202-693).
        ///
        /// <para><c>OnServerDisconnected</c> 는 아무도 구독하지 않으면(서버를 이미 내렸는데
        /// KCP 가 늦게 도착한 끊김을 처리하는 순간) <c>null</c> 이다. <b>상류 Mirror 가 바로
        /// 한 줄 위에서 같은 이유로 이미 고쳐 두었다</b> — <c>OnClientDisconnected?.Invoke()</c>
        /// 에 *"may be null in StopHost()"* 와 이슈 링크가 붙어 있다. 서버 쪽만 빠졌고,
        /// 우리가 터진 것이 그 줄이다.</para>
        ///
        /// <para><b>이 검사가 지키는 것은 Mirror 업데이트다.</b> 저장소에서 벤더 코드를
        /// 고친 것은 이것이 처음이라(<c>ThirdParty/</c> 에 다른 로컬 수정 표식이 없다),
        /// 다음 업데이트가 이 한 글자를 <b>조용히 되돌린다.</b> 되돌아가면 QA 로그에
        /// 예외가 다시 찍히기 시작하는데, 그때는 원인을 다시 찾아야 한다.</para>
        ///
        /// <para>두 파일에 같은 줄이 있어 <b>둘 다</b> 본다 — 릴레이 경로와 직접 경로다.
        /// 한쪽만 고치면 경로를 바꾸는 날 돌아온다.</para>
        /// </summary>
        [TestCase("ThirdParty/Mirror/Transports/Edgegap/EdgegapRelay/EdgegapKcpTransport.cs")]
        [TestCase("ThirdParty/Mirror/Transports/KCP/KcpTransport.cs")]
        public void TheServerDisconnectCallbackStaysNullSafe(string path)
        {
            // **`TestSource.Read` 를 쓰지 않는다.** 그것은 `Assets/_Project/Scripts/` 를
            // 기준으로 잡는데 벤더 코드는 그 밖(`_Project/ThirdParty/`)에 있다.
            string full = Path.Combine(Application.dataPath, "_Project", path);
            Assert.That(File.Exists(full), Is.True, $"벤더 파일을 찾지 못했다: {full}");
            var src = File.ReadAllText(full);

            Assert.That(src, Does.Contain("OnServerDisconnected?.Invoke(connectionId)"),
                $"{path} 의 서버 끊김 콜백에서 `?.` 가 사라졌다 — Mirror 업데이트가 "
                + "되돌렸을 수 있다. 서버를 내린 뒤 도착한 끊김에서 NRE 가 나고, 판마다 "
                + "빨간 줄이 찍혀 진짜 예외를 못 찾게 된다(2026-09-22 QA 2건)");

            Assert.That(src, Does.Not.Contain("OnServerDisconnected.Invoke(connectionId)"),
                $"{path} 에 `?.` 없는 호출이 남아 있다");
        }
    }
}
