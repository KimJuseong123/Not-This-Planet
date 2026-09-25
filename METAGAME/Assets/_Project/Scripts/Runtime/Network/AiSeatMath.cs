namespace Metagame.Network
{
    /// <summary>
    /// AI 자리 수를 셈하는 순수 함수 (S15P21B202-713).
    ///
    /// <para><b>왜 따로 두나.</b> 같은 규칙을 세 곳이 쓴다 — 로비 버튼(더 넣을 수 있나),
    /// 받은 방 상태(값이 범위 안인가), 서버의 <see cref="TeamSeats"/>(어느 자리에 띄우나).
    /// 자리마다 식을 따로 쓰면 하나만 고쳐지는 날이 온다. 백엔드 <c>Room</c> 과도 같은
    /// 규칙이다 — 한쪽을 바꾸면 다른 쪽도 바꾼다.</para>
    ///
    /// <para><b>할당하지 않는다.</b> <see cref="SlotsToFill"/> 는 서버에서 매 프레임 돈다.
    /// 부르는 쪽이 미리 만든 배열을 넘긴다.</para>
    /// </summary>
    public static class AiSeatMath
    {
        /// <summary>
        /// 사람이 이만큼일 때 넣을 수 있는 AI 의 최대 수.
        ///
        /// <para>사람은 적어도 1(방장)로 센다 — 사람 목록이 아직 비어 있어도 방장 자리는
        /// 남겨야 한다. 그래서 AI 는 많아야 <c>cap - 1</c> 이다.</para>
        /// </summary>
        public static int MaxAi(int humans, int cap = TeamSlots.Count)
        {
            int max = cap - (humans < 1 ? 1 : humans);
            return max < 0 ? 0 : max;
        }

        /// <summary>원하는 수를 <c>0 ~ MaxAi</c> 로 줄인다. 백엔드 <c>Room.setAiCount</c> 와 같다.</summary>
        public static int ClampRequested(int wanted, int humans, int cap = TeamSlots.Count)
        {
            int max = MaxAi(humans, cap);
            if (wanted < 0) return 0;
            return wanted > max ? max : wanted;
        }

        /// <summary>AI 를 하나 더 넣을 자리가 있나.</summary>
        public static bool CanAdd(int requested, int humans, int cap = TeamSlots.Count)
            => requested < MaxAi(humans, cap);

        /// <summary>
        /// 사람이 들어와 <paramref name="humans"/> 명이 된 뒤의 AI 수.
        ///
        /// <para>백엔드 <c>Room.tryJoin</c> 과 같은 규칙이다 — 넘치는 만큼만 줄이고, 줄인 값을
        /// 그대로 둔다. 사람이 다시 나가도 AI 가 되살아나지 않는다.</para>
        /// </summary>
        public static int AfterHumanJoined(int requested, int humans, int cap = TeamSlots.Count)
        {
            int free = cap - humans;
            if (free < 0) free = 0;
            if (requested < 0) return 0;
            return requested > free ? free : requested;
        }

        /// <summary>
        /// 실제로 채울 수. 대기 AI 시험(<c>IdleTest</c>)은 예전처럼 빈 자리를 전부 채운다 —
        /// 개발용 동작을 바꾸지 않는다.
        /// </summary>
        public static int EffectiveRequest(int requested, bool idleTest, int cap = TeamSlots.Count)
            => idleTest ? cap : requested;

        /// <summary>사람이 자리 <paramref name="humanSeats"/> 개를 잡고 있을 때 AI 가 앉을 자리 수.</summary>
        public static int SeatTarget(int requested, int humanSeats, int cap = TeamSlots.Count)
        {
            int free = cap - humanSeats;
            int target = requested < free ? requested : free;
            return target < 0 ? 0 : target;
        }

        /// <summary>
        /// 지금 AI 를 띄울 자리를 <b>높은 번호부터</b> 골라 <paramref name="into"/> 에 적고 그 개수를 돌려준다.
        ///
        /// <para><b>왜 높은 번호부터인가.</b> 사람은 <see cref="TeamSlots.Claim"/> 에서 가장 낮은
        /// 빈 자리를 받는다. AI 가 낮은 자리부터 앉으면 늦게 붙는 사람과 부딪쳐, AI 가 밀려났다가
        /// 다른 자리에 다시 뜨며 색이 바뀐다 — 1~4 키로 색을 골라 말하는 기능(-738)이 엉뚱한
        /// 동료를 부른다. 반대 끝에서 채우면 사람 + AI 가 4를 넘지 않는 한 둘은 만나지 않는다.</para>
        ///
        /// <para>이미 떠 있거나 뜨는 중인 AI 도 센다. 안 세면 앉는 사이에 한 번 더 띄운다.
        /// 사람이 잡은 자리는 절대 돌려주지 않는다. 넘치는 AI 를 내리지는 않는다 — 판 중에
        /// 수가 줄어드는 정상 경로가 없다(서버가 대기 중에만 바꾸게 한다).</para>
        /// </summary>
        /// <param name="humanHeld">자리마다 사람이 잡고 있나(끊겨 돌아올 자리 포함)</param>
        /// <param name="aiHeldOrPending">자리마다 AI 가 몰고 있거나 뜨는 중인가</param>
        /// <param name="eligible">자리마다 지금 AI 를 띄워도 되나(<see cref="TeamSeats.NeedsAi"/>)</param>
        /// <param name="requested">채울 AI 수(<see cref="EffectiveRequest"/> 를 거친 값)</param>
        /// <param name="into">고른 자리 번호를 적을 곳. 길이만큼만 적는다</param>
        public static int SlotsToFill(bool[] humanHeld, bool[] aiHeldOrPending, bool[] eligible, int requested, int[] into)
        {
            int cap = humanHeld.Length;
            int humans = 0;
            int ais = 0;
            for (int slot = 0; slot < cap; slot++)
            {
                if (humanHeld[slot]) humans++;
                else if (aiHeldOrPending[slot]) ais++;
            }

            int need = SeatTarget(requested, humans, cap) - ais;
            int count = 0;
            for (int slot = cap - 1; slot >= 0 && count < need && count < into.Length; slot--)
            {
                if (!eligible[slot] || humanHeld[slot] || aiHeldOrPending[slot]) continue;
                into[count++] = slot;
            }
            return count;
        }

        /// <summary>
        /// 채울 수보다 더 떠 있는 AI 의 수. 정상 흐름에서는 늘 0 이다 — 0 이 아니면 부르는 쪽이
        /// 경고를 남긴다(내리지는 않는다).
        /// </summary>
        public static int Excess(bool[] humanHeld, bool[] aiHeldOrPending, int requested)
        {
            int cap = humanHeld.Length;
            int humans = 0;
            int ais = 0;
            for (int slot = 0; slot < cap; slot++)
            {
                if (humanHeld[slot]) humans++;
                else if (aiHeldOrPending[slot]) ais++;
            }

            int excess = ais - SeatTarget(requested, humans, cap);
            return excess > 0 ? excess : 0;
        }
    }
}
