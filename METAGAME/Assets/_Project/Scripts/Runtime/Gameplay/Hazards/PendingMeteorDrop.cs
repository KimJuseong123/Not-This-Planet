using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 예고된 낙하 하나. 어디에 떨어질지와 얼마나 남았는지를 담는다.
    ///
    /// 계산 결과만 들고 있고 아무것도 실행하지 않는다.
    /// 착탄 예고 표시(데칼, 그림자, 경고음)는 이 값을 읽어 UI 와 Art 가 만든다.
    /// </summary>
    public readonly struct PendingMeteorDrop
    {
        public PendingMeteorDrop(Vector3 impactPoint, float remainingSeconds, float warningSeconds,
                                 float impactRadius)
        {
            ImpactPoint = impactPoint;
            RemainingSeconds = remainingSeconds;
            WarningSeconds = warningSeconds;
            ImpactRadius = impactRadius;
        }

        /// <summary>떨어질 지점.</summary>
        public Vector3 ImpactPoint { get; }

        /// <summary>낙하까지 남은 시간.</summary>
        public float RemainingSeconds { get; }

        /// <summary>이 예고의 전체 시간. 진행도를 그리려면 필요하다.</summary>
        public float WarningSeconds { get; }

        /// <summary>
        /// 이 운석의 충격 반경(m).
        ///
        /// 기획 §17.2 가 "<b>실제 충격 범위와 같은 크기</b>의 원형 표시" 를 요구한다.
        /// S15P21B202-234 가 소형(2m)·중형(3.5m)을 나눴는데 그 값이 예고 쪽으로
        /// 전달되지 않아, 표시를 그릴 근거가 없었다(S15P21B202-241 에서 더했다).
        ///
        /// <b>없으면 중형에서 어긋난다.</b> 2m 원을 보고 피했는데 3.5m 로 맞으면
        /// §17.2 가 "같은 크기" 를 못박은 뜻이 사라진다.
        /// </summary>
        public float ImpactRadius { get; }

        /// <summary>0 에서 1 로 차오르는 예고 진행도.</summary>
        public float Progress01 =>
            WarningSeconds <= 0f ? 1f : Mathf.Clamp01(1f - RemainingSeconds / WarningSeconds);

        public PendingMeteorDrop Advanced(float deltaTime) =>
            new(ImpactPoint, RemainingSeconds - deltaTime, WarningSeconds, ImpactRadius);
    }
}
