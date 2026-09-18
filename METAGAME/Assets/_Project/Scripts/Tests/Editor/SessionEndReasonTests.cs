using System.Collections.Generic;
using System.IO;
using Metagame.Bootstrap;
using Metagame.Core;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 판이 <b>어떻게</b> 끝났는지 (S15P21B202-625).
    ///
    /// <para><b>무엇을 막는 검증인가.</b> 씬 교체로 끝나는 길이 하나뿐이라, 깨고 로비로
    /// 돌아간 판과 일시정지 메뉴에서 타이틀로 나간 판이 <c>OnDestroy</c> 에서 <b>똑같이
    /// 보였다</b> — 둘 다 <c>result: "destroy"</c> 였다. 그러면 <i>"사람들이 어디서
    /// 포기했나"</i> 를 셀 수 없는데, 지금 이 게임 단계에서 가장 알고 싶은 것이 그것이다.</para>
    ///
    /// <para>되돌아가기 쉬운 종류의 회귀다 — 표시를 세우는 곳(UI)과 읽는 곳(관찰자)이
    /// 다른 어셈블리에 있고, 둘을 잇는 것은 값 하나뿐이라 한쪽만 지워도 컴파일은 된다.
    /// 그래서 <b>배선까지</b> 본다.</para>
    /// </summary>
    public sealed class SessionEndReasonTests
    {
        /// <summary>
        /// 표시는 <b>프로세스 전역</b>이라 다른 검사가 남긴 값이 흘러들 수 있다
        /// (<c>!527</c> AI 리뷰). <c>finally</c> 로 되돌리는 것만으로는 <b>들어올 때</b>가
        /// 안 덮이므로 양쪽에 둔다.
        /// </summary>
        [SetUp]
        public void NobodyHasLeftYet() => PlayEvents.NoteLeftForTitle(false);

        /// <summary>다음 검사에 흘려보내지 않는다 — 이 파일 밖에도 <c>PlayEvents</c> 검사가 있다.</summary>
        [TearDown]
        public void LeaveNoMark() => PlayEvents.NoteLeftForTitle(false);

        /// <summary>완료 조건 1 — 타이틀로 나간 판과 그냥 씬이 내려간 판이 달라야 한다.</summary>
        [Test]
        public void LeavingForTheTitleIsNotTheSameAsTheSceneJustGoingAway()
        {
            Assert.That(PlayObserver.ResultOf(leftForTitle: true, cleared: false),
                        Is.EqualTo("abandoned"),
                        "사람이 나간 판을 구분하지 못한다 — 포기를 셀 수 없다");

            Assert.That(PlayObserver.ResultOf(leftForTitle: false, cleared: false),
                        Is.EqualTo("destroy"),
                        "이유를 모르는 끝에 이름을 붙였다 — 튕긴 판을 실패한 판으로 세게 된다");
        }

        /// <summary>
        /// <b>깬 것이 먼저다.</b> 깨고 나서 타이틀로 나가는 것은 포기가 아니다 — 나간
        /// 방식은 판의 결과가 아니다.
        /// </summary>
        [Test]
        public void ClearingTheRunBeatsHowTheyLeftIt()
        {
            Assert.That(PlayObserver.ResultOf(leftForTitle: false, cleared: true),
                        Is.EqualTo("cleared"));

            Assert.That(PlayObserver.ResultOf(leftForTitle: true, cleared: true),
                        Is.EqualTo("cleared"),
                        "깬 판을 포기로 센다");
        }

        /// <summary>
        /// 씬이 내려가는 자리가 <b>표시를 실제로 읽어야</b> 한다. 글자를 박아 두면 모든
        /// 씬 교체가 같은 값으로 남고, 이 이슈가 없던 상태로 돌아간다.
        ///
        /// <para><b>부정으로는 못 막는다.</b> <c>Does.Not.Contain("End(\"")</c> 로 쓰면
        /// 아무것도 잡지 못한다 — <see cref="TestSource.Blank"/> 가 <b>여는 따옴표까지</b>
        /// 지우므로(<c>TestSource.cs</c> 의 <c>c[i++] = ' '</c>) 박아 넣은
        /// <c>End("destroy")</c> 도 <c>End(         )</c> 가 되어 걸리지 않는다. 그래서
        /// <b>배선을 그대로 적어</b> 긍정으로 막는다.</para>
        /// </summary>
        [Test]
        public void TheSceneTeardownReadsTheMarkInsteadOfSayingDestroy()
        {
            var body = TestSource.Method(TestSource.Read("Runtime/Bootstrap/PlayObserver.cs"),
                                         "void OnDestroy()");

            Assert.That(body, Does.Contain("ResultOf(PlayEvents.LeftForTitle"),
                        "씬 교체로 끝날 때 떠남 표시를 읽지 않는다 — 모든 씬 교체가 같은 값으로 남는다");
            Assert.That(body, Does.Contain("session.Cleared"),
                        "깬 판을 가리지 않는다 — 깨고 나간 판이 포기로 셈된다");
        }

        /// <summary>
        /// 창을 닫는 길은 <b>그대로 자기 값</b>이어야 한다. 위 함수로 합치면 종료와
        /// 씬 교체가 섞인다.
        ///
        /// <para>원문을 본다 — <see cref="TestSource.Blank"/> 는 문자열 안을 비우므로
        /// 값이 무엇인지 볼 수 없다.</para>
        ///
        /// <para><b>모양이 아니라 값을 본다</b> (<c>!527</c> AI 리뷰). 글자를 그대로 대면
        /// 식 본문(<c>=&gt;</c>)을 블록으로 바꾸는 <b>무해한 정리에도 깨진다</b> — 헛되게
        /// 빨개지는 검사는 다음 사람이 지운다. 그래서 훅과 값 사이를 느슨하게 둔다.</para>
        /// </summary>
        [Test]
        public void ClosingTheWindowKeepsItsOwnResult()
        {
            var raw = TestSource.Read("Runtime/Bootstrap/PlayObserver.cs");

            Assert.That(raw, Does.Match(@"OnApplicationQuit\(\)[\s\S]{0,80}?End\(""quit"""),
                        "창을 닫은 판이 씬 교체와 같은 값으로 남는다");
        }

        /// <summary>
        /// 표시를 <b>세우는 곳과 되돌리는 곳</b>이 둘 다 있어야 한다. 전환은 실패할 수
        /// 있고, 세운 채로 두면 <b>그 뒤에 실제로 끝난 판이 떠난 판으로 셈된다.</b>
        /// </summary>
        [Test]
        public void AFailedTitleTransitionTakesTheMarkBack()
        {
            var menu = TestSource.Read("Runtime/UI/GamePauseMenu.cs");

            Assert.That(TestSource.Method(menu, "private void ReturnToTitle()"),
                        Does.Contain("NoteLeftForTitle(true)"),
                        "타이틀로 나가는 자리가 표시를 남기지 않는다 — 씬이 내려간 뒤엔 알 수 없다");

            Assert.That(TestSource.Method(menu, "private void OnTitleTransitionFailed(string message)"),
                        Does.Contain("NoteLeftForTitle(false)"),
                        "전환이 실패해 판에 남았는데 표시가 남는다 — 다음 끝이 포기로 셈된다");
        }

        /// <summary>
        /// 새 판은 <b>아무도 떠나지 않은 상태</b>로 시작한다. 실제로
        /// <see cref="PlayEvents.Begin"/> 을 불러 본다 — 지우는 줄이 없어지면 한 번
        /// 나간 뒤의 모든 판이 포기로 남는다.
        /// </summary>
        [Test]
        public void ANewPlayStartsWithNobodyHavingLeft()
        {
            string dir = Path.Combine(Path.GetTempPath(), "metagame-end-reason");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var saved = PlayEvents.FolderResolver;
            try
            {
                PlayEvents.FolderResolver = () => dir;

                PlayEvents.NoteLeftForTitle(true);
                Assert.That(PlayEvents.LeftForTitle, Is.True, "표시가 세워지지 않는다");

                PlayEvents.Begin(new Dictionary<string, object>());

                Assert.That(PlayEvents.LeftForTitle, Is.False,
                            "앞 판에서 나간 표시가 남았다 — 그 뒤 모든 판이 포기로 셈된다");
            }
            finally
            {
                PlayEvents.End();
                PlayEvents.NoteLeftForTitle(false);
                PlayEvents.FolderResolver = saved;
            }
        }
    }
}
