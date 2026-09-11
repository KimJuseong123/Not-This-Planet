using UnityEngine;

namespace Metagame.Gameplay.Interaction
{
    /// <summary>
    /// 상호작용할 수 있는 대상. 고정쇠, 용접점, 도구 거치대, 기절한 동료가 모두 이 인터페이스를 구현한다.
    ///
    /// 대부분의 구현체는 <see cref="InteractableBase"/> 를 상속해 점유·포커스 처리를 물려받고
    /// <see cref="GetAvailability"/> 와 <see cref="OnCompleted"/> 만 재정의하면 된다.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>거리·각도 판정의 기준이 되는 Transform.</summary>
        Transform Transform { get; }

        /// <summary>완료까지 눌러야 하는 시간(초). 0 이면 누르는 즉시 완료되는 탭 상호작용이다.</summary>
        float HoldSeconds { get; }

        /// <summary>현재 이 대상을 점유한 Interactor. 없으면 null.</summary>
        Interactor Occupant { get; }

        InteractionAvailability GetAvailability(Interactor actor);

        void OnFocusEnter(Interactor actor);

        void OnFocusExit(Interactor actor);

        /// <summary>
        /// 홀드가 진행되는 동안 매 스텝 호출된다. progress01 은 0~1 이다.
        ///
        /// 대상이 진행분을 스스로 알아야 하는 경우에 쓴다. 용접점이 그렇다.
        /// 여러 명이 나눠 용접하고 중간에 놓았다 이어서 할 수 있어야 하는데,
        /// Interactor 는 취소 시 진행도를 버리므로 대상 쪽에서 누적해야 한다.
        /// </summary>
        void OnHoldProgress(Interactor actor, float progress01);

        /// <summary>점유를 시도한다. 다른 Interactor 가 이미 쓰고 있으면 false.</summary>
        bool TryOccupy(Interactor actor);

        /// <summary>점유를 해제한다. 점유자가 아니면 아무 일도 하지 않는다.</summary>
        void Release(Interactor actor);

        void OnCompleted(Interactor actor);
    }
}
