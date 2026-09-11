using System;
using System.Collections.Generic;
using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>
    /// 1장의 방해 요소인 작은 운석. 떨어져서 부딪힌 뒤 일반 운반물로 전환된다.
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

        /// <summary>
        /// 이번 충격으로 굴릴 기체. 콜라이더가 여러 개라 같은 기체가 여러 번 걸리므로
        /// 모아서 한 번만 밀어야 한다 — 여러 번 주면 회전량이 예측할 수 없게 커진다.
        /// </summary>
        static readonly List<Parts.TransportVehicle> StruckCraft = new();

        /// <summary>
        /// 이 운석이 이미 굴린 기체. 콜라이더가 여러 개라 한 충돌에서 여러 번 들어오고,
        /// 그때마다 굴리면 회전량이 예측할 수 없게 커진다
        /// (<see cref="_directPlayersHit"/> 와 같은 이유다).
        /// </summary>
        readonly HashSet<Parts.TransportVehicle> _craftHit = new();

        /// <summary>
        /// 이 충돌이 어느 탑승체를 때린 것인가. 아니면 null 이다
        /// (S15P21B202-353).
        ///
        /// <b>탑승자를 맞힌 것도 기체를 맞힌 것이다.</b> 인형은 조인트로 붙어 있을 뿐
        /// 기체의 <b>자식이 아니므로</b> <c>GetComponentInParent</c> 로는 찾을 수 없다.
        /// 조종석에 앉은 사람이 운석에 맞으면 기체가 대신 받는다(기획 §14.1 "기체와
        /// 파츠는 충돌로 파괴되지 않는다").
        ///
        /// <b>실제로는 이 경로가 먼저 걸린다</b> — 인형이 플랫폼 위에 서 있으므로 위에서
        /// 떨어지는 운석은 플랫폼보다 인형을 먼저 만난다. 측정에서 운석이 인형 머리
        /// (9.25m)에 닿아 튕겼다.
        ///
        /// <b>갈라 둔 이유.</b> <c>OnCollisionEnter</c> 는 EditMode 에서 돌지 않으므로
        /// (이 저장소의 다른 운석 검증도 <see cref="ApplyImpact"/> 를 직접 부른다) 판정
        /// 규칙만이라도 검증할 수 있어야 한다.
        /// </summary>
        public static Parts.TransportVehicle CraftHitBy(Collider collider, PlayerRig direct)
        {
            var craft = collider != null
                ? collider.GetComponentInParent<Parts.TransportVehicle>()
                : null;

            if (craft == null && direct != null) craft = direct.MountedVehicle;

            return craft;
        }
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

        /// <summary>
        /// 이 돌의 강체. <b>훑는 길에서 다시 찾지 않는다</b>
        /// (S15P21B202-355, <c>!321</c> AI 리뷰).
        ///
        /// <para>흔적 동기화가 송신 틱마다 흔적 전체를 훑는데, 거기서
        /// <c>GetComponent&lt;Rigidbody&gt;()</c> 를 부르면 상한 200개 × 30Hz 로
        /// <b>프레임당 수백 회</b>가 된다. <c>-374</c>(여러 명이 같이 뭔가 하면 프레임이
        /// 떨어진다)가 열려 있는 동안 그런 것을 얹으면 원인을 가릴 수 없다.</para>
        ///
        /// <para><b>비어 있으면 그때 한 번 찾는다.</b> <c>_body</c> 는 <c>Awake</c> 가
        /// 채우는데 <b>에디터 테스트에서는 <c>Awake</c> 가 돌지 않는다</b> — 그러면 이 값이
        /// <c>null</c> 이고, 자세를 강체가 아니라 <c>transform</c> 에 얹어 물리 스텝 전까지
        /// <c>Rigidbody.position</c> 이 0 으로 남는다(2026-09-10 <c>!321</c> 에서 테스트
        /// 2건이 그것을 잡았다). 한 번 찾으면 캐시되므로 매 틱 찾는 것과는 다르다.</para>
        ///
        /// <para>착지한 뒤에도 <b>kinematic 이 아니다</b>(<c>Settle</c> 이
        /// <c>isKinematic = false</c> 로 둔다) — 그래서 스스로 잠들고, 방망이에 맞으면
        /// 깨어난다. 흔적 동기화가 "움직이는 것만" 을 그 잠듦으로 판별한다.</para>
        /// </summary>
        public Rigidbody Body => _body != null ? _body : (_body = GetComponent<Rigidbody>());
        MeteorTuning _tuning;
        bool _tuningScaleApplied;
        float _life;
        float _sinceRolling = -1f;
        float _sinceSettled = -1f;
        float _atRestSeconds;

        /// <summary>정착 안전망의 레이캐스트 버퍼. 매 틱 새로 만들지 않는다.</summary>
        static readonly RaycastHit[] RestProbe = new RaycastHit[8];

        /// <summary>이미 부딪혔는지. 착탄은 한 번만 처리한다.</summary>
        public bool HasImpacted { get; private set; }

        /// <summary>
        /// 낙하 착탄 처리가 끝났는지.
        ///
        /// <b>예전에는 착탄 6초 뒤에 사라졌다.</b> §19 는 "박힌 운석과 분화구는 발사 실패
        /// 후에도 유지되며 <b>스테이지 동안 사라지지 않는다</b>" 고 정한다 — 정확히
        /// 반대였다(S15P21B202-251).
        ///
        /// 착탄 이벤트와 충격 처리는 한 번만 실행하고, 이후에는 일반 운반물 물리로
        /// 전환한다. 시각적인 분화구 기록은 스포너가 별도로 유지한다.
        /// </summary>
        public bool Settled { get; private set; }

        /// <summary>
        /// 쳐내서 <b>굴러다니는 소품</b>이 되었는가(S15P21B202-273).
        ///
        /// 운석은 방망이로 쳐내거나 바닥에 닿은 뒤 소품이 된다.
        ///
        /// <code>
        /// 방망이로 쳐냄   →  동적 유지 · Prop 레이어 · 방망이로 밀린다 · 굴러다닌다
        /// 바닥에 닿음     →  동적 유지 · Prop 레이어 · 손으로 운반할 수 있다
        /// </code>
        ///
        /// Prop 레이어로 옮기는 것이 핵심이다. <see cref="GameLayers.BatStrikeMask"/> 가
        /// <see cref="GameLayers.Meteor"/> 를 빼고 <see cref="GameLayers.Prop"/> 은
        /// 포함하므로(S15P21B202-257), 레이어만 옮기면 <b>때릴 수 있는 소품이 된다</b> —
        /// 새 규칙을 만들지 않는다.
        /// </summary>
        public bool Rolling { get; private set; }

        /// <summary>
        /// 착지 처리가 끝나 일반 운반물로 전환되었는가.
        ///
        /// 착지한 운석은 낙하 위험물로 남지 않고 일반 <see cref="Grabbable"/> 소품이
        /// 된다. 방망이의 소품 밀기와 플레이어의 운반 경로가 같은 Rigidbody를 사용한다.
        /// </summary>
        public bool GroundProp { get; private set; }

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
        /// 착탄한 지면 위치. 분화구와 충격 표시가 사용할 기준점이다.
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

        /// <summary>스포너가 넘긴 장면별 방망이 판정 창.</summary>
        public float BatSwingWindowSeconds =>
            _tuning != null ? _tuning.BatSwingWindowSeconds : PhysicsScale.BatSwingWindowSeconds;

        /// <summary>스포너가 넘긴 장면별 방망이 보조 반경.</summary>
        public float BatAssistRadius =>
            _tuning != null ? _tuning.BatAssistRadius : PhysicsScale.BatAssistRadius;

        /// <summary>
        /// 스포너의 장면별 조정값을 적용한다. Instantiate 직후에도 호출할 수 있도록
        /// 이미 Rigidbody 가 만들어진 경우 질량을 즉시 갱신한다.
        /// </summary>
        public void ApplyTuning(MeteorTuning tuning)
        {
            _tuning = tuning;

            if (tuning == null) return;

            _impactRadius = tuning.ImpactRadiusFor(Size);
            _impactForce = tuning.ImpactForceFor(Size);
            _maxLifeSeconds = tuning.MaxLifeSeconds;

            if (!_tuningScaleApplied && PhysicsScale.MeteorRadius > 0f && tuning.Radius > 0f)
            {
                transform.localScale *= tuning.Radius / PhysicsScale.MeteorRadius;
                _tuningScaleApplied = true;
            }

            if (_body != null) _body.mass = tuning.Mass;
        }

        /// <summary>스포너가 크기를 정한다. 반경이 함께 바뀐다.</summary>
        public void SetSize(MeteorSize size)
        {
            Size = size;
            _impactRadius = _tuning != null ? _tuning.ImpactRadiusFor(size) : RadiusFor(size);
            _impactForce = _tuning != null
                ? _tuning.ImpactForceFor(size)
                : MeteorImpact.ForceFor(size);
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

            // 재사용에 대비해 한 생애의 기록을 비운다. 지금 스포너는 운석을
            // <c>Instantiate</c> 하고 <c>Despawn</c> 으로 없애므로 재사용 경로가 없지만,
            // 풀링이 들어오면 두 번째 생애에서 같은 상대를 다시 때리지 못한다.
            _directPlayersHit.Clear();
            _craftHit.Clear();

            EnsureTrail();

            // 예전 프리팹에 남아 있던 일반 DamageSource는 운석의 충돌 규칙과 중복된다.
            // 운석 피해는 ApplyImpact/ApplyKnockback이 한 번만 소유해야 하므로, 기존
            // 씬·프리팹 인스턴스가 있어도 초기화 즉시 비활성화한다.
            var legacyDamageSource = GetComponent<DamageSource>();
            if (legacyDamageSource != null) legacyDamageSource.enabled = false;

            if (_body != null) return;

            _body = GetComponent<Rigidbody>();
            _body.mass = _tuning != null ? _tuning.Mass : PhysicsScale.MeteorMass;
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
            // 착탄이 끝난 운석은 일반 운반물이다. 착지 후 새 충돌을 직접 충돌로
            // 해석하면 몸이 스치기만 해도 다시 기절하므로, 이후 접촉은 PhysX에 맡긴다.
            if (HasImpacted || Rolling || GroundProp) return;
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

                // <b>기체를 직접 맞혔으면 굴린다</b>(S15P21B202-353).
                //
                // 이것이 실제로 일어나는 경로다. <see cref="ApplyKnockback"/> 의 반경
                // 판정은 2m 인데 호버 고도는 5~8m 라 떠 있는 기체에 닿지 않는다
                // (S15P21B202-349 에서 거리 8.13m 로 측정했다).
                //
                // 운석을 착탄시키지는 않는다 — 위 <c>TryGetSupportContact</c> 가 운반체를
                // 건너뛰는 예전 결정을 그대로 둔다. 기체에 박히면 따라다닌다.
                var craft = CraftHitBy(collision.collider, direct);

                if (craft != null && !_craftHit.Contains(craft))
                {
                    _craftHit.Add(craft);
                    craft.TakeDirectMeteorImpact(collision.GetContact(0).point,
                                                 _body != null ? _body.linearVelocity
                                                               : Vector3.down);
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
            if (HasImpacted || Rolling || GroundProp) return;

            // 쳐낸 운석은 흔적이 되지 않는다(S15P21B202-273). 착지해도 굴러다니는 소품으로
            // 남는다 — 두 상태가 섞이면 "쳐냈는데 박혀 있는" 것이 된다.

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
                if (_sinceSettled >= ScarDespawnSeconds) Expire();
                return;
            }

            // 쳐내서 굴러다니는 소품. 더 짧게 남는다 — 플레이어가 처리한 것이고,
            // 동적 물체가 조립 구역에 오래 굴러다니면 방해가 된다.
            if (Rolling)
            {
                _sinceRolling += deltaTime;
                if (_sinceRolling >= DeflectedDespawnSeconds) Expire();
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

            if (_body.linearVelocity.sqrMagnitude > RestSpeed * RestSpeed)
            {
                _atRestSeconds = 0f;
                return;
            }

            _atRestSeconds += deltaTime;
            if (_atRestSeconds < RestSeconds) return;

            if (!TryFindSupportBelow(out var point, out var normal)) return;

            ApplyImpact(point, null, normal);
        }

        /// <summary>몸통 아래의 정적 지지면. 없으면 <c>false</c>.</summary>
        bool TryFindSupportBelow(out Vector3 point, out Vector3 normal)
        {
            point = transform.position;
            normal = Vector3.up;

            var from = _body.worldCenterOfMass + Vector3.up * MeteorRadius;
            var distance = MeteorRadius * 3f;

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
        /// 착탄 후 일반 운반물로 전환한다.
        ///
        /// 피해는 이미 한 번만 돈다 — <see cref="HasImpacted"/> 가
        /// <see cref="ApplyImpact"/> 와 후속 충돌 콜백을 막고, <c>DamageSource</c> 는
        /// 운석에 붙지 않는다.
        /// </summary>
        void Settle(Vector3 point, Vector3 normal)
        {
            Settled = true;
            GroundProp = true;
            _sinceSettled = 0f;
            StopTrail();

            if (_body != null)
            {
                // 동적으로 **먼저** 돌린다 (S15P21B202-433). 들고 있던 돌은
                // kinematic 이고(`BeginCarriedBody`), 그 상태로 속도를 쓰면 거부된다 —
                // 그러면 착지 순간의 남은 운동량이 지워지지 않는다. 순서가 뜻을 바꾼다.
                _body.isKinematic = false;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.useGravity = true;
            }

            // 착지 직후에도 일반 운반물처럼 바닥 위에 남는다. 기존의 반쯤 묻는 위치
            // 보정은 Collider를 지면 아래에 남겨 운반·방망이 판정을 막았다.
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
            normal.Normalize();
            transform.position = point
                                + normal * MeteorRadius;

            // 착지한 운석은 일반 운반물과 같은 상호작용 경로를 사용한다. 레이어를
            // 바꾸면 방망이의 소품 판정에 들어가고, Grabbable을 붙이면 손으로 집을 수
            // 있다. Rigidbody는 동적으로 유지해 밀기·운반·놓기가 모두 가능하다.
            SetLayerDeep(gameObject, GameLayers.Prop);
            Grabbable.Mark(gameObject, "운석", false, 1f, 0f, GrabbableRole.Transport);
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

                // 래그돌은 뼈마다 Rigidbody 다. 한 사람을 열 번 처리하면 안 된다.
                var player = hit.GetComponentInParent<PlayerRig>();

                // <b>탑승 중인 사람의 뼈에는 힘을 주지 않는다</b>(S15P21B202-349).
                // 기획 §14.1 이 "기체와 파츠는 충돌로 파괴되지 않는다" 로 정했으므로
                // 기체가 대신 맞는다. 예전에는 골반만 조인트로 묶여 있고 나머지 뼈에
                // 폭발력이 들어가 <b>몸이 흩어졌다</b> — 조종석에 앉아 있는데 떨어져
                // 나온 것처럼 보였다.
                if (player != null && player.IsMounted)
                {
                    if (!Struck.Contains(player)) Struck.Add(player);
                    continue;
                }

                // 기체는 모아서 한 번만 처리한다. 폭발력으로는 80kg 기체가 움직이지
                // 않으므로 <see cref="Parts.TransportVehicle.TakeMeteorImpact"/> 가
                // 속도와 각속도를 직접 준다.
                var craft = hit.GetComponentInParent<Parts.TransportVehicle>();
                if (craft != null)
                {
                    if (!StruckCraft.Contains(craft)) StruckCraft.Add(craft);
                    continue;
                }

                body.AddExplosionForce(_impactForce, point, _impactRadius);

                if (player != null && !Struck.Contains(player)) Struck.Add(player);
            }

            for (var i = 0; i < StruckCraft.Count; i++)
                StruckCraft[i].TakeMeteorImpact(point, _impactRadius);

            StruckCraft.Clear();

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

            // <b>탑승 중이면 기체가 맞았다</b>(S15P21B202-349, 기획 §14.1 · §14.2).
            // 기절시키지 않는다 — 조종석에 앉아 있는 사람을 운석이 직접 때릴 수는 없다.
            // 콜라이더로도 그렇다: 조종석은 기체 콜라이더 안이라 운석은 기체를 먼저
            // 맞는다. 밀림은 기체가 돌며 밀리는 것으로 표현된다(§20.1 "밀려나지만
            // 바로 행동 가능").
            if (player.IsMounted) return;

            if (direct)
            {
                // "날아간 뒤 기절". 순서가 중요하다 — GoDown 이 먼저면 KnockDown 이
                // Downed 를 보고 즉시 돌아나가 <b>날아가지 않는다</b>.
                player.KnockDown(AwayFrom(player, point), HeavyKnockSpeed);
                player.GoDown("운석 직접 충돌");
                return;
            }

            // <b>PlayerRig 의 transform 은 원점에 있다.</b> Create 가 빈 GameObject 를
            // 만들고 래그돌만 발 위치에 놓아 부모로 붙인다 — transform.position 으로
            // 재면 모든 플레이어가 원점 기준이 된다. 착탄 지점이 지면이므로 발에서 잰다.
            var distance = Vector3.Distance(player.FootPosition, point);
            if (MeteorImpact.TierFor(distance, _impactRadius, HeavyImpactFraction)
                != MeteorImpactTier.Heavy) return;

            player.KnockDown(AwayFrom(player, point), HeavyKnockSpeed);
            player.DropEquipment();
        }

        /// <summary>착탄 지점에서 멀어지는 수평 방향. 위로 띄우는 것은 폭발력이 한다.</summary>
        static Vector3 AwayFrom(PlayerRig player, Vector3 point)
        {
            var away = player.FootPosition - point;
            away.y = 0f;
            return away.sqrMagnitude < 1e-6f ? Vector3.zero : away.normalized;
        }

        float MeteorRadius => _tuning != null ? _tuning.Radius : PhysicsScale.MeteorRadius;
        float HeavyImpactFraction =>
            _tuning != null ? _tuning.HeavyImpactFraction : PhysicsScale.MeteorHeavyImpactFraction;
        float HeavyKnockSpeed =>
            _tuning != null ? _tuning.HeavyKnockSpeed : PhysicsScale.MeteorHeavyKnockSpeed;
        float RestSpeed => _tuning != null ? _tuning.RestSpeed : PhysicsScale.MeteorRestSpeed;
        float RestSeconds => _tuning != null ? _tuning.RestSeconds : PhysicsScale.MeteorRestSeconds;
        float DeflectedDespawnSeconds =>
            _tuning != null ? _tuning.DeflectedDespawnSeconds : PhysicsScale.MeteorDeflectedDespawnSeconds;
        float ScarDespawnSeconds =>
            _tuning != null ? _tuning.ScarDespawnSeconds : PhysicsScale.MeteorScarDespawnSeconds;
        float ScarSinkRatio =>
            _tuning != null ? _tuning.ScarSinkRatio : PhysicsScale.MeteorScarSinkRatio;
    }
}
