using System;
using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 우주선 몸체에 쌓이는 용접 비드. 기획(S15P21B202-107 §16.2)의 "용접한 구간에는
    /// 실제 금속 비드와 불탄 자국이 남는다" 를 물리 있는 물체로 만든 것이다.
    ///
    /// 한 자리에 계속 대고 있으면 덩어리가 커지고, 상한에 이르면 그 위에 다음 덩어리가
    /// 시작되어 기둥이 뻗는다. 다른 몸체에 닿으면 두 몸체가 하나로 합쳐진다.
    ///
    /// <b>예전에는 원형 접합선이 따로 있었다.</b> 파츠 밑면의 정해진 자리를 훑어 채우는
    /// 방식이었고, S15P21B202-230 이 그것을 걷어냈다 — 자국·강도·피드백을 비드가 모두
    /// 갖게 되어 같은 일을 하는 장치가 둘이 되었기 때문이다.
    ///
    /// <b>강체를 늘리지 않는다.</b> 덩어리마다 Rigidbody 를 주고 조인트로 이으면 가늘고 긴
    /// 조인트 사슬이 되는데, PhysX 에서 그런 사슬은 위에 얹힌 406~745kg 짜리 파츠를 버티지
    /// 못하고 떨거나 터진다. 대신 덩어리를 <b>자란 파츠의 자식 콜라이더</b>로 둔다.
    ///
    /// <code>
    /// Part_1 (Rigidbody 하나)
    ///  ├ Body_0..2        ← 원래 컴파운드 콜라이더 (S15P21B202-166)
    ///  └ WeldBeads
    ///     └ Bead_0_0, Bead_0_1 ...   ← 자란 비드 (콜라이더 + 렌더러, 강체 없음)
    /// </code>
    ///
    /// <b>무게중심은 손대지 않는다.</b> 이 프로젝트는 <c>Rigidbody.centerOfMass</c> 를 어디에서도
    /// 대입하지 않는다. 자식 콜라이더가 늘면 Unity 가 알아서 다시 계산하므로, 비드를 콜라이더로
    /// 붙이는 것만으로 무게중심이 따라온다. 질량만 <see cref="SpaceshipPart.SetExtraMass"/> 로
    /// 넘긴다.
    ///
    /// 그 둘이 <b>따로 논다</b> 는 것을 알아 둔다 — 자동 무게중심은 우리가 정한 질량이 아니라
    /// <b>콜라이더 부피</b>로 계산된다(밀도가 균일하다고 본다). 그래서 비드 질량을 아무리 크게
    /// 잡아도 무게중심은 부피만큼만 움직인다. 파츠 상자 33m³ 에 비드 한 덩어리가 0.004m³ 이므로
    /// 실제 이동량은 밀리미터 단위다 — 3.7m 짜리 파츠에서 그것이 맞는 크기다.
    ///
    /// 만들어진 순서는 이렇다 — S15P21B202-226 이 쌓이는 것, 228 이 닿으면 합쳐지는 것,
    /// 229 가 탑 판정을 그래프로, 230 이 원형 접합선 제거다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeldBead : MonoBehaviour
    {
        /// <summary>
        /// 비드를 가진 파츠들. <see cref="PartSocket.All"/> 과 같은 방식이다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로 <see cref="Register"/> 를
        /// 직접 부른다.
        /// </summary>
        public static readonly List<WeldBead> All = new();

        /// <summary>비드를 모아 두는 자식의 이름. 씬에서 눈으로 찾을 때 쓴다.</summary>
        public const string ContainerName = "WeldBeads";

        /// <summary>
        /// 물리용 구체 아래에 붙는 표시 전용 외형의 이름.
        /// 구체 콜라이더와 렌더러를 분리해 접합 물리는 유지하면서 모양만 바꾼다.
        /// </summary>
        public const string VisualName = "WeldVisual";

        const int ProbeSize = 32;
        static readonly Collider[] Probe = new Collider[ProbeSize];
        static MaterialPropertyBlock appearanceBlock;
        const float BeadCoolingSeconds = 1.25f;

        /// <summary>
        /// 한 자리에서 이어진 덩어리들. 방향은 <see cref="_container"/> 기준이다.
        ///
        /// 직렬화한다 — 비드는 GameObject 로 씬에 남는데 사슬 구조를 잃으면 로드 뒤에
        /// 이어 붙일 수 없어, 이미 쌓은 기둥 옆에 새 기둥이 생긴다.
        /// </summary>
        [Serializable]
        sealed class Chain
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public bool HasBrushFrom;
            public Vector3 BrushFrom;
            public float TipRadius;
            public List<Transform> Blobs = new();

            /// <summary>이 사슬이 닿아 있는 다른 파츠. 없으면 null(S15P21B202-228).</summary>
            public SpaceshipPart Touched;
            [NonSerialized] public ShipAssembly TouchedPad;

            /// <summary>
            /// 상대 몸체에 닿아 있는 덩어리를 <b>최대 크기 환산</b>으로 센 값.
            ///
            /// 개수가 아니라 환산값인 이유는 자라는 중인 끝 덩어리가 부피비만큼만
            /// 세어져야 하기 때문이다 — 그러지 않으면 덩어리가 생기는 순간 강도가
            /// 한 칸 뛴다.
            /// </summary>
            public float ContactBlobs;

            [NonSerialized] public bool? AppliedHelpfulColor;
            [NonSerialized] public float HeatedAt = -999f;
            [NonSerialized] public float AppliedHeat = -1f;
        }

        [SerializeField] List<Chain> _chains = new();

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _owner;

        [SerializeField] Transform _container;

        bool _initialized;
        FixedJoint _padJoint;
        ShipAssembly _weldedPad;
        public bool IsWeldedToPad => _weldedPad != null && _padJoint != null;
        public float PadWeldRatio { get; private set; }

        /// <summary>비드가 붙어 있는 파츠.</summary>
        public SpaceshipPart Owner
        {
            get
            {
                Initialize();
                return _owner;
            }
        }

        /// <summary>이 파츠에 쌓인 덩어리 총 개수.</summary>
        public int BlobCount
        {
            get
            {
                Initialize();

                var total = 0;
                for (var i = 0; i < _chains.Count; i++) total += _chains[i].Blobs.Count;

                return total;
            }
        }

        /// <summary>사슬(기둥) 개수.</summary>
        public int ChainCount
        {
            get
            {
                Initialize();
                return _chains.Count;
            }
        }


        // ── 상태 읽기·복원 (S15P21B202-386) ──────────────────────────
        //
        // 용접은 지금까지 "이 자리에서 이만큼 자랐다" 는 **행동**으로만 전달됐다
        // (WorldSync 의 RpcWeldGrown). 처음부터 같이 있던 사람은 그 행동들을 다 들어
        // 같은 모양이 되지만, **중간에 들어온 사람은 그 전 행동을 못 들어 아무 비드도
        // 없다.** "지금 이만큼 되어 있다" 를 말할 수단이 없었다.
        //
        // 성장을 합쳐 한 번에 보내는 압축은 틀리다 — GrowChain 은 한 번 부를 때 덩어리를
        // 최대 하나만 올리므로, 열 번을 한 번으로 합치면 결과가 다르다. 그래서 행동이
        // 아니라 **상태**를 주고받는다.

        /// <summary>
        /// 사슬 하나를 다시 세우는 데 필요한 값. 시작점·방향은 <b>컨테이너 로컬</b>이라
        /// 파츠가 움직여도 따라간다.
        ///
        /// <para><b>접촉 파츠는 들어 있지 않다.</b> <see cref="RefreshContact"/> 가 형상으로
        /// 스스로 찾으므로 보낼 필요가 없다.</para>
        /// </summary>
        [Serializable]
        public struct ChainState
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public bool HasBrushFrom;
            public Vector3 BrushFrom;
            public float TipRadius;
            public int BlobCount;
            public bool PadWeldIntact;
        }

        /// <summary>엔딩에서도 해당 사슬의 실제 접합 기여 색상을 보존한다.</summary>
        public bool IsChainContributing(int index)
        {
            Initialize();
            if (index < 0 || index >= _chains.Count) return false;
            var chain = _chains[index];
            var link = chain.Touched != null && chain.ContactBlobs > 0f
                ? PartAssemblyLink.FindBetween(_owner, chain.Touched) : null;
            return (link != null && link.IsIntact)
                || (chain.TouchedPad != null && chain.TouchedPad == _weldedPad && IsWeldedToPad);
        }

        /// <summary>사슬 하나의 상태를 읽는다. 서버가 스냅샷을 만들 때 쓴다.</summary>
        public ChainState ReadChain(int index)
        {
            Initialize();

            var chain = _chains[index];
            return new ChainState
            {
                Origin = chain.Origin,
                Direction = chain.Direction,
                HasBrushFrom = chain.HasBrushFrom,
                BrushFrom = chain.BrushFrom,
                TipRadius = chain.TipRadius,
                BlobCount = chain.Blobs != null ? chain.Blobs.Count : 0,
                PadWeldIntact = chain.TouchedPad != null && IsWeldedToPad,
            };
        }

        /// <summary>
        /// 받은 상태로 사슬을 통째로 다시 세운다. <b>기존 사슬은 버린다.</b>
        ///
        /// <para>클라이언트에서만 부른다 — 서버는 원본을 갖고 있다. 만드는 순서는
        /// <see cref="StartChain"/> 과 같다(사슬 추가 → 덩어리 → 질량 → 접촉).</para>
        /// </summary>
        public void RestoreChains(ChainState[] states, int offset, int count)
        {
            Initialize();

            ClearChains();
            if (states == null) return;
            var padWeldIntact = false;

            for (var i = 0; i < count; i++)
            {
                var at = offset + i;
                if (at < 0 || at >= states.Length) break;

                var state = states[at];
                if (state.BlobCount <= 0) continue;
                padWeldIntact |= state.PadWeldIntact;

                var chain = new Chain
                {
                    Origin = state.Origin,
                    Direction = state.Direction,
                    HasBrushFrom = state.HasBrushFrom,
                    BrushFrom = state.BrushFrom,
                    TipRadius = state.TipRadius,
                    HeatedAt = -999f,
                };

                _chains.Add(chain);
                for (var b = 0; b < state.BlobCount; b++) SpawnBlob(chain, b);
                RefreshContact(_chains.Count - 1);
            }

            ApplyMass();
            if (!padWeldIntact) ReleasePadWeld();
        }

        /// <summary>덩어리와 사슬을 전부 치운다. 다시 세우기 전에 부른다.</summary>
        void ClearChains()
        {
            ReleasePadWeld();
            for (var i = 0; i < _chains.Count; i++)
            {
                var chain = _chains[i];
                if (chain?.Blobs == null) continue;

                // 편집 모드에서 Destroy 는 에러만 남기고 아무 일도 하지 않는다 —
                // 이 클래스가 이미 갖고 있는 DestroyObject 를 쓴다.
                for (var b = 0; b < chain.Blobs.Count; b++) DestroyObject(chain.Blobs[b]);
            }

            _chains.Clear();
        }
        /// <summary>현재 파츠 사이에 살아 있는 비드 다리가 있는가.</summary>
        public bool HasLiveBridge
        {
            get
            {
                Initialize();
                if (IsWeldedToPad) return true;
                for (var i = 0; i < _chains.Count; i++)
                    if (_chains[i] != null && _chains[i].Touched != null
                        && _chains[i].Blobs != null && _chains[i].Blobs.Count > 0)
                        return true;
                return false;
            }
        }

        /// <summary>비드 전체의 질량(kg). 파츠에 더해진 값과 같아야 한다.</summary>
        public float Mass
        {
            get
            {
                Initialize();

                var total = 0f;
                for (var i = 0; i < _chains.Count; i++)
                {
                    var chain = _chains[i];
                    var tip = chain.Blobs.Count - 1;

                    for (var b = 0; b < chain.Blobs.Count; b++)
                        total += MassOf(b == tip ? chain.TipRadius : PhysicsScale.WeldBeadMaxRadius);
                }

                return total;
            }
        }

        void OnEnable() => Register();

        void OnDisable() => Unregister();
        void OnDestroy() => ReleasePadWeld();

        void LateUpdate()
        {
            // 접합 파손과 네트워크 링크 복원도 반영한다. 물리 접촉을 다시 재거나
            // 접합을 만들지 않고, 기존 판정에 따라 외형만 갱신한다.
            for (var i = 0; i < _chains.Count; i++) RefreshAppearance(_chains[i]);
        }

        /// <summary>
        /// 소유 파츠와 비드 컨테이너를 찾는다. 여러 번 불러도 안전하다.
        /// <c>Awake</c> 는 EditMode 에서 호출되지 않으므로 도구와 검증이 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_owner == null) _owner = GetComponentInParent<SpaceshipPart>();
            EnsureContainer();
            Register();
        }

        public void Register()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        /// <summary>
        /// 비드를 담는 자식. <b>부모의 스케일을 되돌린다.</b>
        ///
        /// 파츠 루트의 스케일이 1 이 아닐 수 있다(검증 리그가 그렇게 만든다). 그 밑에
        /// 그냥 붙이면 구가 타원이 되고 SphereCollider 가 어느 축을 따르는지도 알 수 없다.
        /// 컨테이너에서 역스케일을 걸면 <b>컨테이너 안이 곧 미터 단위 월드 스케일</b>이 되어
        /// 사슬 좌표를 그대로 미터로 쓸 수 있다.
        /// </summary>
        void EnsureContainer()
        {
            if (_container == null)
            {
                var existing = transform.Find(ContainerName);
                _container = existing != null ? existing : new GameObject(ContainerName).transform;
                _container.SetParent(transform, false);
                _container.localPosition = Vector3.zero;
                _container.localRotation = Quaternion.identity;
            }

            var scale = transform.lossyScale;
            _container.localScale = new Vector3(
                Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
                Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
                Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
        }

        // --- 찾기 ---------------------------------------------------------

        /// <summary>
        /// <paramref name="nozzle"/> 에서 사거리 안에 있는 우주선 몸체에 비드를 쌓는다.
        ///
        /// 사거리는 <see cref="PhysicsScale.WeldRange"/>(0.80m) 다. 용접과 절단이 같은
        /// 값을 써야 한다 — 한쪽만 되는 거리가 생기면 플레이어가 이유를 알 수 없다
        /// (S15P21B202-209 에서 정한 규칙이다).
        ///
        /// <b>조준 방향을 보지 않는다.</b> 노즐 위치만으로 판정하는 것은 S15P21B202-206 이
        /// 접합선에서 정한 규칙이고, 비드도 같아야 "닿았는데 안 되는" 각도가 생기지 않는다.
        /// 기둥이 뻗는 방향은 대신 <b>표면의 바깥 방향</b>으로 정한다.
        /// </summary>
        /// <returns>이번 틱에 실제로 자랐는가. 불꽃·소리가 붙을 신호다.</returns>
        public static bool TryGrowNear(Vector3 nozzle, float deltaTime, out WeldBead bead)
        {
            bead = null;
            if (deltaTime <= 0f) return false;

            // 이미 자라던 기둥이 가까이 있으면 그것을 잇는다. 이 판정이 없으면 좌클릭할
            // 때마다 새 기둥이 생겨 아무것도 길어지지 않는다.
            var existing = FindChainToExtend(nozzle);
            if (existing.Bead != null)
            {
                bead = existing.Bead;
                return bead.GrowChain(existing.Index, deltaTime);
            }

            if (!TryFindBodySurface(nozzle, out var part, out var point, out var outward))
                return false;

            bead = Attach(part);
            return bead.StartChain(point, outward);
        }

        /// <summary>
        /// <paramref name="nozzle"/> 에서 사거리 안의 비드를 깎는다(기획 §16.3 절단).
        ///
        /// 용접만 되고 절단이 안 되면 잘못 쌓은 비드를 되돌릴 방법이 없다. 되돌릴 수
        /// 없는 조작은 상호작용으로 두지 않는다.
        /// </summary>
        /// <returns>이번 틱에 실제로 깎였는가.</returns>
        public static bool TryShrinkNear(Vector3 nozzle, float deltaTime, out WeldBead bead)
        {
            bead = null;
            if (deltaTime <= 0f) return false;

            var found = FindChainToExtend(nozzle);
            if (found.Bead == null) return false;

            bead = found.Bead;
            return bead.ShrinkChain(found.Index, deltaTime);
        }

        /// <summary>
        /// 노즐에 가장 가까운 사슬. 없으면 <c>Bead</c> 가 null 이다.
        ///
        /// <b>끝 덩어리가 아니라 사슬의 모든 덩어리와 재어 본다.</b> 끝만 보면 기둥이
        /// 자라 노즐에서 멀어진 순간 사슬을 놓쳐, 같은 자리에 두 번째 기둥이 겹쳐 생긴다.
        /// 플레이어는 가만히 서 있는데 기둥이 갑자기 둘로 갈라지는 셈이다.
        /// </summary>
        static (WeldBead Bead, int Index) FindChainToExtend(Vector3 nozzle)
        {
            WeldBead best = null;
            var bestIndex = -1;
            var bestDistance = PhysicsScale.WeldBeadChainReuseRadius;

            for (var i = 0; i < All.Count; i++)
            {
                var candidate = All[i];
                if (candidate == null) continue;

                candidate.Initialize();

                for (var c = 0; c < candidate._chains.Count; c++)
                {
                    var distance = candidate.DistanceToChain(c, nozzle);
                    if (distance > bestDistance) continue;

                    bestDistance = distance;
                    best = candidate;
                    bestIndex = c;
                }
            }

            return (best, bestIndex);
        }

        /// <summary>사슬 <paramref name="index"/> 의 가장 가까운 덩어리 표면까지의 거리(m).</summary>
        float DistanceToChain(int index, Vector3 point)
        {
            var chain = _chains[index];
            var tip = chain.Blobs.Count - 1;
            var best = float.PositiveInfinity;

            for (var b = 0; b < chain.Blobs.Count; b++)
            {
                var blob = chain.Blobs[b];
                if (blob == null) continue;

                var radius = b == tip ? chain.TipRadius : PhysicsScale.WeldBeadMaxRadius;
                var distance = Vector3.Distance(point, blob.position) - radius;
                if (distance < best) best = distance;
            }

            return best;
        }

        /// <summary>
        /// 사거리 안에서 가장 가까운 <b>우주선 몸체</b>의 표면.
        ///
        /// <b>레이어와 컴포넌트를 둘 다 본다.</b> 레이어만 보면 파츠와 같은 레이어에 있는
        /// 다른 것에도 붙고, 컴포넌트만 보면 파츠의 자식인 고정못(Interactable 레이어)에도
        /// 붙는다. 지형·운석·소품·운반체는 둘 중 어느 조건도 통과하지 못한다.
        ///
        /// 이미 쌓인 비드는 씨앗으로 쓰지 않는다. 비드는 몸체가 아니고, 비드 위에서
        /// 새 기둥이 시작되면 <see cref="SpaceshipPart.WeldBeadBlobsPerChain"/> 로 정한
        /// 최대 다리 길이가 무의미해진다.
        /// </summary>
        static bool TryFindBodySurface(
            Vector3 nozzle, out SpaceshipPart part, out Vector3 point, out Vector3 outward)
        {
            part = null;
            point = Vector3.zero;
            outward = Vector3.up;

            var count = UnityEngine.Physics.OverlapSphereNonAlloc(
                nozzle, PhysicsScale.WeldRange, Probe,
                GameLayers.PartMask, QueryTriggerInteraction.Ignore);

            var bestDistance = PhysicsScale.WeldRange;

            for (var i = 0; i < count; i++)
            {
                var collider = Probe[i];
                if (collider == null || IsBeadCollider(collider)) continue;

                var owner = collider.GetComponentInParent<SpaceshipPart>();
                if (owner == null) continue;

                var surface = collider.ClosestPoint(nozzle);
                var away = nozzle - surface;

                // 노즐이 콜라이더 안에 있으면 ClosestPoint 가 노즐을 그대로 돌려주어
                // 바깥 방향을 정할 수 없다. 기둥 방향을 찍을 수 없으니 건너뛴다.
                if (away.sqrMagnitude < 1e-6f) continue;

                var distance = away.magnitude;
                if (distance > bestDistance) continue;

                bestDistance = distance;
                part = owner;
                point = surface;
                outward = away / distance;
            }

            return part != null;
        }

        /// <summary>레이가 실제로 맞힌 몸체 또는 비드 사슬만 선택한다. 주변 사슬로 조준을 옮기지 않는다.</summary>
        public static bool CanApplyAt(RaycastHit hit, bool cut)
        {
            if (hit.collider == null || hit.collider.isTrigger) return false;
            if (TryHitChain(hit.collider, out var bead, out var index))
            {
                if (cut) return true;
                var chain = bead._chains[index];
                if (chain.TipRadius < PhysicsScale.WeldBeadMaxRadius - 1e-4f) return true;
                return chain.Blobs.Count < bead.Owner.WeldBeadBlobsPerChain &&
                       bead.BlobCount < bead.Owner.WeldBeadsPerPart &&
                       chain.Touched == null && chain.TouchedPad == null;
            }
            if (cut || (GameLayers.PartMask & (1 << hit.collider.gameObject.layer)) == 0) return false;
            var part = hit.collider.GetComponentInParent<SpaceshipPart>();
            if (part == null) return false;
            bead = part.GetComponent<WeldBead>();
            return bead == null || bead.BlobCount < part.WeldBeadsPerPart;
        }

        public static bool TryApplyAt(
            RaycastHit hit, float deltaTime, bool cut, out WeldBead bead,
            WeldBead brushFromBead = null, Vector3 brushFromWorld = default)
        {
            bead = null;
            if (deltaTime <= 0f || !CanApplyAt(hit, cut)) return false;
            if (TryHitChain(hit.collider, out bead, out var index))
                return cut ? bead.ShrinkChain(index, deltaTime) : bead.GrowChain(index, deltaTime);
            bead = Attach(hit.collider.GetComponentInParent<SpaceshipPart>());
            return bead.StartChain(hit.point, hit.normal,
                brushFromBead == bead, brushFromWorld);
        }

        static bool TryHitChain(Collider collider, out WeldBead bead, out int index)
        {
            bead = collider.GetComponentInParent<WeldBead>();
            index = -1;
            if (bead == null) return false;
            for (int c = 0; c < bead._chains.Count; c++)
                foreach (var blob in bead._chains[c].Blobs)
                    if (blob != null && (collider.transform == blob || collider.transform.IsChildOf(blob)))
                    { index = c; return true; }
            return false;
        }

        internal static bool IsBeadCollider(Collider collider)
        {
            var bead = collider.GetComponentInParent<WeldBead>();
            return bead != null && collider.transform.IsChildOf(bead._container != null
                ? bead._container
                : bead.transform);
        }

        /// <summary>파츠에 붙은 비드 관리자를 찾거나 만든다.</summary>
        public static WeldBead Attach(SpaceshipPart part)
        {
            var bead = part.GetComponent<WeldBead>();
            if (bead == null) bead = part.gameObject.AddComponent<WeldBead>();

            bead.Initialize();
            return bead;
        }

        // --- 자라기 -------------------------------------------------------

        /// <summary>사슬 <paramref name="index"/> 의 끝 덩어리의 월드 좌표.</summary>
        public Vector3 TipPosition(int index)
        {
            Initialize();

            var chain = _chains[index];
            var last = chain.Blobs.Count - 1;
            if (last >= 0 && chain.Blobs[last] != null) return chain.Blobs[last].position;

            return _container.TransformPoint(chain.Origin);
        }

        /// <summary>사슬 <paramref name="index"/> 의 끝 덩어리 반지름(m).</summary>
        public float TipRadius(int index)
        {
            Initialize();
            return _chains[index].TipRadius;
        }

        /// <summary>사슬 <paramref name="index"/> 의 덩어리 수.</summary>
        public int BlobsIn(int index)
        {
            Initialize();
            return _chains[index].Blobs.Count;
        }

        bool StartChain(
            Vector3 worldPoint, Vector3 worldOutward,
            bool hasBrushFrom = false, Vector3 brushFromWorld = default)
        {
            if (BlobCount >= Owner.WeldBeadsPerPart) return false;

            var chain = new Chain
            {
                Origin = _container.InverseTransformPoint(worldPoint),
                Direction = _container.InverseTransformDirection(worldOutward).normalized,
                HasBrushFrom = hasBrushFrom,
                BrushFrom = hasBrushFrom
                    ? _container.InverseTransformPoint(brushFromWorld)
                    : _container.InverseTransformPoint(worldPoint),
                TipRadius = PhysicsScale.WeldBeadMinRadius,
                HeatedAt = Time.time,
            };

            _chains.Add(chain);
            SpawnBlob(chain, 0);
            ApplyMass();
            RefreshContact(_chains.Count - 1);

            // 씨앗을 만든 것 자체가 이번 틱의 진행이다. 남은 시간으로 더 키우지 않는 것은
            // 첫 프레임에 최소 크기가 <b>보이게</b> 하려는 것이다 — 진행 바가 없으므로
            // 눈에 보이는 것이 유일한 피드백이다.
            return true;
        }

        bool GrowChain(int index, float deltaTime)
        {
            var chain = _chains[index];
            if (chain.Blobs.Count == 0) return false;

            var full = chain.Blobs.Count >= Owner.WeldBeadBlobsPerChain;
            var atMax = chain.TipRadius >= PhysicsScale.WeldBeadMaxRadius - 1e-4f;

            // 사슬이 상한 길이에 이르고 끝 덩어리도 다 컸으면 여기서는 더 할 일이 없다.
            // 최대 다리 길이가 이 지점에서 성립한다.
            if (full && atMax) return false;

            if (atMax)
            {
                if (BlobCount >= Owner.WeldBeadsPerPart) return false;

                // <b>닿았으면 더 뻗지 않는다.</b> 끝 덩어리는 계속 굵어지지만 다음 덩어리를
                // 얹지는 않는다(S15P21B202-228).
                //
                // 이것이 없으면 덩어리가 상대 몸체 <b>안으로</b> 파고들며 계속 쌓이고,
                // 파묻힌 덩어리가 모두 접촉으로 세어져 기둥 하나만으로 완전 강도가 나온다.
                // 그러면 "여러 군데를 이어야 튼튼하다" 는 규칙이 무력해진다.
                //
                // 자란 것이 없으므로 진행됐다고 보고하지 않는다 — 다리가 걸린 순간 불꽃이
                // 멎는 것이 플레이어에게는 "이어졌다" 는 신호가 된다.
                if (chain.Touched != null || chain.TouchedPad != null) return false;

                chain.TipRadius = PhysicsScale.WeldBeadMinRadius;
                SpawnBlob(chain, chain.Blobs.Count);
                chain.HeatedAt = Time.time;
                ApplyMass();
                RefreshContact(index);
                return true;
            }

            chain.TipRadius = Mathf.Min(
                PhysicsScale.WeldBeadMaxRadius, chain.TipRadius + GrowthRate * deltaTime);

            chain.HeatedAt = Time.time;
            ApplyRadius(chain, chain.Blobs.Count - 1, chain.TipRadius);
            ApplyMass();
            RefreshContact(index);
            return true;
        }

        bool ShrinkChain(int index, float deltaTime)
        {
            var chain = _chains[index];
            if (chain.Blobs.Count == 0) return false;

            // 사슬이 통째로 사라질 수 있으므로 잇고 있던 상대를 먼저 기억한다.
            // 사라진 뒤에 물으면 알 길이 없고, 그러면 접합이 옛 강도로 남는다.
            var bridged = chain.Touched;

            chain.TipRadius -= GrowthRate * deltaTime;

            if (chain.TipRadius > PhysicsScale.WeldBeadMinRadius)
            {
                ApplyRadius(chain, chain.Blobs.Count - 1, chain.TipRadius);
                ApplyMass();
                RefreshContact(index);
                return true;
            }

            // 끝 덩어리가 최소보다 작아지면 지운다. 아래 덩어리가 새 끝이 되고 최대 크기에서
            // 다시 줄어들기 시작한다.
            DestroyObject(chain.Blobs[chain.Blobs.Count - 1]);
            chain.Blobs.RemoveAt(chain.Blobs.Count - 1);
            chain.TipRadius = PhysicsScale.WeldBeadMaxRadius;

            var removed = chain.Blobs.Count == 0;
            if (removed) _chains.RemoveAt(index);

            ApplyMass();

            if (removed)
            {
                if (bridged != null) ApplyStrengthTo(bridged);
                ApplyPadStrength();
            }
            else
            {
                RefreshContact(index);
            }

            return true;
        }

        /// <summary>최소에서 최대까지 자라는 속도(m/초).</summary>
        static float GrowthRate =>
            (PhysicsScale.WeldBeadMaxRadius - PhysicsScale.WeldBeadMinRadius) /
            PhysicsScale.WeldBeadBlobSeconds;

        void SpawnBlob(Chain chain, int index)
        {
            chain.AppliedHelpfulColor = null;
            var local = chain.Origin + chain.Direction * (index * PhysicsScale.WeldBeadStep);
            var world = _container.TransformPoint(local);

            var go = Prim.Ball(
                $"Bead_{_chains.IndexOf(chain)}_{index}", world,
                PhysicsScale.WeldBeadMinRadius * 2f, BeadColor, BeadSoftness, _container);

            // 파츠와 같은 레이어에 둔다. 비드는 파츠의 일부이므로 견인빔·운석·발밑 판정이
            // 파츠에 하는 것과 같은 취급을 해야 한다.
            go.layer = gameObject.layer;
            go.transform.localPosition = local;

            // 구는 접합·질량·레이 판정을 위한 내부 물리 형태다. 그대로 보이면 덩어리들이
            // 따로 떨어진 알갱이처럼 보이므로 렌더러만 숨기고, 그 아래에 이어진 금속 비드를
            // 표시 전용으로 만든다.
            if (go.TryGetComponent<Renderer>(out var physicsRenderer)) physicsRenderer.enabled = false;

            chain.Blobs.Add(go.transform);
            EnsureVisual(chain, index);
            ApplyRadius(chain, index, chain.TipRadius);
        }

        void ApplyRadius(Chain chain, int index, float radius)
        {
            if (chain == null || index < 0 || index >= chain.Blobs.Count) return;
            var blob = chain.Blobs[index];
            if (blob == null) return;
            blob.localScale = Vector3.one * (radius * 2f);
            ApplyVisualShape(chain, index, radius);
        }

        /// <summary>
        /// 내부 구체에는 콜라이더만 남기고, 플레이어에게 보일 용융 자국/이어진 비드를
        /// 자식으로 만든다. 예전 세이브의 구체도 LateUpdate에서 이 경로로 자동 보강된다.
        /// </summary>
        Transform EnsureVisual(Chain chain, int index)
        {
            if (chain == null || index < 0 || index >= chain.Blobs.Count) return null;
            var blob = chain.Blobs[index];
            if (blob == null) return null;

            if (blob.TryGetComponent<Renderer>(out var physicsRenderer)) physicsRenderer.enabled = false;
            var existing = blob.Find(VisualName);
            if (existing != null) return existing;

            var go = Prim.Capsule(VisualName, Vector3.zero, 1f, 2f, BeadColor, BeadSoftness);
            Prim.Decor(go);
            go.layer = gameObject.layer;
            go.transform.SetParent(blob, false);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = Prim.Dough(BeadColor, BeadSoftness, fogExempt: true);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            return go.transform;
        }

        void ApplyVisualShape(Chain chain, int index, float radius)
        {
            var blob = chain.Blobs[index];
            var visual = EnsureVisual(chain, index);
            if (blob == null || visual == null) return;

            var diameter = Mathf.Max(radius * 2f, 0.001f);
            var direction = chain.Direction.sqrMagnitude > 1e-6f ? chain.Direction.normalized : Vector3.up;

            if (index == 0)
            {
                // 첫 점은 둥근 브러시 끝이고, 누른 채 조준을 옮기면 이전 표면점과 현재
                // 표면점을 캡슐 선분으로 잇는다. 선분끼리 둥근 끝이 겹쳐 그림판 브러시처럼
                // 끊김 없는 실제 조준 궤적이 된다.
                const float width = 0.060f;
                const float thickness = 0.010f;
                var travel = chain.HasBrushFrom
                    ? Vector3.ProjectOnPlane(chain.Origin - chain.BrushFrom, direction)
                    : Vector3.zero;
                var tangent = travel.sqrMagnitude > 1e-6f
                    ? travel.normalized : SurfaceTangent(direction);
                var length = Mathf.Max(width, travel.magnitude + width);
                var centerFromOrigin = travel * -0.5f;
                visual.gameObject.SetActive(true);
                visual.localPosition = (centerFromOrigin + direction * 0.007f) / diameter;
                visual.localRotation = Quaternion.LookRotation(direction, tangent);
                visual.localScale = new Vector3(
                    width / diameter,
                    length * 0.5f / diameter,
                    thickness / diameter);
                return;
            }

            // 두 파츠를 실제로 잇는 나머지 구체는 표면 밖으로 자라는 물리 구조다.
            // 보이면 다시 검은 알갱이 기둥이 되므로 외형만 숨긴다.
            visual.gameObject.SetActive(false);
        }

        static Vector3 SurfaceTangent(Vector3 normal)
        {
            var reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.86f
                ? Vector3.right : Vector3.up;
            return Vector3.Cross(reference, normal).normalized;
        }

        // --- 닿으면 접합 (S15P21B202-228) ---------------------------------

        /// <summary>이 비드가 지금 잇고 있는 파츠들. 검증과 진단이 쓴다.</summary>
        public IEnumerable<SpaceshipPart> BridgedParts
        {
            get
            {
                Initialize();

                for (var i = 0; i < _chains.Count; i++)
                {
                    var touched = _chains[i].Touched;
                    if (touched == null || _chains[i].ContactBlobs <= 0f) continue;

                    var seen = false;
                    for (var j = 0; j < i && !seen; j++)
                        seen = _chains[j].Touched == touched && _chains[j].ContactBlobs > 0f;

                    if (!seen) yield return touched;
                }
            }
        }

        /// <summary>
        /// <paramref name="other"/> 를 잇는 비드의 강도 0~1.
        ///
        /// 사슬마다 <b>상대 몸체에 닿아 있는</b> 덩어리만 세고, 여러 사슬이 같은 파츠를
        /// 이으면 더한다. 그래서 긴 기둥 하나(끝만 닿는다)로는 채울 수 없고 여러 군데를
        /// 이어야 한다.
        /// </summary>
        public float StrengthTo(SpaceshipPart other)
        {
            Initialize();
            if (other == null) return 0f;

            var blobs = 0f;
            for (var i = 0; i < _chains.Count; i++)
                if (_chains[i].Touched == other)
                    blobs += _chains[i].ContactBlobs;

            return Mathf.Clamp01(blobs / PhysicsScale.WeldBeadFullStrengthBlobs);
        }

        /// <summary>
        /// 사슬의 접촉을 다시 재고, 접합을 맺거나 강도를 갈아 끼운다.
        ///
        /// 자라거나 깎인 <b>그 사슬만</b> 다시 잰다. 매 틱 모든 덩어리를 재면 48개 ×
        /// 100틱이 되고, 아래 덩어리들은 파츠에 붙어 있어 저절로 움직이지 않는다.
        /// </summary>
        void RefreshContact(int index)
        {
            if (index < 0 || index >= _chains.Count) return;

            var chain = _chains[index];
            var previous = chain.Touched;

            Measure(chain);

            // 닿는 상대가 바뀌었으면 예전 상대의 강도도 다시 계산해야 한다 —
            // 그러지 않으면 떠난 접합이 옛 강도를 그대로 들고 남는다.
            if (previous != null && previous != chain.Touched) ApplyStrengthTo(previous);
            if (chain.Touched != null) ApplyStrengthTo(chain.Touched);
            ApplyPadStrength();
            RefreshAppearance(chain);
        }

        void RefreshAppearance(Chain chain)
        {
            if (chain == null) return;

            var link = chain.Touched != null && chain.ContactBlobs > 0f
                ? PartAssemblyLink.FindBetween(_owner, chain.Touched) : null;
            var helpful = (link != null && link.IsIntact)
                || (chain.TouchedPad != null && chain.TouchedPad == _weldedPad && IsWeldedToPad);

            var heat = Application.isPlaying
                ? 1f - Mathf.Clamp01((Time.time - chain.HeatedAt) / BeadCoolingSeconds)
                : 0f;
            if (heat <= 0f && chain.AppliedHeat <= 0f && chain.AppliedHelpfulColor == helpful) return;

            var cold = helpful ? HelpfulBeadColor : BeadColor;
            var color = heat > 0.42f
                ? Color.Lerp(WarmBeadColor, HotBeadColor, (heat - 0.42f) / 0.58f)
                : Color.Lerp(cold, WarmBeadColor, heat / 0.42f);
            var ambient = Mathf.Lerp(0.28f, 1.8f, heat);
            var gloss = Mathf.Lerp(0.42f, 0.82f, heat);
            var mottle = Mathf.Lerp(0.16f, 0.025f, heat);

            // 다리 중간 덩어리도 연결을 유지하므로 사슬 전체를 같은 색으로 표시한다.
            // 공유 재질 자체는 수정하지 않고 상태별 캐시 재질과 열 표현 PropertyBlock을 나눈다.
            var material = Prim.Dough(cold, BeadSoftness, fogExempt: true);
            if (appearanceBlock == null) appearanceBlock = new MaterialPropertyBlock();
            for (var b = 0; b < chain.Blobs.Count; b++)
            {
                var blob = chain.Blobs[b];
                if (blob == null) continue;

                // 물리 구체는 화면에는 숨겨져 있어도 기존 접합 상태 판정과 테스트가 같은
                // 상태별 재질 참조를 관찰할 수 있도록 표시 자식과 함께 갱신한다.
                if (blob.TryGetComponent<Renderer>(out var physicsRenderer))
                    physicsRenderer.sharedMaterial = material;

                // 예전 저장 데이터에는 표시 자식이 없다. 보이는 순간 현재 외형으로 보강한다.
                var radius = b == chain.Blobs.Count - 1
                    ? chain.TipRadius : PhysicsScale.WeldBeadMaxRadius;
                ApplyVisualShape(chain, b, radius);
                var visual = blob.Find(VisualName);
                if (visual == null || !visual.TryGetComponent<Renderer>(out var renderer)) continue;

                renderer.sharedMaterial = material;
                renderer.GetPropertyBlock(appearanceBlock);
                appearanceBlock.SetColor("_Color", color);
                appearanceBlock.SetFloat("_Ambient", ambient);
                appearanceBlock.SetFloat("_Gloss", gloss);
                appearanceBlock.SetFloat("_Mottle", mottle);
                renderer.SetPropertyBlock(appearanceBlock);
                appearanceBlock.Clear();
            }

            chain.AppliedHelpfulColor = helpful;
            chain.AppliedHeat = heat;
        }

        /// <summary>사슬이 어느 파츠에 얼마나 닿아 있는지 잰다.</summary>
        void Measure(Chain chain)
        {
            chain.Touched = null;
            chain.TouchedPad = null;
            chain.ContactBlobs = 0f;

            var tip = chain.Blobs.Count - 1;

            for (var b = 0; b < chain.Blobs.Count; b++)
            {
                var blob = chain.Blobs[b];
                if (blob == null) continue;

                var radius = b == tip ? chain.TipRadius : PhysicsScale.WeldBeadMaxRadius;
                var found = PartTouching(blob.position, radius);
                var pad = found == null ? PadTouching(blob.position, radius) : null;
                if (found == null && pad == null) continue;

                // 한 사슬이 두 파츠에 걸쳐 닿는 경우는 처음 만난 쪽으로 정한다.
                // 여러 상대를 동시에 이으려면 링크가 파츠당 여럿이어야 하는데, 지금은
                // 하나다(아래 EnsureLink 의 주석을 본다).
                if (chain.Touched == null && chain.TouchedPad == null)
                {
                    chain.Touched = found;
                    chain.TouchedPad = pad;
                }
                if (chain.Touched != found || chain.TouchedPad != pad) continue;

                var ratio = radius / PhysicsScale.WeldBeadMaxRadius;
                chain.ContactBlobs += ratio * ratio * ratio;
            }
        }

        /// <summary>
        /// <paramref name="point"/> 반지름 <paramref name="radius"/> 안에 닿아 있는
        /// <b>다른</b> 우주선 파츠. 없으면 null.
        ///
        /// 상대의 비드도 몸체로 본다 — 양쪽에서 기둥을 뻗어 가운데서 만나는 것이
        /// 막힐 이유가 없다.
        /// </summary>
        SpaceshipPart PartTouching(Vector3 point, float radius)
            => PartTouching(_owner, point, radius);

        /// <summary>첫 비드로 접합할 수 있는 표면. 표시만으로 비드나 조인트를 만들지 않는다.</summary>
        public static bool CanStartJoiningAt(RaycastHit hit)
        {
            if (!CanApplyAt(hit, false) || IsBeadCollider(hit.collider)) return false;
            var owner = hit.collider.GetComponentInParent<SpaceshipPart>();
            return owner != null && PartTouching(owner, hit.point, PhysicsScale.WeldBeadMinRadius) != null;
        }

        static SpaceshipPart PartTouching(SpaceshipPart owner, Vector3 point, float radius)
        {
            var count = UnityEngine.Physics.OverlapSphereNonAlloc(
                point, radius + PhysicsScale.WeldBeadContactSkin, Probe,
                GameLayers.PartMask, QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var collider = Probe[i];
                if (collider == null) continue;

                var found = collider.GetComponentInParent<SpaceshipPart>();
                if (found == null || found == owner) continue;

                return found;
            }

            return null;
        }

        ShipAssembly PadTouching(Vector3 point, float radius)
        {
            var core = false;
            foreach (var spec in PartSpec.All)
                if (_owner != null && _owner.name == spec.Name) { core = true; break; }
            if (!core) return null;
            var count = Physics.OverlapSphereNonAlloc(point,
                radius + PhysicsScale.WeldBeadContactSkin, Probe,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var collider = Probe[i];
                var pad = ShipAssembly.WeldSurface(collider, collider.ClosestPoint(point));
                if (pad != null) return pad;
            }
            return null;
        }

        void ApplyPadStrength()
        {
            ShipAssembly pad = null;
            var blobs = 0f;
            foreach (var chain in _chains)
            {
                if (chain.TouchedPad == null || chain.ContactBlobs <= 0f) continue;
                if (pad == null) pad = chain.TouchedPad;
                if (pad == chain.TouchedPad) blobs += chain.ContactBlobs;
            }
            if (pad == null) { ReleasePadWeld(); return; }
            if (_weldedPad != pad) ReleasePadWeld();
            if (_padJoint == null)
            {
                // 비드 복원에서도 같은 접합을 만든다. 원격 파츠는 기존 동기화 경로에서
                // kinematic이므로 서버와 물리 시뮬레이션을 경쟁하지 않는다.
                _padJoint = _owner.gameObject.AddComponent<FixedJoint>();
                _padJoint.enablePreprocessing = false;
                _weldedPad = pad;
            }
            PadWeldRatio = Mathf.Clamp01(blobs / PhysicsScale.WeldBeadFullStrengthBlobs);
            // 파츠 사이 비드와 동일하게 절단으로 연결이 사라질 때만 해제한다.
            _padJoint.breakForce = float.PositiveInfinity;
            _padJoint.breakTorque = float.PositiveInfinity;
        }

        void ReleasePadWeld()
        {
            if (_padJoint != null)
            {
                if (Application.isPlaying) Destroy(_padJoint);
                else DestroyImmediate(_padJoint);
            }
            _padJoint = null;
            _weldedPad = null;
            PadWeldRatio = 0f;
        }

        /// <summary>
        /// 두 파츠를 잇는 비드의 강도를 다시 재어 접합에 넘긴다.
        ///
        /// 조인트가 운석에 끊어졌다가 다시 조립되는 경우가 있다. 비드는 파츠에 그대로
        /// 남아 있는데 새 접합의 강도는 0 에서 시작하므로, 물어보지 않으면 <b>눈에 보이는
        /// 비드와 실제 강도가 어긋난다</b>. <see cref="PartAssemblyLink.Bind"/> 가 부른다.
        ///
        /// 접합을 <b>새로 만들지 않는다</b> — 이미 있는 접합에 값을 넣기만 한다. 여기서
        /// 만들려 하면 Bind 안에서 다시 Bind 를 부르게 된다.
        /// </summary>
        public static void RefreshBetween(SpaceshipPart a, SpaceshipPart b)
        {
            if (a == null || b == null) return;

            var link = PartAssemblyLink.FindBetween(a, b);
            if (link == null) return;

            var strength = 0f;
            for (var i = 0; i < All.Count; i++)
            {
                var bead = All[i];
                if (bead == null) continue;

                bead.Initialize();
                if (bead._owner == a) strength += bead.MeasuredStrengthTo(b);
                else if (bead._owner == b) strength += bead.MeasuredStrengthTo(a);
            }

            link.SetBeadWeldRatio(Mathf.Clamp01(strength));
        }

        /// <summary>접촉을 <b>다시 재고</b> 강도를 돌려준다. 파츠가 움직였을 수 있다.</summary>
        float MeasuredStrengthTo(SpaceshipPart other)
        {
            for (var i = 0; i < _chains.Count; i++) Measure(_chains[i]);
            return StrengthTo(other);
        }

        /// <summary>접합을 맺거나(없으면) 강도를 갈아 끼운다.</summary>
        void ApplyStrengthTo(SpaceshipPart other)
        {
            var strength = StrengthTo(other);
            var link = PartAssemblyLink.FindBetween(_owner, other);

            if (link == null)
            {
                if (strength <= 0f) return;

                link = EnsureLink(other);
                if (link == null) return;
            }

            link.SetBeadWeldRatio(strength);

            // 잇는 비드가 하나도 남지 않았으면 접합이 풀린다(기획 §16.3). 소켓으로도
            // 맞물려 있는 접합은 풀지 않는다 — 비드는 보강이었을 뿐이다.
            if (strength <= 0f && !link.HasSockets) link.Release();
        }

        /// <summary>
        /// 비드 접합용 조인트와 링크를 만든다.
        ///
        /// <b>쌍마다 하나</b>다. 파츠 하나가 여러 쌍과 이어지는 것은 S15P21B202-229 에서
        /// 열렸지만, 같은 두 파츠 사이에 접합이 둘이면 강도가 두 배가 되고 하중 시험이
        /// 같은 자리를 두 번 센다. 부르는 쪽(<see cref="ApplyStrengthTo"/>)이
        /// <see cref="PartAssemblyLink.FindBetween"/> 으로 먼저 확인한다.
        /// </summary>
        PartAssemblyLink EnsureLink(SpaceshipPart other)
        {
            if (_owner == null) return null;

            var ownerBody = _owner.GetComponent<Rigidbody>();
            var otherBody = other.GetComponent<Rigidbody>();
            if (ownerBody == null || otherBody == null) return null;

            var joint = _owner.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = otherBody;
            joint.breakForce = PhysicsScale.UnweldedBreakForce;
            joint.breakTorque = PhysicsScale.UnweldedBreakTorque;
            joint.enablePreprocessing = false;

            var link = _owner.gameObject.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(_owner, other, joint);
            return link;
        }

        /// <summary>
        /// 비드 질량을 파츠에 넘긴다.
        ///
        /// <c>Rigidbody.mass</c> 를 직접 건드리지 않는 이유는 <see cref="SpaceshipPart"/> 가
        /// 상태 전환마다 질량을 다시 쓰기 때문이다. 여기서 더해 두면 다음 전환에 사라진다.
        /// </summary>
        void ApplyMass()
        {
            if (_owner == null) return;
            _owner.SetExtraMass(Mass);
        }

        static float MassOf(float radius)
        {
            var ratio = radius / PhysicsScale.WeldBeadMaxRadius;
            return PhysicsScale.WeldBeadFullMass * ratio * ratio * ratio;
        }

        /// <summary>
        /// 임시 외형. 코드로 만든 구 하나다.
        ///
        /// 에셋이 필요하지 않다 — 이 프로젝트의 다른 외형도 모두 <see cref="Prim"/> 의
        /// 코드 도형이다. [Art] 가 실제 비드 메시(울퉁불퉁한 금속, 불탄 자국)를 주면
        /// <see cref="SpawnBlob"/> 의 <c>Prim.Ball</c> 자리만 바꾼다.
        /// </summary>
        static Color BeadColor => new Color(0.025f, 0.020f, 0.022f, 1f);

        // 용접 직후에는 붉게 달아올랐다가 어두운 그을음 자국으로 식는다.
        static Color HotBeadColor => new Color(1f, 0.12f, 0.035f, 1f);
        static Color WarmBeadColor => new Color(0.72f, 0.055f, 0.018f, 1f);

        // 접합에 기여한다는 뜻이며, 완전 강도나 올바른 조립 순서의 표시는 아니다.
        static Color HelpfulBeadColor => new Color(0.14f, 0.035f, 0.025f, 1f);

        const float BeadSoftness = 0.15f;

        /// <summary>
        /// 이 파츠의 비드를 모두 지운다. 검증과 에디터 도구가 쓴다.
        /// </summary>
        public void Clear()
        {
            Initialize();

            // 잇고 있던 상대를 먼저 모은다. 사슬을 지운 뒤에는 알 길이 없고, 그러면
            // 비드가 사라졌는데도 접합이 옛 강도로 남는다.
            var bridged = new List<SpaceshipPart>(BridgedParts);

            for (var i = 0; i < _chains.Count; i++)
                for (var b = 0; b < _chains[i].Blobs.Count; b++)
                    DestroyObject(_chains[i].Blobs[b]);

            _chains.Clear();
            ApplyMass();
            ReleasePadWeld();


            for (var i = 0; i < bridged.Count; i++) ApplyStrengthTo(bridged[i]);
        }

        /// <summary>
        /// EditMode 에서는 <c>Destroy</c> 가 아무 일도 하지 않고 에러를 남긴다. 그 로그
        /// 하나로 검증이 실패하므로 재생 중일 때만 지연 파괴를 쓴다
        /// (<see cref="Prim.Decor"/> · <c>PartAssemblyLink</c> 와 같은 처리다).
        /// </summary>
        static void DestroyObject(Transform blob)
        {
            if (blob == null) return;

            if (Application.isPlaying) Destroy(blob.gameObject);
            else DestroyImmediate(blob.gameObject);
        }
    }
}
