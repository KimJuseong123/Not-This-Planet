using System.Collections.Generic;
using Metagame.Gameplay.Interaction;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Server-authoritative, non-blocking sweep with one impulse per actor per interval.</summary>
    public sealed class Stage1DustStormHazard : MonoBehaviour
    {
        Collider[] overlapBuffer = new Collider[128];
        readonly HashSet<Rigidbody> affectedBodies = new HashSet<Rigidbody>();
        readonly HashSet<PlayerRig> affectedPlayers = new HashSet<PlayerRig>();
        readonly Dictionary<Rigidbody, double> nextHits = new Dictionary<Rigidbody, double>();
        readonly List<Rigidbody> expiredHits = new List<Rigidbody>();
        AtmosphereProfile profile;
        AtmosphereProfile.DustStormSettings settings;
        DustStormSchedule schedule;
        Bounds mapBounds;
        bool configured;
        bool physicsAuthority;
        int activeCycle = -1;
        int? matchSeedOverride;
        BoxCollider stormVolume;
        double lastTime = double.NegativeInfinity;

        public void Configure(AtmosphereProfile atmosphereProfile, Bounds bounds)
        {
            Clear();
            profile = atmosphereProfile;
            if (profile == null) return;
            profile.Validate();
            settings = matchSeedOverride.HasValue ? profile.dustStorm.WithSeed(matchSeedOverride.Value) : profile.dustStorm;
            schedule = new DustStormSchedule(settings);
            mapBounds = bounds;
            configured = bounds.size.x > .01f && bounds.size.z > .01f;
            EnsureVolume();
        }

        public void SetMatchSeed(int seed)
        {
            matchSeedOverride = seed;
            if (profile == null || (settings != null && settings.seed == seed)) return;
            settings = profile.dustStorm.WithSeed(seed);
            schedule = new DustStormSchedule(settings);
            ResetHits();
            if (stormVolume != null) stormVolume.enabled = false;
        }

        public void SetPhysicsAuthority(bool value)
        {
            if (physicsAuthority == value) return;
            physicsAuthority = value;
            ResetHits();
            if (!value && stormVolume != null) stormVolume.enabled = false;
        }

        void EnsureVolume()
        {
            if (stormVolume != null) return;
            var root = new GameObject("Magnetic Storm Trigger");
            root.layer = 2; // Ignore Raycast: never intercept player interaction rays.
            root.transform.SetParent(transform, false);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            stormVolume = root.AddComponent<BoxCollider>();
            stormVolume.isTrigger = true;
            stormVolume.enabled = false;
        }

        public void FixedTick(double simulationTime)
        {
            if (!configured || !physicsAuthority || settings == null || schedule == null
                || !settings.enabled || (GameSession.Instance != null && GameSession.Instance.EndingActive))
            {
                if (stormVolume != null) stormVolume.enabled = false;
                return;
            }
            var pose = schedule.Evaluate(simulationTime, mapBounds);
            var wake = schedule.EvaluateWake(simulationTime, mapBounds);
            var previousWake = schedule.EvaluateWake(simulationTime, mapBounds, 1);
            bool hasWake = settings.wakeInfluence > 0f && (wake.Pose.IsActive || previousWake.Pose.IsActive);
            if (!pose.IsActive && !hasWake)
            {
                ResetHits();
                if (stormVolume != null) stormVolume.enabled = false;
                return;
            }
            if (simulationTime < lastTime) ResetHits();
            if (activeCycle != pose.CycleIndex)
            {
                expiredHits.Clear();
                foreach (var hit in nextHits)
                    if (hit.Key == null || hit.Value <= simulationTime) expiredHits.Add(hit.Key);
                for (int i = 0; i < expiredHits.Count; i++) nextHits.Remove(expiredHits[i]);
                expiredHits.Clear();
            }
            activeCycle = pose.CycleIndex;
            lastTime = simulationTime;
            EnsureVolume();
            stormVolume.enabled = pose.IsActive;
            if (pose.IsActive)
            {
                stormVolume.transform.SetPositionAndRotation(pose.Center, pose.Rotation);
                stormVolume.size = pose.HalfExtents * 2f;
            }
            affectedBodies.Clear();
            affectedPlayers.Clear();
            // Strong main-body hits take precedence; a compound actor is hit at most once.
            if (pose.IsActive) ApplyImpulses(pose, simulationTime);
            if (hasWake)
            {
                if (wake.Pose.IsActive) ApplyImpulses(wake.Pose, simulationTime, wake, true);
                if (previousWake.Pose.IsActive) ApplyImpulses(previousWake.Pose, simulationTime, previousWake, true);
            }
        }

        void ApplyImpulses(StormPose pose, double time, StormWake wake = default, bool residual = false)
        {
            int count;
            // Grow only on saturation so a dense group cannot silently lose eligible targets.
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(pose.Center, pose.HalfExtents, overlapBuffer,
                    pose.Rotation, settings.affectedLayers, QueryTriggerInteraction.Ignore);
                if (count < overlapBuffer.Length) break;
                System.Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
            }
            Vector3 kick = pose.Direction * settings.impulseSpeed + Vector3.up * settings.impulseLift;
            if (kick.sqrMagnitude < .000001f) return;
            for (int i = 0; i < count; i++)
            {
                var collider = overlapBuffer[i];
                var body = collider != null ? collider.attachedRigidbody : null;
                if (body == null || body.isKinematic) continue;
                var player = body.GetComponentInParent<PlayerRig>();
                if (player != null)
                {
                    // 엎드리면 폭풍이 지나간다 (S15P21B202-616). 이 판정은 자세를
                    // 전혀 보지 않는 OverlapBox 였다 — 상자 밑면이 사실상 지면이라
                    // (groundClearance 0.08m) 누워도 그대로 걸린다. 그래서 면제를
                    // 여기 한 줄로 둔다. **서버에서만 도는 자리**라 남의 화면과
                    // 판정이 갈리지 않는다.
                    if (player.Prone) continue;
                    float strength = residual && player.Doll != null && player.Doll.Hips != null
                        ? wake.StrengthAt(player.Doll.Hips.worldCenterOfMass) * settings.wakeInfluence : 1f;
                    if (strength <= .0001f) continue;
                    if (!affectedPlayers.Add(player) || player.Doll == null || player.Doll.Hips == null
                        || player.Doll.Hips.isKinematic || settings.playerImpulseMultiplier <= 0f
                        || !CanHit(player.Doll.Hips, time)) continue;
                    // The existing knockdown path preserves momentum against the walking controller.
                    var playerKick = kick * settings.playerImpulseMultiplier * strength;
                    if (player.Downed) player.RideAlong(playerKick, Vector3.zero, player.Doll.Hips.position);
                    else player.KnockDown(playerKick, playerKick.magnitude);
                    continue;
                }
                float influence = residual ? wake.StrengthAt(body.worldCenterOfMass) * settings.wakeInfluence : 1f;
                if (influence <= .0001f || !affectedBodies.Add(body) || settings.objectImpulseMultiplier <= 0f) continue;
                var grabbable = collider.GetComponentInParent<Grabbable>();
                bool interactive = grabbable != null && grabbable.CanGrab;
                interactive |= collider.GetComponentInParent<InteractableBase>() != null;
                if (!interactive || !CanHit(body, time)) continue;
                body.AddForce(kick * settings.objectImpulseMultiplier * influence, ForceMode.VelocityChange);
            }
        }

        bool CanHit(Rigidbody body, double time)
        {
            if (nextHits.TryGetValue(body, out double next) && time + .000001 < next) return false;
            nextHits[body] = time + settings.impulseInterval;
            return true;
        }

        void ResetHits()
        {
            nextHits.Clear();
            activeCycle = -1;
            lastTime = double.NegativeInfinity;
        }

        public void Clear()
        {
            ResetHits();
            if (stormVolume != null) stormVolume.enabled = false;
            profile = null;
            settings = null;
            schedule = null;
            configured = false;
        }

        void OnDisable() { if (Application.isPlaying) Clear(); }
        void OnDestroy()
        {
            if (stormVolume == null) return;
            if (Application.isPlaying) Destroy(stormVolume.gameObject);
            else DestroyImmediate(stormVolume.gameObject);
        }
    }
}
