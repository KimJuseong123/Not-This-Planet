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
            FindMatch(moving, PhysicsScale.WeldCenterTolerance,
                      PhysicsScale.WeldGapTolerance, PhysicsScale.WeldTiltToleranceDegrees);

        /// <param name="centerTolerance">접합면을 따라 <b>옆으로</b> 어긋나도 되는 거리.</param>
        /// <param name="gapTolerance">접합면 법선 방향으로 <b>위아래로</b> 벌어져도 되는 거리.</param>
        /// <param name="tiltToleranceDegrees">두 접합면이 마주보는 각도의 오차 한계.</param>
        public static SocketMatch FindMatch(SpaceshipPart moving, float centerTolerance,
                                            float gapTolerance, float tiltToleranceDegrees)
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

                    // 두 접합면은 서로 마주봐야 한다. 즉 forward 가 정반대여야 한다.
                    var tilt = Vector3.Angle(movingSocket.transform.forward, -target.transform.forward);
                    if (tilt > tiltToleranceDegrees) continue;

                    var (center, gap) = Offsets(movingSocket, target);
                    if (center > centerTolerance) continue;
                    if (gap > gapTolerance) continue;

                    var candidate = new SocketMatch(movingSocket, target, center, gap, tilt);
                    if (IsBetter(candidate, best)) best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 두 접합면의 어긋남을 <b>옆</b>과 <b>위아래</b>로 나눠 잰다.
        ///
        /// 3차원 거리 하나로 재면 두 가지를 구분하지 못한다. 옆으로 0.5m 어긋난 것과 위아래로
        /// 0.5m 뜬 것은 거리가 같지만, 앞은 접합선이 이어지고 뒤는 이어지지 않는다.
        /// 기준면은 받는 소켓의 forward 다 — 옮기는 쪽은 기울어 있을 수 있어 기준이 못 된다.
        /// </summary>
        static (float Center, float Gap) Offsets(PartSocket movingSocket, PartSocket target)
        {
            var normal = target.transform.forward;
            var delta = movingSocket.transform.position - target.transform.position;

            var gap = Mathf.Abs(Vector3.Dot(delta, normal));
            var center = Vector3.ProjectOnPlane(delta, normal).magnitude;

            return (center, gap);
        }


        static bool IsBetter(SocketMatch candidate, SocketMatch best)
        {
            if (!best.HasValue) return true;

            const float epsilon = 0.001f;

            // 옆으로 덜 어긋난 쪽, 그다음 덜 뜬 쪽, 그다음 덜 기운 쪽이 낫다.
            if (candidate.CenterOffset < best.CenterOffset - epsilon) return true;
            if (candidate.CenterOffset > best.CenterOffset + epsilon) return false;

            if (candidate.Gap < best.Gap - epsilon) return true;
            if (candidate.Gap > best.Gap + epsilon) return false;

            if (candidate.TiltDegrees < best.TiltDegrees - epsilon) return true;
            if (candidate.TiltDegrees > best.TiltDegrees + epsilon) return false;

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

        /// <param name="breakForce">
        /// 조인트를 만들 때 넣는 초기 강도.
        ///
        /// <b>첫 <see cref="PartAssemblyLink.Tick"/> 이 이 값을 덮어쓴다.</b> 링크가 매 틱
        /// <c>WeldRatio</c> 로 강도를 다시 계산하기 때문이다(미접합 → 용접 사이 보간).
        /// 그래서 이 인수로 용접 강도를 흉내낼 수 없다 — 실제로 용접점을 채워야 한다.
        /// S15P21B202-194 에서 이것을 모른 채 강도 스윕을 돌려 32배까지 올려도 결과가
        /// 똑같은 것을 보고서야 알았다.
        /// </param>
        /// <param name="breakTorque">위와 같다.</param>
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

            // <b>파츠를 옮기지 않는다.</b> 올려놓은 자리와 각도 그대로 조인트를 건다.
            //
            // 예전에는 여기서 SetPositionAndRotation 으로 자세를 맞춰 줬다. 그러면 비뚤어진
            // 적층이 존재할 수 없고, 기획(S15P21B202-107 §15.1)이 요구하는 "잘못 올리면
            // 기울거나 떨어진다" 가 성립하지 않는다. 발사 하중 시험(§22.3)도 항상 통과해
            // 붕괴(§22.4)와 절단·재조립(§16.3)이 일어날 일이 없어진다.
            //
            // 속도만 지운다. 내려놓는 순간의 잔여 속도가 남으면 조인트가 걸리자마자
            // 탑 전체를 흔든다.
            movingBody.linearVelocity = Vector3.zero;
            movingBody.angularVelocity = Vector3.zero;

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
