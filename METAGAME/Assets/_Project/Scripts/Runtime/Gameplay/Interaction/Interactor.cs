using System;
using UnityEngine;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>
    /// 플레이어가 주변의 상호작용 대상을 찾고, 선택하고, 홀드해서 완료시키는 주체.
    ///
    /// 입력을 알지 못한다. 소유자(캐릭터)가 매 프레임 <see cref="Tick"/> 을 호출하고
    /// 키 입력에 맞춰 <see cref="TryBegin"/> 과 <see cref="Cancel"/> 을 호출한다.
    /// 이렇게 두면 입력 방식이 바뀌어도 이 클래스는 그대로이고, EditMode 테스트에서
    /// 시간을 직접 넣어 검증할 수 있다.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        const int CandidateBufferSize = 16;
        const float AngleEpsilon = 0.01f;

        /// <summary>
        /// 진행도를 매 프레임 누적하면 부동소수점 오차가 쌓인다.
        /// 예를 들어 1초짜리 홀드를 0.02초씩 50번 더하면 0.99999997 이 되어
        /// 플레이어가 이유 없이 한 프레임을 더 눌러야 한다. 그 오차만큼을 완료로 인정한다.
        /// </summary>
        const float ProgressCompletionEpsilon = 1e-4f;

        [SerializeField]
        [Tooltip("사거리와 시야각의 기준. 비우면 이 오브젝트의 Transform 을 쓴다. 보통 카메라를 넣는다.")]
        Transform _aim;

        [SerializeField, Min(0f)] float _range = 3f;

        [SerializeField, Range(1f, 180f)]
        [Tooltip("정면에서 몇 도까지를 대상으로 볼지. 60 이면 좌우 60도, 총 120도 범위다.")]
        float _halfAngleDegrees = 60f;

        readonly Collider[] _candidates = new Collider[CandidateBufferSize];

        /// <summary>지금 바라보고 있는 대상. 없으면 null.</summary>
        public IInteractable Focus { get; private set; }

        /// <summary>홀드를 진행 중인 대상. 진행 중이 아니면 null.</summary>
        public IInteractable Held { get; private set; }

        public InteractorState State { get; private set; }

        /// <summary>홀드 진행도 0~1.</summary>
        public float HoldProgress { get; private set; }

        public float Range => _range;

        public float HalfAngleDegrees => _halfAngleDegrees;

        /// <summary>(이전 대상, 새 대상). 둘 다 null 일 수 있다.</summary>
        public event Action<IInteractable, IInteractable> FocusChanged;

        /// <summary>(대상, 진행도 0~1).</summary>
        public event Action<IInteractable, float> HoldProgressChanged;

        public event Action<IInteractable> Completed;

        public event Action<IInteractable> Canceled;

        /// <summary>(대상, 실패 사유). 대상이 없으면 첫 인자는 null.</summary>
        public event Action<IInteractable, InteractionFailure> Failed;

        Transform Aim => _aim != null ? _aim : transform;

        public void Configure(Transform aim, float range, float halfAngleDegrees)
        {
            _aim = aim;
            _range = Mathf.Max(0f, range);
            _halfAngleDegrees = Mathf.Clamp(halfAngleDegrees, 1f, 180f);
        }

        /// <summary>
        /// 소유자가 매 프레임 호출한다. 홀드 중이면 진행도를 올리고, 아니면 포커스를 갱신한다.
        /// 홀드 중에 포커스를 갱신하지 않는 것은 의도적이다. 한 번 잡은 대상을 끝까지 붙든다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (State == InteractorState.Holding)
            {
                TickHold(deltaTime);
                return;
            }

            SetFocus(FindBestCandidate());
        }

        /// <summary>
        /// 현재 포커스 대상과의 상호작용을 시작한다.
        /// 홀드 시간이 0 이면 이 호출 안에서 즉시 완료된다.
        /// </summary>
        public bool TryBegin()
        {
            if (State == InteractorState.Holding)
            {
                Failed?.Invoke(Held, InteractionFailure.Busy);
                return false;
            }

            SetFocus(FindBestCandidate());

            var target = Focus;
            if (!IsAlive(target))
            {
                Failed?.Invoke(null, InteractionFailure.NoTarget);
                return false;
            }

            var availability = target.GetAvailability(this);
            if (!availability.IsAllowed)
            {
                Failed?.Invoke(target, availability.Failure);
                return false;
            }

            if (!target.TryOccupy(this))
            {
                Failed?.Invoke(target, InteractionFailure.Occupied);
                return false;
            }

            Held = target;
            HoldProgress = 0f;

            if (target.HoldSeconds <= 0f)
            {
                Complete();
                return true;
            }

            State = InteractorState.Holding;
            HoldProgressChanged?.Invoke(Held, 0f);
            return true;
        }

        /// <summary>진행 중인 홀드를 취소한다. 진행 중이 아니면 아무 일도 하지 않는다.</summary>
        public void Cancel()
        {
            if (State == InteractorState.Holding) CancelHold();
        }

        void TickHold(float deltaTime)
        {
            if (!IsAlive(Held))
            {
                CancelHold();
                return;
            }

            if (!IsWithinReach(Held) || !Held.GetAvailability(this).IsAllowed)
            {
                CancelHold();
                return;
            }

            HoldProgress = Mathf.Clamp01(HoldProgress + deltaTime / Held.HoldSeconds);
            if (HoldProgress >= 1f - ProgressCompletionEpsilon) HoldProgress = 1f;

            Held.OnHoldProgress(this, HoldProgress);
            HoldProgressChanged?.Invoke(Held, HoldProgress);

            if (HoldProgress >= 1f) Complete();
        }

        void Complete()
        {
            var target = Held;

            HoldProgress = 1f;
            Held = null;
            State = InteractorState.Idle;

            target.Release(this);
            target.OnCompleted(this);
            Completed?.Invoke(target);
        }

        void CancelHold()
        {
            var target = Held;

            Held = null;
            State = InteractorState.Idle;
            HoldProgress = 0f;

            if (IsAlive(target)) target.Release(this);

            HoldProgressChanged?.Invoke(target, 0f);
            Canceled?.Invoke(target);
        }

        void SetFocus(IInteractable next)
        {
            var previous = Focus;
            if (ReferenceEquals(previous, next)) return;

            if (IsAlive(previous)) previous.OnFocusExit(this);

            Focus = next;
            State = IsAlive(next) ? InteractorState.Focused : InteractorState.Idle;

            if (IsAlive(next)) next.OnFocusEnter(this);

            FocusChanged?.Invoke(previous, next);
        }

        /// <summary>
        /// 사거리와 시야각 안의 대상 중 하나를 고른다.
        /// 정면에 가까운 것이 우선이고, 각도가 같으면 가까운 것, 그것도 같으면 월드 좌표 순이다.
        /// 같은 상황에서 항상 같은 대상이 나와야 4인이 각자 화면에서 같은 판단을 한다.
        /// 마지막 기준으로 인스턴스 ID 를 쓰지 않는 이유는, ID 가 머신마다 달라서
        /// 같은 장면인데도 클라이언트별로 다른 대상이 뽑힐 수 있기 때문이다.
        ///
        /// 점유된 대상도 포커스에는 포함한다. 제외해 버리면 "누가 쓰는 중"이라는 피드백을
        /// 줄 수 없기 때문이다. 시작 시점에 TryBegin 이 Occupied 로 거절한다.
        /// </summary>
        IInteractable FindBestCandidate()
        {
            var aim = Aim;
            var origin = aim.position;
            var forward = aim.forward;

            var count = Physics.OverlapSphereNonAlloc(
                origin, _range, _candidates, GameLayers.InteractableMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            var bestAngle = float.MaxValue;
            var bestSqrDistance = float.MaxValue;
            var bestPosition = Vector3.positiveInfinity;

            for (var i = 0; i < count; i++)
            {
                var candidateCollider = _candidates[i];
                if (candidateCollider == null) continue;

                var candidate = candidateCollider.GetComponentInParent<IInteractable>();
                if (!IsAlive(candidate)) continue;

                var position = candidate.Transform.position;
                var offset = position - origin;
                var sqrDistance = offset.sqrMagnitude;
                if (sqrDistance > _range * _range) continue;

                var angle = sqrDistance < 1e-6f ? 0f : Vector3.Angle(forward, offset);
                if (angle > _halfAngleDegrees) continue;

                if (!IsBetter(angle, sqrDistance, position, bestAngle, bestSqrDistance, bestPosition)) continue;

                best = candidate;
                bestAngle = angle;
                bestSqrDistance = sqrDistance;
                bestPosition = position;
            }

            return best;
        }

        static bool IsBetter(
            float angle, float sqrDistance, Vector3 position,
            float bestAngle, float bestSqrDistance, Vector3 bestPosition)
        {
            if (angle < bestAngle - AngleEpsilon) return true;
            if (angle > bestAngle + AngleEpsilon) return false;

            if (sqrDistance < bestSqrDistance) return true;
            if (sqrDistance > bestSqrDistance) return false;

            return ComparePosition(position, bestPosition) < 0;
        }

        /// <summary>월드 좌표를 x, y, z 순으로 비교해 순서를 하나로 고정한다.</summary>
        static int ComparePosition(Vector3 left, Vector3 right)
        {
            var comparison = left.x.CompareTo(right.x);
            if (comparison != 0) return comparison;

            comparison = left.y.CompareTo(right.y);
            return comparison != 0 ? comparison : left.z.CompareTo(right.z);
        }

        bool IsWithinReach(IInteractable target)
        {
            var aim = Aim;
            var offset = target.Transform.position - aim.position;

            if (offset.sqrMagnitude > _range * _range) return false;
            if (offset.sqrMagnitude < 1e-6f) return true;

            return Vector3.Angle(aim.forward, offset) <= _halfAngleDegrees;
        }

        /// <summary>
        /// 인터페이스 참조는 Unity 의 "파괴된 오브젝트는 null" 규칙을 우회한다.
        /// 파괴된 대상을 계속 붙들고 있지 않도록 여기서 걸러낸다.
        /// </summary>
        static bool IsAlive(IInteractable interactable) =>
            interactable is MonoBehaviour behaviour ? behaviour != null : interactable != null;
    }
}
