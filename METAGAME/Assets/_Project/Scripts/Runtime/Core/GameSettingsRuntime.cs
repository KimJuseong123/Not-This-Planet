using System;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>
    /// 로컬 설정의 수명과 즉시 적용을 소유한다. 로비에서 만들어져 게임 씬까지 유지된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameSettingsRuntime : MonoBehaviour
    {
        public static GameSettingsRuntime Instance { get; private set; }

        // Unity가 도메인 리로드 없이 오브젝트를 재사용하거나 직렬화 경계를
        // 통과해도 저장소가 반드시 존재하도록 필요할 때 생성한다.
        GameSettingsStore store;
        GameSettingsData defaults;
        GameSettingsData saved;

        public GameSettingsData Current { get; private set; }
        public string FilePath { get { return Store.FilePath; } }
        public event Action<GameSettingsData> Changed;

        GameSettingsStore Store
        {
            get
            {
                if (store == null) store = new GameSettingsStore();
                return store;
            }
        }

        public static GameSettingsRuntime Ensure()
        {
            if (Instance != null) return Instance;

            var host = new GameObject("~게임 설정");
            DontDestroyOnLoad(host);
            return host.AddComponent<GameSettingsRuntime>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Load();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Load()
        {
            defaults = GameSettingsData.CreateDefaults();
            bool recovered;
            saved = Store.Load(out recovered);
            saved.Normalize();
            Current = saved.Clone();
            Apply(Current);

            if (recovered)
                Debug.LogWarning("[GameSettings] 손상된 설정 파일을 기본값으로 복구했습니다.");
        }

        /// <summary>파일에는 쓰지 않고 현재 실행 중인 값만 즉시 바꾼다.</summary>
        public void Preview(GameSettingsData candidate)
        {
            if (candidate == null) return;
            Current = candidate.Clone();
            Apply(Current);
            Changed?.Invoke(Current.Clone());
        }

        /// <summary>성공적으로 파일에 기록된 경우에만 저장값을 교체한다.</summary>
        public bool Save(GameSettingsData candidate, out string error)
        {
            if (candidate == null)
            {
                error = "저장할 설정이 없습니다.";
                return false;
            }

            var copy = candidate.Clone();
            if (!Store.Save(copy, out error)) return false;

            saved = copy.Clone();
            Current = copy;
            Apply(Current);
            Changed?.Invoke(Current.Clone());
            return true;
        }

        /// <summary>저장하지 않은 미리보기를 마지막 저장값으로 되돌린다.</summary>
        public void RestoreSaved()
        {
            if (saved == null) return;
            Current = saved.Clone();
            Apply(Current);
            Changed?.Invoke(Current.Clone());
        }

        /// <summary>프로젝트 최초 실행 상태를 미리보기로 적용한다.</summary>
        public GameSettingsData ResetDefaults()
        {
            var copy = defaults != null ? defaults.Clone() : GameSettingsData.CreateDefaults();
            Preview(copy);
            return Current.Clone();
        }

        // 이전 호출부와의 호환용 별칭이다. 새 UI/API는 ResetDefaults를 사용한다.
        public GameSettingsData ResetToDefaults() => ResetDefaults();

        void Apply(GameSettingsData settings)
        {
            if (settings == null) return;
            settings.Normalize();
            Controls.Apply(settings.controls);

            if (Application.isBatchMode) return;

            var display = settings.display;
            var mode = (FullScreenMode)display.fullscreenMode;
            if (Screen.width != display.width || Screen.height != display.height
                || Screen.fullScreenMode != mode)
            {
                Screen.SetResolution(display.width, display.height, mode);
            }

            if (display.qualityLevel >= 0 && QualitySettings.names != null
                && display.qualityLevel < QualitySettings.names.Length)
            {
                if (QualitySettings.GetQualityLevel() != display.qualityLevel)
                    QualitySettings.SetQualityLevel(display.qualityLevel, true);
            }

            QualitySettings.vSyncCount = display.vSync ? 1 : 0;
            Application.targetFrameRate = display.frameRateLimit <= 0
                ? -1 : display.frameRateLimit;

            // 마스터는 모든 로컬 오디오의 마지막 감쇠 단계다. 효과음 채널은
            // SoundRuntime이 별도로 sfxVolume을 곱한다.
            AudioListener.volume = settings.audio.masterVolume;
        }
    }
}
