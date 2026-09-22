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
        public static bool UsedForNavigation(Collider collider) => UsedForNavigation(collider, out _);

        /// <summary>
        /// 같은 판단을 하되 <b>찾은 파츠를 함께 돌려준다</b> (S15P21B202-704).
        ///
        /// <para><b>왜 돌려주나.</b> 부르는 쪽은 통과한 소스마다 <c>area = 1</c> 인지 정해야
        /// 해서 같은 파츠가 필요하다. 그래서 <c>-690</c> 이후 <c>GetComponentInParent</c> 가
        /// 소스마다 <b>두 번</b> 돌았다 — 이 안에서 한 번, 부르는 쪽에서 또 한 번. 665개면
        /// 굽을 때마다 665번이 더 든다.</para>
        ///
        /// <para><b>미리 찾아 넘기는 것으로는 못 고친다.</b> 그러면 레이어·트리거·사람 몸에서
        /// 걸러질 콜라이더까지 비싼 계층 탐색을 하게 되어, 굽는 고리를 위해 고친 것이
        /// <b>거르는 쪽을 더 느리게</b> 만든다. 실제로 한 번 그렇게 고쳤다가 <c>!595</c> AI
        /// 리뷰가 짚었다. 찾는 자리는 여기 하나로 두고, 값만 밖으로 낸다.</para>
        ///
        /// <para><b>순서가 규칙이다.</b> 싼 필터에서 걸러지면 파츠는 찾지 않는다(0회).
        /// 통과하면 정확히 한 번 찾고, 그 값을 부르는 쪽이 그대로 쓴다.</para>
        ///
        /// <para>판정만 필요한 쪽(<c>AiNavigationGeometryBaker</c>)은 <c>out _</c> 로 버린다 —
        /// 정적 콜라이더에서 예전엔 건너뛰던 탐색을 한 번 하게 되지만, 베이커는 에디터에서
        /// 한 판에 한 번 도는 일이고 <b>규칙이 두 벌로 갈리는 쪽이 더 비싸다.</b></para>
        /// </summary>
        public static bool UsedForNavigation(Collider collider, out SpaceshipPart part)
        {
            part = null;
            if (collider == null) return true;   // 콜라이더가 아닌 소스(터레인 등)는 그대로 쓴다
            if ((PermanentLayerMask & (1 << collider.gameObject.layer)) == 0) return false;
            if (collider.isTrigger || collider.GetComponentInParent<PlayerRig>() != null) return false;
            part = collider.GetComponentInParent<SpaceshipPart>();
            return collider.attachedRigidbody == null || part != null;
        }

        /// <summary>
        /// <see cref="Rebuild"/> 가 <b>무엇을 했는지</b> (S15P21B202-704).
        ///
        /// <para>안 구운 이유를 밖에서 <c>BuildCount</c> 증가 여부로 역추론하면, 나중에 조기
        /// 반환이 하나 늘 때 그것이 <b>조용히 "게이트가 일했다" 로 집계된다.</b> 새 반환 경로는
        /// 여기서 이름을 골라야 한다.</para>
        /// </summary>
        public enum RebuildOutcome { Busy, Unchanged, Baked }

        void Update()
        {
            if (!NetworkServer.active || AiHostSettings.ServerState != AiRoomState.Active) return;
            // 굽기가 끝난 프레임에 걸린 시간을 거둔다. 아래 IsBuilding 으로 먼저 빠져나가면
            // 이 자리를 지나치므로 굽는 시간은 영영 관측되지 않는다.
            if (bakeStartedAt > 0f && !IsBuilding)
            {
                float ms = (Time.realtimeSinceStartup - bakeStartedAt) * 1000f;
                bakeTotalMs += ms; bakeMaxMs = Mathf.Max(bakeMaxMs, ms); baked++;
                bakeStartedAt = 0f;
            }
            // 밀려 있을 때가 이 줄이 가장 필요한 때다 — 아래로 못 내려가도 찍는다.
            Report();
            if (Time.time < nextBuild) return;
            // 굽느라 이번 차례를 거른다. 세지 않으면 로그에서 그냥 사라져서, 기계가 쉬지
            // 못하고 있는 것과 세계가 그대로라 건너뛴 것이 똑같이 "안 구웠다" 로 보인다.
            if (IsBuilding) { stalledFrames++; return; }
            // Work acceptance (tool pickup/boarding) queries paths before its first Tick.
            // Keep discovering changed obstacles while AI exists, even while it is idle;
            // otherwise the first order after an idle period can reject a now-open route.
            if (!NetPlayer.HasAiPlayers) return;
            // 무엇을 했는지 되묻지 않고 그대로 돌려받는다 — BuildCount 증가 여부로 역추론하면
            // 나중에 조기 반환이 하나 늘 때 그것이 조용히 "게이트가 일했다" 로 집계된다.
            if (Rebuild(new Bounds(transform.position, new Vector3(512, 128, 512))) == RebuildOutcome.Baked)
                bakeStartedAt = Time.realtimeSinceStartup;
            else unchanged++;
            nextBuild = Time.time + 2f;
        }

        int baked, unchanged, stalledFrames;
        float bakeStartedAt, bakeTotalMs, bakeMaxMs, nextReport;

        void OnEnable() => nextReport = Time.realtimeSinceStartup + 30f;

        /// <summary>
        /// 30초마다 한 줄 (S15P21B202-704).
        ///
        /// <para><b>왜 프로파일러 표식만으로는 모자라나.</b> <c>ProfilerMarker</c> 는 프로파일러를
        /// 붙였을 때만 보인다. 그런데 이 값이 가장 필요한 자리는 <b>배포된 QA 빌드</b>다 —
        /// 2026-09-22 에 "AI 를 켜면 호스트가 렉이다" 를 원인까지 좁히는 데 하루가 걸렸고,
        /// 그 이유가 <b>얼마나 자주 굽는지 아무도 몰랐던 것</b>이었다. QA 하는 사람 PC 에
        /// 프로파일러를 붙일 수는 없다.</para>
        ///
        /// <para><b>"안 구웠다" 를 둘로 나눠 센다.</b> 둘은 정반대의 뜻인데 한 칸에 섞으면
        /// 어느 쪽인지 알 수 없다.</para>
        /// <list type="bullet">
        /// <item><c>그대로라 건너뜀</c> — 게이트가 일한 것이다. <b>0회면 게이트가 아무것도
        /// 막지 못하고 있다</b>는 뜻이고, 그때는 무엇이 매 주기 흔들리는지부터 봐야 한다.</item>
        /// <item><c>굽느라 밀린 프레임</c> — 기계가 <b>쉬지 못하고 있다</b>. 앞의 값이 아무리
        /// 커도 이것이 크면 렉은 그대로다.</item>
        /// </list>
        ///
        /// <para><b>굽는 시간이 판단 근거다.</b> 범위(512m)와 복셀(12cm)을 줄일지는 이 값을
        /// 보고 정한다 — 둘 다 길찾기 품질을 바꾸는 결정이라 재지 않고 손대면 안 된다.
        /// <c>ProfilerMarker</c> 로는 볼 수 없다. 프로파일러를 붙였을 때만 보이는데, 이 값이
        /// 가장 필요한 자리는 <b>QA 하는 사람의 PC</b>다.</para>
        ///
        /// <para>매 주기 찍지 않는다 — 2초마다면 그 줄이 로그를 못 읽게 만든다. 첫 30초도
        /// 버리지 않는다(<see cref="OnEnable"/> 이 기준을 잡아 둔다) — 시작 직후가 가장 자주
        /// 굽는 구간이라 거기를 버리면 제일 나쁜 수치를 못 본다.</para>
        /// </summary>
        void Report()
        {
            if (Time.realtimeSinceStartup < nextReport) return;
            nextReport = Time.realtimeSinceStartup + 30f;
            Debug.Log($"[AiNav] 30초: 구움 {baked}회 · 평균 {(baked > 0 ? bakeTotalMs / baked : 0f):F0}ms"
                      + $" · 최대 {bakeMaxMs:F0}ms · 그대로라 건너뜀 {unchanged}회"
                      + $" · 굽느라 밀린 프레임 {stalledFrames} · 소스 {sources.Count}개"
                      + $" · 대체 박스 {ApproximateMeshCount}개");
            baked = unchanged = stalledFrames = 0;
            bakeTotalMs = bakeMaxMs = 0;
        }

        public RebuildOutcome Rebuild(Bounds region, bool force = false)
        {
            if (IsBuilding) return RebuildOutcome.Busy;
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
                    // **베이커와 같은 판단을 쓴다** (S15P21B202-690) — 갈리면 조용히 나빠진다.
                    //
                    // **파츠는 여기서 찾지 않는다** (S15P21B202-704). 판단하는 쪽이 이미 찾으므로
                    // 그 값을 돌려받는다 — 미리 찾아 넘기면 걸러질 콜라이더까지 탐색하게 된다.
                    if (!UsedForNavigation(collider, out var part)) { sources.RemoveAt(i); continue; }
                    // Parts block routes; their tops are not walking surfaces.
                    if (part != null) source.area = 1;
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
            if (!force && hasBuilt && bounds.Equals(region) && SameSources()) return RebuildOutcome.Unchanged;
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
            return RebuildOutcome.Baked;
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
            baked = unchanged = stalledFrames = 0;
            bakeStartedAt = bakeTotalMs = bakeMaxMs = 0;
            sources.Clear(); previousSources.Clear();
        }
    }
}
