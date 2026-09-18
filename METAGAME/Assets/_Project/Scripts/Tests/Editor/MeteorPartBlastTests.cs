using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 충격파가 파츠를 민다(2026-09-18).
    ///
    /// <para><b>예전에는 안 움직였다.</b> <c>Meteor.ApplyKnockback</c> 의 마스크가
    /// <see cref="GameLayers.PlayerMask"/> 와 <see cref="GameLayers.PropMask"/> 뿐이라
    /// 파츠는 훑는 대상에서 빠져 있었고(S15P21B202-247 의 판단), 운석 <b>본체가 직접
    /// 맞힌</b> 파츠만 물리 충돌로 움직였다. 플레이 확인에서 "파츠들은 별로 안 날아감"
    /// 이 나온 자리다.</para>
    ///
    /// <para><b>폭발력으로는 못 민다.</b> 놓인 파츠는 500kg 이라
    /// <c>ForceMode.Force</c> 900N 을 한 스텝 받아 0.036m/s 다. 그래서 속도를 직접 준다 —
    /// 기체가 같은 이유로 <see cref="PhysicsScale.CraftImpactSpeed"/> 를 쓴다.</para>
    /// </summary>
    public sealed class MeteorPartBlastTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
        bool _logWasIgnoring;
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            _logWasIgnoring = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;

            // <b>전역 상태다 — 반드시 되돌린다</b>(!525 AI 리뷰). 켜 둔 채로
            // 끝나면 뒤에 도는 다른 검증의 에러 로그까지 삼켜, 실패해야 할 것이
            // 조용히 통과한다.
            LogAssert.ignoreFailingMessages = _logWasIgnoring;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 밀린다 ---------------------------------------------------------

        [Test]
        public void APlacedPartIsPushedByTheBlast()
        {
            var part = MakePart(new Vector3(2f, 0.5f, 0f), PartState.Free);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.Greater(Speed(part), 0.1f, "충격파 안의 파츠는 밀려야 한다.");
            Assert.Greater(part.GetComponent<Rigidbody>().linearVelocity.x, 0f,
                "착탄 지점 반대쪽으로 밀려야 한다.");
        }

        /// <summary>회전이 없으면 무거운 상자가 미끄러지기만 해서 "맞았다" 로 안 보인다.</summary>
        [Test]
        public void TheBlastAlsoSpinsThePart()
        {
            var part = MakePart(new Vector3(2f, 0.5f, 0f), PartState.Free);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.Greater(part.GetComponent<Rigidbody>().angularVelocity.magnitude, 0.1f);
        }

        /// <summary>500kg 이라도 눈에 보이게 움직여야 한다 — 폭발력만으로는 0.036m/s 다.</summary>
        [Test]
        public void EvenAHeavyPartMovesVisibly()
        {
            var part = MakePart(new Vector3(1.5f, 0.5f, 0f), PartState.Free);
            part.GetComponent<Rigidbody>().mass = PhysicsScale.PartMass;

            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            Assert.Greater(Speed(part), 1f, $"500kg 파츠가 {Speed(part):0.###}m/s 로만 움직였다.");
        }

        [Test]
        public void TheBlastFadesWithDistance()
        {
            var near = MakePart(new Vector3(1f, 0.5f, 0f), PartState.Free);
            var far = MakePart(new Vector3(3f, 0.5f, 0f), PartState.Free);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.Greater(Speed(near), Speed(far));
            Assert.Greater(Speed(far), 0f, "가장자리도 0 은 아니다.");
        }

        [Test]
        public void APartOutsideTheShockwaveIsNotTouched()
        {
            var part = MakePart(new Vector3(9f, 0.5f, 0f), PartState.Free);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            Assert.Greater(9f, meteor.ShockwaveRadius, "충격파 밖에 둔 것이 전제다.");

            meteor.ApplyImpact(Vector3.zero);

            Assert.AreEqual(0f, Speed(part), 0.001f);
        }

        // --- 안 밀리는 것 ----------------------------------------------------

        /// <summary>
        /// <see cref="PartState.Locked"/> 의 정의가 "고정쇠로 바닥에 박혀 있다.
        /// <b>운석에 맞아도 움직이지 않는다</b>" 다.
        /// </summary>
        [Test]
        public void AClampedPartDoesNotMove()
        {
            var part = MakePart(new Vector3(1f, 0.5f, 0f), PartState.Locked);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.AreEqual(0f, Speed(part), 0.001f, "고정쇠에 박힌 파츠는 그대로다.");
        }

        /// <summary>
        /// 견인 중인 파츠는 빔이 자세를 잡고 있다. 여기서 속도를 얹으면 둘이 같은
        /// 강체를 두고 다툰다.
        /// </summary>
        [Test]
        public void ACarriedPartIsLeftToTheBeam()
        {
            var part = MakePart(new Vector3(1f, 0.5f, 0f), PartState.Free);
            part.TryTransitionTo(PartState.Carried);
            Assume.That(part.State, Is.EqualTo(PartState.Carried));

            var body = part.GetComponent<Rigidbody>();
            body.isKinematic = false;   // 견인 표현과 무관하게 속도를 잴 수 있게 둔다

            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            Assert.AreEqual(0f, Speed(part), 0.001f);
        }

        /// <summary>착탄은 한 번이다 — 콜라이더가 여럿이라고 여러 번 밀면 안 된다.</summary>
        [Test]
        public void APartWithManyCollidersIsPushedOnce()
        {
            var one = MakePart(new Vector3(1.5f, 0.5f, 0f), PartState.Free);
            AddExtraCollider(one, new Vector3(0.6f, 0f, 0f));
            AddExtraCollider(one, new Vector3(-0.6f, 0f, 0f));

            var plain = MakePart(new Vector3(1.5f, 0.5f, 6f), PartState.Free);

            var first = MakeMeteor(new Vector3(0f, 5f, 0f));
            first.ApplyImpact(Vector3.zero);

            var second = MakeMeteor(new Vector3(0f, 5f, 6f));
            second.ApplyImpact(new Vector3(0f, 0f, 6f));

            Assert.AreEqual(Speed(plain), Speed(one), 0.01f,
                "콜라이더 수가 세기를 바꾸면 덩치 큰 파츠일수록 더 날아간다.");
        }

        // --- 리그 -----------------------------------------------------------

        static float Speed(SpaceshipPart part) =>
            part.GetComponent<Rigidbody>().linearVelocity.magnitude;

        void AddExtraCollider(SpaceshipPart part, Vector3 localOffset)
        {
            var box = part.gameObject.AddComponent<BoxCollider>();
            box.center = localOffset;
            box.size = new Vector3(0.8f, 1f, 0.8f);
            UnityEngine.Physics.SyncTransforms();
        }

        SpaceshipPart MakePart(Vector3 position, PartState state)
        {
            var go = new GameObject("파츠") { layer = GameLayers.Part };
            go.transform.position = position;
            _spawned.Add(go);

            go.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 1f);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var part = go.AddComponent<SpaceshipPart>();
            part.Initialize();
            if (state != PartState.Locked) part.TryTransitionTo(state);

            body.isKinematic = false;
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        Meteor MakeMeteor(Vector3 position)
        {
            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }
    }
}
