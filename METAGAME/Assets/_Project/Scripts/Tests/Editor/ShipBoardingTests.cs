using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 우주선 완성 판정과 탑승·카운트다운을 검증한다(기획 S15P21B202-107 §22.1 · §22.3).
    ///
    /// S15P21B202-195 가 발사 하중 시험을 만들었지만 게임에서 도달할 수 없었다.
    /// LaunchTest.Begin 을 부르는 곳이 없었기 때문이다. 이 규칙이 그 진입점이다.
    ///
    /// <b>용접은 탑승 조건이 아니다.</b> 기획 §22.1 이 명시적으로 제외하며, §16.2 의
    /// "파츠를 전부 쌓기만 하면 발사를 시도할 수 있으며 발사 하중으로 품질을 검증한다" 와
    /// 짝이다. 여기서 그것을 지킨다 — 조건에 넣으면 195 의 미용접 실패 경로가 죽는다.
    /// </summary>
    public sealed class ShipBoardingTests
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
            UnityEngine.Physics.simulationMode = savedMode;

            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
            PartSocket.All.Clear();
            LowGravityField.All.Clear();
        }

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
            go.AddComponent<BoxCollider>().size = new Vector3(60f, 1f, 60f);
            spawned.Add(go);
        }

        /// <summary>
        /// 탑을 쌓는다. <paramref name="tiers"/> 로 몇 층까지 쌓을지 정한다.
        /// </summary>
        SpaceshipPart[] BuildTower(Vector3 origin, int tiers = 4)
        {
            MakeGround();

            var parts = new SpaceshipPart[tiers];
            var y = 0f;

            for (var i = 0; i < tiers; i++)
            {
                var spec = PartSpec.All[i];
                parts[i] = MakePart(spec, origin + new Vector3(0f, y + spec.Height * 0.5f, 0f));
                y += spec.Height;
            }

            parts[0].TryTransitionTo(PartState.Carried);
            parts[0].TryTransitionTo(PartState.Placed);
            UnityEngine.Physics.SyncTransforms();

            for (var i = 1; i < tiers; i++)
            {
                parts[i].TryTransitionTo(PartState.Carried);

                var match = PartAssembler.FindMatch(parts[i]);
                Assert.IsTrue(match.HasValue, $"{i + 1}층 접합 후보가 없다.");
                Assert.IsNotNull(PartAssembler.Assemble(match), $"{i + 1}층 조립이 실패했다.");
            }

            return parts;
        }

        /// <summary>발사대·완성 판정·하중 시험·문을 한 세트로 만든다.</summary>
        (ShipAssembly Assembly, ShipHatch Hatch, LaunchTest Launch) MakeShipRig(Vector3 padPosition)
        {
            var pad = new GameObject("LaunchPad");
            pad.transform.position = padPosition;
            spawned.Add(pad);

            var assembly = pad.AddComponent<ShipAssembly>();
            assembly.Configure(pad.transform);

            var launch = pad.AddComponent<LaunchTest>();

            var hatchGo = new GameObject("Hatch") { layer = GameLayers.Interactable };
            hatchGo.transform.SetParent(pad.transform, false);
            hatchGo.AddComponent<SphereCollider>().isTrigger = true;
            spawned.Add(hatchGo);

            var hatch = hatchGo.AddComponent<ShipHatch>();
            hatch.Configure(assembly, launch);

            return (assembly, hatch, launch);
        }

        /// <summary>
        /// 한 물리 스텝을 런타임과 같은 순서로 돌린다.
        ///
        /// 접합(<see cref="PartAssemblyLink"/>)도 같이 틱해야 한다. 런타임에서는 그것도
        /// <c>FixedUpdate</c> 를 갖고 있어서, 조인트가 끊어지면 소켓 연결을 정리한다.
        /// 빼먹으면 조인트가 없어져도 소켓 사슬이 남아 탑이 멀쩡해 보인다.
        /// </summary>
        void Run(ShipAssembly assembly, ShipHatch hatch, float seconds)
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
                UnityEngine.Physics.Simulate(Step);
            }
        }

        // --- 완성 판정 -----------------------------------------------------

        [Test]
        public void Assembly_IsBoardable_WhenFourTiersStandOnThePad()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);

            rig.Assembly.Evaluate();

            Assert.IsTrue(rig.Assembly.IsBoardable, $"탑승 가능해야 한다. 막는 이유: {rig.Assembly.Blocker}");
            Assert.AreEqual(PartSpec.All.Length, rig.Assembly.Tower.Count, "탑 전체를 모아야 한다.");
        }

        [Test]
        public void Assembly_IsNotBoardable_WhenTiersAreMissing()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero, tiers: 3);

            rig.Assembly.Evaluate();

            Assert.IsFalse(rig.Assembly.IsBoardable, "세 층만 쌓였으면 탑승할 수 없다.");
            Assert.IsNotEmpty(rig.Assembly.Blocker, "막는 이유를 남겨야 한다.");
        }

        /// <summary>기획 §22.1 — 완성된 탑이 발사 구역 안에 있어야 한다.</summary>
        [Test]
        public void Assembly_IsNotBoardable_WhenTowerIsOffThePad()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(new Vector3(PhysicsScale.LaunchPadRadius + 6f, 0f, 0f));

            rig.Assembly.Evaluate();

            Assert.IsFalse(rig.Assembly.IsBoardable, "발사 구역 밖이면 탑승할 수 없다.");
        }

        /// <summary>
        /// 기획 §22.1 — <b>"용접 완료 여부는 탑승 조건으로 검사하거나 표시하지 않는다"</b>.
        ///
        /// 이 검증이 이 규칙의 핵심이다. 용접을 조건에 넣으면 S15P21B202-195 의
        /// 미용접 하중 실패 경로가 도달 불가가 되어, §16.2 의 "발사 하중으로 품질을
        /// 검증한다" 가 성립하지 않는다.
        /// </summary>
        [Test]
        public void Assembly_IsBoardable_EvenWithoutAnyWelding()
        {
            var rig = MakeShipRig(Vector3.zero);
            var parts = BuildTower(Vector3.zero);

            for (var i = 1; i < parts.Length; i++)
            {
                var link = parts[i].GetComponent<PartAssemblyLink>();
                Assert.IsNotNull(link, $"{i + 1}층 접합이 있어야 한다.");
                Assert.AreEqual(0f, link.WeldRatio, 0.001f, "이 검증은 용접이 0 이어야 의미가 있다.");
            }

            rig.Assembly.Evaluate();

            Assert.IsTrue(rig.Assembly.IsBoardable,
                "용접하지 않아도 탑승할 수 있어야 한다. 그래야 하중 시험이 품질을 판정한다.");
        }

        /// <summary>조건은 깨질 수 있다. 한 번 참이면 계속 참으로 두면 무너진 탑에 탑승한다.</summary>
        [Test]
        public void Assembly_StopsBeingBoardable_WhenTowerTilts()
        {
            var rig = MakeShipRig(Vector3.zero);
            var parts = BuildTower(Vector3.zero);

            rig.Assembly.Evaluate();
            Assert.IsTrue(rig.Assembly.IsBoardable, "먼저 탑승 가능해야 한다.");

            parts[0].transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            UnityEngine.Physics.SyncTransforms();
            rig.Assembly.Evaluate();

            Assert.IsFalse(rig.Assembly.IsBoardable, "탑이 기울면 탑승이 다시 막혀야 한다.");
        }

        // --- 탑승과 카운트다운 ---------------------------------------------

        [Test]
        public void Hatch_OpensWhenShipIsBoardable()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);

            Run(rig.Assembly, rig.Hatch, 0.1f);

            Assert.AreEqual(BoardingState.Open, rig.Hatch.State, "조건이 갖춰지면 문이 열려야 한다.");
        }

        [Test]
        public void Hatch_StaysClosedWhenShipIsNotBoardable()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero, tiers: 2);

            Run(rig.Assembly, rig.Hatch, 0.1f);

            Assert.AreEqual(BoardingState.Closed, rig.Hatch.State, "조건이 없으면 닫혀 있어야 한다.");
        }

        /// <summary>기획 §11.2 — "우주선 입구: E — 탑승". 꾹 누르지 않는다.</summary>
        [Test]
        public void Hatch_IsATapInteraction()
        {
            var rig = MakeShipRig(Vector3.zero);

            Assert.AreEqual(0f, rig.Hatch.HoldSeconds, 0.001f,
                "문은 누르는 즉시 탑승이다. 꾹 누르는 것은 고정쇠와 용접점이다.");
        }

        [Test]
        public void Boarding_StartsCountdownWhenEveryoneIsAboard()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);
            Run(rig.Assembly, rig.Hatch, 0.1f);

            rig.Hatch.OnCompleted(null);

            Assert.AreEqual(BoardingState.CountingDown, rig.Hatch.State, "전원 탑승이면 카운트다운이다.");
            Assert.AreEqual(PhysicsScale.LaunchCountdownSeconds, rig.Hatch.Remaining, 0.001f);
        }

        /// <summary>기획 §22.3 — "카운트다운 중 누구든 E 로 하차하면 취소된다".</summary>
        [Test]
        public void Countdown_CancelsWhenSomeoneLeaves()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);
            Run(rig.Assembly, rig.Hatch, 0.1f);

            rig.Hatch.OnCompleted(null);
            Run(rig.Assembly, rig.Hatch, 1f);

            rig.Hatch.OnCompleted(null);

            Assert.AreEqual(BoardingState.Open, rig.Hatch.State, "하차하면 취소되어야 한다.");
            Assert.AreEqual(0f, rig.Hatch.Remaining, 0.001f);
        }

        [Test]
        public void Countdown_StartsTheLoadTestWhenItEnds()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);
            Run(rig.Assembly, rig.Hatch, 0.1f);

            rig.Hatch.OnCompleted(null);
            Run(rig.Assembly, rig.Hatch, PhysicsScale.LaunchCountdownSeconds + 0.2f);

            Assert.AreEqual(BoardingState.Launching, rig.Hatch.State, "카운트다운이 끝나면 발사다.");
            Assert.AreNotEqual(LaunchState.Idle, rig.Launch.State, "하중 시험이 시작되어야 한다.");
        }

        /// <summary>기획 §22.3 — "문이 잠기고 저중력장을 강제로 끈다".</summary>
        [Test]
        public void Launch_ForcesLowGravityFieldsOff()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);

            var craft = new GameObject("Craft");
            craft.transform.position = new Vector3(0f, 14f, 0f);
            spawned.Add(craft);
            var field = craft.AddComponent<LowGravityField>();
            field.SetActive(true);

            Run(rig.Assembly, rig.Hatch, 0.1f);
            rig.Hatch.OnCompleted(null);
            Run(rig.Assembly, rig.Hatch, PhysicsScale.LaunchCountdownSeconds + 0.2f);

            Assert.IsFalse(field.IsActive,
                "저중력장이 켜져 있으면 무게를 대신 들어 주어 자기 무게로 서 있는지 볼 수 없다.");
        }

        /// <summary>발사가 시작되면 문이 잠긴다 — 더 이상 상호작용할 수 없다.</summary>
        [Test]
        public void Hatch_IsLockedAfterLaunchBegins()
        {
            var rig = MakeShipRig(Vector3.zero);
            BuildTower(Vector3.zero);
            Run(rig.Assembly, rig.Hatch, 0.1f);

            rig.Hatch.OnCompleted(null);
            Run(rig.Assembly, rig.Hatch, PhysicsScale.LaunchCountdownSeconds + 0.2f);

            Assert.IsFalse(rig.Hatch.GetAvailability(null).IsAllowed, "발사 뒤에는 문이 잠겨야 한다.");
        }

        /// <summary>
        /// 카운트다운 중 탑이 무너지면 발사할 우주선이 없다.
        ///
        /// 아래 파츠만 돌려서는 무너뜨릴 수 없다 — 위 파츠와 이어진 조인트가 솔버 한두
        /// 스텝 안에 다시 세워 버린다(이 테스트를 처음 그렇게 짰다가 배웠다).
        /// 접합을 끊는 것이 실제 붕괴에 가깝고 상태도 되돌아가지 않는다.
        /// </summary>
        [Test]
        public void Countdown_AbortsWhenTowerCollapses()
        {
            var rig = MakeShipRig(Vector3.zero);
            var parts = BuildTower(Vector3.zero);
            Run(rig.Assembly, rig.Hatch, 0.1f);

            rig.Hatch.OnCompleted(null);
            Assert.AreEqual(BoardingState.CountingDown, rig.Hatch.State, "먼저 카운트다운이어야 한다.");

            var joint = parts[1].GetComponent<FixedJoint>();
            Assert.IsNotNull(joint, "2층 접합이 있어야 이 검증이 성립한다.");
            Object.DestroyImmediate(joint);

            Run(rig.Assembly, rig.Hatch, 0.2f);

            Assert.AreEqual(BoardingState.Closed, rig.Hatch.State, "탑이 무너지면 카운트다운이 중단된다.");
            Assert.AreEqual(LaunchState.Idle, rig.Launch.State, "하중 시험이 시작되어서는 안 된다.");
        }
    }
}
