using System;
using Metagame.Gameplay.Hazards;
using UnityEngine;

namespace Metagame.Gameplay
{
    public enum Stage1SequencePhase : byte { Waiting, Entry, Exploration, Assault, Combat }

    /// <summary>입장과 공세의 권위 상태. 연출 씬은 이 시계를 읽고 실제 월드 위에 표시한다.</summary>
    public sealed class Stage1Sequence : MonoBehaviour
    {
        public const float EntrySeconds = 18f;
        public const float AssaultSeconds = 8f;
        public const float WarningSeconds = 5f;
        public const float EntryWaitTimeoutSeconds = 10f;
        public Stage1SequencePhase Phase { get; private set; }
        public double StartedAt { get; private set; }
        public int Revision { get; private set; }
        public bool AssaultEnabled { get; private set; }
        public bool IsCinematic => Phase == Stage1SequencePhase.Entry || Phase == Stage1SequencePhase.Assault;
        public bool BlocksInput => Phase == Stage1SequencePhase.Waiting || IsCinematic;
        public bool Authoritative => _authority != null && _authority();
        public float Age => _clock == null ? 0f : Mathf.Max(0f, (float)(_clock() - StartedAt));
        public float LookUpWeight
        {
            get
            {
                if (!IsCinematic) return 0f;
                float start = Phase == Stage1SequencePhase.Entry ? 3f : 1f;
                float end = Phase == Stage1SequencePhase.Entry ? 7f : 4f;
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Age - start))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Age - end)));
            }
        }

        GameSession _session;
        MeteorSpawner _spawner;
        Func<bool> _authority;
        Func<double> _clock;
        double _waitingSince;

        public void Configure(GameSession session, MeteorSpawner spawner, Func<bool> authority, Func<double> clock)
        {
            _session = session;
            _spawner = spawner;
            _authority = authority;
            _clock = clock;
            _waitingSince = clock();
            session.Sequence = this;
            if (spawner != null) spawner.SetSequenceHold(true);
        }

        public bool TryBeginEntry()
        {
            if (!Authoritative || IsCinematic || _session == null || _session.EndingActive) return false;
            Change(Stage1SequencePhase.Entry);
            return true;
        }

        public bool TryBeginAssault(bool replay = false)
        {
            if (!Authoritative || (BlocksInput && !(replay && Phase == Stage1SequencePhase.Waiting)) || _session == null || _session.EndingActive
                || (AssaultEnabled && !replay)) return false;
            Change(Stage1SequencePhase.Assault);
            return true;
        }

        void Change(Stage1SequencePhase phase)
        {
            Phase = phase;
            StartedAt = _clock();
            Revision++;
            ApplyGate();
        }

        public void Tick()
        {
            if (!Authoritative || _session == null || _session.EndingActive) return;
            // 준비되지 않은 연결 하나가 전체 세션을 영구 잠그지 못하게 한다.
            // 네트워크/일시정지와 무관한 시계를 쓰고, 서버만 전환을 결정한다.
            if (Phase == Stage1SequencePhase.Waiting && _clock() - _waitingSince >= EntryWaitTimeoutSeconds)
                TryBeginEntry();
            if (AssaultEnabled && _spawner != null && !_spawner.AssaultEnabled) _spawner.BeginAssault();
            if (Phase == Stage1SequencePhase.Entry && Age >= EntrySeconds)
                Change(AssaultEnabled ? Stage1SequencePhase.Combat : Stage1SequencePhase.Exploration);
            if (Phase != Stage1SequencePhase.Assault) return;
            if (Age >= WarningSeconds && !AssaultEnabled)
            {
                AssaultEnabled = true;
                _spawner?.BeginAssault();
                ApplyGate();
            }
            if (Age >= AssaultSeconds) Change(Stage1SequencePhase.Combat);
        }

        public void ApplyReplica(Stage1SequencePhase phase, double startedAt, int revision, bool assault)
        {
            if (Authoritative || revision < Revision) return;
            Phase = phase;
            StartedAt = startedAt;
            Revision = revision;
            AssaultEnabled = assault;
            // 난이도 상태도 보존해야 호스트를 이어받았을 때 저속으로 되돌아가지 않는다.
            if (assault && _spawner != null && !_spawner.AssaultEnabled) _spawner.BeginAssault();
            ApplyGate();
        }

        /// <summary>
        /// 스포너를 멈춰 둘 구간을 정한다.
        ///
        /// <para><b>공세 전에도 조금은 내린다</b>(2026-09-18). 예전 조건은
        /// <c>!AssaultEnabled</c> 를 포함해서, 비행선까지 걸어가는 내내 <b>운석이 한 발도
        /// 떨어지지 않았다.</b> 그러면 운석이 이 게임의 위협이라는 것을 공세 컷신에서
        /// 처음 배우게 된다 — 배우는 자리가 곧 위험해지는 자리면 늦다. 탐색 구간에는
        /// <see cref="Hazards.MeteorIntensity.Drizzle"/>(10초에 한 발)이 흐른다.</para>
        ///
        /// <para>막는 것은 <b>조작을 못 하는 동안</b>뿐이다 — 아직 시작하지 않았거나
        /// (<see cref="Stage1SequencePhase.Waiting"/>) 입장 연출이 도는 중
        /// (<see cref="Stage1SequencePhase.Entry"/>). 그때 떨어뜨리면 피할 수 없는
        /// 운석에 맞는다.</para>
        ///
        /// <para><b>공세 컷신(<see cref="Stage1SequencePhase.Assault"/>)은 막지 않는다.</b>
        /// 카메라가 하늘을 올려다보는 연출이라 그 사이 운석이 보이는 편이 낫고,
        /// 5초차에 <see cref="AssaultEnabled"/> 가 켜지면 그대로 본 공세로 이어진다.</para>
        /// </summary>
        void ApplyGate()
        {
            if (_spawner != null)
                _spawner.SetSequenceHold(Phase == Stage1SequencePhase.Waiting
                    || Phase == Stage1SequencePhase.Entry);
            if (BlocksInput && _session != null && _session.LocalPlayer != null)
                _session.LocalPlayer.SetInput(new Metagame.Core.InputFrame());
        }

        void Update() => Tick();

        void OnDestroy()
        {
            if (_spawner != null) _spawner.SetSequenceHold(false);
            if (_session != null && _session.Sequence == this) _session.Sequence = null;
        }
    }
}
