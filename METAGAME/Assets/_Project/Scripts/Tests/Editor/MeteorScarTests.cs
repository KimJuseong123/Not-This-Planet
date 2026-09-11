using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 착탄 기록과 충돌 흔적을 검증한다(기획 §19, S15P21B202-251).
    ///
    /// 착탄한 운석은 일반 운반물로 전환되고, 스포너는 분화구 연출에 사용할 착탄 기록을
    /// 별도로 유지한다.
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

        /// <summary>착지한 운석은 지면 위의 운반 가능한 물체로 남는다.</summary>
        [Test]
        public void ASettledMeteorStaysAboveTheGround()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));

            meteor.ApplyImpact(Vector3.zero);

            var top = meteor.transform.position.y + PhysicsScale.MeteorRadius;
            var bottom = meteor.transform.position.y - PhysicsScale.MeteorRadius;
            var above = top;                       // 지면이 y=0 이다
            var height = top - bottom;

            Assert.AreEqual(1f, above / height, 1e-3f,
                "운반 가능한 운석은 지면 아래로 묻히지 않아야 한다.");
            Assert.Greater(above, 0f, "운석 전체가 지면 위에 있어야 한다.");
        }

        /// <summary>착지 후에도 일반 소품 물리를 유지한다.</summary>
        [Test]
        public void ASettledMeteorKeepsDynamicPhysics()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            var body = meteor.GetComponent<Rigidbody>();
            body.linearVelocity = new Vector3(0f, -6f, 0f);

            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(body.isKinematic, "착지 후에도 방망이와 운반 물리로 움직여야 한다.");
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 1e-3f);
        }

        /// <summary>착지 후에도 운반물 콜라이더는 남는다.</summary>
        [Test]
        public void ASettledMeteorKeepsItsCollider()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            var collider = meteor.GetComponent<Collider>();

            Assert.IsNotNull(collider);
            Assert.IsTrue(collider.enabled, "걸려서 넘어갈 수 있어야 한다.");
        }

        /// <summary>
        /// <b>이 검증이 뒤집혔다.</b> S15P21B202-251 은 "흔적은 스테이지 동안 남는다"
        /// (기획 §19)를 잠갔는데, 2026-09-02 에 둘 다 사라지게 하기로 정했다 —
        /// §19 를 어기므로 [Plan] 확인이 필요하다(S15P21B202-273).
        /// </summary>
        [Test]
        public void ASettledMeteorDespawnsAfterItsTime()
        {
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            meteor.ApplyImpact(Vector3.zero);

            var expired = false;
            meteor.Expired += _ => expired = true;

            // 분화구가 식는 동안에는 남아 있어야 한다.
            for (var i = 0; i < Mathf.CeilToInt(PhysicsScale.MeteorScarCoolSeconds / Step); i++)
                meteor.Tick(Step);
            Assert.IsFalse(expired, "식는 연출이 끝나기 전에 사라지면 안 된다.");

            var guard = 0;
            while (!expired && guard++ < 200000) meteor.Tick(Step);

            Assert.IsTrue(expired);
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
        /// <b>흔적이 실제로 목록에서 빠진다.</b>
        ///
        /// !347 AI 리뷰가 물은 것 — <c>Faded</c> 경계에서 <c>Age</c> 증가가 멈추는데
        /// 소멸 판정이 그 값에 의존하면 부등호 하나로 영구히 남을 수 있다.
        ///
        /// 실제로는 두 타이머가 <b>별개다.</b> <c>Scar.Age</c> 는
        /// <c>MeteorSpawner.AdvanceScars</c> 가 세고 표시에만 쓰이며,
        /// 소멸은 <c>Meteor.Tick</c> 의 <c>_sinceSettled</c> 가 따로 센다 —
        /// 그쪽은 <c>Faded</c> 와 무관하게 계속 늘어난다. 이 검증이 그 사실을 잠근다.
        /// </summary>
        [Test]
        public void TheScarLeavesTheListWhenItsLifetimeEnds()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);
            Assert.AreEqual(1, spawner.Scars.Count, "흔적이 생겼다.");

            // 다 옅어진 뒤에도 나이 세기는 멈추지만 수명은 따로 흐른다.
            Advance(spawner, PhysicsScale.MeteorScarCoolSeconds
                             + PhysicsScale.MeteorScarFadeSeconds + 0.5f);
            Assert.IsTrue(spawner.Scars.Count == 0 || spawner.Scars[0].Faded,
                "이 시점에는 보이지 않는다.");

            Advance(spawner, PhysicsScale.MeteorScarDespawnSeconds + 1f);

            Assert.AreEqual(0, spawner.Scars.Count,
                "수명이 끝나면 목록에서 빠진다 — 나이가 멈춰도 소멸은 따로 흐른다.");
        }

        /// <summary>
        /// 식은 뒤에도 <b>옅어지는 동안은</b> 나이를 센다(S15P21B202-438).
        ///
        /// <b>예전에는 식으면 멈췄다</b> — 150~200개가 쌓여도 매 프레임 비용이 0 이
        /// 되어야 한다는 §19 의 성능 기준 때문이었다. 그런데 그러면 알파가 0.5 에서
        /// 굳어 <b>검은 원이 지면에 박혀 남는다.</b> 멈추는 지점을
        /// <c>Cooled</c> 에서 <c>Faded</c> 로 옮겨, 화면에서 사라진 뒤부터 건너뛴다 —
        /// 성능 기준은 그대로 지킨다.
        /// </summary>
        [Test]
        public void ACoolingScarKeepsAgingWhileItFades()
        {
            var spawner = MakeSpawner();
            var meteor = MakeMeteor(new Vector3(0f, 4f, 0f));
            Attach(spawner, meteor);

            meteor.ApplyImpact(Vector3.zero);
            spawner.Tick(Step);

            // 식은 직후. 예전에는 여기서 나이 세기가 멈췄다.
            Advance(spawner, PhysicsScale.MeteorScarCoolSeconds + 1f);
            var cooledAge = spawner.Scars[0].Age;
            Assert.IsTrue(spawner.Scars[0].Cooled, "이미 식었다.");
            Assert.IsFalse(spawner.Scars[0].Faded, "아직 옅어지는 중이다.");

            Advance(spawner, 1f);

            Assert.Greater(spawner.Scars[0].Age, cooledAge,
                "식은 뒤에도 옅어지는 동안은 나이가 늘어야 표시가 따라온다 "
                + "(S15P21B202-438). 예전에는 Cooled 에서 멈춰 알파가 0.5 로 굳었다.");
        }

        [Test]
        public void TheColorGoesFromHotToCold()
        {
            var hot = MeteorScarLook.ColorOf(0f);
            var cold = MeteorScarLook.ColorOf(PhysicsScale.MeteorScarCoolSeconds);

            Assert.AreEqual(MeteorScarLook.HotColor, hot);

            // 색조만 본다. 알파는 옅어지는 단계가 따로 다룬다(S15P21B202-438) —
            // 식는 순간에는 아직 1배지만 그 뒤 0 으로 빠진다.
            Assert.AreEqual(MeteorScarLook.ColdColor.r, cold.r, 1e-4f);
            Assert.AreEqual(MeteorScarLook.ColdColor.g, cold.g, 1e-4f);
            Assert.AreEqual(MeteorScarLook.ColdColor.b, cold.b, 1e-4f);
            Assert.Greater(hot.r, cold.r, "붉은 빛이 약해진다(§19).");
        }

        /// <summary>
        /// 식은 뒤에는 <b>색조가 굳고 알파만 빠진다</b>(S15P21B202-438).
        ///
        /// 예전에는 색이 통째로 굳었다 — 그래서 밝기 0.106 의 검은 원이 알파 0.5 로
        /// 지면에 박혀 남았다. 지금은 색조는 그대로 두고 투명해진다. 색조까지
        /// 흔들면 타다 남은 자리가 아니라 다른 것으로 보인다.
        /// </summary>
        [Test]
        public void AfterCooling_OnlyTheAlphaKeepsChanging()
        {
            var cool = PhysicsScale.MeteorScarCoolSeconds;
            var a = MeteorScarLook.ColorOf(cool);
            var b = MeteorScarLook.ColorOf(cool + PhysicsScale.MeteorScarFadeSeconds * 0.5f);

            Assert.AreEqual(a.r, b.r, 1e-4f, "색조는 굳는다.");
            Assert.AreEqual(a.g, b.g, 1e-4f);
            Assert.AreEqual(a.b, b.b, 1e-4f);
            Assert.Less(b.a, a.a, "알파는 계속 빠진다 — 검은 원이 남지 않는 이유다.");
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

            // **수명 안쪽으로 돌린다.** 예전에는 30초를 돌렸는데 그때는 흔적이
            // 사라지는 경로가 아예 없었다(S15P21B202-438 에서 드러났다). 이제는
            // 수명이 실제로 흐르므로 30초를 돌리면 수명 때문에 사라진다 — 그러면
            // 이 검사는 일시정지가 아니라 수명을 재는 것이 된다.
            //
            // 수명이 지우는 것은 TheScarLeavesTheListWhenItsLifetimeEnds 가 맡는다.
            Advance(spawner, PhysicsScale.MeteorScarDespawnSeconds - 1f);

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
