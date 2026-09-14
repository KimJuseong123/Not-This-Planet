using System;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Adds authored Terrain, fatal rifts and physical tools without rebuilding existing puzzles.</summary>
    public static class Stage1AstraExpansionBuilder
    {
        const string ScenePath = "Assets/Scenes/Stage1_Astra_ver2.unity";
        const string ArtPath = "Assets/_Project/Art/Stage1AstraExpansion";
        const string RootName = "Astra ver2 - terrain and toys";
        static Transform root;
        static Material metal, ceramic, yellow, red, cyan, ink, wood;
        static readonly Vector2[][] Roads =
        {
            new[]{P(-24,-20),P(-14,-12),P(-6,-5),P(0,0)},
            new[]{P(-13.2f,25),P(-5,24),P(0,14),P(0,0)},
            new[]{P(25,25),P(25,18),P(25,9),P(12,2),P(0,0)},
            new[]{P(28,-26),P(28,-20),P(16,-20),P(13.2f,-20),P(11,-12),P(5,-7),P(0,0)}
        };
        public static readonly Vector2[][] Trenches =
        {
            new[]{P(-39,-28),P(-37,-17),P(-40,-6)},
            new[]{P(-1,31),P(6,25),P(13,29)},
            new[]{P(23,-3),P(32,-5),P(41,1)}
        };
        static Vector2 P(float x, float z) => new Vector2(x,z);
        static Vector3 V(float x, float y, float z) => new Vector3(x,y,z);

        [MenuItem("Metagame/Stage1/Add Terrain and toys to Astra ver2")]
        public static void Apply()
        {
            EnsureFolder(ArtPath);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (scene.GetRootGameObjects().Any(g=>g.name==RootName)) throw new InvalidOperationException("Expansion already exists; authored edits will not be overwritten.");
                root = new GameObject(RootName).transform;
                PrepareMaterials();
                var oldFloor = Find(scene,"Walkable basin - flush cargo datum");
                if (oldFloor == null) throw new InvalidOperationException("Expected ver2 basin was not found.");
                Object.DestroyImmediate(oldFloor);
                BuildTerrain();
                MoveEcology(scene);
                BuildTrenches();
                BuildPlayground();
                BuildTools();
                ConfigureFuel(scene);
                var sky = MaterialAsset("Astra Milky Way",Shader.Find("Metagame/Atmosphere/AstraMilkyWay"),Color.white);
                sky.SetFloat("_Exposure",1.2f);
                RenderSettings.skybox = sky;
                foreach (var camera in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)))
                { camera.clearFlags=CameraClearFlags.Skybox; camera.farClipPlane=400f; }
                Physics.SyncTransforms();
                Validate(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[AstraExpansion] Saved Terrain, three fatal rifts, physical playground, rocket, ink and fuel in "+ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid()&&previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        static GameObject Find(Scene scene,string name) => scene.GetRootGameObjects()
            .SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name)?.gameObject;
        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=path.Substring(0,path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static Material MaterialAsset(string name,Shader shader,Color color)
        {
            string path=ArtPath+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) { material=new Material(shader){name=name}; AssetDatabase.CreateAsset(material,path); }
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",color);
            return material;
        }
        static Material Glow(string name,Color color)
        {
            var mat=MaterialAsset(name,Resources.Load<Shader>("GuidanceNoFog"),color);
            mat.SetColor("_EmissionColor",color); mat.SetFloat("_EmissionIntensity",.75f); return mat;
        }
        static void PrepareMaterials()
        {
            var lit=Shader.Find("Universal Render Pipeline/Lit");
            metal=MaterialAsset("Dark alloy",lit,new Color(.065f,.08f,.13f));
            ceramic=MaterialAsset("Warm shell",lit,new Color(.69f,.70f,.65f));
            wood=MaterialAsset("Weathered plank",lit,new Color(.38f,.21f,.095f));
            ink=MaterialAsset("Wet violet ink",lit,new Color(.035f,.005f,.075f)); ink.SetFloat("_Smoothness",.85f);
            yellow=Glow("Fuel yellow outline",new Color(1f,.73f,.02f));
            red=Glow("Trench warning",new Color(1f,.15f,.06f));
            cyan=Glow("Rocket exhaust",new Color(.1f,.62f,1f));
        }
        public static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {
            var ab=b-a; float t=ab.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude):0;
            return Vector2.Distance(p,a+ab*t);
        }
        static float DistanceToRect(Vector2 p,float minX,float maxX,float minZ,float maxZ)
            => new Vector2(Mathf.Max(minX-p.x,0,p.x-maxX),Mathf.Max(minZ-p.y,0,p.y-maxZ)).magnitude;
        public static float HeightAt(float x,float z)
        {
            var p=P(x,z); float clearance=Mathf.Max(0,p.magnitude-8.5f);
            foreach(var road in Roads) for(int i=1;i<road.Length;i++)
                clearance=Mathf.Min(clearance,Mathf.Max(0,SegmentDistance(p,road[i-1],road[i])-3.8f));
            clearance=Mathf.Min(clearance,DistanceToRect(p,-43,-8,5,38));
            clearance=Mathf.Min(clearance,DistanceToRect(p,13,35,8,34));
            clearance=Mathf.Min(clearance,DistanceToRect(p,12,36,-33,-14));
            clearance=Mathf.Min(clearance,DistanceToRect(p,-32,-16,-31,-15));
            clearance=Mathf.Min(clearance,DistanceToRect(p,-12,7,-32,-16));
            clearance=Mathf.Min(clearance,DistanceToRect(p,7,13,2,8));
            float blend=Mathf.SmoothStep(0,1,clearance/4.5f);
            float noise=Mathf.PerlinNoise((x+72)*.05f,(z+99)*.05f);
            float height=1+blend*(.35f+noise*noise*5f);
            foreach(var trench in Trenches) for(int i=1;i<trench.Length;i++)
            {
                float d=SegmentDistance(p,trench[i-1],trench[i]);
                float cut=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.25f,4.2f,d));
                height=Mathf.Lerp(height,-8.5f,cut);
            }
            return height;
        }
        static TerrainLayer Layer(string name,Color low,Color high)
        {
            string path=ArtPath+"/"+name;
            var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path+".asset");
            if(tex==null)
            {
                tex=new Texture2D(128,128,TextureFormat.RGB24,true){name=name,wrapMode=TextureWrapMode.Repeat};
                var pixels=new Color[128*128];
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                    pixels[y*128+x]=Color.Lerp(low,high,Mathf.PerlinNoise(x*.19f,y*.19f)*.75f+Mathf.PerlinNoise(x*.75f,y*.75f)*.25f);
                tex.SetPixels(pixels);tex.Apply();AssetDatabase.CreateAsset(tex,path+".asset");
            }
            var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path+".terrainlayer");
            if(layer==null){layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,path+".terrainlayer");}
            layer.diffuseTexture=tex;layer.tileSize=new Vector2(7,7);layer.smoothness=.15f;
            return layer;
        }
        static void BuildTerrain()
        {
            var data=AssetDatabase.LoadAssetAtPath<TerrainData>(ArtPath+"/Astra rolling Terrain.asset");
            if(data==null){data=new TerrainData();AssetDatabase.CreateAsset(data,ArtPath+"/Astra rolling Terrain.asset");}
            data.heightmapResolution=513;data.size=new Vector3(100,20,90);
            var heights=new float[513,513];
            // Keep the Terrain a few centimetres below authored aprons to avoid coplanar flicker.
            for(int z=0;z<513;z++)for(int x=0;x<513;x++) heights[z,x]=(HeightAt(x/512f*100-50,z/512f*90-45)-.045f+12)/20;
            data.SetHeights(0,0,heights);
            data.terrainLayers=new[]{Layer("Mineral meadow",new Color(.22f,.26f,.20f),new Color(.44f,.44f,.33f)),
                Layer("Rift rock",new Color(.15f,.11f,.20f),new Color(.39f,.31f,.43f))};
            data.alphamapResolution=256;var blend=new float[256,256,2];
            for(int z=0;z<256;z++)for(int x=0;x<256;x++)
            { float stone=Mathf.InverseLerp(18,48,data.GetSteepness(x/255f,z/255f));blend[z,x,0]=1-stone;blend[z,x,1]=stone; }
            data.SetAlphamaps(0,0,blend);
            var go=Terrain.CreateTerrainGameObject(data);go.name="Sculpted Terrain - routes level, hills and fatal rifts";
            go.transform.SetParent(root);go.transform.position=V(-50,-12,-45);go.layer=GameLayers.Ground;
            var terrain=go.GetComponent<Terrain>();
            terrain.materialTemplate=MaterialAsset("Astra Terrain surface",Shader.Find("Universal Render Pipeline/Terrain/Lit"),Color.white);
            terrain.heightmapPixelError=3;terrain.drawInstanced=true;
        }
        static void MoveEcology(Scene scene)
        {
            var ecology=Find(scene,"05 - purple ecology framing open sightlines");
            if(ecology==null)return;
            foreach(Transform item in ecology.transform.Cast<Transform>().ToArray())
            {
                var renderers=item.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0)continue;
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                // Environment prefab pivots are offset; reservations use their visible centre.
                var p=bounds.center;
                if(p.x>-13&&p.x<8&&p.z>-33&&p.z<-16) { Object.DestroyImmediate(item.gameObject);continue; }
                float height=HeightAt(p.x,p.z);
                if(height<.6f) { Object.DestroyImmediate(item.gameObject);continue; }
                item.position+=Vector3.up*(height-1);
            }
        }
        static Transform Group(string name) { var t=new GameObject(name).transform;t.SetParent(root,false);return t; }
        static GameObject Shape(string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material mat,Transform parent,bool solid=true)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,true);go.transform.position=pos;go.transform.localScale=scale;
            var parentScale=parent.lossyScale;
            go.transform.localScale=new Vector3(scale.x/parentScale.x,scale.y/parentScale.y,scale.z/parentScale.z);
            go.layer=GameLayers.Ground;go.GetComponent<Renderer>().sharedMaterial=mat;
            var collider=go.GetComponent<Collider>();
            if(!solid)Object.DestroyImmediate(collider);
            else if(type==PrimitiveType.Cylinder)
            {Object.DestroyImmediate(collider);var c=go.AddComponent<MeshCollider>();c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;c.convex=true;}
            return go;
        }
        static GameObject Box(string name,Vector3 p,Vector3 s,Material mat,Transform parent,bool solid=true)
            =>Shape(name,PrimitiveType.Cube,p,s,mat,parent,solid);
        static void BuildTrenches()
        {
            var group=Group("Three marked fatal trenches");
            for(int t=0;t<Trenches.Length;t++)for(int i=1;i<Trenches[t].Length;i++)
            {
                var a=Trenches[t][i-1];var b=Trenches[t][i];var delta=b-a;var center=(a+b)*.5f;
                var go=new GameObject("Fatal rift "+t+" segment "+i);go.transform.SetParent(group);
                go.transform.position=V(center.x,-3.5f,center.y);go.transform.rotation=Quaternion.LookRotation(V(delta.x,0,delta.y));
                var box=go.AddComponent<BoxCollider>();box.isTrigger=true;box.size=V(6,6.4f,delta.magnitude+1.2f);go.AddComponent<AstraFatalTrench>();
                var side=new Vector2(-delta.y,delta.x).normalized;
                for(int k=0;k<=6;k++)for(int sign=-1;sign<=1;sign+=2)
                {
                    var p=Vector2.Lerp(a,b,k/6f)+side*sign*4.35f;
                    var marker=Box("Broken red rim warning",V(p.x,HeightAt(p.x,p.y)+.045f,p.y),V(.75f,.055f,.13f),red,group,false);
                    marker.transform.rotation=go.transform.rotation;
                }
            }
        }
        static void BuildPlayground()
        {
            var yard=Group("Physical playground and loose bridge planks");
            var fulcrum=Shape("Seesaw fulcrum",PrimitiveType.Cylinder,V(-2,1.57f,-25),V(1.1f,.57f,1.1f),metal,yard);
            var beam=Box("Long physical seesaw",V(-2,2.36f,-25),V(12,.26f,1.65f),wood,yard);
            var body=Body(beam,45f);
            var joint=beam.AddComponent<HingeJoint>();joint.axis=Vector3.forward;joint.anchor=Vector3.zero;
            joint.autoConfigureConnectedAnchor=false;joint.connectedAnchor=beam.transform.position;
            joint.useLimits=true;joint.limits=new JointLimits{min=-18,max=18};
            joint.useSpring=true;joint.spring=new JointSpring{spring=7,damper=4,targetPosition=0};
            Grabbable.Mark(beam,"긴 시소 · 끝에 올라서거나 잡아 눌러 보세요",false,1,1).PhysicsGrip=true;
            for(int sign=-1;sign<=1;sign+=2)
                Box("Seesaw end stripe",beam.transform.position+V(sign*5.2f,.14f,0),V(.4f,.025f,1.65f),yellow,beam.transform,false);
            Plank(yard,V(-5,1.3f,-18),9,Quaternion.Euler(0,13,0));
            Plank(yard,V(8,1.3f,20),8,Quaternion.Euler(0,65,0));
            Plank(yard,V(-31,1.3f,-14),8,Quaternion.Euler(0,-15,0));
            Plank(yard,V(33,HeightAt(33,4)+.3f,4),10,Quaternion.Euler(0,20,0));
        }
        static Rigidbody Body(GameObject go,float mass)
        {
            var body=go.AddComponent<Rigidbody>();body.mass=mass;body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.linearDamping=.16f;body.angularDamping=.18f;
            go.AddComponent<AstraV2CargoTool>();return body;
        }
        static void Plank(Transform group,Vector3 p,float length,Quaternion rotation)
        {
            var go=Box("Loose long plank "+p.x,p,V(length,.2f,1.15f),wood,group);go.transform.rotation=rotation;go.layer=GameLayers.Prop;
            Body(go,22f);Grabbable.Mark(go,"긴 널판지 · 잡아서 당기기",false,1,1).PhysicsGrip=true;
        }
        static void BuildTools()
        {
            var group=Group("Carryable rocket ink and yellow fuel cans");
            Vector3[] stations={V(-19,1,-29),V(10,1,5),V(34,HeightAt(34,-11),-11)};
            for(int i=0;i<stations.Length;i++)
            {
                var p=stations[i];
                Tool<AstraRocketTool>("Pocket rocket "+i,p+V(-1,.86f,0),group,cyan);
                var launcher=Tool<AstraInkLauncher>("Ink launcher "+i,p+V(0,.86f,0),group,ink);launcher.ConfigureInk(ink);
                Tool<AstraFuelCan>("Yellow fuel can "+i+" A",p+V(1,.86f,0),group,yellow);
                Tool<AstraFuelCan>("Yellow fuel can "+i+" B",p+V(2,.86f,0),group,yellow);
                Box("Tool shelf",p+V(.5f,.36f,0),V(4.8f,.16f,1.4f),metal,group);
                foreach(int side in new[]{-1,1})Box("Shelf foot",p+V(.5f+side*1.9f,.15f,0),V(.16f,.3f,1.2f),metal,group);
            }
        }
        static T Tool<T>(string name,Vector3 p,Transform group,Material accent) where T:AstraHandTool
        {
            var go=new GameObject(name);go.transform.SetParent(group);go.transform.position=p;go.layer=GameLayers.Prop;
            var collider=go.AddComponent<BoxCollider>();collider.center=V(0,-.015f,.16f);collider.size=V(.5f,.8f,.96f);
            var body=go.AddComponent<Rigidbody>();body.mass=6;body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var model=new GameObject("Model").transform;model.SetParent(go.transform,false);
            Box("Tool shell",p,V(.42f,.46f,.55f),ceramic,model,false);
            Box("Grip",p+V(0,-.28f,-.08f),V(.13f,.25f,.2f),metal,model,false);
            if(typeof(T)==typeof(AstraFuelCan))
            {
                Box("Yellow carry handle",p+V(0,.34f,0),V(.34f,.075f,.1f),yellow,model,false);
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                    Box("Yellow fuel outline vertical",p+V(x*.225f,0,z*.285f),V(.035f,.50f,.035f),yellow,model,false);
                foreach(int y in new[]{-1,1})
                {
                    foreach(int z in new[]{-1,1})Box("Yellow fuel outline horizontal",p+V(0,y*.24f,z*.285f),V(.48f,.035f,.035f),yellow,model,false);
                    foreach(int x in new[]{-1,1})Box("Yellow fuel outline depth",p+V(x*.225f,y*.24f,0),V(.035f,.035f,.60f),yellow,model,false);
                }
            }
            else
            {
                var barrel=Shape("Nozzle",PrimitiveType.Cylinder,p+V(0,.03f,.39f),V(.23f,.25f,.23f),metal,model,false);
                barrel.transform.rotation=Quaternion.Euler(90,0,0);
                Shape("Energy capsule",PrimitiveType.Sphere,p+V(0,.28f,0),V(.3f,.25f,.43f),accent,model,false);
                Box("Tool accent",p+V(0,0,-.29f),V(.42f,.2f,.025f),accent,model,false);
            }
            var muzzle=new GameObject("Muzzle").transform;muzzle.SetParent(go.transform,false);muzzle.localPosition=V(0,0,.64f);
            var tool=go.AddComponent<T>();tool.ConfigureModel(model);
            string label=typeof(T)==typeof(AstraRocketTool)?"휴대 로켓":typeof(T)==typeof(AstraFuelCan)?"노란 연료통":"먹물 발사기";
            Grabbable.Mark(go,label,false,1,2,GrabbableRole.Tool);
            return tool;
        }
        static void ConfigureFuel(Scene scene)
        {
            var part=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SpaceshipPart>(true)).Single(p=>p.name=="Part_4");
            var tank=part.gameObject.AddComponent<AstraFuelTank>();
            var display=new GameObject("Yellow fuel port and gauge").transform;display.SetParent(part.transform,false);display.localPosition=V(0,0,-.9f);
            var bar=Box("Fuel level",display.position+V(0,.1f,0),V(.02f,.1f,.06f),yellow,display,false).transform;
            var text=new GameObject("Fuel status").AddComponent<TextMesh>();text.transform.SetParent(display,false);text.transform.localPosition=V(0,.28f,0);
            text.text="FUEL EMPTY";text.fontSize=42;text.characterSize=.045f;text.anchor=TextAnchor.MiddleCenter;text.color=Color.yellow;
            tank.ConfigureDisplay(bar,text);
        }
        static void Validate(Scene scene)
        {
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))throw new Exception("Missing script");
            if(all.Count(t=>t.GetComponent<Terrain>()!=null)!=1)throw new Exception("Terrain count");
            if(all.Count(t=>t.GetComponent<AstraRocketTool>()!=null)!=3||all.Count(t=>t.GetComponent<AstraInkLauncher>()!=null)!=3
                ||all.Count(t=>t.GetComponent<AstraFuelCan>()!=null)!=6)throw new Exception("Tool count");
            if(all.Count(t=>t.GetComponent<AstraFuelTank>()!=null)!=1||all.Count(t=>t.GetComponent<HingeJoint>()!=null)!=1)throw new Exception("Tank or seesaw missing");
            foreach(var road in Roads)for(int s=1;s<road.Length;s++)for(int i=0;i<=30;i++)
            {
                var p=Vector2.Lerp(road[s-1],road[s],i/30f);
                if(Mathf.Abs(HeightAt(p.x,p.y)-1)>.035f)throw new Exception("Terrain obstructs return path "+p);
            }
        }
        public static void ApplyAndRender() { Apply(); Render(); }
        public static void Render()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var previous=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);
            try
            {
                var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                camera.enabled=true;camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=400;camera.allowHDR=true;
                var map=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AuthoredMapSettings>()).First();map.ConfigureCamera(camera);
                Directory.CreateDirectory("Validation/AstraExpansion");
                Capture(camera,V(64,65,-76),V(0,2,2),"overview");
                Capture(camera,V(-26,7,-36),V(-3,2,-23),"playground-tools");
                Capture(camera,V(42,12,-19),V(29,-1,-2),"terrain-trench");
                Capture(camera,V(5,3,-10),V(8,45,75),"milky-way");
            }
            finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
        }
        static void Capture(Camera camera,Vector3 position,Vector3 look,string name)
        {
            camera.transform.position=position;camera.transform.LookAt(look);
            var rt=new RenderTexture(1600,1000,24);rt.Create();var previous=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt};
                for(int i=0;i<5;i++)UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=rt;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes("Validation/AstraExpansion/"+name+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
            }
            finally{RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;rt.Release();Object.DestroyImmediate(rt);}
        }
    }
}
