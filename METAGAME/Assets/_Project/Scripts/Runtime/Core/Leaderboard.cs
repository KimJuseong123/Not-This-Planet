using System;
using System.Collections.Generic;
using System.Text;

namespace Metagame.Core
{
    /// <summary>리더보드 한 줄. 서버가 준 것을 그대로 담는다.</summary>
    [Serializable]
    public sealed class LeaderboardRow
    {
        public int rank;
        public string crewNames;
        public long clearMillis;
        public int crewSize;
    }

    /// <summary>JsonUtility 가 배열을 바로 못 읽어서 한 겹 감싼다.</summary>
    [Serializable]
    public sealed class LeaderboardPage
    {
        public LeaderboardRow[] rows = Array.Empty<LeaderboardRow>();
    }

    /// <summary>보낼 기록 하나. 서버의 <c>SubmitRunRequest</c> 와 짝이다.</summary>
    [Serializable]
    public sealed class LeaderboardSubmission
    {
        public long clearMillis;
        public List<string> crewNames = new();
        public string build;
    }

    /// <summary>
    /// 리더보드의 <b>계산과 서식</b>만 모아 둔 곳 (S15P21B202-656).
    ///
    /// <para><b>왜 통신에서 갈라 두는가.</b> 여기 있는 것은 전부 "무엇을 넣으면 무엇이
    /// 나오는가" 라서 검증이 값을 직접 넣어 잠글 수 있다. <c>UnityWebRequest</c> 와
    /// 섞여 있으면 서버를 세워야 시험할 수 있고, 그러면 아무도 시험하지 않는다 —
    /// <c>MeteorImpact</c> 를 순수 계산으로 갈라 둔 것과 같은 판단이다.</para>
    /// </summary>
    public static class Leaderboard
    {
        /// <summary>게임이 허용하는 최대 인원. 서버의 <c>RunMode.MAX_CREW</c> 와 같다.</summary>
        public const int MaxCrew = 4;

        /// <summary>서버가 받아 주는 가장 빠른 클리어(밀리초).</summary>
        public const long MinClearMillis = 30_000L;

        /// <summary>서버가 받아 주는 가장 느린 클리어(밀리초).</summary>
        public const long MaxClearMillis = 3 * 60 * 60 * 1000L;

        /// <summary>서버가 닉네임을 잇는 구분자. 표에 그대로 보인다.</summary>
        public const string NameSeparator = " · ";

        /// <summary>
        /// 표가 넷이다 — 1인·2인·3인·4인. 탭이 이 순서로 놓인다.
        ///
        /// <b>모드라는 별도의 값은 없다.</b> 닉네임 수가 곧 인원이고 인원이 곧 표다.
        /// 게임이 인원으로 난이도를 조정하므로(기획 §17.6 혼자면 운석 45%) 인원이
        /// 다르면 애초에 다른 판이고, 한 표에 섞으면 비교가 성립하지 않는다.
        /// </summary>
        public static readonly int[] Boards = { 1, 2, 3, 4 };

        /// <summary>탭에 쓰는 이름.</summary>
        public static string DisplayName(int crewSize) => crewSize + "인";

        /// <summary>물어봐도 되는 표인가.</summary>
        public static bool IsValidCrew(int crewSize) => crewSize >= 1 && crewSize <= MaxCrew;

        /// <summary>
        /// 클리어 시간을 표에 쓰는 꼴로 (<c>04:12</c>).
        ///
        /// <para><b>한 시간을 넘으면 칸이 하나 는다</b>(<c>1:04:12</c>). 서버가 세
        /// 시간까지 받으므로 분이 60 을 넘는 기록이 실제로 들어올 수 있고, 그때
        /// <c>184:12</c> 로 찍히면 읽히지 않는다.</para>
        /// </summary>
        public static string FormatTime(long millis)
        {
            if (millis < 0) millis = 0;

            var total = millis / 1000L;
            var hours = total / 3600L;
            var minutes = (total % 3600L) / 60L;
            var seconds = total % 60L;

            return hours > 0
                ? string.Format("{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        /// <summary>
        /// 보낼 기록을 만든다. <b>서버가 거절할 것은 여기서 거른다.</b>
        ///
        /// <para>보내 봐야 400 을 받을 값이면 굳이 요청을 만들지 않는다 — 실패 로그만
        /// 남고 사용자는 아무것도 얻지 못한다. 서버가 같은 검사를 또 하는 것은
        /// 클라이언트를 믿지 않기 때문이지 여기가 필요 없어서가 아니다.</para>
        /// </summary>
        /// <returns>보낼 것. 보낼 수 없으면 <c>null</c>.</returns>
        public static LeaderboardSubmission BuildSubmission(
            double playTimeSeconds, IEnumerable<string> names, string build)
        {
            var millis = (long)Math.Round(playTimeSeconds * 1000d);
            if (millis < MinClearMillis || millis > MaxClearMillis) return null;

            var cleaned = CleanNames(names);
            if (cleaned.Count == 0) return null;

            return new LeaderboardSubmission
            {
                clearMillis = millis,
                crewNames = cleaned,
                build = Trim(build, 80),
            };
        }

        /// <summary>
        /// 닉네임을 쓸 수 있는 꼴로.
        ///
        /// <para><b>구분자를 이름에서 없앤다.</b> 서버가 한 문자열로 이어 붙이므로,
        /// 이름 안에 <see cref="NameSeparator"/> 가 있으면 읽는 쪽에서 사람이 하나 더
        /// 있는 것처럼 보인다. 서버도 같은 것을 하지만 여기서도 하는 이유는 위와 같다 —
        /// 거절당할 값을 보내지 않는다.</para>
        /// </summary>
        public static List<string> CleanNames(IEnumerable<string> names)
        {
            var result = new List<string>();
            if (names == null) return result;

            foreach (var raw in names)
            {
                if (raw == null) continue;

                var cleaned = Strip(raw).Replace(NameSeparator.Trim(), string.Empty).Trim();
                if (cleaned.Length == 0) continue;

                result.Add(Trim(cleaned, 20));
                if (result.Count >= MaxCrew) break;
            }

            return result;
        }

        /// <summary>제어문자를 턴다 — 표가 깨지고 헤더에도 못 넣는다.</summary>
        static string Strip(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                if (!char.IsControl(c))
                    sb.Append(c);
            return sb.ToString();
        }

        static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var t = Strip(value).Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }
    }
}
