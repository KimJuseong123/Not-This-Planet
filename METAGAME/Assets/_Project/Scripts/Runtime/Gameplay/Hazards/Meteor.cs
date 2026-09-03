using System;
using System.Collections.Generic;
using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 1장의 방해 요소인 작은 운석. 떨어져서 부딪히고 잠시 뒤 사라진다.
    ///
    /// 파츠를 무너뜨리는 힘은 이 코드가 아니라 물리 솔버가 만든다.
    /// 조립된 파츠는 breakForce 가 정해진 조인트로 붙어 있으므로, 운석이 부딪히면
    /// 그 조인트가 스스로 끊어질지 버틸지 결정된다. 여기서 "무너뜨려라" 라고 지시하지 않는다.
    /// 그래서 용접 정도에 따라 결과가 저절로 달라진다.
    ///
    /// 넉백과 <b>충격 단계</b>를 코드로 준다. 착탄 지점 주변에 폭발력을 한 번 넣고,
    /// 기획 §20.1 의 세 단계(약한 충격파 · 강한 충격 · 직접 충돌)를 거리로 나눈다.
    ///
    /// <b>플레이어 피해를 <see cref="DamageSource"/> 에 맡기지 않는다.</b> 그것은 "닿으면
    /// 25" 하나뿐이어서 세 단계가 모두 같아지고, <see cref="Tuning.MaxHealth"/> 100 에서
    /// 네 번을 맞아야 기절했다 — §20.1 은 직접 충돌 <b>한 번</b>으로 기절한다고 정한다.
    /// 그리고 반복 판정이 있어 <b>바닥에 박힌 운석 옆에 서 있으면 계속 맞았다</b>
    /// (§19 "충돌 순간에만 넉백과 기절 판정이 있다"). S15P21B202-247 에서 여기로 옮겼다.
    ///
    /// 소유자(스포너)가 <see cref="Tick"/> 을 호출한다. Time 을 직접 읽지 않아
    /// EditMode 에서 검증할 수 있다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class Meteor : MonoBehaviour
    {
        [SerializeField, Min(0f)] float _impactRadius = PhysicsScale.MeteorSmallImpactRadius;
        [SerializeField, Min(0f)] float _impactForce = PhysicsScale.MeteorImpactForce;

        /// <summary>이번 착탄에 재사용하는 버퍼. 한 플레이어의 여러 뼈가 함께 걸린다.</summary>
        static readonly List<PlayerRig> Struck = new();
        readonly List<PlayerRig> _directPlayersHit = new();
        [SerializeField, Min(0f)] float _maxLifeSeconds = PhysicsScale.MeteorMaxLifeSeconds;

        const float MinimumSupportNormalY = 0.35f;

        /// <summary>
        /// 살아 있는 운석. <see cref="Parts.PartSocket.All"/> 과 같은 방식이다.
        ///
        /// 방망이(<see cref="MeteorBat"/>)가 쳐낼 대상을 여기서 찾는다. 스포너를 거치지
        /// 않는 이유는 <b>쳐낸 운석</b>이다 — 반사된 운석은 스포너가 만든 것이 아니지만
        /// 여전히 운석이고, 다시 떨어지면 또 쳐낼 수 있어야 한다.
        ///
        /// EditMode 에서는 <c>OnEnable</c> 이 호출되지 않으므로
        /// <see cref="Initialize"/> 가 직접 등록한다.
        /// </summary>
        public static readonly List<Meteor> All = new();

        Rigidbody _body;
        float _life;
        float _sinceRolling = -1f;
        float _sinceSettled = -1f;
        float _atRestSeconds;

        /// <summary>정착 안전망의 레이캐스트 버퍼. 매 틱 새로 만들지 않는다.</summary>
        static readonly RaycastHit[] RestProbe = new RaycastHit[8];

        /// <summary>이미 부딪혔는지. 착탄은 한 번만 처리한다.</summary>
        public bool HasImpacted { get; private set; }

        /// <summary>
        /// 지면에 박혀 흔적으로 남았는지(기획 §19).
        ///
        /// <b>예전에는 착탄 6초 뒤에 사라졌다.</b> §19 는 "박힌 운석과 분화구는 발사 실패
        /// 후에도 유지되며 <b>스테이지 동안 사라지지 않는다</b>" 고 정한다 — 정확히
        /// 반대였다(S15P21B202-251).
        ///
        /// 박히면 물리를 끈다. §19 "충돌 후 고비용 물리, 빛과 파티클을 끄고 … 단순
        /// 충돌체로 전환한다" 이고, 150~200개가 쌓이는 것을 성능 기준으로 삼기 때문이다.
        /// </summary>
        public bool Settled { get; private set; }

        /// <summary>
        /// 쳐내서 <b>굴러다니는 소품</b>이 되었는가(S15P21B202-273).
        ///
        /// 운석은 날아간 뒤 두 갈래로 끝난다.
        ///
        /// <code>
        /// 방망이로 쳐냄   →  동적 유지 · Prop 레이어 · 방망이로 밀린다 · 굴러다닌다
        /// 못 쳐냄        →  키네마틱 · 반쯤 박힘 · 못 건드림 · 위에 올라갈 수만
        /// </code>
        ///
        /// Prop 레이어로 옮기는 것이 핵심이다. <see cref="GameLayers.BatStrikeMask"/> 가
        /// <see cref="GameLayers.Meteor"/> 를 빼고 <see cref="GameLayers.Prop"/> 은
        /// 포함하므로(S15P21B202-257), 레이어만 옮기면 <b>때릴 수 있는 소품이 된다</b> —
        /// 새 규칙을 만들지 않는다.
        /// </summary>
        public bool Rolling { get; private set; }

        /// <summary>
        /// 더 이상 사람을 때리지 않는가.
        ///
        /// 기획 §19 "충돌 <b>순간에만</b> 넉백과 기절 판정이 있다". 착지든 방망이 타격이든
        /// <b>한 번 끝나면 다시 때리지 않는다</b> — 예전에는 <see cref="HasImpacted"/>
        /// 하나로만 막아서, 쳐낸 운석이 날아가다 사람을 스치면 기절시켰다
        /// (S15P21B202-273).
        /// </summary>
        public bool Harmless => HasImpacted || Rolling;

        /// <summary>
        /// 착탄한 지면 위치. 박히기 <b>전</b>의 지점이다.
        ///
        /// <see cref="Settle"/> 이 몸통을 반지름의 0.6배만큼 <b>지면 아래로</b> 내리므로
        /// <c>transform.position</c> 은 지면보다 낮다. 분화구를 거기에 그리면 바닥에
        /// 묻힌다 — 그래서 표시가 쓸 값을 따로 남긴다(S15P21B202-254).
        /// </summary>
        public Vector3 ImpactPoint { get; private set; }

        /// <summary>
        /// 착탄할 지면의 높이(m). 스포너가 넣는다.
        ///
        /// 낙하 예측에 기준면이 필요하다. 콜라이더를 레이캐스트로 찾지 않는 이유는
        /// 예측이 <b>매 스윙마다 여러 운석에 대해</b> 돌기 때문이고, 스포너가 이미
        /// 착탄 지점을 알고 있어 다시 찾을 이유가 없다.
        /// </summary>
        public float GroundHeight { get; private set; }

        /// <summary>한 번이라도 쳐냈는가. 기획 §17.1 의 "반사된 이후" 를 구분한다.</summary>
        public bool WasDeflected { get; private set; }

        /// <summary>
        /// 크기(기획 §17.7). 넉백 반경이 여기서 나온다.
        ///
        /// 스포너가 시간대별 비율로 정한다 — 중형은 10분 이후에만 나온다.
        /// 모델 구분은 [Art] 몫이고 여기서는 반경만 다루다.
        /// </summary>
        public MeteorSize Size { get; private set; } = MeteorSize.Small;

        /// <summary>넉백이 미치는 반경(m). 크기가 정한다.</summary>
        public float ImpactRadius => _impactRadius;

        /// <summary>스포너가 크기를 정한다. 반경이 함께 바뀐다.</summary>
        public void SetSize(MeteorSize size)
        {
            Size = size;
            _impactRadius = RadiusFor(size);
            _impactForce = MeteorImpact.ForceFor(size);
        }

        /// <summary>
        /// 크기에 해당하는 충격 반경(m). 기획 §17.7 — 소형 2m · 중형 3.5m.
        ///
        /// <b>매핑이 한 곳에 있어야 한다.</b> 낙하 예고 표시(§17.2 "실제 충격 범위와 같은
        /// 크기")도 같은 값을 써야 하는데, 스포너가 예고를 만드는 시점에는 운석이 아직
        /// 없을 수도 있다. 두 곳에서 각자 분기하면 표시와 실제가 어긋난다.
        /// </summary>
        public static float RadiusFor(MeteorSize size) =>
            size == MeteorSize.Medium
                ? PhysicsScale.MeteorMediumImpactRadius
                : PhysicsScale.MeteorSmallImpactRadius;

        /// <summary>사라질 때. 스포너가 목록에서 지우는 신호로 쓴다.</summary>
        public event Action<Meteor> Expired;

        /// <summary>착탄했을 때. 사운드와 이펙트가 여기에 붙는다.</summary>
        public event Action<Meteor, Vector3> Impacted;
        public static event Action<Meteor, Vector3> AnyImpacted;

        void Awake() => Initialize();

        /// <summary>여러 번 불러도 안전하다. Awake 는 EditMode 에서 호출되지 않는다.</summary>
        public void Initialize()
        {
            Register();

            EnsureTrail();

            if (_body != null) return;

            _body = GetComponent<Rigidbody>();
            _body.mass = PhysicsScale.MeteorMass;
        }

        /// <summary>
        /// 낙하 꼬리. 끄면 만들지 않는다.
        ///
        /// <see cref="Metagame.Core.Puff.Enabled"/> 와 같은 자리다 — 검증이 수백 개의
        /// 운석을 만들므로 연출을 끌 수단이 있어야 한다.
        /// </summary>
        public static bool TrailsEnabled = true;

        /// <summary>
        /// 모든 운석 꼬리가 <b>함께 쓰는</b> 머티리얼.
        ///
        /// 운석마다 따로 만들면 §19 의 기준(20분에 150~200개)에서 그만큼 늘어난다.
        /// 색이 운석마다 다르지 않으므로 나눌 이유가 없다.
        /// </summary>
        static Material trailMaterial;

        TrailRenderer _trail;

        /// <summary>
        /// 떨어지는 동안 꼬리를 남긴다(S15P21B202-276).
        ///
        /// <b>대각선 낙하가 예고 없는 피격을 만들었다.</b> S15P21B202-274 의 경로 검사는
        /// 결정성을 위해 동적 물체를 막힘으로 세지 않으므로, 대각선 경로에 서 있으면
        /// <b>원에서 8m 떨어진 곳에서</b> 남의 운석에 맞는다 — §17.2 의 "원이 어디
        /// 떨어질지 알려준다" 는 계약과 어긋난다. 꼬리가 그 경우의 예고가 된다.
        ///
        /// <b>자식에 붙인다.</b> 운석 본체에 붙이면 <c>GetComponent&lt;Renderer&gt;</c> 가
        /// 꼬리를 집어, 크기·재질을 다루는 기존 코드가 엉뚱한 것을 만진다.
        /// </summary>
        void EnsureTrail()
        {
            if (!TrailsEnabled || _trail != null) return;

            if (trailMaterial == null)
                trailMaterial = Prim.Ghost(TrailColor, 2.2f);

            var go = new GameObject("낙하 꼬리");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _trail = go.AddComponent<TrailRenderer>();
            _trail.time = PhysicsScale.MeteorTrailSeconds;
            _trail.startWidth = PhysicsScale.MeteorTrailWidth;
            _trail.endWidth = 0f;
            _trail.startColor = TrailColor;
            _trail.endColor = new Color(TrailColor.r, TrailColor.g, TrailColor.b, 0f);
            _trail.numCapVertices = 2;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.sharedMaterial = trailMaterial;
        }

        /// <summary>
        /// 꼬리를 끊는다. 지면에 박힌 뒤에도 남아 있으면 흔적이 두 개로 보인다.
        ///
        /// <b>지우지 않고 멈춘다.</b> 지우면 이미 그려진 꼬리가 한 프레임에 사라져
        /// 눈에 띈다. 방출만 끄면 남은 꼬리가 제 시간에 옅어진다.
        /// </summary>
        void StopTrail()
        {
            if (_trail == null) return;

            _trail.emitting = false;
        }

        static readonly Color TrailColor = new(1f, 0.62f, 0.22f, 0.75f);

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        public void Register()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public void Unregister() => All.Remove(this);

        /// <summary>스포너가 착탄 지면 높이를 넣는다.</summary>
        public void SetGroundHeight(float y) => GroundHeight = y;

        /// <summary>
        /// 지면에 닿을 때까지 남은 시간과 그때의 위치를 푼다. 이미 착탄했거나 올라가서
        /// 돌아오지 않으면 <c>false</c>.
        ///
        /// <b>이 예측이 없으면 방망이가 성립하지 않는다.</b> 기획 §18.1 이 "착탄 약 1초
        /// 전부터 타격 가능하다" 고 정하는데, 그 시각을 알 방법이 없었다 —
        /// <see cref="PendingMeteorDrop"/> 은 운석이 <b>생기기 전</b>의 예고 시간이고,
        /// 운석은 예고가 끝난 뒤 <see cref="PhysicsScale.MeteorDropHeight"/>(30m)에서
        /// 생겨 중력 −2.8m/s² 로 4.63초를 더 떨어진다.
        ///
        /// 등가속도 운동을 그대로 푼다. y + v·t + ½g·t² = 지면 이므로
        /// ½g·t² + v·t + (y − 지면) = 0 의 양의 최소 근이 남은 시간이다.
        /// 레이캐스트로 재지 않는 이유는 <b>포물선</b>이기 때문이다 — 쳐낸 운석은 위로
        /// 날아가므로 아래로 쏜 광선이 맞히는 지점과 실제 착탄 지점이 다르다.
        /// </summary>
        public bool TryPredictImpact(out float seconds, out Vector3 point)
        {
            seconds = 0f;
            point = transform.position;

            if (HasImpacted) return false;

            Initialize();
            if (_body == null) return false;

            var gravity = UnityEngine.Physics.gravity.y;
            if (Mathf.Approximately(gravity, 0f)) return false;

            var position = _body.position;
            var velocity = _body.linearVelocity;

            var a = 0.5f * gravity;
            var b = velocity.y;
            var c = position.y - GroundHeight;

            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;

            var root = Mathf.Sqrt(discriminant);
            var first = (-b + root) / (2f * a);
            var second = (-b - root) / (2f * a);

            // 양수인 근 중 작은 것. 위로 올라가는 중이면 큰 근만 양수다.
            seconds = Mathf.Min(
                first > 0f ? first : float.MaxValue,
                second > 0f ? second : float.MaxValue);

            if (seconds >= float.MaxValue) return false;

            point = new Vector3(
                position.x + velocity.x * seconds,
                GroundHeight,
                position.z + velocity.z * seconds);

            return true;
        }

        /// <summary>
        /// 쳐낸다(기획 §18.2). 속도를 갈아 끼우기만 한다.
        ///
        /// 충돌 판정을 손대지 않는다 — 기획 §17.1 "반사된 이후에는 플레이어, 우주선,
        /// 자연물과 지면 어디든 충돌할 수 있다" 는 <see cref="OnCollisionEnter"/> 가
        /// 이미 아무거나 받으므로 저절로 성립한다.
        /// </summary>
        public void Deflect(Vector3 velocity)
        {
            if (HasImpacted) return;

            Initialize();
            if (_body == null) return;

            _body.linearVelocity = velocity;
            WasDeflected = true;

            // 맞은 순간부터 소품이다(S15P21B202-273). 날아가는 동안에도 사람을 때리지
            // 않는다 — §19 의 "충돌 순간에만" 을 타격에도 적용한다.
            BecomeRolling();
        }

        /// <summary>
        /// 쳐낸 운석을 굴러다니는 소품으로 바꾼다(S15P21B202-273).
        ///
        /// <b>물리를 끄지 않는다.</b> 굴러다녀야 하고 방망이로 밀려야 한다 — 박힌 흔적과
        /// 정반대다.
        /// </summary>
        void BecomeRolling()
        {
            if (Rolling) return;

            Rolling = true;
            _sinceRolling = 0f;

            SetLayerDeep(gameObject, GameLayers.Prop);
        }

        static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;

            for (var i = 0; i < go.transform.childCount; i++)
                SetLayerDeep(go.transform.GetChild(i).gameObject, layer);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;

            // 쳐낸 운석은 이미 소품이다(S15P21B202-273). 착지해도 박히지 않고 굴러다니며,
            // 사람을 때리지도 않는다 — 일반 PhysX 충돌에 맡긴다.
            //
            // <b>기획 §18.2 를 어긴다</b> — "반사 운석의 충돌과 폭발은 일반 운석과 같은
            // 피해를 준다" 고 정한다. 2026-09-02 에 "방망이에 친 순간 데미지를 못 주는
            // 물건이 된다" 로 정했다. 쳐낸 것이 동료를 때리면 방망이가 구제가 아니라
            // 위험 전가가 되므로 협동에는 이쪽이 낫다 — [Plan] 확인이 필요하다.
            if (Rolling) return;

            // 첫 접점이 아니라 가장 위를 받치는 접점을 고른다. 운석이 차량이나 파츠의
            // 옆면을 스치는 순간에는 지지면이 아니므로, 그 점을 착탄 지점으로 쓰면
            // 운석이 허공에 박힌 채 차량을 따라가는 문제가 생긴다.
            if (!TryGetSupportContact(collision, out var support, out var supportNormal))
            {
                // 떨어지는 중 플레이어를 직접 맞힌 경우의 충격은 유지한다. 다만 같은
                // 플레이어의 여러 limb collider가 한 운석에 반복 피해를 주지 않게 한다.
                var direct = collision.collider != null
                    ? collision.collider.GetComponentInParent<PlayerRig>()
                    : null;
                if (direct != null && !_directPlayersHit.Contains(direct))
                {
                    _directPlayersHit.Add(direct);
                    ApplyKnockback(collision.GetContact(0).point, direct);
                }

                // 동적 물체와의 접촉은 일반 PhysX 충돌에 맡긴다. 이 경로에서는
                // isKinematic 전환, 위치 고정, Impacted 이벤트를 발생시키지 않는다.
                return;
            }

            // 운석 본체가 몸에 닿았는가. 거리로는 알 수 없다 — 착탄 지점 바로 옆에 서
            // 있는 것과 운석에 얻어맞는 것은 §20.1 이 다른 단계로 나눈다.
            var supportCollider = Other(support).Collider;
            var supportDirect = supportCollider != null
                ? supportCollider.GetComponentInParent<PlayerRig>()
                : null;

            ApplyImpact(support.point, supportDirect, supportNormal);
        }

        bool TryGetSupportContact(Collision collision, out ContactPoint support,
                                  out Vector3 supportNormal)
        {
            support = default;
            supportNormal = Vector3.up;

            var found = false;
            var bestNormalY = MinimumSupportNormalY;

            for (var i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                var (otherCollider, normal) = Other(contact);
                if (otherCollider == null) continue;

                var otherBody = otherCollider.attachedRigidbody;
                if (otherBody != null && otherBody != _body && !otherBody.isKinematic)
                    continue;

                // 운반 장치·동적 파츠 위에 잠시 올라탄 경우도 정착시키지 않는다. 이
                // 명시적 제외는 향후 해당 물체가 kinematic으로 전환되어도 안전하다.
                if (otherCollider.GetComponentInParent<Metagame.Gameplay.Parts.TransportVehicle>() != null)
                    continue;

                // <b>운석 기준으로 뒤집은 법선</b>으로 판정한다(S15P21B202-273).
                if (normal.y < bestNormalY) continue;

                bestNormalY = normal.y;
                support = contact;
                supportNormal = normal;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 접점을 <b>운석 기준</b>으로 바꿔 돌려준다.
        ///
        /// <b>법선의 부호가 이 버그의 원인이었다</b>(S15P21B202-273). Unity 의
        /// <see cref="ContactPoint.normal"/> 은 <c>thisCollider</c> 기준이고, 어느 쪽이
        /// <c>thisCollider</c> 가 되는지는 <b>어느 오브젝트가 콜백을 받았는지에 따라
        /// 달라진다.</b>
        ///
        /// 예전 코드는 그 모호함을 콜라이더에 대해서만 처리하고 법선은 그대로 썼다.
        /// 지면이 <c>thisCollider</c> 로 잡히면 법선이 운석 반대쪽을 향해 y 가 −1 에
        /// 가까워지고, <see cref="MinimumSupportNormalY"/>(0.35) 문턱을 넘지 못한다 —
        /// 그러면 운석이 <b>영영 정착하지 않고</b> 살아 있는 동적 물체로 바닥에 누워,
        /// 사람이 닿을 때마다 직접 충돌로 기절시켰다.
        ///
        /// 두 값이 같은 판단을 쓰므로 한 곳에서 함께 돌려준다.
        /// </summary>
        static (Collider Collider, Vector3 Normal) Other(ContactPoint contact)
        {
            var meteorIsThis = contact.thisCollider != null
                               && contact.thisCollider.GetComponentInParent<Meteor>() != null;

            if (meteorIsThis)
                return (contact.otherCollider, contact.normal);

            // 지면 쪽이 콜백을 받았다. 법선을 뒤집어 운석 기준으로 맞춘다.
            var collider = contact.thisCollider != null
                ? contact.thisCollider
                : contact.otherCollider;

            return (collider, -contact.normal);
        }

        /// <summary>
        /// 외부 호출용 착탄 처리. 테스트와 스포너가 명시한 지점은 실제 착탄으로
        /// 취급하는 기존 계약을 유지한다.
        /// </summary>
        public void ApplyImpact(Vector3 point, PlayerRig directHit = null)
        {
            ApplyImpact(point, directHit, Vector3.up);
        }

        void ApplyImpact(Vector3 point, PlayerRig directHit, Vector3 normal)
        {
            if (HasImpacted) return;

            // 쳐낸 운석은 흔적이 되지 않는다(S15P21B202-273). 착지해도 굴러다니는 소품으로
            // 남는다 — 두 상태가 섞이면 "쳐냈는데 박혀 있는" 것이 된다.
            if (Rolling) return;

            Initialize();
            HasImpacted = true;
            ImpactPoint = point;

            ApplyKnockback(point, directHit);
            Settle(point, normal);
            Impacted?.Invoke(this, point);
            AnyImpacted?.Invoke(this, point);
        }

        /*
         * 이전 구현은 OnCollisionEnter에서 바로 ApplyImpact를 호출해 모든 접촉점을
         * 정착 지점으로 사용했다. 위의 지지 접점 판정으로 동적 물체 충돌은 이 블록에
         * 들어오지 않으며, 실제 착탄만 아래의 공통 경로를 사용한다.
         */

        /// <summary>
        /// 소유자가 매 물리 스텝 호출한다. 착탄 후 사라짐까지의 시간을 센다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            // 박힌 흔적. 일정 시간 뒤 사라진다(S15P21B202-273).
            //
            // <b>기획 §19 를 어긴다</b> — "스테이지 동안 사라지지 않는다" 고 정하고
            // S15P21B202-251 이 그 근거로 소멸을 지웠다. 2026-09-02 에 둘 다 사라지게
            // 하기로 정했고 [Plan] 확인이 필요하다.
            if (Settled)
            {
                _sinceSettled += deltaTime;
                if (_sinceSettled >= PhysicsScale.MeteorScarDespawnSeconds) Expire();
                return;
            }

            // 쳐내서 굴러다니는 소품. 더 짧게 남는다 — 플레이어가 처리한 것이고,
            // 동적 물체가 조립 구역에 오래 굴러다니면 방해가 된다.
            if (Rolling)
            {
                _sinceRolling += deltaTime;
                if (_sinceRolling >= PhysicsScale.MeteorDeflectedDespawnSeconds) Expire();
                return;
            }

            _life += deltaTime;

            // 접점 판정이 놓친 경우의 안전망(S15P21B202-273). 이유는
            // <see cref="PhysicsScale.MeteorRestSpeed"/> 에 적었다.
            SettleIfAtRest(deltaTime);
            if (Settled) return;

            // 착탄 처리는 물리 충돌 콜백에 달려 있다. 운석이 플레이 공간 밖으로 떨어지거나
            // 콜백을 놓치면 영구히 남으므로 수명 상한으로 반드시 사라지게 한다.
            if (_life >= _maxLifeSeconds) Expire();
        }

        /// <summary>
        /// 거의 멈췄고 아래에 정적 지지면이 있으면 정착시킨다(S15P21B202-273).
        ///
        /// <b>접점 법선만 믿을 수 없다.</b> 지면이 자갈이 깔린 MeshCollider 라 급한
        /// 삼각형에 먼저 닿으면 법선이 문턱을 넘지 못하고, 그러면 운석이 살아 있는
        /// 동적 물체로 바닥에 누워 사람을 때린다 — 그것이 이 이슈의 증상이다.
        ///
        /// 아래를 훑어 <b>정적 지지면</b>을 확인한다. 그냥 "멈췄으면 정착" 으로 두면
        /// 견인 운반체나 동적 파츠 위에 얹혀 잠깐 멈춘 것도 박혀 버린다 —
        /// <see cref="TryGetSupportContact"/> 가 그것을 막으려고 만든 판정이므로 같은
        /// 규칙을 지킨다.
        /// </summary>
        void SettleIfAtRest(float deltaTime)
        {
            if (_body == null || _body.isKinematic) return;

            if (_body.linearVelocity.sqrMagnitude
                > PhysicsScale.MeteorRestSpeed * PhysicsScale.MeteorRestSpeed)
            {
                _atRestSeconds = 0f;
                return;
            }

            _atRestSeconds += deltaTime;
            if (_atRestSeconds < PhysicsScale.MeteorRestSeconds) return;

            if (!TryFindSupportBelow(out var point, out var normal)) return;

            ApplyImpact(point, null, normal);
        }

        /// <summary>몸통 아래의 정적 지지면. 없으면 <c>false</c>.</summary>
        bool TryFindSupportBelow(out Vector3 point, out Vector3 normal)
        {
            point = transform.position;
            normal = Vector3.up;

            var from = _body.worldCenterOfMass + Vector3.up * PhysicsScale.MeteorRadius;
            var distance = PhysicsScale.MeteorRadius * 3f;

            var count = UnityEngine.Physics.RaycastNonAlloc(
                from, Vector3.down, RestProbe, distance,
                GameLayers.SurfaceMask, QueryTriggerInteraction.Ignore);

            var best = float.MaxValue;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var hit = RestProbe[i];
                if (hit.collider == null) continue;

                // 자기 자신은 지지면이 아니다.
                if (hit.collider.GetComponentInParent<Meteor>() == this) continue;

                // 동적 물체 위에 잠깐 얹힌 것은 정착이 아니다 — 접점 판정과 같은 규칙이다.
                var body = hit.collider.attachedRigidbody;
                if (body != null && !body.isKinematic) continue;
                if (hit.collider.GetComponentInParent<Parts.TransportVehicle>() != null) continue;

                if (hit.distance >= best) continue;

                best = hit.distance;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 지면에 박아 흔적으로 만든다(기획 §19).
        ///
        /// <b>물리를 끈다.</b> 키네마틱으로 바꾸고 속도를 지운다 — 200개가 쌓여도 솔버가
        /// 매 스텝 풀어야 할 것이 없어야 한다. 콜라이더는 남긴다: §19 이 "접촉 피해가
        /// 없는 <b>낮은 정적 장애물</b>" 이라고 정하므로 걸려서 넘어갈 수 있어야 한다.
        ///
        /// 피해는 이미 한 번만 돈다 — <see cref="HasImpacted"/> 가
        /// <see cref="ApplyImpact"/> 를 막고, <c>DamageSource</c> 는 운석에 붙어 있지 않다
        /// (S15P21B202-247).
        /// </summary>
        void Settle(Vector3 point, Vector3 normal)
        {
            Settled = true;
            _sinceSettled = 0f;
            StopTrail();

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
            }

            // "절반 이상 지면에 박힌 모습"(§19). y만 조정하지 않고 접촉 법선 방향으로
            // 묻어 경사진 MeshCollider에서도 실제 표면을 따라간다.
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
            normal.Normalize();
            transform.position = point
                                + normal * (PhysicsScale.MeteorRadius
                                             * (1f - 2f * PhysicsScale.MeteorScarSinkRatio));
        }

        void Expire()
        {
            Expired?.Invoke(this);
            Despawn();
        }

        public void Despawn()
        {
            Unregister();

            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>
        /// 착탄 지점 주변의 Rigidbody 를 밀어낸다.
        ///
        /// 자기 자신과 파츠는 제외한다. 파츠에 폭발력을 더 얹으면 조인트가 끊어질지를
        /// 솔버가 아니라 이 상수가 결정해 버려서, 용접 정도에 따른 차이가 흐려진다.
        /// 파츠에 대한 영향은 물리 충돌만으로 남긴다.
        /// </summary>
        void ApplyKnockback(Vector3 point, PlayerRig directHit)
        {
            var mask = GameLayers.PlayerMask | GameLayers.PropMask;
            var hits = UnityEngine.Physics.OverlapSphere(
                point, _impactRadius, mask, QueryTriggerInteraction.Ignore);

            Struck.Clear();

            foreach (var hit in hits)
            {
                var body = hit.attachedRigidbody;
                if (body == null || body == _body) continue;

                body.AddExplosionForce(_impactForce, point, _impactRadius);

                // 래그돌은 뼈마다 Rigidbody 다. 한 사람을 열 번 처리하면 안 된다.
                var player = hit.GetComponentInParent<PlayerRig>();
                if (player != null && !Struck.Contains(player)) Struck.Add(player);
            }

            // 직접 맞은 사람은 반경 밖에 있을 수도 있다 — 떨어지는 중에 얻어맞으면
            // 착탄 지점이 그 사람 발밑이 아니다.
            if (directHit != null && !Struck.Contains(directHit)) Struck.Add(directHit);

            for (var i = 0; i < Struck.Count; i++)
                ApplyTier(Struck[i], point, Struck[i] == directHit);

            Struck.Clear();
        }

        /// <summary>
        /// 한 사람이 받는 단계를 정해 적용한다(기획 §20.1).
        ///
        /// 폭발력은 이미 <see cref="ApplyKnockback"/> 이 넣었다. 여기서 더하는 것은
        /// <b>단계에 따라 달라지는 것</b>뿐이다 — 약한 충격파에는 아무것도 없다
        /// ("밀려나지만 바로 행동 가능").
        /// </summary>
        void ApplyTier(PlayerRig player, Vector3 point, bool direct)
        {
            if (player == null) return;

            if (direct)
            {
                // "날아간 뒤 기절". 순서가 중요하다 — GoDown 이 먼저면 KnockDown 이
                // Downed 를 보고 즉시 돌아나가 <b>날아가지 않는다</b>.
                player.KnockDown(AwayFrom(player, point), PhysicsScale.MeteorHeavyKnockSpeed);
                player.GoDown("운석 직접 충돌");
                return;
            }

            // <b>PlayerRig 의 transform 은 원점에 있다.</b> Create 가 빈 GameObject 를
            // 만들고 래그돌만 발 위치에 놓아 부모로 붙인다 — transform.position 으로
            // 재면 모든 플레이어가 원점 기준이 된다. 착탄 지점이 지면이므로 발에서 잰다.
            var distance = Vector3.Distance(player.FootPosition, point);
            if (MeteorImpact.TierFor(distance, _impactRadius) != MeteorImpactTier.Heavy) return;

            player.KnockDown(AwayFrom(player, point), PhysicsScale.MeteorHeavyKnockSpeed);
            player.DropEquipment();
        }

        /// <summary>착탄 지점에서 멀어지는 수평 방향. 위로 띄우는 것은 폭발력이 한다.</summary>
        static Vector3 AwayFrom(PlayerRig player, Vector3 point)
        {
            var away = player.FootPosition - point;
            away.y = 0f;
            return away.sqrMagnitude < 1e-6f ? Vector3.zero : away.normalized;
        }
    }
}
