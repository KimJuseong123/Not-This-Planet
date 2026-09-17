using UnityEngine;

namespace Metagame.Gameplay
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Stage1AssaultTrigger : MonoBehaviour
    {
        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        // Stay도 확인한다. 입장 연출 중 들어와 있던 플레이어도 조작 시작 뒤 발동할 수 있다.
        void OnTriggerStay(Collider other)
        {
            var sequence = GameSession.Instance != null ? GameSession.Instance.Sequence : null;
            if (sequence == null || !sequence.Authoritative || sequence.BlocksInput || sequence.AssaultEnabled) return;
            var rig = other.GetComponentInParent<PlayerRig>();
            if (rig != null && !rig.Downed) sequence.TryBeginAssault();
        }
    }
}
