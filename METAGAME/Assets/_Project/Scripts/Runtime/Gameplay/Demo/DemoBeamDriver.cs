using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.Gameplay.Demo
{
    /// <summary>
    /// 견인 빔을 스스로 조작해 운반을 시연한다.
    ///
    /// 견인 도구의 조작 방식이 아직 정해지지 않아 빔을 구동할 주체가 없다.
    /// Play 하면 잠시 뒤 스스로 붙잡고, 지정한 위치로 옮기고, 내려놓는다.
    ///
    /// 도구와 입력이 붙으면 이 컴포넌트를 씬에서 지우면 된다.
    /// 게임 로직은 이것에 의존하지 않는다.
    /// </summary>
    [RequireComponent(typeof(TractorBeam))]
    public sealed class DemoBeamDriver : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        [Tooltip("Play 후 몇 초 뒤에 붙잡을지.")]
        float _grabDelay = 1f;

        [SerializeField]
        [Tooltip("몇 초 뒤에 놓을지. 0 이하면 계속 들고 있는다.")]
        float _releaseAfter = -1f;

        [SerializeField]
        [Tooltip("붙잡은 뒤 순서대로 지나갈 경유점. 비우면 제자리에 머문다.")]
        System.Collections.Generic.List<Vector3> _waypoints = new();

        [SerializeField, Min(0f)] float _moveSpeed = 2f;

        int _waypointIndex;

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

        /// <summary>
        /// 붙잡은 뒤 지나갈 경유점을 지정한다.
        ///
        /// 두 단계로 나누는 이유가 있다. 놓을 높이로 곧장 이동하면 파츠 밑면이 지면에
        /// 끌리면서 마찰로 쓰러진다. 반대로 높은 곳에서 놓으면 낙하하며 쓰러진다.
        /// 높이 운반한 뒤 제자리에서 내려오는 순서여야 파츠가 서 있는 채로 놓인다.
        /// </summary>
        public void SetWaypoints(float speed, params Vector3[] points)
        {
            _moveSpeed = speed;
            _waypoints.Clear();
            _waypoints.AddRange(points);
            _waypointIndex = 0;
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
                    Debug.Log($"[Demo] 붙잡기 실패: {result}", this);
            }

            if (!_released && _releaseAfter > 0f && _elapsed >= _releaseAfter)
            {
                _released = true;
                _beam.Release();
            }

            if (_beam.IsHolding && _waypointIndex < _waypoints.Count)
            {
                var target = _waypoints[_waypointIndex];
                transform.position = Vector3.MoveTowards(
                    transform.position, target, _moveSpeed * Time.fixedDeltaTime);

                if (Vector3.Distance(transform.position, target) < 0.01f) _waypointIndex++;
            }

            _beam.Tick(Time.fixedDeltaTime);
        }
    }
}
