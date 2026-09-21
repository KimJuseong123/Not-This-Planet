using System.Collections.Generic;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>어떤 순간에 한 줄을 띄우는가 (S15P21B202-665).</summary>
    public enum OnboardingCue
    {
        /// <summary>판이 시작됐다.</summary>
        Start,

        /// <summary>파츠를 처음 찾았다 — 아직 고정못에 물려 있다.</summary>
        PartFound,

        /// <summary>파츠를 처음 쌓았다 — 아직 용접하지 않았다.</summary>
        PartStacked,
    }

    /// <summary>
    /// 필요한 순간에 한 줄로 알려 준다 (S15P21B202-665).
    ///
    /// <para><b>안내가 전부 시작 0초에 몰려 있었다.</b> S15P21B202-657 이 태블릿에서
    /// 정보를 꺼낼 수 있게 했지만, 꺼낼 생각을 해야 닿는다. 처음 하는 사람은 그
    /// 생각을 하지 않는다 — 이 클래스는 <b>찾아가지 않아도 오게</b> 한다.</para>
    ///
    /// <para><b>게임 상태를 바꾸지 않는다.</b> 표시할 글자와 "다음에 태블릿을 열면
    /// 어느 쪽" 만 들고 있다. 그래서 씬 없이 잴 수 있다.</para>
    ///
    /// <para><b>각자 자기 화면이다.</b> 네 명이 서로 다른 순간에 처음 파츠를 찾으므로
    /// 무엇을 봤는지는 동기화하지 않는다.</para>
    /// </summary>
    public sealed class OnboardingNotices
    {
        /// <summary>한 줄이 떠 있는 시간. 운석이 떨어지는 중에는 길면 못 읽는다.</summary>
        public const float VisibleSeconds = 4f;

        readonly HashSet<OnboardingCue> _fired = new();
        readonly Queue<OnboardingCue> _waiting = new();
        OnboardingCue _current;
        float _seconds;

        /// <summary>지금 화면에 띄울 줄. 없으면 <c>null</c>.</summary>
        public string Text { get; private set; }

        /// <summary>
        /// 첫 안내인가. <b>이것은 시간이 지나도 안 사라진다</b> — 태블릿을 열 때까지
        /// 남는다. 한 번 눌러 본 키는 기억하기 때문이다.
        /// </summary>
        public bool HoldingForTablet { get; private set; }

        /// <summary>다음에 태블릿을 열면 펼 안내 쪽. 없으면 −1.</summary>
        public int PendingGuidePage { get; private set; } = -1;

        /// <summary>
        /// 그 순간에 맞는 안내 쪽 (<see cref="TabletGuideState"/> 의 쪽 번호).
        /// 0 조립 · 1 고정못 · 2 운석 · 3 용접.
        /// </summary>
        public static int GuidePageFor(OnboardingCue cue) => cue switch
        {
            OnboardingCue.Start => 0,
            OnboardingCue.PartFound => 1,
            OnboardingCue.PartStacked => 3,
            _ => -1,
        };

        /// <summary>
        /// 그 순간에 띄울 글. <b>한 줄을 넘기지 않는다</b> — 읽을 시간이 없다.
        ///
        /// <para>첫 줄만 꼬리표 대신 <b>누르라는 말</b>이 붙는다. "TAB" 글자만 띄웠을
        /// 때 아무도 안 눌렀고(S15P21B202-657), 그래서 동사를 쓴다.</para>
        /// </summary>
        public static string TextFor(OnboardingCue cue) => cue switch
        {
            OnboardingCue.Start =>
                "파츠를 모아 우주선을 조립하고 탈출하십시오.   [" + Controls.Tablet + "] 을 눌러 태블릿을 여십시오",
            OnboardingCue.PartFound =>
                "고정못을 잡고 뒤로 당겨 파츠를 떼어내십시오.   [" + Controls.Tablet + "] 다시 보기",
            OnboardingCue.PartStacked =>
                "쌓기만 하면 흔들립니다. 맞닿은 곳을 용접하십시오.   [" + Controls.Tablet + "] 다시 보기",
            _ => string.Empty,
        };

        /// <summary>
        /// 그 순간이 왔다. <b>한 판에 한 번만 뜬다</b> — 두 번째부터 거슬리는 순간
        /// 아무도 안 읽는다. 이미 하나가 떠 있으면 줄을 세운다(덮어쓰지 않는다).
        /// </summary>
        public void Raise(OnboardingCue cue)
        {
            if (!_fired.Add(cue)) return;
            PendingGuidePage = GuidePageFor(cue);
            if (Text == null) Show(cue);
            else _waiting.Enqueue(cue);
        }

        /// <summary>
        /// 시간을 흘린다. <b>보이지 않는 동안에는 부르지 않는다</b> — 태블릿을 열어
        /// 놓은 사이에 사라지면 그 안내는 아무도 못 본 것이 된다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (Text == null || HoldingForTablet) return;
            _seconds -= deltaTime;
            if (_seconds > 0f) return;
            if (_waiting.Count > 0) Show(_waiting.Dequeue());
            else Text = null;
        }

        /// <summary>태블릿을 열었다. 첫 안내를 붙잡고 있었다면 이제 놓는다.</summary>
        public void OnTabletOpened()
        {
            if (!HoldingForTablet) return;
            HoldingForTablet = false;
            _seconds = 0f;
            if (_waiting.Count > 0) Show(_waiting.Dequeue());
            else Text = null;
        }

        /// <summary>펼 쪽을 가져간다. 한 번 가져가면 사라진다.</summary>
        public int TakeGuidePage()
        {
            var page = PendingGuidePage;
            PendingGuidePage = -1;
            return page;
        }

        public void ResetRun()
        {
            _fired.Clear();
            _waiting.Clear();
            Text = null;
            HoldingForTablet = false;
            PendingGuidePage = -1;
            _seconds = 0f;
        }

        void Show(OnboardingCue cue)
        {
            _current = cue;
            Text = TextFor(cue);
            _seconds = VisibleSeconds;
            HoldingForTablet = cue == OnboardingCue.Start;
        }

        /// <summary>지금 떠 있는 안내. 검증이 읽는다.</summary>
        public OnboardingCue Current => _current;
    }
}
