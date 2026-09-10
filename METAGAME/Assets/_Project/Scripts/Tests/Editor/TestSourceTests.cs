using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <see cref="TestSource"/> 자신을 검증한다 (S15P21B202-378).
    ///
    /// <para><b>왜 필요한가.</b> 이것은 다른 테스트들이 배선을 지킬 때 쓰는 <b>공용
    /// 도구</b>다. 도구가 조용히 틀리면 <b>그 위의 테스트 전부가 거짓말을 한다</b> —
    /// 초록인데 지켜지지 않는 상태가 가장 나쁘다.</para>
    ///
    /// <para><c>!287</c> AI 리뷰가 실제 결함을 짚었다: 중괄호 짝맞추기가 주석·문자열을
    /// 구분하지 않아서, <b>주석에 적어 둔 코드 조각이 실제 코드로 잡혔다.</b>
    /// 아래 경우들은 그 함정을 있던 그대로 재현한다.</para>
    /// </summary>
    public sealed class TestSourceTests
    {
        /// <summary>
        /// <b>주석에 적힌 서명에 걸리지 않는다.</b> 이것이 <c>!287</c> 에서 실제로
        /// 위험했던 것이다 — <c>NetPlayer.FixedUpdate</c> 의 주석에
        /// <c>if (transport != null)</c> 이라는 글자가 그대로 들어 있었고, 그 주석이
        /// 실제 블록보다 위로 올라가는 날 엉뚱한 범위를 잘라 <b>조용히 통과</b>했을
        /// 것이다.
        /// </summary>
        [Test]
        public void ASignatureInsideACommentIsNotTheBlock()
        {
            const string source = @"
void Tick()
{
    // 처음에는 `if (real != null)` 안에 두었는데 그것이 틀렸다.
    // { 이 중괄호도 코드가 아니다
    if (real != null)
    {
        MARKER;
    }
}";

            var block = TestSource.Method(source, "if (real != null)");

            Assert.That(block, Does.Contain("MARKER"),
                "주석의 같은 글자에 먼저 걸리면 엉뚱한 범위를 검사하고도 통과한다");
        }

        /// <summary>
        /// <b>주석의 중괄호가 짝맞추기를 깨뜨리지 않는다.</b> 위 경우와 달리 이쪽은
        /// 조용한 통과가 아니라 <b>잘못된 실패</b>로 나타난다 — 원인을 찾기 어렵다.
        /// </summary>
        [Test]
        public void BracesInCommentsDoNotBreakMatching()
        {
            const string source = @"
void Tick()
{
    // 여는 괄호만 있는 주석 {
    KEEP;
}
void After() { OUTSIDE; }";

            var body = TestSource.Method(source, "void Tick()");

            Assert.That(body, Does.Contain("KEEP"));
            Assert.That(body, Does.Not.Contain("OUTSIDE"),
                "주석의 { 를 세면 본문이 다음 메서드까지 삼킨다");
        }

        /// <summary>
        /// <b>보간 문자열의 중괄호는 문법이고, 그 안은 코드다.</b> 로그 한 줄에
        /// <c>$"… {a}/{b} …"</c> 가 흔하므로 중괄호를 세면 본문 경계가 어긋난다.
        /// 반대로 구멍 안까지 지우면 <c>-379</c> 의 "어느 고정쇠인지 로그에 남긴다"
        /// (<c>clamp.name</c>) 단정이 깨진다 — 그 이름은 구멍 안에만 있다.
        /// </summary>
        [Test]
        public void InterpolationBracesAreSyntaxButTheHoleIsCode()
        {
            const string source = @"
void Log()
{
    Debug.Log($""점유 {clamp.Pullers}/{max}명 · 닿음={(ok ? ""O"" : ""X"")}"");
    KEEP;
}
void After() { OUTSIDE; }";

            var body = TestSource.Method(source, "void Log()");

            Assert.That(body, Does.Contain("KEEP"));
            Assert.That(body, Does.Not.Contain("OUTSIDE"),
                "보간 문자열의 중괄호를 세면 본문이 다음 메서드까지 삼킨다");

            Assert.That(body, Does.Contain("clamp.Pullers"),
                "구멍 안은 실제로 실행되는 식이다 — 지우면 로그 내용을 검증할 수 없다");
            Assert.That(body, Does.Not.Contain("점유"),
                "글자 부분은 지운다 — 설명 문구가 코드로 보이면 안 된다");
        }

        /// <summary>
        /// <b>문자열 안의 글자는 없는 것으로 본다.</b> 그래서 "이 호출이 여기 있으면
        /// 안 된다" 류의 단정이 설명 문구에 걸려 헛되게 실패하지 않는다.
        /// </summary>
        [Test]
        public void TextInsideStringsIsNotCode()
        {
            const string source = @"
void Tick()
{
    Fail(""SetDollLayer 를 여기서 부르면 안 된다"");
}";

            var body = TestSource.Method(source, "void Tick()");

            Assert.That(body, Does.Not.Contain("SetDollLayer"),
                "문자열 안의 이름이 호출로 보이면 Does.Not.Contain 단정이 무력해진다");
        }

        /// <summary>
        /// <b>세는 것도 코드만 센다.</b> 독스트링에 같은 이름을 여러 번 적어 두는 일이
        /// 흔하므로, 그것이 호출 개수로 잡히면 "자리마다 있다" 단정이 거짓이 된다.
        /// </summary>
        [Test]
        public void CountingIgnoresCommentsAndStrings()
        {
            const string source = @"
// Apply(x); 라고 설명에 적어 둔 줄
void A() { Apply(x); }
void B() { Log(""Apply(x);""); }
void C() { Apply(x); }";

            Assert.That(TestSource.Count(source, "Apply(x);"), Is.EqualTo(2),
                "주석·문자열까지 세면 배선이 빠졌는데도 개수가 맞아 보인다");
        }

        /// <summary>
        /// <b>길이를 바꾸지 않는다.</b> 잘라낸 조각의 줄 수·모양이 원본과 같아야
        /// 실패 메시지를 읽고 소스에서 그 자리를 찾을 수 있다.
        /// </summary>
        [Test]
        public void BlankingKeepsTheShape()
        {
            const string source = "var a = 1; // 설명\nvar b = \"글자\";\n";
            var blanked = TestSource.Blank(source);

            Assert.That(blanked.Length, Is.EqualTo(source.Length));
            Assert.That(blanked.Split('\n').Length, Is.EqualTo(source.Split('\n').Length),
                "줄이 사라지면 실패 메시지의 위치가 원본과 어긋난다");
            Assert.That(blanked, Does.Contain("var a = 1;"));
            Assert.That(blanked, Does.Not.Contain("설명"));
            Assert.That(blanked, Does.Not.Contain("글자"));
        }

        /// <summary>
        /// <b>없는 서명은 조용히 넘기지 않는다.</b> 이름이 바뀌었는데 테스트가 통과하면
        /// 그것이 가장 나쁜 결과다.
        /// </summary>
        [Test]
        public void AMissingSignatureThrows()
        {
            Assert.That(() => TestSource.Method("void A() { }", "void Gone()"),
                        Throws.TypeOf<System.InvalidOperationException>(),
                        "찾지 못한 것을 빈 문자열로 돌려주면 모든 단정이 헛되게 통과한다");
        }
    }
}
