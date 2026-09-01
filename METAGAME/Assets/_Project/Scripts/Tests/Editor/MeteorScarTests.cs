using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 충돌 흔적을 검증한다(기획 §19, S15P21B202-251).
    ///
    /// <b>예전에는 정확히 반대였다.</b> <c>MeteorDespawnSeconds = 6f</c> 로 착탄 6초 뒤에
    /// <c>Destroy</c> 했고, 검증도 "착탄한 운석이 남아 쌓이면 안 된다" 를 잠갔다. §19 는
    /// "박힌 운석과 분화구는 발사 실패 후에도 유지되며 <b>스테이지 동안 사라지지
    /// 않는다</b>" 고 정한다.
    /// </summary>
    public sealed class MeteorScarTests
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

        // --- 남는다 (§19) -------------------------------------------------

        [Test]
        public void ImpactSettlesTheMeteor()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));

            Assert.IsFalse(meteor.Settled);

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsTrue(meteor.Settled);
        }

        /// <summary>§19 "운석 본체는 <b>절반 이상</b> 지면에 박힌 모습으로 남는다".</summary>
        [Test]
        public void ASettledMeteorIsBuriedMoreThanHalfway()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            var top = meteor.transform.position.y + PhysicsScale.MeteorRadius;
            var bottom = meteor.transform.position.y - PhysicsScale.MeteorRadius;
            var above = top;                       // 지면이 y=0 이다
            var height = top - bottom;

            Assert.Less(above / height, 0.5f, "절반 이상 묻혀야 한다.");
            Assert.Greater(above, 0f, "완전히 사라지면 장애물이 못 된다(§19).");
        }

        /// <summary>§19 "충돌 후 <b>고비용 물리</b> … 를 끄고".</summary>
        [Test]
        public void ASettledMeteorHasItsPhysicsTurnedOff()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            var body = meteor.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0f, -6f, 0f);

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsTrue(body.isKinematic, "200개가 쌓여도 솔버가 풀 것이 없어야 한다.");
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 1e-3f);
        }

        /// <summary>§19 "접촉 피해가 없는 <b>낮은 정적 장애물</b>" — 콜라이더는 남는다.</summary>
        [Test]
        public void ASettledMeteorKeepsItsCollider()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            var collider = meteor.GetComponent<Collider>();

            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.enabled, "걸려서 넘어갈 수 있어야 한다.");
        }

        [Test]
        public void ASettledMeteorIgnoresFurtherTicks()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            var expired = false;
            meteor.Expired += _ => expired = true;

            var where = meteor.transform.position;

            // 예전 소멸 시간(6초)과 수명 상한을 모두 훌쩍 넘긴다.
            for (var i = 0; i < 12000; i++) meteor.Tick(Step);

            Assert.IsFalse(expired, "흔적은 스테이지 동안 남는다(§19).");
            Assert.AreEqual(where, meteor.transform.position);
        }

        // --- 스포너의 흔적 목록 --------------------------------------------

        [Test]
        public void TheSpawnerMovesSettledMeteorsFromLiveToScars()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            Assert.AreEqual(1, spawner.Live.Count);
            Assert.AreEqual(0, spawner.Scars.Count);

            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);

            Assert.AreEqual(0, spawner.Live.Count, "더 틱하지 않는다.");
            Assert.AreEqual(1, spawner.Scars.Count);
            Assert.AreSame(meteor, spawner.Scars[0].Meteor);
        }

        [Test]
        public void AScarRemembersWhereAndHowBig()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(3f, 4f, -2f));
            meteor.SetSize(MeteorSize.Medium);
            Attach(spawner, meteor);

            meteor.ApplyImpact(new Vector3(3f, 0f, -2f));
            spawner.Tick(Step);

            var scar = spawner.Scars[0];

            Assert.AreEqual(3f, scar.Point.x, 0.01f);
            Assert.AreEqual(-2f, scar.Point.z, 0.01f);
            Assert.AreEqual(PhysicsScale.MeteorMediumImpactRadius, scar.Radius, 0.01f);
        }

        // --- 5초 감쇠 (§19) -----------------------------------------------

        [Test]
        public void AScarCoolsOverFiveSeconds()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);

            var scar = spawner.Scars[0];
            Assert.IsFalse(scar.Cooled);

            Advance(spawner, PhysicsScale.MeteorScarCoolSeconds * 0.5f);
            Assert.IsFalse(scar.Cooled, "절반에서는 아직 식는 중이다.");

            Advance(spawner, PhysicsScale.MeteorScarCoolSeconds);
            Assert.IsTrue(scar.Cooled);
        }

        /// <summary>
        /// 식은 뒤에는 나이를 <b>더 세지 않는다.</b> 150~200개가 쌓여도 매 프레임 도는
        /// 비용이 0이 되어야 한다 — 그것이 §19 의 성능 기준이다.
        /// </summary>
        [Test]
        public void ACooledScarStopsAging()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);

            Advance(spawner, PhysicsScale.MeteorScarCoolSeconds + 1f);
            var settledAge = spawner.Scars[0].Age;

            Advance(spawner, 30f);

            Assert.AreEqual(settledAge, spawner.Scars[0].Age, 1e-3f);
        }

        [Test]
        public void TheColorGoesFromHotToCold()
        {
            var hot = MeteorScarLook.ColorOf(0f);
            var cold = MeteorScarLook.ColorOf(PhysicsScale.MeteorScarCoolSeconds);

            Assert.AreEqual(MeteorScarLook.HotColor, hot);
            Assert.AreEqual(MeteorScarLook.ColdColor, cold);
            Assert.Greater(hot.r, cold.r, "붉은 빛이 약해진다(§19).");
        }

        [Test]
        public void TheColorStopsChangingAfterItCools()
        {
            var a = MeteorScarLook.ColorOf(PhysicsScale.MeteorScarCoolSeconds);
            var b = MeteorScarLook.ColorOf(PhysicsScale.MeteorScarCoolSeconds * 10f);

            Assert.AreEqual(a, b);
        }

        /// <summary>
        /// 분화구 지름이 충격 반경과 같다. 낙하 예고(§17.2 "실제 충격 범위와 같은 크기")와
        /// 같은 값이어야 맞은 자리와 예고가 어긋나 보이지 않는다.
        /// </summary>
        [Test]
        public void TheCraterMatchesTheImpactRadius()
        {
            Assert.AreEqual(PhysicsScale.MeteorSmallImpactRadius * 2f,
                            MeteorScarLook.DiameterOf(PhysicsScale.MeteorSmallImpactRadius),
                            0.01f);
        }

        // --- 개수 상한 (§19 의 성능 기준) -----------------------------------

        /// <summary>
        /// §19 가 150~200개를 성능 기준으로 삼는다. 넘으면 가장 오래된 것부터 지우고
        /// <b>로그를 남긴다</b> — 조용히 잘라내면 "다 남았다" 로 보인다.
        /// </summary>
        [Test]
        public void PastTheLimit_TheOldestScarsGoAndItIsReported()
        {
            var spawner = MakeSpawner();

            var first = SettleOne(spawner, new Vector3(-50f, 0f, 0f));

            for (var i = 0; i < PhysicsScale.MeteorScarLimit; i++)
                SettleOne(spawner, new Vector3(i, 0f, 0f));

            Assert.AreEqual(PhysicsScale.MeteorScarLimit, spawner.Scars.Count,
                "상한을 넘지 않는다.");
            Assert.Greater(spawner.ScarsTrimmed, 0, "잘라낸 것을 보고한다.");
            Assert.IsTrue(first == null, "가장 오래된 것이 실제로 지워진다.");
        }

        [Test]
        public void UnderTheLimit_NothingIsTrimmed()
        {
            var spawner = MakeSpawner();

            for (var i = 0; i < 10; i++) SettleOne(spawner, new Vector3(i, 0f, 0f));

            Assert.AreEqual(10, spawner.Scars.Count);
            Assert.AreEqual(0, spawner.ScarsTrimmed);
        }

        // --- 초기화 (§19 · §22.4) ------------------------------------------

        /// <summary>
        /// §22.4 "발사 실패는 스테이지 초기화가 아니다" · §19 "발사 실패 후에도 유지되며".
        /// <c>SetPaused</c> 는 예고만 끊는다.
        /// </summary>
        [Test]
        public void PausingForALaunchDoesNotEraseScars()
        {
            var spawner = MakeSpawner();
            SettleOne(spawner, Vector3.zero);

            spawner.SetPaused(true);
            Advance(spawner, 30f);

            Assert.AreEqual(1, spawner.Scars.Count, "발사 실패 뒤에도 남는다.");
        }

        /// <summary>
        /// 목록만 비우면 누출이다. 예전에는 운석이 6초 뒤 스스로 사라졌으니 문제가
        /// 없었는데, 남기기 시작하면 씬에 영영 쌓인다.
        /// </summary>
        [Test]
        public void ClearingScarsActuallyDestroysThem()
        {
            var spawner = MakeSpawner();
            var meteor = SettleOne(spawner, Vector3.zero);

            spawner.ClearScars();

            Assert.AreEqual(0, spawner.Scars.Count);
            Assert.IsTrue(meteor == null, "목록만 비우면 누출이다.");
        }

        [Test]
        public void ResettingTheRunDestroysBothLiveAndScars()
        {
            var spawner = MakeSpawner();

            var settled = SettleOne(spawner, Vector3.zero);

            var falling = MakeMeteor(new Vector3(10f, 8f, 0f));
            Attach(spawner, falling);

            spawner.ResetRun();

            Assert.AreEqual(0, spawner.Scars.Count);
            Assert.AreEqual(0, spawner.Live.Count);
            Assert.IsTrue(settled == null, "흔적이 파괴돼야 한다.");
            Assert.IsTrue(falling == null, "떨어지던 것도 파괴돼야 한다.");
        }

        [Test]
        public void AfterClearing_NewImpactsStillWork()
        {
            var spawner = MakeSpawner();
            SettleOne(spawner, Vector3.zero);
            spawner.ClearScars();

            SettleOne(spawner, new Vector3(5f, 0f, 0f));

            Assert.AreEqual(1, spawner.Scars.Count);
            Assert.AreEqual(0, spawner.ScarsTrimmed, "초기화가 카운터도 되돌린다.");
        }

        // --- 리그 ---------------------------------------------------------

        static void Advance(MeteorSpawner spawner, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++) spawner.Tick(Step);
        }

        /// <summary>운석 하나를 만들어 스포너에 넣고 착탄시킨다.</summary>
        Meteor SettleOne(MeteorSpawner spawner, Vector3 point)
        {
            var meteor = MakeMeteor(point + Vector3.up * 4f);
            Attach(spawner, meteor);

            meteor.ApplyImpact(point);
            spawner.Tick(Step);

            return meteor;
        }

        /// <summary>
        /// 스포너의 <c>_live</c> 는 비공개다. 실제 경로(예고 → 발사)를 돌리는 대신
        /// 리플렉션으로 넣는다 — 이 스위트가 보는 것은 <b>착탄 뒤</b>다.
        /// </summary>
        static void Attach(MeteorSpawner spawner, Meteor meteor)
        {
            var field = typeof(MeteorSpawner).GetField(
                "_live", System.Reflection.BindingFlags.Instance
                         | System.Reflection.BindingFlags.NonPublic);

            var live = (List<Meteor>)field.GetValue(spawner);
            live.Add(meteor);
        }

        MeteorSpawner MakeSpawner()
        {
            var go = new GameObject("MeteorSpawner");
            _spawned.Add(go);

            var spawner = go.AddComponent<MeteorSpawner>();
            spawner.Configure(20260902);

            // 새 운석이 끼어들어 개수 판정을 흐리지 않게 스폰을 멈춘다.
            spawner.SetPaused(true);
            return spawner;
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
    }
}
