using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
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

        /// <summary>
        /// 발사에 실패해 수습 중이다. 플레이어가 사출되고 운석 공세가 다시 돈다.
        ///
        /// <see cref="Closed"/> 로 바로 돌아가지 않는 이유는 <b>실패가 스테이지
        /// 초기화가 아니기 때문</b>이다(기획 §22.4). 무너진 탑을 다시 판정해 조건이
        /// 갖춰지면 그때 문이 다시 열린다.
        /// </summary>
        Failing,
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
        [SerializeField] MeteorSpawner _spawner;

        /// <summary>탑승한 플레이어와 탑승 직전 발 위치. 하선·사출에서 쓴다.</summary>
        readonly List<(PlayerRig Rig, Vector3 Foot, Quaternion Facing)> _boarded = new();

        SpaceshipPart _ridden;
        float _restTime;

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

        public void Configure(ShipAssembly assembly, LaunchTest launch,
            MeteorSpawner spawner = null, int requiredRiders = 1)
        {
            _assembly = assembly;
            _launch = launch;
            _spawner = spawner;
            RequiredRiders = Mathf.Max(1, requiredRiders);
        }

        /// <summary>실려 가는 최상단 견인 조종 모듈. 타고 있지 않으면 null.</summary>
        public SpaceshipPart RiddenModule => _ridden;

        void Awake()
        {
            // 탭 상호작용이다. 문 앞에서 E 를 누르면 바로 탑승한다.
            SetHoldSeconds(0f);
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            // 발사가 시작된 뒤에는 문이 잠긴다(기획 §22.3). 수습 중에도 잠겨 있다.
            if (State == BoardingState.Launching || State == BoardingState.Failing)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            if (_assembly == null || !_assembly.IsBoardable)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            return InteractionAvailability.Allowed;
        }

        /// <summary>E 를 눌렀다. 타지 않았으면 타고, 탔으면 내린다.</summary>
        public override void OnCompleted(Interactor actor)
        {
            if (State == BoardingState.Launching || State == BoardingState.Failing) return;

            if (State == BoardingState.CountingDown)
            {
                // 기획 §22.3 — "카운트다운 중 누구든 E 로 하차하면 취소된다".
                DisembarkAll();
                Remaining = 0f;
                State = BoardingState.Open;
                return;
            }

            if (!Embark(actor)) return;
            if (Riders < RequiredRiders) return;

            Remaining = PhysicsScale.LaunchCountdownSeconds;
            State = BoardingState.CountingDown;
        }

        /// <summary>
        /// 플레이어를 최상단 견인 조종 모듈에 붙인다(기획 §22.4).
        ///
        /// 플레이어를 못 찾으면 인원만 센다. 상호작용 없이 <c>OnCompleted</c> 를 부르는
        /// 검증이 있고, 멀티에서 원격 플레이어를 세는 경로도 그렇게 된다.
        /// </summary>
        bool Embark(Interactor actor)
        {
            if (Riders >= RequiredRiders) return false;

            var module = TopModule();
            if (module == null) return false;

            var rig = actor != null ? actor.GetComponentInParent<PlayerRig>() : null;
            if (rig != null)
            {
                if (rig.IsRiding) return false;

                var foot = rig.Doll != null && rig.Doll.Hips != null
                    ? rig.Doll.Hips.position - Vector3.up * RagdollBuilder.HipsY
                    : transform.position;
                var facing = rig.Doll != null && rig.Doll.Hips != null
                    ? rig.Doll.Hips.rotation
                    : transform.rotation;

                var body = module.GetComponent<Rigidbody>();
                if (body == null || !rig.TryBoardShip(body, module.transform.position)) return false;

                _boarded.Add((rig, foot, facing));
            }

            _ridden = module;
            Riders++;
            return true;
        }

        /// <summary>탑승한 전원을 원래 자리로 돌려놓는다.</summary>
        void DisembarkAll()
        {
            for (var i = 0; i < _boarded.Count; i++)
            {
                var entry = _boarded[i];
                if (entry.Rig != null) entry.Rig.LeaveShip(entry.Foot, entry.Facing);
            }

            _boarded.Clear();
            _ridden = null;
            Riders = 0;
        }

        /// <summary>
        /// 최상단 파츠. 기획 §22.4 가 플레이어가 타는 곳을 최상단 견인 조종 모듈로 정한다.
        /// </summary>
        SpaceshipPart TopModule()
        {
            if (_assembly == null) return null;

            var tower = _assembly.Tower;
            return tower.Count > 0 ? tower[tower.Count - 1] : null;
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
                        DisembarkAll();
                        Remaining = 0f;
                        State = BoardingState.Closed;
                        return;
                    }

                    Remaining -= deltaTime;
                    if (Remaining > 0f) return;

                    BeginLaunch();
                    return;

                case BoardingState.Launching:
                    WatchLaunch(deltaTime);
                    return;

                case BoardingState.Failing:
                    // 수습이 끝났다. 다시 판정해 조건이 갖춰지면 문이 열린다 —
                    // 기획 §22.4 "발사 실패는 스테이지 초기화가 아니며 … 다시 시도한다".
                    State = _assembly != null && _assembly.IsBoardable
                        ? BoardingState.Open
                        : BoardingState.Closed;
                    return;
            }
        }

        /// <summary>
        /// 발사 중 하중 시험을 지켜본다.
        ///
        /// 실패해도 그 순간에 사출하지 않는다. 기획 §22.4 가 "해당 모듈이 지면에 닿는
        /// 순간 모든 플레이어가 사방으로 튀어나온다" 로 정하므로, 접합부가 터진 뒤
        /// 모듈이 떨어지는 동안은 <b>타고 있어야 한다</b>. 그 낙하가 실패의 연출이다.
        /// </summary>
        void WatchLaunch(float deltaTime)
        {
            if (_launch == null)
            {
                FinishFailure();
                return;
            }

            if (_launch.State != LaunchState.Failed) return;

            _restTime += deltaTime;
            if (_ridden != null && !HasLanded(_ridden)) return;

            FinishFailure();
        }

        /// <summary>
        /// 낙하가 끝났는가.
        ///
        /// 처음에는 지면 높이만 봤다. <b>틀렸다</b> — 최상단 모듈이 잔해 위에 얹혀 멈추면
        /// 지면 높이에 영원히 닿지 않는다. 견인 조종 모듈 반높이 1.22m 아래에 엔진 모듈
        /// 2.16m 가 깔리면 밑면이 2.16m 에서 멈춘다. 그러면 사출이 오지 않고 플레이어가
        /// 잔해 위에 매달린 채로 남는다.
        ///
        /// 기획 §22.4 의 "해당 모듈이 지면에 닿는 순간" 이 뜻하는 것은 낙하가 끝나는
        /// 순간이다. 그래서 지면 높이와 정지, 둘 중 먼저 오는 것을 쓴다.
        /// </summary>
        bool HasLanded(SpaceshipPart module)
        {
            var spec = PartSpec.Find(module.name);
            var half = spec != null ? spec.Height * 0.5f : PhysicsScale.EjectGroundClearance;
            if (module.transform.position.y - half <= PhysicsScale.EjectGroundClearance) return true;

            var body = module.GetComponent<Rigidbody>();
            if (body == null) return true;

            if (body.linearVelocity.magnitude > PhysicsScale.EjectRestSpeed)
            {
                _restTime = 0f;
                return false;
            }

            return _restTime >= PhysicsScale.EjectRestSeconds;
        }

        /// <summary>
        /// 실패 수습(기획 §22.4). 플레이어를 사방으로 튕기고 운석 공세를 다시 시작한다.
        ///
        /// 파츠·장비·운석 흔적은 건드리지 않는다. <b>스테이지 초기화가 아니다.</b>
        /// 무너진 자리에 그대로 남아야 절단·재배치·재용접으로 다시 시도할 수 있다.
        /// </summary>
        void FinishFailure()
        {
            var center = _ridden != null ? _ridden.transform.position : transform.position;

            for (var i = 0; i < _boarded.Count; i++)
            {
                var rig = _boarded[i].Rig;
                if (rig == null) continue;

                // 사방으로 흩어지게 인원 수만큼 각도를 나눈다. 한 방향으로 몰면 겹친다.
                var angle = _boarded.Count <= 1 ? 0f : 360f * i / _boarded.Count;
                var away = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                if (rig.Doll != null && rig.Doll.Hips != null)
                {
                    var offset = rig.Doll.Hips.position - center;
                    offset.y = 0f;
                    if (offset.sqrMagnitude > 0.0001f) away = offset.normalized;
                }

                rig.EjectFromShip(away);
            }

            _boarded.Clear();
            _ridden = null;
            Riders = 0;
            Remaining = 0f;
            _restTime = 0f;

            if (_spawner != null) _spawner.SetPaused(false);

            State = BoardingState.Failing;
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

            // 발사 중에는 새 운석을 예고하지 않는다. 이미 떨어지는 것은 그대로 둔다 —
            // 공중에서 멈춰 세우면 하늘에 운석이 박혀 있게 된다.
            if (_spawner != null) _spawner.SetPaused(true);

            if (_launch != null && _assembly != null) _launch.Begin(_assembly.Engine);
        }
    }
}
