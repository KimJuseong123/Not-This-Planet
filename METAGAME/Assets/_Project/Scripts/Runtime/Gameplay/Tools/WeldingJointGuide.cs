using System;
using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    /// <summary>근처 파츠의 접합 가능한 표면만 샘플링하는 로컬 표시. 물리/네트워크 상태는 바꾸지 않는다.</summary>
    internal sealed class WeldingJointGuide : IDisposable
    {
        const int MaxCells = 48;
        const float Step = 0.025f;
        readonly Collider[] nearby = new Collider[128];
        readonly List<Collider> hulls = new List<Collider>();
        readonly List<SpaceshipPart> owners = new List<SpaceshipPart>();
        readonly List<Matrix4x4> poses = new List<Matrix4x4>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        readonly RaycastHit[] samples = new RaycastHit[(MaxCells + 1) * (MaxCells + 1)];
        readonly bool[] valid = new bool[(MaxCells + 1) * (MaxCells + 1)];
        readonly float[] alpha = new float[(MaxCells + 1) * (MaxCells + 1)];
        readonly GameObject root;
        readonly Mesh mesh;
        readonly Material material;
        float nextRefresh;
        public bool Visible => root != null && root.activeSelf;

        public WeldingJointGuide(Color color)
        {
            // 월드 좌표 메시이므로 총의 휴대 포즈 변화에 끌려가지 않는다.
            root = new GameObject("Welding joint recommendations");
            mesh = new Mesh { name = "Projected joint recommendations", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            material = new Material(Resources.Load<Shader>("WeldingGuide"));
            material.SetColor("_Color", color);
            material.renderQueue = 3000;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            root.SetActive(false);
        }

        public void Show(Vector3 center, float range, float offset)
        {
            bool moved = false;
            for (int i = 0; i < hulls.Count; i++)
                if (hulls[i] == null || !hulls[i].enabled || !hulls[i].gameObject.activeInHierarchy ||
                    hulls[i].transform.localToWorldMatrix != poses[i]) { moved = true; break; }
            if (!moved && Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            hulls.Clear(); owners.Clear(); poses.Clear();
            vertices.Clear(); colors.Clear(); triangles.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, range, nearby, GameLayers.PartMask,
                QueryTriggerInteraction.Ignore);
            // 밀집된 비드가 버퍼를 채워 몸체를 누락시키는 경우도 처리한다.
            var results = count == nearby.Length
                ? Physics.OverlapSphere(center, range, GameLayers.PartMask, QueryTriggerInteraction.Ignore) : nearby;
            if (results != nearby) count = results.Length;
            for (int i = 0; i < count; i++)
            {
                var collider = results[i];
                var owner = collider.GetComponentInParent<SpaceshipPart>();
                if (owner == null || collider.attachedRigidbody != owner.GetComponent<Rigidbody>() ||
                    collider.GetComponentInParent<PartClamp>() != null) continue;
                // 비드 대신 파츠 몸체만 그린다. WeldBead는 몸체와 같은 루트에 붙는다.
                if (WeldBead.IsBeadCollider(collider)) continue;
                hulls.Add(collider); owners.Add(owner);
                poses.Add(collider.transform.localToWorldMatrix);
            }
            for (int a = 0; a < hulls.Count; a++)
                for (int b = 0; b < hulls.Count; b++)
                {
                    if (a == b || owners[a] == owners[b]) continue;
                    var link = PartAssemblyLink.FindBetween(owners[a], owners[b]);
                    if (link != null && link.IsIntact) continue;
                    var bounds = hulls[b].bounds;
                    bounds.Expand((PhysicsScale.WeldBeadMinRadius + PhysicsScale.WeldBeadContactSkin) * 2f);
                    if (!bounds.Intersects(hulls[a].bounds)) continue;
                    ProjectPair(hulls[a], bounds, center, range, Mathf.Max(0.002f, offset));
                }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            root.SetActive(triangles.Count > 0);
        }

        void ProjectPair(Collider source, Bounds target, Vector3 center, float range, float offset)
        {
            var bounds = source.bounds;
            var min = Vector3.Max(Vector3.Max(bounds.min, target.min), center - Vector3.one * range);
            var max = Vector3.Min(Vector3.Min(bounds.max, target.max), center + Vector3.one * range);
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                if (max[u] <= min[u] || max[v] <= min[v]) continue;
                int width = Mathf.Clamp(Mathf.CeilToInt((max[u] - min[u]) / Step), 1, MaxCells);
                int height = Mathf.Clamp(Mathf.CeilToInt((max[v] - min[v]) / Step), 1, MaxCells);
                float maxEdge = new Vector2((max[u] - min[u]) / width, (max[v] - min[v]) / height).magnitude * 2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var normal = Vector3.zero; normal[axis] = side;
                    for (int y = 0; y <= height; y++)
                        for (int x = 0; x <= width; x++)
                        {
                            int index = y * (width + 1) + x;
                            var origin = bounds.center;
                            origin[axis] += side * (bounds.extents[axis] + 0.1f);
                            origin[u] = Mathf.Lerp(min[u], max[u], (float)x / width);
                            origin[v] = Mathf.Lerp(min[v], max[v], (float)y / height);
                            valid[index] = source.Raycast(new Ray(origin, -normal), out samples[index], bounds.size[axis] + 0.2f);
                            var hit = samples[index];
                            // 법선의 주축에서만 그려 곡면에 중복 투영하지 않는다.
                            valid[index] &= Vector3.Dot(hit.normal, normal) > 0f &&
                                Mathf.Abs(hit.normal[axis]) >= Mathf.Abs(hit.normal[u]) &&
                                Mathf.Abs(hit.normal[axis]) >= Mathf.Abs(hit.normal[v]);
                            alpha[index] = valid[index] && (hit.point - center).sqrMagnitude <= range * range &&
                                WeldBead.CanStartJoiningAt(hit) ? 1f : 0f;
                        }
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int a = y * (width + 1) + x, b = a + 1, c = a + width + 1, d = c + 1;
                            if (!valid[a] || !valid[b] || !valid[c] || !valid[d] ||
                                alpha[a] + alpha[b] + alpha[c] + alpha[d] == 0f) continue;
                            // 모서리/분리된 면을 가로질러 공중에 띠가 생기지 않도록 한다.
                            if (Vector3.Dot(samples[a].normal, samples[d].normal) < 0.7f ||
                                (samples[a].point - samples[d].point).sqrMagnitude > maxEdge * maxEdge) continue;
                            int start = vertices.Count;
                            Add(a, offset); Add(b, offset); Add(c, offset); Add(d, offset);
                            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                            triangles.Add(start + 2); triangles.Add(start + 1); triangles.Add(start + 3);
                        }
                }
            }
        }

        void Add(int index, float offset)
        {
            vertices.Add(samples[index].point + samples[index].normal * offset);
            colors.Add(new Color(1f, 1f, 1f, alpha[index]));
        }

        public void Hide() { root.SetActive(false); nextRefresh = 0f; }
        public void Dispose() { Release(root); Release(mesh); Release(material); }
        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
