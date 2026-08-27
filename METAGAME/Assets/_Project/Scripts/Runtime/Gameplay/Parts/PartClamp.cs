using System;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠를 바닥에 붙들고 있는 고정쇠. 상호작용 프레임워크의 첫 구현체다.
    ///
    /// 꾹 눌러 해제하면 소속 파츠에 알리고, 필수 고정쇠가 모두 풀리면 파츠가 Free 로 넘어간다.
    /// 해제된 고정쇠는 콜라이더를 꺼서 더 이상 상호작용 대상으로 잡히지 않게 한다.
    ///
    /// 콜라이더는 <see cref="GameLayers.Interactable"/> 레이어에 두어야 한다.
    /// 이 레이어는 충돌 매트릭스에서 전부 꺼져 있어 물리에는 영향을 주지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartClamp : InteractableBase
    {
        /// <summary>홀드 시간을 지정하지 않았을 때 쓰는 기본값.</summary>
        public const float DefaultHoldSeconds = 1.5f;

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _part;

        [SerializeField]
        [Tooltip("끄면 풀지 않아도 파츠가 Free 로 넘어간다. 장식용 고정쇠에 쓴다.")]
        bool _isRequired = true;

        Collider[] _colliders;
        bool _initialized;

        public SpaceshipPart Part => _part;

        public bool IsRequired => _isRequired;

        /// <summary>
        /// 필수 여부를 코드에서 지정한다. 레벨 구성 스크립트와 테스트가 쓴다.
        /// 장식용 고정쇠는 풀지 않아도 파츠가 Free 로 넘어간다.
        /// </summary>
        public void SetRequired(bool required) => _isRequired = required;

        public bool IsReleased { get; private set; }

        public event Action<PartClamp> Released;

        void Awake() => Initialize();

        /// <summary>
        /// 소속 파츠와 콜라이더를 찾고 홀드 시간 기본값을 채운다. 여러 번 불러도 안전하다.
        /// Awake 는 EditMode 에서 호출되지 않으므로 테스트와 <see cref="SpaceshipPart"/> 가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_part == null) _part = GetComponentInParent<SpaceshipPart>();
            _colliders = GetComponents<Collider>();

            if (HoldSeconds <= 0f) SetHoldSeconds(DefaultHoldSeconds);
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            Initialize();

            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            return IsReleased
                ? InteractionAvailability.Denied(InteractionFailure.NotReady)
                : InteractionAvailability.Allowed;
        }

        public override void OnCompleted(Interactor actor) => Release();

        /// <summary>
        /// 고정쇠를 해제한다. 이미 풀려 있으면 아무 일도 하지 않는다.
        /// 상호작용 없이 연출이나 디버그에서 직접 부를 수도 있다.
        /// </summary>
        public void Release()
        {
            Initialize();

            if (IsReleased) return;
            IsReleased = true;

            // 콜라이더를 꺼서 Interactor 의 탐지 결과에서 사라지게 한다.
            // 풀린 고정쇠가 계속 포커스를 뺏으면 남은 고정쇠를 고르기 어려워진다.
            if (_colliders != null)
                foreach (var c in _colliders)
                    if (c != null)
                        c.enabled = false;

            Released?.Invoke(this);

            if (_part != null) _part.NotifyClampReleased(this);
        }
    }
}
