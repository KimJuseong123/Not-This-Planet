using System.Collections.Generic;
using System.IO;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// Stage1 용접 플라즈마 건 프리팹과 실제 모델을 구성한다.
    ///
    /// 프리팹은 프로젝트의 장비 규칙에 맞춰 동적 Rigidbody와 Grabbable을 갖는다.
    /// PlaceholderVisual의 콜라이더는 간단한 물리 외곽선으로 유지하고 렌더러만 끈다.
    /// 실제 모델은 ReplacementModel 아래에 배치해 손·총구 기준점과 독립적으로 교체한다.
    /// </summary>
    public static class Stage1WeldingPlasmaGunAsset
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabDirectory = "Assets/_Project/Prefabs/Props/Stage1";
        const string PrefabPath = PrefabDirectory + "/PF_Stage1_WeldingPlasmaGun.prefab";
        const string ModelPath =
            "Assets/_Project/Models/Props/Stage1/PlasmaWeldingGun/SF_Stage1_PlasmaWeldingGun.fbx";
        const string MaterialDirectory = "Assets/_Project/Materials/Props/Stage1";
        const string ProductionMaterialDirectory = MaterialDirectory + "/PlasmaWeldingGun";
        const string ProductionTextureDirectory =
            "Assets/_Project/Textures/Props/Stage1/PlasmaWeldingGun";
        const string EquipmentPath = "Stage1/Gameplay/Puzzle/Equipment";
        const string InstanceName = "WeldingPlasmaGun_Stage1";

        const float SurfaceGap = 0.02f;
        static readonly Vector3 FallbackPosition =
            new(-4f, TransportVehicle.GroundY, -2f);
        static readonly Vector3 ProductionModelPosition = new(0f, -0.5f, 0.25f);
        // 원본 FBX의 꽃잎 총구는 -X 방향이다. 실제 총구를 Tool 규약의
        // +Z 조준 방향에 맞춰야 1인칭에서 뒤집혀 보이지 않는다.
        static readonly Quaternion ProductionModelRotation =
            Quaternion.FromToRotation(Vector3.left, Vector3.forward);

        public static void Build()
        {
            Directory.CreateDirectory(PrefabDirectory);
            Directory.CreateDirectory(MaterialDirectory);
            AssetDatabase.Refresh();

            var materials = CreateMaterials();
            var prefab = EnsurePrefab(materials);
            if (prefab == null)
            {
                Debug.LogError("[Stage1WeldingGun] 프리팹을 만들지 못했다: " + PrefabPath);
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var puzzle = FindByPath(scene, "Stage1/Gameplay/Puzzle");
            if (puzzle == null)
            {
                Debug.LogError("[Stage1WeldingGun] Stage1/Gameplay/Puzzle 를 찾지 못했다.");
                return;
            }

            var equipment = puzzle.Find("Equipment");
            if (equipment == null)
            {
                equipment = new GameObject("Equipment").transform;
                equipment.SetParent(puzzle, false);
            }

            var existing = equipment.Find(InstanceName);
            Transform instanceTransform = existing;
            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = InstanceName;
                instance.transform.SetParent(equipment, false);

                var part = puzzle.Find("Part_1");
                var position = part != null
                    ? new Vector3(part.position.x + 3.5f, TransportVehicle.GroundY,
                                  part.position.z - 0.4f)
                    : FallbackPosition;
                var lookTarget = part != null ? part.position : position + Vector3.forward;
                var direction = lookTarget - position;
                direction.y = 0f;
                var rotation = direction.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : Quaternion.identity;

                instance.transform.SetPositionAndRotation(position, rotation);
                instanceTransform = instance.transform;
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("[Stage1WeldingGun] Main 씬에 임시 용접 플라즈마 건 배치 → " + position);
            }
            else
            {
                Debug.Log("[Stage1WeldingGun] 기존 배치를 유지한다: " + EquipmentPath + "/" + InstanceName);
            }

            AlignSceneInstanceToSurface(instanceTransform);
            EditorSceneManager.MarkSceneDirty(scene);

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1WeldingGun] 완료 — " + PrefabPath);
        }

        [MenuItem("Tools/둥실둥실 우주 대탈출/플라즈마 용접기 실제 모델 적용", false, 9)]
        public static void ApplyProductionModel()
        {
            Directory.CreateDirectory(ProductionMaterialDirectory);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport |
                                                  ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError("[Stage1WeldingGun] 실제 모델을 찾지 못했다: " + ModelPath);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[Stage1WeldingGun] 대상 프리팹을 찾지 못했다: " + PrefabPath);
                return;
            }

            var productionMaterials = CreateProductionMaterials();

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ApplyProductionModel(root, model, productionMaterials);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AlignMainSceneInstanceToSurface();
            Debug.Log("[Stage1WeldingGun] 실제 모델과 힌지 애니메이션 적용 완료 — " + PrefabPath);
        }

        static void AlignMainSceneInstanceToSurface()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var instance = FindByPath(scene, EquipmentPath + "/" + InstanceName);
            if (instance == null) return;

            AlignSceneInstanceToSurface(instance);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// 최종 외형과 실제 물리 콜라이더를 함께 재서 바닥면에 맞춘다.
        /// 고정된 음수 Y 오프셋은 생산 모델이 바뀔 때마다 메시나 콜라이더를 바닥 아래로
        /// 보내므로, 현재 인스턴스의 최저 경계를 기준으로 보정한다.
        /// </summary>
        static void AlignSceneInstanceToSurface(Transform instance)
        {
            if (instance == null) return;

            Bounds bounds = default;
            var first = true;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer)
                    continue;

                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled) continue;

                if (first) { bounds = collider.bounds; first = false; }
                else bounds.Encapsulate(collider.bounds);
            }

            if (first) return;

            var delta = TransportVehicle.GroundY + SurfaceGap - bounds.min.y;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return;

            instance.position += Vector3.up * delta;
            Physics.SyncTransforms();
        }

        static GunMaterials CreateMaterials()
        {
            return new GunMaterials(
                EnsureMaterial("M_Stage1_WeldingPlasmaGun_Body", new Color(0.86f, 0.25f, 0.07f), 0.35f, 0.32f),
                EnsureMaterial("M_Stage1_WeldingPlasmaGun_Grip", new Color(0.055f, 0.07f, 0.09f), 0.05f, 0.2f),
                EnsureMaterial("M_Stage1_WeldingPlasmaGun_Metal", new Color(0.25f, 0.31f, 0.38f), 0.8f, 0.58f),
                EnsureEmissionMaterial("M_Stage1_WeldingPlasmaGun_Energy", new Color(0.08f, 0.75f, 1f), 5f));
        }

        static Material EnsureMaterial(string name, Color color, float metallic, float smoothness)
        {
            var path = MaterialDirectory + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = FindSurfaceShader();
            if (shader == null) return null;
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            SetColor(material, color);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureEmissionMaterial(string name, Color color, float intensity)
        {
            var material = EnsureMaterial(name, color, 0.15f, 0.3f);
            if (material == null) return null;

            var emission = color * intensity;
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Shader FindSurfaceShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit");
        }

        static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static GameObject EnsurePrefab(GunMaterials materials)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var ensured = EnsureToolComponent(existing);
                ApplyProductionModel();
                return ensured;
            }
            if (materials.Body == null || materials.Grip == null || materials.Metal == null || materials.Energy == null)
                return null;

            var root = new GameObject("PF_Stage1_WeldingPlasmaGun") { layer = GameLayers.Prop };

            var body = root.AddComponent<Rigidbody>();
            body.mass = 4f;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grabbable = Grabbable.Mark(root, "플라즈마 용접기 (임시)", false, 1f, 2f,
                                           GrabbableRole.Tool);
            grabbable.CanGrab = true;

            var visual = new GameObject("PlaceholderVisual") { layer = GameLayers.Prop };
            visual.transform.SetParent(root.transform, false);

            Primitive(visual.transform, "MainHousing", PrimitiveType.Cube,
                new Vector3(0f, 0.08f, -0.04f), new Vector3(0.28f, 0.24f, 0.48f),
                Quaternion.identity, materials.Body);
            Primitive(visual.transform, "RearHousing", PrimitiveType.Cylinder,
                new Vector3(0f, 0.08f, -0.34f), new Vector3(0.16f, 0.15f, 0.16f),
                Quaternion.identity, materials.Metal);
            Primitive(visual.transform, "PowerCell", PrimitiveType.Cylinder,
                new Vector3(0f, 0.22f, -0.23f), new Vector3(0.08f, 0.12f, 0.08f),
                Quaternion.identity, materials.Energy);
            Primitive(visual.transform, "Grip", PrimitiveType.Cube,
                new Vector3(0f, -0.19f, -0.07f), new Vector3(0.16f, 0.34f, 0.18f),
                Quaternion.Euler(-12f, 0f, 0f), materials.Grip);
            Primitive(visual.transform, "TriggerGuard", PrimitiveType.Cube,
                new Vector3(0f, -0.015f, 0.16f), new Vector3(0.13f, 0.035f, 0.12f),
                Quaternion.identity, materials.Metal);
            Primitive(visual.transform, "Barrel", PrimitiveType.Cylinder,
                new Vector3(0f, 0.08f, 0.39f), new Vector3(0.075f, 0.27f, 0.075f),
                Quaternion.Euler(90f, 0f, 0f), materials.Metal);
            Primitive(visual.transform, "Nozzle", PrimitiveType.Cylinder,
                new Vector3(0f, 0.08f, 0.68f), new Vector3(0.12f, 0.09f, 0.12f),
                Quaternion.Euler(90f, 0f, 0f), materials.Body);
            Primitive(visual.transform, "EnergyTip", PrimitiveType.Sphere,
                new Vector3(0f, 0.08f, 0.79f), new Vector3(0.075f, 0.075f, 0.075f),
                Quaternion.identity, materials.Energy);

            var anchors = new GameObject("AttachPoints") { layer = GameLayers.Prop };
            anchors.transform.SetParent(root.transform, false);
            Empty(anchors.transform, "HandGrip", new Vector3(0f, -0.21f, -0.04f), Quaternion.Euler(-12f, 0f, 0f));
            var muzzle = Empty(anchors.transform, "Muzzle", new Vector3(0f, 0.08f, 0.84f), Quaternion.identity);
            Empty(anchors.transform, "VFXPoint", new Vector3(0f, 0.08f, 0.82f), Quaternion.identity);
            Empty(anchors.transform, "ReplacementModel", Vector3.zero, Quaternion.identity);

            var plasma = root.AddComponent<PlasmaWeldingTool>();
            AssignMuzzle(plasma, muzzle);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        static GameObject EnsureToolComponent(GameObject existing)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var plasma = root.GetComponent<PlasmaWeldingTool>();
                if (plasma == null) plasma = root.AddComponent<PlasmaWeldingTool>();

                var muzzle = root.transform.Find("AttachPoints/Muzzle");
                AssignMuzzle(plasma, muzzle);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        static void ApplyProductionModel(GameObject root, GameObject model,
                                         IReadOnlyList<Material> productionMaterials)
        {
            if (root == null || model == null) return;

            var placeholder = root.transform.Find("PlaceholderVisual");
            if (placeholder != null)
                foreach (var renderer in placeholder.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;

            var anchors = root.transform.Find("AttachPoints");
            if (anchors == null)
            {
                anchors = new GameObject("AttachPoints") { layer = GameLayers.Prop }.transform;
                anchors.SetParent(root.transform, false);
            }

            var replacement = anchors.Find("ReplacementModel");
            if (replacement == null)
            {
                replacement = new GameObject("ReplacementModel") { layer = GameLayers.Prop }.transform;
                replacement.SetParent(anchors, false);
            }

            while (replacement.childCount > 0)
                Object.DestroyImmediate(replacement.GetChild(0).gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "SF_Stage1_PlasmaWeldingGun";
            instance.transform.SetParent(replacement, false);
            instance.transform.SetLocalPositionAndRotation(ProductionModelPosition,
                                                            ProductionModelRotation);
            instance.transform.localScale = Vector3.one;
            instance.layer = GameLayers.Prop;
            AssignProductionMaterials(instance, productionMaterials);

            var petalAnimator = replacement.GetComponent<PlasmaGunPetalAnimator>();
            if (petalAnimator == null)
                petalAnimator = replacement.gameObject.AddComponent<PlasmaGunPetalAnimator>();
            petalAnimator.Rebind();
            petalAnimator.SnapToMode(PlasmaBeamMode.None);

            var grabbable = root.GetComponent<Grabbable>();
            if (grabbable != null) grabbable.Label = "플라즈마 용접기";

            var plasma = root.GetComponent<PlasmaWeldingTool>();
            if (plasma == null) plasma = root.AddComponent<PlasmaWeldingTool>();
            AssignMuzzle(plasma, anchors.Find("Muzzle"));
        }

        static Material[] CreateProductionMaterials()
        {
            const int materialCount = 31;
            var materials = new Material[materialCount];
            // 이 프로젝트의 GraphicsSettings 는 Built-in Render Pipeline을 사용한다.
            // URP 패키지가 설치되어 있다는 이유만으로 URP/Lit을 먼저 선택하면
            // Built-in 카메라에서 모든 실제 모델 재질이 분홍색으로 렌더링된다.
            var shader = FindSurfaceShader();
            if (shader == null)
            {
                Debug.LogError("[Stage1WeldingGun] 실제 모델용 Lit 셰이더를 찾지 못했다.");
                return materials;
            }

            for (var i = 0; i < materialCount; i++)
            {
                var suffix = i.ToString("00");
                var baseColorPath = ProductionTextureDirectory +
                                    "/T_Stage1_PlasmaGun_Part" + suffix + "_BaseColor.jpg";
                var normalPath = ProductionTextureDirectory +
                                 "/T_Stage1_PlasmaGun_Part" + suffix + "_Normal.jpg";
                ConfigureNormalTexture(normalPath);

                var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath);
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                if (baseColor == null || normal == null)
                {
                    Debug.LogError("[Stage1WeldingGun] Part " + suffix + " 텍스처가 누락됐다.");
                    continue;
                }

                var path = ProductionMaterialDirectory +
                           "/M_Stage1_PlasmaGun_Part" + suffix + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = "M_Stage1_PlasmaGun_Part" + suffix };
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader != shader)
                {
                    material.shader = shader;
                }

                SetTexture(material, "_BaseMap", baseColor);
                SetTexture(material, "_MainTex", baseColor);
                SetTexture(material, "_BumpMap", normal);
                SetFloat(material, "_BumpScale", 1f);
                SetFloat(material, "_Metallic", 0.25f);
                SetFloat(material, "_Smoothness", 0.5f);
                SetFloat(material, "_Glossiness", 0.5f);
                material.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            return materials;
        }

        static void ConfigureNormalTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        static void AssignProductionMaterials(GameObject instance,
                                              IReadOnlyList<Material> productionMaterials)
        {
            if (instance == null || productionMaterials == null) return;

            const string importedPrefix = "Material_tripo_part_";
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var assigned = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < assigned.Length; i++)
                {
                    var imported = assigned[i];
                    if (imported == null || !imported.name.StartsWith(importedPrefix)) continue;
                    if (!int.TryParse(imported.name.Substring(importedPrefix.Length), out var part)) continue;
                    if (part < 0 || part >= productionMaterials.Count || productionMaterials[part] == null) continue;

                    assigned[i] = productionMaterials[part];
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = assigned;
            }
        }

        static void SetTexture(Material material, string property, Texture texture)
        {
            if (material != null && material.HasProperty(property))
                material.SetTexture(property, texture);
        }

        static void AssignMuzzle(PlasmaWeldingTool tool, Transform muzzle)
        {
            if (tool == null || muzzle == null) return;

            var serialized = new SerializedObject(tool);
            var property = serialized.FindProperty("muzzle");
            if (property == null) return;

            property.objectReferenceValue = muzzle;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Primitive(Transform parent, string name, PrimitiveType type,
                                    Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = GameLayers.Prop;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return go;
        }

        static Transform Empty(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name) { layer = GameLayers.Prop };
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            return go.transform;
        }

        static Transform FindByPath(Scene scene, string path)
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

        readonly struct GunMaterials
        {
            public readonly Material Body;
            public readonly Material Grip;
            public readonly Material Metal;
            public readonly Material Energy;

            public GunMaterials(Material body, Material grip, Material metal, Material energy)
            {
                Body = body;
                Grip = grip;
                Metal = metal;
                Energy = energy;
            }
        }
    }
}
