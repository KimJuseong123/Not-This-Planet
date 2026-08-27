using UnityEngine;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>
    /// 상호작용 대상의 기본 구현. 점유와 포커스 처리를 제공한다.
    ///
    /// 구현체는 보통 <see cref="GetAvailability"/> 를 재정의해 고유 조건을 추가하고
    /// (예: 고정쇠가 남아 있으면 <see cref="InteractionFailure.NotReady"/>)
    /// <see cref="OnCompleted"/> 에서 실제 효과를 수행한다.
    /// 재정의할 때는 base 호출을 유지해 점유 검사를 잃지 않도록 한다.
    ///
    /// 콜라이더는 GameLayers.Interactable 레이어에 두어야 Interactor 가 찾을 수 있다.
    /// 이 레이어는 충돌 매트릭스에서 전부 꺼져 있으므로 물리에는 영향을 주지 않는다.
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [SerializeField, Min(0f)]
        [Tooltip("완료까지 눌러야 하는 시간(초). 0 이면 누르는 즉시 완료되는 탭 상호작용이다.")]
        float _holdSeconds;

        public Transform Transform => transform;

        public float HoldSeconds => _holdSeconds;

        public Interactor Occupant { get; private set; }

        public virtual InteractionAvailability GetAvailability(Interactor actor)
        {
            if (Occupant != null && Occupant != actor)
                return InteractionAvailability.Denied(InteractionFailure.Occupied);

            return InteractionAvailability.Allowed;
        }

        public virtual void OnFocusEnter(Interactor actor)
        {
        }

        public virtual void OnFocusExit(Interactor actor)
        {
        }

        public bool TryOccupy(Interactor actor)
        {
            if (actor == null) return false;
            if (Occupant != null && Occupant != actor) return false;

            Occupant = actor;
            return true;
        }

        public void Release(Interactor actor)
        {
            if (Occupant == actor) Occupant = null;
        }

        public virtual void OnCompleted(Interactor actor)
        {
        }

        /// <summary>홀드 시간을 코드에서 조정한다. 파츠 상태에 따라 달라지는 경우에 사용한다.</summary>
        public void SetHoldSeconds(float seconds) => _holdSeconds = Mathf.Max(0f, seconds);
    }
}
