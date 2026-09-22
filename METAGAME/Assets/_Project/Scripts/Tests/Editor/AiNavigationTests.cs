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
            while (!world.Ready && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.Ready, Is.True, "Navigation build timed out");
        }

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
            controller.Apply("cancel", target);
            Assert.That(controller.Tick(self, true).Move, Is.EqualTo(Vector2.zero));
            yield return new ExitPlayMode();
        }

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
            yield return new ExitPlayMode();
        }
    }
}
