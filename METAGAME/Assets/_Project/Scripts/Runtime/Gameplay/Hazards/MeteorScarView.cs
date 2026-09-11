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
        /// <summary>
        /// 지면 위로 띄우는 높이(m). 두께(0.015)보다 커야 원판이 지면을 파고들지 않는다.
        ///
        /// 예전에는 0.01 이라 두께의 절반이 지면 아래로 들어갔다. 그 위에
        /// 착탄 지점 자체가 바닥보다 낮게 잡혀 있었다(S15P21B202-254).
        /// </summary>
        const float Lift = 0.02f;

        [SerializeField]
        [Tooltip("비우면 같은 오브젝트나 씬에서 스포너를 찾는다.")]
        MeteorSpawner _spawner;

        sealed class Marker
        {
            public Transform Root;
            public Material Material;

            /// <summary>
            /// 지금 이 원판이 그리고 있는 흔적.
            ///
            /// <b>풀은 목록 번호로 배정된다.</b> 흔적 하나가 빠지면 뒤가 앞으로 당겨져
            /// 같은 번호에 다른 흔적이 온다. 그런데 아래 <see cref="ShouldRepaint"/> 는
            /// 다 옅어진 흔적의 색 넣기를 건너뛰므로, 새로 온 흔적이 이미 옅어져
            /// 있으면 <b>앞 흔적의 색이 그대로 남는다.</b>
            ///
            /// 지금은 수명(= 식는 시간 + 옅어지는 시간)이 끝나는 순간 목록에서도
            /// 빠지므로 다 옅어진 흔적이 목록에 남아 있지 않다 — 우연히 성립한다.
            /// 씬에서 수명만 늘리면 곧바로 깨지므로 여기서 막는다.
            /// </summary>
            public MeteorSpawner.Scar Bound;
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
        /// 색을 매 프레임 다시 넣지 않는다 — <b>다 옅어진</b> 흔적은 건드릴 이유가
        /// 없고, 200개에 대해 재질을 만지면 §19 의 성능 기준을 스스로 깬다.
        /// (예전에는 "다 식은" 이 기준이었는데, 그러면 옅어지는 단계가 화면에
        /// 반영되지 않아 검은 원이 남았다 — S15P21B202-438.)
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

                if (ShouldRepaint(scar) || marker.Bound != scar)
                    marker.Material.SetColor("_Color", ColorFor(scar));

                marker.Bound = scar;
            }

            Hide(scars.Count);
            VisibleCount = scars.Count;
        }

        /// <summary>
        /// 이 흔적의 색을 다시 넣어야 하는가.
        ///
        /// <b>Cooled 가 아니라 ColorIsFinal 로 끊는다.</b> 식은 뒤에도 옅어지는 동안은 알파가
        /// 변하므로 계속 넣어야 한다 — <c>Cooled</c> 로 끊으면 알파가 0.5 에서 굳어
        /// <b>검은 원이 그대로 남는다</b>(S15P21B202-438 의 증상이 정확히 그것이다).
        ///
        /// <b>왜 순수 메서드로 빼는가.</b> 이 판단이 <see cref="Tick"/> 안의 조건문으로
        /// 있으면 검증이 닿지 못한다 — 실제로 기준을 <c>Cooled</c> 로 되돌리는 대조
        /// 실험에서 검증 12개가 전부 통과했다. 증상을 되살려도 초록불이었다.
        /// 밖에서 부를 수 있게 두면 그 대조가 걸린다.
        /// </summary>
        public static bool ShouldRepaint(MeteorSpawner.Scar scar) =>
            scar != null && !scar.ColorIsFinal;

        /// <summary>이 흔적에 넣을 색. 식는 단계와 옅어지는 단계를 모두 반영한다.</summary>
        public static Color ColorFor(MeteorSpawner.Scar scar) =>
            scar == null
                ? MeteorScarLook.ColdColor
                : MeteorScarLook.ColorOf(scar.Age, scar.CoolSeconds, scar.FadeSeconds);

        void Hide(int from)
        {
            for (var i = from; i < _pool.Count; i++)
            {
                if (_pool[i] == null) continue;

                // 소멸한 흔적을 풀이 계속 붙들고 있지 않게 한다(!347 AI 리뷰).
                _pool[i].Bound = null;

                if (_pool[i].Root != null) _pool[i].Root.gameObject.SetActive(false);
            }

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
