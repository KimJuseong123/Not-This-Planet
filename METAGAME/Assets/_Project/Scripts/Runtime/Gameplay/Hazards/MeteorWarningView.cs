using System.Collections.Generic;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 낙하 예고를 지면에 원으로 그린다(기획 §17.2).
    ///
    /// <b>이것이 없으면 운석을 피할 수도, 쳐낼 수도 없다.</b> 예고 데이터
    /// (<see cref="PendingMeteorDrop"/>)는 S15P21B202-234 부터 정확했지만 읽는 곳이
    /// 분위기 연출(<see cref="Stage1AtmosphereAlert"/>) 하나뿐이었다 — 지면에 원을 그리는
    /// 코드가 아예 없었다. 방망이(§18)는 "낙하 지점 반경 2.5m 안" 을 요구하는데 원이
    /// 없으면 어디가 2.5m 인지 알 수 없다.
    ///
    /// <b>코드로 만든 원판이다.</b> URP 데칼을 쓰지 않는다 — 프로젝트에
    /// <c>DecalProjector</c> 사용처가 없고 Renderer 설정에 데칼이 켜져 있지 않다. 켜는
    /// 것은 렌더 파이프라인 결정이며 진행 중인 URP 전환과 얽힌다. 비드를
    /// <see cref="Prim.Ball"/> 로 시작한 것과 같은 방식이고(S15P21B202-226),
    /// [Art] 가 실제 데칼을 주면 <see cref="CreateMarker"/> 만 바꾼다.
    ///
    /// 무엇을 그릴지는 <see cref="MeteorWarningLook"/> 이 정한다. 여기는 그 값을
    /// 오브젝트에 옮기기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeteorWarningView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("비우면 같은 오브젝트나 씬에서 스포너를 찾는다.")]
        MeteorSpawner _spawner;

        /// <summary>
        /// 표시 하나. 테두리와 안쪽 두 조각이다.
        ///
        /// 테두리가 <b>충격 범위</b>를 알리고, 안쪽이 차오르며 <b>남은 시간</b>을 알린다.
        /// 하나로 합치면 둘 중 하나를 잃는다 — 크기로 시간을 표현하면 범위를 알 수 없고,
        /// 색으로만 표현하면 얼마나 남았는지가 흐리다.
        /// </summary>
        sealed class Marker
        {
            public Transform Root;
            public Transform Ring;
            public Transform Fill;
            public Material RingMaterial;
            public Material FillMaterial;
        }

        readonly List<Marker> _pool = new();
        Transform _container;

        /// <summary>지금 보이는 표시 개수. 예고 개수와 같아야 한다.</summary>
        public int VisibleCount { get; private set; }

        /// <summary>만들어 둔 표시 개수. 예고가 줄어도 재사용을 위해 남는다.</summary>
        public int PooledCount => _pool.Count;

        public void Configure(MeteorSpawner spawner) => _spawner = spawner;

        void Awake() => ResolveSpawner();

        void Update() => Tick(Time.time);

        /// <summary>
        /// 표시를 갱신한다. <paramref name="time"/> 은 점멸 위상에만 쓴다.
        ///
        /// 시간을 인수로 받는 이유는 <see cref="MeteorSpawner.Tick"/> 과 같다 —
        /// <c>Time</c> 을 직접 읽으면 검증이 프레임을 흉내내야 한다.
        /// </summary>
        public void Tick(float time)
        {
            ResolveSpawner();
            EnsureContainer();

            var pending = _spawner != null ? _spawner.Pending : null;
            var count = pending?.Count ?? 0;

            for (var i = 0; i < count; i++)
            {
                var marker = MarkerAt(i);
                Apply(marker, pending[i], time);
                marker.Root.gameObject.SetActive(true);
            }

            // 남는 것은 지우지 않고 숨긴다. 공세 후반에는 동시에 다섯 개가 뜨고
            // 사이사이 0 이 되므로, 매번 만들고 지우면 GC 가 돈다.
            for (var i = count; i < _pool.Count; i++)
                if (_pool[i].Root != null)
                    _pool[i].Root.gameObject.SetActive(false);

            VisibleCount = count;
        }

        void Apply(Marker marker, in PendingMeteorDrop drop, float time)
        {
            var diameter = MeteorWarningLook.DiameterOf(drop);
            var color = MeteorWarningLook.RingColorOf(drop);
            var fill = MeteorWarningLook.FillOf(drop);
            var blink = MeteorWarningLook.BlinkOf(drop, time);

            marker.Root.position = drop.ImpactPoint + Vector3.up * PhysicsScale.MeteorWarningLift;

            marker.Ring.localScale = new Vector3(diameter, DiscThickness, diameter);

            // 안쪽은 <b>테두리 안에서</b> 차오른다. 0 이면 보이지 않아야 하므로 최소값을
            // 두지 않는다 — 처음부터 점이 찍혀 있으면 "아직 안 찼다" 가 읽히지 않는다.
            marker.Fill.localScale = new Vector3(diameter * fill, DiscThickness * 0.5f,
                                                 diameter * fill);

            // 점멸은 알파에 곱한다. 색상까지 흔들면 주황 → 붉게 가는 것이 묻힌다.
            var ringColor = color;
            ringColor.a *= Mathf.Lerp(1f, BlinkFloor, blink);

            var fillColor = color;
            fillColor.a = Mathf.Min(1f, fillColor.a * FillAlphaScale);
            fillColor.a *= Mathf.Lerp(1f, BlinkFloor, blink);

            SetColor(marker.RingMaterial, ringColor);
            SetColor(marker.FillMaterial, fillColor);
        }

        Marker MarkerAt(int index)
        {
            while (_pool.Count <= index) _pool.Add(CreateMarker(_pool.Count));
            return _pool[index];
        }

        /// <summary>
        /// 표시 하나를 만든다. [Art] 가 실제 데칼을 주면 <b>이 메서드만</b> 바뀐다.
        ///
        /// 머티리얼을 표시마다 따로 갖는다. <see cref="Prim.Ghost"/> 가 부를 때마다 새로
        /// 만들어 주므로 그것을 그대로 들고 색만 바꿔 쓴다 — 색이 매 프레임 바뀌므로
        /// <see cref="Prim.Dough"/> 처럼 색별로 공유하는 캐시를 쓰면 프레임마다 머티리얼이
        /// 하나씩 늘어난다.
        /// </summary>
        Marker CreateMarker(int index)
        {
            var root = new GameObject($"MeteorWarning_{index}").transform;
            root.SetParent(_container, false);

            var ring = Prim.Decor(Prim.Pipe($"Ring_{index}", Vector3.zero, 1f, DiscThickness,
                                            MeteorWarningLook.StartColor, 0.2f, root)).transform;
            var fill = Prim.Decor(Prim.Pipe($"Fill_{index}", Vector3.zero, 1f, DiscThickness,
                                            MeteorWarningLook.StartColor, 0.2f, root)).transform;

            ring.localPosition = Vector3.zero;
            fill.localPosition = Vector3.up * PhysicsScale.MeteorWarningLift * 0.5f;

            var ringMaterial = Prim.Ghost(MeteorWarningLook.StartColor, RingRimBoost);
            var fillMaterial = Prim.Ghost(MeteorWarningLook.StartColor, FillRimBoost);

            ring.GetComponent<Renderer>().sharedMaterial = ringMaterial;
            fill.GetComponent<Renderer>().sharedMaterial = fillMaterial;

            return new Marker
            {
                Root = root,
                Ring = ring,
                Fill = fill,
                RingMaterial = ringMaterial,
                FillMaterial = fillMaterial,
            };
        }

        void EnsureContainer()
        {
            if (_container != null) return;

            var existing = transform.Find(ContainerName);
            _container = existing != null ? existing : new GameObject(ContainerName).transform;
            _container.SetParent(transform, false);
            _container.localPosition = Vector3.zero;
            _container.localRotation = Quaternion.identity;

            // 스포너가 회전·스케일되어 있어도 원판은 지면에 눕고 미터 단위여야 한다.
            _container.localScale = Vector3.one;
        }

        void ResolveSpawner()
        {
            if (_spawner != null) return;

            _spawner = GetComponent<MeteorSpawner>();
            if (_spawner == null) _spawner = FindAnyObjectByType<MeteorSpawner>();
        }

        static void SetColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty(ColorProperty)) material.SetColor(ColorProperty, color);
        }

        void OnDestroy()
        {
            for (var i = 0; i < _pool.Count; i++)
            {
                DestroyObject(_pool[i].RingMaterial);
                DestroyObject(_pool[i].FillMaterial);
            }

            _pool.Clear();
        }

        /// <summary>
        /// EditMode 에서는 <c>Destroy</c> 가 아무 일도 하지 않고 에러를 남긴다.
        /// 그 로그 하나로 검증이 실패하므로 재생 중일 때만 지연 파괴를 쓴다
        /// (<see cref="Prim.Decor"/> 와 같은 처리다).
        /// </summary>
        static void DestroyObject(Object target)
        {
            if (target == null) return;

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        const string ContainerName = "MeteorWarnings";
        const string ColorProperty = "_Color";

        /// <summary>원판 두께(m). 얇아야 지면에 그린 것처럼 보인다.</summary>
        const float DiscThickness = 0.02f;

        /// <summary>점멸이 가장 어두울 때의 알파 배수. 0 이면 완전히 사라져 위치를 잃는다.</summary>
        const float BlinkFloor = 0.35f;

        /// <summary>안쪽을 테두리보다 진하게. 차오르는 것이 읽혀야 한다.</summary>
        const float FillAlphaScale = 1.6f;

        const float RingRimBoost = 2.4f;
        const float FillRimBoost = 0.6f;
    }
}
