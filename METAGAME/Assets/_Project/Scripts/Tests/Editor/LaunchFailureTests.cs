using System.Collections.Generic;
using System.Text;
using Metagame.Core;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 발사 하중과 배치 조건을 검증한다. §16.2 변경으로 연결 비드가 있으면
    /// 부분 용접이나 비뚤어진 배치도 힘·토크로 파손되지 않는다.
    /// 비드 없는 소켓의 하중 한계와 발사 전 조건은 별도로 검증한다.
    /// </summary>
    public sealed class LaunchFailureTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();

            _savedMode = UnityEngine.Physics.simulationMode;
            UnityEngine.Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Physics.simulationMode = _savedMode;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            // 씬에 남은 인형과 파츠까지 치운다. 목록만 비우면 뒤에 도는 스위트의 물리
            // 질의에 남는다(S15P21B202-334 에서 그것이 상승 검증을 넘어뜨렸다).
            foreach (var rig in Object.FindObjectsByType<PlayerRig>(FindObjectsSortMode.None))
                if (rig != null)
                    Object.DestroyImmediate(rig.gameObject);

            _spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // --- 잘 만든 탑은 통과한다 (과잉 교정 방어선) --------------------------

        /// <summary>
        /// <b>이 검증이 먼저다.</b> 실패를 만들려고 손잡이를 돌리다 보면 잘 만든 탑도
        /// 부서지는 쪽으로 넘어가기 쉽다. 그러면 §3.3("실패는 대부분 복구 가능")이
        /// 무의미해지고 플레이가 답답해진다.
        /// </summary>
        [Test]
        public void AStraightFullyWeldedTower_Passes()
        {
            var result = Launch(offsetPerFloor: 0f, weldRatio: 1f);

            Assert.AreEqual(LaunchState.Ascending, result.State,
                $"똑바로 완전 용접한 탑은 통과해야 한다. 사유: {result.Reason}");
        }

        // --- 연결 비드가 있으면 하중으로 끊어지지 않는다 -----------------------

        /// <summary>
        /// §16.2: 부분 용접도 비드가 연결되어 있으면 발사 하중을 버틴다.
        /// </summary>
        [Test]
        public void APartiallyWeldedTower_SurvivesTheLoadTest()
        {
            var result = Launch(offsetPerFloor: 0f, weldRatio: 0.15f);

            Assert.AreEqual(LaunchState.Ascending, result.State, result.Reason);
        }

        [Test]
        public void AnUnweldedTower_BreaksDuringTheLoadTest()
        {
            var result = Launch(offsetPerFloor: 0f, weldRatio: 0f);

            AssertBrokeUnderLoad(result, "용접하지 않은 탑이 날아가면 §16.2 가 무의미하다.");
        }

        // --- 비뚤게 쌓으면 실패한다 -------------------------------------------

        /// <summary>
        /// §16.2: 비뚤어진 배치의 토크도 연결 비드를 파손시키지 않는다.
        /// </summary>
        [Test]
        public void ASlightlyOffsetWeldedTower_SurvivesTheLoadTest()
        {
            var result = Launch(offsetPerFloor: 0.4f, weldRatio: 1f);

            Assert.AreEqual(LaunchState.Ascending, result.State, result.Reason);
        }

        /// <summary>
        /// <b>많이 비뚤면 발사 시험까지 가지 않는다.</b> §22.3 이 "우주선이 자기 무게로
        /// 서 있는지 먼저 확인한다" 로, §15.1 이 조립 조건으로 앞단에서 막는다.
        ///
        /// 이것을 따로 잠그는 이유는 <b>플레이어가 보는 장면이 다르기</b> 때문이다 —
        /// 부서지는 것이 아니라 문이 열리지 않는다. 그래서 태블릿이 이유를 알려줘야
        /// 한다(§15.1 "태블릿 순서와 접합 반응으로 판단한다").
        /// </summary>
        [Test]
        public void ABadlyOffsetTower_IsStoppedBeforeLaunch()
        {
            var result = Launch(offsetPerFloor: 0.8f, weldRatio: 1f);

            Assert.AreEqual(LaunchState.Failed, result.State);
            Assert.IsFalse(BrokeUnderLoad(result),
                $"발사 시험이 아니라 앞단에서 막혀야 한다. 사유: {result.Reason}");
        }

        // --- 결정적이어야 한다 -------------------------------------------------

        /// <summary>§22.3 "같은 배치와 용접 상태에서는 항상 같은 결과".</summary>
        [Test]
        public void TheSameTowerGivesTheSameResult()
        {
            var first = Launch(offsetPerFloor: 0.4f, weldRatio: 0.5f);
            TearDown();
            SetUp();
            var second = Launch(offsetPerFloor: 0.4f, weldRatio: 0.5f);

            Assert.AreEqual(first.State, second.State, "무작위 실패를 쓰지 않는다.");
        }

        // --- 경계 측정 (항상 통과 · 로그로 남긴다) -----------------------------

        /// <summary>
        /// 용접률 × 비뚤기 격자를 돌며 <b>실패 경계를 로그로 남긴다.</b>
        ///
        /// 판정하지 않는다 — 손잡이를 어디까지 돌려야 하는지 알기 위한 측정이다.
        /// 결과는 기록 문서로 옮긴다.
        /// </summary>
        [Test]
        public void MeasureTheFailureBoundary()
        {
            var welds = new[] { 0f, 0.15f, 0.3f, 0.5f, 0.75f, 1f };
            var offsets = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f };

            var table = new StringBuilder();
            table.AppendLine("LAUNCH_BOUNDARY | 행=용접률 열=층당오프셋(m) · O=통과 X=실패");
            table.Append("        ");
            foreach (var offset in offsets) table.Append($"{offset,6:0.0}");
            table.AppendLine();

            foreach (var weld in welds)
            {
                table.Append($"{weld,7:0.00} ");
                foreach (var offset in offsets)
                {
                    var result = Launch(offset, weld);
                    table.Append($"{Mark(result),6}");

                    TearDown();
                    SetUp();
                }
                table.AppendLine();
            }

            table.AppendLine("O=통과  T=탑승 불가  N=발사 미시작  J=접합부 파손  S=서 있지 못함");
            Debug.Log(table.ToString());
            Assert.Pass();
        }

        /// <summary>
        /// 실패를 <b>이유별로</b> 가른다.
        ///
        /// 이 구분이 중요하다. 비뚤어진 탑이 "발사 중 파손" 이 아니라 "탑승 자체가 불가"
        /// 로 걸러졌다면, 발사 시험이 판정한 것이 아니라 §15.1 의 조립 조건이 막은 것이다.
        /// 둘 다 정당한 결과지만 <b>플레이어가 보는 장면이 전혀 다르다.</b>
        /// </summary>
        /// <summary>발사 시험 중 접합부가 끊어져 실패했는가.</summary>
        static bool BrokeUnderLoad(Result result) =>
            result.State == LaunchState.Failed
            && result.Reason != null
            && result.Reason.Contains("접합부");

        static void AssertBrokeUnderLoad(Result result, string because)
        {
            Assert.AreEqual(LaunchState.Failed, result.State, because);
            Assert.IsTrue(BrokeUnderLoad(result),
                $"{because} 그런데 발사 시험이 아니라 앞단에서 막혔다. 사유: {result.Reason}");
        }

        static string Mark(Result result)
        {
            if (result.State == LaunchState.Ascending) return "O";

            var reason = result.Reason ?? "";
            if (reason.StartsWith("탑승 조건 미달")) return "T";
            if (reason.StartsWith("발사가 시작")) return "N";
            if (reason.Contains("서 있지 못")) return "S";
            if (reason.Contains("접합부")) return "J";
            return "?";
        }

        // --- 리그 (ShipAscentTests 의 것을 그대로 쓴다) -------------------------

        readonly struct Result
        {
            public Result(LaunchState state, string reason) { State = state; Reason = reason; }
            public LaunchState State { get; }
            public string Reason { get; }
        }

        /// <summary>
        /// 탑을 세우고 발사해 결과가 정해질 때까지 돌린다.
        ///
        /// <paramref name="offsetPerFloor"/> 는 층마다 <b>같은 방향으로</b> 어긋나는
        /// 거리다. 누적되므로 무게중심이 축에서 가장 크게 벗어나는 배치가 된다 —
        /// 지그재그로 쌓으면 서로 상쇄되어 오히려 안전하다.
        /// </summary>
        Result Launch(float offsetPerFloor, float weldRatio)
        {
            var rig = MakeRig();
            BuildTower(offsetPerFloor, weldRatio);
            var player = MakePlayer(new Vector3(6f, 0f, 6f));

            rig.Assembly.Evaluate();
            rig.Hatch.Tick(Step);

            if (!rig.Assembly.IsBoardable)
                return new Result(LaunchState.Failed, "탑승 조건 미달: " + rig.Assembly.Blocker);

            rig.Hatch.OnCompleted(player.ClampInteractor);
            Run(rig, player, PhysicsScale.LaunchCountdownSeconds + 0.1f);

            if (rig.Hatch.State != BoardingState.Launching)
                return new Result(LaunchState.Failed, "발사가 시작되지 않았다");

            for (var s = 0; s < 900 && rig.Launch.State == LaunchState.Ramping; s++)
                Run(rig, player, Step);

            return new Result(rig.Launch.State, rig.Launch.FailureReason);
        }

        void BuildTower(float offsetPerFloor, float weldRatio)
        {
            MakeGround();

            var parts = new SpaceshipPart[PartSpec.All.Length];
            var y = 0f;

            for (var i = 0; i < parts.Length; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec,
                    new Vector3(i * offsetPerFloor, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < parts.Length; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                if (!match.HasValue) continue;      // §15.2 밖이면 소켓이 안 맺어진다

                var link = PartAssembler.Assemble(match);
                if (link == null) continue;

                if (weldRatio >= 1f) link.ForceWeldComplete();
                else link.SetBeadWeldRatio(weldRatio);
            }
        }

        SpaceshipPart MakePart(PartSpec spec, Vector3 center)
        {
            var root = new GameObject(spec.Name) { layer = GameLayers.Part };
            root.transform.position = center;
            _spawned.Add(root);

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
            _spawned.Add(go);
        }

        (ShipAssembly Assembly, ShipHatch Hatch, LaunchTest Launch) MakeRig()
        {
            var pad = new GameObject("LaunchPad");
            _spawned.Add(pad);

            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);

            var launch = pad.AddComponent<LaunchTest>();

            var spawnerGo = new GameObject("MeteorSpawner");
            spawnerGo.transform.SetParent(pad.transform, false);
            _spawned.Add(spawnerGo);

            var spawner = spawnerGo.AddComponent<MeteorSpawner>();
            spawner.Configure(20260908);

            // 착탄 영역을 멀리 보낸다. 운석이 떨어져도 탑에 닿지 못한다
            // (ShipAscentTests 가 같은 이유로 같은 방식을 쓴다).
            spawnerGo.transform.position = new Vector3(500f, 0f, 500f);
            spawner.SetArea(spawnerGo.transform, new Vector2(36f, 36f));

            var hatchGo = new GameObject("Hatch") { layer = GameLayers.Interactable };
            hatchGo.transform.SetParent(pad.transform, false);
            hatchGo.AddComponent<SphereCollider>().isTrigger = true;
            _spawned.Add(hatchGo);

            var hatch = hatchGo.AddComponent<ShipHatch>();
            hatch.Configure(assembly, launch, spawner);

            return (assembly, hatch, launch);
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            return player;
        }

        void Run((ShipAssembly Assembly, ShipHatch Hatch, LaunchTest Launch) rig,
                 PlayerRig player, float seconds)
        {
            for (var s = 0; s < Mathf.RoundToInt(seconds / Step); s++)
            {
                foreach (var go in _spawned)
                {
                    if (go == null) continue;
                    var link = go.GetComponent<PartAssemblyLink>();
                    if (link != null) link.Tick();
                }

                rig.Assembly.Evaluate();
                rig.Hatch.Tick(Step);
                rig.Launch.Tick(Step);
                if (player != null) player.Tick();
                UnityEngine.Physics.Simulate(Step);
            }
        }
    }
}
