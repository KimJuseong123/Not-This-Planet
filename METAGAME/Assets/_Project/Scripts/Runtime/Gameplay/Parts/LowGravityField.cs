using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 견인 조종 모듈 아래에 펼쳐지는 광역 저중력장.
    ///
    /// <b>왜 빔이 아니라 영역인가.</b> <see cref="TractorBeam"/> 은 조준선 앞의 파츠 <b>하나</b>를
    /// 붙잡아 목표점으로 끌어오는 "선" 이다. 상세 기획(S15P21B202-107 §14.3)이 정한 것은
    /// 기체 아래 깊이 10m · 지면 반경 4m 의 원뿔형 "부피" 이고, 그 안의 파츠와 장비가
    /// <b>동시에</b> 영향을 받으며 서로 충돌한다.
    ///
    /// 이 전환이 없으면 1장을 끝낼 수 없다. 파츠 합산 높이가 8.16m 인데 3층 접합부가 3.50m,
    /// 4층이 5.71m 에 있고, 플레이어가 닿는 높이는 점프 1.5m + 용접기 사거리 0.8m 로 약 3.2m 다.
    /// 제트팩은 기획 §5 에서 제거되었으므로 공중 작업 수단은 이 저중력장뿐이다.
    ///
    /// <b>이 이슈(S15P21B202-174)가 다루는 것은 파츠와 장비까지다.</b> 플레이어 부유(§14.3 후반)와
    /// 고정못이 남은 파츠의 부분 부양(§14.4)은 후속 이슈로 나눴다 — 한 이슈로는 상한 13점을 넘는다.
    ///
    /// <see cref="TractorBeam"/> 을 대체하지 않는다. 빔은 파츠 하나를 정밀하게 놓는 조작으로
    /// 남고, 이 컴포넌트는 영역 안의 것들을 띄운다. 둘 다 <see cref="Tick"/> 에 시간을 받아
    /// EditMode 에서 <c>Physics.Simulate</c> 와 함께 검증할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LowGravityField : MonoBehaviour
    {
        const int BufferSize = 128;

        /// <summary>
        /// 씬에 살아 있는 모든 저중력장.
        ///
        /// 부유할지 판단하는 쪽이 플레이어라서 필요하다. 물리 질의로 찾지 않는 이유는
        /// 저중력장이 씬에 많아야 두세 개뿐이라 목록 순회가 더 단순하고 결정적이기
        /// 때문이다. <see cref="PartSocket.All"/> 이 같은 방식을 쓴다.
        /// </summary>
        public static readonly List<LowGravityField> All = new();

        [SerializeField]
        [Tooltip("원뿔의 꼭짓점. 비우면 이 오브젝트의 Transform 을 쓴다. 아래(-y)로 펼쳐진다.")]
        Transform _origin;

        Collider[] _hits = new Collider[BufferSize];
        readonly HashSet<Rigidbody> _affectedBodies = new();
        Rigidbody _carrierBody;
        bool _physicsAuthority = true;

        /// <summary>지금 켜져 있는가. F 로 켜고 끈다(기획 §14.1).</summary>
        public bool IsActive { get; private set; }

        /// <summary>마지막 <see cref="Tick"/> 에서 영향을 준 강체 수. 검증과 연출에 쓴다.</summary>
        public int AffectedCount { get; private set; }

        /// <summary>현재 인스턴스가 저중력 물리를 계산할 권한이 있는가.</summary>
        public bool PhysicsAuthority => _physicsAuthority;

        public Transform Origin => _origin != null ? _origin : transform;

        /// <summary>원뿔 깊이(m).</summary>
        public float Depth => PhysicsScale.FieldDepth;

        /// <summary>가장 깊은 곳에서의 반경(m).</summary>
        public float GroundRadius => PhysicsScale.FieldGroundRadius;

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        /// <summary>
        /// 씬 목록에 직접 등록한다. <c>OnEnable</c> 이 돌지 않는 EditMode 에서 쓴다 —
        /// <see cref="PartSocket.Register"/> 와 같은 이유이고 같은 방식이다.
        /// </summary>
        public void Register()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        /// <summary>
        /// 이 점을 품고 있는 <b>켜진</b> 저중력장. 없으면 null.
        /// 여러 개가 겹치면 먼저 찾은 것을 쓴다 — 효과가 같아 구분할 이유가 없다.
        /// </summary>
        public static LowGravityField FindContaining(Vector3 point)
        {
            for (var i = 0; i < All.Count; i++)
            {
                var field = All[i];
                if (field != null && field.IsActive && field.Contains(point)) return field;
            }

            return null;
        }

        public void Configure(Transform origin)
        {
            Configure(origin, null);
        }

        /// <summary>
        /// 원뿔의 기준점과 운반 장치를 연결한다. carrier는 영역의 움직임을 물체에
        /// 보간해 전달하는 데만 사용하며, 대상 물체를 부모로 붙이지 않는다.
        /// </summary>
        public void Configure(Transform origin, Rigidbody carrierBody)
        {
            _origin = origin;
            _carrierBody = carrierBody;
        }

        /// <summary>멀티플레이에서 서버만 물리 힘을 계산하도록 권한을 설정한다.</summary>
        public void SetPhysicsAuthority(bool authority) => _physicsAuthority = authority;

        public void SetActive(bool on)
        {
            Register();
            IsActive = on;
            if (!on)
            {
                AffectedCount = 0;
                _affectedBodies.Clear();
            }
            if (on && _carrierBody != null)
                _carrierBody.WakeUp();
        }

        public void Toggle() => SetActive(!IsActive);

        /// <summary>
        /// 꼭짓점에서 <paramref name="depth"/> 만큼 내려간 높이에서의 반경.
        /// 기체 쪽이 좁고 지면 쪽이 넓다 — 기획의 "기체에서 지면으로 갈수록 넓어진다".
        /// </summary>
        public float RadiusAt(float depth)
        {
            var t = Mathf.Clamp01(depth / Depth);
            return Mathf.Lerp(PhysicsScale.FieldCraftRadius, GroundRadius, t);
        }

        /// <summary>
        /// 이 점이 원뿔 안인가. 꼭짓점 위이거나 깊이를 벗어나면 밖이다.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            var offset = point - Origin.position;

            var depth = -offset.y;
            if (depth < 0f || depth > Depth) return false;

            var lateral = new Vector2(offset.x, offset.z).magnitude;
            return lateral <= RadiusAt(depth);
        }

        /// <summary>
        /// 런타임에서는 스스로 돈다. <see cref="PartAssemblyLink"/> 와 같은 방식이다.
        ///
        /// 컨트롤러가 아니라 여기서 도는 이유는 기획 §14.2 다 — 저중력장을 켠 채 하차해도
        /// 기체가 호버하며 저중력장이 유지된다. 조종 중에만 도는 곳에 두면 하차하는 순간
        /// 영역이 죽어 파츠가 떨어진다.
        /// </summary>
        void FixedUpdate() => Tick(Time.fixedDeltaTime);

        /// <summary>
        /// 물리 스텝마다 부른다. 꺼져 있으면 아무것도 하지 않으므로,
        /// 영역을 벗어나거나 끄는 순간 일반 중력이 그대로 돌아온다(기획 §14.3).
        /// </summary>
        public void Tick(float deltaTime)
        {
            AffectedCount = 0;
            _affectedBodies.Clear();
            if (!IsActive || !_physicsAuthority || deltaTime <= 0f) return;

            // 원뿔을 감싸는 구로 한 번에 걷어 온 뒤 Contains 로 걸러낸다. 원뿔을 직접 질의하는
            // 방법이 없어서인데, 반경이 4m 라 후보 수가 적어 이 편이 단순하고 결정적이다.
            var center = Origin.position + Vector3.down * (Depth * 0.5f);
            var radius = Mathf.Sqrt(Depth * Depth * 0.25f + GroundRadius * GroundRadius);

            var count = QueryCandidates(center, radius);

            for (var i = 0; i < count; i++)
            {
                var body = _hits[i] != null ? _hits[i].attachedRigidbody : null;
                if (body == null || body.isKinematic) continue;

                // 기체 자신과 그 자식(플랫폼·조종석)은 제 필드에 뜨지 않는다.
                if (body == _carrierBody || body.transform.IsChildOf(Origin.root)) continue;

                // 고정쇠가 남은 파츠는 지면에 묶인 상태다. Welded 파츠는 하나의 동적
                // 덩어리로 움직여야 하므로 여전히 대상으로 남긴다.
                var part = body.GetComponentInParent<SpaceshipPart>();
                if (part != null && part.State == PartState.Locked) continue;

                var collider = _hits[i];
                var inside = Contains(body.worldCenterOfMass)
                             || (collider != null && Contains(collider.ClosestPoint(Origin.position)));
                if (!inside) continue;

                // 여러 collider가 같은 Rigidbody에 붙은 compound 파츠는 한 번만 처리한다.
                if (!_affectedBodies.Add(body)) continue;

                Lift(body);
                Upright(body);
                AffectedCount++;
            }
        }

        int QueryCandidates(Vector3 center, float radius)
        {
            var count = Physics.OverlapSphereNonAlloc(
                center, radius, _hits, PhysicsScale.FieldMask, QueryTriggerInteraction.Ignore);

            // NonAlloc queries silently truncate at the buffer size. Grow and retry only
            // when the buffer is full so a crowded field still reaches every dynamic body
            // without paying an allocation on the normal path.
            while (count == _hits.Length)
            {
                _hits = new Collider[_hits.Length * 2];
                count = Physics.OverlapSphereNonAlloc(
                    center, radius, _hits, PhysicsScale.FieldMask, QueryTriggerInteraction.Ignore);
            }

            return count;
        }

        /// <summary>
        /// 고정못이 모두 풀린 파츠의 바닥이 아래를 향하도록 돌려 세운다(기획 §14.4).
        ///
        /// 요(y축 회전)는 건드리지 않는다. 어느 방향을 보고 놓을지는 플레이어가 정한다 —
        /// S15P21B202-172 가 없앤 것이 바로 "놓을 때 방향까지 맞춰 주는" 동작이다.
        /// 여기서 되돌리는 것은 넘어진 것을 세우는 것뿐이다.
        ///
        /// 고정못이 남은 파츠는 세우지 않는다. 그 파츠는 지면에 묶여 기울어 있는 것이
        /// 정상이고(§14.4), 억지로 세우면 구속과 싸워 덜컥거린다.
        /// </summary>
        void Upright(Rigidbody body)
        {
            var part = body.GetComponentInParent<SpaceshipPart>();
            if (part == null || part.State == PartState.Locked) return;

            var tilt = Quaternion.FromToRotation(body.transform.up, Vector3.up);
            tilt.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f) angle -= 360f;

            if (Mathf.Abs(angle) < PhysicsScale.FieldUprightToleranceDegrees) return;
            if (axis.sqrMagnitude < 0.0001f) return;

            // 일정한 속도로 돌리되, 남은 각도가 그보다 작으면 그만큼만 돌려 지나치지 않게 한다.
            var step = Mathf.Clamp(angle,
                                   -PhysicsScale.FieldUprightDegreesPerSecond,
                                   PhysicsScale.FieldUprightDegreesPerSecond);

            var correction = axis.normalized * (step * Mathf.Deg2Rad);

            // y 성분은 그대로 둔다 — 플레이어가 돌리는 요를 빼앗지 않는다.
            var current = body.angularVelocity;
            body.angularVelocity = new Vector3(correction.x, current.y, correction.z);
        }

        /// <summary>
        /// 강체 하나를 띄운다. 중력을 대신 들어주고, 목표 높이로 당기고, 축으로 모은다.
        ///
        /// <c>ForceMode.Acceleration</c> 을 쓰므로 질량과 무관하게 같은 반응을 낸다. 745kg 파츠와
        /// 406kg 파츠가 같은 속도로 따라와야 조종이 예측 가능하다. 빔이 이미 같은 방식이다.
        ///
        /// <b>계산은 전부 세계 y 로 한다.</b> 처음에는 깊이(아래가 양수)로 스프링을 짜고 결과를
        /// <c>Vector3.down</c> 에 곱했는데, 그러면 감쇠 항의 부호가 뒤집혀 감쇠가 가속이 된다.
        /// 파츠가 위로 갈수록 더 세게 밀려 원뿔을 뚫고 날아갔다(측정: 45스텝 만에 속도 15.9m/s).
        /// 좌표계를 섞지 않는 것이 유일한 예방책이다.
        /// </summary>
        void Lift(Rigidbody body)
        {
            var center = body.worldCenterOfMass;
            var velocity = body.linearVelocity;

            // 중력 보상. 이것만으로 무중력처럼 뜬다.
            body.AddForce(-Physics.gravity * PhysicsScale.FieldGravitySupport, ForceMode.Acceleration);

            // 세로 — 목표 높이로 당기는 스프링-댐퍼. 감쇠를 임계보다 조금 약하게 두어
            // 급가속하면 뒤처졌다 따라붙는 여운이 남는다(기획 §14.3).
            var targetY = Origin.position.y - PhysicsScale.FieldHoldDepth;
            var vertical = (targetY - center.y) * PhysicsScale.FieldHoldAccel
                           - velocity.y * PhysicsScale.FieldHoldDamp;

            vertical = Mathf.Clamp(vertical, -PhysicsScale.FieldAccelMax, PhysicsScale.FieldAccelMax);
            body.AddForce(Vector3.up * vertical, ForceMode.Acceleration);

            // 가로 — 축으로 천천히 모은다. 기획이 "천천히" 라고 한 대로 약하게 주고,
            // 감쇠를 함께 걸어 축을 지나쳐 좌우로 출렁이지 않게 한다.
            var offset = center - Origin.position;
            var lateral = new Vector3(offset.x, 0f, offset.z);
            var lateralVelocity = new Vector3(velocity.x, 0f, velocity.z);

            var pull = lateral.sqrMagnitude > 0.0001f
                ? -lateral.normalized * PhysicsScale.FieldPullAccel
                : Vector3.zero;

            pull -= lateralVelocity * PhysicsScale.FieldPullDamp;
            body.AddForce(Vector3.ClampMagnitude(pull, PhysicsScale.FieldAccelMax), ForceMode.Acceleration);

            // 장치의 속도를 물체에 그대로 순간 대입하지 않고, 속도 차이를 가속도로
            // 보정한다. 가장자리와 깊은 곳의 물체는 낮은 비율로 따라와 지연과 흔들림이
            // 남고, 장치를 움직인다고 물체가 장치에 붙지는 않는다.
            if (_carrierBody != null)
            {
                var depth = Mathf.Clamp01(-offset.y / Depth);
                var radius = Mathf.Max(0.001f, RadiusAt(-offset.y));
                var lateralRatio = Mathf.Clamp01(lateral.magnitude / radius);
                var followWeight = Mathf.Clamp01(1f - Mathf.Max(lateralRatio, depth * 0.35f));
                var follow = (_carrierBody.linearVelocity - velocity)
                             * (PhysicsScale.FieldCarrierFollowGain * followWeight);
                body.AddForce(Vector3.ClampMagnitude(follow, PhysicsScale.FieldCarrierFollowMaxAccel),
                              ForceMode.Acceleration);
            }
        }
    }
}
