using UnityEngine;

namespace Metagame.Gameplay
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class AstraFatalTrench : MonoBehaviour
    {
        BoxCollider volume;
        void Awake() => volume = GetComponent<BoxCollider>();
        void FixedUpdate()
        {
            if (WorldEvents.RemoteDecides || volume == null) return;
            foreach (var player in PlayerRig.All)
                if (player != null && player.ServerAuthority && !player.IsDead && player.Doll != null
                    && player.gameObject.scene == gameObject.scene && ContainsFoot(player.FootPosition))
                    player.ApplyDamage(player.MaxHealth, "도랑 추락");
        }
        public bool ContainsFoot(Vector3 foot)
        {
            if (volume == null) volume = GetComponent<BoxCollider>();
            var local = transform.InverseTransformPoint(foot) - volume.center;
            var half = volume.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
