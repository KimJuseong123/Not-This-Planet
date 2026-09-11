using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>월드 공간 효과음은 클립이 아니라 논리 이벤트만 전송한다.</summary>
    public struct NetworkSoundMessage : NetworkMessage
    {
        public uint Sequence;
        public int EventId;
        public Vector3 Position;
        public float Intensity;
    }
}
