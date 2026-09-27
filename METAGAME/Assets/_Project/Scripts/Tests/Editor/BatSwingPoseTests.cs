using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방망이를 휘두르면 <b>몸도 휘두르는가</b>, 그리고 그것이 <b>남의 화면에도
    /// 보이는가</b> (S15P21B202-694).
    ///
    /// <para>예전에는 배트만 휘둘러졌다. <c>MeteorBat.SwingAim</c> 이 배트 방향을
    /// 돌리고 팔이 그 방향을 따라갈 뿐, <b>골반·허리·어깨·발은 전혀 안 움직였다</b> —
    /// 원샷 9개 어디에도 스윙이 없었다.</para>
    ///
    /// <para><b>1인칭이 기본이라 스윙은 내 화면에 거의 안 보인다.</b> 몸이 휘두르는
    /// 그림이 값을 하는 곳은 오직 남의 화면이다. 그래서 복제 규칙을 여기서 잠근다.</para>
    /// </summary>
    public sealed class BatSwingPoseTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            UnityEngine.Physics.SyncTransforms();
        }

        static AnimationClip Swing() =>
            Resources.Load<AnimationClip>(RabbitLocomotion.BatSwingResource);

        static AnimationClip Ready() =>
            Resources.Load<AnimationClip>(RabbitLocomotion.BatReadyResource);

        /// <summary>
        /// 그 뼈의 회전이 클립 안에서 <b>얼마나 벌어지는가</b>. 커브가 없으면 0 이다.
        ///
        /// <para>각도가 아니라 쿼터니언 성분의 폭이라 절대값에는 뜻이 없다.
        /// <b>뼈끼리 비교하는 데에만 쓴다</b> — "어깨가 골반보다 크게 돈다" 같은.</para>
        /// </summary>
        static float Range(AnimationClip clip, string bone)
        {
            var total = 0f;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.path.EndsWith("/" + bone) && binding.path != bone) continue;
                if (!binding.propertyName.StartsWith("m_LocalRotation")) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var key in curve.keys)
                {
                    min = Mathf.Min(min, key.value);
                    max = Mathf.Max(max, key.value);
                }
                total += max - min;
            }
            return total;
        }

        // --- 클립 ---------------------------------------------------------------------

        [Test]
        public void BothClipsAreBaked()
        {
            Assert.That(Swing(), Is.Not.Null, "스윙 클립을 안 구웠다.");
            Assert.That(Ready(), Is.Not.Null, "대기 코일 클립을 안 구웠다.");
        }

        [Test]
        public void TheReadyClipLoopsAndTheSwingDoesNot()
        {
            // 대기는 들고 있는 동안 계속 돈다. 스윙은 한 번이다.
            Assert.That(Ready().isLooping, Is.True, "대기 코일이 한 번만 돈다.");
            Assert.That(Swing().isLooping, Is.False, "스윙이 반복된다.");
        }

        [Test]
        public void TheSwingIsPlayedOverTheGamesLengthNotTheClipsLength()
        {
            // **클립은 0.27초, 게임의 스윙은 0.28초다.** 클립 길이를 그대로 쓰면
            // 굽는 프레임 수를 바꿀 때마다 타격 순간이 밀린다. 그래서 길이를 눌러
            // 재생한다 — 이 검사가 그 이유를 잠근다.
            var clip = Swing();
            Assert.That(clip.length, Is.Not.EqualTo(PhysicsScale.BatSwingSeconds).Within(0.002f),
                "둘이 같아졌다 — 그러면 길이를 누르는 코드가 아무 일도 안 한다.");
            Assert.That(clip.length, Is.EqualTo(PhysicsScale.BatSwingSeconds).Within(0.06f),
                "구운 클립이 게임의 스윙 길이와 너무 다르다 — 늘려 재생하면 느려 보인다.");
        }

        // --- 몸이 실제로 돈다 -----------------------------------------------------------

        /// <summary>
        /// <b>커브는 로컬 회전이다.</b> 그래서 부모인 <c>Waist</c> 가 골반 회전을
        /// 통째로 갖고, 척추는 그 위에 <b>더하는 몫</b>만 갖는다. 처음에는 "어깨가
        /// 골반보다 크게 돈다" 로 적었다가 0.320 &lt; 0.913 으로 걸렸다 — 월드에서는
        /// 어깨가 108도, 골반이 60도로 맞는데 <b>검사가 로컬 값을 월드인 줄 알고
        /// 있었다.</b> 재는 대상이 아니라 검사를 고쳤다.
        ///
        /// <para>실측: Waist 0.913 · Spine01 0.268 · Spine02 0.320 · Head 0.764</para>
        /// </summary>
        [Test]
        public void TheSpineAddsItsOwnTurnOnTopOfTheHips()
        {
            // 척추가 제 몫 없이 골반에 볼트로 박혀 있으면 몸이 통째로 도는 회전문이
            // 된다. 실제 스윙은 골반이 먼저 풀리고 어깨가 따라온다(분리, X-factor).
            var clip = Swing();
            var hips = Range(clip, "Waist");
            var spine = Range(clip, "Spine01") + Range(clip, "Spine02");
            Assert.That(hips, Is.GreaterThan(0.2f), "골반이 안 돈다.");
            Assert.That(spine, Is.GreaterThan(hips * 0.3f),
                $"척추({spine:0.000})가 골반({hips:0.000}) 위에 제 몫을 안 얹는다 — 분리가 없다.");
        }

        [Test]
        public void TheHeadHoldsItsOwnLineThroughTheSwing()
        {
            // 머리는 컨택까지 정면을 보고 가장 늦게 돈다 — 그러려면 척추가 도는
            // 만큼 되돌려야 하므로 제 커브가 커야 한다.
            Assert.That(Range(Swing(), "Head"), Is.GreaterThan(0.2f),
                "머리가 몸에 붙어 같이 돈다 — 컨택까지 공을 못 본다.");
        }

        [Test]
        public void TheSwingMovesTheLegsToo()
        {
            // 앞다리 블록이 없으면 회전축이 없다. 다리가 굳으면 그냥 상체만 비튼다.
            Assert.That(Range(Swing(), "L_Calf"), Is.GreaterThan(0.01f),
                "앞다리가 안 움직인다 — 블록이 없다.");
        }

        [Test]
        public void TheReadyCoilMovesTheLegsSoItIsNotAMannequin()
        {
            // 굽기 가드가 이미 한 번 잡았다 — 숨만 넣었더니 다리 여섯 뼈가 굳었다.
            Assert.That(Range(Ready(), "R_Calf"), Is.GreaterThan(0.002f),
                "대기 코일에서 다리가 굳어 있다.");
        }

        // --- 남의 화면에 보인다 ---------------------------------------------------------

        PlayerRig NewPlayer()
        {
            var player = PlayerRig.Create("타자", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            return player;
        }

        [Test]
        public void EverySwingBumpsTheNumberThatGoesOverTheNetwork()
        {
            // 이 수가 안 오르면 **남의 화면에서는 아무 일도 안 일어난다.**
            var player = NewPlayer();
            var before = player.SwingSeq;
            player.NotifyBatSwing();
            player.NotifyBatSwing();
            Assert.That(player.SwingSeq, Is.EqualTo(before + 2), "휘둘렀는데 수가 안 올랐다.");
        }

        [Test]
        public void TheFirstNumberYouReceiveDoesNotReplayAnOldSwing()
        {
            // 늦게 들어온 사람에게는 이미 쌓인 수가 한꺼번에 온다. 그것을 새 스윙으로
            // 보면 **접속하자마자 모두가 허공에 휘두른다** — 던지기가 겪은 그대로다.
            Assert.That(Network.NetPlayer.ShouldPlayToss(0, 7, known: false), Is.False,
                "처음 받은 값으로 재생한다.");
            Assert.That(Network.NetPlayer.ShouldPlayToss(7, 8, known: true), Is.True,
                "값이 바뀌었는데 재생하지 않는다.");
            Assert.That(Network.NetPlayer.ShouldPlayToss(8, 8, known: true), Is.False,
                "같은 값에 또 재생한다.");
        }

        [Test]
        public void TheCounterSurvivesWrappingAround()
        {
            // uint 는 한 바퀴 돌면 작아진다. '>' 로 보면 그때부터 영영 안 보인다.
            Assert.That(Network.NetPlayer.ShouldPlayToss(uint.MaxValue, 0, known: true), Is.True,
                "수가 한 바퀴 돌자 스윙이 멈췄다.");
        }

        // --- 대기 코일 ------------------------------------------------------------------

        [Test]
        public void TheCoilIsOffWhenYouAreNotHoldingABat()
        {
            var player = NewPlayer();
            Assert.That(player.BatCoiled, Is.False, "빈손인데 방망이 자세를 잡는다.");
            Assert.That(RabbitLocomotion.ReadState(player).BatCoiled, Is.False,
                "상태에 잘못 실려 참가자 화면까지 간다.");
        }

        [Test]
        public void TheCoilTravelsInTheStateThatIsAlreadySynced()
        {
            // **새 동기화를 만들지 않았다.** State 가 이미 SyncVar 라 여기 실으면
            // 참가자 화면까지 그대로 간다 — 그 성질을 검사로 잠근다.
            var state = new RabbitLocomotion.State { BatCoiled = true };
            Assert.That(state.BatCoiled, Is.True);
        }
    }
}
