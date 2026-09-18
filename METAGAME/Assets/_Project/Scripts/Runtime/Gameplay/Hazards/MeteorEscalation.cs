using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 시간이 갈수록 공세를 조이는 계산(2026-09-18).
    ///
    /// <para><b>왜 표 위에 따로 얹는가.</b> 기획 §17.4 의 표
    /// (<see cref="MeteorWaveTable"/>)는 다섯 칸짜리 <b>계단</b>이고 마지막 칸이
    /// "20분~ · 15초 · 5개" 하나다 — 그 뒤로는 영원히 같고, 칸 안에서도 5분 내내
    /// 아무것도 안 바뀐다. 플레이 확인에서 "진행될수록 점점 더 강하게" 가 나온 자리다.</para>
    ///
    /// <para><b>표를 고치지 않은 이유는 두 가지다.</b> 하나는 표가 기획서 그대로여야
    /// 한다는 것(<see cref="MeteorWaveTable.Rows"/> 의 주석). 다른 하나가 더 실질적인데,
    /// <b>이미 저장된 씬은 시간대를 직접 직렬화해 두어 표를 고쳐도 따라오지 않는다</b> —
    /// <c>CheatArena</c> 와 <c>Stage1_Astra</c> 가 그렇다. 표를 고치면 새로 만드는
    /// 스포너에만 반영되고 정작 시험하는 씬은 그대로다.</para>
    ///
    /// <para><b>계단을 없애지는 않는다.</b> <see cref="MeteorSpawner.IntervalAt"/> 이
    /// 적어 둔 대로 공세는 하나씩 흐르는 것이 아니라 <b>묶음</b>으로 와야 §17.3 의
    /// "5초 전 신호" 가 붙을 자리가 있다. 여기서 바꾸는 것은 묶음의 세기와 간격뿐이고,
    /// 묶음이라는 구조는 그대로 둔다.</para>
    ///
    /// <para><b>상태가 없는 순수 계산이다</b> — <see cref="MeteorImpact"/> 와 같은
    /// 방식이다. 검증이 경과 시간을 직접 넣어 곡선을 잠글 수 있다.</para>
    /// </summary>
    public static class MeteorEscalation
    {
        /// <summary>
        /// 경과 시간이 만든 배율. 1 에서 시작해 <b>끝없이</b> 오른다.
        ///
        /// <para>15분에 ×2, 30분에 ×3, 45분에 ×4. 상한을 두지 않는 것은 의도다 —
        /// 오래 끌수록 불리해야 발사를 서두른다. 실제 한계는 간격 바닥
        /// (<see cref="PhysicsScale.MeteorEscalationIntervalFloor"/>)과 개수 상한
        /// (<see cref="PhysicsScale.MeteorEscalationCountCap"/>)이 정한다.</para>
        /// </summary>
        public static float FactorAt(float elapsedSeconds) =>
            FactorAt(elapsedSeconds, PhysicsScale.MeteorEscalationRampMinutes);

        public static float FactorAt(float elapsedSeconds, float rampMinutes)
        {
            if (rampMinutes <= 0f) return 1f;

            var minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
            return 1f + minutes / rampMinutes;
        }

        /// <summary>
        /// 조여진 공세 간격(초). 시간대가 정한 값을 배율로 나눈다.
        ///
        /// <b>바닥이 있다.</b> §17.3 의 신호가 5초 전에 울리는데 간격이 그보다 짧으면
        /// 신호가 이전 공세 안에서 울려 무엇을 예고하는지 알 수 없다.
        /// </summary>
        public static float IntervalFor(float bandSeconds, float elapsedSeconds) =>
            IntervalFor(bandSeconds, elapsedSeconds,
                        PhysicsScale.MeteorEscalationRampMinutes,
                        PhysicsScale.MeteorEscalationIntervalFloor);

        public static float IntervalFor(float bandSeconds, float elapsedSeconds,
                                        float rampMinutes, float floorSeconds)
        {
            if (bandSeconds <= 0f) return bandSeconds;

            var scaled = bandSeconds / FactorAt(elapsedSeconds, rampMinutes);

            // 바닥이 원래 간격보다 크면 원래 간격을 쓴다 — 초반(30초)을 바닥(5초)까지
            // 끌어올리는 일은 없어야 한다.
            return Mathf.Max(Mathf.Min(bandSeconds, floorSeconds), scaled);
        }

        /// <summary>
        /// 조여진 운석 수. 시간대가 정한 값에 배율을 곱하고 상한에서 자른다.
        ///
        /// <b>인원 보정(§17.6) 전의 값이다.</b> 보정은 스포너가 그다음에 곱한다 —
        /// 순서를 바꾸면 혼자 할 때 상한이 8 이 아니라 3.6 이 되어 뜻이 달라진다.
        /// </summary>
        public static int CountFor(int bandCount, float elapsedSeconds) =>
            CountFor(bandCount, elapsedSeconds,
                     PhysicsScale.MeteorEscalationRampMinutes,
                     PhysicsScale.MeteorEscalationCountCap);

        public static int CountFor(int bandCount, float elapsedSeconds,
                                   float rampMinutes, int cap)
        {
            if (bandCount <= 0) return bandCount;

            var scaled = Mathf.RoundToInt(bandCount * FactorAt(elapsedSeconds, rampMinutes));
            return Mathf.Clamp(scaled, bandCount, Mathf.Max(bandCount, cap));
        }

        /// <summary>
        /// 조여진 중형 확률.
        ///
        /// <b>표가 0 이면 0 으로 둔다.</b> §17.7 이 "중형 · 등장 10분 이후" 로 정하는데,
        /// 경과 시간만 보고 더하면 <b>1분에도 중형이 나온다.</b> 등장 시점을 정하는 것은
        /// 여전히 표이고, 여기서 하는 일은 이미 열린 문을 더 여는 것뿐이다.
        /// </summary>
        public static float MediumChanceFor(float bandChance, float elapsedSeconds) =>
            MediumChanceFor(bandChance, elapsedSeconds,
                            PhysicsScale.MeteorEscalationMediumPerMinute,
                            PhysicsScale.MeteorEscalationMediumCap);

        public static float MediumChanceFor(float bandChance, float elapsedSeconds,
                                            float perMinute, float cap)
        {
            if (bandChance <= 0f) return 0f;

            var minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
            var raised = bandChance + perMinute * minutes;

            return Mathf.Clamp(raised, bandChance, Mathf.Max(bandChance, cap));
        }

        /// <summary>
        /// 조여진 충격 반경 배율.
        ///
        /// <b>예고 원도 이 값을 거친다.</b> <see cref="MeteorSpawner.ImpactRadiusScale"/>
        /// 하나가 예고·운석·흔적 모두의 출처이므로, 여기만 바꾸면 세 곳이 함께 넓어진다 —
        /// 한 곳이라도 빠지면 §17.2 의 "실제 충격 범위와 같은 크기" 가 깨진다.
        /// </summary>
        public static float RadiusScaleFor(float bandScale, float elapsedSeconds) =>
            RadiusScaleFor(bandScale, elapsedSeconds,
                           PhysicsScale.MeteorEscalationRadiusPerMinute,
                           PhysicsScale.MeteorEscalationRadiusCap);

        public static float RadiusScaleFor(float bandScale, float elapsedSeconds,
                                           float perMinute, float cap)
        {
            if (bandScale <= 0f) return bandScale;

            var minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
            var grown = Mathf.Min(1f + perMinute * minutes, Mathf.Max(1f, cap));

            return bandScale * grown;
        }
    }
}
