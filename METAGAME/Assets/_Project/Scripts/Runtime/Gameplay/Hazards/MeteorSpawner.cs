using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// Stage1은 독립 낙하 시계를, 기존 시험장/프리셋은 공세 묶음을 사용한다.
    /// 아래 공세 설명은 기존 묶음 모드의 계약이다.
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
        /// <summary>
        /// 기획 §17.4 의 시간대 표. <b>2026-09-18 부터 읽지 않는다.</b>
        ///
        /// 강도를 시계가 아니라 공세 컷신이 정하게 바꿨다 — 이유는
        /// <see cref="MeteorIntensity"/> 에 적었다. 필드를 지우지 않는 것은 이미 저장된
        /// 씬(<c>Stage1_Astra</c>·<c>CheatArena</c>)이 값을 들고 있고, 시간대 방식으로
        /// 되돌릴 수 있게 남겨 두기 위해서다.
        ///
        /// <b>인스펙터에서 숨긴다</b>(!525 AI 리뷰). 값이 보이면 고칠 수 있다고 믿게
        /// 되고, 고쳐도 아무 일이 없으면 그 시간이 통째로 버려진다. 직렬화는 유지하되
        /// 보이지만 않게 한다 — 씬을 다시 저장해도 값이 날아가지 않는다.
        /// </summary>
        [HideInInspector]
        [SerializeField] MeteorWaveBand[] _waveBands = MeteorWaveBand.CreateDefaults();
        [SerializeField, Min(0f)] float _waveLeadSeconds = PhysicsScale.MeteorWaveLeadSeconds;
        [SerializeField, Min(0f)] float _withinWaveMinSeconds = PhysicsScale.MeteorWithinWaveMinSeconds;
        [SerializeField, Min(0f)] float _withinWaveMaxSeconds = PhysicsScale.MeteorWithinWaveMaxSeconds;
        [Header("Stage1 트리거 공세 · 기획 §17.8")]
        [SerializeField, Range(0.05f, 1f)] float _assaultWaveIntervalMultiplier = 0.35f;
        [SerializeField, Range(0.05f, 1f)] float _assaultWithinWaveIntervalMultiplier = 0.5f;
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
        MeteorDropSchedule _dropSchedule;
        bool _independentDrops;
        bool UsesIndependentDrops => _independentDrops && _pinnedBand == null;
        int ActiveDropClocks => AssaultEnabled ? MeteorDropSchedule.SpawnerCount : 1;

        [Header("Stage1 독립 낙하")]
        [Tooltip("공세 때 선택한 플레이어의 수평 위치 주변에 낙하할 최대 반경(m).") ]
        [SerializeField, Min(0f)] float _assaultPlayerTargetRadius = 5f;
        [Tooltip("동시에 살아 있는 운석 상한. 기본 네트워크 패킷 용량에도 여유를 둔다.")]
        [SerializeField, Range(1, 16)] int _maxIndependentLiveMeteors = 16;

        /// <summary>솔로와 멀티 모두 동일한 8개 시계. 생성·복제 관리자는 하나만 유지한다.</summary>
        public void EnableIndependentDrops()
        {
            if (_independentDrops) return;
            _independentDrops = true;
            _dropSchedule = new MeteorDropSchedule(_seed);
            _waveRemaining = 0;
            _leadSent = false;
        }

        public bool IndependentDropsEnabled => UsesIndependentDrops;
        public float AssaultPlayerTargetRadius => Mathf.Max(0f, _assaultPlayerTargetRadius);

        /// <summary>대각선 경로 검사의 버퍼. 운석마다 새로 만들지 않는다.</summary>
        static readonly RaycastHit[] EntryProbe = new RaycastHit[16];
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
            /// <summary>
            /// 이 흔적의 고유 번호 (S15P21B202-355).
            ///
            /// <para><b>인덱스로는 짝을 지을 수 없다.</b> <see cref="Trim"/> 이
            /// <c>_scars.RemoveAt(0)</c> 으로 <b>앞에서</b> 지우므로, 상한을 넘는 순간
            /// 남은 전부가 한 칸씩 밀린다 — 인덱스로 자세를 보내면 그 프레임에 돌들이
            /// 서로의 자리로 순간이동한다. 하늘의 운석은 "몇 개가 어디에" 만 중요해서
            /// 번호로 짝지을 필요가 없었지만(<c>ApplyNetworkMeteors</c> 의 주석),
            /// 흔적은 사람이 <b>집어 옮기므로</b> 신원이 필요하다.</para>
            /// </summary>
            public int Id;

            public Meteor Meteor;
            public Vector3 Point;
            public float Radius;

            /// <summary>
            /// 박힌 뒤 지난 시간(초). <b>이것이 소멸 타이머다</b> —
            /// <see cref="AdvanceScars"/> 가 <see cref="MeteorTuning.ScarDespawnSeconds"/>
            /// 를 넘으면 운석을 없앤다.
            ///
            /// <para><b>표시가 사라져도 이 값은 남는다</b>(S15P21B202-653). 원반을
            /// 걷어내면서 색 관련 필드는 지웠지만 나이는 지우면 안 된다 — 예전에
            /// 운석 쪽 타이머와 갈라져 있다가 <b>흔적이 영구히 남은</b> 결함이
            /// 그것이었다(S15P21B202-438, 실제 플레이 로그에 36개가 살아 있었다).</para>
            /// </summary>
            public float Age;
        }

        readonly List<Scar> _scars = new();

        /// <summary>다음에 줄 흔적 번호. 0 은 "번호 없음" 으로 남겨 둔다.</summary>
        int _nextScarId;

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

        public bool AssaultEnabled { get; private set; }
        public bool SequenceHold { get; private set; }
        public void SetSequenceHold(bool hold) => SequenceHold = hold;

        /// <summary>기존 운석·파츠·난수 스트림을 유지한 채 공세 주기만 올린다.</summary>
        public void BeginAssault() => SetAssault(true);

        /// <summary>
        /// 공세 컷신을 지났는지 직접 세운다. 시험장이 두 단계를 오가며 재려고 쓴다
        /// (<see cref="MeteorIntensity"/>).
        ///
        /// <b>켜는 순간 첫 공세가 곧바로 온다</b> — <c>_sinceLastWave</c> 를 간격만큼
        /// 채워 둔다. 컷신이 끝났는데 몇 초 조용하면 연출이 거짓말이 된다.
        /// </summary>
        public void SetAssault(bool enabled)
        {
            if (AssaultEnabled == enabled) return;

            AssaultEnabled = enabled;
            if (UsesIndependentDrops)
            {
                // 각 시계의 첫 낙하도 무작위로 분산한다. 이전 묶음은 이어서 내보내지 않는다.
                _dropSchedule.Reset(_seed);
                _waveRemaining = 0;
                _leadSent = false;
                return;
            }

            // 켤 때는 곧바로 한 발, <b>끌 때는 처음부터</b>(!525 AI 리뷰). 풀 가동
            // 5초 기준으로 쌓인 경과를 그대로 두면 가랑비 10초 기준으로는 이미 넘겨서,
            // 되돌리자마자 한 발이 나가 "버튼에 적힌 초와 다르다" 가 된다.
            _sinceLastWave = enabled ? CurrentInterval : 0f;
            _leadSent = false;
        }

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
        public void SetCrew(int crew) =>
            _crew = _crewOverride > 0 ? _crewOverride : Mathf.Clamp(crew, 1, 4);

        public void SetLaunchPad(Transform launchPad) => _launchPad = launchPad;

        // ── 시험용 조작 (CheatArena) ─────────────────────────────────────────
        //
        // 아래 것들은 치트 시험장(<c>CheatArenaConsole</c>)이 쓴다. 공세를 기다리지 않고
        // 원하는 지점에 바로 떨어뜨리거나, 난이도 표의 뒷 시간대로 건너뛰기 위한 것이다.
        // 스케줄·난수·네트워크 경로는 손대지 않는다 — 여기서 만든 운석도 <see cref="Live"/>·
        // <see cref="Pending"/> 에 똑같이 들어가므로 예고 표시·방망이·흔적이 그대로 동작한다.

        /// <summary>
        /// 원하는 지점에 운석 하나를 <b>지금</b> 떨어뜨린다. 예고도 함께 만든다.
        /// 공세 스케줄과 난수 스트림은 건드리지 않는다(진입 방향만 난수를 쓴다).
        /// </summary>
        public void LaunchAt(Vector3 impactPoint, MeteorSize size)
        {
            if (_random == null) ResetRun();
            Launch(impactPoint, size);
        }

        /// <summary>
        /// 다음 <see cref="Tick"/> 에서 공세가 바로 시작되게 한다. 5초 전 신호도 같은
        /// 틱에 먼저 나간다.
        /// </summary>
        public void TriggerWave()
        {
            if (_waveRemaining > 0) return;
            _leadSent = false;
            _sinceLastWave = Mathf.Max(_sinceLastWave, CurrentInterval);
        }

        /// <summary>경과 시간을 옮긴다. 기획 §17.4 표의 뒷 시간대를 바로 시험할 때 쓴다.</summary>
        public void SkipTo(float elapsedSeconds) => _elapsed = Mathf.Max(0f, elapsedSeconds);

        /// <summary>다음 공세까지 남은 초. 공세 중이면 0.</summary>
        public float SecondsUntilWave =>
            UsesIndependentDrops ? _dropSchedule.SecondsUntilNext(ActiveDropClocks) :
            _waveRemaining > 0 ? 0f : Mathf.Max(0f, CurrentInterval - _sinceLastWave);

        /// <summary>
        /// 인원수 보정(§17.6)을 강제한다. 0 이면 해제하고 <see cref="SetCrew"/> 가 넣는 값을
        /// 다시 따른다. 혼자 시험하면서 4인 공세 규모를 보고 싶을 때 쓴다.
        /// </summary>
        public void OverrideCrew(int crew)
        {
            _crewOverride = Mathf.Clamp(crew, 0, 4);
            if (_crewOverride > 0) _crew = _crewOverride;
        }

        /// <summary>강제된 인원수. 0 이면 강제하지 않는다.</summary>
        public int CrewOverride => _crewOverride;

        int _crewOverride;

        /// <summary>
        /// 프리셋이 고정한 시간대. 있으면 <see cref="WaveBandAt"/> 가 경과 시간을 보지 않는다.
        /// </summary>
        MeteorWaveBand _pinnedBand;

        /// <summary>목표 비율 중 플레이어 주변을 강제한 값. 음수면 강제하지 않는다.</summary>
        float _nearPlayerWeightOverride = -1f;

        /// <summary>지금 걸려 있는 공세 강도. 없으면 <c>null</c> 이고 시간대 표를 따른다.</summary>
        public MeteorWavePreset ActivePreset { get; private set; }

        /// <summary>
        /// 지금 시간대의 충격파 반경 배율. 크기별 반경(§17.7)에 곱해 쓴다.
        ///
        /// <b>예고 원·운석·흔적이 모두 이 값을 거쳐야 한다.</b> 한 곳이라도 빠지면 §17.2 의
        /// "실제 충격 범위와 같은 크기" 가 깨져, 원 밖인데 맞거나 원 안인데 멀쩡해진다.
        /// </summary>
        public float ImpactRadiusScale => WaveBandAt(_elapsed).ImpactRadiusScale;

        /// <summary>크기별 충격파 반경(m)에 배율까지 적용한 값.</summary>
        public float ScaledImpactRadiusFor(MeteorSize size)
        {
            EnsureConfiguration();
            return _tuning.ImpactRadiusFor(size) * ImpactRadiusScale;
        }

        /// <summary>
        /// 공세 강도를 고정한다(<see cref="MeteorWavePreset"/>).
        ///
        /// <para>경과 시간을 보지 않게 되므로 같은 강도를 몇 번이고 반복해 잴 수 있다.
        /// <b>인원 보정을 4인으로 고정한다</b> — 그러지 않으면 혼자 고른 "후반" 이 개수 보정
        /// (§17.6 혼자 45%)에 눌려 이름과 다른 것이 나온다.</para>
        ///
        /// <para>진행 중이던 공세와 예고는 비운다. 강도를 바꾸는 순간 이전 강도로 예고된
        /// 운석이 남아 있으면 무엇을 재고 있는지 알 수 없다.</para>
        /// </summary>
        public void ApplyPreset(MeteorWavePreset preset)
        {
            if (preset == null) { ClearPreset(); return; }

            EnsureConfiguration();

            ActivePreset = preset;
            _pinnedBand = preset.ToBand();
            _nearPlayerWeightOverride = Mathf.Clamp01(preset.NearPlayerWeight);
            OverrideCrew(4);

            // <b><see cref="ResetRun"/> 을 그대로 쓴다.</b> 시계·예고·운석·흔적을 비우고
            // 시드를 다시 심어 같은 강도를 몇 번이고 같은 순서로 잴 수 있게 한다.
            // <see cref="BeginAssault"/> 로 켜진 §17.8 트리거 공세도 여기서 꺼진다 —
            // 켜진 채로 두면 간격이 한 번 더 곱해져 프리셋에 적힌 초와 달라진다.
            ResetRun();
            IsPaused = false;
            SetSequenceHold(false);
        }

        /// <summary>강도 고정을 풀고 시간대 표로 돌아간다. 인원 보정 강제도 함께 푼다.</summary>
        public void ClearPreset()
        {
            ActivePreset = null;
            _pinnedBand = null;
            _nearPlayerWeightOverride = -1f;
            OverrideCrew(0);

            // <b>강제를 푸는 것만으로는 4 가 남는다.</b> 실행 중에는 MeteorClockBridge 가 매
            // 고정틱 SetCrew 를 불러 곧 덮이지만, 그 어댑터가 없는 자리(검증·모래상자)에서는
            // 해제한 뒤에도 4인 규모가 그대로 나온다. 같은 출처로 즉시 되돌린다.
            SetCrew(Mathf.Max(1, PlayerRig.All.Count));
        }

        /// <summary>
        /// 목표 비율 중 플레이어 주변을 강제한다. 음수면 푼다.
        ///
        /// 나머지 셋(환경·작업 구역·발사 구역)은 원래 비율대로 남은 몫을 나눠 가진다.
        /// </summary>
        public void OverrideNearPlayerWeight(float weight) =>
            _nearPlayerWeightOverride = weight < 0f ? -1f : Mathf.Clamp01(weight);

        /// <summary>
        /// <b>시험용 목표 선택이 켜져 있는가.</b> 프리셋이나 근처 비율 강제가 켜면 참이 된다.
        ///
        /// <para><b>이 문지기가 스테이지를 지킨다.</b> 아래 두 가지는 플레이어 주변에 운석을
        /// 모으기 위한 것이라 §17.5 가 정한 분포를 바꾼다. 켜지지 않은 판
        /// (<c>Stage1_Astra</c> 를 포함한 모든 실제 판)에서는 예전 코드와 <b>한 줄도 다르지
        /// 않게</b> 돈다 — 난수를 뽑는 횟수와 순서까지 같다.</para>
        ///
        /// <list type="number">
        /// <item><see cref="PickPlayer"/> 가 후보가 바닥나면 연속 금지를 푼다.</item>
        /// <item><see cref="NextImpactPoint"/> 가 거리 검사에 걸려도 종류를 유지한다.</item>
        /// </list>
        /// </summary>
        bool TestTargeting => _nearPlayerWeightOverride >= 0f;

        /// <summary>지금 쓰이는 플레이어 주변 비율 0~1.</summary>
        public float NearPlayerWeight
        {
            get
            {
                if (_nearPlayerWeightOverride >= 0f) return _nearPlayerWeightOverride;

                var total = Mathf.Max(0f, _environmentWeight) + Mathf.Max(0f, _nearPlayerWeight)
                            + Mathf.Max(0f, _workAreaWeight) + Mathf.Max(0f, _launchPadWeight);
                return total > 0f ? Mathf.Max(0f, _nearPlayerWeight) / total : 0f;
            }
        }

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
            _dropSchedule = new MeteorDropSchedule(_seed);
            _elapsed = 0f;
            AssaultEnabled = false;
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
        ///
        /// <b>시계가 아니라 컷신이 정한다</b>(2026-09-18). 경과 시간은 더 이상 간격에
        /// 영향을 주지 않는다 — 이유는 <see cref="MeteorIntensity"/> 에 적었다.
        /// 인자를 남겨 두는 것은 프리셋과 검증이 특정 시점을 물어보기 때문이다.
        ///
        /// <b>공세 배율을 더 곱하지 않는다.</b> 예전에는 여기에
        /// <c>_assaultWaveIntervalMultiplier</c>(0.35)를 곱했는데, 컷신 뒤 값이
        /// <see cref="MeteorIntensity.FullBlast"/> 안에 이미 들어 있으므로 또 곱하면
        /// 5초가 1.75초가 된다.
        /// </summary>
        /// <param name="elapsedSeconds">
        /// <b>지금은 결과에 영향을 주지 않는다</b>(!525 AI 리뷰). 강도를 정하는 것은
        /// 컷신 단계뿐이고, 인자는 호출부 계약과 시간 기반으로 되돌릴 여지를 위해
        /// 남겨 둔다. 이 값을 바꿔 가며 곡선을 재려 하면 아무것도 안 바뀐다.
        /// </param>
        public float IntervalAt(float elapsedSeconds) =>
            WaveBandAt(elapsedSeconds).WaveIntervalSeconds;

        /// <summary>
        /// 그 시각의 중형 확률 0~1. 컷신 전에는 0 이다.
        ///
        /// <paramref name="elapsedSeconds"/> 는 <see cref="IntervalAt"/> 과 같은 이유로
        /// 결과에 영향을 주지 않는다.
        /// </summary>
        public float MediumChanceAt(float elapsedSeconds) =>
            WaveBandAt(elapsedSeconds).MediumChance;

        /// <summary>
        /// 지금 적용되는 강도. 우선순위는 <b>프리셋 → 컷신 단계</b> 다.
        ///
        /// <b>시간대 표(§17.4)를 더 이상 읽지 않는다</b>(2026-09-18) — 이유는
        /// <see cref="MeteorIntensity"/> 에 적었다. <paramref name="elapsedSeconds"/> 는
        /// 호출부 계약을 유지하려고 남겨 둔다.
        /// </summary>
        MeteorWaveBand WaveBandAt(float elapsedSeconds)
        {
            EnsureConfiguration();

            // 강도를 고정했으면 컷신 단계도 보지 않는다(<see cref="ApplyPreset"/>).
            if (_pinnedBand != null) return _pinnedBand;

            return MeteorIntensity.For(AssaultEnabled).ToBand();
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

            // 플레이어 주변을 강제했으면 그 몫을 먼저 떼고, 남은 몫을 나머지 셋이 원래
            // 비율대로 나눠 가진다. 나머지가 전부 0 이면(비율을 다 비워 둔 씬) 환경이 받는다.
            if (_nearPlayerWeightOverride >= 0f)
            {
                nearPlayerWeight = _nearPlayerWeightOverride;

                var rest = environmentWeight + workAreaWeight + launchPadWeight;
                var share = 1f - nearPlayerWeight;

                if (rest > 0f)
                {
                    var k = share / rest;
                    environmentWeight *= k;
                    workAreaWeight *= k;
                    launchPadWeight *= k;
                }
                else
                {
                    environmentWeight = share;
                }
            }

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

            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
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
            if (IsPaused || SequenceHold) return;
            if (UsesIndependentDrops)
            {
                var due = _dropSchedule.Tick(deltaTime, ActiveDropClocks);
                for (int i = 0; i < MeteorDropSchedule.SpawnerCount; i++)
                {
                    // 상한에 막힌 낙하는 버린다. 재개 시 밀린 운석이 폭주하지 않는다.
                    if ((due & (1 << i)) == 0 || _live.Count >= Mathf.Clamp(_maxIndependentLiveMeteors, 1, 16)) continue;
                    Launch();
                }
                return;
            }

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
            var min = band.MinMeteors;
            var max = band.MaxMeteors;
            var span = max - min;
            var raw = min + (int)(Mathf.Clamp01((float)_random.NextDouble()) * (span + 1));
            raw = Mathf.Clamp(raw, min, max);

            // 인원 보정(§17.6)은 <b>조인 다음</b>이다. 순서를 바꾸면 혼자 할 때 상한이
            // 8 이 아니라 3.6 이 되어 상한의 뜻이 달라진다.
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
                (float)_random.NextDouble())
                * (AssaultEnabled ? Mathf.Clamp(_assaultWithinWaveIntervalMultiplier, 0.05f, 1f) : 1f);

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

            Launch(point, size);
        }

        /// <summary>지점과 크기가 정해진 운석 하나를 예고와 함께 낸다.</summary>
        void Launch(Vector3 point, MeteorSize size)
        {
            var meteor = Spawn(point, size);

            var drop = new PendingMeteorDrop(point, _warningSeconds, _warningSeconds,
                                             ScaledImpactRadiusFor(size),
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
        // ── 네트워크에서 받은 예고 (S15P21B202-352) ──────────────────────────
        //
        // 참가자 기계는 공세를 **스스로 계산하지 않는다** — MeteorClockBridge 가 Tick 을
        // 부르지 않는다. 시드가 같아도 난수를 뽑는 횟수가 로컬 세계 상태에 달려 스트림이
        // 갈리기 때문이다(NearPlayerPoint 의 candidates, WorkAreaPoint 의 _workAreas).
        //
        // 그러면 참가자는 예고 원을 못 봐서 피하지도 쳐내지도 못한다. 그래서 호스트가 정한
        // 예고를 받아 여기 넣는다. **소비처는 하나도 안 고친다** — MeteorWarningView 와
        // Stage1AtmosphereAlert 가 둘 다 Pending 을 읽으므로 목록을 갈면 그대로 그려진다.

        /// <summary>
        /// 호스트가 정한 예고 목록으로 갈아 끼운다 (S15P21B202-352).
        ///
        /// <para><b>권위 쪽에서 부르면 안 된다</b> — 자기 스케줄을 지운다.
        /// <see cref="Tick"/> 을 돌리는 기계와 이것을 부르는 기계는 서로 배타적이다.</para>
        /// </summary>
        public void ApplyNetworkPending(IReadOnlyList<PendingMeteorDrop> drops)
        {
            _pending.Clear();
            if (drops == null) return;
            for (var i = 0; i < drops.Count; i++) _pending.Add(drops[i]);
        }

        /// <summary>
        /// 받은 예고의 남은 시간만 줄인다 (S15P21B202-352).
        ///
        /// <para>예고는 남은 시간 하나만 바뀌므로 매 틱 실어 보내기에는 아깝다(핫스팟
        /// 업링크에서는 패킷 수가 비용이다). 목록이 <b>달라질 때만</b> 받고 그 사이는
        /// 이 함수가 로컬에서 세어 원이 매끄럽게 줄어들게 한다.</para>
        ///
        /// <para><see cref="AdvancePending"/> 를 그대로 쓴다 — 그쪽은 <b>뺄셈과 목록 정리뿐</b>
        /// 이고 스폰·난수·스케줄 전진이 없다(스폰은 <see cref="AdvanceSchedule"/> 안에 있다).
        /// 갈림의 원인 셋이 다 그 밖에 있으므로 참가자가 불러도 안전하다.</para>
        /// </summary>
        public void AgeNetworkPending(float deltaTime) => AdvancePending(deltaTime);

        // ── 네트워크에서 받은 낙하 그림 (S15P21B202-352) ──────────────────
        //
        // 예고 원만 받으면 참가자 화면에 **돌이 안 보인다** — 하늘에서 떨어지는 그림이
        // 없고 바닥의 원만 깜빡인다("운석이 한쪽에서만 보인다", 2026-09-08 2대 실측).
        //
        // 참가자가 스스로 만들 수는 없다. 스폰은 AdvanceSchedule 안에 있고 그 난수
        // 스트림은 로컬 세계 상태에 달려 갈린다(ApplyNetworkPending 의 주석). 그래서
        // 호스트가 **지금 살아 있는 운석의 자세**를 실어 보내고 참가자는 그림만 띄운다.
        //
        // 그림은 판정에 관여하지 않는다: Meteor 컴포넌트를 끄고 충돌러를 모두 끈다.
        // 그래서 Meteor.cs 를 한 줄도 안 고친다 — 꺼진 컴포넌트에는 OnCollisionEnter 도
        // Tick 도 오지 않고, OnDisable 이 Meteor.All 에서 빼 준다.

        /// <summary>
        /// 살아 있는 운석 하나의 자세.
        ///
        /// <para><b>크기는 싣지 않는다</b> — <see cref="Meteor.SetSize"/> 는 충격 반경과
        /// 힘만 바꾸고 그림 크기는 건드리지 않는다. 판정에 관여하지 않는 그림에는 그 둘이
        /// 필요 없다.</para>
        ///
        /// <para><b>속도를 함께 싣는 이유.</b> 자세만 받아 보간하면 빠르게 떨어지는 물체는
        /// 눈에 보이게 뒤처진다(초당 10m 를 20의 속도로 좇으면 정상 편차가 0.5m — 운석
        /// 지름만큼이다). 속도를 한 번 맞춰 주면 다음 묶음이 올 때까지 <b>로컬 중력이
        /// 정확히 같은 궤적</b>을 그린다. 보간으로 흉내내는 대신 물리에 맡긴다.</para>
        /// </summary>
        public struct NetworkMeteorPose
        {
            public byte DeflectionGrade;
            public float GroundHeight;
            public bool Harmless;
            public float BatWindow;
            public float BatRadius;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
        }

        readonly List<Meteor> _ghosts = new();

        /// <summary>참가자 화면에 떠 있는 낙하 그림. 권위 쪽에서는 항상 비어 있다.</summary>
        public IReadOnlyList<Meteor> Ghosts => _ghosts;

        /// <summary>
        /// 호스트가 보낸 낙하 자세로 그림을 맞춘다 (S15P21B202-352).
        ///
        /// <para><b>권위 쪽에서 부르면 안 된다</b> — 자기 운석 위에 그림을 겹쳐 띄운다.
        /// <see cref="Tick"/> 을 돌리는 기계와 이것을 부르는 기계는 서로 배타적이다.</para>
        ///
        /// <para>짝은 번호로 짓는다. 돌은 서로 구별되지 않으므로 어느 그림이 어느 운석인지는
        /// 중요하지 않고 <b>몇 개가 어디에 있는지</b>가 중요하다 — 자세 목록처럼 순서가
        /// 어긋나 남의 자리로 가는 사고가 여기서는 눈에 보이지 않는다.</para>
        /// </summary>
        public void ApplyNetworkMeteors(IReadOnlyList<NetworkMeteorPose> poses)
        {
            var n = poses != null ? poses.Count : 0;

            while (_ghosts.Count > n)
            {
                var last = _ghosts.Count - 1;
                if (_ghosts[last] != null) _ghosts[last].Despawn();
                _ghosts.RemoveAt(last);
            }
            while (_ghosts.Count < n) _ghosts.Add(CreateGhost());

            for (var i = 0; i < n; i++)
            {
                var ghost = _ghosts[i];
                if (ghost == null) continue;

                ghost.ApplyGuideSnapshot(poses[i].GroundHeight, poses[i].Harmless, poses[i].BatWindow, poses[i].BatRadius,
                    poses[i].DeflectionGrade);
                var body = ghost.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = poses[i].Position;
                    body.rotation = poses[i].Rotation;
                    body.linearVelocity = poses[i].Velocity;
                    continue;
                }

                ghost.transform.SetPositionAndRotation(poses[i].Position, poses[i].Rotation);
            }
        }

        /// <summary>
        /// 그림을 모두 지운다. 매치가 끝나면 씬에 남겨 두지 않는다 — 충돌러가 꺼져 있어
        /// 남으면 지면을 뚫고 영원히 떨어진다.
        ///
        /// <para><see cref="Meteor.Despawn"/> 를 쓴다 — 이 파일의 다른 정리 경로와 같다.
        /// 그쪽이 <c>Application.isPlaying</c> 을 갈라 에디터에서도 옳게 지우고
        /// <c>Meteor.All</c> 에서도 빼 준다.</para>
        /// </summary>
        public void ClearNetworkMeteors()
        {
            for (var i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i] != null) _ghosts[i].Despawn();

            _ghosts.Clear();
        }

        // ── 네트워크에서 받은 흔적 (S15P21B202-355) ───────────────────────
        //
        // 참가자는 착탄을 스스로 계산하지 않으므로(MeteorClockBridge 가 조기 반환한다)
        // **착지한 돌이 아예 없다.** 그런데 상호작용은 서버가 판정하므로 — 입력 프레임이
        // 서버로 가고 서버 세계에서 포커스를 고른다 — **안 보이는 돌을 집을 수 있었다.**
        // 2026-09-10 QA 신고가 그것이다: "호스트 화면에서 운석이 보이는데 나는 안 보여.
        // 그런데 보이지만 않고 상호작용은 돼."
        //
        // 낙하 그림과 마찬가지로 **판정은 하지 않는다** — Meteor 컴포넌트를 끈다.
        // 다른 점이 둘이다.
        //
        //  · **번호로 짝을 짓는다.** 사람이 집어 옮기므로 어느 돌인지가 중요하다.
        //  · **콜라이더는 켜 둔다**(S15P21B202-658). 예전에는 낙하 그림처럼 콜라이더까지
        //    껐는데, 그러면 참가자가 착지한 돌을 **몸으로 통과**한다 — 호스트에게는
        //    막힌 벽이고, 피해는 호스트가 주므로 **안 부딪혔는데 체력만 깎였다.**
        //    판정을 막는 데 필요한 것은 Meteor 컴포넌트를 끄는 것 하나이고,
        //    콜라이더는 막는 일만 할 뿐 아무것도 판정하지 않는다(`-654` Q7).

        /// <summary>
        /// 착지한 흔적 하나의 자세. <b>속도를 싣지 않는다</b> — 쉬는 돌은 속도가 0이고,
        /// 끌리는 동안의 궤적은 자세를 자주 보내는 것으로 충분하다(낙하와 달리 초당
        /// 10m 로 움직이지 않는다).
        /// </summary>
        public struct NetworkScarState
        {
            public int Id;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        readonly Dictionary<int, Meteor> _scarGhosts = new();

        /// <summary>
        /// 그림이 마지막으로 받은 자세와 그 시각 (S15P21B202-434).
        ///
        /// <para><b>왜 필요한가.</b> 움직임 묶음은 전송 틱(30Hz, 33ms)마다 오는데
        /// 화면은 그보다 자주 그려진다(실측 120fps). 자리만 찍고 속도를 0 으로 두면
        /// 그림이 <b>33ms 동안 완전히 멈춰 있다가 순간이동</b>하므로, 굴러가는 돌이
        /// 뚝뚝 끊겨 보인다 — 2026-09-11 QA 신고가 그것이다.</para>
        ///
        /// <para>두 자세의 차이를 시간으로 나누면 속도가 나온다. 강체에 속도를 주면
        /// <c>RigidbodyInterpolation.Interpolate</c>(<see cref="CreateGhost"/> 가 켠다)가
        /// 그 사이를 렌더 프레임마다 채운다. <b>대역폭은 늘지 않는다</b> — 보내는 쪽은
        /// 그대로다.</para>
        ///
        /// <para>그림마다 따로 재는 이유: 묶음마다 실린 흔적이 다르다. 자고 있던 돌이
        /// 깨어나 처음 실리면 그 자세는 최대 2초(<c>ScarKeepalive</c>) 낡은 것이라,
        /// 묶음 하나의 간격으로 나누면 속도가 터진다.</para>
        /// </summary>
        readonly Dictionary<int, ScarMotion> _scarMotion = new();

        struct ScarMotion
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Time;

            /// <summary>
            /// 다음 묶음까지 이어서 밟을 속도 (S15P21B202-658).
            ///
            /// <para><b>왜 강체에 넣지 않고 여기 두나.</b> 흔적 그림은 이제
            /// <b>키네마틱</b>이다(콜라이더를 켜면서 그렇게 바꿨다 — 아니면 참가자가
            /// 돌을 밀어 버린다). 키네마틱 강체는 <c>linearVelocity</c> 를 무시하므로
            /// 예전처럼 속도를 넣어 두면 <b>돌이 아예 안 움직인다</b>.
            /// <see cref="AdvanceScarGhosts"/> 가 이 값으로 직접 밟는다.</para>
            /// </summary>
            public Vector3 Linear;
            public Vector3 Angular;
        }

        /// <summary>
        /// 델타로 얻은 속도의 상한. 이보다 빠르면 <b>보간이 아니라 순간이동</b>이므로
        /// 속도를 주지 않고 자리만 찍는다.
        ///
        /// <para>낡은 자세에서 갓 깨어난 돌, 묶음 유실, 서버 렉이 모두 여기로 온다.
        /// 상한이 없으면 그림이 화면 밖으로 날아가고 다음 묶음까지 돌아오지 않는다.</para>
        /// </summary>
        const float ScarInterpolationSpeedLimit = 40f;

        /// <summary>
        /// 델타를 속도로 볼 수 있는 시간 창. 너무 짧으면 0 으로 나누고, 너무 길면
        /// 그 사이에 무슨 일이 있었는지 알 수 없다(자고 깨어난 것과 구별되지 않는다).
        /// </summary>
        const float ScarInterpolationMinInterval = 0.005f;
        const float ScarInterpolationMaxInterval = 0.5f;

        /// <summary>참가자 화면에 놓인 흔적 그림의 수. 권위 쪽에서는 항상 0 이다.</summary>
        public int ScarGhostCount => _scarGhosts.Count;

        /// <summary>번호로 흔적 그림을 찾는다. 테스트와 진단이 쓴다.</summary>
        public bool TryGetScarGhost(int id, out Meteor ghost) => _scarGhosts.TryGetValue(id, out ghost);

        /// <summary>
        /// 호스트가 보낸 흔적으로 그림을 맞춘다 (S15P21B202-355).
        ///
        /// <para><b>권위 쪽에서 부르면 안 된다</b> — 자기 흔적 위에 그림을 겹쳐 놓는다.
        /// <see cref="Tick"/> 을 돌리는 기계와 이것을 부르는 기계는 서로 배타적이다.</para>
        ///
        /// <para><paramref name="full"/> 이 참이면 <b>이 목록이 전부</b>라는 뜻이라, 목록에
        /// 없는 그림을 지운다. 거짓이면 <b>움직인 것만</b> 온 것이므로 자세만 얹고 아무것도
        /// 지우지 않는다 — 그 묶음은 신뢰 없는 채널로 오고, 하나 잃었다고 돌을 지우면
        /// 화면에서 돌이 깜빡인다.</para>
        /// </summary>
        public void ApplyNetworkScars(IReadOnlyList<NetworkScarState> states, bool full)
        {
            var n = states != null ? states.Count : 0;

            if (full)
            {
                _seenScarIds.Clear();
                for (var i = 0; i < n; i++) _seenScarIds.Add(states[i].Id);

                _staleScarIds.Clear();
                foreach (var pair in _scarGhosts)
                    if (!_seenScarIds.Contains(pair.Key)) _staleScarIds.Add(pair.Key);

                for (var i = 0; i < _staleScarIds.Count; i++)
                {
                    var id = _staleScarIds[i];
                    if (_scarGhosts.TryGetValue(id, out var gone) && gone != null) gone.Despawn();
                    _scarGhosts.Remove(id);
                    _scarMotion.Remove(id);   // 같이 지운다 — 안 지우면 id 가 재사용될 때 낡은 자세로 속도를 잰다
                }
            }

            for (var i = 0; i < n; i++)
            {
                var state = states[i];

                // **움직임 묶음으로는 새로 만들지 않는다.** 그 묶음은 신뢰가 없어서
                // 순서가 어긋나 도착할 수 있고, 전체 목록이 오기 전에 만들면 지울 근거가
                // 없는 그림이 생긴다.
                if (!_scarGhosts.TryGetValue(state.Id, out var ghost))
                {
                    if (!full) continue;
                    // **막히는 돌로 만든다** (S15P21B202-658). 낙하 그림과 다른 유일한 곳이다 —
                    // 참가자가 착지한 돌을 몸으로 통과하던 것이 여기서 갈렸다.
                    ghost = CreateGhost(solid: true);
                    ghost.name = "Meteor(원격 흔적)";
                    _scarGhosts[state.Id] = ghost;
                }

                if (ghost == null) continue;

                // 보내는 쪽도 받는 쪽도 body 로 읽고 쓴다 — 보간이 걸려 있어
                // transform.position 은 표시 위치다(ApplyNetworkMeteors 와 같은 이유).
                //
                // `Meteor.Body` 를 쓴다 (`!321` AI 리뷰) — 전체 목록은 흔적 전부를
                // 훑으므로 여기서 GetComponent 를 부르면 그 개수만큼 쌓인다.
                var body = ghost.Body;
                if (body != null)
                {
                    // **방어선** (S15P21B202-658, `WorldSync.FollowOnClient` 과 같은 이유).
                    // 만들 때 키네마틱으로 세우지만 게임플레이 코드가 나중에 풀 수 있고,
                    // 풀린 채로 두면 참가자가 돌을 밀어 서버 자세와 다투게 된다.
                    if (!body.isKinematic || body.interpolation != RigidbodyInterpolation.Interpolate)
                        PrepareSolidGhostBody(body);

                    body.position = state.Position;
                    body.rotation = state.Rotation;

                    // **움직임 묶음은 속도를 준다** (S15P21B202-434).
                    //
                    // 전체 목록은 권위이므로 그 자리에 세운다(속도 0). 움직임 묶음은
                    // 30Hz 로 오는데 화면은 120fps 라, 속도를 0 으로 두면 33ms 동안
                    // 멈춰 있다가 순간이동해 굴러가는 돌이 뚝뚝 끊긴다.
                    //
                    // 그 속도는 이제 **강체가 아니라 기록에** 넣고
                    // <see cref="AdvanceScarGhosts"/> 가 밟는다 — 키네마틱 강체는
                    // <c>linearVelocity</c> 를 무시하므로 넣어 봐야 돌이 안 움직인다
                    // (S15P21B202-658).
                    if (full) SetScarAtRest(state);
                    else DeadReckonScar(state);
                    continue;
                }

                ghost.transform.SetPositionAndRotation(state.Position, state.Rotation);
                RememberScarMotion(state, Vector3.zero, Vector3.zero);
            }
        }

        /// <summary>전체 목록이 말하는 자리에 세운다. 그 목록이 권위다.</summary>
        void SetScarAtRest(NetworkScarState state)
        {
            RememberScarMotion(state, Vector3.zero, Vector3.zero);
        }

        /// <summary>
        /// 다음 묶음까지 이어서 움직이게 한다 (S15P21B202-434).
        ///
        /// <para>마지막으로 받은 자세와의 차이를 그 사이 시간으로 나눠 속도를 만든다.
        /// 자리는 받은 값으로 찍고(권위), 속도는 <b>그다음 33ms 를 채우는 데</b> 쓴다 —
        /// 다음 묶음이 오면 다시 찍히므로 오차가 누적되지 않는다.</para>
        ///
        /// <para><b>회전도 함께 준다.</b> 굴러가는 돌은 회전이 보이는 것의 절반이다.
        /// 자리만 부드럽고 회전이 계단이면 여전히 끊겨 보인다.</para>
        ///
        /// <para><b>상한을 넘으면 속도를 주지 않는다.</b> 자고 있다 깨어난 돌은 최대
        /// 2초 낡은 자세에서 출발하고, 묶음이 유실되면 간격이 벌어진다. 그때 계산된
        /// 속도는 보간이 아니라 순간이동이므로 자리만 찍고 다음 묶음을 기다린다.</para>
        /// </summary>
        void DeadReckonScar(NetworkScarState state)
        {
            float now = Time.time;

            if (!_scarMotion.TryGetValue(state.Id, out var last))
            {
                // 처음 받는 것 — 비빌 자세가 없다. 세워 두고 다음 묶음부터 잇는다.
                RememberScarMotion(state, Vector3.zero, Vector3.zero);
                return;
            }

            float dt = now - last.Time;
            if (dt < ScarInterpolationMinInterval || dt > ScarInterpolationMaxInterval)
            {
                RememberScarMotion(state, Vector3.zero, Vector3.zero);
                return;
            }

            Vector3 linear = (state.Position - last.Position) / dt;
            if (linear.magnitude > ScarInterpolationSpeedLimit)
            {
                RememberScarMotion(state, Vector3.zero, Vector3.zero);
                return;
            }

            RememberScarMotion(state, linear, AngularFrom(last.Rotation, state.Rotation, dt));
        }

        /// <summary>
        /// 두 회전 사이의 각속도(라디안/초). 쿼터니언 차이를 축·각으로 풀어 쓴다.
        ///
        /// <para><c>ToAngleAxis</c> 는 0~360 도를 주므로 180 도를 넘으면 <b>반대로 돌린
        /// 것</b>이다 — 그대로 쓰면 돌이 긴 쪽으로 휙 돈다. 그리고 축이 유효하지 않은
        /// 경우(각이 0 이거나 수치 오차)에는 0 을 돌려준다. NaN 을 강체에 넣으면 그
        /// 프레임부터 물리가 조용히 죽는다.</para>
        /// </summary>
        static Vector3 AngularFrom(Quaternion from, Quaternion to, float dt)
        {
            (to * Quaternion.Inverse(from)).ToAngleAxis(out float degrees, out Vector3 axis);

            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return Vector3.zero;
            if (axis.sqrMagnitude < 1e-6f) return Vector3.zero;

            if (degrees > 180f) degrees -= 360f;
            return axis.normalized * (degrees * Mathf.Deg2Rad / dt);
        }

        /// <summary>다음 묶음이 비빌 기준과, 그까지 밟을 속도를 남긴다.</summary>
        void RememberScarMotion(NetworkScarState state, Vector3 linear, Vector3 angular)
        {
            _scarMotion[state.Id] = new ScarMotion
            {
                Position = state.Position,
                Rotation = state.Rotation,
                Time = Time.time,
                Linear = linear,
                Angular = angular,
            };
        }

        readonly HashSet<int> _seenScarIds = new();
        readonly List<int> _staleScarIds = new();

        /// <summary>
        /// 흔적 그림을 모두 지운다. 접속이 끝나거나 이 기계가 호스트를 이어받으면
        /// 그림 대신 진짜 흔적을 쓴다.
        /// </summary>
        public void ClearNetworkScars()
        {
            foreach (var pair in _scarGhosts)
                if (pair.Value != null) pair.Value.Despawn();

            _scarGhosts.Clear();
            _scarMotion.Clear();
        }

        /// <summary>
        /// <b>판정에 관여하지 않는</b> 그림 하나. <see cref="_live"/> 에는 넣지 않는다 —
        /// 그 목록은 권위 쪽의 것이고 <see cref="AdvanceLive"/> 가 틱한다.
        ///
        /// <para>판정을 막는 것은 <c>ghost.enabled = false</c> 한 줄이다. 그래서
        /// <paramref name="solid"/> 로 <b>막히는 돌</b>을 만들어도 착탄·피해를 이 기계가
        /// 스스로 판단하는 일은 없다(S15P21B202-658).</para>
        ///
        /// <para>낙하 꼬리는 그대로 나온다. <c>Meteor.Awake</c> 가 <c>EnsureTrail</c> 로
        /// 만들고 방출을 끄는 곳은 착탄 처리(<c>StopTrail</c>)뿐이라, 컴포넌트를 꺼도 이미
        /// 만들어진 <c>TrailRenderer</c> 는 계속 그린다 — 하늘에서 뭔가 떨어지는 것이
        /// 보이는 데 이 꼬리가 큰 몫을 한다.</para>
        /// </summary>
        /// <param name="solid">
        /// 참가자의 몸을 <b>막는</b> 돌인가 (S15P21B202-658).
        ///
        /// <para><b>왜 갈랐나.</b> 예전에는 "판정에 관여하지 않는다" 를 <b>콜라이더까지
        /// 끄는 것</b>으로 구현했다. 그런데 판정을 끄는 데 필요한 것은 <see cref="Meteor"/>
        /// 컴포넌트를 끄는 것 하나다 — 콜라이더는 <b>막는</b> 일만 하고 아무것도 판정하지
        /// 않는다. 그래서 참가자는 착지한 돌을 <b>몸으로 통과</b>했고, 피해는 호스트가
        /// 주므로 <b>안 부딪혔는데 체력만 깎였다</b>. 기획 확인 결과 의도가 아니었다
        /// (`-654` Q7).</para>
        ///
        /// <para><b>착지한 흔적에만 켠다.</b> 흔적은 멈춰 있어 참가자 화면의 자리가
        /// 서버와 같다. 낙하 중인 돌은 ~166ms(≈1.6m) 낡은 자리에 있어서, 막기 시작하면
        /// "호스트에는 안 맞았는데 참가자만 막히는" 새 어긋남이 생긴다 — 그쪽은 실제
        /// 플레이로 확인한 뒤 정한다.</para>
        /// </param>
        Meteor CreateGhost(bool solid = false)
        {
            EnsureConfiguration();

            var ghost = _meteorPrefab != null
                ? Instantiate(_meteorPrefab)
                : CreateFallbackMeteor(Vector3.zero);

            ghost.ApplyTuning(_tuning);
            ghost.name = "Meteor(원격 그림)";

            // 끄는 것이 핵심이다. 꺼진 컴포넌트에는 충돌 통보도 틱도 오지 않는다.
            // **이 한 줄이 판정을 막는다** — 아래에서 콜라이더를 켜도 그대로다.
            ghost.enabled = false;

            var body = ghost.GetComponent<Rigidbody>();

            if (!solid)
            {
                foreach (var collider in ghost.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

                if (body != null)
                {
                    body.detectCollisions = false;
                    body.isKinematic = false;
                    body.useGravity = true;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                }

                return ghost;
            }

            // **레이어를 옮긴다** (S15P21B202-658). 호스트에서는 <c>Meteor.Settle</c> 이
            // 착지한 돌을 <see cref="GameLayers.Prop"/> 으로 옮기는데(`Meteor.cs:538`),
            // 그림은 <see cref="Meteor"/> 컴포넌트가 꺼져 있어 <b>그 줄을 절대 지나지
            // 않는다</b>. 콜라이더만 켜고 레이어를 두면 방망이 타격 범위
            // (<see cref="GameLayers.BatStrikeMask"/> 는 Meteor 를 빼고 Prop 은 넣는다)와
            // 상호작용이 호스트와 다르게 잡힌다.
            SetLayerDeep(ghost.gameObject, GameLayers.Prop);

            if (body != null) PrepareSolidGhostBody(body);
            return ghost;
        }

        /// <summary>
        /// 막히는 그림의 강체를 "받은 자세만 따라가는" 상태로 세운다 (S15P21B202-658).
        ///
        /// <para><see cref="Network"/> 쪽 <c>WorldSync.PrepareClientBody</c> 와 같은 모양이다 —
        /// 참가자 화면의 부품이 이미 이렇게 서 있고, 그래서 참가자 몸이 부품에는 막힌다.
        /// 운석만 다른 방식이었던 것이 `-654` Q7 의 원인이다.</para>
        ///
        /// <para><b>키네마틱이어야 한다.</b> 콜라이더를 켜면서 동적 강체로 두면 참가자가
        /// 돌을 <b>밀어 버리고</b>, 다음 묶음이 자리를 되돌려 덜덜 떨린다. 키네마틱 강체는
        /// 남을 밀되 남에게 밀리지 않는다 — 호스트의 돌과 같은 성질이다.</para>
        ///
        /// <para><b>중력은 끈다.</b> 키네마틱에는 애초에 안 걸리지만, 게임플레이 코드가
        /// 강체를 되돌렸을 때 그림이 지면을 뚫고 내려가지 않게 함께 꺼 둔다.</para>
        /// </summary>
        static void PrepareSolidGhostBody(Rigidbody body)
        {
            body.detectCollisions = true;
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>깊이 우선으로 레이어를 바꾼다. <c>Meteor.SetLayerDeep</c> 과 같다.</summary>
        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerDeep(go.transform.GetChild(i).gameObject, layer);
        }

        /// <summary>
        /// 흔적 그림을 다음 묶음까지 이어서 밟는다 (S15P21B202-658).
        ///
        /// <para><b>왜 필요해졌나.</b> 예전에는 강체에 속도를 넣어 두면 물리가 적분해
        /// 주었다. 콜라이더를 켜면서 키네마틱으로 바꿨는데, 키네마틱 강체는
        /// <c>linearVelocity</c> 를 <b>무시한다</b> — 그대로 두면 굴러가는 돌이 묶음과
        /// 묶음 사이에 멈춰 `-434` 가 고친 "뚝뚝 끊김" 이 그대로 돌아온다.</para>
        ///
        /// <para><b>참가자에서만 부른다.</b> 호스트에는 그림이 없다
        /// (<see cref="_scarGhosts"/> 가 비어 있다). <c>MeteorClockBridge</c> 의 참가자
        /// 갈래가 물리 스텝마다 부른다 — <c>AgeNetworkPending</c> 과 같은 자리다.</para>
        ///
        /// <para><b><c>MovePosition</c> 을 쓴다.</b> 키네마틱 강체에 자리를 직접 대입하면
        /// 순간이동으로 취급되어 그 스텝의 보간이 건너뛰어진다 — 보간을 켠 의미가 없어진다
        /// (<c>WorldSync.FollowOnClient</c> 의 같은 주석).</para>
        /// </summary>
        public void AdvanceScarGhosts(float deltaTime)
        {
            if (deltaTime <= 0f || _scarGhosts.Count == 0) return;

            foreach (var pair in _scarGhosts)
            {
                var ghost = pair.Value;
                if (ghost == null) continue;

                var body = ghost.Body;
                if (body == null) continue;

                if (!_scarMotion.TryGetValue(pair.Key, out var motion)) continue;
                if (motion.Linear == Vector3.zero && motion.Angular == Vector3.zero) continue;

                body.MovePosition(body.position + motion.Linear * deltaTime);

                float radians = motion.Angular.magnitude * deltaTime;
                if (radians > 1e-6f)
                    body.MoveRotation(Quaternion.AngleAxis(radians * Mathf.Rad2Deg,
                                                           motion.Angular.normalized) * body.rotation);
            }
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
                Id = ++_nextScarId,
                Meteor = meteor,
                Point = meteor.ImpactPoint,
                Radius = meteor.ImpactRadius,
                Age = 0f,
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

                Debug.Log("[NotThisPlanet] 운석 흔적 상한("
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

                // **나이는 언제나 센다.** 예전에는 다 식으면 건너뛰었는데(§19 의
                // 성능 기준), 그러면 아래 수명 판정이 영영 오지 않는다.
                // 비용은 float 덧셈 하나뿐이다.
                scar.Age += deltaTime;

                // **흔적을 여기서 없앤다.**
                //
                // 예전에는 Meteor.Tick 의 _sinceSettled 가 수명을 셌다. 그런데
                // AdvanceLive 는 정착한 운석을 _live 에서 빼면서 Remember 로 옮기고
                // **더 틱하지 않는다** — Meteor 에는 자체 FixedUpdate 도 없다.
                // 그래서 _sinceSettled 가 늘지 않아 Expire() 가 영영 불리지 않았고,
                // **흔적이 영구히 남았다.** MeteorScarDespawnSeconds 는 죽은 상수였다
                // (S15P21B202-438 에서 실측으로 드러났다 — 실제 플레이 로그에 흔적
                // 36개가 살아 있던 기록이 있다).
                //
                // 나이를 세는 쪽이 수명도 재는 것이 맞다. 두 타이머가 갈라져 있던
                // 것이 이 결함의 뿌리다.
                if (scar.Age >= _tuning.ScarDespawnSeconds)
                {
                    scar.Meteor.Despawn();   // 위에서 null 을 이미 걸렀다
                    _scars.RemoveAt(i);
                    continue;
                }
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
            if (UsesIndependentDrops)
            {
                // 탐색은 맵 전체, 공세는 기존 플레이어 주변 비율(기본 30%)을 유지한다.
                // 연속 위치 필터로 플레이어 주변 후보가 맵 반대편으로 바뀌지 않게 한다.
                return OnGround(AssaultEnabled && _random.NextDouble() < NearPlayerWeight
                    ? NearPlayerPoint() : EnvironmentPoint());
            }
            // <b>시험용일 때만 종류를 유지한다</b>(<see cref="TestTargeting"/>).
            //
            // 거리 검사는 <b>가까운 목표만 골라 탈락시킨다</b> — 멀리 있는 환경 목표는 직전
            // 자리와 늘 멀어 언제나 통과하고, 사람 주변 1~3m 에서 뽑은 자리는 서로 가까워
            // 자주 걸린다. 걸릴 때마다 종류까지 다시 뽑으면 목표가 환경 쪽으로 밀려,
            // 비율을 90% 로 올려도 실제로는 68% 만 근처에 떨어졌다(2026-09-17 실측, 60발).
            //
            // 그래서 시험용에서는 종류를 유지한 채 자리만 다시 뽑고, 네 번 다 걸리면 그중
            // 가장 먼 자리를 쓴다. 사람 둘레 1~3m 고리에서는 두 자리를 아무리 벌려도 최대
            // 6m, 둘 다 안쪽이면 2m 라 이 거리를 못 지킬 때가 있기 때문이다.
            //
            // <b>실제 판은 예전 그대로다.</b> 종류를 매번 다시 뽑고, 네 번 다 걸리면 환경으로
            // 돌아간다 — 난수를 뽑는 횟수와 순서까지 같다.
            var keepKind = TestTargeting;
            var kind = TargetKindFor(_random.NextDouble());
            var best = Vector3.zero;
            var bestGap = -1f;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0 && !keepKind) kind = TargetKindFor(_random.NextDouble());

                var point = PointFor(kind);
                var gap = _hasLastTarget ? Vector3.Distance(point, _lastTarget) : float.MaxValue;

                // 같은 자리를 연속으로 고르지 않는다(§17.5).
                if (gap >= _targetRepeatDistance)
                {
                    Remember(point);
                    return point;
                }

                if (gap > bestGap) { bestGap = gap; best = point; }
            }

            var fallback = keepKind ? best : OnGround(EnvironmentPoint());
            Remember(fallback);
            return fallback;
        }

        /// <summary>
        /// 낙하 예고를 <b>그리는 컴포넌트를 붙인다</b>(S15P21B202-275).
        ///
        /// <b>아무도 이것을 만들지 않았다.</b> S15P21B202-241 이
        /// <see cref="MeteorWarningView"/> 를 만들고 268 이 줄어드는 원으로 바꿨지만,
        /// 씬에도 프리팹에도 붙어 있지 않았고 런타임에 붙이는 코드도 없었다 —
        /// <b>그 이슈들이 만든 표현이 인게임에서 한 번도 실행된 적이 없다.</b>
        /// "운석 워닝포인트가 안보인다" 의 진짜 원인이 이것이다.
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

            // <b>흔적 원반은 더 이상 그리지 않는다</b>(S15P21B202-653). 색으로 흉내 낸
            // 그을음이 회색 반투명 원반으로 보였다 — 이유는 그 이슈에 적었다. 기록
            // 자체(<see cref="Scar"/>)는 남는다: 그것이 운석 소멸 타이머다.
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

            if (UsesIndependentDrops)
                radius = Mathf.Sqrt((float)_random.NextDouble()) * AssaultPlayerTargetRadius;

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
        /// <summary>
        /// 노릴 사람 하나(§17.5).
        ///
        /// <para><b>후보가 직전에 노린 그 사람뿐이면 연속 금지를 푼다.</b> §17.5 의 "동일한
        /// 플레이어를 연속 선택하지 않는다" 는 여럿일 때 <b>번갈아 노리라</b>는 뜻이다.
        /// 혼자 하는 판에서 그대로 적용하면 후보가 0 이 되어 환경으로 밀려나고, 비율을
        /// 아무리 올려도 <b>두 번째부터는 근처에 떨어지지 않는다</b> — 한 번 걸러 한 번만
        /// 근처가 되므로 30% 를 80% 로 올려도 실제로는 절반에서 멈춘다.</para>
        ///
        /// <para><b>시험용일 때만 푼다</b>(<see cref="TestTargeting"/>). 실제 판에서는 예전
        /// 그대로 후보가 없으면 <c>null</c> 을 돌려 환경 목표로 간다.</para>
        ///
        /// <para>첫 시도에서 후보를 세기만 하고 난수를 쓰지 않으므로, 다시 고르는 경우에도
        /// 난수는 정확히 한 번만 소비된다 — 시드 재현성이 깨지지 않는다.</para>
        /// </summary>
        PlayerRig PickPlayer()
        {
            var target = PickPlayer(false);
            if (target != null || (!TestTargeting && !UsesIndependentDrops)) return target;

            return PickPlayer(true);
        }

        PlayerRig PickPlayer(bool allowRepeat)
        {
            var candidates = 0;
            for (var i = 0; i < PlayerRig.All.Count; i++)
                if (IsTargetable(PlayerRig.All[i], allowRepeat)) candidates++;

            if (candidates == 0) return null;

            var index = (int)(_random.NextDouble() * candidates);
            if (index >= candidates) index = candidates - 1;

            for (var i = 0; i < PlayerRig.All.Count; i++)
            {
                if (!IsTargetable(PlayerRig.All[i], allowRepeat)) continue;
                if (index-- == 0) return PlayerRig.All[i];
            }

            return null;
        }

        bool IsTargetable(PlayerRig player, bool allowRepeat) =>
            player != null && !player.Downed
            && (allowRepeat || player != _lastTargetPlayer);

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
            var entry = EntryOffsetFor(impactPoint, ScaledImpactRadiusFor(size));

            var position = impactPoint + entry + Vector3.up * _dropHeight;
            var meteor = _meteorPrefab != null
                ? Instantiate(_meteorPrefab, position, Quaternion.identity)
                : CreateFallbackMeteor(position);

            meteor.ApplyTuning(_tuning);
            meteor.Initialize();
            meteor.SetSize(size);

            // 시간대·프리셋의 반경 배율. 예고 원과 같은 값이어야 한다(§17.2).
            meteor.SetImpactScale(ImpactRadiusScale);

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
