using System;
using Metagame.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    /// <summary>원 둘레를 맞힌 콜라이더에 재투영하는 표시 전용 메시. 모서리 밖은 연결하지 않는다.</summary>
    internal sealed class WeldingSurfaceGuide : IDisposable
    {
        const int Segments = 48;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly Material material;
        readonly Vector3[] vertices = new Vector3[Segments * 4];
        readonly Vector3[] inner = new Vector3[Segments];
        readonly Vector3[] outer = new Vector3[Segments];
        readonly bool[] valid = new bool[Segments];

        public WeldingSurfaceGuide(Transform parent, Color color)
        {
            root = new GameObject("Welding surface guide");
            root.transform.SetParent(parent, false);
            mesh = new Mesh { name = "Projected welding ring" };
            mesh.MarkDynamic();
            var triangles = new int[Segments * 6];
            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            material = Prim.Flat(color);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Show(RaycastHit hit, float radius, float surfaceOffset)
        {
            surfaceOffset = Mathf.Max(0.002f, surfaceOffset);
            radius = Mathf.Max(0.005f, radius);
            if (hit.collider is SphereCollider sphere)
            {
                var scale = sphere.transform.lossyScale;
                float sphereRadius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                radius = Mathf.Min(radius, sphereRadius * 0.65f);
            }
            var normal = hit.normal;
            var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var bitangent = Vector3.Cross(normal, tangent);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                var radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                valid[i] = Project(hit, radial * (radius - 0.0015f), surfaceOffset, out inner[i]) &&
                           Project(hit, radial * (radius + 0.0015f), surfaceOffset, out outer[i]);
            }
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments, v = i * 4;
                if (!valid[i] || !valid[next])
                {
                    for (int j = 0; j < 4; j++) vertices[v + j] = Vector3.zero;
                    continue;
                }
                vertices[v] = root.transform.InverseTransformPoint(inner[i]);
                vertices[v + 1] = root.transform.InverseTransformPoint(outer[i]);
                vertices[v + 2] = root.transform.InverseTransformPoint(inner[next]);
                vertices[v + 3] = root.transform.InverseTransformPoint(outer[next]);
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            root.SetActive(true);
        }

        static bool Project(RaycastHit center, Vector3 offset, float surfaceOffset, out Vector3 point)
        {
            var ray = new Ray(center.point + offset + center.normal * 0.12f, -center.normal);
            if (center.collider.Raycast(ray, out var hit, 0.24f) && Vector3.Dot(hit.normal, center.normal) > 0.2f)
            {
                point = hit.point + hit.normal * surfaceOffset;
                return true;
            }
            point = default;
            return false;
        }

        public void Hide() { if (root != null) root.SetActive(false); }

        public void Dispose()
        {
            Release(root); Release(mesh); Release(material);
        }

        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
