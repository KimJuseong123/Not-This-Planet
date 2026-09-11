using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay.Tests
{
    public sealed class GameSettingsTests
    {
        string filePath;

        [SetUp]
        public void SetUp()
        {
            filePath = Path.Combine(Path.GetTempPath(),
                "metagame-settings-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            DeleteIfPresent(filePath);
            DeleteIfPresent(filePath + ".tmp");
        }

        [Test]
        public void DefaultsHaveExpectedGameplayValues()
        {
            var settings = GameSettingsData.CreateDefaults();

            Assert.That(settings.version, Is.EqualTo(GameSettingsData.CurrentVersion));
            Assert.That(settings.display.width, Is.GreaterThanOrEqualTo(320));
            Assert.That(settings.display.height, Is.GreaterThanOrEqualTo(240));
            Assert.That(settings.display.frameRateLimit, Is.EqualTo(120));
            Assert.That(settings.display.uiTextScale, Is.EqualTo(1f));
            Assert.That(settings.audio.masterVolume, Is.EqualTo(1f));
            Assert.That(settings.audio.sfxVolume, Is.EqualTo(1f));
            Assert.That(settings.controls.lookSensitivity, Is.EqualTo(2.4f));
            Assert.That(settings.controls.jump, Is.EqualTo(KeyCode.Space));
            Assert.That(settings.voice, Is.Not.Null);
        }

        [Test]
        public void NormalizeRestoresMissingKeysAndClampsRanges()
        {
            var settings = new GameSettingsData
            {
                display = new GameSettingsData.DisplaySettings
                {
                    width = 1,
                    height = 1,
                    frameRateLimit = 999
                },
                audio = new GameSettingsData.AudioSettings
                {
                    masterVolume = -1f,
                    sfxVolume = 2f
                },
                controls = new GameSettingsData.ControlSettings
                {
                    lookSensitivity = 99f,
                    jump = KeyCode.None
                }
            };

            settings.Normalize();

            Assert.That(settings.display.width, Is.EqualTo(320));
            Assert.That(settings.display.height, Is.EqualTo(240));
            Assert.That(settings.display.frameRateLimit, Is.EqualTo(360));
            Assert.That(settings.audio.masterVolume, Is.EqualTo(0f));
            Assert.That(settings.audio.sfxVolume, Is.EqualTo(1f));
            Assert.That(settings.controls.lookSensitivity, Is.EqualTo(5f));
            Assert.That(settings.controls.jump, Is.EqualTo(KeyCode.Space));
        }

        [Test]
        public void SaveThenLoadRoundTripsSettings()
        {
            var store = new GameSettingsStore(filePath);
            var original = GameSettingsData.CreateDefaults();
            original.display.width = 1280;
            original.display.height = 720;
            original.display.vSync = false;
            original.display.uiTextScale = 1.35f;
            original.audio.masterVolume = 0.35f;
            original.audio.sfxVolume = 0.65f;
            original.controls.lookSensitivity = 1.7f;
            original.controls.invertLookY = true;
            original.controls.jump = KeyCode.J;

            string error;
            Assert.That(store.Save(original, out error), Is.True, error);
            Assert.That(File.Exists(filePath), Is.True);

            bool recovered;
            var loaded = store.Load(out recovered);

            Assert.That(recovered, Is.False);
            Assert.That(loaded.display.width, Is.EqualTo(1280));
            Assert.That(loaded.display.height, Is.EqualTo(720));
            Assert.That(loaded.display.vSync, Is.False);
            Assert.That(loaded.display.uiTextScale, Is.EqualTo(1.35f).Within(0.0001f));
            Assert.That(loaded.audio.masterVolume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(loaded.audio.sfxVolume, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(loaded.controls.lookSensitivity, Is.EqualTo(1.7f).Within(0.0001f));
            Assert.That(loaded.controls.invertLookY, Is.True);
            Assert.That(loaded.controls.jump, Is.EqualTo(KeyCode.J));
        }

        [Test]
        public void MissingFieldsKeepProjectDefaults()
        {
            File.WriteAllText(filePath, "{\"display\":{\"width\":1280},\"audio\":{\"masterVolume\":0.25}}");
            var store = new GameSettingsStore(filePath);

            bool recovered;
            var loaded = store.Load(out recovered);

            Assert.That(recovered, Is.False);
            Assert.That(loaded.audio.masterVolume, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(loaded.audio.sfxVolume, Is.EqualTo(1f));
            Assert.That(loaded.controls.jump, Is.EqualTo(KeyCode.Space));
            Assert.That(loaded.display.frameRateLimit, Is.EqualTo(120));
            Assert.That(loaded.display.uiTextScale, Is.EqualTo(1f));
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(0.5f, 0.8f)]
        [TestCase(2f, 1.5f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        public void TextScaleNormalizesInvalidValues(float value, float expected)
        {
            var settings = new GameSettingsData();
            settings.display.uiTextScale = value;
            settings.Normalize();
            Assert.That(settings.display.uiTextScale, Is.EqualTo(expected));
        }

        [Test]
        public void TextScalePreviewCopyDoesNotChangeSavedData()
        {
            var saved = new GameSettingsData();
            saved.display.uiTextScale = 1.25f;
            var preview = saved.Clone();
            Assert.That(preview.display.uiTextScale, Is.EqualTo(1.25f));
            preview.display.uiTextScale = 1.5f;
            Assert.That(saved.display.uiTextScale, Is.EqualTo(1.25f));
        }

        [Test]
        public void CorruptJsonRecoversToDefaults()
        {
            File.WriteAllText(filePath, "{\"display\":{");
            var store = new GameSettingsStore(filePath);

            bool recovered;
            var loaded = store.Load(out recovered);

            Assert.That(recovered, Is.True);
            Assert.That(loaded.audio.masterVolume, Is.EqualTo(1f));
            Assert.That(loaded.controls.jump, Is.EqualTo(KeyCode.Space));
        }

        [Test]
        public void EmptyJsonIsAlsoRecoveredToDefaults()
        {
            File.WriteAllText(filePath, string.Empty);
            var store = new GameSettingsStore(filePath);

            bool recovered;
            var loaded = store.Load(out recovered);

            Assert.That(recovered, Is.True);
            Assert.That(loaded.display.frameRateLimit, Is.EqualTo(120));
        }

        [Test]
        public void DuplicateBindingsAreRejectedButDefaultBindingsAreValid()
        {
            var settings = GameSettingsData.CreateDefaults();
            KeyCode duplicate;

            Assert.That(Controls.HasDuplicateBindings(settings.controls, out duplicate), Is.False);
            Assert.That(duplicate, Is.EqualTo(KeyCode.None));

            settings.controls.jump = settings.controls.moveForward;
            Assert.That(Controls.HasDuplicateBindings(settings.controls, out duplicate), Is.True);
            Assert.That(duplicate, Is.EqualTo(KeyCode.W));
        }

        static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
