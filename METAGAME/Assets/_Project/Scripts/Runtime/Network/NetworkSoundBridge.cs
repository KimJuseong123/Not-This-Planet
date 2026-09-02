using UnityEngine;
using Mirror;
using Metagame.Gameplay.Audio;

namespace Metagame.Network
{
    /// <summary>
    /// 권한 있는 호스트의 공간 이벤트를 위치·강도·순번만으로 중계한다.
    /// 로컬 호스트 연결은 건너뛰어 같은 이벤트가 두 번 들리지 않게 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSoundBridge : MonoBehaviour
    {
        SoundRuntime runtime;
        bool clientHandlerRegistered;
        uint nextSequence;
        uint lastReceivedSequence;
        bool receivedAny;

        public void Configure(SoundRuntime source)
        {
            if (runtime != null) runtime.WorldSoundRequested -= OnWorldSoundRequested;
            runtime = source;
            if (isActiveAndEnabled && runtime != null)
                runtime.WorldSoundRequested += OnWorldSoundRequested;
        }

        void OnEnable()
        {
            if (runtime != null) runtime.WorldSoundRequested += OnWorldSoundRequested;
            RegisterClientHandlerIfReady();
        }

        void OnDisable()
        {
            if (runtime != null) runtime.WorldSoundRequested -= OnWorldSoundRequested;
            if (clientHandlerRegistered)
            {
                NetworkClient.UnregisterHandler<NetworkSoundMessage>();
                clientHandlerRegistered = false;
            }
        }

        void Update()
        {
            if (runtime != null)
            {
                runtime.SetWorldAuthority(!NetworkClient.active || NetworkServer.active);
                runtime.SetNetworked(NetworkClient.active || NetworkServer.active);
            }

            if (!NetworkClient.active)
            {
                clientHandlerRegistered = false;
                receivedAny = false;
                lastReceivedSequence = 0u;
                return;
            }

            RegisterClientHandlerIfReady();
        }

        void RegisterClientHandlerIfReady()
        {
            if (clientHandlerRegistered || !NetworkClient.active) return;
            NetworkClient.RegisterHandler<NetworkSoundMessage>(OnNetworkSoundMessage);
            clientHandlerRegistered = true;
        }

        void OnWorldSoundRequested(SoundEventId eventId, Vector3 position, float intensity)
        {
            if (!NetworkServer.active) return;

            var message = new NetworkSoundMessage
            {
                Sequence = ++nextSequence,
                EventId = (int)eventId,
                Position = position,
                Intensity = Mathf.Clamp01(intensity)
            };

            foreach (var pair in NetworkServer.connections)
            {
                var connection = pair.Value;
                if (connection == null || connection == NetworkServer.localConnection || !connection.isReady)
                    continue;
                connection.Send(message);
            }
        }

        void OnNetworkSoundMessage(NetworkSoundMessage message)
        {
            if (runtime == null || message.EventId < 0 || message.EventId >= (int)SoundEventId.Count)
                return;
            if (!AcceptSequence(message.Sequence)) return;

            runtime.ReceiveNetworkWorldEvent((SoundEventId)message.EventId,
                message.Position, message.Intensity);
        }

        bool AcceptSequence(uint sequence)
        {
            if (receivedAny && !IsNewer(sequence, lastReceivedSequence)) return false;
            receivedAny = true;
            lastReceivedSequence = sequence;
            return true;
        }

        static bool IsNewer(uint candidate, uint previous) =>
            candidate != previous && (candidate - previous) < 0x80000000u;
    }
}
