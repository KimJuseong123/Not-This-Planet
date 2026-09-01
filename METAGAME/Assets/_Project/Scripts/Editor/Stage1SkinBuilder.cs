using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Main 씬에 1장 아트 에셋을 입힌다.
    ///
    /// 퍼즐은 <see cref="Stage1PuzzleBuilder"/> 가 프리미티브로 세워 둔 상태고,
    /// 아트 에셋(docs/asset-catalog.md)은 만들어졌지만 씬에서 쓰이지 않았다.
    /// 그 카탈로그의 미해결 3번 — "Main.unity 에 아트 에셋이 하나도 배치되지 않았다" — 을 메운다.
    ///
    /// 규칙 두 가지를 지킨다.
    ///   · 물리·게임플레이는 건드리지 않는다. 콜라이더·Rigidbody·스크립트·좌표는 그대로 두고
    ///     프리미티브의 MeshRenderer 만 끈 뒤 그 자리에 모델을 자식으로 얹는다.
    ///     그래서 이 도구를 되돌려도(스킨 자식만 지우면) 퍼즐이 그대로 돈다.
    ///   · 카탈로그 규칙대로 FBX 가 아니라 <c>PF_*</c> 프리팹만 인스턴스화한다.
    ///
    /// 파츠 콜라이더는 <see cref="PartSpec"/> 가 파츠마다 실측 형태로 갖고 있으므로,
    /// 모델을 배율 1 로 얹고 바닥만 맞추면 둘이 저절로 일치한다.
    ///
    /// 예전에는 네 파츠가 모두 3 × 3.7 × 3 m 상자를 써서, 모델을 균일 배율로 그 안에 넣은 뒤
    /// 세로로 최대 1.35배까지 늘렸다. 납작한 HoverPod 은 그래도 박스 높이를 못 채워 위에 쌓인
    /// 파츠와 사이가 벌어졌다. 그때 이 주석은 "파츠별 높이를 따로 두는 것이 정답이지만 물리
    /// 기준값을 바꾸는 일이라 아트 작업에서 하지 않는다" 고 적어 두었는데,
    /// S15P21B202-166 이 그 물리 기준값을 바꾸어 해결했다.
    ///
    /// 이 도구가 소유하는 것은 이름이 <see cref="SkinName"/> 인 자식들과
    /// Environment 의 Boundaries · Nature · DistantBackground 그룹 내용이다.
    /// 다시 실행하면 그것만 지우고 새로 만든다. 나머지는 손대지 않는다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.Stage1SkinBuilder.Apply
    /// </summary>
    public static class Stage1SkinBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>이 도구가 만든 스킨 자식의 이름. 다시 실행할 때 이 이름만 지운다.</summary>
        const string SkinName = "Visual";

        const string SpaceshipDir = "Assets/_Project/Prefabs/Spaceships/Stage1";
        const string PropDir = "Assets/_Project/Prefabs/Props/Stage1";

        /// <summary>
        /// 스킨 모델을 콜라이더에 맞추려고 배율을 걸지 않는다.
        ///
        /// 예전에는 네 파츠가 모두 3 x 3.7 x 3m 상자를 쓰는 바람에, 모델을 그 안에 채우려고
        /// 균일 배율을 건 뒤 세로로 최대 1.35배까지 더 늘렸다. 기획(S15P21B202-107 §7.1)은
        /// 그 방식을 금지하고 실제 에셋 크기를 그대로 쓰라고 정한다.
        ///
        /// 지금은 콜라이더가 <see cref="PartSpec"/> 의 실측 형태를 따르므로 모델을 배율 1 로
        /// 얹으면 둘이 저절로 맞는다. 프리팹 피벗이 바닥에 있어(실측 확인) 바닥만 맞추면 된다.
        /// </summary>
        const float SkinScale = 1f;

        /// <summary>모델 높이와 콜라이더 높이가 이만큼 넘게 벌어지면 실측값을 의심한다.</summary>
        const float SkinHeightTolerance = 0.05f;

        /// <summary>
        /// 파츠별 스킨. 조립 순서는 기획에서 미정이라 시연 순서에 맞춰 정했다.
        /// 시연 드라이버가 Part_1 을 맨 아래, Part_2 를 그 위로 옮긴다.
        /// 아래에서 위로 부양 추진부 → 로봇 → 조종석이고, 견인 장치는 도구 역할이다.
        /// </summary>
        /// <summary>
        /// 파츠에 얹을 프리팹 경로. 어느 파츠에 무엇이 올라가는지는 <see cref="PartSpec"/> 가
        /// 정한다 — 조립 순서와 물리 형태가 한 표에 있어야 서로 어긋나지 않는다.
        /// </summary>
        static string SkinPathOf(PartSpec spec) => SpaceshipDir + "/" + spec.SkinPrefab + ".prefab";

        /// <summary>고정못을 모아 두는 그룹. 파츠와 함께 움직이지 않아야 해서 따로 둔다.</summary>
        const string PinGroupName = "AnchorPins";

        const string AnchorPinPrefab = PropDir + "/PF_Stage1_AnchorPin.prefab";
        const string TractorCraftPrefab = SpaceshipDir + "/PF_Stage1_TractorCraft.prefab";

        // --- 아레나 ---
        /// <summary>지면(ENV_Ground_Arena_50m)이 50 x 50 m 다.</summary>
        const float ArenaHalf = 25f;

        /// <summary>절벽 벽을 세우는 거리. 지면 안쪽에 살짝 들여 세운다.</summary>
        const float WallRing = 23.5f;

        const float WallScale = 1.6f;
        const int WallsPerSide = 8;

        /// <summary>
        /// 절벽 벽에 붙이는 보이지 않는 물리 벽의 높이.
        /// 저중력(-2.8)에서 점프가 높아 모델 높이(약 3.4m)만으로는 넘어간다.
        /// </summary>
        const float WallBlockHeight = 9f;

        /// <summary>배치 난수 씨앗. 운석 스포너와 같은 값을 쓴다 — 결과가 매번 같아야 diff 가 조용하다.</summary>
        const uint Seed = 20260827u;

        /// <summary>자연물 종류: 프리팹 이름, 개수, 서로 떨어질 최소 거리, 크기 흔들기 범위.</summary>
        static readonly (string Name, int Count, float Spacing, float ScaleMin, float ScaleMax)[] NatureKinds =
        {
            ("RockPillars",    8, 6.0f, 0.85f, 1.25f),
            ("MushroomTree",  10, 5.0f, 0.80f, 1.30f),
            ("BoulderCluster", 12, 4.0f, 0.85f, 1.30f),
            ("FlowerCluster",  20, 2.5f, 0.80f, 1.40f),
            ("SpiralPlant",    16, 2.5f, 0.80f, 1.40f),
        };

        /// <summary>
        /// 아레나 지면 재질. 기본 흰색 재질이라 해(1.75)를 받으면 화면이 하얗게 날아간다.
        /// 바위 텍스처를 타일링해 레골리스 색으로 눌러 쓴다.
        /// </summary>
        const string GroundMaterialPath =
            "Assets/_Project/Materials/Environment/Stage1/M_Stage1_ArenaGround.mat";

        const float GroundTiling = 10f;
        static readonly Color GroundTint = new(0.46f, 0.45f, 0.43f);

        /// <summary>
        /// 아레나 지면을 살짝 띄우는 높이.
        ///
        /// 런타임 <c>LevelBuilder</c> 가 예전 SpaceFlop 코스를 같은 좌표에 세우는데(카탈로그 §11-9),
        /// 그 착륙장 슬랩이 y=0, 크레이터 바닥이 y≈0.05 라 지면과 같은 평면에서 z-파이팅이 난다.
        /// 6cm 띄우면 아레나 안에서는 Stage1 지면만 보인다. <c>buildLevel</c> 을 끄면 필요 없는 값이다.
        /// </summary>
        const float GroundLift = 0.06f;

        /// <summary>먼 배경. 위치와 지름(m).</summary>
        static readonly (Vector3 Pos, float Diameter)[] Planets =
        {
            (new Vector3(-55f, 95f, 210f), 90f),
            (new Vector3(120f, 130f, -150f), 40f),
        };

        static uint _rng;
        static readonly Dictionary<string, Fit> FitCache = new();

        struct Fit
        {
            public Vector3 Size;
            public Vector3 Center;
        }

        public static void Apply()
        {
            Stage1EnvironmentAssets.Build();          // 환경 Material·프리팹이 없으면 먼저 만든다
            FitCache.Clear();
            _rng = Seed;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var puzzle = FindByPath(scene, "Stage1/Gameplay/Puzzle");
            var environment = FindByPath(scene, "Stage1/Environment");
            if (puzzle == null || environment == null)
            {
                Debug.LogError("[Stage1Skin] Stage1/Gameplay/Puzzle 또는 Stage1/Environment 를 찾지 못했다. 씬 구조를 확인한다.");
                return;
            }

            int parts = SkinParts(puzzle);
            int pins = SkinClamps(puzzle);
            int beams = SkinBeams(puzzle);
            int walls = BuildBoundaries(environment);
            int nature = BuildNature(environment, puzzle, scene);
            int planets = BuildDistantBackground(environment);
            bool ground = SkinGround(environment);
            bool light = TidyLighting(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Stage1Skin] 스킨 완료 — 파츠 {parts}기, 고정못 {pins}개, 견인 장치 {beams}기, "
                      + $"절벽 {walls}개, 자연물 {nature}개, 배경 행성 {planets}개, "
                      + $"지면 재질 {(ground ? "적용" : "실패")}, 템플릿 라이트 {(light ? "끔" : "이미 꺼짐")} → {ScenePath}");
        }

        // ---------------------------------------------------------------- 파츠

        static int SkinParts(Transform puzzle)
        {
            var done = 0;
            foreach (var spec in PartSpec.All)
            {
                var part = puzzle.Find(spec.Name);
                if (part == null) { Debug.LogWarning("[Stage1Skin] " + spec.Name + " 이 없다."); continue; }

                var prefab = Load(SkinPathOf(spec));
                if (prefab == null) continue;

                // 콜라이더 상자로 쓰는 프리미티브 큐브는 렌더러만 끈다. 컴포넌트를 지우지 않는
                // 것은 스킨을 걷어내면 원래 룩으로 돌아갈 수 있게 하려는 것이다.
                foreach (var cube in part.GetComponentsInChildren<MeshRenderer>(true))
                    if (cube.name.StartsWith(Stage1PuzzleBuilder.BodyPrefix, System.StringComparison.Ordinal))
                        cube.enabled = false;

                var fit = Measure(prefab);
                var visual = Attach(part, prefab);
                var world = Vector3.one * SkinScale;
                SetWorldScale(visual, world);

                // x·z 는 파츠 중심에, 바닥은 콜라이더 바닥(= 지면)에 맞춘다.
                SetWorldOffset(visual, new Vector3(
                    -fit.Center.x * world.x,
                    -spec.Height * 0.5f - (fit.Center.y - fit.Size.y * 0.5f) * world.y,
                    -fit.Center.z * world.z));

                float modelHeight = fit.Size.y * world.y;
                float gap = Mathf.Abs(modelHeight - spec.Height);
                if (gap > SkinHeightTolerance)
                    Debug.LogWarning($"[Stage1Skin] {spec.Name} 모델 높이 {modelHeight:0.00}m 와 "
                                     + $"콜라이더 높이 {spec.Height:0.00}m 가 {gap:0.00}m 어긋난다 — "
                                     + "PartSpec 의 실측값을 다시 재야 한다.");

                Debug.Log($"[Stage1Skin] {spec.Name} ({spec.DisplayName}) ← {prefab.name} "
                          + $"높이 {modelHeight:0.00}/{spec.Height:0.00}m · 질량 {spec.Mass:0}kg");
                done++;
            }
            return done;
        }

        /// <summary>
        /// 고정쇠 자리에 고정못 모델을 세운다. 고정쇠는 파츠 밑면 = 지면 높이에 있다.
        ///
        /// 못을 고정쇠(= 파츠의 자식) 밑에 붙이지 않고 따로 모아 세운다. 자식으로 붙이면
        /// 견인 빔이 파츠를 들어 올릴 때 못 16개가 같이 공중으로 따라 올라간다 — 빌드를 실행해
        /// 확인했다. 못은 땅에 박혀 있는 물건이라 파츠가 떠나도 그 자리에 남아야 한다.
        /// 크기는 카탈로그의 제작 크기(0.20 × 0.54 × 0.20 m) 그대로 둔다.
        /// </summary>
        static int SkinClamps(Transform puzzle)
        {
            var group = EnsureGroup(puzzle, PinGroupName);
            Clear(group);

            var prefab = Load(AnchorPinPrefab);
            if (prefab == null) return 0;

            var fit = Measure(prefab);
            var done = 0;

            foreach (var clamp in puzzle.GetComponentsInChildren<PartClamp>(true))
            {
                // 예전 실행이 고정쇠 밑에 붙여 둔 못이 있으면 걷어낸다.
                var stale = clamp.transform.Find(SkinName);
                if (stale != null) Object.DestroyImmediate(stale.gameObject);

                var spot = clamp.transform.position;
                var pin = Attach(group, prefab);
                pin.name = clamp.transform.parent.name + "_" + clamp.name;
                pin.position = new Vector3(
                    spot.x - fit.Center.x,
                    -(fit.Center.y - fit.Size.y * 0.5f),                  // 못 바닥을 지면에
                    spot.z - fit.Center.z);
                clamp.AssignReleasedVisual(pin);
                done++;
            }
            return done;
        }

        /// <summary>견인 빔 기준점에 비행접시형 운반 도구를 띄운다.</summary>
        static int SkinBeams(Transform puzzle)
        {
            var prefab = Load(TractorCraftPrefab);
            if (prefab == null) return 0;

            var fit = Measure(prefab);
            var done = 0;

            foreach (var beam in puzzle.GetComponentsInChildren<TractorBeam>(true))
            {
                var visual = Attach(beam.transform, prefab);

                // 빔 기준점은 아래를 겨누도록 회전해 있다. 도구는 수평으로 떠 있어야 하므로
                // 부모 회전을 따라가지 않게 월드 기준으로 직접 세운다.
                visual.rotation = Quaternion.identity;
                // 빔 루트의 높이는 바닥 기준점이지 모델의 중심 높이가 아니다. 중심을
                // 루트에 맞추면 실제 비행접시 높이의 절반이 지면 아래로 내려간다.
                var bottomOffset = fit.Center.y - fit.Size.y * 0.5f;
                visual.position = new Vector3(
                    beam.transform.position.x - fit.Center.x,
                    TransportVehicle.GroundY - bottomOffset,
                    beam.transform.position.z - fit.Center.z);
                done++;
            }
            return done;
        }

        // ---------------------------------------------------------------- 환경

        /// <summary>아레나를 둘러싸는 절벽 벽. 보이지 않는 물리 벽을 함께 붙여 경계로 쓴다.</summary>
        static int BuildBoundaries(Transform environment)
        {
            var group = EnsureGroup(environment, "Boundaries");
            Clear(group);

            var prefab = Load(Stage1EnvironmentAssets.PrefabPath("CliffWall"));
            if (prefab == null) return 0;

            var fit = Measure(prefab);
            float spacing = ArenaHalf * 2f / WallsPerSide;
            float bottom = fit.Center.y - fit.Size.y * 0.5f;
            var done = 0;

            for (var side = 0; side < 4; side++)
            {
                for (var i = 0; i < WallsPerSide; i++)
                {
                    float t = -ArenaHalf + spacing * (i + 0.5f);

                    // 안쪽을 바라보게 세운다.
                    Vector3 spot;
                    float yaw;
                    switch (side)
                    {
                        case 0: spot = new Vector3(t, 0f, WallRing); yaw = 180f; break;
                        case 1: spot = new Vector3(t, 0f, -WallRing); yaw = 0f; break;
                        case 2: spot = new Vector3(WallRing, 0f, t); yaw = 270f; break;
                        default: spot = new Vector3(-WallRing, 0f, t); yaw = 90f; break;
                    }

                    var wall = Attach(group, prefab);
                    wall.name = $"CliffWall_{side}_{i}";
                    wall.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    wall.localScale = Vector3.one * WallScale;

                    var offset = wall.localRotation * new Vector3(fit.Center.x, 0f, fit.Center.z) * WallScale;
                    wall.localPosition = new Vector3(spot.x - offset.x, -bottom * WallScale, spot.z - offset.z);

                    // 모델은 3.4m 남짓이라 저중력 점프로 넘어간다. 콜라이더만 위로 올려 막는다.
                    var box = wall.gameObject.AddComponent<BoxCollider>();
                    float local = WallBlockHeight / WallScale;
                    box.size = new Vector3(fit.Size.x, local, fit.Size.z);
                    box.center = new Vector3(fit.Center.x, bottom + local * 0.5f, fit.Center.z);

                    done++;
                }
            }

            return done;
        }

        /// <summary>
        /// 자연물을 아레나 바닥에 흩는다. 작업 공간(파츠 자리 · 조립 위치 · 스폰)은 비워 둔다.
        /// 콜라이더는 붙이지 않는다 — 장식이고, 물리를 늘리면 운석·파츠와 얽힌다.
        /// </summary>
        static int BuildNature(Transform environment, Transform puzzle, UnityEngine.SceneManagement.Scene scene)
        {
            var group = EnsureGroup(environment, "Nature");
            Clear(group);

            var keepOut = CollectKeepOut(puzzle, scene);
            var placed = new List<(Vector2 Spot, float Radius)>();
            float limit = WallRing - 2f;
            var done = 0;

            foreach (var (name, count, spacing, scaleMin, scaleMax) in NatureKinds)
            {
                var prefab = Load(Stage1EnvironmentAssets.PrefabPath(name));
                if (prefab == null) continue;

                var fit = Measure(prefab);
                float bottom = fit.Center.y - fit.Size.y * 0.5f;

                for (var i = 0; i < count; i++)
                {
                    if (!FindSpot(limit, spacing, keepOut, placed, out var spot)) break;
                    placed.Add((spot, spacing * 0.5f));

                    float scale = Mathf.Lerp(scaleMin, scaleMax, Rand01());
                    var prop = Attach(group, prefab);
                    prop.name = $"{name}_{i}";
                    prop.localRotation = Quaternion.Euler(0f, Rand01() * 360f, 0f);
                    prop.localScale = Vector3.one * scale;

                    var offset = prop.localRotation * new Vector3(fit.Center.x, 0f, fit.Center.z) * scale;
                    prop.localPosition = new Vector3(spot.x - offset.x, -bottom * scale, spot.y - offset.z);
                    done++;
                }
            }

            return done;
        }

        /// <summary>하늘의 먼 행성. 별밭 껍데기(반지름 300) 안쪽에 둔다.</summary>
        static int BuildDistantBackground(Transform environment)
        {
            var group = EnsureGroup(environment, "DistantBackground");
            Clear(group);

            var prefab = Load(Stage1EnvironmentAssets.PrefabPath("Planet"));
            if (prefab == null) return 0;

            var fit = Measure(prefab);
            float diameter = Mathf.Max(fit.Size.x, Mathf.Max(fit.Size.y, fit.Size.z));
            var done = 0;

            for (var i = 0; i < Planets.Length; i++)
            {
                var (pos, wanted) = Planets[i];
                float scale = wanted / diameter;

                var planet = Attach(group, prefab);
                planet.name = $"Planet_{i}";
                planet.localScale = Vector3.one * scale;
                planet.localPosition = pos - fit.Center * scale;
                planet.localRotation = Quaternion.Euler(0f, Rand01() * 360f, Rand01() * 20f);

                // 배경이다. 그림자를 주고받게 두면 계산만 늘고 보이지도 않는다.
                foreach (var renderer in planet.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                done++;
            }

            return done;
        }

        // ---------------------------------------------------------------- 지면·조명

        /// <summary>
        /// 아레나 지면에 레골리스 재질을 물리고 z-파이팅을 피해 살짝 띄운다.
        ///
        /// 지면은 프리미티브 Plane 에 기본 흰색 재질이 붙어 있었다. 알베도가 1.0 백색이라
        /// 해를 받으면 그대로 날아가 화면이 눈부시다. 빌드를 실행해 확인했다.
        /// </summary>
        static bool SkinGround(Transform environment)
        {
            var ground = environment.Find("Ground/ENV_Ground_Arena_50m");
            if (ground == null)
            {
                Debug.LogWarning("[Stage1Skin] Environment/Ground/ENV_Ground_Arena_50m 을 찾지 못했다.");
                return false;
            }

            var material = EnsureGroundMaterial();
            if (material == null) return false;

            var renderer = ground.GetComponent<MeshRenderer>();
            if (renderer == null) return false;
            renderer.sharedMaterial = material;

            var local = ground.localPosition;
            ground.localPosition = new Vector3(local.x, GroundLift, local.z);
            return true;
        }

        static Material EnsureGroundMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (existing != null)
            {
                var currentShader = Shader.Find("Universal Render Pipeline/Lit");
                if (currentShader != null && existing.shader != currentShader)
                    existing.shader = currentShader;
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[Stage1Skin] URP Lit 셰이더를 찾지 못했다.");
                return null;
            }

            var material = new Material(shader) { name = "M_Stage1_ArenaGround" };

            // 바위 군락의 BaseColor 를 그대로 빌려 쓴다. 지면 전용 텍스처는 아직 없다.
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/_Project/Models/Environment/Stage1/mossy_boulder_cluster.fbm/mossy_boulder_cluster_basecolor.jpg");
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
                    material.SetTextureScale("_BaseMap", new Vector2(GroundTiling, GroundTiling));
                }
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                    material.SetTextureScale("_MainTex", new Vector2(GroundTiling, GroundTiling));
                }
            }

            if (material.HasProperty("_Color")) material.SetColor("_Color", GroundTint);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", GroundTint);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.08f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);

            AssetDatabase.CreateAsset(material, GroundMaterialPath);
            Debug.Log("[Stage1Skin] 지면 재질 생성 → " + GroundMaterialPath);
            return material;
        }

        /// <summary>
        /// Unity 템플릿이 남긴 씬 Directional Light 를 끈다.
        ///
        /// 런타임 조명은 <c>Vacuum</c> 이 소유한다 — 해(강도 1.75, Soft 그림자)와 행성 반사광을
        /// 코드가 직접 세운다. <c>SpaceBootstrap</c> 은 씬 카메라만 끄고 라이트는 끄지 않으므로
        /// 템플릿 라이트(강도 2.0, Soft 그림자)가 함께 켜져 있었다. 결과가 두 가지였다.
        ///   · 빛이 약 두 배가 되어 지면이 하얗게 날아간다
        ///   · 그림자를 굽는 방향성 광원이 둘인데 강도가 2.0 대 1.75 로 비슷해, 프레임마다
        ///     주광원 선택이 뒤집히면서 그림자가 깜빡인다
        /// 둘 다 빌드를 실행해 확인했다. 오브젝트는 남겨 두고 Light 컴포넌트만 꺼서 되돌리기 쉽게 한다.
        /// </summary>
        static bool TidyLighting(UnityEngine.SceneManagement.Scene scene)
        {
            var holder = FindByPath(scene, "Stage1/Lighting/Directional Light");
            if (holder == null) return false;

            var light = holder.GetComponent<Light>();
            if (light == null || !light.enabled) return false;

            light.enabled = false;
            return true;
        }

        // ---------------------------------------------------------------- 배치 보조

        /// <summary>비워 둘 자리 — 파츠가 박혀 있는 곳, 조립 위치, 플레이어 스폰.</summary>
        static List<(Vector2 Spot, float Radius)> CollectKeepOut(
            Transform puzzle, UnityEngine.SceneManagement.Scene scene)
        {
            var list = new List<(Vector2, float)>();

            // 파츠마다 발자국이 다르므로 명세에서 읽는다. 명세가 없으면 예전 기본값을 쓴다.
            foreach (var part in puzzle.GetComponentsInChildren<SpaceshipPart>(true))
            {
                var spec = PartSpec.Find(part.name);
                var footprint = spec != null
                    ? Mathf.Max(spec.Footprint.x, spec.Footprint.y)
                    : PhysicsScale.PartWidth;

                list.Add((Flat(part.transform.position), footprint + 4f));
            }

            var assembly = puzzle.Find("AssemblyPoint");
            // 발사 구역 반지름과 같은 값이어야 한다. 비워 두는 범위가 판정 범위보다
            // 좁으면 발사 구역 안에 지형이 들어온다.
            if (assembly != null) list.Add((Flat(assembly.position), PhysicsScale.LaunchPadRadius));

            var spawn = FindByPath(scene, "Stage1/Gameplay/SpawnPoints/PlayerSpawn");
            if (spawn != null) list.Add((Flat(spawn.position), 6f));

            return list;
        }

        static bool FindSpot(float limit, float spacing,
            List<(Vector2 Spot, float Radius)> keepOut,
            List<(Vector2 Spot, float Radius)> placed,
            out Vector2 spot)
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                spot = new Vector2(
                    Mathf.Lerp(-limit, limit, Rand01()),
                    Mathf.Lerp(-limit, limit, Rand01()));

                var ok = true;
                foreach (var (center, radius) in keepOut)
                    if (Vector2.Distance(spot, center) < radius) { ok = false; break; }

                if (ok)
                    foreach (var (center, radius) in placed)
                        if (Vector2.Distance(spot, center) < radius + spacing * 0.5f) { ok = false; break; }

                if (ok) return true;
            }

            spot = Vector2.zero;
            Debug.LogWarning("[Stage1Skin] 자연물을 놓을 자리를 찾지 못했다. 개수나 간격을 줄인다.");
            return false;
        }

        static Vector2 Flat(Vector3 v) { return new Vector2(v.x, v.z); }

        /// <summary>결정론적 난수. 배치가 매번 같아야 씬 diff 가 조용하다.</summary>
        static float Rand01()
        {
            _rng = _rng * 1664525u + 1013904223u;
            return (_rng >> 8) * (1f / 16777216f);
        }

        // ---------------------------------------------------------------- 프리팹·트랜스폼

        static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning("[Stage1Skin] 프리팹을 찾지 못했다: " + path);
            return prefab;
        }

        /// <summary>프리팹을 host 의 자식으로 붙인다. 이미 있는 스킨 자식은 지운다.</summary>
        static Transform Attach(Transform host, GameObject prefab)
        {
            var stale = host.Find(SkinName);
            if (stale != null) Object.DestroyImmediate(stale.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = SkinName;
            instance.transform.SetParent(host, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance.transform;
        }

        /// <summary>부모의 비균일 스케일을 상쇄한다. 파츠 박스가 3 x 3.7 x 3 이라 그냥 붙이면 찌그러진다.</summary>
        static void SetWorldScale(Transform t, Vector3 world)
        {
            var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(world.x / p.x, world.y / p.y, world.z / p.z);
        }

        /// <summary>부모 중심에서의 월드 거리로 위치를 준다(부모 회전이 없는 경우에만 쓴다).</summary>
        static void SetWorldOffset(Transform t, Vector3 world)
        {
            var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localPosition = new Vector3(world.x / p.x, world.y / p.y, world.z / p.z);
        }

        /// <summary>프리팹의 실제 크기와 중심. 배율 1·회전 없음으로 한 번 세워서 렌더러 경계를 잰다.</summary>
        static Fit Measure(GameObject prefab)
        {
            string key = AssetDatabase.GetAssetPath(prefab);
            if (FitCache.TryGetValue(key, out var cached)) return cached;

            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;

            var renderers = probe.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            var first = true;
            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            Object.DestroyImmediate(probe);

            if (first)
            {
                Debug.LogWarning("[Stage1Skin] 렌더러가 없어 크기를 재지 못했다: " + key);
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }

            var fit = new Fit { Size = bounds.size, Center = bounds.center };
            FitCache[key] = fit;
            return fit;
        }

        static Transform EnsureGroup(Transform parent, string name)
        {
            var group = parent.Find(name);
            if (group != null) return group;

            var made = new GameObject(name).transform;
            made.SetParent(parent, false);
            return made;
        }

        static void Clear(Transform group)
        {
            for (var i = group.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(group.GetChild(i).gameObject);
        }

        static Transform FindByPath(UnityEngine.SceneManagement.Scene scene, string path)
        {
            var parts = path.Split('/');

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;

                var current = root.transform;
                for (var i = 1; i < parts.Length && current != null; i++)
                    current = current.Find(parts[i]);

                return current;
            }

            return null;
        }
    }
}
