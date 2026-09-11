using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Metagame.Core
{
    /// <summary>개인 기기의 설정 JSON 파일만 담당한다.</summary>
    public sealed class GameSettingsStore
    {
        public const string FileName = "metagame-settings.json";

        readonly string filePath;

        public string FilePath { get { return filePath; } }

        public GameSettingsStore()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        // EditMode 테스트와 손상 파일 복구 검증을 위해 경로를 주입할 수 있다.
        public GameSettingsStore(string path)
        {
            filePath = path;
        }

        public GameSettingsData Load(out bool recovered)
        {
            recovered = false;
            var defaults = GameSettingsData.CreateDefaults();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return defaults;

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    throw new FormatException("설정 파일이 비어 있습니다.");

                // 기본 인스턴스에 덮어써서 구버전/부분 파일의 누락 필드를 보완한다.
                JsonUtility.FromJsonOverwrite(json, defaults);
                defaults.Normalize();
                return defaults;
            }
            catch (Exception exception)
            {
                recovered = true;
                Debug.LogWarning("[GameSettings] 설정 파일을 읽지 못해 기본값을 사용합니다: "
                                 + filePath + " · " + exception.Message);
                return GameSettingsData.CreateDefaults();
            }
        }

        public bool Save(GameSettingsData source, out string error)
        {
            error = string.Empty;
            if (source == null)
            {
                error = "저장할 설정이 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "설정 파일 경로를 만들 수 없습니다.";
                return false;
            }

            string temporaryPath = filePath + ".tmp";
            try
            {
                var copy = source.Clone();
                copy.Normalize();

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(temporaryPath, JsonUtility.ToJson(copy, true),
                                  new UTF8Encoding(false));

                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Replace(temporaryPath, filePath, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, filePath, true);
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, filePath, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "설정을 저장하지 못했습니다.";
                Debug.LogWarning("[GameSettings] 설정 파일 저장 실패: " + filePath + " · "
                                 + exception.Message);
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning("[GameSettings] 임시 파일 정리 실패: "
                                     + cleanupException.Message);
                }

                return false;
            }
        }
    }
}
