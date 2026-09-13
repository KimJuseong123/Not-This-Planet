using System;
using System.Globalization;
using System.Threading;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 실행별 로그 파일의 <b>이름 규칙과 켜지는 조건</b>을 검증한다 (S15P21B202-380).
    ///
    /// <para>파일을 실제로 쓰는 것은 여기서 돌리지 않는다 — 에디터에서는 <c>RunLog</c>
    /// 가 아무 일도 하지 않고, 그것이 <b>의도된 동작</b>이다(에디터에서 돌면
    /// <c>Application.dataPath</c> 의 부모가 저장소 안이라 리포에 로그를 쓴다).
    /// 대신 이름 규칙과 그 안전 조건을 잡는다.</para>
    /// </summary>
    public sealed class RunLogTests
    {
        /// <summary>
        /// <b>이름이 시각순으로 정렬돼야 한다.</b> 여러 사람이 보낸 것을 한 폴더에 모아
        /// 놓고 보게 되고, 오래된 것을 지우는 것도 이름 정렬에 기댄다.
        /// </summary>
        [Test]
        public void FileNamesSortByTime()
        {
            var earlier = RunLog.FileNameFor(new DateTime(2026, 9, 10, 9, 5, 3));
            var later = RunLog.FileNameFor(new DateTime(2026, 9, 10, 11, 42, 33));

            Assert.That(earlier, Is.EqualTo("20260910-090503.log"));
            Assert.That(later, Is.EqualTo("20260910-114233.log"));
            Assert.That(string.CompareOrdinal(earlier, later), Is.LessThan(0),
                "문자열 정렬이 시각 정렬과 같아야 한다 — 오래된 것 정리가 그것에 기댄다");
        }

        /// <summary>날짜가 바뀌어도 정렬이 유지된다 — 자릿수를 채워야 성립한다.</summary>
        [Test]
        public void FileNamesStayPaddedAcrossDays()
        {
            var lastYear = RunLog.FileNameFor(new DateTime(2026, 1, 2, 3, 4, 5));

            Assert.That(lastYear, Is.EqualTo("20260102-030405.log"));
            Assert.That(string.CompareOrdinal(lastYear, RunLog.FileNameFor(new DateTime(2026, 9, 10, 0, 0, 0))),
                Is.LessThan(0));
        }

        /// <summary>
        /// <b>이름이 시스템 로케일을 따르지 않는다.</b> 달력·숫자 표기가 다른 환경에서
        /// 이름이 달라지면 정렬이 깨지고 파서·정리 로직이 같이 어긋난다.
        /// </summary>
        [Test]
        public void FileNamesDoNotFollowTheSystemLocale()
        {
            var before = Thread.CurrentThread.CurrentCulture;
            try
            {
                // th-TH 는 기본 달력이 불교력이라 연도가 2569 로 나올 수 있다.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");
                Assert.That(RunLog.FileNameFor(new DateTime(2026, 9, 10, 11, 42, 33)),
                    Is.EqualTo("20260910-114233.log"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        /// <summary>
        /// <b>에디터에서는 아무 파일도 만들지 않는다.</b>
        ///
        /// <para>에디터의 <c>Application.dataPath</c> 는 <c>METAGAME/Assets</c> 라, 그
        /// 부모에 로그 폴더를 만들면 <b>저장소 안에 `METAGAME/logs/` 가 생긴다.</b>
        /// 그래서 <c>Start()</c> 가 불려도 경로가 비어 있어야 한다.</para>
        ///
        /// <para><b>왜 라벨이 아니라 에디터를 보는가</b> (S15P21B202-443). 전에는
        /// <c>RunLog</c> 가 실험 라벨이 있을 때만 켜졌고, 그 검사가 이 보호를 겸하고
        /// 있었다. 배포판에서 문제가 나도 로그가 없다는 문제 때문에 라벨 검사를
        /// 없앴으므로, 이 보호는 <b>따로 세워야 한다</b> — 실제로 라벨 검사를 없앤
        /// 커밋에서 이 테스트가 빨개졌고 CI 가 그것을 잡았다.</para>
        /// </summary>
        [Test]
        public void NothingIsWrittenInsideTheEditor()
        {
            Assert.That(Application.isEditor, Is.True,
                "이 테스트는 에디터에서 돈다 — 이 전제가 깨지면 아래 단정의 뜻이 달라진다");
            Assert.That(RunLog.Path, Is.Empty,
                "에디터에서는 파일을 열지 않는다 (열면 저장소에 로그를 쓴다)");
        }

        /// <summary>정리 상한이 있고, 한 번의 실험 세션을 담을 만큼은 된다.</summary>
        [Test]
        public void OldFilesAreTrimmedButNotTooEagerly()
        {
            Assert.That(RunLog.KeepFiles, Is.GreaterThan(5),
                "한 세션에 여러 번 실행한다 — 너무 적게 두면 방금 잰 것이 지워진다");
            Assert.That(RunLog.KeepFiles, Is.LessThanOrEqualTo(100));
            Assert.That(RunLog.FolderName, Is.EqualTo("logs"),
                "docs/experiment-builds.md 가 이 이름을 적고 있다");
        }
    }
}
