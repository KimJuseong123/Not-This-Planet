using System.IO;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 테스트가 <b>소스를 읽어 배선을 지킬 때</b> 쓰는 도구 (S15P21B202-378).
    ///
    /// <para><b>왜 이런 것이 필요한가.</b> 어떤 규칙은 값으로 검증할 수 없다 —
    /// "서버에서는 되쓰지 않는다", "몸을 만드는 자리마다 적용한다", "여기서 레이어를
    /// 건드리지 않는다", "이 대입이 저 조건 블록 안에 있으면 안 된다" 같은 것들이다.
    /// 그런 배선은 <b>조용히 빠지고</b>, 빠지면 증상이 엉뚱하게 보인다(기절 복제가
    /// 빠졌을 때 "클릭이 안 먹는다" 로 보였다).</para>
    ///
    /// <para>CI 스크립트 쪽에서 같은 목적으로 <c>inspect.getsource</c> 를 쓰고 있다 —
    /// <c>access_raw_diffs</c> 가 빠지지 않게 지키는 테스트(S15P21B202-354)가 그것이다.
    /// 여기는 그 C# 판이다.</para>
    ///
    /// <para><b>주석과 문자열은 지운 뒤에 본다.</b> 그러지 않으면 도구가 거짓말을 한다 —
    /// <c>!287</c> AI 리뷰가 짚었고 실제로 위험이 있었다. <c>NetPlayer.FixedUpdate</c> 의
    /// 주석에 <c>if (transport != null)</c> 이라는 글자가 그대로 들어 있어서, 그 주석이
    /// 실제 블록보다 위로 올라가는 날 <see cref="Method"/> 가 <b>엉뚱한 범위를 잘라
    /// 조용히 통과</b>했을 것이다.</para>
    ///
    /// <para><b>다만 보간 구멍은 코드다.</b> <c>$"... · {clamp.name}"</c> 의
    /// <c>clamp.name</c> 은 실제로 실행되는 식이므로 남긴다 — 로그가 무엇을 적는지
    /// 검증하는 테스트가 그것을 본다. 지우는 것은 글자 부분과 <b>중괄호 자체</b>다.
    /// 중괄호를 남기면 짝맞추기가 문자열 때문에 깨진다.</para>
    ///
    /// <para><b>남용하지 않는다.</b> 값으로 검증할 수 있으면 값으로 한다. <c>-383</c>
    /// 에서 실제로 그 판단을 했다 — 거부 사유 문구를 소스에서 찾다가, 순수 함수로 떼어
    /// 열거형을 훑는 값 테스트로 바꿨더니 소스 검사로는 못 잡는 것(사유가 새로 생겨
    /// <c>default</c> 로 떨어지는 경우)이 잡혔다. 소스 검사는 리팩터링에 약하므로,
    /// 실패 메시지에 <b>왜 그 배선이 필요한지</b>를 반드시 적는다.</para>
    /// </summary>
    public static class TestSource
    {
        /// <summary><c>Assets/_Project/Scripts/</c> 아래의 상대 경로로 소스를 읽는다.</summary>
        public static string Read(string relativePath)
        {
            string full = Path.Combine(Application.dataPath, "_Project", "Scripts", relativePath);
            Assert(File.Exists(full), $"소스를 찾지 못했다: {full}");
            return File.ReadAllText(full);
        }

        /// <summary>
        /// 서명 줄부터 <b>중괄호가 맞는 곳까지</b> 잘라 준다. 그 안만 보고 단정할 수
        /// 있어야 한다 — 파일 전체를 훑으면 다른 곳에 걸린다.
        ///
        /// <para>메서드만이 아니라 <b>조건 블록에도 그대로 쓸 수 있다</b>
        /// (<c>Method(fixedUpdate, "if (transport != null)")</c>). 하는 일은
        /// "이 글자부터 중괄호가 맞는 곳까지" 다.</para>
        ///
        /// <para><b>돌려주는 것은 주석·문자열 글자가 지워진 코드다</b>
        /// (<see cref="Blank"/>). 그래서 <c>Does.Contain</c> 은 코드에만 걸리고,
        /// <c>Does.Not.Contain</c> 이 <b>주석에 걸려 헛되게 실패하지 않는다.</b>
        /// 주석 내용을 단정하고 싶으면 이 도구를 쓰지 않는다.</para>
        /// </summary>
        public static string Method(string source, string signature)
        {
            var code = Blank(source);

            int start = code.IndexOf(signature, System.StringComparison.Ordinal);
            Assert(start >= 0, $"서명을 찾지 못했다: {signature}");

            int open = code.IndexOf('{', start);
            Assert(open >= 0, $"본문 시작을 찾지 못했다: {signature}");

            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0) return code.Substring(start, i - start + 1);
                }
            }

            Assert(false, $"본문이 닫히지 않았다: {signature}");
            return string.Empty;
        }

        /// <summary>
        /// 몇 번 나오는가. 배선이 <b>자리마다</b> 있는지 세는 데 쓴다.
        /// 주석·문자열 글자는 세지 않는다 — 설명에 적어 둔 이름이 호출로 잡히면 안 된다.
        /// </summary>
        public static int Count(string source, string needle)
        {
            var code = Blank(source);

            int n = 0;
            int at = 0;
            while ((at = code.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                n++;
                at += needle.Length;
            }
            return n;
        }

        /// <summary>
        /// 주석과 문자열·문자 리터럴의 <b>글자를 공백으로 덮는다.</b> 길이를 그대로
        /// 두므로 잘라낸 조각의 줄 수와 모양이 원본과 같다 — 실패 메시지를 읽고 소스에서
        /// 그 자리를 찾을 수 있어야 한다.
        ///
        /// <para><b>보간 구멍 안은 남긴다.</b> <c>$"…{clamp.name}"</c> 에서
        /// <c>clamp.name</c> 은 실행되는 식이다. 대신 <b>중괄호 자체를 지운다</b> —
        /// 그것은 문자열 문법이고, 남기면 짝맞추기가 깨진다. 이중 중괄호
        /// (<c>{{</c>·<c>}}</c>)는 글자이므로 통째로 지운다.</para>
        ///
        /// <para>C# 파서가 아니다. 우리 소스에 실제로 나오는 것만 다룬다 —
        /// <c>//</c> · <c>///</c> · <c>/* */</c> · <c>"…"</c>(이스케이프 포함) ·
        /// <c>@"…"</c>(<c>""</c> 포함) · <c>$"…"</c> · <c>$@"…"</c> · <c>'c'</c>.
        /// C# 11 의 <c>"""</c> 는 쓰지 않으므로 다루지 않는다.</para>
        /// </summary>
        public static string Blank(string source)
        {
            var c = source.ToCharArray();
            int i = 0;

            while (i < c.Length)
            {
                // 줄 주석 — /// 도 여기서 함께 덮인다.
                if (c[i] == '/' && i + 1 < c.Length && c[i + 1] == '/')
                {
                    while (i < c.Length && c[i] != '\n') c[i++] = ' ';
                    continue;
                }

                // 블록 주석
                if (c[i] == '/' && i + 1 < c.Length && c[i + 1] == '*')
                {
                    c[i++] = ' ';
                    c[i++] = ' ';
                    while (i < c.Length && !(c[i] == '*' && i + 1 < c.Length && c[i + 1] == '/'))
                    {
                        if (c[i] != '\n') c[i] = ' ';
                        i++;
                    }
                    if (i < c.Length) c[i++] = ' ';
                    if (i < c.Length) c[i++] = ' ';
                    continue;
                }

                // $"…" · $@"…" — 보간 문자열
                if (c[i] == '$' && i + 1 < c.Length
                    && (c[i + 1] == '"' || (c[i + 1] == '@' && i + 2 < c.Length && c[i + 2] == '"')))
                {
                    bool verbatim = c[i + 1] == '@';
                    c[i++] = ' ';                      // $
                    if (verbatim) c[i++] = ' ';        // @
                    c[i++] = ' ';                      // 여는 "
                    i = BlankInterpolatedBody(c, i, verbatim);
                    continue;
                }

                // @"…" — 축자 문자열. 안에서 "" 는 따옴표 하나를 뜻하므로 닫지 않는다.
                if (c[i] == '@' && i + 1 < c.Length && c[i + 1] == '"')
                {
                    c[i++] = ' ';
                    c[i++] = ' ';
                    i = BlankVerbatimBody(c, i);
                    continue;
                }

                // "…" · 'c'
                if (c[i] == '"' || c[i] == '\'')
                {
                    char quote = c[i];
                    c[i++] = ' ';
                    i = BlankSimpleBody(c, i, quote);
                    continue;
                }

                i++;
            }

            return new string(c);
        }

        /// <summary>보통 문자열·문자 리터럴의 안쪽을 덮는다. 닫는 따옴표까지 소비한다.</summary>
        static int BlankSimpleBody(char[] c, int i, char quote)
        {
            while (i < c.Length && c[i] != quote)
            {
                // 줄이 끝나면 멈춘다. 닫히지 않은 리터럴 때문에 파일의 나머지를 통째로
                // 덮어 버리면 실패 원인을 알 수 없게 된다.
                if (c[i] == '\n') return i;

                if (c[i] == '\\' && i + 1 < c.Length)
                {
                    c[i++] = ' ';
                    c[i++] = ' ';
                    continue;
                }
                c[i++] = ' ';
            }

            if (i < c.Length && c[i] == quote) c[i++] = ' ';
            return i;
        }

        /// <summary>축자 문자열의 안쪽을 덮는다. 줄바꿈은 그대로 두고 <c>""</c> 를 넘긴다.</summary>
        static int BlankVerbatimBody(char[] c, int i)
        {
            while (i < c.Length)
            {
                if (c[i] == '"')
                {
                    if (i + 1 < c.Length && c[i + 1] == '"')
                    {
                        c[i++] = ' ';
                        c[i++] = ' ';
                        continue;
                    }
                    c[i++] = ' ';
                    return i;
                }

                if (c[i] != '\n') c[i] = ' ';
                i++;
            }
            return i;
        }

        /// <summary>
        /// 보간 문자열의 안쪽을 덮되 <b>구멍 안의 식은 남긴다.</b> 중괄호는 문자열
        /// 문법이므로 지운다 — 여는 것과 닫는 것을 함께 지우므로 짝이 맞는다.
        /// </summary>
        static int BlankInterpolatedBody(char[] c, int i, bool verbatim)
        {
            while (i < c.Length)
            {
                if (c[i] == '"')
                {
                    if (verbatim && i + 1 < c.Length && c[i + 1] == '"')
                    {
                        c[i++] = ' ';
                        c[i++] = ' ';
                        continue;
                    }
                    c[i++] = ' ';
                    return i;
                }

                if (!verbatim && c[i] == '\\' && i + 1 < c.Length)
                {
                    c[i++] = ' ';
                    c[i++] = ' ';
                    continue;
                }

                // {{ 와 }} 는 중괄호 한 글자를 뜻하는 글자다 — 구멍이 아니다.
                if ((c[i] == '{' || c[i] == '}') && i + 1 < c.Length && c[i + 1] == c[i])
                {
                    c[i++] = ' ';
                    c[i++] = ' ';
                    continue;
                }

                if (c[i] == '{')
                {
                    c[i++] = ' ';                      // 여는 중괄호는 문법이다
                    i = BlankHole(c, i, verbatim);
                    continue;
                }

                if (c[i] != '\n') c[i] = ' ';
                i++;
            }
            return i;
        }

        /// <summary>
        /// 구멍 안 — <b>여기는 코드다.</b> 중첩 리터럴만 덮고 나머지는 남긴다.
        /// 닫는 중괄호를 지우고 그 뒤 위치를 돌려준다.
        /// </summary>
        static int BlankHole(char[] c, int i, bool verbatim)
        {
            int depth = 1;

            while (i < c.Length)
            {
                if (c[i] == '"' || c[i] == '\'')
                {
                    char quote = c[i];
                    c[i++] = ' ';
                    i = verbatim && quote == '"'
                        ? BlankVerbatimBody(c, i)
                        : BlankSimpleBody(c, i, quote);
                    continue;
                }

                if (c[i] == '{')
                {
                    depth++;
                    i++;
                    continue;
                }

                if (c[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        c[i++] = ' ';                  // 닫는 중괄호도 문법이다
                        return i;
                    }
                    i++;
                    continue;
                }

                i++;
            }
            return i;
        }

        /// <summary>NUnit 을 참조하지 않고도 쓸 수 있게 자체 단정을 둔다.</summary>
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }
    }
}
