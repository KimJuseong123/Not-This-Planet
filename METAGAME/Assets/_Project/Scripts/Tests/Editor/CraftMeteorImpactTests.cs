using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>탑승 중 운석에 맞으면 기체가 돌며 밀리고 탑승자는 함께 흔들린다</b>
    /// (기획 §14.1 · §14.2 · §14.4, S15P21B202-349).
    ///
    /// 예전에는 <b>탑승자만 부서졌다.</b> 원인이 셋이었다.
    ///
    /// <code>
    /// Meteor.ApplyTier      탑승 여부를 보지 않고 인형 뼈 전부에 속도를 넣고
    ///                       직접 충돌이면 GoDown 까지 불렀다
    /// mountedJoint          breakForce 가 무한이라 골반만 묶여 있어 나머지 뼈만 흩어졌다
    /// TransportVehicle      FreezeRotation 때문에 애초에 기울 수 없고,
    ///                       DriveHover 가 매 스텝 속도·각속도를 덮어써 충격을 지웠다
    /// </code>
    ///
    /// 기획이 원하는 것은 <b>기울었다가 수평 회복</b>이다.
    ///
    /// <code>
    /// §14.1  기체가 우주선 또는 파츠에 충돌하면 실제 충격이 전달된다.
    ///        충돌 후 기체는 자동으로 수평을 회복하지만 원래 위치로 돌아오지는 않는다.
    ///        기체와 파츠는 충돌로 파괴되지 않는다.
    /// §14.2  호버 중 운석이나 파츠에 맞으면 기체와 저중력장 위치가 함께 밀린다.
    ///        수평은 복구하지만 호버 위치를 자동으로 되찾지는 않는다.
    /// </code>
    ///
    /// <b>척도를 조심한다.</b> "각속도가 0 이 아니다" 는 한 스텝만 봐도 통과하므로
    /// <b>누적 회전각</b>으로 잰다 — S15P21B202-345 에서 검증 다섯 개가 조용히 헛돌았다.
    /// </summary>
    public sealed class CraftMeteorImpactTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;
        bool _savedIgnore;

        [SetUp]
        public void SetUp()
        {
            _savedMode = Physics.simulationMode;
            _savedIgnore = LogAssert.ignoreFailingMessages;
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();
            Meteor.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _savedMode;
            LogAssert.ignoreFailingMessages = _savedIgnore;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            foreach (var rig in Object.FindObjectsByType<PlayerRig>(FindObjectsSortMode.None))
                if (rig != null)
                    Object.DestroyImmediate(rig.gameObject);

            _spawned.Clear();
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();
            Meteor.All.Clear();
        }

        // --- 탑승자를 부수지 않는다 -------------------------------------------

        /// <summary>
        /// <b>이 이슈의 핵심이다.</b> 조종석에 앉아 있는데 몸이 흩어지고 기절했다.
        /// </summary>
        [Test]
        public void TheRiderIsNotKnockedOut()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: true);
            Run(rig.Vehicle, 1f);

            Assert.IsFalse(rig.Player.Downed,
                "탑승 중에 기절했다. 기체가 대신 맞아야 한다(§14.1).");
        }

        /// <summary>
        /// 직접 충돌로 지목해도 마찬가지다. 조종석은 기체 콜라이더 안이라 운석이 사람을
        /// 직접 때릴 수 없다.
        /// </summary>
        [Test]
        public void TheRiderStaysAttachedToTheCraft()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: true);
            Run(rig.Vehicle, 1f);

            Assert.IsTrue(rig.Player.IsMounted,
                "탑승자가 기체에서 떨어져 나왔다.");
            Assert.IsTrue(rig.Player.IsRiding,
                "조인트가 끊어졌다.");
        }

        /// <summary>
        /// 기체가 도는 동안 탑승자도 함께 움직여야 한다 — 골반이 조인트로 묶여 있으므로
        /// 기체를 따라간다.
        /// </summary>
        [Test]
        public void TheRiderMovesWithTheCraft()
        {
            var rig = MakeFlyingRig();

            var before = rig.Player.FootPosition - rig.Vehicle.transform.position;

            Strike(rig, direct: false);
            Run(rig.Vehicle, 0.4f);

            var after = rig.Player.FootPosition - rig.Vehicle.transform.position;

            Assert.Less((after - before).magnitude, 2.5f,
                $"탑승자가 기체에서 떨어져 나갔다. {before} → {after}");
        }

        // --- 기체가 돌고 밀린다 ------------------------------------------------

        /// <summary>
        /// <b>누적 회전각으로 잰다.</b> 각속도 한 스텝을 보면 잠금이 안 풀렸어도 통과할
        /// 수 있다.
        ///
        /// <b>굴림 구간 전체를 돌린다 — 여기서 한 번 틀렸다.</b> 처음에 0.8초만 재고
        /// "32도밖에 안 돈다" 고 판단했다. 탑승자 인형이 회전을 먹어 각속도가 구간 안에서
        /// 잦아들지만 <b>누적으로는 한 바퀴 가까이 돈다.</b>
        /// </summary>
        [Test]
        public void TheCraftActuallySpins()
        {
            var rig = MakeFlyingRig();
            var previous = rig.Vehicle.transform.rotation;

            Strike(rig, direct: false);

            var turned = 0f;
            var steps = Mathf.RoundToInt((PhysicsScale.CraftTumbleMaxSeconds + 1f) / Step);
            for (var i = 0; i < steps; i++)
            {
                rig.Vehicle.Tick(Step);
                Physics.Simulate(Step);
                turned += Quaternion.Angle(previous, rig.Vehicle.transform.rotation);
                previous = rig.Vehicle.transform.rotation;
                if (!rig.Vehicle.Tumbling) break;
            }

            Assert.Greater(turned, 200f,
                $"기체가 {turned:0}도 밖에 돌지 않았다. 한 바퀴 가까이 돌아야 한다.");
        }

        [Test]
        public void TheCraftIsPushedAway()
        {
            var rig = MakeFlyingRig();
            var before = rig.Vehicle.transform.position;

            Strike(rig, direct: false);
            Run(rig.Vehicle, 0.5f);

            var moved = rig.Vehicle.transform.position - before;
            moved.y = 0f;

            Assert.Greater(moved.magnitude, 0.5f,
                $"기체가 {moved.magnitude:0.00}m 밖에 밀리지 않았다(§14.2).");
        }

        /// <summary>
        /// <b>내려간다고 단정하면 틀린다 — 여기서 한 번 틀렸다.</b> 운석은 지면에서
        /// 터지므로 떠 있는 기체는 <b>아래에서</b> 맞는다. 그래서 먼저 위로 밀리고 그 뒤에
        /// 떨어진다. 기획 §14.2 가 요구하는 것은 방향이 아니라 <b>밀린다</b>는 것이다.
        /// </summary>
        [Test]
        public void TheCraftIsPushedVertically()
        {
            var rig = MakeFlyingRig();
            var before = rig.Vehicle.transform.position.y;

            Strike(rig, direct: false);
            Run(rig.Vehicle, 0.6f);

            var moved = Mathf.Abs(rig.Vehicle.transform.position.y - before);

            Assert.Greater(moved, 0.3f,
                $"고도가 {moved:0.00}m 밖에 안 바뀌었다. 중력·충격이 들어가지 않았다.");
        }

        [Test]
        public void TheImpactStartsTheTumble()
        {
            var rig = MakeFlyingRig();

            Assert.IsFalse(rig.Vehicle.Tumbling, "맞기 전에는 돌지 않아야 한다.");

            Strike(rig, direct: false);

            Assert.IsTrue(rig.Vehicle.Tumbling, "충격이 굴림을 시작하지 않았다.");
        }

        // --- 반드시 돌아온다 ---------------------------------------------------

        /// <summary>
        /// <b>340 의 교훈이다.</b> 상한이 없거나 나오는 경로가 막히면 탑승자가 조종할 수
        /// 없는 상태로 갇힌다.
        /// </summary>
        [Test]
        public void TheTumbleAlwaysEnds()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 1f);

            Assert.IsFalse(rig.Vehicle.Tumbling,
                "상한이 지났는데 아직 돌고 있다. 탑승자가 갇힌다.");
        }

        [Test]
        public void TheCraftComesBackLevel()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 4f);

            Assert.IsFalse(rig.Vehicle.Righting, "자세 복구가 끝나지 않았다.");

            var level = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var tilt = Quaternion.Angle(rig.Vehicle.transform.rotation, level);

            Assert.Less(tilt, 2f,
                $"기체가 {tilt:0.0}도 기운 채로 남았다(§14.1 수평 회복).");
        }

        /// <summary>
        /// <b>즉시 세우지 않는다.</b> 뒤집힌 기체가 한 프레임에 수평이 되면 충격이 없던
        /// 일이 된다. 굴림이 끝난 <b>그 순간</b>에는 아직 되찾는 중이어야 한다.
        /// </summary>
        [Test]
        public void TheRightingIsVisibleAndNotInstant()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);
            var ended = RunUntilTumbleEnds(rig.Vehicle);

            Assert.Greater(ended, 0f, "굴림이 끝나지 않았다.");
            Assert.IsTrue(rig.Vehicle.Righting,
                "돌기를 멈춘 직후에는 자세를 되찾는 중이어야 한다.");
        }

        // --- 기울어진 채 바닥까지 떨어진다 (S15P21B202-353) --------------------

        /// <summary>
        /// <b>이것이 이 이슈의 요구다.</b> 기획 §14.1 은 수평 회복을 "<b>충돌 후</b>" 로
        /// 정했다. 예전에는 상한 2.2초가 지나면 <b>공중에서</b> 자세를 되찾고 호버가 그
        /// 고도를 잡았다 — 그래서 "쿵" 이 아니라 통제된 하강으로 보였다.
        /// </summary>
        [Test]
        public void TheCraftStaysTiltedWhileItIsStillInTheAir()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);

            var sawAirborneTilt = false;
            var rightedInTheAir = false;
            var steps = Mathf.RoundToInt((PhysicsScale.CraftTumbleMaxSeconds + 1f) / Step);

            for (var i = 0; i < steps; i++)
            {
                rig.Vehicle.Tick(Step);
                Physics.Simulate(Step);

                var airborne = rig.Vehicle.transform.position.y
                               > TransportVehicle.GroundY
                                 + PhysicsScale.CraftTumbleLandingBand + 0.5f;
                if (!airborne) break;

                var tilt = Quaternion.Angle(rig.Vehicle.transform.rotation,
                                            TransportVehicle.LevelRotation);
                if (tilt > 30f) sawAirborneTilt = true;
                if (rig.Vehicle.Righting) rightedInTheAir = true;
            }

            Assert.IsTrue(sawAirborneTilt, "공중에서 기울지 않았다.");
            Assert.IsFalse(rightedInTheAir,
                "공중에서 자세를 되찾았다. §14.1 은 '충돌 후' 회복이라고 정했다.");
        }

        [Test]
        public void TheCraftEndsUpOnTheGround()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 4f);

            Assert.Less(rig.Vehicle.transform.position.y,
                TransportVehicle.GroundY + PhysicsScale.CraftTumbleLandingBand + 0.5f,
                "맞은 기체가 바닥까지 떨어지지 않았다.");
        }

        /// <summary>
        /// <b>착지가 종료 조건이라는 것을 구분하는 척도다.</b> "착지 뒤에 복구한다" 만
        /// 보면 상한으로 끝나도 통과한다 — 어차피 그때는 이미 바닥이기 때문이다.
        /// 굴림이 <b>닿는 그 스텝에</b> 끝나는지를 본다.
        /// </summary>
        [Test]
        public void TheTumbleEndsOnTheStepItLands()
        {
            var rig = MakeFlyingRig();
            Strike(rig, direct: false);

            Physics.simulationMode = SimulationMode.Script;
            var landedAt = -1f;
            var endedAt = -1f;
            var steps = Mathf.RoundToInt((PhysicsScale.CraftTumbleMaxSeconds + 2f) / Step);

            for (var i = 0; i < steps; i++)
            {
                rig.Vehicle.Tick(Step);
                Physics.Simulate(Step);
                var now = (i + 1) * Step;

                // <b>운영 코드와 같은 것을 재야 한다.</b> 코드는 몸체에서 아래로
                // CraftTumbleLandingBand 안에 지면이 있는지 본다. 하니스의 지면은 y=0
                // 이므로 몸체 높이가 곧 그 거리다. GroundY 를 더해 재면 0.06m 만큼
                // 느슨해져 두 스텝이 어긋난다 — 그렇게 써서 한 번 틀렸다.
                if (landedAt < 0f
                    && rig.Vehicle.transform.position.y
                       <= PhysicsScale.CraftTumbleLandingBand)
                    landedAt = now;

                if (rig.Vehicle.Tumbling) continue;

                endedAt = now;
                break;
            }

            Assert.Greater(landedAt, 0f, "기체가 바닥에 닿지 않았다.");
            Assert.Greater(endedAt, 0f, "굴림이 끝나지 않았다.");
            Assert.LessOrEqual(endedAt - landedAt, Step * 1.5f,
                $"착지 {landedAt:0.00}초인데 굴림은 {endedAt:0.00}초에 끝났다. "
                + "접지가 종료 조건이 아니다.");
        }

        [Test]
        public void TheRightingStartsNearTheGround()
        {
            var rig = MakeFlyingRig();

            Strike(rig, direct: false);
            var ended = RunUntilTumbleEnds(rig.Vehicle);
            Assert.Greater(ended, 0f, "굴림이 끝나지 않았다.");

            Assert.Less(rig.Vehicle.transform.position.y,
                TransportVehicle.GroundY + PhysicsScale.CraftTumbleLandingBand + 0.5f,
                $"공중 {rig.Vehicle.transform.position.y:0.00}m 에서 복구를 시작했다.");
        }

        // --- 직접 충돌이 방아쇠다 (S15P21B202-353) ----------------------------

        /// <summary>
        /// <b>349 가 못 한 것이다.</b> 폭발 반경은 2m 인데 호버 고도는 5~8m 라 반경
        /// 판정으로는 떠 있는 기체에 닿지 않는다. 여기서 그 사실도 함께 못박는다.
        /// </summary>
        [Test]
        public void TheBlastRadiusAloneNeverReachesAHoveringCraft()
        {
            var rig = MakeFlyingRig();

            // 지면에서 터진 운석 — 실제 상황이다.
            rig.Vehicle.TakeMeteorImpact(new Vector3(1.5f, 0f, 0f),
                                         PhysicsScale.MeteorSmallImpactRadius);

            Assert.IsFalse(rig.Vehicle.Tumbling,
                "반경 판정이 호버 고도에 닿았다. 전제가 바뀌었으면 이 검증을 지워야 한다.");
        }

        [Test]
        public void ADirectHitStartsTheTumbleAtHoverAltitude()
        {
            var rig = MakeFlyingRig();
            var point = rig.Vehicle.transform.position + new Vector3(0.7f, 0.2f, 0f);

            rig.Vehicle.TakeDirectMeteorImpact(point, new Vector3(2f, -12f, 1f));

            Assert.IsTrue(rig.Vehicle.Tumbling, "직접 충돌이 굴림을 시작하지 않았다.");
            Assert.Greater(rig.Vehicle.Body.angularVelocity.magnitude, 1f,
                "직접 충돌인데 회전이 거의 없다.");
        }

        /// <summary>
        /// 무게중심을 정통으로 맞아도 돌아야 한다 — 회전축이 없으면 진행 방향과 수직인
        /// 수평축을 쓴다.
        /// </summary>
        [Test]
        public void ADeadCentreHitStillTumbles()
        {
            var rig = MakeFlyingRig();

            rig.Vehicle.TakeDirectMeteorImpact(rig.Vehicle.Body.worldCenterOfMass,
                                               Vector3.down * 12f);

            Assert.IsTrue(rig.Vehicle.Tumbling);
            Assert.Greater(rig.Vehicle.Body.angularVelocity.magnitude, 1f,
                "정통으로 맞으면 회전축이 0 이 된다. 대체 축이 없다.");
        }

        /// <summary>
        /// <b>탑승자를 맞힌 것도 기체를 맞힌 것이다.</b> 인형은 조인트로 붙어 있을 뿐
        /// 기체의 자식이 아니므로 <c>GetComponentInParent</c> 로는 찾을 수 없다.
        ///
        /// 측정에서 위에서 떨어지는 운석이 <b>인형 머리(9.25m)에 먼저 닿아 튕겼다</b> —
        /// 플랫폼(8.38m)보다 인형이 위에 있으므로 이 경로가 실제로 먼저 걸린다.
        ///
        /// <b>충돌 콜백으로는 검증할 수 없다.</b> <c>OnCollisionEnter</c> 는 EditMode 에서
        /// 돌지 않는다 — 이 저장소의 다른 운석 검증도 <c>ApplyImpact</c> 를 직접 부른다.
        /// 그래서 판정 규칙만 떼어 검증한다. <b>인게임 확인이 필수다.</b>
        /// </summary>
        [Test]
        public void HittingTheRiderCountsAsHittingTheCraft()
        {
            var rig = MakeFlyingRig();
            var head = rig.Player.Doll.Head.GetComponentInChildren<Collider>();
            Assert.IsNotNull(head, "인형 머리에 콜라이더가 있어야 한다.");

            var craft = Meteor.CraftHitBy(head, rig.Player);

            Assert.AreSame(rig.Vehicle, craft,
                "탑승자를 맞혔는데 기체를 찾지 못한다. 인형은 기체의 자식이 아니다.");
        }

        [Test]
        public void HittingTheCraftItselfAlsoCounts()
        {
            var rig = MakeFlyingRig();
            var platform = rig.Vehicle.GetComponentInChildren<BoxCollider>();
            Assert.IsNotNull(platform);

            Assert.AreSame(rig.Vehicle, Meteor.CraftHitBy(platform, null),
                "기체 콜라이더를 맞혔는데 기체를 찾지 못한다.");
        }

        /// <summary>
        /// <b>타지 않은 사람을 맞힌 것은 기체와 무관하다.</b> 이 문이 넓으면 걸어 다니는
        /// 사람이 맞을 때마다 엉뚱한 기체가 굴러간다.
        /// </summary>
        [Test]
        public void HittingSomeoneOnFootIsNotACraftHit()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var player = MakePlayer(new Vector3(20f, 0.5f, 20f));
            var head = player.Doll.Head.GetComponentInChildren<Collider>();

            Assert.IsNull(Meteor.CraftHitBy(head, player),
                "타지 않은 사람을 맞혔는데 기체를 찾았다.");
        }

        // --- 카메라가 함께 돈다 (S15P21B202-353) ------------------------------

        /// <summary>
        /// <b>화면이 실제로 돌아가는지 본다.</b> 처음에는 <c>ViewTilt</c> 값만 확인했는데
        /// 그것은 저장한 값이라 <c>LateUpdate</c> 의 곱셈을 빼도 통과했다 — 대조로
        /// 잡았다.
        /// </summary>
        [Test]
        public void TheViewTiltActuallyRotatesTheCamera()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var player = MakePlayer(new Vector3(20f, 0.5f, 20f));
            var cam = MakeCamera(player);

            cam.SetViewTilt(Quaternion.identity);
            cam.Tick();
            var level = cam.transform.rotation;

            cam.SetViewTilt(Quaternion.AngleAxis(50f, Vector3.forward));
            cam.Tick();
            var tilted = cam.transform.rotation;

            Assert.AreEqual(50f, Quaternion.Angle(level, tilted), 3f,
                "기준 회전을 넣었는데 카메라가 돌지 않는다.");
        }

        /// <summary>탑승체의 기울기가 카메라까지 전달되는가 — 배선 검증.</summary>
        [Test]
        public void TheCraftTiltReachesTheCamera()
        {
            var rig = MakeFlyingRig();
            var cam = MakeCamera(rig.Player);

            Strike(rig, direct: false);
            Run(rig.Vehicle, 0.4f);
            rig.Player.Tick();

            Assert.Greater(Quaternion.Angle(cam.ViewTilt, Quaternion.identity), 20f,
                $"기체가 도는데 화면 기준이 수평이다. {cam.ViewTilt.eulerAngles}");
            Assert.AreEqual(
                Quaternion.Angle(rig.Vehicle.LevelOffset, Quaternion.identity),
                Quaternion.Angle(cam.ViewTilt, Quaternion.identity), 1f,
                "화면 기울기가 기체 기울기와 다르다.");
        }

        [Test]
        public void TheCameraComesBackLevelAfterTheTumble()
        {
            var rig = MakeFlyingRig();
            var cam = MakeCamera(rig.Player);

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 4f);
            rig.Player.Tick();

            Assert.Less(Quaternion.Angle(cam.ViewTilt, Quaternion.identity), 1f,
                $"굴림이 끝났는데 화면이 기울어 있다. {cam.ViewTilt.eulerAngles}");
        }

        /// <summary>타지 않은 사람의 화면은 절대 기울지 않는다 — 과잉 교정 방어선.</summary>
        [Test]
        public void TheCameraStaysLevelWhenNotMounted()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var player = MakePlayer(new Vector3(20f, 0.5f, 20f));
            var cam = MakeCamera(player);

            player.Tick();

            Assert.Less(Quaternion.Angle(cam.ViewTilt, Quaternion.identity), 0.01f,
                "타지도 않았는데 화면이 기울었다.");
        }

        // --- 하니스 -----------------------------------------------------------

        /// <summary>
        /// §14.2 "수평은 복구하지만 <b>호버 위치를 자동으로 되찾지는 않는다</b>".
        ///
        /// 밀린 방향을 단정하지 않는다 — 되찾지 <b>않았다</b>는 것만 본다.
        /// </summary>
        [Test]
        public void TheCraftDoesNotReturnToItsOldAltitude()
        {
            var rig = MakeFlyingRig();
            var before = rig.Vehicle.transform.position.y;

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 4f);

            var after = rig.Vehicle.transform.position.y;

            Assert.Greater(Mathf.Abs(after - before), 0.3f,
                $"기체가 원래 높이({before:0.00}m)로 돌아갔다({after:0.00}m). §14.2 를 어긴다.");
        }

        // --- 과잉 교정 방어선 --------------------------------------------------

        /// <summary>
        /// 평상시에는 회전이 잠겨 있어야 한다. 상시 해제는 이 이슈의 범위가 아니다 —
        /// S15P21B202-216 이 "탑승자 인형이 항상 흔들린다" 를 우려했다.
        /// </summary>
        [Test]
        public void RotationStaysLockedWhenNothingHitsTheCraft()
        {
            var rig = MakeFlyingRig();

            Run(rig.Vehicle, 1f);

            Assert.IsFalse(rig.Vehicle.Tumbling);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotation,
                rig.Vehicle.Body.constraints,
                "맞지도 않았는데 회전 잠금이 풀렸다.");
        }

        /// <summary>
        /// <b>타지 않은 사람은 §20.1 그대로다.</b> 탑승 예외가 걸어 놓은 문이 너무 넓으면
        /// 운석이 아무도 기절시키지 못한다.
        /// </summary>
        [Test]
        public void APlayerOnFootStillGetsKnockedOut()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            var player = MakePlayer(new Vector3(0f, 0.5f, 0f));
            var meteor = MakeMeteor(new Vector3(0f, 2f, 0f));

            meteor.ApplyImpact(Vector3.zero, player);

            Assert.IsTrue(player.Downed,
                "걸어 다니는 사람은 운석 직접 충돌에 기절해야 한다(§20.1).");
        }

        /// <summary>
        /// 하차하며 내려앉는 중에는 충격을 받지 않는다 — 끊으면 탑승자가 공중에 갇힐
        /// 수 있다.
        /// </summary>
        [Test]
        public void ADescendingCraftIgnoresTheImpact()
        {
            var rig = MakeFlyingRig();

            rig.Vehicle.BeginDismount();
            Assert.IsTrue(rig.Vehicle.IsReturning, "먼저 하차 중이어야 한다.");

            Strike(rig, direct: false);

            Assert.IsFalse(rig.Vehicle.Tumbling,
                "하차 중에 굴리면 탑승자가 공중에 갇힐 수 있다.");
        }

        /// <summary>
        /// 같은 충격에서 같은 결과가 나와야 한다. 무작위를 쓰지 않았다는 것이다
        /// (§22.3 과 같은 원칙).
        /// </summary>
        [Test]
        public void TheSameImpactGivesTheSameSpinDirection()
        {
            var first = SpinAxisAfterStrike();
            TearDown();
            SetUp();
            var second = SpinAxisAfterStrike();

            Assert.Greater(Vector3.Dot(first.normalized, second.normalized), 0.9f,
                $"같은 충격인데 회전 방향이 다르다. {first} 대 {second}");
        }

        Vector3 SpinAxisAfterStrike()
        {
            var rig = MakeFlyingRig();
            Strike(rig, direct: false);
            return rig.Vehicle.Body.angularVelocity;
        }

        // --- 조종석이 시야를 따른다 (§14.1) ----------------------------------

        /// <summary>
        /// 굴림과 복구가 끝나면 조종석이 <b>다시</b> 시야를 따라야 한다.
        /// </summary>
        [Test]
        public void TheCockpitFollowsTheViewAgainAfterTheTumble()
        {
            var rig = MakeFlyingRig();
            var visual = rig.Vehicle.transform.Find("Visual");
            Assert.IsNotNull(visual);

            Strike(rig, direct: false);
            Run(rig.Vehicle, PhysicsScale.CraftTumbleMaxSeconds + 4f);

            Assert.IsFalse(rig.Vehicle.Tumbling);
            Assert.IsFalse(rig.Vehicle.Righting);

            rig.Vehicle.SetFacingDirection(Vector3.right);
            Run(rig.Vehicle, 0.1f);

            var facing = visual.rotation * Vector3.forward;
            facing.y = 0f;

            Assert.Greater(Vector3.Dot(facing.normalized, Vector3.right), 0.9f,
                $"조종석이 시야를 따라가지 않는다. 정면 {facing}");
        }

        /// <summary>맞기 전에는 당연히 따라가야 한다 — 과잉 교정 방어선.</summary>
        [Test]
        public void TheCockpitFollowsTheViewWhenNothingHappened()
        {
            var rig = MakeFlyingRig();
            var visual = rig.Vehicle.transform.Find("Visual");

            rig.Vehicle.SetFacingDirection(Vector3.right);
            Run(rig.Vehicle, 0.1f);

            var facing = visual.rotation * Vector3.forward;
            facing.y = 0f;

            Assert.Greater(Vector3.Dot(facing.normalized, Vector3.right), 0.9f,
                $"맞지도 않았는데 조종석이 시야를 따라가지 않는다. 정면 {facing}");
        }

        // --- 하니스 -----------------------------------------------------------

        struct Rig
        {
            public TransportVehicle Vehicle;
            public PlayerRig Player;
        }

        /// <summary>탑승해서 떠 있는 상태를 만든다. 충격은 공중에서 온다.</summary>
        Rig MakeFlyingRig()
        {
            MakeSurface("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));

            var position = new Vector3(0f, TransportVehicle.GroundY, 0f);
            var go = new GameObject("TractorBeam");
            go.transform.SetPositionAndRotation(
                position, Quaternion.LookRotation(Vector3.down, Vector3.forward));

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);

            var beam = go.AddComponent<TractorBeam>();
            var vehicle = go.AddComponent<TransportVehicle>();
            vehicle.Initialize(beam, TransportVehicle.BeamHoldDistance);
            _spawned.Add(go);

            var player = MakePlayer(position + Vector3.up * 0.5f);
            Assert.IsTrue(vehicle.TryBoard(player), "먼저 탑승해야 한다.");

            // 떠오른 뒤에 맞아야 밀리고 떨어지는 것이 보인다.
            vehicle.SetVerticalInput(1f);
            Run(vehicle, 2f);
            vehicle.SetVerticalInput(0f);
            Run(vehicle, 1f);

            Assert.Greater(vehicle.transform.position.y, 2f,
                "충격 전에 떠 있어야 한다.");

            return new Rig { Vehicle = vehicle, Player = player };
        }

        /// <summary>기체 옆 아래에서 운석이 터진다.</summary>
        void Strike(Rig rig, bool direct)
        {
            var point = rig.Vehicle.transform.position
                        + new Vector3(0.8f, -0.6f, 0f);
            var meteor = MakeMeteor(point + Vector3.up * 0.3f);

            meteor.ApplyImpact(point, direct ? rig.Player : null);
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

            Physics.SyncTransforms();
            return meteor;
        }

        PlayerRig MakePlayer(Vector3 position)
        {
            // PlayerRig.Create 경로의 Prim.Decor 가 EditMode 에서 에러 로그를 남긴다.
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", position, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            return player;
        }

        void MakeSurface(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = 0 };
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            _spawned.Add(go);
        }

        /// <summary>굴림이 끝날 때까지 돌린다. 끝난 시각(초)을 준다. 안 끝나면 -1.</summary>
        float RunUntilTumbleEnds(TransportVehicle vehicle)
        {
            Physics.simulationMode = SimulationMode.Script;
            var steps = Mathf.RoundToInt((PhysicsScale.CraftTumbleMaxSeconds + 2f) / Step);

            for (var i = 0; i < steps; i++)
            {
                vehicle.Tick(Step);
                Physics.Simulate(Step);
                if (!vehicle.Tumbling) return (i + 1) * Step;
            }

            return -1f;
        }

        OrbitCamera MakeCamera(PlayerRig player)
        {
            LogAssert.ignoreFailingMessages = true;
            var cam = OrbitCamera.Create(player.Doll.Hips.transform, Color.black);
            _spawned.Add(cam.gameObject);
            player.Cam = cam;
            return cam;
        }

        void Run(TransportVehicle vehicle, float seconds)
        {
            Physics.simulationMode = SimulationMode.Script;
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                vehicle.Tick(Step);
                Physics.Simulate(Step);
            }
        }
    }
}
