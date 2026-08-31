using System.IO;
using Metagame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 실제 모델이 준비되기 전까지 사용할 Stage1 용접 플라즈마 건을 만든다.
    ///
    /// 프리팹은 프로젝트의 장비 규칙에 맞춰 동적 Rigidbody와 Grabbable을 갖는다.
    /// 시각물은 PlaceholderVisual 아래에 모아 두고, 교체 모델을 붙일 기준점은
    /// AttachPoints 아래에 따로 둔다.
    /// </summary>
    public static class Stage1WeldingPlasmaGunAsset
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabDirectory = "Assets/_Project/Prefabs/Props/Stage1";
        const string PrefabPath = PrefabDirectory + "/PF_Stage1_WeldingPlasmaGun.prefab";
        const string MaterialDirectory = "Assets/_Project/Materials/Props/Stage1";
        const string EquipmentPath = "Stage1/Gameplay/Puzzle/Equipment";
        const string InstanceName = "WeldingPlasmaGun_Stage1";

        static readonly Vector3 FallbackPosition = new(-11.5f, 0.42f, -8.4f);

        [MenuItem("Tools/둥실둥실 우주 대탈출/임시 용접 플라즈마 건 만들고 배치", false, 8)]
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
            if (existing == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = InstanceName;
                instance.transform.SetParent(equipment, false);

                var part = puzzle.Find("Part_1");
                var position = part != null
                    ? part.position + new Vector3(3.5f, -1.43f, -0.4f)
                    : FallbackPosition;
                var lookTarget = part != null ? part.position : position + Vector3.forward;
                var direction = lookTarget - position;
                direction.y = 0f;
                var rotation = direction.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : Quaternion.identity;

                instance.transform.SetPositionAndRotation(position, rotation);
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("[Stage1WeldingGun] Main 씬에 임시 용접 플라즈마 건 배치 → " + position);
            }
            else
            {
                Debug.Log("[Stage1WeldingGun] 기존 배치를 유지한다: " + EquipmentPath + "/" + InstanceName);
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1WeldingGun] 완료 — " + PrefabPath);
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
            if (material == null)
            {
                var shader = FindSurfaceShader();
                if (shader == null) return null;

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
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
            return Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
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
                return EnsureToolComponent(existing);
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
