using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEngine;

namespace Metagame.AIExperiment
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
            if (speaker == null) return null;
            var player = speaker.GetComponentInParent<PlayerRig>();
            var focused = player != null ? player.CurrentInteractionTarget.Clamp : null;
            if (Eligible(focused)) return focused;
            PartClamp only = null;
            int count = 0;
            foreach (var pin in PartClamp.All)
            {
                if (!Eligible(pin) || pin.gameObject.scene != speaker.gameObject.scene
                    || Vector3.Distance(speaker.position, Center(pin)) > 12) continue;
                only = pin; count++;
            }
            if (count == 1) return only;
            if (count > 1) reason = "고정못이 여러 개예요 · 원하는 못을 가까이서 바라보고 요청해 주세요";
            return null;
        }
    }
}
