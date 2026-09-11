using System.IO;
using System.Text;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 플레이 로그 업로드 (S15P21B202-443).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이 기능은 배포판에서만 돌고 우리가 직접 볼
    /// 일이 거의 없다 — 조용히 안 되면 <b>아무도 모른 채 로그가 안 모인다.</b> 그러면
    /// 정작 버그 신고를 받았을 때 "왜 로그가 없지" 부터 시작하게 된다.</para>
    /// </summary>
    public sealed class RunLogUploadTests
    {
        [Test]
        public void TheLogIsWrittenInEveryBuildNotJustExperiments()
        {
            // 전에는 `if (!BuildInfo.IsExperiment) return;` 이 첫 줄이었다. 그러면
            // **배포판에서 문제가 나도 로그가 아예 없다** — 이 기능 전체가 무의미해진다.
            var src = TestSource.Read("Runtime/Core/RunLog.cs");

            Assert.That(src, Does.Not.Contain("if (!BuildInfo.IsExperiment) return;"),
                "RunLog 가 실험 빌드에서만 돈다 — 배포판 로그가 안 남는다");
            Assert.That(src, Does.Contain("MaxBytes"),
                "상한이 없다 — 오래 켜 두는 사람의 로그가 무한히 커진다");
        }

        [Test]
        public void TheTailIsKeptWhenTheFileIsTooBig()
        {
            // 상한을 넘는 파일은 **뒤쪽**이 남아야 한다. 문제는 보통 끝에 있다.
            var path = Path.Combine(Path.GetTempPath(), "ntp-runlog-tail.log");
            try
            {
                File.WriteAllText(path, "앞부분은 버린다\n" + new string('x', 500) + "\n끝이 중요하다");

                var got = Encoding.UTF8.GetString(RunLogUpload.Tail(path, 40));

                Assert.AreEqual(40, Encoding.UTF8.GetBytes(got).Length,
                    "상한만큼 가져오지 않았다");
                Assert.That(got, Does.Contain("끝이 중요하다"),
                    "뒤쪽이 아니라 앞쪽을 가져왔다 — 문제는 보통 끝에 있다");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ASmallFileComesBackWhole()
        {
            var path = Path.Combine(Path.GetTempPath(), "ntp-runlog-small.log");
            try
            {
                File.WriteAllText(path, "짧은 로그");
                var got = Encoding.UTF8.GetString(RunLogUpload.Tail(path, 1024));
                Assert.AreEqual("짧은 로그", got, "상한보다 작은데 잘렸다");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void HeaderValuesCannotCarryNewlines()
        {
            // 빌드 스탬프를 **헤더로** 보낸다. 줄바꿈이 들어가면 헤더가 쪼개져서
            // 우리가 의도하지 않은 헤더를 만들 수 있다(HTTP 응답/요청 분할).
            var dirty = "2026-09-11 15:11\n· edc0831\r\nX-Injected: yes";
            var clean = RunLogUpload.Sanitize(dirty);

            Assert.That(clean, Does.Not.Contain("\n"));
            Assert.That(clean, Does.Not.Contain("\r"));
            Assert.That(clean, Does.Contain("edc0831"), "쓸모 있는 부분까지 지웠다");
        }

        [Test]
        public void TheUploadCapMatchesWhatTheLoggerWrites()
        {
            // 둘이 어긋나면 **로그가 매번 잘려서** 올라가거나, 서버가 거절하는 크기를
            // 보내게 된다. 한쪽만 고치는 일이 생기므로 검사로 묶는다.
            Assert.AreEqual(RunLog.MaxBytes, RunLogUpload.MaxUploadBytes,
                "쓰는 쪽 상한과 보내는 쪽 상한이 다르다");
        }

        [Test]
        public void ItDoesNotUploadTheRunThatIsStillBeingWritten()
        {
            // 지금 실행의 파일은 계속 쓰이는 중이라 보내 봐야 반쪽이다. 다음 실행에서
            // 온전한 것이 간다. 소스로 확인한다 — 파일 시스템 상태를 만들기 어렵다.
            var src = TestSource.Read("Runtime/Core/RunLogUpload.cs");

            Assert.That(src, Does.Contain("RunLog.Path"),
                "이번 실행의 파일을 가려내지 않는다");
            Assert.That(src, Does.Contain("SentSuffix"),
                "보낸 것을 표시하지 않는다 — 같은 로그를 매번 다시 보낸다");
        }

        [Test]
        public void TheSweepRunsAfterLoginNotAtQuit()
        {
            // 종료 훅에서 보내면 ① 종료가 지연되고(완료 조건이 금지한다) ② 크래시로
            // 죽은 판은 아예 못 받는다 — 그것이 정작 가장 알고 싶은 경우다.
            var session = TestSource.Read("Runtime/Network/NetworkSession.cs");

            Assert.That(session, Does.Contain("RunLogUpload.SweepOnce"),
                "로그인 뒤에 로그를 보내지 않는다");

            var upload = TestSource.Read("Runtime/Core/RunLogUpload.cs");
            Assert.That(upload, Does.Not.Contain("OnApplicationQuit"),
                "종료 훅에서 보낸다 — 종료가 지연되고 크래시 판을 못 받는다");
            Assert.That(upload, Does.Not.Contain("Application.quitting"),
                "종료 훅에서 보낸다 — 종료가 지연되고 크래시 판을 못 받는다");
        }

        [Test]
        public void TheTokenGoesInAHeaderNotTheQuery()
        {
            // 쿼리에 실으면 nginx 접근 로그에 그대로 남는다. 그 토큰이면 그 사람
            // 행세를 할 수 있다(`NetworkSession.AccessToken` 주석과 같은 이유).
            var src = TestSource.Read("Runtime/Core/RunLogUpload.cs");

            Assert.That(src, Does.Contain("SetRequestHeader(\"Authorization\""),
                "토큰을 헤더로 보내지 않는다");
            Assert.That(src, Does.Not.Contain("?token="),
                "토큰이 쿼리에 실린다 — 접근 로그에 남는다");
        }
    }
}
