using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>낙하 예고가 지금 어느 단계인지(기획 §17.2 · §18.1).</summary>
    public enum MeteorWarningStage
    {
        /// <summary>차오르는 중. 점멸하지 않는다.</summary>
        Filling = 0,

        /// <summary>
        /// 방망이로 쳐낼 수 있다. 기획 §18.1 "타격 가능한 순간에는 낙하 표시가
        /// <b>강하게 점멸한다</b>".
        /// </summary>
        Strikable = 1,

        /// <summary>기획 §17.2 "마지막 0.5초에는 <b>빠르게 점멸한다</b>".</summary>
        Imminent = 2,
    }

    /// <summary>
    /// 낙하 예고를 어떻게 보여줄지 계산한다(기획 §17.2 · §18.1).
    ///
    /// <b>그리지 않고 값만 낸다.</b> 표시를 실제로 만드는 것은
    /// <see cref="MeteorWarningView"/> 이고, 여기는 "지금 무슨 색이고 얼마나 찼고
    /// 어떻게 점멸하는가" 만 정한다. 나눈 이유는 검증이다 — 렌더러와 머티리얼을 세우지
    /// 않고도 규칙을 잠글 수 있고, [Art] 가 실제 데칼로 바꿔도 이 규칙은 그대로 산다.
    ///
    /// 계산은 <see cref="PendingMeteorDrop"/> 하나에서만 나온다. 상태를 갖지 않으므로
    /// 같은 예고를 몇 번 물어도 같은 답이다.
    /// </summary>
    public static class MeteorWarningLook
    {
        /// <summary>
        /// 예고가 시작될 때의 색. 기획 §17.2 "<b>주황색으로 시작해</b>".
        /// </summary>
        public static readonly Color StartColor = new(1f, 0.55f, 0.10f, 0.45f);

        /// <summary>
        /// 착탄 직전의 색. 기획 §17.2 "운석이 가까워질수록 <b>붉게 변하고</b>".
        /// </summary>
        public static readonly Color EndColor = new(1f, 0.12f, 0.05f, 0.85f);

        /// <summary>
        /// 지금 어느 단계인가.
        ///
        /// 경계값을 <see cref="PhysicsScale.BatSwingWindowSeconds"/> 에서 가져온다 —
        /// 방망이가 쳐낼 수 있는 순간과 표시가 알리는 순간이 <b>같은 값</b>이어야 한다.
        /// 따로 적으면 "점멸하는데 안 맞는" 구간이 생기고, 플레이어는 이유를 알 수 없다.
        /// </summary>
        public static MeteorWarningStage StageOf(in PendingMeteorDrop drop)
        {
            var remaining = drop.RemainingSeconds;

            if (remaining <= PhysicsScale.MeteorWarningImminentSeconds)
                return MeteorWarningStage.Imminent;

            if (remaining <= PhysicsScale.BatSwingWindowSeconds)
                return MeteorWarningStage.Strikable;

            return MeteorWarningStage.Filling;
        }

        /// <summary>테두리 색. 진행도에 따라 주황에서 붉게 간다.</summary>
        public static Color RingColorOf(in PendingMeteorDrop drop) =>
            Color.Lerp(StartColor, EndColor, drop.Progress01);

        /// <summary>
        /// 안쪽이 차오른 비율 0~1. 기획 §17.2 "<b>안쪽이 채워진다</b>".
        ///
        /// 진행도를 그대로 쓴다. 예고가 끝나는 순간이 착탄이므로(S15P21B202-234),
        /// 가득 찬 순간이 곧 맞는 순간이다.
        /// </summary>
        public static float FillOf(in PendingMeteorDrop drop) => drop.Progress01;

        /// <summary>표시 지름(m). 기획 §17.2 "실제 충격 범위와 <b>같은 크기</b>".</summary>
        public static float DiameterOf(in PendingMeteorDrop drop) => drop.ImpactRadius * 2f;

        /// <summary>
        /// 점멸 세기 0~1. 단계마다 주기가 다르다.
        ///
        /// <see cref="MeteorWarningStage.Filling"/> 에서는 0 이다 — 예고 내내 깜빡이면
        /// 마지막 0.5초의 "빠른 점멸" 이 눈에 띄지 않는다.
        /// </summary>
        public static float BlinkOf(in PendingMeteorDrop drop, float time)
        {
            var stage = StageOf(drop);
            if (stage == MeteorWarningStage.Filling) return 0f;

            var hz = stage == MeteorWarningStage.Imminent
                ? PhysicsScale.MeteorWarningImminentBlinkHz
                : PhysicsScale.MeteorWarningStrikableBlinkHz;

            // 0~1 로 오르내린다. 색이나 알파에 곱해 쓴다.
            return 0.5f + 0.5f * Mathf.Sin(time * hz * Mathf.PI * 2f);
        }
    }
}
