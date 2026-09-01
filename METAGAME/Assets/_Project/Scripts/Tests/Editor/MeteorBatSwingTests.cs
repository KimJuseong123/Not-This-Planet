using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방망이를 휘두르는 <b>동작</b>을 검증한다(S15P21B202-243, 기획 §18.1).
    ///
    /// 판정은 S15P21B202-233 이 만들었고 <see cref="MeteorBatTests"/> 가 지킨다. 여기서
    /// 보는 것은 "휘두르는 것이 보이는가" 다 — 그 전까지는 좌클릭해도 팔이 운반 자세
    /// 그대로여서 스윙이 일어났는지 알 수 없었다.
    ///
    /// <b>Animator 로 하지 않는다.</b> 이 프로젝트에 Animator 가 한 개도 없고 인형은
    /// 코드로 자세를 잡는 물리 래그돌이다. <see cref="PlayerRig"/> 가 <b>방향 하나로</b>
    /// 도구의 위치와 회전을 정하므로, 그 방향을 흔들면 도구가 호를 그린다.
    ///
    /// 지키려는 것은 셋이다.
    /// <list type="number">
    /// <item>동작이 <b>판정을 좌우하지 않는다</b>(§18.1 "완벽한 프레임 판정은 없다").</item>
    /// <item>궤적이 위에서 앞아래로 간다.</item>
    /// <item>동작이 끝나면 운반 자세로 돌아온다.</item>
    /// </list>
    /// </summary>
    public sealed class MeteorBatSwingTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            Meteor.All.Clear();
            PlayerRig.All.Clear();

            // 입자를 끈다. 검증이 보려는 것은 궤적 계산이고, 켜 두면 씬에 알갱이
            // 오브젝트가 쌓여 다음 검증에 섞인다.
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
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

        // --- 동작 수명 -----------------------------------------------------

        [Test]
        public void SwingingStartsTheMotion()
        {
            var bat = MakeBat();

            Assert.IsFalse(bat.IsSwinging, "가만히 있으면 동작이 없다.");

            bat.Swing(Vector3.zero, Vector3.forward);

            Assert.IsTrue(bat.IsSwinging, "휘두르면 동작이 시작돼야 한다.");
            Assert.AreEqual(0f, bat.SwingProgress01, 0.01f);
        }

        [Test]
        public void TheMotionEndsWithinItsTime()
        {
            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.forward);

            // 절반쯤에서는 아직 도는 중이어야 한다.
            bat.Tick(PhysicsScale.BatSwingSeconds * 0.5f);
            Assert.IsTrue(bat.IsSwinging);
            Assert.AreEqual(0.5f, bat.SwingProgress01, 0.05f);

            bat.Tick(PhysicsScale.BatSwingSeconds * 0.6f);
            Assert.IsFalse(bat.IsSwinging, "정해진 시간 안에 끝나야 한다.");
            Assert.AreEqual(0f, bat.SwingProgress01, 0.01f);
        }

        /// <summary>
        /// 동작이 타격 창(1초)보다 짧아야 한다. 길면 창 안에서 클릭한 스윙이 창이 닫힌
        /// 뒤에 끝나, 플레이어가 "쳤는데 안 맞았다" 고 느낀다.
        /// </summary>
        [Test]
        public void TheMotionIsShorterThanTheStrikeWindow()
        {
            Assert.Less(PhysicsScale.BatSwingSeconds, PhysicsScale.BatSwingWindowSeconds,
                "동작이 타격 창보다 길면 안 된다.");
        }

        // --- 동작이 판정을 좌우하지 않는다 --------------------------------------

        /// <summary>
        /// <b>이 검증이 §18.1 의 "완벽한 프레임, 등급과 치명타 판정은 없다" 를 지킨다.</b>
        ///
        /// 동작이 도는 중에 휘둘러도 결과가 같아야 한다 — 동작 위상이 판정에 섞이면
        /// 타이밍 게임이 되고, 기획이 명시적으로 그것을 금지한다.
        /// </summary>
        [Test]
        public void TheMotionDoesNotChangeTheOutcome()
        {
            var meteor = MakeMeteorAtWindow();
            var bat = MakeBat();

            Assert.IsTrue(bat.Swing(Vector3.zero, Vector3.forward), "먼저 한 번 맞힌다.");
            Assert.AreSame(meteor, bat.LastDeflected);

            // 동작이 도는 중에 다시 휘두른다. 맞힌 뒤에는 대기가 없으므로 가능하다.
            Assert.IsTrue(bat.IsSwinging);

            MakeMeteorAtWindow();
            var again = bat.Swing(Vector3.zero, Vector3.forward);

            Assert.IsTrue(again, "동작 중에도 판정은 같아야 한다.");
        }

        [Test]
        public void AMissAlsoPlaysTheMotion()
        {
            var bat = MakeBat();

            Assert.IsFalse(bat.Swing(Vector3.zero, Vector3.forward), "맞힐 것이 없다.");
            Assert.IsTrue(bat.IsSwinging,
                "헛스윙도 휘두르는 것은 보여야 한다 — 안 보이면 클릭이 먹혔는지 알 수 없다.");
        }

        // --- 궤적 ---------------------------------------------------------

        /// <summary>위에서 앞아래로 내려찍는다. 목표가 <b>떨어지는</b> 운석이기 때문이다.</summary>
        [Test]
        public void TheArcGoesFromAboveToBelow()
        {
            var start = MeteorBat.SwingAim(Vector3.forward, 0f);
            var end = MeteorBat.SwingAim(Vector3.forward, 1f);

            Assert.Greater(start.y, 0f, "시작은 조준선보다 위여야 한다.");
            Assert.Less(end.y, 0f, "끝은 조준선보다 아래여야 한다.");
            Assert.Greater(start.y, end.y);
        }

        [Test]
        public void TheArcKeepsTheAimedHeading()
        {
            var aim = new Vector3(1f, 0f, 1f).normalized;

            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                var swung = MeteorBat.SwingAim(aim, t);
                var flat = new Vector3(swung.x, 0f, swung.z).normalized;

                Assert.Greater(Vector3.Dot(flat, aim), 0.99f,
                    $"진행도 {t:0.0} 에서 겨눈 쪽을 벗어났다 — 옆으로 휘면 무엇을 쳤는지 알 수 없다.");
            }
        }

        /// <summary>
        /// 위·아래를 극단으로 봐도 궤적이 같아야 한다. 조준의 <b>수평 성분</b>만 쓰는
        /// 것이 <see cref="MeteorBat"/> 의 반사 방향 규칙과 같다(§18.2).
        /// </summary>
        [Test]
        public void LookingUpOrDown_GivesTheSameArc()
        {
            var up = MeteorBat.SwingAim(new Vector3(0f, 4f, 1f), 0.5f);
            var down = MeteorBat.SwingAim(new Vector3(0f, -4f, 1f), 0.5f);

            Assert.AreEqual(up.x, down.x, 0.01f);
            Assert.AreEqual(up.y, down.y, 0.01f);
            Assert.AreEqual(up.z, down.z, 0.01f);
        }

        [Test]
        public void TheArcIsAlwaysANormalDirection()
        {
            for (var t = 0f; t <= 1f; t += 0.05f)
            {
                var swung = MeteorBat.SwingAim(Vector3.forward, t);

                Assert.AreEqual(1f, swung.magnitude, 0.01f, $"진행도 {t:0.00}");
                Assert.IsFalse(float.IsNaN(swung.x), $"진행도 {t:0.00} 에서 NaN");
            }
        }

        [Test]
        public void AVerticalAim_StillGivesAUsableArc()
        {
            var swung = MeteorBat.SwingAim(Vector3.down, 0.5f);

            Assert.AreEqual(1f, swung.magnitude, 0.01f);
            Assert.IsFalse(float.IsNaN(swung.x), "수직으로 겨눠도 방향을 정하지 못하면 안 된다.");
        }

        /// <summary>가속하며 내려간다. 등속이면 휘두르는 것이 아니라 미끄러지는 것으로 보인다.</summary>
        [Test]
        public void TheArcAccelerates()
        {
            var firstHalf = MeteorBat.SwingAim(Vector3.forward, 0f).y
                            - MeteorBat.SwingAim(Vector3.forward, 0.5f).y;
            var secondHalf = MeteorBat.SwingAim(Vector3.forward, 0.5f).y
                             - MeteorBat.SwingAim(Vector3.forward, 1f).y;

            Assert.Greater(secondHalf, firstHalf,
                "뒤쪽 절반이 더 많이 움직여야 휘두르는 것으로 보인다.");
        }

        // --- 운반 자세로 돌아온다 ---------------------------------------------

        [Test]
        public void WhileIdle_TheCarryAimIsTheAimItself()
        {
            var bat = MakeBat();
            var aim = new Vector3(0.3f, -0.2f, 1f).normalized;

            Assert.AreEqual(aim, bat.CarryAim(aim), "휘두르지 않으면 운반 자세 그대로다.");
        }

        [Test]
        public void WhileSwinging_TheCarryAimLeavesTheAim()
        {
            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.forward);

            var carry = bat.CarryAim(Vector3.forward);

            Assert.AreNotEqual(Vector3.forward, carry, "휘두르는 동안은 조준선에서 벗어난다.");
            Assert.Greater(carry.y, 0f, "시작 자세는 위로 들려 있다.");
        }

        [Test]
        public void AfterTheMotion_TheCarryAimComesBack()
        {
            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.forward);
            bat.Tick(PhysicsScale.BatSwingSeconds + 0.01f);

            Assert.AreEqual(Vector3.forward, bat.CarryAim(Vector3.forward),
                "동작이 끝나면 운반 자세로 돌아와야 한다.");
        }

        /// <summary>
        /// 휘두르는 중에 카메라를 돌려도 궤적이 따라 휘지 않아야 한다. 휘면 무엇을
        /// 쳤는지 알 수 없다.
        /// </summary>
        [Test]
        public void TurningTheCameraMidSwing_DoesNotBendTheArc()
        {
            var bat = MakeBat();
            bat.Swing(Vector3.zero, Vector3.forward);
            bat.Tick(PhysicsScale.BatSwingSeconds * 0.4f);

            var straight = bat.CarryAim(Vector3.forward);
            var turned = bat.CarryAim(Vector3.right);

            Assert.AreEqual(straight, turned, "스윙을 시작한 순간의 조준을 지켜야 한다.");
        }

        /// <summary>기본 도구는 자세를 흔들지 않는다 — 용접기·운반체가 그대로여야 한다.</summary>
        [Test]
        public void OtherTools_DoNotMoveTheCarryPose()
        {
            var go = new GameObject("PlasmaWeldingTool");
            _spawned.Add(go);

            var gun = go.AddComponent<PlasmaWeldingTool>();
            var aim = new Vector3(0.5f, 0.5f, 1f).normalized;

            Assert.AreEqual(aim, gun.CarryAim(aim));
        }

        // --- 리그 ---------------------------------------------------------

        MeteorBat MakeBat()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);
            return go.AddComponent<MeteorBat>();
        }

        /// <summary>착탄까지 타격 창 안인 높이의 운석.</summary>
        Meteor MakeMeteorAtWindow()
        {
            var g = -UnityEngine.Physics.gravity.y;
            var t = PhysicsScale.BatSwingWindowSeconds * 0.5f;
            var height = 0.5f * g * t * t;

            var go = new GameObject("Meteor") { layer = GameLayers.Meteor };
            go.transform.position = new Vector3(0f, height, 0f);
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
