using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Interaction;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 휴대 중에는 도구가 다른 것과 부딪히지 않는다(S15P21B202-237).
    ///
    /// 1인칭으로 들고 아래를 볼 때 도구 콜라이더가 지면을 파고들면, PhysX 는 그 겹침을
    /// 풀기 위해 <b>비키네마틱인 쪽 — 플레이어 —</b> 를 밀어낸다. 도구는 손에 잡힐 때
    /// 키네마틱이 되므로(<see cref="Tuning"/> 의 40kg 문턱) 밀리는 것은 언제나 플레이어다.
    ///
    /// <b>도구 전체의 규칙이므로 도구 전체를 잠근다.</b> 예전에는 용접기가 자기
    /// <c>OnEquipped</c> 안에서 직접 껐고, S15P21B202-233 이 방망이를 만들 때 그 세 줄을
    /// 빠뜨려 방망이를 들고 바닥을 보면 플레이어가 날아갔다. 지금은
    /// <see cref="ToolBase"/> 가 하므로 <b>새 도구가 생겨도 저절로 지켜진다.</b>
    /// </summary>
    public sealed class ToolCarryCollisionTests
    {
        readonly List<GameObject> _spawned = new();

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void AimedEquipmentAtPickupRange_EquipsAndChargesUntilQIsReleased(bool welding, bool rightClick)
        {
            var player = PlayerRig.Create("Tool pickup test", new Vector3(100f, 20f, 100f),
                Quaternion.identity, Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            ToolBase tool;
            Rigidbody body;
            if (welding) (tool, body) = MakeTool<PlasmaWeldingTool>();
            else (tool, body) = MakeTool<MeteorBat>();
            var target = tool.gameObject.AddComponent<Grabbable>();
            target.Role = GrabbableRole.Tool;
            tool.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.12f;
            // **`Rigidbody.position` 이 아니라 Transform 을 옮긴다** (S15P21B202-463).
            // `Rigidbody.position` 은 PhysX 쪽만 옮기고 Transform 은 다음 물리 스텝까지
            // 그대로인데, 바로 뒤의 `SyncTransforms()` 는 **Transform → PhysX** 방향이다 —
            // 즉 방금 옮긴 것을 원점으로 도로 밀어 넣는다. 그러면 도구가 플레이어에게서
            // 140m 떨어진 곳에 있게 되어 조준에 아무것도 안 걸린다.
            tool.transform.position = player.AimOrigin + player.AimForward * 0.9f;
            Physics.SyncTransforms();

            player.RefreshInteractionFocus();
            Assert.AreEqual(PlayerInteractionKind.Grabbable, player.CurrentInteractionTarget.Kind);
            Assert.AreSame(target, player.CurrentInteractionTarget.Grabbable);
            Assert.AreEqual("[좌클릭]", Metagame.UI.GameHud.PromptFor(false, false,
                player.CurrentInteractionTarget.CanInteract, player.CurrentInteractionTarget.Kind).Key);
            player.SetInput(new InputFrame { TransportAction = true });
            player.Tick();
            Assert.IsNull(player.HeldTool, "탑승 키로 방망이·용접 건을 줍는 동작을 추가하면 안 된다.");
            player.SetInput(new InputFrame { GrabLeft = !rightClick, GrabRight = rightClick });
            player.Tick();
            Assert.AreSame(target, player.HeldTool);
            Assert.AreSame(tool, player.ActiveTool);
            Assert.AreSame(target, player.LeftHand.Held);
            Assert.AreSame(target, player.RightHand.Held);

            player.SetInput(new InputFrame { ToolAction = true });
            for (var i = 0; i < 30; i++) player.Tick();
            Assert.IsTrue(player.ChargingTool, "HUD must receive the Q charging state.");
            Assert.Greater(player.ToolCharge, Tuning.ToolDropChargeThreshold);

            player.SetInput(new InputFrame());
            player.Tick();
            Assert.IsNull(player.HeldTool);
            Assert.IsFalse(player.ChargingTool);
            Assert.AreEqual(0f, player.ToolCharge);
            Assert.IsFalse(body.isKinematic);
            player.Tick();
            Assert.Greater(body.linearVelocity.magnitude, 0f);
        }

        [Test]
        public void EquipmentCannotBypassEquippingThroughAnOrdinaryHandGrip()
        {
            var (tool, body) = MakeTool<MeteorBat>();
            var target = tool.gameObject.AddComponent<Grabbable>();
            target.Role = GrabbableRole.Tool;
            var handObject = new GameObject("Hand");
            _spawned.Add(handObject);
            var hand = handObject.AddComponent<Hand>();
            hand.Body = handObject.AddComponent<Rigidbody>();

            Assert.IsFalse(hand.GripTo(target, body));
            Assert.IsNull(hand.Held);
            Assert.AreEqual(0, target.HandCount);

            target.Role = GrabbableRole.Transport;
            Assert.IsTrue(hand.GripTo(target, body), "Ordinary cargo must still be carried.");
            hand.Release();
        }

        [TestCase(1.8f, 0f)]
        [TestCase(0.9f, 0.4f)]
        public void EquipmentOutsideReachOrCrosshairAssist_IsNotFocused(float depth, float side)
        {
            var player = PlayerRig.Create("Tool range test", new Vector3(100f, 20f, 100f),
                Quaternion.identity, Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            var (tool, body) = MakeTool<MeteorBat>();
            tool.gameObject.AddComponent<Grabbable>().Role = GrabbableRole.Tool;
            tool.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.12f;
            body.position = player.AimOrigin + player.AimForward * depth + Vector3.right * side;
            Physics.SyncTransforms();

            player.RefreshInteractionFocus();

            Assert.AreEqual(PlayerInteractionKind.None, player.CurrentInteractionTarget.Kind);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        // --- 두 도구 모두 ---------------------------------------------------

        [Test]
        public void EquippingABat_TurnsOffItsCollisions() =>
            AssertSuspendsWhileHeld<MeteorBat>();

        [Test]
        public void EquippingTheWeldingGun_TurnsOffItsCollisions() =>
            AssertSuspendsWhileHeld<PlasmaWeldingTool>();

        [Test]
        public void UnequippingABat_RestoresItsCollisions() =>
            AssertRestoresOnRelease<MeteorBat>(originallyOn: true);

        [Test]
        public void UnequippingTheWeldingGun_RestoresItsCollisions() =>
            AssertRestoresOnRelease<PlasmaWeldingTool>(originallyOn: true);

        /// <summary>
        /// 원래 꺼져 있었으면 <b>꺼진 채로</b> 돌아와야 한다. 무조건 켜면 일부러 끄고
        /// 둔 물체가 도구를 한 번 잡았다 놓는 것으로 되살아난다.
        /// </summary>
        [Test]
        public void AToolThatStartedWithCollisionsOff_StaysOff() =>
            AssertRestoresOnRelease<MeteorBat>(originallyOn: false);

        /// <summary>
        /// 장착하지 않은 도구를 해제해도 값이 바뀌지 않아야 한다.
        /// <c>Unequip</c> 은 여러 경로에서 불린다.
        /// </summary>
        [Test]
        public void UnequippingSomethingNeverEquipped_ChangesNothing()
        {
            var (tool, body) = MakeTool<MeteorBat>();
            body.detectCollisions = true;

            tool.Unequip();

            Assert.IsTrue(body.detectCollisions, "장착하지 않았으면 건드리지 않아야 한다.");
        }

        [Test]
        public void DirectlyUnequippedBatRestoresGravityAndFreeRotation()
        {
            var (tool, body) = MakeTool<MeteorBat>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            tool.Equip(null, body);
            tool.Unequip();

            Assert.IsFalse(body.isKinematic, "해제된 방망이는 다시 동적 Rigidbody여야 한다.");
            Assert.IsTrue(body.useGravity, "해제된 방망이는 중력의 영향을 받아야 한다.");
            Assert.AreEqual(RigidbodyConstraints.None, body.constraints,
                "바닥에서 충돌에 의해 눕도록 회전 제한을 풀어야 한다.");
        }

        /// <summary>
        /// 장착 중에 다시 장착되어도 <b>처음 값</b>을 지켜야 한다.
        ///
        /// 두 번째 장착에서 이미 꺼진 <c>false</c> 를 원래 값으로 기억해 버리면,
        /// 내려놓아도 충돌이 돌아오지 않는다.
        /// </summary>
        [Test]
        public void EquippingTwice_KeepsTheOriginalValue()
        {
            var (tool, body) = MakeTool<MeteorBat>();
            body.detectCollisions = true;

            tool.Equip(null, body);
            Assert.IsFalse(body.detectCollisions);

            tool.Equip(null, body);          // 안에서 Unequip → Equip 을 돈다
            Assert.IsFalse(body.detectCollisions, "휴대 중에는 여전히 꺼져 있어야 한다.");

            tool.Unequip();
            Assert.IsTrue(body.detectCollisions, "처음 값(켜짐)으로 돌아와야 한다.");
        }

        // --- 리그 ---------------------------------------------------------

        void AssertSuspendsWhileHeld<T>() where T : ToolBase
        {
            var (tool, body) = MakeTool<T>();
            body.detectCollisions = true;

            tool.Equip(null, body);

            Assert.IsTrue(tool.IsEquipped);
            Assert.IsFalse(body.detectCollisions,
                $"{typeof(T).Name} 를 들면 충돌이 꺼져야 한다 — 아래를 볼 때 플레이어가 튄다.");
        }

        void AssertRestoresOnRelease<T>(bool originallyOn) where T : ToolBase
        {
            var (tool, body) = MakeTool<T>();
            body.detectCollisions = originallyOn;

            tool.Equip(null, body);
            tool.Unequip();

            Assert.AreEqual(originallyOn, body.detectCollisions,
                "내려놓으면 휴대 전 상태로 돌아와야 한다.");
        }

        /// <summary>
        /// 도구 하나. <c>Owner</c> 없이 장착한다 — 보고 싶은 것이 충돌 상태 하나이므로
        /// 플레이어 리그를 세울 이유가 없다.
        /// </summary>
        (T Tool, Rigidbody Body) MakeTool<T>() where T : ToolBase
        {
            var go = new GameObject(typeof(T).Name) { layer = GameLayers.Prop };
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 3f;

            return (go.AddComponent<T>(), body);
        }
    }
}
