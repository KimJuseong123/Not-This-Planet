using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 탑승 중에는 <b>좌석 자세가 유일한 진실</b>임을 고정한다 (S15P21B202-434).
    ///
    /// <b>왜 바꿨는가.</b> 예전에는 골반에 <c>FixedJoint</c> 를 걸어 몸을 탈것에 매달았다.
    /// 그러면 탑승자 위치를 정하는 주체가 <b>셋</b>이 된다 — 그 조인트 ·
    /// <c>TransportVehicle.RideAlong</c> 의 속도 주입 · 참가자 쪽의 좌석 추종
    /// (S15P21B202-423). 한 성질을 세 곳에서 판단하면 반드시 어긋나고, 실제로 참가자
    /// 화면에서 몸이 기체를 벗어났다.
    ///
    /// 이제 몸을 물리에서 빼고(kinematic · 충돌 끔) 탈것에 대한 <b>상대 자세</b>로만
    /// 옮긴다. 이 검증이 잠그는 것은 그 두 가지다 — <b>따라오는가</b>와
    /// <b>하차하면 정확히 되돌아오는가</b>.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 는 <c>ShipRideTests</c> 와 같은 이유다 —
    /// <c>PlayerRig.Create</c> 경로가 EditMode 에서 에러 로그를 남긴다(develop 에 이미 있다).
    /// </summary>
    public sealed class SeatHoldTests
    {
        const float Step = 0.02f;

        /// <summary>다른 검증과 멀리 떨어뜨린다 (S15P21B202-329 의 교훈).</summary>
        static readonly Vector3 Far = new Vector3(0f, 0f, 900f);

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();
            savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartSocket.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>탑승하면 몸이 물리에서 빠진다 — 좌석과 싸울 것이 없어야 한다.</summary>
        [Test]
        public void BoardingTakesTheBodyOutOfPhysics()
        {
            var player = MakePlayer();
            var seat = MakeSeat();

            Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position), "먼저 타야 한다.");

            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var part = player.Doll.Bodies[i];
                if (part == null) continue;

                Assert.IsTrue(part.isKinematic, $"{part.name} 이 물리에 남아 있다");
                Assert.IsFalse(part.detectCollisions, $"{part.name} 의 충돌이 살아 있다");
            }
        }

        /// <summary>
        /// 좌석이 움직이면 몸이 그만큼 따라간다.
        ///
        /// <para>조인트가 없으니 이것이 유일한 이동 수단이다 — 안 따라오면 탑승자가
        /// 기체 밖에 남는다(원래 증상).</para>
        /// </summary>
        [Test]
        public void TheBodyFollowsWhenTheSeatMoves()
        {
            var player = MakePlayer();
            var seat = MakeSeat();
            Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position));

            var before = player.Doll.Hips.position - seat.transform.position;

            seat.transform.position += new Vector3(12f, 7f, -5f);
            player.Tick();
            UnityEngine.Physics.Simulate(Step);

            var after = player.Doll.Hips.position - seat.transform.position;

            Assert.Less((after - before).magnitude, 0.05f,
                $"좌석 기준 자리가 달라졌다 — {(after - before).magnitude:0.000}m 벌어졌다");
        }

        /// <summary>
        /// 좌석이 <b>돌아도</b> 따라간다.
        ///
        /// <para>상대 자세로 적어 두는 이유가 이것이다. 월드 자세를 적어 두면 운석에
        /// 맞아 기체가 구를 때(S15P21B202-349·-353) 몸만 제자리에 남는다.</para>
        /// </summary>
        [Test]
        public void TheBodyFollowsWhenTheSeatTurns()
        {
            var player = MakePlayer();
            var seat = MakeSeat();
            Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position));

            var localBefore = seat.transform.InverseTransformPoint(player.Doll.Hips.position);

            seat.transform.rotation = Quaternion.Euler(0f, 90f, 25f);
            player.Tick();
            UnityEngine.Physics.Simulate(Step);

            var localAfter = seat.transform.InverseTransformPoint(player.Doll.Hips.position);

            Assert.Less((localAfter - localBefore).magnitude, 0.05f,
                $"돌았을 때 좌석 기준 자리가 달라졌다 — {(localAfter - localBefore).magnitude:0.000}m");
        }

        /// <summary>
        /// 하차하면 원래 강체 설정으로 돌아온다.
        ///
        /// <para>되돌리지 않으면 하차한 사람이 공중에 굳는다 — 원래 버그보다 나쁘다.</para>
        /// </summary>
        [Test]
        public void LeavingRestoresThePhysicsExactly()
        {
            var player = MakePlayer();
            var seat = MakeSeat();

            var wasKinematic = new bool[player.Doll.Bodies.Count];
            var wasColliding = new bool[player.Doll.Bodies.Count];
            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var part = player.Doll.Bodies[i];
                if (part == null) continue;
                wasKinematic[i] = part.isKinematic;
                wasColliding[i] = part.detectCollisions;
            }

            Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position));
            player.LeaveShip(Far, Quaternion.identity);

            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var part = player.Doll.Bodies[i];
                if (part == null) continue;

                Assert.AreEqual(wasKinematic[i], part.isKinematic, $"{part.name} kinematic");
                Assert.AreEqual(wasColliding[i], part.detectCollisions, $"{part.name} 충돌");
            }
        }

        /// <summary>타고 내리기를 되풀이해도 설정이 어긋나지 않는다.</summary>
        [Test]
        public void BoardingAndLeavingRepeatedlyDoesNotDrift()
        {
            var player = MakePlayer();
            var seat = MakeSeat();

            for (var round = 0; round < 3; round++)
            {
                Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position),
                              $"{round + 1}번째 탑승이 안 됐다");
                player.LeaveShip(Far, Quaternion.identity);
            }

            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var part = player.Doll.Bodies[i];
                if (part == null) continue;

                Assert.IsFalse(part.isKinematic, $"{part.name} 이 kinematic 으로 남았다");
                Assert.IsTrue(part.detectCollisions, $"{part.name} 의 충돌이 꺼진 채 남았다");
            }
        }

        /// <summary>사출은 그대로 동작한다 — 좌석을 놓은 뒤 충격이 들어가야 한다.</summary>
        [Test]
        public void EjectingStillPushesTheBodyOut()
        {
            var player = MakePlayer();
            var seat = MakeSeat();
            Assert.IsTrue(player.TryBoardShip(seat, seat.transform.position));

            var from = player.Doll.Hips.position;
            player.EjectFromShip(Vector3.forward);

            for (var s = 0; s < 100; s++)   // 2초
            {
                player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }

            var moved = player.Doll.Hips.position - from;
            moved.y = 0f;

            Assert.Greater(moved.magnitude, 1.5f,
                $"좌석을 놓고도 밀려나지 않았다 — {moved.magnitude:0.00}m");
        }

        // --- 도구 -----------------------------------------------------------

        PlayerRig MakePlayer()
        {
            var player = PlayerRig.Create("테스트", Far, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        /// <summary>
        /// 좌석 역할을 하는 강체. <b>콜라이더를 붙이지 않는다</b> —
        /// <c>ShipRideTests.Ejected_PlayerMovesAwayFromTheWreck</c> 가 적어 둔 이유와 같다.
        /// 단단한 상자 안에서 사출하면 결과가 관통 해소에 달린다.
        /// </summary>
        Rigidbody MakeSeat()
        {
            var go = new GameObject("Seat");
            go.transform.position = Far + Vector3.up * 1f;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            spawned.Add(go);
            return body;
        }
    }
}
