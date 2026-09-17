using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SurveyVegetation
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var lines = all.Where(t => t.parent == null || t.GetComponent<Collider>() != null || t.name.Contains("Tree") || t.name.Contains("tree"))
            .Select(t => $"{PathOf(t)}|position={t.position}|collider={(t.GetComponent<Collider>() ? t.GetComponent<Collider>().bounds.ToString() : "none")}|renderer={(t.GetComponent<Renderer>() ? t.GetComponent<Renderer>().bounds.ToString() : "none")}");
        File.WriteAllLines("../vegetation-survey.txt", lines);
        foreach (var folder in Directory.GetDirectories("Assets/_Project/Models/Environment/Stage1").Where(p => char.IsDigit(Path.GetFileName(p)[0])))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Directory.GetFiles(folder,"*.fbx").Single());
            var instance = Object.Instantiate(model);
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds=renderers[0].bounds;
            foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            File.AppendAllText("../vegetation-models.txt", $"{folder}|{bounds}|vertices={instance.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.vertexCount)}\n");
            Object.DestroyImmediate(instance);
        }
    }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent)+"/"+t.name;
}
