using System;
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
        [SerializeField, Min(0f)] float _impactRadius = PhysicsScale.MeteorImpactRadius;
        [SerializeField, Min(0f)] float _impactForce = PhysicsScale.MeteorImpactForce;
        [SerializeField, Min(0f)] float _despawnSeconds = PhysicsScale.MeteorDespawnSeconds;
        [SerializeField, Min(0f)] float _maxLifeSeconds = PhysicsScale.MeteorMaxLifeSeconds;

        Rigidbody _body;
        float _sinceImpact;
        float _life;

        /// <summary>이미 부딪혔는지. 착탄은 한 번만 처리한다.</summary>
        public bool HasImpacted { get; private set; }

        /// <summary>사라질 때. 스포너가 목록에서 지우는 신호로 쓴다.</summary>
        public event Action<Meteor> Expired;

        /// <summary>착탄했을 때. 사운드와 이펙트가 여기에 붙는다.</summary>
        public event Action<Meteor, Vector3> Impacted;

        void Awake() => Initialize();

        /// <summary>여러 번 불러도 안전하다. Awake 는 EditMode 에서 호출되지 않는다.</summary>
        public void Initialize()
        {
            if (_body != null) return;

            _body = GetComponent<Rigidbody>();
            _body.mass = PhysicsScale.MeteorMass;
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
