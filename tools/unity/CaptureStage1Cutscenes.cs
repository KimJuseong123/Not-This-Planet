// Copy into Assets/_Project/Scripts/Editor in an isolated validation project.
// Unity -projectPath <project> -executeMethod CaptureStage1Cutscenes.Run -logFile <log>
// Requires graphics (do not use -batchmode or -nographics).
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Gameplay.Hazards;
using Object = UnityEngine.Object;

public static class CaptureStage1Cutscenes
{
    const string Output = "Validation/Review587";
    static double started, nextFrame;
    static int step, frame;
    static PlayerRig recordedPlayer;

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(Stage1CutsceneFlow.StagePath);
        SessionState.SetBool("Capture587", true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod] static void Resume()
    {
        if (!SessionState.GetBool("Capture587", false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 150) throw new Exception("Capture timeout");
            var boot = Object.FindAnyObjectByType<SpaceBootstrap>();
            if (boot == null || boot.Session == null || boot.Session.Sequence == null) return;
            var state = boot.Session.Sequence;
            var flow = boot.GetComponent<Stage1CutsceneFlow>();
            bool viewClosed = Object.FindAnyObjectByType<Stage1CutsceneScene>() == null;
            if (step == 0 && state.Phase == Stage1SequencePhase.Exploration && viewClosed)
            {
                recordedPlayer = boot.Session.LocalPlayer;
                if (!flow.PlayEntry()) throw new Exception("F10 entry route rejected");
                step = 1; frame = 0; nextFrame = 0;
            }
            if ((step == 1 || step == 2) && state.IsCinematic && state.Age >= nextFrame)
            {
                Capture(boot.Session.LocalPlayer.Cam.Cam, step == 1 ? "entry" : "assault", state.Age);
                nextFrame = state.Age + 0.1;
            }
            if (step == 1 && state.Phase == Stage1SequencePhase.Exploration && viewClosed)
            {
                VerifyRestored(boot);
                if (!flow.PlayAssault()) throw new Exception("F9 assault route rejected");
                step = 2; frame = 0; nextFrame = 0;
            }
            if (step == 2 && state.Phase == Stage1SequencePhase.Combat && viewClosed)
            {
                VerifyRestored(boot);
                var spawner = Object.FindAnyObjectByType<MeteorSpawner>();
                File.AppendAllText(Output + "/runtime.txt", "PASS F10/F9 same player; camera/input restored; interval=" + spawner.CurrentInterval + "\n");
                Finish(0);
            }
        }
        catch (Exception error)
        {
            File.AppendAllText(Output + "/runtime.txt", error + "\n");
            Finish(2);
        }
    }

    static void VerifyRestored(SpaceBootstrap boot)
    {
        if (boot.Session.LocalPlayer != recordedPlayer || !boot.Session.LocalPlayer.Cam.enabled
            || boot.Session.CinematicInputBlocked || GameObject.Find("Cutscene meteor shower") != null)
            throw new Exception("Player, camera, input or temporary VFX did not restore");
    }

    static void Capture(Camera camera, string name, float age)
    {
        var target = RenderTexture.GetTemporary(854, 480, 24);
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        Texture2D texture = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture = new Texture2D(854, 480, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 854, 480), 0, 0);
            texture.Apply();
            string path = Output + "/" + name + "-" + (frame++).ToString("D4") + ".jpg";
            File.WriteAllBytes(path, texture.EncodeToJPG(85));
            File.AppendAllText(Output + "/frames.csv", name + "," + Path.GetFileName(path) + "," + age.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }
        finally
        {
            if (texture != null) Object.Destroy(texture);
            camera.targetTexture = previous;
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static void Finish(int code)
    {
        SessionState.SetBool("Capture587", false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}
