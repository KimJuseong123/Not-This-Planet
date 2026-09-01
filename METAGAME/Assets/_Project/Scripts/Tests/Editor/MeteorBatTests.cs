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
    /// <item>쳐낸 운석이 조준의 <b>수평</b> 방향으로 날아간다.</item>
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
            MakeMeteor(new Vector3(0f, 30f, 0f), Vector3.zero);
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward),
                "아직 멀었으면 헛스윙이다.");
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

            Assert.AreEqual(PhysicsScale.BatDeflectSpeed, velocity.magnitude, 0.01f);
            Assert.Greater(velocity.x, 0f, "조준한 쪽으로 가야 한다.");
            Assert.Greater(velocity.y, 0f, "약간 위로 들려야 한다(기획 §18.2).");
            Assert.AreEqual(0f, velocity.z, 0.01f);
        }

        /// <summary>
        /// 기획 §18.2 "카메라가 지나치게 위나 아래를 보더라도 수직 각도는 자동 보정한다".
        ///
        /// 보정을 따로 만들지 않았다 — 조준의 수평 성분만 쓰므로 위를 보든 아래를 보든
        /// 결과가 같다. 그것을 여기서 잠근다.
        /// </summary>
        [Test]
        public void AimingUpOrDown_GivesTheSameLaunchAngle()
        {
            var up = DeflectWith(new Vector3(1f, 4f, 0f));
            var down = DeflectWith(new Vector3(1f, -4f, 0f));

            Assert.AreEqual(up.y, down.y, 0.01f, "위를 봐도 아래를 봐도 들리는 각도가 같아야 한다.");
            Assert.AreEqual(up.x, down.x, 0.01f);
            Assert.Greater(up.y, 0f);
        }

        [Test]
        public void AimingStraightDown_StillPicksAHorizontalDirection()
        {
            var velocity = DeflectWith(Vector3.down);

            Assert.AreEqual(PhysicsScale.BatDeflectSpeed, velocity.magnitude, 0.01f);
            Assert.Greater(velocity.y, 0f, "수직으로 겨눠도 위로 들려 날아가야 한다.");
            Assert.IsFalse(float.IsNaN(velocity.x), "방향을 정하지 못해 NaN 이 되면 안 된다.");
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
            MakePlayer(new Vector3(PhysicsScale.BatPlayerReach + 1f, 0f, 0f));
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

        // --- 리그 ---------------------------------------------------------

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
