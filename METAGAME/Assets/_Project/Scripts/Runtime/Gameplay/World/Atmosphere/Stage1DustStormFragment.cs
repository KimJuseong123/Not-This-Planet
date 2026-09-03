using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Server-side pooled collision fragment emitted by the Stage1 dust storm.
    /// </summary>
    public sealed class Stage1DustStormFragment : MonoBehaviour
    {
        Rigidbody body;
        Collider fragmentCollider;
        float damage;
        float repeatCooldown;
        double expiresAt;
        readonly Dictionary<PlayerRig, double> nextDamageAt = new Dictionary<PlayerRig, double>();

        public Rigidbody Body { get { return body; } }

        public void Configure(float damageAmount, float cooldown, float lifetime)
        {
            damage = Mathf.Max(0f, damageAmount);
            repeatCooldown = Mathf.Max(0.01f, cooldown);
            if (body == null) body = GetComponent<Rigidbody>();
            if (fragmentCollider == null) fragmentCollider = GetComponent<Collider>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            if (fragmentCollider == null) fragmentCollider = gameObject.AddComponent<SphereCollider>();

            body.mass = 0.25f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(Vector3 position, Vector3 velocity, double now, float lifetime)
        {
            if (body == null) Configure(damage, repeatCooldown, lifetime);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(
                velocity.sqrMagnitude > 0.0001f ? velocity : Vector3.forward, Vector3.up));
            body.isKinematic = false;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
            expiresAt = now + Mathf.Max(0.1f, lifetime);
            nextDamageAt.Clear();
            gameObject.SetActive(true);
        }

        public bool IsExpired(double now)
        {
            return now >= expiresAt;
        }

        public void ResetForPool()
        {
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            nextDamageAt.Clear();
            expiresAt = 0d;
            gameObject.SetActive(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null) return;
            TryDamage(collision.collider);
        }

        void OnTriggerEnter(Collider other)
        {
            TryDamage(other);
        }

        void TryDamage(Collider target)
        {
            if (damage <= 0f || target == null) return;
            var player = target.GetComponentInParent<PlayerRig>();
            if (player == null || player.IsDead) return;

            double now = Time.timeAsDouble;
            if (nextDamageAt.TryGetValue(player, out var next) && now < next) return;
            nextDamageAt[player] = now + repeatCooldown;
            player.ApplyDamage(damage, "모래폭풍 파편");
        }
    }
}
