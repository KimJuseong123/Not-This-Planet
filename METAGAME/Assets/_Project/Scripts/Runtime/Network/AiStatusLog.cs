using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 화면에 적은 것을 <b>로그에도 남긴다</b> (S15P21B202-708).
    ///
    /// <para><b>왜 따로 적지 않나.</b> AI 는 판단 지점마다 이미 <c>Message</c> 에 무슨 일이
    /// 있었는지 적는다. 그런데 그 값이 화면에서만 쓰여서, 다음 문구로 덮이면 사라진다 —
    /// 2026-09-22 QA 에서 "AI 가 실제로 어떻게 됐는지" 를 보려고 <c>Player.log</c> 를 열었더니
    /// <b>AI 줄이 하나도 없었다.</b></para>
    ///
    /// <para>기록할 자리를 새로 만들지 않는 것이 핵심이다. 따로 만들면 <b>다음에 추가되는
    /// 판단 지점이 조용히 빠진다</b> — 한 곳은 고쳐지고 다른 곳은 잊힌다.</para>
    ///
    /// <para><b>진행 표시가 로그를 덮지 않게 한다.</b> <c>모델 데우는 중 42초</c> ·
    /// <c>다운로드 45%</c> 는 계속 바뀐다. 매번 적으면 정작 읽어야 할 줄이 묻힌다 — 빨간 것이
    /// 늘 있으면 없는 것과 같다. 숫자를 지운 줄이 같으면 <see cref="RepeatQuiet"/> 초에 한 번만
    /// 적는다. 진행도 보이고 로그도 읽힌다.</para>
    /// </summary>
    public sealed class AiStatusLog
    {
        /// <summary>같은 종류의 줄을 이만큼 사이에 두고 적는다.</summary>
        public const float RepeatQuiet = 10f;

        string lastKind;
        float lastAt = -RepeatQuiet * 2f;

        /// <summary>
        /// 지금 이 줄을 적을 때인가. <b>적기로 하면 그 사실을 기억한다</b> —
        /// 부르는 쪽이 따로 상태를 들고 있지 않아도 되게.
        /// </summary>
        public bool Due(string message, float now)
        {
            string kind = Kind(message);
            if (kind == lastKind && now - lastAt < RepeatQuiet) return false;
            lastKind = kind; lastAt = now;
            return true;
        }

        /// <summary>
        /// 숫자를 지운 줄. <c>"모델 데우는 중 42초"</c> 와 <c>"모델 데우는 중 55초"</c> 는
        /// <b>같은 종류</b>이고, <c>"추가팩이 종료됐습니다"</c> 는 다른 종류다.
        /// </summary>
        public static string Kind(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            var text = new System.Text.StringBuilder(message.Length);
            foreach (char c in message) if (!char.IsDigit(c)) text.Append(c);
            return text.ToString();
        }

        /// <summary>바뀐 문구를 <c>Player.log</c> 에 한 줄로. 같은 종류가 이어지면 건너뛴다.</summary>
        public void Write(string tag, string message)
        {
            if (Due(message, Time.realtimeSinceStartup)) Debug.Log(tag + " " + message);
        }
    }
}
