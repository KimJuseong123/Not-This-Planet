using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 쓰러진 동료를 일으키는 상호작용(기획 §26-13 "전원 기절 실패가 긴장감을 주되").
    ///
    /// 구조는 <see cref="Parts.PartClamp"/>·<see cref="Parts.WeldBead"/> 와 같다 —
    /// 꾹 누르고, 놓아도 진행분이 남고, 다른 사람이 이어받는다. 그 성질을 여기서 세
    /// 번째로 다시 쓴다. <b>공용으로 뽑지 않은 이유</b>는 세 곳의 진행 단위가 서로
    /// 다르기 때문이다 — 고정못은 인원 수에 비례하고, 접합선은 조각별이며, 구조는
    /// 단일 값이다. 공통 부분이 "0~1 을 누적한다" 뿐이라 뽑아도 남는 것이 없다.
    /// 네 번째가 생기면 그때 다시 본다.
    ///
    /// 이 컴포넌트는 다운한 동안만 켜진다. 서 있는 플레이어에게 붙어 있으면 아무
    /// 때나 남을 밀 수 있는 상호작용이 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DownedRescue : InteractableBase
    {
        [SerializeField] PlayerRig _target;

        /// <summary>
        /// 일으킨 정도 0~1. 직렬화하지 않는다 — 다운 자체가 런타임 상태다.
        /// </summary>
        float _rescuedSeconds;

        float _liveSeconds;

        public PlayerRig Target => _target;

        /// <summary>
        /// 다 채우기까지의 <b>전체</b> 진행도 0~1.
        ///
        /// <para><b>화면은 이 값을 그려야 한다</b> (S15P21B202-390). 인터랙터의
        /// <c>HoldProgress</c> 는 <b>이번에 누른 시간</b>만 알아서, 그 값을 그리면
        /// 이어받는 사람 화면에서 게이지가 <b>0부터 다시 차 보인다.</b> 실제로는
        /// <see cref="RefreshRemainingHoldSeconds"/> 가 남은 시간만 요구하므로 완료는
        /// 빨리 오는데 화면은 그것을 말하지 않는다(<c>!297</c> AI 리뷰 지적).</para>
        ///
        /// <para>이 프로퍼티는 원래 있었다 — 배선이 없어서 아무도 읽지 않았을 뿐이다.</para>
        /// </summary>
        public float Progress =>
            Mathf.Clamp01((_rescuedSeconds + _liveSeconds) / PhysicsScale.RescueSeconds);

        public void Configure(PlayerRig target)
        {
            _target = target;
            SetHoldSeconds(PhysicsScale.RescueSeconds);
        }

        public override InteractionAvailability GetAvailability(Interactor actor)
        {
            var occupancy = base.GetAvailability(actor);
            if (!occupancy.IsAllowed) return occupancy;

            if (_target == null || !_target.Downed)
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            // 쓰러진 사람이 쓰러진 사람을 일으킬 수는 없다.
            var rescuer = actor != null ? actor.GetComponentInParent<PlayerRig>() : null;
            if (rescuer != null && (rescuer.Downed || rescuer == _target))
                return InteractionAvailability.Denied(InteractionFailure.NotReady);

            return InteractionAvailability.Allowed;
        }

        public override void OnHoldProgress(Interactor actor, float progress01)
        {
            // 진행 중에는 확정하지 않는다. HoldSeconds 가 중간에 바뀌면 Interactor 의
            // 진행도 계산이 어긋난다(WeldPoint 가 같은 이유로 이렇게 했다).
            _liveSeconds = progress01 * HoldSeconds;
        }

        /// <summary>
        /// 홀드가 끝났다. 완료든 취소든 여기로 온다.
        ///
        /// 구조자가 그 사이 다운되면 <see cref="Interactor"/> 가 취소하고 이 경로로
        /// 온다 — 누른 만큼은 남으므로 다른 사람이 이어받는다.
        /// </summary>
        public override void Release(Interactor actor)
        {
            base.Release(actor);

            _rescuedSeconds = Mathf.Clamp(
                _rescuedSeconds + _liveSeconds, 0f, PhysicsScale.RescueSeconds);
            _liveSeconds = 0f;

            RefreshRemainingHoldSeconds();
        }

        public override void OnCompleted(Interactor actor)
        {
            if (_target == null || !_target.Downed) return;

            // 부활은 서버가 판정한다 (S15P21B202-145). 클라이언트에서 여기까지 오는
            // 것은 **예측**이고, 실제로 일어나는 것은 서버가 정해 `downed` SyncVar 로
            // 내려온다 (S15P21B202-378).
            //
            // 여기서 되살리면 그 화면에서만 일어난다. 그리고 되돌아오지도 않는다 —
            // downed 훅은 값이 **바뀔 때만** 불리므로, 서버가 여전히 true 인 동안에는
            // 다시 내려오는 것이 없다. -383 의 "판정을 두 곳에 두면 반드시 갈라진다"
            // 가 그대로 적용된다.
            var rescuer = actor != null ? actor.GetComponentInParent<PlayerRig>() : null;
            if (rescuer == null || !rescuer.ServerAuthority) return;

            _rescuedSeconds = 0f;
            _liveSeconds = 0f;
            RefreshRemainingHoldSeconds();

            _target.Revive(_target.MaxHealth * PhysicsScale.RescueHealthRatio);
        }

        /// <summary>다운이 풀렸으니 진행분을 버린다. 다음 다운은 처음부터다.</summary>
        public void ResetProgress()
        {
            _rescuedSeconds = 0f;
            _liveSeconds = 0f;
            RefreshRemainingHoldSeconds();
        }

        /// <summary>남은 시간을 홀드 시간으로 둔다. 절반 일으킨 동료는 절반만 더 누른다.</summary>
        void RefreshRemainingHoldSeconds() =>
            SetHoldSeconds(PhysicsScale.RescueSeconds - _rescuedSeconds);
    }
}
