using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 광역 저중력장 안에서 플레이어가 뜨는 것을 검증한다.
    ///
    /// 파츠는 저중력장이 힘으로 끌어당기지만 플레이어는 그러지 않는다. 기획
    /// (S15P21B202-107 §14.3)이 "플레이어는 중심으로 강제 견인되지 않고 직접 움직인다" 고
    /// 정했기 때문이다. 그래서 여기서 보는 것은 "끌려가지 않는가" 와 "직접 움직이는가" 다.
    ///
    /// 이것이 §14 의 목적지다. 3층 접합부가 3.50m, 4층이 5.71m 인데 플레이어가 닿는 높이는
    /// 약 3.2m 이고 제트팩은 기획 §5 에서 제거되었다. 이 부유가 없으면 3·4층을 용접할 수 없다.
    /// </summary>
    public sealed class PlayerFloatTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;
        bool savedIgnoreFailingMessages;

        [SetUp]
        public void SetUp()
        {
            savedMode = UnityEngine.Physics.simulationMode;
            savedIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = savedMode;
            LogAssert.ignoreFailingMessages = savedIgnoreFailingMessages;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            LowGravityField.All.Clear();
        }

        GameObject MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            spawned.Add(go);
            return go;
        }

        /// <summary>꼭짓점이 위에 있는 저중력장. 아래로 펼쳐진다.</summary>
        LowGravityField MakeField(Vector3 apex)
        {
            var go = new GameObject("Craft");
            go.transform.position = apex;
            spawned.Add(go);

            var field = go.AddComponent<LowGravityField>();
            field.SetActive(true);
            return field;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            // Prim.Decor 가 만드는 로그는 이 검증과 무관하다.
            LogAssert.ignoreFailingMessages = true;

            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        void Run(PlayerRig player, InputFrame input, float seconds)
        {
            var steps = Mathf.RoundToInt(seconds / Step);
            for (var i = 0; i < steps; i++)
            {
                player.SetInput(input);
                player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }

        // --- 상태 전환 -------------------------------------------------------

        [Test]
        public void FieldVisual_TogglesAndStaysWorldDownWhenCraftRolls()
        {
            var field = MakeField(new Vector3(0f, 8f, 0f));
            var effect = field.gameObject.AddComponent<LowGravityFieldVisual>();
            effect.Initialize(field);
            effect.TickVisual(0.3f);
            var renderer = field.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(renderer);
            Assert.IsTrue(renderer.enabled);
            Assert.Greater(renderer.sharedMaterial.GetFloat("_Visibility"), 0.9f);
            field.transform.rotation = Quaternion.Euler(85f, 60f, 20f);
            effect.TickVisual(0.1f);
            Assert.Less(Vector3.Distance(renderer.transform.position, field.Origin.position), 0.001f);
            Assert.Greater(Vector3.Dot(renderer.transform.up, Vector3.up), 0.999f);
            field.SetActive(false);
            effect.TickVisual(0.3f);
            Assert.IsFalse(renderer.enabled);
            field.SetActive(true);
            effect.TickVisual(0.3f);
            Assert.IsTrue(renderer.enabled);
        }

        [Test]
        public void Player_IsLiftedWithoutJump_ThenHoldsHeight()
        {
            MakeGround();
            MakeField(new Vector3(0f, 8f, 0f));
            var player = MakePlayer(Vector3.zero);
            float start = player.Doll.Hips.position.y;
            Run(player, default, 2f);
            Assert.Greater(player.Doll.Hips.position.y, start + 1f);
            Run(player, default, 1f);
            Assert.Less(Mathf.Abs(player.Doll.Hips.linearVelocity.y), 0.4f);
        }

        [Test]
        public void Player_EntersFloating_InsideTheField()
        {
            MakeGround();
            MakeField(new Vector3(0f, 8f, 0f));
            var player = MakePlayer(new Vector3(0f, 0f, 0f));

            Run(player, default, 0.2f);

            Assert.IsTrue(player.Floating, "저중력장 안이면 부유 상태여야 한다.");
            Assert.IsFalse(player.Grounded,
                "부유 중에는 접지가 아니어야 한다. 접지로 잡히면 지면 스프링이 부유를 밀어 올린다.");
        }

        [Test]
        public void Player_DoesNotFloat_OutsideTheField()
        {
            MakeGround();
            MakeField(new Vector3(0f, 8f, 0f));

            // 지면 반경 4m 를 확실히 벗어난 곳.
            var player = MakePlayer(new Vector3(12f, 0f, 0f));

            Run(player, default, 0.2f);

            Assert.IsFalse(player.Floating, "영역 밖에서는 부유하지 않아야 한다.");
        }

        [Test]
        public void Player_DoesNotFloat_WhenFieldIsOff()
        {
            MakeGround();
            var field = MakeField(new Vector3(0f, 8f, 0f));
            field.SetActive(false);

            var player = MakePlayer(Vector3.zero);
            Run(player, default, 0.2f);

            Assert.IsFalse(player.Floating, "꺼진 저중력장에서는 부유하지 않아야 한다.");
        }

        // --- 강제 견인이 아니다 ----------------------------------------------

        /// <summary>
        /// 기획 §14.3 — "플레이어는 중심으로 강제 견인되지 않고 직접 움직인다".
        /// 파츠는 축으로 끌려가지만 사람은 가만히 있으면 그 자리에 머물러야 한다.
        /// </summary>
        [Test]
        public void Player_IsNotPulledTowardTheAxis()
        {
            MakeGround();
            MakeField(new Vector3(0f, 8f, 0f));

            var start = new Vector3(2.2f, 0f, 0f);
            var player = MakePlayer(start);

            Run(player, default, 1.5f);

            var hips = player.Doll.Hips.position;
            var drift = new Vector2(hips.x - start.x, hips.z - start.z).magnitude;

            Assert.IsTrue(player.Floating, "이 검증은 부유 중이어야 의미가 있다.");
            Assert.Less(drift, 0.6f,
                $"축 쪽으로 {drift:0.00}m 끌려갔다. 사람은 강제 견인 대상이 아니다.");
        }

        // --- 직접 움직인다 ---------------------------------------------------

        /// <summary>Space 로 올라간다. 이것이 3·4층 접합부에 닿는 유일한 수단이다.</summary>
        [Test]
        public void Player_RisesWithJumpInput()
        {
            MakeGround();
            MakeField(new Vector3(0f, 9f, 0f));
            var player = MakePlayer(Vector3.zero);

            var start = player.Doll.Hips.position.y;
            Run(player, new InputFrame { Jump = true }, 2f);

            var risen = player.Doll.Hips.position.y - start;
            Assert.Greater(risen, 1.5f,
                $"2초 동안 {risen:0.00}m 밖에 오르지 않았다. 초당 약 2m 로 올라야 한다.");
        }

        [Test]
        public void Player_DescendsWithArmsUpInput()
        {
            MakeGround();
            MakeField(new Vector3(0f, 9f, 0f));
            var player = MakePlayer(new Vector3(0f, 5f, 0f));

            var start = player.Doll.Hips.position.y;
            Run(player, new InputFrame { ArmsUp = true }, 1f);

            Assert.Less(player.Doll.Hips.position.y, start - 0.8f, "Ctrl 을 누르면 내려가야 한다.");
        }

        /// <summary>입력을 놓으면 약 0.5초 관성 뒤 멈춘다(기획 §14.3).</summary>
        [Test]
        public void Player_StopsRisingAfterReleasingInput()
        {
            MakeGround();
            MakeField(new Vector3(0f, 9f, 0f));
            var player = MakePlayer(Vector3.zero);

            Run(player, new InputFrame { Jump = true }, 1f);
            Run(player, default, 1f);

            var speed = Mathf.Abs(player.Doll.Hips.linearVelocity.y);
            Assert.Less(speed, 0.4f,
                $"입력을 놓고 1초 뒤에도 세로 속도가 {speed:0.00}m/s 다. 0.5초 안에 멈춰야 한다.");
        }

        // --- 이탈 -----------------------------------------------------------

        /// <summary>영역을 벗어나면 일반 중력이 즉시 복구된다(기획 §14.3).</summary>
        [Test]
        public void Player_FallsAfterFieldTurnsOff()
        {
            MakeGround();
            var field = MakeField(new Vector3(0f, 9f, 0f));
            var player = MakePlayer(new Vector3(0f, 4f, 0f));

            Run(player, default, 0.4f);
            Assert.IsTrue(player.Floating, "먼저 떠 있어야 한다.");

            field.SetActive(false);
            var before = player.Doll.Hips.position.y;
            Run(player, default, 0.6f);

            Assert.IsFalse(player.Floating, "저중력장을 끄면 부유가 풀려야 한다.");
            Assert.Less(player.Doll.Hips.position.y, before - 0.2f, "떨어져야 한다.");
        }

        /// <summary>
        /// 떠 있는 것은 떨어지는 것이 아니다. 낙하 기준 높이를 갱신하지 않으면
        /// 부유했다 내려올 때 그 높이차가 추락으로 집계된다.
        /// </summary>
        [Test]
        public void Floating_DoesNotAccumulateAirTime()
        {
            MakeGround();
            MakeField(new Vector3(0f, 9f, 0f));
            var player = MakePlayer(Vector3.zero);

            Run(player, new InputFrame { Jump = true }, 1.5f);

            Assert.AreEqual(0f, player.AirTime, 0.001f,
                "부유는 낙하가 아니므로 체공 시간이 쌓이면 안 된다.");
        }
    }
}
