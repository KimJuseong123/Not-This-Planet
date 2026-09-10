using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 방 안의 자리 번호(0~3)와 그 자리의 색을 정한다 (S15P21B202-375).
    ///
    /// <b>왜 필요한가.</b> 예전에는 우주복 색을 <c>userId</c> 에서 뽑았다. 그 값은 DB
    /// 자동 증가라서 <b>같은 사람이 판마다 다른 색</b>이 됐고, 방 안의 네 명이 서로 잘
    /// 갈리는 색을 받는다는 보장도 없었다. "파란 사람이 그거 잡아" 같은 말이 성립하지
    /// 않는다.
    ///
    /// <b>서버(호스트)에서만 쓴다.</b> 자리를 서버가 정하고 SyncVar 로 내려야 모든
    /// 화면에서 같은 사람이 같은 색이다.
    /// </summary>
    public static class TeamSlots
    {
        /// <summary>한 방의 최대 인원. 백엔드 <c>Room.MAX_PLAYERS</c> 와 같은 값이다.</summary>
        public const int Count = 4;

        /// <summary>
        /// 자리마다 정해진 색. <b>채도를 높게 잡았다</b> — 예전 값(채도 0.35 · 명도 0.95)은
        /// 색을 살짝 입힌 파스텔이라 네 명이 다 비슷한 흰색으로 보였다.
        ///
        /// <para>빨강·파랑·노랑·초록 순이다. 서로 색상환에서 멀고, 이름으로 부르기 쉽고,
        /// 적록색약에서도 명도가 갈린다(빨강 0.85 · 초록 0.65).</para>
        /// </summary>
        static readonly Color[] Suits =
        {
            new Color(0.88f, 0.26f, 0.24f),   // 0 빨강
            new Color(0.22f, 0.48f, 0.90f),   // 1 파랑
            new Color(0.95f, 0.78f, 0.20f),   // 2 노랑
            new Color(0.30f, 0.72f, 0.38f),   // 3 초록
        };

        /// <summary>자리 이름. 로그와 안내에 쓴다 — 색을 말로 부를 수 있어야 한다.</summary>
        static readonly string[] Names = { "빨강", "파랑", "노랑", "초록" };

        /// <summary>지금 판에서 누가 어느 자리인지. 서버에만 있다.</summary>
        static readonly Dictionary<long, int> Assigned = new Dictionary<long, int>();

        /// <summary>
        /// 이 사람의 자리를 정한다. <b>같은 사람은 같은 자리를 다시 받는다</b> —
        /// 끊겼다 돌아와도 색이 바뀌지 않는다(S15P21B202-150 과 짝).
        ///
        /// <para>빈 자리가 없으면(정원을 넘겼으면) 0 을 준다. 백엔드가 정원을 막으므로
        /// 정상 흐름에서는 오지 않는 길이다.</para>
        /// </summary>
        public static int Claim(long userId)
        {
            if (Assigned.TryGetValue(userId, out int existing)) return existing;

            for (int slot = 0; slot < Count; slot++)
            {
                if (!Assigned.ContainsValue(slot))
                {
                    Assigned[userId] = slot;
                    return slot;
                }
            }

            Debug.LogWarning($"[TeamSlots] 빈 자리가 없다 · userId={userId} — 0번을 준다");
            return 0;
        }

        /// <summary>
        /// 배정을 전부 버린다. 새 판이 시작될 때 서버가 부른다.
        ///
        /// <para>없으면 지난 판의 배정이 남아 새 판에서 자리가 이어진다 — 두 명만 남았는데
        /// 2번·3번 자리를 쓰는 식이 된다.</para>
        /// </summary>
        public static void Clear() => Assigned.Clear();

        /// <summary>자리의 우주복 색.</summary>
        public static Color Suit(int slot) => Suits[Wrap(slot)];

        /// <summary>
        /// 자리의 강조 색. 이름표·핑처럼 <b>배경 위에 글씨로</b> 올라가는 것에 쓴다.
        /// 우주복 색을 그대로 쓰면 어두운 화면에서 파랑·초록이 읽히지 않는다.
        /// </summary>
        public static Color Accent(int slot)
        {
            Color.RGBToHSV(Suits[Wrap(slot)], out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, 0.95f));
        }

        /// <summary>자리 이름("빨강" 등).</summary>
        public static string Name(int slot) => Names[Wrap(slot)];

        static int Wrap(int slot) => ((slot % Count) + Count) % Count;
    }
}
