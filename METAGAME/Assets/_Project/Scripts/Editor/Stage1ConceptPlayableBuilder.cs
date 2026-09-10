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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Integrates a copy of Main's real puzzle into the separate concept environment.</summary>
    public static class Stage1ConceptPlayableBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Stage1_Concept_Playable.unity";
        public const string LobbyPath = "Assets/_Project/Scenes/Stage1_Concept_Lobby.unity";
        private const string MainPath = "Assets/Scenes/Main.unity";
        private const string Art = "Assets/_Project/Art/Stage1Concept";

        public static void RebuildAndRender()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry only.");
            SessionState.SetBool("Metagame.URPSetupApplied", true);
            Stage1ConceptMapBuilder.Build();
            Build();
            Stage1ConceptMapBuilder.RenderPreviews();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/플레이용 씬 열기")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening the concept scene.");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/플레이용 씬 생성·재구성 (백업)")]
        public static void Build()
        {
            RequireCleanEditor();
            Backup(ScenePath);
            // Open the art scene, save as a different scene, then modify only the copy.
            var scene = EditorSceneManager.OpenScene(Stage1ConceptMapBuilder.ScenePath);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenery = scene.GetRootGameObjects().Single(g => g.name.StartsWith("STAGE 01"));
            scenery.name = "STAGE 01 — ASTRA BASIN / Environment";
            RemoveArtStandIns(scenery);
            PrepareCollision(scenery);

            var areaObject = new GameObject("Concept Play Area · 102 × 94 m");
            areaObject.AddComponent<Stage1PlayArea>();
            var puzzleRoot = new GameObject("Stage1");
            var gameplay = new GameObject("Gameplay");
            gameplay.transform.SetParent(puzzleRoot.transform);

            GameObject puzzle;
            SpaceBootstrap bootstrap;
            var original = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var sourcePuzzle = original.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == "Puzzle" && t.parent != null && t.parent.name == "Gameplay");
                puzzle = Object.Instantiate(sourcePuzzle.gameObject, gameplay.transform);
                puzzle.name = "Puzzle";
                var sourceBootstrap = original.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceBootstrap>(true)).Single();
                bootstrap = Object.Instantiate(sourceBootstrap.gameObject).GetComponent<SpaceBootstrap>();
                bootstrap.name = "SpaceBootstrap · Concept Playable";
                SceneManager.MoveGameObjectToScene(bootstrap.gameObject, scene);
            }
            finally { EditorSceneManager.CloseScene(original, true); }

            // Authored static stand-ins must never compete with the real physics bodies.
            var parts = puzzle.GetComponentsInChildren<SpaceshipPart>(true).OrderBy(p => p.name).ToArray();
            if (parts.Length != 4) throw new InvalidOperationException("Main must contain exactly four spaceship parts.");
            Vector3[] feet = { new Vector3(-20, .25f, -20), new Vector3(28, 8.10f, 19),
                new Vector3(-29, 5.42f, 25), new Vector3(19, 1.0f, -19) };
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].transform.SetPositionAndRotation(feet[i] + Vector3.up * (PartSpec.All[i].Height * .5f), Quaternion.identity);
                // Main converts the engine into a vehicle and disables its assembly sockets.
                // The final/top part becomes the carrier; the engine must stay an engine.
                var activator = parts[i].GetComponent<PartTractorActivator>();
                if (activator != null) Object.DestroyImmediate(activator);
            }

            var assembly = puzzle.GetComponentInChildren<ShipAssembly>(true);
            if (assembly == null) throw new InvalidOperationException("Main assembly is missing.");
            assembly.transform.position = new Vector3(0, .99f, 0);
            assembly.Configure(assembly.transform);
            var meteor = puzzle.GetComponentInChildren<MeteorSpawner>(true);
            if (meteor == null) throw new InvalidOperationException("Main meteor system is missing.");
            meteor.transform.position = Vector3.zero;
            meteor.SetArea(areaObject.transform, new Vector2(94, 86));
            var meteorSettings = new SerializedObject(meteor);
            meteorSettings.FindProperty("_launchPad").objectReferenceValue = assembly.transform;
            meteorSettings.ApplyModifiedPropertiesWithoutUndo();
            var hatch = puzzle.GetComponentInChildren<ShipHatch>(true);
            hatch.Configure(assembly, assembly.GetComponent<LaunchTest>(), meteor);

            var equipment = puzzle.transform.Find("Equipment");
            if (equipment == null || equipment.childCount == 0) throw new InvalidOperationException("Main equipment is missing.");
            int item = 0;
            foreach (Transform tool in equipment)
            {
                tool.position = new Vector3(-23.5f + (item % 3) * 1.1f, 1.1f, -26 + (item / 3) * 1.2f);
                item++;
            }

            var spawn = new GameObject("Player Spawn · southwest camp").transform;
            spawn.position = new Vector3(-22, .4f, -29);
            spawn.rotation = Quaternion.Euler(0, 10, 0);
            bootstrap.playerSpawn = spawn;
            bootstrap.buildLevel = false;
            bootstrap.randomizeTransportVehicles = false;
            bootstrap.preserveSceneAtmosphere = true;
            bootstrap.starfield = false;

            var presentation = areaObject.AddComponent<Stage1ConceptRuntime>();
            presentation.Configure(AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(Art + "/ConceptPipeline.asset"), null);
            BuildRoutes(gameplay.transform);
            AlignInteractionPlacement(parts);
            ConfigureManualParts(parts);
            ConfigureSingleTractor(parts, assembly);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            CreateLobbyCopy(scene);
            RegisterConceptScenes();
            Validate();
            Debug.Log("[ConceptPlayable] SAVED: " + ScenePath + ". Main/Lobby and shared prefabs were not saved or replaced.");
        }

        public static void RepairInteractionPlacement()
        {
            RequireCleanEditor();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the concept playable scene first.");
            Backup(ScenePath);
            AlignInteractionPlacement(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceshipPart>(true)).ToArray());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void EnableManualTransport()
        {
            RequireCleanEditor();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the concept playable scene first.");
            Backup(ScenePath);
            ConfigureManualParts(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceshipPart>(true)).ToArray());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void RepairTractorAssembly()
        {
            RequireCleanEditor();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the concept playable scene first.");
            var parts = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpaceshipPart>(true)).ToArray();
            if (parts.Length != 4 || parts.Count(p => p.name == PartSpec.All[3].Name) != 1)
                throw new InvalidOperationException("Expected the four original parts and one top TractorCraft part.");
            var assembly = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ShipAssembly>(true)).Single();
            var duplicate = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t => t.name == "Concept Carrier · reusable tractor (E to board)");
            Backup(ScenePath);
            ConfigureSingleTractor(parts, assembly);
            foreach (var runtime in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Stage1ConceptRuntime>(true)))
            {
                var so = new SerializedObject(runtime);
                so.FindProperty("_carrier").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }
            if (duplicate != null) Undo.DestroyObjectImmediate(duplicate.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Validate();
        }

        private static void ConfigureSingleTractor(SpaceshipPart[] parts, ShipAssembly assembly)
        {
            foreach (var part in parts)
            {
                var hover = part.GetComponent<PartGroundHover>();
                if (hover == null) hover = Undo.AddComponent<PartGroundHover>(part.gameObject);
                Undo.RecordObject(hover, "Protect placed assembly parts");
                hover.ConfigureAssemblyPad(assembly.LaunchPad);
                if (part.name != PartSpec.All[3].Name) continue;
                var activator = part.GetComponent<PartTractorActivator>();
                if (activator == null) activator = Undo.AddComponent<PartTractorActivator>(part.gameObject);
                Undo.RecordObject(activator, "Use final module as tractor");
                activator.ConfigureFinalModule(true, parts.Single(p => p.name == PartSpec.All[2].Name));
            }
        }

        private static void ConfigureManualParts(SpaceshipPart[] parts)
        {
            foreach (var part in parts)
            {
                var push = part.GetComponent<PartPush>();
                if (push == null) push = Undo.AddComponent<PartPush>(part.gameObject);
                Undo.RecordObject(push, "Enable manual part transport");
                push.ConfigureManualTransport(push.SpeedMultiplier, true);
                if (part.GetComponent<PartGroundHover>() == null) Undo.AddComponent<PartGroundHover>(part.gameObject);
            }
        }

        private static void AlignInteractionPlacement(SpaceshipPart[] parts)
        {
            Physics.SyncTransforms();
            foreach (var part in parts)
            {
                var solids = part.GetComponentsInChildren<Collider>(true)
                    .Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<PartClamp>() == null).ToArray();
                if (solids.Length == 0) throw new InvalidOperationException("Part has no solid body: " + part.name);
                var bounds = solids[0].bounds;
                foreach (var solid in solids.Skip(1)) bounds.Encapsulate(solid.bounds);
                var origin = new Vector3(bounds.center.x, bounds.max.y + 2f, bounds.center.z);
                if (!Physics.Raycast(origin, Vector3.down, out var ground, 30f, GameLayers.GroundMask))
                    throw new InvalidOperationException("Part has no supporting ground: " + part.name);
                part.transform.position += Vector3.up * (ground.point.y + .025f - bounds.min.y);
                Physics.SyncTransforms();
                // Put each pin just outside the actual body, not inside an assumed PartSpec footprint.
                foreach (var clamp in part.GetComponentsInChildren<PartClamp>(true))
                {
                    var offset = clamp.transform.position - bounds.center;
                    var position = new Vector3(bounds.center.x + Mathf.Sign(offset.x) * (bounds.extents.x + .18f),
                        bounds.max.y + 2f, bounds.center.z + Mathf.Sign(offset.z) * (bounds.extents.z + .18f));
                    if (!Physics.Raycast(position, Vector3.down, out var pinGround, 30f, GameLayers.GroundMask))
                        throw new InvalidOperationException("Pin has no supporting ground: " + clamp.name);
                    position.y = pinGround.point.y;
                    clamp.transform.position = position;
                }
                Physics.SyncTransforms();
            }
        }

        public static void RequireCleanEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your edited scene before building the concept copy.");
        }

        private static void RegisterConceptScenes()
        {
            // Append only: preserve the production startup scene and existing scene indices.
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { LobbyPath, ScenePath })
                if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            if (scenes.Count == EditorBuildSettings.scenes.Length) return;
            string folder = Path.GetFullPath("../concept-backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            File.Copy("ProjectSettings/EditorBuildSettings.asset", Path.Combine(folder, "EditorBuildSettings.asset"));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void Backup(string path)
        {
            if (!File.Exists(path)) return;
            string destination = Path.GetFullPath("../concept-backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(destination);
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));
        }

        private static void RemoveArtStandIns(GameObject root)
        {
            string[] prefixes = { "ENGINE ·", "TRACTOR CONTROL ·", "OPEN-AIR HOVER POD ·", "ENCLOSED CAPSULE ·", "Rabbit crew", "09 · Viewer" };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && prefixes.Any(p => t.name.StartsWith(p))) Object.DestroyImmediate(t.gameObject);
        }

        private static void PrepareCollision(GameObject root)
        {
            // Flora remains non-blocking; rocks/architecture use their authored surfaces.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Distant alien world")) continue;
                if (t.name.StartsWith("Perimeter") || t.name.StartsWith("Hill cliff") || t.name.StartsWith("Hangar cliff")
                    || t.name.StartsWith("Interior mineral island") || t.name.StartsWith("Habitat ·"))
                    MeshColliderUtility.EnsureMeshColliders(t.gameObject, false, true);
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = GameLayers.Ground;
        }

        private static void BuildRoutes(Transform parent)
        {
            var routes = new GameObject("Playable access · ramps and safety perimeter").transform;
            routes.SetParent(parent);
            Ramp("Assembly cargo ramp", new Vector3(-10, .15f, -10), new Vector3(-5, .84f, -6), 4f, routes);
            Ramp("Hangar pedestrian access", new Vector3(-35, .15f, 3), new Vector3(-29, 5.3f, 20), 3.2f, routes);
            Ramp("Hangar cargo exit threshold", new Vector3(-17, 5.42f, 27), new Vector3(-12, 5.85f, 27), 3.5f, routes);
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject("Map boundary " + i);
                wall.layer = GameLayers.Ground;
                wall.transform.SetParent(routes);
                wall.transform.position = i < 2 ? new Vector3(i == 0 ? -53 : 53, 11, 0) : new Vector3(0, 11, i == 2 ? -49 : 49);
                wall.AddComponent<BoxCollider>().size = i < 2 ? new Vector3(1, 24, 100) : new Vector3(108, 24, 1);
            }
        }

        private static void Ramp(string name, Vector3 a, Vector3 b, float width, Transform parent)
        {
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = name;
            ramp.layer = GameLayers.Ground;
            ramp.transform.SetParent(parent);
            ramp.transform.position = (a + b) * .5f - Vector3.up * .2f;
            ramp.transform.rotation = Quaternion.LookRotation(b - a);
            ramp.transform.localScale = new Vector3(width, .4f, Vector3.Distance(a, b) + .5f);
            ramp.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/Titanium blue.mat");
        }

        private static void CreateLobbyCopy(Scene playable)
        {
            Backup(LobbyPath);
            var lobby = EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", OpenSceneMode.Additive);
            try
            {
                // Exact copy of the authenticated multiplayer path, only online/offline scenes differ.
                EditorSceneManager.SaveScene(lobby, LobbyPath);
                foreach (var behaviour in lobby.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true)))
                {
                    if (behaviour == null) continue;
                    var serialized = new SerializedObject(behaviour);
                    var online = serialized.FindProperty("onlineScene");
                    if (online == null) continue;
                    online.stringValue = ScenePath;
                    serialized.FindProperty("offlineScene").stringValue = LobbyPath;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.SaveScene(lobby);
            }
            finally
            {
                EditorSceneManager.CloseScene(lobby, true);
                SceneManager.SetActiveScene(playable);
            }
        }

        public static void Validate()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the playable concept scene to validate it.");
            var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var t in objects)
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    throw new InvalidOperationException("Missing script: " + t.name);
            if (objects.Count(t => t.GetComponent<SpaceshipPart>() != null) != 4) throw new InvalidOperationException("Expected four actual parts.");
            if (objects.Count(t => t.GetComponent<SpaceBootstrap>() != null) != 1) throw new InvalidOperationException("Expected one gameplay bootstrap.");
            if (objects.Any(t => t.GetComponent<Stage1ConceptExplorer>() != null)) throw new InvalidOperationException("Fly camera remains in gameplay.");
            Debug.Log("[ConceptPlayable] VALID: four parts, one player bootstrap, authored ground, real Main equipment, carrier, assembly and meteors.");
        }
    }
}
