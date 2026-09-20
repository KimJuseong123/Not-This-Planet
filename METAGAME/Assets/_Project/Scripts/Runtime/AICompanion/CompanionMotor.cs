using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.AICompanion
{
    /// <summary>Navigation chooses movement input; existing PlayerRig owns physics and animation.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed partial class CompanionMotor : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float _stopDistance = 1.8f;
        [SerializeField, Range(0.1f, 1f)] float _walkInput = 0.7f;
        PlayerRig _rig;
        Transform _target;
        NavMeshPath _path;
        float _nextPath;
        PartClamp _pin;
        readonly Queue<PartClamp> _pendingPins = new Queue<PartClamp>();
        bool _advanceBatch;
        int _completedPins;
        public int PendingPins => _pendingPins.Count;
        Vector3 _approach;
        enum PinPhase { Approach, Grip, Pull }
        PinPhase _pinPhase;
        float _phaseStart;
        float _taskStart;
        float _lastProgressTime;
        float _lastProgress;
        public CompanionOrder Order { get; private set; }
        public string Status { get; private set; } = "대기";
        public PlayerRig Rig => _rig;
        public PartClamp TargetPin => _pin;

        /// <summary>
        /// 할 일이 없을 때 따라다닐 대상. <c>null</c> 이면 그 자리에 선다.
        ///
        /// <para>값은 밖에서(자리 관리자) 가장 가까운 사람으로 갱신해 준다 — 이 어셈블리는
        /// 누가 사람인지 모른다.</para>
        /// </summary>
        public Transform StandbyAnchor { get; set; }

        /// <summary>
        /// 따라다닐 때 유지할 거리(m). 자리 관리자가 <b>AI 마다 다르게</b> 준다.
        ///
        /// <para>셋이 같은 거리를 노리면 한 점에 겹쳐 서로 밀어낸다. 조금씩 다르게 두면
        /// 줄줄이 늘어선다.</para>
        /// </summary>
        public float StandbyDistance { get; set; } = 2.5f;

        /// <summary>유지 거리보다 이만큼 더 벌어지면 따라 걷기 시작한다(m).</summary>
        ///
        /// <para>딱 맞춰 쫓으면 사람이 한 발 떼자마자 같이 움직여 들러붙은 것처럼 보인다.
        /// 여유를 둬야 사람이 서 있을 때 AI 도 선다.</para>
        const float StandbySlack = 1.2f;

        /// <summary>이보다 가까우면 비켜선다(m). 길을 막지 않으려는 것이다.</summary>
        const float StandbyNear = 1.6f;

        /// <summary>
        /// 사람이 <b>직접</b> "여기서 기다려" 라고 했나. 그때는 곁에 머무르지 않고 선 자리를
        /// 지킨다 — 명령이 느슨한 대기보다 우선이다.
        ///
        /// <para>명령인지 아닌지는 <see cref="SetOrder"/> 의 speaker 로 가른다. 작업이 끝나
        /// 내부적으로 Wait 로 돌아올 때는 speaker 가 없다.</para>
        /// </summary>
        bool _holdStill;

        /// <summary>길찾기에서 보는 몸 굵기(m). 바닥을 구울 때와 같은 값이어야 한다.</summary>
        public const float AgentRadius = 0.45f;

        /// <summary>길찾기에서 보는 키(m).</summary>
        public const float AgentHeight = 1.5f;

        /// <summary>
        /// 서로 비켜설 때의 우선순위(0~99, 낮을수록 세다). 자리 관리자가 AI 마다 다르게 준다.
        ///
        /// <para>같은 값이면 둘이 똑같이 양보하다 마주 보고 굳는다. 값을 달리 줘야 한쪽이
        /// 먼저 비킨다.</para>
        /// </summary>
        public int AvoidancePriority
        {
            get => _agent != null ? _agent.avoidancePriority : 50;
            set { if (_agent != null) _agent.avoidancePriority = Mathf.Clamp(value, 0, 99); }
        }

        NavMeshAgent _agent;

        public void Initialize(PlayerRig rig)
        {
            _rig = rig;
            _path = new NavMeshPath();

            Vector3 here = rig != null && rig.Hips != null ? rig.Hips.position : Vector3.zero;
            CompanionNavMesh.EnsureCovers(here);

            // 길찾기와 <b>서로 비켜서기</b>를 Unity 에 맡긴다. 직접 만들면 셋이 마주쳤을 때
            // 서로 밀고 굳는 것을 일일이 풀어야 한다.
            //
            // 다만 몸을 옮기는 것은 맡기지 않는다 — 우리 몸은 래그돌이고 InputFrame 으로만
            // 움직인다(CLAUDE.md: AI 입력은 기존 PlayerRig 경로를 쓴다). 그래서 위치·회전
            // 갱신을 끄고 <b>방향만</b> 받아 쓴다.
            _agent = rig.gameObject.AddComponent<NavMeshAgent>();
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
            _agent.radius = AgentRadius;
            _agent.height = AgentHeight;
            _agent.speed = 3.5f;
            _agent.acceleration = 12f;
            _agent.angularSpeed = 720f;
            _agent.autoBraking = false;
            _agent.autoRepath = true;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
            SyncAgent(here);

            SetOrder(CompanionOrder.Wait, null);
        }

        /// <summary>
        /// 길잡이를 몸이 실제로 서 있는 자리로 끌어다 놓는다.
        ///
        /// <para><c>updatePosition</c> 을 끄면 길잡이는 제자리에 머문 채 자기가 걸어간 줄
        /// 안다. 매번 맞춰 주지 않으면 몸과 어긋나 엉뚱한 방향을 내놓는다.</para>
        /// </summary>
        void SyncAgent(Vector3 position)
        {
            if (_agent == null || !_agent.enabled) return;

            if (!_agent.isOnNavMesh)
            {
                // 바닥 밖이다(떨어졌거나 아직 안 구워졌다). <b>바로 옆에 바닥이 있을 때만</b>
                // 옮겨 붙인다 — 멀리까지 찾아 옮기면 벽 너머로 건너뛰어, 길잡이가 벽 밖에서
                // 방향을 계산해 몸은 벽에 처박힌다. 가까이 없으면 그냥 두고, 부르는 쪽이
                // 곧장 걷기로 넘어간다.
                if (NavMesh.SamplePosition(position, out var spot, WarpRange, NavMesh.AllAreas))
                    _agent.Warp(spot.position);
                return;
            }

            _agent.nextPosition = position;
        }

        public void SetOrder(CompanionOrder order, Transform speaker)
        {
            Order = order;
            ResetToolJob();
            _pendingPins.Clear();
            _advanceBatch = false;
            _completedPins = 0;
            _pin = null;
            _target = order == CompanionOrder.Follow ? speaker : null;

            // 사람이 직접 "여기서 기다려" 라고 한 경우에만 선 자리를 지킨다. 작업이 끝나
            // 내부적으로 Wait 로 돌아온 것(speaker 없음)은 느슨한 대기로 이어진다.
            _holdStill = order == CompanionOrder.Wait && speaker != null;

            _nextPath = 0;
            Status = order == CompanionOrder.Follow ? "따라오는 중" : order == CompanionOrder.Cancel ? "작업 중단" : "여기서 대기";
            if (_rig != null) _rig.SetInput(default);
        }

        public bool TryStartPin(PartClamp pin, out string reason)
            => TryStartPin(pin, CompanionOrder.PullPin, false, out reason);

        public bool TryMoveToPin(PartClamp pin, out string reason)
            => TryStartPin(pin, CompanionOrder.MoveToPin, false, out reason);

        public bool TryStartNearbyPins(IReadOnlyList<PartClamp> pins, out string reason)
        {
            reason = "주변에 뽑을 고정못이 없어요";
            if (pins == null || pins.Count == 0) return false;
            if (!TryStartPin(pins[0], CompanionOrder.PullNearbyPins, false, out reason)) return false;
            // Bounded snapshot: moving the speaker never adds new work to an accepted request.
            for (int i = 1; i < pins.Count && i < 32; ++i) _pendingPins.Enqueue(pins[i]);
            reason = $"주변 고정못 {_pendingPins.Count + 1}개를 차례로 뽑을게요";
            return true;
        }

        bool TryStartPin(PartClamp pin, CompanionOrder order, bool continuing, out string reason)
        {
            reason = "뽑을 수 있는 고정못이 아니에요";
            if (_rig != null && _rig.HeldTool != null)
            { reason = "도구를 든 상태예요 · 먼저 내려놓아 주세요"; return false; }
            if (_rig == null || !CompanionPinTarget.Eligible(pin)) return false;
            Vector3 center = CompanionPinTarget.Center(pin);
            Vector3 outward = center - pin.Part.transform.position; outward.y = 0;
            if (outward.sqrMagnitude < 0.01f) outward = _rig.Hips.position - center;
            outward.y = 0;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.back;
            Vector3 approach = center + outward.normalized * 0.55f;

            // 갈 수 있는지 보기 전에 그 근처 바닥을 깔아 둔다. 안 그러면 아직 안 구운
            // 자리라는 이유로 "길이 없어요" 가 나온다.
            CompanionNavMesh.EnsureCovers(_rig.Hips.position, approach);

            if (!NavMesh.SamplePosition(approach, out var spot, 1.2f, NavMesh.AllAreas))
            { reason = "고정못 앞에 설 수 있는 곳이 없어요"; return false; }
            if (!NavMesh.SamplePosition(_rig.Hips.position, out var from, 2f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(from.position, spot.position, NavMesh.AllAreas, _path)
                || _path.status != NavMeshPathStatus.PathComplete)
            { reason = "고정못까지 갈 수 있는 길이 없어요"; return false; }
            if (!continuing) SetOrder(order, null);
            else { _nextPath = 0; _rig.SetInput(default); }
            _pin = pin; _approach = spot.position;
            _pinPhase = PinPhase.Approach;
            _phaseStart = _taskStart = _lastProgressTime = Time.time;
            _lastProgress = pin.Progress;
            Status = "고정못으로 이동 중";
            reason = order == CompanionOrder.MoveToPin ? "고정못 앞으로 가서 기다릴게요" : "고정못으로 이동해서 잡고 뒤로 당길게요";
            return true;
        }

        void FixedUpdate()
        {
            if (_rig == null || _rig.Hips == null) return;
            var input = new InputFrame { AimYaw = _rig.Hips.rotation.eulerAngles.y, AimFrom = _rig.Hips.position };
            if (IsToolOrder(Order))
            {
                TickTool(ref input);
                _rig.SetInput(input);
                return;
            }
            if (Order == CompanionOrder.PullPin || Order == CompanionOrder.MoveToPin || Order == CompanionOrder.PullNearbyPins)
            {
                if (_advanceBatch)
                {
                    _advanceBatch = false;
                    bool next = false;
                    while (_pendingPins.Count > 0)
                    {
                        var pin = _pendingPins.Dequeue();
                        if (!CompanionPinTarget.Eligible(pin)) continue;
                        if (!TryStartPin(pin, CompanionOrder.PullNearbyPins, true, out var reason))
                        { FinishPin("작업 실패 · " + reason); _rig.SetInput(default); return; }
                        next = true; break;
                    }
                    if (!next)
                    {
                        int completed = _completedPins;
                        SetOrder(CompanionOrder.Wait, null);
                        Status = $"작업 완료 · 고정못 {completed}개를 뽑았어요";
                    }
                    _rig.SetInput(default);
                    return;
                }
                TickPin(ref input);
                _rig.SetInput(input);
                return;
            }
            if (Order != CompanionOrder.Follow || _target == null)
            {
                if (Order == CompanionOrder.Follow) SetOrder(CompanionOrder.Cancel, null);
                TickStandby(ref input);
                _rig.SetInput(input); return;
            }
            if (Navigate(_target.position, _stopDistance, ref input)) Status = "도착 · 거리를 유지해요";
            else Status = input.Move.sqrMagnitude > 0 ? "따라오는 중" : "이동 가능한 길을 찾는 중";
            _rig.SetInput(input);
        }

        /// <summary>
        /// 할 일이 없을 때는 사람을 따라다닌다. <b>벌어지면 쫓아가고, 너무 붙으면 비켜선다.</b>
        ///
        /// <para>유지 거리에서 <see cref="StandbySlack"/> 만큼은 그냥 둔다 — 딱 맞춰 쫓으면
        /// 사람이 한 발 뗄 때마다 같이 움직여 들러붙은 것처럼 보이고, 멈춰 서 있을 때도
        /// 옆에서 꼼지락댄다.</para>
        /// </summary>
        void TickStandby(ref InputFrame input)
        {
            if (_holdStill || StandbyAnchor == null) return;

            Vector3 delta = StandbyAnchor.position - _rig.Hips.position;
            delta.y = 0;
            float distance = delta.magnitude;

            if (distance > StandbyDistance + StandbySlack)
            {
                Navigate(StandbyAnchor.position, StandbyDistance, ref input);
                Status = "따라가는 중";
                return;
            }

            if (distance < StandbyNear && distance > 0.05f)
            {
                // 길을 막고 있다. 경로를 찾을 것도 없이 사람 반대쪽으로 한 걸음 물러난다.
                input.AimYaw = Mathf.Atan2(-delta.x, -delta.z) * Mathf.Rad2Deg;
                input.Move = new Vector2(0, _walkInput * 0.5f);
                Status = "비켜서는 중";
                return;
            }

            // 사람을 바라보고 선다. 등지고 서 있으면 말을 걸어도 반응이 없어 보인다.
            if (distance > 0.05f) SteerTowards(delta, ref input);

            // 서 있는 동안에도 막힘 시계를 되감는다. 안 그러면 다시 걷는 첫 걸음에
            // "한참 못 나아갔다" 로 판정돼 곧바로 뛴다.
            ResetStuck(_rig.Hips.position);
            Status = "대기 중";
        }

        bool Navigate(Vector3 destination, float stopDistance, ref InputFrame input)
        {
            Vector3 position = _rig.Hips.position;
            Vector3 delta = destination - position;
            delta.y = 0;
            if (delta.magnitude <= stopDistance && Mathf.Abs(destination.y - position.y) < 1.5f) return true;

            // 넓은 맵에서는 걷다 보면 구워 둔 영역을 벗어난다. 나와 목적지를 모두 덮게 다시 굽는다.
            CompanionNavMesh.EnsureCovers(position, destination);
            SyncAgent(position);

            Vector3 steer = Vector3.zero;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.stoppingDistance = stopDistance;
                if (Time.time >= _nextPath)
                {
                    _nextPath = Time.time + 0.25f;
                    _agent.SetDestination(destination);
                }

                // desiredVelocity 에는 <b>서로 비켜서기까지 반영돼 있다</b>. 이것을 쓰려고
                // 길잡이를 붙였다 — 방향만 받아 우리 입력으로 옮긴다.
                steer = _agent.desiredVelocity;
                steer.y = 0;
            }

            // 길잡이가 방향을 못 내놓았다(바닥 밖·경로 없음). 곧장 걸어도 되는지 본다 —
            // <b>사이에 벽이 있으면 걷지 않는다.</b> 예전에는 무조건 직선으로 밀어서,
            // 실내에서 문으로 돌아 나오지 않고 벽에 처박혔다.
            if (steer.sqrMagnitude < 0.01f && !Blocked(position, destination)) steer = delta;

            if (steer.sqrMagnitude > 0.01f)
            {
                SteerTowards(steer, ref input);
                ApplyGait(delta.magnitude - stopDistance, ref input);
                TickStuck(position, ref input);
            }
            else
            {
                ResetStuck(position);
            }
            return false;
        }

        /// <summary>두 지점 사이가 길찾기 바닥에서 막혀 있나. 바닥이 없으면 막힌 것으로 본다.</summary>
        static bool Blocked(Vector3 from, Vector3 to)
        {
            if (!NavMesh.SamplePosition(from, out var a, 2f, NavMesh.AllAreas)) return true;
            if (!NavMesh.SamplePosition(to, out var b, 2f, NavMesh.AllAreas)) return true;
            return NavMesh.Raycast(a.position, b.position, out _, NavMesh.AllAreas);
        }

        /// <summary>
        /// 가려는 쪽으로 <b>몸을 돌려 가며</b> 걷는다.
        ///
        /// <para>예전에는 목표 방향을 그대로 <c>AimYaw</c> 에 꽂았다. 그러면 몸은 아직 안
        /// 돌았는데 옆으로 미끄러지는 상태가 되고, 그 상태는 걷기 애니메이션 조건
        /// (<c>RabbitLocomotion.CanAnimate</c>: 앞뒤로 갈 때만)에 걸리지 않는다. 그래서
        /// 다리가 멎은 채 스르륵 미끄러져 왔다.</para>
        ///
        /// <para>사람이 마우스를 돌리는 것처럼 <b>돌아가는 속도에 한계를 둔다.</b> 많이 꺾을
        /// 때는 속도도 줄인다 — 사람도 급히 꺾을 때 제자리에서 돌고 나서 간다.</para>
        /// </summary>
        void SteerTowards(Vector3 steer, ref InputFrame input)
        {
            float want = Mathf.Atan2(steer.x, steer.z) * Mathf.Rad2Deg;

            // <b>Hips 의 회전을 쓰면 안 된다.</b> 골반은 래그돌 뼈라 향한 방향과 무관하게
            // 돌아가 있다. 조준 기준 방향(PlanarForward)이 이 몸이 향한 쪽이다.
            Vector3 facing = _rig.PlanarForward;
            float have = facing.sqrMagnitude > 0.01f
                ? Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg
                : want;

            input.AimYaw = Mathf.MoveTowardsAngle(have, want, TurnDegreesPerSecond * Time.fixedDeltaTime);

            // 많이 꺾여 있으면 앞으로 덜 민다. 그래야 옆으로 미끄러지지 않는다.
            //
            // <b>너무 줄이면 도착을 못 한다.</b> 고정못 앞은 0.16m 까지 다가가야 하는데,
            // 속도를 크게 깎으면 그 거리를 좁히지 못하고 시간 초과로 실패한다(실측).
            float off = Mathf.Abs(Mathf.DeltaAngle(have, want));
            _turnEase = off > TurnEaseAngle
                ? Mathf.Lerp(1f, TurnEaseFloor, Mathf.InverseLerp(TurnEaseAngle, 150f, off))
                : 1f;
        }

        /// <summary>몸이 도는 속도 한계(초당 도). 사람이 마우스로 도는 정도로 잡는다.</summary>
        const float TurnDegreesPerSecond = 540f;

        /// <summary>이보다 많이 꺾여 있으면 속도를 줄이기 시작한다(도).</summary>
        const float TurnEaseAngle = 60f;

        /// <summary>돌아설 때도 이 정도는 앞으로 민다. 더 줄이면 정밀 접근이 시간 초과된다.</summary>
        const float TurnEaseFloor = 0.6f;

        /// <summary>이번 스텝에 앞으로 미는 정도(0~1). 많이 꺾여 있으면 작아진다.</summary>
        float _turnEase = 1f;

        /// <summary>
        /// 남은 거리에 맞춰 <b>걷기와 달리기를 가른다.</b>
        ///
        /// <para>예전에는 늘 이동 입력을 0.7 만 넣고 달리기를 쓰지 않았다. 사람이 걷기만 해도
        /// (2.9m/s) AI 는 그보다 느려 계속 뒤처졌고, 달리면(4.6m/s) 아예 따라잡지 못했다.</para>
        ///
        /// <para>가까우면 천천히 걷는다 — 다 와서까지 뛰면 사람을 들이받는다.</para>
        /// </summary>
        void ApplyGait(float remaining, ref InputFrame input)
        {
            // 많이 꺾여 있으면 뛰지 않는다 — 돌면서 뛰면 옆으로 미끄러진다.
            if (remaining > RunBeyond && _turnEase > 0.9f)
            {
                input.Run = true;
                input.Move = new Vector2(0, 1f);
                return;
            }

            input.Run = false;
            float forward = remaining > EaseWithin ? 1f : _walkInput;
            input.Move = new Vector2(0, forward * _turnEase);
        }

        /// <summary>이보다 더 남으면 달린다(m). 사람이 달려도 따라잡을 수 있어야 한다.</summary>
        const float RunBeyond = 3.5f;

        /// <summary>이 안으로 들어오면 속도를 늦춘다(m). 다 와서 뛰면 들이받는다.</summary>
        const float EaseWithin = 1.2f;

        /// <summary>길잡이를 바닥으로 끌어다 붙일 때 찾아보는 거리(m). 넓히면 벽을 건너뛴다.</summary>
        const float WarpRange = 1.2f;

        /// <summary>
        /// <b>막히면 뛰어 본다.</b> 턱이나 낮은 장애물은 길찾기 바닥에 안 잡히는 일이 많다.
        ///
        /// <para>옆으로 비집는 것만으로는 못 넘는 자리가 있어서, 비집기와 번갈아 쓴다.</para>
        /// </summary>
        void TickHop(ref InputFrame input)
        {
            if (!_rig.Grounded) return;
            if (Time.time < _nextHop) return;

            _nextHop = Time.time + HopInterval;
            input.Jump = true;
        }

        /// <summary>막혔을 때 뛰어 보는 간격(초).</summary>
        const float HopInterval = 0.9f;

        float _nextHop;

        /// <summary>
        /// <b>가려는데 못 가고 있으면 옆으로 한 걸음 낸다.</b>
        ///
        /// <para>길잡이가 방향을 내놓아도 몸이 실제로 못 나아가는 자리가 있다 — 좁은 틈,
        /// 다른 몸에 낀 경우. 앞으로만 계속 밀면 영영 낀 채로 있는다. 사람이 비켜 줘야
        /// 풀리는 것이 그 증상이다.</para>
        /// </summary>
        void TickStuck(Vector3 position, ref InputFrame input)
        {
            if ((position - _stuckFrom).sqrMagnitude > StuckRadius * StuckRadius)
            {
                ResetStuck(position);
                return;
            }

            if (Time.time - _stuckSince < StuckSeconds) return;

            // 낀 쪽 반대가 어딘지 알 수 없으므로 한쪽으로 돈다. 몇 초마다 방향을 바꿔
            // 한쪽으로만 밀어붙이다 같은 자리에 머무는 것을 피한다.
            float side = Mathf.Repeat(Time.time - _stuckSince, StuckSeconds * 2) < StuckSeconds ? 1f : -1f;
            input.AimYaw += side * 70f;

            // 턱이나 낮은 장애물은 옆으로 비집어도 못 넘는다. 같이 뛰어 본다.
            TickHop(ref input);
            Status = "길을 비집는 중";
        }

        /// <summary>이 거리 안에 머물면 못 나아간 것으로 본다(m).</summary>
        const float StuckRadius = 0.35f;

        /// <summary>이만큼 못 나아가면 옆으로 낸다(초).</summary>
        const float StuckSeconds = 1.2f;

        /// <summary>
        /// 막힘 판정을 처음부터 다시 잰다.
        ///
        /// <para><b>가만히 있을 때도 불러야 한다.</b> 서 있는 동안 갱신하지 않으면 시계가
        /// 계속 흘러, 걷기 시작하는 순간 "1.2초째 못 나아갔다" 로 판정돼 <b>첫 걸음에
        /// 곧바로 뛴다.</b></para>
        /// </summary>
        void ResetStuck(Vector3 position)
        {
            _stuckFrom = position;
            _stuckSince = Time.time;
        }

        Vector3 _stuckFrom;
        float _stuckSince;

        void TickPin(ref InputFrame input)
        {
            if (_pin == null || !_pin.isActiveAndEnabled)
            { FinishPin("작업 실패 · 대상 고정못이 없어졌어요"); return; }
            if (_pin.IsReleased)
            { FinishPin("작업 완료 · 고정못을 뽑았어요", true); return; }
            if (_rig.Downed || _rig.IsRiding || Time.time - _taskStart > 30)
            { FinishPin("작업 실패 · 이동/작업 상태를 확인해 주세요"); return; }
            if (_pinPhase == PinPhase.Approach)
            {
                if (!Navigate(_approach, 0.16f, ref input))
                { Status = "고정못으로 이동 중"; return; }
                if (Order == CompanionOrder.MoveToPin)
                { input = default; FinishPin("도착 · 고정못 앞에서 기다려요"); return; }
                _pinPhase = PinPhase.Grip; _phaseStart = Time.time;
            }
            // Use the same camera-less aim and hand-grip input path as a server-side human player.
            input.AimFrom = _rig.Doll.Head.position;
            Vector3 toPin = CompanionPinTarget.Center(_pin) - input.AimFrom;
            var aim = Quaternion.LookRotation(toPin).eulerAngles;
            input.AimYaw = aim.y;
            input.AimPitch = Mathf.DeltaAngle(0, aim.x);
            if (_rig.ClampGrabbed && _rig.ClampGrabTarget == _pin)
            {
                if (_pinPhase != PinPhase.Pull) _lastProgressTime = Time.time;
                _pinPhase = PinPhase.Pull;
                input.GrabLeft = input.GrabRight = true;
                input.Move = Vector2.down;
                Status = "고정못을 잡고 뒤로 당기는 중";
                if (_pin.Progress > _lastProgress + 0.001f)
                { _lastProgress = _pin.Progress; _lastProgressTime = Time.time; }
                if (Time.time - _lastProgressTime > 4)
                { input = default; FinishPin("작업 실패 · 잡았지만 당기기가 진행되지 않아요"); }
                return;
            }
            if (_pinPhase == PinPhase.Pull) { _pinPhase = PinPhase.Grip; _phaseStart = Time.time; }
            Status = "고정못에 손을 뻗는 중";
            // Never blindly click: another prop or player could have entered the aim ray.
            if (_rig.CurrentInteractionTarget.Clamp == _pin && _rig.CurrentInteractionTarget.CanInteract)
                input.GrabLeft = input.GrabRight = true;
            if (Time.time - _phaseStart > 5)
            { input = default; FinishPin("작업 실패 · 손이 고정못에 닿지 않아요"); }
        }

        void FinishPin(string status, bool success = false)
        {
            if (success && Order == CompanionOrder.PullNearbyPins)
            {
                ++_completedPins;
                _pin = null;
                _advanceBatch = true;
                _rig.SetInput(default);
                Status = $"{_completedPins}개 완료 · 다음 고정못 확인 중";
                return;
            }
            SetOrder(CompanionOrder.Wait, null);
            Status = status;
        }

        void OnDisable() { if (_rig != null) _rig.SetInput(default); }
        void OnDestroy()
        {
            // 길찾기 바닥은 이제 모두가 함께 쓴다(CompanionNavMesh) — 여기서 걷어내면
            // 남은 AI 들의 길이 같이 사라진다. 내 길잡이만 치운다.
            if (_agent != null) Destroy(_agent);
        }
    }
}
