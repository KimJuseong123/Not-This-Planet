using System;
using System.Collections;
using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.Gameplay
{
    /// <summary>근처 파츠의 접합 가능한 표면만 샘플링하는 로컬 표시. 물리/네트워크 상태는 바꾸지 않는다.</summary>
    public sealed class WeldingJointGuide : IDisposable
    {
        // This translucent recommendation is not the weld hit test. A 7.5 cm
        // display grid avoids hundreds of thousands of overlapping vertices near
        // assembled cores; the actual bead/contact precision stays unchanged.
        const int MaxCells = 16;
        const float Step = 0.075f;
        public const int WorkPerFrame = 2048;
        const double MillisecondsPerFrame = 2.0;
        IEnumerator build;
        Vector3 buildCenter;
        float buildRange;
        float buildOffset;
        public int LastWorkCount { get; private set; }
        public bool IsBuilding => build != null;
        readonly Collider[] nearby = new Collider[128];
        readonly List<Collider> hulls = new List<Collider>();
        readonly List<SpaceshipPart> owners = new List<SpaceshipPart>();
        readonly List<Matrix4x4> poses = new List<Matrix4x4>();

        /// <summary>
        /// 움직였다고 볼 거리(m). <b>정확한 일치를 요구하면 멀티에서 매 프레임 다시
        /// 짓는다</b>(S15P21B202-722).
        ///
        /// <para>참가자 화면의 파츠는 <c>WorldSync</c> 가 목표를 향해 <c>Lerp</c> 로
        /// 미는 키네마틱 강체이고 <c>RigidbodyInterpolation.Interpolate</c> 가 걸려
        /// 있다. Lerp 는 목표에 <b>정확히 닿지 않으므로</b> 눈에는 멈춰 있어도 행렬이
        /// 매 프레임 조금씩 달라진다 — 그러면 아래 0.1초 스로틀이 통째로 무시되고
        /// O(n²) 재구성과 메시 재생성이 프레임마다 돈다.</para>
        ///
        /// <para>싱글에서 안 보이던 이유가 이것이다. 방장 쪽 파츠는 보통 강체이고
        /// 멈추면 잠들어 행렬이 그대로다.</para>
        ///
        /// <para>0.5mm 는 비드 판정(<see cref="PhysicsScale.WeldBeadContactSkin"/>)보다
        /// 훨씬 작다 — 이 안에서 움직인 것은 표시가 달라질 이유가 없다.</para>
        /// </summary>
        public const float MoveEpsilon = 0.0005f;

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
            LastWorkCount = 0;
            if (root == null) return;
            // Hide stale geometry immediately, but never bypass the per-frame work
            // budget when network interpolation keeps moving a nearby part.
            bool changed = (center - buildCenter).sqrMagnitude > 0.25f
                || range != buildRange || offset != buildOffset;
            for (int i = 0; i < hulls.Count; i++)
                if (hulls[i] == null || !hulls[i].enabled || !hulls[i].gameObject.activeInHierarchy ||
                    Moved(hulls[i].transform.localToWorldMatrix, poses[i])) { changed = true; break; }

            if (changed)
            {
                CancelBuild();
                root.SetActive(false);
                nextRefresh = 0f;
            }
            if (build == null)
            {
                if (Time.unscaledTime < nextRefresh) return;
                buildCenter = center;
                buildRange = range;
                buildOffset = offset;
                build = Rebuild(center, range, offset);
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            while (LastWorkCount < WorkPerFrame)
            {
                LastWorkCount++;
                if (!build.MoveNext())
                {
                    CancelBuild();
                    nextRefresh = Time.unscaledTime + 0.1f;
                    return;
                }
                if (LastWorkCount % 32 == 0 &&
                    (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 /
                    System.Diagnostics.Stopwatch.Frequency >= MillisecondsPerFrame) return;
            }
        }

        IEnumerator Rebuild(Vector3 center, float range, float offset)
        {
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
            {
                // Several neighbouring core parts can cover the same source face.
                // Project it once; the contact query still rejects non-joining points.
                bool hasTarget = false;
                Bounds targetBounds = default;
                for (int b = 0; b < hulls.Count; b++)
                {
                    yield return null;
                    if (a == b || owners[a] == owners[b]) continue;
                    var link = PartAssemblyLink.FindBetween(owners[a], owners[b]);
                    if (link != null && link.IsIntact) continue;
                    var bounds = hulls[b].bounds;
                    bounds.Expand((PhysicsScale.WeldBeadMinRadius + PhysicsScale.WeldBeadContactSkin) * 2f);
                    if (!bounds.Intersects(hulls[a].bounds)) continue;
                    if (hasTarget) targetBounds.Encapsulate(bounds);
                    else { targetBounds = bounds; hasTarget = true; }
                }
                if (!hasTarget) continue;
                var projection = ProjectPair(hulls[a], targetBounds, center, range, Mathf.Max(0.002f, offset));
                while (projection.MoveNext()) yield return null;
            }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            root.SetActive(triangles.Count > 0);
        }

        /// <summary>
        /// 다시 지어야 할 만큼 움직였는가. <b>행렬을 그대로 비교하지 않는다</b> —
        /// 이유는 <see cref="MoveEpsilon"/> 에 적었다.
        /// </summary>
        public static bool Moved(in Matrix4x4 now, in Matrix4x4 before)
        {
            if ((now.GetColumn(3) - before.GetColumn(3)).sqrMagnitude
                > MoveEpsilon * MoveEpsilon) return true;

            // 회전·크기도 본다. 제자리에서 도는 파츠를 놓치면 표시가 옛 면에 남는다.
            // 축 셋을 그대로 견주는 것이 쿼터니언을 뽑는 것보다 싸다.
            for (int axis = 0; axis < 3; axis++)
                if ((now.GetColumn(axis) - before.GetColumn(axis)).sqrMagnitude
                    > MoveEpsilon * MoveEpsilon) return true;

            return false;
        }

        IEnumerator ProjectPair(Collider source, Bounds target, Vector3 center, float range, float offset)
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
                            yield return null;
                        }
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            yield return null;
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

        // 바로 위 Visible 은 root 가 없어질 수 있다는 것을 이미 알고 검사한다.
        // 여기만 안 해서, 안내선 오브젝트가 먼저 지워진 뒤 OnDisable 이 오면
        // MissingReferenceException 이 난다 — 씬이 정리되는 순서에 따라 터진다.
        void CancelBuild() { (build as IDisposable)?.Dispose(); build = null; }
        public void Hide()
        {
            CancelBuild();
            if (root != null) root.SetActive(false);
            nextRefresh = 0f;
        }
        public void Dispose() { CancelBuild(); Release(root); Release(mesh); Release(material); }
        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
