using System.Collections.Generic;
using System.IO;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Metagame.EditorTools
{
    /// <summary>Authors an independent scene with editable objects. Never rewrites Stage1 or source prefabs.</summary>
    public static class PracticeLobbySceneBuilder
    {
        public const string ScenePath = LobbyTransition.PracticeScenePath;
        const string AssetDir = "Assets/_Project/Art/PracticeLobby";
        const string Props = "Assets/_Project/Prefabs/Props/Stage1/";
        static Mesh rounded;
        static Mesh structuralCube;
        static Material ivory, purple, navy, mint, pink, gold, floor, dark;
        static Transform root;

        [MenuItem("Tools/Not This Planet/연습 로비 새로 만들기")]
        public static void BuildFromMenu()
        {
            if (File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("연습 로비", "이미 있는 연습 씬은 덮어쓰지 않습니다. Assets/Scenes/PracticeLobby를 열어 수정해 주세요.", "확인");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void BuildBatch()
        {
            Build();
            CapturePreviews();
        }

        public static void Build()
        {
            if (File.Exists(ScenePath)) throw new System.InvalidOperationException("PracticeLobby already exists; refusing to overwrite.");
            Directory.CreateDirectory(AssetDir);
            AssetDatabase.Refresh();
            rounded = RoundedMesh();
            ivory = Mat("Porcelain", new Color(0.89f, 0.9f, 0.94f));
            purple = Mat("Lavender", new Color(0.48f, 0.32f, 0.69f));
            navy = Mat("Ink", new Color(0.075f, 0.105f, 0.22f));
            mint = Mat("MintLight", new Color(0.36f, 0.95f, 0.82f), 0.65f);
            pink = Mat("Peach", new Color(1f, 0.54f, 0.66f));
            gold = Mat("Butter", new Color(1f, 0.8f, 0.39f));
            floor = Mat("Deck", new Color(0.37f, 0.42f, 0.57f));
            dark = Mat("Space", new Color(0.016f, 0.023f, 0.07f), 0.1f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("PracticeLobby · 우주선 놀이터").transform;
            var architecture = Child("01 · Cabin", root);
            Shell(architecture);
            var lounge = Child("02 · Four seats & window", root);
            Lounge(lounge);
            var play = Child("03 · Practice stations", root);
            var batPoint = Batting(play);
            var weldPoint = Welding(play);
            var pinPoint = Anchors(play);
            var exit = Departure(root);
            var reset = ResetConsole(root);
            var spawn = Child("Player spawn", root);
            spawn.position = new Vector3(-8.5f, 0.05f, -1f);
            var map = root.gameObject.AddComponent<AuthoredMapSettings>();
            map.playerSpawn = spawn;
            map.flightHalfSize = new Vector2(12f, 10f);
            map.maximumAltitude = 4f;
            map.preserveSceneLighting = true;
            var boot = root.gameObject.AddComponent<PracticeLobbyBootstrap>();
            var so = new SerializedObject(boot);
            Reference(so, "departure", exit);
            Reference(so, "resetConsole", reset);
            Reference(so, "battingStation", batPoint);
            Reference(so, "weldingStation", weldPoint);
            Reference(so, "anchorStation", pinPoint);
            so.ApplyModifiedPropertiesWithoutUndo();
            Lighting(root);
            var camera = new GameObject("Editor preview camera").AddComponent<Camera>();
            camera.transform.SetParent(root, false);
            camera.transform.position = new Vector3(-9.7f, 3.35f, -7.5f);
            camera.transform.LookAt(new Vector3(2.7f, 1.3f, 0.7f));
            camera.fieldOfView = 69f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.025f, 0.065f);
            ApplyStage1Gameplay(root.gameObject);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var entries = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!entries.Exists(e => e.path == ScenePath)) entries.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = entries.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[PracticeLobby] Created editable scene: " + ScenePath);
        }

        public static void SyncStage1GameplayBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var owner=Object.FindAnyObjectByType<PracticeLobbyBootstrap>();
            if(owner==null) throw new System.InvalidOperationException("Practice room owner not found.");
            ApplyStage1Gameplay(owner.gameObject);
            EditorSceneManager.SaveScene(owner.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[PracticeLobby] Stage1 gameplay/settings/tools/clamp synchronized. Cabin gravity=-9.81.");
        }

        static void ApplyStage1Gameplay(GameObject room)
        {
            var targetScene=room.scene;
            var sourceScene=EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity",OpenSceneMode.Additive);
            try
            {
                SpaceBootstrap source=null;
                var sourceTools=new List<ToolBase>();
                PartClamp sourceClamp=null;
                foreach(var sourceRoot in sourceScene.GetRootGameObjects())
                {
                    if(source==null) source=sourceRoot.GetComponentInChildren<SpaceBootstrap>(true);
                    sourceTools.AddRange(sourceRoot.GetComponentsInChildren<ToolBase>(true));
                    if(sourceClamp==null) sourceClamp=sourceRoot.GetComponentInChildren<PartClamp>(true);
                }
                if(source==null || sourceClamp==null) throw new System.InvalidOperationException("Stage1 gameplay or clamp is missing.");
                var shared=room.GetComponent<SpaceBootstrap>() ?? room.AddComponent<SpaceBootstrap>();
                EditorUtility.CopySerialized(source,shared);
                shared.gravity=new Vector3(0,-9.81f,0);
                shared.buildLevel=false;
                shared.starfield=false;
                // Indoor practice has no outdoor atmosphere hazards; gameplay/audio settings stay shared.
                var bootstrapSettings=new SerializedObject(shared);
                bootstrapSettings.FindProperty("atmosphereProfile").objectReferenceValue=null;
                bootstrapSettings.ApplyModifiedPropertiesWithoutUndo();
                var practiceSettings=new SerializedObject(room.GetComponent<PracticeLobbyBootstrap>());
                Reference(practiceSettings,"gameplay",shared);
                practiceSettings.ApplyModifiedPropertiesWithoutUndo();

                foreach(var tool in room.GetComponentsInChildren<ToolBase>(true))
                {
                    var original=sourceTools.Find(candidate=>candidate.GetType()==tool.GetType());
                    if(original==null) throw new System.InvalidOperationException("Missing Stage1 tool: "+tool.GetType().Name);
                    CloneStageObject(original.gameObject,tool.gameObject,targetScene);
                }
                foreach(var clamp in room.GetComponentsInChildren<PartClamp>(true))
                {
                    var part=clamp.GetComponentInParent<SpaceshipPart>();
                    var clone=CloneStageObject(sourceClamp.gameObject,clamp.gameObject,targetScene);
                    var binding=new SerializedObject(clone.GetComponent<PartClamp>());
                    Reference(binding,"_part",part);
                    binding.ApplyModifiedPropertiesWithoutUndo();
                }
                FitStationsToPlayer(room);
            }
            finally
            {
                EditorSceneManager.CloseScene(sourceScene,true);
                SceneManager.SetActiveScene(targetScene);
            }
        }

        static GameObject CloneStageObject(GameObject source,GameObject previous,Scene scene)
        {
            var parent=previous.transform.parent;
            var position=previous.transform.position;
            var rotation=previous.transform.rotation;
            var clone=Object.Instantiate(source);
            clone.transform.localScale=source.transform.lossyScale;
            clone.name=previous.name;
            SceneManager.MoveGameObjectToScene(clone,scene);
            clone.transform.SetParent(parent,true);
            clone.transform.SetPositionAndRotation(position,rotation);
            Object.DestroyImmediate(previous);
            return clone;
        }

        // The production character is much shorter than a human-scale workbench.
        // Only practice furniture/targets move; tool meshes, grip poses and reach stay untouched.
        static void FitStationsToPlayer(GameObject room)
        {
            foreach(var item in room.GetComponentsInChildren<Transform>(true))
            {
                if(item.name=="Rounded worktop")
                    item.position=new Vector3(item.position.x,0.28f,item.position.z);
                if(item.name=="Table foot")
                {
                    item.position=new Vector3(item.position.x,0.14f,item.position.z);
                    item.localScale=new Vector3(item.localScale.x,0.28f,item.localScale.z);
                }
                if(item.name=="Happy meteor sandbag")
                {
                    item.position=new Vector3(3,0.95f,-6.4f);
                    item.GetComponent<ConfigurableJoint>().linearLimit=new SoftJointLimit{limit=3.6f};
                }
                if(item.name=="Mini rocket lower" || item.name=="Mini rocket upper")
                {
                    item.localScale=Vector3.one*0.65f;
                    var upper=item.name=="Mini rocket upper";
                    item.position=new Vector3(upper?3.078f:3f,upper?1.156f:0.62f,6.8f);
                }
            }
            foreach(var tool in room.GetComponentsInChildren<ToolBase>(true))
                PlaceOnSurface(tool.gameObject,tool is MeteorBat?new Vector3(0.7f,0.36f,-4.35f):new Vector3(0.4f,0.36f,4.8f));
            foreach(var pin in room.GetComponentsInChildren<PartClamp>(true))
                PlaceOnSurface(pin.gameObject,new Vector3(-4,0.10f,5.6f));
        }

        static void Shell(Transform parent)
        {
            Box("Structural floor", new(0,-0.35f,0), new(24,0.7f,20), navy, parent, true);
            for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                Box("Soft deck panel", new(x*4.55f,0.015f,z*3.75f), new(4.47f,0.08f,3.67f), floor, parent);
            Box("Rear hull", new(-12,2.5f,0), new(0.6f,5,20), ivory, parent, true);
            Box("Forward hull", new(12,2.5f,0), new(0.6f,5,20), ivory, parent, true);
            foreach (float z in new[] {-10f,10f})
            {
                Box("Cabin side", new(0,2.5f,z), new(24,5,0.6f), ivory, parent, true);
                Box("Purple skirting", new(0,0.5f,z*0.973f), new(24,0.7f,0.28f), purple, parent);
                Box("Ribbon light", new(0,3.85f,z*0.969f), new(23,0.08f,0.09f), mint, parent);
                for(int x=-9;x<=9;x+=6)
                    Box("Wall inset", new(x,2.55f,z*0.965f), new(4.5f,1.5f,0.14f), navy, parent);
            }
            Box("Ceiling", new(0,5.35f,0), new(24,0.5f,20), ivory, parent, true);
            foreach(float x in new[] {-8f,0f,8f})
            {
                Box("Ceiling rib", new(x,4.92f,0), new(0.4f,0.45f,19.6f), purple, parent);
                Box("Overhead soft light", new(x,4.75f,0), new(0.65f,0.08f,10), mint, parent);
            }
            // Center kept clear; inset dots suggest circulation without creating obstacles.
            for(int i=-7;i<=7;i+=2)
                Box("Inlaid walkway marker", new(i,0.065f,0), new(0.6f,0.025f,0.15f), mint, parent);
        }

        static void Lounge(Transform parent)
        {
            // An opaque space mural avoids transparency sorting and remains cheap in a small cabin.
            Box("Observation window frame", new(-11.55f,2.55f,0), new(0.4f,3.7f,13), purple,parent);
            Box("Observation window", new(-11.30f,2.55f,0), new(0.13f,3.25f,12.3f), dark,parent);
            Sphere("Lavender planet", new(-11.14f,2.8f,1.7f), new(0.12f,2.2f,2.2f), purple,parent);
            Sphere("Planet glow", new(-11.05f,3.2f,1.15f), new(0.04f,0.4f,0.7f), pink,parent);
            var random = new System.Random(529);
            for(int i=0;i<52;i++)
            {
                float z=(float)random.NextDouble()*11.5f-5.75f;
                float y=1.12f+(float)random.NextDouble()*2.8f;
                float s=i%7==0?0.055f:0.025f;
                Sphere("Window star",new(-11.06f,y,z),new(0.025f,s,s),mint,parent);
            }
            for(int i=0;i<4;i++)
            {
                float z=-4.2f+i*2.8f;
                Box("Seat pedestal "+(i+1),new(-9.75f,0.24f,z),new(1.35f,0.45f,1.65f),navy,parent,true);
                Box("Seat cushion",new(-9.6f,0.6f,z),new(1.25f,0.35f,1.45f),i%2==0?purple:pink,parent,true);
                Box("Seat back",new(-10.16f,1.14f,z),new(0.32f,1.15f,1.5f),ivory,parent,true);
                foreach(float side in new[]{-0.85f,0.85f})
                    Box("Rounded armrest",new(-9.6f,0.95f,z+side),new(1.1f,0.24f,0.22f),ivory,parent,true);
            }
            Sign("CREW LOUNGE / 04",new(-10.96f,4.25f,0),Quaternion.Euler(0,-90,0),0.19f,parent);
        }

        static Transform Batting(Transform parent)
        {
            var area = Child("A · Happy meteor",parent);
            var point = Child("Bat station focus",area); point.position = new(3,1.0f,-6.4f);
            Box("Pink practice mat",new(3,0.08f,-6.4f),new(6,0.10f,5),pink,area);
            var ceiling = Child("Ceiling suspension",area); ceiling.position = new(3,4.55f,-6.4f);
            Sphere("Suspension mount",ceiling.position,new(0.6f,0.2f,0.6f),gold,area);
            var ball=Sphere("Happy meteor sandbag",new(3,1.6f,-6.4f),Vector3.one*1.3f,purple,area);
            var collider=ball.AddComponent<SphereCollider>(); collider.radius=0.5f;
            ball.layer=GameLayers.Prop;
            var rb=ball.AddComponent<Rigidbody>(); rb.mass=5f; rb.linearDamping=0.35f; rb.angularDamping=1.8f;
            rb.interpolation=RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            var joint=ball.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor=false; joint.connectedAnchor=ceiling.position;
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Limited;
            joint.linearLimit=new SoftJointLimit{limit=2.9f};
            var face=Sphere("Happy visor",new(3,1.65f,-5.78f),new(0.82f,0.58f,0.09f),mint,area);
            face.transform.SetParent(ball.transform,true);
            foreach(float dx in new[]{-0.20f,0.20f})
            {
                var eye=Sphere("Smiling eye",new(3+dx,1.72f,-5.71f),new(0.095f,0.13f,0.035f),navy,area);
                eye.transform.SetParent(ball.transform,true);
            }
            var mouth=Box("Little smile",new(3,1.51f,-5.71f),new(0.23f,0.045f,0.025f),navy,area);
            mouth.transform.SetParent(ball.transform,true);
            var rope=Child("Soft tether",area).gameObject.AddComponent<LineRenderer>();
            rope.sharedMaterial=gold; rope.positionCount=2; rope.startWidth=rope.endWidth=0.055f;
            rope.numCapVertices=4; rope.SetPosition(0,ceiling.position); rope.SetPosition(1,rb.position+Vector3.up*0.5f);
            var target=ball.AddComponent<PracticeMeteorTarget>();
            var so=new SerializedObject(target);
            Reference(so,"body",rb); Reference(so,"ceilingAnchor",ceiling); Reference(so,"face",face.GetComponent<Renderer>()); Reference(so,"tether",rope);
            so.ApplyModifiedPropertiesWithoutUndo();
            Table(new(0.7f,0,-4.35f),new(2.2f,0.85f,1.05f),area);
            Tool("PF_Stage1_MeteorBat.prefab",new(0.7f,0.93f,-4.35f),area);
            Sign("01  /  BONK!",new(3,2.55f,-9.58f),Quaternion.Euler(0,180,0),0.28f,area);
            return point;
        }

        static Transform Welding(Transform parent)
        {
            var area=Child("B · Mini rocket workshop",parent);
            var point=Child("Weld station focus",area); point.position=new(3,1,6.3f);
            Box("Mint practice mat",new(3,0.08f,6.3f),new(6,0.1f,4.8f),mint,area);
            Table(new(3,0,6.8f),new(4.4f,0.82f,1.9f),area);
            var lower=Part("Mini rocket lower",new(3,1.26f,6.8f),new(1.35f,0.8f,1.2f),ivory,PartState.Placed,area);
            var upper=Part("Mini rocket upper",new(3.12f,2.08f,6.8f),new(1.35f,0.8f,1.2f),purple,PartState.Placed,area);
            var eye=Sphere("Toy rocket porthole",new(3.12f,2.10f,6.17f),new(0.42f,0.42f,0.08f),mint,area);
            eye.transform.SetParent(upper.transform,true);
            foreach(float dx in new[]{-0.72f,0.72f})
            {
                var fin=Box("Toy rocket fin",new(3+dx,1.3f,6.8f),new(0.32f,0.6f,0.9f),pink,area);
                fin.transform.SetParent(lower.transform,true);
            }
            Table(new(0.4f,0,4.8f),new(1.8f,0.85f,1.1f),area);
            Tool("PF_Stage1_WeldingPlasmaGun.prefab",new(0.4f,0.93f,4.8f),area);
            Sign("02  /  FIX & MIX",new(3,2.55f,9.58f),Quaternion.identity,0.25f,area);
            return point;
        }

        static Transform Anchors(Transform parent)
        {
            var area=Child("C · Anchor cargo",parent);
            var point=Child("Anchor station focus",area); point.position=new(-4,1,6f);
            Box("Butter practice mat",new(-4,0.08f,6.2f),new(4.5f,0.1f,5),gold,area);
            var cargo=Part("Anchor practice cargo",new(-4,0.80f,7f),new(1.5f,1.35f,1.5f),purple,PartState.Locked,area);
            var belt=Box("Cargo mint belt",new(-4,0.80f,6.22f),new(1.3f,0.15f,0.04f),mint,area);
            belt.transform.SetParent(cargo.transform,true);
            var pin=Prefab(Props+"PF_Stage1_AnchorPin.prefab",cargo.transform);
            pin.name="Practice anchor pin";
            PlaceOnSurface(pin,new(-4,0.10f,5.6f));
            var clamp=pin.GetComponent<PartClamp>() ?? pin.AddComponent<PartClamp>();
            foreach(var c in pin.GetComponentsInChildren<Collider>(true)) c.gameObject.layer=GameLayers.Interactable;
            var so=new SerializedObject(clamp); Reference(so,"_part",cargo); so.ApplyModifiedPropertiesWithoutUndo();
            Sign("03  /  PULL & PLAY",new(-4,2.55f,9.58f),Quaternion.identity,0.22f,area);
            // Extra light cargo teaches ordinary grabbing without changing spaceship part rules.
            var loose=Box("Loose toy cargo",new(-4,0.44f,3.5f),new(0.65f,0.65f,0.65f),pink,area,true);
            loose.layer=GameLayers.Prop; loose.AddComponent<Rigidbody>().mass=4f;
            Grabbable.Mark(loose,"작은 연습 화물");
            return point;
        }

        static Transform Departure(Transform parent)
        {
            var area=Child("04 · Departure airlock",parent);
            Box("Airlock frame",new(11.25f,2.1f,0),new(0.4f,4.2f,4.5f),purple,area,true);
            Box("Mint airlock",new(10.99f,2,0),new(0.13f,3.65f,3.9f),mint,area);
            Box("Door left",new(10.86f,2,-0.87f),new(0.13f,3.5f,1.68f),navy,area);
            Box("Door right",new(10.86f,2,0.87f),new(0.13f,3.5f,1.68f),navy,area);
            Box("Departure carpet",new(9,0.085f,0),new(3,0.08f,3.5f),mint,area);
            Sign("READY TO GO?",new(10.67f,3.8f,0),Quaternion.Euler(0,90,0),0.21f,area);
            var point=Child("Departure zone",area); point.position=new(9,1,0);
            return point;
        }

        static Transform ResetConsole(Transform parent)
        {
            var area=Child("05 · Reset console",parent);
            Box("Console base",new(-6,0.55f,-5.4f),new(1.1f,1.1f,0.85f),ivory,area,true);
            Box("Console display",new(-6,1.25f,-5.4f),new(1.5f,0.75f,0.35f),navy,area);
            Sign("RESET",new(-6,1.28f,-5.19f),Quaternion.identity,0.15f,area);
            Sphere("Reset button",new(-6,0.87f,-4.93f),new(0.28f,0.28f,0.10f),pink,area);
            var point=Child("Reset focus",area); point.position=new(-6,1,-4.5f);
            return point;
        }

        static void Table(Vector3 p,Vector3 size,Transform parent)
        {
            Box("Rounded worktop",p+Vector3.up*size.y,new(size.x,0.16f,size.z),ivory,parent,true);
            foreach(float dx in new[]{-1f,1f})
                Box("Table foot",p+new Vector3(dx*size.x*0.32f,size.y*0.5f,0),new(0.28f,size.y,0.65f),purple,parent,true);
        }
        static SpaceshipPart Part(string name,Vector3 p,Vector3 size,Material mat,PartState state,Transform parent)
        {
            // Collider and Rigidbody live on an unscaled root, just like the authored stage parts.
            var go=Child(name,parent).gameObject; go.transform.position=p;
            var visual=Box("Shell",p,size,mat,go.transform); visual.layer=GameLayers.Part;
            go.layer=GameLayers.Part;
            var collider=go.AddComponent<BoxCollider>(); collider.size=size;
            var rb=go.AddComponent<Rigidbody>(); rb.mass=PhysicsScale.PartMass;
            rb.interpolation=RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            var part=go.AddComponent<SpaceshipPart>();
            var so=new SerializedObject(part); so.FindProperty("_state").enumValueIndex=(int)state; so.ApplyModifiedPropertiesWithoutUndo();
            return part;
        }
        static void Tool(string file,Vector3 p,Transform parent)
        {
            var go=Prefab(Props+file,parent);
            PlaceOnSurface(go,p);
            if(go.GetComponent<ToolBase>()==null) throw new System.InvalidOperationException("Missing production tool: "+file);
        }
        static GameObject Prefab(string path,Transform parent)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset==null) throw new System.IO.FileNotFoundException(path);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent.gameObject.scene);
            go.transform.SetParent(parent,false);
            return go;
        }
        static void PlaceOnSurface(GameObject go,Vector3 p)
        {
            go.transform.position=p;
            var colliders=go.GetComponentsInChildren<Collider>();
            Physics.SyncTransforms();
            if(colliders.Length==0) return;
            var bounds=colliders[0].bounds;
            foreach(var c in colliders) bounds.Encapsulate(c.bounds);
            go.transform.position+=new Vector3(p.x-bounds.center.x,p.y-bounds.min.y,p.z-bounds.center.z);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        }
        static void Lighting(Transform parent)
        {
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(0.65f,0.69f,0.83f);
            RenderSettings.fog=false;
            RenderSettings.skybox=null;
            var sun=Child("Cabin key light",parent).gameObject.AddComponent<Light>();
            sun.type=LightType.Directional; sun.intensity=0.8f; sun.color=new Color(1f,0.91f,0.83f);
            sun.transform.rotation=Quaternion.Euler(65,-25,0); sun.shadows=LightShadows.Soft;
            foreach(float x in new[]{-6f,5f}) foreach(float z in new[]{-5f,5f})
            {
                var light=Child("Soft cabin fill",parent).gameObject.AddComponent<Light>();
                light.type=LightType.Point; light.transform.position=new(x,4.1f,z);
                light.range=13; light.intensity=4; light.color=new Color(0.83f,0.92f,1f); light.shadows=LightShadows.None;
            }
        }
        static Transform Child(string name,Transform parent)
        { var go=new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }
        static GameObject Box(string name,Vector3 p,Vector3 size,Material mat,Transform parent,bool collision=false)
        {
            var go=Child(name,parent).gameObject; go.transform.position=p; go.transform.localScale=size;
            go.AddComponent<MeshFilter>().sharedMesh=IsStructural(name)?StructuralMesh():rounded; go.AddComponent<MeshRenderer>().sharedMaterial=mat;
            if(collision) go.AddComponent<BoxCollider>();
            return go;
        }
        static bool IsStructural(string name) => name is "Structural floor" or "Rear hull" or "Forward hull" or "Cabin side" or "Ceiling";
        static Mesh StructuralMesh()
        {
            if(structuralCube!=null) return structuralCube;
            var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);
            structuralCube=temporary.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temporary);
            return structuralCube;
        }
        static GameObject Sphere(string name,Vector3 p,Vector3 size,Material mat,Transform parent)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name=name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent,false); go.transform.position=p; go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=mat; return go;
        }
        static void Sign(string text,Vector3 p,Quaternion r,float size,Transform parent)
        {
            var t=Child(text,parent); t.position=p; t.rotation=r;
            var mesh=t.gameObject.AddComponent<TextMesh>(); mesh.text=text; mesh.fontSize=72;
            mesh.characterSize=size*0.28f; mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=TextAlignment.Center;
            mesh.color=new Color(0.83f,1f,0.95f);
        }
        static Material Mat(string name,Color color,float emission=0)
        {
            var mat=new Material(AssetDatabase.LoadAssetAtPath<Shader>(AssetDir+"/PracticeCabin.shader")); mat.name=name;
            ConfigureCabinMaterial(mat,color,emission);
            AssetDatabase.CreateAsset(mat,AssetDir+"/"+name+".mat"); return mat;
        }
        static void ConfigureCabinMaterial(Material mat,Color color,float emission)
        {
            mat.shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetDir+"/PracticeCabin.shader");
            mat.SetColor("_BaseColor",color);
            mat.SetFloat("_Glow",emission);
        }
        static void Reference(SerializedObject so,string field,Object value) => so.FindProperty(field).objectReferenceValue=value;
        static Mesh RoundedMesh()
        {
            var vertices=new List<Vector3>(); var normals=new List<Vector3>(); var triangles=new List<int>();
            const int n=10; const float radius=0.14f;
            void Face(Vector3 normal,Vector3 u,Vector3 v)
            {
                int start=vertices.Count;
                for(int y=0;y<=n;y++) for(int x=0;x<=n;x++)
                {
                    var p=normal*0.5f+u*(x/(float)n-0.5f)+v*(y/(float)n-0.5f);
                    var q=new Vector3(Mathf.Clamp(p.x,-0.36f,0.36f),Mathf.Clamp(p.y,-0.36f,0.36f),Mathf.Clamp(p.z,-0.36f,0.36f));
                    var outward=(p-q).normalized; vertices.Add(q+outward*radius); normals.Add(outward);
                }
                for(int y=0;y<n;y++) for(int x=0;x<n;x++)
                {int a=start+y*(n+1)+x; triangles.AddRange(new[]{a,a+1,a+n+1,a+1,a+n+2,a+n+1});}
            }
            Face(Vector3.forward,Vector3.right,Vector3.up); Face(Vector3.back,Vector3.left,Vector3.up);
            Face(Vector3.right,Vector3.back,Vector3.up); Face(Vector3.left,Vector3.forward,Vector3.up);
            Face(Vector3.up,Vector3.right,Vector3.back); Face(Vector3.down,Vector3.right,Vector3.forward);
            var mesh=new Mesh{name="Rounded cabin panel"}; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,AssetDir+"/RoundedPanel.asset"); return mesh;
        }

        public static void CapturePreviews()
        {
            string directory=Path.GetFullPath("../.local-validation/lobby-preview"); Directory.CreateDirectory(directory);
            var camera=Object.FindAnyObjectByType<Camera>();
            Capture(camera,Path.Combine(directory,"cabin.png"));
            camera.transform.position=new Vector3(8,3.4f,-1.6f); camera.transform.LookAt(new Vector3(-5,1.5f,1.5f));
            Capture(camera,Path.Combine(directory,"lounge.png"));
            camera.transform.position=new Vector3(3,2.5f,-0.5f); camera.transform.LookAt(new Vector3(2,1.7f,6.7f));
            Capture(camera,Path.Combine(directory,"workshop.png"));
            // Preview camera poses are not saved back into the scene.
        }

        public static void RefinePresentationBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{AssetDir}))
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var color=mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor"):mat.GetColor("_Color");
                ConfigureCabinMaterial(mat,color,mat.name=="MintLight"?0.65f:0);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            foreach(var filter in Object.FindObjectsByType<MeshFilter>())
                if(IsStructural(filter.name)) filter.sharedMesh=StructuralMesh();
            foreach(var label in Object.FindObjectsByType<TextMesh>())
                if(Mathf.Abs(label.transform.position.z)>8f)
                {
                    var p=label.transform.position; p.y=2.55f; label.transform.position=p;
                }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            CapturePreviews();
        }
        static void Capture(Camera camera,string path)
        {
            var rt=new RenderTexture(1600,900,24); var previous=RenderTexture.active; var previousTarget=camera.targetTexture;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try { camera.targetTexture=rt; RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=rt}); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally {camera.targetTexture=previousTarget; RenderTexture.active=previous; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);}
        }
    }
}
