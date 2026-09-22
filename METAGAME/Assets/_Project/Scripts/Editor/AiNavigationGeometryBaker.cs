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
            long vertices = 0;
            try
            {
                foreach (var scene in new[] { "Assets/Scenes/Stage1_Astra.unity", "Assets/Scenes/PracticeLobby.unity" })
                {
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                    foreach (var collider in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var mesh = collider.sharedMesh;
                        if (mesh == null || mesh.isReadable || !seen.Add(mesh)) continue;
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
                        geometry.SetEntries(entries.ToArray()); // Keep source references alive across scene switches.
                    }
                }
                geometry.SetEntries(entries.ToArray());
                geometry.hideFlags = originalFlags;
                EditorUtility.SetDirty(geometry);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AI Navigation Bake] {entries.Count} collision meshes, {vertices} vertices. Original importers/scenes unchanged.");
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
