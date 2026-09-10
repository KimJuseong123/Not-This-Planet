using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    public sealed class CameraAudioListenerTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void LocalCameraProvidesEnabledAudioListener(bool firstPerson)
        {
            var target = new GameObject("Audio camera target");
            OrbitCamera camera = null;
            try
            {
                camera = OrbitCamera.Create(target.transform, Color.black, firstPerson);
                var listeners = camera.GetComponents<AudioListener>();
                Assert.That(listeners.Length, Is.EqualTo(1), "Local camera must receive BGM and SFX.");
                Assert.That(listeners[0].isActiveAndEnabled, Is.True);
                Assert.That(listeners[0].transform, Is.EqualTo(camera.Cam.transform));
            }
            finally
            {
                if (camera != null) Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
            }
        }
    }
}
