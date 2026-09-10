using NUnit.Framework;
using UnityEditor;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 게임 <b>표시 이름</b>이 한 곳에서 어긋나지 않게 지킨다 (S15P21B202-391).
    ///
    /// <para><b>왜 필요한가.</b> 이름이 <b>두 곳</b>에 있다 —
    /// <c>ProjectSettings.asset</c> 의 <c>productName</c> 과
    /// <c>SpaceBuild.ApplyPlayerSettings()</c> 의 대입이다. 뒤쪽은 <b>빌드할 때마다
    /// 다시 박으므로</b>, 앞쪽만 고치면 <b>에디터에서는 맞는데 팀에 나가는 빌드가
    /// 옛 이름을 달고 나간다.</b></para>
    ///
    /// <para><b>두 곳인 것은 설계다.</b> <c>ApplyPlayerSettings()</c> 는 회사명·해상도·
    /// 창 모드·그래픽 API 까지 함께 강제해서 CI 빌드가 로컬 설정에 흔들리지 않게 한다.
    /// 그러니 대입을 지우는 것이 답이 아니고, <b>둘이 같은지 지키는 것</b>이 답이다.</para>
    ///
    /// <para><b>내부 식별자는 일부러 그대로 두었다</b>(<c>-391</c>) — 셰이더 경로
    /// (<c>SpaceFlop/…</c>) · 실행 파일 이름(<c>SpaceFlop.exe</c>) · 로그 접두사
    /// (<c>[SpaceFlop]</c>). 특히 셰이더는 <b>머티리얼이 이름으로 참조</b>하므로 바꾸면
    /// 화면이 조용히 깨진다. 아래 마지막 경우가 그 결정이 실수로 뒤집히는 것을 막는다.</para>
    /// </summary>
    public sealed class DisplayNameTests
    {
        const string Expected = "Not This Planet";

        /// <summary>
        /// <see cref="TestSource.Blank"/> 는 <b>길이를 바꾸지 않는다.</b> 그래서 지운
        /// 사본에서 찾은 위치를 <b>원본에 그대로 쓸 수 있다</b> — 주석·설명 문구에
        /// 걸리지 않고 찾은 다음, 문자열 리터럴의 <b>내용</b>을 볼 수 있다.
        /// (<see cref="TestSource.Method"/> 는 리터럴을 지운 사본을 돌려주므로 이 용도로는
        /// 쓸 수 없다.)
        /// </summary>
        static string RawAt(string raw, string needle, string what)
        {
            int at = TestSource.Blank(raw).IndexOf(needle, System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), $"{what}: 코드에서 '{needle}' 을 찾지 못했다");
            return raw.Substring(at);
        }

        /// <summary>에디터가 들고 있는 이름 — 창 제목과 <c>persistentDataPath</c> 의 출처다.</summary>
        [Test]
        public void TheProjectCarriesTheDisplayName()
        {
            Assert.That(PlayerSettings.productName, Is.EqualTo(Expected),
                "ProjectSettings 의 productName 이 표시 이름이다 — 창 제목이 여기서 나온다");
        }

        /// <summary>
        /// <b>빌드가 같은 이름을 박는다.</b> 이것이 이 스위트의 본론이다 — 여기가
        /// 어긋나면 에디터에서 확인하고 끝낸 사람은 알 수 없고, 나가는 빌드만 틀린다.
        /// </summary>
        [Test]
        public void TheBuildStampsTheSameName()
        {
            var raw = TestSource.Read("Editor/SpaceBuild.cs");
            var tail = RawAt(raw, "PlayerSettings.productName", "빌드의 이름 대입");

            Assert.That(tail, Does.StartWith($"PlayerSettings.productName = \"{Expected}\";"),
                $"빌드가 박는 이름이 '{Expected}' 가 아니다 — ProjectSettings 를 고쳐도 "
                + "다음 빌드에 옛 이름으로 되돌아간다");
        }

        /// <summary>
        /// <b>실행 파일 이름은 표시 이름과 독립이다.</b> <c>-391</c> 에서 일부러 그대로
        /// 두었다 — 바꾸면 기존 빌드 폴더·바로가기와 문서의 <c>SpaceFlop_Data</c> 경로가
        /// 함께 어긋난다. 바꾸려면 팀장 확인이 필요하다.
        /// </summary>
        [Test]
        public void TheExecutableNameIsNotTheDisplayName()
        {
            var raw = TestSource.Read("Editor/SpaceBuild.cs");
            var head = Window(RawAt(raw, "locationPathName", "빌드 산출 경로"));

            Assert.That(head, Does.Contain("SpaceFlop.exe"),
                "실행 파일 이름을 바꾸려면 팀장 확인이 필요하다(-391) — 기존 빌드 폴더·"
                + "바로가기와 문서의 SpaceFlop_Data 경로가 함께 어긋난다");
            Assert.That(head, Does.Not.Contain(Expected),
                "실행 파일 이름을 표시 이름으로 바꾸면 위와 같은 이유로 어긋난다");
        }

        /// <summary>
        /// 찾은 자리부터 <b>한 문장 정도</b>만 본다. 파일 전체를 보면 다른 곳의
        /// 같은 글자에 걸리고, 길이를 넘겨 자르면 <b>어긋남이 아니라 편집 위치 때문에
        /// 터진다</b> — 처음 판이 <c>Substring(0, 120)</c> 이어서 그 자리가
        /// 파일 끝 120자 안으로 옮겨지면 예외가 났다(<c>!295</c> AI 리뷰 지적).
        /// </summary>
        static string Window(string tail) =>
            tail.Substring(0, System.Math.Min(StatementWindow, tail.Length));

        /// <summary>대입문 한 줄이 들어갈 만큼 — 근거는 그것뿐이라 이름으로 밝혀 둔다.</summary>
        const int StatementWindow = 120;
    }
}
