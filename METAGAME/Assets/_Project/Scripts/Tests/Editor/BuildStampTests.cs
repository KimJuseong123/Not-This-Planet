using Metagame.EditorTools;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 빌드 스탬프가 **빌드된 것**을 가리키는지 (S15P21B202-398).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 스탬프는 "이 빌드가 어떤 코드인지" 를 말하려고
    /// 만든 것이다(`-269`). 그런데 첫 줄이 <b>HEAD</b> 를 찍고 있어서, 우리가 실제로 쓰는
    /// 방식 — 작업 트리에 커밋 안 된 문서 작업이 있어 <c>git switch</c> 를 못 할 때
    /// <c>git checkout &lt;커밋&gt; -- METAGAME/</c> 로 게임 코드만 덮는 방식 — 에서는
    /// <b>빌드된 코드와 아무 상관 없는 해시</b>가 박혔다.</para>
    ///
    /// <para>2026-09-10 qa2 빌드가 그랬고, 팀이 곧바로 "develop 에 61건 남았는데 옛날 걸로
    /// 빌드한 거 아니냐" 고 물었다. 빌드는 옛것이 아니었다 — <b>표시가 거짓말을 했다.</b>
    /// 해시는 그럴싸해서 사람이 믿는다. 그래서 <b>재현할 수 없으면 찍지 않는다.</b></para>
    /// </summary>
    public sealed class BuildStampTests
    {
        [Test]
        public void ACleanTreeStillStampsTheCommit()
        {
            // 회귀 검사. 고치려는 것은 더러운 경우이고, 깨끗한 경우는 지금 그대로여야 한다.
            Assert.AreEqual("20d8835",
                BuildStamp.SourceLabel("20d8835", "", "", "", ""),
                "깨끗한 작업 트리에서 해시가 사라졌다");
        }

        [Test]
        public void OverwritingWithAnotherCommitStampsThatCommitNotHead()
        {
            // `git checkout <커밋> -- METAGAME/` 는 **인덱스까지** 그 커밋으로 바꾼다.
            // 그래서 "작업 트리 ↔ 인덱스" 는 같고 "인덱스 ↔ HEAD" 만 다르다.
            // 이 상태에서 HEAD 해시를 찍으면 그것이 바로 2026-09-10 의 거짓말이다.
            //
            // **찾았으면 그 커밋을 찍는다.** 그 빌드는 재현할 수 있다 —
            // `git checkout 0fede8e -- METAGAME/` 이면 같은 것이 나온다. 완료 조건의
            // "라벨 없이 첫 줄만으로 기준을 안다" 가 이것이다.
            var got = BuildStamp.SourceLabel(
                "20d8835",
                worktree: "",
                staged: "Assets/_Project/Scripts/Runtime/A.cs\nAssets/_Project/Scripts/Runtime/B.cs",
                untracked: "",
                overwrittenFrom: "0fede8e");

            Assert.That(got, Does.Not.Contain("20d8835"),
                "빌드된 것이 HEAD 가 아닌데 HEAD 해시를 찍는다");
            Assert.That(got, Does.Contain("0fede8e"),
                "덮어쓴 커밋을 찾았는데 찍지 않는다 — 라벨 없이는 기준을 알 수 없게 된다");
            Assert.That(got, Does.Not.Contain("재현불가"),
                "커밋으로 재현할 수 있는데 재현 불가라고 적는다");
        }

        [Test]
        public void AStagedChangeThatMatchesNoCommitIsNotCalledAnOverwrite()
        {
            // 평소에 `git add` 만 해 둔 상태도 "인덱스 ↔ HEAD 다름" 이다. 그것을
            // "다른 커밋을 덮었다" 로 단정하면 이 표시가 **또** 부정확해진다
            // (`!384` 리뷰가 짚었다). 찾지 못했으면 **관찰한 사실만** 적는다.
            var got = BuildStamp.SourceLabel(
                "20d8835",
                worktree: "",
                staged: "Assets/_Project/Scripts/Runtime/A.cs",
                untracked: "",
                overwrittenFrom: "");

            Assert.That(got, Does.Not.Contain("20d8835"),
                "인덱스가 HEAD 와 다른데 HEAD 해시를 찍는다");
            Assert.That(got, Does.Contain("재현불가"), got);
            Assert.That(got, Does.Not.Contain("덮"),
                "어느 커밋인지 모르면서 '덮었다' 고 단정한다");
            Assert.That(got, Does.Contain("1"), "몇 개가 다른지 적지 않는다");
        }

        [Test]
        public void HandEditsAreToldApartFromAnOverwrite()
        {
            // 손으로 고친 것은 **어느 커밋에도 없다.** 덮어쓴 것보다 강한 이야기이므로
            // 둘이 겹치면 이쪽을 말한다.
            var edited = BuildStamp.SourceLabel("20d8835", "Assets/_Project/Scripts/Runtime/A.cs", "", "", "");
            Assert.That(edited, Does.Contain("수정"), edited);
            Assert.That(edited, Does.Not.Contain("덮"), edited);

            // 겹치면 **손으로 고친 쪽**이 이긴다. 덮어쓴 커밋을 찾았더라도 그 위에 손을
            // 댔으면 그 커밋으로 재현되지 않는다 — 해시를 찍으면 다시 거짓말이 된다.
            var both = BuildStamp.SourceLabel("20d8835", "Assets/A.cs", "Assets/B.cs", "", "0fede8e");
            Assert.That(both, Does.Contain("수정"),
                "덮어쓴 것과 손으로 고친 것이 겹칠 때 약한 쪽을 말한다");
            Assert.That(both, Does.Not.Contain("0fede8e"),
                "손댄 코드인데 덮어쓴 커밋의 해시를 찍는다 — 재현되지 않는다");
        }

        [Test]
        public void UntrackedFilesCountAsNotReproducible()
        {
            // 추적되지 않는 파일도 **빌드에 들어간다.** `git diff` 에는 안 나오므로
            // 따로 세지 않으면 그 빌드가 재현 가능한 것처럼 보인다.
            var got = BuildStamp.SourceLabel("20d8835", "", "", "Assets/_Project/Art/New.png", "");

            Assert.That(got, Does.Not.Contain("20d8835"),
                "추적되지 않는 파일이 섞였는데 해시를 찍는다");
            Assert.That(got, Does.Contain("수정"), got);
        }

        [Test]
        public void WithoutGitItSaysSoInsteadOfGuessing()
        {
            // git 이 없거나 저장소가 아니면 `Git()` 이 빈 문자열을 준다. 그때 빈 해시를
            // 찍으면 첫 줄이 `2026-09-10 14:55 ·  · develop` 처럼 깨진다.
            Assert.AreEqual("커밋 모름", BuildStamp.SourceLabel("", "", "", "", ""));
        }

        [Test]
        public void CountingIgnoresBlankLines()
        {
            // git 출력은 끝에 줄바꿈이 붙는다. 그것을 세면 개수가 하나씩 늘어난다.
            Assert.AreEqual(0, BuildStamp.CountLines(""));
            Assert.AreEqual(0, BuildStamp.CountLines("\n"));
            Assert.AreEqual(1, BuildStamp.CountLines("a.cs\n"));
            Assert.AreEqual(2, BuildStamp.CountLines("a.cs\nb.cs\n"));
        }

        [Test]
        public void ChangesOutsideTheUnityProjectDoNotClaimIrreproducible()
        {
            // 예전에는 저장소 **전체**를 보고 `+커밋안한변경` 을 붙였다. 작업 트리에
            // 커밋 안 된 `docs/` 96개가 이어지고 있어서 **재현 가능한 빌드에도 매번**
            // 그 표시가 붙었고, 늘 붙는 표시는 아무도 안 읽는다.
            //
            // 여기서 보는 세 값은 모두 `-- .`(= `METAGAME/`) 로 좁혀져 있으므로,
            // 프로젝트 밖 변경은 애초에 들어오지 않는다.
            var src = TestSource.Read("Editor/BuildStamp.cs");

            foreach (var scoped in new[] { "diff --name-only -- .",
                                           "diff --cached --name-only -- .",
                                           "ls-files --others --exclude-standard -- ." })
            {
                Assert.That(src, Does.Contain(scoped),
                    "기준 판정이 METAGAME/ 로 좁혀져 있지 않다: " + scoped);
            }

            // 주석에는 그 문구가 남아 있다(왜 바꿨는지 적어 두었다). **붙이는 코드**가
            // 사라졌는지를 본다 — 그것이 이 검사가 지키려는 것이다.
            Assert.That(src, Does.Not.Contain("Append(\" · +커밋안한변경\")"),
                "두 경우를 한 문구로 뭉쳐 붙이던 코드가 남아 있다");

            // 덮어쓴 커밋을 찾는 일은 **손으로 고친 것이 없을 때만** 한다 — 손댄 코드는
            // 어느 커밋으로도 재현되지 않으므로 찾아 봐야 의미가 없고, git 을 최대
            // 120번 부르는 비용만 든다.
            Assert.That(src, Does.Contain("SearchDepth"),
                "덮어쓴 커밋을 찾는 범위에 상한이 없다 — 저장소 역사 전체를 훑게 된다");
        }

        [Test]
        public void AFailedGitCallIsNeverReadAsAMatch()
        {
            // **빈 출력을 "같다" 로 읽으면 안 된다** (`!384` 2차 리뷰). `Git()` 은 실패해도
            // 빈 문자열을 주므로, `diff --cached --name-only` 의 빈 출력으로 판정하면
            // git 호출이 한 번만 삐끗해도 **아무 상관 없는 커밋을 "덮어씀" 으로 찍는다** —
            // 이 이슈가 고치려던 거짓 해시가 다른 모양으로 돌아온다.
            //
            // 그래서 `--quiet` 의 **종료 코드**로 가른다: 0 = 같다 · 1 = 다르다 ·
            // 그 밖 = 물어보지 못했다(찾기를 포기한다).
            var src = TestSource.Read("Editor/BuildStamp.cs");
            var search = src.Substring(src.IndexOf("static string OverwrittenFrom"));

            Assert.That(search, Does.Contain("diff --cached --quiet"),
                "종료 코드가 아니라 출력으로 판정한다 — 실패가 '일치' 로 읽힌다");
            Assert.That(search, Does.Not.Contain("diff --cached --name-only"),
                "빈 출력으로 판정하던 코드가 남아 있다");
            Assert.That(search, Does.Contain("rc != 1"),
                "0 도 1 도 아닌 종료 코드(= 물어보지 못했다)를 가르지 않는다");

            // `Git()` 이 실패를 삼키는 것 자체는 그대로 둔다 — 스탬프 하나 때문에 빌드를
            // 막지 않는다는 원래 결정이다. 대신 **뜻을 싣는 자리는 `Run` 을 쓴다.**
            Assert.That(src, Does.Contain("static string Run(string arguments, out int exit)"),
                "종료 코드를 볼 수 있는 통로가 없다");
        }
    }
}
