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
    /// 넉백만 코드로 준다. 착탄 지점 주변에 폭발력을 한 번 넣는다.
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
        [SerializeField, Min(0f)] float _despawnSeconds = PhysicsScale.MeteorDespawnSeconds;
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
        float _sinceImpact;
        float _life;

        /// <summary>이미 부딪혔는지. 착탄은 한 번만 처리한다.</summary>
        public bool HasImpacted { get; private set; }

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
            _impactRadius = size == MeteorSize.Medium
                ? PhysicsScale.MeteorMediumImpactRadius
                : PhysicsScale.MeteorSmallImpactRadius;
        }

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

            ApplyImpact(collision.GetContact(0).point);
        }

        /// <summary>
        /// 착탄 처리. 주변에 넉백을 주고 사라짐 타이머를 시작한다.
        ///
        /// 물리 콜백은 EditMode 의 Physics.Simulate 에서 오지 않으므로 공개해 두었다.
        /// 테스트와 연출 스크립트가 직접 부를 수 있다.
        /// </summary>
        public void ApplyImpact(Vector3 point)
        {
            if (HasImpacted) return;

            Initialize();
            HasImpacted = true;
            _sinceImpact = 0f;

            ApplyKnockback(point);
            Impacted?.Invoke(this, point);
        }

        /// <summary>
        /// 소유자가 매 물리 스텝 호출한다. 착탄 후 사라짐까지의 시간을 센다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _life += deltaTime;

            // 착탄 처리는 물리 충돌 콜백에 달려 있다. 운석이 플레이 공간 밖으로 떨어지거나
            // 콜백을 놓치면 영구히 남으므로 수명 상한으로 반드시 사라지게 한다.
            if (_life >= _maxLifeSeconds)
            {
                Expire();
                return;
            }

            if (!HasImpacted) return;

            _sinceImpact += deltaTime;
            if (_sinceImpact < _despawnSeconds) return;

            Expire();
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
        void ApplyKnockback(Vector3 point)
        {
            var mask = GameLayers.PlayerMask | GameLayers.PropMask;
            var hits = UnityEngine.Physics.OverlapSphere(
                point, _impactRadius, mask, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                var body = hit.attachedRigidbody;
                if (body == null || body == _body) continue;

                body.AddExplosionForce(_impactForce, point, _impactRadius);
            }
        }
    }
}
