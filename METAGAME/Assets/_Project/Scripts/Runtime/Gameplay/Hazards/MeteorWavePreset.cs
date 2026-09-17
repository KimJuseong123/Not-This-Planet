using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 공세 강도 한 단계. 시험장에서 <b>고르는</b> 난이도다.
    ///
    /// <para><b>왜 시간대 표와 따로 두는가.</b> <see cref="MeteorWaveTable"/> 은 판이
    /// 시작하고 <b>몇 분이 지났는지</b>로 강도를 정한다. 그것을 그대로 쓰면 후반 공세를
    /// 한 번 보는 데 20분을 기다려야 한다. 프리셋은 그 시계를 무시하고 강도 하나를
    /// 고정해, 같은 조건을 몇 번이고 반복해 재게 한다.</para>
    ///
    /// <para><b>간격이 실제 표보다 훨씬 짧다.</b> 표의 30초는 파츠를 찾아 고정못을 풀고
    /// 운반하는 <b>전체 판</b>의 리듬이다. 여기서 재려는 것은 "공세를 피하며 중앙까지 갈 수
    /// 있는가" 하나뿐이라, 걸어가는 동안 공세가 여러 번 와야 시험이 된다.</para>
    ///
    /// <para><b>인원 보정을 적용하지 않는다.</b> 적용하면 혼자 고른 "후반"이 4~5개가 아니라
    /// 2개가 되어 이름과 실제가 어긋난다. 프리셋의 개수는 <b>본 그대로</b> 나온다 —
    /// <see cref="MeteorSpawner.ApplyPreset"/> 이 인원 보정을 4인으로 고정하는 이유다.</para>
    /// </summary>
    public sealed class MeteorWavePreset
    {
        public MeteorWavePreset(string name, string summary, float waveIntervalSeconds,
                                int minMeteors, int maxMeteors, float mediumChance,
                                float impactRadiusScale, float nearPlayerWeight)
        {
            Name = name;
            Summary = summary;
            WaveIntervalSeconds = waveIntervalSeconds;
            MinMeteors = minMeteors;
            MaxMeteors = maxMeteors;
            MediumChance = mediumChance;
            ImpactRadiusScale = impactRadiusScale;
            NearPlayerWeight = nearPlayerWeight;
        }

        /// <summary>패널 버튼에 쓰는 짧은 이름.</summary>
        public string Name { get; }

        /// <summary>무엇을 재려는 단계인지 한 줄.</summary>
        public string Summary { get; }

        public float WaveIntervalSeconds { get; }
        public int MinMeteors { get; }
        public int MaxMeteors { get; }
        public float MediumChance { get; }

        /// <summary>크기별 충격파 반경(§17.7 소형 2m · 중형 3.5m)에 곱하는 값.</summary>
        public float ImpactRadiusScale { get; }

        /// <summary>
        /// 목표 위치 중 <b>플레이어 주변</b>의 비율 0~1(§17.5 는 30%).
        ///
        /// 나머지 비율은 환경·작업 구역·발사 구역이 원래 비율대로 나눠 가진다.
        /// </summary>
        public float NearPlayerWeight { get; }

        /// <summary>
        /// 시험장에서 고를 수 있는 단계.
        ///
        /// <b>순서가 곧 세기다.</b> 패널이 이 순서로 버튼을 늘어놓으므로 뒤로 갈수록
        /// 세져야 한다.
        /// </summary>
        public static readonly MeteorWavePreset[] All =
        {
            new("연습", "한 발씩 확인. 예고 원과 넉백을 눈으로 익힌다.",
                waveIntervalSeconds: 14f, minMeteors: 1, maxMeteors: 1,
                mediumChance: 0f, impactRadiusScale: 1f, nearPlayerWeight: 0.60f),

            new("초반", "기획 0~5분 밀도. 걸어가며 충분히 피해진다.",
                waveIntervalSeconds: 12f, minMeteors: 1, maxMeteors: 2,
                mediumChance: 0f, impactRadiusScale: 1f, nearPlayerWeight: 0.65f),

            new("중반", "기획 10~15분 밀도. 중형이 섞이고 반경이 넓어지기 시작한다.",
                waveIntervalSeconds: 9f, minMeteors: 3, maxMeteors: 4,
                mediumChance: 0.15f, impactRadiusScale: 1.2f, nearPlayerWeight: 0.70f),

            new("후반", "기획 20분 이후 밀도. 혼자 피하기가 빠듯한 지점을 찾는 단계.",
                waveIntervalSeconds: 7f, minMeteors: 4, maxMeteors: 5,
                mediumChance: 0.30f, impactRadiusScale: 1.45f, nearPlayerWeight: 0.80f),

            new("과부하", "일부러 넘긴 값. 혼자서는 못 지나가는 경계를 확인한다.",
                waveIntervalSeconds: 5f, minMeteors: 6, maxMeteors: 7,
                mediumChance: 0.50f, impactRadiusScale: 1.8f, nearPlayerWeight: 0.90f),
        };

        /// <summary>이 강도를 시간대 하나로 바꾼다. 스포너가 시계 대신 이것을 본다.</summary>
        public MeteorWaveBand ToBand() =>
            new(0f, WaveIntervalSeconds, MinMeteors, MaxMeteors, MediumChance, ImpactRadiusScale);

        /// <summary>이름으로 찾는다. 없으면 <c>null</c>.</summary>
        public static MeteorWavePreset Find(string name)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Name == name) return All[i];

            return null;
        }

        /// <summary>소형·중형 충격파 반경(m)을 이 강도에서 실제로 얼마인지 돌려준다.</summary>
        public Vector2 ImpactRadii =>
            new(PhysicsScale.MeteorSmallImpactRadius * ImpactRadiusScale,
                PhysicsScale.MeteorMediumImpactRadius * ImpactRadiusScale);
    }
}
