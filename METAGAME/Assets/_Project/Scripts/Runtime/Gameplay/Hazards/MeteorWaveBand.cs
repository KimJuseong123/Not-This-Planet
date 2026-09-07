using System;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>메테오 공세 시간대 하나의 Inspector 조정값.</summary>
    [Serializable]
    public sealed class MeteorWaveBand
    {
        [SerializeField, Min(0f)] float _fromMinutes;
        [SerializeField, Min(0f)] float _waveIntervalSeconds;
        [SerializeField, Min(0)] int _minMeteors;
        [SerializeField, Min(0)] int _maxMeteors;
        [SerializeField, Range(0f, 1f)] float _mediumChance;

        public MeteorWaveBand(float fromMinutes, float waveIntervalSeconds,
                             int minMeteors, int maxMeteors, float mediumChance)
        {
            _fromMinutes = fromMinutes;
            _waveIntervalSeconds = waveIntervalSeconds;
            _minMeteors = minMeteors;
            _maxMeteors = maxMeteors;
            _mediumChance = mediumChance;
        }

        public float FromMinutes => _fromMinutes;
        public float WaveIntervalSeconds => _waveIntervalSeconds;
        public int MinMeteors => _minMeteors;
        public int MaxMeteors => Mathf.Max(_minMeteors, _maxMeteors);
        public float MediumChance => _mediumChance;

        public static MeteorWaveBand[] CreateDefaults()
        {
            var rows = MeteorWaveTable.Rows;
            var defaults = new MeteorWaveBand[rows.Length];

            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                defaults[i] = new MeteorWaveBand(row.FromMinutes, row.WaveIntervalSeconds,
                                                 row.MinMeteors, row.MaxMeteors,
                                                 row.MediumChance);
            }

            return defaults;
        }
    }
}
