using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>
        /// 어깨 위에서 시작해 살짝 처지며 끝난다(우타자 야구 스윙, S15P21B202-261).
        ///
        /// 예전에는 위 55도 → 아래 −35도로 <b>내려찍었다.</b> 근거가 "목표가 떨어지는
        /// 운석" 이었는데(S15P21B202-243) 인게임에서 보고 바꿨다.
        /// </summary>
        [Test]
        public void TheArcStartsHighAndEndsSlightlyLow()
        {
            var start = MeteorBat.SwingAim(Vector3.forward, 0f);
            var end = MeteorBat.SwingAim(Vector3.forward, 1f);

            Assert.Greater(start.y, 0f, "어깨 위에서 시작한다.");
            Assert.Less(end.y, 0f, "팔로스루로 살짝 처진다.");
            Assert.Greater(start.y, end.y);
        }

        // --- 우타자 스윙 (S15P21B202-261) ----------------------------------

        /// <summary>오른손잡이 타자는 배트를 <b>오른쪽 뒤</b>에 두고 시작한다.</summary>
        [Test]
        public void ItStartsBehindTheRightShoulder()
        {
            Assert.Greater(SweepDegrees(0f), 90f,
                "90도를 넘겨야 뒤쪽이다 — 넘지 않으면 들었다 내리는 것으로 보인다.");
        }

        /// <summary>맞는 순간에는 정면을 지난다.</summary>
        [Test]
        public void ItPassesTheFrontAtContact()
        {
            Assert.AreEqual(0f, SweepDegrees(PhysicsScale.BatSwingContactAt), 1f,
                "맞는 순간이 정면이어야 어디를 쳤는지 읽힌다.");
        }

        [Test]
        public void ItFinishesToTheLeftAndForward()
        {
            var end = SweepDegrees(1f);

            Assert.Less(end, -20f, "왼쪽으로 빠진다.");
            Assert.Greater(end, -90f, "앞쪽에서 끝난다 — 뒤로 넘어가지 않는다.");
        }

        /// <summary>
        /// 좌우가 <b>한 방향으로만</b> 지나간다. 되돌아오면 부채질로 보인다.
        /// </summary>
        [Test]
        public void TheSweepNeverReverses()
        {
            var previous = float.MaxValue;

            for (var t = 0f; t <= 1f; t += 0.05f)
            {
                var sweep = SweepDegrees(t);

                Assert.LessOrEqual(sweep, previous + 1e-2f,
                    $"진행도 {t:0.00} 에서 오른쪽으로 되돌아갔다.");
                previous = sweep;
            }
        }

        /// <summary>
        /// 시작이 오른쪽 110도, 끝이 왼쪽 70도다. 대칭이면 부채질이 된다.
        /// </summary>
        [Test]
        public void TheSweepIsNotSymmetric()
        {
            Assert.Greater(Mathf.Abs(SweepDegrees(0f)), Mathf.Abs(SweepDegrees(1f)),
                "뒤에서 시작해 앞으로 끝난다 — 좌우를 같은 폭으로 흔들지 않는다.");
        }

        /// <summary>
        /// 겨눈 쪽을 기준으로 궤적이 돈다. 조준을 돌리면 스윙 전체가 함께 돈다.
        ///
        /// <b>예전 검증은 "겨눈 쪽을 벗어나지 않는다" 였다.</b> 좌우 성분이 없던 궤적이라
        /// 성립했고, 야구 스윙에서는 성립할 수 없다 — 대신 <b>회전이 따라오는지</b>를
        /// 본다(S15P21B202-261).
        /// </summary>
        [Test]
        public void TheArcRotatesWithTheAim()
        {
            var aim = new Vector3(1f, 0f, 1f).normalized;
            var turn = Quaternion.FromToRotation(Vector3.forward, aim);

            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                var straight = MeteorBat.SwingAim(Vector3.forward, t);
                var turned = MeteorBat.SwingAim(aim, t);

                Assert.Greater(Vector3.Dot((turn * straight).normalized, turned.normalized),
                    0.99f, $"진행도 {t:0.0} 에서 궤적이 조준을 따라오지 않았다.");
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

        /// <summary>
        /// 맞는 순간까지 가속한다. 등속이면 휘두르는 것이 아니라 미끄러지는 것으로 보인다.
        ///
        /// 좌우 진행으로 본다 — 야구 스윙의 움직임은 대부분 좌우다(S15P21B202-261).
        /// </summary>
        [Test]
        public void TheSwingAcceleratesIntoContact()
        {
            var contact = PhysicsScale.BatSwingContactAt;

            var firstHalf = SweepDegrees(0f) - SweepDegrees(contact * 0.5f);
            var secondHalf = SweepDegrees(contact * 0.5f) - SweepDegrees(contact);

            Assert.Greater(secondHalf, firstHalf,
                "맞는 순간에 가까울수록 더 많이 움직여야 한다.");
        }

        /// <summary>팔로스루는 감속한다. 배트가 몸을 감고 멈춘다.</summary>
        [Test]
        public void TheFollowThroughDecelerates()
        {
            var contact = PhysicsScale.BatSwingContactAt;
            var mid = contact + (1f - contact) * 0.5f;

            var firstHalf = SweepDegrees(contact) - SweepDegrees(mid);
            var secondHalf = SweepDegrees(mid) - SweepDegrees(1f);

            Assert.Greater(firstHalf, secondHalf,
                "팔로스루 뒤쪽이 덜 움직여야 멈추는 것으로 보인다.");
        }

        /// <summary>
        /// 진행도에서의 <b>좌우 각도</b>(도). 오른쪽이 양수다.
        ///
        /// <b>원시 내적으로 재면 안 된다.</b> 처음에 그렇게 썼다가 두 가지에 걸렸다.
        ///
        /// <code>
        /// ① pitch 와 섞인다     45도 들린 방향은 수평 성분이 cos(45)=0.71 로 줄어든다.
        ///                       그래서 pitch 가 평평해지는 것만으로 "오른쪽으로 되돌아간
        ///                       것" 처럼 보였다.
        /// ② 90도에서 꺾인다     sin(110)=0.94 이고 sin(90)=1.0 이라, yaw 가 110→90 으로
        ///                       줄어드는 동안 성분은 오히려 커진다.
        /// </code>
        ///
        /// 각도를 직접 재면 둘 다 사라진다.
        /// </summary>
        static float SweepDegrees(float progress01)
        {
            var swung = MeteorBat.SwingAim(Vector3.forward, progress01);
            return Mathf.Atan2(swung.x, swung.z) * Mathf.Rad2Deg;
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

        [Test]
        public void Bat_AppliesCompactHeldVisualPoseAndRestoresDroppedPose()
        {
            var bat = MakeBat();
            var body = bat.gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;

            var visual = new GameObject("Visual_ZUpToYUp").transform;
            visual.SetParent(bat.transform, false);
            visual.localPosition = new Vector3(0.02f, 0.03f, 0.04f);
            visual.localRotation = Quaternion.Euler(5f, 10f, 15f);
            visual.localScale = Vector3.one * 1.2244761f;

            var droppedPosition = visual.localPosition;
            var droppedRotation = visual.localRotation;
            var droppedScale = visual.localScale;

            bat.Equip(null, body);

            Assert.That(visual.localPosition,
                Is.EqualTo(new Vector3(0.18f, -0.12f, 0.12f)));
            Assert.That(Quaternion.Angle(
                visual.localRotation,
                Quaternion.Euler(0f, 0f, -35f) * Quaternion.FromToRotation(new Vector3(-0.62f, 0.60f, 0.70f).normalized, Vector3.up)), Is.LessThan(0.01f));
            Assert.That(visual.localScale, Is.EqualTo(Vector3.one * 0.64f));

            bat.Unequip();

            Assert.That(visual.localPosition, Is.EqualTo(droppedPosition));
            Assert.That(Quaternion.Angle(visual.localRotation, droppedRotation), Is.LessThan(0.01f));
            Assert.That(visual.localScale, Is.EqualTo(droppedScale));
        }

        [Test]
        public void Bat_LocalFirstPersonVisualFollowsCameraAndReturnsForThirdPerson()
        {
            var player = PlayerRig.Create("방망이 화면 표시 테스트", Vector3.zero,
                                          Quaternion.identity, Color.white, Color.gray,
                                          useModel: false);
            _spawned.Add(player.gameObject);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            _spawned.Add(camera.gameObject);
            player.Cam = camera;

            var bat = MakeBat();
            var body = bat.gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            var visual = new GameObject("Visual_ZUpToYUp").transform;
            visual.SetParent(bat.transform, false);

            bat.Equip(player, body);

            Assert.AreSame(bat.transform, visual.parent, "월드 모델은 원래 계층을 유지한다.");
            var view = bat.FirstPersonModel;
            Assert.AreSame(camera.transform, view.parent);
            Assert.That(view.localPosition,
                        Is.EqualTo(new Vector3(0.23f, -0.20f, 0.58f)));

            body.position = new Vector3(-12f, 4f, 8f);
            RunLateUpdate(bat);
            Assert.AreSame(camera.transform, view.parent,
                           "물리 본체 이동 뒤 화면 모델이 카메라에서 떨어졌다.");

            camera.FirstPerson = false;
            RunLateUpdate(bat);
            Assert.AreSame(bat.transform, visual.parent,
                           "3인칭 전환 때 모델이 물리 도구 계층으로 돌아오지 않았다.");
            Assert.IsFalse(view.gameObject.activeSelf);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator Bat_DestroyWhileHeldRemovesCameraVisual()
        {
            // A plain EditMode object has not entered the runtime lifecycle, so its
            // OnDestroy is not a reliable cleanup trigger. Exercise actual destruction.
            yield return new UnityEngine.TestTools.EnterPlayMode();

            var playerObject = new GameObject("방망이 파괴 테스트");
            _spawned.Add(playerObject);
            var player = playerObject.AddComponent<PlayerRig>();
            player.enabled = false;
            var camera = OrbitCamera.Create(player.transform, Color.black, true);
            // This lifecycle test does not need audio alongside the test scene's listener.
            camera.GetComponent<AudioListener>().enabled = false;
            _spawned.Add(camera.gameObject);
            player.Cam = camera;
            var bat = MakeBat();
            var body = bat.gameObject.AddComponent<Rigidbody>();
            var visual = new GameObject("Visual_ZUpToYUp");
            visual.transform.SetParent(bat.transform, false);
            bat.Equip(player, body);
            Assert.AreSame(bat.transform, visual.transform.parent);
            var view = bat.FirstPersonModel;
            Assert.AreSame(camera.transform, view.parent);

            Object.Destroy(bat.gameObject);
            // The tool's OnDestroy schedules destruction of the camera-owned visual.
            // Allow both the tool and that separately owned GameObject to be removed.
            yield return null;
            yield return null;

            Assert.IsTrue(visual == null, "파괴된 방망이 외형이 카메라 아래에 남으면 안 된다.");
            Assert.IsTrue(view == null, "도구 파괴 시 표시 복사본도 제거해야 한다.");
            Assert.IsTrue(camera != null, "외형 정리가 카메라를 파괴하면 안 된다.");
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        // --- 리그 ---------------------------------------------------------

        MeteorBat MakeBat()
        {
            var go = new GameObject("MeteorBat") { layer = GameLayers.Prop };
            _spawned.Add(go);
            return go.AddComponent<MeteorBat>();
        }

        static void RunLateUpdate(ToolBase tool)
        {
            var method = typeof(ToolBase).GetMethod(
                "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ToolBase.LateUpdate를 찾지 못했다.");
            method.Invoke(tool, null);
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
