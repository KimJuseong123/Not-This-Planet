using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 운석을 <b>공세 단위</b>로 예고하고 떨어뜨린다(기획 §17.3~17.7).
    ///
    /// 흐름은 이렇다.
    ///  1) 공세 간격이 되기 5초 전에 <see cref="WaveIncoming"/> 을 보낸다(§17.3).
    ///  2) 간격이 되면 이번 공세의 운석 수를 정하고(§17.4 · §17.6) 1~2초 간격으로
    ///     하나씩 떨어뜨린다.
    ///  3) 운석 하나를 낼 때 <b>예고와 운석을 함께</b> 만든다. 예고가 켜져 있는 동안
    ///     운석이 실제로 떨어지고 있어야 한다(§17.2 "착탄 3초 전").
    ///
    /// <b>3번이 예전에는 반대였다.</b> 예고가 <b>끝난 뒤</b> 운석을 30m 위에 만들었고,
    /// 중력 −2.8m/s² 에서 그 높이의 자유낙하는 4.63초라, 표시가 착탄 4.63초 <b>전에
    /// 사라졌다</b>. S15P21B202-233 이 방망이를 만들며 드러났다 — §18.1 은 "타격 가능한
    /// 순간에는 낙하 표시가 강하게 점멸한다" 고 정하는데 그 순간에는 표시가 없었다.
    /// 지금은 <see cref="PhysicsScale.MeteorDropSpeed"/> 로 아래로 밀어 예고가 끝나는
    /// 순간 닿게 맞춘다.
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

        [SerializeField, Min(0f)] float _warningSeconds = PhysicsScale.MeteorWarningSeconds;
        [SerializeField, Min(0f)] float _dropHeight = PhysicsScale.MeteorDropHeight;

        [SerializeField]
        [Tooltip("발사 구역. 기획 §17.5 의 10% 후보이며, 비우면 그 후보를 환경으로 돌린다.")]
        Transform _launchPad;

        [SerializeField, Range(1, 4)]
        [Tooltip("접속 인원. 기획 §17.6 이 이 값으로 운석 수만 조정한다.")]
        int _crew = 4;

        [SerializeField]
        [Tooltip("같은 시드면 같은 순서로 떨어진다. 재현과 동기화에 쓴다.")]
        int _seed = 12345;

        readonly List<PendingMeteorDrop> _pending = new();
        readonly List<Meteor> _live = new();

        /// <summary>작업 중인 곳을 모으는 임시 버퍼. 매 틱 새로 만들지 않는다.</summary>
        readonly List<Vector3> _workAreas = new();

        System.Random _random;
        float _elapsed;

        // 공세 상태. 공세 사이에는 _waveRemaining 이 0 이다.
        float _sinceLastWave;
        int _waveRemaining;
        float _sinceLastInWave;
        float _nextInWaveDelay;
        bool _leadSent;

        /// <summary>
        /// 5초 전 신호에서 정한 이번 공세의 운석 수.
        ///
        /// <b>신호 때 정해 기억한다.</b> 신호와 공세 시작 사이에 5초가 흐르므로, 두 곳에서
        /// 각각 계산하면 경과 시간이 달라 값이 갈린다 — 검증에서 신호는 1개라 하고 실제로
        /// 2개가 떨어지는 것을 보고 알았다. 신호가 말한 수가 곧 오는 수여야 한다.
        /// </summary>
        int _plannedWaveSize;

        /// <summary>
        /// 마지막으로 고른 목표. 기획 §17.5 "동일한 플레이어나 위치를 연속 선택하지
        /// 않는다" 를 위해 기억한다.
        /// </summary>
        Vector3 _lastTarget;
        bool _hasLastTarget;
        PlayerRig _lastTargetPlayer;

        /// <summary>예고 중인 낙하. 표시는 UI·Art 가 이 값을 읽어 만든다.</summary>
        public IReadOnlyList<PendingMeteorDrop> Pending => _pending;

        /// <summary>아직 살아 있는 운석.</summary>
        public IReadOnlyList<Meteor> Live => _live;

        public float Elapsed => _elapsed;

        /// <summary>지금 적용되는 공세 간격(초). 기획 §17.4 의 표가 정한다.</summary>
        public float CurrentInterval => IntervalAt(_elapsed);

        /// <summary>이번 공세에 남은 운석 수. 공세 사이에는 0 이다.</summary>
        public int WaveRemaining => _waveRemaining;

        /// <summary>공세가 도는 중인가.</summary>
        public bool WaveActive => _waveRemaining > 0;

        /// <summary>접속 인원(1~4). 기획 §17.6 의 보정에 쓴다.</summary>
        public int Crew => _crew;

        /// <summary>
        /// 접속 인원을 넣는다. <c>GameSession</c> 이 판정을 한 곳에 모아 두었으므로
        /// 거기서 부른다.
        /// </summary>
        public void SetCrew(int crew) => _crew = Mathf.Clamp(crew, 1, 4);

        public void SetLaunchPad(Transform launchPad) => _launchPad = launchPad;

        public event Action<PendingMeteorDrop> DropScheduled;

        public event Action<Meteor> Spawned;

        /// <summary>
        /// 공세가 약 5초 뒤 시작된다(기획 §17.3). 인수는 이번 공세의 운석 수다.
        ///
        /// <b>신호만 보낸다.</b> 유성우가 많아지고 진동음이 울리고 조명이 깜빡이는 것은
        /// [Art]·[Sound] 가 만들고, §17.3 이 "문구나 카운트다운 숫자는 표시하지 않는다"
        /// 고 금지한다.
        /// </summary>
        public event Action<int> WaveIncoming;

        Transform Area => _area != null ? _area : transform;

        void Awake() => ResetRun();

        /// <summary>
        /// 시드를 다시 심고 진행 상황을 비운다. 씬 시작과 재시도에서 부른다.
        /// </summary>
        public void ResetRun()
        {
            _random = new System.Random(_seed);
            _elapsed = 0f;
            _sinceLastWave = 0f;
            _waveRemaining = 0;
            _sinceLastInWave = 0f;
            _nextInWaveDelay = 0f;
            _leadSent = false;
            _plannedWaveSize = 0;
            _hasLastTarget = false;
            _lastTargetPlayer = null;
            _pending.Clear();
            _live.Clear();
        }

        /// <summary>
        /// 새 예고를 만들지 않는가. 기획 §22.3 의 발사 중과 §22.4 의 실패 수습 사이에
        /// 쓴다.
        ///
        /// <b>이미 떨어지는 중인 운석은 그대로 둔다.</b> 공중에서 멈춰 세우면 발사 순간에
        /// 하늘에 운석이 박혀 있게 된다. 예고만 끊고 남은 것은 떨어지게 두는 편이 맞다.
        ///
        /// 재개할 때 <see cref="ResetRun"/> 을 쓰지 않는다 — 시드를 다시 심으면 실패
        /// 이전과 같은 순서가 반복되고, 무엇보다 기획 §22.4 가 "발사 실패는 스테이지
        /// 초기화가 아니다" 라고 못박았다.
        /// </summary>
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;

        /// <summary>
        /// 시드만 다시 심는다.
        ///
        /// <b>간격 인수를 받지 않는다.</b> 예전에는 시작·최소 간격과 상승 시간을 받아
        /// 선형으로 줄였는데, 기획 §17.4 는 시간대별 <b>표</b>다(30 → 25 → 20 → 15초).
        /// 곡선을 인수로 흉내내면 표와 어긋나므로 <see cref="MeteorWaveTable"/> 하나만
        /// 본다. 시간대를 바꾸려면 그 표를 고친다.
        /// </summary>
        public void Configure(int seed)
        {
            _seed = seed;
            ResetRun();
        }

        public void SetArea(Transform area, Vector2 size)
        {
            _area = area;
            _areaSize = size;
        }

        /// <summary>
        /// 그 시각의 공세 간격(초). 기획 §17.4 의 표가 정한다.
        ///
        /// <b>선형이 아니라 계단이다.</b> 예전에는 8초에서 2초까지 180초에 걸쳐 매끄럽게
        /// 줄었는데, 그러면 §17.3 의 "공세 약 5초 전 신호" 가 붙을 자리가 없다 — 운석이
        /// 끊임없이 하나씩 오는 것과 <b>묶음으로</b> 오는 것은 다른 리듬이고, 기획이
        /// 요구하는 것은 후자다.
        /// </summary>
        public float IntervalAt(float elapsedSeconds) =>
            MeteorWaveTable.WaveIntervalAt(elapsedSeconds);

        /// <summary>그 시각의 중형 확률 0~1. 10분 전에는 0 이다(§17.7).</summary>
        public float MediumChanceAt(float elapsedSeconds) =>
            MeteorWaveTable.MediumChanceAt(elapsedSeconds);

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

        /// <summary>
        /// 공세를 시작하고 공세 안에서 하나씩 떨어뜨린다(기획 §17.4).
        /// </summary>
        void AdvanceSchedule(float deltaTime)
        {
            if (IsPaused) return;

            if (_waveRemaining > 0)
            {
                AdvanceWithinWave(deltaTime);
                return;
            }

            _sinceLastWave += deltaTime;

            var interval = CurrentInterval;

            // 5초 전 신호. 간격이 5초보다 짧을 일은 없지만(표의 최소가 15초다) 그래도
            // 0 아래로 내려가지 않게 잡는다.
            var leadAt = Mathf.Max(0f, interval - PhysicsScale.MeteorWaveLeadSeconds);
            if (!_leadSent && _sinceLastWave >= leadAt)
            {
                _leadSent = true;
                _plannedWaveSize = RollWaveSize();
                WaveIncoming?.Invoke(_plannedWaveSize);
            }

            if (_sinceLastWave < interval) return;

            // 신호를 못 보낸 경우(간격이 신호 시간보다 짧으면)에는 여기서 정한다.
            if (!_leadSent) _plannedWaveSize = RollWaveSize();

            _sinceLastWave = 0f;
            _leadSent = false;
            _waveRemaining = _plannedWaveSize;
            _sinceLastInWave = 0f;
            _nextInWaveDelay = 0f;   // 공세의 첫 운석은 바로 떨어진다
        }

        /// <summary>
        /// 이번 공세의 운석 수를 뽑는다. <b>한 공세에 한 번만 부른다</b> —
        /// <see cref="_plannedWaveSize"/> 의 주석이 그 이유를 적어 두었다.
        /// </summary>
        int RollWaveSize() =>
            MeteorWaveTable.WaveSizeAt(_elapsed, _crew, _random.NextDouble());

        void AdvanceWithinWave(float deltaTime)
        {
            _sinceLastInWave += deltaTime;
            if (_sinceLastInWave < _nextInWaveDelay) return;

            _sinceLastInWave = 0f;
            _nextInWaveDelay = Mathf.Lerp(
                PhysicsScale.MeteorWithinWaveMinSeconds,
                PhysicsScale.MeteorWithinWaveMaxSeconds,
                (float)_random.NextDouble());

            _waveRemaining--;
            Launch();
        }

        /// <summary>예고와 운석을 <b>함께</b> 만든다.</summary>
        void Launch()
        {
            var point = NextImpactPoint();
            var size = _random.NextDouble() < MediumChanceAt(_elapsed)
                ? MeteorSize.Medium
                : MeteorSize.Small;

            var meteor = Spawn(point, size);

            var drop = new PendingMeteorDrop(point, _warningSeconds, _warningSeconds,
                                             Meteor.RadiusFor(size));
            _pending.Add(drop);
            DropScheduled?.Invoke(drop);

            if (meteor != null) Spawned?.Invoke(meteor);
        }

        /// <summary>
        /// 예고 표시를 진행시킨다. <b>여기서 운석을 만들지 않는다.</b>
        ///
        /// 예전에는 예고가 끝나면 그때 운석을 만들었다. 지금은 <see cref="Launch"/> 가
        /// 예고와 운석을 함께 내므로, 이 함수는 표시가 끝난 예고를 치우기만 한다 —
        /// 그 순간이 곧 착탄 시각이다(기획 §17.2).
        /// </summary>
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

        /// <summary>
        /// 다음 착탄 지점. 기획 §17.5 의 비율로 후보를 고른다.
        ///
        /// <b>균등 무작위가 아니다.</b> 예전에는 사각 영역 안에서 아무 데나 골랐는데,
        /// 그러면 §17.5 의 "무작위 플레이어 주변 30%" 와 "작업 지역 25%" 가 성립하지
        /// 않아 운석이 <b>방해가 되지 않는다</b> — 넓은 맵에서 아무 데나 떨어지면
        /// 플레이어 근처에 올 일이 거의 없다.
        ///
        /// 고른 후보에 대상이 없으면(플레이어가 다 기절했거나 작업 중인 곳이 없거나
        /// 발사대가 비어 있으면) <b>환경으로 돌린다.</b> 비율을 억지로 맞추려고 없는
        /// 대상을 만들 이유가 없다.
        /// </summary>
        Vector3 NextImpactPoint()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var kind = MeteorWaveTable.TargetKindFor(_random.NextDouble());
                var point = PointFor(kind);

                // 같은 자리를 연속으로 고르지 않는다(§17.5).
                if (_hasLastTarget &&
                    Vector3.Distance(point, _lastTarget) < PhysicsScale.MeteorTargetRepeatDistance)
                    continue;

                Remember(point);
                return point;
            }

            var fallback = EnvironmentPoint();
            Remember(fallback);
            return fallback;
        }

        void Remember(Vector3 point)
        {
            _lastTarget = point;
            _hasLastTarget = true;
        }

        Vector3 PointFor(MeteorTargetKind kind)
        {
            if (kind == MeteorTargetKind.NearPlayer) return NearPlayerPoint();

            // <b>플레이어를 노리지 않았으면 연속 금지를 푼다.</b>
            //
            // 이것이 없으면 한 번 노린 사람이 영구히 제외된다. 혼자 하는 동안에는
            // 후보가 그 한 명뿐이라 두 번째부터 플레이어 주변이 아예 나오지 않는다 —
            // 200번 중 6번(3%)밖에 나오지 않는 것을 보고 알았다. 기획 §17.5 는 30% 다.
            _lastTargetPlayer = null;

            return kind switch
            {
                MeteorTargetKind.WorkArea => WorkAreaPoint(),
                MeteorTargetKind.LaunchPad => LaunchPadPoint(),
                _ => EnvironmentPoint(),
            };
        }

        /// <summary>영역 안의 열린 지면. 후보를 찾지 못했을 때의 기본값이기도 하다.</summary>
        Vector3 EnvironmentPoint()
        {
            var center = Area.position;
            var x = center.x + ((float)_random.NextDouble() - 0.5f) * _areaSize.x;
            var z = center.z + ((float)_random.NextDouble() - 0.5f) * _areaSize.y;

            return new Vector3(x, center.y, z);
        }

        /// <summary>
        /// 무작위 플레이어 주변 1~3m(§17.5).
        ///
        /// <b>기절한 플레이어는 제외한다.</b> §17.5 가 그렇게 정한다 — 쓰러진 사람을
        /// 계속 때리면 §20.4 의 "안전한 곳으로 옮긴 뒤 구조한다" 가 성립하지 않는다.
        ///
        /// <b>정확한 위치를 노리지 않는다.</b> §17.5 "플레이어의 정확한 위치에 강제로
        /// 명중시키지 않는다" — 피할 수 없는 공격은 §3.3 과 어긋난다.
        /// </summary>
        Vector3 NearPlayerPoint()
        {
            var target = PickPlayer();
            if (target == null) return EnvironmentPoint();

            _lastTargetPlayer = target;

            var angle = (float)_random.NextDouble() * Mathf.PI * 2f;
            var radius = Mathf.Lerp(
                PhysicsScale.MeteorPlayerTargetMinRadius,
                PhysicsScale.MeteorPlayerTargetMaxRadius,
                (float)_random.NextDouble());

            var position = target.Position;
            return new Vector3(
                position.x + Mathf.Cos(angle) * radius,
                Area.position.y,
                position.z + Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 노릴 플레이어. 기절한 사람과 <b>직전에 노린 사람</b>을 뺀다(§17.5).
        ///
        /// 뺀 뒤 아무도 남지 않으면 null 이다 — 혼자 하는 동안 연속 금지를 지키면
        /// 노릴 사람이 없어지므로, 그때는 환경으로 돌아간다.
        /// </summary>
        PlayerRig PickPlayer()
        {
            var candidates = 0;
            for (var i = 0; i < PlayerRig.All.Count; i++)
                if (IsTargetable(PlayerRig.All[i])) candidates++;

            if (candidates == 0) return null;

            var index = (int)(_random.NextDouble() * candidates);
            if (index >= candidates) index = candidates - 1;

            for (var i = 0; i < PlayerRig.All.Count; i++)
            {
                if (!IsTargetable(PlayerRig.All[i])) continue;
                if (index-- == 0) return PlayerRig.All[i];
            }

            return null;
        }

        bool IsTargetable(PlayerRig player) =>
            player != null && !player.Downed && player != _lastTargetPlayer;

        /// <summary>
        /// 작업 중인 곳 주변(§17.5 25%).
        ///
        /// "작업 중" 을 두 가지로 읽었다 — <b>당기고 있는 고정못</b>(§12)과 <b>비드가
        /// 쌓인 파츠</b>(§16). 기획이 "고정못 제거·운반·용접 중인 작업 지역" 이라고
        /// 적었고, 그 둘이 코드에서 알 수 있는 전부다. 운반 중인 위치는 견인빔이
        /// 들고 있는 파츠의 위치이므로 결국 파츠에서 같이 잡힌다.
        /// </summary>
        Vector3 WorkAreaPoint()
        {
            _workAreas.Clear();

            for (var i = 0; i < Parts.PartClamp.All.Count; i++)
            {
                var clamp = Parts.PartClamp.All[i];
                if (clamp == null || clamp.IsReleased || clamp.Pullers <= 0) continue;

                _workAreas.Add(clamp.transform.position);
            }

            for (var i = 0; i < Parts.WeldBead.All.Count; i++)
            {
                var bead = Parts.WeldBead.All[i];
                if (bead == null || bead.BlobCount <= 0 || bead.Owner == null) continue;

                _workAreas.Add(bead.Owner.transform.position);
            }

            if (_workAreas.Count == 0) return EnvironmentPoint();

            var index = (int)(_random.NextDouble() * _workAreas.Count);
            if (index >= _workAreas.Count) index = _workAreas.Count - 1;

            var around = _workAreas[index];
            var angle = (float)_random.NextDouble() * Mathf.PI * 2f;
            var radius = Mathf.Lerp(
                PhysicsScale.MeteorPlayerTargetMinRadius,
                PhysicsScale.MeteorPlayerTargetMaxRadius,
                (float)_random.NextDouble());

            return new Vector3(
                around.x + Mathf.Cos(angle) * radius,
                Area.position.y,
                around.z + Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 발사 구역 <b>주변</b>(§17.5 10%).
        ///
        /// 기획이 "우주선 중심이 아니라 주변의 열린 지면을 선택한다" 고 못박는다.
        /// 그래서 반경 안이 아니라 <b>테두리</b>를 고른다 — 중심을 노리면 쌓아 둔 탑을
        /// 직접 때려, §22.4 의 "발사 하중으로 품질을 검증한다" 보다 운이 앞선다.
        /// </summary>
        Vector3 LaunchPadPoint()
        {
            if (_launchPad == null) return EnvironmentPoint();

            var angle = (float)_random.NextDouble() * Mathf.PI * 2f;
            var radius = Mathf.Lerp(
                PhysicsScale.LaunchPadRadius * 0.7f,
                PhysicsScale.LaunchPadRadius,
                (float)_random.NextDouble());

            var center = _launchPad.position;
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                Area.position.y,
                center.z + Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 운석 하나를 만든다. <b>예고가 끝나는 순간 닿도록</b> 초기 속도를 준다.
        /// </summary>
        Meteor Spawn(Vector3 impactPoint, MeteorSize size)
        {
            var position = impactPoint + Vector3.up * _dropHeight;
            var meteor = _meteorPrefab != null
                ? Instantiate(_meteorPrefab, position, Quaternion.identity)
                : CreateFallbackMeteor(position);

            meteor.Initialize();
            meteor.SetSize(size);

            // 낙하 예측의 기준면을 넘긴다(S15P21B202-233). 방망이가 "착탄 몇 초 전" 을
            // 이 값으로 푼다.
            meteor.SetGroundHeight(impactPoint.y);

            var body = meteor.GetComponent<Rigidbody>();
            if (body != null) body.linearVelocity = Vector3.down * DropSpeedFor(_dropHeight);

            meteor.Expired += OnMeteorExpired;
            _live.Add(meteor);
            return meteor;
        }

        /// <summary>
        /// 예고 시간에 맞춰 닿게 하는 초기 하강 속도(m/s).
        ///
        /// h = v₀·t + ½·|g|·t² 를 v₀ 로 푼다. 기본값(30m · 3초 · 2.8m/s²)에서 5.8m/s 이며
        /// <see cref="PhysicsScale.MeteorDropSpeed"/> 와 같아야 한다 — 상수는 사람이 읽는
        /// 값이고 이 계산은 높이나 예고 시간을 바꿔도 맞게 두기 위한 것이다.
        ///
        /// 자유낙하만으로 예고보다 빨리 닿는 높이라면 음수가 나온다. 그때는 0 으로 둔다 —
        /// <b>위로 던지지 않는다.</b> 하늘에서 떨어지는 것이 §17.1 이다.
        /// </summary>
        float DropSpeedFor(float height)
        {
            if (_warningSeconds <= 0f) return 0f;

            var gravity = -UnityEngine.Physics.gravity.y;
            var speed = (height - 0.5f * gravity * _warningSeconds * _warningSeconds)
                        / _warningSeconds;

            return Mathf.Max(0f, speed);
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

            // DamageSource 를 붙이지 않는다. 그것은 "닿으면 25" 하나뿐이어서 §20.1 의
            // 세 단계를 표현할 수 없고, 착탄 후에도 0.5초마다 다시 때렸다.
            // Meteor 가 직접 단계를 정한다(S15P21B202-247).

            return go.AddComponent<Meteor>();
        }
    }
}
