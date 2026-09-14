using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Metagame.EditorTools
{
    /// <summary>Non-destructive Astra variant authoring, retaining production parts and hint materials.</summary>
    public static class Stage1AstraVer2Builder
    {
        public const string ScenePath = "Assets/Scenes/Stage1_Astra_ver2.unity";
        const string Output = "Assets/_Project/Art/Stage1AstraVer2";
        const string Art = "Assets/_Project/Art/Stage1Concept/Materials/";
        static Transform root;
        static Material rock, moss, ground, ceramic, metal, route;
        static Material[] colors;
        static PhysicsMaterial floorPhysics, rollerPhysics;
        static readonly List<Renderer> assemblyLights = new List<Renderer>();
        static readonly List<Vector3[]> roads = new List<Vector3[]>();

        [MenuItem("Metagame/Stage1/Create Astra ver2 (preserve original)")]
        public static void Build()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Astra ver2 already exists; refusing to overwrite authored edits.");
            EnsureFolder("Assets/Scenes"); EnsureFolder(Output);
            var previous = SceneManager.GetActiveScene();
            if (!AssetDatabase.CopyAsset(Stage1AstraIntegration.ScenePath, ScenePath))
                throw new InvalidOperationException("Could not copy source scene through AssetDatabase.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                var guide = all.Select(t => t.GetComponent<AssemblyGuidanceLight>()).First(c => c != null);
                var guideData = new SerializedObject(guide);
                var sourceColors = guideData.FindProperty("floorMaterials");
                colors = Enumerable.Range(0, sourceColors.arraySize)
                    .Select(i => (Material)sourceColors.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                var ready = (Material)guideData.FindProperty("completeMaterial").objectReferenceValue;
                var beams = all.Where(t => t.name.Contains("Module beacon") || t.name == "02 Tractor sky beacon").ToArray();
                var beamCopies = beams.Select(t => Object.Instantiate(t.gameObject)).ToArray();
                foreach (var beam in beamCopies) { SceneManager.MoveGameObjectToScene(beam, scene); beam.SetActive(false); }
                var parts = all.Select(t => t.GetComponent<SpaceshipPart>()).Where(p => p != null).OrderBy(p => p.name).ToArray();
                if (parts.Length != 4) throw new InvalidOperationException("Expected four source parts.");
                foreach(var part in parts) part.gameObject.AddComponent<AstraV2ManualPart>();
                var sourceBounces=all.Select(t=>t.GetComponent<Light>()).Where(l=>l!=null && l.type==LightType.Point).ToArray();
                var bounceCopies=sourceBounces.Select(l=>Object.Instantiate(l.gameObject)).ToArray();
                foreach(var bounce in bounceCopies) SceneManager.MoveGameObjectToScene(bounce,scene);
                root = new GameObject("Astra ver2 - four recovery routes").transform;
                guide.transform.SetParent(root, true);
                // Keep the component and original color references, replace its obsolete paths.
                foreach (Transform child in guide.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                var atmosphere = all.FirstOrDefault(t => t.name.StartsWith("08 · Atmosphere"));
                if (atmosphere != null) atmosphere.SetParent(null, true);
                foreach (var old in scene.GetRootGameObjects().Where(g => g.name.StartsWith("STAGE 01")
                    || g.name.StartsWith("Playable access") || g.name.StartsWith("Concept Play Area")).ToArray())
                    Object.DestroyImmediate(old);

                PrepareMaterials(); assemblyLights.Clear(); roads.Clear();
                BuildBasin(); BuildHub();
                var assembly = parts[0].GetComponentInParent<Transform>().root.GetComponentsInChildren<ShipAssembly>(true).First();
                assembly.transform.position = new Vector3(0, 1f, 0); assembly.Configure(assembly.transform);
                BuildOpen(parts[0]); BuildCliff(parts[1]); BuildPressureRoom(parts[2]); BuildRollingRoom(parts[3]);
                Road("A - amber return", new[] { V(-24,-20), V(-14,-12), V(-6,-5), V(0,0) }, colors[0]);
                Road("B - cyan return", new[] { V(-10.4f,25), V(-5,24), V(0,14), V(0,0) }, colors[1]);
                Road("C - orchid return", new[] { V(25,18), V(25,9), V(12,2), V(0,0) }, colors[2]);
                Road("D - lime return", new[] { V(16,-20), V(13.2f,-20), V(11,-12), V(5,-7), V(0,0) }, colors[3]);
                guide.Configure(assembly, assemblyLights.ToArray(), colors, ready);
                RelocateBeacons(beamCopies, parts);
                for(int i=0;i<bounceCopies.Length;i++)
                {
                    bounceCopies[i].transform.SetParent(root,true);
                    bounceCopies[i].transform.position=i<4?parts[i].transform.position+new Vector3(1.8f,2,-1.8f):new Vector3(0,3,0);
                    if(i<4) bounceCopies[i].GetComponent<Light>().color=colors[i].GetColor("_BaseColor");
                }
                PopulateEcology();
                var map = all.Select(t => t != null ? t.GetComponent<AuthoredMapSettings>() : null).First(m => m != null);
                map.playerSpawn.position = V(-24,-28) + Vector3.up * .1f;
                map.flightHalfSize = new Vector2(51,47);
                foreach (var duplicate in scene.GetRootGameObjects().Where(g => g.name.StartsWith("Player Spawn") && g.transform != map.playerSpawn))
                    duplicate.transform.position = map.playerSpawn.position;
                var equipment = root.root.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t=>t.name=="Equipment");
                if(equipment != null)
                {
                    int i=0;
                    foreach(Transform tool in equipment) tool.position = V(-28+(i%3)*1.4f,-26+(i++/3)*1.4f)+Vector3.up*.7f;
                }
                var camera = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
                if(camera != null) { camera.transform.position=new Vector3(67,72,-82); camera.transform.LookAt(new Vector3(0,2,4)); }
                EditorSceneManager.SaveScene(scene);
                var buildScenes = EditorBuildSettings.scenes.ToList();
                if(!buildScenes.Any(s=>s.path==ScenePath)) { buildScenes.Add(new EditorBuildSettingsScene(ScenePath,true)); EditorBuildSettings.scenes=buildScenes.ToArray(); }
                AssetDatabase.SaveAssets();
                ValidateScene(scene);
                WriteAudit(scene);
                Debug.Log("[AstraV2] CREATED " + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static Vector3 V(float x,float z) => new Vector3(x,1,z);
        static Transform Group(string name) { var go=new GameObject(name); go.transform.SetParent(root,false); return go.transform; }
        static void EnsureFolder(string path)
        { if(AssetDatabase.IsValidFolder(path)) return; var parent=path.Substring(0,path.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path)); }
        static Material Material(string name,Color color)
        {
            string path=Output+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat!=null) return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name}; mat.SetColor("_BaseColor",color); mat.SetFloat("_Smoothness",.22f);
            AssetDatabase.CreateAsset(mat,path); return mat;
        }
        static void PrepareMaterials()
        {
            rock=Material("Lavender cliff",new Color(.36f,.32f,.43f)); moss=Material("Moss terrace",new Color(.28f,.36f,.15f));
            ceramic=AssetDatabase.LoadAssetAtPath<Material>(Art+"Warm ceramic.mat");
            metal=AssetDatabase.LoadAssetAtPath<Material>(Art+"Midnight chassis.mat");
            route=Material("Cargo route",new Color(.46f,.43f,.37f));
            ground=new Material(AssetDatabase.LoadAssetAtPath<Material>(Art+"Ground — lilac mineral sand.mat"));
            ground.SetColor("_BaseColor",new Color(.36f,.39f,.30f));
            AssetDatabase.CreateAsset(ground,Output+"/MineralBasin.mat");
            floorPhysics=new PhysicsMaterial("Astra cargo surface"){dynamicFriction=.28f,staticFriction=.36f,bounciness=0,frictionCombine=PhysicsMaterialCombine.Minimum};
            rollerPhysics=new PhysicsMaterial("Astra roller contact"){dynamicFriction=.45f,staticFriction=.5f,bounciness=0,frictionCombine=PhysicsMaterialCombine.Average};
            AssetDatabase.CreateAsset(floorPhysics,Output+"/CargoSurface.physicMaterial");
            AssetDatabase.CreateAsset(rollerPhysics,Output+"/RollerContact.physicMaterial");
        }
        static GameObject Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material mat,Transform parent,bool solid=true)
        {
            var go=GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,true); go.transform.position=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=mat; go.layer=GameLayers.Ground;
            var collider=go.GetComponent<Collider>();
            // Unity's primitive cylinder has a capsule collider. Flattening it leaves an oversized
            // sphere-shaped collision volume; use the actual convex cylinder for pads and rollers.
            if(solid && type==PrimitiveType.Cylinder)
            {
                Object.DestroyImmediate(collider);
                var mesh=go.AddComponent<MeshCollider>(); mesh.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;
                mesh.convex=true; collider=mesh;
            }
            if(!solid) Object.DestroyImmediate(collider); else collider.sharedMaterial=floorPhysics;
            return go;
        }
        static GameObject Box(string name,Vector3 pos,Vector3 size,Material mat,Transform parent,bool solid=true) => Shape(name,PrimitiveType.Cube,pos,size,mat,parent,solid);
        static void BuildBasin()
        {
            var terrain=Group("01 - continuous mineral basin");
            Box("Walkable basin - flush cargo datum",new Vector3(0,-.5f,0),new Vector3(100,3,90),ground,terrain);
            for(int i=0;i<20;i++)
            {
                float x=-47+i*5f;
                Rock("North skyline",new Vector3(x,1,43),new Vector3(7,4+(i%3),6),terrain);
                Rock("South skyline",new Vector3(x,1,-43),new Vector3(7,2+(i%3),6),terrain);
            }
            for(int i=0;i<16;i++)
            {
                float z=-38+i*5f;
                Rock("West skyline",new Vector3(-48,1,z),new Vector3(5,3+(i%4),7),terrain);
                Rock("East skyline",new Vector3(48,1,z),new Vector3(5,3+(i%4),7),terrain);
            }
            Box("Boundary west",new Vector3(-51,12,0),new Vector3(1,24,94),rock,terrain).GetComponent<Renderer>().enabled=false;
            Box("Boundary east",new Vector3(51,12,0),new Vector3(1,24,94),rock,terrain).GetComponent<Renderer>().enabled=false;
            Box("Boundary north",new Vector3(0,12,47),new Vector3(102,24,1),rock,terrain).GetComponent<Renderer>().enabled=false;
            Box("Boundary south",new Vector3(0,12,-47),new Vector3(102,24,1),rock,terrain).GetComponent<Renderer>().enabled=false;
        }
        static void Rock(string name,Vector3 foot,Vector3 size,Transform parent)
        {
            Box(name+" stable collider",foot+Vector3.up*(size.y*.5f),size,rock,parent).GetComponent<Renderer>().enabled=false;
            Prop("PF_Stage1_CliffWall",foot,size,parent,false);
            Prop("PF_Stage1_FlatRock",foot+Vector3.up*(size.y-.22f),new Vector3(size.x,.22f,size.z),parent,false);
        }
        static void BuildHub()
        {
            var hub=Group("02 - assembly hub with original dynamic color logic");
            Shape("Flush assembly apron",PrimitiveType.Cylinder,new Vector3(0,.82f,0),new Vector3(16,.18f,16),ceramic,hub);
            for(int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16; var p=new Vector3(Mathf.Cos(angle)*7.1f,1.02f,Mathf.Sin(angle)*7.1f);
                var lamp=Box("Required part - assembly ring",p,new Vector3(.55f,.035f,.25f),colors[0],hub,false);
                lamp.transform.rotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0); assemblyLights.Add(lamp.GetComponent<Renderer>());
            }
            Sign("ASSEMBLY",new Vector3(0,1.04f,-4),hub,2f,true);
        }
        static void MovePart(SpaceshipPart part,Vector3 foot,Quaternion rotation)
        {
            part.transform.SetPositionAndRotation(foot,rotation); Physics.SyncTransforms();
            if(!part.TryGetHullBounds(out var b)) throw new InvalidOperationException("Missing hull "+part.name);
            part.transform.position+=Vector3.up*(foot.y+.025f-b.min.y); Physics.SyncTransforms();
            part.TryGetHullBounds(out b);
            int i=0;
            foreach(var pin in part.GetComponentsInChildren<PartClamp>(true))
            {
                float sx=(i%2==0?-1:1), sz=(i/2%2==0?-1:1); i++;
                pin.transform.SetPositionAndRotation(new Vector3(b.center.x+sx*(b.extents.x+.3f),foot.y,b.center.z+sz*(b.extents.z+.3f)),Quaternion.identity);
            }
        }
        static void BuildOpen(SpaceshipPart part)
        {
            var area=Group("A - open access and first manual push");
            Shape("Open recovery apron",PrimitiveType.Cylinder,new Vector3(-24,.88f,-20),new Vector3(11,.12f,11),route,area);
            MovePart(part,V(-24,-20),Quaternion.identity);
            Sign("A  /  1",new Vector3(-29,2,-18),area,1f);
            ToolSet(V(-29,-23),area);
        }
        static void BuildCliff(SpaceshipPart part)
        {
            var area=Group("B - jump ascent and braked gravity descent");
            Rock("Summit",new Vector3(-23,1,27),new Vector3(14,6.4f,14),area);
            Vector3[] ledges={new Vector3(-35,1.8f,9),new Vector3(-37,2.6f,12.4f),new Vector3(-34.8f,3.4f,15.8f),
                new Vector3(-37,4.2f,19.2f),new Vector3(-34.8f,5,22.6f),new Vector3(-33.7f,5.8f,26),new Vector3(-31.5f,6.6f,29)};
            for(int i=0;i<ledges.Length;i++)
            {
                var p=ledges[i]; var size=new Vector3(i==3?3.6f:2.8f,p.y-1,2.7f);
                Rock("Jump ledge "+(i+1),new Vector3(p.x,1,p.z),size,area);
                Box("Cyan landing edge",p+new Vector3(0,.025f,-.9f),new Vector3(1.4f,.04f,.16f),colors[1],area,false);
            }
            MovePart(part,new Vector3(-23,7.4f,27),Quaternion.identity);
            var deck=Box("Gravity cargo deck",new Vector3(-13.2f,7.2f,25),new Vector3(5.5f,.4f,5),metal,area);
            var body=deck.AddComponent<Rigidbody>(); body.isKinematic=true; body.interpolation=RigidbodyInterpolation.Interpolate;
            // Children use world dimensions, independently of the scaled primitive deck.
            var railRoot=new GameObject("Deck fittings").transform; railRoot.SetParent(area,true); railRoot.position=deck.transform.position;
            for(int side=-1;side<=1;side+=2)
            {
                Box("Guide rack",new Vector3(-13.2f+side*2.9f,4.8f,27.7f),new Vector3(.3f,8,.3f),metal,area);
                Box("Cargo side rail",new Vector3(-13.2f,8.05f,25+side*2.4f),new Vector3(5.4f,.18f,.16f),ceramic,railRoot);
            }
            var brake=Control("HOLD - lower / RELEASE - brake",new Vector3(-11.2f,8.15f,26.8f),railRoot,colors[1]);
            railRoot.SetParent(deck.transform,true);
            var release=Control("Release after all pins removed",new Vector3(-17.5f,8.25f,28.4f),area,colors[1]);
            var reset=Control("Return empty platform",new Vector3(-9,1.85f,28.5f),area,colors[1]);
            var signal=Box("Freight ready signal",new Vector3(-16.1f,8.3f,27.4f),Vector3.one*.3f,colors[0],area,false).GetComponent<Renderer>();
            var lift=area.gameObject.AddComponent<AstraV2FreightLift>();
            lift.Configure(body,release,brake,reset,part,new Vector3(-13.2f,.8f,25),signal,colors[0],colors[1]);
            ToolSet(V(-8,29),area);
            Sign("B  /  2",new Vector3(-37,2,7),area,1);
            Sign("HOLD TO LOWER\nRELEASE TO STOP",new Vector3(-11.3f,8.7f,27.2f),railRoot,.45f);
        }
        static void BuildPressureRoom(SpaceshipPart part)
        {
            var area=Group("C - align circuit reveal plate hold gate");
            Box("Chamber floor",new Vector3(25,.85f,24),new Vector3(12,.3f,12),route,area);
            Box("Back retaining wall",new Vector3(25,3.25f,30),new Vector3(13,4.5f,.8f),rock,area);
            Box("West retaining wall",new Vector3(18.6f,3.25f,24),new Vector3(.8f,4.5f,12),rock,area);
            Box("East retaining wall",new Vector3(31.4f,3.25f,24),new Vector3(.8f,4.5f,12),rock,area);
            Box("Sealed moss roof",new Vector3(25,5.5f,24),new Vector3(13.5f,.6f,13),moss,area);
            RoomExterior(area,new Vector3(25,1,24),12,12,4.5f,5.8f);
            Box("Front left",new Vector3(20.3f,3.25f,18),new Vector3(3.9f,4.5f,.7f),rock,area);
            Box("Front right",new Vector3(29.7f,3.25f,18),new Vector3(3.9f,4.5f,.7f),rock,area);
            Box("Gate lintel",new Vector3(25,4.9f,18),new Vector3(5.5f,1.1f,.9f),ceramic,area);
            MovePart(part,V(25,25),Quaternion.identity);
            var door=Box("Pressure gate - flush threshold",new Vector3(25,2.7f,18),new Vector3(5.5f,3.4f,.35f),metal,area);
            var body=door.AddComponent<Rigidbody>(); body.isKinematic=true; body.interpolation=RigidbodyInterpolation.Interpolate;
            var sensorGo=new GameObject("Door safety beam"); sensorGo.transform.SetParent(area); sensorGo.transform.position=new Vector3(25,2.7f,18);
            var sensor=sensorGo.AddComponent<BoxCollider>(); sensor.size=new Vector3(5.5f,3.4f,1.4f); sensor.isTrigger=true;
            var plate=Shape("Revealed pressure plate",PrimitiveType.Cylinder,new Vector3(21,.475f,14),new Vector3(1.6f,.125f,1.6f),colors[2],area).transform;
            for(int side=-1;side<=1;side+=2)
                Box("Footprint - a player must stand here",plate.position+new Vector3(side*.23f,.13f,0),new Vector3(.18f,.012f,.43f),metal,plate,false);
            var dials=new Grabbable[3]; var needles=new Transform[3]; var indicators=new Renderer[3];
            for(int i=0;i<3;i++)
            {
                float x=16+i*1.3f;
                Box("Circuit pedestal",new Vector3(x,1.6f,11),new Vector3(1.05f,1.2f,.5f),ceramic,area);
                dials[i]=Control("Circuit dial "+(i+1)+" - grab and release",new Vector3(x,2.05f,10.65f),area,colors[2]);
                var pivot=new GameObject("Rotating circuit needle").transform; pivot.SetParent(area); pivot.position=new Vector3(x,2.05f,10.4f);
                needles[i]=pivot;
                Box("Needle",pivot.position+Vector3.up*.18f,new Vector3(.09f,.38f,.06f),colors[2],pivot,false);
                var targetDir=Quaternion.Euler(0,0,-90*i)*Vector3.up;
                indicators[i]=Box("Target notch",pivot.position+targetDir*.47f,new Vector3(.18f,.18f,.08f),colors[0],area,false).GetComponent<Renderer>();
            }
            var puzzle=area.gameObject.AddComponent<AstraV2Puzzle>();
            puzzle.Configure(dials,needles,indicators,colors[0],colors[2],plate,body,sensor);
            Cable(new[]{new Vector3(16,1.03f,11.5f),new Vector3(19.8f,1.03f,11.5f),new Vector3(21,1.03f,14)},colors[2],area);
            Cable(new[]{new Vector3(21,1.03f,14),new Vector3(21,1.03f,17),new Vector3(22,1.03f,18)},colors[2],area);
            ToolSet(V(29,13),area);
            Sign("C  /  3",new Vector3(30,2,16),area,1);
            Sign("ALIGN  >  STAND  >  ROLL",new Vector3(18,2.8f,11.1f),area,.48f);
        }
        static void BuildRollingRoom(SpaceshipPart part)
        {
            var area=Group("D - low clearance cooperative rolling and turning pocket");
            Box("Rolling room floor",new Vector3(28,.85f,-25),new Vector3(10,.3f,10),route,area);
            Box("Rolling corridor floor",new Vector3(20,.85f,-20),new Vector3(8,.3f,3.2f),route,area);
            Box("Back wall",new Vector3(28,2.3f,-30),new Vector3(11,2.6f,.6f),rock,area);
            Box("East wall",new Vector3(33,2.3f,-25),new Vector3(.6f,2.6f,10),rock,area);
            Box("North wall",new Vector3(28,2.3f,-17.8f),new Vector3(11,2.6f,.6f),rock,area);
            Box("Blind west wall",new Vector3(23,2.3f,-26),new Vector3(.6f,2.6f,8),rock,area);
            Box("Corridor north",new Vector3(19.5f,2.3f,-18.1f),new Vector3(7.6f,2.6f,.6f),rock,area);
            Box("Corridor south",new Vector3(19.5f,2.3f,-21.9f),new Vector3(7.6f,2.6f,.6f),rock,area);
            Box("Low roof - upright part cannot pass",new Vector3(28,3.42f,-24),new Vector3(10.6f,.5f,12.6f),moss,area);
            Box("Corridor low roof",new Vector3(19.5f,3.42f,-20),new Vector3(8,.5f,4.4f),moss,area);
            RoomExterior(area,new Vector3(28,1,-24),10,12,2.6f,3.67f);
            for(int i=0;i<3;i++) Prop("PF_Stage1_BoulderCluster",new Vector3(16.5f+i*2.3f,3.6f,-20),new Vector3(3,.65f,4.7f),area,false);
            MovePart(part,V(28,-26),Quaternion.Euler(0,0,90));
            for(int i=0;i<4;i++) Box("Lime rolling direction",new Vector3(29-i*2.6f,1.015f,-20),new Vector3(.7f,.025f,.16f),colors[3],area,false);
            ToolSet(V(14,-24),area);
            Sign("D  /  4",new Vector3(16,2,-18),area,1);
            Sign("TURN TOGETHER",new Vector3(30,2.2f,-29.5f),area,.45f);
        }
        static Grabbable Control(string label,Vector3 pos,Transform parent,Material mat)
        {
            var go=Shape(label,PrimitiveType.Sphere,pos,Vector3.one*.5f,mat,parent);
            var grab=Grabbable.Mark(go,label,false,1,1); return grab;
        }
        static void ToolSet(Vector3 foot,Transform parent)
        {
            for(int i=0;i<3;i++)
            {
                var roller=Shape("Manual roller "+i,PrimitiveType.Cylinder,foot+new Vector3(0,.16f,i*.48f),new Vector3(.28f,1.5f,.28f),ceramic,parent);
                roller.transform.rotation=Quaternion.Euler(0,0,90); roller.layer=GameLayers.Prop;
                roller.GetComponent<Collider>().sharedMaterial=rollerPhysics;
                var rb=roller.AddComponent<Rigidbody>(); rb.mass=3; rb.interpolation=RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                Grabbable.Mark(roller,"운반 롤러 · 뒤에서 빼 앞에 놓기",false,1,1);
                roller.AddComponent<AstraV2CargoTool>();
            }
            var lever=Box("Manual lever bar",foot+new Vector3(0,.2f,2),new Vector3(3,.12f,.18f),metal,parent);
            lever.layer=GameLayers.Prop; var body=lever.AddComponent<Rigidbody>(); body.mass=8; body.interpolation=RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; Grabbable.Mark(lever,"지렛대 · 받침 위에 걸쳐 밀기",false,1,1).PhysicsGrip=true;
            lever.AddComponent<AstraV2CargoTool>();
            Shape("Lever fulcrum",PrimitiveType.Cylinder,foot+new Vector3(0,.15f,2),new Vector3(.45f,.15f,.45f),rock,parent);
        }
        static void Road(string name,Vector3[] points,Material color)
        {
            if(points[points.Length-1]==V(0,0))
            {
                var approach=points[points.Length-2]; approach.y=0;
                points[points.Length-1]=approach.normalized*7.8f+Vector3.up;
            }
            roads.Add(points); var group=Group(name);
            for(int i=1;i<points.Length;i++)
            {
                var delta=points[i]-points[i-1]; var mid=(points[i]+points[i-1])*.5f;
                var road=Box("Unbroken manual return surface",mid+Vector3.up*.007f,new Vector3(5.8f,.012f,delta.magnitude+.15f),route,group,false);
                road.transform.rotation=Quaternion.LookRotation(delta);
                var side=Vector3.Cross(Vector3.up,delta.normalized);
                int n=Mathf.CeilToInt(delta.magnitude/2f);
                for(int k=0;k<=n;k++)
                {
                    var p=Vector3.Lerp(points[i-1],points[i],k/(float)n);
                    for(int s=-1;s<=1;s+=2)
                    {
                        var marker=Box("Color route edge",p+side*(s*2.55f)+Vector3.up*.035f,new Vector3(.12f,.05f,.65f),color,group,false);
                        marker.transform.rotation=road.transform.rotation;
                    }
                }
            }
        }
        static void Cable(Vector3[] points,Material mat,Transform parent)
        {
            var go=new GameObject("Visible circuit connection - no blocking collider"); go.transform.SetParent(parent);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=mat; line.useWorldSpace=true;
            line.positionCount=points.Length; line.SetPositions(points); line.widthMultiplier=.065f;
        }
        static void Sign(string text,Vector3 pos,Transform parent,float scale,bool floor=false)
        {
            var go=new GameObject("Diegetic sign - "+text.Replace('\n',' ')); go.transform.SetParent(parent); go.transform.position=pos;
            go.transform.rotation=floor?Quaternion.Euler(90,0,0):Quaternion.identity;
            var label=go.AddComponent<TextMesh>(); label.text=text; label.fontSize=48; label.characterSize=.13f*scale;
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; label.color=new Color(.85f,.9f,.82f);
        }
        static GameObject Prop(string prefab,Vector3 foot,Vector3 size,Transform parent,bool solid)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Environment/Stage1/"+prefab+".prefab");
            if(asset==null) throw new InvalidOperationException("Missing environment prefab "+prefab);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);
            PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var renderers=go.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) throw new InvalidOperationException("No renderers in "+prefab);
            var bounds=renderers[0].bounds; foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(size.x/Mathf.Max(.01f,bounds.size.x),size.y/Mathf.Max(.01f,bounds.size.y),size.z/Mathf.Max(.01f,bounds.size.z)));
            bounds=renderers[0].bounds; foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            go.transform.position+=foot-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            if(solid) { var c=go.AddComponent<BoxCollider>(); c.center=go.transform.InverseTransformPoint(foot+Vector3.up*size.y*.5f); c.size=new Vector3(size.x/go.transform.lossyScale.x,size.y/go.transform.lossyScale.y,size.z/go.transform.lossyScale.z); }
            foreach(var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer=GameLayers.Ground;
            return go;
        }
        static void PopulateEcology()
        {
            var nature=Group("05 - purple ecology framing open sightlines");
            Vector3[] trees={V(-42,-32),V(-34,-10),V(-14,-31),V(2,-32),V(39,-30),V(39,-9),V(38,28),V(10,34),V(-7,38),new Vector3(-25,7.4f,32),V(-42,34),V(-42,1)};
            foreach(var p in trees) Prop("PF_Stage1_MushroomTree",p,new Vector3(5,5.8f,5),nature,true);
            Vector3[] islands={V(-22,0),V(-24,-8),V(-5,-23),V(10,29),V(38,4),V(38,-15),V(-8,11)};
            for(int i=0;i<islands.Length;i++)
            {
                var p=islands[i]; Prop("PF_Stage1_BoulderCluster",p,new Vector3(7,2.5f,5),nature,true);
                Prop("PF_Stage1_MushroomTree",p+new Vector3(1.5f,1.8f,.5f),new Vector3(4,4.5f,4),nature,false);
                for(int j=0;j<3;j++) Prop("PF_Stage1_PurpleFlowerSmall",p+new Vector3(-3+j*2,0,-3),new Vector3(1.3f,1,1.3f),nature,false);
            }
            var random=new System.Random(214);
            for(int i=0;i<110;i++)
            {
                var p=V((float)(random.NextDouble()*86-43),(float)(random.NextDouble()*76-38));
                if(p.x*p.x+p.z*p.z<100 || InReserved(p)) continue;
                float s=.7f+(float)random.NextDouble()*1.4f;
                Prop(i%3==0?"PF_Stage1_BoulderCluster":"PF_Stage1_PurpleFlowerSmall",p,new Vector3(s,s*.65f,s),nature,i%3==0);
            }
        }
        static bool InReserved(Vector3 p)
        {
            if(p.x < -8 && p.z>5 || p.x>13 && p.z>8 || p.x>12 && p.z < -14 || p.x < -15 && p.z < -14) return true;
            foreach(var line in roads) for(int i=1;i<line.Length;i++)
            {
                var d=line[i]-line[i-1]; float t=Mathf.Clamp01(Vector3.Dot(p-line[i-1],d)/d.sqrMagnitude);
                if(Vector3.Distance(p,line[i-1]+d*t)<4) return true;
            }
            return false;
        }
        static void RoomExterior(Transform parent,Vector3 center,float width,float depth,float height,float roofTop)
        {
            // Render skins sit outside the intentional simple collision hull, leaving the cargo opening clear.
            for(int side=-1;side<=1;side+=2)
                for(int i=0;i<4;i++)
                {
                    if(parent.name.StartsWith("D -") && side<0 && i>=2) continue;
                    Prop("PF_Stage1_CliffWall",center+new Vector3(side*(width*.5f+.5f),0,-depth*.35f+i*depth*.24f),new Vector3(1.3f,height,depth*.29f),parent,false);
                }
            for(int i=0;i<4;i++)
            {
                Prop("PF_Stage1_CliffWall",center+new Vector3(-width*.37f+i*width*.25f,0,depth*.5f+.4f),new Vector3(width*.28f,height,1.3f),parent,false);
                for(int j=0;j<3;j++)
                    Prop("PF_Stage1_BoulderCluster",new Vector3(center.x-width*.37f+i*width*.25f,roofTop-.12f,center.z-depth*.32f+j*depth*.32f),new Vector3(width*.33f,.85f,depth*.38f),parent,false);
            }
            Prop("PF_Stage1_PurpleFlowerSmall",new Vector3(center.x-2,roofTop+.3f,center.z),new Vector3(2,1.2f,2),parent,false);
        }
        static void RelocateBeacons(GameObject[] originals,SpaceshipPart[] parts)
        {
            var group=Group("06 - retained no-fog color sky beacons");
            for(int i=0;i<4;i++)
            {
                if(originals.Length==0) throw new InvalidOperationException("Source sky beacons missing.");
                var beam=i<originals.Length?originals[i]:Object.Instantiate(originals[0]);
                beam.transform.SetParent(group,true); beam.SetActive(true);
                var rs=beam.GetComponentsInChildren<Renderer>(true);
                var b=rs[0].bounds; foreach(var r in rs.Skip(1)) b.Encapsulate(r.bounds);
                var target=parts[i].transform.position;
                parts[i].TryGetHullBounds(out var hull);
                beam.transform.position+=new Vector3(target.x-b.center.x,hull.max.y+.1f-b.min.y,target.z-b.center.z);
                var beamMat=new Material(Resources.Load<Shader>("GuidanceNoFog"));
                var tint=colors[i].GetColor("_BaseColor");
                beamMat.SetColor("_BaseColor",tint); beamMat.SetColor("_EmissionColor",tint);
                beamMat.SetFloat("_EmissionIntensity",1.25f);
                AssetDatabase.CreateAsset(beamMat,Output+"/Part"+(i+1)+"Beacon.mat");
                foreach(var r in rs) r.sharedMaterial=beamMat;
                foreach(var c in beam.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                beam.name="Part_"+(i+1)+" - retained sky color beacon";
            }
        }
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            try { ValidateScene(scene); WriteAudit(scene); } finally { EditorSceneManager.CloseScene(scene,true); }
        }
        public static void BuildAndRender() { Build(); Render(); }
        [MenuItem("Metagame/Stage1/Render Astra ver2 previews")]
        public static void Render()
        {
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
                camera.enabled=true; camera.allowHDR=true; camera.farClipPlane=300;
                var map=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AuthoredMapSettings>()).First();
                map.ConfigureCamera(camera);
                Directory.CreateDirectory("Validation/AstraVer2");
                Capture(camera,new Vector3(64,70,-77),new Vector3(0,2,2),"overview");
                Capture(camera,new Vector3(-46,19,-3),new Vector3(-24,5,24),"cliff");
                Capture(camera,new Vector3(38,12,3),new Vector3(23,2,20),"pressure-room");
                Capture(camera,new Vector3(8,7,-31),new Vector3(25,2,-22),"rolling-room");
            }
            finally { EditorSceneManager.CloseScene(scene,true); if(previous.IsValid()&&previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }
        static void Capture(Camera camera,Vector3 position,Vector3 look,string name)
        {
            camera.transform.position=position; camera.transform.LookAt(look);
            var rt=new RenderTexture(1600,1000,24); rt.Create();
            var previous=RenderTexture.active; bool async=ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation=false;
            try
            {
                var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt};
                for(int i=0;i<5;i++) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=rt;
                var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                File.WriteAllBytes("Validation/AstraVer2/"+name+".png",image.EncodeToPNG()); Object.DestroyImmediate(image);
            }
            finally { RenderTexture.active=previous; ShaderUtil.allowAsyncCompilation=async; rt.Release(); Object.DestroyImmediate(rt); }
        }
        static void ValidateScene(Scene scene)
        {
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)) throw new InvalidOperationException("Missing scripts");
            var parts=all.Select(t=>t.GetComponent<SpaceshipPart>()).Where(p=>p!=null).ToArray();
            if(parts.Length!=4 || parts.Select(p=>p.name).Distinct().Count()!=4) throw new InvalidOperationException("Part identities lost");
            if(all.Count(t=>t.GetComponent<SpaceBootstrap>()!=null)!=1 || all.Count(t=>t.GetComponent<AssemblyGuidanceLight>()!=null)!=1) throw new InvalidOperationException("Guidance/bootstrap count");
            if(all.Count(t=>t.GetComponent<AstraV2Puzzle>()!=null)!=1 || all.Count(t=>t.GetComponent<AstraV2FreightLift>()!=null)!=1) throw new InvalidOperationException("Mechanisms missing");
            Physics.SyncTransforms();
            foreach(var part in parts)
            {
                if(!part.TryGetHullBounds(out var bounds)) throw new InvalidOperationException("Hull missing");
                foreach(var pin in part.GetComponentsInChildren<PartClamp>(true))
                    if(!Physics.Raycast(pin.transform.position+Vector3.up*.1f,Vector3.down,.35f,GameLayers.GroundMask)) throw new InvalidOperationException("Unsupported pin "+part.name);
            }
            Debug.Log("[AstraV2] VALID: four original parts; supported pins; original guidance; puzzle; lift; no missing scripts.");
        }
        static void WriteAudit(Scene scene)
        {
            var text=new System.Text.StringBuilder();
            foreach(var r in scene.GetRootGameObjects())
                foreach(var c in r.GetComponentsInChildren<MonoBehaviour>(true))
                    if(c!=null && !(c is Stage1GeneratedCollider)) text.AppendLine(c.GetType().Name+" | "+c.name+" | "+c.transform.position);
            Directory.CreateDirectory("Validation/AstraVer2"); File.WriteAllText("Validation/AstraVer2/composition.txt",text.ToString());
        }
    }
}
