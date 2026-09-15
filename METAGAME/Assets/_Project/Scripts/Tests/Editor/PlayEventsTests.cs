using Metagame.Bootstrap;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 플레이 이벤트 (S15P21B202-498).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 이 기록은 배포판에서만 쌓이고 우리가 직접 볼
    /// 일이 거의 없다 — 조용히 틀리면 <b>틀린 줄이 그대로 분석에 들어간다.</b> 평문
    /// 로그와 달리 사람이 읽고 이상하다고 느낄 기회가 없다.
    ///
    /// <para><b>파일 쓰기는 여기서 검사하지 않는다.</b> 에디터에서는 일부러 안 돌기
    /// 때문이다(<see cref="PlayEvents.Begin"/>) — 저장소 안에 폴더가 생기는 것을 막는
    /// 결정이고, <see cref="RunLog"/> 와 같은 이유다. 그래서 <b>값을 만드는 쪽</b>을
    /// 검사한다.
    /// </summary>
    public sealed class PlayEventsTests
    {
        [Test]
        public void TheEditorWritesNothing()
        {
            // 에디터의 Application.dataPath 는 저장소 안이다. 켜 두면 리포에
            // `METAGAME/events/` 가 생긴다.
            PlayEvents.Begin(null);

            Assert.That(PlayEvents.Active, Is.False, "에디터에서 이벤트 파일이 열렸다");
            Assert.That(PlayEvents.Path, Is.Empty);
        }

        [Test]
        public void ALineNeverBreaksTheFile()
        {
            // NDJSON 은 **한 줄이 한 이벤트**다. 줄바꿈이 값에 섞이면 그 파일 전체를
            // 못 읽는다 — 자유 문자열을 안 싣는 것이 규칙이지만 규칙에 기대지 않는다.
            string got = PlayEvents.Escape("a\nb\r\nc\td\"e\\f");

            Assert.That(got, Does.Not.Contain("\n"));
            Assert.That(got, Does.Not.Contain("\r"));
            Assert.That(got, Does.Not.Contain("\t"));
            Assert.That(got, Is.EqualTo("a\\nb\\r\\nc\\td\\\"e\\\\f"));
        }

        [Test]
        public void ControlCharactersBecomeEscapes()
        {
            // 제어문자가 그대로 들어가면 JSON 파서가 줄 하나를 통째로 버린다.
            Assert.That(PlayEvents.Escape("ab"), Is.EqualTo("a\\u0001b"));
        }

        [Test]
        public void TheFileNameSortsByTime()
        {
            // 이름이 곧 정렬 순서여야 오래된 것부터 지울 수 있다 — 평문 로그와 같은 규칙.
            string a = PlayEvents.FileNameFor(new System.DateTime(2026, 9, 15, 4, 3, 9));
            string b = PlayEvents.FileNameFor(new System.DateTime(2026, 9, 15, 9, 33, 0));

            Assert.That(a, Is.EqualTo("20260915-040309.ndjson"));
            Assert.That(string.CompareOrdinal(a, b), Is.LessThan(0), "문자열 정렬이 곧 시각 정렬이어야 한다");
        }

        [Test]
        public void TheBlockerBecomesAName()
        {
            // 한글 문구를 그대로 실으면 문구가 바뀔 때마다 집계가 갈라진다.
            // 이름은 `ShipAssembly.Evaluate()` 의 `// code:` 주석과 같아야 하고,
            // CI 가 둘이 어긋나지 않는지 본다(`scripts/ci/tests/test_event_schema.py`).
            Assert.That(PlayObserver.BlockerCode("주 추진 엔진 모듈이 조립대에 없다"), Is.EqualTo("no_engine"));
            Assert.That(PlayObserver.BlockerCode("파츠가 2/4 층만 쌓였다"), Is.EqualTo("incomplete"));
            Assert.That(PlayObserver.BlockerCode("3층이 승무원 거주 모듈 이 아니다"), Is.EqualTo("wrong_order"));
            Assert.That(PlayObserver.BlockerCode("우주선이 발사 구역 밖에 있다"), Is.EqualTo("off_pad"));
            Assert.That(PlayObserver.BlockerCode("Part_2 이 12도 기울어 있다"), Is.EqualTo("tilted"));
        }

        [Test]
        public void AnUnknownBlockerIsNotSilentlyDropped()
        {
            // 모르는 사유를 빈 값으로 두면 **막혔는데 안 막힌 것처럼** 보인다.
            // `other` 로 남겨야 "새 사유가 생겼는데 이름을 안 붙였다" 를 알 수 있다.
            Assert.That(PlayObserver.BlockerCode("연료가 없다"), Is.EqualTo("other"));

            // 막히지 않은 상태는 빈 값이 맞다.
            Assert.That(PlayObserver.BlockerCode(""), Is.Empty);
            Assert.That(PlayObserver.BlockerCode(null), Is.Empty);
        }

        [Test]
        public void TheObserverLooksRarely()
        {
            // 프레임에 얹히는 유일한 자리다. 보는 것은 이미 계산된 값 몇 개뿐이고,
            // 파트를 옮기고 용접하는 일은 초 단위라 자주 볼 값어치가 없다(`-374`).
            Assert.That(PlayObserver.Interval, Is.GreaterThanOrEqualTo(0.25f),
                "관찰 주기가 너무 촘촘하다 — 프레임 저하와 구분이 안 된다");
        }

        [Test]
        public void TheEventFolderSitsBesideTheLogFolder()
        {
            // 두 폴더가 같은 규칙으로 정해져야 **로그는 남는데 이벤트는 안 남는** 상태가
            // 안 생긴다. 그 상태의 원인을 찾는 것이 가장 어렵다.
            var src = TestSource.Read("Runtime/Core/PlayEvents.cs");

            Assert.That(src, Does.Contain("RunLog.FolderFor("),
                "이벤트가 폴더 규칙을 따로 갖고 있다 — 한쪽만 고쳐진다");
        }
    }
}
