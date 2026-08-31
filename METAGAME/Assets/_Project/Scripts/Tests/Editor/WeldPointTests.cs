using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 용접 진행도 누적, 여러 명의 인계, 완료 비율에 따른 조인트 강화를 검증한다.
    ///
    /// 용접은 한 번에 끝까지 누르는 동작이 아니다. 중간에 놓아도 채운 만큼 남고
    /// 다른 플레이어가 이어서 채울 수 있어야 한다. 그 누적이 이 파일의 주된 관심사다.
    /// </summary>
    public sealed class WeldPointTests
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

        // --- 진행도 누적 -----------------------------------------------------

        [Test]
        public void FreshWeldPoint_StartsEmptyAndNeedsFullTime()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];

            Assert.AreEqual(0f, point.Progress);
            Assert.IsFalse(point.IsComplete);
            Assert.AreEqual(PhysicsScale.WeldSecondsPerPoint, point.HoldSeconds, 0.001f);
        }

        [Test]
        public void WeldPoint_CompletesAfterFullHold()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];
            var interactor = CreateInteractor(point);

            Assert.IsTrue(interactor.TryBegin());
            interactor.Tick(PhysicsScale.WeldSecondsPerPoint);

            Assert.IsTrue(point.IsComplete);
            Assert.AreEqual(1f, point.Progress, 0.001f);
        }

        [Test]
        public void CancelledHold_KeepsPartialProgress()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];
            var interactor = CreateInteractor(point);

            interactor.TryBegin();
            interactor.Tick(PhysicsScale.WeldSecondsPerPoint * 0.4f);
            interactor.Cancel();

            Assert.AreEqual(0.4f, point.Progress, 0.01f,
                "중간에 놓아도 채운 만큼은 남아야 한다.");
            Assert.IsFalse(point.IsComplete);
        }

        [Test]
        public void PartialProgress_ShortensRemainingHoldTime()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];
            var interactor = CreateInteractor(point);

            interactor.TryBegin();
            interactor.Tick(PhysicsScale.WeldSecondsPerPoint * 0.5f);
            interactor.Cancel();

            Assert.AreEqual(PhysicsScale.WeldSecondsPerPoint * 0.5f, point.HoldSeconds, 0.01f,
                "절반 채운 용접점은 절반만 더 누르면 끝나야 한다.");
        }

        [Test]
        public void SecondPlayer_CanFinishWhatFirstPlayerStarted()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];

            var first = CreateInteractor(point);
            first.TryBegin();
            first.Tick(PhysicsScale.WeldSecondsPerPoint * 0.6f);
            first.Cancel();

            Assert.IsFalse(point.IsComplete);

            var second = CreateInteractor(point);
            Assert.IsTrue(second.TryBegin(), "앞사람이 놓았으면 다른 사람이 이어받을 수 있어야 한다.");
            second.Tick(PhysicsScale.WeldSecondsPerPoint * 0.4f + 0.05f);

            Assert.IsTrue(point.IsComplete, "두 사람이 나눠 채워도 완료되어야 한다.");
        }

        [Test]
        public void CompletedWeldPoint_IsNoLongerInteractable()
        {
            var (_, _, points) = AssembledPair(1);
            var point = points[0];
            var interactor = CreateInteractor(point);

            interactor.TryBegin();
            interactor.Tick(PhysicsScale.WeldSecondsPerPoint);

            var availability = point.GetAvailability(interactor);
            Assert.IsFalse(availability.IsAllowed);
            Assert.AreEqual(InteractionFailure.NotReady, availability.Failure);

            foreach (var c in point.GetComponents<Collider>())
                Assert.IsFalse(c.enabled, "완료된 용접점이 계속 포커스를 뺏으면 안 된다.");
        }

        [Test]
        public void WeldPoint_OnUnassembledPart_IsNotAvailable()
        {
            var part = SocketTestRig.Freed(_spawned, Vector3.zero);
            var point = AddWeldPoint(part, 0);
            point.Initialize();

            var interactor = CreateInteractor(point);

            Assert.IsFalse(point.GetAvailability(interactor).IsAllowed,
                "붙어 있지 않은 파츠에는 용접할 것이 없다.");
        }

        [Test]
        public void WeldProgress_SurvivesSerialization()
        {
            // 씬이나 프리팹에 저장된 용접 상태가 로드 후에도 남아야 한다.
            // 저장되지 않으면 파츠는 Welded 인데 진행도는 0 으로 보여,
            // 조인트 강도가 미접합 값으로 되돌아가 용접한 탑이 충격에 떨어진다.
            var (_, _, points) = AssembledPair(1);
            var source = points[0];
            CompleteWeld(source);

            var host = new GameObject("SerializationTarget");
            _spawned.Add(host);
            var copy = host.AddComponent<WeldPoint>();

            UnityEditor.EditorUtility.CopySerialized(source, copy);

            Assert.AreEqual(1f, copy.Progress, 0.001f,
                "용접 진행도가 직렬화되지 않으면 저장된 용접이 사라진다.");
        }

        // --- 조인트 강화 -----------------------------------------------------

        [Test]
        public void UnweldedJoint_UsesWeakBreakForce()
        {
            var (_, link, _) = AssembledPair(4);
            link.Tick();

            Assert.AreEqual(0f, link.WeldRatio, 0.001f);
            Assert.AreEqual(PhysicsScale.UnweldedBreakForce,
                link.MovingSocket.Owner.GetComponent<FixedJoint>().breakForce, 1f);
        }

        [Test]
        public void HalfWeldedJoint_UsesInterpolatedBreakForce()
        {
            var (part, link, points) = AssembledPair(4);

            CompleteWeld(points[0]);
            CompleteWeld(points[1]);
            link.Tick();

            Assert.AreEqual(0.5f, link.WeldRatio, 0.001f);

            var expected = Mathf.Lerp(
                PhysicsScale.UnweldedBreakForce, PhysicsScale.WeldedBreakForce, 0.5f);
            Assert.AreEqual(expected, part.GetComponent<FixedJoint>().breakForce, 1f,
                "절반만 용접했으면 절반만 튼튼해야 한다.");
            Assert.AreEqual(PartState.Placed, part.State,
                "아직 다 채우지 않았으면 Welded 가 되어서는 안 된다.");
        }

        [Test]
        public void FullyWeldedJoint_PromotesPartAndUsesStrongBreakForce()
        {
            var (part, link, points) = AssembledPair(4);

            PartAssemblyLink welded = null;
            link.Welded += l => welded = l;

            foreach (var point in points) CompleteWeld(point);
            link.Tick();

            Assert.IsTrue(link.IsFullyWelded);
            Assert.AreEqual(PartState.Welded, part.State);
            Assert.AreSame(link, welded, "용접 완료 이벤트가 와야 한다.");
            Assert.AreEqual(PhysicsScale.WeldedBreakForce,
                part.GetComponent<FixedJoint>().breakForce, 1f);
        }

        [Test]
        public void WeldedPart_CannotBeCarriedAgain()
        {
            var (part, link, points) = AssembledPair(2);

            foreach (var point in points) CompleteWeld(point);
            link.Tick();

            Assert.AreEqual(PartState.Welded, part.State);
            Assert.IsFalse(part.TryTransitionTo(PartState.Carried),
                "용접된 파츠는 다시 들 수 없다.");
        }

        [Test]
        public void JointBreak_ResetsWeldProgress()
        {
            var (part, link, points) = AssembledPair(2);

            CompleteWeld(points[0]);
            link.Tick();
            Assert.AreEqual(0.5f, link.WeldRatio, 0.001f);

            Object.DestroyImmediate(part.GetComponent<FixedJoint>());
            link.Tick();

            Assert.AreEqual(0f, points[0].Progress, 0.001f,
                "접합이 풀렸으면 용접도 없던 일이 되어야 한다.");
            foreach (var c in points[0].GetComponents<Collider>())
                Assert.IsTrue(c.enabled, "다시 용접할 수 있어야 한다.");
        }

        [Test]
        public void WeldRatio_IsZero_WhenJointHasNoWeldPoints()
        {
            var (_, link, _) = AssembledPair(0);

            Assert.AreEqual(0f, link.WeldRatio);
            Assert.IsFalse(link.IsFullyWelded,
                "용접점이 없는 접합이 저절로 완성되면 안 된다.");
        }

        // --- 헬퍼 ------------------------------------------------------------

        /// <summary>아래 파츠 위에 조립된 위 파츠와 그 링크, 용접점들을 만든다.</summary>
        (SpaceshipPart Part, PartAssemblyLink Link, WeldPoint[] Points) AssembledPair(int weldPointCount)
        {
            SocketTestRig.Placed(_spawned, Vector3.zero);

            var moving = SocketTestRig.Freed(
                _spawned, Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));

            var points = new WeldPoint[weldPointCount];
            for (var i = 0; i < weldPointCount; i++) points[i] = AddWeldPoint(moving, i);

            moving.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(moving));
            Assert.IsNotNull(link, "조립이 되어야 용접 검증을 할 수 있다.");
            link.RefreshWeldPoints();

            return (moving, link, points);
        }

        static WeldPoint AddWeldPoint(SpaceshipPart part, int index)
        {
            var go = new GameObject($"Weld_{index}") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = new Vector3(index * 0.2f - 0.3f, -0.5f, 0.5f);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.1f;
            sphere.isTrigger = true;

            return go.AddComponent<WeldPoint>();
        }

        /// <summary>Interactor 를 거쳐 용접점을 끝까지 채운다.</summary>
        void CompleteWeld(WeldPoint point)
        {
            var interactor = CreateInteractor(point);
            Assert.IsTrue(interactor.TryBegin(), "용접을 시작할 수 있어야 한다.");
            interactor.Tick(PhysicsScale.WeldSecondsPerPoint + 0.05f);
            Assert.IsTrue(point.IsComplete);
        }

        /// <summary>용접점을 정면에서 바라보는 Interactor.</summary>
        Interactor CreateInteractor(WeldPoint point)
        {
            var position = point.transform.position - Vector3.forward * 1.5f;

            var go = new GameObject("Interactor");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.forward));

            var interactor = go.AddComponent<Interactor>();
            interactor.Configure(go.transform, 3f, 80f);

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return interactor;
        }
    }
}
