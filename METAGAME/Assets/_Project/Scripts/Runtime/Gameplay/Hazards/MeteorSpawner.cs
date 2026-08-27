using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 운석을 예고하고 떨어뜨린다. 시간이 지날수록 낙하 간격이 줄어든다.
    ///
    /// 흐름은 두 단계다.
    ///  1) 간격이 되면 착탄 지점을 정해 <see cref="PendingMeteorDrop"/> 으로 예고한다.
    ///  2) 예고 시간이 지나면 그 지점 위에 운석을 만든다.
    ///
    /// 난수는 시드로 만든 System.Random 을 쓴다. UnityEngine.Random 은 전역 상태라
    /// 테스트 재현성과 4인 동기화에 쓸 수 없다. 같은 시드면 같은 순서로 떨어진다.
    ///
    /// 소유자가 <see cref="Tick"/> 을 호출한다. Time 을 직접 읽지 않아 EditMode 에서
    /// 3분치 난이도 곡선을 몇 밀리초 만에 검증할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeteorSpawner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("떨어뜨릴 운석 프리팹. 비우면 구체를 코드로 만든다.")]
        Meteor _meteorPrefab;

        [SerializeField]
        [Tooltip("착탄 지점을 고르는 사각 영역의 중심. 비우면 이 오브젝트 위치를 쓴다.")]
        Transform _area;

        [SerializeField] Vector2 _areaSize = new(40f, 40f);

        [SerializeField, Min(0f)] float _intervalStart = PhysicsScale.MeteorIntervalStart;
        [SerializeField, Min(0f)] float _intervalMin = PhysicsScale.MeteorIntervalMin;
        [SerializeField, Min(0f)] float _rampSeconds = PhysicsScale.MeteorRampSeconds;
        [SerializeField, Min(0f)] float _warningSeconds = PhysicsScale.MeteorWarningSeconds;
        [SerializeField, Min(0f)] float _dropHeight = PhysicsScale.MeteorDropHeight;

        [SerializeField]
        [Tooltip("같은 시드면 같은 순서로 떨어진다. 재현과 동기화에 쓴다.")]
        int _seed = 12345;

        readonly List<PendingMeteorDrop> _pending = new();
        readonly List<Meteor> _live = new();

        System.Random _random;
        float _elapsed;
        float _sinceLastSchedule;

        /// <summary>예고 중인 낙하. 표시는 UI·Art 가 이 값을 읽어 만든다.</summary>
        public IReadOnlyList<PendingMeteorDrop> Pending => _pending;

        /// <summary>아직 살아 있는 운석.</summary>
        public IReadOnlyList<Meteor> Live => _live;

        public float Elapsed => _elapsed;

        /// <summary>지금 적용되는 낙하 간격. 시간이 지날수록 줄어든다.</summary>
        public float CurrentInterval => IntervalAt(_elapsed);

        public event Action<PendingMeteorDrop> DropScheduled;

        public event Action<Meteor> Spawned;

        Transform Area => _area != null ? _area : transform;

        void Awake() => ResetRun();

        /// <summary>
        /// 시드를 다시 심고 진행 상황을 비운다. 씬 시작과 재시도에서 부른다.
        /// </summary>
        public void ResetRun()
        {
            _random = new System.Random(_seed);
            _elapsed = 0f;
            _sinceLastSchedule = 0f;
            _pending.Clear();
            _live.Clear();
        }

        public void Configure(int seed, float intervalStart, float intervalMin, float rampSeconds)
        {
            _seed = seed;
            _intervalStart = Mathf.Max(0f, intervalStart);
            _intervalMin = Mathf.Max(0f, intervalMin);
            _rampSeconds = Mathf.Max(0f, rampSeconds);
            ResetRun();
        }

        public void SetArea(Transform area, Vector2 size)
        {
            _area = area;
            _areaSize = size;
        }

        /// <summary>
        /// 초반에는 느리게, 시간이 지나면 최소 간격까지 선형으로 줄어든다.
        /// 초반에 조작과 핵심 루프를 익힐 여유를 주고, 후반에 역할을 나누게 만드는 것이 의도다.
        /// </summary>
        public float IntervalAt(float elapsedSeconds)
        {
            if (_rampSeconds <= 0f) return _intervalMin;

            var t = Mathf.Clamp01(elapsedSeconds / _rampSeconds);
            return Mathf.Lerp(_intervalStart, _intervalMin, t);
        }

        /// <summary>
        /// 소유자가 매 물리 스텝 호출한다.
        /// 예고를 만들고, 예고 시간이 지난 것을 떨어뜨리고, 살아 있는 운석을 갱신한다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_random == null) ResetRun();

            _elapsed += deltaTime;

            AdvanceSchedule(deltaTime);
            AdvancePending(deltaTime);
            AdvanceLive(deltaTime);
        }

        void AdvanceSchedule(float deltaTime)
        {
            _sinceLastSchedule += deltaTime;
            if (_sinceLastSchedule < CurrentInterval) return;

            _sinceLastSchedule = 0f;

            var drop = new PendingMeteorDrop(NextImpactPoint(), _warningSeconds, _warningSeconds);
            _pending.Add(drop);
            DropScheduled?.Invoke(drop);
        }

        void AdvancePending(float deltaTime)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var advanced = _pending[i].Advanced(deltaTime);

                if (advanced.RemainingSeconds > 0f)
                {
                    _pending[i] = advanced;
                    continue;
                }

                _pending.RemoveAt(i);
                Spawn(advanced.ImpactPoint);
            }
        }

        void AdvanceLive(float deltaTime)
        {
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var meteor = _live[i];
                if (meteor == null)
                {
                    _live.RemoveAt(i);
                    continue;
                }

                meteor.Tick(deltaTime);
            }
        }

        Vector3 NextImpactPoint()
        {
            var center = Area.position;
            var x = center.x + ((float)_random.NextDouble() - 0.5f) * _areaSize.x;
            var z = center.z + ((float)_random.NextDouble() - 0.5f) * _areaSize.y;

            return new Vector3(x, center.y, z);
        }

        Meteor Spawn(Vector3 impactPoint)
        {
            var position = impactPoint + Vector3.up * _dropHeight;
            var meteor = _meteorPrefab != null
                ? Instantiate(_meteorPrefab, position, Quaternion.identity)
                : CreateFallbackMeteor(position);

            meteor.Initialize();
            meteor.Expired += OnMeteorExpired;

            _live.Add(meteor);
            Spawned?.Invoke(meteor);
            return meteor;
        }

        void OnMeteorExpired(Meteor meteor)
        {
            meteor.Expired -= OnMeteorExpired;
            _live.Remove(meteor);
        }

        /// <summary>
        /// 프리팹이 없을 때 쓰는 그레이박스 운석.
        /// 아트가 들어오면 프리팹을 꽂아 대체한다.
        /// </summary>
        static Meteor CreateFallbackMeteor(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Meteor";
            go.layer = GameLayers.Meteor;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (PhysicsScale.MeteorRadius * 2f);

            go.AddComponent<Rigidbody>();

            // 플레이어 피해는 새 시스템을 만들지 않고 캐릭터 파트의 DamageSource 를 그대로 쓴다.
            go.AddComponent<DamageSource>();

            return go.AddComponent<Meteor>();
        }
    }
}
