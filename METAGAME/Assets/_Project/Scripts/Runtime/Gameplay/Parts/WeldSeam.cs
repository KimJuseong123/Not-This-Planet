using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 파츠 밑면의 360도 원형 접합선. 기획(S15P21B202-107 §16.1)의 "파츠 사이의 360도
    /// 원형 접합부 전체를 따라간다" 다.
    ///
    /// <b>예전에는 용접점 4개였다.</b> 각 점에 E 를 꾹 눌러 채웠고, 한 자리에 서서 네 번
    /// 누르면 끝이었다. 기획이 명시적으로 "점 네 개를 누르는 방식이 아니라" 고 하는 그
    /// 방식이다. 이제 접합선을 <see cref="PhysicsScale.WeldSeamSegments"/> 조각으로 나누고
    /// 조각마다 진행분을 따로 갖는다.
    ///
    /// <b>조각이 곧 규칙이다.</b> §16.1 의 "같은 위치에 계속 대고 있어도 추가 진행되지
    /// 않는다" 를 따로 구현하지 않았다 — 조각이 1 에 차면 그 자리에서는 더 오르지 않으므로
    /// 플레이어는 돌아야 한다. 규칙을 코드로 두 번 쓰지 않는 편이 어긋나지 않는다.
    ///
    /// 진행분은 접합선이 갖는다. 그래서 §16.1 의 "용접을 멈춰도 이미 용접한 구간은
    /// 유지되고 다른 플레이어가 이어서 작업할 수 있다" 가 성립한다 — 누가 채웠는지는
    /// 기록하지 않는다.
    ///
    /// <b>진행도를 화면에 띄우지 않는다.</b> 기획 §16.2 가 "용접률, 완료 문구, 진행 바,
    /// 완료 색상과 완료 효과음을 제공하지 않는다" 고 금지한다. 빠뜨린 구간은 플레이어가
    /// 직접 둘러보며 찾는 것이 설계다. <see cref="Ratio"/> 는 조인트 강도를 정하는 데
    /// 쓰는 값이고 UI 로 내보내면 안 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeldSeam : MonoBehaviour
    {
        /// <summary>
        /// 등록된 접합선. <see cref="PartSocket.All"/> 과 같은 방식이다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로
        /// <see cref="Register"/> 를 직접 부른다.
        /// </summary>
        public static readonly List<WeldSeam> All = new();

        [SerializeField]
        [Tooltip("비우면 부모에서 SpaceshipPart 를 찾는다.")]
        SpaceshipPart _owner;

        /// <summary>
        /// 조각별 진행분 0~1. 직렬화한다.
        ///
        /// 씬이나 프리팹에 저장된 용접 상태가 로드된 뒤에도 유지되어야 한다. 저장하지
        /// 않으면 파츠는 Welded 인데 진행도는 0 으로 보여, 조인트 강도가 미접합 값으로
        /// 되돌아가 용접한 탑이 충격에 떨어진다(예전 용접점도 같은 이유로 저장했다).
        /// </summary>
        [SerializeField, HideInInspector] float[] _segments;

        /// <summary>
        /// 조각별 절단 진행분 0~1. 용접과 나란히 둔다(기획 §16.3).
        ///
        /// <b>용접 진행분을 깎는 것으로 절단을 표현할 수 없다.</b> 기획이 두 상태를
        /// 다르게 다루기 때문이다 — §15.1 은 "잘 올리면 용접 전에도 서 있을 수 있고"
        /// 이므로 용접 0% 는 붙어 있고, §16.3 은 "100% 절단하면 해당 접합부 위쪽
        /// 파츠들이 하나의 덩어리로 분리된다" 다. 값이 하나면 두 경우가 같아진다.
        ///
        /// 강도에 대한 기여는 <c>용접 × (1 - 절단)</c> 이다. 그래서 절단한 만큼 약해지고,
        /// 다 자르면 기여가 0 이면서 <see cref="IsSevered"/> 로 분리까지 구분된다.
        /// </summary>
        [SerializeField, HideInInspector] float[] _cuts;

        bool _initialized;

        /// <summary>접합선을 가진 파츠. 위에 올라가는 쪽이다.</summary>
        public SpaceshipPart Owner
        {
            get
            {
                Initialize();
                return _owner;
            }
        }

        /// <summary>조각 수.</summary>
        public int SegmentCount => PhysicsScale.WeldSeamSegments;

        /// <summary>
        /// 용접된 호의 비율 0~1.
        ///
        /// 기획 §16.2 "처음 조립하는 접합부의 강도는 실제로 용접한 길이에 비례한다" 가
        /// 이 값으로 성립한다. 조각 길이가 모두 같으므로 평균이 곧 길이 비율이다.
        /// </summary>
        public float Ratio
        {
            get
            {
                Initialize();

                var sum = 0f;
                for (var i = 0; i < _segments.Length; i++) sum += Contribution(i);

                return _segments.Length == 0 ? 0f : Mathf.Clamp01(sum / _segments.Length);
            }
        }

        public bool IsComplete => Ratio >= 1f;

        /// <summary>잘린 호의 비율 0~1.</summary>
        public float CutRatio
        {
            get
            {
                Initialize();

                var sum = 0f;
                for (var i = 0; i < _cuts.Length; i++) sum += _cuts[i];

                return _cuts.Length == 0 ? 0f : Mathf.Clamp01(sum / _cuts.Length);
            }
        }

        /// <summary>
        /// 접합선이 완전히 잘렸는가. 기획 §16.3 "100% 절단하면 해당 접합부 위쪽
        /// 파츠들이 하나의 덩어리로 분리된다".
        /// </summary>
        public bool IsSevered
        {
            get
            {
                Initialize();

                if (_cuts.Length == 0) return false;
                for (var i = 0; i < _cuts.Length; i++)
                    if (_cuts[i] < 1f) return false;

                return true;
            }
        }

        /// <summary>조각 하나가 강도에 기여하는 값. 잘린 만큼 깎인다.</summary>
        float Contribution(int index) => _segments[index] * (1f - _cuts[index]);

        /// <summary>접합선의 세계 중심. 파츠 밑면 가운데다.</summary>
        public Vector3 Center => transform.position;

        /// <summary>
        /// 접합선의 반지름(m). 파츠 밑면 상자의 절반이다.
        ///
        /// 사각 발자국에 원형 선을 그리는 것이 어색해 보이지만, 기획이 요구하는 것은
        /// "한 바퀴 돌아야 한다" 이고 파츠 겉모양은 스킨이 결정한다. 원으로 두면
        /// 각도 하나로 조각을 정할 수 있어 판정이 단순해진다.
        /// </summary>
        public float Radius { get; private set; } = 1f;

        /// <summary>용접이 모두 끝났을 때.</summary>
        public event Action<WeldSeam> Completed;

        void Awake() => Initialize();

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        /// <summary>
        /// 소유 파츠와 반지름을 찾는다. 여러 번 불러도 안전하다.
        /// <c>Awake</c> 는 EditMode 에서 호출되지 않으므로 테스트와 링크가 직접 부른다.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (_owner == null) _owner = GetComponentInParent<SpaceshipPart>();
            if (_segments == null || _segments.Length != SegmentCount)
                _segments = new float[SegmentCount];
            if (_cuts == null || _cuts.Length != SegmentCount)
                _cuts = new float[SegmentCount];

            var spec = _owner != null ? PartSpec.Find(_owner.name) : null;
            if (spec != null) Radius = Mathf.Max(spec.Boxes[0].Width, spec.Boxes[0].Depth) * 0.5f;

            Register();
        }

        public void Register()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        /// <summary>
        /// <paramref name="nozzle"/> 에서 <see cref="PhysicsScale.WeldSeamRange"/> 안에 있는
        /// 접합선 중 가장 가까운 것.
        ///
        /// 아직 붙지 않은 파츠의 접합선은 건너뛴다 — 작업할 것이 없다.
        /// </summary>
        public static WeldSeam FindNearest(Vector3 nozzle)
        {
            WeldSeam best = null;
            var bestDistance = PhysicsScale.WeldSeamRange;

            for (var i = 0; i < All.Count; i++)
            {
                var seam = All[i];
                if (seam == null || !seam.CanWork()) continue;

                var distance = Vector3.Distance(nozzle, seam.ClosestPointOnSeam(nozzle));
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = seam;
            }

            return best;
        }

        /// <summary>접합선 위에서 <paramref name="point"/> 에 가장 가까운 지점.</summary>
        public Vector3 ClosestPointOnSeam(Vector3 point)
        {
            var local = transform.InverseTransformPoint(point);
            var flat = new Vector2(local.x, local.z);

            // 정확히 축 위에 있으면 방향이 없다. 아무 방향이나 하나 고른다.
            if (flat.sqrMagnitude < 1e-6f) flat = Vector2.right;

            flat = flat.normalized * Radius;
            return transform.TransformPoint(new Vector3(flat.x, 0f, flat.y));
        }

        /// <summary>
        /// <paramref name="point"/> 가 접합선의 몇 번째 조각인지. 0 부터
        /// <see cref="SegmentCount"/> - 1 까지다.
        /// </summary>
        public int SegmentAt(Vector3 point)
        {
            var local = transform.InverseTransformPoint(point);
            var degrees = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
            if (degrees < 0f) degrees += 360f;

            var index = (int)(degrees / (360f / SegmentCount));
            return Mathf.Clamp(index, 0, SegmentCount - 1);
        }

        /// <summary>조각 하나의 진행분 0~1.</summary>
        public float SegmentProgress(int index)
        {
            Initialize();
            return index < 0 || index >= _segments.Length ? 0f : _segments[index];
        }

        /// <summary>
        /// 이 접합선에 작업(용접·절단)할 수 있는 상태인가.
        ///
        /// 기획 §16.1 "파츠가 움직여 접합면 간격이 벌어지면 작업이 중단된다". 간격 기준은
        /// 조립 판정이 쓰는 <see cref="PhysicsScale.WeldGapTolerance"/> 를 그대로 쓴다 —
        /// 새 기준을 만들면 "붙었다고 판정했는데 용접은 안 되는" 구간이 생긴다.
        ///
        /// <b><see cref="PartState.Welded"/> 도 받는다.</b> 처음에는 용접용과 절단용
        /// 조건을 나누고 용접은 <see cref="PartState.Placed"/> 만 받게 두었다. 그러면
        /// 기획 §16.3 의 "절단한 구간은 좌클릭으로 다시 용접할 수 있다" 가 성립하지 않는다 —
        /// 용접된 접합을 반쯤 자른 뒤 다시 용접할 방법이 없어진다.
        ///
        /// 조건을 넓혀도 안전하다. 완전히 용접되고 잘리지 않은 접합선에서는
        /// <see cref="TryWeld"/> 가 채울 조각을 찾지 못해 아무것도 하지 않는다.
        /// "할 수 있는가" 와 "할 것이 있는가" 를 다른 곳에서 판정하는 편이 맞다.
        /// </summary>
        public bool CanWork()
        {
            Initialize();

            if (_owner == null) return false;
            if (_owner.State is not (PartState.Placed or PartState.Welded)) return false;

            var link = _owner.GetComponent<PartAssemblyLink>();
            if (link == null || !link.IsIntact) return false;

            return link.Gap <= PhysicsScale.WeldGapTolerance;
        }

        /// <summary>
        /// <paramref name="point"/> 가 가리키는 조각을 <paramref name="deltaTime"/> 만큼
        /// 채운다. 실제로 진행됐으면 <c>true</c>.
        ///
        /// 조각이 이미 찼으면 <c>false</c> 를 돌려준다 — 기획 §16.1 의 "같은 위치에 계속
        /// 대고 있어도 추가 진행되지 않는다" 다. 남는 시간을 옆 조각으로 넘기지 않는다.
        /// 넘기면 한 자리에 대고 있어도 한 바퀴가 채워진다.
        /// </summary>
        public bool TryWeld(Vector3 point, float deltaTime)
        {
            if (deltaTime <= 0f || !CanWork()) return false;

            var index = SegmentAt(point);

            // 기획 §16.3 "절단한 구간은 좌클릭으로 다시 용접할 수 있다".
            // 잘린 조각은 먼저 메운다. 한 번의 호출로 메우고 채우기까지 하지 않는 이유는
            // 남는 시간을 다음 단계로 넘기는 순간 "같은 자리에서 더 진행되지 않는다"
            // 규칙이 흐려지기 때문이다.
            if (_cuts[index] > 0f)
            {
                _cuts[index] = Mathf.Clamp01(
                    _cuts[index] - deltaTime / PhysicsScale.WeldSeamSecondsPerSegment);
                return true;
            }

            if (_segments[index] >= 1f) return false;

            var before = IsComplete;
            _segments[index] = Mathf.Clamp01(
                _segments[index] + deltaTime / PhysicsScale.WeldSeamSecondsPerSegment);

            if (!before && IsComplete) Completed?.Invoke(this);
            return true;
        }

        /// <summary>
        /// <paramref name="point"/> 가 가리키는 조각을 <paramref name="deltaTime"/> 만큼
        /// 자른다. 실제로 진행됐으면 <c>true</c>.
        ///
        /// 조각이 이미 다 잘렸으면 <c>false</c> 다 — 용접과 같은 규칙이다(§16.3
        /// "절단을 멈춰도 이미 자른 구간은 유지된다" 와 §16.1 의 한 자리 규칙).
        ///
        /// 마지막 조각이 잘리면 <b>접합을 해제한다.</b> 기획 §16.3 "100% 절단하면 해당
        /// 접합부 위쪽 파츠들이 하나의 덩어리로 분리된다" — 위쪽 파츠들은 서로의 접합이
        /// 남아 있으므로 따로 묶는 코드가 필요하지 않다.
        /// </summary>
        public bool TryCut(Vector3 point, float deltaTime)
        {
            if (deltaTime <= 0f || !CanWork()) return false;

            var index = SegmentAt(point);
            if (_cuts[index] >= 1f) return false;

            _cuts[index] = Mathf.Clamp01(
                _cuts[index] + deltaTime / PhysicsScale.CutSeamSecondsPerSegment);

            if (!IsSevered) return true;

            Severed?.Invoke(this);

            // Release 가 소켓 맞물림을 풀고 진행분을 되돌린다. 다시 쌓아 처음부터
            // 용접해야 하는 것이 맞다 — 잘라 낸 접합이 기억될 이유가 없다.
            var link = _owner.GetComponent<PartAssemblyLink>();
            if (link != null) link.Release();
            return true;
        }

        /// <summary>완전히 잘려 접합이 분리될 때. 연출이 붙을 자리다(§16.3).</summary>
        public event Action<WeldSeam> Severed;

        /// <summary>
        /// 상호작용 없이 접합선을 모두 채운다.
        /// 레벨 초기 상태 구성과 검증에서 쓴다.
        /// </summary>
        public void ForceComplete()
        {
            Initialize();

            var before = IsComplete;
            for (var i = 0; i < _segments.Length; i++)
            {
                _segments[i] = 1f;
                _cuts[i] = 0f;      // 잘린 곳도 메운다. "완전히 용접된" 상태여야 한다.
            }

            if (!before) Completed?.Invoke(this);
        }

        /// <summary>
        /// 상호작용 없이 접합선을 모두 자른다. 검증에서 쓴다.
        /// <see cref="TryCut"/> 과 달리 접합을 해제하지 않는다 — 자른 상태 자체를
        /// 만들어 놓고 보고 싶을 때가 있다.
        /// </summary>
        public void ForceCut(float ratio = 1f)
        {
            Initialize();

            var value = Mathf.Clamp01(ratio);
            for (var i = 0; i < _cuts.Length; i++) _cuts[i] = value;
        }

        /// <summary>진행분을 0 으로 되돌린다. 조인트가 끊어져 접합이 풀렸을 때 호출된다.</summary>
        public void ResetProgress()
        {
            Initialize();

            for (var i = 0; i < _segments.Length; i++)
            {
                _segments[i] = 0f;
                _cuts[i] = 0f;
            }
        }
    }
}
