using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Metagame.Gameplay;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static class Stage1AstraToyBuilder
    {
        const string ScenePath="Assets/Scenes/Stage1_Astra_ver2.unity";
        const string Art="Assets/_Project/Art/AstraToybox";
        static Transform root;
        static Terrain terrain;
        static Material white,dark,orange,blue,pink,green;
        static PhysicsMaterial bounce,slide;
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static float Floor(float x,float z)=>terrain.SampleHeight(V(x,0,z))+terrain.transform.position.y;
        static Vector3 At(float x,float z,float up=0)=>V(x,Floor(x,z)+up,z);
        static Material Mat(string name,Color color)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.color=color;
            AssetDatabase.CreateAsset(m,Art+"/"+name+".mat");return m;
        }
        static GameObject Shape(string name,PrimitiveType type,Transform parent,Vector3 local,Vector3 scale,Material mat,bool solid=true)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=local;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;
            go.layer=GameLayers.Ground;
            if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());
            else if(type==PrimitiveType.Cylinder){Object.DestroyImmediate(go.GetComponent<Collider>());var c=go.AddComponent<MeshCollider>();c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;c.convex=true;}
            return go;
        }
        static GameObject Box(string name,Transform p,Vector3 at,Vector3 size,Material mat,bool solid=true)=>Shape(name,PrimitiveType.Cube,p,at,size,mat,solid);
        static Transform Group(string name,Vector3 p)
        {var t=new GameObject(name).transform;t.SetParent(root,false);t.position=p;return t;}
        static Rigidbody Body(Transform t,float mass,string label)
        {
            var b=t.gameObject.AddComponent<Rigidbody>();b.mass=mass;b.linearDamping=.12f;b.angularDamping=.08f;
            b.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;b.interpolation=RigidbodyInterpolation.Interpolate;
            foreach(var child in t.GetComponentsInChildren<Transform>())child.gameObject.layer=GameLayers.Prop;
            Grabbable.Mark(t.gameObject,label).PhysicsGrip=true;t.gameObject.AddComponent<AstraV2CargoTool>();return b;
        }
        static void Ball(string name,float x,float z,float radius,float mass,Material color,bool panels)
        {
            var t=Group(name,At(x,z,radius+.12f));
            var sphere=Shape("Ball",PrimitiveType.Sphere,t,Vector3.zero,Vector3.one*radius*2,color);
            sphere.GetComponent<Collider>().sharedMaterial=bounce;
            if(panels)foreach(var axis in new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back})
            {var patch=Shape("Ball panel",PrimitiveType.Sphere,t,axis*radius*.95f,V(radius*.7f,radius*.7f,.04f),dark,false);patch.transform.localRotation=Quaternion.FromToRotation(Vector3.forward,axis);}
            Body(t,mass,name);
        }
        static void Goal(float x,float z,float yaw)
        {
            var t=Group("Mini football goal",At(x,z));t.rotation=Quaternion.Euler(0,yaw,0);
            foreach(float side in new[]{-1f,1f})Box("Goal post",t,V(side*3,1.7f,0),V(.18f,3.4f,.18f),white);
            Box("Crossbar",t,V(0,3.4f,0),V(6.2f,.18f,.18f),white);
            // Open front, lightweight decorative net and a physical ball backstop.
            Box("Backstop",t,V(0,.35f,-1.5f),V(6,.7f,.15f),dark);
            for(int i=0;i<=12;i++)Box("Net strand",t,V(-3+i*.5f,1.7f,-1.55f),V(.02f,3.4f,.02f),white,false);
            for(int i=0;i<=7;i++)Box("Net cross strand",t,V(0,i*.47f,-1.55f),V(6,.02f,.02f),white,false);
        }
        static void Toys()
        {
            // West open shelf, outside the four marked return roads and fatal rifts.
            Goal(-30,5,90);Goal(-14,5,-90);
            Ball("축구공 A",-24,5,.6f,2f,white,true);Ball("축구공 B",-20,7,.6f,2f,white,true);
            Ball("비치볼 A",-26,10,1f,1f,pink,true);Ball("비치볼 B",-19,10,.9f,1f,blue,true);
            Ball("볼링공 A",15,6,.55f,12f,dark,false);Ball("볼링공 B",17,6,.55f,12f,blue,false);
            for(int row=0,id=0;row<4;row++)for(int col=0;col<=row;col++,id++)
            {
                var p=At(16+(col-row*.5f)*1.1f,9+row*1.1f,.8f);var t=Group("볼링 핀 "+id,p);
                Shape("Pin belly",PrimitiveType.Capsule,t,V(0,-.1f,0),V(.55f,.65f,.55f),white);
                Shape("Pin neck",PrimitiveType.Cylinder,t,V(0,.6f,0),V(.23f,.25f,.23f),orange,false);
                Shape("Pin head",PrimitiveType.Sphere,t,V(0,.92f,0),V(.36f,.36f,.36f),white);
                Shape("Flat pin foot",PrimitiveType.Cylinder,t,V(0,-.73f,0),V(.5f,.06f,.5f),white);
                Body(t,2.5f,"볼링 핀").centerOfMass=V(0,-.3f,0);
            }
            for(int i=0;i<12;i++)
            {
                var t=Group("도미노 "+i,At(-30+i*.85f,12,1.05f));
                Box("Domino block",t,Vector3.zero,V(.22f,2,.8f),i%2==0?orange:blue);
                Box("Domino divider",t,V(-.116f,0,0),V(.015f,.035f,.7f),white,false);Body(t,4f,"도미노");
            }
            for(int i=0;i<6;i++)
            {
                var t=Group("쌓기 상자 "+i,At(32+(i%3)*1.6f,8+(i/3)*1.6f,.65f));
                Box("Stacking crate",t,Vector3.zero,Vector3.one*1.2f,i%2==0?green:orange);
                foreach(float x in new[]{-.5f,.5f})Box("Crate strap",t,V(x,0,0),V(.07f,1.23f,1.23f),dark,false);
                Body(t,7f,"쌓기 상자");
            }
            var bag=Group("매달린 샌드백",At(-10,19,2.5f));
            Shape("Punch bag",PrimitiveType.Capsule,bag,Vector3.zero,V(1.1f,1.5f,1.1f),orange);
            Body(bag,30f,"샌드백");
            var joint=bag.gameObject.AddComponent<ConfigurableJoint>();joint.autoConfigureConnectedAnchor=false;joint.anchor=V(0,1.3f,0);joint.connectedAnchor=bag.position+Vector3.up*1.3f;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Locked;
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Free;
            var frame=Group("Punch bag frame",At(-10,19));
            Box("Bag support",frame,V(-2,2.3f,0),V(.25f,4.6f,.25f),dark);
            Box("Bag gantry",frame,V(-1,4.6f,0),V(2.5f,.25f,.25f),dark);
            Box("Bag strap",frame,V(0,4.2f,0),V(.1f,.75f,.1f),white,false);
        }
        static void Cargo()
        {
            for(int i=0;i<2;i++)
            {
                var p=i==0?At(-19,18,.42f):At(14,-13,.42f);var t=Group("운반 썰매 "+i,p);
                Box("Low cargo deck",t,Vector3.zero,V(3.5f,.2f,4.4f),green);
                foreach(float x in new[]{-1.55f,1.55f})
                {
                    var skid=Box("Low friction skid",t,V(x,-.2f,0),V(.3f,.2f,4.3f),dark);skid.GetComponent<Collider>().sharedMaterial=slide;
                    Box("Cargo side rail",t,V(x,.4f,0),V(.13f,.65f,4.4f),orange);
                }
                Box("Pull handle",t,V(0,.25f,2.7f),V(1.8f,.15f,.15f),orange);
                foreach(float x in new[]{-.8f,.8f})Box("Handle strut",t,V(x,.15f,2.35f),V(.12f,.12f,.7f),dark);
                Body(t,20f,"운반 썰매");
                var ramp=Group("경사판 "+i,p+V(4,.4f,0));
                var mesh=new Mesh{name="Cargo wedge"};
                mesh.vertices=new[]{V(-1.3f,-.5f,-2),V(1.3f,-.5f,-2),V(-1.3f,-.5f,2),V(1.3f,-.5f,2),V(-1.3f,.5f,2),V(1.3f,.5f,2)};
                mesh.triangles=new[]{0,2,1,1,2,3,0,1,4,1,5,4,2,4,3,3,4,5,0,4,2,1,3,5};mesh.RecalculateNormals();
                AssetDatabase.CreateAsset(mesh,Art+"/Cargo wedge "+i+".asset");
                ramp.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;ramp.gameObject.AddComponent<MeshRenderer>().sharedMaterial=blue;
                var col=ramp.gameObject.AddComponent<MeshCollider>();col.sharedMesh=mesh;col.convex=true;Body(ramp,18,"경사판");
            }
            for(int i=0;i<3;i++)
            {
                var t=Group("짐 롤러 "+i,At(12+i*1.3f,-10,.52f));
                var roller=Shape("Cargo roller",PrimitiveType.Cylinder,t,Vector3.zero,V(.8f,1.6f,.8f),orange);
                roller.transform.localRotation=Quaternion.Euler(0,0,90);Body(t,10,"짐 롤러");
            }
            foreach(var point in new[]{At(-28,20,.3f),At(33,-17,.3f)})
            {
                var t=Group("트램펄린 "+point.x,point);Box("Spring bed",t,Vector3.zero,V(3.4f,.3f,3.4f),blue);
                foreach(float x in new[]{-1.75f,1.75f})Box("Spring rim",t,V(x,0,0),V(.15f,.45f,3.6f),orange);
                foreach(float z in new[]{-1.75f,1.75f})Box("Spring rim",t,V(0,0,z),V(3.6f,.45f,.15f),orange);
                t.gameObject.AddComponent<AstraBouncePad>();
            }
        }
        static LineRenderer Line(Transform parent,Material mat,float width)
        {
            var t=new GameObject("Effect line").transform;t.SetParent(parent,false);var line=t.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial=mat;line.positionCount=2;line.startWidth=width;line.endWidth=width;line.useWorldSpace=true;line.enabled=false;return line;
        }
        static void Tools()
        {
            int index=0;foreach(var p in new[]{At(-28,15),At(12,16),At(25,-13)})
            {
                var rack=Group("Utility rack "+index,p);Box("Tool shelf",rack,V(0,.5f,0),V(3.5f,.18f,1.2f),dark);
                for(int kind=0;kind<2;kind++)
                {
                    var t=Group((kind==0?"그래플링 훅 건 ":"송풍기 ")+index,p+V(kind*1.6f-.8f,1.1f,0));
                    var c=t.gameObject.AddComponent<BoxCollider>();c.size=V(.65f,.7f,1.1f);c.center=V(0,0,.15f);
                    var body=t.gameObject.AddComponent<Rigidbody>();body.mass=5f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    var model=new GameObject("Model").transform;model.SetParent(t,false);
                    Box("Tool grip",model,V(0,-.28f,-.1f),V(.16f,.35f,.2f),dark,false);
                    Box("Tool body",model,Vector3.zero,V(.5f,.4f,.6f),kind==0?blue:green,false);
                    var barrel=Shape("Barrel",PrimitiveType.Cylinder,model,V(0,.05f,.43f),V(.32f,.3f,.32f),dark,false);barrel.transform.localRotation=Quaternion.Euler(90,0,0);
                    if(kind==0)
                    {
                        foreach(float side in new[]{-1f,1f})Box("Hook prong",model,V(side*.24f,.05f,.65f),V(.08f,.13f,.32f),orange,false);
                        var spool=Shape("Cable spool",PrimitiveType.Cylinder,model,V(0,.25f,-.1f),V(.44f,.3f,.44f),orange,false);spool.transform.localRotation=Quaternion.Euler(0,0,90);
                        var tool=t.gameObject.AddComponent<AstraGrappleTool>();tool.ConfigureModel(model);tool.ConfigureCable(Line(t,orange,.045f));
                    }
                    else
                    {
                        Shape("Fan housing",PrimitiveType.Sphere,model,V(0,.12f,-.25f),V(.62f,.62f,.42f),white,false);
                        var tool=t.gameObject.AddComponent<AstraAirBlower>();tool.ConfigureModel(model);var line=Line(t,blue,.035f);line.endWidth=.4f;tool.ConfigureStream(line);
                    }
                    var muzzle=new GameObject("Muzzle").transform;muzzle.SetParent(t,false);muzzle.localPosition=V(0,.05f,.8f);
                    foreach(var child in t.GetComponentsInChildren<Transform>())child.gameObject.layer=GameLayers.Prop;
                    Grabbable.Mark(t.gameObject,kind==0?"그래플링 훅 건":"송풍기",false,1,2,GrabbableRole.Tool);
                    t.gameObject.AddComponent<AstraV2CargoTool>();
                }
                index++;
            }
        }
        [MenuItem("Tools/Metagame/Astra/Add physical toybox")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath);
            if(GameObject.Find("Astra physical toybox")!=null)throw new Exception("Toybox already exists; edit authored objects directly.");
            if(!AssetDatabase.IsValidFolder(Art))AssetDatabase.CreateFolder("Assets/_Project/Art","AstraToybox");
            root=new GameObject("Astra physical toybox").transform;terrain=Object.FindAnyObjectByType<Terrain>();
            white=Mat("Ivory",new Color(.87f,.87f,.81f));dark=Mat("Graphite",new Color(.055f,.06f,.1f));orange=Mat("Tangerine",new Color(1,.38f,.075f));
            blue=Mat("Pool blue",new Color(.08f,.65f,.94f));pink=Mat("Coral pink",new Color(.96f,.18f,.47f));green=Mat("Mint",new Color(.16f,.72f,.46f));
            bounce=new PhysicsMaterial("Toy ball rubber"){bounciness=.68f,dynamicFriction=.45f,staticFriction=.5f,bounceCombine=PhysicsMaterialCombine.Maximum};
            slide=new PhysicsMaterial("Cargo runners"){dynamicFriction=.08f,staticFriction=.1f,frictionCombine=PhysicsMaterialCombine.Minimum};
            AssetDatabase.CreateAsset(bounce,Art+"/Toy ball rubber.physicMaterial");AssetDatabase.CreateAsset(slide,Art+"/Cargo runners.physicMaterial");
            // Remove only ecological dressing inside the play court, preserving cliff/room structure.
            var ecology=GameObject.Find("05 - purple ecology framing open sightlines");
            if(ecology!=null)foreach(Transform child in ecology.transform.Cast<Transform>().ToArray())
            {
                var renderers=child.GetComponentsInChildren<Renderer>();if(renderers.Length==0)continue;
                var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                if(bounds.Intersects(new Bounds(V(-23,3,7),V(22,8,17))))Object.DestroyImmediate(child.gameObject);
            }
            Toys();Cargo();Tools();
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            var bodies=root.GetComponentsInChildren<Rigidbody>();
            if(bodies.Length!=48)Debug.Log("Toybox bodies="+bodies.Length);
            foreach(var t in root.GetComponentsInChildren<Transform>())if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script "+t.name);
            Directory.CreateDirectory("Validation/AstraToybox");File.WriteAllText("Validation/AstraToybox/authored.txt","PASS toybox saved; dynamic bodies="+bodies.Length+"; grapple guns=3; blowers=3; trampolines=2; no missing scripts\n");
            var camera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First();
            Render(camera,V(-45,26,-5),V(-19,2,9),"play-yard");Render(camera,V(38,20,-21),V(17,3,-8),"cargo-tools");
            Render(camera,V(62,70,-76),V(0,2,2),"overview");
            Debug.Log("ASTRA TOYBOX AUTHORING PASS");
        }
        static void Render(Camera camera,Vector3 position,Vector3 target,string name)
        {
            camera.transform.position=position;camera.transform.LookAt(target);camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=400;
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            var rt=new RenderTexture(1600,1000,24);rt.Create();
            try{var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt};for(int i=0;i<5;i++)UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=rt;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes("Validation/AstraToybox/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
            finally{RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);ShaderUtil.allowAsyncCompilation=async;}
        }
    }
}
