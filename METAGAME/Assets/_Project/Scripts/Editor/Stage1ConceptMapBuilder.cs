using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Metagame.Gameplay;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Builds only the standalone concept scene; production scenes and shared prefabs are never saved.</summary>
    public static class Stage1ConceptMapBuilder
    {
        public const string ScenePath = "Assets/Scenes/Stage1_Concept.unity";
        private const string Generated = "Assets/_Project/Art/Stage1Concept";
        private const string Environment = "Assets/_Project/Prefabs/Environment/Stage1/PF_Stage1_";
        private const string Ships = "Assets/_Project/Prefabs/Spaceships/Stage1/PF_Stage1_";
        private static Transform _root;
        private static Material _ground, _stone, _metal, _dark, _ivory, _cyan, _amber, _purple;
        private static System.Random _random;
        private static readonly Dictionary<string, Material> Tinted = new Dictionary<string, Material>();

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/별도 컨셉 씬 열기")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/컨셉 씬 생성·재구성")]
        public static void Build()
        {
            if (!Application.isBatchMode)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("컨셉 씬 재구성",
                    "Stage1_Concept의 수동 편집 내용이 생성 버전으로 교체됩니다. 기존 씬은 프로젝트 밖 concept-backups에 백업합니다. Main은 변경하지 않습니다.", "백업 후 재구성", "취소")) return;
            }
            BackupScene();
            Directory.CreateDirectory(Generated + "/Materials");
            Directory.CreateDirectory(Generated + "/Meshes");
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("STAGE 01 — ASTRA BASIN [ART STUDY]").transform;
            _random = new System.Random(173185);
            Tinted.Clear();
            CreateMaterials();
            BuildLandscape();
            BuildAssembly();
            BuildCamp();
            BuildTractorStation();
            BuildHill();
            BuildHangar();
            DressLandscape();
            BuildSkyAndLighting();
            BuildViewer();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(_root.gameObject.scene, ScenePath);
            Validate();
            Debug.Log("[ConceptMap] SAVED " + ScenePath + ". Production scenes, networking, physics settings unchanged.");
        }

        private static void BackupScene()
        {
            if (!File.Exists(ScenePath)) return;
            string directory = Path.GetFullPath("../concept-backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);
            File.Copy(ScenePath, directory + "/Stage1_Concept.unity");
            Debug.Log("[ConceptMap] Previous scene backed up: " + directory);
        }

        private static Transform Group(string name, Transform parent = null)
        {
            var transform = new GameObject(name).transform;
            transform.SetParent(parent != null ? parent : _root, false);
            return transform;
        }

        private static void CreateMaterials()
        {
            _ground = Material("Ground — lilac mineral sand", new Color(.32f, .36f, .47f), 0f, .05f);
            _ground.shader = Shader.Find("NOT THIS PLANET/Concept Mineral Ground");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_Project/Textures/Environment/Stage1/SandTexture.png");
            _ground.SetTexture("_BaseMap", texture);
            _ground.SetTextureScale("_BaseMap", Vector2.one);
            _stone = Material("Amethyst stone", new Color(.40f, .39f, .54f), 0f, .12f);
            _metal = Material("Titanium blue", new Color(.43f, .49f, .57f), .18f, .32f);
            _dark = Material("Midnight chassis", new Color(.12f, .17f, .24f), .12f, .3f);
            _ivory = Material("Warm ceramic", new Color(.72f, .69f, .64f), .28f, .42f);
            _cyan = Material("Ion cyan", new Color(.04f, .65f, .85f), .25f, .4f, new Color(.02f, 1.5f, 3.2f));
            _amber = Material("Habitat amber", new Color(1f, .58f, .15f), .1f, .3f, new Color(3f, 1.2f, .2f));
            _purple = Material("Signal orchid", new Color(.56f, .22f, .88f), .2f, .4f, new Color(.8f, .12f, 1.7f));
        }

        private static Material Material(string name, Color color, float metallic, float smoothness, Color emission = default)
        {
            string path = Generated + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission.maxColorComponent > 0f)
            {
                material.SetColor("_EmissionColor", emission);
                // URP's importer otherwise removes _EMISSION while EmissiveIsBlack remains set.
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                material.EnableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Solid(string name, PrimitiveType type, Vector3 position, Vector3 scale,
            Material material, Transform parent, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            else if (type == PrimitiveType.Cylinder)
            {
                // Unity cylinders default to CapsuleCollider. On a broad, thin deck this
                // creates a domed collision volume instead of the visible flat surface.
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            go.isStatic = true;
            return go;
        }

        private static GameObject Box(string name, Vector3 p, Vector3 s, Material m, Transform parent)
            => Solid(name, PrimitiveType.Cube, p, s, m, parent);

        private static GameObject Disc(string name, Vector3 p, float radius, float height, Material m, Transform parent)
            => Solid(name, PrimitiveType.Cylinder, p, new Vector3(radius * 2f, height / 2f, radius * 2f), m, parent);

        private static Mesh PersistMesh(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = Generated + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent, bool collider = false)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        private static void Ring(string name, Vector3 center, float radius, float width, Material material, Transform parent, int segments = 96)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices.Add(direction * (radius - width / 2f));
                vertices.Add(direction * (radius + width / 2f));
                uv.Add(new Vector2((float)i / segments, 0));
                uv.Add(new Vector2((float)i / segments, 1));
                if (i == segments) continue;
                int n = i * 2;
                triangles.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
            }
            var go = MeshObject("Ring · " + name, PersistMesh("Ring-" + name, vertices, triangles, uv), material, parent);
            go.transform.position = center;
        }

        private static float GroundHeight(float x, float z)
        {
            float hillRadius = Vector2.Distance(new Vector2(x, z), new Vector2(28f, 19f));
            float hill = 7f * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(9f, 12f, hillRadius)));
            float hangar = 5f * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0f, 4f,
                Mathf.Max(Mathf.Abs(x + 23f) - 16f, Mathf.Abs(z - 27f) - 10f))));
            float noise = (Mathf.PerlinNoise(x * .09f + 35f, z * .09f + 74f) - .5f) * .38f;
            return Mathf.Max(hill, hangar) + noise * Mathf.Clamp01((new Vector2(x, z).magnitude - 8f) / 5f);
        }

        private static void BuildLandscape()
        {
            var group = Group("01 · Landscape / walkable mineral basin");
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            const int size = 112;
            for (int z = 0; z <= size; z++)
            for (int x = 0; x <= size; x++)
            {
                float px = x - size / 2f, pz = z - size / 2f;
                vertices.Add(new Vector3(px, GroundHeight(px, pz), pz));
                uv.Add(new Vector2(px / 9f, pz / 9f));
                if (x == size || z == size) continue;
                int n = z * (size + 1) + x;
                triangles.AddRange(new[] { n, n + size + 1, n + 1, n + 1, n + size + 1, n + size + 2 });
            }
            MeshObject("Basin · sculpted ground", PersistMesh("BasinGround", vertices, triangles, uv), _ground, group, true);

            // Winding eastern walking route remains a deliberate alternative to the future tractor shortcut.
            var rampPoints = new[] { new Vector3(28, 0, -7), new Vector3(40, 1.7f, -2),
                new Vector3(45, 3.5f, 10), new Vector3(43, 5.4f, 25), new Vector3(33, 7.1f, 28), new Vector3(28, 7.1f, 22) };
            for (int i = 0; i < rampPoints.Length - 1; i++)
            {
                Vector3 a = rampPoints[i], b = rampPoints[i + 1];
                var ramp = Box("Long walking trail " + i, (a + b) / 2f - Vector3.up * .3f,
                    new Vector3(4.4f, .6f, Vector3.Distance(a, b) + .7f), _stone, group);
                ramp.transform.rotation = Quaternion.LookRotation(b - a);
                for (int j = 0; j < 4; j++)
                {
                    Vector3 p = Vector3.Lerp(a, b, j / 4f);
                    Vector3 side = Vector3.Cross((b - a).normalized, Vector3.up) * 1.85f;
                    Bollard(p + side, _amber, group, .62f);
                }
            }
        }

        private static void Platform(string name, Vector3 center, float radius, Material glow, Transform parent, bool pillars)
        {
            Disc(name + " · foundation", center + Vector3.up * .2f, radius, .75f, _dark, parent);
            Disc(name + " · rim", center + Vector3.up * .57f, radius * .97f, .25f, _ivory, parent);
            Disc(name + " · deck", center + Vector3.up * .72f, radius * .87f, .2f, _metal, parent);
            Disc(name + " · center", center + Vector3.up * .83f, radius * .44f, .06f, _dark, parent);
            Ring(name + " outer", center + Vector3.up * .84f, radius * .88f, .11f, glow, parent);
            Ring(name + " inner", center + Vector3.up * .88f, radius * .46f, .07f, glow, parent);
            Ring(name + " seam", center + Vector3.up * .86f, radius * .67f, .035f, _dark, parent);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2f / 16f;
                Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var tile = Box(name + " · rim panel " + i, center + radial * radius * .935f + Vector3.up * .755f,
                    new Vector3(radius * .22f, .11f, radius * .075f), i % 4 == 0 ? _dark : _metal, parent);
                tile.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0);
                if (i % 2 == 0)
                {
                    var strip = Box(name + " · light strip " + i, center + radial * radius * .94f + Vector3.up * .82f,
                        new Vector3(.35f, .035f, .1f), glow, parent);
                    strip.transform.rotation = tile.transform.rotation;
                }
            }
            if (pillars)
                for (int i = 0; i < 4; i++)
                {
                    float angle = (45f + 90f * i) * Mathf.Deg2Rad;
                    Bollard(center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius * .91f + Vector3.up * .8f,
                        glow, parent, 2.3f);
                }
            for (int i = 0; i < 4; i++)
                Box(name + " · entry stair " + i, center + new Vector3(0, .12f + i * .19f, -radius - 1.4f + i * .48f),
                    new Vector3(radius * .55f, .24f, .6f), _metal, parent);
        }

        private static void Bollard(Vector3 p, Material glow, Transform parent, float height = 1.2f)
        {
            Disc("Lamp · foot", p + Vector3.up * .1f, height * .2f, .2f, _dark, parent);
            Disc("Lamp · ceramic body", p + Vector3.up * height * .46f, height * .12f, height * .75f, _ivory, parent);
            Disc("Lamp · light", p + Vector3.up * height * .64f, height * .073f, height * .72f, glow, parent);
            Disc("Lamp · cap", p + Vector3.up * height, height * .12f, height * .07f, _dark, parent);
        }

        private static void BuildAssembly()
        {
            var group = Group("02 · CENTRAL LAUNCH ASSEMBLY / rendezvous");
            Platform("Assembly", Vector3.zero, 9.5f, _cyan, group, true);
            Disc("Assembly socket", new Vector3(0, .91f, 0), 1.3f, .12f, _metal, group);
            Ring("Assembly socket guide", new Vector3(0, .99f, 0), 1.1f, .07f, _cyan, group);
            Terminal(new Vector3(10f, 0, -3f), 230f, group, false);
            PointLight(new Vector3(0, 3f, 0), new Color(.1f, .55f, 1f), 8f, 14f, group);
            Rabbit(new Vector3(-5.5f, .84f, -3.2f), 120f, group);
        }

        private static GameObject Asset(string path, string name, Vector3 groundPosition, float height, float yaw, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing required asset: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            Bounds bounds = BoundsOf(go);
            go.transform.localScale *= height / Mathf.Max(bounds.size.y, .01f);
            bounds = BoundsOf(go);
            go.transform.position += groundPosition - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) behaviour.enabled = false;
            return go;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No renderer: " + go.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void Rabbit(Vector3 p, float yaw, Transform parent)
        {
            var go = Asset("Assets/_Project/Prefabs/Resources/SF_Rabbit_Base.prefab", "Rabbit crew · scale reference", p, 1.55f, yaw, parent);
            // Relax the bind-pose arms on scene instances only; no shared rig or avatar is edited.
            foreach (var bone in go.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "L_Upperarm") bone.localRotation *= Quaternion.Euler(0, 0, 48);
                if (bone.name == "R_Upperarm") bone.localRotation *= Quaternion.Euler(0, 0, -48);
            }
        }

        private static void BuildCamp()
        {
            var group = Group("03 · SOUTHWEST / starting habitat + pushable engine [visual]");
            Vector3 engine = new Vector3(-20f, .2f, -20f);
            Box("Engine parking plinth", engine + Vector3.up * .1f, new Vector3(5f, .4f, 4.5f), _metal, group);
            Asset(Ships + "Spaceship_Robot.prefab", "ENGINE · first module / manual route", engine + Vector3.up * .3f, 3.2f, 55f, group);
            Beacon(new Vector3(-22.3f, 0f, -18f), _amber, group);
            BuildExpeditionShelter(group);
            Box("Habitat front deck", new Vector3(-29f, .2f, -26f), new Vector3(8, .4f, 5), _metal, group);
            // Segmented canopy reads as a small expedition camp, made entirely from local geometry.
            for (int i = 0; i < 4; i++)
            {
                float x = -33f + i * 2.4f;
                Box("Canopy support", new Vector3(x, 1.9f, -28f), new Vector3(.12f, 3.8f, .12f), _dark, group);
                var roof = Box("Amber awning panel", new Vector3(x + 1f, 4f, -26.2f), new Vector3(2.38f, .14f, 4.6f), _ivory, group);
                roof.transform.rotation = Quaternion.Euler(-8f, 0, 0);
                Box("Awning warm strip", new Vector3(x + 1f, 3.55f, -28.45f), new Vector3(1.6f, .06f, .08f), _amber, group);
            }
            for (int i = 0; i < 5; i++) Crate(new Vector3(-34f + i % 3 * 1.25f, .1f + (i / 3) * .9f, -22f + i / 3), group);
            for (int i = 0; i < 5; i++)
                Bollard(new Vector3(-29f + i * 2.2f, 0, -29f), _amber, group);
            PointLight(new Vector3(-28f, 3.2f, -25f), new Color(1f, .52f, .2f), 12f, 11f, group);
            Rabbit(new Vector3(-22f, .2f, -23f), 45f, group);
            // Dotted route leads back to the assembly pad without introducing a new puzzle rule.
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = Vector3.Lerp(new Vector3(-18, 0, -18), new Vector3(-5, 0, -7), i / 11f);
                var tile = Box("Engine route · stepping plate", p + Vector3.up * .08f, new Vector3(1.4f, .16f, .8f), _metal, group);
                tile.transform.rotation = Quaternion.Euler(0, -48, 0);
            }
        }

        private static void Crate(Vector3 p, Transform parent)
        {
            Box("Supply crate", p + Vector3.up * .45f, new Vector3(1.1f, .9f, .9f), _ivory, parent);
            Box("Crate inset", p + new Vector3(0, .45f, -.46f), new Vector3(.8f, .6f, .04f), _dark, parent);
            Box("Crate latch", p + new Vector3(0, .6f, -.49f), new Vector3(.22f, .1f, .04f), _amber, parent);
        }

        private static void BuildExpeditionShelter(Transform parent)
        {
            // Purpose-built shelter: spaceship modules are not duplicated as static buildings.
            var shelter = Group("Expedition shelter · original camp architecture", parent);
            Vector3 center = new Vector3(-29, 1.1f, -23);
            Box("Shelter floor", center + Vector3.down * .85f, new Vector3(7, .4f, 5.5f), _metal, shelter);
            for (int i = 0; i < 12; i++)
            {
                float angle = (-82.5f + i * 15f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0);
                var panel = Box("Curved insulated roof panel " + i, center + radial * 3.3f,
                    new Vector3(.9f, .22f, 5.1f), _ivory, shelter);
                panel.transform.rotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
                for (int end = -1; end <= 1; end += 2)
                {
                    var rim = Box("Roof end frame", center + radial * 3.32f + Vector3.forward * end * 2.56f,
                        new Vector3(.88f, .29f, .18f), _metal, shelter);
                    rim.transform.rotation = panel.transform.rotation;
                }
            }
            Box("Shelter rear bulkhead", center + new Vector3(0, .65f, 2.48f), new Vector3(5.4f, 3.2f, .2f), _ivory, shelter);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Entrance side panel", center + new Vector3(side * 2.2f, .25f, -2.48f), new Vector3(1.9f, 2.7f, .22f), _ivory, shelter);
                Box("Warm door jamb", center + new Vector3(side * 1.25f, .25f, -2.63f), new Vector3(.085f, 2.6f, .085f), _amber, shelter);
                Box("Front utility inset", center + new Vector3(side * 2.2f, .25f, -2.62f), new Vector3(1.15f, .62f, .045f), _dark, shelter);
            }
            Box("Entrance canopy header", center + new Vector3(0, 1.65f, -2.6f), new Vector3(2.6f, .22f, .3f), _metal, shelter);
            Box("Entrance warm light", center + new Vector3(0, 1.55f, -2.78f), new Vector3(2.1f, .07f, .07f), _amber, shelter);
            PointLight(center + new Vector3(0, 1.2f, -.8f), new Color(1f, .71f, .42f), 6, 7, shelter);
        }

        private static void BuildTractorStation()
        {
            var group = Group("04 · SOUTHEAST / tractor activation · color sequence [visual]");
            Vector3 center = new Vector3(23, 0, -19);
            Platform("Tractor dock", center, 6f, _amber, group, false);
            Asset(Ships + "TractorCraft.prefab", "TRACTOR CONTROL · final module", center + new Vector3(1, 1.45f, 1), 4.7f, 205f, group);
            Terminal(center + new Vector3(-4, .84f, -1.5f), 20f, group, true);
            for (int i = 0; i < 5; i++)
            {
                float angle = (i * 65f + 20f) * Mathf.Deg2Rad;
                Bollard(center + new Vector3(Mathf.Sin(angle) * 5.5f, .8f, Mathf.Cos(angle) * 5.5f), _amber, group);
            }
            Rabbit(center + new Vector3(-2.7f, .83f, -3.2f), 5, group);
            PointLight(center + new Vector3(0, 3, 0), new Color(1, .58f, .25f), 6, 11, group);
        }

        private static void Terminal(Vector3 p, float yaw, Transform parent, bool sequence)
        {
            Transform group = Group(sequence ? "Color-sequence terminal · visual placeholder" : "Assembly console", parent);
            Box("Console foot", p + Vector3.up * .2f, new Vector3(1.3f, .4f, .95f), _dark, group);
            Box("Console support", p + Vector3.up * .8f, new Vector3(.7f, 1.1f, .65f), _ivory, group);
            var screen = Box("Console housing", p + new Vector3(0, 1.5f, 0), new Vector3(1.5f, .18f, 1.45f), _ivory, group);
            screen.transform.rotation = Quaternion.Euler(-32, 0, 0);
            var glass = Box("Glass", p + new Vector3(0, 1.61f, -.025f), new Vector3(1.26f, .04f, 1.18f), _dark, group);
            glass.transform.rotation = screen.transform.rotation;
            for (int i = 0; i < 4; i++)
            {
                Material color = sequence ? new[] { _amber, _cyan, _purple, _cyan }[i] : _cyan;
                var cell = Box("Signal cell " + i, p + new Vector3(0, 1.41f + i * .14f, -.39f + i * .22f),
                    new Vector3(.62f, .035f, .14f), color, group);
                cell.transform.rotation = screen.transform.rotation;
            }
            // Rotate the assembly around its own center, not around the scene origin.
            group.position = p;
            foreach (Transform child in group) child.position -= p;
            group.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private static void BuildHill()
        {
            var group = Group("05 · EAST / elevated open-air module · long ramp + lift shortcut");
            Vector3 center = new Vector3(28, 7.2f, 19);
            Platform("Hill observatory", center, 6.1f, _cyan, group, false);
            Asset(Ships + "Spaceship_HoverPod.prefab", "OPEN-AIR HOVER POD · future extraction puzzle", center + Vector3.up * .87f, 2.7f, 200f, group);
            Beacon(center + new Vector3(4.5f, .8f, 1), _cyan, group);
            for (int i = 0; i < 3; i++)
            {
                float a = (30f + i * 120f) * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * 4.6f, .85f, Mathf.Cos(a) * 4.6f);
                Disc("Observatory instrument", p + Vector3.up * .45f, .4f, .9f, _ivory, group);
                Disc("Instrument indicator", p + Vector3.up * .94f, .29f, .08f, _cyan, group);
            }
            // South-facing exposed cliff makes the lifting shortcut legible from the assembly pad.
            for (int i = 0; i < 10; i++)
            {
                float a = (155f + i * 15f) * Mathf.Deg2Rad;
                var rock = Asset(Environment + "RockPillars.prefab", "Hill cliff · exposed lift face",
                    new Vector3(28 + Mathf.Sin(a) * 10.1f, -.4f, 19 + Mathf.Cos(a) * 10.1f), 7.8f + Next(-.6f, .6f), Next(0, 360), group);
                TintRock(rock);
            }
        }

        private static void Beacon(Vector3 p, Material glow, Transform parent)
        {
            Bollard(p, glow, parent, 2.5f);
            // Thin emissive mast is a readable blockout for a future skyward beacon, not a gameplay trigger.
            Solid("Module beacon · visual guide", PrimitiveType.Cylinder, p + Vector3.up * 7.5f,
                new Vector3(.075f, 5f, .075f), glow, parent, false);
        }

        private static void BuildHangar()
        {
            var group = Group("06 · NORTHWEST / low-ceiling rock hangar + open pickup apron");
            Vector3 origin = new Vector3(-25, 5.15f, 27);
            Box("Hangar slab", origin, new Vector3(19, .5f, 15), _metal, group);
            Box("Back wall", origin + new Vector3(0, 3.1f, 7.1f), new Vector3(19, 6.4f, 1.1f), _dark, group);
            Box("Left wall", origin + new Vector3(-9, 3.1f, 0), new Vector3(1.1f, 6.4f, 15), _dark, group);
            Box("Cargo guide · outer lip", origin + new Vector3(4.6f, .65f, -6.9f), new Vector3(9, 1.3f, .7f), _metal, group);
            Box("Low solid ceiling · tractor cannot enter", origin + new Vector3(0, 6.6f, 1.4f), new Vector3(19.7f, .6f, 12.1f), _stone, group);
            for (int i = 0; i < 3; i++)
            {
                float x = -34f + i * 9f;
                Box("Front portal · structural rib", new Vector3(x, 8.2f, 22.1f), new Vector3(.9f, 6f, 1.1f), _ivory, group);
                Box("Portal amber light", new Vector3(x + .47f, 8.5f, 21.5f), new Vector3(.075f, 4.7f, .08f), _amber, group);
            }
            Box("Portal lintel", new Vector3(-25.4f, 11.1f, 22f), new Vector3(19.8f, .7f, .8f), _ivory, group);
            Box("Portal horizontal light", new Vector3(-25.4f, 10.8f, 21.56f), new Vector3(18f, .085f, .045f), _amber, group);
            Asset(Ships + "Spaceship_Capsule.prefab", "ENCLOSED CAPSULE · move outside before lifting", new Vector3(-29, 5.42f, 25f), 3.6f, 170f, group);
            Beacon(new Vector3(-32.5f, 11.7f, 30), _purple, group);
            Platform("Cargo pickup apron", new Vector3(-9.5f, 5f, 29f), 6f, _amber, group, false);
            Box("Cargo connector", new Vector3(-15, 5.35f, 27), new Vector3(6, .6f, 6), _metal, group);
            Box("Cargo corner baffle", new Vector3(-20, 6.2f, 26), new Vector3(1, 1.6f, 6), _metal, group);
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = new Vector3(-20 + i * 2, 5.73f, 30.8f);
                Box("Cargo route marking", p, new Vector3(.85f, .035f, .17f), _amber, group);
            }
            // Separate narrow access bridge with a gap; the broad cargo apron remains open to the sky.
            for (int i = 0; i < 7; i++)
            {
                if (i == 3 || i == 4) continue;
                Box("Broken personnel bridge " + i, new Vector3(-29, 5f, 12 + i * 1.35f), new Vector3(2.6f, .4f, 1.15f), _metal, group);
            }
            for (int i = 0; i < 10; i++)
            {
                float x = -39 + i * 3.8f;
                if (x > -35 && x < -27) continue; // leave the pedestrian ramp unobstructed
                var rock = Asset(Environment + "RockPillars.prefab", "Hangar cliff face",
                    new Vector3(x, -.25f, 13.7f), Next(5.6f, 6.2f), Next(0, 360), group);
                TintRock(rock);
            }
            for (int i = 0; i < 7; i++)
            {
                Box("Hangar roof panel", new Vector3(-33f + i * 2.65f, 12.1f, 28.4f),
                    new Vector3(2.48f, .16f, 11.5f), _metal, group);
            }
            // Finish the interior as a recognizable mining outpost rather than a bare box.
            for (int i = 0; i < 7; i++)
            {
                float x = -33f + i * 2.5f;
                Box("Rear wall panel", new Vector3(x, 8.2f, 33.48f), new Vector3(2.3f, 4.8f, .14f), _metal, group);
                Box("Rear utility light", new Vector3(x, 9.1f, 33.36f), new Vector3(1.3f, .13f, .08f), _amber, group);
                Box("Roof inner rib", new Vector3(x, 11.16f, 27.7f), new Vector3(.18f, .4f, 11.8f), _metal, group);
            }
            for (int i = 0; i < 5; i++) Crate(new Vector3(-31.5f + i * 1.3f, 5.42f, 31.6f), group);
            PointLight(new Vector3(-29, 8, 24), new Color(1, .6f, .28f), 14, 12, group);
            for (int i = 0; i < 5; i++)
                Bollard(new Vector3(-13f + i * 1.7f, 5.9f, 24.5f), _amber, group, 1f);
            Rabbit(new Vector3(-11, 5.85f, 27), 230, group);
        }

        private static void TintRock(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;
                    string key = source.name;
                    if (!Tinted.TryGetValue(key, out Material material))
                    {
                        string path = Generated + "/Materials/RockTint-" + key.Replace('/', '-') + ".mat";
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material == null)
                        {
                            material = new Material(source);
                            AssetDatabase.CreateAsset(material, path);
                        }
                        // Preserve the original atlas and its value variation; do not multiply
                        // already-purple assets by another dark saturated purple wash.
                        material.CopyPropertiesFromMaterial(source);
                        material.SetColor("_BaseColor", new Color(.93f, .94f, 1f));
                        material.SetFloat("_Smoothness", .12f);
                        EditorUtility.SetDirty(material);
                        Tinted[key] = material;
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static float Next(float min, float max) => Mathf.Lerp(min, max, (float)_random.NextDouble());

        private static void DressLandscape()
        {
            var group = Group("07 · Ecology / amethyst outcrops, mushrooms, bioluminescence");
            // Layered perimeter rather than a repeated straight wall; keep the foreground below the overview sightline.
            for (int i = 0; i < 68; i++)
            {
                float a = i * Mathf.PI * 2f / 68f;
                float radius = Next(45f, 50f);
                Vector3 p = new Vector3(Mathf.Sin(a) * radius, -.6f, Mathf.Cos(a) * radius * .95f);
                float height = p.z < -25f ? Next(2.4f, 3.5f) : Next(3.8f, 5.5f);
                var rock = Asset(Environment + (i % 3 == 0 ? "CliffWall" : "RockPillars") + ".prefab",
                    "Perimeter outcrop " + i, p, height, Next(0, 360), group);
                TintRock(rock);
                if (i % 2 == 0)
                    Asset(Environment + "MushroomTree.prefab", "Canopy silhouette " + i, p + new Vector3(0, height * .35f, 2),
                        p.z < -25 ? Next(2.8f, 4.2f) : Next(3.5f, 5.8f), Next(0, 360), group);
            }
            Vector3[] clusters = { new Vector3(-17, 0, 6), new Vector3(15, 0, 8), new Vector3(-7, 0, -26),
                new Vector3(12, 0, -32), new Vector3(-36, 0, -9), new Vector3(3, 0, 37), new Vector3(36, 0, -15),
                new Vector3(-40, 0, 2), new Vector3(13, 0, 36), new Vector3(-5, 0, 17) };
            for (int i = 0; i < clusters.Length; i++)
            {
                Vector3 center = clusters[i];
                center.y = GroundHeight(center.x, center.z);
                var rock = Asset(Environment + "RockClusterA.prefab", "Interior mineral island " + i,
                    center, Next(1.2f, 2.2f), Next(0, 360), group);
                TintRock(rock);
                Asset(Environment + "MushroomTree.prefab", "Mushroom grove " + i, center + new Vector3(1, 0, 1),
                    Next(2.8f, 4.2f), Next(0, 360), group);
                for (int j = 0; j < 6; j++)
                {
                    Vector3 p = center + new Vector3(Next(-3.5f, 3.5f), 0, Next(-3.5f, 3.5f));
                    p.y = GroundHeight(p.x, p.z);
                    Asset(Environment + (j % 2 == 0 ? "PurpleFlowerSmall" : "AlienFlowerCluster") + ".prefab",
                        "Understory flowers", p, Next(.45f, 1.1f), Next(0, 360), group);
                }
                CrystalCluster(center + new Vector3(-2.3f, 0, -1), group, i);
            }
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 p = new Vector3(Mathf.Sin(a) * Next(37, 43), 0, Mathf.Cos(a) * Next(38, 43));
                p.y = GroundHeight(p.x, p.z);
                CrystalCluster(p, group, i + 10);
            }
            BuildPebbles(group);
        }

        private static void BuildPebbles(Transform parent)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            for (int i = 0; i < 1000; i++)
            {
                float x = Next(-43f, 43f), z = Next(-39f, 42f);
                if (new Vector2(x, z).magnitude < 10.5f ||
                    Vector2.Distance(new Vector2(x,z), new Vector2(23,-19)) < 7f ||
                    (x < -17 && z < -17) || (x < -3 && z > 19) ||
                    Vector2.Distance(new Vector2(x,z), new Vector2(28,19)) < 8f) continue;
                Vector3 p = new Vector3(x, GroundHeight(x,z) + .015f, z);
                float size = Next(.055f, .22f);
                int n = vertices.Count;
                float rotation = Next(0, Mathf.PI * 2);
                for (int v = 0; v < 6; v++)
                {
                    float angle = rotation + v * Mathf.PI / 3;
                    vertices.Add(p + new Vector3(Mathf.Cos(angle) * size, 0, Mathf.Sin(angle) * size * .7f));
                    uv.Add(Vector2.zero);
                }
                vertices.Add(p + new Vector3(size*.13f,size*.55f,0));
                uv.Add(Vector2.zero);
                for (int v = 0; v < 6; v++) triangles.AddRange(new[] { n+v,n+6,n+(v+1)%6 });
            }
            MeshObject("Mineral scree · single batched mesh", PersistMesh("BasinScree", vertices, triangles, uv), _stone, parent);
        }

        private static void CrystalCluster(Vector3 p, Transform parent, int index)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            // Hexagonal tapered crystals: authored mesh, no paid generation and no external dependency.
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = new Vector3(Next(-.6f, .6f), 0, Next(-.6f, .6f));
                float height = Next(.55f, 1.7f), radius = height * .16f;
                for (int side = 0; side < 6; side++)
                {
                    float a = side * Mathf.PI / 3f, b = (side + 1) * Mathf.PI / 3f;
                    Vector3 a0 = offset + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                    Vector3 b0 = offset + new Vector3(Mathf.Cos(b) * radius, 0, Mathf.Sin(b) * radius);
                    Vector3 a1 = a0 + Vector3.up * height * .72f, b1 = b0 + Vector3.up * height * .72f;
                    int n = vertices.Count;
                    vertices.AddRange(new[] { a0, a1, b1, b0, offset + Vector3.up * height });
                    for (int v = 0; v < 5; v++) uv.Add(Vector2.zero);
                    triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3, n + 1, n + 4, n + 2 });
                }
            }
            var crystalMaterial = Material("Crystal facets · restrained glow", new Color(.12f, .55f, .78f), .1f, .55f, new Color(.025f, .18f, .30f));
            var go = MeshObject("Ion crystals " + index, PersistMesh("Crystals-" + index, vertices, triangles, uv), crystalMaterial, parent);
            go.transform.position = p;
        }

        private static void PointLight(Vector3 p, Color color, float intensity, float range, Transform parent)
        {
            var light = new GameObject("Local bounce · " + color).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.position = p;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void BuildSkyAndLighting()
        {
            var group = Group("08 · Atmosphere / twilight art lighting");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(Generated + "/Materials/Twilight sky.mat");
            if (sky == null)
            {
                sky = new Material(Shader.Find("Metagame/Atmosphere/SkyGradient"));
                AssetDatabase.CreateAsset(sky, Generated + "/Materials/Twilight sky.mat");
            }
            sky.SetColor("_ZenithColor", new Color(.012f, .018f, .065f));
            sky.SetColor("_HorizonColor", new Color(.15f, .12f, .29f));
            sky.SetColor("_NadirColor", new Color(.035f, .026f, .065f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.53f, .59f, .78f);
            RenderSettings.ambientEquatorColor = new Color(.42f, .45f, .59f);
            RenderSettings.ambientGroundColor = new Color(.28f, .28f, .37f);
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(.13f, .17f, .24f));
            RenderSettings.ambientProbe = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.085f, .078f, .17f);
            RenderSettings.fogDensity = .0028f;
            var sun = new GameObject("Dusk key · warm lavender").AddComponent<Light>();
            sun.transform.SetParent(group);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0);
            sun.color = new Color(1f, .90f, .80f);
            sun.intensity = 1.05f;
            sun.shadowStrength = .72f;
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .25f;
            RenderSettings.sun = sun;
            var fill = new GameObject("Cool rim · no extra shadow map").AddComponent<Light>();
            fill.transform.SetParent(group);
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(25, 145, 0);
            fill.color = new Color(.65f, .75f, 1f);
            fill.intensity = .48f;

            var planet = Asset(Environment + "Planet.prefab", "Distant alien world", new Vector3(42, -52, 113), 100f, 30f, group);
            foreach (var renderer in planet.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            BuildStars(group);
            var volume = new GameObject("Concept grading · isolated volume").AddComponent<Volume>();
            volume.transform.SetParent(group);
            volume.isGlobal = true;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Generated + "/ConceptVolume.asset");
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Generated + "/ConceptVolume.asset");
            }
            Bloom bloom = VolumeComponent<Bloom>(profile);
            bloom.intensity.Override(.25f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(.65f);
            Tonemapping tone = VolumeComponent<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);
            ColorAdjustments colors = VolumeComponent<ColorAdjustments>(profile);
            colors.postExposure.Override(.12f);
            colors.contrast.Override(2);
            colors.saturation.Override(-10);
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(profile);
        }

        private static void BuildStars(Transform parent)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uv = new List<Vector2>();
            for (int i = 0; i < 1000; i++)
            {
                Vector3 p = new Vector3(Next(-340, 340), Next(10, 210), Next(240, 300));
                float size = Next(.055f, .24f);
                int n = vertices.Count;
                vertices.AddRange(new[] { p + new Vector3(-size,-size,0), p + new Vector3(-size,size,0),
                    p + new Vector3(size,size,0), p + new Vector3(size,-size,0) });
                uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                triangles.AddRange(new[] { n,n+1,n+2,n,n+2,n+3 });
            }
            Material star = Material("Starlight", new Color(.65f,.75f,1), 0, 0, new Color(.8f,1f,1.5f));
            MeshObject("Distant star field", PersistMesh("Stars", vertices, triangles, uv), star, parent);
        }

        private static UniversalRenderPipelineAsset ConceptPipeline()
        {
            string path = Generated + "/ConceptPipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipeline == null)
            {
                pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"));
                pipeline.name = "ConceptPipeline · isolated art preview";
                AssetDatabase.CreateAsset(pipeline, path);
            }
            pipeline.shadowDistance = 180f;
            pipeline.supportsHDR = true;
            pipeline.renderScale = 1f;
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        private static T VolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void BuildViewer()
        {
            Transform group = Group("09 · Viewer / Play = free flight, 1–5 viewpoints");
            Vector3[] positions = { new Vector3(0, 61, -72), new Vector3(-15, 5, -30),
                new Vector3(15, 7, -21), new Vector3(-28.5f, 8.1f, 18), new Vector3(16, 13, 7) };
            Vector3[] targets = { new Vector3(0, 2, 4), new Vector3(-26, 2, -22),
                new Vector3(0, 1, 2), new Vector3(-27, 7.2f, 29), new Vector3(28, 9, 19) };
            string[] names = { "01 OVERVIEW", "02 START CAMP", "03 ASSEMBLY", "04 HANGAR", "05 HILL" };
            var viewpoints = new Transform[positions.Length];
            for (int i = 0; i < viewpoints.Length; i++)
            {
                viewpoints[i] = Group(names[i], group);
                viewpoints[i].position = positions[i];
                viewpoints[i].LookAt(targets[i]);
            }
            var camera = new GameObject("Main Camera · concept explorer", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.transform.SetParent(group, false);
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(viewpoints[0].position, viewpoints[0].rotation);
            camera.fieldOfView = 48f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 500f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            camera.gameObject.AddComponent<Stage1ConceptExplorer>().Configure(viewpoints, ConceptPipeline());
        }

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/저장된 컨셉 씬 검사")]
        public static void Validate()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int missing = 0, renderers = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                foreach (var renderer in transform.GetComponents<Renderer>())
                {
                    renderers++;
                    foreach (var material in renderer.sharedMaterials)
                        if (material == null || material.shader == null || !material.shader.isSupported)
                            throw new InvalidOperationException("Invalid material on " + renderer.name);
                }
            }
            if (missing != 0) throw new InvalidOperationException("Missing scripts: " + missing);
            if (Object.FindObjectsByType<Camera>().Length != 1)
                throw new InvalidOperationException("Concept scene must contain exactly one camera.");
            Debug.Log("[ConceptMap] VALIDATION PASS: " + renderers + " renderers; no missing scripts or unsupported materials.");
        }

        // Batch entry point. Saves a real Unity-rendered view, never AI concept art disguised as a screenshot.
        public static void RenderPreviews()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Validate();
            string directory = Path.GetFullPath("../concept-previews");
            Directory.CreateDirectory(directory);
            var camera = Camera.main;
            var previousPipeline = QualitySettings.renderPipeline;
            bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            QualitySettings.renderPipeline = ConceptPipeline();
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
            var viewer = camera.GetComponent<Stage1ConceptExplorer>();
            viewer.enabled = false;
            var serialized = new SerializedObject(viewer);
            var views = serialized.FindProperty("_viewpoints");
            for (int i = 0; i < views.arraySize; i++)
            {
                var point = (Transform)views.GetArrayElementAtIndex(i).objectReferenceValue;
                camera.transform.SetPositionAndRotation(point.position, point.rotation);
                var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                // StandardRequest includes the camera's Volume update; SingleCameraRequest does not.
                var request = new RenderPipeline.StandardRequest { destination = rt };
                // Warm up SRP/shader variants before reading back. First-frame imports can show placeholder shaders.
                for (int warmup = 0; warmup < 4; warmup++) RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                string path = Path.Combine(directory, "stage1-" + (i + 1) + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                rt.Release();
                Object.DestroyImmediate(rt);
                Debug.Log("[ConceptMap] RENDERED " + path);
            }
            }
            finally
            {
                QualitySettings.renderPipeline = previousPipeline;
                ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
            }
        }
    }
}
