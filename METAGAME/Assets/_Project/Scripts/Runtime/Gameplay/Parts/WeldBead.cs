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
    /// <b>원형 접합선(<see cref="WeldSeam"/>)과 다른 물건이다.</b> 접합선은 정해진 자리를
    /// 훑어 채우는 것이고, 비드는 몸체 아무 데나 <b>쌓는</b> 것이다. 한 자리에 계속 대고
    /// 있으면 덩어리가 커지고, 상한에 이르면 그 위에 다음 덩어리가 시작되어 기둥이 뻗는다.
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
    /// 이 이슈(S15P21B202-226)는 <b>쌓이는 것까지</b> 다. 비드가 다른 몸체에 닿아 하나로
    /// 합쳐지는 것은 다음 이슈에서 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeldBead : MonoBehaviour
    {
        /// <summary>
        /// 비드를 가진 파츠들. <see cref="WeldSeam.All"/> 과 같은 방식이다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로 <see cref="Register"/> 를
        /// 직접 부른다.
        /// </summary>
        public static readonly List<WeldBead> All = new();

        /// <summary>비드를 모아 두는 자식의 이름. 씬에서 눈으로 찾을 때 쓴다.</summary>
        public const string ContainerName = "WeldBeads";

        const int ProbeSize = 32;
        static readonly Collider[] Probe = new Collider[ProbeSize];

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
            public float TipRadius;
            public List<Transform> Blobs = new();
        }

        [SerializeField] List<Chain> _chains = new();

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _owner;

        [SerializeField] Transform _container;

        bool _initialized;

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
        /// 사거리는 원형 접합선과 같은 <see cref="PhysicsScale.WeldSeamRange"/> 다.
        /// 접합선은 되는데 비드는 안 되는(또는 그 반대인) 거리가 생기면 플레이어가 이유를
        /// 알 수 없다 — S15P21B202-209 에서 절단 사거리를 용접과 맞춘 것과 같은 이유다.
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
        /// 새 기둥이 시작되면 <see cref="PhysicsScale.WeldBeadBlobsPerChain"/> 로 정한
        /// 최대 다리 길이가 무의미해진다.
        /// </summary>
        static bool TryFindBodySurface(
            Vector3 nozzle, out SpaceshipPart part, out Vector3 point, out Vector3 outward)
        {
            part = null;
            point = Vector3.zero;
            outward = Vector3.up;

            var count = UnityEngine.Physics.OverlapSphereNonAlloc(
                nozzle, PhysicsScale.WeldSeamRange, Probe,
                GameLayers.PartMask, QueryTriggerInteraction.Ignore);

            var bestDistance = PhysicsScale.WeldSeamRange;

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

        static bool IsBeadCollider(Collider collider)
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

        bool StartChain(Vector3 worldPoint, Vector3 worldOutward)
        {
            if (BlobCount >= PhysicsScale.WeldBeadsPerPart) return false;

            var chain = new Chain
            {
                Origin = _container.InverseTransformPoint(worldPoint),
                Direction = _container.InverseTransformDirection(worldOutward).normalized,
                TipRadius = PhysicsScale.WeldBeadMinRadius,
            };

            _chains.Add(chain);
            SpawnBlob(chain, 0);
            ApplyMass();

            // 씨앗을 만든 것 자체가 이번 틱의 진행이다. 남은 시간으로 더 키우지 않는 것은
            // 첫 프레임에 최소 크기가 <b>보이게</b> 하려는 것이다 — 진행 바가 없으므로
            // 눈에 보이는 것이 유일한 피드백이다.
            return true;
        }

        bool GrowChain(int index, float deltaTime)
        {
            var chain = _chains[index];
            if (chain.Blobs.Count == 0) return false;

            var full = chain.Blobs.Count >= PhysicsScale.WeldBeadBlobsPerChain;
            var atMax = chain.TipRadius >= PhysicsScale.WeldBeadMaxRadius - 1e-4f;

            // 사슬이 상한 길이에 이르고 끝 덩어리도 다 컸으면 여기서는 더 할 일이 없다.
            // 최대 다리 길이가 이 지점에서 성립한다.
            if (full && atMax) return false;

            if (atMax)
            {
                if (BlobCount >= PhysicsScale.WeldBeadsPerPart) return false;

                chain.TipRadius = PhysicsScale.WeldBeadMinRadius;
                SpawnBlob(chain, chain.Blobs.Count);
                ApplyMass();
                return true;
            }

            chain.TipRadius = Mathf.Min(
                PhysicsScale.WeldBeadMaxRadius, chain.TipRadius + GrowthRate * deltaTime);

            ApplyRadius(chain.Blobs[chain.Blobs.Count - 1], chain.TipRadius);
            ApplyMass();
            return true;
        }

        bool ShrinkChain(int index, float deltaTime)
        {
            var chain = _chains[index];
            if (chain.Blobs.Count == 0) return false;

            chain.TipRadius -= GrowthRate * deltaTime;

            if (chain.TipRadius > PhysicsScale.WeldBeadMinRadius)
            {
                ApplyRadius(chain.Blobs[chain.Blobs.Count - 1], chain.TipRadius);
                ApplyMass();
                return true;
            }

            // 끝 덩어리가 최소보다 작아지면 지운다. 아래 덩어리가 새 끝이 되고 최대 크기에서
            // 다시 줄어들기 시작한다.
            DestroyObject(chain.Blobs[chain.Blobs.Count - 1]);
            chain.Blobs.RemoveAt(chain.Blobs.Count - 1);
            chain.TipRadius = PhysicsScale.WeldBeadMaxRadius;

            if (chain.Blobs.Count == 0) _chains.RemoveAt(index);

            ApplyMass();
            return true;
        }

        /// <summary>최소에서 최대까지 자라는 속도(m/초).</summary>
        static float GrowthRate =>
            (PhysicsScale.WeldBeadMaxRadius - PhysicsScale.WeldBeadMinRadius) /
            PhysicsScale.WeldBeadBlobSeconds;

        void SpawnBlob(Chain chain, int index)
        {
            var local = chain.Origin + chain.Direction * (index * PhysicsScale.WeldBeadStep);
            var world = _container.TransformPoint(local);

            var go = Prim.Ball(
                $"Bead_{_chains.IndexOf(chain)}_{index}", world,
                PhysicsScale.WeldBeadMinRadius * 2f, BeadColor, BeadSoftness, _container);

            // 파츠와 같은 레이어에 둔다. 비드는 파츠의 일부이므로 견인빔·운석·발밑 판정이
            // 파츠에 하는 것과 같은 취급을 해야 한다.
            go.layer = gameObject.layer;
            go.transform.localPosition = local;

            chain.Blobs.Add(go.transform);
            ApplyRadius(go.transform, chain.TipRadius);
        }

        static void ApplyRadius(Transform blob, float radius)
        {
            if (blob == null) return;
            blob.localScale = Vector3.one * (radius * 2f);
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
        static Color BeadColor => new Color(0.42f, 0.36f, 0.32f, 1f);

        const float BeadSoftness = 0.15f;

        /// <summary>
        /// 이 파츠의 비드를 모두 지운다. 검증과 에디터 도구가 쓴다.
        /// </summary>
        public void Clear()
        {
            Initialize();

            for (var i = 0; i < _chains.Count; i++)
                for (var b = 0; b < _chains[i].Blobs.Count; b++)
                    DestroyObject(_chains[i].Blobs[b]);

            _chains.Clear();
            ApplyMass();
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
