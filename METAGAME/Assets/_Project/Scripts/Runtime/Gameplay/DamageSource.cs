using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 운석·함정·기타 위험 오브젝트에 붙여 플레이어 충돌 피해를 제공한다.
    /// Collider가 있는 오브젝트에 이 컴포넌트를 추가하면 물리 충돌과 트리거를 모두 받을 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageSource : MonoBehaviour
    {
        [SerializeField, Min(0f)] float damage = 25f;
        [SerializeField, Min(0f)] float minImpactSpeed;
        [SerializeField, Min(0f)] float repeatCooldown = 0.5f;

        readonly Dictionary<PlayerRig, float> nextDamageTime = new Dictionary<PlayerRig, float>();

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            TryDamage(collision.collider, collision.relativeVelocity.magnitude, true);
        }

        void OnTriggerEnter(Collider other)
        {
            TryDamage(other, 0f, false);
        }

        void TryDamage(Collider target, float impactSpeed, bool checkImpactSpeed)
        {
            if (target == null) return;
            if (checkImpactSpeed && impactSpeed < minImpactSpeed) return;

            var player = target.GetComponentInParent<PlayerRig>();
            if (player == null || player.IsDead) return;

            float now = Time.time;
            float next;
            if (nextDamageTime.TryGetValue(player, out next) && now < next) return;

            nextDamageTime[player] = now + repeatCooldown;
            player.ApplyDamage(damage, gameObject.name);
        }
    }
}
