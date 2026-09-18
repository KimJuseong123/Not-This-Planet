using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 체력이 참가자에게 전달되는지, 그리고 참가자가 로컬로 깎지 않는지 검증한다
    /// (S15P21B202-513).
    ///
    /// <para><b>무엇이 문제였나.</b> 빨간 테두리(<c>GameHud.HealthFrame</c>)는
    /// <see cref="PlayerRig.Health01"/> 을 읽는데 <b>체력이 전송되지 않았다</b> —
    /// (테두리는 <c>-539</c> 에서 <see cref="PlayerRig.DamageFlash01"/> 을 읽도록
    /// 바뀌었다. 체력을 보내야 하는 이유는 그대로다 — 번쩍임이 그 값에서 나온다.)
    /// 실측으로 <c>NetPlayer</c> 와 <c>WorldSync</c> 둘 다에서 <c>health</c> 가
    /// <b>0회</b>였다. 운석 직격은 <c>ApplyDamage</c> 를 거치지 않고 곧바로
    /// <c>GoDown</c> 이고 <c>downed</c> 는 전송되므로, 참가자에게 체력은 사실상
    /// <b>"가득" 아니면 "기절" 둘뿐</b>이었다.</para>
    ///
    /// <para><b>그리고 갈라질 수도 있었다.</b> <c>ApplyDamage</c> 에 권한 게이트가 없어
    /// 참가자 기계의 트리거가 자기 몸을 깎았다(실측: <c>ServerAuthority=False</c> 인데
    /// 1.000 → 0.700). <b>전송과 게이트는 짝이다</b> — 전송만 붙이면 두 값이 싸워
    /// 깜빡이고, 게이트만 두면 영영 안 뜬다.</para>
    /// </summary>
    public sealed class HealthNetworkStateTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── 받는 쪽 ─────────────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b></summary>
        [Test]
        public void AParticipantTakesTheHealthFromTheServer()
        {
            var rig = MakeParticipant();
            Assert.AreEqual(1f, rig.Health01, 1e-4f, "받기 전에는 가득이다.");

            rig.SetRemoteHealth(rig.MaxHealth * 0.4f);

            Assert.AreEqual(0.4f, rig.Health01, 1e-4f,
                "서버가 깎은 값이 그대로 들어와야 한다.");
        }

        /// <summary>
        /// <b>비율이 아니라 절댓값을 받는 이유.</b> <see cref="PlayerRig.IsDead"/> 가
        /// <c>Health &lt;= 0</c> 이라, 비율만 받으면 그 값이 로컬로 남아 어긋난다 —
        /// <c>DamageSource</c> 가 <c>IsDead</c> 로 피해를 거르므로 서버가 기절시킨
        /// 사람을 참가자 기계에서 계속 때리게 된다.
        /// </summary>
        [Test]
        public void ItAlsoFixesIsDead()
        {
            var rig = MakeParticipant();
            Assert.IsFalse(rig.IsDead);

            rig.SetRemoteHealth(0f);

            Assert.IsTrue(rig.IsDead, "서버가 0 이라고 하면 여기서도 죽은 것이다.");
        }

        [Test]
        public void HealingComesDownToo()
        {
            var rig = MakeParticipant();
            rig.SetRemoteHealth(rig.MaxHealth * 0.2f);

            rig.SetRemoteHealth(rig.MaxHealth);

            Assert.AreEqual(1f, rig.Health01, 1e-4f, "차오르는 것도 내려온다.");
        }

        /// <summary>
        /// 권위가 있는 몸은 받은 값을 <b>무시한다</b>. 두 곳에서 정하면 갈라진다
        /// (<c>-383</c>).
        /// </summary>
        [Test]
        public void AnAuthoritativeBodyIgnoresIt()
        {
            var rig = MakePlayer();
            Assert.IsTrue(rig.ServerAuthority, "기본값이 권위 있음이다.");

            rig.SetRemoteHealth(rig.MaxHealth * 0.1f);

            Assert.AreEqual(1f, rig.Health01, 1e-4f,
                "호스트는 자기 판정이 정답이다.");
        }

        [Test]
        public void TheIncomingValueIsClamped()
        {
            var rig = MakeParticipant();

            rig.SetRemoteHealth(-50f);
            Assert.AreEqual(0f, rig.Health01, 1e-4f);

            rig.SetRemoteHealth(rig.MaxHealth * 10f);
            Assert.AreEqual(1f, rig.Health01, 1e-4f);
        }

        // ── 참가자는 로컬로 깎지 않는다 ─────────────────────────

        /// <summary>
        /// <b>전송과 짝이다.</b> 이것이 없으면 서버 값이 내려오는 동안 로컬에서도 깎여
        /// 화면이 깜빡인다.
        /// </summary>
        [Test]
        public void AParticipantCannotDamageItself()
        {
            var rig = MakeParticipant();

            rig.ApplyDamage(rig.MaxHealth * 0.5f, "검증");

            Assert.AreEqual(1f, rig.Health01, 1e-4f,
                "판정은 서버가 한다 — 참가자 기계의 트리거가 깎으면 값이 갈라진다.");
        }

        [Test]
        public void TheHostStillTakesDamage()
        {
            var rig = MakePlayer();

            rig.ApplyDamage(rig.MaxHealth * 0.5f, "검증");

            Assert.AreEqual(0.5f, rig.Health01, 1e-4f, "호스트·솔로 회귀가 있으면 안 된다.");
        }

        [Test]
        public void EnoughDamageStillDownsTheHost()
        {
            var rig = MakePlayer();

            rig.ApplyDamage(rig.MaxHealth, "검증");

            Assert.IsTrue(rig.Downed, "체력이 다하면 쓰러진다(§20.1).");
        }

        // ── 보내는 쪽 (배선) ────────────────────────────────────

        /// <summary>
        /// 보내는 자리가 실제로 있는가. 이것이 없으면 위의 받는 쪽 검증이 전부 통과해도
        /// 게임에서는 아무 일이 일어나지 않는다.
        ///
        /// <para><b>파일 전체가 아니라 해당 메서드 안을 본다</b> — <c>-505</c> 에서
        /// 파일 전체를 보다가 호출을 지워도 통과하는 검증을 만들었다.</para>
        /// </summary>
        [Test]
        public void TheServerActuallySendsIt()
        {
            var net = TestSource.Read("Runtime/Network/NetPlayer.cs");
            var blanked = TestSource.Blank(net);

            Assert.That(blanked, Does.Contain("health = body.Health;"),
                "서버가 채우지 않으면 참가자에게 아무것도 안 온다.");
            Assert.That(blanked, Does.Contain("body.SetRemoteHealth(health)"),
                "받은 값을 몸에 넣는 자리가 있어야 한다.");
            Assert.That(blanked, Does.Contain("isLocalPlayer"),
                "내 몸에만 넣는다 — 남의 체력을 읽는 곳이 없다.");
        }

        // ── 테두리가 그리기로 하는가 ────────────────────────────

        [Test]
        public void ACooledFlashDrawsNothing()
        {
            Assert.Zero(UI.GameHud.HealthFrameStrength(0f),
                "다 식으면 테두리가 없어야 한다.");
        }

        [Test]
        public void AHarderHitIsStronger()
        {
            var light = UI.GameHud.HealthFrameStrength(0.2f);
            var heavy = UI.GameHud.HealthFrameStrength(0.9f);

            Assert.Greater(light, 0f, "조금만 맞아도 보여야 한다.");
            Assert.Greater(heavy, light, "세게 맞을수록 짙어진다.");
        }

        [Test]
        public void ANearlyCooledFlashStillDrawsNothing()
        {
            Assert.Zero(UI.GameHud.HealthFrameStrength(1e-7f),
                "부동소수 찌꺼기로 테두리가 깜빡이면 안 된다.");
        }

        // ── 구조된 뒤에 테두리가 남지 않는가 (S15P21B202-539) ──

        /// <summary>
        /// <b>이 검증이 원래 문제다.</b> 구조는 체력을 절반만 돌려주는데
        /// (<c>PhysicsScale.RescueHealthRatio</c>) 예전 테두리는 남은 체력을 읽어서
        /// 판이 끝날 때까지 빨간 테두리가 남았다.
        /// </summary>
        [Test]
        public void RescueLeavesNoFrame()
        {
            var rig = MakePlayer();
            rig.ResetHealth();

            rig.GoDown("검증");
            Assert.Greater(rig.DamageFlash01, 0f, "쓰러질 때는 번쩍인다.");

            rig.Revive(rig.MaxHealth * PhysicsScale.RescueHealthRatio);

            // 번쩍임이 식을 만큼 시간을 보낸다.
            int ticks = Mathf.CeilToInt(Tuning.DamageFlashSeconds / Time.fixedDeltaTime) + 2;
            for (int i = 0; i < ticks; i++) rig.Tick();

            Assert.AreEqual(0.5f, rig.Health01, 1e-4f,
                "밸런스는 그대로다 — 구조는 절반만 돌려준다.");
            Assert.Zero(UI.GameHud.HealthFrameStrength(rig.DamageFlash01),
                "체력이 절반이어도 구조된 뒤에는 테두리가 없어야 한다.");
        }

        /// <summary>
        /// 체력이 늘어나는 것은 번쩍이지 않는다 — 구조와 판 초기화가 그 경로다.
        /// </summary>
        [Test]
        public void HealingDoesNotFlash()
        {
            var rig = MakePlayer();          // 권위가 있어야 ApplyDamage 가 돈다
            rig.ApplyDamage(rig.MaxHealth * 0.3f, "검증");
            Assert.Greater(rig.DamageFlash01, 0f, "맞으면 번쩍인다.");

            int ticks = Mathf.CeilToInt(Tuning.DamageFlashSeconds / Time.fixedDeltaTime) + 2;
            for (int i = 0; i < ticks; i++) rig.Tick();
            Assert.Zero(rig.DamageFlash01, "식을 시간을 줬다.");

            rig.ResetHealth();

            Assert.Zero(rig.DamageFlash01, "체력이 차오르는 것은 피해가 아니다.");
        }

        /// <summary>
        /// 참가자 기계도 번쩍인다. 거기서는 <c>ApplyDamage</c> 가 막혀 있고
        /// (<see cref="AParticipantCannotDamageItself"/>) 서버가 보낸 값만 들어오므로,
        /// 세터에 번쩍임을 두지 않으면 <b>참가자에게만 테두리가 안 뜬다</b> —
        /// <c>-513</c> 이 겪은 것과 같은 모양의 구멍이다.
        /// </summary>
        [Test]
        public void AParticipantFlashesFromTheServerValue()
        {
            var rig = MakeParticipant();

            rig.SetRemoteHealth(rig.MaxHealth * 0.6f);

            Assert.Greater(rig.DamageFlash01, 0f,
                "서버가 깎은 것도 피해다.");
        }

        // ── 도구 ────────────────────────────────────────────────

        PlayerRig MakePlayer()
        {
            // Prim.Decor 가 내는 로그만 넘긴다. 만들고 나면 바로 되돌려서 이후 경로의
            // 에러는 검증을 빨간불로 만든다.
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            _spawned.Add(player.gameObject);
            return player;
        }

        /// <summary>참가자 기계의 자기 몸 — 권위가 없다.</summary>
        PlayerRig MakeParticipant()
        {
            var player = MakePlayer();
            player.ServerAuthority = false;
            return player;
        }
    }
}
