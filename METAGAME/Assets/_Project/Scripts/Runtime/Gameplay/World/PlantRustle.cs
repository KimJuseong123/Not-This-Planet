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
        [SerializeField, Min(0f)] float playerCooldown = 0.08f;

        readonly Dictionary<PlayerRig, float> playerCooldowns = new();
        Quaternion baseLocalRotation;
        Vector3 sway;
        Vector3 swayVelocity;

        public Transform Visual => visual;
        public Vector3 CurrentSway => sway;

        void Awake()
        {
            ResolveVisual();
            if (visual != null) baseLocalRotation = visual.localRotation;
        }

        void Update()
        {
            if (visual == null) return;

            var deltaTime = Time.deltaTime;
            swayVelocity = Vector3.MoveTowards(swayVelocity, Vector3.zero, damping * deltaTime);
            sway = Vector3.MoveTowards(sway, Vector3.zero, damping * deltaTime);
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

        void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

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
            swayVelocity += new Vector3(-localDirection.z, 0f, localDirection.x)
                            * (swayDegrees * Mathf.Lerp(0.35f, 1f, strength));
            sway += swayVelocity * 0.08f;
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
