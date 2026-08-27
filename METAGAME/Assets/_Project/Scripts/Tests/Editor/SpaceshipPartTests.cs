using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 파츠 상태 전환 규칙과 상태별 물리 표현을 검증한다.
    /// 잘못된 전환이 통과하면 "견인은 끝났는데 질량이 안 돌아온" 파츠가 생긴다.
    /// </summary>
    public sealed class SpaceshipPartTests
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
        public void NewPart_StartsLocked_AndIsKinematic()
        {
            var part = CreatePart();

            Assert.AreEqual(PartState.Locked, part.State);
            Assert.IsTrue(part.GetComponent<Rigidbody>().isKinematic,
                "고정된 파츠는 운석에 맞아도 밀리면 안 된다.");
            Assert.AreEqual(GameLayers.Part, part.gameObject.layer);
        }

        [Test]
        public void Locked_CannotGoDirectlyToCarried()
        {
            var part = CreatePart();

            Assert.IsFalse(part.TryTransitionTo(PartState.Carried));
            Assert.AreEqual(PartState.Locked, part.State);
        }

        [Test]
        public void Locked_StaysLocked_WhileRequiredClampRemains()
        {
            var part = CreatePart();
            AddClamp(part);
            AddClamp(part);

            Assert.AreEqual(2, part.RemainingRequiredClamps);
            Assert.IsFalse(part.TryTransitionTo(PartState.Free));
            Assert.AreEqual(PartState.Locked, part.State);
        }

        [Test]
        public void PartWithoutClamps_CanUnlockImmediately()
        {
            var part = CreatePart();

            Assert.AreEqual(0, part.RemainingRequiredClamps);
            Assert.IsTrue(part.TryTransitionTo(PartState.Free));
            Assert.IsFalse(part.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void Carried_UsesCarriedLayerAndLighterMass()
        {
            var part = Unlocked();

            Assert.IsTrue(part.TryTransitionTo(PartState.Carried));

            var body = part.GetComponent<Rigidbody>();
            Assert.AreEqual(GameLayers.PartCarried, part.gameObject.layer);
            Assert.AreEqual(PhysicsScale.CarriedPartMass, body.mass, 0.001f);
            Assert.IsFalse(body.isKinematic);
        }

        [Test]
        public void Placed_RestoresPartMassAndLayer()
        {
            var part = Unlocked();
            part.TryTransitionTo(PartState.Carried);

            Assert.IsTrue(part.TryTransitionTo(PartState.Placed));

            var body = part.GetComponent<Rigidbody>();
            Assert.AreEqual(GameLayers.Part, part.gameObject.layer);
            Assert.AreEqual(PhysicsScale.PartMass, body.mass, 0.001f,
                "내려놓은 파츠는 다시 무거워져야 한다.");
        }

        [Test]
        public void Placed_CanBePickedUpAgain_BeforeWelding()
        {
            var part = Unlocked();
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);

            Assert.IsTrue(part.TryTransitionTo(PartState.Carried));
        }

        [Test]
        public void Welded_IsTerminal()
        {
            var part = Unlocked();
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Placed);

            Assert.IsTrue(part.TryTransitionTo(PartState.Welded));

            foreach (PartState next in System.Enum.GetValues(typeof(PartState)))
                Assert.IsFalse(part.TryTransitionTo(next),
                    $"용접된 파츠가 {next} 로 되돌아갔다.");
        }

        [Test]
        public void TransitionToSameState_IsRejected()
        {
            var part = CreatePart();

            Assert.IsFalse(part.TryTransitionTo(PartState.Locked));
        }

        [Test]
        public void StateChanged_ReportsPreviousAndNext()
        {
            var part = CreatePart();

            var seen = new List<(PartState From, PartState To)>();
            part.StateChanged += (from, to) => seen.Add((from, to));

            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            part.TryTransitionTo(PartState.Welded);   // 거부되어야 한다

            CollectionAssert.AreEqual(
                new[] { (PartState.Locked, PartState.Free), (PartState.Free, PartState.Carried) },
                seen);
        }

        [Test]
        public void RepresentationOf_IsLightOnlyWhileCarried()
        {
            foreach (PartState state in System.Enum.GetValues(typeof(PartState)))
            {
                var (layer, mass, kinematic) = SpaceshipPart.RepresentationOf(state);

                if (state == PartState.Carried)
                {
                    Assert.AreEqual(GameLayers.PartCarried, layer);
                    Assert.AreEqual(PhysicsScale.CarriedPartMass, mass, 0.001f);
                }
                else
                {
                    Assert.AreEqual(GameLayers.Part, layer);
                    Assert.AreEqual(PhysicsScale.PartMass, mass, 0.001f);
                }

                Assert.AreEqual(state == PartState.Locked, kinematic,
                    "kinematic 인 상태는 Locked 하나뿐이어야 한다.");
            }
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

        /// <summary>고정쇠가 없어 바로 Free 로 넘어간 파츠.</summary>
        SpaceshipPart Unlocked()
        {
            var part = CreatePart();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        static PartClamp AddClamp(SpaceshipPart part, bool required = true)
        {
            var go = new GameObject("Clamp") { layer = GameLayers.Interactable };
            go.transform.SetParent(part.transform, false);
            go.AddComponent<SphereCollider>().isTrigger = true;

            var clamp = go.AddComponent<PartClamp>();
            clamp.SetRequired(required);

            part.RefreshClamps();
            return clamp;
        }
    }
}
