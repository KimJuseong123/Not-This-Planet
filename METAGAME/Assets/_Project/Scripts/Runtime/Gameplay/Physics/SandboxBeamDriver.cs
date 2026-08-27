using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Sandbox_Physics 씬 전용 디버그 컴포넌트.
    ///
    /// 견인 도구의 조작 방식이 아직 정해지지 않아 빔을 구동할 주체가 없다.
    /// Play 하면 잠시 뒤 스스로 붙잡고 매 물리 스텝마다 Tick 을 돌려
    /// 빔 거동을 눈으로 확인할 수 있게 한다. 실제 게임 로직에는 사용하지 않는다.
    /// </summary>
    [RequireComponent(typeof(TractorBeam))]
    public sealed class SandboxBeamDriver : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("Play 후 몇 초 뒤에 붙잡을지.")]
        float _grabDelay = 1f;

        [SerializeField]
        [Tooltip("몇 초 뒤에 놓을지. 0 이하면 계속 들고 있는다.")]
        float _releaseAfter = -1f;

        [SerializeField]
        [Tooltip("붙잡은 뒤 이 위치로 옮겨 간다. 사용하지 않으면 UseMoveTarget 을 끈다.")]
        Vector3 _moveTarget;

        [SerializeField] bool _useMoveTarget;

        [SerializeField, Min(0f)] float _moveSpeed = 2f;

        TractorBeam _beam;
        float _elapsed;
        bool _grabAttempted;
        bool _released;

        void Awake() => _beam = GetComponent<TractorBeam>();

        /// <summary>레벨 구성 스크립트가 동작을 지정한다.</summary>
        public void Configure(float grabDelay, float releaseAfter)
        {
            _grabDelay = grabDelay;
            _releaseAfter = releaseAfter;
        }

        /// <summary>붙잡은 뒤 옮겨 갈 위치를 지정한다.</summary>
        public void SetMoveTarget(Vector3 target, float speed)
        {
            _moveTarget = target;
            _moveSpeed = speed;
            _useMoveTarget = true;
        }

        void FixedUpdate()
        {
            if (_beam == null) return;

            _elapsed += Time.fixedDeltaTime;

            if (!_grabAttempted && _elapsed >= _grabDelay)
            {
                _grabAttempted = true;
                var result = _beam.TryGrab();
                if (result != BeamGrabResult.Grabbed)
                    Debug.Log($"[SandboxBeamDriver] 붙잡기 실패: {result}", this);
            }

            if (!_released && _releaseAfter > 0f && _elapsed >= _releaseAfter)
            {
                _released = true;
                _beam.Release();
            }

            if (_useMoveTarget && _beam.IsHolding)
                transform.position = Vector3.MoveTowards(
                    transform.position, _moveTarget, _moveSpeed * Time.fixedDeltaTime);

            _beam.Tick(Time.fixedDeltaTime);
        }
    }
}
