using System.Collections.Generic;
using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 끼임 탈출을 검증한다(기획 §21, S15P21B202-248).
    ///
    /// <b>기능은 이미 있었고 §21 을 다섯 군데에서 어겼다.</b> 그중 <c>Revive</c> 호출은
    /// §20 을 통째로 무너뜨렸다 — §20.3 의 "동료가 구조할 때까지", 솔로 6초 대기,
    /// §20.5 의 전원 기절 실패가 모두 <c>T</c> 한 번으로 우회됐다. 그래서 이 스위트의
    /// 절반은 <b>일어나지 않아야 하는 일</b>을 잠근다.
    /// </summary>
    public sealed class StuckEscapeTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
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
            PlayerRig.All.Clear();
        }

        // --- 3초 유지 ------------------------------------------------------

        [Test]
        public void HoldingBriefly_DoesNothing()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);
            var before = player.FootPosition;

            Hold(escape, PhysicsScale.StuckEscapeSeconds - 0.1f);

            Assert.AreEqual(0, escape.EscapeCount, "3초 전에는 옮기지 않는다.");
            Assert.AreEqual(before, player.FootPosition);
        }

        [Test]
        public void HoldingForThreeSeconds_Moves()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);
            var before = player.FootPosition;

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);

            Assert.AreEqual(1, escape.EscapeCount);
            Assert.Greater(Vector3.Distance(before, player.FootPosition), 0.5f,
                "가까운 다른 자리로 옮겨야 한다.");
        }

        [Test]
        public void TheProgressIsReportedForDisplay()
        {
            MakeGround();
            var escape = new StuckEscape(MakePlayer(Vector3.zero));

            Assert.AreEqual(0f, escape.Progress01, 1e-3f);

            Hold(escape, PhysicsScale.StuckEscapeSeconds * 0.5f);

            Assert.AreEqual(0.5f, escape.Progress01, 0.05f);
        }

        /// <summary>
        /// 고정못(§12.1)처럼 진행분을 유지하면 끼지 않았을 때 조금씩 눌러 모아 둘 수 있고,
        /// 그러면 "3초 유지" 가 아니라 "합쳐서 3초" 가 된다.
        /// </summary>
        [Test]
        public void LettingGo_ThrowsTheProgressAway()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds - 0.2f);
            escape.Tick(Step, false);                       // 놓았다

            Assert.AreEqual(0f, escape.Progress01, 1e-3f);

            Hold(escape, 0.4f);                             // 다시 눌러 합쳐서 3초 넘김
            Assert.AreEqual(0, escape.EscapeCount, "합쳐서 3초로는 안 된다.");
        }

        // --- 기절 중에는 안 된다 (§21) --------------------------------------

        [Test]
        public void WhileDowned_TheProgressDoesNotBuild()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            player.GoDown("검증");

            var escape = new StuckEscape(player);
            Hold(escape, PhysicsScale.StuckEscapeSeconds * 2f);

            Assert.AreEqual(0f, escape.Progress01, 1e-3f);
            Assert.AreEqual(0, escape.EscapeCount);
        }

        /// <summary>
        /// <b>예전에는 이것이 됐다.</b> <c>Respawn</c> 이 <c>Revive(MaxHealth)</c> 를 불러
        /// §20.3 "동료가 구조할 때까지 일어나지 않는다" 와 §20.5 "전원 기절이면 스테이지
        /// 실패" 를 <c>T</c> 한 번으로 우회했다.
        /// </summary>
        [Test]
        public void EscapingDoesNotReviveYou()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            player.GoDown("운석 직접 충돌");

            var escape = new StuckEscape(player);
            Hold(escape, PhysicsScale.StuckEscapeSeconds * 2f);

            Assert.IsTrue(player.Downed, "T 로 기절이 풀리면 안 된다.");
            Assert.AreEqual(0f, player.Health, 1e-3f);
        }

        /// <summary>
        /// 맞기 직전에 눌러 두고 기절한 뒤에 채우는 것도 막는다 — 그러면 §20.3 의
        /// 구조 대기가 의미를 잃는다.
        /// </summary>
        [Test]
        public void GoingDownMidHold_ThrowsTheProgressAway()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds - 0.2f);
            player.GoDown("운석 직접 충돌");
            Hold(escape, 1f);

            Assert.AreEqual(0f, escape.Progress01, 1e-3f);
            Assert.AreEqual(0, escape.EscapeCount);
            Assert.IsTrue(player.Downed);
        }

        // --- 가까운 안전 지면 ----------------------------------------------

        [Test]
        public void ItLandsOnTheGround()
        {
            MakeGround(height: 3f);
            var player = MakePlayer(new Vector3(0f, 3f, 0f));
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);

            Assert.AreEqual(1, escape.EscapeCount);
            Assert.AreEqual(3f, player.FootPosition.y, 0.2f, "지면 위에 서야 한다.");
        }

        /// <summary>
        /// §21 은 "<b>가까운</b> 안전 지면" 이다. 스테이지 처음 자리로 보내면 20분짜리
        /// 스테이지에서 끼었다고 처벌하는 것이 된다.
        /// </summary>
        [Test]
        public void ItStaysNearby()
        {
            MakeGround();
            var player = MakePlayer(new Vector3(30f, 0f, 30f));
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);

            Assert.AreEqual(1, escape.EscapeCount);
            Assert.LessOrEqual(
                Vector3.Distance(new Vector3(30f, 0f, 30f), player.FootPosition),
                PhysicsScale.StuckEscapeSearchRadius + 1f,
                "스폰 지점이 아니라 가까운 자리여야 한다.");
        }

        /// <summary>벽 속으로 옮기는 것보다 아무것도 하지 않는 것이 낫다.</summary>
        [Test]
        public void WithNoGroundAnywhere_NothingHappens()
        {
            var player = MakePlayer(Vector3.zero);   // 지면을 안 만든다
            var escape = new StuckEscape(player);
            var before = player.FootPosition;

            Hold(escape, PhysicsScale.StuckEscapeSeconds * 2f);

            Assert.AreEqual(0, escape.EscapeCount);
            Assert.AreEqual(before, player.FootPosition);
            Assert.IsTrue(escape.WaitingForRoom, "이유를 화면에 보여줄 수 있어야 한다.");
        }

        [Test]
        public void ABlockedSpotIsNotSafe()
        {
            MakeGround();

            // 몸이 들어갈 자리에 상자를 놓는다.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "장애물";
            box.layer = GameLayers.Prop;
            box.transform.position = new Vector3(0f, 0.5f, 0f);
            _spawned.Add(box);
            UnityEngine.Physics.SyncTransforms();

            Assert.IsFalse(SafeGround.IsClear(Vector3.zero), "막힌 자리다.");
            Assert.IsTrue(SafeGround.IsClear(new Vector3(5f, 0f, 0f)), "빈 자리다.");
        }

        /// <summary>
        /// 자기 팔다리가 "자리가 없다" 를 만들면 영영 못 나온다. 지면도 세지 않는다 —
        /// 발이 지면에 닿아 있는 것이 정상이다.
        /// </summary>
        [Test]
        public void YourOwnBodyAndTheGroundDoNotBlockYou()
        {
            MakeGround();
            MakePlayer(Vector3.zero);

            Assert.IsTrue(SafeGround.IsClear(Vector3.zero));
        }

        // --- 장비는 이동 전 위치에 (§21) ------------------------------------

        [Test]
        public void TheHeldToolStaysBehind()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            var tool = MakeTool(new Vector3(0.4f, 0.5f, 0f));
            var toolPosition = tool.transform.position;

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);

            Assert.AreEqual(1, escape.EscapeCount);
            Assert.AreEqual(toolPosition, tool.transform.position,
                "도구가 새 자리까지 따라오면 안 된다(§21).");
        }

        // --- 캐릭터만 옮긴다 (§21) ------------------------------------------

        [Test]
        public void AfterEscaping_YouAreStanding()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);

            Assert.IsFalse(player.Ragdolled, "§21 은 캐릭터만 옮기는 것이다.");
            Assert.IsFalse(player.Downed);
        }

        [Test]
        public void EscapingTwice_NeedsThreeSecondsAgain()
        {
            MakeGround();
            var player = MakePlayer(Vector3.zero);
            var escape = new StuckEscape(player);

            Hold(escape, PhysicsScale.StuckEscapeSeconds + Step);
            Assert.AreEqual(1, escape.EscapeCount);

            Hold(escape, PhysicsScale.StuckEscapeSeconds - 0.2f);
            Assert.AreEqual(1, escape.EscapeCount, "진행도가 0에서 다시 시작해야 한다.");
        }

        // --- 리그 ---------------------------------------------------------

        static void Hold(StuckEscape escape, float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Step);
            for (var i = 0; i < steps; i++) escape.Tick(Step, true);
        }

        void MakeGround(float height = 0f)
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, height - 0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(200f, 1f, 200f);
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
        }

        GameObject MakeTool(Vector3 position)
        {
            var go = new GameObject("PF_Stage1_WeldingPlasmaGun") { layer = GameLayers.Prop };
            go.transform.position = position;
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<Rigidbody>();
            body.mass = 4f;
            body.useGravity = false;
            body.isKinematic = true;   // 물리로 굴러가는 것과 따라오는 것을 섞지 않는다
            _spawned.Add(go);
            UnityEngine.Physics.SyncTransforms();
            return go;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
