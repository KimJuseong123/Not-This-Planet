using System.Collections;
using Metagame.Gameplay;
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
            foreach (var outcome in new[] { "Busy", "Unchanged", "Reordered", "Baked" })
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
