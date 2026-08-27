using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 스냅 후보를 찾고 실제 조립을 수행한다.
    ///
    /// 배치는 하이브리드다. 소켓 근처에서는 위치와 회전을 정렬해 붙이고,
    /// 소켓이 없는 곳에서는 자유 배치를 그대로 둔다. 자유 물리만 쓰면 4인이 3.7m 파츠를
    /// 손으로 맞추는 일이 되어 조립이 성립하지 않고, 스냅만 쓰면 물리 놀이의 재미가 사라진다.
    ///
    /// 붙인 파츠는 곧바로 단단해지지 않는다. 끊어질 수 있는 조인트로만 잡아 두어
    /// "아직 용접하지 않은 탑" 을 물리로 표현한다. 운석에 무너지는 연출이 여기서 공짜로 나온다.
    /// </summary>
    public static class PartAssembler
    {
        /// <summary>
        /// 옮기는 파츠에 맞는 스냅 후보를 찾는다. 없으면 <see cref="SocketMatch.None"/> 이다.
        ///
        /// 정렬 기준은 거리 → 각도 오차 → 월드 좌표다. 마지막 기준으로 인스턴스 ID 를 쓰지 않는다.
        /// ID 는 머신마다 다르게 배정되어 4인이 각자 다른 후보를 고를 수 있기 때문이다.
        /// </summary>
        public static SocketMatch FindMatch(SpaceshipPart moving) =>
            FindMatch(moving, PhysicsScale.SnapDistance, PhysicsScale.SnapAngleDegrees);

        public static SocketMatch FindMatch(SpaceshipPart moving, float snapDistance, float snapAngleDegrees)
        {
            if (moving == null) return SocketMatch.None;

            var best = SocketMatch.None;

            foreach (var movingSocket in moving.Sockets)
            {
                if (movingSocket == null) continue;
                if (movingSocket.Role != SocketRole.Base) continue;
                if (movingSocket.IsOccupied) continue;

                foreach (var target in PartSocket.All)
                {
                    if (target == null || !target.CanReceive) continue;
                    if (target.Owner == moving) continue;

                    var distance = Vector3.Distance(movingSocket.transform.position, target.transform.position);
                    if (distance > snapDistance) continue;

                    // 두 접합면은 서로 마주봐야 한다. 즉 forward 가 정반대여야 한다.
                    var angle = Vector3.Angle(movingSocket.transform.forward, -target.transform.forward);
                    if (angle > snapAngleDegrees) continue;

                    var (position, rotation) = Snapped(moving, movingSocket, target);
                    var candidate = new SocketMatch(movingSocket, target, position, rotation, distance, angle);

                    if (IsBetter(candidate, best)) best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 옮기는 소켓이 대상 소켓에 정확히 맞물리도록 파츠를 놓을 자세를 계산한다.
        ///
        /// 파츠는 localScale 이 1 이 아니므로 로컬 좌표로 오프셋을 다루면 스케일이 두 번 곱해진다.
        /// 월드 오프셋을 회전 변화량만큼 돌리는 방식으로 계산해 스케일과 무관하게 만들었다.
        /// </summary>
        static (Vector3 Position, Quaternion Rotation) Snapped(
            SpaceshipPart moving, PartSocket movingSocket, PartSocket target)
        {
            var partTransform = moving.transform;
            var socketTransform = movingSocket.transform;

            // 맞물린 뒤 옮기는 소켓이 가져야 할 세계 회전.
            var desiredSocketRotation = Quaternion.LookRotation(
                -target.transform.forward, target.transform.up);

            var socketLocalRotation = Quaternion.Inverse(partTransform.rotation) * socketTransform.rotation;
            var partRotation = desiredSocketRotation * Quaternion.Inverse(socketLocalRotation);

            var deltaRotation = partRotation * Quaternion.Inverse(partTransform.rotation);
            var socketOffset = socketTransform.position - partTransform.position;
            var partPosition = target.transform.position - deltaRotation * socketOffset;

            return (partPosition, partRotation);
        }

        static bool IsBetter(SocketMatch candidate, SocketMatch best)
        {
            if (!best.HasValue) return true;

            const float epsilon = 0.001f;

            if (candidate.Distance < best.Distance - epsilon) return true;
            if (candidate.Distance > best.Distance + epsilon) return false;

            if (candidate.AngleDegrees < best.AngleDegrees - epsilon) return true;
            if (candidate.AngleDegrees > best.AngleDegrees + epsilon) return false;

            return ComparePosition(
                candidate.TargetSocket.transform.position,
                best.TargetSocket.transform.position) < 0;
        }

        /// <summary>월드 좌표를 x, y, z 순으로 비교해 순서를 하나로 고정한다.</summary>
        static int ComparePosition(Vector3 left, Vector3 right)
        {
            var comparison = left.x.CompareTo(right.x);
            if (comparison != 0) return comparison;

            comparison = left.y.CompareTo(right.y);
            return comparison != 0 ? comparison : left.z.CompareTo(right.z);
        }

        /// <summary>
        /// 후보대로 파츠를 정렬해 놓고 끊어질 수 있는 조인트로 잡는다.
        /// 파츠 상태는 <see cref="SpaceshipPart.TryTransitionTo"/> 로만 바꾼다.
        /// </summary>
        public static PartAssemblyLink Assemble(SocketMatch match) =>
            Assemble(match, PhysicsScale.UnweldedBreakForce, PhysicsScale.UnweldedBreakTorque);

        public static PartAssemblyLink Assemble(SocketMatch match, float breakForce, float breakTorque)
        {
            if (!match.HasValue) return null;

            var moving = match.MovingSocket.Owner;
            var target = match.TargetSocket.Owner;
            if (moving == null || target == null) return null;

            var movingBody = moving.GetComponent<Rigidbody>();
            var targetBody = target.GetComponent<Rigidbody>();
            if (movingBody == null || targetBody == null) return null;

            // 조립은 빔이 들고 있던 파츠를 내려놓는 동작이다. 그래서 Carried 에서 Placed 로 올린다.
            // 이미 Placed / Welded 인 파츠를 다시 붙이는 것은 허용한다(위치 재조정).
            // 그 밖의 상태(Locked, Free)는 조립 대상이 아니므로 조용히 넘기지 않고 실패시킨다.
            // 예전에는 Carried 가 아닐 때 상태 전환을 건너뛰어, Free 파츠가 붙었는데도
            // Placed 가 되지 않아 그 위에 다음 층을 쌓을 수 없는 상태가 됐다.
            if (moving.State == PartState.Carried)
            {
                if (!moving.TryTransitionTo(PartState.Placed)) return null;
            }
            else if (moving.State is not (PartState.Placed or PartState.Welded))
            {
                return null;
            }

            // 파츠 하나에 링크는 하나다. 다시 조립하는 경우 이전 링크를 먼저 정리한다.
            var existing = moving.GetComponent<PartAssemblyLink>();
            if (existing != null) existing.Release();

            moving.transform.SetPositionAndRotation(match.SnappedPosition, match.SnappedRotation);
            movingBody.linearVelocity = Vector3.zero;
            movingBody.angularVelocity = Vector3.zero;
            UnityEngine.Physics.SyncTransforms();

            var joint = moving.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = targetBody;
            joint.breakForce = breakForce;
            joint.breakTorque = breakTorque;
            joint.enablePreprocessing = false;

            match.MovingSocket.Connect(match.TargetSocket);

            var link = moving.gameObject.AddComponent<PartAssemblyLink>();
            link.Bind(match.MovingSocket, match.TargetSocket, joint);
            return link;
        }
    }
}
