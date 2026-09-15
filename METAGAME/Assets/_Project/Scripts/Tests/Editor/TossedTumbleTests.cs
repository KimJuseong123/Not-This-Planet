using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 던져진 몸이 <b>실제로 넘어가는지</b> 지킨다 (S15P21B202-551).
    ///
    /// <para><b>무엇이 막고 있었나.</b> <c>-550</c> 이 몸을 늘어뜨린 뒤에도 각속도를 주면
    /// 0.15초 만에 멈췄다. 지렛대를 하나씩 떼어 재 보니(12 rad/s, 1.2초, 골반 기울기)
    /// 강체의 각저항을 0 으로 해도 8.7도로 그대로였고, <b>균형 조인트의 감쇠를 걷었을
    /// 때만 179.6도</b>가 나왔다. 그 조인트는 <c>connectedBody = null</c> 이라 세계를
    /// 기준으로 붙잡으므로, 감쇠가 세계에 대한 회전을 통째로 먹고 있었다.</para>
    /// </summary>
    public sealed class TossedTumbleTests
    {
        const float Step = 0.01f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;
            Puff.Enabled = _puffWasEnabled;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        PlayerRig MakePlayer(Vector3 at)
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", at, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            player.ServerAuthority = true;
            _spawned.Add(player.gameObject);
            return player;
        }

        void MakeGround()
        {
            var go = new GameObject("바닥") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        static float Tilt(PlayerRig rig) =>
            Vector3.Angle(rig.Doll.Hips.transform.up, Vector3.up);

        /// <summary>1.2초 동안 골반이 가장 많이 넘어간 각도.</summary>
        float Fly(PlayerRig rig, int steps = 120, InputFrame input = default)
        {
            float worst = 0f;
            for (var i = 0; i < steps; i++)
            {
                rig.SetInput(input);
                rig.Tick();
                UnityEngine.Physics.Simulate(Step);
                worst = Mathf.Max(worst, Tilt(rig));
            }
            return worst;
        }

        // ── 축과 세기 (순수 계산) ──────────────────────────────────

        [Test]
        public void TheSpinAxisIsSidewaysSoTheBodyRollsForward()
        {
            var spin = PlayerRig.TossSpin(new Vector3(0f, 8.5f, 3.2f), 1f, Vector3.right);

            Assert.Greater(spin.magnitude, 0f, "회전을 안 준다");

            var axis = spin.normalized;
            Assert.Less(Mathf.Abs(axis.y), 0.01f,
                "축에 위 성분이 있다 — 팽이처럼 돈다");
            Assert.Less(Mathf.Abs(Vector3.Dot(axis, new Vector3(0f, 0f, 1f))), 0.01f,
                "축이 날아가는 방향과 겹친다 — 꼬치처럼 제자리에서 돈다");
        }

        [Test]
        public void ThrowingHarderSpinsFaster()
        {
            var weak = PlayerRig.TossSpin(new Vector3(0f, 8.5f, 3.2f), 0f, Vector3.right);
            var strong = PlayerRig.TossSpin(new Vector3(0f, 8.5f, 3.2f), 1f, Vector3.right);

            Assert.Greater(strong.magnitude, weak.magnitude,
                "꽉 채워 던져도 약하게 던진 것과 같게 돈다");
            Assert.AreEqual(Tuning.TossSpinSpeed, strong.magnitude, 0.01f,
                "꽉 채웠을 때 정해 둔 빠르기가 나와야 한다");
        }

        [Test]
        public void ThrowingStraightUpStillRollsUsingTheBodysOwnRight()
        {
            // 앞 성분이 없으면 축을 못 고른다. 몸의 오른쪽으로 대신한다.
            var spin = PlayerRig.TossSpin(Vector3.up * 8.5f, 1f, Vector3.right);

            Assert.Greater(spin.magnitude, 0f, "위로만 던지면 안 넘어간다");
            Assert.AreEqual(Vector3.right, spin.normalized, "대신 쓸 축을 안 썼다");
        }

        [Test]
        public void WithNoAxisAtAllItSpinsNothingRatherThanPickingOneAtRandom()
        {
            // 아무 축이나 고르면 던질 때마다 다르게 돈다.
            Assert.AreEqual(Vector3.zero, PlayerRig.TossSpin(Vector3.up * 8.5f, 1f, Vector3.zero));
        }

        // ── 실제로 넘어가는가 ──────────────────────────────────────

        [Test]
        public void ATossedBodyActuallyTurnsOver()
        {
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();

            rig.LaunchTossed(PlayerRig.TossVelocity(Vector3.forward, 1f), 1f);

            // -550 이후에도 8.5도였다. 그것이 이 티켓의 증상이다.
            Assert.Greater(Fly(rig), 90f,
                $"던져진 몸이 {Fly(rig):F1}도밖에 안 넘어갔다");
        }

        [Test]
        public void AWeakThrowTipsTheBodyButDoesNotFlipIt()
        {
            var strong = MakePlayer(new Vector3(0f, 1f, 0f));
            var weak = MakePlayer(new Vector3(20f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();

            strong.LaunchTossed(PlayerRig.TossVelocity(Vector3.forward, 1f), 1f);
            weak.LaunchTossed(PlayerRig.TossVelocity(Vector3.forward, 0f), 0f);

            var strongTilt = Fly(strong);
            var weakTilt = Fly(weak);

            Assert.Greater(strongTilt, weakTilt + 20f,
                $"세게 던진 몸 {strongTilt:F1}도 · 약하게 던진 몸 {weakTilt:F1}도 — "
                + "던진 세기가 넘어가는 정도에 안 실린다");
        }

        [Test]
        public void ADownedBodyTumblesTooNowThatTheFloorIsGone()
        {
            // 기절도 같은 조건을 탄다 — 기획 §26-13 "운석에 굴러다녀야 한다".
            // 이 변화를 모르고 지나가지 않도록 못박는다.
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            UnityEngine.Physics.SyncTransforms();
            rig.GoDown();
            rig.Tick();

            foreach (var body in rig.Doll.Bodies)
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity += Vector3.up * 8f;
                    body.angularVelocity = new Vector3(12f, 0f, 0f);
                }

            Assert.Greater(Fly(rig), 90f,
                "기절한 몸이 굴러다니지 않는다 — 감쇠 바닥이 아직 남아 있다");
        }

        // ── 조종이 살아 있는 몸은 그대로여야 한다 ───────────────────

        [Test]
        public void AnOrdinaryJumpStillLandsUpright()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            Fly(rig, 60);
            foreach (var body in rig.Doll.Bodies)
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity += Vector3.up * 8f;
                    body.angularVelocity = new Vector3(12f, 0f, 0f);
                }

            Fly(rig, 300);

            Assert.Less(Tilt(rig), 25f,
                $"그냥 뛴 몸이 {Tilt(rig):F1}도로 누웠다 — 감쇠 바닥을 너무 많이 걷었다");
            Assert.IsFalse(rig.Ragdolled, "그냥 뛴 몸이 늘어졌다");
        }

        [Test]
        public void StaggeringStillSettlesDown()
        {
            // 휘청임은 조종이 살아 있는 상태다. 감쇠가 0 이 되면 안 멈춘다.
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));
            Fly(rig, 60);

            rig.Stagger(1.0f);
            foreach (var body in rig.Doll.Bodies)
                if (body != null && !body.isKinematic)
                    body.angularVelocity = new Vector3(6f, 0f, 0f);

            Fly(rig, 300);

            Assert.Less(Tilt(rig), 25f,
                $"휘청이던 몸이 {Tilt(rig):F1}도로 안 멈췄다");
        }

        // ── 넘어가고 나서도 끝나야 한다 (-550 이 지킨 것) ───────────

        [Test]
        public void ATumblingBodyStillLandsAndStandsBackUp()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            rig.LaunchTossed(PlayerRig.TossVelocity(Vector3.forward, 1f), 1f);
            Assert.Greater(Fly(rig, 150), 90f, "먼저 넘어가야 한다");

            Fly(rig, 450);

            Assert.IsFalse(rig.Tossed,
                $"넘어간 몸이 착지 판정을 못 받았다 — Grounded={rig.Grounded} "
                + $"높이={rig.Doll.Hips.position.y:F2} 기울기={Tilt(rig):F1}도. "
                + "여기서 막히면 그 사람은 다시는 못 움직인다");
            Assert.Less(Tilt(rig), 30f, $"일어나지 못했다 — {Tilt(rig):F1}도");
        }

        [Test]
        public void ControlComesBackAfterTumbling()
        {
            MakeGround();
            var rig = MakePlayer(new Vector3(0f, 1f, 0f));

            rig.LaunchTossed(PlayerRig.TossVelocity(Vector3.forward, 1f), 1f);
            Fly(rig, 600);
            Assert.IsFalse(rig.Tossed, "먼저 던져짐이 끝나야 한다");

            var start = rig.Doll.Hips.position;
            Fly(rig, 150, new InputFrame { Move = new Vector2(1f, 0f) });

            var moved = rig.Doll.Hips.position - start;
            moved.y = 0f;
            Assert.Greater(moved.magnitude, 0.3f,
                "굴러 착지한 뒤 조종이 안 돌아온다");
        }
    }
}
