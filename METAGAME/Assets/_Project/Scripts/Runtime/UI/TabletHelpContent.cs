using Metagame.Core;

namespace Metagame.UI
{
    /// <summary>
    /// 조작 안내 문구. <b>여기 한 곳에만 적는다</b> (S15P21B202-657).
    ///
    /// <para>예전에는 같은 목록이 <see cref="Stage1TabletScreen"/> 과
    /// <see cref="Stage1ExplorationHud"/> 에 따로 있었고, 태블릿 안에 쓰이지도 않는
    /// 세 번째 사본까지 있었다. 그래서 <b>화면마다 다른 키를 가르쳤다</b> — 태블릿은
    /// 동료 구조를 <c>E</c> 라고 적었는데 실제 키는 <c>G</c> 다.</para>
    ///
    /// <para><b>키 이름은 <see cref="Controls"/> 에서 읽는다.</b> 손으로 적으면 키를
    /// 바꾼 사람에게 안내가 거짓말을 한다 — 이 이슈가 고치는 것이 정확히 그것이다.
    /// 마우스처럼 바인딩이 없는 것만 글자로 적는다.</para>
    /// </summary>
    public static class TabletHelpContent
    {
        public readonly struct Entry
        {
            public readonly string Keys;
            public readonly string Description;

            public Entry(string keys, string description)
            {
                Keys = keys;
                Description = description;
            }
        }

        /// <summary>한 쪽에 들어갈 수 있는 최대 줄 수. 카드를 이 수만큼 만들어 둔다.</summary>
        public const int MaxRows = 12;

        /// <summary>
        /// 키 이름을 사람이 읽는 말로. <c>KeyCode.ToString()</c> 을 그대로 쓰면
        /// "LeftShift" · "LeftControl" · "Escape" 가 되어 <b>지금보다 읽기 나빠진다.</b>
        /// </summary>
        public static string Name(UnityEngine.KeyCode key) => key switch
        {
            UnityEngine.KeyCode.LeftShift or UnityEngine.KeyCode.RightShift => "Shift",
            UnityEngine.KeyCode.LeftControl or UnityEngine.KeyCode.RightControl => "Ctrl",
            UnityEngine.KeyCode.LeftAlt or UnityEngine.KeyCode.RightAlt => "Alt",
            UnityEngine.KeyCode.Escape => "Esc",
            UnityEngine.KeyCode.Return => "Enter",
            _ => key.ToString(),
        };

        public static Entry[] Movement() => new[]
        {
            new Entry("WASD", "이동"),
            new Entry(Name(Controls.Run), "달리기"),
            new Entry(Name(Controls.Jump), "점프"),
            // S15P21B202-616 에서 엎드리기를 넣고 안내를 안 고쳤다. 폭풍을 피할 수
            // 있다는 것이 이 기능의 존재 이유라, 키만 적으면 쓸 이유를 모른다.
            new Entry(Name(Controls.Prone), "엎드리기 — 엎드리면 모래폭풍을 피한다"),
            new Entry(Name(Controls.FieldToggle), "저중력장 켜기·끄기"),
            new Entry("마우스", "시점과 캐릭터 정면·조준 기준"),
            new Entry(Name(Controls.FreeLook), "시점만 자유롭게 돌리기"),
        };

        public static Entry[] Interaction() => new[]
        {
            new Entry("좌·우클릭", "고정쇠에 손을 결합하고 반대 방향으로 뒤로 이동"),
            new Entry(Name(Controls.ArmsUp), "팔을 위로 올려 레일·케이블 잡기"),
            new Entry("W + 잡기", "잡은 레일·케이블을 따라 기어오르기"),
            // "내려놓기" 로는 버리는 방법인 줄 모른다 — 처음 하는 사람이 못 찾았다.
            new Entry(Name(Controls.ToolAction) + " 짧게", "들고 있는 것 버리기"),
            new Entry(Name(Controls.ToolAction) + " 길게", "충전 후 바라보는 방향으로 던지기"),
        };

        public static Entry[] Transport() => new[]
        {
            // 예전에는 이 줄이 "동료 구조" 까지 떠안고 있었다. 구조는 E 가 아니다.
            new Entry(Name(Controls.TransportAction), "장비 줍기·교환, 운반 장치 탑승·하차"),
            new Entry(Name(Controls.RescueTeammate), "쓰러진 동료 일으키기"),
            new Entry("WASD", "탑승 중 운반 장치 운전"),
            new Entry("좌·우클릭", "탑승 중 견인 빔 사용"),
            new Entry(Name(Controls.Jump) + " / " + Name(Controls.ArmsUp), "탑승 중 상승 / 하강"),
            new Entry(Name(Controls.StuckEscape), "그 자리에서 다시 서기"),
            new Entry(Name(Controls.Restart), "처음부터 다시 시작"),
            new Entry(Name(Controls.Tablet), "태블릿 열기·닫기"),
            new Entry("← → / A D", "이전·다음 태블릿 페이지"),
            new Entry(Name(Controls.FreeCursor), "커서 잠금 풀기"),
        };

        /// <summary>조작 안내 쪽(1~3)의 목록. 다른 쪽은 이동 안내를 돌려준다.</summary>
        public static Entry[] Page(int page) => page switch
        {
            1 => Movement(),
            2 => Interaction(),
            3 => Transport(),
            _ => Movement(),
        };

        /// <summary>안내에 쓰이는 모든 글자. 폰트가 이만큼은 갖고 있어야 네모가 안 뜬다.</summary>
        public static void AppendGlyphs(System.Text.StringBuilder text)
        {
            text.Append("●○|[]");     // 쪽 표시와 키 괄호도 글자다 — 빠지면 네모가 뜬다
            for (var page = 1; page <= 3; page++)
            {
                var entries = Page(page);
                for (var i = 0; i < entries.Length; i++)
                    text.Append(entries[i].Keys).Append(entries[i].Description);
            }
        }

        /// <summary>
        /// 쪽 표시 — <b>안내 4 · 진행도 1 · 조작 3 을 한 줄로</b> (S15P21B202-657).
        ///
        /// <para>구분선을 넣는다. 점 여덟 개가 나란히 있으면 "얼마나 남았지" 가 안
        /// 읽히는데, 세 덩어리로 끊으면 지금 어느 구역에 있는지가 보인다.</para>
        /// </summary>
        public static string PageDots(bool inGuide, int page)
        {
            var text = new System.Text.StringBuilder(28);
            for (var i = 0; i < Metagame.Gameplay.TabletGuideState.PageCount; i++)
                text.Append(inGuide && i == page ? '●' : '○').Append(' ');
            text.Append("| ").Append(!inGuide && page == 0 ? '●' : '○').Append(" | ");
            for (var i = 1; i < Metagame.Gameplay.Stage1Exploration.TabletPageCount; i++)
                text.Append(!inGuide && i == page ? '●' : '○').Append(' ');
            return text.ToString();
        }

        /// <summary>조작 안내 쪽의 제목.</summary>
        public static string PageTitle(int page) => page switch
        {
            0 => "탐사 태블릿 · 파츠 상태",
            1 => "조작 안내 · 이동과 몸",
            2 => "조작 안내 · 손과 도구",
            3 => "조작 안내 · 운반과 복구",
            _ => "탐사 태블릿",
        };
    }
}
