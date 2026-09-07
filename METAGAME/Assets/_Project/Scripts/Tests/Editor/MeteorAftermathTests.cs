using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석이 날아간 뒤의 두 상태를 검증한다.
    ///
    /// <code>
    /// 방망이로 쳐냄  →  굴러다니는 소품   동적 · Prop 레이어 · 밀린다
    /// 바닥에 닿음    →  운반 가능한 소품  동적 · Prop 레이어 · 집고 밀 수 있다
    /// </code>
    ///
    /// <b>증상은 즉사 지뢰였다.</b> 바닥에 놓인 운석에 닿기만 해도 기절했다 — 접점 법선이
    /// 문턱을 넘지 못해 정착에 실패하고, 살아 있는 동적 물체로 누워 사람이 닿을 때마다
    /// 직접 충돌로 처리됐다.
    ///
    /// <b>이 경로에 "정착한 운석이 사람을 때리지 않는다" 는 검증이 없어서</b> 인게임까지
    /// 갔다. 그것이 이 스위트의 첫 묶음이다.
    /// </summary>
    public sealed class MeteorAftermathTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 증상: 정착한 운석은 때리지 않는다 -----------------------------

        [Test]
        public void ASettledMeteorIsHarmless()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            Assert.IsTrue(meteor.Settled);
            Assert.IsTrue(meteor.Harmless, "닿아도 데미지를 못 주는 물건이어야 한다.");
        }

        /// <summary>밟아도 기절하지 않는다 — 이 이슈의 증상이다.</summary>
        [Test]
        public void SteppingOnASettledMeteor_DoesNotKnockYouOut()
        {
            var player = MakePlayer(new Vector3(0.3f, 0f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));

            meteor.ApplyImpact(Vector3.zero);
            Assert.IsFalse(player.Downed, "착탄 순간에 옆에 있었어도 여기서는 반경 밖이다.");

            // 정착한 뒤 다시 착탄을 시도해도 아무 일이 없어야 한다.
            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsFalse(player.Downed, "정착한 운석은 사람을 때리지 않는다.");
            Assert.AreEqual(Tuning.MaxHealth, player.Health, 1e-3f);
        }

        /// <summary>
        /// 여러 개를 밟아도 마찬가지다. <c>_directPlayersHit</c> 가드는 <b>운석 하나당
        /// 사람 하나</b>라, 바닥에 깔린 운석이 여러 개면 각각 한 번씩 때렸다.
        /// </summary>
        [Test]
        public void SteppingOnManySettledMeteors_DoesNotKnockYouOut()
        {
            var player = MakePlayer(new Vector3(20f, 0f, 0f));

            for (var i = 0; i < 8; i++)
            {
                var meteor = MakeMeteor(new Vector3(i, 4f, 0f));
                meteor.ApplyImpact(new Vector3(i, 0f, 0f));
                meteor.ApplyImpact(new Vector3(i, 0f, 0f), player);
            }

            Assert.IsFalse(player.Downed);
            Assert.AreEqual(Tuning.MaxHealth, player.Health, 1e-3f);
        }

        // --- 살려야 하는 것 -------------------------------------------------

        /// <summary>기획 §20.1 "운석 직접 충돌: 날아간 뒤 기절". 고치면서 죽이면 안 된다.</summary>
        [Test]
        public void AFallingMeteorStillDownsYouOnADirectHit()
        {
            var player = MakePlayer(Vector3.zero);
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            Assert.IsFalse(meteor.Harmless, "떨어지는 중에는 위험해야 한다.");

            meteor.ApplyImpact(new Vector3(0f, 1f, 0f), player);

            Assert.IsTrue(player.Downed, "§20.1 을 죽이면 안 된다.");
        }

        // --- 쳐낸 소품 ------------------------------------------------------

        [Test]
        public void DeflectingMakesItARollingProp()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            Assert.IsTrue(meteor.WasDeflected);
            Assert.IsTrue(meteor.Rolling);
            Assert.IsFalse(meteor.Settled, "박히지 않는다 — 굴러다녀야 한다.");
        }

        [Test]
        public void ARollingPropStaysDynamic()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            var body = meteor.GetComponent<Rigidbody>();

            Assert.IsFalse(body.isKinematic, "굴러다녀야 하고 방망이로 밀려야 한다.");
        }

        /// <summary>
        /// Prop 레이어로 옮기는 것이 "때릴 수 있는 소품" 의 실제 의미다.
        /// <see cref="GameLayers.BatStrikeMask"/> 가 Meteor 를 빼고 Prop 은 포함한다
        /// (S15P21B202-257).
        /// </summary>
        [Test]
        public void ARollingPropMovesToThePropLayer()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            Assert.AreEqual(GameLayers.Meteor, meteor.gameObject.layer);

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            Assert.AreEqual(GameLayers.Prop, meteor.gameObject.layer);
            Assert.AreNotEqual(0, GameLayers.BatStrikeMask & (1 << meteor.gameObject.layer),
                "방망이 탐색 범위에 들어와야 한다.");
        }

        [Test]
        public void TheWholeObjectMovesLayers_NotJustTheRoot()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            var child = new GameObject("Visual") { layer = GameLayers.Meteor };
            child.transform.SetParent(meteor.transform, false);

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            Assert.AreEqual(GameLayers.Prop, child.layer,
                "자식이 남으면 방망이 탐색이 옛 레이어로 걸린다.");
        }

        /// <summary>
        /// <b>기획 §18.2 를 어긴다.</b> "반사 운석의 충돌과 폭발은 일반 운석과 같은 피해를
        /// 준다" 고 정하는데, 2026-09-02 에 "방망이에 친 순간 데미지를 못 주는 물건이
        /// 된다" 로 정했다. 쳐낸 것이 동료를 때리면 방망이가 구제가 아니라 위험 전가가
        /// 되므로 협동에는 이쪽이 낫다 — [Plan] 확인이 필요하다.
        /// </summary>
        [Test]
        public void ADeflectedMeteorIsHarmlessInFlight()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));

            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            Assert.IsTrue(meteor.Harmless, "친 순간부터 데미지를 못 준다.");
        }

        [Test]
        public void ARollingPropDoesNotBecomeAScar()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            // 착탄을 시도해도 박히지 않는다.
            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(meteor.Settled);
            Assert.IsTrue(meteor.Rolling);
        }

        // --- 착지 후 운반물 -------------------------------------------------

        [Test]
        public void ASettledMeteorBecomesACarryableProp()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            var body = meteor.GetComponent<Rigidbody>();

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsTrue(meteor.GroundProp, "착지한 운석은 일반 운반물이어야 한다.");
            Assert.IsFalse(body.isKinematic, "착지 후에도 밀고 운반할 수 있어야 한다.");
            Assert.IsTrue(body.useGravity, "일반 운반물처럼 중력을 받아야 한다.");
            Assert.IsFalse(meteor.Rolling);
            Assert.AreEqual(GameLayers.Prop, meteor.gameObject.layer);
            Assert.IsNotNull(meteor.GetComponent<Grabbable>(),
                "손 운반 경로에 들어가야 한다.");
            Assert.GreaterOrEqual(meteor.transform.position.y, meteor.ImpactPoint.y,
                "착지한 운반물은 지면 아래로 묻히면 안 된다.");
        }

        [Test]
        public void ASettledMeteorCanBePushedByTheBat()
        {
            var meteor = MakeMeteor(new Vector3(1f, 4f, 0f));
            meteor.ApplyImpact(new Vector3(1f, 0f, 0f));

            var batObject = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(batObject);
            var bat = batObject.AddComponent<MeteorBat>();

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.right),
                "착지한 운석은 일반 소품처럼 방망이에 맞아야 한다.");
            Assert.AreSame(meteor.GetComponent<Rigidbody>(), bat.LastPushed);
        }

        [Test]
        public void ASettledMeteorHasNoLegacyDamageSource()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            var legacy = meteor.gameObject.AddComponent<DamageSource>();

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(legacy.enabled,
                "운석의 착탄 피해와 일반 DamageSource가 중복되면 착지 후 접촉 피해가 생긴다.");
        }

        [Test]
        public void AScarKeepsItsColliderSoYouCanStandOnIt()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            var collider = meteor.GetComponent<Collider>();

            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.enabled, "위에 올라갈 수 있어야 한다.");
        }

        // --- 소멸 (기획 §19 를 어긴다) --------------------------------------

        /// <summary>
        /// §19 는 "스테이지 동안 사라지지 않는다" 고 정하고 S15P21B202-251 이 그 근거로
        /// 소멸을 지웠다. 2026-09-02 에 둘 다 사라지게 하기로 정했다 — [Plan] 확인 필요.
        /// </summary>
        [Test]
        public void ARollingPropDespawns()
        {
            var meteor = MakeMeteor(new Vector3(0f, 5f, 0f));
            meteor.Deflect(Vector3.right * PhysicsScale.BatDeflectSpeed);

            var expired = false;
            meteor.Expired += _ => expired = true;

            Advance(meteor, PhysicsScale.MeteorDeflectedDespawnSeconds - 0.5f, ref expired);
            Assert.IsFalse(expired, "너무 빨리 사라지면 쳐낸 보람이 없다.");

            Advance(meteor, 1f, ref expired);
            Assert.IsTrue(expired);
        }

        /// <summary>
        /// 쳐낸 소품이 흔적보다 <b>먼저</b> 사라진다. 플레이어가 처리한 것이므로 치워지는
        /// 편이 맞고, 굴러다니는 동적 물체가 조립 구역에 오래 남으면 방해가 된다.
        /// </summary>
        [Test]
        public void TheRollingPropGoesBeforeTheScar()
        {
            Assert.Less(PhysicsScale.MeteorDeflectedDespawnSeconds,
                        PhysicsScale.MeteorScarDespawnSeconds);
        }

        /// <summary>
        /// 흔적이 <b>식는 연출이 끝난 뒤에</b> 사라진다. §19 "약 5초 동안 붉은 빛, 연기와
        /// 불꽃이 약해진다" 가 화면에 보이지 않으면 의미가 없다.
        /// </summary>
        [Test]
        public void TheScarOutlivesItsCooldown()
        {
            Assert.Greater(PhysicsScale.MeteorScarDespawnSeconds,
                           PhysicsScale.MeteorScarCoolSeconds);
        }

        // --- 리그 ---------------------------------------------------------

        static void Advance(Meteor meteor, float seconds, ref bool expired)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps && !expired; i++) meteor.Tick(Step);
        }

        Meteor MakeMeteor(Vector3 position)
        {
            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = position;
            _spawned.Add(go);

            go.AddComponent<SphereCollider>().radius = PhysicsScale.MeteorRadius;

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
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
