using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 접합 후보 하나. 어느 소켓끼리 맞물릴지와, 얼마나 어긋나 있는지를 담는다.
    ///
    /// <b>맞물렸을 때의 자세는 담지 않는다.</b> 예전에는 정렬된 위치·회전을 함께 들고 있다가
    /// 조립할 때 파츠를 그 자세로 옮겼다. 기획(S15P21B202-107 §15.1)은 자동 정렬을 금지하므로
    /// 지금은 올려놓은 자세를 그대로 두고, 이 구조체는 "붙일 수 있는가" 만 말한다.
    ///
    /// 계산 결과만 들고 있고 아무것도 실행하지 않는다.
    /// </summary>
    public readonly struct SocketMatch
    {
        public SocketMatch(
            PartSocket movingSocket, PartSocket targetSocket,
            float centerOffset, float gap, float tiltDegrees)
        {
            MovingSocket = movingSocket;
            TargetSocket = targetSocket;
            CenterOffset = centerOffset;
            Gap = gap;
            TiltDegrees = tiltDegrees;
        }

        /// <summary>옮기는 파츠의 아래쪽 소켓.</summary>
        public PartSocket MovingSocket { get; }

        /// <summary>받는 파츠의 위쪽 소켓.</summary>
        public PartSocket TargetSocket { get; }

        /// <summary>접합면을 따라 옆으로 어긋난 거리(m).</summary>
        public float CenterOffset { get; }

        /// <summary>접합면 법선 방향으로 벌어진 거리(m).</summary>
        public float Gap { get; }

        /// <summary>두 접합면이 마주보는 정도의 오차(도).</summary>
        public float TiltDegrees { get; }

        public bool HasValue => MovingSocket != null && TargetSocket != null;

        public static SocketMatch None => default;
    }
}
