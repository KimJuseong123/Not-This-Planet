using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 플레이어가 로컬에서 사용하는 설정 데이터. Unity 오브젝트 참조를 담지 않아
    /// JSON 파일과 런타임 미리보기 양쪽에서 같은 구조를 사용한다.
    /// </summary>
    [Serializable]
    public sealed class GameSettingsData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public DisplaySettings display = new DisplaySettings();
        public AudioSettings audio = new AudioSettings();
        public ControlSettings controls = new ControlSettings();

        // 현재 소비자는 없지만 다음 음성 시스템이 같은 파일을 이어 쓸 수 있도록 보존한다.
        public VoiceSettings voice = new VoiceSettings();

        [Serializable]
        public sealed class DisplaySettings
        {
            public int width = 1600;
            public int height = 900;
            public int fullscreenMode = (int)FullScreenMode.FullScreenWindow;
            public int qualityLevel = -1;
            public bool vSync = true;
            public bool showFps;
            public int frameRateLimit = 120;
            [Range(0.8f, 1.5f)] public float uiTextScale = 1f;
        }

        [Serializable]
        public sealed class AudioSettings
        {
            [Range(0f, 1f)] public float masterVolume = 1f;
            [Range(0f, 1f)] public float sfxVolume = 1f;

            // BGM 기본 음량에 곱하는 사용자 음량. 효과음과 독립적으로 저장한다.
            [Range(0f, 1f)] public float musicVolume = 1f;
            [Range(0f, 1f)] public float voiceVolume = 1f;
        }

        [Serializable]
        public sealed class ControlSettings
        {
            [Range(0.1f, 5f)] public float lookSensitivity = 2.4f;
            public bool invertLookY;

            public KeyCode moveForward = KeyCode.W;
            public KeyCode moveBack = KeyCode.S;
            public KeyCode moveLeft = KeyCode.A;
            public KeyCode moveRight = KeyCode.D;
            public KeyCode jump = KeyCode.Space;
            public KeyCode run = KeyCode.LeftShift;
            public KeyCode armsUp = KeyCode.LeftControl;
            public KeyCode freeLook = KeyCode.LeftAlt;
            public KeyCode restart = KeyCode.R;
            public KeyCode prone = KeyCode.Z;
            public KeyCode stuckEscape = KeyCode.T;
            public KeyCode rescueTeammate = KeyCode.G;
            public KeyCode toolAction = KeyCode.Q;
            public KeyCode transportAction = KeyCode.E;
            public KeyCode tablet = KeyCode.Tab;
            public KeyCode fieldToggle = KeyCode.F;
            public KeyCode tabletPreviousPage = KeyCode.LeftArrow;
            public KeyCode tabletNextPage = KeyCode.RightArrow;
            public KeyCode freeCursor = KeyCode.Escape;
        }

        [Serializable]
        public sealed class VoiceSettings
        {
            public bool microphoneMuted;
            public bool pushToTalk;
            [Range(0f, 2f)] public float microphoneSensitivity = 1f;
            public string microphoneDevice = string.Empty;
            public List<string> mutedPlayerIds = new List<string>();
        }

        /// <summary>현재 실행 환경의 프로젝트 기본값을 만든다.</summary>
        public static GameSettingsData CreateDefaults()
        {
            var result = new GameSettingsData();

            if (Screen.width > 0) result.display.width = Screen.width;
            if (Screen.height > 0) result.display.height = Screen.height;
            result.display.fullscreenMode = (int)Screen.fullScreenMode;
            result.display.qualityLevel = QualitySettings.GetQualityLevel();
            result.display.vSync = QualitySettings.vSyncCount > 0;

            // SpaceBootstrap의 기존 기본값을 유지한다.
            result.display.frameRateLimit = 120;
            result.Normalize();
            return result;
        }

        /// <summary>파일 누락·구버전·사용자 입력값을 안전한 범위로 맞춘다.</summary>
        public void Normalize()
        {
            if (version <= 0) version = CurrentVersion;
            if (display == null) display = new DisplaySettings();
            if (audio == null) audio = new AudioSettings();
            if (controls == null) controls = new ControlSettings();
            if (voice == null) voice = new VoiceSettings();
            if (voice.mutedPlayerIds == null) voice.mutedPlayerIds = new List<string>();

            version = CurrentVersion;
            display.width = Mathf.Clamp(display.width, 320, 16384);
            display.height = Mathf.Clamp(display.height, 240, 16384);
            if (!Enum.IsDefined(typeof(FullScreenMode), display.fullscreenMode))
                display.fullscreenMode = (int)FullScreenMode.FullScreenWindow;
            if (QualitySettings.names != null && QualitySettings.names.Length > 0)
                display.qualityLevel = Mathf.Clamp(display.qualityLevel, -1,
                                                   QualitySettings.names.Length - 1);
            else
                display.qualityLevel = -1;
            display.frameRateLimit = Mathf.Clamp(display.frameRateLimit, 0, 360);
            display.uiTextScale = float.IsNaN(display.uiTextScale)
                || float.IsInfinity(display.uiTextScale) || display.uiTextScale <= 0f
                ? 1f : Mathf.Clamp(display.uiTextScale, 0.8f, 1.5f);

            audio.masterVolume = Mathf.Clamp01(audio.masterVolume);
            audio.sfxVolume = Mathf.Clamp01(audio.sfxVolume);
            audio.musicVolume = Mathf.Clamp01(audio.musicVolume);
            audio.voiceVolume = Mathf.Clamp01(audio.voiceVolume);

            controls.lookSensitivity = Mathf.Clamp(controls.lookSensitivity, 0.1f, 5f);
            controls.moveForward = NormalizeKey(controls.moveForward, KeyCode.W);
            controls.moveBack = NormalizeKey(controls.moveBack, KeyCode.S);
            controls.moveLeft = NormalizeKey(controls.moveLeft, KeyCode.A);
            controls.moveRight = NormalizeKey(controls.moveRight, KeyCode.D);
            controls.jump = NormalizeKey(controls.jump, KeyCode.Space);
            controls.run = NormalizeKey(controls.run, KeyCode.LeftShift);
            controls.armsUp = NormalizeKey(controls.armsUp, KeyCode.LeftControl);
            controls.freeLook = NormalizeKey(controls.freeLook, KeyCode.LeftAlt);
            controls.restart = NormalizeKey(controls.restart, KeyCode.R);
            controls.prone = NormalizeKey(controls.prone, KeyCode.Z);
            controls.stuckEscape = NormalizeKey(controls.stuckEscape, KeyCode.T);
            controls.rescueTeammate = NormalizeKey(controls.rescueTeammate, KeyCode.G);
            controls.toolAction = NormalizeKey(controls.toolAction, KeyCode.Q);
            controls.transportAction = NormalizeKey(controls.transportAction, KeyCode.E);
            controls.tablet = NormalizeKey(controls.tablet, KeyCode.Tab);
            controls.fieldToggle = NormalizeKey(controls.fieldToggle, KeyCode.F);
            controls.tabletPreviousPage = NormalizeKey(controls.tabletPreviousPage, KeyCode.LeftArrow);
            controls.tabletNextPage = NormalizeKey(controls.tabletNextPage, KeyCode.RightArrow);
            controls.freeCursor = NormalizeKey(controls.freeCursor, KeyCode.Escape);
            voice.microphoneSensitivity = Mathf.Clamp(voice.microphoneSensitivity, 0f, 2f);
            if (voice.microphoneDevice == null) voice.microphoneDevice = string.Empty;
        }

        static KeyCode NormalizeKey(KeyCode value, KeyCode fallback) =>
            value == KeyCode.None ? fallback : value;

        /// <summary>미리보기 편집본이 저장된 값과 같은 참조를 공유하지 않게 복사한다.</summary>
        public GameSettingsData Clone()
        {
            var result = new GameSettingsData
            {
                version = version,
                display = new DisplaySettings
                {
                    width = display != null ? display.width : 1600,
                    height = display != null ? display.height : 900,
                    fullscreenMode = display != null ? display.fullscreenMode : (int)FullScreenMode.FullScreenWindow,
                    qualityLevel = display != null ? display.qualityLevel : -1,
                    vSync = display != null && display.vSync,
                    showFps = display != null && display.showFps,
                    frameRateLimit = display != null ? display.frameRateLimit : 120,
                    uiTextScale = display != null ? display.uiTextScale : 1f
                },
                audio = new AudioSettings
                {
                    masterVolume = audio != null ? audio.masterVolume : 1f,
                    sfxVolume = audio != null ? audio.sfxVolume : 1f,
                    musicVolume = audio != null ? audio.musicVolume : 1f,
                    voiceVolume = audio != null ? audio.voiceVolume : 1f
                },
                controls = new ControlSettings
                {
                    lookSensitivity = controls != null ? controls.lookSensitivity : 2.4f,
                    invertLookY = controls != null && controls.invertLookY,
                    moveForward = controls != null ? controls.moveForward : KeyCode.W,
                    moveBack = controls != null ? controls.moveBack : KeyCode.S,
                    moveLeft = controls != null ? controls.moveLeft : KeyCode.A,
                    moveRight = controls != null ? controls.moveRight : KeyCode.D,
                    jump = controls != null ? controls.jump : KeyCode.Space,
                    run = controls != null ? controls.run : KeyCode.LeftShift,
                    armsUp = controls != null ? controls.armsUp : KeyCode.LeftControl,
                    freeLook = controls != null ? controls.freeLook : KeyCode.LeftAlt,
                    restart = controls != null ? controls.restart : KeyCode.R,
                    prone = controls != null ? controls.prone : KeyCode.Z,
                    stuckEscape = controls != null ? controls.stuckEscape : KeyCode.T,
                    rescueTeammate = controls != null ? controls.rescueTeammate : KeyCode.G,
                    toolAction = controls != null ? controls.toolAction : KeyCode.Q,
                    transportAction = controls != null ? controls.transportAction : KeyCode.E,
                    tablet = controls != null ? controls.tablet : KeyCode.Tab,
                    fieldToggle = controls != null ? controls.fieldToggle : KeyCode.F,
                    tabletPreviousPage = controls != null ? controls.tabletPreviousPage : KeyCode.LeftArrow,
                    tabletNextPage = controls != null ? controls.tabletNextPage : KeyCode.RightArrow,
                    freeCursor = controls != null ? controls.freeCursor : KeyCode.Escape
                },
                voice = new VoiceSettings
                {
                    microphoneMuted = voice != null && voice.microphoneMuted,
                    pushToTalk = voice != null && voice.pushToTalk,
                    microphoneSensitivity = voice != null ? voice.microphoneSensitivity : 1f,
                    microphoneDevice = voice != null && voice.microphoneDevice != null
                        ? voice.microphoneDevice : string.Empty,
                    mutedPlayerIds = voice != null && voice.mutedPlayerIds != null
                        ? new List<string>(voice.mutedPlayerIds) : new List<string>()
                }
            };
            result.Normalize();
            return result;
        }
    }
}
