using System.Collections.Generic;
using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 운석 흔적을 지면에 원으로 그린다(기획 §19 "작은 분화구").
    ///
    /// <b>풀에서 꺼내 쓴다.</b> §19 가 "정상 20분 플레이에서 약 150~200개의 흔적이
    /// 누적되는 상황을 성능 기준으로 삼는다" 고 정하므로 매번 새로 만들면 안 된다.
    /// <see cref="MeteorWarningView"/> 가 낙하 예고를 그렇게 그리는 것과 같은 구조다
    /// (S15P21B202-241).
    ///
    /// 무엇을 그릴지는 <see cref="MeteorScarLook"/> 이 정한다. 여기는 그 값을 오브젝트에
    /// 옮기기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeteorScarView : MonoBehaviour
    {
        const string ContainerName = "MeteorScars";
        const float DiscThickness = 0.015f;
        const float RimBoost = 0.5f;
        const float Lift = 0.01f;

        [SerializeField]
        [Tooltip("비우면 같은 오브젝트나 씬에서 스포너를 찾는다.")]
        MeteorSpawner _spawner;

        sealed class Marker
        {
            public Transform Root;
            public Material Material;
        }

        readonly List<Marker> _pool = new();
        Transform _container;

        /// <summary>지금 보이는 분화구 개수. 흔적 개수와 같아야 한다.</summary>
        public int VisibleCount { get; private set; }

        /// <summary>만들어 둔 분화구 개수. 흔적이 줄어도 재사용을 위해 남는다.</summary>
        public int PooledCount => _pool.Count;

        public void Configure(MeteorSpawner spawner) => _spawner = spawner;

        void Awake() => ResolveSpawner();

        void LateUpdate() => Tick();

        /// <summary>
        /// 흔적 목록을 화면에 옮긴다.
        ///
        /// 색을 매 프레임 다시 넣지 않는다 — 다 식은 흔적은 건드릴 이유가 없고,
        /// 200개에 대해 재질을 만지면 §19 의 성능 기준을 스스로 깬다.
        /// </summary>
        public void Tick()
        {
            ResolveSpawner();

            if (_spawner == null)
            {
                Hide(0);
                return;
            }

            var scars = _spawner.Scars;
            EnsureContainer();

            for (var i = 0; i < scars.Count; i++)
            {
                var scar = scars[i];
                if (scar == null) continue;

                var marker = At(i);

                var diameter = MeteorScarLook.DiameterOf(scar.Radius);
                marker.Root.position = scar.Point + Vector3.up * Lift;
                marker.Root.localScale = new Vector3(diameter, DiscThickness, diameter);
                marker.Root.gameObject.SetActive(true);

                if (!scar.Cooled)
                    marker.Material.SetColor("_Color", MeteorScarLook.ColorOf(scar.Age));
            }

            Hide(scars.Count);
            VisibleCount = scars.Count;
        }

        void Hide(int from)
        {
            for (var i = from; i < _pool.Count; i++)
                if (_pool[i]?.Root != null)
                    _pool[i].Root.gameObject.SetActive(false);

            if (from == 0) VisibleCount = 0;
        }

        Marker At(int index)
        {
            while (_pool.Count <= index) _pool.Add(CreateMarker(_pool.Count));
            return _pool[index];
        }

        Marker CreateMarker(int index)
        {
            var disc = Prim.Decor(Prim.Pipe($"MeteorScar_{index}", Vector3.zero, 1f,
                                            DiscThickness, MeteorScarLook.HotColor, 0.1f,
                                            _container)).transform;

            var material = Prim.Ghost(MeteorScarLook.HotColor, RimBoost);
            disc.GetComponent<Renderer>().sharedMaterial = material;

            return new Marker { Root = disc, Material = material };
        }

        void EnsureContainer()
        {
            if (_container != null) return;

            var existing = transform.Find(ContainerName);
            _container = existing != null ? existing : new GameObject(ContainerName).transform;
            _container.SetParent(transform, false);
            _container.localPosition = Vector3.zero;
            _container.localRotation = Quaternion.identity;

            // 스포너가 회전·스케일되어 있어도 분화구는 지면에 눕고 미터 단위여야 한다.
            _container.localScale = Vector3.one;
        }

        void ResolveSpawner()
        {
            if (_spawner != null) return;
            _spawner = GetComponent<MeteorSpawner>();
        }

        void OnDestroy()
        {
            for (var i = 0; i < _pool.Count; i++)
            {
                var material = _pool[i]?.Material;
                if (material == null) continue;

                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }

            _pool.Clear();
        }
    }
}
