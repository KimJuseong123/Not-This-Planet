using System.Collections.Generic;
using Metagame.Core;
using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// Server-authoritative physical part of the Stage1 dust storm.
    /// Decorative particles never enter this component's physics path.
    /// </summary>
    public sealed class Stage1DustStormHazard : MonoBehaviour
    {
        const int OverlapCapacity = 256;

        readonly Collider[] overlapBuffer = new Collider[OverlapCapacity];
        readonly HashSet<Rigidbody> affectedBodies = new HashSet<Rigidbody>();
        readonly HashSet<PlayerRig> affectedPlayers = new HashSet<PlayerRig>();
        readonly List<Stage1DustStormFragment> fragmentPool = new List<Stage1DustStormFragment>(3);

        AtmosphereProfile profile;
        AtmosphereProfile.DustStormSettings settings;
        DustStormSchedule schedule;
        Bounds mapBounds;
        bool configured;
        bool physicsAuthority;
        int spawnedCycle = -1;

        public void Configure(AtmosphereProfile atmosphereProfile, Bounds bounds)
        {
            ReleaseAllFragments();
            profile = atmosphereProfile;
            if (profile == null)
            {
                settings = null;
                schedule = null;
                configured = false;
                return;
            }

            profile.Validate();
            settings = profile.dustStorm;
            schedule = new DustStormSchedule(settings);
            mapBounds = bounds;
            configured = bounds.size.x > 0.01f && bounds.size.z > 0.01f;
            spawnedCycle = -1;
        }

        public void SetPhysicsAuthority(bool value)
        {
            if (physicsAuthority == value) return;
            physicsAuthority = value;
            if (!physicsAuthority) ReleaseAllFragments();
        }

        public void FixedTick(double simulationTime)
        {
            if (!configured || !physicsAuthority || settings == null || schedule == null)
                return;

            ReleaseExpiredFragments(Time.timeAsDouble);
            if (!settings.enabled) return;

            var pose = schedule.Evaluate(simulationTime, mapBounds);
            if (!pose.IsActive)
            {
                if (spawnedCycle >= 0) ReleaseAllFragments();
                spawnedCycle = -1;
                return;
            }

            if (spawnedCycle != pose.CycleIndex)
            {
                ReleaseAllFragments();
                SpawnFragments(pose);
                spawnedCycle = pose.CycleIndex;
            }

            ApplyWind(pose);
        }

        public void Clear()
        {
            ReleaseAllFragments();
            profile = null;
            settings = null;
            schedule = null;
            configured = false;
            spawnedCycle = -1;
        }

        void OnDisable()
        {
            if (Application.isPlaying) Clear();
        }

        void ApplyWind(StormPose pose)
        {
            if (settings.windForce <= 0f || settings.maximumAffectedMass <= 0f)
                return;

            affectedBodies.Clear();
            affectedPlayers.Clear();
            int count = Physics.OverlapBoxNonAlloc(
                pose.Center,
                pose.HalfExtents,
                overlapBuffer,
                pose.Rotation,
                settings.affectedLayers,
                QueryTriggerInteraction.Ignore);

            Vector3 force = pose.Direction * settings.windForce;
            for (int i = 0; i < count; i++)
            {
                var collider = overlapBuffer[i];
                if (collider == null) continue;
                var body = collider.attachedRigidbody;
                if (body == null || body.isKinematic) continue;

                var player = body.GetComponentInParent<PlayerRig>();
                if (player != null)
                {
                    // A ragdoll is one physical actor. Push its hips once rather than
                    // applying the same force to every limb collider.
                    if (!affectedPlayers.Add(player) || player.Doll == null || player.Doll.Hips == null)
                        continue;
                    if (player.Doll.TotalMass <= settings.maximumAffectedMass
                        && !player.Doll.Hips.isKinematic)
                        player.Doll.Hips.AddForce(force, ForceMode.Force);
                    continue;
                }

                if (!affectedBodies.Add(body) || body.mass > settings.maximumAffectedMass)
                    continue;
                body.AddForce(force, ForceMode.Force);
            }
        }

        void SpawnFragments(StormPose pose)
        {
            int maximum = settings.maxPhysicalFragments;
            if (maximum <= 0) return;

            int count = 1 + Mathf.FloorToInt(
                DustStormSchedule.Sample01(settings.seed, pose.CycleIndex, 20) * maximum);
            count = Mathf.Clamp(count, 1, maximum);

            Vector3 side = Vector3.Cross(Vector3.up, pose.Direction).normalized;
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;

            for (int i = 0; i < count; i++)
            {
                var fragment = GetOrCreateFragment(i);
                if (fragment == null) continue;

                float along = Mathf.Lerp(-pose.HalfExtents.x * 0.7f,
                                         pose.HalfExtents.x * 0.7f,
                                         DustStormSchedule.Sample01(settings.seed, pose.CycleIndex, 30 + i * 3));
                float lateral = Mathf.Lerp(-pose.HalfExtents.z * 0.7f,
                                           pose.HalfExtents.z * 0.7f,
                                           DustStormSchedule.Sample01(settings.seed, pose.CycleIndex, 31 + i * 3));
                float vertical = Mathf.Lerp(-pose.HalfExtents.y * 0.65f,
                                            pose.HalfExtents.y * 0.65f,
                                            DustStormSchedule.Sample01(settings.seed, pose.CycleIndex, 32 + i * 3));
                Vector3 position = pose.Center + pose.Direction * along + side * lateral + Vector3.up * vertical;
                float speed = settings.fragmentSpeed * Mathf.Lerp(0.8f, 1.25f,
                    DustStormSchedule.Sample01(settings.seed, pose.CycleIndex, 40 + i));
                fragment.Launch(position, pose.Direction * speed, Time.timeAsDouble, settings.fragmentLifetime);
            }
        }

        Stage1DustStormFragment GetOrCreateFragment(int index)
        {
            while (fragmentPool.Count <= index)
            {
                GameObject instance = InstantiateAsGameObject(settings.smallDebrisPrefab, transform);
                if (instance == null)
                    instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);

                instance.name = "Stage1 Dust Storm Fragment";
                instance.layer = GameLayers.Prop;
                instance.transform.localScale = Vector3.one * 0.22f;
                MeshVisual.EnsureRenderers(instance);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].enabled = false;

                var sphere = instance.GetComponent<SphereCollider>();
                if (sphere == null) sphere = instance.AddComponent<SphereCollider>();
                sphere.radius = 0.45f;

                var fragment = instance.GetComponent<Stage1DustStormFragment>();
                if (fragment == null) fragment = instance.AddComponent<Stage1DustStormFragment>();
                fragment.Configure(settings.fragmentDamage,
                                   settings.fragmentRepeatCooldown,
                                   settings.fragmentLifetime);
                fragment.ResetForPool();
                fragmentPool.Add(fragment);
            }

            var result = fragmentPool[index];
            if (result != null)
                result.Configure(settings.fragmentDamage,
                                 settings.fragmentRepeatCooldown,
                                 settings.fragmentLifetime);
            return result;
        }

        static GameObject InstantiateAsGameObject(GameObject original, Transform parent)
        {
            if (!IsAlive(original)) return null;

            // Imported model assets can be serialized as a GameObject while the
            // runtime clone returned by Unity is a different Object subtype.
            // The generic Instantiate<GameObject> overload then throws an
            // InvalidCastException. Keep the operation non-generic and normalize
            // the result before using it as a physics root.
            Object clone = null;
            try
            {
                clone = Object.Instantiate((Object)original, parent);
                var gameObject = clone as GameObject;
                if (IsAlive(gameObject)) return gameObject;

                var component = clone as Component;
                if (IsAlive(component) && IsAlive(component.gameObject)) return component.gameObject;
            }
            catch (MissingReferenceException)
            {
                // A destroyed model reference is treated like a missing debris asset;
                // the caller creates its deterministic sphere fallback instead.
            }

            if (clone != null)
            {
                if (Application.isPlaying) Object.Destroy(clone);
                else Object.DestroyImmediate(clone);
            }
            return null;
        }

        static bool IsAlive(Object target) =>
            !ReferenceEquals(target, null) && target != null;

        void ReleaseExpiredFragments(double now)
        {
            for (int i = 0; i < fragmentPool.Count; i++)
            {
                var fragment = fragmentPool[i];
                if (fragment != null && fragment.gameObject.activeSelf && fragment.IsExpired(now))
                    fragment.ResetForPool();
            }
        }

        void ReleaseAllFragments()
        {
            for (int i = 0; i < fragmentPool.Count; i++)
                if (fragmentPool[i] != null)
                    fragmentPool[i].ResetForPool();
        }

        void OnDestroy()
        {
            for (int i = fragmentPool.Count - 1; i >= 0; i--)
                if (fragmentPool[i] != null)
                    Destroy(fragmentPool[i].gameObject);
            fragmentPool.Clear();
        }
    }
}
