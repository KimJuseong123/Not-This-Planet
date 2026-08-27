using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 환경 자연물 FBX 7종에 Material 에셋과 배치용 프리팹을 만들어 준다.
    ///
    /// docs/asset-catalog.md 의 미해결 1번 — "환경 자연물 7종에 Material 에셋과 프리팹이 없다" —
    /// 를 해소하는 도구다. 텍스처가 <c>.fbm/</c> 임베드뿐이라 FBX 를 씬에 끌면 재질을
    /// 편집할 수 없고, 프리팹이 없어 카탈로그의 첫 번째 규칙(FBX 를 씬에 직접 끌지 않는다)도
    /// 지킬 수 없었다.
    ///
    /// 하는 일은 두 가지다.
    ///   1. Material 을 만들고 <c>.fbm</c> BaseColor 를 물린 뒤, ModelImporter 의
    ///      외부 오브젝트 맵으로 FBX 임베드 재질을 이 Material 로 갈아끼운다(재질 추출).
    ///      셰이더는 지금 켜져 있는 파이프라인을 보고 고른다 — <see cref="SurfaceShader"/> 참고.
    ///   2. 그 FBX 를 감싸는 <c>PF_Stage1_*</c> 프리팹을 만든다.
    ///
    /// 이미 있는 에셋은 건드리지 않는다. 손으로 콜라이더를 붙여 둔 프리팹을 다시 돌렸다고
    /// 날려 버리면 안 되기 때문이다. 다시 만들려면 해당 파일을 지우고 실행한다.
    ///
    /// Stage1 우주선 파츠(<c>SF_Stage1_*</c>)와 달리 이 7종은 FBX 가 Y-up 이라
    /// 축 변환용 자식이 필요 없다. 실제 크기는 <c>.meta</c> 의 globalScale 이 이미 잡아 뒀다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath . -executeMethod Metagame.EditorTools.Stage1EnvironmentAssets.Build
    /// </summary>
    public static class Stage1EnvironmentAssets
    {
        const string ModelDir = "Assets/_Project/Models/Environment/Stage1";
        const string MaterialDir = "Assets/_Project/Materials/Environment/Stage1";
        const string PrefabDir = "Assets/_Project/Prefabs/Environment/Stage1";

        /// <summary>
        /// FBX 파일명 → <c>PF_Stage1_</c>·<c>M_Stage1_</c> 뒤에 붙일 이름.
        ///
        /// FBX 는 Tripo3D 산출물 이름(소문자 snake_case)을 그대로 두고, 새로 만드는
        /// Material·프리팹만 프로젝트 명명 규칙인 PascalCase 로 맞춘다.
        /// </summary>
        public static readonly (string Fbx, string Name)[] Models =
        {
            ("planet",                "Planet"),
            ("modular_cliff_wall",    "CliffWall"),
            ("rock_pillars",          "RockPillars"),
            ("alien_mushroom_tree",   "MushroomTree"),
            ("mossy_boulder_cluster", "BoulderCluster"),
            ("alien_flower_cluster",  "FlowerCluster"),
            ("glowing_spiral_plant",  "SpiralPlant"),
        };

        public static string PrefabPath(string name) { return PrefabDir + "/PF_Stage1_" + name + ".prefab"; }
        static string MaterialPath(string name) { return MaterialDir + "/M_Stage1_" + name + ".mat"; }
        static string FbxPath(string fbx) { return ModelDir + "/" + fbx + ".fbx"; }
        static string TexturePath(string fbx) { return ModelDir + "/" + fbx + ".fbm/" + fbx + "_basecolor.jpg"; }

        [MenuItem("Tools/둥실둥실 우주 대탈출/환경 자연물 Material·프리팹 만들기", false, 6)]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();

            var made = 0;
            foreach (var (fbx, name) in Models)
            {
                var material = EnsureMaterial(fbx, name);
                if (RemapFbxMaterial(fbx, material)) made++;
                if (EnsurePrefab(fbx, name)) made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1Env] 환경 자연물 에셋 점검 완료 — 새로 만든 것 " + made + "건, 모델 " + Models.Length + "종");
        }

        /// <summary>
        /// 지금 켜져 있는 렌더 파이프라인의 표준 표면 셰이더.
        ///
        /// 이 프로젝트는 URP 패키지가 들어 있지만 <c>ProjectSettings/GraphicsSettings.asset</c> 의
        /// <c>m_CustomRenderPipeline</c> 이 비어 있어 실제로는 Built-in 파이프라인으로 돈다.
        /// 그래서 기존 <c>M_Stage1_*</c> 5개도 Standard 를 쓴다. 여기서 URP Lit 을 쓰면
        /// 에디터에서는 멀쩡해 보이고 빌드한 플레이어에서 전부 마젠타로 나온다.
        /// 파이프라인을 보고 고르므로 나중에 URP 로 전환해도 이 도구는 따라간다.
        /// </summary>
        static Shader SurfaceShader()
        {
            bool scriptable = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            string wanted = scriptable ? "Universal Render Pipeline/Lit" : "Standard";

            var shader = Shader.Find(wanted);
            if (shader == null) Debug.LogError("[Stage1Env] 셰이더를 찾지 못했다: " + wanted);
            return shader;
        }

        /// <summary>
        /// Material 을 만들고 BaseColor 를 물린다.
        ///
        /// 이미 있으면 그대로 두되, 셰이더가 현재 파이프라인과 어긋나 있으면 그것만 고친다.
        /// 손으로 조정한 값을 지우지 않으면서 마젠타 사고는 스스로 복구하게 하려는 것이다.
        /// </summary>
        static Material EnsureMaterial(string fbx, string name)
        {
            string path = MaterialPath(name);
            var shader = SurfaceShader();
            if (shader == null) return null;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(fbx));
            if (texture == null)
                Debug.LogWarning("[Stage1Env] BaseColor 텍스처가 없다: " + TexturePath(fbx));

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader == shader) return existing;

                string was = existing.shader != null ? existing.shader.name : "(없음)";
                Configure(existing, shader, texture);
                Debug.Log("[Stage1Env] 셰이더 교체 " + was + " → " + shader.name + " : " + path);
                return existing;
            }

            var material = new Material(shader) { name = "M_Stage1_" + name };
            Configure(material, shader, texture);
            AssetDatabase.CreateAsset(material, path);
            Debug.Log("[Stage1Env] Material 생성 → " + path + " (" + shader.name + ")");
            return material;
        }

        /// <summary>
        /// 셰이더와 값 물리기. 프로퍼티 이름이 파이프라인마다 다르므로 있는 것만 건드린다
        /// (Built-in 은 <c>_MainTex</c>·<c>_Glossiness</c>, URP 는 <c>_BaseMap</c>·<c>_Smoothness</c>).
        /// </summary>
        static void Configure(Material material, Shader shader, Texture2D texture)
        {
            material.shader = shader;

            if (texture != null)
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            }

            // Tripo3D 산출물은 BaseColor 한 장뿐이다. 금속 맵이 없으니 자연물답게 비금속·저광택으로 둔다.
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// FBX 임베드 재질을 방금 만든 Material 로 갈아끼운다.
        ///
        /// 프리팹에서 재질을 덮어쓰지 않고 임포터 쪽에서 바꾼다. 그래야 이 FBX 를 어디에
        /// 인스턴스화하든 같은 재질이 따라오고, 프리팹 오버라이드가 쌓이지 않는다.
        /// </summary>
        static bool RemapFbxMaterial(string fbx, Material material)
        {
            if (material == null) return false;

            string path = FbxPath(fbx);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[Stage1Env] FBX 임포터를 찾지 못했다: " + path);
                return false;
            }

            foreach (var pair in importer.GetExternalObjectMap())
                if (pair.Key.type == typeof(Material) && pair.Value == material)
                    return false;                     // 이미 연결돼 있다

            var names = new System.Collections.Generic.List<string>();
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is Material embedded && !names.Contains(embedded.name))
                    names.Add(embedded.name);

            if (names.Count == 0)
            {
                Debug.LogWarning("[Stage1Env] 임베드 재질을 찾지 못해 연결을 건너뛴다: " + path);
                return false;
            }

            foreach (var embeddedName in names)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embeddedName), material);

            importer.SaveAndReimport();
            Debug.Log("[Stage1Env] " + fbx + " 재질 연결 → " + material.name + " (임베드 " + names.Count + "개)");
            return true;
        }

        /// <summary>FBX 를 감싸는 배치용 프리팹. 이미 있으면 그대로 쓴다.</summary>
        static bool EnsurePrefab(string fbx, string name)
        {
            string path = PrefabPath(name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return false;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(fbx));
            if (asset == null)
            {
                Debug.LogWarning("[Stage1Env] FBX 를 찾지 못했다: " + FbxPath(fbx));
                return false;
            }

            var root = new GameObject("PF_Stage1_" + name);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log("[Stage1Env] 프리팹 생성 → " + path);
            return true;
        }
    }
}
