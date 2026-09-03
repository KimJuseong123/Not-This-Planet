using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 충격의 세 단계를 검증한다(기획 §20.1, S15P21B202-247).
    ///
    /// 예전에는 세 단계가 <b>모두 같았다.</b> <c>DamageSource</c> 의 "닿으면 25" 하나뿐이고
    /// <see cref="Tuning.MaxHealth"/> 가 100 이라 <b>네 번</b>을 맞아야 기절했다 — §20.1 은
    /// 직접 충돌 <b>한 번</b>으로 기절한다고 정한다.
    /// </summary>
    public sealed class MeteorImpactTierTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;
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

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 경계 (순수 계산) ---------------------------------------------

        [Test]
        public void OutsideTheRadius_IsNothing()
        {
            Assert.AreEqual(MeteorImpactTier.None, MeteorImpact.TierFor(2.1f, 2f));
        }

        [Test]
        public void TheOuterHalfIsAShockwave_TheInnerHalfIsHeavy()
        {
            // 반경 2m → 경계는 1m
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(0f, 2f));
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(1f, 2f));
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.01f, 2f));
            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(2f, 2f));
        }

        /// <summary>중형은 반경이 넓으니 강한 충격 범위도 함께 넓어져야 한다.</summary>
        [Test]
        public void AMediumMeteorHasAWiderHeavyZone()
        {
            var small = PhysicsScale.MeteorSmallImpactRadius;
            var medium = PhysicsScale.MeteorMediumImpactRadius;

            Assert.AreEqual(MeteorImpactTier.Shockwave, MeteorImpact.TierFor(1.5f, small));
            Assert.AreEqual(MeteorImpactTier.Heavy, MeteorImpact.TierFor(1.5f, medium));
        }

        /// <summary>
        /// 예전에는 힘이 900 하나였다 — 크기를 나눠 놓고(S15P21B202-234) 중형은
        /// <b>맞는 범위만 넓고 아프지는 않았다.</b>
        /// </summary>
        [Test]
        public void AMediumMeteorHitsHarderThanASmallOne()
        {
            var small = MeteorImpact.ForceFor(MeteorSize.Small);
            var medium = MeteorImpact.ForceFor(MeteorSize.Medium);

            Assert.AreEqual(PhysicsScale.MeteorImpactForce, small, 0.01f,
                "소형이 기준값이다.");
            Assert.Greater(medium, small);
            Assert.AreEqual(small * (PhysicsScale.MeteorMediumImpactRadius
                                     / PhysicsScale.MeteorSmallImpactRadius),
                            medium, 0.01f,
                "반경 비율만큼 커진다 — 상수를 따로 두지 않는다.");
        }

        [Test]
        public void SettingTheSize_ChangesBothRadiusAndForce()
        {
            var meteor = MakeMeteor(Vector3.up * 5f);
            meteor.SetSize(MeteorSize.Medium);

            Assert.AreEqual(PhysicsScale.MeteorMediumImpactRadius, meteor.ImpactRadius, 0.01f);

            // 힘은 비공개다. 실제로 더 세게 밀리는지로 본다.
            var near = MakeProp(new Vector3(1.6f, 0.5f, 0f));
            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(near.linearVelocity.magnitude, 0.1f,
                "중형의 넓은 반경 안에 있으므로 밀려야 한다.");
        }

        // --- 직접 충돌 ----------------------------------------------------

        /// <summary>기획 §20.1 "운석 직접 충돌: 날아간 뒤 기절".</summary>
        [Test]
        public void ADirectHit_DownsThePlayerInOneGo()
        {
            var player = MakePlayer(new Vector3(0f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(new Vector3(0f, 1f, 0f), player);

            Assert.IsTrue(player.Downed, "한 번에 기절해야 한다.");
        }

        /// <summary>
        /// 순서가 중요하다. <c>GoDown</c> 이 먼저 돌면 <c>KnockDown</c> 이 <c>Downed</c> 를
        /// 보고 즉시 돌아나가 <b>날아가지 않는다</b> — §20.1 은 "날아간 뒤 기절" 이다.
        /// </summary>
        [Test]
        public void ADirectHit_FliesBeforeItFalls()
        {
            var player = MakePlayer(new Vector3(2f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(2f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsTrue(player.Downed);
            Assert.Greater(BodySpeed(player), 0.1f, "기절만 하고 멈춰 있으면 안 된다.");
        }

        /// <summary>
        /// 직접 맞은 사람은 <b>반경 밖에 있을 수도 있다</b> — 떨어지는 중에 얻어맞으면
        /// 착탄 지점이 그 사람 발밑이 아니다.
        /// </summary>
        [Test]
        public void ADirectHitFarFromTheImpactPoint_StillDowns()
        {
            var player = MakePlayer(new Vector3(20f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(20f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsTrue(player.Downed, "거리로 판정하면 이 경우를 놓친다.");
        }

        // --- 강한 충격 ----------------------------------------------------

        /// <summary>기획 §20.1 "강한 충격: 크게 날아가고 들고 있던 장비를 떨어뜨림".</summary>
        [Test]
        public void AHeavyShock_ThrowsAndDropsButDoesNotDown()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));   // 경계 1m 안
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed, "강한 충격은 기절이 아니다.");
            Assert.IsTrue(player.Ragdolled, "크게 날아가야 한다.");
            Assert.Greater(BodySpeed(player), 0.1f);
        }

        /// <summary>
        /// 예전에는 놓는 코드가 <c>GoDown</c> 안에만 있어서 <b>기절하지 않으면 장비를
        /// 떨어뜨릴 수가 없었다.</b>
        /// </summary>
        [Test]
        public void AHeavyShock_LetsGoOfTheHeldTool()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            player.DropEquipment();   // 진입점이 열려 있는지부터 확인한다
            Assert.IsFalse(player.Downed, "장비를 놓는 것이 기절을 부르지는 않는다.");

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(player.Downed);
        }

        // --- 약한 충격파 --------------------------------------------------

        /// <summary>기획 §20.1 "약한 운석 충격파: 밀려나지만 바로 행동 가능".</summary>
        [Test]
        public void AWeakShockwave_PushesButLeavesYouInControl()
        {
            var player = MakePlayer(new Vector3(1.6f, 0f, 0f));   // 경계 1m 밖, 반경 2m 안
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed);
            Assert.IsFalse(player.Ragdolled, "바로 행동 가능해야 한다.");
            Assert.Greater(BodySpeed(player), 0f, "밀려나기는 한다.");
        }

        [Test]
        public void OutsideTheRadius_NothingHappens()
        {
            var player = MakePlayer(new Vector3(6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Downed);
            Assert.IsFalse(player.Ragdolled);
        }

        // --- 착탄 순간에만 (기획 §19) --------------------------------------

        /// <summary>
        /// §19 "충돌 <b>순간에만</b> 넉백과 기절 판정이 있다". 예전에는
        /// <c>DamageSource</c> 가 0.5초마다 다시 때리고 운석이 6초 뒤에 사라져
        /// <b>박힌 운석 옆에 서 있으면 열두 번 맞았다.</b>
        /// </summary>
        [Test]
        public void AfterImpact_TheMeteorStopsHurting()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            Assert.IsTrue(meteor.HasImpacted);

            // 착탄한 자리에서 6초를 버틴다. 예전 반복 주기(0.5초)의 열두 배다.
            for (var i = 0; i < 600; i++) meteor.Tick(Step);

            Assert.IsFalse(player.Downed, "두 번째 판정이 없어야 한다.");
        }

        [Test]
        public void ASecondImpactCallIsIgnored()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            meteor.ApplyImpact(Vector3.zero, player);   // 두 번째 — 직접 충돌이라도

            Assert.IsFalse(player.Downed, "착탄은 한 번만 처리한다.");
        }

        // --- 쳐낸 운석 ----------------------------------------------------

        /// <summary>
        /// <b>쳐낸 순간부터 순해진다</b>(S15P21B202-273).
        ///
        /// <b>기획 §18.2 를 어긴다.</b> §18.2 는 "반사된 운석의 충돌과 폭발은 일반
        /// 운석과 같은 피해를 준다" 고 정하고, 이 검증이 원래 그것을 지켰다.
        ///
        /// 2026-09-02 에 운석을 두 상태로 나누기로 정했다 — 쳐낸 것은 <b>굴러다니며
        /// 때릴 수 있는 소품</b>, 못 쳐낸 것은 <b>박힌 흔적</b>이고 둘 다 사람을 때리지
        /// 않는다. 그러지 않으면 방망이로 쳐낸 운석이 날아가다 동료를 기절시켜,
        /// <b>잘한 플레이가 팀을 해친다.</b> [Plan] 확인이 필요하다.
        /// </summary>
        [Test]
        public void ADeflectedMeteor_StopsHurting()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);
            Assert.IsTrue(meteor.WasDeflected);
            Assert.IsTrue(meteor.Harmless, "맞은 순간부터 소품이다.");

            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.IsFalse(player.Ragdolled, "쳐낸 운석은 동료를 때리지 않는다.");
            Assert.IsFalse(player.Downed);
        }

        // --- 기절한 사람 --------------------------------------------------

        /// <summary>
        /// §20.1 "기절한 플레이어도 다른 물체의 충격으로 다시 밀릴 수 있다".
        /// <see cref="GameLayers.PlayerMask"/> 에 <c>PlayerDowned</c> 가 들어 있어
        /// <b>이미 동작한다</b> — 이 검증은 그것이 깨지지 않았다는 방어선이다.
        /// </summary>
        [Test]
        public void ADownedPlayer_CanStillBePushed()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            player.GoDown("검증");

            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.ApplyImpact(Vector3.zero);
            UnityEngine.Physics.Simulate(Step);

            Assert.Greater(BodySpeed(player), 0f, "쓰러진 사람도 밀린다.");
        }

        /// <summary>한 사람의 뼈가 여러 개 걸려도 처리는 한 번이다.</summary>
        [Test]
        public void ARagdollIsHandledOncePerPlayer()
        {
            var player = MakePlayer(new Vector3(0.6f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(player.Downed,
                "뼈마다 강한 충격을 겹쳐 넣으면 기절로 넘어가 버린다.");
        }

        // --- 리그 ---------------------------------------------------------

        static float BodySpeed(PlayerRig player)
        {
            var fastest = 0f;
            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var body = player.Doll.Bodies[i];
                if (body == null) continue;
                fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            }

            return fastest;
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

        Rigidbody MakeProp(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "화물 상자";
            go.layer = GameLayers.Prop;
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 20f;
            body.useGravity = false;

            UnityEngine.Physics.SyncTransforms();
            return body;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
