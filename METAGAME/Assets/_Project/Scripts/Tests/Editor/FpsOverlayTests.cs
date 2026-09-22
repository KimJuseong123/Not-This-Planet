using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Metagame.Core;
using Metagame.UI;
using NUnit.Framework;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class FpsOverlayTests
    {
        [UnityTest]
        public IEnumerator PreviewCancelSaveResetAndOwnerLifetime()
        {
            yield return new EnterPlayMode();
            string path = Path.Combine(Path.GetTempPath(), "fps-settings-" + Guid.NewGuid() + ".json");
            var owner = new GameObject("FPS settings test");
            owner.SetActive(false);
            var runtime = owner.AddComponent<GameSettingsRuntime>();
            typeof(GameSettingsRuntime).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runtime, new GameSettingsStore(path));
            owner.SetActive(true);
            var overlay = owner.AddComponent<FpsOverlay>();
            try
            {
                Assert.That(overlay.enabled, Is.False);
                var candidate = runtime.Current.Clone();
                int limit = candidate.display.frameRateLimit;
                bool vSync = candidate.display.vSync;
                candidate.display.showFps = true;
                runtime.Preview(candidate);
                Assert.That(overlay.enabled, Is.True);
                Assert.That(File.Exists(path), Is.False, "Preview must not write a settings file.");
                runtime.RestoreSaved();
                Assert.That(overlay.enabled, Is.False);

                Assert.That(runtime.Save(candidate, out string error), Is.True, error);
                Assert.That(overlay.enabled, Is.True);
                runtime.ResetDefaults();
                Assert.That(overlay.enabled, Is.False);
                runtime.RestoreSaved();
                Assert.That(overlay.enabled, Is.True);
                Assert.That(runtime.Current.display.frameRateLimit, Is.EqualTo(limit));
                Assert.That(runtime.Current.display.vSync, Is.EqualTo(vSync));

                // Disabled components must still receive the next settings change.
                candidate.display.showFps = false;
                runtime.Preview(candidate);
                candidate.display.showFps = true;
                runtime.Preview(candidate);
                Assert.That(overlay.enabled, Is.True);

                float previousTimeScale = Time.timeScale;
                try
                {
                    Time.timeScale = 0f;
                    yield return new WaitForSecondsRealtime(0.65f);
                    var counter = (FpsCounter)typeof(FpsOverlay)
                        .GetField("_counter", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(overlay);
                    Assert.That(counter.HasSample, Is.True, "FPS must update even when game time is paused.");
                    candidate.display.showFps = false;
                    runtime.Preview(candidate);
                    Assert.That(counter.HasSample, Is.False, "Hiding must discard the previous sample.");
                }
                finally
                {
                    Time.timeScale = previousTimeScale;
                }

                Object.Destroy(overlay);
                yield return null;
                runtime.RestoreSaved(); // No dangling callback into the destroyed overlay.
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(owner);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
            yield return null;
            yield return new ExitPlayMode();
        }
    }
}
