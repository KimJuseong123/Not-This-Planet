using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Audio
{
    public enum SoundEventId
    {
        HandGrabAttempt = 0,
        HandGrabSuccess = 1,
        HandRelease = 2,
        HandThrow = 3,
        ObjectLand = 4,
        ImpactMetalMetal = 5,
        ImpactMetalHard = 6,
        ImpactMetalSoft = 7,
        ImpactHardHard = 8,
        ImpactHardSoft = 9,
        ImpactSoftSoft = 10,
        MeteorImpactSmall = 11,
        MeteorImpactMedium = 12,
        ClampPullProgress = 13,
        TractorBoard = 14,
        TractorMoveLoop = 15,
        TractorTowLoop = 16,
        TractorDescendLoop = 17,
        TractorDismount = 18,
        WeldBeamAirLoop = 19,
        CutBeamAirLoop = 20,
        WeldBeamContactLoop = 21,
        WeldBeadGrowLoop = 22,
        WeldBridgeGrowLoop = 23,
        WeldConfirm = 24,
        CutBeamContactLoop = 25,
        WeldBeadRemoveLoop = 26,
        UnweldConfirm = 27,
        Footstep = 28,
        PlayerRunFootstep = 29,
        PlayerJump = 30,
        PlayerLand = 31,
        PlayerExertLoop = 32,
        PlayerStagger = 33,
        PlayerKnockback = 34,
        PlayerStun = 35,
        PlayerRecover = 36,
        ClampTensionLoop = 37,
        ClampMetalShakeLoop = 38,
        ClampEnergyUnstableLoop = 39,
        ClampDetached = 40,
        PartFloatStart = 41,
        PartPushLoop = 42,
        PartTipOver = 43,
        TractorEngineIdleLoop = 44,
        TractorEngineAccelLoop = 45,
        TractorAscendLoop = 46,
        TractorCollision = 47,
        TractorAutoLevel = 48,
        TractorGravityFieldOn = 49,
        TractorGravityFieldLoop = 50,
        TractorGravityFieldOff = 51,
        WeldInvalidContactLoop = 52,
        WeldJointCrack = 53,
        BatSwing = 54,
        BatMiss = 55,
        BatMeteorReflect = 56,
        BatPlayerHit = 57,
        MeteorWaveWarning = 58,
        MeteorFallSmallLoop = 59,
        MeteorFallMediumLoop = 60,
        MeteorPassBy = 61,
        MeteorExplosion = 62,
        MeteorGroundEmbed = 63,
        ShipBoardingDoor = 64,
        ShipLaunchCountdown = 65,
        ShipLaunchPowerUpLoop = 66,
        ShipBreak = 67,
        ShipLaunchSuccess = 68,
        EnvironmentWindLoop = 69,
        EnvironmentMeteorShowerLoop = 70,
        EnvironmentBasePowerDown = 71,
        EnvironmentDangerZoneLowLoop = 72,
        ExplorationFoundGlobal = 73,
        WeldAllCompleteGlobal = 74,
        UiPing = 75,
        UiTabletOpen = 76,
        UiTabletClose = 77,
        UiConnection = 78,
        UiReconnect = 79,
        UiVoiceTransmit = 80,
        Count = 81
    }

    public enum SoundStyle
    {
        Stylized = 0,
        Realistic = 1
    }

    public enum SoundSelection
    {
        GlobalStyle = 0,
        Stylized = 1,
        Realistic = 2,
        Custom = 3
    }

    public enum SoundRouting
    {
        Spatial3D = 0,
        Global2D = 1,
        Local2D = 2
    }

    [Serializable]
    public sealed class Stage1SoundSlot
    {
        public SoundEventId eventId;
        public SoundSelection selection = SoundSelection.GlobalStyle;
        public AudioClip stylized;
        public AudioClip realistic;
        public AudioClip custom;
        [Tooltip("본음과 함께 재생할 선택적 노이즈 레이어. 비워 두면 노이즈 없이 재생한다.")]
        public AudioClip noise;
        [Range(0f, 2f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitch = 1f;
        [Range(0f, 2f)] public float noiseVolume = 1f;
        [Range(0.1f, 3f)] public float noisePitch = 1f;
        [Min(0.1f)] public float maxDistance = 35f;
        public SoundRouting routing = SoundRouting.Spatial3D;
        public bool loop;

        public AudioClip Resolve(SoundStyle globalStyle)
        {
            switch (selection)
            {
                case SoundSelection.Stylized:
                    return stylized != null ? stylized : realistic;
                case SoundSelection.Realistic:
                    return realistic != null ? realistic : stylized;
                case SoundSelection.Custom:
                    return custom != null ? custom : ResolveGlobal(globalStyle);
                default:
                    return ResolveGlobal(globalStyle);
            }
        }

        AudioClip ResolveGlobal(SoundStyle style) =>
            style == SoundStyle.Realistic
                ? (realistic != null ? realistic : stylized)
                : (stylized != null ? stylized : realistic);
    }

    [CreateAssetMenu(menuName = "Metagame/Audio/Stage 1 Sound Bank", fileName = "Stage1SoundBank")]
    public sealed class Stage1SoundBank : ScriptableObject
    {
        [SerializeField] SoundStyle globalStyle = SoundStyle.Stylized;
        [SerializeField] List<Stage1SoundSlot> slots = new List<Stage1SoundSlot>();

        public SoundStyle GlobalStyle => globalStyle;
        public IReadOnlyList<Stage1SoundSlot> Slots => slots;

        public Stage1SoundSlot GetSlot(SoundEventId eventId)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.eventId == eventId) return slot;
            }

            return null;
        }

        public AudioClip Resolve(SoundEventId eventId, SoundStyle style, out Stage1SoundSlot slot)
        {
            slot = GetSlot(eventId);
            return slot != null ? slot.Resolve(style) : null;
        }

        public bool HasAllEventSlots()
        {
            for (var i = 0; i < (int)SoundEventId.Count; i++)
                if (GetSlot((SoundEventId)i) == null) return false;
            return true;
        }
    }
}
