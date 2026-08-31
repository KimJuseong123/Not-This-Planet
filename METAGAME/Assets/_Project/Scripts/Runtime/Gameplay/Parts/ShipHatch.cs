using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>발사 준비의 진행 상태.</summary>
    public enum BoardingState
    {
        /// <summary>탑승 조건이 갖춰지지 않았다.</summary>
        Closed,

        /// <summary>문이 열려 탑승할 수 있다.</summary>
        Open,

        /// <summary>전원이 탑승해 카운트다운이 돌고 있다.</summary>
        CountingDown,

        /// <summary>카운트다운이 끝나 하중 시험을 시작했다.</summary>
        Launching,
    }

    /// <summary>
    /// 최하단 엔진 모듈의 출입문. 기획(S15P21B202-107 §22.1 · §22.3 앞부분)의 탑승 흐름이다.
    ///
    /// 순서는 이렇다 — <see cref="ShipAssembly"/> 가 조건을 만족하면 문이 열리고, 플레이어가
    /// E 로 탑승하고, 접속 중인 전원이 탑승하면 3초 카운트다운이 돌고, 끝나면 문이 잠기고
    /// 저중력장이 꺼지며 <see cref="LaunchTest.Begin"/> 이 불린다.
    ///
    /// <b>탑승은 꾹 누르지 않는다.</b> <see cref="PartClamp"/> 는 "꾹 눌러 해제",
    /// <see cref="WeldPoint"/> 는 "꾹 눌러 채움" 이지만 문은 누르는 즉시 탑승이다
    /// (기획 §11.2 "우주선 입구: E — 탑승"). 그래서 <c>HoldSeconds</c> 를 0 으로 둔다.
    ///
    /// <b>인원 판정을 한 곳에 모았다</b>(<see cref="RequiredRiders"/>). 기획은 "접속 중인 모든
    /// 플레이어" 라고 하지만 멀티 동기화는 [Network] 파트의 몫이므로, 지금은 로컬 한 명으로
    /// 본다. 나중에 이 값 하나만 바꾸면 된다 — 여기저기서 인원을 세면 그때 전부 찾아야 한다.
    ///
    /// 이 컴포넌트가 하지 않는 것: 문 열림 애니메이션과 진입 연출(§22.1 후반),
    /// 탑승 궤도 카메라(§22.2), 실패·성공 연출(§22.4 후반 · §22.5).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipHatch : InteractableBase
    {
        [SerializeField] ShipAssembly _assembly;
        [SerializeField] LaunchTest _launch;

        /// <summary>
        /// 카운트다운을 시작하는 데 필요한 탑승 인원.
        /// 멀티가 붙으면 접속 인원으로 바뀐다 — 그때 고칠 곳은 여기 하나다.
        /// </summary>
        public int RequiredRiders { get; private set; } = 1;

        public BoardingState State { get; private set; } = BoardingState.Closed;

        /// <summary>지금 탑승한 인원.</summary>
        public int Riders { get; private set; }

        /// <summary>카운트다운 남은 시간. 돌고 있지 않으면 0 이다.</summary>
        public float Remaining { get; private set; }

        public void Configure(ShipAssembly assembly, LaunchTest launch, int requiredRiders = 1)
        {
            _assembly = assembly;
            _launch = launch;
            RequiredRiders = Mathf.Max(1, requiredRiders);
        }

        void Awake()
        {
            // 탭 상호작용이다. 문 앞에서 E 를 누르면 바로 탑승한다.
            SetHoldSeconds(0f);
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            // 발사가 시작된 뒤에는 문이 잠긴다(기획 §22.3).
            if (State == BoardingState.Launching)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            if (_assembly == null || !_assembly.IsBoardable)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            return InteractionAvailability.Allowed;
        }

        /// <summary>E 를 눌렀다. 타지 않았으면 타고, 탔으면 내린다.</summary>
        public override void OnCompleted(Interactor actor)
        {
            if (State == BoardingState.Launching) return;

            if (State == BoardingState.CountingDown)
            {
                // 기획 §22.3 — "카운트다운 중 누구든 E 로 하차하면 취소된다".
                Riders = Mathf.Max(0, Riders - 1);
                Remaining = 0f;
                State = BoardingState.Open;
                return;
            }

            Riders = Mathf.Min(RequiredRiders, Riders + 1);
            if (Riders < RequiredRiders) return;

            Remaining = PhysicsScale.LaunchCountdownSeconds;
            State = BoardingState.CountingDown;
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 물리 스텝마다 부른다. 런타임에서는 <c>FixedUpdate</c> 가 부르고, EditMode
        /// 테스트는 직접 부른다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            switch (State)
            {
                case BoardingState.Closed:
                case BoardingState.Open:
                    // 조건은 계속 깨질 수 있다. 파츠가 떨어지거나 탑이 기울면 문이 다시 닫힌다.
                    var boardable = _assembly != null && _assembly.IsBoardable;
                    if (!boardable) Riders = 0;

                    State = boardable ? BoardingState.Open : BoardingState.Closed;
                    return;

                case BoardingState.CountingDown:
                    // 카운트다운 중 탑이 무너지면 발사할 우주선이 없다.
                    if (_assembly == null || !_assembly.IsBoardable)
                    {
                        Riders = 0;
                        Remaining = 0f;
                        State = BoardingState.Closed;
                        return;
                    }

                    Remaining -= deltaTime;
                    if (Remaining > 0f) return;

                    BeginLaunch();
                    return;
            }
        }

        /// <summary>
        /// 카운트다운이 끝났다. 문을 잠그고 저중력장을 끈 뒤 하중 시험을 시작한다.
        ///
        /// 저중력장을 끄는 이유는 기획 §22.3 이 "문이 잠기고 저중력장을 강제로 끈다" 고
        /// 정하기 때문이다. 켜 둔 채로 시험하면 저중력장이 무게를 대신 들어 주어
        /// "자기 무게로 서 있는지" 를 못 본다.
        /// </summary>
        void BeginLaunch()
        {
            Remaining = 0f;
            State = BoardingState.Launching;

            var fields = LowGravityField.All;
            for (var i = fields.Count - 1; i >= 0; i--)
                if (fields[i] != null) fields[i].SetActive(false);

            if (_launch != null && _assembly != null) _launch.Begin(_assembly.Engine);
        }
    }
}
