using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// <c>Models/Environment/Stage1/</c> 아래 FBX 에 Material 에셋과 배치용 프리팹을 만들어 준다
    /// (자연물 7종 + 운석 4종).
    ///
    /// docs/asset-catalog.md 의 미해결 1번 — "환경 자연물 7종에 Material 에셋과 프리팹이 없다" —
    /// 를 해소하는 도구다. 텍스처가 <c>.fbm/</c> 임베드뿐이라 FBX 를 씬에 끌면 재질을
    /// 편집할 수 없고, 프리팹이 없어 카탈로그의 첫 번째 규칙(FBX 를 씬에 직접 끌지 않는다)도
    /// 지킬 수 없었다.
    ///
    /// 하는 일은 두 가지다.
    ///   1. Material 을 만들고 <c>.fbm</c> 텍스처(BaseColor·Normal·Metallic)를 물린 뒤, ModelImporter 의
    ///      외부 오브젝트 맵으로 FBX 임베드 재질을 이 Material 로 갈아끼운다(재질 추출).
    ///      셰이더는 지금 켜져 있는 파이프라인을 보고 고른다 — <see cref="SurfaceShader"/> 참고.
    ///   2. 그 FBX 를 감싸는 <c>PF_Stage1_*</c> 프리팹을 만든다.
    ///
    /// 이미 있는 에셋은 건드리지 않는다. 손으로 콜라이더를 붙여 둔 프리팹을 다시 돌렸다고
    /// 날려 버리면 안 되기 때문이다. 다시 만들려면 해당 파일을 지우고 실행한다.
    ///
    /// Stage1 우주선 파츠(<c>SF_Stage1_*</c>)와 달리 이쪽 FBX 는 Y-up 이라
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
        /// <c>Models/Environment/Stage1/</c> 기준 상대 경로(확장자 없이) → <c>PF_Stage1_</c>·<c>M_Stage1_</c>
        /// 뒤에 붙일 이름.
        ///
        /// FBX 는 Tripo3D 산출물 이름(소문자 snake_case)을 그대로 두고, 새로 만드는
        /// Material·프리팹만 프로젝트 명명 규칙인 PascalCase 로 맞춘다.
        /// 경로에 하위 폴더가 있으면 Material·프리팹도 같은 하위 폴더에 만든다.
        /// </summary>
        public static readonly (string Path, string Name)[] Models =
        {
            ("planet",                "Planet"),
            ("modular_cliff_wall",    "CliffWall"),
            ("rock_pillars",          "RockPillars"),
            ("alien_mushroom_tree",   "MushroomTree"),
            ("mossy_boulder_cluster", "BoulderCluster"),
            ("alien_flower_cluster",  "FlowerCluster"),
            ("glowing_spiral_plant",  "SpiralPlant"),

            // 운석 4종. 자연물과 달리 BaseColor·Metallic·Normal·Roughness 4장 세트를 갖는다.
            ("Meteors/meteor_main", "MeteorMain"),
            ("Meteors/meteor_debris_large", "MeteorDebrisLarge"),
            ("Meteors/meteor_debris_medium", "MeteorDebrisMedium"),
            ("Meteors/meteor_debris_small", "MeteorDebrisSmall"),
        };

        /// <summary>텍스처 확장자가 세트 안에서도 섞여 있다 (`.jpg` 와 `.jpeg`). 있는 것을 찾아 쓴다.</summary>
        static readonly string[] TextureExtensions = { ".jpg", ".jpeg", ".png" };

        /// <summary>이름으로 프리팹 경로를 찾는다. 배치하는 쪽(<see cref="Stage1SkinBuilder"/>)이 쓴다.</summary>
        public static string PrefabPath(string name)
        {
            foreach (var (path, entry) in Models)
                if (entry == name) return PrefabDir + SubDir(path) + "/PF_Stage1_" + name + ".prefab";

            return PrefabDir + "/PF_Stage1_" + name + ".prefab";
        }

        static string PrefabPathOf(string path, string name) { return PrefabDir + SubDir(path) + "/PF_Stage1_" + name + ".prefab"; }
        static string MaterialPath(string path, string name) { return MaterialDir + SubDir(path) + "/M_Stage1_" + name + ".mat"; }
        static string FbxPath(string path) { return ModelDir + "/" + path + ".fbx"; }
        static string TextureDir(string path) { return ModelDir + "/" + path + ".fbm"; }

        /// <summary>경로의 폴더 부분("Meteors/x" → "/Meteors"). 없으면 빈 문자열.</summary>
        static string SubDir(string path)
        {
            int cut = path.LastIndexOf('/');
            return cut < 0 ? string.Empty : "/" + path.Substring(0, cut);
        }

        /// <summary>경로의 파일 이름 부분("Meteors/x" → "x"). 텍스처 이름의 접두사다.</summary>
        static string StemOf(string path)
        {
            int cut = path.LastIndexOf('/');
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        /// <summary><c>&lt;모델&gt;.fbm/&lt;모델&gt;_&lt;suffix&gt;.&lt;확장자&gt;</c> 를 찾는다. 없으면 null.</summary>
        static Texture2D FindTexture(string path, string suffix)
        {
            string stem = TextureDir(path) + "/" + StemOf(path) + "_" + suffix;
            foreach (var extension in TextureExtensions)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + extension);
                if (texture != null) return texture;
            }
            return null;
        }

        public static void Build()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();

            var made = 0;
            foreach (var (path, name) in Models)
            {
                Directory.CreateDirectory(MaterialDir + SubDir(path));
                Directory.CreateDirectory(PrefabDir + SubDir(path));

                var material = EnsureMaterial(path, name);
                if (RemapFbxMaterial(path, material)) made++;
                if (EnsurePrefab(path, name)) made++;
            }

            var colliders = Stage1MeshColliderSetup.ApplyKnownPrefabColliders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1Env] 환경 자연물 에셋 점검 완료 — 새로 만든 것 " + made
                      + "건, MeshCollider 연결 " + colliders + "개, 모델 " + Models.Length + "종");
        }

        /// <summary>
        /// Stage1 환경 자산이 사용할 표면 셰이더.
        ///
        /// 프로젝트는 URP를 기준으로 빌드하므로, 에디터의 현재 파이프라인 상태가
        /// URP Lit만 사용한다. 파이프라인 전환 직후에도 Built-in Standard를 새 자산에
        /// 기록하지 않아, Editor 상태와 빌드 상태가 달라지는 문제를 막는다.
        /// </summary>
        static Shader SurfaceShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) Debug.LogError("[Stage1Env] URP Lit 셰이더를 찾지 못했다.");
            return shader;
        }

        /// <summary>
        /// Material 을 만들고 BaseColor 를 물린다.
        ///
        /// 이미 있으면 그대로 두되, 셰이더가 현재 파이프라인과 어긋나 있으면 그것만 고친다.
        /// 손으로 조정한 값을 지우지 않으면서 마젠타 사고는 스스로 복구하게 하려는 것이다.
        /// </summary>
        static Material EnsureMaterial(string model, string name)
        {
            string assetPath = MaterialPath(model, name);
            var shader = SurfaceShader();
            if (shader == null) return null;

            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                if (existing.shader == shader) return existing;

                string was = existing.shader != null ? existing.shader.name : "(없음)";
                Configure(existing, shader, model);
                Debug.Log("[Stage1Env] 셰이더 교체 " + was + " → " + shader.name + " : " + assetPath);
                return existing;
            }

            var material = new Material(shader) { name = "M_Stage1_" + name };
            Configure(material, shader, model);
            AssetDatabase.CreateAsset(material, assetPath);
            Debug.Log("[Stage1Env] Material 생성 → " + assetPath + " (" + shader.name + ")");
            return material;
        }

        /// <summary>
        /// 셰이더와 텍스처 물리기.
        ///
        /// 프로퍼티 이름이 파이프라인마다 다르므로 있는 것만 건드린다
        /// (Built-in 은 <c>_MainTex</c>·<c>_Glossiness</c>, URP 는 <c>_BaseMap</c>·<c>_Smoothness</c>).
        /// 텍스처는 세트에 있는 것만 물린다 — 자연물 7종은 BaseColor 한 장,
        /// 운석 3종은 BaseColor·Metallic·Normal·Roughness 4장이다.
        ///
        /// <b>Roughness 는 물리지 않는다.</b> Built-in Standard 는 smoothness 를 Metallic 맵의
        /// 알파에서만 읽어서 별도 Roughness 텍스처를 꽂을 자리가 없다. 기존 <c>M_Stage1_*</c> 5개도
        /// 같은 이유로 BaseColor·Normal·Metallic 3장만 쓴다 (카탈로그 §11-2).
        /// </summary>
        static void Configure(Material material, Shader shader, string model)
        {
            material.shader = shader;

            var baseColor = FindTexture(model, "basecolor");
            if (baseColor == null)
                Debug.LogWarning("[Stage1Env] BaseColor 텍스처가 없다: " + TextureDir(model));
            else
            {
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseColor);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseColor);
            }

            var normal = FindTexture(model, "normal");
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                EnsureNormalMapImport(normal);
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");        // SetTexture 만으로는 셰이더가 읽지 않는다
            }

            var metallic = FindTexture(model, "metallic");
            if (metallic != null && material.HasProperty("_MetallicGlossMap"))
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.EnableKeyword("_METALLICGLOSSMAP");

                // 금속 맵이 있으면 smoothness 는 그 알파에서 온다. JPEG 는 알파가 없어 1(거울)로
                // 읽히므로 배율을 0 으로 눌러 둔다. 기존 M_Stage1_* 5개와 같은 값이다.
                if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 0f);
            }
            else if (material.HasProperty("_Metallic"))
            {
                // 금속 맵이 없는 자연물은 비금속으로 둔다.
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// 노멀맵으로 임포트돼 있는지 확인하고 아니면 고친다.
        /// 컬러 텍스처를 <c>_BumpMap</c> 에 꽂으면 Unity 가 경고를 띄우고 결과도 틀린다.
        /// </summary>
        static void EnsureNormalMapImport(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
            Debug.Log("[Stage1Env] 노멀맵으로 임포트 설정 변경 → " + path);
        }

        /// <summary>
        /// FBX 임베드 재질을 방금 만든 Material 로 갈아끼운다.
        ///
        /// 프리팹에서 재질을 덮어쓰지 않고 임포터 쪽에서 바꾼다. 그래야 이 FBX 를 어디에
        /// 인스턴스화하든 같은 재질이 따라오고, 프리팹 오버라이드가 쌓이지 않는다.
        /// </summary>
        static bool RemapFbxMaterial(string model, Material material)
        {
            if (material == null) return false;

            string path = FbxPath(model);
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
            Debug.Log("[Stage1Env] " + model + " 재질 연결 → " + material.name + " (임베드 " + names.Count + "개)");
            return true;
        }

        /// <summary>FBX 를 감싸는 배치용 프리팹. 이미 있으면 그대로 쓴다.</summary>
        static bool EnsurePrefab(string model, string name)
        {
            string path = PrefabPathOf(model, name);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return false;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath(model));
            if (asset == null)
            {
                Debug.LogWarning("[Stage1Env] FBX 를 찾지 못했다: " + FbxPath(model));
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
