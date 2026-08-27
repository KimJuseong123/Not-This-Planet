using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 스냅 후보 하나. 어느 소켓끼리 맞물릴지와, 그렇게 맞물렸을 때 파츠가 놓일 자세를 담는다.
    ///
    /// 계산 결과만 들고 있고 아무것도 실행하지 않는다.
    /// 미리보기 표시(고스트)는 이 값을 읽어 UI 가 그린다.
    /// </summary>
    public readonly struct SocketMatch
    {
        public SocketMatch(
            PartSocket movingSocket, PartSocket targetSocket,
            Vector3 snappedPosition, Quaternion snappedRotation,
            float distance, float angleDegrees)
        {
            MovingSocket = movingSocket;
            TargetSocket = targetSocket;
            SnappedPosition = snappedPosition;
            SnappedRotation = snappedRotation;
            Distance = distance;
            AngleDegrees = angleDegrees;
        }

        /// <summary>옮기는 파츠의 아래쪽 소켓.</summary>
        public PartSocket MovingSocket { get; }

        /// <summary>받는 파츠의 위쪽 소켓.</summary>
        public PartSocket TargetSocket { get; }

        /// <summary>맞물렸을 때 옮기는 파츠의 위치.</summary>
        public Vector3 SnappedPosition { get; }

        /// <summary>맞물렸을 때 옮기는 파츠의 회전.</summary>
        public Quaternion SnappedRotation { get; }

        /// <summary>두 소켓 사이 거리. 후보 정렬에 쓴다.</summary>
        public float Distance { get; }

        /// <summary>두 접합면이 마주보는 정도의 오차(도). 후보 정렬에 쓴다.</summary>
        public float AngleDegrees { get; }

        public bool HasValue => MovingSocket != null && TargetSocket != null;

        public static SocketMatch None => default;
    }
}
