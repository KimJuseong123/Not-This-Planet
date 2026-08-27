using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 고정쇠 해제 흐름을 검증한다.
    /// 상호작용 프레임워크(S15P21B202-61)의 첫 구현체이므로
    /// Interactor 를 통한 실제 홀드 경로도 함께 확인한다.
    /// </summary>
    public sealed class PartClampTests
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

        [Test]
        public void Clamp_UsesDefaultHoldSeconds_WhenNotConfigured()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            Assert.AreEqual(PartClamp.DefaultHoldSeconds, clamp.HoldSeconds, 0.001f,
                "고정쇠는 탭이 아니라 꾹 눌러 푸는 상호작용이다.");
        }

        [Test]
        public void Release_MarksReleasedAndDisablesColliders()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            clamp.Release();

            Assert.IsTrue(clamp.IsReleased);
            foreach (var c in clamp.GetComponents<Collider>())
                Assert.IsFalse(c.enabled, "풀린 고정쇠가 계속 포커스를 뺏으면 안 된다.");
        }

        [Test]
        public void Release_IsIdempotent()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            var raised = 0;
            clamp.Released += _ => raised++;

            clamp.Release();
            clamp.Release();
            clamp.Release();

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void ReleasedClamp_ReportsNotReady()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            clamp.Release();

            var availability = clamp.GetAvailability(interactor);
            Assert.IsFalse(availability.IsAllowed);
            Assert.AreEqual(InteractionFailure.NotReady, availability.Failure);
        }

        [Test]
        public void ReleasingAllRequiredClamps_UnlocksPart()
        {
            var part = CreatePart();
            var first = AddClamp(part, Vector3.forward * 2f);
            var second = AddClamp(part, Vector3.right * 2f);

            first.Release();
            Assert.AreEqual(PartState.Locked, part.State, "하나 남았는데 풀리면 안 된다.");
            Assert.AreEqual(1, part.RemainingRequiredClamps);

            second.Release();
            Assert.AreEqual(PartState.Free, part.State);
            Assert.AreEqual(0, part.RemainingRequiredClamps);
            Assert.IsFalse(part.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void OptionalClamp_DoesNotBlockUnlock()
        {
            var part = CreatePart();
            var required = AddClamp(part, Vector3.forward * 2f);
            AddClamp(part, Vector3.right * 2f, required: false);

            Assert.AreEqual(1, part.RemainingRequiredClamps);

            required.Release();

            Assert.AreEqual(PartState.Free, part.State);
        }

        [Test]
        public void Interactor_ReleasesClampAfterHold()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.Tick(0.02f);
            Assert.AreSame(clamp, interactor.Focus, "고정쇠가 Interactable 레이어에서 탐지되어야 한다.");

            Assert.IsTrue(interactor.TryBegin());
            Assert.AreEqual(InteractorState.Holding, interactor.State);

            interactor.Tick(PartClamp.DefaultHoldSeconds * 0.5f);
            Assert.IsFalse(clamp.IsReleased, "절반만 눌렀는데 풀리면 안 된다.");

            interactor.Tick(PartClamp.DefaultHoldSeconds * 0.5f);
            Assert.IsTrue(clamp.IsReleased);
            Assert.AreEqual(PartState.Free, part.State);
        }

        [Test]
        public void Interactor_CancelledHold_LeavesClampAttached()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.TryBegin();
            interactor.Tick(PartClamp.DefaultHoldSeconds * 0.8f);
            interactor.Cancel();

            Assert.IsFalse(clamp.IsReleased);
            Assert.AreEqual(PartState.Locked, part.State);
            Assert.IsNull(clamp.Occupant, "취소하면 점유가 풀려 다른 사람이 시도할 수 있어야 한다.");
        }

        [Test]
        public void Interactor_DoesNotFocusReleasedClamp()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            clamp.Release();
            Physics.SyncTransforms();

            interactor.Tick(0.02f);

            Assert.IsNull(interactor.Focus);
        }

        // --- 헬퍼 ---

        SpaceshipPart CreatePart()
        {
            var go = new GameObject("Part");
            go.AddComponent<BoxCollider>();
            var part = go.AddComponent<SpaceshipPart>();
            _spawned.Add(go);
            part.Initialize();
            return part;
        }

        static PartClamp AddClamp(SpaceshipPart part, Vector3 localPosition, bool required = true)
        {
            var go = new GameObject("Clamp") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = localPosition;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.3f;
            sphere.isTrigger = true;

            var clamp = go.AddComponent<PartClamp>();
            clamp.SetRequired(required);

            part.RefreshClamps();
            Physics.SyncTransforms();
            return clamp;
        }

        Interactor CreateInteractor(Vector3 position, Vector3 forward)
        {
            var go = new GameObject("Interactor");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));

            var interactor = go.AddComponent<Interactor>();
            interactor.Configure(go.transform, 3f, 60f);

            _spawned.Add(go);
            Physics.SyncTransforms();
            return interactor;
        }
    }
}
