using UnityEngine;
using UnityEngine.Rendering;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Renders the active storm volume as one red wireframe mesh for test builds.
    /// It intentionally has no Collider and is not part of the gameplay query volume.
    /// </summary>
    public sealed class DustStormBoundaryRenderer : MonoBehaviour
    {
        const string RenderObjectName = "Dust Storm Boundary Mesh";
        const int EdgeCount = 13;
        const int VerticesPerEdge = 8;
        const int IndicesPerEdge = 36;

        static readonly int[] EdgePairs =
        {
            0, 1, 0, 2, 0, 4,
            1, 3, 1, 5, 2, 3,
            2, 6, 3, 7, 4, 5,
            4, 6, 5, 7, 6, 7
        };

        static readonly int[] FacePattern =
        {
            0, 4, 5, 0, 5, 1,
            1, 5, 6, 1, 6, 2,
            2, 6, 7, 2, 7, 3,
            3, 7, 4, 3, 4, 0,
            0, 1, 2, 0, 2, 3,
            4, 7, 6, 4, 6, 5
        };

        Transform renderObject;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        Mesh mesh;
        Material material;
        Vector3[] vertices;
        Color[] colors;
        int[] indices;
        Vector3 lastHalfExtents;
        float lastWidth = -1f;
        float lastSkyMarkerHeight = -1f;

        void Awake()
        {
            EnsureRenderObject();
        }

        public void SetStyle(Color color, float width)
        {
            SetStyle(color, width, 80f);
        }

        public void SetStyle(Color color, float width, float skyMarkerHeight)
        {
            EnsureRenderObject();
            width = Mathf.Max(0.001f, width);
            skyMarkerHeight = Mathf.Max(10f, skyMarkerHeight);
            if (material != null)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            }
            if (meshRenderer != null) meshRenderer.sharedMaterial = material;

            bool styleChanged = !Mathf.Approximately(lastWidth, width)
                || !Mathf.Approximately(lastSkyMarkerHeight, skyMarkerHeight);
            if (styleChanged)
            {
                lastWidth = width;
                lastSkyMarkerHeight = skyMarkerHeight;
                if (lastHalfExtents.sqrMagnitude > 0.000001f)
                    BuildMesh(lastHalfExtents, lastWidth);
            }
        }

        public void SetPose(StormPose pose)
        {
            if (!pose.IsActive)
            {
                SetVisible(false);
                return;
            }

            EnsureRenderObject();
            if (renderObject != null)
                renderObject.SetPositionAndRotation(pose.Center, pose.Rotation);
            if (lastHalfExtents != pose.HalfExtents || lastWidth <= 0f || lastSkyMarkerHeight <= 0f)
            {
                lastHalfExtents = pose.HalfExtents;
                if (lastWidth <= 0f) lastWidth = 0.04f;
                if (lastSkyMarkerHeight <= 0f) lastSkyMarkerHeight = 80f;
                BuildMesh(lastHalfExtents, lastWidth);
            }
            SetVisible(true);
        }

        public void SetVisible(bool visible)
        {
            EnsureRenderObject();
            if (meshRenderer != null) meshRenderer.enabled = visible;
        }

        public void Clear()
        {
            SetVisible(false);
        }

        void EnsureRenderObject()
        {
            // The controller is intentionally renderer-free. This avoids the
            // RequireComponent/AddComponent ordering problem that previously
            // left "Dust Storm Debug Boundary" without a MeshRenderer.
            if (renderObject == null)
            {
                var existing = transform.Find(RenderObjectName);
                if (existing != null)
                    renderObject = existing;
                else
                {
                    var go = new GameObject(RenderObjectName);
                    renderObject = go.transform;
                    renderObject.SetParent(transform, false);
                }

                renderObject.localPosition = Vector3.zero;
                renderObject.localRotation = Quaternion.identity;
                renderObject.localScale = Vector3.one;
            }
            if (!renderObject.gameObject.activeSelf)
                renderObject.gameObject.SetActive(true);

            if (meshFilter == null)
            {
                meshFilter = renderObject.GetComponent<MeshFilter>();
                if (meshFilter == null) meshFilter = renderObject.gameObject.AddComponent<MeshFilter>();
            }
            if (meshRenderer == null)
            {
                meshRenderer = renderObject.GetComponent<MeshRenderer>();
                if (meshRenderer == null) meshRenderer = renderObject.gameObject.AddComponent<MeshRenderer>();
            }

            if (meshFilter == null || meshRenderer == null) return;

            if (vertices == null) vertices = new Vector3[EdgeCount * VerticesPerEdge];
            if (colors == null)
            {
                colors = new Color[EdgeCount * VerticesPerEdge];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            }
            if (indices == null)
            {
                indices = new int[EdgeCount * IndicesPerEdge];
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    int vertexOffset = edge * VerticesPerEdge;
                    int indexOffset = edge * IndicesPerEdge;
                    for (int i = 0; i < FacePattern.Length; i++)
                        indices[indexOffset + i] = vertexOffset + FacePattern[i];
                }
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Dust Storm Debug Boundary" };
                mesh.MarkDynamic();
            }
            // Reassign on every recovery path. A scene reload or a destroyed
            // native component must never leave a valid mesh disconnected.
            meshFilter.sharedMesh = mesh;

            if (material == null)
            {
                // This shader lives in Resources so it is available in a player
                // build and always renders the test marker above scene geometry.
                var shader = Resources.Load<Shader>("DustStormBoundary");
                if (shader == null) shader = Shader.Find("Metagame/Atmosphere/DustStormBoundary");
                if (shader == null) shader = Prim.FlatShader;
                if (shader != null)
                {
                    material = new Material(shader) { name = "Dust Storm Debug Boundary (Runtime)" };
                    material.hideFlags = HideFlags.DontSave;
                    material.enableInstancing = true;
                    material.renderQueue = (int)RenderQueue.Overlay;
                }
            }

            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.allowOcclusionWhenDynamic = false;
        }

        void BuildMesh(Vector3 halfExtents, float width)
        {
            if (mesh == null || vertices == null || indices == null) return;

            Vector3[] corners =
            {
                new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z),
                new Vector3(-halfExtents.x, -halfExtents.y, halfExtents.z),
                new Vector3(-halfExtents.x, halfExtents.y, -halfExtents.z),
                new Vector3(-halfExtents.x, halfExtents.y, halfExtents.z),
                new Vector3(halfExtents.x, -halfExtents.y, -halfExtents.z),
                new Vector3(halfExtents.x, -halfExtents.y, halfExtents.z),
                new Vector3(halfExtents.x, halfExtents.y, -halfExtents.z),
                new Vector3(halfExtents.x, halfExtents.y, halfExtents.z)
            };

            float radius = Mathf.Max(0.0005f, width * 0.5f);
            for (int edge = 0; edge < EdgeCount; edge++)
            {
                if (edge >= EdgePairs.Length / 2) break;
                Vector3 a = corners[EdgePairs[edge * 2]];
                Vector3 b = corners[EdgePairs[edge * 2 + 1]];
                Vector3 direction = (b - a).normalized;
                Vector3 normalA = Vector3.Cross(direction, Vector3.up);
                if (normalA.sqrMagnitude < 0.0001f)
                    normalA = Vector3.Cross(direction, Vector3.right);
                normalA.Normalize();
                Vector3 normalB = Vector3.Cross(direction, normalA).normalized;

                int offset = edge * VerticesPerEdge;
                WriteTubeEnd(a, normalA, normalB, radius, vertices, offset);
                WriteTubeEnd(b, normalA, normalB, radius, vertices, offset + 4);
            }

            // A tall marker makes the moving storm discoverable from a distance.
            // The renderer is yaw-rotated with the storm, so this local Y edge
            // remains world-vertical while the box follows its travel direction.
            int markerOffset = (EdgeCount - 1) * VerticesPerEdge;
            Vector3 markerStart = new Vector3(0f, -halfExtents.y, 0f);
            Vector3 markerEnd = markerStart + Vector3.up * Mathf.Max(10f, lastSkyMarkerHeight);
            WriteTubeEnd(markerStart, Vector3.right, Vector3.forward, radius, vertices, markerOffset);
            WriteTubeEnd(markerEnd, Vector3.right, Vector3.forward, radius, vertices, markerOffset + 4);

            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            float maximumY = Mathf.Max(halfExtents.y,
                -halfExtents.y + Mathf.Max(10f, lastSkyMarkerHeight));
            mesh.bounds = new Bounds(
                new Vector3(0f, (maximumY - halfExtents.y) * 0.5f, 0f),
                new Vector3(
                    (halfExtents.x + width) * 2f,
                    maximumY + halfExtents.y + width * 2f,
                    (halfExtents.z + width) * 2f));
        }

        static void WriteTubeEnd(Vector3 point, Vector3 normalA, Vector3 normalB,
                                  float radius, Vector3[] target, int offset)
        {
            target[offset] = point - normalA * radius - normalB * radius;
            target[offset + 1] = point - normalA * radius + normalB * radius;
            target[offset + 2] = point + normalA * radius + normalB * radius;
            target[offset + 3] = point + normalA * radius - normalB * radius;
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (mesh != null) Destroy(mesh);
                if (material != null) Destroy(material);
            }
            else
            {
                if (mesh != null) DestroyImmediate(mesh);
                if (material != null) DestroyImmediate(material);
            }
        }
    }
}
