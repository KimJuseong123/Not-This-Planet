using System.Collections.Generic;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 발사 성공 뒤 상승과 임시 1장 완료 처리를 검증한다(기획 S15P21B202-107 §22.5).
    ///
    /// S15P21B202-201 로 실패 경로는 끝까지 이어졌지만 성공 경로는 안 됐다.
    /// <c>LaunchTest.Tick</c> 의 첫 줄이 <c>State != Ramping</c> 이면 돌아가므로,
    /// 하중 시험을 통과하는 순간 <b>추력이 끊기고 우주선이 발사대로 다시 떨어졌다.</b>
    ///
    /// <c>LogAssert.ignoreFailingMessages</c> 를 켜는 이유는 이 규칙과 무관하다 —
    /// <c>PlayerRig.Create</c> 경로의 <c>Prim.Decor</c> 가 EditMode 에서
    /// <c>Object.Destroy</c> 를 불러 에러 로그를 남긴다(develop 에 이미 있는 문제).
    /// </summary>
    public sealed class ShipAscentTests
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

        // --- 조립대 -------------------------------------------------------

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

        /// <summary>용접까지 끝낸 탑. 상승을 보려면 하중 시험을 통과해야 한다(§16.2).</summary>
        SpaceshipPart[] BuildWeldedTower()
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

            // 간격을 아주 길게 준다. 실제로 운석이 떨어지면 상승 중에 탑을 때린다.
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

        /// <summary>탑승해서 카운트다운을 끝내고 하중 시험을 통과하는 지점까지 간다.</summary>
        (ShipAssembly Assembly, ShipHatch Hatch, LaunchTest Launch, MeteorSpawner Spawner,
         PlayerRig Player, SpaceshipPart[] Parts) LaunchToAscent()
        {
            var rig = MakeRig();
            var parts = BuildWeldedTower();
            var player = MakePlayer(new Vector3(3f, 0f, 0f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);
            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig.Assembly, rig.Hatch, rig.Launch, player,
                PhysicsScale.LaunchCountdownSeconds + 0.1f);

            Assert.AreEqual(BoardingState.Launching, rig.Hatch.State, "발사가 시작되어야 한다.");

            for (var s = 0; s < 600 && rig.Launch.State == LaunchState.Ramping; s++)
                Run(rig.Assembly, rig.Hatch, rig.Launch, player, Step);

            Assert.AreEqual(LaunchState.Ascending, rig.Launch.State,
                $"용접한 탑은 하중 시험을 통과해야 한다. 사유: {rig.Launch.FailureReason}");

            return (rig.Assembly, rig.Hatch, rig.Launch, rig.Spawner, player, parts);
        }

        // --- 상승 ---------------------------------------------------------

        /// <summary>
        /// <b>이 이슈의 회귀 검증이다.</b> 예전에는 하중 시험을 통과하는 순간
        /// <c>State</c> 가 <c>Success</c> 가 되어 <c>Tick</c> 이 아무것도 하지 않았고,
        /// 추력이 끊겨 우주선이 발사대로 다시 떨어졌다.
        /// </summary>
        [Test]
        public void PassingTheLoadTest_KeepsThrustingInsteadOfDropping()
        {
            var r = LaunchToAscent();
            var atPass = r.Launch.Altitude;

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, 2f);

            Assert.Greater(r.Launch.Altitude, atPass + 3f,
                $"통과 직후에도 계속 올라가야 한다. {atPass:0.00}m → {r.Launch.Altitude:0.00}m");
        }

        /// <summary>기획 §22.5 — "성공 후 약 8~10초 동안 우주선이 상승한다".</summary>
        [Test]
        public void Ascent_LastsThePlannedTime()
        {
            var r = LaunchToAscent();

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds - 1f);
            Assert.AreEqual(LaunchState.Ascending, r.Launch.State, "9초 전에 끝나면 안 된다.");

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, 1.5f);
            Assert.AreEqual(LaunchState.Success, r.Launch.State, "9초가 지나면 완료다.");

            Assert.GreaterOrEqual(PhysicsScale.AscentSeconds, 8f, "기획은 약 8~10초다.");
            Assert.LessOrEqual(PhysicsScale.AscentSeconds, 10f, "기획은 약 8~10초다.");
        }

        /// <summary>
        /// 추력을 그냥 유지하면 안 된다. 추력이 무게의 3.2배이므로 순가속이 중력의
        /// 2.2배로 계속 붙어, 9초면 60m/s 를 넘고 카메라가 따라갈 수 없다.
        /// 기획 §22.5 는 상승 중에 카메라를 돌려 볼 수 있어야 한다고 한다.
        /// </summary>
        [Test]
        public void Ascent_SpeedDoesNotRunAway()
        {
            var r = LaunchToAscent();
            var body = r.Parts[0].GetComponent<Rigidbody>();

            var peak = 0f;
            for (var s = 0; s < Mathf.RoundToInt((PhysicsScale.AscentSeconds - 0.5f) / Step); s++)
            {
                Run(r.Assembly, r.Hatch, r.Launch, r.Player, Step);
                peak = Mathf.Max(peak, body.linearVelocity.y);
            }

            Assert.Less(peak, PhysicsScale.AscentMaxSpeed * 1.6f,
                $"상승 속도가 발산했다. 최고 {peak:0.0}m/s · 상한 {PhysicsScale.AscentMaxSpeed}m/s");
            Assert.Greater(peak, PhysicsScale.AscentMaxSpeed * 0.5f,
                $"너무 느리면 올라가는 것처럼 보이지 않는다. 최고 {peak:0.0}m/s");
        }

        /// <summary>상승이 끝났을 때 고도가 볼 만한 범위 안이어야 한다.</summary>
        [Test]
        public void Ascent_EndsAtAViewableAltitude()
        {
            var r = LaunchToAscent();
            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 0.5f);

            var expected = PhysicsScale.AscentSeconds * PhysicsScale.AscentMaxSpeed;

            Assert.Greater(r.Launch.Altitude, expected * 0.4f,
                $"너무 낮다. {r.Launch.Altitude:0.0}m (기대 약 {expected:0}m)");
            Assert.Less(r.Launch.Altitude, expected * 2f,
                $"너무 높다 — 지형이 사라져 상승이 안 보인다. {r.Launch.Altitude:0.0}m");
        }

        /// <summary>상승 진행도는 0 에서 1 까지 간다. HUD·연출이 붙을 값이다.</summary>
        [Test]
        public void Ascent_ReportsProgress()
        {
            var r = LaunchToAscent();
            Assert.AreEqual(0f, r.Launch.Ascent01, 0.05f, "시작은 0 이다.");

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds * 0.5f);
            Assert.AreEqual(0.5f, r.Launch.Ascent01, 0.1f, "절반이면 0.5 근처다.");

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds);
            Assert.AreEqual(1f, r.Launch.Ascent01, 0.001f, "끝나면 1 이다.");
        }

        // --- 플레이어와 탑 -------------------------------------------------

        /// <summary>S15P21B202-201 의 조인트가 상승 내내 버텨야 한다.</summary>
        [Test]
        public void Ascent_CarriesThePlayerAlong()
        {
            var r = LaunchToAscent();
            var module = r.Parts[r.Parts.Length - 1];
            var before = r.Player.Doll.Hips.position - module.transform.position;

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 0.5f);

            var after = r.Player.Doll.Hips.position - module.transform.position;

            Assert.IsTrue(r.Player.IsRiding, "상승이 끝나도 타고 있어야 한다 — 다음 장으로 간다.");
            Assert.Less((after - before).magnitude, 0.5f,
                $"플레이어가 우주선과 함께 올라가야 한다. 상대 위치가 {(after - before).magnitude:0.00}m 벌어졌다.");
            Assert.Greater(r.Player.Doll.Hips.position.y, 20f, "플레이어도 실제로 올라가야 한다.");
        }

        /// <summary>
        /// 상승 중에는 접합을 다시 판정하지 않는다. <c>SealForLaunch</c> 로 무한 강도이며,
        /// 하중 시험이 이미 품질을 판정했다(기획 §16.2).
        /// </summary>
        [Test]
        public void Ascent_KeepsTheTowerTogether()
        {
            var r = LaunchToAscent();
            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 0.5f);

            Assert.AreEqual(LaunchState.Success, r.Launch.State, "상승이 끝나야 한다.");

            for (var i = 1; i < r.Parts.Length; i++)
            {
                var link = r.Parts[i].GetComponent<PartAssemblyLink>();
                Assert.IsNotNull(link, $"{i + 1}층 접합이 있어야 한다.");
                Assert.IsTrue(link.IsIntact, $"{i + 1}층 접합이 상승 중에 끊어졌다.");
            }
        }

        // --- 완료 처리 -----------------------------------------------------

        /// <summary>
        /// 기획 §22.5 — 대기권 컷신과 2장 로딩이 붙을 자리. 여기서는 이벤트만 남긴다.
        /// </summary>
        [Test]
        public void Completion_RaisesTheEventOnce()
        {
            var r = LaunchToAscent();

            var fired = 0;
            LaunchTest got = null;
            r.Launch.Completed += t => { fired++; got = t; };

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 1f);

            Assert.AreEqual(1, fired, "완료는 한 번만 알려야 한다.");
            Assert.AreSame(r.Launch, got, "누가 끝났는지 넘겨야 한다.");
        }

        /// <summary>
        /// 기획 §22.5 — "별도의 장 사이 로비와 다시 준비하는 절차는 없다".
        /// 완료가 초기화면 파츠와 진행이 사라진다.
        /// </summary>
        [Test]
        public void Completion_IsNotAStageReset()
        {
            var r = LaunchToAscent();
            for (var s = 0; s < 50; s++) r.Spawner.Tick(Step);
            var before = r.Spawner.Elapsed;

            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 1f);

            Assert.AreEqual(LaunchState.Success, r.Launch.State, "완료에 이르러야 한다.");

            for (var i = 0; i < r.Parts.Length; i++)
                Assert.IsNotNull(r.Parts[i], $"{i + 1}층 파츠가 지워지면 안 된다.");

            Assert.GreaterOrEqual(r.Spawner.Elapsed, before,
                "운석 진행이 되돌아가면 ResetRun 을 부른 것이다 — 그것이 초기화다.");
        }

        /// <summary>발사 뒤 문은 계속 잠겨 있다. 1장은 끝났다.</summary>
        [Test]
        public void Completion_LeavesTheHatchLocked()
        {
            var r = LaunchToAscent();
            Run(r.Assembly, r.Hatch, r.Launch, r.Player, PhysicsScale.AscentSeconds + 1f);

            Assert.AreEqual(BoardingState.Launching, r.Hatch.State, "수습으로 넘어가면 안 된다.");
            Assert.IsFalse(r.Hatch.GetAvailability(null).IsAllowed, "문은 잠겨 있어야 한다.");
        }
    }
}
