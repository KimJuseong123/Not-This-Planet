using Mirror;
using UnityEngine;
using Metagame.Gameplay;

namespace Metagame.Network
{
    /// <summary>
    /// Supplies one shared clock to the visual and physics halves of the Stage1 storm.
    /// Gameplay remains Mirror-free; this adapter owns the network-specific choice.
    /// </summary>
    public sealed class DustStormClockBridge : MonoBehaviour
    {
        AtmosphereDustStorm visual;
        Stage1DustStormHazard hazard;
        bool configured;

        public void Configure(AtmosphereDustStorm atmosphereVisual,
                               Stage1DustStormHazard atmosphereHazard)
        {
            visual = atmosphereVisual;
            hazard = atmosphereHazard;
            configured = visual != null || hazard != null;
        }

        void Update()
        {
            if (!configured || visual == null) return;
            visual.Tick(SimulationTime());
        }

        void FixedUpdate()
        {
            if (!configured || hazard == null) return;
            hazard.SetPhysicsAuthority(HasPhysicsAuthority());
            hazard.FixedTick(SimulationTime());
        }

        static double SimulationTime()
        {
            return NetworkClient.active || NetworkServer.active
                ? NetworkTime.time
                : Time.timeAsDouble;
        }

        static bool HasPhysicsAuthority()
        {
            return MatchRole.DedicatedServer || NetworkServer.active || !NetworkClient.active;
        }
    }
}
