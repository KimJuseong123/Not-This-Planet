import os
import re
import uuid


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ASSET_ROOT = os.path.join(ROOT, "METAGAME", "Assets", "_Project")
AUDIO_ROOT = os.path.join(ASSET_ROOT, "Audio")
WALK_AUDIO = os.path.join(AUDIO_ROOT, "SFX_Rabbit_Walk_Space_Light.wav.meta")
RESOURCE_ROOT = os.path.join(ASSET_ROOT, "Resources", "Audio")
SCRIPT_ROOT = os.path.join(ASSET_ROOT, "Scripts", "Runtime", "Gameplay", "Audio")
EDITOR_SCRIPT_ROOT = os.path.join(ASSET_ROOT, "Scripts", "Editor")
MANAGER_SCRIPT = os.path.join(SCRIPT_ROOT, "AudioManager.cs.meta")
NETWORK_SCRIPT_ROOT = os.path.join(ASSET_ROOT, "Scripts", "Runtime", "Network")
TEST_SCRIPT_ROOT = os.path.join(ASSET_ROOT, "Scripts", "Tests", "Editor")

EVENTS = [
    ("HandGrabAttempt", "SFX_Hand_Grab_Attempt", 0.55, 12.0, False),
    ("HandGrabSuccess", "SFX_Hand_Grab_Success", 0.68, 14.0, False),
    ("HandRelease", "SFX_Hand_Release", 0.52, 12.0, False),
    ("HandThrow", "SFX_Hand_Throw", 0.76, 18.0, False),
    ("ObjectLand", "SFX_Object_Land", 0.72, 35.0, False),
    ("ImpactMetalMetal", "SFX_Impact_Metal_Metal", 0.70, 35.0, False),
    ("ImpactMetalHard", "SFX_Impact_Metal_Hard", 0.70, 35.0, False),
    ("ImpactMetalSoft", "SFX_Impact_Metal_Soft", 0.64, 30.0, False),
    ("ImpactHardHard", "SFX_Impact_Hard_Hard", 0.68, 35.0, False),
    ("ImpactHardSoft", "SFX_Impact_Hard_Soft", 0.62, 30.0, False),
    ("ImpactSoftSoft", "SFX_Impact_Soft_Soft", 0.56, 26.0, False),
    ("MeteorImpactSmall", "SFX_Meteor_Impact_Small", 0.78, 45.0, False),
    ("MeteorImpactMedium", "SFX_Meteor_Impact_Medium", 0.88, 50.0, False),
    ("ClampPullProgress", "SFX_Clamp_Pull_Progress", 0.54, 25.0, False),
    ("TractorBoard", "SFX_Tractor_Board", 0.66, 30.0, False),
    ("TractorMoveLoop", "SFX_Tractor_Move_Loop", 0.43, 40.0, True),
    ("TractorTowLoop", "SFX_Tractor_Tow_Loop", 0.50, 45.0, True),
    ("TractorDescendLoop", "SFX_Tractor_Descend_Loop", 0.47, 40.0, True),
    ("TractorDismount", "SFX_Tractor_Dismount", 0.65, 30.0, False),
    ("WeldBeamAirLoop", "SFX_Weld_Beam_Air_Loop", 0.34, 22.0, True),
    ("CutBeamAirLoop", "SFX_Cut_Beam_Air_Loop", 0.38, 24.0, True),
    ("WeldBeamContactLoop", "SFX_Weld_Beam_Contact_Loop", 0.44, 25.0, True),
    ("WeldBeadGrowLoop", "SFX_Weld_Bead_Grow_Loop", 0.48, 25.0, True),
    ("WeldBridgeGrowLoop", "SFX_Weld_Bridge_Grow_Loop", 0.54, 28.0, True),
    ("WeldConfirm", "SFX_Weld_Confirm", 0.72, 28.0, False),
    ("CutBeamContactLoop", "SFX_Cut_Beam_Contact_Loop", 0.46, 25.0, True),
    ("WeldBeadRemoveLoop", "SFX_Weld_Bead_Remove_Loop", 0.43, 25.0, True),
    ("UnweldConfirm", "SFX_Unweld_Confirm", 0.70, 28.0, False),
    ("Footstep", None, 0.72, 24.0, False),
]

# 실제 제작 세트는 귀여운 게임풍 Stylized 한 종류만 유지한다.
# Stage1SoundBank의 realistic 필드는 기존 직렬화 호환을 위해 같은 Stylized 클립을 가리킨다.
# 25.9의 필수 사운드 목록 중 아직 원본 WAV가 없는 슬롯이다.
# routing: 0 = 위치 기반 3D, 1 = 모든 플레이어가 같은 크기로 듣는 전역 2D,
# 2 = 해당 클라이언트의 UI·로컬 신호용 2D.
FUTURE_EVENTS = [
    ("PlayerRunFootstep", 0.78, 28.0, False, 0),
    ("PlayerJump", 0.52, 16.0, False, 0),
    ("PlayerLand", 0.70, 22.0, False, 0),
    ("PlayerExertLoop", 0.44, 14.0, True, 0),
    ("PlayerStagger", 0.58, 16.0, False, 0),
    ("PlayerKnockback", 0.68, 24.0, False, 0),
    ("PlayerStun", 0.76, 28.0, False, 0),
    ("PlayerRecover", 0.62, 20.0, False, 0),
    ("ClampTensionLoop", 0.42, 20.0, True, 0),
    ("ClampMetalShakeLoop", 0.46, 22.0, True, 0),
    ("ClampEnergyUnstableLoop", 0.38, 24.0, True, 0),
    ("ClampDetached", 0.68, 28.0, False, 0),
    ("PartFloatStart", 0.58, 28.0, False, 0),
    ("PartPushLoop", 0.48, 24.0, True, 0),
    ("PartTipOver", 0.72, 32.0, False, 0),
    ("TractorEngineIdleLoop", 0.34, 42.0, True, 0),
    ("TractorEngineAccelLoop", 0.52, 46.0, True, 0),
    ("TractorAscendLoop", 0.48, 42.0, True, 0),
    ("TractorCollision", 0.78, 42.0, False, 0),
    ("TractorAutoLevel", 0.42, 36.0, False, 0),
    ("TractorGravityFieldOn", 0.50, 34.0, False, 0),
    ("TractorGravityFieldLoop", 0.30, 30.0, True, 0),
    ("TractorGravityFieldOff", 0.44, 34.0, False, 0),
    ("WeldInvalidContactLoop", 0.24, 18.0, True, 0),
    ("WeldJointCrack", 0.62, 28.0, False, 0),
    ("BatSwing", 0.54, 16.0, False, 0),
    ("BatMiss", 0.26, 12.0, False, 0),
    ("BatMeteorReflect", 0.72, 34.0, False, 0),
    ("BatPlayerHit", 0.60, 22.0, False, 0),
    ("MeteorWaveWarning", 0.62, 55.0, False, 0),
    ("MeteorFallSmallLoop", 0.34, 44.0, True, 0),
    ("MeteorFallMediumLoop", 0.46, 52.0, True, 0),
    ("MeteorPassBy", 0.58, 48.0, False, 0),
    ("MeteorExplosion", 0.72, 48.0, False, 0),
    ("MeteorGroundEmbed", 0.56, 42.0, False, 0),
    ("ShipBoardingDoor", 0.58, 32.0, False, 0),
    ("ShipLaunchCountdown", 0.64, 80.0, False, 0),
    ("ShipLaunchPowerUpLoop", 0.52, 80.0, True, 0),
    ("ShipBreak", 0.86, 70.0, False, 0),
    ("ShipLaunchSuccess", 0.86, 100.0, False, 0),
    ("EnvironmentWindLoop", 0.24, 35.0, True, 0),
    ("EnvironmentMeteorShowerLoop", 0.20, 60.0, True, 0),
    ("EnvironmentBasePowerDown", 0.42, 45.0, False, 0),
    ("EnvironmentDangerZoneLowLoop", 0.28, 32.0, True, 0),
    ("ExplorationFoundGlobal", 0.72, 0.1, False, 1),
    ("WeldAllCompleteGlobal", 0.78, 0.1, False, 1),
    ("UiPing", 0.52, 0.1, False, 2),
    ("UiTabletOpen", 0.44, 0.1, False, 2),
    ("UiTabletClose", 0.40, 0.1, False, 2),
    ("UiConnection", 0.42, 0.1, False, 2),
    ("UiReconnect", 0.50, 0.1, False, 2),
    ("UiVoiceTransmit", 0.28, 0.1, False, 2),
]

ALL_EVENTS = [
    (event, filename, volume, max_distance, loop, 0)
    for event, filename, volume, max_distance, loop in EVENTS
] + [
    (event, None, volume, max_distance, loop, routing)
    for event, volume, max_distance, loop, routing in FUTURE_EVENTS
]


def read_guid(path):
    with open(path, encoding="utf-8") as handle:
        match = re.search(r"^guid:\s*([0-9a-f]+)\s*$", handle.read(), re.MULTILINE)
    if not match:
        raise RuntimeError("missing guid: " + path)
    return match.group(1)


def ref(guid):
    return "{fileID: 8300000, guid: %s, type: 3}" % guid


def zero_ref():
    return "{fileID: 0}"


def create_bank():
    script_guid = read_guid(os.path.join(SCRIPT_ROOT, "Stage1SoundBank.cs.meta"))
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &11400000",
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % script_guid,
        "  m_Name: Stage1SoundBank",
        "  m_EditorClassIdentifier:",
        "  globalStyle: 0",
        "  slots:",
    ]

    for index, (event, filename, volume, max_distance, loop, routing) in enumerate(ALL_EVENTS):
        if event == "Footstep":
            stylized = realistic = read_guid(WALK_AUDIO)
        elif filename is None:
            stylized = realistic = None
        else:
            stylized = read_guid(os.path.join(AUDIO_ROOT, "SFX", "Stylized", filename + ".wav.meta"))
            realistic = stylized
        lines.extend([
            "  - eventId: %d" % index,
            "    selection: 0",
            "    stylized: %s" % (ref(stylized) if stylized else zero_ref()),
            "    realistic: %s" % (ref(realistic) if realistic else zero_ref()),
            "    custom: %s" % zero_ref(),
            "    noise: %s" % zero_ref(),
            "    volume: %s" % volume,
            "    pitch: 1",
            "    noiseVolume: 1",
            "    noisePitch: 1",
            "    maxDistance: %s" % max_distance,
            "    routing: %d" % routing,
            "    loop: %d" % (1 if loop else 0),
        ])

    os.makedirs(RESOURCE_ROOT, exist_ok=True)
    with open(os.path.join(RESOURCE_ROOT, "Stage1SoundBank.asset"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")


def create_manager():
    manager_guid = read_guid(MANAGER_SCRIPT)
    bank_guid = read_guid(os.path.join(RESOURCE_ROOT, "Stage1SoundBank.asset.meta"))
    lines = [
        "%YAML 1.1",
        "%TAG !u! tag:unity3d.com,2011:",
        "--- !u!114 &11400000",
        "MonoBehaviour:",
        "  m_ObjectHideFlags: 0",
        "  m_CorrespondingSourceObject: {fileID: 0}",
        "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}",
        "  m_GameObject: {fileID: 0}",
        "  m_Enabled: 1",
        "  m_EditorHideFlags: 0",
        "  m_Script: {fileID: 11500000, guid: %s, type: 3}" % manager_guid,
        "  m_Name: Stage1AudioManager",
        "  m_EditorClassIdentifier:",
        "  soundBank: {fileID: 11400000, guid: %s, type: 2}" % bank_guid,
        "  globalStyle: 0",
        "  masterVolume: 1",
        "  muted: 0",
        "  outputMixerGroup: {fileID: 0}",
        "  spatialMinDistance: 1.2",
        "  defaultFadeInSeconds: 0.005",
        "  defaultFadeOutSeconds: 0.04",
        "  noiseMasterVolume: 1",
        "  noiseMasterPitch: 1",
        "  eventOverrides:",
    ]

    for index, (_event, _filename, _volume, _max_distance, _loop, _routing) in enumerate(ALL_EVENTS):
        lines.extend([
            "  - eventId: %d" % index,
            "    enabled: 1",
            "    clipOverride: %s" % zero_ref(),
            "    noiseClipOverride: %s" % zero_ref(),
            "    volumeScale: 1",
            "    pitchScale: 1",
            "    maxDistanceScale: 1",
            "    fadeInScale: 1",
            "    fadeOutScale: 1",
            "    noiseVolumeScale: 1",
            "    noisePitchScale: 1",
            "    loopPolicy: 0",
            "    concurrencyPolicy: 0",
            "    maxInstances: 8",
            "    minRetriggerInterval: 0",
        ])

    with open(os.path.join(RESOURCE_ROOT, "Stage1AudioManager.asset"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")


def create_meta(path, kind):
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    if kind == "folder":
        content = "fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\ntimeCreated: 0\nlicenseType: Free\nDefaultImporter:\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n" % uuid.uuid4().hex
    elif kind == "asset":
        content = "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n" % uuid.uuid4().hex
    else:
        content = "fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n" % uuid.uuid4().hex
    with open(meta, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def main():
    for filename in os.listdir(SCRIPT_ROOT):
        if filename.endswith(".cs"):
            create_meta(os.path.join(SCRIPT_ROOT, filename), "script")
    create_meta(os.path.join(EDITOR_SCRIPT_ROOT, "AudioManagerEditor.cs"), "script")
    create_meta(SCRIPT_ROOT, "folder")
    for filename in ("NetworkSoundBridge.cs", "NetworkSoundMessage.cs"):
        create_meta(os.path.join(NETWORK_SCRIPT_ROOT, filename), "script")
    create_meta(os.path.join(TEST_SCRIPT_ROOT, "Stage1SoundTests.cs"), "script")
    create_meta(os.path.join(AUDIO_ROOT, "SFX"), "folder")
    create_meta(os.path.join(AUDIO_ROOT, "SFX", "Stylized"), "folder")
    create_meta(RESOURCE_ROOT, "folder")
    create_bank()
    create_meta(os.path.join(RESOURCE_ROOT, "Stage1SoundBank.asset"), "asset")
    create_manager()
    create_meta(os.path.join(RESOURCE_ROOT, "Stage1AudioManager.asset"), "asset")
    print("soundbank=created slots=%d manager=created overrides=%d" %
          (len(ALL_EVENTS), len(ALL_EVENTS)))


if __name__ == "__main__":
    main()
