using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>Session-owned, server-only navigation. Never moves bodies or changes colliders.</summary>
    public sealed class AiNavigationWorld : MonoBehaviour
    {
        readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();
        NavMeshData data;
        NavMeshDataInstance instance;
        AsyncOperation building;
        float nextBuild;
        Bounds bounds;
        AiNavigationGeometry geometry;
        bool loadedGeometry;
        public bool Ready => instance.valid && building != null && building.isDone;
        public int ApproximateMeshCount { get; private set; }
        public int SourceCount => sources.Count;

        void Update()
        {
            if (!NetworkServer.active || AiHostSettings.ServerState != AiRoomState.Active
                || Time.time < nextBuild || (building != null && !building.isDone)) return;
            Rebuild(new Bounds(transform.position, new Vector3(512, 128, 512)));
            nextBuild = Time.time + 2f;
        }

        public void Rebuild(Bounds region)
        {
            if (building != null && !building.isDone) return;
            bounds = region;
            if (!loadedGeometry)
            {
                geometry = Resources.Load<AiNavigationGeometry>("AiNavigationGeometry");
                loadedGeometry = true;
            }
            sources.Clear();
            int mask = ~((1 << GameLayers.Player) | (1 << GameLayers.PlayerDowned)
                | (1 << GameLayers.Interactable) | (1 << GameLayers.BeamQuery) | (1 << GameLayers.Meteor));
            NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            ApproximateMeshCount = 0;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                var collider = source.component as Collider;
                if (collider != null)
                {
                    var part = collider.GetComponentInParent<SpaceshipPart>();
                    // Characters and loose tools are local collision concerns, not permanent walls.
                    if (collider.isTrigger || collider.GetComponentInParent<PlayerRig>() != null
                        || (collider.attachedRigidbody != null && part == null))
                    { sources.RemoveAt(i); continue; }
                    if (part != null) source.area = 1; // Parts block routes; their tops are not walking surfaces.
                }
                if (source.shape == NavMeshBuildSourceShape.Mesh && source.sourceObject is Mesh mesh && !mesh.isReadable)
                {
                    var readable = geometry != null ? geometry.Get(mesh) : null;
                    if (readable != null && readable.isReadable)
                    {
                        source.sourceObject = readable;
                        sources[i] = source;
                        continue;
                    }
                    // Runtime players cannot read imported CPU mesh data. Conservative obstacle box;
                    // never enable Read/Write globally or silently bake inaccessible geometry as floor.
                    source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                    source.size = mesh.bounds.size;
                    source.shape = NavMeshBuildSourceShape.Box;
                    source.sourceObject = null;
                    source.area = 1;
                    ApproximateMeshCount++;
                }
                sources[i] = source;
            }
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .55f;
            settings.agentHeight = 1.8f;
            // PlayerRig is force-driven and has no automatic step-up. Route around curbs to ramps.
            settings.agentClimb = .05f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .12f;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            if (data == null)
            {
                data = new NavMeshData(settings.agentTypeID);
                instance = NavMesh.AddNavMeshData(data);
            }
            building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        }

        public bool CalculatePath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (!Ready || !Project(from, out var start) || !Project(to, out var end)) return false;
            return NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        static bool Project(Vector3 point, out Vector3 projected)
        {
            // Do not snap a point on a raised platform to an unrelated floor below it.
            if (NavMesh.SamplePosition(point, out var hit, .85f, NavMesh.AllAreas)
                && Mathf.Abs(hit.position.y - point.y) <= .65f)
            { projected = hit.position; return true; }
            projected = default;
            return false;
        }

        void OnDisable()
        {
            if (data != null) NavMeshBuilder.Cancel(data);
            if (instance.valid) instance.Remove();
            if (data != null) Destroy(data);
            data = null; building = null; nextBuild = 0;
        }
    }
}
