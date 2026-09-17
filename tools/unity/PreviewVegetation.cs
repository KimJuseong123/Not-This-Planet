using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

// Offscreen-only renders in the isolated project; never manipulates a user window.
public static class PreviewVegetation
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
        Metagame.EditorTools.Stage1VegetationBuilder.Validate();
        var output = Path.GetFullPath("../../../artifacts/stage1-vegetation");
        Directory.CreateDirectory(output);
        var go = new GameObject("Temporary offscreen vegetation QA");
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 52f;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 260f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f,.12f,.18f);
        Render(camera,new Vector3(86,88,-105),new Vector3(0,0,0),output+"/overview.png");
        var root = scene.GetRootGameObjects().Single(g=>g.name==Metagame.EditorTools.Stage1VegetationBuilder.RootName);
        var plants=root.GetComponentsInChildren<Metagame.Gameplay.PlantRustle>();
        var target=plants.OrderBy(p=>(p.transform.position-new Vector3(-18,0,-22)).sqrMagnitude).First().transform.position;
        Render(camera,target+new Vector3(7,4,-9),target+Vector3.up*.5f,output+"/grove.png");
        Object.DestroyImmediate(go);
    }
    static void Render(Camera camera,Vector3 position,Vector3 target,string path)
    {
        camera.transform.position=position;
        camera.transform.LookAt(target);
        var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);
        camera.targetTexture=rt;
        camera.Render();
        var previous=RenderTexture.active;
        RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
        image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;
        camera.targetTexture=null;
        Object.DestroyImmediate(image);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
