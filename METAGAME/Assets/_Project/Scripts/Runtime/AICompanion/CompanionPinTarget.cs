using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.AICompanion
{
    public static class CompanionPinTarget
    {
        public static bool Eligible(PartClamp pin) => pin != null && pin.isActiveAndEnabled
            && pin.IsRequired && !pin.IsReleased && pin.Part != null;

        public static Vector3 Center(PartClamp pin)
        {
            var collider = pin.GetComponentInChildren<Collider>();
            return collider != null ? collider.bounds.center : pin.transform.position;
        }

        public static PartClamp Resolve(Transform speaker, out string reason)
        {
            reason = "주변에 뽑을 고정못이 없어요";
            var nearby = Nearby(speaker);
            if (nearby.Count == 0) return null;
            reason = "화자에게 가장 가까운 고정못: " + nearby[0].name;
            return nearby[0];
        }

        // Snapshot at command receipt: looking at a pin is not required or given priority.
        public static List<PartClamp> Nearby(Transform speaker)
        {
            var nearby = new List<PartClamp>();
            if (speaker == null) return nearby;
            foreach (var pin in PartClamp.All)
            {
                if (!Eligible(pin) || pin.gameObject.scene != speaker.gameObject.scene
                    || Vector3.Distance(speaker.position, Center(pin)) > 12) continue;
                nearby.Add(pin);
            }
            Vector3 origin = speaker.position;
            nearby.Sort((a, b) =>
            {
                int distance = (Center(a) - origin).sqrMagnitude.CompareTo((Center(b) - origin).sqrMagnitude);
                return distance != 0 ? distance : string.CompareOrdinal(a.name, b.name);
            });
            return nearby;
        }
    }
}
