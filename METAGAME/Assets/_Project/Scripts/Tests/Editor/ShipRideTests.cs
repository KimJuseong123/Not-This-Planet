using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 우주선에 실제로 타고, 발사 실패로 사출되는 것을 검증한다
    /// (기획 S15P21B202-107 §22.4).
    ///
    /// S15P21B202-200 의 탑승은 <c>Riders</c> 를 세는 것뿐이었다. 플레이어를 우주선에
    /// 붙이지 않으므로 발사하면 발사대에 서서 우주선이 올라가는 것을 구경했다.
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 를 켜는 이유는 이 규칙과 무관하다 —
    /// <c>PlayerRig.Create</c> 경로의 <c>Prim.Decor</c> 가 EditMode 에서
    /// <c>Object.Destroy</c> 를 불러 에러 로그를 남긴다(develop 에 이미 있는 문제).
    /// </summary>
    public sealed class ShipRideTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> spawned = new();
        SimulationMode savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // --- 조립대 ---------------------------------------------------------

        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            spawned.Add(root);

            foreach (var box in spec.Boxes)
            {
                var child = new GameObject("Body");
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.CenterFromPartCenter(spec.Height);
                child.AddComponent<BoxCollider>().size = box.Size;
            }

            root.AddComponent<Rigidbody>();
            var part = root.AddComponent<SpaceshipPart>();

            foreach (var role in new[] { SocketRole.Base, SocketRole.Top })
            {
                var direction = role == SocketRole.Base ? Vector3.down : Vector3.up;
                var socket = new GameObject(role.ToString());
                socket.transform.SetParent(root.transform, false);
                socket.transform.localPosition = direction * (spec.Height * 0.5f);
                socket.transform.localRotation = Quaternion.LookRotation(direction, Vector3.forward);
                socket.AddComponent<PartSocket>().SetRole(role);
            }

            // 원형 용접선 하나(기획 §16.1). 예전에는 용접점 4개를 놓았다.
            var seam = new GameObject("WeldSeam");
            seam.transform.SetParent(root.transform, false);
            seam.transform.localPosition = Vector3.down * (spec.Height * 0.5f);
            seam.AddComponent<WeldSeam>();

            part.SetPlacedMass(spec.Mass);
            part.Initialize();
            part.RefreshSockets();
            UnityEngine.Physics.SyncTransforms();
            part.TryTransitionTo(PartState.Free);
            return part;
        }

        void MakeGround()
        {
            var go = new GameObject("Ground") { layer = GameLayers.Ground };
            go.transform.position = new Vector3(0f, -0.5f, 0f);
            go.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
            spawned.Add(go);
        }

        SpaceshipPart[] BuildTower(bool weld)
        {
            MakeGround();

            var parts = new SpaceshipPart[PartSpec.All.Length];
            var y = 0f;

            for (var i = 0; i < parts.Length; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < parts.Length; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}층 접합 후보가 없다.");
                var link = PartAssembler.Assemble(match);
                Assert.IsNotNull(link, $"{i + 1}층 조립이 실패했다.");

                if (!weld) continue;

                // 강도는 PartAssemblyLink 가 WeldRatio 로 정한다. Assemble 인수로는 바꿀 수 없다
                // (S15P21B202-194 에서 확인한 함정).
                link.ForceWeldComplete();
            }

            return parts;
        }

        (ShipAssembly Assembly, ShipHatch Hatch, LaunchTest Launch, MeteorSpawner Spawner) MakeRig()
        {
            var pad = new GameObject("LaunchPad");
            spawned.Add(pad);

            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);

            var launch = pad.AddComponent<LaunchTest>();

            var spawnerGo = new GameObject("MeteorSpawner");
            spawnerGo.transform.SetParent(pad.transform, false);
            spawned.Add(spawnerGo);
            var spawner = spawnerGo.AddComponent<MeteorSpawner>();

            // 간격을 아주 길게 준다. 실제로 운석이 떨어지면 하중 시험 중에 탑을 때려
            // 무엇이 실패를 만들었는지 알 수 없다. 여기서 보는 것은 멈춤·재개 상태다.
            spawner.Configure(20260827, 1000f, 1000f, 1f);
            spawner.SetArea(spawnerGo.transform, new Vector2(36f, 36f));

            var hatchGo = new GameObject("Hatch") { layer = GameLayers.Interactable };
            hatchGo.transform.SetParent(pad.transform, false);
            hatchGo.AddComponent<SphereCollider>().isTrigger = true;
            spawned.Add(hatchGo);

            var hatch = hatchGo.AddComponent<ShipHatch>();
            hatch.Configure(assembly, launch, spawner);

            return (assembly, hatch, launch, spawner);
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            spawned.Add(player.gameObject);
            return player;
        }

        /// <summary>런타임과 같은 순서로 한 스텝. 링크도 틱해야 끊어진 접합이 정리된다.</summary>
        void Run(ShipAssembly assembly, ShipHatch hatch, LaunchTest launch,
                 PlayerRig player, float seconds)
        {
            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++)
            {
                foreach (var go in spawned)
                {
                    if (go == null) continue;
                    var link = go.GetComponent<PartAssemblyLink>();
                    if (link != null) link.Tick();
                }

                assembly.Evaluate();
                hatch.Tick(Step);
                launch.Tick(Step);
                if (player != null) player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }

        // --- 실제 탑승 ------------------------------------------------------

        /// <summary>
        /// 기획 §22.4 — 플레이어가 타는 곳은 최상단 견인 조종 모듈이다.
        /// </summary>
        [Test]
        public void Boarding_AttachesPlayerToTopModule()
        {
            var rig = MakeRig();
            var parts = BuildTower(weld: true);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);

            Assert.IsTrue(player.IsRiding, "탑승하면 몸이 강체에 붙어야 한다.");
            Assert.AreSame(parts[parts.Length - 1], rig.Hatch.RiddenModule,
                "붙는 곳은 최상단 견인 조종 모듈이다.");
            Assert.IsNull(player.MountedVehicle,
                "우주선은 운반 장치가 아니다. 조종 입력과 조종석 시야를 쓰지 않는다.");
        }

        /// <summary>탑승했으면 우주선과 함께 움직여야 한다. 이것이 200 에 없던 것이다.</summary>
        [Test]
        public void Boarding_PlayerMovesWithTheShip()
        {
            var rig = MakeRig();
            var parts = BuildTower(weld: true);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);

            var module = parts[parts.Length - 1];
            var before = player.Doll.Hips.position - module.transform.position;

            var body = module.GetComponent<Rigidbody>();
            for (var s = 0; s < 25; s++)
            {
                body.linearVelocity = Vector3.up * 4f;
                player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }

            var after = player.Doll.Hips.position - module.transform.position;

            Assert.Greater(module.transform.position.y, 5f, "모듈이 실제로 올라가야 한다.");
            Assert.Less((after - before).magnitude, 0.5f,
                $"플레이어가 모듈과 함께 움직여야 한다. 상대 위치가 {(after - before).magnitude:0.00}m 벌어졌다.");
        }

        /// <summary>기획 §22.3 — 카운트다운을 취소하면 원래 자리로 돌아온다.</summary>
        [Test]
        public void Cancel_PutsThePlayerBackAndLeavesNoJoint()
        {
            var rig = MakeRig();
            BuildTower(weld: true);
            var start = new Vector3(3f, 0f, 0f);
            var player = MakePlayer(start);

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Assert.IsTrue(player.IsRiding, "먼저 타야 한다.");

            rig.Hatch.OnCompleted(player.ClampInteractor);

            Assert.IsFalse(player.IsRiding, "하차하면 붙어 있지 않아야 한다.");
            Assert.IsNull(player.Doll.Hips.GetComponent<FixedJoint>(),
                "조인트가 남으면 다음 탑승이 막히고 몸이 끌려간다.");

            var foot = player.Doll.Hips.position - Vector3.up * RagdollBuilder.HipsY;
            Assert.Less(Vector3.Distance(new Vector3(foot.x, 0f, foot.z), start), 1.0f,
                "탑승 직전 자리로 돌아와야 한다.");
        }

        // --- 발사 실패와 사출 ----------------------------------------------

        /// <summary>
        /// 기획 §22.4 — <b>"해당 모듈이 지면에 닿는 순간"</b> 튀어나온다.
        ///
        /// 접합부가 터진 즉시가 아니다. 모듈이 떨어지는 동안은 타고 있어야 하고,
        /// 그 낙하가 실패의 연출이다.
        /// </summary>
        [Test]
        public void Failure_KeepsThePlayerAboardUntilTheModuleLands()
        {
            var rig = MakeRig();
            BuildTower(weld: false);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig.Assembly, rig.Hatch, rig.Launch, player,
                PhysicsScale.LaunchCountdownSeconds + 0.1f);

            Assert.AreEqual(BoardingState.Launching, rig.Hatch.State, "발사가 시작되어야 한다.");

            // 접합부가 터지는 순간을 찾는다.
            var brokeAt = -1f;
            var elapsed = 0f;
            for (var s = 0; s < 400 && rig.Hatch.State == BoardingState.Launching; s++)
            {
                Run(rig.Assembly, rig.Hatch, rig.Launch, player, Step);
                elapsed += Step;

                if (brokeAt < 0f && rig.Launch.State == LaunchState.Failed)
                {
                    brokeAt = elapsed;
                    Assert.IsTrue(player.IsRiding,
                        "접합부가 터진 순간에는 아직 타고 있어야 한다. 여기서 내리면 낙하 연출이 없다.");
                }
            }

            Assert.Greater(brokeAt, 0f, $"미용접 탑은 하중 시험에서 터져야 한다. 사유: {rig.Launch.FailureReason}");
            Assert.AreEqual(BoardingState.Failing, rig.Hatch.State, "착지하면 수습으로 넘어간다.");
            Assert.IsFalse(player.IsRiding, "착지하면 사출되어야 한다.");
        }

        /// <summary>기획 §22.4 — "잠시 구르지만 기절하지 않고 자동으로 일어난다".</summary>
        [Test]
        public void Ejected_PlayerGetsUpByItselfAndKeepsItsHealth()
        {
            var rig = MakeRig();
            BuildTower(weld: true);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);

            var health = player.Health;
            player.EjectFromShip(Vector3.forward);

            Assert.IsFalse(player.IsRiding, "사출되면 붙어 있지 않아야 한다.");
            Assert.AreEqual(health, player.Health, 0.001f,
                "기절하지 않는다. 체력을 깎으면 운석 맞은 상태에서 발사 실패가 사망이 된다.");

            for (var s = 0; s < 250; s++)   // 5초
            {
                player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }

            Assert.IsFalse(player.Ragdolled, "5초 안에 스스로 일어나야 한다.");
            Assert.IsFalse(player.IsDead, "발사 실패로 죽지 않는다.");
        }

        /// <summary>사출은 바깥으로 밀어낸다. 잔해 속에 남으면 끼인다.</summary>
        [Test]
        public void Ejected_PlayerMovesAwayFromTheWreck()
        {
            MakeGround();
            var player = MakePlayer(new Vector3(0f, 0f, 0f));

            // 콜라이더를 붙이지 않는다. 붙이면 플레이어가 2m 짜리 단단한 상자 <b>안에</b>
            // 들어간 상태로 사출되고, 조인트를 끊는 순간 충돌이 되살아나 결과가 관통
            // 해소에 달린다 — 스위트 전체를 돌릴 때만 실패하는 원인이 이것이었다.
            // 실제 견인 조종 모듈은 속이 비어 있고, 이 검증이 보려는 것은 사출 충격이다.
            var block = new GameObject("Module");
            block.transform.position = new Vector3(0f, 1f, 0f);
            var body = block.AddComponent<Rigidbody>();
            body.isKinematic = true;
            spawned.Add(block);

            Assert.IsTrue(player.TryBoardShip(body, block.transform.position), "먼저 타야 한다.");

            var from = player.Doll.Hips.position;
            player.EjectFromShip(Vector3.forward);

            for (var s = 0; s < 100; s++)   // 2초
            {
                player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }

            var moved = player.Doll.Hips.position - from;
            moved.y = 0f;

            Assert.Greater(moved.magnitude, 1.5f,
                $"잔해 밖으로 나가야 한다. {moved.magnitude:0.00}m 만 움직였다.");
            Assert.Greater(Vector3.Dot(moved.normalized, Vector3.forward), 0.5f,
                "준 방향으로 나가야 한다.");
        }

        // --- 운석 공세 -----------------------------------------------------

        /// <summary>기획 §22.3 발사 중 정지 · §22.4 "일반 운석 공세가 다시 시작된다".</summary>
        [Test]
        public void Meteors_PauseForLaunchAndResumeAfterFailure()
        {
            var rig = MakeRig();
            BuildTower(weld: false);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            Assert.IsFalse(rig.Spawner.IsPaused, "처음에는 돌고 있어야 한다.");

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig.Assembly, rig.Hatch, rig.Launch, player,
                PhysicsScale.LaunchCountdownSeconds + 0.1f);

            Assert.IsTrue(rig.Spawner.IsPaused, "발사 중에는 새 운석을 예고하지 않는다.");

            for (var s = 0; s < 400 && rig.Hatch.State == BoardingState.Launching; s++)
                Run(rig.Assembly, rig.Hatch, rig.Launch, player, Step);

            Assert.AreEqual(BoardingState.Failing, rig.Hatch.State, "실패 수습에 이르러야 한다.");
            Assert.IsFalse(rig.Spawner.IsPaused, "실패 후에는 운석 공세가 다시 돈다.");
        }

        /// <summary>
        /// 기획 §22.4 — <b>"발사 실패는 스테이지 초기화가 아니다"</b>.
        /// 파츠는 무너진 자리에 남아야 절단·재배치·재용접으로 다시 시도할 수 있다.
        /// </summary>
        [Test]
        public void Failure_IsNotAStageReset()
        {
            var rig = MakeRig();
            var parts = BuildTower(weld: false);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            // 운석 공세가 얼마간 진행된 상태에서 발사한다. 실패가 그것을 되돌리면 초기화다.
            for (var s = 0; s < 50; s++) rig.Spawner.Tick(Step);
            var before = rig.Spawner.Elapsed;
            Assert.Greater(before, 0f, "이 검증은 진행값이 0 이 아니어야 의미가 있다.");

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig.Assembly, rig.Hatch, rig.Launch, player,
                PhysicsScale.LaunchCountdownSeconds + 0.1f);

            for (var s = 0; s < 400 && rig.Hatch.State == BoardingState.Launching; s++)
                Run(rig.Assembly, rig.Hatch, rig.Launch, player, Step);

            Assert.AreEqual(BoardingState.Failing, rig.Hatch.State, "실패 수습에 이르러야 한다.");

            for (var i = 0; i < parts.Length; i++)
                Assert.IsNotNull(parts[i], $"{i + 1}층 파츠가 지워지면 안 된다.");

            Assert.GreaterOrEqual(rig.Spawner.Elapsed, before,
                "운석 진행이 되돌아가면 ResetRun 을 부른 것이다 — 그것이 초기화다.");
        }

        /// <summary>실패 수습이 끝나면 다시 시도할 수 있어야 한다.</summary>
        [Test]
        public void Failure_LetsYouTryAgain()
        {
            var rig = MakeRig();
            BuildTower(weld: false);
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig.Assembly, rig.Hatch, rig.Launch, player,
                PhysicsScale.LaunchCountdownSeconds + 0.1f);

            for (var s = 0; s < 400 && rig.Hatch.State == BoardingState.Launching; s++)
                Run(rig.Assembly, rig.Hatch, rig.Launch, player, Step);

            Assert.AreEqual(BoardingState.Failing, rig.Hatch.State, "실패 수습에 이르러야 한다.");

            Run(rig.Assembly, rig.Hatch, rig.Launch, player, 0.1f);

            Assert.AreNotEqual(BoardingState.Failing, rig.Hatch.State,
                "수습에 머물러 있으면 다시 시도할 수 없다.");
            Assert.AreEqual(0, rig.Hatch.Riders, "인원이 남아 있으면 다음 탑승이 어긋난다.");
        }
    }
}
