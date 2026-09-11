using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 파츠 프리팹의 메시를 높이 구간으로 잘라 <b>측면 외곽선</b>을 재는 측정 도구.
    ///
    /// 태블릿 실루엣을 실제 에셋 형태로 그리기 위한 것이다(S15P21B202-345).
    /// <see cref="Metagame.Gameplay.Parts.PartSpec"/> 의 콜라이더 상자는 파츠당 2~4개라
    /// 계단처럼 보인다. 여기서는 구간을 촘촘히 잘라 <b>구간별 최소·최대</b>를 재므로
    /// 곡면과 좌우 비대칭이 남는다.
    ///
    /// <b>판정하지 않는다.</b> 표를 파일로 남기고, 그 표를 코드로 굳히는 것은 사람이 한다.
    /// </summary>
    public static class PartSilhouetteProbe
    {
        /// <summary>높이 구간 수. 곡선으로 보이려면 상자 개수(2~4)보다 훨씬 촘촘해야 한다.</summary>
        const int Bands = 40;

        static readonly string[] Prefabs =
        {
            "PF_Stage1_Spaceship_Robot",
            "PF_Stage1_Spaceship_HoverPod",
            "PF_Stage1_Spaceship_Capsule",
            "PF_Stage1_TractorCraft",
        };

        [MenuItem("Metagame/측정/파츠 단면 재기")]
        public static void Measure()
        {
            var log = new StringBuilder();
            log.AppendLine("=== 파츠 측면 외곽선 (프리팹 메시 실측) ===");
            log.AppendLine($"구간 {Bands}개. 각 구간에서 x·z 의 최소·최대를 재고,");
            log.AppendLine("정규화는 그 파츠에서 가장 넓은 폭을 1 로 둔다.");

            for (var p = 0; p < Prefabs.Length; p++)
            {
                var path = $"Assets/_Project/Prefabs/Spaceships/Stage1/{Prefabs[p]}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    log.AppendLine($"[없음] {path}");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    Profile(instance, Prefabs[p], log);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            var outPath = Path.Combine(Path.GetTempPath(), "part-silhouette.txt");
            File.WriteAllText(outPath, log.ToString(), new UTF8Encoding(false));
            Debug.Log($"파츠 단면 측정 완료 → {outPath}\n{log}");
        }

        static void Profile(GameObject instance, string label, StringBuilder log)
        {
            var root = instance.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            Sweep(filters, skinned, root, ref minY, ref maxY);

            if (minY > maxY)
            {
                log.AppendLine();
                log.AppendLine($"[메시 없음] {label}");
                return;
            }

            var height = maxY - minY;
            var band = height / Bands;

            var minX = new float[Bands];
            var maxX = new float[Bands];
            var minZ = new float[Bands];
            var maxZ = new float[Bands];
            var hit = new bool[Bands];
            for (var b = 0; b < Bands; b++)
            {
                minX[b] = minZ[b] = float.MaxValue;
                maxX[b] = maxZ[b] = float.MinValue;
            }

            Bin(filters, skinned, root, minY, band, minX, maxX, minZ, maxZ, hit);

            // 정점이 하나도 안 걸린 구간은 위아래에서 메운다. 메시가 촘촘하지 않은
            // 얇은 부분(첨탑 끝)에서 생긴다.
            Fill(minX, maxX, minZ, maxZ, hit);

            var widestX = 0f;
            var widestZ = 0f;
            for (var b = 0; b < Bands; b++)
            {
                widestX = Mathf.Max(widestX, maxX[b] - minX[b]);
                widestZ = Mathf.Max(widestZ, maxZ[b] - minZ[b]);
            }

            log.AppendLine();
            log.AppendLine($"-- {label}   높이 {height:0.000}m   폭 x {widestX:0.000}m   폭 z {widestZ:0.000}m");
            log.AppendLine("  구간   높이(m)     x 최소   x 최대     z 최소   z 최대");
            for (var b = 0; b < Bands; b++)
            {
                log.AppendLine($"  {b,4}  {b * band,8:0.000}"
                               + $"  {minX[b],9:0.000}  {maxX[b],7:0.000}"
                               + $"  {minZ[b],9:0.000}  {maxZ[b],7:0.000}");
            }

            // 코드로 굳힐 형태. 파츠 가장 넓은 폭을 1 로 두고 중심을 0 으로 본다.
            Emit(log, "x", minX, maxX, widestX);
            Emit(log, "z", minZ, maxZ, widestZ);
        }

        static void Emit(StringBuilder log, string axis, float[] min, float[] max, float widest)
        {
            if (widest <= 0f) return;

            var left = new StringBuilder($"  // {axis} 왼쪽\n            ");
            var right = new StringBuilder($"  // {axis} 오른쪽\n            ");
            for (var b = 0; b < Bands; b++)
            {
                left.Append($"{min[b] / widest,6:0.000}f, ");
                right.Append($"{max[b] / widest,6:0.000}f, ");
                if (b % 8 == 7)
                {
                    left.Append("\n            ");
                    right.Append("\n            ");
                }
            }

            log.AppendLine(left.ToString());
            log.AppendLine(right.ToString());
        }

        static void Sweep(MeshFilter[] filters, SkinnedMeshRenderer[] skinned, Transform root,
                          ref float minY, ref float maxY)
        {
            for (var i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                var t = filters[i].transform;
                var verts = mesh.vertices;
                for (var v = 0; v < verts.Length; v++)
                {
                    var y = root.InverseTransformPoint(t.TransformPoint(verts[v])).y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            for (var i = 0; i < skinned.Length; i++)
            {
                var mesh = skinned[i].sharedMesh;
                if (mesh == null) continue;
                var t = skinned[i].transform;
                var verts = mesh.vertices;
                for (var v = 0; v < verts.Length; v++)
                {
                    var y = root.InverseTransformPoint(t.TransformPoint(verts[v])).y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        static void Bin(MeshFilter[] filters, SkinnedMeshRenderer[] skinned, Transform root,
                        float minY, float band,
                        float[] minX, float[] maxX, float[] minZ, float[] maxZ, bool[] hit)
        {
            for (var i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                if (mesh != null)
                    BinOne(mesh.vertices, filters[i].transform, root, minY, band,
                           minX, maxX, minZ, maxZ, hit);
            }

            for (var i = 0; i < skinned.Length; i++)
            {
                var mesh = skinned[i].sharedMesh;
                if (mesh != null)
                    BinOne(mesh.vertices, skinned[i].transform, root, minY, band,
                           minX, maxX, minZ, maxZ, hit);
            }
        }

        static void BinOne(Vector3[] verts, Transform t, Transform root, float minY, float band,
                           float[] minX, float[] maxX, float[] minZ, float[] maxZ, bool[] hit)
        {
            for (var v = 0; v < verts.Length; v++)
            {
                var local = root.InverseTransformPoint(t.TransformPoint(verts[v]));
                var b = Mathf.Clamp(Mathf.FloorToInt((local.y - minY) / band), 0, Bands - 1);
                minX[b] = Mathf.Min(minX[b], local.x);
                maxX[b] = Mathf.Max(maxX[b], local.x);
                minZ[b] = Mathf.Min(minZ[b], local.z);
                maxZ[b] = Mathf.Max(maxZ[b], local.z);
                hit[b] = true;
            }
        }

        static void Fill(float[] minX, float[] maxX, float[] minZ, float[] maxZ, bool[] hit)
        {
            var last = -1;
            for (var b = 0; b < Bands; b++)
            {
                if (!hit[b]) continue;
                if (last >= 0 && b - last > 1)
                {
                    for (var g = last + 1; g < b; g++)
                    {
                        var k = (g - last) / (float)(b - last);
                        minX[g] = Mathf.Lerp(minX[last], minX[b], k);
                        maxX[g] = Mathf.Lerp(maxX[last], maxX[b], k);
                        minZ[g] = Mathf.Lerp(minZ[last], minZ[b], k);
                        maxZ[g] = Mathf.Lerp(maxZ[last], maxZ[b], k);
                        hit[g] = true;
                    }
                }

                last = b;
            }

            // 첫 구간과 마지막 구간은 minY·maxY 를 만든 정점이 들어가므로 반드시 걸린다.
            // 그래도 남는 구간이 있으면 0 으로 둔다 — 폭이 없는 것으로 그려진다.
            for (var b = 0; b < Bands; b++)
            {
                if (hit[b]) continue;
                minX[b] = maxX[b] = minZ[b] = maxZ[b] = 0f;
            }
        }
    }
}
