using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 캐릭터 시스템과 퍼즐 시스템이 같은 자원을 두고 다투지 않는지 검증한다.
    /// 레이어 번호와 Rigidbody 소유권이 그 자원이다.
    /// </summary>
    public sealed class CharacterIntegrationTests
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
        public void RagdollPlayerLayer_MatchesSharedLayerScheme()
        {
            // 인형이 임의의 번호를 쓰면 충돌 매트릭스의 Player 규칙이 적용되지 않는다.
            Assert.AreEqual(GameLayers.Player, RagdollBuilder.PlayerLayer,
                "인형 레이어가 공용 체계의 Player 와 달라졌다.");
        }

        [Test]
        public void WorldMask_ExcludesPlayerLayers()
        {
            var mask = RagdollBuilder.WorldMask;

            Assert.AreEqual(0, mask & (1 << GameLayers.Player),
                "자기 몸이 지형 탐색에 잡히면 접지 판정이 어긋난다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.PlayerDowned),
                "기절한 동료가 지형으로 잡히면 그 위에 올라선다.");
        }

        [Test]
        public void WorldMask_ExcludesQueryOnlyLayers()
        {
            var mask = RagdollBuilder.WorldMask;

            Assert.AreEqual(0, mask & (1 << GameLayers.Interactable),
                "고정쇠 트리거가 접지 판정에 걸리면 허공에 서게 된다.");
            Assert.AreEqual(0, mask & (1 << GameLayers.BeamQuery));
        }

        [Test]
        public void WorldMask_KeepsGroundAndParts()
        {
            var mask = RagdollBuilder.WorldMask;

            Assert.AreNotEqual(0, mask & (1 << GameLayers.Ground),
                "지면이 마스크에서 빠지면 캐릭터가 바닥을 찾지 못한다.");
            Assert.AreNotEqual(0, mask & (1 << GameLayers.Part));
            Assert.AreNotEqual(0, mask & (1 << GameLayers.PartCarried));
        }

        [Test]
        public void CarriedPart_IsBelowHandLiftThreshold_SoClimbableGuardIsRequired()
        {
            // PlayerRig 는 FixedGripMass 보다 가벼운 물체를 운반할 때 isKinematic 을 직접 켠다.
            // 견인 중 파츠가 그 아래에 있는 한 아래의 Climbable 가드가 필요하다.
            // 이 단정이 깨지면 가드 없이도 안전해진 상태이므로 그때 다시 판단한다.
            Assert.Less(PhysicsScale.CarriedPartMass, Tuning.FixedGripMass,
                "견인 중 파츠가 손 들기 임계값보다 가볍다. Climbable 가드가 필요하다.");
        }

        [Test]
        public void Part_IsMarkedClimbable_SoHandsCannotHijackIt()
        {
            var part = CreatePart();

            var grabbable = part.GetComponent<Grabbable>();
            Assert.IsNotNull(grabbable, "파츠에 Grabbable 이 붙어 있어야 손 잡기 경로가 정해진다.");
            Assert.IsTrue(grabbable.Climbable,
                "Climbable 이 꺼지면 손이 파츠를 들면서 isKinematic 을 켜고 상태머신과 다툰다.");
        }

        [Test]
        public void Part_KeepsClimbableFlag_AcrossStateTransitions()
        {
            var part = CreatePart();
            var grabbable = part.GetComponent<Grabbable>();

            part.TryTransitionTo(PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            Assert.IsTrue(grabbable.Climbable, "견인 중에도 손이 가로채면 안 된다.");

            part.TryTransitionTo(PartState.Placed);
            Assert.IsTrue(grabbable.Climbable);
        }

        [Test]
        public void LockedPart_IsKinematic_SoHandsSkipItRegardlessOfMass()
        {
            // PlayerRig 의 도구 경로는 동적 Rigidbody가 있는 도구만 받는다.
            // Climbable 가드와 별개로 고정된 파츠는 이 조건에서도 걸러진다.
            var part = CreatePart();

            Assert.AreEqual(PartState.Locked, part.State);
            Assert.IsTrue(part.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void GrabbableRole_DefaultsToTransport()
        {
            var go = new GameObject("Transport");
            _spawned.Add(go);

            var grabbable = Grabbable.Mark(go, "운반물");

            Assert.AreEqual(GrabbableRole.Transport, grabbable.Role);
            Assert.IsTrue(grabbable.IsTransport);
            Assert.IsFalse(grabbable.IsTool);
        }

        [Test]
        public void GrabbableRole_ToolUsesExplicitToolClassification()
        {
            var go = new GameObject("Tool");
            _spawned.Add(go);

            var grabbable = Grabbable.Mark(go, "도구", role: GrabbableRole.Tool);

            Assert.AreEqual(GrabbableRole.Tool, grabbable.Role);
            Assert.IsTrue(grabbable.IsTool);
        }

        [Test]
        public void ToolBrace_UsesFixedGripInsteadOfTransportState()
        {
            var handObject = new GameObject("Hand");
            var handBody = handObject.AddComponent<Rigidbody>();
            var hand = handObject.AddComponent<Hand>();
            hand.Body = handBody;
            _spawned.Add(handObject);

            var toolObject = new GameObject("Tool");
            toolObject.AddComponent<BoxCollider>();
            var toolBody = toolObject.AddComponent<Rigidbody>();
            var tool = toolObject.AddComponent<Grabbable>();
            _spawned.Add(toolObject);

            Assert.IsTrue(hand.BraceTo(tool, toolBody));
            Assert.IsTrue(hand.Gripping);
            Assert.IsFalse(hand.Carrying);
            Assert.AreSame(toolBody, hand.HeldBody);
            Assert.IsNull(hand.GetComponent<FixedJoint>(),
                "도구는 논리 고정 그립이어야 하며 물리 FixedJoint를 만들면 안 된다.");
        }

        SpaceshipPart CreatePart()
        {
            var go = new GameObject("Part");
            go.AddComponent<BoxCollider>();
            var part = go.AddComponent<SpaceshipPart>();
            _spawned.Add(go);
            part.Initialize();
            return part;
        }
    }
}
