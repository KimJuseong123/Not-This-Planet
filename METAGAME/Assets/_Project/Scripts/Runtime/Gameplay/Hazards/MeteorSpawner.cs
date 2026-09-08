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

        [Header("낙하")]
        [SerializeField, Min(0f)] float _warningSeconds = PhysicsScale.MeteorWarningSeconds;
        [SerializeField, Min(0f)] float _dropHeight = PhysicsScale.MeteorDropHeight;
        [SerializeField]
        [Tooltip("켜면 낙하 속도를 직접 사용한다. 끄면 고도·예고 시간·중력으로 자동 계산한다.")]
        bool _overrideDropSpeed;
        [SerializeField, Min(0f)] float _dropSpeed = PhysicsScale.MeteorDropSpeed;
        [SerializeField, Range(1f, 89.9f)] float _entryAngleDegrees = PhysicsScale.MeteorEntryAngleDegrees;
        [SerializeField, Min(0)] int _entryAttempts = PhysicsScale.MeteorEntryAttempts;
        [SerializeField, Min(0f)] float _entryClearance = PhysicsScale.MeteorEntryClearance;
        [SerializeField, Min(0f)] float _groundProbe = PhysicsScale.MeteorGroundProbe;

        [Header("공세")]
        [SerializeField] MeteorWaveBand[] _waveBands = MeteorWaveBand.CreateDefaults();
        [SerializeField, Min(0f)] float _waveLeadSeconds = PhysicsScale.MeteorWaveLeadSeconds;
        [SerializeField, Min(0f)] float _withinWaveMinSeconds = PhysicsScale.MeteorWithinWaveMinSeconds;
        [SerializeField, Min(0f)] float _withinWaveMaxSeconds = PhysicsScale.MeteorWithinWaveMaxSeconds;
        [SerializeField, Range(0f, 1f)] float _soloCrewScale = 0.45f;
        [SerializeField, Range(0f, 1f)] float _twoCrewScale = 0.65f;
        [SerializeField, Range(0f, 1f)] float _threeCrewScale = 0.80f;
        [SerializeField, Range(0f, 1f)] float _fourCrewScale = 1f;

        [Header("목표 위치")]
        [SerializeField, Range(0f, 1f)] float _environmentWeight = 0.35f;
        [SerializeField, Range(0f, 1f)] float _nearPlayerWeight = 0.30f;
        [SerializeField, Range(0f, 1f)] float _workAreaWeight = 0.25f;
        [SerializeField, Range(0f, 1f)] float _launchPadWeight = 0.10f;
        [SerializeField, Min(0f)] float _playerTargetMinRadius = PhysicsScale.MeteorPlayerTargetMinRadius;
        [SerializeField, Min(0f)] float _playerTargetMaxRadius = PhysicsScale.MeteorPlayerTargetMaxRadius;
        [SerializeField, Min(0f)] float _targetRepeatDistance = PhysicsScale.MeteorTargetRepeatDistance;

        [Header("운석 물리·경고·방망이")]
        [SerializeField] MeteorTuning _tuning = new();

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

        /// <summary>대각선 경로 검사의 버퍼. 운석마다 새로 만들지 않는다.</summary>
        static readonly RaycastHit[] EntryProbe = new RaycastHit[16];
        static readonly MeteorWaveBand DefaultWaveBand = new(0f, 30f, 1, 2, 0f);
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

        /// <summary>
        /// 지면에 박힌 흔적 하나(기획 §19).
        ///
        /// 나이를 <b>여기서</b> 센다. 운석 쪽에서 세면 200개가 매 스텝 틱을 받아야 하고,
        /// §19 이 "충돌 후 고비용 물리 … 를 끄고" 라고 정한 것과 어긋난다. 식은 뒤에는
        /// 여기서도 세지 않으므로 비용이 0이 된다.
        /// </summary>
        public sealed class Scar
        {
            public Meteor Meteor;
            public Vector3 Point;
            public float Radius;
            public float Age;

            /// <summary>다 식었는가. 기획 §19 "약 5초 동안 … 약해진다".</summary>
            public float CoolSeconds;

            public bool Cooled => Age >= CoolSeconds;
        }

        readonly List<Scar> _scars = new();

        /// <summary>
        /// 지면에 박힌 흔적. 오래된 것이 앞이다(기획 §19).
        ///
        /// <b>발사 실패 뒤에도 유지된다</b> — §22.4 가 "발사 실패는 스테이지 초기화가
        /// 아니다" 라고 못박고 §19 도 같은 말을 한다. 지우는 것은
        /// <see cref="ClearScars"/> 뿐이다.
        /// </summary>
        public IReadOnlyList<Scar> Scars => _scars;

        /// <summary>상한에 걸려 지운 흔적 수. 조용히 잘라내지 않는다는 증거다.</summary>
        public int ScarsTrimmed { get; private set; }

        public float Elapsed => _elapsed;

        /// <summary>이 스포너가 생성하는 운석에 적용할 Inspector 설정.</summary>
        public MeteorTuning Tuning => _tuning;

        /// <summary>현재 설정으로 계산한 운석의 초기 수직 하강 속도(m/s).</summary>
        public float CurrentDropSpeed => _overrideDropSpeed ? _dropSpeed : DropSpeedFor(_dropHeight);

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

        void Awake()
        {
            EnsureConfiguration();
            ResetRun();
        }

        void EnsureConfiguration()
        {
            if (_tuning == null) _tuning = new MeteorTuning();
            if (_waveBands == null || _waveBands.Length == 0)
                _waveBands = MeteorWaveBand.CreateDefaults();
        }

        /// <summary>
        /// 시드를 다시 심고 진행 상황을 비운다. 씬 시작과 재시도에서 부른다.
        /// </summary>
        public void ResetRun()
        {
            EnsureConfiguration();
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

            // <b>목록만 비우면 누출이다.</b> 예전에는 운석이 착탄 6초 뒤 스스로
            // 사라졌으므로 문제가 없었는데, 흔적으로 남기기 시작하면(§19) 씬에 영영
            // 쌓인다(S15P21B202-251).
            DestroyAll(_live);
            ClearScars();
        }

        /// <summary>
        /// 흔적을 모두 지운다(기획 §19 "전원 기절로 스테이지를 완전히 재시작할 때만
        /// 초기화한다").
        ///
        /// <b>이것을 부르는 곳이 아직 하나뿐이다.</b> §20.5 의 전원 기절 판정
        /// (<c>GameSession.TickAllDowned</c>)은 지금 로컬 플레이어만 보고 스폰 복귀만 하는
        /// 상태라 "완전 재시작" 이 아니다. 그것은 멀티 판정이 걸려 [Network] 몫이므로,
        /// 여기서는 함수를 만들어 두고 재시작 경로에 연결만 한다.
        /// </summary>
        public void ClearScars()
        {
            for (var i = 0; i < _scars.Count; i++)
                if (_scars[i] != null && _scars[i].Meteor != null)
                    _scars[i].Meteor.Despawn();

            _scars.Clear();
            ScarsTrimmed = 0;
        }

        static void DestroyAll(List<Meteor> meteors)
        {
            for (var i = 0; i < meteors.Count; i++)
                if (meteors[i] != null)
                    meteors[i].Despawn();

            meteors.Clear();
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
        public float IntervalAt(float elapsedSeconds) => WaveBandAt(elapsedSeconds).WaveIntervalSeconds;

        /// <summary>그 시각의 중형 확률 0~1. 10분 전에는 0 이다(§17.7).</summary>
        public float MediumChanceAt(float elapsedSeconds) => WaveBandAt(elapsedSeconds).MediumChance;

        MeteorWaveBand WaveBandAt(float elapsedSeconds)
        {
            EnsureConfiguration();

            var minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
            var selected = DefaultWaveBand;

            for (var i = 0; i < _waveBands.Length; i++)
            {
                var band = _waveBands[i];
                if (band != null && minutes >= band.FromMinutes) selected = band;
            }

            return selected;
        }

        float CrewScaleFor(int crew)
        {
            return Mathf.Clamp(crew, 1, 4) switch
            {
                1 => _soloCrewScale,
                2 => _twoCrewScale,
                3 => _threeCrewScale,
                _ => _fourCrewScale,
            };
        }

        MeteorTargetKind TargetKindFor(double roll)
        {
            var environmentWeight = Mathf.Max(0f, _environmentWeight);
            var nearPlayerWeight = Mathf.Max(0f, _nearPlayerWeight);
            var workAreaWeight = Mathf.Max(0f, _workAreaWeight);
            var launchPadWeight = Mathf.Max(0f, _launchPadWeight);
            var total = environmentWeight + nearPlayerWeight + workAreaWeight + launchPadWeight;
            if (total <= 0f) return MeteorTargetKind.Environment;

            var value = Mathf.Clamp01((float)roll) * total;
            if (value < environmentWeight) return MeteorTargetKind.Environment;
            value -= environmentWeight;
            if (value < nearPlayerWeight) return MeteorTargetKind.NearPlayer;
            value -= nearPlayerWeight;
            if (value < workAreaWeight) return MeteorTargetKind.WorkArea;

            return MeteorTargetKind.LaunchPad;
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
            AdvanceScars(deltaTime);
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
            var leadAt = Mathf.Max(0f, interval - _waveLeadSeconds);
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
        int RollWaveSize()
        {
            var band = WaveBandAt(_elapsed);
            var span = band.MaxMeteors - band.MinMeteors;
            var raw = band.MinMeteors
                      + (int)(Mathf.Clamp01((float)_random.NextDouble()) * (span + 1));
            raw = Mathf.Clamp(raw, band.MinMeteors, band.MaxMeteors);

            return Mathf.Max(1, Mathf.RoundToInt(raw * CrewScaleFor(_crew)));
        }

        void AdvanceWithinWave(float deltaTime)
        {
            _sinceLastInWave += deltaTime;
            if (_sinceLastInWave < _nextInWaveDelay) return;

            _sinceLastInWave = 0f;
            _nextInWaveDelay = Mathf.Lerp(
                _withinWaveMinSeconds,
                _withinWaveMaxSeconds,
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
                                             _tuning.ImpactRadiusFor(size),
                                             _tuning.BatSwingWindowSeconds,
                                             _tuning.WarningImminentSeconds,
                                             _tuning.WarningStrikableBlinkHz,
                                             _tuning.WarningImminentBlinkHz,
                                             _tuning.WarningLift,
                                             _tuning.Radius);
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

                // 박힌 운석은 흔적으로 옮기고 더 틱하지 않는다(기획 §19).
                if (meteor.Settled)
                {
                    _live.RemoveAt(i);
                    Remember(meteor);
                    continue;
                }

                meteor.Tick(deltaTime);
            }
        }

        /// <summary>
        /// 흔적을 늘린다. 나이는 <see cref="AdvanceScars"/> 가 센다.
        /// </summary>
        void Remember(Meteor meteor)
        {
            _scars.Add(new Scar
            {
                Meteor = meteor,
                // 박힌 몸통은 지면 아래다 — 분화구를 거기에 그리면 묻힌다.
                Point = meteor.ImpactPoint,
                Radius = meteor.ImpactRadius,
                Age = 0f,
                CoolSeconds = _tuning.ScarCoolSeconds,
            });

            Trim();
        }

        /// <summary>
        /// 상한을 넘으면 가장 오래된 것부터 지운다(기획 §19 의 성능 기준 150~200개).
        ///
        /// <b>로그를 남긴다.</b> 조용히 잘라내면 "다 남았다" 로 보인다.
        /// </summary>
        void Trim()
        {
            while (_scars.Count > _tuning.ScarLimit)
            {
                var oldest = _scars[0];
                _scars.RemoveAt(0);
                ScarsTrimmed++;

                if (oldest?.Meteor != null) oldest.Meteor.Despawn();

                Debug.Log("[SpaceFlop] 운석 흔적 상한("
                          + _tuning.ScarLimit
                          + ")을 넘어 가장 오래된 것을 지웠다 — 누적 "
                          + ScarsTrimmed + "개");
            }
        }

        /// <summary>
        /// 흔적의 나이를 센다(기획 §19 "약 5초 동안 … 약해진다").
        ///
        /// <b>식은 것은 건너뛴다.</b> 150~200개가 쌓여도 매 프레임 도는 비용이 0이 되어야
        /// 한다 — 그것이 §19 의 성능 기준이다.
        /// </summary>
        void AdvanceScars(float deltaTime)
        {
            for (var i = _scars.Count - 1; i >= 0; i--)
            {
                var scar = _scars[i];
                if (scar == null || scar.Meteor == null)
                {
                    _scars.RemoveAt(i);
                    continue;
                }

                if (scar.Cooled) continue;

                scar.Age += deltaTime;
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
                var kind = TargetKindFor(_random.NextDouble());
                var point = PointFor(kind);

                // 같은 자리를 연속으로 고르지 않는다(§17.5).
                if (_hasLastTarget &&
                    Vector3.Distance(point, _lastTarget) < _targetRepeatDistance)
                    continue;

                Remember(point);
                return point;
            }

            var fallback = OnGround(EnvironmentPoint());
            Remember(fallback);
            return fallback;
        }

        /// <summary>
        /// 낙하 예고와 흔적을 <b>그리는 컴포넌트를 붙인다</b>(S15P21B202-275).
        ///
        /// <b>아무도 이것을 만들지 않았다.</b> S15P21B202-241 이
        /// <see cref="MeteorWarningView"/> 를, 251 이 <see cref="MeteorScarView"/> 를
        /// 만들고 268 이 줄어드는 원으로 바꿨지만, 씬에도 프리팹에도 붙어 있지 않았고
        /// 런타임에 붙이는 코드도 없었다 — <b>세 이슈가 만든 표현이 인게임에서 한 번도
        /// 실행된 적이 없다.</b> "운석 워닝포인트가 안보인다" 의 진짜 원인이 이것이다.
        ///
        /// S15P21B202-254 가 예고 지점을 실제 지면 위로 내린 것은 필요했지만, 그리는
        /// 컴포넌트가 없으면 그것만으로는 아무것도 보이지 않는다.
        ///
        /// <b>검증이 이 실수를 잡을 수 없었다.</b> 검증은 뷰를 직접 만들어 붙이므로
        /// "게임에서 아무도 만들지 않는다" 를 구조적으로 볼 수 없다. 그래서 붙이는
        /// 장치를 여기 두고(검증이 부를 수 있다), 세계에 실제로 있는지는
        /// <c>SpaceSelfTest</c> 가 본다.
        ///
        /// <b>부르는 쪽은 부트스트랩이다.</b> 여기서 <c>Awake</c> 로 스스로 붙이지 않는
        /// 이유는 전용 서버다 — 화면이 없는 쪽에서 원판 200개를 만들 필요가 없고,
        /// <c>Metagame.Gameplay</c> 는 <c>Metagame.Network</c> 를 참조하지 않으므로
        /// 여기서는 전용 서버인지 알 수 없다. 그 판단은 부트스트랩이 갖는다.
        /// </summary>
        /// <returns>이번 호출로 새로 붙인 개수. 이미 있으면 0.</returns>
        public int EnsureViews()
        {
            var added = 0;

            if (GetComponent<MeteorWarningView>() == null)
            {
                gameObject.AddComponent<MeteorWarningView>().Configure(this);
                added++;
            }

            if (GetComponent<MeteorScarView>() == null)
            {
                gameObject.AddComponent<MeteorScarView>().Configure(this);
                added++;
            }

            return added;
        }

        /// <summary>
        /// 후보 지점을 <b>실제 지면 위</b>로 내린다(S15P21B202-254).
        ///
        /// 예전에는 스폰 영역 transform 의 y 를 그대로 썼다. 씬에서 그 값이 0 인데
        /// 바닥 메시가 0.06 에 있어서 낙하 예고 원(<see cref="PhysicsScale.MeteorWarningLift"/>
        /// 0.03)과 분화구가 <b>바닥에 묻혀 보이지 않았다.</b> 상수로 맞추지 않고 훑는 이유는
        /// 지형 높이가 바뀌어도 따라가야 하기 때문이다.
        ///
        /// 못 찾으면 받은 값을 그대로 돌려준다. 예고를 빼면 공세 개수가 흔들리고
        /// (§17.4 의 표가 어긋난다) 운석은 어차피 물리로 떨어져 무엇에든 부딪힌다 —
        /// 예고가 조금 어긋나는 편이 공세가 비는 것보다 낫다.
        /// </summary>
        Vector3 OnGround(Vector3 point)
        {
            var from = point + Vector3.up * _groundProbe;
            var distance = _groundProbe * 2f;

            return UnityEngine.Physics.Raycast(from, Vector3.down, out var hit, distance,
                                               GameLayers.SurfaceMask,
                                               QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }

        void Remember(Vector3 point)
        {
            _lastTarget = point;
            _hasLastTarget = true;
        }

        Vector3 PointFor(MeteorTargetKind kind) => OnGround(RawPointFor(kind));

        Vector3 RawPointFor(MeteorTargetKind kind)
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
                _playerTargetMinRadius,
                _playerTargetMaxRadius,
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
                _playerTargetMinRadius,
                _playerTargetMaxRadius,
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
        /// 운석 하나를 만든다. 자동 속도 모드에서는 <b>예고가 끝나는 순간 닿도록</b>
        /// 초기 속도를 계산해 준다.
        ///
        /// <b>대각선으로 온다</b>(S15P21B202-274). 착지 지점에서 수평으로 떨어진 곳에서
        /// 만들어 비스듬히 날린다 — 이유는
        /// <see cref="PhysicsScale.MeteorEntryAngleDegrees"/> 에 적었다.
        ///
        /// <b>착지 시각은 수직 낙하와 정확히 같다.</b> 중력은 y 만 건드리므로 수평 속도를
        /// 얼마로 주든 <see cref="DropSpeedFor"/> 가 푼 착탄 시각이 변하지 않는다.
        /// 방망이의 "착탄 몇 초 전" 계약(§18.1)을 그대로 지킨다.
        /// </summary>
        Meteor Spawn(Vector3 impactPoint, MeteorSize size)
        {
            var fallSpeed = CurrentDropSpeed;
            var entry = EntryOffsetFor(impactPoint, _tuning.ImpactRadiusFor(size));

            var position = impactPoint + entry + Vector3.up * _dropHeight;
            var meteor = _meteorPrefab != null
                ? Instantiate(_meteorPrefab, position, Quaternion.identity)
                : CreateFallbackMeteor(position);

            meteor.ApplyTuning(_tuning);
            meteor.Initialize();
            meteor.SetSize(size);

            // 낙하 예측의 기준면을 넘긴다(S15P21B202-233). 방망이가 "착탄 몇 초 전" 을
            // 이 값으로 푼다.
            meteor.SetGroundHeight(impactPoint.y);

            var body = meteor.GetComponent<Rigidbody>();
            if (body != null)
            {
                // 수평은 등속이다 — 공기 저항이 없으므로 거리 / 예고 시간이면 정확히
                // 예고가 끝나는 순간 착지 지점에 온다.
                var horizontal = _warningSeconds > 0f
                    ? -entry / _warningSeconds
                    : Vector3.zero;

                body.linearVelocity = horizontal + Vector3.down * fallSpeed;
            }

            meteor.Expired += OnMeteorExpired;
            _live.Add(meteor);
            return meteor;
        }

        /// <summary>
        /// 착지 지점 기준의 <b>수평 생성 오프셋</b>. 수직으로 떨어뜨릴 때는 0 이다.
        ///
        /// 방향은 <see cref="_random"/> 으로 뽑아 시드가 같으면 같은 경로가 나오게
        /// 한다 — 검증과 재현에 필요하다(<c>SameSeed_ProducesSameImpactPoints</c>).
        ///
        /// 경로가 막혀 있으면 방향을 다시 뽑고, 다 막히면 <see cref="Vector3.zero"/> 를
        /// 돌려 수직 낙하로 되돌린다.
        /// </summary>
        Vector3 EntryOffsetFor(Vector3 impactPoint, float radius)
        {
            var angle = Mathf.Clamp(_entryAngleDegrees, 1f, 89.9f);
            var reach = _dropHeight / Mathf.Tan(angle * Mathf.Deg2Rad);
            if (reach <= 0.01f) return Vector3.zero;

            for (var attempt = 0; attempt < _entryAttempts; attempt++)
            {
                var azimuth = (float)_random.NextDouble() * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(azimuth) * reach, 0f,
                                         Mathf.Sin(azimuth) * reach);

                if (PathIsClear(impactPoint + offset + Vector3.up * _dropHeight,
                                impactPoint, radius))
                    return offset;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 생성 지점에서 착지 지점까지 <b>정적 지형에 막히지 않았는가</b>.
        ///
        /// 경로 끝에는 반드시 지면이 있으므로 마지막
        /// <see cref="PhysicsScale.MeteorEntryClearance"/> 구간은 보지 않는다.
        ///
        /// <b>움직이는 것은 막힘으로 세지 않는다.</b> 플레이어나 굴러다니는 파츠가 잠깐
        /// 경로에 들어와 방향이 바뀌면, 같은 시드에서 다른 경로가 나와 재현이 깨진다 —
        /// 게다가 날아오는 운석에 맞는 것은 막을 일이 아니다. 판정 규칙은
        /// <c>Meteor.TryFindSupportBelow</c> 와 같다.
        /// </summary>
        bool PathIsClear(Vector3 from, Vector3 to, float radius)
        {
            var along = to - from;
            var length = along.magnitude - _entryClearance;
            if (length <= 0.01f) return true;

            var count = UnityEngine.Physics.SphereCastNonAlloc(
                from, radius, along.normalized, EntryProbe, length,
                GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var hit = EntryProbe[i];
                if (hit.collider == null) continue;

                var body = hit.collider.attachedRigidbody;
                if (body != null && !body.isKinematic) continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// 예고 시간에 맞춰 닿게 하는 자동 초기 하강 속도(m/s).
        ///
        /// h = v₀·t + ½·|g|·t² 를 v₀ 로 푼다. 기본값(30m · 3초 · 5m/s²)에서 2.5m/s 이며
        /// <see cref="PhysicsScale.MeteorDropSpeed"/> 와 같은 기본값을 만들지만, 이 계산은
        /// 높이나 예고 시간을 바꿔도 착탄 시각을 맞추기 위한 것이다.
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
