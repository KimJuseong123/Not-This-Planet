using System.IO;
using System.Text;
using Metagame.UI;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>Jira S15P21B202-185 태블릿 FBX를 프리팹으로 구성하고 222 회귀를 방지한다.</summary>
    public static class Stage1TabletAsset
    {
        const string ModelPath = "Assets/_Project/Models/Props/Stage1/Tablet/SF_Stage1_Tablet.fbx";
        const string TextureDirectory = "Assets/_Project/Textures/Props/Stage1/Tablet";
        const string MaterialDirectory = "Assets/_Project/Materials/Props/Stage1/Tablet";
        const string PrefabDirectory = "Assets/_Project/Prefabs/Resources";
        const string MaterialPath = MaterialDirectory + "/M_Stage1_Tablet.mat";
        const string ScreenMaterialPath = MaterialDirectory + "/M_Stage1_Tablet_Screen.mat";
        const string PrefabPath = PrefabDirectory + "/PF_Stage1_Tablet.prefab";
        const float TargetWidth = 0.36f;

        [MenuItem("Tools/둥실둥실 우주 대탈출/1Stage 태블릿 프리팹 재생성", false, 10)]
        public static void Build()
        {
            EnsureDirectory(MaterialDirectory);
            EnsureDirectory(PrefabDirectory);
            ConfigureImporters();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new System.InvalidOperationException("태블릿 FBX를 불러오지 못했습니다: " + ModelPath);

            var bodyMaterial = CreateBodyMaterial();
            var screenMaterial = CreateScreenMaterial();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Bounds importedBounds;
            if (existing == null)
            {
                var root = BuildPrefabRoot(model, bodyMaterial, screenMaterial, out importedBounds);
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            else
            {
                // 기존 프리팹을 새 임시 루트로 덮어쓰면 Unity의 local file ID와
                // 사용자가 승인한 휴대 자세까지 매번 바뀐다. 내용을 제자리에서
                // 갱신해 메뉴를 반복 실행해도 같은 프리팹이 남게 한다.
                var root = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    importedBounds = ConfigureExistingPrefab(root, bodyMaterial, screenMaterial);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Stage1TabletAsset] 태블릿 프리팹 갱신 완료: " + PrefabPath
                      + " | FBX bounds=" + importedBounds.size);
        }

        public static void BuildBatch()
        {
            Build();
        }

        public static void ReportBatch()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new System.InvalidOperationException("태블릿 FBX를 불러오지 못했습니다: " + ModelPath);

            var instance = Object.Instantiate(model);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                var bounds = CombinedBounds(renderers, instance.transform.position);
                var report = new StringBuilder();
                report.AppendLine("[Stage1TabletAsset] Unity import report");
                report.AppendLine("Bounds center=" + bounds.center + " size=" + bounds.size);
                report.AppendLine("Renderers=" + renderers.Length);
                foreach (var renderer in renderers)
                {
                    var mesh = renderer is SkinnedMeshRenderer skinned
                        ? skinned.sharedMesh
                        : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    report.Append(renderer.transform.name)
                        .Append(" | ").Append(renderer.GetType().Name)
                        .Append(" | mesh=").Append(mesh != null ? mesh.name : "null")
                        .Append(" | vertices=").Append(mesh != null ? mesh.vertexCount : 0)
                        .Append(" | bounds=").Append(renderer.bounds.size)
                        .AppendLine();
                }
                Debug.Log(report.ToString());
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>그래픽 장치가 있는 배치 실행에서 프리팹의 정면 미리보기를 저장한다.</summary>
        public static void RenderPreviewBatch()
        {
            Build();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("TabletPreviewCamera");
            var lightObject = new GameObject("TabletPreviewLight");
            var renderTexture = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(960, 720, TextureFormat.RGBA32, false);
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                var view = instance.GetComponent<Stage1TabletView>();
                if (view != null) view.enabled = false;

                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.015f, 0.02f, 0.04f);
                camera.fieldOfView = 42f;
                camera.transform.position = new Vector3(0f, 0f, -0.78f);
                camera.transform.LookAt(Vector3.zero);
                camera.targetTexture = renderTexture;

                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2.2f;
                light.color = new Color(0.86f, 0.91f, 1f);
                light.transform.rotation = Quaternion.Euler(32f, -28f, 0f);

                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                var output = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../../tablet-prefab-preview.png"));
                File.WriteAllBytes(output, texture.EncodeToPNG());
                Debug.Log("[Stage1TabletAsset] preview=" + output);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                renderTexture.Release();
                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(instance);
            }
        }

        static GameObject BuildPrefabRoot(GameObject model, Material bodyMaterial,
                                          Material screenMaterial, out Bounds importedBounds)
        {
            var root = new GameObject("PF_Stage1_Tablet");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "SF_Stage1_Tablet";
            visual.transform.SetParent(root.transform, false);

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            importedBounds = CombinedBounds(renderers, visual.transform.position);
            var scale = TargetWidth / Mathf.Max(0.001f, importedBounds.size.x);
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = -importedBounds.center * scale;
            foreach (var renderer in renderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (var i = 0; i < materials.Length; i++) materials[i] = bodyMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var deviceHeight = importedBounds.size.y * scale;
            var deviceDepth = importedBounds.size.z * scale;
            var screenSize = Stage1TabletView.ApprovedScreenSize;
            var screenAnchor = Marker(root.transform, "ScreenAnchor",
                Stage1TabletView.ApprovedScreenAnchorLocalPosition,
                Quaternion.Euler(Stage1TabletView.ApprovedScreenAnchorLocalEuler));

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "DynamicScreenSurface";
            screen.transform.SetParent(screenAnchor, false);
            screen.transform.localPosition = Vector3.zero;
            screen.transform.localRotation = Quaternion.identity;
            screen.transform.localScale = new Vector3(screenSize.x, screenSize.y, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.GetComponent<MeshRenderer>().sharedMaterial = screenMaterial;

            Marker(root.transform, "HandGrip_Left",
                new Vector3(-TargetWidth * 0.39f, -deviceHeight * 0.26f, 0f));
            Marker(root.transform, "HandGrip_Right",
                new Vector3(TargetWidth * 0.39f, -deviceHeight * 0.26f, 0f));
            Marker(root.transform, "BackMount", new Vector3(0f, 0f, deviceDepth * 0.5f));

            var view = root.AddComponent<Stage1TabletView>();
            view.ConfigurePrefab(screenAnchor, screenSize);
            return root;
        }

        static Bounds ConfigureExistingPrefab(GameObject root, Material bodyMaterial,
                                               Material screenMaterial)
        {
            var visual = root.transform.Find("SF_Stage1_Tablet");
            if (visual == null)
                throw new System.InvalidOperationException("태블릿 프리팹에서 SF_Stage1_Tablet를 찾지 못했습니다.");

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (var i = 0; i < materials.Length; i++) materials[i] = bodyMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            var screenAnchor = root.transform.Find("ScreenAnchor");
            if (screenAnchor == null)
                screenAnchor = Marker(root.transform, "ScreenAnchor",
                    Stage1TabletView.ApprovedScreenAnchorLocalPosition,
                    Quaternion.Euler(Stage1TabletView.ApprovedScreenAnchorLocalEuler));

            screenAnchor.SetLocalPositionAndRotation(
                Stage1TabletView.ApprovedScreenAnchorLocalPosition,
                Quaternion.Euler(Stage1TabletView.ApprovedScreenAnchorLocalEuler));

            var screen = screenAnchor.Find("DynamicScreenSurface");
            if (screen == null)
            {
                var screenObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                screenObject.name = "DynamicScreenSurface";
                screenObject.transform.SetParent(screenAnchor, false);
                Object.DestroyImmediate(screenObject.GetComponent<Collider>());
                screen = screenObject.transform;
            }

            screen.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            screen.localScale = new Vector3(Stage1TabletView.ApprovedScreenSize.x,
                                            Stage1TabletView.ApprovedScreenSize.y, 1f);
            screen.GetComponent<MeshRenderer>().sharedMaterial = screenMaterial;

            var view = root.GetComponent<Stage1TabletView>();
            if (view == null) view = root.AddComponent<Stage1TabletView>();
            view.ConfigurePrefab(screenAnchor, Stage1TabletView.ApprovedScreenSize);
            return CombinedBounds(renderers, visual.position);
        }

        static Material CreateBodyMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Lit 셰이더가 없습니다.");
            var material = LoadOrCreateMaterial(MaterialPath, shader);
            material.SetTexture("_BaseMap", LoadTexture("T_Stage1_Tablet_BaseColor.jpg"));
            material.SetTexture("_BumpMap", LoadTexture("T_Stage1_Tablet_Normal.jpg"));
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Metallic", 0.26f);
            material.SetFloat("_Smoothness", 0.58f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material CreateScreenMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP Unlit 셰이더가 없습니다.");
            var material = LoadOrCreateMaterial(ScreenMaterialPath, shader);
            material.SetColor("_BaseColor", new Color(0.025f, 0.07f, 0.10f, 1f));
            material.SetColor("_Color", new Color(0.025f, 0.07f, 0.10f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.shader = shader;
                return material;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Texture2D LoadTexture(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDirectory + "/" + fileName);
        }

        static void ConfigureImporters()
        {
            if (AssetImporter.GetAtPath(ModelPath) is ModelImporter modelImporter)
            {
                modelImporter.importCameras = false;
                modelImporter.importLights = false;
                modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
                modelImporter.SaveAndReimport();
            }

            ConfigureTexture("T_Stage1_Tablet_BaseColor.jpg", false, true);
            ConfigureTexture("T_Stage1_Tablet_Normal.jpg", true, false);
            ConfigureTexture("T_Stage1_Tablet_Metallic.jpg", false, false);
            ConfigureTexture("T_Stage1_Tablet_RM.jpg", false, false);
            ConfigureTexture("T_Stage1_Tablet_Roughness.jpg", false, false);
        }

        static void ConfigureTexture(string fileName, bool normalMap, bool sRgb)
        {
            var path = TextureDirectory + "/" + fileName;
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = sRgb;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        static Bounds CombinedBounds(Renderer[] renderers, Vector3 fallbackCenter)
        {
            if (renderers == null || renderers.Length == 0)
                return new Bounds(fallbackCenter, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Transform Marker(Transform parent, string name, Vector3 localPosition,
                                Quaternion? localRotation = null)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = localPosition;
            marker.localRotation = localRotation ?? Quaternion.identity;
            return marker;
        }

        static void EnsureDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
