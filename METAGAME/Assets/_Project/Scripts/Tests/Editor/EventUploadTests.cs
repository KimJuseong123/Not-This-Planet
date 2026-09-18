using System.Collections.Generic;
using System.IO;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 이벤트가 <b>서버까지 간다</b> (S15P21B202-607).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> <c>PlayEvents</c> 가 NDJSON 을 잘 쓰고 있었는데
    /// 보내는 쪽(<see cref="RunLogUpload"/>)은 <c>*.log</c> 만 모았다. 그래서 <c>-498</c>
    /// 이 심은 이벤트가 <b>한 건도 서버에 도착하지 않았다</b> — 쌓기만 하고 플레이어
    /// 기기에 남았다.</para>
    ///
    /// <para>그리고 보내는 시점이 <b>로그인 직후 한 번</b>뿐이라, "한 번 켜서 한 판 하고
    /// 끄는 사람" 의 기록은 영영 오지 않았다. 데모에서는 그쪽이 다수다.</para>
    /// </summary>
    public sealed class EventUploadTests
    {
        static string Temp(string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "metagame-upload-" + name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// 확장자는 <b>쓰는 쪽과 보내는 쪽이 같은 상수</b>를 봐야 한다. 양쪽에 글자를
        /// 적어 두면 한쪽만 바뀌는 날 수집이 조용히 멈춘다.
        /// </summary>
        [Test]
        public void TheExtensionIsOneConstantBothSidesRead()
        {
            Assert.That(PlayEvents.Extension, Is.EqualTo(".ndjson"));

            var naming = TestSource.Method(TestSource.Read("Runtime/Core/PlayEvents.cs"),
                                          "public static string FileNameFor(DateTime when)");
            Assert.That(naming, Does.Contain("Extension"),
                        "이름을 짓는 쪽이 공용 상수를 안 쓴다 — 양쪽이 어긋날 수 있다");

            var sender = TestSource.Method(TestSource.Read("Runtime/Core/RunLogUpload.cs"),
                                           "public static List<string> Pending()");
            Assert.That(sender, Does.Contain("PlayEvents.Extension"),
                        "보내는 쪽이 확장자를 따로 적었다 — 한쪽만 바뀌면 수집이 멈춘다");
        }

        /// <summary>
        /// <b>지금 쓰고 있는 파일은 안 보낸다.</b> 반쪽짜리가 올라가면 <c>seq</c> 가
        /// 끊긴 것처럼 보이고, 그 판은 영영 반쪽으로 남는다(보낸 표시가 붙으므로).
        /// </summary>
        [Test]
        public void TheFileStillBeingWrittenIsNotSent()
        {
            string dir = Temp("current");
            string older = Path.Combine(dir, "20260101-000000.ndjson");
            string current = Path.Combine(dir, "20260101-010000.ndjson");
            File.WriteAllText(older, "{}");
            File.WriteAllText(current, "{}");

            var pending = RunLogUpload.PendingIn(dir, "*" + PlayEvents.Extension, current);

            Assert.That(pending, Does.Contain(older), "지난 판을 안 보낸다");
            Assert.That(pending, Does.Not.Contain(current), "쓰고 있는 파일을 보낸다 — 반쪽이 올라간다");
        }

        /// <summary>보낸 것은 다시 보내지 않는다 — 이름이 바뀌거나 표시 파일이 있으면 뺀다.</summary>
        [Test]
        public void AlreadySentFilesAreNotSentAgain()
        {
            string dir = Temp("sent");
            string a = Path.Combine(dir, "20260101-000000.ndjson");
            string b = Path.Combine(dir, "20260101-000001.ndjson");
            File.WriteAllText(a, "{}");
            File.WriteAllText(b, "{}");
            File.WriteAllText(b + RunLogUpload.SentSuffix, "");

            var pending = RunLogUpload.PendingIn(dir, "*" + PlayEvents.Extension, null);

            Assert.That(pending, Does.Contain(a));
            Assert.That(pending, Does.Not.Contain(b), "이미 보낸 것을 또 보낸다");
        }

        /// <summary>
        /// <b>한쪽이 굶지 않는다.</b> 로그가 밀려 있으면 이벤트가 영영 안 가고, 반대도
        /// 마찬가지다. 상한은 <b>합계</b>로 지킨다 — 서버가 사용자당 6건/창이다.
        /// </summary>
        [Test]
        public void NeitherKindStarvesTheOther()
        {
            var logs = new List<string> { "a.log", "b.log", "c.log", "d.log" };
            var events = new List<string> { "x.ndjson", "y.ndjson" };

            var picked = RunLogUpload.Interleave(logs, events, RunLogUpload.MaxPerRun);

            Assert.That(picked.Count, Is.EqualTo(RunLogUpload.MaxPerRun), "상한을 안 지킨다");
            Assert.That(picked, Does.Contain("x.ndjson"),
                        "로그가 밀려 있으면 이벤트가 한 건도 안 간다 — 그게 이 이슈가 고친 것이다");
            Assert.That(picked, Does.Contain("a.log"), "로그가 통째로 굶는다");

            // 한쪽이 비어도 다른 쪽으로 상한을 채운다.
            Assert.That(RunLogUpload.Interleave(logs, new List<string>(), 3).Count, Is.EqualTo(3));
            Assert.That(RunLogUpload.Interleave(null, events, 3).Count, Is.EqualTo(2));
            Assert.That(RunLogUpload.Interleave(null, null, 3), Is.Empty);
        }

        /// <summary>
        /// <b>판이 끝나며 닫은 파일이 실제로 목록에 든다</b> (<c>!514</c> AI 리뷰).
        ///
        /// <para><see cref="PlayEvents.End"/> 는 <c>Path</c> 를 비우지 않는다 — 그 값을
        /// 그대로 "쓰는 중" 으로 보고 빼면 <b>판 끝 전송이 아무것도 안 보낸다.</b>
        /// 이 이슈가 만든 길이 통째로 헛도는 것이라, 값으로 잡는다.</para>
        /// </summary>
        [Test]
        public void TheFileFromTheMatchThatJustEndedIsSent()
        {
            string dir = Temp("justclosed");
            var saved = PlayEvents.FolderResolver;
            try
            {
                PlayEvents.FolderResolver = () => dir;
                PlayEvents.Begin(new Dictionary<string, object> { ["build"] = "test" });
                string written = PlayEvents.Path;
                Assert.That(written, Is.Not.Empty, "이벤트 파일이 열리지 않아 이 검사가 아무것도 못 본다");

                PlayEvents.End(null);

                Assert.That(RunLogUpload.Pending(), Does.Contain(written),
                            "방금 닫은 파일을 '쓰는 중' 으로 보고 뺐다 — 판 끝 전송이 아무것도 안 보낸다");
            }
            finally
            {
                PlayEvents.FolderResolver = saved;
            }
        }

        /// <summary>
        /// <b>이번 실행이 아직 파일을 열지 않았어도 찾는다</b> (<c>!514</c> AI 리뷰).
        ///
        /// <para>로그인 직후 쓸어 담기가 그 상태다 — 폴더를 <c>PlayEvents.Path</c> 에서
        /// 역산하면 그때 빈 값이라 <b>events 를 통째로 건너뛴다.</b> 그것이 이 기능의
        /// 주 경로이므로 수집이 조용히 0건이 된다.</para>
        /// </summary>
        [Test]
        public void EventsAreFoundBeforeThisRunOpensAnyFile()
        {
            string dir = Temp("beforeopen");
            string older = Path.Combine(dir, "20260101-000000.ndjson");
            File.WriteAllText(older, "{}");

            var saved = PlayEvents.FolderResolver;
            try
            {
                PlayEvents.FolderResolver = () => dir;
                Assert.That(RunLogUpload.Pending(), Does.Contain(older),
                            "이번 실행이 파일을 열기 전에는 events 를 아예 안 본다 — 로그인 직후 쓸어 담기가 그 상태다");
            }
            finally
            {
                PlayEvents.FolderResolver = saved;
            }
        }

        /// <summary>
        /// 모으는 쪽이 <b>두 폴더를 다 본다</b>. 값으로는 잡히지 않는 배선이라 여기서 본다.
        /// </summary>
        [Test]
        public void BothFoldersAreCollected()
        {
            var pending = TestSource.Method(TestSource.Read("Runtime/Core/RunLogUpload.cs"),
                                            "public static List<string> Pending()");

            Assert.That(pending, Does.Contain("RunLog.Path"), "평문 로그를 안 모은다");
            Assert.That(pending, Does.Contain("EventsFolder"), "이벤트를 안 모은다 — 기기에만 쌓인다");
            Assert.That(pending, Does.Contain("Interleave"), "한쪽이 굶는 채로 모은다");

            // **폴더를 Path 에서 역산하지 않는다** (`!514` AI 리뷰). 그러면 이번 실행이
            // 파일을 열기 전에는 events 폴더가 null 이 되어 통째로 건너뛴다.
            Assert.That(pending, Does.Not.Contain("FolderOf(PlayEvents.Path)"),
                        "이벤트 폴더를 Path 에서 역산한다 — 파일을 열기 전에는 아무것도 안 본다");
        }

        /// <summary>
        /// <b>판이 끝나면 한 번 더 보낸다.</b> 로그인 때만 보내면 한 번 켜고 끄는 사람의
        /// 기록이 영영 안 온다. 그리고 그 요청을 받는 쪽은 <b>내려갈 때 놓아야</b> 한다 —
        /// 죽은 오브젝트를 붙잡으면 다음 세션이 그 자리에 못 들어온다.
        /// </summary>
        [Test]
        public void TheEndOfAMatchAsksForOneMoreSweep()
        {
            var observer = TestSource.Read("Runtime/Bootstrap/PlayObserver.cs");
            var end = TestSource.Method(observer, "public void End(string result, bool sweep");
            Assert.That(end, Does.Contain("RunLogUpload.Request"),
                        "판을 닫으면서 보내지 않는다 — 한 번 켜고 끄는 사람의 기록이 안 온다");

            // **앱 종료 경로는 빼 둔다** (`!514` AI 리뷰). 거기서 시작한 전송은 끝나지
            // 못한다 — 그 결정을 `if` 로 지킨다. 자세한 단정은 `RunLogUploadTests` 에 있다.
            Assert.That(end, Does.Contain("if (sweep)"),
                        "종료 경로를 가릴 스위치가 없다 — 종료 훅에서도 보내게 된다");

            var session = TestSource.Read("Runtime/Network/NetworkSession.cs");
            Assert.That(session, Does.Contain("RunLogUpload.SweepRequested = SweepNow"),
                        "요청을 받을 사람이 없다");
            var destroy = TestSource.Method(session, "void OnDestroy()");
            Assert.That(destroy, Does.Contain("SweepRequested = null"),
                        "내려가면서 대리자를 놓지 않는다 — 죽은 오브젝트를 붙잡는다");

            // **플레이 중에는 보내지 않는다**(-443) 는 그대로여야 한다.
            Assert.That(session, Does.Contain("RunLogUpload.StopNow"),
                        "플레이가 시작될 때 전송을 멈추던 것이 사라졌다");
        }

        /// <summary>듣는 쪽이 없어도, 던져도 게임이 멈추지 않는다 — 보내는 일이 막을 이유는 없다.</summary>
        [Test]
        public void ARequestWithNobodyListeningIsHarmless()
        {
            RunLogUpload.SweepRequested = null;
            Assert.DoesNotThrow(RunLogUpload.Request, "듣는 쪽이 없는데 던진다");

            int called = 0;
            RunLogUpload.SweepRequested = () => { called++; throw new IOException("세션이 내려가는 중"); };
            Assert.DoesNotThrow(RunLogUpload.Request, "듣는 쪽이 던졌는데 그대로 올라온다");
            Assert.That(called, Is.EqualTo(1));

            RunLogUpload.SweepRequested = null;
        }
    }
}
