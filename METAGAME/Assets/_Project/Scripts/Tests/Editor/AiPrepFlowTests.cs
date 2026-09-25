using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>준비 흐름이 <b>무엇을 하고 있는지 말하는가</b> (S15P21B202-706).</summary>
    public sealed class AiPrepFlowTests
    {
        [Test] public void TheClockReadsLikeAPerson()
        {
            Assert.That(AiLocalRuntime.Clock(0), Is.EqualTo("0초"));
            Assert.That(AiLocalRuntime.Clock(42), Is.EqualTo("42초"));
            Assert.That(AiLocalRuntime.Clock(59.9f), Is.EqualTo("59초"));
            Assert.That(AiLocalRuntime.Clock(60), Is.EqualTo("1분 0초"));
            Assert.That(AiLocalRuntime.Clock(72), Is.EqualTo("1분 12초"));
            Assert.That(AiLocalRuntime.Clock(-5), Is.EqualTo("0초"), "음수 경과는 나올 수 없지만 나와도 읽을 수 있어야 한다");
        }

        /// <summary>
        /// 준비 중 화면의 글자가 <b>상황에 따라 달라진다</b> (S15P21B202-706).
        ///
        /// <para><b>멈춘 것과 느린 것은 다르다.</b> 예전에는 <c>"추가팩 로딩 중"</c> 이 최대 2분
        /// 동안 한 글자도 바뀌지 않아 방장이 둘을 구분할 수 없었다 — QA 에서 "진행 중인 건지
        /// 멈춘 건지 모르겠다" 가 그것이다.</para>
        ///
        /// <para>봐야 할 것은 둘이다. <b>시간이 흐르는가</b>(살아 있다), 그리고 <b>워커가
        /// 대답하는가</b>(어느 단계인가). 이 구분은 <b>로그</b>에 남긴다.</para>
        ///
        /// <para><b>화면은 남은 시간을 말한다</b> (S15P21B202-713). 예전 검사는 "첫 실행은 몇 분"
        /// 이라는 힌트(<c>"분"</c>)를 요구했는데, 그 말은 방장에게 속사정이고 얼마나 더 기다릴지는
        /// 알려 주지 않았다. 화면은 경과 대신 남은 시간을, 예상을 넘기면 숫자 없이
        /// "조금 더 걸리고 있어요" 를 말한다.</para>
        /// </summary>
        [Test] public void TheWaitingTextChangesWithWhatIsHappening()
        {
            string early = AiLocalRuntime.WarmupMessage(false, 3, 227);
            string later = AiLocalRuntime.WarmupMessage(false, 40, 227);
            Assert.That(later, Is.Not.EqualTo(early), "시간이 흘러도 로그 글자가 그대로다 — 멈춘 것처럼 보인다");

            string warming = AiLocalRuntime.WarmupMessage(true, 40, 227);
            Assert.That(warming, Is.Not.EqualTo(later),
                "대답이 오는 것과 오지 않는 것이 로그에서 같은 말로 나온다 — 어느 단계인지 알 수 없다");
            Assert.That(warming, Does.Contain(AiLocalRuntime.Clock(40)), "로그에서 경과 시간이 빠졌다");

            var waiting = new AiPrepSnapshot
            {
                Phase = AiPrepPhase.Start, WarmupElapsed = 40, WarmupExpected = 227, Elapsed = 40, Progress = .2f,
            };
            string shown = AiPrepProgress.Display(waiting);
            Assert.That(shown, Does.Contain(AiPrepProgress.FormatRemaining(187)),
                "화면이 남은 시간을 말하지 않는다 — 방장은 얼마나 더 기다릴지 모른다");
            Assert.That(shown, Does.Not.Contain(AiLocalRuntime.Clock(40)), "화면이 경과를 센다 — 남은 시간이 아니다");
            Assert.That(shown, Does.Not.Contain("첫 실행"), "속사정 힌트가 화면에 돌아왔다");

            var longer = waiting; longer.WarmupElapsed = 300;
            string late = AiPrepProgress.Display(longer);
            Assert.That(late, Is.Not.EqualTo(shown), "예상을 넘겨도 화면이 그대로다");
            Assert.That(late, Does.Contain(AiPrepProgress.Overrun));
            Assert.That(late, Does.Not.Contain("남음"), "예상을 넘겼는데 남은 시간을 댄다 — 그 숫자는 이미 틀렸다");
        }

        /// <summary>
        /// 데우는 시간이 <b>실측보다 넉넉하다</b> (S15P21B202-706).
        ///
        /// <para><b>2026-09-22 실측 3분 47초(227초).</b> 예전 마감은 120초 하나뿐이어서, 워커가
        /// 살아서 대답하며 모델을 데우고 있는데도 <b>절반쯤에서 잘렸다.</b> QA 에서 "추가팩 설치
        /// 완료인데 AI 준비 중에서 안 넘어간다" 로 보인 것이 이것이다.</para>
        ///
        /// <para>그 숫자를 검사에 적어 둔다 — 누군가 마감을 다시 줄이면 <b>여기서 먼저 걸린다.</b>
        /// 주석에만 적으면 줄일 때 아무도 읽지 않는다.</para>
        /// </summary>
        [Test] public void TheWarmupLimitExceedsTheMeasuredColdStart()
        {
            const float measured = 227f;   // 2026-09-22 실측, 첫 실행
            Assert.That(AiLocalRuntime.WarmupLimit, Is.GreaterThan(measured),
                "실측한 첫 실행 시간보다 마감이 짧다 — 되는 것을 실패로 자른다");
            Assert.That(AiLocalRuntime.SilenceLimit, Is.LessThan(AiLocalRuntime.WarmupLimit),
                "대답조차 없는 경우와 데우는 경우의 마감이 갈려 있지 않다 — 둘을 나눈 이유가 없어진다");
        }

        /// <summary>
        /// 받는 중에 <b>아무것도 안 올 때 그렇다고 말한다</b> (S15P21B202-706).
        ///
        /// <para>2.2GB 를 받는 동안 숫자가 멈추면 방장은 멈춘 건지 느린 건지 알 수 없다. 받는
        /// 고리는 60초 동안 한 바이트도 안 오면 끊는데, 그때까지 화면은 마지막 숫자 그대로
        /// 서 있었다 — <b>끊기고 나서야 실패를 안다.</b></para>
        /// </summary>
        [Test] public void TheDownloadTextSaysWhenNothingIsArriving()
        {
            string flowing = AiPackInstaller.DownloadMessage(.45f, 1024L * 1048576, 2260L * 1048576, 0f);
            Assert.That(flowing, Does.Contain("1024"), "받은 양이 빠졌다");
            Assert.That(flowing, Does.Contain("2260"), "전체 양이 빠졌다");

            string stalled = AiPackInstaller.DownloadMessage(.45f, 1024L * 1048576, 2260L * 1048576,
                                                            AiPackInstaller.StallNotice + 7f);
            Assert.That(stalled, Is.Not.EqualTo(flowing),
                "한 바이트도 안 오는데 화면은 흐를 때와 똑같다 — 끊기고 나서야 알게 된다");
            Assert.That(stalled, Does.Contain("12"), "몇 초째 조용한지가 빠졌다");
            Assert.That(AiPackInstaller.StallNotice, Is.LessThan(60f),
                "끊는 시각과 말하는 시각이 같다 — 미리 알려 주는 뜻이 없어진다");
        }

        /// <summary>
        /// 대답하는 워커는 <b>침묵 마감을 다시 민다</b> (S15P21B202-706).
        ///
        /// <para>값으로는 볼 수 없다 — 진짜 워커 프로세스가 있어야 한다. 그래서 배선을 글자로
        /// 못박는다. 이 줄이 빠지면 마감이 다시 하나가 되어, <b>살아서 데우고 있는 워커가
        /// 잘린다.</b></para>
        /// </summary>
        [Test] public void AnAnsweringWorkerPushesTheSilenceDeadline()
        {
            var watch = TestSource.Method(
                TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "IEnumerator Watch(int epoch)");

            Assert.That(watch, Does.Contain("if (answered) silentUntil ="),
                "대답이 와도 침묵 마감을 밀지 않는다 — 모델을 데우는 동안 잘린다");
            Assert.That(watch, Does.Contain("WarmupLimit"), "절대 상한이 없다 — 영영 안 되는 것과 오래 걸리는 것이 같아진다");
            Assert.That(watch, Does.Contain("WarmupMessage("), "기다리는 동안 화면이 그대로 서 있는다");
        }

        /// <summary>
        /// 준비 버튼이 <b>하나다</b> (S15P21B202-706).
        ///
        /// <para>예전에는 자리는 하나인데 단계마다 다른 버튼이 나왔다 — 다운로드 / 다시 준비.
        /// 어느 쪽이 나올지 알려면 방장이 지금 어느 단계인지를 먼저 알아야 했는데,
        /// <b>그것이 바로 화면에 없던 것</b>이다.</para>
        ///
        /// <para>글자(버튼 이름)가 아니라 <b>배선</b>을 본다 — <c>TestSource</c> 는 문자열을
        /// 지우고 보므로 이름으로는 단정할 수 없다.</para>
        ///
        /// <para><b>방 목록이 들어온 뒤에도 하나다</b> (S15P21B202-713). 이 자리의 켜기/끄기는 지웠고,
        /// AI 수는 방 목록의 "AI 동료 추가" 와 줄마다의 × 가 정한다. 그 버튼은 <b>수만 요청한다</b> —
        /// 받기나 다시 준비를 부르면 준비로 들어가는 입구가 둘이 된다. 추가를 누르면 설치 창이 뜨는
        /// 흐름으로 바꿀 때 이 검사도 함께, 일부러 고친다.</para>
        /// </summary>
        [Test] public void OneEntryPointCarriesTheWholePreparation()
        {
            var draw = TestSource.Method(
                TestSource.Read("Runtime/UI/AiRoomControls.cs"), "public static void Draw(");

            Assert.That(TestSource.Count(draw, "Prepare(runtime)"), Is.EqualTo(1),
                "준비로 들어가는 자리가 하나가 아니다 — 방장이 단계를 알아야 고를 수 있게 된다");
            Assert.That(draw, Does.Not.Contain("Installer.Begin()"),
                "받기를 화면이 직접 부른다 — 다시 갈라진다");
            Assert.That(draw, Does.Not.Contain("runtime.Retry()"),
                "다시 준비를 화면이 직접 부른다 — 다시 갈라진다");
            Assert.That(draw, Does.Not.Contain("TrySetRequestedCount("),
                "AI 수를 바꾸는 버튼이 방 목록 밖에 또 있다 — 두 곳이 서로 다른 수를 보내게 된다");

            string lobby = TestSource.Read("Runtime/UI/LobbyHud.cs");
            Assert.That(TestSource.Count(lobby, "AiHostSettings.TrySetRequestedCount("), Is.EqualTo(1),
                "방 화면에서 AI 수를 보내는 자리가 하나가 아니다");
            Assert.That(TestSource.Count(lobby, "Installer"), Is.Zero,
                "방 목록이 받기를 직접 부른다 — 준비로 들어가는 입구가 둘이 된다");
            Assert.That(TestSource.Count(lobby, "Retry()"), Is.Zero,
                "방 목록이 다시 준비를 직접 부른다 — 준비로 들어가는 입구가 둘이 된다");
            Assert.That(TestSource.Count(lobby, "AiRoomControls.Draw("), Is.EqualTo(1),
                "로비의 준비 버튼이 사라졌거나 둘이 됐다 — 팩이 없는 방장이 받을 길이 없다");
        }
    }
}
