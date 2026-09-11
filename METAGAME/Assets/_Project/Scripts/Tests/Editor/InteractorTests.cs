using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// Interactor 의 탐지, 포커스 선정, 홀드, 취소, 점유 규칙을 검증한다.
    ///
    /// Interactor 가 Time.deltaTime 을 직접 읽지 않고 Tick(deltaTime) 을 받기 때문에
    /// PlayMode 없이 EditMode 에서 시간을 직접 넣어 검증할 수 있다.
    /// </summary>
    public sealed class InteractorTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        // --- 탐지 및 포커스 -------------------------------------------------

        [Test]
        public void Focus_IsNull_WhenNothingIsInRange()
        {
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.Tick(0.02f);

            Assert.IsNull(interactor.Focus);
            Assert.AreEqual(InteractorState.Idle, interactor.State);
        }

        [Test]
        public void Focus_FindsTargetInFront()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f));
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.Tick(0.02f);

            Assert.AreSame(target, interactor.Focus);
            Assert.AreEqual(InteractorState.Focused, interactor.State);
            Assert.AreEqual(1, target.FocusEnterCount);
        }

        [Test]
        public void Focus_IgnoresTargetBeyondRange()
        {
            CreateInteractable(new Vector3(0f, 0f, 10f));
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward, range: 3f);

            interactor.Tick(0.02f);

            Assert.IsNull(interactor.Focus);
        }

        [Test]
        public void Focus_IgnoresTargetOutsideViewAngle()
        {
            CreateInteractable(new Vector3(0f, 0f, -2f));   // 정확히 등 뒤
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward, halfAngle: 60f);

            interactor.Tick(0.02f);

            Assert.IsNull(interactor.Focus);
        }

        [Test]
        public void Focus_PrefersSmallerAngleOverCloserDistance()
        {
            // 정면이지만 먼 대상과, 가깝지만 비스듬한 대상. 정면이 이겨야 한다.
            var straightAhead = CreateInteractable(new Vector3(0f, 0f, 2.5f));
            CreateInteractable(new Vector3(0.7f, 0f, 0.7f));

            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.Tick(0.02f);

            Assert.AreSame(straightAhead, interactor.Focus);
        }

        [Test]
        public void FocusChanged_ReportsPreviousAndCurrent()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f));
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            IInteractable seenPrevious = null;
            IInteractable seenCurrent = null;
            var raised = 0;
            interactor.FocusChanged += (previous, current) =>
            {
                seenPrevious = previous;
                seenCurrent = current;
                raised++;
            };

            interactor.Tick(0.02f);
            Assert.AreEqual(1, raised);
            Assert.IsNull(seenPrevious);
            Assert.AreSame(target, seenCurrent);

            Move(target.gameObject, new Vector3(0f, 0f, 10f));
            interactor.Tick(0.02f);

            Assert.AreEqual(2, raised);
            Assert.AreSame(target, seenPrevious);
            Assert.IsNull(seenCurrent);
            Assert.AreEqual(1, target.FocusExitCount);
        }

        [Test]
        public void Focus_IsNotRaisedAgain_WhenTargetIsUnchanged()
        {
            CreateInteractable(new Vector3(0f, 0f, 2f));
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            var raised = 0;
            interactor.FocusChanged += (_, _) => raised++;

            interactor.Tick(0.02f);
            interactor.Tick(0.02f);
            interactor.Tick(0.02f);

            Assert.AreEqual(1, raised);
        }

        // --- 시작과 홀드 ----------------------------------------------------

        [Test]
        public void TryBegin_FailsWithNoTarget_WhenNothingIsFocused()
        {
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            IInteractable failedTarget = null;
            var reason = InteractionFailure.None;
            interactor.Failed += (target, failure) =>
            {
                failedTarget = target;
                reason = failure;
            };

            Assert.IsFalse(interactor.TryBegin());
            Assert.IsNull(failedTarget);
            Assert.AreEqual(InteractionFailure.NoTarget, reason);
        }

        [Test]
        public void TryBegin_CompletesImmediately_WhenHoldSecondsIsZero()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 0f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            IInteractable completed = null;
            interactor.Completed += t => completed = t;

            Assert.IsTrue(interactor.TryBegin());
            Assert.AreSame(target, completed);
            Assert.AreEqual(1, target.CompletedCount);
            Assert.AreEqual(InteractorState.Idle, interactor.State);
            Assert.IsNull(target.Occupant, "완료 후에는 점유가 풀려야 한다.");
        }

        [Test]
        public void TryBegin_FailsWithReason_WhenTargetIsNotReady()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f));
            target.IsBlocked = true;
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            var reason = InteractionFailure.None;
            interactor.Failed += (_, failure) => reason = failure;

            Assert.IsFalse(interactor.TryBegin());
            Assert.AreEqual(InteractionFailure.NotReady, reason);
            Assert.AreEqual(0, target.CompletedCount);
        }

        [Test]
        public void Hold_CompletesAfterHoldSeconds()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            Assert.IsTrue(interactor.TryBegin());
            Assert.AreEqual(InteractorState.Holding, interactor.State);

            for (var i = 0; i < 25; i++) interactor.Tick(0.02f);   // 0.5초
            Assert.AreEqual(0, target.CompletedCount, "절반만 눌렀는데 완료되면 안 된다.");
            Assert.AreEqual(0.5f, interactor.HoldProgress, 0.01f);

            for (var i = 0; i < 25; i++) interactor.Tick(0.02f);   // 1.0초
            Assert.AreEqual(1, target.CompletedCount);
            Assert.AreEqual(InteractorState.Idle, interactor.State);
        }

        [Test]
        public void Hold_ProgressIsClampedToOne()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 0.5f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            var maxReported = 0f;
            interactor.HoldProgressChanged += (_, progress) => maxReported = Mathf.Max(maxReported, progress);

            interactor.TryBegin();
            interactor.Tick(10f);   // 홀드 시간을 크게 초과하는 한 프레임

            Assert.AreEqual(1f, maxReported, 0.0001f);
            Assert.AreEqual(1, target.CompletedCount);
        }

        [Test]
        public void Hold_CancelsWhenTargetLeavesRange()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward, range: 3f);

            IInteractable canceled = null;
            interactor.Canceled += t => canceled = t;

            interactor.TryBegin();
            interactor.Tick(0.3f);
            Assert.Greater(interactor.HoldProgress, 0f);

            Move(target.gameObject, new Vector3(0f, 0f, 20f));
            interactor.Tick(0.02f);

            Assert.AreSame(target, canceled);
            Assert.AreEqual(0f, interactor.HoldProgress);
            Assert.AreEqual(InteractorState.Idle, interactor.State);
            Assert.AreEqual(0, target.CompletedCount);
            Assert.IsNull(target.Occupant, "취소 후에는 점유가 풀려야 한다.");
        }

        [Test]
        public void Hold_CancelsWhenAvailabilityIsRevoked()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.TryBegin();
            interactor.Tick(0.3f);

            target.IsBlocked = true;   // 홀드 도중 조건이 깨진다
            interactor.Tick(0.02f);

            Assert.AreEqual(InteractorState.Idle, interactor.State);
            Assert.AreEqual(0f, interactor.HoldProgress);
            Assert.AreEqual(0, target.CompletedCount);
        }

        [Test]
        public void Cancel_ReleasesOccupancyAndResetsProgress()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.TryBegin();
            interactor.Tick(0.4f);
            interactor.Cancel();

            Assert.AreEqual(InteractorState.Idle, interactor.State);
            Assert.AreEqual(0f, interactor.HoldProgress);
            Assert.IsNull(target.Occupant);
        }

        [Test]
        public void TryBegin_FailsWithBusy_WhileAlreadyHolding()
        {
            CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.TryBegin();

            var reason = InteractionFailure.None;
            interactor.Failed += (_, failure) => reason = failure;

            Assert.IsFalse(interactor.TryBegin());
            Assert.AreEqual(InteractionFailure.Busy, reason);
        }

        // --- 점유 ------------------------------------------------------------

        [Test]
        public void Occupancy_BlocksSecondInteractor()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var first = CreateInteractor(Vector3.zero, Vector3.forward);
            var second = CreateInteractor(new Vector3(0.5f, 0f, 0f), Vector3.forward);

            Assert.IsTrue(first.TryBegin());
            Assert.AreSame(first, target.Occupant);

            var reason = InteractionFailure.None;
            second.Failed += (_, failure) => reason = failure;

            Assert.IsFalse(second.TryBegin());
            Assert.AreEqual(InteractionFailure.Occupied, reason);
            Assert.AreSame(first, target.Occupant, "점유자가 바뀌면 안 된다.");
        }

        [Test]
        public void Occupancy_IsHandedOver_AfterFirstInteractorCompletes()
        {
            var target = CreateInteractable(new Vector3(0f, 0f, 2f), holdSeconds: 1f);
            var first = CreateInteractor(Vector3.zero, Vector3.forward);
            var second = CreateInteractor(new Vector3(0.5f, 0f, 0f), Vector3.forward);

            first.TryBegin();
            first.Tick(1f);
            Assert.AreEqual(1, target.CompletedCount);

            Assert.IsTrue(second.TryBegin(), "앞사람이 끝냈으면 다음 사람이 쓸 수 있어야 한다.");
            Assert.AreSame(second, target.Occupant);
        }

        // --- 헬퍼 ------------------------------------------------------------

        TestInteractable CreateInteractable(Vector3 position, float holdSeconds = 0f)
        {
            var go = new GameObject("Interactable")
            {
                layer = GameLayers.Interactable,
            };
            go.transform.position = position;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            sphere.isTrigger = true;

            var interactable = go.AddComponent<TestInteractable>();
            interactable.SetHoldSeconds(holdSeconds);

            _spawned.Add(go);
            Physics.SyncTransforms();
            return interactable;
        }

        Interactor CreateInteractor(Vector3 position, Vector3 forward, float range = 3f, float halfAngle = 60f)
        {
            var go = new GameObject("Interactor");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));

            var interactor = go.AddComponent<Interactor>();
            interactor.Configure(go.transform, range, halfAngle);

            _spawned.Add(go);
            Physics.SyncTransforms();
            return interactor;
        }

        /// <summary>
        /// autoSyncTransforms 가 꺼져 있으므로 오브젝트를 옮긴 뒤 물리 쿼리를 하려면 직접 동기화해야 한다.
        /// </summary>
        static void Move(GameObject go, Vector3 position)
        {
            go.transform.position = position;
            Physics.SyncTransforms();
        }

        sealed class TestInteractable : InteractableBase
        {
            public bool IsBlocked;
            public int CompletedCount;
            public int FocusEnterCount;
            public int FocusExitCount;

            public override InteractionAvailability GetAvailability(Interactor actor)
            {
                var occupancy = base.GetAvailability(actor);
                if (!occupancy.IsAllowed) return occupancy;

                return IsBlocked
                    ? InteractionAvailability.Denied(InteractionFailure.NotReady)
                    : InteractionAvailability.Allowed;
            }

            public override void OnFocusEnter(Interactor actor) => FocusEnterCount++;

            public override void OnFocusExit(Interactor actor) => FocusExitCount++;

            public override void OnCompleted(Interactor actor) => CompletedCount++;
        }
    }
}
