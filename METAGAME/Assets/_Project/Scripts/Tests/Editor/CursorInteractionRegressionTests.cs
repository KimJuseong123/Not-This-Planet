using System.Collections;
using Metagame.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class CursorInteractionRegressionTests
    {
        CursorInteractionTestWindow _window;

        [UnityTest]
        public IEnumerator WrappedControls_PreserveClickReleaseDisableAndClipping()
        {
            _window = ScriptableObject.CreateInstance<CursorInteractionTestWindow>();
            _window.position = new Rect(100, 100, 320, 400);
            _window.ShowUtility();
            _window.Focus();
            yield return null;
            foreach (bool wrapped in new[] { false, true })
            {
                _window.Wrapped = wrapped;
                _window.Clicks = 0;
                _window.ControlsEnabled = true;
                Send(EventType.MouseDown, new Vector2(40, 35));
                Assert.That(_window.Clicks, Is.Zero, "Buttons activate on release, not press.");
                Send(EventType.MouseUp, new Vector2(40, 35));
                Assert.That(_window.Clicks, Is.EqualTo(1), "Wrapped=" + wrapped);
                Send(EventType.MouseDown, new Vector2(40, 35));
                Send(EventType.MouseUp, new Vector2(240, 35));
                Assert.That(_window.Clicks, Is.EqualTo(1), "Release outside cancels.");
                _window.ControlsEnabled = false;
                Send(EventType.MouseDown, new Vector2(40, 35));
                Send(EventType.MouseUp, new Vector2(40, 35));
                Assert.That(_window.Clicks, Is.EqualTo(1), "Disabled controls cannot activate.");
                _window.ControlsEnabled = true;
                Send(EventType.MouseDown, new Vector2(40, 240));
                Send(EventType.MouseUp, new Vector2(40, 240));
                Assert.That(_window.Clicks, Is.EqualTo(1), "Clipped controls cannot activate.");
            }
        }

        void Send(EventType type, Vector2 point)
        {
            _window.SendEvent(new Event { type = type, button = 0, mousePosition = point });
        }

        [UnityTest]
        public IEnumerator WrappedSettingsControls_PreserveToggleSliderTextAndLayoutSelection()
        {
            _window = ScriptableObject.CreateInstance<CursorInteractionTestWindow>();
            _window.position = new Rect(100, 100, 320, 400);
            _window.ShowUtility();
            _window.Focus();
            yield return null;
            foreach (bool wrapped in new[] { false, true })
            {
                _window.Wrapped = wrapped;
                _window.Mode = 1;
                _window.Toggle = false;
                Send(EventType.MouseDown, new Vector2(40, 35));
                Send(EventType.MouseUp, new Vector2(40, 35));
                Assert.That(_window.Toggle, Is.True);
                _window.Mode = 2;
                _window.Slider = 0f;
                Send(EventType.MouseDown, new Vector2(140, 25));
                Send(EventType.MouseUp, new Vector2(140, 25));
                Assert.That(_window.Slider, Is.GreaterThan(0f));
                _window.Mode = 3;
                _window.Text = "";
                Send(EventType.MouseDown, new Vector2(40, 35));
                Send(EventType.MouseUp, new Vector2(40, 35));
                _window.SendEvent(new Event { type = EventType.KeyDown, character = 'x', keyCode = KeyCode.X });
                Assert.That(_window.Text, Is.EqualTo("x"));
                _window.Mode = 4;
                _window.Selected = 0;
                _window.Repaint();
                yield return null; // GUILayout must establish the new control geometry first.
                Send(EventType.MouseDown, new Vector2(140, 35));
                Send(EventType.MouseUp, new Vector2(140, 35));
                Assert.That(_window.Selected, Is.EqualTo(1));
                _window.Mode = 5;
                _window.Clicks = 0;
                _window.Repaint();
                yield return null;
                Send(EventType.MouseDown, new Vector2(40, 35));
                Send(EventType.MouseUp, new Vector2(40, 35));
                Assert.That(_window.Clicks, Is.EqualTo(1));
            }
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator CursorLifecycle_PreservesLockAndUsesOnlyLocalAudio()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Test camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<AudioListener>();
            yield return new EnterPlayMode();
            var host = new GameObject("Cursor regression");
            var feedback = host.AddComponent<LocalCursorFeedback>();
            bool visible = Cursor.visible;
            CursorLockMode lockState = Cursor.lockState;
            yield return null;
            Assert.That(Cursor.visible, Is.EqualTo(visible));
            Assert.That(Cursor.lockState, Is.EqualTo(lockState));
            var audio = host.GetComponent<AudioSource>();
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.spatialBlend, Is.Zero);
            Assert.That(audio.ignoreListenerPause, Is.True);
            Assert.That(host.GetComponent<Mirror.NetworkIdentity>(), Is.Null);
            feedback.enabled = false;
            yield return null;
            Assert.That(audio.isPlaying, Is.False);
            feedback.enabled = true;
            yield return null;
            Assert.That(host.GetComponents<AudioSource>().Length, Is.EqualTo(1));
            Object.Destroy(host);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_window != null) _window.Close();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }

    public sealed class CursorInteractionTestWindow : EditorWindow
    {
        public bool Wrapped, ControlsEnabled = true;
        public int Clicks, Mode, Selected;
        public bool Toggle;
        public float Slider;
        public string Text = "";

        void OnGUI()
        {
            GUI.enabled = ControlsEnabled;
            var rect = new Rect(20, 20, 160, 36);
            switch (Mode)
            {
                case 1:
                    Toggle = Wrapped ? UiInteraction.Toggle(rect, Toggle, "Toggle") : GUI.Toggle(rect, Toggle, "Toggle");
                    return;
                case 2:
                    Slider = Wrapped ? UiInteraction.HorizontalSlider(rect, Slider, 0, 1) : GUI.HorizontalSlider(rect, Slider, 0, 1);
                    return;
                case 3:
                    Text = Wrapped ? UiInteraction.TextField(rect, Text, 12) : GUI.TextField(rect, Text, 12);
                    return;
                case 4:
                    GUILayout.BeginArea(rect);
                    var choices = new[] { "One", "Two" };
                    Selected = Wrapped ? UiInteraction.SelectionGrid(Selected, choices, 2, GUI.skin.button, GUILayout.Height(36))
                        : GUILayout.SelectionGrid(Selected, choices, 2, GUI.skin.button, GUILayout.Height(36));
                    GUILayout.EndArea();
                    return;
                case 5:
                    GUILayout.BeginArea(rect);
                    if (Wrapped ? UiInteraction.LayoutButton("Click", GUI.skin.button, GUILayout.Height(36))
                        : GUILayout.Button("Click", GUI.skin.button, GUILayout.Height(36))) Clicks++;
                    GUILayout.EndArea();
                    return;
            }
            if (Wrapped ? UiInteraction.Button(rect, "Click") : GUI.Button(rect, "Click")) Clicks++;
            GUI.BeginScrollView(new Rect(20, 100, 160, 80), Vector2.zero, new Rect(0, 0, 140, 400));
            rect = new Rect(0, 130, 120, 36);
            if (Wrapped ? UiInteraction.Button(rect, "Clipped") : GUI.Button(rect, "Clipped")) Clicks++;
            GUI.EndScrollView();
            GUI.enabled = true;
        }
    }
}
