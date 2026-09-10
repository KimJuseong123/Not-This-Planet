using System.IO;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 테스트가 <b>소스를 읽어 배선을 지킬 때</b> 쓰는 도구 (S15P21B202-378).
    ///
    /// <para><b>왜 이런 것이 필요한가.</b> 어떤 규칙은 값으로 검증할 수 없다 —
    /// "서버에서는 되쓰지 않는다", "몸을 만드는 자리마다 적용한다", "여기서 레이어를
    /// 건드리지 않는다" 같은 것들이다. 그런 배선은 <b>조용히 빠지고</b>, 빠지면 증상이
    /// 엉뚱하게 보인다(기절 복제가 빠졌을 때 "클릭이 안 먹는다" 로 보였다).</para>
    ///
    /// <para>CI 스크립트 쪽에서 같은 목적으로 <c>inspect.getsource</c> 를 쓰고 있다 —
    /// <c>access_raw_diffs</c> 가 빠지지 않게 지키는 테스트(S15P21B202-354)가 그것이다.
    /// 여기는 그 C# 판이다.</para>
    ///
    /// <para><b>남용하지 않는다.</b> 값으로 검증할 수 있으면 값으로 한다. 소스 검사는
    /// 리팩터링에 약하므로, 실패 메시지에 <b>왜 그 배선이 필요한지</b>를 반드시 적는다.</para>
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
        /// 서명 줄부터 <b>중괄호가 맞는 곳까지</b> 잘라 준다. 그 메서드 안만 보고
        /// 단정할 수 있어야 한다 — 파일 전체를 훑으면 다른 곳의 문자열에 걸린다.
        /// </summary>
        public static string Method(string source, string signature)
        {
            int start = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assert(start >= 0, $"서명을 찾지 못했다: {signature}");

            int open = source.IndexOf('{', start);
            Assert(open >= 0, $"본문 시작을 찾지 못했다: {signature}");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(start, i - start + 1);
                }
            }

            Assert(false, $"본문이 닫히지 않았다: {signature}");
            return string.Empty;
        }

        /// <summary>몇 번 나오는가. 배선이 <b>자리마다</b> 있는지 세는 데 쓴다.</summary>
        public static int Count(string source, string needle)
        {
            int n = 0;
            int at = 0;
            while ((at = source.IndexOf(needle, at, System.StringComparison.Ordinal)) >= 0)
            {
                n++;
                at += needle.Length;
            }
            return n;
        }

        /// <summary>NUnit 을 참조하지 않고도 쓸 수 있게 자체 단정을 둔다.</summary>
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }
    }
}
