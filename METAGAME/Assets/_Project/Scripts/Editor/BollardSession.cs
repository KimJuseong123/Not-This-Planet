using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    [InitializeOnLoad]
    public static class BollardSession
    {
        static readonly string Dir = Path.GetFullPath("../tmp/bollard-integration");
        static BollardSession() { EditorApplication.update += Tick; }
        static double _next;
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string request = Dir + "/request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "inspect") Inspect();
                else if (command == "prepare") Stage1Bollards.Prepare();
                else if (command == "apply") Stage1Bollards.Apply();
                else if (command == "validate") Stage1Bollards.Validate();
                else if (command == "capture") Stage1Bollards.Capture();
                else if (command == "supports") Stage1Bollards.AlignSupports();
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "tests") AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Metagame.Gameplay.Tests.BollardTestRun")).Single(t=>t!=null).GetMethod("Run").Invoke(null,null);
                else throw new InvalidOperationException("Unknown request: " + command);
                File.WriteAllText(Dir + "/done.txt", command + " OK");
            }
            catch (Exception ex) { File.WriteAllText(Dir + "/done.txt", ex.ToString()); }
        }
        static void Inspect()
        {
            var sb = new StringBuilder();
            sb.AppendLine("project=" + Application.dataPath + " playing=" + EditorApplication.isPlaying);
            for (int i=0; i<SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                sb.AppendLine("scene=" + scene.path + " dirty=" + scene.isDirty);
                foreach (var t in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name.StartsWith("Lamp")))
                {
                    var r=t.GetComponent<Renderer>(); var c=t.GetComponent<Collider>();
                    sb.AppendLine("LAMP " + GlobalObjectId.GetGlobalObjectIdSlow(t) + " name="+t.name+" parent="+t.parent.name+" pos="+t.position.ToString("F5")+" scale="+t.lossyScale.ToString("F5")+" active="+t.gameObject.activeInHierarchy+" material="+(r?AssetDatabase.GetAssetPath(r.sharedMaterial):"")+" bounds="+(r?r.bounds.ToString("F5"):"")+" collider="+(c?c.GetType().Name+"/"+c.enabled:"") );
                }
            }
            string f=Directory.GetFiles("Assets/_Project/Models/Bollard", "*.fbx", SearchOption.AllDirectories).Single().Replace('\\','/');
            var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f));
            try
            {
                foreach(var mf in model.GetComponentsInChildren<MeshFilter>())
                    sb.AppendLine("MODEL name="+mf.name+" mesh="+mf.sharedMesh.name+" bounds="+mf.sharedMesh.bounds.ToString("F5")+" world="+mf.GetComponent<Renderer>().bounds.ToString("F5")+" rotation="+mf.transform.eulerAngles+" vertices="+mf.sharedMesh.vertexCount+" submeshes="+mf.sharedMesh.subMeshCount);
            }
            finally {UnityEngine.Object.DestroyImmediate(model);}
            File.WriteAllText(Dir+"/inspection.txt",sb.ToString());
        }
    }
}
