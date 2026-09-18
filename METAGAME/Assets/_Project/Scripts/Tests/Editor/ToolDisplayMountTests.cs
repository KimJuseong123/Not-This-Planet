using System.Collections;
using System.IO;
using System.Linq;
using Metagame.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public class ToolDisplayMountTests
    {
        [TestCase("MeteorBat_Stage1")]
        [TestCase("WeldingPlasmaGun_Stage1")]
        public void AuthoredVisibleModelOverlapsItsPhysicalBody(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
            var tool = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<ToolBase>(true))
                .Single(t => t.name == name);
            Physics.SyncTransforms();
            var colliders = tool.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && !c.isTrigger).ToArray();
            var renderers = tool.GetComponentsInChildren<MeshRenderer>()
                .Where(r => r.enabled).ToArray();
            Assert.IsNotEmpty(colliders);
            Assert.IsNotEmpty(renderers);
            foreach (var renderer in renderers)
            {
                float distance = colliders.Min(c =>
                    Vector3.Distance(c.ClosestPoint(renderer.bounds.center), renderer.bounds.center));
                Assert.Less(distance, .25f,
                    $"{name}/{renderer.name}: visible geometry must stay near its collision body.");
            }
        }

        [TestCase("MeteorBat_Stage1")]
        [TestCase("WeldingPlasmaGun_Stage1")]
        public void AuthoredToolIsVisibleInFirstPersonAndRestoresDroppedPose(string name)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
            var tool = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<ToolBase>(true))
                .Single(t => t.name == name);
            var transforms = tool.GetComponentsInChildren<Transform>();
            var positions = transforms.Select(t => t.localPosition).ToArray();
            var rotations = transforms.Select(t => t.localRotation).ToArray();
            var scales = transforms.Select(t => t.localScale).ToArray();
            var player = PlayerRig.Create("Authored tool view test", Vector3.zero,
                Quaternion.identity, Color.white, Color.gray, false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            camera.Cam.aspect = 16f / 9f;
            camera.transform.SetPositionAndRotation(new Vector3(100, 20, 100), Quaternion.identity);
            player.Cam = camera;
            try
            {
                tool.Equip(player, tool.GetComponent<Rigidbody>());
                Assert.IsNotNull(tool.FirstPersonModel);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera.Cam);
                var visible = tool.FirstPersonModel.GetComponentsInChildren<MeshRenderer>();
                Assert.IsTrue(visible.Any(r => r.enabled && !r.forceRenderingOff &&
                    GeometryUtility.TestPlanesAABB(planes, r.bounds)), name + " must appear in the camera frustum.");
                tool.Unequip();
                for (int i = 0; i < transforms.Length; i++)
                {
                    Assert.That(Vector3.Distance(positions[i], transforms[i].localPosition), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(rotations[i], transforms[i].localRotation), Is.LessThan(.01f));
                    Assert.That(Vector3.Distance(scales[i], transforms[i].localScale), Is.LessThan(.0001f));
                }
            }
            finally
            {
                if (tool.IsEquipped) tool.Unequip();
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(player.gameObject);
            }
        }

        /// <summary>
        /// <b>연 씬을 반드시 닫는다</b> (S15P21B202-604). 열어 둔 채 끝나면 뒤이어 도는
        /// 검증들이 이 씬의 콜라이더를 만나 무너진다 — 2026-09-17 에 그렇게
        /// <c>CraftMeteorImpactTests</c> 9건이 한꺼번에 깨졌다(기체가 남의 씬 바닥
        /// 4m 에 걸렸다). <c>test_scene_cleanup.py</c> 가 이 정리를 강제한다.
        /// </summary>
        [OneTimeTearDown]
        public void CloseAnyOpenScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator AuthoredToolsStaySeatedAndCanBePickedUpAndDropped()
        {
            var original = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
            var all = original.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var spawn = all.Single(t => t.name == "Player Spawn - inside Shelter");
            Assert.AreEqual(new Vector3(-31.8f, .346f, -24f), spawn.position);
            var standRoot = all.Single(t => t.name == "Shelter - Tool Displays");
            Assert.AreEqual(2, standRoot.childCount);
            var sources = new[] { all.Single(t => t.name == "MeteorBat_Stage1"), all.Single(t => t.name == "WeldingPlasmaGun_Stage1") };
            Assert.IsTrue(sources.All(t => t.GetComponent<ToolDisplayMount>() != null));
            Assert.IsTrue(sources.All(t => !t.GetComponent<Rigidbody>().isKinematic));

            // Exercise the exact authored models/physics without starting the match or network.
            var isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(isolated);
            var stands = Object.Instantiate(standRoot.gameObject);
            stands.name = "Test stands";
            var clones = sources.Select(source =>
            {
                var clone = Object.Instantiate(source.gameObject);
                clone.name = source.name;
                clone.transform.SetPositionAndRotation(source.position, source.rotation);
                clone.transform.localScale = source.lossyScale;
                return clone;
            }).ToArray();
            for (int i = 0; i < clones.Length; i++)
            {
                var serialized = new SerializedObject(clones[i].GetComponent<ToolDisplayMount>());
                serialized.FindProperty("stand").objectReferenceValue = stands.transform.GetChild(i);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var floor = new GameObject("Test shelter floor");
            floor.layer = GameLayers.Ground;
            var floorCollider = floor.AddComponent<BoxCollider>();
            floorCollider.center = new Vector3(-30.5f, .163f, -24);
            floorCollider.size = new Vector3(8.4f, .266f, 9.8f);
            EditorSceneManager.CloseScene(original, true);
            yield return new EnterPlayMode();

            var tools = Object.FindObjectsByType<ToolDisplayMount>(FindObjectsSortMode.None)
                .OrderBy(t => t.name).ToArray();
            Assert.AreEqual(2, tools.Length);
            var bodies = tools.Select(t => t.GetComponent<Rigidbody>()).ToArray();
            var positions = bodies.Select(b => b.position).ToArray();
            var rotations = bodies.Select(b => b.rotation).ToArray();
            var mode = Physics.simulationMode;
            var gravity = Physics.gravity;
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = Vector3.down * 9.81f;
            Physics.SyncTransforms();
            for (int i = 0; i < 600; i++) Physics.Simulate(1f / 60f);
            var report = "10 seconds at Earth gravity; original scene tool models and colliders.\n";
            for (int i = 0; i < bodies.Length; i++)
            {
                var distance = Vector3.Distance(positions[i], bodies[i].position);
                var angle = Quaternion.Angle(rotations[i], bodies[i].rotation);
                report += bodies[i].name + ": displacement=" + distance + ", rotation=" + angle + "\n";
                Assert.Less(distance, .01f, bodies[i].name);
                Assert.Less(angle, 1f, bodies[i].name);
            }

            foreach (var mount in tools)
            {
                var tool = mount.GetComponent<ToolBase>();
                var body = mount.GetComponent<Rigidbody>();
                // Use PlayerRig's real focus -> grab -> equip -> Q release path.
                var player = PlayerRig.Create("Stand pickup test", new Vector3(100, 20, 100),
                    Quaternion.identity, Color.white, Color.gray, false);
                var handle = mount.transform.Find("Handle_Collider").GetComponent<Collider>();
                var desiredAim = handle.bounds.center - Vector3.forward * .55f;
                player.transform.position += desiredAim - player.AimOrigin;
                Physics.SyncTransforms();
                player.RefreshInteractionFocus();
                Assert.AreSame(tool.GetComponent<Grabbable>(), player.CurrentInteractionTarget.Grabbable, tool.name + " is reachable");
                player.SetInput(new InputFrame { GrabLeft = true });
                player.Tick();
                Assert.AreSame(tool, player.ActiveTool);
                yield return null;
                Assert.IsNull(tool.GetComponent<FixedJoint>(), "Pickup must release the world mount.");
                Assert.IsFalse(mount.enabled);
                player.SetInput(new InputFrame { ToolAction = true });
                player.Tick();
                player.SetInput(new InputFrame());
                player.Tick();
                Assert.IsNull(player.ActiveTool);
                Assert.IsFalse(body.isKinematic);
                Assert.IsTrue(body.useGravity);
                Assert.AreEqual(RigidbodyConstraints.None, body.constraints);
                Assert.IsTrue(body.detectCollisions);
                Object.Destroy(player.gameObject);
                yield return null;
                var dropped = body.position;
                body.AddForce(Vector3.right * 2f + Vector3.up, ForceMode.VelocityChange);
                for (int i = 0; i < 60; i++) Physics.Simulate(1f / 60f);
                Assert.Greater(Vector3.Distance(dropped, body.position), .05f);
                // Check settling, not just that a released body moves. Use the authored
                // colliders and restored model, above the middle of the isolated floor.
                body.position = new Vector3(-30.5f, 2f, -24f);
                body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                for (int i = 0; i < 600; i++) Physics.Simulate(1f / 60f);
                Assert.Greater(body.position.y, -.5f, tool.name + " must not fall through the floor.");
                Assert.Less(body.linearVelocity.magnitude, .1f, tool.name + " must settle after dropping.");
                foreach (var renderer in tool.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.enabled)
                        Assert.Greater(renderer.bounds.center.y, .15f,
                            tool.name + "/" + renderer.name + " must remain above the floor.");
                report += tool.name + ": focus, pickup, detach, Q drop, gravity/throw PASS\n";
            }
            Physics.gravity = gravity;
            Physics.simulationMode = mode;
            Directory.CreateDirectory("../tmp/shelter-tool-stands");
            File.WriteAllText("../tmp/shelter-tool-stands/play-validation.txt", report);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator RemotePickupLateJoinAndDisableReleaseMounts()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var stand = new GameObject("Test stand");
            var standCollider = stand.AddComponent<BoxCollider>();
            GameObject Make(string name)
            {
                var go = new GameObject(name);
                go.SetActive(false);
                go.AddComponent<Rigidbody>();
                go.AddComponent<SphereCollider>();
                go.AddComponent<MeteorBat>();
                var mount = go.AddComponent<ToolDisplayMount>();
                var so = new SerializedObject(mount);
                so.FindProperty("stand").objectReferenceValue = stand.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                go.SetActive(true);
                return go;
            }
            var remote = Make("remote");
            Assert.IsTrue(Physics.GetIgnoreCollision(remote.GetComponent<Collider>(), standCollider));
            remote.GetComponent<MeteorBat>().ApplyRemoteHeld(true);
            remote.GetComponent<MeteorBat>().ApplyRemoteHeld(false);
            yield return null;
            Assert.IsNull(remote.GetComponent<FixedJoint>());
            Assert.IsFalse(Physics.GetIgnoreCollision(remote.GetComponent<Collider>(), standCollider));
            var late = Make("late-joining dropped ghost");
            late.GetComponent<Rigidbody>().isKinematic = true;
            late.transform.position = Vector3.right * 3;
            Physics.SyncTransforms();
            yield return null;
            yield return null;
            Assert.IsNull(late.GetComponent<FixedJoint>());
            Assert.AreEqual(Vector3.right * 3, late.transform.position);
            var disabled = Make("disabled");
            disabled.GetComponent<ToolDisplayMount>().enabled = false;
            yield return null;
            Assert.IsNull(disabled.GetComponent<FixedJoint>());
            Assert.IsFalse(Physics.GetIgnoreCollision(disabled.GetComponent<Collider>(), standCollider));
            Object.Destroy(remote);
            Object.Destroy(late);
            Object.Destroy(disabled);
            Object.Destroy(stand);
            yield return new ExitPlayMode();
        }
    }
}
