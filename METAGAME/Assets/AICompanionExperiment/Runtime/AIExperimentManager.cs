using Mirror;
using UnityEngine;

namespace Metagame.AIExperiment
{
    /// <summary>Local experiment only. Does not replace the production room/authentication flow.</summary>
    public sealed class AIExperimentManager : NetworkManager
    {
        public const string EntryScene = "Assets/Scenes/AICompanionTest.unity";
        public const string RoomScene = "Assets/Scenes/AIPracticeLobby.unity";
        public override void OnServerConnect(NetworkConnectionToClient connection)
        {
            // This unauthenticated test entry is strictly same-PC, not an Internet/LAN server.
            string address = connection.address;
            if (!(connection is LocalConnectionToClient) && address != "127.0.0.1" && address != "::1" && address != "::ffff:127.0.0.1")
            { connection.Disconnect(); return; }
            base.OnServerConnect(connection);
        }
    }
}
