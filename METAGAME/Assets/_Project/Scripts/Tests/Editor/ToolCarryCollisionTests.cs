using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

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
