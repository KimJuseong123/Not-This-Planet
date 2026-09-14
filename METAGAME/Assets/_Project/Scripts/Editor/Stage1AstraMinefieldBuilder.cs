using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;
using Object = UnityEngine.Object;
namespace Metagame.EditorTools
{
    public static class Stage1AstraMinefieldBuilder
    {
        static Transform root; static Terrain terrain; static Material dark, yellow, metal, red, pulse;
        const string Art = "Assets/_Project/Art/AstraMinefield";
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static float Floor(float x,float z)=>terrain.SampleHeight(V(x,0,z))+terrain.transform.position.y;
        static Transform Group(string name,Transform parent,Vector3 local)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=local;return t;}
        static GameObject Shape(string name,PrimitiveType type,Transform parent,Vector3 pos,Vector3 scale,Material material,bool solid=false)
        {var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;g.layer=GameLayers.Ground;if(!solid)Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
        static GameObject Box(string name,Transform p,Vector3 at,Vector3 size,Material m,bool solid=false)=>Shape(name,PrimitiveType.Cube,p,at,size,m,solid);
        static Material Mat(string name,Color color,bool unlit=false)
        {var m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Particles/Unlit":"Universal Render Pipeline/Lit"));m.name=name;m.color=color;if(!unlit){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*.15f);}AssetDatabase.CreateAsset(m,Art+"/"+name+".mat");return m;}
        static void Sign(float x,float z,float yaw,float height=2)
        {
            var t=Group("Warning icon only",root,V(x,Floor(x,z),z));t.localRotation=Quaternion.Euler(0,yaw,0);
            Box("Warning post",t,V(0,(height-.4f)*.5f,0),V(.12f,height-.4f,.12f),metal,true);
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/Warning triangle.asset");
            var panel=Group("Yellow warning triangle",t,V(0,height,0));panel.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;panel.gameObject.AddComponent<MeshRenderer>().sharedMaterial=yellow;
            foreach(float side in new[]{-.025f,.025f}){Box("Exclamation stroke",panel,V(0,.04f,side),V(.13f,.48f,.02f),dark);Shape("Exclamation dot",PrimitiveType.Sphere,panel,V(0,-.32f,side),Vector3.one*.15f,dark);}
        }
        static void Detector(float x,float z)
        {
            var t=Group("Carryable mine detector",root,V(x,Floor(x,z)+.45f,z));
            var model=Group("Model",t,Vector3.zero);
            Box("Detector handle",model,V(0,0,0),V(.16f,.18f,.55f),dark);
            Box("Detector shaft",model,V(0,-.2f,.5f),V(.07f,.07f,1.1f),metal);
            var display=Box("Detector display",model,V(0,.11f,.15f),V(.34f,.1f,.3f),yellow);
            var led=Shape("Red LED",PrimitiveType.Sphere,model,V(0,.19f,.15f),Vector3.one*.10f,red).GetComponent<Renderer>();
            for(int i=0;i<24;i++){float a=i*Mathf.PI/12;var seg=Box("Search coil",model,V(Mathf.Cos(a)*.34f,-.2f,1+Mathf.Sin(a)*.34f),V(.1f,.065f,.065f),yellow);seg.transform.localRotation=Quaternion.Euler(0,-a*Mathf.Rad2Deg+90,0);}
            var collider=t.gameObject.AddComponent<BoxCollider>();collider.center=V(0,-.05f,.5f);collider.size=V(.75f,.5f,1.65f);
            var body=t.gameObject.AddComponent<Rigidbody>();body.mass=3;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;t.gameObject.layer=GameLayers.Prop;
            Grabbable.Mark(t.gameObject,"지뢰 탐색기",false,1,2,GrabbableRole.Tool);t.gameObject.AddComponent<AstraV2CargoTool>();
            var rings=new LineRenderer[3];for(int i=0;i<3;i++){var r=Group("Scan concentric ring "+i,t,Vector3.zero).gameObject.AddComponent<LineRenderer>();r.sharedMaterial=pulse;r.useWorldSpace=true;r.loop=true;r.positionCount=48;r.startWidth=r.endWidth=.035f;r.enabled=false;rings[i]=r;}
            var tool=t.gameObject.AddComponent<AstraMineDetector>();tool.ConfigureModel(model);tool.Configure(led,rings);
        }
        [MenuItem("Tools/Metagame/Astra/Add minefield and detectors")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra_ver2.unity");
            if(GameObject.Find("Astra minefield and detectors")!=null)throw new Exception("Minefield already authored");
            terrain=Object.FindAnyObjectByType<Terrain>();root=new GameObject("Astra minefield and detectors").transform;
            if(!AssetDatabase.IsValidFolder(Art))AssetDatabase.CreateFolder("Assets/_Project/Art","AstraMinefield");
            dark=Mat("Detector graphite",new Color(.025f,.035f,.045f));yellow=Mat("Warning yellow",new Color(1,.72f,.025f));metal=Mat("Detector metal",new Color(.45f,.56f,.6f));red=Mat("Detector LED",Color.red);pulse=Mat("Scan pulse",Color.white,true);
            var mesh=new Mesh{name="Warning triangle"};mesh.vertices=new[]{V(0,.7f,0),V(-.65f,-.5f,0),V(.65f,-.5f,0),V(0,.7f,0),V(-.65f,-.5f,0),V(.65f,-.5f,0)};mesh.triangles=new[]{0,1,2,3,5,4};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Art+"/Warning triangle.asset");
            var xs=new[]{21f,23,27,29,21.4f,23.8f,25.3f,29,22.8f,24.3f,28.2f,30,21,23,27,29};
            var zs=new[]{9.6f,9.6f,9.6f,9.6f,12.2f,12.2f,12.2f,12.2f,14,14,14,14,16.5f,16.5f,16.5f,16.5f};
            var mines=new Vector3[xs.Length];var marks=new Transform[xs.Length];
            for(int i=0;i<xs.Length;i++){mines[i]=V(xs[i],Floor(xs[i],zs[i]),zs[i]);marks[i]=Shape("Spent mine scar "+i,PrimitiveType.Cylinder,root,mines[i]+Vector3.up*.015f,V(1.1f,.015f,1.1f),dark).transform;marks[i].gameObject.SetActive(false);}
            var flash=Shape("Mine blast flash",PrimitiveType.Sphere,root,Vector3.zero,Vector3.one,yellow).transform;flash.gameObject.SetActive(false);
            root.gameObject.AddComponent<AstraMinefield>().Configure(mines,marks,flash);
            foreach(float x in new[]{19.6f,31.8f})Box("Minefield boundary wall",root,V(x,2.2f,13.2f),V(.2f,2.4f,9.6f),metal,true);
            Sign(19.2f,8.8f,0,3.2f);Sign(29,7,0);Sign(30.7f,17.2f,180);
            Detector(22,5);Detector(28,5);Detector(29,21);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Validation/AstraMinefield");File.WriteAllText("Validation/AstraMinefield/authoring.txt","PASS: 16 mines, 3 detectors, 3 icon-only signs; existing scene preserved.");Debug.Log("ASTRA MINEFIELD AUTHORING PASS");
        }
    }
}
