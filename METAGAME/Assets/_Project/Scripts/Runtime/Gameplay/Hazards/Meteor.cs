using System;
using System.Collections.Generic;
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
        [SerializeField, Min(0f)] float _maxLifeSeconds = PhysicsScale.MeteorMaxLifeSeconds;

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

        void Awake() => Initialize();

        /// <summary>여러 번 불러도 안전하다. Awake 는 EditMode 에서 호출되지 않는다.</summary>
        public void Initialize()
        {
            Register();

            if (_body != null) return;

            _body = GetComponent<Rigidbody>();
            _body.mass = PhysicsScale.MeteorMass;
        }

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
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;

            // 운석 본체가 몸에 닿았는가. 거리로는 알 수 없다 — 착탄 지점 바로 옆에 서
            // 있는 것과 운석에 얻어맞는 것은 §20.1 이 다른 단계로 나눈다.
            var direct = collision.collider != null
                ? collision.collider.GetComponentInParent<PlayerRig>()
                : null;

            ApplyImpact(collision.GetContact(0).point, direct);
        }

        /// <summary>
        /// 착탄 처리. 주변에 넉백을 주고 사라짐 타이머를 시작한다.
        ///
        /// 물리 콜백은 EditMode 의 Physics.Simulate 에서 오지 않으므로 공개해 두었다.
        /// 테스트와 연출 스크립트가 직접 부를 수 있다.
        /// </summary>
        public void ApplyImpact(Vector3 point, PlayerRig directHit = null)
        {
            if (HasImpacted) return;

            Initialize();
            HasImpacted = true;

            ApplyKnockback(point, directHit);
            Settle(point);
            Impacted?.Invoke(this, point);
        }

        /// <summary>
        /// 소유자가 매 물리 스텝 호출한다. 착탄 후 사라짐까지의 시간을 센다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // 박힌 뒤에는 아무것도 하지 않는다(기획 §19). 스포너가 흔적 목록으로 옮겨
            // 더 부르지 않지만, 누가 불러도 안전해야 한다.
            if (Settled) return;

            _life += deltaTime;

            // 착탄 처리는 물리 충돌 콜백에 달려 있다. 운석이 플레이 공간 밖으로 떨어지거나
            // 콜백을 놓치면 영구히 남으므로 수명 상한으로 반드시 사라지게 한다.
            //
            // 착탄한 운석에는 이 상한이 걸리지 않는다 — Settled 에서 이미 돌아나갔다.
            if (_life >= _maxLifeSeconds) Expire();
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
        void Settle(Vector3 point)
        {
            Settled = true;

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
            }

            // "절반 이상 지면에 박힌 모습"(§19). 착탄 지점이 지면이므로 그 아래로 내린다.
            var sunk = point;
            sunk.y = point.y
                     + PhysicsScale.MeteorRadius * (1f - 2f * PhysicsScale.MeteorScarSinkRatio);
            transform.position = sunk;
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
