using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// A non-blocking plant interaction. The trigger reports a passing player and
    /// only the visual hierarchy receives the resulting sway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlantRustle : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField, Min(0f)] float swayDegrees = 13f;
        [SerializeField, Min(0.01f)] float damping = 5.5f;
        [SerializeField, Range(0.5f, 5f)] float _swayFrequency = 2.2f;
        [SerializeField, Min(0f)] float playerCooldown = 0.08f;
        [SerializeField, Range(0.1f, 1f)]
        [Tooltip("이 수풀 안에서의 이동속도 배율. 1은 감속 없음. 새 키 큰 수풀의 초기값은 0.45입니다.")]
        float _movementSpeedMultiplier = 1f;

        readonly Dictionary<PlayerRig, float> playerCooldowns = new();
        Quaternion baseLocalRotation;
        Vector3 sway;
        Vector3 swayVelocity;

        public Transform Visual => visual;
        public Vector3 CurrentSway => sway;
        public float MovementSpeedMultiplier => Mathf.Clamp(_movementSpeedMultiplier, 0.1f, 1f);

        /// <summary>
        /// Sample current physics geometry, rather than retaining trigger membership:
        /// teleport, disabled colliders and respawn cannot leave a stale slowdown.
        /// Each authoritative/predicted rig owns its query buffer. Overlapping plants
        /// use the strongest slowdown, never a compounded near-zero speed.
        /// </summary>
        public static float SampleMovementMultiplier(Vector3 position, Collider[] buffer)
        {
            int count = Physics.OverlapSphereNonAlloc(position, 0.2f, buffer,
                GameLayers.PropMask, QueryTriggerInteraction.Collide);
            var hits = buffer;
            if (count == buffer.Length)
            {
                hits = Physics.OverlapSphere(position, 0.2f,
                    GameLayers.PropMask, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            float multiplier = 1f;
            for (int i = 0; i < count; i++)
            {
                if (hits[i] != null && hits[i].isTrigger &&
                    hits[i].TryGetComponent<PlantRustle>(out var plant) && plant.isActiveAndEnabled)
                    multiplier = Mathf.Min(multiplier, plant.MovementSpeedMultiplier);
            }
            return multiplier;
        }

        void Awake()
        {
            ResolveVisual();
            if (visual != null) baseLocalRotation = visual.localRotation;
        }

        void Update()
        {
            if (visual == null) return;
            if (sway == Vector3.zero && swayVelocity == Vector3.zero && playerCooldowns.Count == 0) return;

            var deltaTime = Time.deltaTime;
            // A damped spring gives a clearly visible bend and return oscillation.
            // Bound the step after a hitch to keep the spring stable.
            float remainingTime = Mathf.Min(deltaTime, 0.1f);
            float frequency = Mathf.PI * 2f * _swayFrequency;
            while (remainingTime > 0f)
            {
                float step = Mathf.Min(remainingTime, 1f / 120f);
                swayVelocity += (-frequency * frequency * sway - 2f * damping * swayVelocity) * step;
                sway += swayVelocity * step;
                remainingTime -= step;
            }
            sway = Vector3.ClampMagnitude(sway, swayDegrees);
            if (sway.sqrMagnitude < 0.0001f && swayVelocity.sqrMagnitude < 0.0001f)
                sway = swayVelocity = Vector3.zero;
            visual.localRotation = Quaternion.Euler(sway) * baseLocalRotation;

            if (playerCooldowns.Count == 0) return;
            var expired = ListPool<PlayerRig>.Get();
            var active = ListPool<PlayerRig>.Get();
            foreach (var entry in playerCooldowns)
                active.Add(entry.Key);

            for (var i = 0; i < active.Count; i++)
            {
                var player = active[i];
                if (!playerCooldowns.TryGetValue(player, out var remaining)) continue;
                remaining -= deltaTime;
                if (remaining <= 0f) expired.Add(player);
                else playerCooldowns[player] = remaining;
            }
            for (var i = 0; i < expired.Count; i++) playerCooldowns.Remove(expired[i]);
            ListPool<PlayerRig>.Release(expired);
            ListPool<PlayerRig>.Release(active);
        }

        void OnDisable()
        {
            if (visual != null && baseLocalRotation != default) visual.localRotation = baseLocalRotation;
            sway = Vector3.zero;
            swayVelocity = Vector3.zero;
            playerCooldowns.Clear();
        }

        void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || other == null) return;

            var player = other.GetComponentInParent<PlayerRig>();
            if (player == null || playerCooldowns.ContainsKey(player)) return;

            var velocity = other.attachedRigidbody != null
                ? other.attachedRigidbody.linearVelocity
                : player.Position - transform.position;
            var speed = velocity.magnitude;
            if (speed < 0.05f) speed = 1f;

            playerCooldowns[player] = playerCooldown;
            Pulse(transform.position - player.Position, speed);
        }

        /// <summary>Allows builders and deterministic tests to emit the same visual pulse.</summary>
        public void Pulse(Vector3 collisionDirection, float relativeSpeed = 1f)
        {
            ResolveVisual();
            if (visual == null) return;

            if (collisionDirection.sqrMagnitude < 0.0001f)
                collisionDirection = visual.forward;
            collisionDirection.y = 0f;
            if (collisionDirection.sqrMagnitude < 0.0001f)
                collisionDirection = Vector3.forward;

            var localDirection = visual.parent != null
                ? visual.parent.InverseTransformDirection(collisionDirection.normalized)
                : collisionDirection.normalized;
            var strength = Mathf.Clamp01(relativeSpeed / 4f);
            var impulse = new Vector3(-localDirection.z, 0f, localDirection.x)
                          * (swayDegrees * Mathf.Lerp(0.65f, 1f, strength));
            swayVelocity += impulse * (Mathf.PI * 2f * _swayFrequency);
            sway += impulse * 0.4f;
            sway = Vector3.ClampMagnitude(sway, swayDegrees);
        }

        void ResolveVisual()
        {
            if (visual != null) return;
            visual = transform.Find("Visual");
            if (visual == null)
            {
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (child.GetComponentInChildren<Renderer>(true) != null)
                    {
                        visual = child;
                        break;
                    }
                }
            }

            if (visual != null && baseLocalRotation == default)
                baseLocalRotation = visual.localRotation;
        }

        // Small reusable list pool avoids per-trigger allocations while preserving the
        // per-player de-duplication contract for ragdoll limbs.
        static class ListPool<T>
        {
            static readonly Stack<List<T>> Pool = new();

            public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>(4);

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
