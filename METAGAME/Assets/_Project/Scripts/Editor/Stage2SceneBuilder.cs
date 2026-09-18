using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>Chapter 2 §§6–16: authored, editable blockout. Never overwrites an existing scene.</summary>
    public static class Stage2SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Stage2_Earth.unity";
        const string MaterialFolder = "Assets/_Project/Materials/Stage2";
        static readonly List<Stage2Target> Targets = new();
        static Material _concrete, _dark, _orange, _white, _blue, _green, _rust;

        [MenuItem("Not This Planet/Scenes/Create Stage2 Earth Draft")]
        public static void Create() => Build(false);
        // Used only while iterating on this generated draft in the isolated 603 worktree.
        public static void RefreshDraft() => Build(true);
        static void Build(bool refresh)
        {
            if (File.Exists(ScenePath) && !refresh) throw new InvalidOperationException("Stage2_Earth already exists; edit it without rebuilding.");
            Targets.Clear();
            _concrete = Material("Concrete", new Color(.25f, .3f, .3f));
            _dark = Material("Steel", new Color(.08f, .13f, .16f));
            _orange = Material("SafetyYellow", new Color(.95f, .6f, .12f));
            _white = Material("RocketIvory", new Color(.84f, .86f, .78f));
            _blue = Material("SupportBlue", new Color(.13f, .6f, .8f));
            _green = Material("HazardGreen", new Color(.48f, .66f, .19f));
            _rust = Material("RustedMetal", new Color(.48f, .25f, .14f));
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.38f, .45f, .44f);
                RenderSettings.ambientEquatorColor = new Color(.24f, .29f, .28f);
                RenderSettings.ambientGroundColor = new Color(.12f, .15f, .16f);
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(.27f, .34f, .32f); RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 160;
                var sun = new GameObject("Overcast late afternoon").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.4f; sun.color = new Color(1, .9f, .72f);
                sun.transform.rotation = Quaternion.Euler(38, -35, 0); sun.shadows = LightShadows.Soft;
                var root = new GameObject("Stage2 Earth Launch Yard").transform;
                var yard = root.gameObject.AddComponent<Stage2Yard>();
                var terrain = Group(root, "01 Ground and wayfinding", Vector3.zero);
                Box(terrain, "Foundation", new Vector3(0,-.5f,0), new Vector3(86,1,78), _concrete);
                for (int i = -4; i <= 4; i++)
                    Box(terrain, "Concrete expansion joint", new Vector3(i*9,.012f,0), new Vector3(.055f,.02f,76), _dark, false);
                for (int i = -4; i <= 4; i++)
                    Box(terrain, "Concrete expansion joint", new Vector3(0,.015f,i*9), new Vector3(84,.02f,.055f), _dark, false);
                Box(terrain, "Preparation route", new Vector3(-15,.025f,-19), new Vector3(42,.03f,2.5f), _blue, false);
                Box(terrain, "Assembly route", new Vector3(6,.026f,-8), new Vector3(2.5f,.03f,22), _orange, false);
                for (int i=0;i<4;i++)
                {
                    float angle = i*90;
                    var marker = Quaternion.Euler(0,angle,0)*new Vector3(0,.04f,26);
                    Label(terrain, "Sector " + i, marker + Vector3.up*.1f, "SECTOR 0" + (i+1), 1.4f, Quaternion.Euler(90,angle,0));
                }
                BuildPerimeter(root);
                var prep = Group(root, "02 Crash and preparation", new Vector3(-27,0,-18));
                Box(prep, "Safe equipment apron", new Vector3(0,.06f,0), new Vector3(17,.12f,12), _dark);
                var wreck = Group(prep,"Meteor damaged alien lander",new Vector3(-5,2,0));
                Primitive(wreck,"Broken hull",PrimitiveType.Sphere,Vector3.zero,new Vector3(6,2.5f,5),_rust);
                Box(wreck,"Exposed frame",new Vector3(2,0,0),new Vector3(1,3,3),_dark);
                for(int i=0;i<4;i++) Box(wreck,"Scattered panel",new Vector3(-2+i*1.6f,-1,-4),new Vector3(1,.2f,1.5f),_white);
                Label(prep,"Arrival story",new Vector3(0,3.5f,4),"02 / EARTH\nMETEOR IMPACT — REBUILD TO ESCAPE",.32f,Quaternion.Euler(0,180,0));
                ToolRack(prep,new Vector3(4,0,-1),YardTargetKind.Wrench,"수동 렌치 장착",false);
                ToolRack(prep,new Vector3(4,0,2),YardTargetKind.Sprayer,"제독 분사기 장착",true);
                var shower = Group(root,"03 Fixed decontamination shower",new Vector3(-17,0,-12));
                Box(shower,"Blue wash floor",new Vector3(0,.04f,0),new Vector3(4,.08f,4),_blue);
                Box(shower,"Shower post",new Vector3(-1.8f,1.7f,0),new Vector3(.2f,3.4f,.2f),_blue);
                Box(shower,"Shower head",new Vector3(0,3.3f,0),new Vector3(3.6f,.2f,1.2f),_blue);
                Label(shower,"Shower sign",new Vector3(0,3.8f,0),"DECONTAMINATION\nSOLO / SUPPORT",.25f,Quaternion.identity);
                Target(prep,"Recovery panel",new Vector3(6,1,-4),YardTargetKind.Recovery,0,"주변에 사람이 없는 미체결 파츠 회수",_blue);

                var launch = Group(root,"04 Launchpad",new Vector3(9,0,9));
                Primitive(launch,"Launch concrete",PrimitiveType.Cylinder,new Vector3(0,.13f,0),new Vector3(10,.13f,10),_dark);
                Primitive(launch,"Fixed rocket lower",PrimitiveType.Cylinder,new Vector3(0,1.1f,0),new Vector3(3,1,3),_white);
                for(int i=0;i<4;i++)
                {
                    var pos=Quaternion.Euler(0,i*90,0)*new Vector3(1.7f,.5f,0);
                    Box(launch,"Launch support",pos,new Vector3(.4f,1,.4f),_orange);
                }
                Target(launch,"Launch control",new Vector3(-5,1,-4),YardTargetKind.Launch,0,"발사 — 모두 집결 / 체결 / 와이어 분리 확인",_orange);
                Label(launch,"Assembly sign",new Vector3(-6,2,-4),"ASSEMBLY\n01 → 02 → 03",.35f,Quaternion.Euler(0,90,0));

                var crane = Group(root,"05 Fixed-axis crane",new Vector3(-8,0,5));
                Box(crane,"Foundation",new Vector3(0,.2f,0),new Vector3(5,.4f,5),_dark);
                for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
                    Box(crane,"Tower column",new Vector3(x*1.5f,10,z*1.5f),new Vector3(.35f,20,.35f),_orange);
                for(int i=1;i<10;i++) Box(crane,"Tower brace",new Vector3(0,i*2,0),new Vector3(3.5f,.15f,3.5f),_dark);
                var arm=Group(crane,"Rotating boom",Vector3.zero);
                Box(arm,"Boom",new Vector3(0,20,11),new Vector3(1.5f,1.2f,28),_orange);
                Box(arm,"Counterweight",new Vector3(0,20,-5),new Vector3(4,2,3),_dark);
                // Fixed control balcony provides a safe interface to the rotating crane.
                var balcony=Group(crane,"Fixed cockpit balcony",new Vector3(0,8,3.7f));
                Box(balcony,"Deck",Vector3.zero,new Vector3(5,.3f,5),_dark);
                Box(balcony,"Below sight obstruction",new Vector3(0,1,1.8f),new Vector3(4,1.5f,.6f),_orange);
                Box(balcony,"Cabin roof",new Vector3(0,3,0),new Vector3(5,.2f,5),_dark);
                var seat=Group(balcony,"Control position",new Vector3(0,.2f,0));
                Target(balcony,"Crane controls",new Vector3(0,1,1),YardTargetKind.Crane,0,"크레인 조종 / 교대",_orange);
                var hook=Group(root,"06 Crane hook",Stage2Rules.Hook(crane.position,0,17,5));
                Primitive(hook,"Hook weight",PrimitiveType.Sphere,Vector3.zero,new Vector3(.6f,.7f,.6f),_orange,false);
                var lift=Group(root,"07 Cockpit lift",new Vector3(-12,.2f,8.7f));
                Box(lift,"Lift floor",Vector3.zero,new Vector3(3.5f,.3f,3.5f),_blue);
                for(int side=-1;side<=1;side+=2)
                    Box(lift,"Lift side rail",new Vector3(0,.8f,side*1.7f),new Vector3(3.4f,1.2f,.12f),_blue);
                Box(lift,"Gate",new Vector3(1.7f,.9f,0),new Vector3(.15f,1.8f,3.4f),_orange);
                Box(root,"Upper lift gate",new Vector3(-10.15f,9,8.7f),new Vector3(.15f,1.8f,3.4f),_orange);
                Box(root,"Lower lift gate",new Vector3(-13.85f,1,8.7f),new Vector3(.15f,1.8f,3.4f),_orange);
                Target(lift,"Lift cabin control",new Vector3(-1.4f,1,0),YardTargetKind.Lift,0,"승강기 상승 / 하강",_blue);
                Target(root,"Lift ground call",new Vector3(-14.5f,1,8.7f),YardTargetKind.Lift,0,"승강기 호출 / 이동",_blue);
                Target(balcony,"Lift upper call",new Vector3(-2,1,0),YardTargetKind.Lift,0,"승강기 호출 / 이동",_blue);
                Box(root,"Lift upper bridge",new Vector3(-10.8f,8,8.7f),new Vector3(2.5f,.3f,3.5f),_dark);

                var bodies=new Rigidbody[3]; var sockets=new Transform[3]; var lids=new Transform[3];
                Vector3[] stores={new Vector3(-8,0,22),new Vector3(8,0,24),new Vector3(16,0,-1)};
                for(int i=0;i<3;i++)
                {
                    var container=Group(root,"08 Container " + (i+1),stores[i]);
                    Box(container,"Base",new Vector3(0,.15f,0),new Vector3(5,.3f,5),_dark);
                    Box(container,"Back wall",new Vector3(0,1.9f,2.4f),new Vector3(5,3.8f,.2f),_rust);
                    for(int side=-1;side<=1;side+=2) Box(container,"Side wall",new Vector3(side*2.4f,1.9f,0),new Vector3(.2f,3.8f,5),_rust);
                    lids[i]=Group(container,"Roof hinge",new Vector3(0,3.9f,2.5f));
                    Box(lids[i],"Opening roof",new Vector3(0,0,-2.5f),new Vector3(5,.2f,5),_rust);
                    Target(container,"Container latch",new Vector3(-2,1,-2.6f),YardTargetKind.Container,i,"컨테이너 " + (i+1) + " 상부 개방",_orange);
                    Label(container,"Container number",new Vector3(0,3,-2.6f),"MODULE 0"+(i+1),.3f,Quaternion.identity);
                    var part=Group(root,"09 Rocket module " + (i+1),stores[i]+Vector3.up*1.9f);
                    Primitive(part,"Pressure shell",PrimitiveType.Cylinder,Vector3.zero,new Vector3(2.8f,1.5f,2.8f),_white);
                    Primitive(part,"Orange identification band",PrimitiveType.Cylinder,new Vector3(0,.7f,0),new Vector3(2.86f,.18f,2.86f),i==1?_blue:_orange,false);
                    if(i==2) Primitive(part,"Nose cap",PrimitiveType.Sphere,new Vector3(0,1,0),new Vector3(2.78f,2,2.78f),_white,false);
                    // The large ring includes a reachable front handle connected to the top sling.
                    Target(part,"Lifting ring",new Vector3(0,.1f,-1.65f),YardTargetKind.Ring,i,"인양 연결 / 장력 해제 후 분리",_orange);
                    Box(part,"Sling",new Vector3(0,1.6f,0),new Vector3(.15f,.5f,.15f),_orange,false);
                    for(int b=0;b<3;b++)
                    {
                        float angle=(b-1)*48*Mathf.Deg2Rad;
                        Target(part,"Bolt " + b,new Vector3(Mathf.Sin(angle)*1.55f,-1.25f,-Mathf.Cos(angle)*1.55f),YardTargetKind.Bolt,i*3+b,"볼트 " + (b+1),_orange);
                    }
                    bodies[i]=part.gameObject.AddComponent<Rigidbody>(); bodies[i].mass=80; bodies[i].linearDamping=.4f; bodies[i].angularDamping=2;
                    bodies[i].collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    bodies[i].interpolation=RigidbodyInterpolation.Interpolate;
                    sockets[i]=Group(launch,"Assembly socket " + (i+1),new Vector3(0,3.6f+i*3,0));
                }

                var ladder=Group(root,"10 Portable A-frame work ladder",new Vector3(9,0,4.5f));
                var workPlatform=Group(ladder,"Platform",new Vector3(0,1,0));
                Box(workPlatform,"Work deck",Vector3.zero,new Vector3(3.4f,.25f,3.4f),_blue);
                for(int side=-1;side<=1;side+=2)
                {
                    for(int front=-1;front<=1;front+=2)
                        Box(ladder,"Telescoping rail",new Vector3(side*1.35f,.5f,front),new Vector3(.13f,1,.13f),_orange);
                    Box(workPlatform,"Side handrail",new Vector3(side*1.65f,1,0),new Vector3(.1f,.1f,3.3f),_orange);
                    for(int front=-1;front<=1;front+=2)
                        Box(workPlatform,"Guard post",new Vector3(side*1.65f,.55f,front*1.65f),new Vector3(.1f,1.1f,.1f),_orange);
                }
                Box(workPlatform,"Front handrail",new Vector3(0,1,1.65f),new Vector3(3.3f,.1f,.1f),_orange);
                for(int rung=0;rung<22;rung++) Box(ladder,"Step",new Vector3(0,.3f+rung*.3f,-1.4f),new Vector3(2.7f,.1f,.3f),_dark);
                Target(ladder,"Carry handle",new Vector3(1.7f,.6f,-1.6f),YardTargetKind.Ladder,0,"사다리 운반 / 설치 (점유 중 금지)",_orange);
                Target(ladder,"ClimbBottom",new Vector3(0,.6f,-1.8f),YardTargetKind.LadderClimb,0,"사다리 오르기",_blue);
                Target(ladder,"ClimbTop",new Vector3(0,1.6f,-1.4f),YardTargetKind.LadderClimb,0,"사다리 내려가기",_blue);

                var hazards=new BoxCollider[2];
                hazards[0]=GasZone(root,new Vector3(16,3.5f,10),new Vector3(9,7,12),0);
                hazards[1]=GasZone(root,new Vector3(10,7,9),new Vector3(8,4,8),1);
                var map=root.gameObject.AddComponent<AuthoredMapSettings>();
                map.playerSpawn=Group(prep,"Player spawn",new Vector3(0,.3f,-3));
                map.preserveSceneLighting=true; map.useCodeStarfield=false; map.flightHalfSize=new Vector2(42,38); map.maximumAltitude=28;
                var bootstrap=new GameObject("Stage2 Bootstrap").AddComponent<SpaceBootstrap>();
                bootstrap.buildLevel=false; bootstrap.starfield=false; bootstrap.gravity=new Vector3(0,-9.81f,0);
                bootstrap.gameObject.AddComponent<Stage2Bootstrap>().Configure(yard);
                var serialized=new SerializedObject(yard);
                Set(serialized,"_cranePivot",crane); Set(serialized,"_arm",arm); Set(serialized,"_hook",hook); Set(serialized,"_seat",seat);
                Set(serialized,"_lift",lift); Set(serialized,"_ladder",ladder); Set(serialized,"_launch",launch); Set(serialized,"_shower",shower);
                SetArray(serialized,"_parts",bodies); SetArray(serialized,"_sockets",sockets); SetArray(serialized,"_lids",lids);
                SetArray(serialized,"_gasZones",hazards); SetArray(serialized,"_targets",Targets.ToArray()); serialized.ApplyModifiedPropertiesWithoutUndo();
                var camera=new GameObject("Editor overview camera").AddComponent<Camera>();
                camera.transform.position=new Vector3(48,44,-53); camera.transform.LookAt(new Vector3(0,4,5));
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=RenderSettings.fogColor;
                root.gameObject.AddComponent<Stage2BlockoutPalette>().Apply();
                EditorSceneManager.SaveScene(scene,ScenePath);
                var scenes=EditorBuildSettings.scenes.ToList();
                if(scenes.All(s=>s.path!=ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
                EditorBuildSettings.scenes=scenes.ToArray(); AssetDatabase.SaveAssets();
                Debug.Log("[Stage2] Authored scene created: " + Targets.Count + " interactions, 3 modules, 9 bolts.");
            }
            finally { if(previous.IsValid()&&previous.isLoaded) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene,true); }
        }

        static BoxCollider GasZone(Transform root, Vector3 position, Vector3 size, int index)
        {
            var area=Group(root,"11 Gas hazard " + index,position);
            var collider=area.gameObject.AddComponent<BoxCollider>(); collider.size=size; collider.isTrigger=true;
            for(int side=-1;side<=1;side+=2)
            {
                Box(area,"Gas boundary edge",new Vector3(side*size.x*.5f,-size.y/2+.06f,0),new Vector3(.1f,.08f,size.z),_green,false);
                Box(area,"Gas boundary edge",new Vector3(0,-size.y/2+.06f,side*size.z*.5f),new Vector3(size.x,.08f,.1f),_green,false);
            }
            var cloud=Group(area,"Leak volume",Vector3.zero).gameObject.AddComponent<ParticleSystem>();
            var main=cloud.main; main.playOnAwake=false; main.loop=true; main.startLifetime=3;
            main.startSpeed=.15f; main.startSize=new ParticleSystem.MinMaxCurve(1.2f,2.5f); main.maxParticles=150;
            main.startColor=new Color(.45f,.65f,.17f,.2f);
            var shape=cloud.shape; shape.shapeType=ParticleSystemShapeType.Box; shape.scale=size;
            var emission=cloud.emission; emission.rateOverTime=25;
            string gasPath=MaterialFolder+"/M_Stage2_Gas.mat";
            var gas=AssetDatabase.LoadAssetAtPath<Material>(gasPath);
            if(gas==null)
            {
                gas=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                gas.SetColor("_BaseColor",new Color(.45f,.65f,.17f,.2f));
                gas.SetFloat("_Surface",1); gas.SetFloat("_SrcBlend",5); gas.SetFloat("_DstBlend",10); gas.SetFloat("_ZWrite",0);
                gas.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); gas.renderQueue=3000; AssetDatabase.CreateAsset(gas,gasPath);
            }
            cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial=gas;
            var tank=Primitive(root,"Ruptured toxic tank",PrimitiveType.Cylinder,new Vector3(25,2,8+index*7),new Vector3(4,2,4),_rust);
            Box(root,"Leaking feed pipe",new Vector3(20,1,8+index*7),new Vector3(8,.4f,.4f),_green);
            Label(root,"Hazard warning",new Vector3(22,3,6+index*7),"TOXIC / KEEP CLEAR",.23f,Quaternion.identity);
            return collider;
        }

        static void BuildPerimeter(Transform root)
        {
            var group=Group(root,"12 Ruined skyline and safety boundary",Vector3.zero);
            for(int i=0;i<16;i++)
            {
                float x=-37+i*5;
                Box(group,"Collapsed factory",new Vector3(x,3+i%4,34),new Vector3(3.5f,6+i%4*2,5),i%2==0?_rust:_dark);
                Box(group,"Broken roof",new Vector3(x,7+i%4,34),new Vector3(4,.4f,6),_dark).rotation=Quaternion.Euler(0,0,i%3*10);
            }
            Box(group,"West boundary",new Vector3(-43,2,0),new Vector3(1,4,78),_dark);
            Box(group,"East boundary",new Vector3(43,2,0),new Vector3(1,4,78),_dark);
            Box(group,"South boundary",new Vector3(0,2,-39),new Vector3(86,4,1),_dark);
            Box(group,"North boundary",new Vector3(0,2,39),new Vector3(86,4,1),_dark);
        }
        static void ToolRack(Transform parent,Vector3 position,YardTargetKind kind,string label,bool spray)
        {
            var rack=Group(parent,label,position);
            Box(rack,"Rack",new Vector3(0,.5f,0),new Vector3(1.4f,1,1),_dark);
            var handle=Target(rack,"Tool handle",new Vector3(0,1.2f,0),kind,0,label,spray?_blue:_white);
            Box(handle.transform,"Grip",new Vector3(0,-.35f,0),new Vector3(.12f,.6f,.12f),_dark,false);
            Box(handle.transform,spray?"Spray nozzle":"Wrench jaw",new Vector3(0,.25f,0),spray?new Vector3(.2f,.2f,.65f):new Vector3(.45f,.2f,.15f),spray?_blue:_white,false);
        }
        static Material Material(string name,Color color)
        {
            Directory.CreateDirectory(MaterialFolder);
            string path=MaterialFolder+"/M_Stage2_"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path); if(existing!=null)return existing;
            var value=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="M_Stage2_"+name,color=color};
            value.SetFloat("_Smoothness",.25f); AssetDatabase.CreateAsset(value,path); return value;
        }
        static Transform Group(Transform parent,string name,Vector3 local)
        { var t=new GameObject(name).transform; t.SetParent(parent,false); t.localPosition=local; return t; }
        static Transform Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material,bool collision=true) => Primitive(parent,name,PrimitiveType.Cube,position,scale,material,collision);
        static Transform Primitive(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material,bool collision=true)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material; if(!collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
        static Stage2Target Target(Transform parent,string name,Vector3 position,YardTargetKind kind,int index,string label,Material material)
        {
            var visual=Box(parent,name,position,Vector3.one*.4f,material,false);
            var target=visual.gameObject.AddComponent<Stage2Target>(); target.Configure(Targets.Count,kind,index,label); Targets.Add(target); return target;
        }
        static void Label(Transform parent,string name,Vector3 position,string text,float size,Quaternion rotation)
        {
            var root=Group(parent,name,position); root.localRotation=rotation;
            var mesh=root.gameObject.AddComponent<TextMesh>(); mesh.text=text; mesh.fontSize=64; mesh.characterSize=size*.25f;
            mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=TextAlignment.Center; mesh.color=new Color(.88f,.91f,.78f);
        }
        static void Set(SerializedObject so,string name,UnityEngine.Object value)=>so.FindProperty(name).objectReferenceValue=value;
        static void SetArray<T>(SerializedObject so,string name,T[] values) where T:UnityEngine.Object
        { var property=so.FindProperty(name); property.arraySize=values.Length; for(int i=0;i<values.Length;i++)property.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
    }
}

