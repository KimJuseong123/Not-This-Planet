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
        public void TheEditorStillWritesNothing()
        {
            // 라벨 검사가 **저장소 오염 방지도 겸하고 있었다.** 에디터의
            // `Application.dataPath` 는 `METAGAME/Assets` 라, 그 부모에 로그 폴더를
            // 만들면 리포에 `METAGAME/logs/` 가 생긴다. 라벨 검사를 없앤 커밋에서
            // 실제로 `RunLogTests` 가 빨개졌고 CI 가 그것을 잡았다.
            var src = TestSource.Read("Runtime/Core/RunLog.cs");

            Assert.That(src, Does.Contain("Application.isEditor"),
                "에디터를 막지 않는다 — 저장소 안에 로그 폴더가 생긴다");
            Assert.That(RunLog.Path, Is.Empty,
                "이 검사가 도는 에디터에서 이미 파일이 열렸다");
        }

        [Test]
        public void TheCapIsMeasuredInBytesNotCharacters()
        {
            // **한글은 UTF-8 에서 글자당 3바이트다.** 글자 수로 세면 8 MB 상한이
            // 실제로는 24 MB 가 되어 서버(8 MB)와 nginx(9 MB)가 거절한다 — 그러면
            // 정작 가장 큰, 가장 알고 싶은 로그만 못 올라온다.
            var src = TestSource.Read("Runtime/Core/RunLog.cs");

            Assert.That(src, Does.Contain("BaseStream.Position"),
                "실제 파일 크기가 아니라 다른 것으로 상한을 잰다");
            Assert.That(src, Does.Not.Contain("message.Length + 5"),
                "글자 수로 바이트 상한을 잰다 — 한글에서 세 배가 된다");
        }

        [Test]
        public void ReachingTheCapKeepsTheEndOfTheRun()
        {
            // 사람이 신고하는 문제는 보통 **마지막에** 일어난다(튕김·멈춤·크래시).
            // 상한에 닿았다고 거기서 멈추면 앞부분만 남아 정작 필요한 구간이 사라진다.
            // 머리도 남긴다 — 빌드·역할·커밋이 거기 있어야 무엇의 로그인지 안다.
            var src = TestSource.Read("Runtime/Core/RunLog.cs");

            Assert.That(src, Does.Contain("Compact"),
                "상한에 닿으면 그냥 멈춘다 — 마지막 구간이 사라진다");
            Assert.That(src, Does.Contain("HeadBytes"),
                "머리를 남기지 않는다 — 무엇의 로그인지 알 수 없다");
            Assert.That(RunLog.HeadBytes, Is.LessThan(RunLog.MaxBytes / 2),
                "머리가 너무 커서 꼬리가 밀린다");
        }

        [Test]
        public void SendingIsMarkedByRenamingNotBySideCarFiles()
        {
            // 표시 파일을 따로 쓰는 방식은 그 쓰기가 실패했을 때 **같은 로그를 매번 다시
            // 올린다.** 서버는 받을 때마다 새 이름을 만들어 쌓으므로 덮어쓰이지도 않고,
            // 사용자별 빈도 상한까지 그것으로 채워진다.
            var src = TestSource.Read("Runtime/Core/RunLogUpload.cs");

            Assert.That(src, Does.Contain("File.Move(path, path + SentSuffix)"),
                "이름을 바꿔 표시하지 않는다");

            // 이름이 바뀐 것도 오래되면 정리돼야 한다 — `*.log` 만 세면 영원히 쌓인다.
            var log = TestSource.Read("Runtime/Core/RunLog.cs");
            Assert.That(log, Does.Contain("RunLogUpload.SentSuffix"),
                "보낸 파일이 정리 대상에서 빠진다 — 계속 쌓인다");
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

                // 상한 **이하**다. 정확히 40이 아니다 — 줄 경계까지 앞을 버리기
                // 때문이다(아래 검사 참고). 40을 넘으면 서버·nginx 가 거절한다.
                Assert.That(Encoding.UTF8.GetBytes(got).Length, Is.LessThanOrEqualTo(40),
                    "상한을 넘겨 가져왔다 — 서버가 거절하는 크기가 된다");
                Assert.That(got, Does.Contain("끝이 중요하다"),
                    "뒤쪽이 아니라 앞쪽을 가져왔다 — 문제는 보통 끝에 있다");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void TheTailStartsAtALineBoundarySoCharactersAreNotCutInHalf()
        {
            // **한글은 UTF-8 에서 글자당 3바이트다.** 바이트 수로 아무 데나 자르면
            // 첫 글자의 가운데가 잘려 받은 로그의 첫 줄이 깨진 채 서버에 남는다.
            // 우리가 그 파일을 여는 때는 이미 문제가 난 뒤라 다시 받을 수도 없다.
            var path = Path.Combine(Path.GetTempPath(), "ntp-runlog-boundary.log");
            try
            {
                // 각 줄이 3바이트 글자 넷(12바이트) + 줄바꿈 = 13바이트.
                File.WriteAllText(path, "가나다라\n마바사아\n자차카타\n");

                // 18바이트를 달라고 하면 경계가 글자(사) 가운데에 떨어진다.
                var raw = RunLogUpload.Tail(path, 18);
                var got = Encoding.UTF8.GetString(raw);

                Assert.That(got, Does.StartWith("자"),
                    "줄 중간에서 시작했다 — 첫 글자가 깨진다");
                Assert.That(got, Does.Not.Contain(BrokenChar),
                    "글자 가운데가 잘렸다 (U+FFFD 가 나왔다)");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>디코더가 깨진 바이트 자리에 넣는 글자.</summary>
        const string BrokenChar = "\uFFFD";

        [Test]
        public void ALineWithoutAnyNewlineIsNotThrownAway()
        {
            // 줄바꿈이 하나도 없으면 잘라 낼 자리가 없다. 그때 빈 것을 돌려주면
            // **로그가 통째로 사라진다** — 깨진 글자 하나보다 그쪽이 훨씬 나쁘다.
            var path = Path.Combine(Path.GetTempPath(), "ntp-runlog-noline.log");
            try
            {
                File.WriteAllText(path, new string('x', 200));
                var got = RunLogUpload.Tail(path, 40);
                Assert.AreEqual(40, got.Length, "줄바꿈이 없다고 로그를 버렸다");
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
        public void PlayStopsTheUploadInsteadOfCompetingWithIt()
        {
            // 최대 3개 × 최대 8 MB 를 요청당 15초로 보내므로, 느린 회선에서는 로비에서
            // 시작한 전송이 **1분 가까이** 이어질 수 있다. 그 사이 매칭이 시작되면
            // "플레이 중에는 네트워킹을 쓰지 않는다" 를 실제로는 못 지킨다.
            var upload = TestSource.Read("Runtime/Core/RunLogUpload.cs");
            var session = TestSource.Read("Runtime/Network/NetworkSession.cs");

            Assert.That(upload, Does.Contain("req.Abort()"),
                "나가 있는 요청을 끊을 방법이 없다");
            Assert.That(session, Does.Contain("RunLogUpload.StopNow()"),
                "게임이 시작돼도 전송이 계속된다");

            // 세션이 사라질 때도 멈춰야 한다 — 안 그러면 "돌고 있다" 표시가 선 채로
            // 남아 **그 프로세스가 끝날 때까지 다시는 안 돈다**.
            var afterDestroy = session.Substring(session.IndexOf("void OnDestroy"));
            Assert.That(afterDestroy, Does.Contain("RunLogUpload.StopNow()"),
                "세션이 사라질 때 정리하지 않는다 — 다음 로그인에서 재시도되지 않는다");
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
