using System.Text;
using UnityEditor;
using UnityEngine;

namespace Metagame.EditorTools
{
    /// <summary>
    /// 임포트된 FBX 의 계층·본·메시·재질을 로그로 쏟는다.
    /// 모델을 코드에 붙이기 전에 "실제로 뭐가 들어왔는지" 를 보기 위한 도구다.
    ///
    /// CI: Unity.exe -batchmode -quit -projectPath . -executeMethod SpaceFlop.EditorTools.ArtInspect.DumpAll
    /// </summary>
    public static class ArtInspect
    {
        [MenuItem("Tools/둥실둥실 우주 대탈출/모델 구조 찍어보기", false, 60)]
        public static void DumpAll()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Models" });
            System.Array.Sort(guids);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                var probe = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool skinned = probe != null && probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                Dump(path, skinned);
            }
        }

        static void Dump(string path, bool isCharacter)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { Debug.Log("[ART] 못 찾음: " + path); return; }

            var go = Object.Instantiate(asset);
            go.name = asset.name;

            var sb = new StringBuilder();
            sb.AppendLine("[ART] ===== " + asset.name + " =====");

            var all = go.GetComponentsInChildren<Transform>(true);
            sb.AppendLine("[ART] 트랜스폼 " + all.Length + "개");

            // 전체 바운드
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                sb.AppendLine("[ART] 바운드 center=" + b.center.ToString("F3") + " size=" + b.size.ToString("F3"));
            }
            sb.AppendLine("[ART] Renderer " + rends.Length + "개 (Skinned "
                          + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length + ")");

            // 계층 (깊이 3까지, 메시가 아닌 노드 위주)
            sb.AppendLine("[ART] --- 계층 ---");
            Walk(go.transform, 0, 3, sb);

            if (isCharacter)
            {
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                sb.AppendLine("[ART] --- 스킨 메시 → 본 ---");
                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    string bone = "?";
                    var mesh = smr.sharedMesh;
                    if (mesh != null && smr.bones != null && smr.bones.Length > 0)
                    {
                        var w = mesh.boneWeights;
                        if (w != null && w.Length > 0)
                        {
                            int idx = w[0].boneIndex0;
                            if (idx >= 0 && idx < smr.bones.Length && smr.bones[idx] != null)
                                bone = smr.bones[idx].name;
                        }
                    }
                    sb.AppendLine("[ART] mesh " + smr.name + "  ← " + bone
                                  + "  tri=" + (mesh != null ? mesh.triangles.Length / 3 : 0)
                                  + "  mat=" + (smr.sharedMaterial != null ? smr.sharedMaterial.name : "-"));
                }

                sb.AppendLine("[ART] --- 본 월드 위치 (바인드 자세) ---");
                // 실제로 들어온 본을 전부 찍는다. 이름 규약은 모델마다 다르다 (Tripo, Mixamo, 자체 리그...)
                var seen = new System.Collections.Generic.HashSet<Transform>();
                for (int i = 0; i < smrs.Length; i++)
                {
                    var bs = smrs[i].bones;
                    for (int k = 0; bs != null && k < bs.Length; k++)
                        if (bs[k] != null) seen.Add(bs[k]);
                }
                var sorted = new System.Collections.Generic.List<Transform>(seen);
                sorted.Sort((a, b) => b.position.y.CompareTo(a.position.y));
                for (int i = 0; i < sorted.Count; i++)
                {
                    var t = sorted[i];
                    sb.AppendLine("[ART] bone " + t.name.PadRight(20)
                                  + " pos=" + t.position.ToString("F3")
                                  + " up=" + (t.rotation * Vector3.up).ToString("F2")
                                  + " parent=" + (t.parent != null ? t.parent.name : "-"));
                }
            }
            else
            {
                sb.AppendLine("[ART] --- 부품 (Renderer 별 바운드) ---");
                for (int i = 0; i < rends.Length; i++)
                    sb.AppendLine("[ART] part " + rends[i].name.PadRight(28)
                                  + " center=" + rends[i].bounds.center.ToString("F2")
                                  + " size=" + rends[i].bounds.size.ToString("F2")
                                  + " mat=" + (rends[i].sharedMaterial != null ? rends[i].sharedMaterial.name : "-"));
            }

            Debug.Log(sb.ToString());
            Object.DestroyImmediate(go);
        }

        static void Walk(Transform t, int depth, int maxDepth, StringBuilder sb)
        {
            if (depth > maxDepth) return;
            sb.AppendLine("[ART] " + new string(' ', depth * 2) + "· " + t.name
                          + " (자식 " + t.childCount + ")");
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, maxDepth, sb);
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
