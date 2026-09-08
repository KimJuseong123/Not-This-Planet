using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// <b>층별 상태가 태블릿이 쓸 수 있는 형태로 나오는지</b> 검증한다
    /// (기획 §11.1, S15P21B202-345).
    ///
    /// <see cref="ShipAssembly.Blocker"/> 로는 태블릿을 만들 수 없다. 그 문장은
    /// <b>첫</b> 문제에서 멈춘 결과 하나뿐이고 어느 층인지도 담고 있지 않으며, §15.1 이
    /// 오류 문구를 금지하므로 화면에 띄울 수도 없다. 그래서
    /// <see cref="ShipAssembly.Floors"/> 를 새로 만들었고, 여기가 그것을 잠근다.
    ///
    /// <b>기준을 새로 만들지 않았다는 것도 함께 본다.</b> 태블릿이 초록인데 문은 열리지
    /// 않는 상태가 생기면 §15.1 의 "태블릿 순서로 판단한다" 가 거짓말이 된다.
    /// </summary>
    public sealed class AssemblyFloorStateTests
    {
        readonly List<GameObject> _spawned = new();
        SimulationMode _savedMode;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PartSocket.All.Clear();

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

            CleanScene();

            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }


        /// <summary>
        /// 씬과 정적 목록에 남는 것을 치운다.
        ///
        /// <b>목록만 비우면 안 된다.</b> S15P21B202-334 에서 씬에 남은 인형이 뒤에 도는
        /// 스위트의 물리 질의에 걸려 상승 검증을 넘어뜨렸다. 여기서는 조립까지 하므로
        /// <c>PartAssemblyLink.All</c> 과 <c>WeldBead.All</c> 도 남는다 — 그것이 남으면
        /// 뒤 스위트의 탑 판정이 이 스위트의 접합을 함께 본다.
        /// </summary>
        void CleanScene()
        {
            DestroyAll<SpaceshipPart>();
            DestroyAll<ShipAssembly>();
            DestroyAll<PlayerRig>();
            DestroyAll<LowGravityField>();

            PartSocket.All.Clear();
            PartAssemblyLink.All.Clear();
            WeldBead.All.Clear();
            PartClamp.All.Clear();
            PartPush.All.Clear();
            LowGravityField.All.Clear();
            PlayerRig.All.Clear();
        }

        static void DestroyAll<T>() where T : Component
        {
            foreach (var found in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (found != null)
                    Object.DestroyImmediate(found.gameObject);
        }

        // --- 빈 조립대 -------------------------------------------------------

        [Test]
        public void AnEmptyPadShowsEveryFloorEmpty()
        {
            var assembly = MakeAssembly();
            assembly.Evaluate();

            Assert.AreEqual(PartSpec.All.Length, assembly.Floors.Count,
                "층 개수가 파츠 개수와 다르면 태블릿의 층이 어긋난다.");

            foreach (var floor in assembly.Floors)
                Assert.AreEqual(ShipAssembly.FloorState.Empty, floor);

            Assert.AreEqual(0, assembly.NextFloor,
                "빈 조립대에서는 1층이 다음 차례여야 한다.");
        }

        // --- 제대로 쌓은 탑 ---------------------------------------------------

        [Test]
        public void AStackedTowerShowsEveryFloorStacked()
        {
            var assembly = MakeAssembly();
            Stack(PartSpec.All.Length, weld: false);
            assembly.Evaluate();

            // 최하단은 아래에 접합할 상대가 없으므로 놓인 순간 접합이 끝난 것이다.
            Assert.AreEqual(ShipAssembly.FloorState.Welded, assembly.Floors[0],
                "1층은 아래에 용접할 상대가 없다. 미완성으로 두면 끝낼 방법이 없다.");

            for (var i = 1; i < assembly.Floors.Count; i++)
                Assert.AreEqual(ShipAssembly.FloorState.Stacked, assembly.Floors[i],
                    $"{i + 1}층이 올라갔지만 아직 용접 전인데 그렇게 보고하지 않는다.");

            Assert.AreEqual(-1, assembly.NextFloor,
                "다 쌓였으면 다음 차례가 없어야 한다.");
        }

        /// <summary>
        /// 사용자 스케치 2·3 의 "제대로 쌓여 있거나 용접이 되면 초록" 중 <b>용접</b> 쪽이다.
        /// 용접까지 끝난 층은 쌓인 층과 구분돼야 한다.
        /// </summary>
        [Test]
        public void AWeldedTowerShowsEveryFloorWelded()
        {
            var assembly = MakeAssembly();
            Stack(PartSpec.All.Length, weld: true);
            assembly.Evaluate();

            for (var i = 0; i < assembly.Floors.Count; i++)
                Assert.AreEqual(ShipAssembly.FloorState.Welded, assembly.Floors[i],
                    $"{i + 1}층이 용접까지 끝났는데 그렇게 보고하지 않는다.");
        }

        /// <summary>
        /// <b>최하단은 용접할 상대가 없다.</b> <c>PartAssemblyLink</c> 는 용접이 끝나면
        /// <b>위쪽</b> 파츠만 <see cref="PartState.Welded"/> 로 올린다. 최하단은 영원히
        /// <see cref="PartState.Placed"/> 에 머무므로, 그것을 "미완성" 으로 두면 태블릿의
        /// 1층이 <b>끝낼 방법 없이</b> 계속 비어 보인다.
        ///
        /// 이 검증이 없으면 다음 사람이 <c>i == 0</c> 예외를 특례로 보고 지운다.
        /// </summary>
        [Test]
        public void TheBottomFloorIsDoneAsSoonAsItIsPlaced()
        {
            var assembly = MakeAssembly();
            var parts = Stack(1, weld: false);
            assembly.Evaluate();

            Assert.AreEqual(PartState.Placed, parts[0].State,
                "전제가 깨졌다 — 최하단 파츠가 Placed 가 아니다.");
            Assert.AreEqual(ShipAssembly.FloorState.Welded, assembly.Floors[0],
                "1층에 남은 할 일이 없는데 미완성으로 보고한다.");
        }

        /// <summary>
        /// <b>용접 상태를 보여주는 것이 §22.1 을 어기지 않는다.</b> §22.1 은 용접을
        /// <b>탑승 조건</b>으로 검사하지 말라고 했다. 용접해도 안 해도 문은 열려야 한다.
        /// </summary>
        [Test]
        public void WeldingIsShownButStillIsNotABoardingCondition()
        {
            var assembly = MakeAssembly();
            Stack(PartSpec.All.Length, weld: false);
            assembly.Evaluate();

            Assert.IsTrue(assembly.IsBoardable,
                $"용접하지 않아도 탑승할 수 있어야 한다(§22.1). 사유: {assembly.Blocker}");
        }

        // --- 덜 쌓인 탑 -------------------------------------------------------

        /// <summary>
        /// <b>조기 탈출이 문제였다.</b> 예전 <c>Evaluate</c> 는 층이 덜 쌓이면 그 자리에서
        /// 돌아나갔다. 그러면 쌓인 층들의 상태를 아무도 모른다.
        /// </summary>
        [Test]
        public void APartialTowerStillReportsTheFloorsThatAreThere()
        {
            var assembly = MakeAssembly();
            Stack(2, weld: false);
            assembly.Evaluate();

            Assert.AreEqual(ShipAssembly.FloorState.Welded, assembly.Floors[0],
                "1층이 쌓였는데 빈 것으로 보고한다.");
            Assert.AreEqual(ShipAssembly.FloorState.Stacked, assembly.Floors[1],
                "2층이 쌓였는데 빈 것으로 보고한다.");
            Assert.AreEqual(ShipAssembly.FloorState.Empty, assembly.Floors[2]);
            Assert.AreEqual(ShipAssembly.FloorState.Empty, assembly.Floors[3]);
        }

        [Test]
        public void ThePartialTowerPointsAtTheNextFloor()
        {
            var assembly = MakeAssembly();
            Stack(2, weld: false);
            assembly.Evaluate();

            Assert.AreEqual(2, assembly.NextFloor,
                "두 층이 쌓였으면 3층이 다음 차례여야 한다.");
        }

        // --- 잘못된 조립 ------------------------------------------------------

        /// <summary>
        /// 사용자 스케치 4 의 "뭔가 잘못되면 빨강" 이다. 순서를 어긴 층이 지목돼야 한다.
        /// </summary>
        [Test]
        public void AWrongPartMarksThatFloorAndOnlyThatFloor()
        {
            var assembly = MakeAssembly();

            // 1층은 제대로, 2층 자리에 3층 파츠를 올린다.
            var first = MakePart(PartSpec.All[0], new Vector3(0f, PartSpec.All[0].Height * 0.5f, 0f));
            first.TryTransitionTo(PartState.Carried);
            first.TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            var wrong = MakePart(PartSpec.All[2],
                new Vector3(0f, PartSpec.All[0].Height + PartSpec.All[2].Height * 0.5f, 0f));
            wrong.TryTransitionTo(PartState.Carried);
            Join(wrong, weld: false);

            assembly.Evaluate();

            Assert.AreNotEqual(ShipAssembly.FloorState.WrongPart, assembly.Floors[0],
                "1층은 올바른데 함께 빨갛게 칠하면 플레이어가 어디를 고쳐야 할지 모른다.");
            Assert.AreEqual(ShipAssembly.FloorState.WrongPart, assembly.Floors[1],
                "2층에 다른 파츠가 올라갔는데 지목하지 않는다.");
        }

        /// <summary>
        /// 기울어진 층도 "잘못됨" 이다. 판정 기준은
        /// <see cref="PhysicsScale.BoardingMaxTilt"/> 로 <see cref="ShipAssembly.IsBoardable"/>
        /// 과 <b>같은 것</b>을 쓴다.
        /// </summary>
        [Test]
        public void ATiltedFloorIsMarkedWithTheSameThresholdAsBoarding()
        {
            var assembly = MakeAssembly();
            var parts = Stack(PartSpec.All.Length, weld: false);

            parts[1].transform.rotation =
                Quaternion.Euler(PhysicsScale.BoardingMaxTilt + 8f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();

            assembly.Evaluate();

            Assert.AreEqual(ShipAssembly.FloorState.Tilted, assembly.Floors[1],
                "2층이 탑승 한계를 넘게 기울었는데 지목하지 않는다.");
            Assert.IsFalse(assembly.IsBoardable,
                "태블릿이 빨간데 문이 열리면 §15.1 의 판단 근거가 거짓이 된다.");
        }

        /// <summary>
        /// <b>기준이 하나여야 한다.</b> 한계 안쪽으로 살짝 기운 탑은 태블릿도 초록,
        /// 문도 열려야 한다.
        /// </summary>
        [Test]
        public void ASlightTiltInsideTheLimitStaysGreenAndBoardable()
        {
            var assembly = MakeAssembly();
            var parts = Stack(PartSpec.All.Length, weld: false);

            parts[1].transform.rotation =
                Quaternion.Euler(PhysicsScale.BoardingMaxTilt - 3f, 0f, 0f);
            UnityEngine.Physics.SyncTransforms();

            assembly.Evaluate();

            Assert.AreEqual(ShipAssembly.FloorState.Stacked, assembly.Floors[1],
                "한계 안쪽으로 기운 것을 빨갛게 칠하면 플레이어가 멀쩡한 탑을 다시 쌓는다.");
            Assert.IsTrue(assembly.IsBoardable, $"사유: {assembly.Blocker}");
        }

        /// <summary>
        /// §11.1 "파츠가 탑에서 떨어져 분리되면 청록색에서 발견 상태로 되돌아간다".
        /// </summary>
        [Test]
        public void APartThatFallsOffGoesBackToEmpty()
        {
            var assembly = MakeAssembly();
            var parts = Stack(PartSpec.All.Length, weld: false);

            assembly.Evaluate();
            Assert.AreEqual(ShipAssembly.FloorState.Stacked, assembly.Floors[3],
                "먼저 4층이 올라가 있어야 한다.");

            Object.DestroyImmediate(parts[3].gameObject);
            PartSocket.All.RemoveAll(socket => socket == null);
            assembly.Evaluate();

            Assert.AreEqual(ShipAssembly.FloorState.Empty, assembly.Floors[3],
                "떨어진 파츠가 계속 올라가 있는 것으로 보인다.");
            Assert.AreEqual(3, assembly.NextFloor,
                "떨어진 자리가 다음 차례여야 한다.");
        }

        // --- 하니스 -----------------------------------------------------------

        ShipAssembly MakeAssembly()
        {
            MakeGround();

            var pad = new GameObject("LaunchPad");
            _spawned.Add(pad);

            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);
            return assembly;
        }

        /// <summary>아래에서부터 <paramref name="floors"/> 층까지 순서대로 쌓는다.</summary>
        SpaceshipPart[] Stack(int floors, bool weld)
        {
            var parts = new SpaceshipPart[floors];
            var y = 0f;

            for (var i = 0; i < floors; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < floors; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);
                Join(parts[i], weld);
            }

            return parts;
        }

        static void Join(SpaceshipPart part, bool weld)
        {
            var match = PartAssembler.FindMatch(part);
            if (!match.HasValue) return;

            var link = PartAssembler.Assemble(match);
            if (link == null) return;

            if (weld) link.ForceWeldComplete();
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
                socket.transform.localRotation =
                    Quaternion.LookRotation(direction, Vector3.forward);
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
    }
}
