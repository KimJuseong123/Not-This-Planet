using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Gameplay.Audio
{
    [DisallowMultipleComponent]
    public sealed class SoundCollisionEmitter : MonoBehaviour
    {
        SoundRuntime runtime;
        Rigidbody body;

        public void Configure(SoundRuntime owner)
        {
            runtime = owner;
            if (body == null) body = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (runtime == null || collision == null || collision.contactCount == 0) return;

            var otherCollider = collision.collider;
            if (otherCollider == null) return;

            var ownPlayer = GetComponentInParent<PlayerRig>();
            var otherPlayer = otherCollider.GetComponentInParent<PlayerRig>();
            if (ownPlayer != null && otherPlayer != null) return;

            var otherBody = collision.rigidbody;
            if (otherBody != null)
            {
                if (body == null) body = GetComponent<Rigidbody>();
                if (body != null && body.GetEntityId().CompareTo(otherBody.GetEntityId()) > 0) return;
                if (otherBody == body) return;

                var ownCollider = GetComponent<Collider>();
                if (ownCollider == null) ownCollider = GetComponentInChildren<Collider>();
                var first = SoundSurface.Resolve(ownCollider);
                var second = SoundSurface.Resolve(otherCollider);
                var eventId = SoundSurface.ImpactEvent(first, second);
                Emit(eventId, collision.relativeVelocity.magnitude, collision.GetContact(0).point);
                return;
            }

            var speed = collision.relativeVelocity.magnitude;
            if (speed < 0.2f) return;
            Emit(SoundEventId.ObjectLand, speed, collision.GetContact(0).point);
        }

        void Emit(SoundEventId eventId, float speed, Vector3 point)
        {
            var intensity = Mathf.Clamp01(speed / 8f);
            if (intensity < 0.04f) return;
            runtime.EmitWorldOneShot(eventId, point, Mathf.Lerp(0.25f, 1f, intensity));
        }
    }
}
