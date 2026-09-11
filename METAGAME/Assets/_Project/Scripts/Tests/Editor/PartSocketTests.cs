using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 접합 지점의 점유 상태와 조립 순서 규칙을 검증한다.
    /// "아래에서 위로" 순서는 위쪽 소켓이 언제 받을 수 있는지로 표현된다.
    /// </summary>
    public sealed class PartSocketTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp() => PartSocket.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
        }

        [Test]
        public void Socket_ResolvesOwnerFromParent()
        {
            var part = SocketTestRig.CreatePart(_spawned, Vector3.zero);
            var top = SocketTestRig.TopSocketOf(part);

            Assert.AreSame(part, top.Owner);
        }

        [Test]
        public void TopSocket_CannotReceive_WhilePartIsLocked()
        {
            var part = SocketTestRig.CreatePart(_spawned, Vector3.zero);
            var top = SocketTestRig.TopSocketOf(part);

            Assert.AreEqual(PartState.Locked, part.State);
            Assert.IsFalse(top.CanReceive,
                "아직 고정쇠에 박혀 있는 파츠 위에 쌓을 수 없어야 한다.");
        }

        [Test]
        public void TopSocket_CannotReceive_WhilePartIsMerelyFree()
        {
            var part = SocketTestRig.CreatePart(_spawned, Vector3.zero);
            part.TryTransitionTo(PartState.Free);

            Assert.IsFalse(SocketTestRig.TopSocketOf(part).CanReceive,
                "떼어냈을 뿐 아직 조립에 들어가지 않은 파츠 위에는 쌓을 수 없다.");
        }

        [Test]
        public void TopSocket_CanReceive_WhenPartIsPlaced()
        {
            var part = SocketTestRig.Placed(_spawned, Vector3.zero);

            Assert.IsTrue(SocketTestRig.TopSocketOf(part).CanReceive);
        }

        [Test]
        public void TopSocket_CanReceive_WhenPartIsWelded()
        {
            var part = SocketTestRig.Placed(_spawned, Vector3.zero);
            part.TryTransitionTo(PartState.Welded);

            Assert.IsTrue(SocketTestRig.TopSocketOf(part).CanReceive,
                "용접이 끝난 파츠 위에도 계속 쌓을 수 있어야 한다.");
        }

        [Test]
        public void BaseSocket_NeverReceives()
        {
            var part = SocketTestRig.Placed(_spawned, Vector3.zero);

            Assert.IsFalse(SocketTestRig.BaseSocketOf(part).CanReceive);
        }

        [Test]
        public void OccupiedTopSocket_CannotReceiveAgain()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var upper = SocketTestRig.Placed(_spawned, Vector3.up * 10f);

            var top = SocketTestRig.TopSocketOf(lower);
            SocketTestRig.BaseSocketOf(upper).Connect(top);

            Assert.IsTrue(top.IsOccupied);
            Assert.IsFalse(top.CanReceive);
        }

        [Test]
        public void Connect_MarksBothSides()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var upper = SocketTestRig.Placed(_spawned, Vector3.up * 10f);

            var top = SocketTestRig.TopSocketOf(lower);
            var bottom = SocketTestRig.BaseSocketOf(upper);

            bottom.Connect(top);

            Assert.AreSame(top, bottom.ConnectedTo);
            Assert.AreSame(bottom, top.ConnectedTo);
        }

        [Test]
        public void Disconnect_ClearsBothSides()
        {
            var lower = SocketTestRig.Placed(_spawned, Vector3.zero);
            var upper = SocketTestRig.Placed(_spawned, Vector3.up * 10f);

            var top = SocketTestRig.TopSocketOf(lower);
            var bottom = SocketTestRig.BaseSocketOf(upper);

            bottom.Connect(top);
            bottom.Disconnect();

            Assert.IsFalse(bottom.IsOccupied);
            Assert.IsFalse(top.IsOccupied);
            Assert.IsTrue(top.CanReceive, "풀린 자리에는 다시 쌓을 수 있어야 한다.");
        }

        [Test]
        public void SocketFacingConvention_BasePointsDown_TopPointsUp()
        {
            var part = SocketTestRig.CreatePart(_spawned, Vector3.zero);

            Assert.Less(SocketTestRig.BaseSocketOf(part).transform.forward.y, -0.99f);
            Assert.Greater(SocketTestRig.TopSocketOf(part).transform.forward.y, 0.99f);
        }
    }
}
