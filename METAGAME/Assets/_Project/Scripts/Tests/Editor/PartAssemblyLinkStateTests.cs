using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 조립 연결을 읽어서 다시 세우는 것을 검증한다 (S15P21B202-388).
    ///
    /// <b>왜 이것이 필요한가.</b> 참가자 화면의 부품은 <c>isKinematic</c> 으로 서버가 보낸
    /// 자세만 따라가고 <b>관절이 하나도 없다</b>. 보기에는 이어붙은 탑인데 실제로는 나란히
    /// 놓인 부품들이라, 누군가 이어받아 물리를 켜는 순간 무너진다.
    ///
    /// <b>보내는 것은 "무엇이 무엇에 붙었나" 뿐이다.</b> 용접 강도는 싣지 않는다 —
    /// S15P21B202-230 이후 강도의 출처는 비드 하나뿐이고 비드는 이미 복제된다
    /// (S15P21B202-386). 접합을 세우면 <c>Bind</c> 가 비드에게 직접 묻는다.
    ///
    /// <b>부품은 번호로 가리킨다.</b> 부르는 쪽이 양쪽에서 같은 순서인 목록을 주고 그
    /// 안의 자리를 적는다 — 그래서 "목록에 없는 부품" 과 "범위를 벗어난 번호" 가 이
    /// 검증의 핵심이다. 잘못 다루면 엉뚱한 두 부품이 이어지거나 남의 접합이 사라진다.
    /// </summary>
    public sealed class PartAssemblyLinkStateTests
    {
        /// <summary>
        /// 다른 검증과 멀리 떨어뜨린다. S15P21B202-329 에서 원점에 만든 오브젝트가 남의
        /// 물리 검증을 깨뜨린 적이 있다.
        /// </summary>
        static readonly Vector3 Far = new Vector3(0f, 0f, 700f);

        readonly List<GameObject> _spawned = new();

        SimulationMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();

            _previousMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _previousMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            WeldBead.All.Clear();
            PartAssemblyLink.All.Clear();
        }

        // --- 읽기 -----------------------------------------------------------

        /// <summary>소켓으로 맞물린 접합은 부품 번호와 소켓 자리까지 그대로 읽힌다.</summary>
        [Test]
        public void ReadAll_RecordsPartAndSocketIndexes()
        {
            var (lower, upper) = Stack();
            var link = PartAssemblyLink.FindBetween(lower, upper);
            Assert.IsNotNull(link, "조립이 접합을 만들지 않았다 — 이 검증의 전제가 깨졌다.");

            var parts = new List<SpaceshipPart> { lower, upper };
            var read = new List<PartAssemblyLink.LinkState>();
            PartAssemblyLink.ReadAll(parts, read);

            Assert.That(read.Count, Is.EqualTo(1), "접합 하나가 읽혀야 한다.");

            var state = read[0];
            Assert.That(state.MovingPart, Is.EqualTo(1), "조인트를 가진 쪽은 위에 올린 부품이다.");
            Assert.That(state.TargetPart, Is.EqualTo(0));

            Assert.That(IndexOfSocket(upper, link.MovingSocket), Is.EqualTo(state.MovingSocket),
                "읽어 온 소켓 자리가 실제 소켓과 다르다.");
            Assert.That(IndexOfSocket(lower, link.TargetSocket), Is.EqualTo(state.TargetSocket));
            Assert.That(state.MovingSocket, Is.GreaterThanOrEqualTo(0), "소켓 접합인데 -1 로 읽혔다.");
        }

        /// <summary>비드로 이은 접합에는 소켓이 없다 — 그것을 -1 로 표시한다.</summary>
        [Test]
        public void ReadAll_MarksBeadWeldLinksWithoutSockets()
        {
            var a = SocketTestRig.Placed(_spawned, Far);
            var b = SocketTestRig.Placed(_spawned, Far + new Vector3(PhysicsScale.PartWidth * 2f, 0f, 0f));

            Assert.IsNotNull(PartAssemblyLink.Rebuild(a, b, null, null), "비드 접합을 만들지 못했다.");

            var read = new List<PartAssemblyLink.LinkState>();
            PartAssemblyLink.ReadAll(new List<SpaceshipPart> { a, b }, read);

            Assert.That(read.Count, Is.EqualTo(1));
            Assert.That(read[0].MovingSocket, Is.EqualTo(-1), "비드 접합에 소켓 자리가 실렸다.");
            Assert.That(read[0].TargetSocket, Is.EqualTo(-1));
        }

        /// <summary>
        /// 목록에 없는 부품이 끼어 있는 접합은 건너뛴다.
        ///
        /// 번호로 가리킬 수 없으므로 실어 보낼 방법이 없다. 억지로 0 을 넣으면 받는 쪽이
        /// 엉뚱한 부품을 잇는다.
        /// </summary>
        [Test]
        public void ReadAll_SkipsLinksWhosePartIsNotListed()
        {
            var (lower, upper) = Stack();

            var read = new List<PartAssemblyLink.LinkState>();
            PartAssemblyLink.ReadAll(new List<SpaceshipPart> { lower }, read);

            Assert.That(read.Count, Is.EqualTo(0), "위 부품이 목록에 없는데도 접합이 읽혔다.");
        }

        /// <summary>
        /// 번호 순으로 줄 세운다.
        ///
        /// <c>All</c> 의 순서는 접합이 생긴 순서라 기계마다 다를 수 있다. 그대로 두면 내용이
        /// 같은데도 "바뀌었다" 로 보여, 보낼 이유가 없는 것을 주기마다 계속 보낸다.
        /// </summary>
        [Test]
        public void ReadAll_IsOrderedByPartIndex()
        {
            var (lower, upper) = Stack();
            var third = SocketTestRig.Placed(_spawned, Far + new Vector3(PhysicsScale.PartWidth * 2f, 0f, 0f));
            Assert.IsNotNull(PartAssemblyLink.Rebuild(third, lower, null, null));

            // 목록에서 위 부품을 먼저 둔다 — 접합이 생긴 순서와 번호 순서를 어긋나게 만든다.
            var parts = new List<SpaceshipPart> { third, lower, upper };
            var read = new List<PartAssemblyLink.LinkState>();
            PartAssemblyLink.ReadAll(parts, read);

            Assert.That(read.Count, Is.EqualTo(2));
            Assert.That(read[0].MovingPart, Is.LessThanOrEqualTo(read[1].MovingPart),
                "번호 순으로 줄 세우지 않았다 — 같은 내용이 매번 달라 보인다.");
        }

        // --- 다시 세우기 ----------------------------------------------------

        /// <summary>없는 접합을 만든다 — 늦게 들어온 사람과 이어받는 사람이 보는 것이 이것이다.</summary>
        [Test]
        public void RestoreAll_BuildsTheMissingSocketLink()
        {
            var (lower, upper) = Stack();
            var parts = new List<SpaceshipPart> { lower, upper };

            var snapshot = ReadArray(parts);

            // 접합을 없앤 뒤 스냅샷만으로 되살린다.
            PartAssemblyLink.FindBetween(lower, upper).Release();
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper), "전제: 접합이 풀려 있어야 한다.");

            PartAssemblyLink.RestoreAll(parts, snapshot, snapshot.Length);

            var rebuilt = PartAssemblyLink.FindBetween(lower, upper);
            Assert.IsNotNull(rebuilt, "접합이 다시 세워지지 않았다.");
            Assert.IsTrue(rebuilt.IsIntact, "조인트가 없다 — 물리를 이어받으면 무너진다.");
            Assert.IsTrue(rebuilt.HasSockets, "소켓 접합으로 세워지지 않았다.");
            Assert.That(rebuilt.MovingPart, Is.SameAs(upper), "조인트를 가진 쪽이 뒤집혔다.");
            Assert.IsTrue(rebuilt.MovingSocket.IsOccupied, "소켓 맞물림 표시가 없다.");
        }

        /// <summary>소켓 자리가 -1 이면 비드 접합으로 세운다.</summary>
        [Test]
        public void RestoreAll_BuildsABeadWeldLinkWhenSocketsAreAbsent()
        {
            var a = SocketTestRig.Placed(_spawned, Far);
            var b = SocketTestRig.Placed(_spawned, Far + new Vector3(PhysicsScale.PartWidth * 2f, 0f, 0f));
            var parts = new List<SpaceshipPart> { a, b };

            var snapshot = new[]
            {
                new PartAssemblyLink.LinkState
                {
                    MovingPart = 0, TargetPart = 1, MovingSocket = -1, TargetSocket = -1,
                },
            };

            PartAssemblyLink.RestoreAll(parts, snapshot, snapshot.Length);

            var link = PartAssemblyLink.FindBetween(a, b);
            Assert.IsNotNull(link, "비드 접합이 세워지지 않았다.");
            Assert.IsFalse(link.HasSockets, "소켓 없는 접합인데 소켓이 붙었다.");
            Assert.IsTrue(link.IsIntact);
        }

        /// <summary>
        /// 이미 있는 접합 위에 하나 더 만들지 않는다.
        ///
        /// 주기마다 같은 스냅샷이 오는데 매번 새로 세우면 조인트가 계속 생겨 강도가 배로
        /// 오르고, 그 순간마다 물리가 흔들린다.
        /// </summary>
        [Test]
        public void RestoreAll_DoesNotDuplicateAnExistingLink()
        {
            var (lower, upper) = Stack();
            var parts = new List<SpaceshipPart> { lower, upper };
            var snapshot = ReadArray(parts);

            int before = PartAssemblyLink.All.Count;

            PartAssemblyLink.RestoreAll(parts, snapshot, snapshot.Length);
            PartAssemblyLink.RestoreAll(parts, snapshot, snapshot.Length);

            Assert.That(PartAssemblyLink.All.Count, Is.EqualTo(before),
                "같은 스냅샷을 두 번 넣었더니 접합이 늘었다.");
            Assert.That(upper.GetComponents<FixedJoint>().Length, Is.EqualTo(1),
                "조인트가 겹쳐 붙었다 — 강도가 배로 오른다.");
        }

        /// <summary>서버에서 끊어진 접합은 여기서도 풀린다.</summary>
        [Test]
        public void RestoreAll_ReleasesALinkThatIsNoLongerInTheSnapshot()
        {
            var (lower, upper) = Stack();
            var parts = new List<SpaceshipPart> { lower, upper };

            PartAssemblyLink.RestoreAll(parts, new PartAssemblyLink.LinkState[0], 0);

            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper),
                "스냅샷에 없는 접합이 남아 있다 — 서버에서 끊어진 것이 여기서는 붙어 있다.");
        }

        /// <summary>
        /// 목록 밖의 부품이 끼어 있는 접합은 건드리지 않는다.
        ///
        /// <c>ReadAll</c> 이 그런 접합을 실어 보내지 못하므로, "명단에 없다" 를 "풀어야
        /// 한다" 로 읽으면 남이 이어 놓은 것을 지운다.
        /// </summary>
        [Test]
        public void RestoreAll_LeavesLinksInvolvingUnlistedParts()
        {
            var (lower, upper) = Stack();
            var outsider = SocketTestRig.Placed(_spawned, Far + new Vector3(PhysicsScale.PartWidth * 2f, 0f, 0f));
            Assert.IsNotNull(PartAssemblyLink.Rebuild(outsider, lower, null, null));

            // 명단에는 아래·위 부품만 넣는다. 스냅샷은 비어 있다.
            PartAssemblyLink.RestoreAll(new List<SpaceshipPart> { lower, upper },
                                       new PartAssemblyLink.LinkState[0], 0);

            Assert.IsNotNull(PartAssemblyLink.FindBetween(outsider, lower),
                "명단 밖 부품의 접합이 풀렸다.");
            Assert.IsNull(PartAssemblyLink.FindBetween(lower, upper),
                "명단 안 접합은 스냅샷대로 풀려야 한다.");
        }

        /// <summary>범위를 벗어난 번호에도 죽지 않는다 — 개수가 어긋난 순간이 있다.</summary>
        [Test]
        public void RestoreAll_SurvivesOutOfRangeIndexes()
        {
            var a = SocketTestRig.Placed(_spawned, Far);
            var parts = new List<SpaceshipPart> { a };

            var junk = new[]
            {
                new PartAssemblyLink.LinkState { MovingPart = 5, TargetPart = 0, MovingSocket = -1, TargetSocket = -1 },
                new PartAssemblyLink.LinkState { MovingPart = 0, TargetPart = -3, MovingSocket = 9, TargetSocket = 9 },
                new PartAssemblyLink.LinkState { MovingPart = 0, TargetPart = 0, MovingSocket = -1, TargetSocket = -1 },
            };

            Assert.DoesNotThrow(() => PartAssemblyLink.RestoreAll(parts, junk, junk.Length));
            Assert.That(PartAssemblyLink.All.Count, Is.EqualTo(0), "쓰레기 번호로 접합이 생겼다.");
        }

        /// <summary>읽어서 다시 세우면 같은 짝이 나온다.</summary>
        [Test]
        public void RoundTrip_RebuildsTheSamePairing()
        {
            var (lower, upper) = Stack();
            var parts = new List<SpaceshipPart> { lower, upper };

            var before = ReadArray(parts);
            PartAssemblyLink.FindBetween(lower, upper).Release();
            PartAssemblyLink.RestoreAll(parts, before, before.Length);
            var after = ReadArray(parts);

            Assert.That(after.Length, Is.EqualTo(before.Length));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(after[i].MovingPart, Is.EqualTo(before[i].MovingPart));
                Assert.That(after[i].TargetPart, Is.EqualTo(before[i].TargetPart));
                Assert.That(after[i].MovingSocket, Is.EqualTo(before[i].MovingSocket));
                Assert.That(after[i].TargetSocket, Is.EqualTo(before[i].TargetSocket));
            }
        }

        // --- 도구 -----------------------------------------------------------

        /// <summary>아래 하나에 위 하나를 실제로 조립해 소켓 접합을 만든다.</summary>
        (SpaceshipPart lower, SpaceshipPart upper) Stack()
        {
            var lower = SocketTestRig.Placed(_spawned, Far);

            var upper = SocketTestRig.Freed(_spawned,
                Far + new Vector3(0f, PhysicsScale.PartHeight + SocketTestRig.StackGap, 0f));
            upper.TryTransitionTo(PartState.Carried);
            UnityEngine.Physics.SyncTransforms();

            var match = PartAssembler.FindMatch(upper);
            Assert.IsTrue(match.HasValue, "전제: 붙을 수 있는 자리여야 한다.");
            Assert.IsNotNull(PartAssembler.Assemble(match), "전제: 조립이 성공해야 한다.");

            return (lower, upper);
        }

        static PartAssemblyLink.LinkState[] ReadArray(IReadOnlyList<SpaceshipPart> parts)
        {
            var read = new List<PartAssemblyLink.LinkState>();
            PartAssemblyLink.ReadAll(parts, read);
            return read.ToArray();
        }

        static int IndexOfSocket(SpaceshipPart part, PartSocket socket)
        {
            var sockets = part.Sockets;
            for (int i = 0; i < sockets.Count; i++)
                if (ReferenceEquals(sockets[i], socket)) return i;

            return -1;
        }
    }
}
