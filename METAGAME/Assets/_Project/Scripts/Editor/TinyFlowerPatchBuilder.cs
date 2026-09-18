using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    public sealed class TinyFlowerPatchBuilder : EditorWindow
    {
        private const string Root = "Assets/_Project/Art/Stage1Concept/TinyFlowerPatch";
        private const string MeshPath = Root + "/Meshes/TinyFlowerPatch.asset";
        private const string TexturePath = Root + "/Textures/TinyFlowerPalette.asset";
        private const string MaterialPath = Root + "/Materials/TinyFlowerPatch.mat";
        private const string PrefabPath = Root + "/Prefabs/PF_TinyFlowerPatch.prefab";

        [SerializeField, Min(1)] private int flowerCount = 48;
        [SerializeField, Min(0.1f)] private float patchRadius = 2.6f;
        [SerializeField] private Vector2 flowerSize = new Vector2(0.055f, 0.1f);
        [SerializeField, Min(0)] private int randomSeed = 583;
        [SerializeField] private bool placeInCurrentScene = true;

        [MenuItem("Tools/Not This Planet/Vegetation/작은 꽃밭 만들기", false, 20)]
        private static void Open() => GetWindow<TinyFlowerPatchBuilder>("작은 꽃밭");

        [MenuItem("Tools/Not This Planet/Vegetation/기본 작은 꽃밭 바로 생성", false, 21)]
        public static void BuildDefault()
        {
            var window = CreateInstance<TinyFlowerPatchBuilder>();
            window.placeInCurrentScene = false;
            window.Build();
            DestroyImmediate(window);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("작은 꽃밭 · Single Batched Mesh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "작은 꽃 여러 송이를 하나의 MeshRenderer와 하나의 Material로 합칩니다.\n" +
                "현재 선택한 오브젝트 위치에 배치되며, 선택이 없으면 원점에 생성됩니다.",
                MessageType.Info);

            flowerCount = EditorGUILayout.IntSlider("꽃 개수", flowerCount, 1, 200);
            patchRadius = EditorGUILayout.Slider("꽃밭 반지름", patchRadius, 0.2f, 12f);
            flowerSize = EditorGUILayout.Vector2Field("꽃 크기 범위", flowerSize);
            randomSeed = EditorGUILayout.IntField("랜덤 시드", randomSeed);
            placeInCurrentScene = EditorGUILayout.Toggle("현재 씬에 배치", placeInCurrentScene);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("꽃밭 에셋 생성", GUILayout.Height(36))) Build();
        }

        private void Build()
        {
            flowerCount = Mathf.Max(1, flowerCount);
            patchRadius = Mathf.Max(0.1f, patchRadius);
            flowerSize.x = Mathf.Max(0.02f, flowerSize.x);
            flowerSize.y = Mathf.Max(flowerSize.x, flowerSize.y);

            EnsureFolders();
            Texture2D palette = CreateOrReplacePalette();
            Material material = CreateOrUpdateMaterial(palette);
            Mesh mesh = CreateFlowerPatchMesh();
            SaveOrReplaceMesh(mesh);

            var root = new GameObject("Tiny flowers · single batched mesh");
            root.isStatic = true;
            root.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            root.AddComponent<MeshRenderer>().sharedMaterial = material;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            DestroyImmediate(root);

            if (placeInCurrentScene)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (Selection.activeTransform != null)
                {
                    instance.transform.SetParent(Selection.activeTransform, false);
                    instance.transform.localPosition = Vector3.zero;
                }
                else
                {
                    instance.transform.position = Vector3.zero;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Create tiny flower patch");
                Selection.activeGameObject = instance;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[TinyFlowerPatch] 생성 완료: {PrefabPath} ({flowerCount} flowers, one mesh/material)");
        }

        private Mesh CreateFlowerPatchMesh()
        {
            var data = new MeshData();
            var random = new System.Random(randomSeed);
            var placedPositions = new List<Vector3>();

            for (int i = 0; i < flowerCount; i++)
            {
                float size = Next(random, flowerSize.x, flowerSize.y);
                Vector3 basePosition = FindSeparatedPosition(random, placedPositions, size);
                placedPositions.Add(basePosition);
                float yaw = Next(random, 0f, 360f);
                int flowerColor = 2 + random.Next(0, 3);
                AddGroundFlower(data, basePosition, size, yaw, flowerColor);
            }

            var mesh = new Mesh { name = "TinyFlowerPatch" };
            mesh.SetVertices(data.vertices);
            mesh.SetNormals(data.normals);
            mesh.SetUVs(0, data.uvs);
            mesh.SetTriangles(data.triangles, 0, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        private Vector3 FindSeparatedPosition(System.Random random, List<Vector3> occupied, float size)
        {
            Vector3 candidate = Vector3.zero;
            float minimumGap = Mathf.Max(flowerSize.y * 2.35f, size * 2.35f);

            for (int attempt = 0; attempt < 80; attempt++)
            {
                float angle = Next(random, 0f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(Next(random, 0f, 1f)) * patchRadius;
                candidate = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                bool overlaps = false;
                for (int i = 0; i < occupied.Count; i++)
                {
                    if ((occupied[i] - candidate).sqrMagnitude < minimumGap * minimumGap)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps) return candidate;
            }

            return candidate;
        }

        private static void AddGroundFlower(MeshData d, Vector3 groundPosition, float size, float yaw, int color)
        {
            Quaternion flowerRotation = Quaternion.Euler(0f, yaw, 0f);
            float stemHeight = size * 0.18f;
            Vector3 center = AddCurvedStem(d, groundPosition, stemHeight, size * 0.105f, yaw, Vector3.zero);
            const int petalCount = 5;

            for (int petal = 0; petal < petalCount; petal++)
            {
                float angle = petal * Mathf.PI * 2f / petalCount;
                Vector3 localDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 direction = flowerRotation * localDirection;
                Vector3 petalCenter = center + direction * size * 0.56f;
                Quaternion petalRotation = flowerRotation * Quaternion.LookRotation(localDirection, Vector3.up);

                AddRoundedPetal(d, petalCenter, petalRotation,
                    size * 0.29f, size * 0.4f, size * 0.22f, color);
            }

            AddRoundedPetal(d, center + Vector3.up * size * 0.12f, flowerRotation,
                size * 0.26f, size * 0.26f, size * 0.18f, 6);
        }

        private static void AddRoundedPetal(MeshData d, Vector3 center, Quaternion rotation,
            float halfWidth, float halfLength, float domeHeight, int palette)
        {
            const int segments = 10;
            int start = d.vertices.Count;
            Vector2 uv = PaletteUv(palette);
            Vector3 up = rotation * Vector3.up;
            float rimHeight = domeHeight * 0.28f;

            // Top center: the gentle rise makes the petal read as a soft cushion.
            d.vertices.Add(center + up * domeHeight);
            d.normals.Add(up);
            d.uvs.Add(uv);

            // Inner ring rounds the transition from the crown into the outer rim.
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 local = new Vector3(Mathf.Cos(angle) * halfWidth * 0.55f, 0f,
                    Mathf.Sin(angle) * halfLength * 0.55f);
                Vector3 outward = (rotation * local).normalized;
                d.vertices.Add(center + rotation * local + up * domeHeight * 0.78f);
                d.normals.Add((up * 0.9f + outward * 0.1f).normalized);
                d.uvs.Add(uv);
            }

            // Outer top ring.
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 local = new Vector3(Mathf.Cos(angle) * halfWidth, 0f,
                    Mathf.Sin(angle) * halfLength);
                Vector3 outward = (rotation * local).normalized;
                d.vertices.Add(center + rotation * local + up * rimHeight);
                d.normals.Add((up * 0.64f + outward * 0.36f).normalized);
                d.uvs.Add(uv);
            }

            // Inset bottom ring supplies visible thickness without touching neighbouring petals.
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 local = new Vector3(Mathf.Cos(angle) * halfWidth * 0.9f, 0f,
                    Mathf.Sin(angle) * halfLength * 0.9f);
                Vector3 outward = (rotation * local).normalized;
                d.vertices.Add(center + rotation * local);
                d.normals.Add((outward + up * 0.06f).normalized);
                d.uvs.Add(uv);
            }

            for (int i = 0; i < segments; i++)
            {
                int inner = start + 1 + i;
                int innerNext = start + 1 + (i + 1) % segments;
                int outer = start + 1 + segments + i;
                int outerNext = start + 1 + segments + (i + 1) % segments;
                int bottom = start + 1 + segments * 2 + i;
                int bottomNext = start + 1 + segments * 2 + (i + 1) % segments;

                d.triangles.AddRange(new[]
                {
                    start, innerNext, inner,
                    inner, innerNext, outerNext,
                    inner, outerNext, outer,
                    outer, bottom, bottomNext,
                    outer, bottomNext, outerNext
                });
            }
        }

        private static Vector3 AddCurvedStem(MeshData d, Vector3 bottom, float height, float radius, float yaw, Vector3 lean)
        {
            const int sides = 5;
            const int sections = 3;
            for (int section = 0; section < sections; section++)
            {
                float t0 = section / (float)sections;
                float t1 = (section + 1) / (float)sections;
                Vector3 c0 = bottom + Vector3.up * height * t0 + lean * (t0 * t0);
                Vector3 c1 = bottom + Vector3.up * height * t1 + lean * (t1 * t1);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = (yaw + i * 360f / sides) * Mathf.Deg2Rad;
                    float a1 = (yaw + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                    Vector3 n0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    Vector3 n1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    float taper0 = Mathf.Lerp(1f, 0.72f, t0);
                    float taper1 = Mathf.Lerp(1f, 0.72f, t1);
                    AddQuad(d, c0 + n0 * radius * taper0, c0 + n1 * radius * taper0,
                        c1 + n1 * radius * taper1, c1 + n0 * radius * taper1,
                        (n0 + n1).normalized, 0, false);
                }
            }

            return bottom + Vector3.up * height + lean;
        }

        private static void AddRoundedLeaves(MeshData d, Vector3 bottom, Vector3 top, float size, float yaw)
        {
            for (int i = 0; i < 2; i++)
            {
                float a = (yaw + 55f + i * 165f) * Mathf.Deg2Rad;
                Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 stemPoint = Vector3.Lerp(bottom, top, 0.32f + i * 0.2f);
                Vector3 center = stemPoint + side * size * 0.48f + Vector3.up * size * 0.06f;
                Quaternion rotation = Quaternion.LookRotation(side, Vector3.up) * Quaternion.Euler(-12f, 0f, 0f);
                AddEllipsoid(d, center, rotation, new Vector3(size * 0.42f, size * 0.09f, size * 0.72f), 3, 8, 1);
            }
        }

        private static void AddLayeredFlower(MeshData d, Vector3 center, float size, float yaw, int color)
        {
            Quaternion flowerRotation = Quaternion.Euler(NextTilt(yaw), yaw, -NextTilt(yaw * 0.73f));
            const int petalCount = 6;
            for (int petal = 0; petal < petalCount; petal++)
            {
                float angle = petal * Mathf.PI * 2f / petalCount;
                Vector3 localDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 direction = flowerRotation * localDirection;
                Vector3 petalCenter = center + direction * size * 0.42f + flowerRotation * Vector3.up * size * 0.13f;
                Quaternion petalRotation = flowerRotation * Quaternion.LookRotation(localDirection, Vector3.up) * Quaternion.Euler(-22f, 0f, 0f);
                AddEllipsoid(d, petalCenter, petalRotation,
                    new Vector3(size * 0.34f, size * 0.12f, size * 0.62f), 3, 8, color);
            }

            AddEllipsoid(d, center + flowerRotation * Vector3.up * size * 0.2f, flowerRotation,
                new Vector3(size * 0.27f, size * 0.16f, size * 0.27f), 3, 8, 6);
        }

        private static void AddBud(MeshData d, Vector3 center, float size, float yaw, int color)
        {
            Quaternion rotation = Quaternion.Euler(NextTilt(yaw), yaw, -NextTilt(yaw * 0.73f));
            AddEllipsoid(d, center + rotation * Vector3.up * size * 0.18f, rotation,
                new Vector3(size * 0.42f, size * 0.58f, size * 0.42f), 4, 8, color);

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f * Mathf.Deg2Rad;
                Vector3 direction = rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 sepalCenter = center + direction * size * 0.22f;
                Quaternion sepalRotation = rotation * Quaternion.LookRotation(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), Vector3.up)
                                             * Quaternion.Euler(-34f, 0f, 0f);
                AddEllipsoid(d, sepalCenter, sepalRotation,
                    new Vector3(size * 0.18f, size * 0.07f, size * 0.42f), 2, 6, 1);
            }
        }

        private static void AddEllipsoid(MeshData d, Vector3 center, Quaternion rotation, Vector3 radii,
            int rings, int segments, int palette)
        {
            int start = d.vertices.Count;
            Vector2 uv = PaletteUv(palette);

            for (int ring = 0; ring <= rings; ring++)
            {
                float v = ring / (float)rings;
                float latitude = -Mathf.PI * 0.5f + v * Mathf.PI;
                float ringRadius = Mathf.Cos(latitude);
                float y = Mathf.Sin(latitude);

                for (int segment = 0; segment < segments; segment++)
                {
                    float longitude = segment * Mathf.PI * 2f / segments;
                    Vector3 unit = new Vector3(Mathf.Cos(longitude) * ringRadius, y,
                        Mathf.Sin(longitude) * ringRadius);
                    Vector3 local = Vector3.Scale(unit, radii);
                    Vector3 localNormal = new Vector3(
                        unit.x / Mathf.Max(radii.x, 0.0001f),
                        unit.y / Mathf.Max(radii.y, 0.0001f),
                        unit.z / Mathf.Max(radii.z, 0.0001f)).normalized;
                    d.vertices.Add(center + rotation * local);
                    d.normals.Add(rotation * localNormal);
                    d.uvs.Add(uv);
                }
            }

            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    int a = start + ring * segments + segment;
                    int b = start + ring * segments + next;
                    int c = start + (ring + 1) * segments + next;
                    int e = start + (ring + 1) * segments + segment;
                    d.triangles.AddRange(new[] { a, b, c, a, c, e });
                }
            }
        }

        private static float NextTilt(float value) => Mathf.Sin(value * 0.17f) * 8f;

        private static void AddDisc(MeshData d, Vector3 center, Quaternion rotation, float radius, int sides, int palette)
        {
            Vector3 normal = rotation * Vector3.up;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = center + rotation * new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = center + rotation * new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                AddDoubleTriangle(d, center, p0, p1, palette, normal);
            }
        }

        private static void AddDoubleQuad(MeshData d, Vector3 a, Vector3 b, Vector3 c, Vector3 e, int palette)
        {
            Vector3 normal = Vector3.Cross(b - a, e - a).normalized;
            AddQuad(d, a, b, c, e, normal, palette, true);
        }

        private static void AddQuad(MeshData d, Vector3 a, Vector3 b, Vector3 c, Vector3 e, Vector3 normal, int palette, bool doubleSided)
        {
            int start = d.vertices.Count;
            Vector2 uv = PaletteUv(palette);
            d.vertices.AddRange(new[] { a, b, c, e });
            d.normals.AddRange(new[] { normal, normal, normal, normal });
            d.uvs.AddRange(new[] { uv, uv, uv, uv });
            d.triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            if (doubleSided) d.triangles.AddRange(new[] { start + 2, start + 1, start, start + 3, start + 2, start });
        }

        private static void AddDoubleTriangle(MeshData d, Vector3 a, Vector3 b, Vector3 c, int palette, Vector3? forcedNormal = null)
        {
            int start = d.vertices.Count;
            Vector3 normal = forcedNormal ?? Vector3.Cross(b - a, c - a).normalized;
            Vector2 uv = PaletteUv(palette);
            d.vertices.AddRange(new[] { a, b, c });
            d.normals.AddRange(new[] { normal, normal, normal });
            d.uvs.AddRange(new[] { uv, uv, uv });
            d.triangles.AddRange(new[] { start, start + 1, start + 2, start + 2, start + 1, start });
        }

        private static Vector2 PaletteUv(int index) => new Vector2((index + 0.5f) / 8f, 0.5f);
        private static float Next(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static Texture2D CreateOrReplacePalette(string assetPath = TexturePath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            bool isNew = texture == null;
            if (isNew) texture = new Texture2D(8, 1, TextureFormat.RGBA32, false, false);
            else texture.Reinitialize(8, 1, TextureFormat.RGBA32, false);
            texture.name = "TinyFlowerPalette";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(new[]
            {
                new Color32(71, 111, 66, 255),
                new Color32(112, 157, 82, 255),
                new Color32(222, 198, 255, 255),
                new Color32(155, 114, 232, 255),
                new Color32(255, 172, 205, 255),
                new Color32(242, 255, 159, 255),
                new Color32(255, 241, 177, 255),
                new Color32(255, 255, 255, 255)
            });
            texture.Apply(false, false);

            if (isNew) AssetDatabase.CreateAsset(texture, assetPath);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material CreateOrUpdateMaterial(Texture2D palette)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            // Tiny flowers should keep their cheerful palette even in Stage 1's dark lighting.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            if (material == null)
            {
                material = new Material(shader) { name = "TinyFlowerPatch" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            material.mainTexture = palette;
            material.color = Color.white;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SaveOrReplaceMesh(Mesh mesh, string assetPath = MeshPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, assetPath);
                return;
            }

            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.SetTriangles(mesh.triangles, 0);
            existing.bounds = mesh.bounds;
            EditorUtility.SetDirty(existing);
            DestroyImmediate(mesh);
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Project/Art/Stage1Concept", "TinyFlowerPatch");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Textures");
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Prefabs");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private sealed class MeshData
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }
    }
}
