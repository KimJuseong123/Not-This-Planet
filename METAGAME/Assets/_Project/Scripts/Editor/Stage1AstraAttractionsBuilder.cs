using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1AstraAttractionsBuilder
    {
        static Transform root;static Terrain terrain;
        static Material dark,white,orange,blue,red,rock;
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static float Floor(float x,float z)=>terrain.SampleHeight(V(x,0,z))+terrain.transform.position.y;
        static Transform Group(string name,Vector3 position,Transform parent=null)
        {var t=new GameObject(name).transform;t.SetParent(parent!=null?parent:root,false);t.position=position;return t;}
        static GameObject Shape(string name,PrimitiveType type,Transform parent,Vector3 local,Vector3 size,Material mat,bool solid=true)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=local;go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=mat;go.layer=GameLayers.Ground;
            if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());
            else if(type==PrimitiveType.Cylinder){Object.DestroyImmediate(go.GetComponent<Collider>());var c=go.AddComponent<MeshCollider>();c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;c.convex=true;}
            return go;
        }
        static GameObject Box(string n,Transform p,Vector3 at,Vector3 size,Material m,bool solid=true)=>Shape(n,PrimitiveType.Cube,p,at,size,m,solid);
        static void Rod(string name,Transform parent,Vector3 a,Vector3 b,float width,Material m,bool solid=false)
        {var go=Shape(name,PrimitiveType.Cylinder,parent,(a+b)*.5f,V(width,Vector3.Distance(a,b)*.5f,width),m,solid);go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);}
        static LineRenderer Line(Transform t,Vector3 a,Vector3 b,Material m,float width)
        {var line=t.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=m;line.positionCount=2;line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.SetPosition(0,a);line.SetPosition(1,b);return line;}
        static Rigidbody FixedBody(Transform t,bool tracked=false)
        {var b=t.gameObject.AddComponent<Rigidbody>();b.isKinematic=true;b.interpolation=RigidbodyInterpolation.Interpolate;if(tracked)t.gameObject.AddComponent<AstraV2CargoTool>();return b;}
        static void Wheel()
        {
            var center=V(-36,13,-31);var t=Group("Always turning Ferris wheel",center);var rotor=Group("Ferris rotor",center,t);
            var cabins=new Rigidbody[8];
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<64;i++)
                    Rod("Wheel rim",rotor,AstraFerrisWheel.CabinOffset(i*360f/64,9)+Vector3.right*side*1.7f,AstraFerrisWheel.CabinOffset((i+1)*360f/64,9)+Vector3.right*side*1.7f,.15f,orange);
                for(int i=0;i<8;i++)Rod("Spoke",rotor,Vector3.right*side*1.7f,AstraFerrisWheel.CabinOffset(i*45,9)+Vector3.right*side*1.7f,.09f,blue);
                Rod("Wheel A leg",t,V(side*2.5f,-11,-4),V(side*2.5f,0,0),.45f,dark,true);
                Rod("Wheel B leg",t,V(side*2.5f,-11,4),V(side*2.5f,0,0),.45f,dark,true);
            }
            Rod("Axle",t,V(-3,0,0),V(3,0,0),.65f,white);
            for(int i=0;i<8;i++)
            {
                var cabin=Group("Open cabin "+i,center+AstraFerrisWheel.CabinOffset(i*45,9),t);
                Box("Cabin floor",cabin,V(0,-1.3f,0),V(3.8f,.22f,3.1f),i%2==0?blue:orange);
                Box("Cabin roof",cabin,V(0,1.4f,0),V(3.8f,.16f,3.1f),white);
                Box("Cabin back",cabin,V(-1.8f,-.4f,0),V(.16f,1.7f,3.1f),dark);
                foreach(float z in new[]{-1.48f,1.48f})
                {
                    Box("Side railing",cabin,V(0,-.5f,z),V(3.6f,1.3f,.12f),i%2==0?blue:orange);
                    foreach(float x in new[]{-1.8f,1.8f})Box("Canopy post",cabin,V(x,.1f,z),V(.1f,2.6f,.1f),white);
                }
                // East side stays open: no door or invisible entry collider.
                cabins[i]=FixedBody(cabin);
            }
            t.gameObject.AddComponent<AstraFerrisWheel>().Configure(rotor,cabins);
            var dock=Group("Ferris walk-on boarding dock",V(-31,0,-31));
            Box("Boarding landing",dock,V(-1.2f,2.48f,0),V(3.8f,.35f,4),dark);
            for(int i=0;i<5;i++)Box("Boarding step "+i,dock,V(2.7f-i*.55f,1.13f+i*.3f,0),V(.6f,.3f,4),white);
        }
        static void Cannon()
        {
            var p=V(3,Floor(3,-32),-32);var t=Group("Three second crew cannon",p);
            Box("Cannon plinth",t,V(0,.25f,0),V(3.5f,.5f,3.8f),dark);
            var barrel=Group("Hollow cannon barrel",p+V(0,1.6f,0),t);barrel.localRotation=Quaternion.Euler(-30,0,0);
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16;var v=V(Mathf.Cos(angle)*1.05f,Mathf.Sin(angle)*1.05f,0);
                Box("Barrel stave "+i,barrel,v,V(.23f,.23f,3.6f),dark);
                Shape("Muzzle rim",PrimitiveType.Sphere,barrel,v+Vector3.forward*1.8f,Vector3.one*.3f,orange,false);
            }
            Box("Barrel rear",barrel,V(0,0,-1.8f),V(1.9f,1.9f,.16f),dark);
            foreach(float x in new[]{-1.6f,1.6f})
            {var wheel=Shape("Cannon wheel",PrimitiveType.Cylinder,t,V(x,.7f,0),V(1.6f,.22f,1.6f),orange);wheel.transform.localRotation=Quaternion.Euler(0,0,90);}
            var seat=Group("Cannon occupied seat",barrel.position,t);var body=FixedBody(seat,true);
            var launch=Group("Cannon launch exit",barrel.TransformPoint(V(0,0,3.4f)),t);launch.rotation=barrel.rotation;
            var wayOut=Group("Cannon walk out",p+V(2.6f,.1f,0),t);
            var entry=Shape("Cannon mouth interact",PrimitiveType.Sphere,t,V(0,1.5f,2.2f),Vector3.one*.6f,blue);
            var mouth=Grabbable.Mark(entry,"대포 입구 · 잡아서 탑승",false,1,3);
            var rear=Box("Cannon rear igniter",t,V(0,Floor(3,-35)-p.y+.65f,-3),V(.65f,.8f,.55f),orange);
            var ignition=Grabbable.Mark(rear,"대포 뒤 · 잡아서 점화",false,1,3);
            var wire=Group("Burning three second fuse",p,t);var a=barrel.TransformPoint(V(0,0,-1.9f));var b=rear.transform.position+Vector3.up*.5f;
            var line=Line(wire,a,b,white,.055f);var ember=Shape("Fuse ember",PrimitiveType.Sphere,t,Vector3.zero,Vector3.one*.2f,orange,false).transform;ember.gameObject.SetActive(false);
            t.gameObject.AddComponent<AstraPlayerCannon>().Configure(body,launch,wayOut,mouth,ignition,line,ember);
        }
        static void Vault()
        {
            float floor=Mathf.Max(Floor(37,18),Floor(37,25));var p=V(37,floor,18);var t=Group("TNT rubble supply vault",p);
            Box("Vault floor",t,V(0,0,3.5f),V(6.5f,.25f,7.5f),white);
            foreach(float x in new[]{-3.15f,3.15f})Box("Vault wall",t,V(x,2,3.5f),V(.4f,4,7.5f),rock);
            Box("Vault back",t,V(0,2,7),V(6.5f,4,.4f),rock);Box("Vault roof",t,V(0,4.1f,3.5f),V(6.5f,.35f,7.5f),rock);
            for(int i=0;i<4;i++)Box("Vault approach step "+i,t,V(0,-.6f+i*.2f,-2.5f+i*.6f),V(5.8f,.3f,.65f),white);
            var rubble=Group("Destructible entrance boulders",p+V(0,1.8f,0),t);
            var collider=rubble.gameObject.AddComponent<BoxCollider>();collider.size=V(5.9f,3.8f,1.8f);rubble.gameObject.layer=GameLayers.Ground;
            for(int i=0;i<7;i++){var stone=Shape("Fractured boulder "+i,PrimitiveType.Cube,rubble,V((i%3-1)*1.8f,(i/3-1)*.9f,0),V(2.4f,1.8f,1.8f),rock,false);stone.transform.localRotation=Quaternion.Euler(i*9,i*13,i*11);}
            var charge=Group("Carryable wired TNT",V(38,Floor(38,10)+.65f,10),t);
            for(int i=0;i<3;i++){var stick=Shape("TNT stick",PrimitiveType.Cylinder,charge,V((i-1)*.32f,0,0),V(.32f,.65f,.32f),red,false);stick.transform.localRotation=Quaternion.Euler(90,0,0);}
            Box("TNT band",charge,Vector3.zero,V(1.05f,.5f,.2f),dark,false);
            var label=Group("TNT label",charge.position+V(0,.27f,0),charge);label.localRotation=Quaternion.Euler(90,0,0);
            var text=label.gameObject.AddComponent<TextMesh>();text.text="TNT";text.anchor=TextAnchor.MiddleCenter;text.characterSize=.075f;text.fontSize=60;text.color=Color.white;
            var box=charge.gameObject.AddComponent<BoxCollider>();box.size=V(1.05f,.5f,1.3f);
            var rb=charge.gameObject.AddComponent<Rigidbody>();rb.mass=8;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            charge.gameObject.layer=GameLayers.Prop;Grabbable.Mark(charge.gameObject,"TNT · 바위 앞에 내려놓기").PhysicsGrip=true;charge.gameObject.AddComponent<AstraV2CargoTool>();
            var button=Group("Remote TNT plunger",V(42,Floor(42,7),7),t);
            Box("Detonator box",button,V(0,.5f,0),V(1.2f,1,.9f),red);
            Box("Plunger stem",button,V(0,1.1f,0),V(.16f,.4f,.16f),white);
            var handle=Box("TNT switch handle",button,V(0,1.35f,0),V(1.2f,.18f,.22f),orange);
            var control=Grabbable.Mark(handle,"원격 기폭 스위치",false,1,3);
            var cable=Line(Group("Visible TNT cable",p,t),control.transform.position,charge.position,orange,.045f);
            var blast=Shape("Blast flash",PrimitiveType.Sphere,t,Vector3.zero,Vector3.one,orange,false).transform;blast.gameObject.SetActive(false);
            var chips=new Transform[10];for(int i=0;i<chips.Length;i++){chips[i]=Box("Flying rock fragment "+i,t,Vector3.zero,Vector3.one*(.2f+i*.03f),rock,false).transform;chips[i].gameObject.SetActive(false);}
            t.gameObject.AddComponent<AstraBlastGate>().Configure(rb,rubble.gameObject,control,cable,blast,chips);
            var source=Object.FindObjectsByType<AstraFuelCan>().First();
            for(int i=0;i<2;i++){var can=Object.Instantiate(source.gameObject,t);can.name="Vault reward fuel "+i;can.transform.position=p+V(-1+i*2,.65f,5);}
        }
        [MenuItem("Tools/Metagame/Astra/Add wheel cannon and TNT vault")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra_ver2.unity");
            if(GameObject.Find("Astra wheel cannon and demolition")!=null)throw new Exception("Attractions already authored");
            root=new GameObject("Astra wheel cannon and demolition").transform;terrain=Object.FindAnyObjectByType<Terrain>();
            const string art="Assets/_Project/Art/AstraToybox/";
            dark=AssetDatabase.LoadAssetAtPath<Material>(art+"Graphite.mat");white=AssetDatabase.LoadAssetAtPath<Material>(art+"Ivory.mat");orange=AssetDatabase.LoadAssetAtPath<Material>(art+"Tangerine.mat");blue=AssetDatabase.LoadAssetAtPath<Material>(art+"Pool blue.mat");
            if(!AssetDatabase.IsValidFolder("Assets/_Project/Art/AstraAttractions"))AssetDatabase.CreateFolder("Assets/_Project/Art","AstraAttractions");
            red=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="TNT safety red"};red.color=new Color(.7f,.035f,.035f);AssetDatabase.CreateAsset(red,"Assets/_Project/Art/AstraAttractions/TNT safety red.mat");
            rock=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Stage1AstraVer2/Lavender cliff.mat");
            if(rock==null||dark==null||white==null||blue==null||orange==null)throw new Exception("Required attraction material is missing");
            Wheel();Cannon();Vault();
            foreach(var child in root.GetComponentsInChildren<Transform>())if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject)>0)throw new Exception("Missing script");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Validation/AstraAttractions");
            var camera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First();
            Render(camera,V(-12,14,-45),V(-35,12,-30),"ferris-wheel");Render(camera,V(11,7,-39),V(3,2,-32),"cannon");Render(camera,V(48,14,3),V(37,3,18),"tnt-vault");
            Debug.Log("ASTRA ATTRACTIONS AUTHORING PASS");
        }
        static void Render(Camera camera,Vector3 pos,Vector3 look,string name)
        {
            camera.transform.position=pos;camera.transform.LookAt(look);camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=400;
            ShaderUtil.allowAsyncCompilation=false;var rt=new RenderTexture(1400,1000,24);rt.Create();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt};for(int i=0;i<5;i++)UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=rt;var img=new Texture2D(1400,1000,TextureFormat.RGB24,false);img.ReadPixels(new Rect(0,0,1400,1000),0,0);img.Apply();File.WriteAllBytes("Validation/AstraAttractions/"+name+".png",img.EncodeToPNG());
            RenderTexture.active=null;Object.DestroyImmediate(img);rt.Release();Object.DestroyImmediate(rt);
        }
    }
}
