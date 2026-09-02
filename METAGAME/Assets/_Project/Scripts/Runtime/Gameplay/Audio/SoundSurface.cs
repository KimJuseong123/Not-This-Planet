using UnityEngine;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay.Audio
{
    public enum SoundSurfaceKind
    {
        Auto = 0,
        Metal = 1,
        HardNonMetal = 2,
        SoftNonMetal = 3
    }

    [DisallowMultipleComponent]
    public sealed class SoundSurface : MonoBehaviour
    {
        [SerializeField] SoundSurfaceKind kind = SoundSurfaceKind.Auto;

        public SoundSurfaceKind Kind => kind;

        public void SetKind(SoundSurfaceKind value) => kind = value;

        public static SoundSurfaceKind Resolve(Collider collider)
        {
            if (collider == null) return SoundSurfaceKind.HardNonMetal;

            var marker = collider.GetComponentInParent<SoundSurface>();
            if (marker != null && marker.kind != SoundSurfaceKind.Auto)
                return marker.kind;

            if (collider.GetComponentInParent<ToolBase>() != null
                || collider.GetComponentInParent<TransportVehicle>() != null
                || collider.GetComponentInParent<PartClamp>() != null
                || collider.GetComponentInParent<SpaceshipPart>() != null)
                return SoundSurfaceKind.Metal;

            if (collider.GetComponentInParent<PlayerRig>() != null)
                return SoundSurfaceKind.SoftNonMetal;

            if (collider.GetComponentInParent<Meteor>() != null)
                return SoundSurfaceKind.HardNonMetal;

            return SoundSurfaceKind.HardNonMetal;
        }

        public static SoundEventId ImpactEvent(SoundSurfaceKind first, SoundSurfaceKind second)
        {
            if (first == SoundSurfaceKind.Auto) first = SoundSurfaceKind.HardNonMetal;
            if (second == SoundSurfaceKind.Auto) second = SoundSurfaceKind.HardNonMetal;

            if (first > second)
            {
                var swap = first;
                first = second;
                second = swap;
            }

            if (first == SoundSurfaceKind.Metal && second == SoundSurfaceKind.Metal)
                return SoundEventId.ImpactMetalMetal;
            if (first == SoundSurfaceKind.Metal && second == SoundSurfaceKind.HardNonMetal)
                return SoundEventId.ImpactMetalHard;
            if (first == SoundSurfaceKind.Metal && second == SoundSurfaceKind.SoftNonMetal)
                return SoundEventId.ImpactMetalSoft;
            if (first == SoundSurfaceKind.HardNonMetal && second == SoundSurfaceKind.HardNonMetal)
                return SoundEventId.ImpactHardHard;
            if (first == SoundSurfaceKind.HardNonMetal && second == SoundSurfaceKind.SoftNonMetal)
                return SoundEventId.ImpactHardSoft;
            return SoundEventId.ImpactSoftSoft;
        }
    }
}
