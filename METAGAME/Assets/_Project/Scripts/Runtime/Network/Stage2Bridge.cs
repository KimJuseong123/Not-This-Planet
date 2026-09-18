using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    public struct Stage2CommandMessage : NetworkMessage { public YardCommand Command; }
    public struct Stage2SnapshotMessage : NetworkMessage { public uint Sequence; public string Json; }

    /// <summary>Authenticated connection selects the worker; clients never submit outcome state.</summary>
    public sealed class Stage2Bridge : MonoBehaviour
    {
        Stage2Yard _yard;
        bool _serverRegistered, _clientRegistered;
        readonly HashSet<uint> _known = new(), _present = new();
        readonly List<uint> _removed = new();
        float _sendAt;
        uint _sequence, _received;
        public uint LocalId => NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 1u;
        public void Configure(Stage2Yard yard)
        { _yard = yard; _yard.SetClock(() => NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.timeAsDouble); Register(); }

        void Register()
        {
            if (_yard == null) return;
            if (!NetworkServer.active) _serverRegistered = false;
            if (!NetworkClient.active) { _clientRegistered = false; _received = 0; }
            _yard.SetAuthority(!HostMigration.Pending && !HostMigration.Taking && !HostMigration.Settling
                && (NetworkServer.active || !NetworkClient.active));
            if (NetworkServer.active && !_serverRegistered)
            { NetworkServer.ReplaceHandler<Stage2CommandMessage>(ReceiveCommand); _serverRegistered = true; }
            if (NetworkClient.active && !_clientRegistered)
            { NetworkClient.ReplaceHandler<Stage2SnapshotMessage>(ReceiveSnapshot); _clientRegistered = true; }
        }

        void Update()
        {
            if (_yard == null) return;
            Register();
            if (!_yard.Authority) return;
            _present.Clear();
            if (NetworkServer.active)
            {
                foreach (var connection in NetworkServer.connections.Values)
                {
                    if (connection == null || !connection.isAuthenticated || connection.identity == null) continue;
                    var player = connection.identity.GetComponent<NetPlayer>();
                    if (player == null || player.VisualRig == null) continue;
                    uint id = connection.identity.netId;
                    _present.Add(id); _known.Add(id); _yard.Register(id, player.VisualRig);
                }
            }
            else if (GameSession.Instance != null && GameSession.Instance.LocalPlayer != null)
            { _present.Add(1); _known.Add(1); _yard.Register(1, GameSession.Instance.LocalPlayer); }
            _removed.Clear();
            foreach (uint id in _known) if (!_present.Contains(id)) _removed.Add(id);
            foreach (uint id in _removed) { _yard.Remove(id); _known.Remove(id); }
            if (Time.unscaledTime < _sendAt) return;
            _sendAt = Time.unscaledTime + .05f;
            var state = _yard.Capture();
            state.Time = NetworkServer.active ? NetworkTime.time : Time.timeAsDouble;
            if (NetworkServer.active)
                NetworkServer.SendToAll(new Stage2SnapshotMessage { Sequence = ++_sequence, Json = JsonUtility.ToJson(state) }, Channels.Unreliable, sendToReadyOnly: true);
        }

        public void Send(YardCommand command)
        {
            if (_yard == null) return;
            if (NetworkClient.isConnected && NetworkClient.ready)
                NetworkClient.Send(new Stage2CommandMessage { Command = command });
            else if (!NetworkClient.active && !NetworkServer.active) _yard.Submit(1, command);
        }

        void ReceiveCommand(NetworkConnectionToClient connection, Stage2CommandMessage message)
        {
            if (_yard == null || connection == null || !connection.isAuthenticated || !connection.isReady || connection.identity == null) return;
            _yard.Submit(connection.identity.netId, message.Command);
        }

        void ReceiveSnapshot(Stage2SnapshotMessage message)
        {
            if (_yard == null || NetworkServer.active || message.Sequence <= _received || string.IsNullOrEmpty(message.Json) || message.Json.Length > 24000) return;
            _received = message.Sequence;
            var state = JsonUtility.FromJson<Stage2State>(message.Json);
            _yard.ApplyReplica(state);
            if (state != null && GameSession.Instance != null)
                GameSession.Instance.ApplyHostProgress(0,state.Launched,state.ClearTime,state.Elapsed,state.Falls);
        }

        void OnDestroy()
        {
            if (_serverRegistered) NetworkServer.UnregisterHandler<Stage2CommandMessage>();
            if (_clientRegistered) NetworkClient.UnregisterHandler<Stage2SnapshotMessage>();
        }
    }
}
