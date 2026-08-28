using System;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 견인 광선. 조준선 앞의 파츠를 붙잡아 기준점 앞의 목표점으로 끌어온다.
    ///
    /// 빔의 물리와 빔을 들고 다니는 방식을 분리했다. 이 컴포넌트는 기준점 Transform 하나만
    /// 알고 있으며, 그 기준점이 비행접시에 붙는지 손에 드는 장치에 붙는지는 도구 프리팹이 정한다.
    ///
    /// Interactor 와 같은 규칙을 따른다. 입력을 알지 못하고
    /// 소유자가 <see cref="Tick"/> 에 시간을 넣어 준다. 그래서 EditMode 에서
    /// Physics.Simulate 와 함께 검증할 수 있다.
    ///
    /// 파츠의 질량과 레이어는 절대 직접 건드리지 않는다. 상태 전환은
    /// <see cref="SpaceshipPart.TryTransitionTo"/> 하나로만 요청한다.
    /// 그러지 않으면 상태와 물리 표현이 어긋난다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TractorBeam : MonoBehaviour
    {
        const int HitBufferSize = 8;

        /// <summary>조준선의 굵기. 큰 파츠를 겨눌 때 너무 예민하지 않도록 구체로 쏜다.</summary>
        const float CastRadius = 0.5f;

        [SerializeField]
        [Tooltip("조준의 기준. 비우면 이 오브젝트의 Transform 을 쓴다.")]
        Transform _origin;

        [SerializeField, Min(0f)] float _range = PhysicsScale.BeamRange;
        [SerializeField, Min(0f)] float _holdDistance = PhysicsScale.BeamHoldDistance;
        [SerializeField, Min(0f)] float _accel = PhysicsScale.BeamAccel;
        [SerializeField, Min(0f)] float _damp = PhysicsScale.BeamDamp;
        [SerializeField, Min(0f)] float _accelMax = PhysicsScale.BeamAccelMax;
        [SerializeField, Range(0f, 1f)] float _gravitySupport = PhysicsScale.BeamGravitySupport;
        [SerializeField, Min(0f)] float _angularGain = PhysicsScale.BeamAngularGain;

        readonly RaycastHit[] _hits = new RaycastHit[HitBufferSize];

        Rigidbody _heldBody;
        Quaternion _targetRotation = Quaternion.identity;

        /// <summary>지금 들고 있는 파츠. 없으면 null.</summary>
        public SpaceshipPart Held { get; private set; }

        public bool IsHolding => Held != null;

        public float Range => _range;

        /// <summary>플레이어가 견인 장치를 조준하고 이동시킬 때 사용하는 기준점이다.</summary>
        public Transform BeamOrigin => Origin;

        /// <summary>파츠 무게중심이 향하는 지점.</summary>
        public Vector3 HoldPoint => Origin.position + Origin.forward * _holdDistance;

        /// <summary>
        /// 들고 있는 파츠가 맞춰 갈 자세. 기본값은 붙잡은 순간의 회전이고,
        /// 스냅 후보가 잡히면 후보의 정렬 회전으로 바뀐다.
        /// </summary>
        public Quaternion TargetRotation => _targetRotation;

        /// <summary>
        /// 지금 잡혀 있는 스냅 후보. 미리보기(고스트) 표시가 이 값을 읽는다.
        /// 놓는 순간 후보가 있으면 정렬 배치되고, 없으면 그 자리에 자유 배치된다.
        /// </summary>
        public SocketMatch CurrentMatch { get; private set; }

        public event Action<SpaceshipPart> Grabbed;

        /// <summary>스냅 후보가 바뀌었을 때. 미리보기를 켜고 끄는 신호로 쓴다.</summary>
        public event Action<SocketMatch> MatchChanged;

        /// <summary>정렬 배치로 조립되었을 때. 자유 배치로 놓은 경우에는 오지 않는다.</summary>
        public event Action<PartAssemblyLink> Assembled;

        /// <summary>놓았을 때. 사거리 이탈로 자동 해제된 경우도 포함한다.</summary>
        public event Action<SpaceshipPart> ReleasedPart;

        Transform Origin => _origin != null ? _origin : transform;

        public void Configure(Transform origin, float range, float holdDistance)
        {
            _origin = origin;
            _range = Mathf.Max(0f, range);
            _holdDistance = Mathf.Max(0f, holdDistance);
        }

        /// <summary>
        /// 비행접시형 견인 장치를 세계 좌표로 옮긴다.
        /// 빔의 방향은 장치가 아래를 보도록 유지한다. 파츠의 위치·속도는 이 메서드가
        /// 직접 건드리지 않고, 다음 물리 틱의 <see cref="Tick"/> 이 처리한다.
        /// </summary>
        public void SetWorldPosition(Vector3 position)
        {
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        /// <summary>
        /// 조준선 앞의 파츠를 붙잡는다. 성공하면 파츠가 Carried 로 전환된다.
        /// </summary>
        public BeamGrabResult TryGrab()
        {
            if (IsHolding) return BeamGrabResult.BeamBusy;
            return TryGrab(FindTarget());
        }

        /// <summary>
        /// 외부 조준 시스템이 선택한 파츠를 빔으로 붙잡는다.
        /// 운반 장치는 카메라가 파츠를 보고 있을 때만 입력을 받으므로, 빔의 물리 사거리와
        /// 빔 아래에 놓였는지 여부를 여기서 한 번 더 검증한다.
        /// </summary>
        public BeamGrabResult TryGrab(SpaceshipPart candidate)
        {
            if (IsHolding) return BeamGrabResult.BeamBusy;
            if (candidate == null || !CanReach(candidate)) return BeamGrabResult.NoTarget;

            switch (candidate.State)
            {
                case PartState.Locked: return BeamGrabResult.Clamped;
                case PartState.Welded: return BeamGrabResult.Welded;
                case PartState.Carried: return BeamGrabResult.AlreadyCarried;
            }

            if (!candidate.TryTransitionTo(PartState.Carried)) return BeamGrabResult.NoTarget;

            Held = candidate;
            _heldBody = candidate.GetComponent<Rigidbody>();

            // 붙잡은 순간의 자세를 유지한다. 파츠가 제멋대로 굴러가면 조립 위치를 맞출 수 없다.
            _targetRotation = candidate.transform.rotation;

            Grabbed?.Invoke(candidate);
            return BeamGrabResult.Grabbed;
        }

        bool CanReach(SpaceshipPart candidate)
        {
            var body = candidate.GetComponent<Rigidbody>();
            if (body == null || Origin == null) return false;

            var offset = body.worldCenterOfMass - Origin.position;
            if (offset.magnitude > _range) return false;

            // 빔은 아래를 향한다. 약간의 여유를 둬 장치 중심과 파츠 중심이 정확히 일치하지
            // 않아도 작동하지만, 옆에 있는 파츠를 끌어오는 식의 오작동은 막는다.
            var lateral = Vector3.ProjectOnPlane(offset, Origin.forward).magnitude;
            return lateral <= CastRadius + 1.25f;
        }

        /// <summary>
        /// 목표 자세를 지정한다. 스냅 후보가 잡히면 이 값이 후보의 정렬 회전으로 덮인다.
        /// </summary>
        public void SetTargetRotation(Quaternion rotation) => _targetRotation = rotation;

        /// <summary>
        /// 들고 있던 파츠를 내려놓는다. 들고 있지 않으면 아무 일도 하지 않는다.
        ///
        /// 스냅 후보가 잡혀 있으면 소켓에 정렬해 조립하고, 없으면 그 자리에 그대로 놓는다.
        /// </summary>
        public void Release()
        {
            if (!IsHolding) return;

            var part = Held;
            var match = CurrentMatch;

            Held = null;
            _heldBody = null;
            SetMatch(SocketMatch.None);

            if (part != null && match.HasValue && match.MovingSocket.Owner == part)
            {
                var link = PartAssembler.Assemble(match);
                if (link != null)
                {
                    ReleasedPart?.Invoke(part);
                    Assembled?.Invoke(link);
                    return;
                }
            }

            // 파츠가 파괴된 경우 전환을 요청할 대상이 없다.
            if (part != null) part.TryTransitionTo(PartState.Placed);

            ReleasedPart?.Invoke(part);
        }

        /// <summary>
        /// 소유자가 물리 스텝마다 호출한다. 들고 있는 파츠를 목표점으로 끌어당기고
        /// 자세를 유지시킨다. 사거리를 벗어나거나 대상이 사라지면 자동으로 놓는다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsHolding) return;

            if (_heldBody == null || Held == null)
            {
                Release();
                return;
            }

            if (Held.State != PartState.Carried)
            {
                // 다른 시스템이 상태를 바꿨다면 빔이 물러난다.
                Release();
                return;
            }

            var toHoldPoint = HoldPoint - _heldBody.worldCenterOfMass;
            if (Vector3.Distance(Origin.position, _heldBody.worldCenterOfMass) > _range)
            {
                Release();
                return;
            }

            UpdateMatch();
            ApplyTracking(toHoldPoint);
            ApplyRotation();
        }

        /// <summary>
        /// 스냅 후보를 갱신하고, 후보가 있으면 목표 자세를 정렬 회전으로 맞춘다.
        /// 파츠가 다가가는 동안 미리 방향이 돌아가므로 플레이어가 맞출 필요가 없다.
        /// </summary>
        void UpdateMatch()
        {
            var match = PartAssembler.FindMatch(Held);
            SetMatch(match);

            if (match.HasValue) _targetRotation = match.SnappedRotation;
        }

        void SetMatch(SocketMatch match)
        {
            var previous = CurrentMatch;
            var changed = previous.TargetSocket != match.TargetSocket
                          || previous.MovingSocket != match.MovingSocket;

            CurrentMatch = match;
            if (changed) MatchChanged?.Invoke(match);
        }

        /// <summary>
        /// 목표점을 향한 스프링-댐퍼에 중력 보상을 더한다.
        /// ForceMode.Acceleration 을 쓰므로 파츠 질량과 무관하게 같은 반응을 낸다.
        /// 500kg 을 들면서도 조작이 가볍게 느껴지는 이유가 이 한 줄이다.
        /// 캐릭터의 손 잡기가 같은 방식을 쓴다 (Tuning.CarryAccel 계열).
        /// </summary>
        void ApplyTracking(Vector3 toHoldPoint)
        {
            var gravityCompensation = -UnityEngine.Physics.gravity * _gravitySupport;
            var acceleration = toHoldPoint * _accel
                               - _heldBody.linearVelocity * _damp
                               + gravityCompensation;

            if (acceleration.sqrMagnitude > _accelMax * _accelMax)
                acceleration = acceleration.normalized * _accelMax;

            _heldBody.AddForce(acceleration, ForceMode.Acceleration);
        }

        /// <summary>
        /// 목표 자세로 돌린다. 각속도를 직접 써서 <see cref="PhysicsScale.PartMaxAngularSpeed"/> 상한을
        /// 확실히 지킨다. 토크로 밀면 대형 파츠가 관성으로 넘어서기 쉽다.
        /// </summary>
        void ApplyRotation()
        {
            var delta = _targetRotation * Quaternion.Inverse(_heldBody.rotation);
            delta.ToAngleAxis(out var angleDegrees, out var axis);

            if (float.IsInfinity(axis.x) || axis.sqrMagnitude < 1e-6f)
            {
                _heldBody.angularVelocity = Vector3.zero;
                return;
            }

            if (angleDegrees > 180f) angleDegrees -= 360f;

            if (Mathf.Abs(angleDegrees) < 0.05f)
            {
                _heldBody.angularVelocity = Vector3.zero;
                return;
            }

            var desired = axis.normalized * (angleDegrees * Mathf.Deg2Rad * _angularGain);
            var max = PhysicsScale.PartMaxAngularSpeed;
            if (desired.sqrMagnitude > max * max) desired = desired.normalized * max;

            _heldBody.angularVelocity = desired;
        }

        /// <summary>
        /// 조준선에서 가장 가까운 파츠를 고른다. 구체로 쏘아 큰 파츠를 겨누기 쉽게 했다.
        ///
        /// 마스크에 BeamQuery 를 함께 넣는다. 파츠 본체와 별도로 "여기를 겨누면 이 파츠"
        /// 라는 대리 콜라이더를 둘 수 있게 하기 위한 것이다. 그 레이어는 충돌 매트릭스에서
        /// 전부 꺼져 있어 물리에는 영향을 주지 않는다.
        /// </summary>
        SpaceshipPart FindTarget()
        {
            var origin = Origin;
            var count = UnityEngine.Physics.SphereCastNonAlloc(
                origin.position, CastRadius, origin.forward, _hits, _range,
                GameLayers.PartMask | GameLayers.BeamQueryMask, QueryTriggerInteraction.Collide);

            SpaceshipPart best = null;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;

                var part = hit.collider.GetComponentInParent<SpaceshipPart>();
                if (part == null) continue;

                if (hit.distance >= bestDistance) continue;

                best = part;
                bestDistance = hit.distance;
            }

            return best;
        }
    }
}
