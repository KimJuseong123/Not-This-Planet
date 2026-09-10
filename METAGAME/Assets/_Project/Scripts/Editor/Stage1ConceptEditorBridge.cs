using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    // Narrow local authoring bridge. Only explicitly named concept-map operations are allowed.
    [InitializeOnLoad]
    public static class Stage1ConceptEditorBridge
    {
        [Serializable] private class Request { public string id; public string action; }
        private static double _nextPoll;
        private static readonly string RequestPath = Path.GetFullPath("../concept-request.json");
        private static readonly string ResultPath = Path.GetFullPath("../concept-result.json");

        static Stage1ConceptEditorBridge() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(RequestPath)) return;
            Request request;
            try { request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath)); }
            catch { return; }
            File.Delete(RequestPath);
            try
            {
                if (request.action == "status") WriteStatus(request.id);
                else if (request.action == "tractor-repair")
                {
                    Stage1ConceptPlayableBuilder.RepairTractorAssembly();
                    WriteStatus(request.id);
                }
                else if (request.action == "tractor-unit-tests")
                {
                    if (!EditorApplication.ExecuteMenuItem("Tools/NOT THIS PLANET/컨셉 맵/견인 회귀 테스트"))
                        throw new InvalidOperationException("Test Runner menu unavailable.");
                }
                else if (request.action == "manual-transport-setup")
                {
                    Stage1ConceptPlayableBuilder.EnableManualTransport();
                    WriteStatus(request.id);
                }
                else if (request.action == "interaction-report") Stage1ConceptInteractionCheck.Report();
                else if (request.action == "interaction-test") Stage1ConceptInteractionCheck.Begin();
                else if (request.action == "manual-transport-test") Stage1ConceptInteractionCheck.BeginManual();
                else if (request.action == "interaction-repair")
                {
                    Stage1ConceptPlayableBuilder.RepairInteractionPlacement();
                    WriteStatus(request.id);
                }
                else if (request.action == "build-playable")
                {
                    Stage1ConceptPlayableBuilder.Build();
                    WriteStatus(request.id);
                }
                else if (request.action == "play")
                {
                    Stage1ConceptPlayableBuilder.RequireCleanEditor();
                    EditorSceneManager.OpenScene(Stage1ConceptPlayableBuilder.ScenePath);
                    EditorApplication.EnterPlaymode();
                    File.WriteAllText(ResultPath, request.id + "\nEntering Play");
                }
                else if (request.action == "stop")
                {
                    EditorApplication.ExitPlaymode();
                    File.WriteAllText(ResultPath, request.id + "\nExiting Play");
                }
                else if (request.action == "smoke")
                {
                    Stage1ConceptPlaySmoke.Begin();
                    File.WriteAllText(ResultPath, request.id + "\nSmoke running");
                }
                else throw new InvalidOperationException("Unsupported concept action: " + request.action);
            }
            catch (Exception ex) { File.WriteAllText(ResultPath, request.id + "\nERROR: " + ex); }
        }

        private static void WriteStatus(string id)
        {
            string scenes = string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
            {
                var scene = SceneManager.GetSceneAt(i);
                return scene.path + " | dirty=" + scene.isDirty + " | roots=" + string.Join(", ", scene.GetRootGameObjects().Select(g => g.name));
            }));
            File.WriteAllText(ResultPath, id + "\nplaying=" + EditorApplication.isPlaying + "\n" + scenes);
        }
    }
}
