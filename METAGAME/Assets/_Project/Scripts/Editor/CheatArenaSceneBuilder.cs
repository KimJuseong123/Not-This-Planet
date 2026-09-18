using System.IO;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Gameplay.Cheats;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 치트 시험장 <c>Assets/Scenes/CheatArena.unity</c> 를 코드로 만든다.
    ///
    /// <para><b>무엇인가.</b> 400 × 400m 평평한 바닥 위에 <c>Stage1_Astra</c> 의
    /// <c>Stage1/Gameplay/Puzzle</c> 그룹(조립대·파츠 4기·운석 스포너·도구)을 그대로 복제해
    /// 놓은 씬이다. 기획 수치를 바꿔 보고 운석·조립·용접을 기다리지 않고 확인하는 자리다.
    /// 치트는 <see cref="CheatArenaConsole"/>(F8)이 담당한다.</para>
    ///
    /// <para><b>왜 복제인가.</b> 파츠·고정못·도구는 Stage1 씬에 저장된 상태(스킨·질량·콜라이더·
    /// 프리팹 참조)가 곧 실제 판이다. 코드로 새로 세우면 그때부터 둘이 갈라진다.
    /// <c>PracticeLobbySceneBuilder</c> 가 도구·고정못을 Stage1 에서 복제하는 것과 같은 이유다.
    /// 그래서 이 씬은 <b>손으로 고치지 않고 다시 만든다</b> — Stage1 이 바뀌면 메뉴를 한 번 더 돌린다.</para>
    ///
    /// <para><b>빌드에 들어가지 않는다.</b> 빌드 목록(<c>EditorBuildSettings</c>)에 넣지 않고,
    /// <c>SpaceBuild</c> 도 씬을 이름으로 고르므로 파일이 <c>Assets/Scenes/</c> 에 있어도 릴리스에는 없다.</para>
    /// </summary>
    public static class CheatArenaSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/CheatArena.unity";
        public const string SourceScenePath = "Assets/Scenes/Stage1_Astra.unity";
        public const string SourceGameplayPath = "Gameplay/Puzzle";
        public const string SourceRootName = "Stage1";
        public const string ClonedGroupName = "Stage1 Gameplay (Stage1_Astra 복제)";

        const string AssetDir = "Assets/_Project/Art/CheatArena";

        /// <summary>바닥 한 변(m). 운석 예고 원·낙하 각도·견인 비행을 넉넉히 볼 만큼.</summary>
        public const float ArenaSize = 400f;

        /// <summary>바닥 격자 간격(m). 거리 감각용이다.</summary>
        const float MajorGrid = 50f;

        /// <summary>체커 무늬 한 칸(m). 격자보다 촘촘해 걷는 속도를 눈으로 잰다.</summary>
        const float CheckerCell = 5f;

        /// <summary>운석 스포너의 착탄 후보 영역 한 변(m). Stage1(94 × 86)과 비슷하게.</summary>
        const float SpawnerArea = 80f;

        /// <summary>플레이어 출발 자리. 조립대(원점)를 +X 방향으로 바라본다.</summary>
        static readonly Vector3 PlayerSpawn = new(-12f, 0.05f, 0f);

        /// <summary>
        /// 바닥에 세우는 안내 표지판 문구.
        ///
        /// <para><b>키를 글자로 적지 않는다.</b> <see cref="CheatArenaConsole"/> 의 상수에서
        /// 만든다 — 손으로 적으면 키를 옮겼을 때 씬에 구워진 안내만 옛 키로 남는다. 실제로
        /// 씬 재시작을 F9 에서 Shift+F5 로 옮겼을 때 그렇게 됐다(S15P21B202-601 AI 리뷰).
        /// <c>CheatArenaTests</c> 가 씬의 표지판이 이 문구와 같은지 본다 — 생성기만 고치고
        /// 씬을 다시 만들지 않으면 거기서 걸린다.</para>
        /// </summary>
        public static string HintSign =>
            $"치트 시험장  ·  {CheatArenaConsole.ToggleKey} 패널  {CheatArenaConsole.QuickResetKey} 리셋  "
            + $"{CheatArenaConsole.DropMeteorKey} 운석  {CheatArenaConsole.WaveKey} 공세  "
            + $"Shift+{CheatArenaConsole.QuickResetKey} 씬 다시";

        static readonly Color GroundA = new(0.42f, 0.41f, 0.40f);
        static readonly Color GroundB = new(0.48f, 0.47f, 0.45f);
        static readonly Color LineColor = new(0.95f, 0.55f, 0.16f);
        static readonly Color LabelColor = new(0.83f, 1f, 0.95f);

        [MenuItem("Tools/Not This Planet/치트 시험장 씬 만들기 (CheatArena)", false, 40)]
        public static void BuildFromMenu()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog("치트 시험장",
                    "이미 있는 CheatArena 씬을 Stage1_Astra 기준으로 다시 만들어 덮어씁니다.\n"
                    + "씬을 손으로 고친 내용은 사라집니다. (치트 코드는 그대로입니다)",
                    "다시 만들기", "취소"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Build();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>CI·배치 진입. <c>-executeMethod Metagame.EditorTools.CheatArenaSceneBuilder.BuildBatch</c></summary>
        public static void BuildBatch() => Build();

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("플레이 중에는 씬을 만들 수 없다.");

            Directory.CreateDirectory(AssetDir);
            AssetDatabase.Refresh();

            var groundMaterial = GroundMaterial();
            var lineMaterial = UnlitMaterial("CheatArenaLine", LineColor);
            var groundPhysics = GroundPhysics();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("CheatArena · 치트 시험장").transform;

            BuildGround(root, groundMaterial, lineMaterial, groundPhysics);
            BuildLighting(root);

            var spawn = Child("Player Spawn", root);
            spawn.position = PlayerSpawn;

            var map = root.gameObject.AddComponent<AuthoredMapSettings>();
            map.playerSpawn = spawn;
            map.flightHalfSize = new Vector2(ArenaSize * 0.5f - 10f, ArenaSize * 0.5f - 10f);
            map.maximumAltitude = 60f;
            map.preserveSceneLighting = true;   // 이 씬의 조명·안개를 그대로 쓴다 (분위기 프로필은 별만 뿌린다)
            map.useCodeStarfield = true;
            map.randomizeVehicles = false;      // 운반체 자리는 매번 같아야 시험이 재현된다

            ImportStage1Gameplay(root, scene);

            root.gameObject.AddComponent<CheatArenaConsole>();
            PreviewCamera(root);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CheatArena] 치트 시험장 생성 완료 → {ScenePath}  "
                      + $"(빌드 목록에는 넣지 않았다. {HintSign})");
        }

        // ── Stage1 게임플레이 복제 ───────────────────────────────────────────

        static void ImportStage1Gameplay(Transform root, Scene target)
        {
            var source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            try
            {
                SpaceBootstrap sourceBootstrap = null;
                Transform puzzle = null;
                foreach (var sourceRoot in source.GetRootGameObjects())
                {
                    if (sourceBootstrap == null)
                        sourceBootstrap = sourceRoot.GetComponentInChildren<SpaceBootstrap>(true);
                    if (puzzle == null && sourceRoot.name == SourceRootName)
                        puzzle = sourceRoot.transform.Find(SourceGameplayPath);
                }

                if (sourceBootstrap == null)
                    throw new System.InvalidOperationException($"{SourceScenePath} 에 SpaceBootstrap 이 없다.");
                if (puzzle == null)
                    throw new System.InvalidOperationException(
                        $"{SourceScenePath} 에서 '{SourceRootName}/{SourceGameplayPath}' 를 찾지 못했다. Stage1 구조가 바뀌었는지 확인한다.");

                // 그룹을 통째로 복제한다. 조립대 ↔ 문 ↔ 스포너, 고정못 ↔ 파츠처럼 그룹 안에서
                // 서로 가리키는 참조가 복제본 안에서 그대로 이어지게 하기 위해서다.
                var clone = Object.Instantiate(puzzle.gameObject);
                clone.name = ClonedGroupName;
                SceneManager.MoveGameObjectToScene(clone, target);
                clone.transform.SetParent(root, true);
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;

                SettleOnGround(clone.transform);
                WireSpawner(clone.transform, root);

                // 게임플레이 부트스트랩은 Stage1 설정을 그대로 쓴다 (PracticeLobby 와 같은 방식).
                var bootstrap = root.gameObject.AddComponent<SpaceBootstrap>();
                EditorUtility.CopySerialized(sourceBootstrap, bootstrap);
                bootstrap.buildLevel = false;    // 바닥은 이 씬이 이미 세웠다 (SpaceFlop 코스를 세우지 않는다)
                bootstrap.starfield = false;

                // 원본 씬의 폭풍 연출 오브젝트를 가리키면 씬을 닫은 뒤 깨진 참조가 된다.
                // 비워 두면 SpaceBootstrap 이 스스로 하나 만든다.
                var serialized = new SerializedObject(bootstrap);
                var storm = serialized.FindProperty("stormPresentation");
                if (storm != null) storm.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // SpaceBootstrap 이 에디터·개발 빌드에서 GetComponent<DevManager>() 를 그대로 부른다.
                root.gameObject.AddComponent<DevManager>();
            }
            finally
            {
                EditorSceneManager.CloseScene(source, true);
                SceneManager.SetActiveScene(target);
            }
        }

        /// <summary>
        /// Stage1 의 지형 높이에 맞춰 있던 것들을 평평한 바닥(y = 0) 위에 내려 앉힌다.
        /// 파츠는 콜라이더 밑면이 0, 도구는 살짝 띄워 바닥에 끼지 않게 한다.
        /// </summary>
        static void SettleOnGround(Transform group)
        {
            Physics.SyncTransforms();
            for (var i = 0; i < group.childCount; i++)
            {
                var child = group.GetChild(i);

                if (child.GetComponent<MeteorSpawner>() != null)
                {
                    child.localPosition = Vector3.zero;
                    continue;
                }

                if (child.GetComponentInChildren<ToolBase>(true) != null && child.GetComponent<SpaceshipPart>() == null)
                {
                    // Equipment 그룹. 도구마다 따로 앉힌다 — 원본에서는 작업대 위에 있었다.
                    for (var j = 0; j < child.childCount; j++) Settle(child.GetChild(j), 0.05f);
                    continue;
                }

                Settle(child, 0f);
            }
        }

        static void Settle(Transform target, float lift)
        {
            if (!TryBounds(target, out var bounds)) return;
            target.position += Vector3.up * (lift - bounds.min.y);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 충돌 콜라이더 기준 경계. 트리거(고정못·출입문)는 뺀다. 콜라이더가 없으면 렌더러.
        ///
        /// <para><c>Collider.bounds</c> 를 읽지 않는다 — 에디터에서 막 복제한 강체의 상자 콜라이더는
        /// 물리 장면에 아직 올라가지 않아 경계가 비어 나왔다(2026-09-17 실측: 파츠가 8~13cm
        /// 묻혔다). 콜라이더의 로컬 상자 여덟 귀퉁이를 직접 월드로 옮겨 잰다.</para>
        /// </summary>
        static bool TryBounds(Transform target, out Bounds bounds)
        {
            bounds = default;
            var any = false;

            foreach (var collider in target.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !TryLocalBounds(collider, out var local)) continue;
                Encapsulate(ref bounds, ref any, collider.transform, local);
            }

            if (!any)
                foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { bounds = renderer.bounds; any = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }

            return any;
        }

        static bool TryLocalBounds(Collider collider, out Bounds local)
        {
            switch (collider)
            {
                case BoxCollider box:
                    local = new Bounds(box.center, box.size);
                    return true;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    local = mesh.sharedMesh.bounds;
                    return true;
                case SphereCollider sphere:
                    local = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;
                case CapsuleCollider capsule:
                {
                    var size = Vector3.one * (capsule.radius * 2f);
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    local = new Bounds(capsule.center, size);
                    return true;
                }
                default:
                    local = default;
                    return false;
            }
        }

        static void Encapsulate(ref Bounds bounds, ref bool any, Transform space, Bounds local)
        {
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var world = space.TransformPoint(corner);
                if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                else bounds.Encapsulate(world);
            }
        }

        /// <summary>착탄 영역을 시험장 중앙으로, 발사 구역을 복제된 조립대로 잇는다.</summary>
        static void WireSpawner(Transform group, Transform root)
        {
            var spawner = group.GetComponentInChildren<MeteorSpawner>(true);
            if (spawner == null)
            {
                Debug.LogWarning("[CheatArena] 복제한 그룹에 MeteorSpawner 가 없다. 운석 치트가 동작하지 않는다.");
                return;
            }

            var assembly = group.GetComponentInChildren<ShipAssembly>(true);
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_area").objectReferenceValue = root;
            serialized.FindProperty("_areaSize").vector2Value = new Vector2(SpawnerArea, SpawnerArea);
            if (assembly != null)
                serialized.FindProperty("_launchPad").objectReferenceValue = assembly.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (serialized.FindProperty("_meteorPrefab").objectReferenceValue == null
                && !Stage1MeteorAssets.TryWire(spawner))
                Debug.LogWarning("[CheatArena] 운석 프리팹이 없어 폴백 구체로 떨어진다.");
        }

        // ── 바닥 · 조명 · 카메라 ─────────────────────────────────────────────

        static void BuildGround(Transform root, Material ground, Material line, PhysicsMaterial physics)
        {
            var environment = Child("Environment", root);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Ground";
            floor.layer = GameLayers.Ground;
            floor.isStatic = true;
            floor.transform.SetParent(environment, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(ArenaSize, 1f, ArenaSize);
            floor.GetComponent<MeshRenderer>().sharedMaterial = ground;
            floor.GetComponent<Collider>().sharedMaterial = physics;

            var grid = Child($"Grid ({MajorGrid:0}m)", environment);
            var labels = Child("Labels", environment);
            var half = ArenaSize * 0.5f;
            var count = Mathf.RoundToInt(ArenaSize / MajorGrid);

            for (var i = 0; i <= count; i++)
            {
                var p = -half + i * MajorGrid;
                var axis = Mathf.Approximately(p, 0f);
                var width = axis ? 0.3f : 0.12f;

                Line($"X = {p:0}", new Vector3(p, 0.01f, 0f), new Vector3(width, 0.02f, ArenaSize), line, grid);
                Line($"Z = {p:0}", new Vector3(0f, 0.01f, p), new Vector3(ArenaSize, 0.02f, width), line, grid);

                if (axis || Mathf.Abs(p) >= half) continue;
                Sign($"x {p:+0;-0}", new Vector3(p, 1.1f, 2.5f), 0.32f, labels);
                Sign($"z {p:+0;-0}", new Vector3(2.5f, 1.1f, p), 0.32f, labels);
            }

            Sign(HintSign, new Vector3(-20f, 3.2f, 0f), 0.45f, labels);
        }

        static void Line(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true;
        }

        /// <summary>글자는 시험장 중앙(조립대)에서 바라볼 때 읽히는 방향으로 세운다.</summary>
        static void Sign(string text, Vector3 position, float size, Transform parent)
        {
            var t = Child(text, parent);
            t.position = position;
            var away = new Vector3(position.x, 0f, position.z);
            t.rotation = away.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(away.normalized, Vector3.up)
                : Quaternion.identity;

            var mesh = t.gameObject.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 72;
            mesh.characterSize = size * 0.28f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = LabelColor;
        }

        static void BuildLighting(Transform root)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.78f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            var sun = Child("Sun", root).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }

        /// <summary>에디터에서 씬을 열었을 때 보이는 카메라. 실행하면 SpaceBootstrap 이 끈다.</summary>
        static void PreviewCamera(Transform root)
        {
            var camera = new GameObject("Editor preview camera").AddComponent<Camera>();
            camera.transform.SetParent(root, false);
            camera.transform.position = new Vector3(-48f, 26f, -48f);
            camera.transform.LookAt(new Vector3(0f, 1f, 0f));
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 900f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.035f, 0.08f);
        }

        // ── 에셋 ─────────────────────────────────────────────────────────────

        static Material GroundMaterial()
        {
            var texture = CheckerTexture();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CheatArenaGround" };
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one * (ArenaSize / (CheckerCell * 2f)));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.08f);
            return SaveAsset(material, "CheatArenaGround.mat");
        }

        static Material UnlitMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            material.SetColor("_BaseColor", color);
            return SaveAsset(material, name + ".mat");
        }

        /// <summary>2 × 2 체커 한 장. 타일링으로 5m 칸을 만든다.</summary>
        static Texture2D CheckerTexture()
        {
            var path = AssetDir + "/CheatArenaChecker.png";
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    texture.SetPixel(x, y, ((x / (size / 2) + y / (size / 2)) % 2 == 0) ? GroundA : GroundB);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static PhysicsMaterial GroundPhysics()
        {
            // Prim.GroundPhysics 와 같은 값. 그쪽은 런타임 메모리 객체라 씬에 저장할 수 없어 에셋으로 둔다.
            var material = new PhysicsMaterial("CheatArenaGround")
            {
                dynamicFriction = 0.85f,
                staticFriction = 0.95f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
            };
            return SaveAsset(material, "CheatArenaGround.physicMaterial");
        }

        /// <summary>같은 경로에 이미 있으면 내용만 덮어써 GUID 를 지킨다 — 씬 참조가 끊기지 않는다.</summary>
        static T SaveAsset<T>(T asset, string file) where T : Object
        {
            var path = AssetDir + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
