using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 운석 방망이를 검증한다(S15P21B202-233, 기획 §18).
    ///
    /// 지키려는 것은 넷이다.
    /// <list type="number">
    /// <item>낙하 예측이 맞는다 — 이것이 없으면 "착탄 1초 전" 을 알 수 없다.</item>
    /// <item>사거리·시각 조건이 맞을 때만 쳐낸다.</item>
    /// <item>쳐낸 운석이 조준의 높낮이를 포함한 시선 방향으로 날아간다.</item>
    /// <item>동료를 때리면 넘어지지만 <b>기절하지 않는다</b>.</item>
    /// </list>
    /// </summary>
    public sealed class MeteorBatTests
    {
        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            Meteor.All.Clear();
            PlayerRig.All.Clear();
        }

        // --- 낙하 예측 -----------------------------------------------------

        /// <summary>
        /// <b>이 이슈의 선행 작업이다.</b> 낙하 예고(<see cref="PendingMeteorDrop"/>)는
        /// 운석이 생기기 전의 시간이고, 운석은 예고가 끝난 뒤 30m 에서 생겨 더 떨어진다.
        /// 그래서 착탄 시각을 직접 풀어야 한다.
        /// </summary>
        [Test]
        public void FreeFallPrediction_MatchesTheClosedForm()
        {
            var gravity = UnityEngine.Physics.gravity.y;
            const float height = 30f;

            var meteor = MakeMeteor(new Vector3(0f, height, 0f), Vector3.zero);

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out var point));

            // t = sqrt(2h / |g|)
            var expected = Mathf.Sqrt(2f * height / -gravity);
            Assert.AreEqual(expected, seconds, 0.01f,
                $"중력 {gravity} 에서 {height}m 자유낙하는 {expected:0.00}초다.");

            Assert.AreEqual(0f, point.y, 1e-3f, "착탄 지점은 지면 높이여야 한다.");
            Assert.AreEqual(0f, point.x, 1e-3f);
        }

        [Test]
        public void HorizontalVelocity_MovesThePredictedPoint()
        {
            var meteor = MakeMeteor(new Vector3(0f, 30f, 0f), new Vector3(2f, 0f, 0f));

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out var point));
            Assert.AreEqual(seconds * 2f, point.x, 0.01f,
                "옆으로 흐르는 운석은 낙하 지점도 옆으로 간다.");
        }

        /// <summary>
        /// 올라가는 중이어도 <b>돌아올 시각</b>을 풀어야 한다. 쳐낸 운석이 그렇다.
        /// 위로 쏜 광선으로는 이 값을 얻을 수 없다 — 포물선이기 때문이다.
        /// </summary>
        [Test]
        public void RisingMeteor_PredictsWhenItComesBackDown()
        {
            var meteor = MakeMeteor(new Vector3(0f, 2f, 0f), new Vector3(0f, 10f, 0f));

            Assert.IsTrue(meteor.TryPredictImpact(out var seconds, out _));
            Assert.Greater(seconds, 0f, "올라가는 중이면 돌아올 시각이 나와야 한다.");

            // 올라갔다 내려오므로 그냥 떨어지는 것보다 오래 걸린다.
            var falling = MakeMeteor(new Vector3(5f, 2f, 0f), Vector3.zero);
            Assert.IsTrue(falling.TryPredictImpact(out var fallingSeconds, out _));
            Assert.Greater(seconds, fallingSeconds);
        }

        [Test]
        public void AnImpactedMeteor_PredictsNothing()
        {
            var meteor = MakeMeteor(new Vector3(0f, 1f, 0f), Vector3.zero);
            meteor.ApplyImpact(Vector3.zero);

            Assert.IsFalse(meteor.TryPredictImpact(out _, out _),
                "이미 착탄한 운석은 쳐낼 수 없다.");
        }

        // --- 타격 판정 -----------------------------------------------------

        [Test]
        public void AMeteorAboutToLandNearby_IsDeflected()
        {
            var meteor = MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();

            var hit = bat.Swing(Vector3.zero, Vector3.forward);

            Assert.IsTrue(hit, "사거리 안에서 착탄 직전이면 쳐내야 한다.");
            Assert.AreSame(meteor, bat.LastDeflected);
            Assert.IsTrue(meteor.WasDeflected);
            Assert.IsTrue(bat.LastSwingConnected);
        }

        [Test]
        public void AMeteorOutsideTheAssistRadius_IsMissed()
        {
            var far = new Vector3(PhysicsScale.BatAssistRadius + 1f, 0f, 0f);
            MakeMeteorAtWindow(far);
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward),
                "낙하 지점이 사거리 밖이면 헛스윙이다.");
            Assert.IsNull(bat.LastDeflected);
        }

        /// <summary>기획 §18.1 "너무 일찍 휘두르면 헛스윙하지만 다시 시도할 수 있다".</summary>
        [Test]
        public void SwingingTooEarly_Misses()
        {
            // 착탄까지 창(1초)보다 훨씬 많이 남은 높이에 둔다.
            var meteor = MakeMeteor(new Vector3(0f, 30f, 0f), Vector3.zero);
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward),
                "아직 멀었으면 헛스윙이다.");
            Assert.AreEqual(0, bat.RhythmGrade);
            Assert.IsFalse(meteor.WasDeflected);
            Assert.AreEqual(Vector3.zero, meteor.Body.linearVelocity);
        }

        [Test]
        public void AMissLocksTheBatForAWhile()
        {
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.AreEqual(PhysicsScale.BatMissCooldownSeconds, bat.Cooldown, 1e-3f);
            Assert.IsFalse(bat.CanSwing);

            // 대기 중에는 맞힐 수 있는 운석이 와도 휘두르지 못한다.
            MakeMeteorAtWindow(Vector3.zero);
            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward), "대기 중에는 휘두를 수 없다.");

            bat.Tick(PhysicsScale.BatMissCooldownSeconds);
            Assert.IsTrue(bat.CanSwing, "대기가 끝나면 다시 휘두를 수 있다.");
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));
        }

        /// <summary>맞히면 대기가 없다. 기획 §18.1 은 헛스윙에만 대기를 둔다.</summary>
        [Test]
        public void AHitLeavesNoCooldown()
        {
            MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.AreEqual(0f, bat.Cooldown, 1e-3f);
            Assert.IsTrue(bat.CanSwing);
        }

        /// <summary>
        /// 기획 §18.1 "같은 범위에 운석이 여러 개면 <b>가장 먼저 착탄할</b> 운석을 선택한다".
        /// 가장 가까운 것이 아니다 — 막아야 하는 것은 곧 떨어질 쪽이다.
        /// </summary>
        [Test]
        public void SeveralMeteors_TheSoonestIsChosen()
        {
            // 멀리 있지만 더 낮은(= 먼저 떨어질) 운석과, 가깝지만 더 높은 운석.
            var soonest = MakeMeteor(new Vector3(2f, 0.6f, 0f), Vector3.zero);
            var nearer = MakeMeteor(new Vector3(0.2f, 1.3f, 0f), Vector3.zero);

            Assert.IsTrue(soonest.TryPredictImpact(out var soonestSeconds, out _));
            Assert.IsTrue(nearer.TryPredictImpact(out var nearerSeconds, out _));
            Assert.Less(soonestSeconds, nearerSeconds, "리그가 의도한 순서여야 한다.");
            Assert.LessOrEqual(nearerSeconds, PhysicsScale.BatSwingWindowSeconds,
                "둘 다 창 안이어야 선택이 의미가 있다.");

            var bat = MakeBat();
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));

            Assert.AreSame(soonest, bat.LastDeflected, "먼저 착탄할 쪽을 골라야 한다.");
        }

        // --- 반사 방향 -----------------------------------------------------

        [Test]
        public void ADeflectedMeteor_FliesTowardTheAim()
        {
            var meteor = MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            var velocity = meteor.GetComponent<Rigidbody>().linearVelocity;

            Assert.AreEqual(PhysicsScale.BatDeflectSpeed * 1.35f, velocity.magnitude, 0.01f);
            Assert.Greater(velocity.x, 0f, "조준한 쪽으로 가야 한다.");
            Assert.AreEqual(0f, velocity.y, 0.01f, "수평 시선이면 수평으로 출발한다.");
            Assert.AreEqual(0f, velocity.z, 0.01f);
        }

        [TestCase(1f, 4f, 0f)]
        [TestCase(1f, -4f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, -1f, 0f)]
        [TestCase(-3f, 2f, -5f)]
        public void MeteorLaunchFollowsTheFullAim(float x, float y, float z)
        {
            var aim = new Vector3(x, y, z);
            var velocity = DeflectWith(aim);
            Assert.Less(Vector3.Distance(aim.normalized, velocity.normalized), 0.001f);
            Assert.AreEqual(PhysicsScale.BatDeflectSpeed * 1.35f, velocity.magnitude, 0.01f);
        }

        [Test]
        public void MissingAimStillProducesAFiniteLaunch()
        {
            var velocity = DeflectWith(Vector3.zero);

            Assert.AreEqual(PhysicsScale.BatDeflectSpeed * 1.35f, velocity.magnitude, 0.01f);
            Assert.Greater(velocity.y, 0f, "시선이 없을 때만 기존 방향 보정을 사용한다.");
            Assert.IsFalse(float.IsNaN(velocity.x), "방향을 정하지 못해 NaN 이 되면 안 된다.");
        }

        [TestCase(-35f)]
        [TestCase(35f)]
        public void EquippedMeteorLaunchUsesServerPlayerViewInsteadOfArmPose(float pitch)
        {
            var player = MakePlayer(Vector3.zero);
            player.SetInput(new Metagame.Core.InputFrame { AimYaw = 120f, AimPitch = pitch });
            var meteor = MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();
            bat.Equip(player, bat.gameObject.AddComponent<Rigidbody>());

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.up));
            var expected = Quaternion.Euler(pitch, 120f, 0f) * Vector3.forward;
            Assert.Less(Vector3.Distance(expected, meteor.Body.linearVelocity.normalized), 0.001f);
        }

        // --- 동료 직접 타격 --------------------------------------------------

        /// <summary>기획 §18.3 "정면으로 맞으면 넘어지지만 <b>기절하지는 않는다</b>".</summary>
        [Test]
        public void StrikingATeammate_KnocksThemDownWithoutDowning()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var bat = MakeBat();

            var hit = bat.Swing(Vector3.zero, Vector3.right);

            Assert.IsTrue(hit, "사거리 안 정면의 동료는 맞아야 한다.");
            Assert.AreSame(target, bat.LastStruck);
            Assert.IsTrue(target.Ragdolled, "넘어져야 한다.");
            Assert.IsFalse(target.Downed, "기절하면 안 된다(기획 §18.3).");
        }

        [Test]
        public void ATeammateBehindYou_IsNotStruck()
        {
            MakeGround();
            MakePlayer(new Vector3(-1f, 0f, 0f));
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right),
                "뒤에 선 동료가 맞으면 플레이어가 이유를 알 수 없다.");
        }

        [Test]
        public void ATeammateOutOfReach_IsNotStruck()
        {
            MakeGround();
            MakePlayer(new Vector3(PhysicsScale.BatStrikeReach + 1f, 0f, 0f));
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.right));
        }

        /// <summary>운석과 동료가 함께 사거리에 있으면 운석을 고른다. 그것이 도구의 목적이다.</summary>
        [Test]
        public void AMeteorWins_OverATeammate()
        {
            MakeGround();
            var target = MakePlayer(new Vector3(1f, 0f, 0f));
            var meteor = MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();

            bat.Swing(Vector3.zero, Vector3.right);

            Assert.AreSame(meteor, bat.LastDeflected);
            Assert.IsNull(bat.LastStruck);
            Assert.IsFalse(target.Ragdolled, "운석을 쳤으면 동료는 멀쩡해야 한다.");
        }

        [TestCase(0.60f, 3, 1.8f)]
        [TestCase(0.45f, 2, 1.35f)]
        [TestCase(0.90f, 1, 1f)]
        public void TimingGradeChangesLaunchSpeed(float fraction, int grade, float multiplier)
        {
            float t = PhysicsScale.BatSwingWindowSeconds * fraction;
            var meteor = MakeMeteor(Vector3.up * (-0.5f * UnityEngine.Physics.gravity.y * t * t), Vector3.zero);
            var bat = MakeBat();
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.AreEqual(grade, bat.RhythmGrade);
            Assert.AreEqual(grade, meteor.DeflectionGrade);
            Assert.IsTrue(meteor.Harmless, "성공 직후부터 피해 없는 소품이어야 한다.");
            Assert.AreEqual(meteor.transform.position, bat.RhythmImpactPoint);
            Assert.AreEqual(PhysicsScale.BatDeflectSpeed * multiplier, meteor.Body.linearVelocity.magnitude, 0.01f);
            Assert.Less(Vector3.Distance(Vector3.forward, meteor.Body.linearVelocity.normalized), 0.001f);
            Assert.AreEqual(1u, bat.RhythmSequence);
            Assert.IsNull(MeteorBat.FindGuideTarget(Vector3.zero, Meteor.All));
        }

        [Test]
        public void GuideAppearsBeforeSwingWindowButEarlySwingDoesNotDeflect()
        {
            float t = PhysicsScale.BatSwingWindowSeconds + 1f;
            var meteor = MakeMeteor(Vector3.up * (-0.5f * UnityEngine.Physics.gravity.y * t * t), Vector3.zero);
            var bat = MakeBat();
            Assert.AreSame(meteor, MeteorBat.FindGuideTarget(Vector3.zero, Meteor.All));
            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.IsFalse(meteor.Harmless);
            Assert.AreEqual(0, bat.RhythmGrade);
            Assert.Greater(bat.Cooldown, 0f);
            bat.Swing(Vector3.zero, Vector3.forward);
            Assert.AreEqual(1u, bat.RhythmSequence, "Cooldown input must not emit another result.");
        }

        [TestCase(-0.1f)]
        [TestCase(0f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        public void InvalidOrMissedTimingIsFailure(float seconds)
        {
            Assert.AreEqual(0, MeteorBat.GradeFor(seconds, 1f));
        }

        [Test]
        public void RemoteGuideUsesServerGroundAndExcludesDeflectedGhost()
        {
            var meteor = MakeMeteor(new Vector3(0f, 12f, 0f), Vector3.down);
            meteor.enabled = false;
            meteor.Unregister();
            meteor.ApplyGuideSnapshot(10f, false, 0.8f, 2f);
            var ghosts = new[] { meteor };
            Assert.AreSame(meteor, MeteorBat.FindGuideTarget(Vector3.zero, ghosts));
            Assert.IsNull(MeteorBat.FindGuideTarget(Vector3.right * 3f, ghosts));
            Assert.AreEqual(0.8f, meteor.BatSwingWindowSeconds);
            Assert.IsTrue(meteor.TryPredictImpact(out _, out var point));
            Assert.AreEqual(10f, point.y);
            Assert.IsFalse(Meteor.All.Contains(meteor), "HUD prediction must not register a network ghost.");
            meteor.ApplyGuideSnapshot(10f, true, 0.8f, 2f);
            Assert.IsNull(MeteorBat.FindGuideTarget(Vector3.zero, ghosts));
        }

        [Test]
        public void EveryNearbyMeteorHasItsOwnTimingAndTheNextBarDoesNotRestart()
        {
            var early = MakeMeteorAtSeconds(0.6f);
            var next = MakeMeteorAtSeconds(0.85f);
            var later = MakeMeteorAtSeconds(1.8f);
            var rows = new List<MeteorBat.GuideTarget>();
            MeteorBat.CollectGuideTargets(Vector3.zero, new[] { later, early, next }, rows);
            Assert.AreEqual(3, rows.Count);
            Assert.AreSame(early, rows[0].Meteor);
            Assert.AreSame(next, rows[1].Meteor);
            Assert.AreSame(later, rows[2].Meteor);
            Assert.Greater(rows[0].Progress01, rows[1].Progress01);
            Assert.Greater(rows[1].Progress01, rows[2].Progress01);
            float nextProgress = rows[1].Progress01;
            float laterProgress = rows[2].Progress01;

            var bat = MakeBat();
            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward));
            Assert.AreSame(early, bat.LastDeflected);
            MeteorBat.CollectGuideTargets(Vector3.zero, Meteor.All, rows);
            Assert.AreEqual(2, rows.Count);
            Assert.AreSame(next, rows[0].Meteor);
            Assert.AreEqual(nextProgress, rows[0].Progress01);
            Assert.AreEqual(laterProgress, rows[1].Progress01);

            next.Body.position = Vector3.up * (-0.5f * UnityEngine.Physics.gravity.y * 0.3f * 0.3f);
            MeteorBat.CollectGuideTargets(Vector3.zero, Meteor.All, rows);
            Assert.Greater(rows[0].Progress01, nextProgress);
            Assert.AreEqual(laterProgress, rows[1].Progress01);
        }

        [Test]
        public void GuideListIncludesAllRemoteMeteorsWithoutDuplicatesOrRegistration()
        {
            var first = MakeMeteorAtSeconds(0.6f);
            var second = MakeMeteorAtSeconds(1.2f);
            first.enabled = second.enabled = false;
            first.Unregister();
            second.Unregister();
            var rows = new List<MeteorBat.GuideTarget>();
            MeteorBat.CollectGuideTargets(Vector3.zero, new[] { first }, rows, new[] { first, second });
            Assert.AreEqual(2, rows.Count);
            Assert.IsEmpty(Meteor.All);
            first.ApplyGuideSnapshot(0f, true, 1f, 15f, 3);
            MeteorBat.CollectGuideTargets(Vector3.zero, Meteor.All, rows, new[] { first, second });
            Assert.AreEqual(1, rows.Count);
            Assert.AreSame(second, rows[0].Meteor);
            MeteorBat.CollectGuideTargets(Vector3.right * 100f, Meteor.All, rows, new[] { first, second });
            Assert.IsEmpty(rows, "Leaving the assist area removes all out-of-range gauges.");
        }

        [Test]
        public void EqualImpactTimesKeepTheSamePriorityAsTheActualStrike()
        {
            var first = MakeMeteorAtSeconds(0.6f);
            var second = MakeMeteorAtSeconds(0.6f);
            var source = new[] { second, first };
            var rows = new List<MeteorBat.GuideTarget>();
            MeteorBat.CollectGuideTargets(Vector3.zero, source, rows);
            Assert.AreSame(MeteorBat.FindGuideTarget(Vector3.zero, source, true), rows[0].Meteor);
            Assert.AreSame(second, rows[0].Meteor);
            Assert.AreSame(first, rows[1].Meteor);
        }

        [TestCase(1, 1920f, 1080f)]
        [TestCase(3, 1280f, 720f)]
        [TestCase(8, 1280f, 720f)]
        [TestCase(16, 1920f, 1080f)]
        [TestCase(8, 800f, 600f)]
        [TestCase(3, 640f, 360f)]
        public void TimingRowsRemainVisibleAndDoNotOverlap(int count, float width, float height)
        {
            for (int i = 0; i < count; i++)
            {
                var row = Metagame.UI.GameHud.MeteorRhythmRowRect(i, count, width, height);
                Assert.Greater(row.width, 200f);
                Assert.GreaterOrEqual(row.xMin, 0f);
                Assert.GreaterOrEqual(row.yMin, 58f);
                Assert.LessOrEqual(row.xMax, width);
                Assert.LessOrEqual(row.yMax + 32f, height);
                for (int j = 0; j < i; j++)
                    Assert.IsFalse(row.Overlaps(Metagame.UI.GameHud.MeteorRhythmRowRect(j, count, width, height)));
            }
        }

        Meteor MakeMeteorAtSeconds(float seconds) => MakeMeteor(
            Vector3.up * (-0.5f * UnityEngine.Physics.gravity.y * seconds * seconds), Vector3.zero);

        // --- 리그 ---------------------------------------------------------

        [Test]
        public void PerfectTrailDoesNotRecolorOtherMeteorsAndRemoteReuseRestoresIt()
        {
            bool trails = Meteor.TrailsEnabled;
            Meteor.TrailsEnabled = true;
            try
            {
                var perfect = MakeMeteor(Vector3.up * 2f, Vector3.down);
                var normal = MakeMeteor(Vector3.up * 3f, Vector3.down);
                var perfectTrail = perfect.GetComponentInChildren<TrailRenderer>();
                var normalTrail = normal.GetComponentInChildren<TrailRenderer>();
                var normalMaterial = normalTrail.sharedMaterial;
                var normalColor = normalTrail.startColor;
                perfect.Deflect(Vector3.forward * MeteorBat.SpeedFor(3), 3);
                Assert.Less(Vector4.Distance(BatSwingLook.ImpactColor(3), perfectTrail.startColor), 0.01f);
                Assert.AreNotSame(normalMaterial, perfectTrail.sharedMaterial);
                Assert.AreEqual(normalColor, normalTrail.startColor);
                Assert.IsTrue(perfect.Harmless);
                Assert.AreEqual(0, normal.DeflectionGrade);

                // A disabled network visual may represent a different meteor next snapshot.
                normal.enabled = false;
                normal.Unregister();
                normal.ApplyGuideSnapshot(0f, true, 1f, 15f, 3);
                Assert.AreEqual(perfectTrail.startColor, normalTrail.startColor);
                normal.ApplyGuideSnapshot(0f, false, 1f, 15f, 0);
                Assert.AreSame(normalMaterial, normalTrail.sharedMaterial);
                Assert.AreEqual(normalColor, normalTrail.startColor);
                Assert.IsFalse(normal.Harmless);
                Assert.IsFalse(Meteor.All.Contains(normal));
            }
            finally { Meteor.TrailsEnabled = trails; }
        }

        [Test]
        public void ImpactsAppliedBeforeTrailCreationKeepTheirGrade()
        {
            bool trails = Meteor.TrailsEnabled;
            try
            {
                Meteor.TrailsEnabled = false;
                var meteor = MakeMeteor(Vector3.up, Vector3.down);
                meteor.Deflect(Vector3.forward, 3);
                Meteor.TrailsEnabled = true;
                meteor.Initialize();
                Assert.Less(Vector4.Distance(BatSwingLook.ImpactColor(3),
                    meteor.GetComponentInChildren<TrailRenderer>().startColor), 0.01f);
            }
            finally { Meteor.TrailsEnabled = trails; }
        }

        [UnityTest]
        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-678)
        public System.Collections.IEnumerator RemoteImpactPlaysOnceAndParticlesExpire()
        {
            yield return new EnterPlayMode();
            bool enabled = Puff.Enabled;
            Puff.Enabled = true;
            var go = new GameObject("Remote feedback test");
            try
            {
                var bat = go.AddComponent<MeteorBat>();
                var point = new Vector3(10f, 5f, 20f);
                var random = Random.state;
                bat.ApplyRhythmResult(10, 3, 0f, point);
                Assert.IsNull(Object.FindAnyObjectByType<Puff>(), "Joining must not replay an old impact.");
                bat.ApplyRhythmResult(11, 3, 0f, point);
                var puff = Object.FindAnyObjectByType<Puff>();
                Assert.IsNotNull(puff);
                Assert.AreEqual(32, puff.transform.childCount);
                Assert.AreEqual(point, puff.transform.GetChild(0).position);
                Assert.AreEqual(random, Random.state, "Cosmetic feedback must not change gameplay randomness.");
                bat.ApplyRhythmResult(11, 3, 0f, point);
                bat.ApplyRhythmResult(12, 0);
                Assert.AreEqual(32, puff.transform.childCount, "Duplicate results and misses must not emit particles.");
                float expiresAt = Time.time + 0.6f;
                while (Time.time < expiresAt) yield return null;
                yield return null;
                foreach (Transform grain in puff.transform) Assert.IsFalse(grain.gameObject.activeSelf);
                bat.ApplyRhythmResult(13, 1, 0f, point);
                Assert.AreEqual(32, puff.transform.childCount, "The next hit must reuse expired particles.");
                Object.Destroy(puff.gameObject);
            }
            finally
            {
                Puff.Enabled = enabled;
                Object.Destroy(go);
            }
            yield return new ExitPlayMode();
        }

        /// <summary>착탄까지 남은 시간이 타격 창(1초) 안인 높이.</summary>
        static float WindowHeight
        {
            get
            {
                // h = ½·|g|·t² 에서 t 를 창의 절반으로 잡아 여유를 둔다.
                var g = -UnityEngine.Physics.gravity.y;
                var t = PhysicsScale.BatSwingWindowSeconds * 0.5f;
                return 0.5f * g * t * t;
            }
        }

        Meteor MakeMeteor(Vector3 position, Vector3 velocity)
        {
            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = position;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;   // 검증은 예측만 본다. 실제로 떨어뜨리지 않는다.
            body.linearVelocity = velocity;

            var meteor = go.AddComponent<Meteor>();
            meteor.Initialize();
            meteor.SetGroundHeight(0f);

            UnityEngine.Physics.SyncTransforms();
            return meteor;
        }

        /// <summary>낙하 지점이 <paramref name="impactPoint"/> 이고 곧 착탄할 운석.</summary>
        Meteor MakeMeteorAtWindow(Vector3 impactPoint) =>
            MakeMeteor(new Vector3(impactPoint.x, WindowHeight, impactPoint.z), Vector3.zero);

        MeteorBat MakeBat()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);
            return go.AddComponent<MeteorBat>();
        }

        /// <summary><paramref name="aim"/> 으로 쳐낸 운석의 속도.</summary>
        Vector3 DeflectWith(Vector3 aim)
        {
            var meteor = MakeMeteorAtWindow(Vector3.zero);
            var bat = MakeBat();

            Assert.IsTrue(bat.Swing(Vector3.zero, aim), "리그가 맞히도록 되어 있어야 한다.");
            return meteor.GetComponent<Rigidbody>().linearVelocity;
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, 60f);
            _spawned.Add(go);
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            LogAssert.ignoreFailingMessages = true;
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
