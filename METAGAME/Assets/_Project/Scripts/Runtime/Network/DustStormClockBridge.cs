using System;
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
        bool matchSeedApplied;
        int appliedMatchSeed;

        public void Configure(AtmosphereDustStorm atmosphereVisual,
                               Stage1DustStormHazard atmosphereHazard)
        {
            visual = atmosphereVisual;
            hazard = atmosphereHazard;
            configured = visual != null || hazard != null;
            matchSeedApplied = false;
        }

        void Update()
        {
            if (!configured || visual == null) return;
            if (!ApplyMatchSeed()) return;
            visual.Tick(SimulationTime());
        }

        void FixedUpdate()
        {
            if (!configured || hazard == null) return;
            if (!ApplyMatchSeed()) return;
            hazard.SetPhysicsAuthority(HasPhysicsAuthority());
            hazard.FixedTick(SimulationTime());
        }

        bool ApplyMatchSeed()
        {
            if (!DustStormMatchSeed.TryGet(out var matchSeed))
                return false;

            if (matchSeedApplied && appliedMatchSeed == matchSeed)
                return true;

            visual?.SetMatchSeed(matchSeed);
            hazard?.SetMatchSeed(matchSeed);
            appliedMatchSeed = matchSeed;
            matchSeedApplied = true;
            return true;
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

    /// <summary>
    /// Holds the one dust-storm seed for the current match.
    /// The server creates it once; clients only accept the value replicated by Mirror.
    /// </summary>
    public static class DustStormMatchSeed
    {
        public static bool IsReady { get; private set; }
        public static int Value { get; private set; }

        public static void PrepareForMatch()
        {
            if (NetworkServer.active)
            {
                BeginServerMatch();
                return;
            }

            if (NetworkClient.active)
            {
                WaitForServer();
                return;
            }

            BeginLocalMatch();
        }

        public static int BeginServerMatch()
        {
            Value = CreateSeed();
            IsReady = true;
            return Value;
        }

        public static int EnsureServerMatch()
        {
            if (!IsReady)
                BeginServerMatch();

            return Value;
        }

        public static void BeginLocalMatch()
        {
            Value = CreateSeed();
            IsReady = true;
        }

        public static void WaitForServer()
        {
            Value = 0;
            IsReady = false;
        }

        public static void ApplyServerSeed(int seed)
        {
            if (seed == 0) return;

            Value = seed;
            IsReady = true;
        }

        public static bool TryGet(out int seed)
        {
            seed = Value;
            return IsReady && seed != 0;
        }

        static int CreateSeed()
        {
            int seed = unchecked(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0)
                               ^ Environment.TickCount);
            return seed == 0 ? 1 : seed;
        }
    }
}
