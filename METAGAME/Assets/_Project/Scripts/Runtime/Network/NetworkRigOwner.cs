using System.Collections.Generic;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>Presentation binding for server-controlled actors that are not NetPlayers.</summary>
    public sealed class NetworkRigOwner : MonoBehaviour
    {
        static readonly Dictionary<PlayerRig, NetworkRigOwner> Owners = new();
        public PlayerRig Rig { get; private set; }
        NetworkIdentity _identity;
        public void Bind(PlayerRig rig)
        {
            Unbind();
            _identity = GetComponent<NetworkIdentity>();
            Rig = rig;
            if (rig != null) Owners[rig] = this;
        }
        public static uint NetIdOf(PlayerRig rig)
            => rig != null && Owners.TryGetValue(rig, out var owner) && owner != null && owner._identity != null
                ? owner._identity.netId : 0;
        void Unbind()
        {
            if (!ReferenceEquals(Rig, null) && Owners.TryGetValue(Rig, out var owner) && owner == this) Owners.Remove(Rig);
            Rig = null;
        }
        void OnDestroy() => Unbind();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Owners.Clear();
    }
}
