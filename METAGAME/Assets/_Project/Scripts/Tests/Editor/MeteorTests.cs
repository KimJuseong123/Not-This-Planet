using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석의 착탄 처리와 붕괴 선택성을 검증한다.
    ///
    /// 이 파일의 핵심은 마지막 테스트다. 같은 운석이 떨어졌을 때
    /// 용접하지 않은 접합만 끊어지고 용접한 접합은 버텨야 한다.
    /// 그 판단은 이 코드가 아니라 물리 솔버가 조인트의 breakForce 로 한다.
    /// </summary>
    public sealed class MeteorTests
    {
        readonly List<GameObject> _spawned = new();

        SimulationMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
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
        }

        // --- 착탄 처리 -------------------------------------------------------

        [Test]
        public void NewMeteor_HasNotImpacted()
        {
            var meteor = CreateMeteor(Vector3.up * 10f);

            Assert.IsFalse(meteor.HasImpacted);
        }

        [Test]
        public void ApplyImpact_RaisesEventOnce()
        {
            var meteor = CreateMeteor(Vector3.up * 10f);

            var raised = 0;
            meteor.Impacted += (_, _) => raised++;

            meteor.ApplyImpact(Vector3.zero);
            meteor.ApplyImpact(Vector3.zero);

            Assert.AreEqual(1, raised, "착탄은 한 번만 처리해야 한다.");
            Assert.IsTrue(meteor.HasImpacted);
        }

        [Test]
        public void Meteor_DynamicPropCollisionDoesNotSettleInMidair()
        {
            // The moving prop is deliberately the only thing below the meteor. A collision
            // is expected, but it is not a support surface for the permanent impact state.
            var prop = CreateLooseBody(Vector3.zero, GameLayers.Prop, PhysicsScale.PartMass);
            prop.useGravity = false;
            var meteor = CreateMeteor(Vector3.up * 2f);

            Simulate(1.0f);

            Assert.IsFalse(meteor.HasImpacted, "동적 소품 충돌만으로 착탄 이벤트가 나가면 안 된다.");
            Assert.IsFalse(meteor.Settled, "동적 소품 위 허공에 운석을 고정하면 안 된다.");
            Assert.IsFalse(meteor.GetComponent<Rigidbody>().isKinematic,
                "동적 물체와 부딪힌 운석은 계속 일반 물리로 움직여야 한다.");
        }

        [Test]
        public void Meteor_StaticSupportSettlesAndRaisesGlobalImpactOnce()
        {
            CreateGround();
            var meteor = CreateMeteor(Vector3.up * 2f);
            var raised = 0;
            System.Action<Meteor, Vector3> handler = (_, _) => raised++;
            Meteor.AnyImpacted += handler;

            try
            {
                Simulate(2f);

                Assert.IsTrue(meteor.Settled, "실제 정적 지지면에 닿은 경우에만 정착해야 한다.");
                Assert.IsTrue(meteor.GroundProp);
                Assert.IsFalse(meteor.GetComponent<Rigidbody>().isKinematic,
                    "착지 후 일반 운반물 물리를 유지해야 한다.");
                Assert.AreEqual(1, raised, "실제 착탄 이벤트는 한 번만 발생해야 한다.");
                Assert.AreEqual(0f, meteor.ImpactPoint.y, 0.08f);
            }
            finally
            {
                Meteor.AnyImpacted -= handler;
            }
        }

        [Test]
        public void Meteor_DoesNotExpireBeforeImpact()
        {
            var meteor = CreateMeteor(Vector3.up * 10f);

            var expired = false;
            meteor.Expired += _ => expired = true;

            // 낙하 시간(30m, 저중력에서 약 4.6초)보다는 확실히 오래 남아야 한다.
            for (var i = 0; i < Mathf.RoundToInt(6f / Tuning.FixedStep); i++)
                meteor.Tick(Tuning.FixedStep);

            Assert.IsFalse(expired, "떨어지는 중에 사라지면 안 된다.");
        }

        /// <summary>
        /// <b>이 검증이 두 번 뒤집혔다.</b> 처음에는 "착탄한 운석이 남아 쌓이면 안 된다"
        /// 였고, S15P21B202-251 이 기획 §19("스테이지 동안 사라지지 않는다")를 근거로
        /// "영영 남는다" 로 뒤집었다. 2026-09-02 에 <b>둘 다 사라지게</b> 하기로 정해
        /// 다시 뒤집는다 — §19 를 어기므로 [Plan] 확인이 필요하다(S15P21B202-273).
        /// </summary>
        [Test]
        public void Meteor_ScarDespawnsAfterItsTime()
        {
            var meteor = CreateMeteor(Vector3.up * 10f);
            meteor.ApplyImpact(Vector3.zero);

            var expired = false;
            meteor.Expired += _ => expired = true;

            Assert.IsTrue(meteor.Settled);

            // 분화구가 식는 시간(5초)보다는 오래 남아야 한다.
            for (var i = 0; i < Mathf.RoundToInt(PhysicsScale.MeteorScarCoolSeconds
                                                 / Tuning.FixedStep); i++)
                meteor.Tick(Tuning.FixedStep);
            Assert.IsFalse(expired, "식는 연출이 끝나기 전에 사라지면 안 된다.");

            var guard = 0;
            while (!expired && guard++ < 200000) meteor.Tick(Tuning.FixedStep);

            Assert.IsTrue(expired, "일정 시간 뒤 사라진다(S15P21B202-273).");
        }

        [Test]
        public void Meteor_ExpiresByLifetimeCap_EvenWithoutImpact()
        {
            // 착탄 처리는 물리 충돌 콜백에 달려 있다. 운석이 플레이 공간 밖으로 떨어지거나
            // 콜백을 놓치면 영구히 남으므로 상한이 필요하다.
            var meteor = CreateMeteor(Vector3.up * 10f);

            var expired = false;
            meteor.Expired += _ => expired = true;

            var guard = 0;
            while (!expired && guard++ < 200000) meteor.Tick(Tuning.FixedStep);

            Assert.IsTrue(expired, "착탄하지 않은 운석도 수명이 지나면 사라져야 한다.");
        }

        [Test]
        public void Impact_KnocksBackNearbyPlayer()
        {
            var meteor = CreateMeteor(Vector3.up * 5f);
            var player = CreateLooseBody(Vector3.right * 1.5f, GameLayers.Player, Tuning.BodyMass);

            meteor.ApplyImpact(Vector3.zero);
            Simulate(0.1f);   // AddExplosionForce 는 다음 물리 스텝에서 속도로 반영된다

            Assert.Greater(player.linearVelocity.magnitude, 0.1f,
                "착탄 반경 안의 플레이어는 밀려나야 한다.");
        }

        [Test]
        public void Impact_DoesNotKnockBackDistantPlayer()
        {
            var meteor = CreateMeteor(Vector3.up * 5f);
            var player = CreateLooseBody(
                Vector3.right * (PhysicsScale.MeteorMediumImpactRadius + 5f), GameLayers.Player, Tuning.BodyMass);

            meteor.ApplyImpact(Vector3.zero);
            Simulate(0.1f);

            Assert.Less(player.linearVelocity.magnitude, 0.001f);
        }

        [Test]
        public void Impact_DoesNotDirectlyPushParts()
        {
            // 파츠에 폭발력을 얹으면 조인트가 끊어질지를 솔버가 아니라 이 상수가 결정해 버려서
            // 용접 정도에 따른 차이가 흐려진다. 파츠에 대한 영향은 물리 충돌만으로 남긴다.
            var meteor = CreateMeteor(Vector3.up * 5f);
            var part = CreateLooseBody(Vector3.right * 1.5f, GameLayers.Part, PhysicsScale.PartMass);

            meteor.ApplyImpact(Vector3.zero);
            Simulate(0.1f);

            Assert.Less(part.linearVelocity.magnitude, 0.001f,
                "파츠는 코드가 아니라 물리 충돌로만 영향을 받아야 한다.");
        }

        // --- 붕괴 선택성 -----------------------------------------------------

        [Test]
        public void Meteor_BreaksUnweldedJointButNotWeldedOne()
        {
            // 지면이 없으면 탑과 운석이 같이 자유낙하해 운석이 영원히 따라잡지 못한다.
            CreateGround();

            var unwelded = AssembledPair(new Vector3(0f, 0f, 0f), welded: false);
            var welded = AssembledPair(new Vector3(40f, 0f, 0f), welded: true);

            Assert.AreEqual(PhysicsScale.UnweldedBreakForce,
                unwelded.GetComponent<FixedJoint>().breakForce, 1f);
            Assert.IsTrue(float.IsPositiveInfinity(welded.GetComponent<FixedJoint>().breakForce));

            DropMeteorOnto(unwelded);
            DropMeteorOnto(welded);

            Simulate(8f);

            Assert.IsNull(unwelded.GetComponent<FixedJoint>(),
                "용접하지 않은 접합은 운석 충격에 끊어져야 한다.");
            Assert.IsNotNull(welded.GetComponent<FixedJoint>(),
                "용접한 접합은 같은 운석을 버텨야 한다.");
        }

        // --- 헬퍼 ------------------------------------------------------------

        /// <summary>
        /// 물리와 운석 틱을 함께 돌린다.
        ///
        /// <b>Tick 을 같이 불러야 한다</b>(S15P21B202-273). EditMode 에서는
        /// <c>OnCollisionEnter</c> 가 오지 않으므로 접점 판정 경로가 돌지 않는다 —
        /// 예전에는 이 도우미가 <c>Physics.Simulate</c> 만 돌려서
        /// <c>Meteor_StaticSupportSettlesAndRaisesGlobalImpactOnce</c> 가 <b>원래 통과할
        /// 수 없었다.</b> 정착 안전망은 <c>Tick</c> 에 있으므로 그것을 태운다.
        /// </summary>
        void Simulate(float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Tuning.FixedStep);
            for (var i = 0; i < steps; i++)
            {
                UnityEngine.Physics.Simulate(Tuning.FixedStep);

                for (var m = Meteor.All.Count - 1; m >= 0; m--)
                {
                    var meteor = Meteor.All[m];
                    if (meteor != null) meteor.Tick(Tuning.FixedStep);
                }
            }
        }

        /// <summary>정적 지면. 탑이 떠 있지 않게 받쳐 준다.</summary>
        void CreateGround()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ground";
            go.layer = GameLayers.Ground;
            go.transform.position = new Vector3(20f, -0.5f, 0f);
            go.transform.localScale = new Vector3(140f, 1f, 60f);

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        Meteor CreateMeteor(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Meteor";
            go.layer = GameLayers.Meteor;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (PhysicsScale.MeteorRadius * 2f);
            go.AddComponent<Rigidbody>();

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }

        /// <summary>넉백 대상으로 쓰는 단순한 물체.</summary>
        Rigidbody CreateLooseBody(Vector3 position, int layer, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.layer = layer;
            go.transform.position = position;

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;

            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        /// <summary>아래 파츠 위에 조립된 위 파츠를 만들고, 필요하면 용접까지 끝낸다.</summary>
        SpaceshipPart AssembledPair(Vector3 origin, bool welded)
        {
            SocketTestRig.Placed(_spawned, origin);

            var upper = SocketTestRig.Freed(
                _spawned, origin + Vector3.up * (PhysicsScale.PartHeight + SocketTestRig.StackGap));


            upper.TryTransitionTo(PartState.Carried);

            var link = PartAssembler.Assemble(PartAssembler.FindMatch(upper));
            Assert.IsNotNull(link);

            if (welded) link.ForceWeldComplete();

            link.Tick();
            return upper;
        }

        /// <summary>파츠 위쪽에서 운석을 떨어뜨린다.</summary>
        void DropMeteorOnto(SpaceshipPart part)
        {
            var top = part.transform.position + Vector3.up * (PhysicsScale.PartHeight * 0.5f);
            CreateMeteor(top + Vector3.up * PhysicsScale.MeteorDropHeight);
        }
    }
}
