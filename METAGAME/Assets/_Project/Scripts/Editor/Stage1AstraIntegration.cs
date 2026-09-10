using System;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Gameplay.Hazards;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1AstraIntegration
    {
        public const string ScenePath = "Assets/Scenes/Stage1_Astra.unity";
        const string Art = "Assets/_Project/Art/Stage1Concept/Materials/";

        // One-shot import on a separate worktree. Source is copied externally before execution.
        // Never writes Main, existing prefabs, build settings or the 392 project.
        public static void Integrate()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch import workflow.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (scene.GetRootGameObjects().Any(g => g.GetComponent<AuthoredMapSettings>()))
                throw new InvalidOperationException("Scene is already integrated; do not replace authored edits.");
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var access = all.Single(t => t.name.StartsWith("Playable access"));
            access.SetParent(null, true);
            var oldPuzzle = scene.GetRootGameObjects().Single(g => g.name == "Stage1");
            Object.DestroyImmediate(oldPuzzle);
            foreach (var boot in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceBootstrap>(true)).ToArray())
                Object.DestroyImmediate(boot.gameObject);
            // Unknown components belong to the excluded concept gameplay, never production assets.
            foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            var source = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
            GameObject puzzle;
            SpaceBootstrap bootstrap;
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Stage1");
                var gameplay = new GameObject("Gameplay");
                gameplay.transform.SetParent(root.transform);
                var original = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == "Puzzle" && t.parent != null && t.parent.name == "Gameplay");
                puzzle = Object.Instantiate(original.gameObject, gameplay.transform);
                puzzle.name = "Puzzle";
                var originalBoot = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceBootstrap>(true)).Single();
                bootstrap = Object.Instantiate(originalBoot.gameObject).GetComponent<SpaceBootstrap>();
                SceneManager.MoveGameObjectToScene(bootstrap.gameObject, scene);
                bootstrap.name = "SpaceBootstrap - develop gameplay";
            }
            finally { EditorSceneManager.CloseScene(source, true); }
            SceneManager.SetActiveScene(scene);
            var map = new GameObject("Authored Map Settings").AddComponent<AuthoredMapSettings>();
            var spawn = new GameObject("Player Spawn - southwest camp").transform;
            spawn.position = new Vector3(-22, .4f, -29);
            spawn.SetParent(map.transform, true);
            map.playerSpawn = spawn;
            bootstrap.buildLevel = false;
            bootstrap.starfield = false;
            var parts = puzzle.GetComponentsInChildren<SpaceshipPart>(true).OrderBy(p => p.name).ToArray();
            Vector3[] feet = { new Vector3(-20,.25f,-20), new Vector3(28,8.1f,19),
                new Vector3(-29,5.42f,25), new Vector3(19,1f,-19) };
            if (parts.Length != 4) throw new InvalidOperationException("Expected four develop parts.");
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].transform.position = feet[i] + Vector3.up * PartSpec.All[i].Height * .5f;
                Physics.SyncTransforms();
                AlignToGround(parts[i]);
            }
            var assembly = puzzle.GetComponentInChildren<ShipAssembly>(true);
            assembly.transform.position = new Vector3(0,.99f,0);
            assembly.Configure(assembly.transform);
            var meteor = puzzle.GetComponentInChildren<MeteorSpawner>(true);
            meteor.SetArea(map.transform, new Vector2(94,86));
            var so = new SerializedObject(meteor);
            so.FindProperty("_launchPad").objectReferenceValue = assembly.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            puzzle.GetComponentInChildren<ShipHatch>(true).Configure(assembly, assembly.GetComponent<LaunchTest>(), meteor);
            int item = 0;
            foreach (Transform tool in puzzle.transform.Find("Equipment"))
                tool.position = new Vector3(-23.5f + item % 3 * 1.1f, 1.1f, -26 + item++ / 3 * 1.2f);
            var central = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name.StartsWith("02 · CENTRAL"));
            var lamps = central.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Lamp · light"
                || r.name.Contains("light strip") || r.name == "Ring · Assembly inner" || r.name == "Ring · Assembly outer").ToArray();
            var guides = scene.GetRootGameObjects().Single(g => g.name == "Module Runway Guides");
            var signal = guides.AddComponent<AssemblyGuidanceLight>();
            signal.Configure(assembly, lamps, new[] { Mat("Route amber"), Mat("Route cyan"), Mat("Route orchid"), Mat("Route lime") }, Mat("Assembly ready"));
            if (!scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Any())
            {
                var camera = new GameObject("Preview Camera").AddComponent<Camera>();
                camera.transform.position = new Vector3(67,65,-80);
                camera.transform.LookAt(new Vector3(0,0,0));
                camera.farClipPlane = 400;
            }
            Physics.SyncTransforms();
            EditorSceneManager.SaveScene(scene);
            Validate();
            Debug.Log("[Astra403] INTEGRATION COMPLETE " + ScenePath);
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Art + name + ".mat");

        static void AlignToGround(SpaceshipPart part)
        {
            var colliders = part.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger
                && c.GetComponentInParent<PartClamp>() == null).ToArray();
            var bounds = colliders[0].bounds;
            foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
            if (!Physics.Raycast(new Vector3(bounds.center.x,bounds.max.y+2,bounds.center.z), Vector3.down,
                out var ground, 40, GameLayers.GroundMask)) throw new InvalidOperationException("Missing ground: " + part.name);
            part.transform.position += Vector3.up * (ground.point.y + .025f - bounds.min.y);
            Physics.SyncTransforms();
            foreach (var pin in part.GetComponentsInChildren<PartClamp>(true))
            {
                var offset = pin.transform.position - bounds.center;
                var pos = new Vector3(bounds.center.x + Mathf.Sign(offset.x) * (bounds.extents.x + .18f),
                    bounds.max.y + 2, bounds.center.z + Mathf.Sign(offset.z) * (bounds.extents.z + .18f));
                if (!Physics.Raycast(pos, Vector3.down, out var support, 40, GameLayers.GroundMask))
                    throw new InvalidOperationException("Missing pin ground: " + pin.name);
                pos.y = support.point.y;
                pin.transform.position = pos;
            }
        }

        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            if (all.Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
                throw new InvalidOperationException("Missing scripts remain.");
            if (all.Count(t => t.GetComponent<SpaceshipPart>()) != 4 || all.Count(t => t.GetComponent<SpaceBootstrap>()) != 1)
                throw new InvalidOperationException("Invalid gameplay composition.");
            if (all.Count(t => t.GetComponent<PartTractorActivator>()) != 1)
                throw new InvalidOperationException("Develop tractor activator was not preserved.");
            Debug.Log("[Astra403] VALID: 4 develop parts, 1 bootstrap, 1 original activator, no missing scripts.");
        }

        public static void RenderPreview()
        {
            Validate();
            var camera = Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(65,80,-90);
            camera.transform.LookAt(new Vector3(0,1,0));
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.farClipPlane = 400;
            var rt = new RenderTexture(1600,900,24);
            rt.Create();
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                for (int i = 0; i < 4; i++) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt;
                var image = new Texture2D(1600,900,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,900),0,0);
                image.Apply();
                File.WriteAllBytes(Path.GetFullPath("../astra-preview.png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                Debug.Log("[Astra403] PREVIEW COMPLETE");
            }
            finally
            {
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = async;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        public static void PlaySmoke()
        {
            // The existing project auto-migration edits Main/Lobby; it must not run during this isolated test.
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            Validate();
            SessionState.SetBool("Astra403Smoke", true);
            SessionState.SetFloat("Astra403SmokeStart", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update += PollSmoke;
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void ResumeSmoke()
        {
            if (SessionState.GetBool("Astra403Smoke", false)) EditorApplication.update += PollSmoke;
        }

        static void PollSmoke()
        {
            if (!EditorApplication.isPlaying) return;
            float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat("Astra403SmokeStart", 0);
            if (elapsed < 12f) return;
            EditorApplication.update -= PollSmoke;
            SessionState.SetBool("Astra403Smoke", false);
            var bootstrap = Object.FindFirstObjectByType<SpaceBootstrap>();
            var parts = Object.FindObjectsByType<SpaceshipPart>(FindObjectsSortMode.None);
            bool okay = bootstrap != null && bootstrap.Player != null && parts.Length == 4
                && Vector3.Distance(bootstrap.Player.FootPosition, new Vector3(-22,.4f,-29)) < 5;
            Debug.Log("[Astra403] PLAY SMOKE " + (okay ? "PASS" : "FAIL")
                + " player=" + (bootstrap != null && bootstrap.Player != null ? bootstrap.Player.FootPosition.ToString() : "missing")
                + " parts=" + parts.Length);
            EditorApplication.Exit(okay ? 0 : 2);
        }
    }
}
