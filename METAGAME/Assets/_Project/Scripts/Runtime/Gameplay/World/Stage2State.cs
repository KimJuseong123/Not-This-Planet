using System;
using UnityEngine;

namespace Metagame.Gameplay.Stage2
{
    [Serializable] public struct YardCommand
    {
        public YardAction Action;
        public int Target;
        public Vector3 Axes;
        public Vector3 Aim;
        public bool Slow, Primary;
        public float Turns;
    }
    [Serializable] public struct YardPartState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float A, B, C;
        public bool Open, Secured;
    }
    [Serializable] public struct YardPlayerState
    {
        public uint Id;
        public float Contamination;
        public YardTool Tool;
        public bool Spraying;
        public bool Downed;
        public Vector3 Position;
        public Vector3 SprayStart, SprayEnd;
    }
    [Serializable] public struct YardLooseToolState
    {
        public int Id;
        public Vector3 Position;
        public Quaternion Rotation;
    }
    [Serializable] public sealed class Stage2State
    {
        public double Time;
        public float PressureClock;
        public float[] PressureRelief = new float[3];
        public float Elapsed, ClearTime;
        public int Falls;
        public float Yaw, Radius = 17f, Height = 5f, LiftHeight, LiftGoal, GasClock, LadderHeight = 1f;
        public uint Pilot, LadderCarrier, Climber;
        public Vector3 LadderPosition;
        public bool LadderDeployed = true, GasEnabled, Launched;
        public int Attached = -1;
        public YardPartState[] Parts = new YardPartState[3];
        public YardPlayerState[] Players = Array.Empty<YardPlayerState>();
        public YardLooseToolState[] LooseTools = Array.Empty<YardLooseToolState>();
    }
}
