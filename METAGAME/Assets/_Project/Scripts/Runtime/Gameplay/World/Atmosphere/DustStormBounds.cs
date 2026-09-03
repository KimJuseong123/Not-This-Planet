using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Finds the playable Stage1 perimeter without hard-coding the current tile layout.</summary>
    public static class DustStormBounds
    {
        public static Bounds FindOrFallback()
        {
            if (TryFind(out var bounds)) return bounds;

            // This is only a safety net for tests or a scene before Terrain setup.
            return new Bounds(new Vector3(25f, 3f, 0f), new Vector3(150f, 6f, 100f));
        }

        public static bool TryFind(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                var terrain = terrains[i];
                if (terrain == null || !terrain.enabled) continue;

                var collider = terrain.GetComponent<TerrainCollider>();
                var candidate = collider != null && collider.enabled
                    ? collider.bounds
                    : terrain.terrainData != null
                        ? new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f,
                                     terrain.terrainData.size)
                        : new Bounds(terrain.transform.position, Vector3.zero);

                if (!found)
                {
                    bounds = candidate;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(candidate);
                }
            }

            return found && bounds.size.x > 0.01f && bounds.size.z > 0.01f;
        }
    }
}
