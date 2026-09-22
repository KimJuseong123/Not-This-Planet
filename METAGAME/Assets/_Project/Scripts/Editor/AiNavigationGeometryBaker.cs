using System.Collections.Generic;
using Metagame.Network;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Metagame.EditorTools
{
    public static class AiNavigationGeometryBaker
    {
        const string Path = "Assets/_Project/Resources/AiNavigationGeometry.asset";

        [MenuItem("Tools/Not This Planet/AI/Rebuild Navigation Geometry")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var geometry = AssetDatabase.LoadAssetAtPath<AiNavigationGeometry>(Path);
            if (geometry == null)
            {
                geometry = ScriptableObject.CreateInstance<AiNavigationGeometry>();
                AssetDatabase.CreateAsset(geometry, Path);
            }
            var seen = new HashSet<Mesh>();
            var entries = new List<AiNavigationGeometry.Entry>();
            var originalFlags = geometry.hideFlags;
            geometry.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            long vertices = 0, bytes = 0;
            try
            {
                foreach (var scene in new[] { "Assets/Scenes/Stage1_Astra.unity", "Assets/Scenes/PracticeLobby.unity" })
                {
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                    foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var mesh = collider.sharedMesh;
                        if (mesh == null || mesh.isReadable) continue;

                        // **런타임이 버리는 것은 굽지 않는다** (S15P21B202-690).
                        //
                        // 예전에는 두 씬의 MeshCollider 를 **전부** 구웠다. 그런데
                        // `AiNavigationWorld.Rebuild` 는 레이어 마스크로 사람·상호작용·운석을
                        // 빼고, 통과하는 트리거와 파츠 아닌 동적 콜라이더도 버린다 — 그것들의
                        // 읽기용 사본은 **쓰이지 않는데 기본 빌드에 실린다.** 이 에셋이
                        // `Resources/` 에 있어서 AI 를 쓰지 않는 사람도 그만큼을 받는다.
                        //
                        // **판단을 여기 베껴 쓰지 않는다.** 런타임과 같은 함수를 부른다 —
                        // 두 벌로 두면 갈리고, 갈리면 런타임이 사본을 못 찾아 **대체 박스**로
                        // 때운다. 그건 실제 바닥을 장애물로 바꾸는 조용한 악화다.
                        // `AuthoredStageBuildsNavigationFromActualColliders` 가 대체 박스 0개를
                        // 단언하므로, 너무 많이 버리면 그 검사가 빨개진다.
                        if (!AiNavigationWorld.UsedForNavigation(collider)) continue;

                        if (!seen.Add(mesh)) continue;
                        var copy = geometry.Get(mesh);
                        if (copy == null)
                        {
                            copy = new Mesh { name = "Navigation_" + mesh.name };
                            AssetDatabase.AddObjectToAsset(copy, geometry);
                        }
                        // MeshUtility bypasses runtime Read/Write restrictions without importer changes.
                        using var meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
                        var input = meshData[0];
                        using var positions = new NativeArray<Vector3>(input.vertexCount, Allocator.Temp);
                        input.GetVertices(positions);
                        var indices = new List<int>();
                        for (int sub = 0; sub < input.subMeshCount; sub++)
                        {
                            using var subIndices = new NativeArray<int>(input.GetSubMesh(sub).indexCount, Allocator.Temp);
                            input.GetIndices(subIndices, sub);
                            indices.AddRange(subIndices.ToArray());
                        }
                        copy.Clear(); copy.indexFormat = mesh.indexFormat;
                        copy.vertices = positions.ToArray(); copy.triangles = indices.ToArray();
                        copy.RecalculateBounds();
                        EditorUtility.SetDirty(copy);
                        vertices += copy.vertexCount;
                        entries.Add(new AiNavigationGeometry.Entry { source = mesh, readable = copy });
                        bytes += copy.vertexCount * 12L + copy.triangles.Length * 4L;
                        geometry.SetEntries(entries.ToArray()); // Keep source references alive across scene switches.
                    }
                }
                geometry.SetEntries(entries.ToArray());
                geometry.hideFlags = originalFlags;
                EditorUtility.SetDirty(geometry);
                AssetDatabase.SaveAssets();
                // 크기를 함께 적는다 (S15P21B202-690). 이 에셋이 기본 빌드에 실리므로
                // "얼마나 줄었나" 를 재베이크 로그 한 줄로 알 수 있어야 한다.
                Debug.Log($"[AI Navigation Bake] {entries.Count} collision meshes, {vertices} vertices, "
                          + $"~{bytes / 1048576.0:F1}MB of position+index data. Original importers/scenes unchanged.");
            }
            finally
            {
                if (geometry != null) geometry.hideFlags = originalFlags;
                bool hasActive = false;
                foreach (var scene in setup) hasActive |= scene.isActive && scene.isLoaded;
                if (hasActive) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
