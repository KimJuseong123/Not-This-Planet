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

        public void Configure(GameSession session, MeteorSpawner spawner, Func<bool> authority, Func<double> clock)
        {
            _session = session;
            _spawner = spawner;
            _authority = authority;
            _clock = clock;
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

        void ApplyGate()
        {
            if (_spawner != null)
                _spawner.SetSequenceHold(!AssaultEnabled || Phase == Stage1SequencePhase.Waiting
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
