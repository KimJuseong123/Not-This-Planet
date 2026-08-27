using System;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 접합 지점 하나를 용접하는 상호작용 대상. 구조는 <see cref="PartClamp"/> 와 거울이다.
    /// 고정쇠는 꾹 눌러 풀고, 용접점은 꾹 눌러 채운다.
    ///
    /// 진행분이 누적된다. 중간에 놓아도 채운 만큼 남고, 다른 플레이어가 이어서 채울 수 있다.
    /// 여러 명이 나눠 용접하는 것이 1장 협동의 한 축이기 때문이다.
    ///
    /// <see cref="Interactor"/> 는 홀드를 취소하면 진행도를 버리므로,
    /// <see cref="OnHoldProgress"/> 로 받은 값을 여기서 초 단위로 누적한다.
    /// <see cref="HoldSeconds"/> 는 항상 "남은 시간" 이라서 이어서 누를 때 그만큼만 누르면 된다.
    ///
    /// 콜라이더는 <see cref="GameLayers.Interactable"/> 레이어에 두어야 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeldPoint : InteractableBase
    {
        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _owner;

        [SerializeField]
        [Tooltip("켜면 용접 도구를 들고 있어야 용접할 수 있다. 도구 연동 전까지는 꺼 둔다.")]
        bool _requiresTool;

        /// <summary>
        /// 확정된 용접 시간. 직렬화한다.
        ///
        /// 씬이나 프리팹에 저장된 용접 상태가 로드된 뒤에도 유지되어야 한다.
        /// 저장하지 않으면 파츠는 Welded 인데 진행도는 0 으로 보여, 조인트 강도가
        /// 미접합 값으로 되돌아가 용접한 탑이 충격에 떨어진다.
        /// </summary>
        [SerializeField, HideInInspector] float _weldedSeconds;

        Collider[] _colliders;

        /// <summary>지금 누르고 있는 분. 진행 중에만 의미가 있어 저장하지 않는다.</summary>
        float _liveSeconds;

        bool _initialized;

        public SpaceshipPart Owner
        {
            get
            {
                Initialize();
                return _owner;
            }
        }

        /// <summary>0~1. 커밋된 진행분과 지금 누르고 있는 분을 합친 값이다.</summary>
        public float Progress => Mathf.Clamp01((_weldedSeconds + _liveSeconds) / PhysicsScale.WeldSecondsPerPoint);

        public bool IsComplete => Progress >= 1f;

        /// <summary>용접 도구가 필요한지. 도구 연동은 후속 티켓에서 붙인다.</summary>
        public bool RequiresTool => _requiresTool;

        public event Action<WeldPoint> Completed;

        void Awake() => Initialize();

        /// <summary>
        /// 소유 파츠와 콜라이더를 찾고 남은 홀드 시간을 채운다. 여러 번 불러도 안전하다.
        /// Awake 는 EditMode 에서 호출되지 않으므로 테스트와 링크가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_owner == null) _owner = GetComponentInParent<SpaceshipPart>();
            _colliders = GetComponents<Collider>();

            RefreshRemainingHoldSeconds();
        }

        public void SetRequiresTool(bool required) => _requiresTool = required;

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            Initialize();

            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            if (IsComplete) return InteractionAvailability.Denied(InteractionFailure.NotReady);

            // 붙어 있지 않은 파츠의 용접점은 용접할 것이 없다.
            if (_owner == null || _owner.State != PartState.Placed)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            var link = _owner.GetComponent<PartAssemblyLink>();
            if (link == null || !link.IsIntact)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            return InteractionAvailability.Allowed;
        }

        public override void OnHoldProgress(Interactor actor, float progress01)
        {
            // 진행 중에는 커밋하지 않는다. HoldSeconds 가 중간에 바뀌면
            // Interactor 의 진행도 계산이 어긋나기 때문이다.
            _liveSeconds = progress01 * HoldSeconds;
        }

        /// <summary>
        /// 홀드가 끝났다. 완료든 취소든 여기로 온다. 누르고 있던 만큼을 확정한다.
        /// </summary>
        public override void Release(Interactor actor)
        {
            base.Release(actor);
            Commit();
        }

        public override void OnCompleted(Interactor actor)
        {
            _weldedSeconds = PhysicsScale.WeldSecondsPerPoint;
            _liveSeconds = 0f;
            RefreshRemainingHoldSeconds();
            SetCollidersEnabled(false);

            Completed?.Invoke(this);
        }

        /// <summary>
        /// 상호작용 없이 즉시 완료 처리한다.
        /// 레벨 초기 상태 구성, 연출, 디버그에서 쓴다.
        /// </summary>
        public void ForceComplete() => OnCompleted(null);

        /// <summary>진행분을 0 으로 되돌린다. 조인트가 끊어져 접합이 풀렸을 때 호출된다.</summary>
        public void ResetProgress()
        {
            Initialize();

            _weldedSeconds = 0f;
            _liveSeconds = 0f;
            RefreshRemainingHoldSeconds();
            SetCollidersEnabled(true);
        }

        void Commit()
        {
            _weldedSeconds = Mathf.Clamp(
                _weldedSeconds + _liveSeconds, 0f, PhysicsScale.WeldSecondsPerPoint);
            _liveSeconds = 0f;

            RefreshRemainingHoldSeconds();
        }

        /// <summary>
        /// 남은 시간을 홀드 시간으로 둔다. 절반 채운 용접점은 절반만 더 누르면 끝난다.
        /// </summary>
        void RefreshRemainingHoldSeconds() =>
            SetHoldSeconds(PhysicsScale.WeldSecondsPerPoint - _weldedSeconds);

        void SetCollidersEnabled(bool enabled)
        {
            if (_colliders == null) return;

            foreach (var c in _colliders)
                if (c != null)
                    c.enabled = enabled;
        }
    }
}
