using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절(다운)과 동료 구조를 검증한다(기획 S15P21B202-107 §26-13).
    ///
    /// 예전에는 이 상태가 없었다. 체력이 0이 되면 <c>GameSession</c> 이 곧바로
    /// <c>Respawn()</c> 을 불러 스폰 지점에 되살렸다 — 예전 SpaceFlop 코스의 낙사
    /// 처리를 그대로 쓴 것이다. <b>운석에 쓰러지는 순간 혼자 되살아나</b> 팀원이
    /// 도우러 갈 틈도 이유도 없었다.
    ///
    /// <b>이 규칙의 절반은 "여전히 스스로 일어나는가" 다.</b> 자동 기립을 막는 변경이
    /// S15P21B202-201(사출)과 211(마지막 고정못 넘어짐)이 쓰는 경로를 건드린다.
    /// 그 둘은 다운이 아니므로 계속 일어나야 한다.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 는 <c>PlayerRig.Create</c> 경로의
    /// <c>Prim.Decor</c> 가 EditMode 에서 남기는 에러 로그 때문이다.
    /// </summary>
    public sealed class DownedRescueTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
        }

        // --- 다운 ----------------------------------------------------------

        /// <summary>
        /// <b>이 이슈의 핵심이다.</b> 체력이 0이 되면 그 자리에 쓰러진다.
        /// 순간이동하지 않는다.
        /// </summary>
        [Test]
        public void LosingAllHealth_DownsThePlayerInPlace()
        {
            var player = MakePlayer(new Vector3(3f, 0f, 4f));
            var before = player.Doll.Hips.position;

            player.ApplyDamage(player.MaxHealth, "운석");

            Assert.IsTrue(player.Downed, "체력이 0이면 기절해야 한다.");
            Assert.AreEqual(before.x, player.Doll.Hips.position.x, 0.5f, "그 자리에 쓰러져야 한다.");
            Assert.AreEqual(before.z, player.Doll.Hips.position.z, 0.5f, "그 자리에 쓰러져야 한다.");
        }

        /// <summary>기절한 플레이어는 스스로 일어나지 않는다. 동료가 필요하다.</summary>
        [Test]
        public void DownedPlayer_DoesNotGetUpByItself()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");

            Run(player, 10f);

            Assert.IsTrue(player.Downed, "10초가 지나도 스스로 일어나면 구조가 필요 없어진다.");
            Assert.AreEqual(0f, player.Health, 0.001f);
        }

        /// <summary>기절 중에는 조작이 먹지 않는다.</summary>
        [Test]
        public void DownedPlayer_CannotMove()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");

            var before = player.Doll.Hips.position;
            RunWithInput(player, new Core.InputFrame { Move = new Vector2(0f, 1f) }, 3f);

            var moved = player.Doll.Hips.position - before;
            moved.y = 0f;

            Assert.Less(moved.magnitude, 1f, $"기절 중에는 걸을 수 없다. {moved.magnitude:0.00}m 움직였다.");
            Assert.AreEqual(0f, player.Speed, 0.001f);
        }

        /// <summary>
        /// 기절 중에도 넉백은 받는다 — 운석에 굴러다녀야 §26-13 의 긴장이 생긴다.
        /// </summary>
        [Test]
        public void DownedPlayer_StillTakesKnockback()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");
            Run(player, 0.5f);

            var before = player.Doll.Hips.position;
            for (var i = 0; i < player.Doll.Bodies.Count; i++)
            {
                var body = player.Doll.Bodies[i];
                if (body != null && !body.isKinematic) body.linearVelocity += Vector3.right * 5f;
            }

            Run(player, 1.5f);

            var moved = player.Doll.Hips.position - before;
            moved.y = 0f;

            Assert.Greater(moved.magnitude, 1f, $"넉백은 받아야 한다. {moved.magnitude:0.00}m 만 밀렸다.");
        }

        /// <summary>
        /// 기절하면 <c>PlayerDowned</c> 레이어로 옮긴다. 그 레이어는 이미 있었고
        /// 충돌 마스크들이 그것을 전제한다.
        /// </summary>
        [Test]
        public void DownedPlayer_MovesToTheDownedLayer()
        {
            var player = MakePlayer(Vector3.zero);
            Assert.AreEqual(GameLayers.Player, player.Doll.Hips.gameObject.layer);

            player.ApplyDamage(player.MaxHealth, "운석");
            Assert.AreEqual(GameLayers.PlayerDowned, player.Doll.Hips.gameObject.layer);

            player.Revive(player.MaxHealth);
            Assert.AreEqual(GameLayers.Player, player.Doll.Hips.gameObject.layer,
                "일어나면 원래 레이어로 돌아와야 한다.");
        }

        /// <summary>
        /// 기절하면 잡고 있던 것을 놓는다. 쓰러진 사람이 고정못을 계속 잡고 있으면
        /// 정원(§12.2 최대 2명) 때문에 다른 사람이 붙을 수 없다.
        /// </summary>
        [Test]
        public void GoingDown_LetsGoOfEverything()
        {
            var rig = MakeClampRig();

            RunWithInput(rig.Player, AwayInput(rig), 1f);
            Assert.IsTrue(rig.Player.ClampGrabbed, "먼저 고정못을 잡아야 한다.");
            var clamp = rig.Player.ClampGrabTarget;
            Assert.AreEqual(1, clamp.Pullers);

            rig.Player.ApplyDamage(rig.Player.MaxHealth, "운석");

            Assert.IsFalse(rig.Player.ClampGrabbed, "기절하면 놓아야 한다.");
            Assert.AreEqual(0, clamp.Pullers, "인원이 남으면 다른 사람이 정원에 막힌다.");
            Assert.Greater(clamp.Progress, 0f, "뽑힌 정도는 남는다(기획 §12.1).");
        }

        // --- 자동 기립이 여전히 되는가 (201 · 211 회귀) ----------------------

        /// <summary>
        /// <b>S15P21B202-201 회귀.</b> 발사 실패로 사출된 플레이어는 기절이 아니다 —
        /// 기획 §22.4 가 "기절하지 않고 자동으로 일어난다" 로 못박았다.
        /// </summary>
        [Test]
        public void EjectedPlayer_StillGetsUpByItself()
        {
            var player = MakePlayer(Vector3.zero);

            var block = new GameObject("Module");
            block.transform.position = new Vector3(0f, 1f, 0f);
            var body = block.AddComponent<Rigidbody>();
            body.isKinematic = true;
            spawned.Add(block);

            Assert.IsTrue(player.TryBoardShip(body, block.transform.position));
            player.EjectFromShip(Vector3.forward);

            Assert.IsFalse(player.Downed, "사출은 기절이 아니다.");
            Assert.AreEqual(player.MaxHealth, player.Health, 0.001f, "체력을 깎지 않는다.");

            Run(player, 5f);

            Assert.IsFalse(player.Ragdolled, "5초 안에 스스로 일어나야 한다.");
            Assert.IsFalse(player.Downed);
        }

        /// <summary>
        /// <b>S15P21B202-211 회귀.</b> 마지막 고정못이 빠져 넘어진 플레이어도
        /// 기절이 아니다 — 기획 §12.2 는 "뒤로 넘어질 수 있다" 까지다.
        /// </summary>
        [Test]
        public void PlayerKnockedBackByLastClamp_StillGetsUpByItself()
        {
            var rig = MakeClampRig(clampCount: 1);

            for (var i = 0; i < 1000 && !rig.Clamp.IsReleased; i++)
                RunWithInput(rig.Player, AwayInput(rig), Step);

            Assert.IsTrue(rig.Clamp.IsReleased, "마지막 고정못이 빠져야 한다.");
            Assert.IsTrue(rig.Player.Ragdolled, "넘어져야 한다.");
            Assert.IsFalse(rig.Player.Downed, "넘어짐은 기절이 아니다.");

            Run(rig.Player, 5f);

            Assert.IsFalse(rig.Player.Ragdolled, "5초 안에 스스로 일어나야 한다.");
        }

        // --- 동료 구조 ------------------------------------------------------

        [Test]
        public void RescueTarget_OnlyExistsWhileDowned()
        {
            var player = MakePlayer(Vector3.zero);

            player.ApplyDamage(player.MaxHealth, "운석");
            Assert.IsNotNull(player.RescueTarget, "기절하면 구조 지점이 생겨야 한다.");
            Assert.IsTrue(player.RescueTarget.gameObject.activeInHierarchy);

            player.Revive(player.MaxHealth);
            Assert.IsFalse(player.RescueTarget.gameObject.activeInHierarchy,
                "서 있는 플레이어에게 붙어 있으면 아무 때나 남을 밀 수 있게 된다.");
        }

        [Test]
        public void Rescue_TakesThePlannedTimeAndRestoresHalfHealth()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");

            var rescue = player.RescueTarget;
            Assert.AreEqual(PhysicsScale.RescueSeconds, rescue.HoldSeconds, 0.01f);

            var interactor = MakeInteractor(rescue);
            Assert.IsTrue(interactor.TryBegin(), "구조를 시작할 수 있어야 한다.");

            interactor.Tick(PhysicsScale.RescueSeconds * 0.5f);
            Assert.IsTrue(player.Downed, "절반만 눌렀는데 일어나면 안 된다.");

            interactor.Tick(PhysicsScale.RescueSeconds * 0.5f + 0.05f);

            Assert.IsFalse(player.Downed, "구조가 끝나면 일어나야 한다.");
            Assert.AreEqual(player.MaxHealth * PhysicsScale.RescueHealthRatio,
                            player.Health, 0.5f, "체력을 절반 돌려준다.");
        }

        /// <summary>고정못·용접선과 같은 성질 — 놓아도 남고 이어받는다.</summary>
        [Test]
        public void Rescue_KeepsProgressAndCanBeHandedOver()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");
            var rescue = player.RescueTarget;

            var first = MakeInteractor(rescue);
            first.TryBegin();
            first.Tick(PhysicsScale.RescueSeconds * 0.6f);
            first.Cancel();

            Assert.IsTrue(player.Downed, "취소했으므로 아직 기절 중이다.");
            Assert.Greater(rescue.Progress, 0.4f, "누른 만큼 남아야 한다.");
            Assert.Less(rescue.HoldSeconds, PhysicsScale.RescueSeconds,
                "남은 시간만 더 누르면 된다.");

            var second = MakeInteractor(rescue);
            Assert.IsTrue(second.TryBegin(), "다른 사람이 이어받을 수 있어야 한다.");
            second.Tick(PhysicsScale.RescueSeconds);

            Assert.IsFalse(player.Downed, "이어받아 끝낼 수 있어야 한다.");
        }

        /// <summary>다음 기절은 처음부터다. 진행분을 물려주지 않는다.</summary>
        [Test]
        public void Rescue_ProgressResetsForTheNextDown()
        {
            var player = MakePlayer(Vector3.zero);
            player.ApplyDamage(player.MaxHealth, "운석");
            var rescue = player.RescueTarget;

            var interactor = MakeInteractor(rescue);
            interactor.TryBegin();
            interactor.Tick(PhysicsScale.RescueSeconds * 0.7f);
            interactor.Cancel();
            Assert.Greater(rescue.Progress, 0f);

            player.Revive(player.MaxHealth);
            player.ApplyDamage(player.MaxHealth, "운석");

            Assert.AreEqual(0f, rescue.Progress, 0.001f, "다음 기절은 처음부터다.");
            Assert.AreEqual(PhysicsScale.RescueSeconds, rescue.HoldSeconds, 0.01f);
        }

        // --- 헬퍼 ----------------------------------------------------------

        struct ClampRig
        {
            public PlayerRig Player;
            public SpaceshipPart Part;
            public PartClamp Clamp;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            MakeGround();

            LogAssert.ignoreFailingMessages = true;
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }

        /// <summary>고정못 하나와 그 앞에 선 플레이어. <c>ClampPullTests</c> 와 같은 방식이다.</summary>
        ClampRig MakeClampRig(int clampCount = 4)
        {
            MakeGround();

            var partGo = new GameObject("Part") { layer = GameLayers.Part };
            partGo.transform.position = new Vector3(0f, PhysicsScale.PartHeight * 0.5f, 0f);
            partGo.AddComponent<BoxCollider>().size = Vector3.one * PhysicsScale.PartHeight;
            partGo.AddComponent<Rigidbody>();
            spawned.Add(partGo);

            var part = partGo.AddComponent<SpaceshipPart>();

            PartClamp first = null;
            for (var i = 0; i < clampCount; i++)
            {
                // 첫 고정못만 손이 닿는 곳에 둔다 — 여벌은 조준에 걸리면 대상이 오간다.
                // -Z 쪽에 두는 이유는 S15P21B202-218 이 캐릭터를 카메라 정면으로 돌리게
                // 바꿨고 이 검증에는 카메라가 없어 월드 +Z 를 보기 때문이다
                // (ClampPullTests 와 같은 이유).
                var offset = i == 0 ? Vector3.back * 0.8f : Vector3.right * (6f + i);

                var clampGo = new GameObject($"Clamp_{i}") { layer = GameLayers.Interactable };
                clampGo.transform.SetParent(partGo.transform, false);
                clampGo.transform.localPosition =
                    offset - Vector3.up * (PhysicsScale.PartHeight * 0.5f);
                clampGo.AddComponent<SphereCollider>().isTrigger = true;

                var clamp = clampGo.AddComponent<PartClamp>();
                clamp.Initialize();
                clamp.Released += released => spawned.Add(released.gameObject);
                if (i == 0) first = clamp;
            }

            part.Initialize();
            part.RefreshClamps();
            UnityEngine.Physics.SyncTransforms();

            var foot = first.transform.position + Vector3.back * 1.2f;
            foot.y = 0f;

            LogAssert.ignoreFailingMessages = true;
            var player = PlayerRig.Create("테스트", foot,
                Quaternion.LookRotation(Vector3.forward, Vector3.up),
                Color.white, Color.gray, false);
            spawned.Add(player.gameObject);

            UnityEngine.Physics.SyncTransforms();
            return new ClampRig { Player = player, Part = part, Clamp = first };
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            spawned.Add(go);
        }

        /// <summary>구조 지점을 정면에서 바라보는 Interactor.</summary>
        Interactor MakeInteractor(DownedRescue rescue)
        {
            var position = rescue.transform.position - Vector3.forward * 1.2f;

            var go = new GameObject("Interactor");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.forward));
            spawned.Add(go);

            var interactor = go.AddComponent<Interactor>();
            interactor.Configure(go.transform, 3f, 80f);

            UnityEngine.Physics.SyncTransforms();
            interactor.Tick(0f);
            return interactor;
        }

        static Core.InputFrame AwayInput(ClampRig rig)
        {
            var target = rig.Player.ClampGrabTarget != null ? rig.Player.ClampGrabTarget : rig.Clamp;

            var away = rig.Player.Doll.Hips.position - target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            away.Normalize();

            var fwd = rig.Player.Cam != null ? rig.Player.Cam.PlanarForward : Vector3.forward;
            var right = rig.Player.Cam != null ? rig.Player.Cam.PlanarRight : Vector3.right;

            return new Core.InputFrame
            {
                GrabLeft = true,
                Move = new Vector2(Vector3.Dot(away, right), Vector3.Dot(away, fwd)),
            };
        }

        void Run(PlayerRig player, float seconds) =>
            RunWithInput(player, new Core.InputFrame(), seconds);

        void RunWithInput(PlayerRig player, Core.InputFrame input, float seconds)
        {
            var step = Time.fixedDeltaTime;
            for (var s = 0; s < Mathf.Max(1, Mathf.RoundToInt(seconds / step)); s++)
            {
                player.SetInput(input);
                player.Tick();
                UnityEngine.Physics.Simulate(step);
            }
        }
    }
}
