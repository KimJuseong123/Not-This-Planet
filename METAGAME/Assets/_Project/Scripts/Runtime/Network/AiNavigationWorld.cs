using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

namespace Metagame.Network
{
    /// <summary>Session-owned, server-only navigation. Never moves bodies or changes colliders.</summary>
    public sealed class AiNavigationWorld : MonoBehaviour
    {
        readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();
        readonly List<NavMeshBuildSource> previousSources = new();
        static readonly ProfilerMarker CollectMarker = new("AI.Navigation.CollectSources");
        static readonly ProfilerMarker BuildMarker = new("AI.Navigation.ScheduleBuild");
        bool hasBuilt;
        NavMeshData data;
        NavMeshDataInstance instance;
        AsyncOperation building;
        float nextBuild;
        Bounds bounds;
        AiNavigationGeometry geometry;
        bool loadedGeometry;
        public bool Ready => instance.valid && (hasBuilt || (building != null && building.isDone));
        public bool IsBuilding => building != null && !building.isDone;
        public int BuildCount { get; private set; }
        public int ApproximateMeshCount { get; private set; }
        public int SourceCount => sources.Count;

        /// <summary>
        /// 길찾기가 <b>영구한 벽으로 세는 레이어</b>만 남기는 마스크.
        ///
        /// <para><see cref="AiNavigationGeometryBaker"/> 가 같은 값을 쓴다 — 굽는 쪽과
        /// 쓰는 쪽이 갈리면 <b>조용히 나빠진다</b>(S15P21B202-690). 베이커가 덜 구우면
        /// 런타임이 읽기용 사본을 못 찾아 <see cref="ApproximateMeshCount"/> 의 대체
        /// 박스로 때우는데, 그것은 실제 바닥을 <b>장애물로</b> 바꾼다.</para>
        /// </summary>
        public static int PermanentLayerMask => ~((1 << GameLayers.Player) | (1 << GameLayers.PlayerDowned)
            | (1 << GameLayers.Interactable) | (1 << GameLayers.BeamQuery) | (1 << GameLayers.Meteor));

        /// <summary>
        /// 이 콜라이더가 <b>길찾기에 쓰이는가</b>. 굽는 쪽과 쓰는 쪽이 이 함수를 함께 쓴다.
        ///
        /// <para>버리는 것은 셋이다 — 통과하는 트리거, 사람 몸, 그리고 <b>파츠가 아닌
        /// 동적 콜라이더</b>(굴러다니는 도구는 영구한 벽이 아니다). 파츠는 남기되
        /// <c>area = 1</c> 로 "막지만 걸을 수는 없는" 것이 된다.</para>
        /// </summary>
        public static bool UsedForNavigation(Collider collider)
        {
            if (collider == null) return true;   // 콜라이더가 아닌 소스(터레인 등)는 그대로 쓴다
            if ((PermanentLayerMask & (1 << collider.gameObject.layer)) == 0) return false;
            if (collider.isTrigger || collider.GetComponentInParent<PlayerRig>() != null) return false;
            return collider.attachedRigidbody == null || collider.GetComponentInParent<SpaceshipPart>() != null;
        }

        void Update()
        {
            if (!NetworkServer.active || AiHostSettings.ServerState != AiRoomState.Active
                || Time.time < nextBuild || IsBuilding) return;
            // Work acceptance (tool pickup/boarding) queries paths before its first Tick.
            // Keep discovering changed obstacles while AI exists, even while it is idle;
            // otherwise the first order after an idle period can reject a now-open route.
            if (!NetPlayer.HasAiPlayers) return;
            Rebuild(new Bounds(transform.position, new Vector3(512, 128, 512)));
            nextBuild = Time.time + 2f;
        }

        public void Rebuild(Bounds region, bool force = false)
        {
            if (IsBuilding) return;
            hasBuilt = Ready;
            if (!loadedGeometry)
            {
                geometry = Resources.Load<AiNavigationGeometry>("AiNavigationGeometry");
                loadedGeometry = true;
            }
            sources.Clear();
            using (CollectMarker.Auto())
                NavMeshBuilder.CollectSources(region, PermanentLayerMask, NavMeshCollectGeometry.PhysicsColliders,
                    0, markups, sources);
            ApproximateMeshCount = 0;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                var collider = source.component as Collider;
                if (collider != null)
                {
                    // Characters and loose tools are local collision concerns, not permanent walls.
                    // **베이커와 같은 함수를 쓴다** (S15P21B202-690) — 갈리면 조용히 나빠진다.
                    if (!UsedForNavigation(collider)) { sources.RemoveAt(i); continue; }
                    // Parts block routes; their tops are not walking surfaces.
                    if (collider.GetComponentInParent<SpaceshipPart>() != null) source.area = 1;
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
            // Keep the discovery pass: doors, spawned scenery and destroyed colliders need no
            // registration contract. Unchanged geometry must not schedule another native build.
            if (!force && hasBuilt && bounds.Equals(region) && SameSources()) return;
            bounds = region;
            previousSources.Clear();
            previousSources.AddRange(sources);
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
            using (BuildMarker.Auto())
                building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
            BuildCount++;
        }

        bool SameSources()
        {
            if (sources.Count != previousSources.Count) return false;
            for (int i = 0; i < sources.Count; i++)
            {
                var a = sources[i]; var b = previousSources[i];
                if (a.component != b.component || a.sourceObject != b.sourceObject || a.shape != b.shape
                    || a.area != b.area || !a.size.Equals(b.size) || !a.transform.Equals(b.transform)) return false;
            }
            return true;
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
            hasBuilt = false; BuildCount = 0;
            sources.Clear(); previousSources.Clear();
        }
    }
}
