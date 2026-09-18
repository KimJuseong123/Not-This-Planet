using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1MainLaunchpadIntegration
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/AssemblyParts/PF_Stage1_MainAssemblyLaunchpad.prefab";
        const string ModelFolder = "Assets/_Project/Models/MainAssemblyLaunchpad";
        const string Generated = ModelFolder + "/Integration";
        const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        static string Output => Path.GetFullPath("../tmp/main-launchpad-integration");

        [MenuItem("Metagame/Stage1/Apply Main Launchpad")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlaying || scene.isDirty)
                throw new InvalidOperationException("Open the saved Stage1_Astra scene in Edit Mode first.");
            Directory.CreateDirectory(Output);
            File.Copy(ScenePath, Path.Combine(Output, "Stage1_Astra.before.unity"), true);
            BuildPrefab();
            var assembly = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShipAssembly>(true)).Single();
            Undo.RecordObject(assembly.transform, "Position main assembly deck");
            assembly.transform.position = new Vector3(0, 1.44f, 0);
            var old = assembly.transform.Find("MainAssemblyLaunchpad");
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), assembly.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Place main launchpad");
            instance.name = "MainAssemblyLaunchpad";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0, 45, 0);
            var dock = assembly.GetComponent<AssemblyDock>();
            if (!dock) dock = Undo.AddComponent<AssemblyDock>(assembly.gameObject);
            var serialized = new SerializedObject(dock);
            serialized.FindProperty("_authoredVisual").objectReferenceValue = instance.GetComponent<AssemblyDockVisual>();
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Stage1_Astra.");
            AssetDatabase.SaveAssets();
            Validate();
            Capture();
        }

        static void BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder(ModelFolder, "Integration");
            var root = new GameObject("MainAssemblyLaunchpad");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "/SM_Astra_MainAssemblyLaunchpad.fbx"));
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var waiting = StatusMaterial("M_Launchpad_Waiting", new Color(1f, .12f, .025f));
                var locked = StatusMaterial("M_Launchpad_Locked", new Color(.04f, 1f, 1f));
                var jaws = model.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Jaw_")).OrderBy(t => t.name).ToArray();
                if (jaws.Length != 4) throw new InvalidOperationException("FBX must contain four moving jaws.");
                var open = jaws.Select(t => t.localPosition).ToArray();
                var closed = jaws.Select(t => t.localPosition + t.parent.InverseTransformVector(-Vector3.ProjectOnPlane(t.position, Vector3.up).normalized * .25f)).ToArray();
                var renderers = new List<Renderer>();
                foreach (var jaw in jaws)
                {
                    var filter = jaw.GetComponent<MeshFilter>();
                    var mesh = Object.Instantiate(filter.sharedMesh);
                    mesh.name = jaw.name + "_Status";
                    var body = new List<int>();
                    var lamp = new List<int>();
                    var uv = mesh.uv;
                    var triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var sample = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3f;
                        var target = sample.x > .5f && sample.x < .75f && sample.y > .25f && sample.y < .5f ? lamp : body;
                        target.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
                    }
                    if (lamp.Count == 0) throw new InvalidOperationException("No status lamp faces found: " + jaw.name);
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(body, 0); mesh.SetTriangles(lamp, 1);
                    filter.sharedMesh = SaveMesh(mesh, Generated + "/" + mesh.name + ".asset");
                    var renderer = jaw.GetComponent<MeshRenderer>();
                    renderer.sharedMaterials = new[] { renderer.sharedMaterial, waiting };
                    renderers.Add(renderer);
                }
                root.AddComponent<AssemblyDockVisual>().Configure(jaws, open, closed, renderers.ToArray(), waiting, locked);
                var collision = new GameObject("StaticCollision"); collision.transform.SetParent(root.transform, false);
                AddMesh(collision.transform, "Deck", DeckMesh());
                // The entry piece is already a small mesh and accurately preserves its sloped surface.
                var entry = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name.StartsWith("05_Entry"));
                var ramp = new GameObject("EntryRamp"); ramp.transform.SetParent(collision.transform, false);
                ramp.transform.SetPositionAndRotation(entry.transform.position, entry.transform.rotation);
                ramp.transform.localScale = entry.transform.lossyScale;
                ramp.AddComponent<MeshCollider>().sharedMesh = entry.sharedMesh;
                foreach (int side in new[] { -1, 1 })
                {
                    AddBox(collision.transform, "PylonBase" + side, new Vector3(side * 2.52f, .18f, 2.04f), new Vector3(1.08f, .68f, 1.04f));
                    var column = AddBox(collision.transform, "PylonColumn" + side, new Vector3(side * 2.68f, 1.64f, 2.04f), new Vector3(.86f, 2.25f, .82f));
                    column.localRotation = Quaternion.Euler(0, 0, -side * 8.6f);
                }
                // Fixed peripheral equipment; no collider over the rocket assembly volume or moving jaws.
                foreach (var prefix in new[] { "08_RearService", "09_Console" })
                {
                    var r = model.GetComponentsInChildren<MeshRenderer>().Single(x => x.name.StartsWith(prefix));
                    AddBox(collision.transform, prefix, root.transform.InverseTransformPoint(r.bounds.center), r.bounds.size);
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material StatusMaterial(string name, Color color)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", color * 2f);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Smoothness", .5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        static Mesh DeckMesh()
        {
            const int count = 48;
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            var radii = new[] { 2.16f, 2.5f, 3.56f, 3.84f };
            var heights = new[] { 0f, .025f, .035f, -.45f };
            for (int ring = 0; ring < radii.Length; ring++)
                for (int i = 0; i < count; i++)
                {
                    float angle = 2 * Mathf.PI * i / count;
                    vertices.Add(new Vector3(Mathf.Sin(angle) * radii[ring], heights[ring], Mathf.Cos(angle) * radii[ring]));
                }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.AddRange(new[] { 0, 1 + i, 1 + next });
                for (int ring = 0; ring < radii.Length - 1; ring++)
                {
                    int a = 1 + ring * count + i, b = 1 + ring * count + next, c = a + count, d = b + count;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }
            var mesh = new Mesh { name = "Launchpad_Deck_Collision" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return SaveMesh(mesh, Generated + "/Launchpad_Deck_Collision.asset");
        }

        static void AddMesh(Transform parent, string name, Mesh mesh)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        static Transform AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = center; go.AddComponent<BoxCollider>().size = size; return go.transform;
        }

        [MenuItem("Metagame/Stage1/Validate Main Launchpad")]
        public static void Validate()
        {
            Directory.CreateDirectory(Output);
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Stage1_Astra first.");
            var assembly = Object.FindObjectsByType<ShipAssembly>().Single();
            var visual = assembly.GetComponentInChildren<AssemblyDockVisual>();
            if (!visual || !assembly.GetComponent<AssemblyDock>()) throw new InvalidOperationException("Missing dock wiring.");
            if (new SerializedObject(assembly.GetComponent<AssemblyDock>()).FindProperty("_authoredVisual").objectReferenceValue != visual)
                throw new InvalidOperationException("Authored visual reference missing.");
            UnityEngine.Physics.SyncTransforms();
            var colliders = visual.GetComponentsInChildren<Collider>();
            if (colliders.Length != 8 || colliders.Any(c => c.isTrigger || c.attachedRigidbody || c.gameObject.layer != 8))
                throw new InvalidOperationException("Invalid static collision layout.");
            var deck = colliders.Single(c => c.name == "Deck");
            var report = new StringBuilder();
            // Actual support plane across the engine footprint and unobstructed rocket column.
            foreach (float x in new[] { -.8f, 0f, .8f }) foreach (float z in new[] { -.8f, 0f, .8f })
            {
                var ray = new Ray(assembly.LaunchPad.position + new Vector3(x, 10, z), Vector3.down);
                if (!deck.Raycast(ray, out var hit, 11) || Mathf.Abs(hit.point.y - assembly.LaunchPad.position.y) > .005f)
                    throw new InvalidOperationException("Engine support mismatch.");
                foreach (var c in colliders.Where(c => c != deck))
                    if (c.Raycast(ray, out var obstacle, 9.9f)) throw new InvalidOperationException("Rocket column obstructed: " + c.name);
            }
            if (!assembly.GetComponent<LaunchTest>() || !assembly.GetComponentInChildren<ShipHatch>(true))
                throw new InvalidOperationException("Launch or hatch integration missing.");
            report.AppendLine("PASS: 9 engine support rays; rocket column clear; 8 static World-layer colliders; original ShipAssembly/LaunchTest/ShipHatch retained.");
            report.AppendLine("Assembly datum=" + assembly.LaunchPad.position.ToString("F4") + "; launchpad yaw=" + visual.transform.eulerAngles.y);
            report.AppendLine("Renderers=" + visual.GetComponentsInChildren<Renderer>().Length + "; colliders=" + colliders.Length);
            File.WriteAllText(Path.Combine(Output, "validation.txt"), report.ToString());
        }

        public static void Capture()
        {
            Directory.CreateDirectory(Output);
            var target = Object.FindAnyObjectByType<AssemblyDockVisual>();
            var go = new GameObject("LaunchpadReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            var rt = new RenderTexture(1400, 1000, 24);
            var texture = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.nearClipPlane = .05f; camera.farClipPlane = 300;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.transform.position = target.transform.position + new Vector3(-10, 9, -12);
                camera.transform.LookAt(target.transform.position + Vector3.up * .7f);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(Output, "launchpad-scene.png"), texture.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
        }
    }
}
