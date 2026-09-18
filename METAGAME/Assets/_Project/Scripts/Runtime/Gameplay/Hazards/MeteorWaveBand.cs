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

        [SerializeField, Min(0f)]
        [Tooltip("이 시간대의 충격파 반경 배율. 0 이면 1 로 읽는다 — 이 칸이 생기기 전에 저장된 씬 때문이다.")]
        float _impactRadiusScale;

        public MeteorWaveBand(float fromMinutes, float waveIntervalSeconds,
                             int minMeteors, int maxMeteors, float mediumChance,
                             float impactRadiusScale = 1f)
        {
            _fromMinutes = fromMinutes;
            _waveIntervalSeconds = waveIntervalSeconds;
            _minMeteors = minMeteors;
            _maxMeteors = maxMeteors;
            _mediumChance = mediumChance;
            _impactRadiusScale = impactRadiusScale;
        }

        public float FromMinutes => _fromMinutes;
        public float WaveIntervalSeconds => _waveIntervalSeconds;
        public int MinMeteors => _minMeteors;
        public int MaxMeteors => Mathf.Max(_minMeteors, _maxMeteors);
        public float MediumChance => _mediumChance;

        /// <summary>
        /// 이 시간대의 충격파 반경 배율(기획 §17.7 의 크기별 반경에 곱한다).
        ///
        /// <para><b>0 을 1 로 읽는다.</b> 이 칸은 나중에 생겼고, 이미 저장된 씬
        /// (<c>Stage1_Astra</c>·<c>CheatArena</c>)의 시간대에는 값이 없어 0 으로 역직렬화된다.
        /// 그대로 쓰면 충격파 반경이 0 이 되어 <b>운석이 아무에게도 닿지 않는다</b> —
        /// 조용히 망가지는 종류라 여기서 막는다. 그래서 기존 씬은 이 칸을 채우기 전까지
        /// 예전과 똑같이 ×1 로 돈다.</para>
        /// </summary>
        public float ImpactRadiusScale => _impactRadiusScale > 0f ? _impactRadiusScale : 1f;

        public static MeteorWaveBand[] CreateDefaults()
        {
            var rows = MeteorWaveTable.Rows;
            var defaults = new MeteorWaveBand[rows.Length];

            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                defaults[i] = new MeteorWaveBand(row.FromMinutes, row.WaveIntervalSeconds,
                                                 row.MinMeteors, row.MaxMeteors,
                                                 row.MediumChance, row.ImpactRadiusScale);
            }

            return defaults;
        }
    }
}
