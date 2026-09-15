using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static partial class AstraFullArt
    {
        const string ScenePath="Assets/Scenes/Stage1_Astra_ver2.unity";
        const string Art="Assets/_Project/Art/AstraFullArt";
        const string Evidence="Validation/AstraFullArt";
        [Serializable] public class Node
        {
            public string path,name,mesh,meshPath;
            public long id;
            public Vector3 position,localPosition,scale,worldScale,euler,localEuler,meshCenter,meshSize;
            public string[] components,materials;
            public bool enabled;
        }
        [Serializable] public class Inventory { public List<Node> nodes=new(); public List<string> references=new(); }
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        public static void Inspect()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);var data=new Inventory();
            foreach(var t in All(scene))
            {
                var mf=t.GetComponent<MeshFilter>();var r=t.GetComponent<Renderer>();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t,out string guid,out long id);
                data.nodes.Add(new Node { path=PathOf(t),name=t.name,id=id,position=t.position,
                    localPosition=t.localPosition,scale=t.localScale,worldScale=t.lossyScale,euler=t.eulerAngles,localEuler=t.localEulerAngles,
                    mesh=mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.name:"",meshPath=mf!=null?AssetDatabase.GetAssetPath(mf.sharedMesh):"",
                    meshCenter=mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.bounds.center:Vector3.zero,
                    meshSize=mf!=null&&mf.sharedMesh!=null?mf.sharedMesh.bounds.size:Vector3.zero,
                    enabled=r!=null&&r.enabled,materials=r!=null?r.sharedMaterials.Select(m=>m!=null?m.name:"NULL").ToArray():Array.Empty<string>(),
                    components=t.GetComponents<Component>().Select(c=>c!=null?c.GetType().Name:"MISSING").ToArray() });
                foreach(var c in t.GetComponents<MonoBehaviour>())
                {
                    if(c==null)continue;var so=new SerializedObject(c);var p=so.GetIterator();
                    while(p.Next(true)) if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue!=null)
                    {
                        var referenced=p.objectReferenceValue;
                        var target=referenced is Component component?component.transform:referenced is GameObject go?go.transform:null;
                        if(target!=null)data.references.Add(PathOf(t)+" :: "+c.GetType().Name+"."+p.propertyPath+" => "+PathOf(target)+" ("+referenced.GetType().Name+")");
                    }
                }
            }
            Directory.CreateDirectory(Evidence);File.WriteAllText(Evidence+"/inventory.json",JsonUtility.ToJson(data,true));
            Debug.Log("ASTRA_FULL_ART_INSPECT "+data.nodes.Count);
        }
    }
}
