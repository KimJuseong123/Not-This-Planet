namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 공세 강도. <b>컷신 전과 후, 둘뿐이다</b>(2026-09-18).
    ///
    /// <para><b>시계가 아니라 컷신이 정한다.</b> 처음에는 경과 시간으로 서서히 조이는
    /// 곡선을 넣었다. 그런데 이 스테이지의 난이도 전환점은 이미 정해져 있다 —
    /// 기획 §17.8 의 공세 트리거이고, 플레이어가 비행선 구역에 들어서면
    /// <c>Stage1AssaultTrigger</c> 가 컷신을 띄운다. 컷신이 "이제부터 다르다" 를
    /// 말하는데 시계가 따로 돌면, 컷신을 일찍 본 판과 늦게 본 판의 난이도가 달라져
    /// <b>같은 연출이 다른 뜻이 된다.</b></para>
    ///
    /// <para><b>시간대 표(§17.4)를 더 이상 읽지 않는다.</b> 다섯 칸짜리 계단은 시계를
    /// 전제로 한 것이라 이 구조와 맞지 않는다. <c>MeteorSpawner</c> 의
    /// <c>_waveBands</c> 는 씬에 남아 있지만 읽히지 않는다 — 그 필드의 주석에 적었다.</para>
    ///
    /// <para><b>치트 프리셋은 여전히 이긴다.</b> <see cref="MeteorWavePreset"/> 은
    /// 강도를 손으로 고정해 반복 측정하는 장치이고, 고른 동안에는 이 두 단계도
    /// 무시된다.</para>
    ///
    /// <para><b>상태가 없는 값이다</b> — <see cref="MeteorImpact"/> 와 같은 방식이라
    /// 검증이 두 단계를 직접 넣어 잠글 수 있다.</para>
    /// </summary>
    public readonly struct MeteorIntensity
    {
        public MeteorIntensity(float waveIntervalSeconds, int meteors,
                               float mediumChance, float impactRadiusScale)
        {
            WaveIntervalSeconds = waveIntervalSeconds;
            Meteors = meteors;
            MediumChance = mediumChance;
            ImpactRadiusScale = impactRadiusScale;
        }

        /// <summary>공세 사이의 조용한 시간(초).</summary>
        public float WaveIntervalSeconds { get; }

        /// <summary>한 공세의 운석 수. 인원 보정(§17.6) <b>전</b>의 값이다.</summary>
        public int Meteors { get; }

        public float MediumChance { get; }

        /// <summary>크기별 충격 반경(§17.7)에 곱하는 값.</summary>
        public float ImpactRadiusScale { get; }

        /// <summary>
        /// 컷신 전 — 한 방울씩. 피할 것도 없이 "저것이 떨어지는구나" 만 알려 준다.
        ///
        /// <b>0 발이 아닌 것이 핵심이다.</b> 예전에는 공세가 켜지기 전까지 스포너가
        /// 통째로 막혀 하늘이 완전히 조용했고, 그러면 운석이 위협이라는 것을
        /// <b>컷신에서 처음</b> 배웠다 — 배우는 자리가 곧 위험해지는 자리면 늦다.
        /// </summary>
        public static MeteorIntensity Drizzle => new(
            PhysicsScale.MeteorDrizzleIntervalSeconds,
            PhysicsScale.MeteorDrizzleMeteors,
            mediumChance: 0f,
            impactRadiusScale: 1f);

        /// <summary>
        /// 컷신 뒤 — 전부 건다. 서서히 오르지 않고 <b>컷신이 끝나는 순간</b> 이 값이다.
        ///
        /// <see cref="MeteorSpawner.BeginAssault"/> 가 <c>_sinceLastWave</c> 를 간격만큼
        /// 채워 두므로 첫 공세가 곧바로 온다 — 컷신이 끝나고 조용하면 연출이 거짓말이 된다.
        /// </summary>
        public static MeteorIntensity FullBlast => new(
            PhysicsScale.MeteorFullBlastIntervalSeconds,
            PhysicsScale.MeteorFullBlastMeteors,
            PhysicsScale.MeteorFullBlastMediumChance,
            PhysicsScale.MeteorFullBlastRadiusScale);

        /// <param name="assaultBegun"><c>MeteorSpawner.AssaultEnabled</c> — 컷신을 지났는가.</param>
        public static MeteorIntensity For(bool assaultBegun) =>
            assaultBegun ? FullBlast : Drizzle;

        /// <summary>
        /// 시간대 한 칸으로 바꾼다. 스포너가 시계 대신 이것을 본다.
        ///
        /// <see cref="MeteorWavePreset.ToBand"/> 와 같은 방식이다 — 뒤쪽 코드는
        /// <see cref="MeteorWaveBand"/> 하나만 알면 되고, 그 값이 표에서 왔는지
        /// 프리셋에서 왔는지 여기서 왔는지 구분하지 않는다.
        /// </summary>
        public MeteorWaveBand ToBand() =>
            new(0f, WaveIntervalSeconds, Meteors, Meteors, MediumChance, ImpactRadiusScale);
    }
}
