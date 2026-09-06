using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>낙하 예고가 지금 어느 단계인지(기획 §17.2 · §18.1).</summary>
    public enum MeteorWarningStage
    {
        /// <summary>
        /// 다가오는 중. 점멸하지 않는다.
        ///
        /// 예전 이름은 <c>Filling</c> 이었다 — 안쪽 원이 차오르던 때의 이름이다.
        /// 줄어드는 원으로 바꾸면서(S15P21B202-268) 이름이 사실과 어긋났다.
        /// </summary>
        Approaching = 0,

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

            if (remaining <= drop.WarningImminentSeconds)
                return MeteorWarningStage.Imminent;

            if (remaining <= drop.BatSwingWindowSeconds)
                return MeteorWarningStage.Strikable;

            return MeteorWarningStage.Approaching;
        }

        /// <summary>테두리 색. 진행도에 따라 주황에서 붉게 간다.</summary>
        public static Color RingColorOf(in PendingMeteorDrop drop) =>
            Color.Lerp(StartColor, EndColor, drop.Progress01);

        /// <summary>표시 지름(m). 기획 §17.2 "실제 충격 범위와 <b>같은 크기</b>".</summary>
        public static float DiameterOf(in PendingMeteorDrop drop) => drop.ImpactRadius * 2f;

        /// <summary>
        /// 떨어질 운석의 겉보기 지름(m). 안쪽 원이 여기까지 줄어든다.
        /// </summary>
        public static float MeteorDiameter => PhysicsScale.MeteorRadius * 2f;

        /// <summary>
        /// 안쪽 원의 크기 비율. 테두리 지름에 곱해 쓴다. 1 이면 테두리와 같은 크기다.
        ///
        /// <b>기획과 반대다.</b> §17.2 는 "주황색으로 시작해 운석이 가까워질수록 붉게
        /// 변하고 <b>안쪽이 채워진다</b>" 고 정하고, S15P21B202-241 이 그대로 만들었다.
        /// 인게임에서 보고 <b>줄어드는 원</b>으로 뒤집었다(S15P21B202-268) — 시간이
        /// 없어지는 것을 그쪽이 더 잘 읽힌다는 판단이다. [Plan] 확인이 필요하다.
        ///
        /// <code>
        /// 예고 시작   테두리와 같은 크기        1.0
        /// 착탄 순간   운석 겉보기 크기(0.9m)    MeteorDiameter / DiameterOf
        /// </code>
        ///
        /// <b>0 까지 줄이지 않는다.</b> 0 이면 마지막 순간에 아무것도 안 보이고, 무엇이
        /// 그 자리에 올지도 알려주지 못한다. 운석 크기에서 멈추면 <b>줄어들다 멈춘 그
        /// 자리에 실제 운석이 박힌다</b> — 크기가 운석과 맞는다는 것이 그 뜻이다.
        ///
        /// 테두리는 건드리지 않는다. 그것이 §17.2 의 "실제 충격 범위" 이고 어디까지
        /// 위험한지를 알린다.
        /// </summary>
        public static float CountdownScaleOf(in PendingMeteorDrop drop)
        {
            var ring = DiameterOf(drop);
            if (ring <= 0f) return 1f;

            var floor = Mathf.Clamp01(drop.MeteorRadius * 2f / ring);
            return Mathf.Lerp(1f, floor, drop.Progress01);
        }

        /// <summary>
        /// 점멸 세기 0~1. 단계마다 주기가 다르다.
        ///
        /// <see cref="MeteorWarningStage.Approaching"/> 에서는 0 이다 — 예고 내내 깜빡이면
        /// 마지막 0.5초의 "빠른 점멸" 이 눈에 띄지 않는다.
        /// </summary>
        public static float BlinkOf(in PendingMeteorDrop drop, float time)
        {
            var stage = StageOf(drop);
            if (stage == MeteorWarningStage.Approaching) return 0f;

            var hz = stage == MeteorWarningStage.Imminent
                ? drop.WarningImminentBlinkHz
                : drop.WarningStrikableBlinkHz;

            // 0~1 로 오르내린다. 색이나 알파에 곱해 쓴다.
            return 0.5f + 0.5f * Mathf.Sin(time * hz * Mathf.PI * 2f);
        }
    }
}
