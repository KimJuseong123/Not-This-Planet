using System;
using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>플레이어가 현재 화면 중심으로 선택한 현장 상호작용 종류.</summary>
    public enum PlayerInteractionKind
    {
        None,
        Clamp,
        Grabbable,
        Transport,
    }

    /// <summary>
    /// HUD와 입력 처리가 함께 사용하는 단일 상호작용 대상이다.
    /// 대상 후보를 여러 시스템이 각각 고르면 화면에는 하나가 보여도 다른 대상이
    /// 작동하는 문제가 생기므로, PlayerRig가 만든 이 값만 읽도록 한다.
    /// </summary>
    public readonly struct PlayerInteractionTarget
    {
        public PlayerInteractionKind Kind { get; }
        public PartClamp Clamp { get; }
        public Grabbable Grabbable { get; }
        public TransportVehicle Transport { get; }
        public Vector3 Point { get; }
        public float Angle { get; }
        public float Distance { get; }

        /// <summary>
        /// 탐색 포커스가 살아 있는 것과 실제 클릭이 가능한 것은 다르다.
        /// 예를 들어 고정못은 멀리서도 탐색 포커스로 잡아 크로스헤드 색상을 바꾸지만,
        /// 손이 실제 Collider 표면에 닿을 수 있을 때만 이 값이 참이다.
        /// </summary>
        readonly bool _canInteract;

        public bool CanInteract => _canInteract && IsValid;

        public bool IsValid
        {
            get
            {
                switch (Kind)
                {
                    case PlayerInteractionKind.Clamp:
                        return Clamp != null && Clamp.isActiveAndEnabled && !Clamp.IsReleased;
                    case PlayerInteractionKind.Grabbable:
                        return Grabbable != null && Grabbable.isActiveAndEnabled && Grabbable.CanGrab;
                    case PlayerInteractionKind.Transport:
                        return Transport != null && Transport.isActiveAndEnabled
                               && !Transport.IsReturning;
                    default:
                        return false;
                }
            }
        }

        public string Keyword => Kind == PlayerInteractionKind.Transport ? "E" :
            Kind == PlayerInteractionKind.None ? "" : "클릭";

        public PlayerInteractionTarget(PlayerInteractionKind kind, PartClamp clamp,
                                       Grabbable grabbable, TransportVehicle transport,
                                       Vector3 point, float angle, float distance,
                                       bool canInteract = true)
        {
            Kind = kind;
            Clamp = clamp;
            Grabbable = grabbable;
            Transport = transport;
            Point = point;
            Angle = angle;
            Distance = distance;
            _canInteract = canInteract;
        }

        public static PlayerInteractionTarget None =>
            new PlayerInteractionTarget(PlayerInteractionKind.None, null, null, null,
                                        Vector3.zero, float.MaxValue, float.MaxValue, false);
    }

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

        /// <summary><see cref="ExtraCandidates"/> 가 채우는 버퍼. 매 틱 재사용한다.</summary>
        readonly List<IInteractable> _extra = new List<IInteractable>(8);

        /// <summary>지금 바라보고 있는 대상. 없으면 null.</summary>
        public IInteractable Focus { get; private set; }

        /// <summary>
        /// 이 인터랙터가 <b>받아들일 대상</b>만 고르게 한다 (S15P21B202-390).
        /// <c>null</c> 이면 전부 받는다 — 기존 사용처의 동작은 그대로다.
        ///
        /// <para><b>왜 필요한가.</b> <see cref="FindBestCandidate"/> 는
        /// <c>Interactable</c> 레이어의 <see cref="IInteractable"/> 이면 <b>아무거나</b>
        /// 잡는다. 그래서 용도가 다른 인터랙터를 하나 더 두면 <b>남의 대상을 점유해
        /// 버린다</b> — 살리기 인터랙터가 고정쇠를 잡으면, <c>PartClamp</c> 는
        /// <c>HoldSeconds</c> 가 0 이라 그 자리에서 완료로 처리되고 실제 고정쇠 조작이
        /// 막힌다.</para>
        /// </summary>
        public System.Predicate<IInteractable> Accepts;

        /// <summary>
        /// <b>물리 질의로 찾을 수 없는 후보를 대는 곳</b>(S15P21B202-448).
        /// 넘겨준 목록에 담으면 질의로 찾은 것과 <b>같은 점수 규칙</b>으로 함께 겨룬다.
        ///
        /// <para><b>왜 필요한가.</b> 참가자 기계에서 남의 몸은 그림이라
        /// <c>RemoteAvatar</c> 가 <c>detectCollisions</c> 를 끈다. 그러면 그 몸에 매달린
        /// 트리거는 <b>존재하고 켜져 있고 레이어도 맞는데</b> 씬 질의에 걸리지 않는다 —
        /// 자식 콜라이더는 자기 <c>Rigidbody</c> 가 없으면 부모 것에 붙고, 그 스위치는
        /// 액터를 시뮬레이션에서만이 아니라 <b>질의에서도</b> 뺀다.</para>
        ///
        /// <para>실측(2026-09-11): 같은 장면에서 호스트는 질의에 1개가 걸리고 참가자는
        /// <b>0개</b>다. <c>detectCollisions</c> 하나만 되돌리면 즉시 걸린다.</para>
        ///
        /// <para><b>마스크를 여는 것으로는 풀 수 없다</b> — 레이어는 이미 맞다. 그리고
        /// 여는 것 자체가 <c>S15P21B202-249</c> 가 막아 둔 길이다(고정못이 파츠의 잡기
        /// 지점으로 딸려 온다). <see cref="PlayerRig.FindTeammateGrabPoint"/> 가 같은
        /// 이유로 목록을 훑고, <b>그래서 그쪽은 참가자에서도 동작한다.</b></para>
        /// </summary>
        public Action<List<IInteractable>> ExtraCandidates;

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

        /// <summary>
        /// <b>이 거리 안이면 시야각을 안 본다</b> (S15P21B202-699). 0 이면 늘 본다.
        ///
        /// <para><b>바닥에 있는 대상을 위한 것이다.</b> 원뿔은 눈에서 나가므로, 발치에
        /// 있는 것일수록 내려다보는 각이 가팔라져 시야각 밖으로 나간다 — 다가갈수록
        /// 후보에서 떨어진다. 그 자리에서는 누구를 가리키는지 모호하지도 않다.</para>
        ///
        /// <para>고르는 규칙(<see cref="IsBetter"/>)은 그대로라, 둘이 나란히 있어도
        /// 더 정면에 가까운 쪽이 이긴다.</para>
        /// </summary>
        public float NearRadius { get; set; }

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

            return TryBeginFocused();
        }

        /// <summary>
        /// 현재 포커스된 대상을 다시 찾지 않고 상호작용을 시작한다.
        /// 조준한 고정쇠를 먼저 잡은 뒤 카메라가 흔들려도 같은 대상을 유지해야
        /// 하는 상호작용에서 사용한다.
        /// </summary>
        public bool TryBeginFocused()
        {
            if (State == InteractorState.Holding)
            {
                Failed?.Invoke(Held, InteractionFailure.Busy);
                return false;
            }

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

                Consider(candidateCollider.GetComponentInParent<IInteractable>(), origin, forward,
                         ref best, ref bestAngle, ref bestSqrDistance, ref bestPosition);
            }

            // **물리 질의가 볼 수 없는 후보를 소유자가 댄다**(S15P21B202-448).
            //
            // 참가자 기계에서 남의 몸은 그림이라 RemoteAvatar 가 detectCollisions 를
            // 끈다. 그러면 그 몸에 매달린 트리거는 <b>존재하고 켜져 있어도</b> 씬 질의에
            // 걸리지 않는다 — 자식 콜라이더는 자기 Rigidbody 가 없으면 부모 것에 붙고,
            // 그 스위치는 액터를 시뮬레이션에서만이 아니라 질의에서도 뺀다.
            //
            // 그래서 살리기 후보가 호스트에서만 잡혔다(실측: 호스트 1개 / 참가자 0개).
            // 마스크를 열어서 풀 수 있는 문제가 아니다 — 레이어는 이미 맞다.
            //
            // 점수 계산은 위와 <b>같은 것을 쓴다.</b> 후보를 어디서 가져왔는지가
            // 고르는 규칙을 바꾸면 "4인이 각자 화면에서 같은 판단을 한다" 가 깨진다.
            if (ExtraCandidates != null)
            {
                _extra.Clear();
                ExtraCandidates(_extra);

                for (var i = 0; i < _extra.Count; i++)
                    Consider(_extra[i], origin, forward,
                             ref best, ref bestAngle, ref bestSqrDistance, ref bestPosition);
            }

            return best;
        }

        /// <summary>
        /// 후보 하나를 채점해 지금까지의 최선과 견준다.
        ///
        /// 질의로 찾은 것과 소유자가 댄 것이 <b>같은 길을 지나게</b> 하려고 뽑았다.
        /// 같은 대상이 양쪽에서 두 번 들어와도 문제가 없다 — 두 번째는 각도·거리·좌표가
        /// 모두 같아 <see cref="IsBetter"/> 가 거짓을 돌려준다.
        /// </summary>
        void Consider(IInteractable candidate, Vector3 origin, Vector3 forward,
                      ref IInteractable best, ref float bestAngle,
                      ref float bestSqrDistance, ref Vector3 bestPosition)
        {
            if (!IsAlive(candidate)) return;
            if (Accepts != null && !Accepts(candidate)) return;

            var position = candidate.Transform.position;
            var offset = position - origin;
            var sqrDistance = offset.sqrMagnitude;
            if (sqrDistance > _range * _range) return;

            var angle = sqrDistance < 1e-6f ? 0f : Vector3.Angle(forward, offset);
            // 발치에 있는 것은 각도를 묻지 않는다 (S15P21B202-699).
            bool near = NearRadius > 0f && sqrDistance <= NearRadius * NearRadius;
            if (!near && angle > _halfAngleDegrees) return;

            if (!IsBetter(angle, sqrDistance, position, bestAngle, bestSqrDistance, bestPosition)) return;

            best = candidate;
            bestAngle = angle;
            bestSqrDistance = sqrDistance;
            bestPosition = position;
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
