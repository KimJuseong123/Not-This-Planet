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
        /// <summary>
        /// 지난번 <b>구운</b> 소스를 값으로 (S15P21B202-737). 자리대로 한 벌, 정렬해서 한 벌 —
        /// 순서만 다른 것을 "바뀌었다" 로 읽지 않으려면 정렬한 쪽이 있어야 한다.
        /// </summary>
        readonly List<AiNavigationSources.Key> keys = new(), bakedKeys = new(), bakedSorted = new(), currentSorted = new();
        readonly List<bool> sourceIsPart = new();
        readonly System.Diagnostics.Stopwatch watch = new();
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
        public enum RebuildOutcome { Busy, Unchanged, Reordered, Deferred, Baked }

        /// <summary>
        /// AI 가 쉬는 동안 바뀐 세계를 이만큼 묵힌 뒤에 굽는다(초) (S15P21B202-737).
        ///
        /// <para>쉬는 동안에는 아무도 길을 안 묻는다(요청자 없는 AI 는 멈춰 선다). 명령이 오면 그 순간
        /// <see cref="AiNavigationSources.Wakes"/> 가 곧바로 굽게 하므로, 이 값은 "명령 없이 길을 묻는 길"
        /// (디버그 버튼)이 볼 수 있는 가장 낡은 정도다 — 로그의 "쉬는 중 묵은 길 물음" 이 그것을 센다.</para>
        /// </summary>
        public const float IdleStaleSeconds = 10f;

        /// <summary>
        /// 복셀 한 칸(m). 굽는 설정과 로그의 "반 복셀 안" 이 같은 값을 본다. <b>값은 그대로다</b> — 바꾸는 것은
        /// 길찾기 품질을 바꾸는 결정이라 재지 않고 손대지 않는다.
        /// </summary>
        public const float VoxelSize = .12f;

        void Update()
        {
            if (!NetworkServer.active || AiHostSettings.ServerState != AiRoomState.Active) return;
            // 굽는 동안의 프레임과 평소 프레임을 따로 잰다 (S15P21B202-737). "AI 를 켜면 끊긴다" 가 굽기
            // 탓인지는 두 값을 견줘야 안다 — 굽기는 다른 코어에서 돌아 밀린 프레임이 0 이어도 느려질 수 있다.
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (IsBuilding) Add(ref bakingFrames, frameMs); else Add(ref quietFrames, frameMs);
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
            // **쓸 일이 생긴 순간 기다리지 않고 굽는다** (S15P21B202-737). V 를 누르거나 계획을 세우기
            // 시작하면 다음 2초 점검을 기다리지 않는다 — 계획이 서는 데 몇 초가 걸리므로 첫 길을 물을
            // 때는 새 길이다. 이 줄이 주기 검사 **앞**에 있어야 깨우기가 뜻이 있다.
            var demand = CurrentDemand();
            if (AiNavigationSources.Wakes(lastDemand, demand)) nextBuild = 0;
            lastDemand = demand;
            if (Time.time < nextBuild) return;
            // 굽느라 이번 차례를 거른다. 세지 않으면 로그에서 그냥 사라져서, 기계가 쉬지
            // 못하고 있는 것과 세계가 그대로라 건너뛴 것이 똑같이 "안 구웠다" 로 보인다.
            if (IsBuilding) { stalledFrames++; return; }
            // Work acceptance (tool pickup/boarding) queries paths before its first Tick.
            // Keep discovering changed obstacles while AI exists, even while it is idle;
            // otherwise the first order after an idle period can reject a now-open route.
            //
            // **쉬는 동안에는 찾기만 하고 굽기는 묵힌다** (S15P21B202-737). 묵히는 것은 Rebuild 가 하고,
            // 여기서는 점검 수를 센다.
            if (!NetPlayer.HasAiPlayers) return;
            CountDemand(lastDemand);
            // 무엇을 했는지 되묻지 않고 그대로 돌려받는다 — BuildCount 증가 여부로 역추론하면
            // 나중에 조기 반환이 하나 늘 때 그것이 조용히 "게이트가 일했다" 로 집계된다.
            // **결과마다 이름 있는 칸이 있다** (S15P21B202-737) — 새 결과가 생기면 여기서 칸을 골라야 한다.
            switch (Rebuild(new Bounds(transform.position, new Vector3(512, 128, 512)), demand: lastDemand))
            {
                case RebuildOutcome.Baked: bakeStartedAt = Time.realtimeSinceStartup; break;
                case RebuildOutcome.Unchanged: unchanged++; break;
                case RebuildOutcome.Reordered: reordered++; break;
                case RebuildOutcome.Deferred: deferred++; break;
                case RebuildOutcome.Busy: stalledFrames++; break;
            }
            nextBuild = Time.time + 2f;
        }

        int baked, unchanged, reordered, deferred, stalledFrames;

        AiNavigationSources.Demand lastDemand = AiNavigationSources.Demand.Idle;
        /// <summary>구운 뒤 바뀐 것을 처음 본 때. 0 이면 바뀐 것이 없다.</summary>
        float changedSince;
        int idleChecks, incomingChecks, workingChecks, idleStaleQueries;
        float idleStaleMax;

        void CountDemand(AiNavigationSources.Demand demand)
        {
            if (demand == AiNavigationSources.Demand.Working) workingChecks++;
            else if (demand == AiNavigationSources.Demand.Incoming) incomingChecks++;
            else idleChecks++;
        }

        /// <summary>서버의 지금 수요. 값 판단은 <see cref="AiNavigationSources.DemandOf"/> 가 한다.</summary>
        static AiNavigationSources.Demand CurrentDemand()
        {
            // 매 프레임 돈다 — 인터페이스 foreach 는 열거자를 박싱해 프레임마다 할당하므로 번호로 돈다.
            bool aiBusy = false, hearing = false;
            var players = NetPlayer.AllServerPlayers;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                if (player.IsAi) aiBusy |= player.AiBusy;
                else hearing |= player.AiHearingOpen;
            }
            bool planning = AiLocalRuntime.Current != null && AiLocalRuntime.Current.Busy;
            return AiNavigationSources.DemandOf(aiBusy, planning, hearing);
        }
        float bakeStartedAt, bakeTotalMs, bakeMaxMs, nextReport;

        /// <summary>합 · 최대 · 횟수. 30초마다 "평균/최대" 로 적고 비운다.</summary>
        struct Span { public float Sum, Max; public int Count; }
        Span collectCost, filterCost, compareCost, scheduleCost, bakingFrames, quietFrames;
        static void Add(ref Span span, float ms) { span.Sum += ms; span.Max = Mathf.Max(span.Max, ms); span.Count++; }
        static string Of(Span span) => (span.Count > 0 ? span.Sum / span.Count : 0f).ToString("0.0") + "/" + span.Max.ToString("0.0") + "ms";
        float Lap() { float ms = (float)watch.Elapsed.TotalMilliseconds; watch.Restart(); return ms; }

        /// <summary>30초 동안 "바뀌었다" 가 무엇이었는지 (S15P21B202-737).</summary>
        AiNavigationSources.Tally window;

        /// <summary>조종 중이라 뺀 소스 수 — 이번 수집 · 30초 중 최대와 그 파츠.</summary>
        int controlledNow, controlledMax;
        Object controlledOwner, controlledMaxOwner;

        /// <summary>
        /// 이 파츠를 지금 누가 조종하고 있는가 (S15P21B202-737). 판정 자체는 값으로 시험하는
        /// <see cref="AiNavigationSources.MovesUnderControl"/> 이고, 여기서는 상태를 읽기만 한다.
        /// </summary>
        static bool UnderControl(SpaceshipPart part)
            => AiNavigationSources.MovesUnderControl(part.State,
                   part.TryGetComponent(out TransportVehicle vehicle) && vehicle.IsOccupied);

        void Accumulate(AiNavigationSources.Tally t)
        {
            window.PartMoved += t.PartMoved; window.PartShape += t.PartShape; window.OtherChanged += t.OtherChanged;
            window.Added += t.Added; window.Removed += t.Removed; window.WithinHalfVoxel += t.WithinHalfVoxel;
            if (t.MaxShift > window.MaxShift) { window.MaxShift = t.MaxShift; window.MaxShiftOwner = t.MaxShiftOwner; }
        }

        /// <summary>로그에 적는 주인 이름. 파츠면 파츠 이름 — 콜라이더 자식 이름은 네 파츠가 다 비슷하다.</summary>
        static string Owner(Object owner)
        {
            if (owner == null) return "?";
            var part = owner is Component c ? c.GetComponentInParent<SpaceshipPart>() : null;
            return part != null && part != owner ? part.name + "/" + owner.name : owner.name;
        }

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
            // **무엇이 바뀌어서 구웠는지** (S15P21B202-737). 첫 줄은 그대로 둔다 — 전의 QA 로그와 견줄 수
            // 있어야 한다. 이 줄이 없어서 이슈가 원인을 운석 흔적으로 잘못 짚었다.
            Debug.Log($"[AiNav] 30초 원인: 파츠 이동 {window.PartMoved}회 · 파츠 모양 {window.PartShape}회"
                      + $"(+{window.Added}/-{window.Removed}) · 그 밖의 변화 {window.OtherChanged}회"
                      + $" · 순서만 달라 건너뜀 {reordered}회 · 최대 이동 {window.MaxShift:F3}m({Owner(window.MaxShiftOwner)})"
                      + $" · 반 복셀({VoxelSize * 50f:0}cm) 안 {window.WithinHalfVoxel}회"
                      + $" · 조종 중이라 뺀 소스 최대 {controlledMax}개({Owner(controlledMaxOwner)})");
            // **굽기 말고 메인 스레드가 내는 값** (S15P21B202-737). 수집·거르기·비교는 굽는 ms 에 안 들어간다.
            Debug.Log($"[AiNav] 30초 비용: 수집 {Of(collectCost)} · 거르기 {Of(filterCost)} · 비교 {Of(compareCost)}"
                      + $" · 예약 {Of(scheduleCost)} · 굽는 중 프레임 {Of(bakingFrames)}({bakingFrames.Count})"
                      + $" · 평소 프레임 {Of(quietFrames)}({quietFrames.Count})");
            // **쉬는 동안 얼마나 미뤘고, 그 사이 누가 묵은 길을 물었는지** (S15P21B202-737). 묵은 길 물음이
            // 0 이 아니면 명령 없이 길을 묻는 길이 있다는 뜻이다 — 미루는 값을 줄이거나 그 길에서 깨워야 한다.
            Debug.Log($"[AiNav] 30초 까닭: 점검 쉼/예고/작업 {idleChecks}/{incomingChecks}/{workingChecks}"
                      + $" · 쉬는 중이라 미룸 {deferred}회 · 쉬는 중 묵은 길 물음 {idleStaleQueries}회(최대 {idleStaleMax:0.0}s)");
            baked = unchanged = reordered = deferred = stalledFrames = 0;
            idleChecks = incomingChecks = workingChecks = idleStaleQueries = 0; idleStaleMax = 0;
            bakeTotalMs = bakeMaxMs = 0;
            window = default;
            controlledMax = 0; controlledMaxOwner = null;
            collectCost = filterCost = compareCost = scheduleCost = bakingFrames = quietFrames = default;
        }

        /// <param name="demand">지금 AI 가 길을 쓸 일이 있는가. 쉬고 있으면 바뀐 세계를
        /// <see cref="IdleStaleSeconds"/> 만큼 묵힌 뒤에 굽는다. 직접 부르는 쪽(시험 · 강제 굽기)은 기본값
        /// <c>Working</c> 이라 미루지 않는다.</param>
        public RebuildOutcome Rebuild(Bounds region, bool force = false,
                                      AiNavigationSources.Demand demand = AiNavigationSources.Demand.Working)
        {
            if (IsBuilding) return RebuildOutcome.Busy;
            hasBuilt = Ready;
            if (!loadedGeometry)
            {
                geometry = Resources.Load<AiNavigationGeometry>("AiNavigationGeometry");
                loadedGeometry = true;
            }
            sources.Clear();
            watch.Restart();
            using (CollectMarker.Auto())
                NavMeshBuilder.CollectSources(region, PermanentLayerMask, NavMeshCollectGeometry.PhysicsColliders,
                    0, markups, sources);
            Add(ref collectCost, Lap());
            ApproximateMeshCount = 0;
            controlledNow = 0;
            sourceIsPart.Clear();
            for (int i = 0; i < sources.Count; i++) sourceIsPart.Add(false);
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
                    if (!UsedForNavigation(collider, out var part)) { sources.RemoveAt(i); sourceIsPart.RemoveAt(i); continue; }
                    // **조종 중인 파츠는 굽지 않는다** (S15P21B202-737). 견인 빔에 들렸거나 누가 타고 있으면
                    // 물리 한 걸음마다 자세가 바뀌어 게이트를 매 주기 연다 — 2026-09-23 로그에서 Part_4 를
                    // 타는 동안 30초에 15/15번 구웠다. 그 자리의 구멍은 2초 뒤면 틀린 자리이고, 가까이서
                    // 부딪히지 않는 것은 걸음 검사(SafeStep)가 이미 파츠를 장애물로 본다. 내려놓이면 다시
                    // 소스가 되어 **한 번** 굽는다.
                    //
                    // UsedForNavigation 에 넣지 않는다 — 에디터 베이커가 같은 함수를 쓰는데, 거기서 빠지면
                    // 읽기용 사본이 안 구워져 실제 바닥이 대체 박스(장애물)가 된다(S15P21B202-690).
                    if (part != null && UnderControl(part))
                    { sources.RemoveAt(i); sourceIsPart.RemoveAt(i); controlledNow++; controlledOwner = part; continue; }
                    // Parts block routes; their tops are not walking surfaces.
                    if (part != null) { source.area = 1; sourceIsPart[i] = true; }
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
            if (controlledNow > controlledMax) { controlledMax = controlledNow; controlledMaxOwner = controlledOwner; }
            keys.Clear();
            for (int i = 0; i < sources.Count; i++) keys.Add(new AiNavigationSources.Key(sources[i], sourceIsPart[i]));
            Add(ref filterCost, Lap());
            // Keep the discovery pass: doors, spawned scenery and destroyed colliders need no
            // registration contract. Unchanged geometry must not schedule another native build.
            //
            // **순서만 다른 것은 굽지 않는다** (S15P21B202-737) — 네이티브 수집이 같은 순서를 준다는
            // 보장이 없다. 스냅숏은 **굽기로 정한 뒤에만** 바꾼다: 건너뛰는 길에서 바꾸면 조금씩 움직이는
            // 것이 "지난번과 같다" 로 이어져 한 번도 안 굽게 된다.
            bool comparable = !force && hasBuilt && bounds.Equals(region);
            var verdict = comparable ? AiNavigationSources.Compare(bakedKeys, bakedSorted, keys, currentSorted)
                                     : AiNavigationSources.Verdict.Changed;
            Add(ref compareCost, Lap());
            if (verdict == AiNavigationSources.Verdict.Same) { changedSince = 0; return RebuildOutcome.Unchanged; }
            if (verdict == AiNavigationSources.Verdict.Reordered) { changedSince = 0; return RebuildOutcome.Reordered; }
            // 바뀌었다. 쉬는 중이면 묵힌다 — 스냅숏은 그대로 두어 다음 점검도 "바뀌었다" 로 본다.
            if (comparable)
            {
                if (changedSince <= 0) changedSince = Time.realtimeSinceStartup;
                if (!AiNavigationSources.BakeDue(demand, Time.realtimeSinceStartup - changedSince, IdleStaleSeconds))
                    return RebuildOutcome.Deferred;
            }
            changedSince = 0;
            if (!comparable) AiNavigationSources.Sort(keys, currentSorted);
            else Accumulate(AiNavigationSources.Diff(bakedSorted, currentSorted, VoxelSize));
            bounds = region;
            bakedKeys.Clear(); bakedKeys.AddRange(keys);
            bakedSorted.Clear(); bakedSorted.AddRange(currentSorted);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .55f;
            settings.agentHeight = 1.8f;
            // PlayerRig is force-driven and has no automatic step-up. Route around curbs to ramps.
            settings.agentClimb = .05f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = VoxelSize;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            if (data == null)
            {
                data = new NavMeshData(settings.agentTypeID);
                instance = NavMesh.AddNavMeshData(data);
            }
            watch.Restart();
            using (BuildMarker.Auto())
                building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
            Add(ref scheduleCost, Lap());
            BuildCount++;
            return RebuildOutcome.Baked;
        }

        public bool CalculatePath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            // 묵히는 동안 누가 길을 물으면 센다 (S15P21B202-737). 명령으로 오는 길은 깨우기가 먼저 굽는다 —
            // 그래도 여기 걸리면 그 길이 깨우기를 거치지 않는다는 뜻이다.
            if (changedSince > 0)
            {
                idleStaleQueries++;
                idleStaleMax = Mathf.Max(idleStaleMax, Time.realtimeSinceStartup - changedSince);
            }
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
            baked = unchanged = reordered = stalledFrames = 0;
            bakeStartedAt = bakeTotalMs = bakeMaxMs = 0;
            window = default;
            controlledNow = controlledMax = 0; controlledOwner = controlledMaxOwner = null;
            deferred = idleChecks = incomingChecks = workingChecks = idleStaleQueries = 0; idleStaleMax = 0;
            changedSince = 0; lastDemand = AiNavigationSources.Demand.Idle;
            collectCost = filterCost = compareCost = scheduleCost = bakingFrames = quietFrames = default;
            sources.Clear(); sourceIsPart.Clear();
            keys.Clear(); bakedKeys.Clear(); bakedSorted.Clear(); currentSorted.Clear();
        }
    }
}
