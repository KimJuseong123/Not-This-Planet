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
        public void Clamp_IsNotReleasedByHolding()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            Assert.AreEqual(0f, clamp.HoldSeconds, 0.001f,
                "고정못은 꾹 눌러 푸는 것이 아니다 — 뒤로 이동해 뽑는다(기획 §12.1).");
            Assert.AreEqual(PhysicsScale.ClampPullSeconds, PartClamp.PullSecondsFor(1), 0.001f,
                "1명이 뽑는 시간은 기획 §12.2 의 약 8초다.");
        }

        [Test]
        public void Release_MarksReleasedAndMakesLoosePhysicsObject()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            clamp.Release();

            Assert.IsTrue(clamp.IsReleased);
            Assert.IsNull(clamp.transform.parent, "해제된 고정쇠는 파츠와 분리되어야 한다.");
            Assert.IsNotNull(clamp.ReleasedBody);
            Assert.IsFalse(clamp.ReleasedBody.isKinematic);
            Assert.Greater(clamp.ReleasedBody.linearVelocity.y, 0f,
                "해제된 고정쇠는 항상 위쪽으로 튀어야 한다.");
            Assert.AreEqual(GameLayers.Prop, clamp.gameObject.layer);
            foreach (var c in clamp.GetComponents<Collider>())
            {
                Assert.IsTrue(c.enabled, "해제된 고정쇠는 물리 오브젝트로 남아야 한다.");
                Assert.IsFalse(c.isTrigger, "해제된 고정쇠는 지면과 충돌해야 한다.");
            }
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
        public void Interactor_FindsClampButHoldingDoesNotRelease()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.Tick(0.02f);
            Assert.AreSame(clamp, interactor.Focus, "고정못이 Interactable 레이어에서 탐지되어야 한다.");

            // 예전에는 여기서 1.5초를 누르면 풀렸다. 기획 §12.1 은 뒤로 이동해 뽑는
            // 것으로 정하므로, 홀드로 풀리면 그 조작을 우회하는 길이 생긴다.
            Assert.IsTrue(interactor.TryBegin());
            interactor.Tick(PhysicsScale.ClampPullSeconds * 2f);

            Assert.IsFalse(clamp.IsReleased, "홀드만으로는 풀리지 않는다.");
            Assert.AreEqual(PartState.Locked, part.State);
        }

        /// <summary>기획 §12.2 — "일반 1명 약 8초", "2명 약 4.5초".</summary>
        [Test]
        public void Pull_TakesThePlannedTime()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            Assert.IsTrue(clamp.TryJoinPull());

            var step = 0.02f;
            var spent = 0f;
            for (var i = 0; i < 1000 && !clamp.IsReleased; i++)
            {
                clamp.Pull(step);
                spent += step;
            }

            Assert.IsTrue(clamp.IsReleased, "언젠가는 뽑혀야 한다.");
            Assert.AreEqual(PhysicsScale.ClampPullSeconds, spent, 0.1f,
                $"1명은 약 8초여야 한다. {spent:0.00}초 걸렸다.");
            Assert.AreEqual(PartState.Free, part.State);

            Assert.AreEqual(4.5f, PartClamp.PullSecondsFor(2), 0.1f,
                "2명은 기획 §12.2 의 약 4.5초여야 한다.");
        }

        /// <summary>기획 §12.1 — "좌클릭을 놓거나 운석에 맞아도 빠진 정도는 유지된다".</summary>
        [Test]
        public void LettingGo_KeepsTheProgress()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            clamp.TryJoinPull();
            clamp.Pull(PhysicsScale.ClampPullSeconds * 0.5f);
            clamp.LeavePull();

            Assert.AreEqual(0.5f, clamp.Progress, 0.01f, "놓아도 빠진 정도가 남아야 한다.");
            Assert.IsFalse(clamp.IsReleased);
            Assert.AreEqual(0, clamp.Pullers, "손을 뗐으므로 인원은 0 이다.");
        }

        /// <summary>기획 §12.1 — "다른 플레이어가 남은 진행을 이어서 수행할 수 있다".</summary>
        [Test]
        public void SecondPlayer_CanFinishWhatFirstPlayerStarted()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            clamp.TryJoinPull();
            clamp.Pull(PhysicsScale.ClampPullSeconds * 0.6f);
            clamp.LeavePull();

            Assert.IsTrue(clamp.TryJoinPull(), "다른 사람이 이어받을 수 있어야 한다.");
            clamp.Pull(PhysicsScale.ClampPullSeconds * 0.5f);

            Assert.IsTrue(clamp.IsReleased, "이어받아 끝낼 수 있어야 한다.");
        }

        /// <summary>기획 §12.2 — "같은 고정못은 최대 2명".</summary>
        [Test]
        public void Clamp_TakesAtMostTwoPullers()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);

            for (var i = 0; i < PhysicsScale.ClampMaxPullers; i++)
                Assert.IsTrue(clamp.TryJoinPull(), $"{i + 1}번째는 붙을 수 있어야 한다.");

            Assert.IsFalse(clamp.TryJoinPull(),
                "정원을 넘겨 붙으면 3~4명이 한 곳에 몰려 §12.2 의 병렬 작업이 사라진다.");
            Assert.AreEqual(PhysicsScale.ClampMaxPullers, clamp.Pullers);
        }

        /// <summary>인원이 늘면 같은 시간에 더 많이 진행된다.</summary>
        [Test]
        public void TwoPullers_ProgressFasterThanOne()
        {
            var part = CreatePart();
            var solo = AddClamp(part, Vector3.forward * 2f);
            var pair = AddClamp(part, Vector3.back * 2f);

            solo.TryJoinPull();
            pair.TryJoinPull();
            pair.TryJoinPull();

            solo.Pull(1f);
            pair.Pull(1f);

            Assert.Greater(pair.Progress, solo.Progress * 1.5f,
                $"둘이면 더 빨라야 한다. 1명 {solo.Progress:0.000} · 2명 {pair.Progress:0.000}");
        }

        [Test]
        public void Interactor_CancelledHold_LeavesClampAttached()
        {
            var part = CreatePart();
            var clamp = AddClamp(part, Vector3.forward * 2f);
            var interactor = CreateInteractor(Vector3.zero, Vector3.forward);

            interactor.TryBegin();
            interactor.Tick(PhysicsScale.ClampPullSeconds * 0.8f);
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

        PartClamp AddClamp(SpaceshipPart part, Vector3 localPosition, bool required = true)
        {
            var go = new GameObject("Clamp") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = localPosition;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.3f;
            sphere.isTrigger = true;

            var clamp = go.AddComponent<PartClamp>();
            clamp.SetRequired(required);

            _spawned.Add(go);
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
