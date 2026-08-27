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
        public PendingMeteorDrop(Vector3 impactPoint, float remainingSeconds, float warningSeconds)
        {
            ImpactPoint = impactPoint;
            RemainingSeconds = remainingSeconds;
            WarningSeconds = warningSeconds;
        }

        /// <summary>떨어질 지점.</summary>
        public Vector3 ImpactPoint { get; }

        /// <summary>낙하까지 남은 시간.</summary>
        public float RemainingSeconds { get; }

        /// <summary>이 예고의 전체 시간. 진행도를 그리려면 필요하다.</summary>
        public float WarningSeconds { get; }

        /// <summary>0 에서 1 로 차오르는 예고 진행도.</summary>
        public float Progress01 =>
            WarningSeconds <= 0f ? 1f : Mathf.Clamp01(1f - RemainingSeconds / WarningSeconds);

        public PendingMeteorDrop Advanced(float deltaTime) =>
            new(ImpactPoint, RemainingSeconds - deltaTime, WarningSeconds);
    }
}
