using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Applies a MeshCollider to mesh-based visual children when a static object needs its
    /// authored surface for physics. Existing colliders are preserved so a purpose-built
    /// compound collider remains the source of truth for dynamic gameplay objects.
    /// </summary>
    public static class MeshColliderUtility
    {
        static readonly HashSet<string> Stage1StaticGroupNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Cliffs",
            "MushroomTrees",
            "RockPillars",
            "Boulders",
            "RockClusters",
        };

        /// <summary>
        /// Repairs the static Stage1 environment in the currently loaded scene. This is a
        /// runtime safety net for scenes that were created before the editor setup ran; it is
        /// also required on a dedicated server because the server owns the physics world.
        /// </summary>
        public static int EnsureStage1EnvironmentColliders()
        {
            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var added = 0;

            for (var i = 0; i < transforms.Length; i++)
            {
                var group = transforms[i];
                if (group == null || !Stage1StaticGroupNames.Contains(group.name)) continue;

                for (var childIndex = 0; childIndex < group.childCount; childIndex++)
                    added += EnsureMeshColliders(group.GetChild(childIndex).gameObject, false);
            }

            return added;
        }

        /// <summary>
        /// Adds non-convex colliders for static geometry or convex colliders for an explicitly
        /// opted-in dynamic visual. A non-convex collider is never attached below a Rigidbody.
        /// </summary>
        public static int EnsureMeshColliders(GameObject root, bool convex, bool markGenerated = false)
        {
            if (!IsAlive(root)) return 0;

            MeshFilter[] filters;
            try
            {
                filters = root.GetComponentsInChildren<MeshFilter>(true);
            }
            catch (MissingReferenceException)
            {
                return 0;
            }

            var added = 0;
            for (var i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (!IsAlive(filter) || !IsAlive(filter.sharedMesh)
                    || filter.sharedMesh.vertexCount == 0)
                    continue;

                // PhysX rejects a non-convex MeshCollider on a Rigidbody hierarchy. Dynamic
                // parts, vehicles, pins, and meteors keep their designed simple colliders.
                if (!convex && filter.GetComponentInParent<Rigidbody>() != null)
                    continue;

                var existing = filter.GetComponent<MeshCollider>();
                if (existing != null)
                {
                    // A placeholder MeshCollider can still be repaired without replacing an
                    // authored non-null mesh.
                    if (!IsAlive(existing.sharedMesh)) existing.sharedMesh = filter.sharedMesh;
                    continue;
                }

                // Do not stack a mesh collider beside an authored Box/Sphere/Capsule collider.
                // Those simple shapes are intentional on gameplay roots and are cheaper for
                // moving objects than a render mesh.
                if (filter.GetComponent<Collider>() != null) continue;

                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = convex;
                if (markGenerated && filter.GetComponent<Stage1GeneratedCollider>() == null)
                    filter.gameObject.AddComponent<Stage1GeneratedCollider>();
                added++;
            }

            return added;
        }

        static bool IsAlive(UnityEngine.Object target) =>
            !ReferenceEquals(target, null) && target != null;
    }
}
