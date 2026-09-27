using System.Collections;
using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class AiNavigationTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>
        /// 굽는 고리가 <b>파츠를 스스로 찾지 않는다</b> (S15P21B202-704).
        ///
        /// <para><b>값으로는 볼 수 없다.</b> 어느 쪽이든 같은 길찾기를 내놓는다 — 다른 것은
        /// 굽을 때마다 드는 비용뿐이다. 소스가 실측 665개라 한 번이 그대로 665번이 된다.</para>
        ///
        /// <para><b>두 번 틀린 자리다.</b> <c>-690</c> 에서 굽는 쪽과 쓰는 쪽의 판단을
        /// <c>UsedForNavigation</c> 한 벌로 합치면서 계층 탐색이 소스마다 두 번 돌게 됐다
        /// (판단 안에서 한 번, <c>area = 1</c> 판정에서 또 한 번). 처음 고칠 때는 부르는 쪽이
        /// <b>미리 찾아 넘기게</b> 했는데, 그러면 싼 필터에서 걸러질 콜라이더까지 탐색하게
        /// 되어 <b>거르는 쪽이 더 느려진다</b> — <c>!595</c> AI 리뷰가 짚었다.</para>
        ///
        /// <para>그래서 <b>굽는 고리 안에 탐색이 하나도 없어야 한다.</b> 찾는 자리는
        /// <c>UsedForNavigation</c> 하나뿐이고, 값은 <c>out</c> 으로 나온다. 그 형태가 아니면
        /// 둘 중 한 가지 실수로 되돌아간 것이다.</para>
        /// </summary>
        [Test]
        public void TheBakeLoopNeverLooksUpThePartItself()
        {
            var scope = TestSource.Method(
                TestSource.Read("Runtime/Network/AiNavigationWorld.cs"), "public RebuildOutcome Rebuild(");

            Assert.That(scope, Does.Contain("UsedForNavigation(collider, out var part)"),
                "판단하는 쪽이 찾은 파츠를 돌려받지 않는다 — 같은 계층 탐색이 두 번 돌거나, "
                + "부르는 쪽이 미리 찾아 넘기게 된다");
            Assert.That(scope, Does.Not.Contain("GetComponentInParent<SpaceshipPart>"),
                "굽는 고리가 파츠를 스스로 찾는다. 소스 665개면 굽을 때마다 665번이 더 들고, "
                + "그 자리가 필터보다 앞이면 걸러질 콜라이더까지 탐색하게 된다");
        }

        /// <summary>
        /// 파츠 찾기는 <b>싼 필터를 모두 지난 뒤</b>에만 한다 (S15P21B202-704).
        ///
        /// <para>레이어·트리거·사람 몸에서 걸러질 콜라이더는 계층 탐색을 <b>0번</b> 해야 한다.
        /// 위의 검사가 굽는 고리를 보고, 이 검사가 <b>찾는 자리 안의 순서</b>를 본다 — 탐색을
        /// 함수 안으로 옮기기만 하고 순서를 지키지 않으면 비용은 그대로다.</para>
        /// </summary>
        [Test]
        public void ThePartLookupComesAfterTheCheapFilters()
        {
            var scope = TestSource.Method(TestSource.Read("Runtime/Network/AiNavigationWorld.cs"),
                "public static bool UsedForNavigation(Collider collider, out SpaceshipPart part)");

            int trigger = scope.IndexOf("isTrigger", System.StringComparison.Ordinal);
            int part = scope.IndexOf("GetComponentInParent<SpaceshipPart>", System.StringComparison.Ordinal);
            Assert.That(trigger, Is.GreaterThanOrEqualTo(0), "싼 필터가 사라졌다");
            Assert.That(part, Is.GreaterThan(trigger),
                "파츠를 싼 필터보다 먼저 찾는다 — 걸러질 콜라이더까지 계층 탐색을 하게 되어 "
                + "굽는 고리를 위해 고친 것이 베이커를 더 느리게 만든다");
        }

        /// <summary>
        /// 굽기 결과마다 <b>이름 있는 칸</b>이 있다 (S15P21B202-737).
        ///
        /// <para>전에는 <c>else unchanged++</c> 한 줄이 "안 구웠다" 를 전부 받았다. 순서만 달라 건너뛴 것이
        /// 새로 생기면 그것이 조용히 "그대로라 건너뜀" 에 섞여, 게이트가 일한 것처럼 보인다.</para>
        /// </summary>
        [Test]
        public void EveryBakeOutcomeHasItsOwnCounter()
        {
            var update = TestSource.Method(TestSource.Read("Runtime/Network/AiNavigationWorld.cs"), "void Update()");
            foreach (var outcome in new[] { "Busy", "Unchanged", "Reordered", "Deferred", "Baked", "Jitter", "Settling", "Spacing" })
                Assert.That(update, Does.Contain("case RebuildOutcome." + outcome + ":"), outcome + " 을 세는 칸이 없다");
            Assert.That(update, Does.Not.Contain("else unchanged++"), "안 구운 것을 한 칸에 몰아 센다");
        }

        /// <summary>
        /// 스냅숏은 <b>굽기로 정한 뒤에만</b> 바꾼다 (S15P21B202-737). 건너뛰는 길에서 바꾸면 조금씩 움직이는
        /// 것이 매번 "지난번과 같다" 로 이어져 한 번도 안 굽는다 — 낡은 길을 조용히 쓴다.
        /// </summary>
        [Test]
        public void TheSnapshotIsTakenOnlyWhenBaking()
        {
            var rebuild = TestSource.Method(TestSource.Read("Runtime/Network/AiNavigationWorld.cs"), "public RebuildOutcome Rebuild(");
            int skip = rebuild.IndexOf("return RebuildOutcome.Reordered;", System.StringComparison.Ordinal);
            int copy = rebuild.IndexOf("bakedKeys.AddRange(keys);", System.StringComparison.Ordinal);
            Assert.That(skip, Is.GreaterThanOrEqualTo(0), "순서만 다른 것을 건너뛰는 길이 없다");
            Assert.That(copy, Is.GreaterThan(skip), "건너뛰기 전에 스냅숏을 바꾼다");
        }

        /// <summary>
        /// 조종 중인 파츠를 빼는 자리는 <b>굽는 고리</b>이지 공용 판단이 아니다 (S15P21B202-737).
        ///
        /// <para><c>UsedForNavigation</c> 은 에디터 베이커도 쓴다. 거기서 빼면 그 파츠의 읽기용 사본이 안 구워져,
        /// 런타임이 실제 바닥을 대체 박스(장애물)로 바꾼다 — <c>-690</c> 에서 한 번 겪은 일이다.</para>
        /// </summary>
        [Test]
        public void PartsUnderControlLeaveOnlyTheRuntimeBake()
        {
            string source = TestSource.Read("Runtime/Network/AiNavigationWorld.cs");
            var shared = TestSource.Method(source, "public static bool UsedForNavigation(Collider collider, out SpaceshipPart part)");
            Assert.That(shared, Does.Contain("isTrigger"), "공용 판단을 못 찾았다 — 이 검사가 헛돈다");
            foreach (var banned in new[] { "UnderControl", "IsOccupied", "PartState.Carried" })
                Assert.That(shared, Does.Not.Contain(banned), "조종 판정이 공용 판단에 들어갔다 — 베이커가 바닥을 덜 굽는다");
            var rebuild = TestSource.Method(source, "public RebuildOutcome Rebuild(");
            Assert.That(rebuild, Does.Contain("if (part != null && UnderControl(part))"), "굽는 고리가 조종 중인 파츠를 빼지 않는다");
        }

        /// <summary>
        /// 수요는 <b>세 곳</b>에서 온다 — 일하는 동료, 말하고 있는 사람, 계획을 세우는 중 (S15P21B202-737).
        /// 하나라도 빠지면 그 길로 오는 명령이 묵은 길로 첫 길을 묻는다. 서버 상태라 값으로 따라가기 어려워
        /// 글자로 못박는다. 묵은 길을 물으면 세는 줄도 함께 — 그것이 이 판단이 틀렸는지 알려 주는 유일한 값이다.
        /// </summary>
        [Test]
        public void DemandReadsWorkHearingAndPlanning()
        {
            string source = TestSource.Read("Runtime/Network/AiNavigationWorld.cs");
            var demand = TestSource.Method(source, "static AiNavigationSources.Demand CurrentDemand()");
            foreach (var needle in new[] { ".AiBusy", ".AiHearingOpen", "AiLocalRuntime.Current" })
                Assert.That(demand, Does.Contain(needle), needle + " 를 안 본다 — 그 길로 오는 명령이 묵은 길을 묻는다");
            var query = TestSource.Method(source, "public bool CalculatePath(");
            Assert.That(query, Does.Contain("idleStaleQueries++;"), "묵은 길 물음을 안 센다 — 미루기가 틀려도 모른다");
        }

        static GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = center; go.transform.localScale = size;
            return go;
        }

        static IEnumerator Bake(AiNavigationWorld world)
        {
            Physics.SyncTransforms();
            world.Rebuild(new Bounds(Vector3.zero, Vector3.one * 100));
            float until = Time.realtimeSinceStartup + 20f;
            while ((!world.Ready || world.IsBuilding) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.Ready, Is.True, "Navigation build timed out");
            Assert.That(world.IsBuilding, Is.False);
        }

        static readonly Bounds Area = new Bounds(Vector3.zero, Vector3.one * 100);

        static IEnumerator Built(AiNavigationWorld world)
        {
            float until = Time.realtimeSinceStartup + 20f;
            while (world.IsBuilding && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.IsBuilding, Is.False, "Navigation build timed out");
        }

        /// <summary>
        /// <b>조종 중인 파츠는 굽기에서 빠졌다가, 내려놓이면 한 번 돌아온다</b> (S15P21B202-737).
        ///
        /// <para>2026-09-23 로그에서 Part_4 를 타는 동안 30초에 15/15번 구웠다. 타 있는 동안 파츠가 움직여도
        /// 굽지 않아야 하고, 내리면 <b>그 자리에 장애물로 돌아와야</b> 한다 — 돌아오지 않으면 AI 가 파츠를
        /// 뚫고 가는 길을 받는다. 견인 빔(Carried → Placed)도 같다.</para>
        ///
        /// <para>각 단계에서 파츠가 <b>거르기는 통과하는</b> 상태인지 먼저 확인한다. 거르기에서 이미 빠지는
        /// 상태라면 이 검사는 제외 규칙이 없어도 통과한다.</para>
        /// </summary>
        [Category("Heavy")]   // 플레이 모드와 실제 굽기를 쓴다
        [UnityTest]
        public IEnumerator RiddenOrCarriedPartLeavesTheBakeAndReturnsOnce()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box("floor", new Vector3(0, -.5f, 0), new Vector3(30, 1, 30));
            var world = new GameObject("navigation").AddComponent<AiNavigationWorld>();

            // BeamOccupiedCraftTests.BoardedCraft 와 같은 모양 — 고정못을 푼 파츠 하나.
            var go = new GameObject("Part_1") { layer = GameLayers.Part };
            go.transform.position = new Vector3(0, .5f, 0);
            var body = go.AddComponent<Rigidbody>();
            var collider = go.AddComponent<BoxCollider>();
            collider.size = Vector3.one;
            var part = go.AddComponent<SpaceshipPart>();
            go.AddComponent<PartPush>();
            var clamps = new List<PartClamp>(4);
            for (int i = 0; i < 4; i++)
            {
                var clampObject = new GameObject($"Clamp_{i}") { layer = GameLayers.Interactable };
                clampObject.transform.SetParent(go.transform, false);
                clampObject.AddComponent<SphereCollider>().isTrigger = true;
                clamps.Add(clampObject.AddComponent<PartClamp>());
            }
            part.RefreshClamps();
            var activator = go.AddComponent<PartTractorActivator>();
            activator.Initialize();
            for (int i = 0; i < clamps.Count; i++)
            {
                clamps[i].transform.position = new Vector3(50f + i * 2f, 0f, 0f);
                clamps[i].Release();
            }
            var pilot = PlayerRig.Create("Pilot", go.transform.position + Vector3.right * 2f,
                Quaternion.identity, Color.white, Color.gray, false);
            yield return new WaitForSeconds(1f);   // 풀린 파츠가 바닥에 자리 잡을 때까지

            yield return Bake(world);
            int withPart = world.SourceCount;

            // ── 탄다 ──
            var vehicle = activator.Vehicle;
            Assert.That(vehicle.TryBoard(pilot), Is.True, "검증 준비: 탑승이 돼야 한다");
            Physics.SyncTransforms();
            Assert.That(AiNavigationWorld.UsedForNavigation(collider), Is.True,
                "탄 파츠가 이미 거르기에서 빠진다 — 이 검사가 제외 규칙 없이도 통과한다");
            Assert.That(world.Rebuild(Area), Is.EqualTo(AiNavigationWorld.RebuildOutcome.Baked),
                "탔는데 굽지 않았다 — 옮겨 갈 파츠의 구멍이 그 자리에 남는다");
            Assert.That(world.SourceCount, Is.EqualTo(withPart - 1), "탄 파츠가 굽기에 남아 있다");
            yield return Built(world);
            for (int i = 0; i < 3; i++)
            {
                body.position += Vector3.right * .2f;
                Physics.SyncTransforms();
                Assert.That(world.Rebuild(Area), Is.EqualTo(AiNavigationWorld.RebuildOutcome.Unchanged),
                    "탄 파츠가 움직일 때마다 굽는다 — 2026-09-23 의 30초 15/15번이 그대로다");
            }

            // ── 내린다 ──
            vehicle.ReleaseRiderForRespawn();
            Physics.SyncTransforms();
            Assert.That(world.Rebuild(Area), Is.EqualTo(AiNavigationWorld.RebuildOutcome.Baked),
                "내렸는데 굽지 않는다 — 내려놓인 파츠가 길찾기에 없어 AI 가 뚫고 간다");
            Assert.That(world.SourceCount, Is.EqualTo(withPart), "내린 파츠가 장애물로 돌아오지 않았다");
            yield return Built(world);

            // ── 견인 빔에 들렸다가 내려놓인다 ──
            Assert.That(part.TryTransitionTo(PartState.Carried), Is.True, "검증 준비: 들 수 있어야 한다");
            Physics.SyncTransforms();
            Assert.That(AiNavigationWorld.UsedForNavigation(collider), Is.True,
                "들린 파츠가 이미 거르기에서 빠진다 — 이 검사가 제외 규칙 없이도 통과한다");
            Assert.That(world.Rebuild(Area), Is.EqualTo(AiNavigationWorld.RebuildOutcome.Baked));
            Assert.That(world.SourceCount, Is.EqualTo(withPart - 1), "들린 파츠가 굽기에 남아 있다");
            yield return Built(world);
            Assert.That(part.TryTransitionTo(PartState.Placed), Is.True, "검증 준비: 내려놓을 수 있어야 한다");
            Physics.SyncTransforms();
            Assert.That(world.Rebuild(Area), Is.EqualTo(AiNavigationWorld.RebuildOutcome.Baked));
            Assert.That(world.SourceCount, Is.EqualTo(withPart), "내려놓인 파츠가 장애물로 돌아오지 않았다");
            yield return Built(world);
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator UnchangedGeometrySkipsBuildAndRefreshKeepsLastValidNavigation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box("floor", new Vector3(0, -.5f, 0), new Vector3(30, 1, 30));
            var world = new GameObject("navigation").AddComponent<AiNavigationWorld>();
            yield return Bake(world);
            int builds = world.BuildCount;
            yield return Bake(world);
            Assert.That(world.BuildCount, Is.EqualTo(builds));
            var wall = Box("wall", new Vector3(0, 2, 0), new Vector3(1, 4, 10));
            Physics.SyncTransforms();
            world.Rebuild(new Bounds(Vector3.zero, Vector3.one * 100));
            Assert.That(world.Ready, Is.True, "A refresh must not disable an existing valid mesh");
            Assert.That(world.BuildCount, Is.EqualTo(builds + 1));
            float until = Time.realtimeSinceStartup + 20;
            while (world.IsBuilding && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.IsBuilding, Is.False);
            var path = new NavMeshPath();
            Assert.That(world.CalculatePath(new Vector3(-4, 0, 0), new Vector3(4, 0, 0), path), Is.True);
            Assert.That(path.corners.Length, Is.GreaterThan(2));
            wall.transform.position += Vector3.forward * 12;
            yield return Bake(world);
            Assert.That(world.BuildCount, Is.EqualTo(builds + 2));
            world.enabled = false;
            Assert.That(world.Ready, Is.False);
            world.enabled = true;
            yield return Bake(world);
            Assert.That(world.Ready, Is.True);
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator RoutesAroundWallUpdatesAfterRemovalAndRejectsDisconnectedPlatform()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box("floor", new Vector3(0,-.5f,0), new Vector3(30,1,30));
            var wall = Box("wall", new Vector3(0,2,0), new Vector3(1,4,10));
            var world = new GameObject("navigation").AddComponent<AiNavigationWorld>();
            yield return Bake(world);
            var path = new NavMeshPath();
            Assert.That(world.CalculatePath(new Vector3(-4,0,0), new Vector3(4,0,0), path), Is.True);
            Assert.That(path.corners.Length, Is.GreaterThan(2), "Must use wall corners, not a straight line");
            var route = new AiNavigationRoute();
            Assert.That(route.Steer(world, new Vector3(-1.2f,0,0), new Vector3(1.2f,0,0), 2.3f, out _), Is.True);
            Assert.That(route.Arrived, Is.False, "Close through a wall is not arrival");
            Object.Destroy(wall); yield return null;
            yield return Bake(world);
            Assert.That(world.CalculatePath(new Vector3(-4,0,0), new Vector3(4,0,0), path), Is.True);
            Assert.That(path.corners.Length, Is.EqualTo(2));
            Box("isolated platform", new Vector3(5,2,5), new Vector3(4,1,4));
            yield return Bake(world);
            Assert.That(world.CalculatePath(Vector3.zero, new Vector3(5,2.5f,5), path), Is.False);
            world.enabled = false;
            Assert.That(world.Ready, Is.False);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator PhysicalRigFollowsAroundWall()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box("floor", new Vector3(0,-.5f,0), new Vector3(30,1,30));
            Box("wall", new Vector3(0,2,0), new Vector3(1,4,6));
            var session = new GameObject("session").AddComponent<GameSession>();
            var target = PlayerRig.Create("target", new Vector3(4,.1f,0), Quaternion.identity, Color.red, Color.white, false);
            session.Begin(target, null, Vector3.zero); session.ScriptedInput = true;
            var self = PlayerRig.Create("follower", new Vector3(-4,.1f,0), Quaternion.identity, Color.blue, Color.white, false);
            var world = session.gameObject.AddComponent<AiNavigationWorld>();
            yield return Bake(world);
            var controller = new AiFollowController();
            controller.Apply("follow", target);
            float until = Time.time + 25f;
            float furthestZ = 0;
            while (Time.time < until && Vector3.Distance(self.FootPosition, target.FootPosition) > 2.8f)
            {
                self.SetInput(controller.Tick(self, true));
                furthestZ = Mathf.Max(furthestZ, Mathf.Abs(self.FootPosition.z));
                yield return new WaitForFixedUpdate();
            }
            self.SetInput(default);
            Assert.That(furthestZ, Is.GreaterThan(3f), "Must physically walk around wall");
            Assert.That(Vector3.Distance(self.FootPosition, target.FootPosition), Is.LessThan(2.8f), controller.Status + " " + self.FootPosition);
            self.Teleport(target.FootPosition + Vector3.right, Quaternion.identity);
            controller.Tick(self, true);
            Assert.That(controller.Arrived, Is.True);
            typeof(AiFollowController).GetProperty(nameof(AiFollowController.Status))
                .GetSetMethod(true).Invoke(controller, new object[] { "Waiting near requester" });
            Assert.That(controller.WantsMovement, Is.False, "Display text must not decide arrival");
            controller.SuspendMovement();
            Assert.That(controller.Arrived, Is.False, "Recovery invalidates the previous arrival");
            Assert.That(controller.WantsMovement, Is.True);
            controller.Apply("cancel", target);
            Assert.That(controller.Arrived, Is.False, "Cancellation is not successful arrival");
            Assert.That(controller.Tick(self, true).Move, Is.EqualTo(Vector2.zero));
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator RampConnectsRaisedPlatform()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Box("floor", new Vector3(0,-.5f,0), new Vector3(30,1,30));
            Box("platform", new Vector3(0,.75f,5), new Vector3(6,1.5f,4));
            var ramp = Box("ramp", new Vector3(0,.65f,.7f), new Vector3(3,.3f,5));
            ramp.transform.rotation = Quaternion.Euler(-17,0,0);
            var world = new GameObject("navigation").AddComponent<AiNavigationWorld>();
            yield return Bake(world);
            Assert.That(world.CalculatePath(new Vector3(0,0,-4), new Vector3(0,1.5f,5), new NavMeshPath()), Is.True);
            var session = world.gameObject.AddComponent<GameSession>();
            var target = PlayerRig.Create("target", new Vector3(0,1.6f,5), Quaternion.identity, Color.red, Color.white, false);
            session.Begin(target, null, Vector3.zero); session.ScriptedInput = true;
            var self = PlayerRig.Create("follower", new Vector3(0,.1f,-4), Quaternion.identity, Color.blue, Color.white, false);
            var controller = new AiFollowController(); controller.Apply("follow", target);
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until && self.FootPosition.y < 1.2f)
            {
                self.SetInput(controller.Tick(self, true));
                yield return new WaitForFixedUpdate();
            }
            self.SetInput(default);
            Assert.That(self.FootPosition.y, Is.GreaterThan(1.2f), controller.Status + " " + self.FootPosition);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator AuthoredStageBuildsNavigationFromActualColliders()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;
            var session = GameSession.Instance;
            Assert.That(session, Is.Not.Null);
            var world = session.gameObject.AddComponent<AiNavigationWorld>();
            Physics.SyncTransforms();
            world.Rebuild(new Bounds(Vector3.zero, new Vector3(512,128,512)));
            float until = Time.realtimeSinceStartup + 60f;
            while (!world.Ready && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.Ready, Is.True);
            var path = new NavMeshPath();
            int reachable = 0, sampled = 0;
            foreach (var clamp in Object.FindObjectsByType<Metagame.Gameplay.Parts.PartClamp>(FindObjectsSortMode.None))
            {
                for (int i = 0; i < 8; i++)
                {
                    var point = clamp.transform.position + Quaternion.Euler(0,i*45,0) * Vector3.forward * 1.2f;
                    if (!Physics.Raycast(point + Vector3.up * 2, Vector3.down, out var hit, 5f, GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore)) continue;
                    sampled++;
                    if (world.CalculatePath(session.SpawnFoot, hit.point, path)) reachable++;
                }
            }
            Debug.Log($"[AI Navigation Audit] sources={world.SourceCount}, approximateMeshes={world.ApproximateMeshCount}, reachablePinApproaches={reachable}/{sampled}, spawn={session.SpawnFoot}");
            Assert.That(reachable, Is.GreaterThan(0), "No route from authored spawn to any pin approach");

            // **대체 박스가 하나라도 생기면 실패다** (S15P21B202-690).
            //
            // 읽기용 사본을 못 찾으면 런타임은 그 메시를 **메시 경계 상자**로 바꾸고
            // `area = 1` 을 준다 — 실제 바닥이 **장애물로** 바뀐다. 길은 여전히 어딘가로
            // 나므로 위의 `reachable > 0` 은 통과하고, 지금까지 이 값은 **로그로만** 찍혀
            // 아무도 읽지 않았다.
            //
            // 베이커가 런타임과 같은 필터를 쓰게 만든 김에(`AiNavigationGeometryBaker`)
            // 이것을 단언으로 올린다. 베이커가 필요한 메시를 빠뜨리면 여기서 걸리고,
            // 빠뜨리지 않으면서 크기가 줄었다는 것을 이 검사가 증명한다.
            Assert.That(world.ApproximateMeshCount, Is.Zero,
                "읽기용 사본을 못 찾아 대체 박스로 때운 메시가 있다 — 실제 바닥이 장애물이 "
                + "된다. `Tools/Not This Planet/AI/Rebuild Navigation Geometry` 를 다시 돌릴 것");
            yield return new ExitPlayMode();
        }
    }
}
