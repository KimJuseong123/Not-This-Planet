using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>공세 목표를 고르는 후보(기획 §17.5).</summary>
    public enum MeteorTargetKind
    {
        /// <summary>플레이어에게서 떨어진 환경 지역. 35%.</summary>
        Environment = 0,

        /// <summary>무작위 플레이어 주변 1~3m. 30%.</summary>
        NearPlayer = 1,

        /// <summary>고정못 제거·용접 중인 작업 지역 주변. 25%.</summary>
        WorkArea = 2,

        /// <summary>중앙 발사 구역 주변. 10%.</summary>
        LaunchPad = 3,
    }

    /// <summary>운석 크기(기획 §17.7).</summary>
    public enum MeteorSize
    {
        Small = 0,
        Medium = 1,
    }

    /// <summary>
    /// 기획 §17.4~17.7 의 표를 코드로 옮긴 것(S15P21B202-234).
    ///
    /// <b>상수로 흩어 두지 않고 표로 둔다.</b> 다섯 시간대 × 세 값(간격·개수·중형 비율)이
    /// 서로 맞물려 있어서 하나만 고치면 난이도 곡선이 어긋난다. 표 하나를 보고 고치는
    /// 편이 안전하다 — <see cref="Parts.PartSpec"/> 이 파츠 치수를 표로 두는 것과 같은
    /// 이유다.
    ///
    /// 여기 있는 값은 기획서 용어로 <b>초기 테스트값</b>이며 플레이 테스트로 조정한다.
    /// </summary>
    public static class MeteorWaveTable
    {
        /// <summary><c>FromMinutes</c> 이후부터 적용되는 시간대 한 칸.</summary>
        public readonly struct Row
        {
            public Row(float fromMinutes, float waveIntervalSeconds,
                       int minMeteors, int maxMeteors, float mediumChance)
            {
                FromMinutes = fromMinutes;
                WaveIntervalSeconds = waveIntervalSeconds;
                MinMeteors = minMeteors;
                MaxMeteors = maxMeteors;
                MediumChance = mediumChance;
            }

            public float FromMinutes { get; }
            public float WaveIntervalSeconds { get; }
            public int MinMeteors { get; }
            public int MaxMeteors { get; }

            /// <summary>이 시간대에서 중형이 나올 확률 0~1.</summary>
            public float MediumChance { get; }
        }

        /// <summary>
        /// 기획 §17.4 의 시간대별 공세 초기값과 §17.7 의 중형 비율.
        ///
        /// <code>
        /// 경과      간격   공세당   중형
        /// 0~5분     30초   1~2      0%
        /// 5~10분    25초   2~3      0%
        /// 10~15분   20초   3~4      15%
        /// 15~20분   15초   4~5      25%
        /// 20분~     15초   5        30%
        /// </code>
        ///
        /// 중형은 10분 이후에만 나온다(§17.7 "중형 · 등장 10분 이후").
        /// </summary>
        public static readonly Row[] Rows =
        {
            new(0f, 30f, 1, 2, 0f),
            new(5f, 25f, 2, 3, 0f),
            new(10f, 20f, 3, 4, 0.15f),
            new(15f, 15f, 4, 5, 0.25f),
            new(20f, 15f, 5, 5, 0.30f),
        };

        /// <summary>
        /// 기획 §17.6 의 인원수 보정. 인덱스가 접속 인원(1~4)이다.
        ///
        /// <b>운석 수만 조정한다.</b> §17.6 이 "공세 간격과 시간대별 상승 속도는 유지하고
        /// 운석 수만 조정한다" 고 못박는다 — 간격까지 늘리면 혼자 할 때 난이도가 아니라
        /// <b>플레이 시간</b>이 달라진다.
        /// </summary>
        static readonly float[] CrewScale = { 1f, 0.45f, 0.65f, 0.80f, 1f };

        /// <summary>
        /// 기획 §17.5 의 목표 위치 비율. 합이 1 이고 순서는
        /// <see cref="MeteorTargetKind"/> 와 같아야 한다.
        /// </summary>
        public static readonly float[] TargetWeights = { 0.35f, 0.30f, 0.25f, 0.10f };

        /// <summary><paramref name="elapsedSeconds"/> 에 적용되는 시간대.</summary>
        public static Row RowAt(float elapsedSeconds)
        {
            var minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
            var row = Rows[0];

            for (var i = 0; i < Rows.Length; i++)
                if (minutes >= Rows[i].FromMinutes)
                    row = Rows[i];

            return row;
        }

        /// <summary>공세 사이의 간격(초).</summary>
        public static float WaveIntervalAt(float elapsedSeconds) =>
            RowAt(elapsedSeconds).WaveIntervalSeconds;

        /// <summary>
        /// 이번 공세의 운석 수. 인원수 보정을 적용한다(§17.6).
        ///
        /// 보정 뒤에도 <b>최소 한 개</b>는 나온다. 0 이 되면 §17.3 의 5초 전 신호만 울리고
        /// 아무것도 떨어지지 않아, 플레이어가 신호를 믿지 않게 된다.
        /// </summary>
        public static int WaveSizeAt(float elapsedSeconds, int crew, double roll)
        {
            var row = RowAt(elapsedSeconds);
            var span = row.MaxMeteors - row.MinMeteors;
            var raw = row.MinMeteors + (int)(Mathf.Clamp01((float)roll) * (span + 1));
            raw = Mathf.Clamp(raw, row.MinMeteors, row.MaxMeteors);

            return Mathf.Max(1, Mathf.RoundToInt(raw * ScaleForCrew(crew)));
        }

        /// <summary>중형이 나올 확률 0~1. 10분 전에는 0 이다(§17.7).</summary>
        public static float MediumChanceAt(float elapsedSeconds) =>
            RowAt(elapsedSeconds).MediumChance;

        public static float ScaleForCrew(int crew) =>
            CrewScale[Mathf.Clamp(crew, 1, CrewScale.Length - 1)];

        /// <summary><paramref name="roll"/>(0~1)이 가리키는 목표 후보(§17.5).</summary>
        public static MeteorTargetKind TargetKindFor(double roll)
        {
            var value = Mathf.Clamp01((float)roll);
            var sum = 0f;

            for (var i = 0; i < TargetWeights.Length; i++)
            {
                sum += TargetWeights[i];
                if (value < sum) return (MeteorTargetKind)i;
            }

            return MeteorTargetKind.LaunchPad;
        }
    }
}
