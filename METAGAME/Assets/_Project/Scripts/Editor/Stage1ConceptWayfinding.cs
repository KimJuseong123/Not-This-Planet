using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metagame.EditorTools
{
    /// <summary>Static walkable route guidance and physical pickup apron guards.</summary>
    public static class Stage1ConceptWayfinding
    {
        private const string RootName = "Module Runway Guides";
        private const string Materials = "Assets/_Project/Art/Stage1Concept/Materials/";
        private static Collider[] _surfaces;

        [MenuItem("Tools/NOT THIS PLANET/컨셉 맵/바닥 유도등 추가")]
        public static void Build()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Stage1ConceptPlayableBuilder.ScenePath)
                throw new InvalidOperationException("Open the concept playable scene outside Play mode first.");
            var amber = Load("Habitat amber");
            var cyan = Load("Ion cyan");
            var purple = Load("Signal orchid");
            var dark = Load("Midnight chassis");
            var green = AssetDatabase.LoadAssetAtPath<Material>(Materials + "Tractor signal lime.mat");
            if (!green)
            {
                green = new Material(cyan) { name = "Tractor signal lime" };
                green.SetColor("_BaseColor", new Color(.32f,.9f,.08f));
                green.SetColor("_EmissionColor", new Color(.48f,2.1f,.08f));
                AssetDatabase.CreateAsset(green,Materials + "Tractor signal lime.mat");
            }
            var ground = GameObject.Find("Basin · sculpted ground").GetComponent<Collider>();
            var oldAmber = amber;
            amber = Signal("Route amber", new Color(1f,.38f,.045f));
            cyan = Signal("Route cyan", new Color(.035f,.65f,1f));
            purple = Signal("Route orchid", new Color(.65f,.12f,.95f));
            green = Signal("Route lime", new Color(.38f,.85f,.055f));
            if (!ground) throw new InvalidOperationException("Concept ground collider is required.");
            Undo.IncrementCurrentGroup();
            var undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add module runway guides");
            var previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (previous) Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add module runway guides");
            Physics.SyncTransforms();
            Ramp(root.transform,"Wayfinding hill deck transition",new Vector3(30.8f,7.1f,25.8f),new Vector3(29.6f,8.1f,24),3.8f,dark);
            Ramp(root.transform,"Wayfinding east assembly ramp",new Vector3(12,0,0),new Vector3(8,.84f,0),4f,dark);
            Physics.SyncTransforms();
            _surfaces = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == scene && (c == ground || c.name.Contains("walking trail") || c.name.Contains("cargo ramp") || c.name.StartsWith("Wayfinding ")
                    || c.name.Contains("entry stair") || c.name.EndsWith(" · deck") || c.name.EndsWith(" · center") || c.name.EndsWith(" · rim")
                    || c.name == "Cargo connector" || c.name == "Hangar slab" || c.name == "Hangar cargo exit threshold")).ToArray();
            // Progression numbers intentionally differ from Part_1..Part_4 assembly identifiers.
            Route(root.transform, "01 Amber · start to southwest ramp", amber, dark, ground,
                new[] { new Vector3(-20,0,-18), new Vector3(-21,0,-15), new Vector3(-20,0,-12), new Vector3(-17,0,-10),new Vector3(-13,0,-10),new Vector3(-10,0,-10), new Vector3(-6,.84f,-7) });
            Route(root.transform, "02 Lime · tractor south entrance", green, dark, ground,
                new[] {new Vector3(23,.8f,-24.8f),new Vector3(23,0,-27),new Vector3(20,0,-29),new Vector3(16,0,-28),new Vector3(12,0,-25),new Vector3(9,0,-21),new Vector3(4,0,-18),new Vector3(1,0,-15),new Vector3(0,0,-12),new Vector3(0,.8f,-9.5f)});
            Route(root.transform, "03 Cyan · long walkable hill route", cyan, dark, ground,
                new[] {new Vector3(29.6f,8.1f,24),new Vector3(30.8f,7.1f,25.8f),new Vector3(33,7.1f,28),new Vector3(43,5.4f,25),new Vector3(45,3.5f,10),new Vector3(40,1.7f,-2),new Vector3(28,0,-7),new Vector3(24,0,-9),new Vector3(19,0,-8),new Vector3(15,0,-5),new Vector3(13,0,-2),new Vector3(12,0,0),new Vector3(8,.84f,0)});
            // No floor line joins the elevated hangar apron to the central pad.
            var hangar = Group(root.transform, "04 Orchid · manual extraction ends at pickup");
            Lane(hangar, purple, dark, ground, new[] {new Vector3(-29,5.4f,25),new Vector3(-28,5.4f,29),new Vector3(-25,5.4f,31),new Vector3(-22,5.4f,31),new Vector3(-17.8f,5.4f,31),new Vector3(-18,5.6f,27),new Vector3(-14,5.8f,27),new Vector3(-11,5.9f,28),new Vector3(-9.5f,5.9f,29)}, true);
            Ring(hangar, new Vector3(-9.5f,5.94f,29), 3.7f, purple);
            Ring(root.transform, new Vector3(28,8.13f,19), 4.7f, cyan);
            PickupGuards(root.transform,purple,dark);
            // Recolor only the tractor station's existing amber signals, not the shared material.
            var station=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name.StartsWith("04 · SOUTHEAST"));
            foreach(var renderer in station.GetComponentsInChildren<Renderer>(true))
                if(renderer.sharedMaterial == oldAmber || (renderer.sharedMaterial && renderer.sharedMaterial.name == "Tractor signal lime")) { Undo.RecordObject(renderer,"Tractor signal color"); renderer.sharedMaterial=green; }
            foreach(var light in station.GetComponentsInChildren<Light>(true))
            { Undo.RecordObject(light,"Tractor signal color"); light.color=new Color(.4f,1,.1f); }
            Box(root.transform,"02 Tractor sky beacon",new Vector3(27,8.5f,-19),new Vector3(.09f,13,.09f),Quaternion.identity,green);
            // Remove the old amber trail from the hill and hangar to avoid contradictory guidance.
            var hill=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name.StartsWith("01 · Landscape"));
            foreach(var renderer in hill.GetComponentsInChildren<Renderer>(true))
                if(renderer.name=="Lamp · light" && renderer.sharedMaterial==oldAmber) { Undo.RecordObject(renderer,"Hill signal color"); renderer.sharedMaterial=cyan; }
            foreach(var renderer in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
                if(renderer.name=="Cargo route marking") { Undo.RecordObject(renderer,"Hide legacy cargo marks"); renderer.enabled=false; }
            ConfigureAssemblySignals(root, amber, cyan, purple, green);
            MatchStartBeacon();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject = root;
            Debug.Log("[Wayfinding] Four progression routes updated. Air shortcuts use pickup symbols only. Rails and two short deck transitions have collision.");
        }

        public static void MatchStartBeacon()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Stage1ConceptPlayableBuilder.ScenePath)
                throw new InvalidOperationException("Open the concept playable scene outside Play mode first.");
            var signal = Load("Route amber");
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
            {
                var p = renderer.transform.position;
                if (Mathf.Abs(p.x + 22.3f) > .2f || Mathf.Abs(p.z + 18f) > .2f) continue;
                if (renderer.name != "Module beacon · visual guide" && renderer.name != "Lamp · light") continue;
                Undo.RecordObject(renderer, "Match start beacon to route");
                renderer.sharedMaterial = signal;
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Material Signal(string name, Color color)
        {
            var path = Materials + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader) throw new InvalidOperationException("URP Unlit shader is missing.");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigureAssemblySignals(GameObject root, Material amber, Material cyan, Material purple, Material lime)
        {
            var assembly = UnityEngine.Object.FindFirstObjectByType<Metagame.Gameplay.Parts.ShipAssembly>();
            if (!assembly) throw new InvalidOperationException("AssemblyPoint is required.");
            var central = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .First(t => t.name.StartsWith("02 · CENTRAL"));
            // Move each existing bollard as one set, including its light/collider.
            var positions = central.GetComponentsInChildren<Renderer>()
                .Where(r=>r.name=="Lamp · ceramic body").Select(r=>r.transform.position).ToArray();
            foreach (var p in positions)
            {
                float quadrant = Mathf.Floor(Mathf.Repeat(Mathf.Atan2(p.x,p.z)*Mathf.Rad2Deg,360f)/90f);
                float angle = (quadrant*90f+67f)*Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Sin(angle)*11f-p.x,1.06f-p.y,Mathf.Cos(angle)*11f-p.z);
                foreach (Transform t in central)
                    if (t.name.StartsWith("Lamp") && Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(p.x,p.z)) < .15f)
                    { Undo.RecordObject(t,"Clear assembly cargo entrance"); t.position += offset; }
            }
            var lamps = central.GetComponentsInChildren<Renderer>()
                .Where(r=>r.name=="Lamp · light" || r.name.Contains("light strip") || r.name=="Ring · Assembly inner" || r.name=="Ring · Assembly outer").ToArray();
            foreach(var lamp in lamps) { Undo.RecordObject(lamp,"Assembly signal"); lamp.sharedMaterial=amber; }
            var signal = Undo.AddComponent<Metagame.Gameplay.Parts.AssemblyGuidanceLight>(root);
            var tractor = UnityEngine.Object.FindObjectsByType<Metagame.Gameplay.Parts.SpaceshipPart>(FindObjectsSortMode.None)
                .First(p=>p.name=="Part_4");
            signal.Configure(assembly,lamps,new[]{amber,cyan,purple,lime},Signal("Assembly ready",new Color(.7f,.9f,1f)),tractor);
        }

        private static Material Load(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat");
            if (!material) throw new InvalidOperationException("Missing material: " + name);
            return material;
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Vector3 Surface(Vector3 p, Collider ground)
        {
            RaycastHit hit;
            float height=float.NegativeInfinity;
            foreach (var floor in _surfaces)
                if (floor.Raycast(new Ray(new Vector3(p.x,50,p.z),Vector3.down),out hit,100)) height=Mathf.Max(height,hit.point.y);
            if(float.IsNegativeInfinity(height)) throw new InvalidOperationException("No walkable surface at " + p);
            p.y=height;
            return p + Vector3.up * .08f;
        }

        private static void Route(Transform root, string name, Material glow, Material dark, Collider ground, Vector3[] points)
        {
            var group = Group(root,name);
            Lane(group,glow,dark,ground,points,false);
        }

        private static void Lane(Transform parent, Material glow, Material dark, Collider ground, Vector3[] points, bool raised)
        {
            for(int segment=0; segment<points.Length-1; segment++)
            {
                Vector3 a=points[segment], b=points[segment+1];
                var direction=(b-a).normalized;
                direction.y=0;
                var rotation=Quaternion.LookRotation(direction);
                var side=Vector3.Cross(Vector3.up,direction).normalized;
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/4.5f));
                for(int i=0;i<count;i++)
                {
                    var center=Vector3.Lerp(a,b,(float)i/count);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        var p=Surface(center+side*(raised ? .8f : 1.15f)*sign,ground);
                        Box(parent,"Runway housing",p-Vector3.up*.035f,new Vector3(.18f,.035f,.48f),rotation,dark);
                        Box(parent,"Runway color",p-Vector3.up*.012f,new Vector3(.09f,.018f,.36f),rotation,glow);
                    }
                }
            }
        }

        private static void Gate(Transform parent, Vector3 center, Material glow, Material dark)
        {
            var direction=new Vector3(-center.x,0,-center.z).normalized;
            var side=Vector3.Cross(Vector3.up,direction);
            var group=Group(parent,glow.name+" · assembly receiving gate");
            for(int sign=-1;sign<=1;sign+=2)
            {
                var p=center+side*1.7f*sign;
                Box(group,"Gate pillar",p+Vector3.up*.8f,new Vector3(.35f,1.6f,.35f),Quaternion.identity,dark);
                Box(group,"Matching signal",p+Vector3.up*1.15f,new Vector3(.39f,.8f,.39f),Quaternion.identity,glow);
            }
        }

        private static void Ring(Transform parent, Vector3 center, float radius, Material glow)
        {
            for(int i=0;i<12;i++)
            {
                float angle=i*30*Mathf.Deg2Rad;
                Box(parent,"Pickup / departure marker",center+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius,
                    new Vector3(.65f,.04f,.13f),Quaternion.Euler(0,i*30,0),glow);
            }
        }

        private static void LiftArrow(Transform parent,Vector3 p,Material glow)
        {
            Box(parent,"Lift upward · shaft",p+Vector3.up*.7f,new Vector3(.12f,1.1f,.12f),Quaternion.identity,glow);
            for(int sign=-1;sign<=1;sign+=2)
                Box(parent,"Lift upward · head",p+new Vector3(sign*.2f,1.15f,0),new Vector3(.12f,.65f,.12f),Quaternion.Euler(0,0,sign*42),glow);
        }

        private static void Ramp(Transform parent,string name,Vector3 a,Vector3 b,float width,Material material)
        {
            var go=Solid(parent,name,(a+b)*.5f-Vector3.up*.18f,new Vector3(width,.36f,Vector3.Distance(a,b)+.4f),Quaternion.LookRotation(b-a),material);
            go.AddComponent<BoxCollider>();
        }

        private static void PickupGuards(Transform parent,Material glow,Material dark)
        {
            var group=Group(parent,"04 Pickup guards · open sky, west cargo entrance");
            // West/southwest entrance includes the capsule's full width while turning off the connector.
            for(int i=0;i<24;i++)
            {
                float angle=i*15*Mathf.Deg2Rad;
                if(i>=15 && i<=20) continue;
                var p=new Vector3(-9.5f,6.5f,29)+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*5.15f;
                var rotation=Quaternion.Euler(0,i*15,0);
                var go=Solid(group,"Pickup safety curb",p,new Vector3(1.42f,1.25f,.35f),rotation,dark);
                go.AddComponent<BoxCollider>();
                Box(group,"Pickup rim signal",p+Vector3.up*.66f,new Vector3(1.25f,.07f,.38f),rotation,glow);
            }
            // The connector remains open along x; side guards stop pushing cargo off its edges.
            for(int sign=-1;sign<=1;sign+=2)
            {
                // North-west stays open for the turn around the interior baffle.
                var go=Solid(group,"Connector side curb",new Vector3(sign>0 ? -13 : -15,6.25f,27+2.9f*sign),new Vector3(sign>0 ? 2 : 6,1.25f,.3f),Quaternion.identity,dark);
                go.AddComponent<BoxCollider>();
            }
            // Keep the pedestrian access open (1.6m), but too narrow for the capsule.
            var frontA=Solid(group,"Hangar front cargo stop west",new Vector3(-32,6.05f,20.75f),new Vector3(4.4f,1.25f,.35f),Quaternion.identity,dark);
            frontA.AddComponent<BoxCollider>();
            var frontB=Solid(group,"Hangar front cargo stop east",new Vector3(-21.8f,6.05f,20.75f),new Vector3(12.8f,1.25f,.35f),Quaternion.identity,dark);
            frontB.AddComponent<BoxCollider>();
            var sideA=Solid(group,"Hangar side cargo stop south",new Vector3(-15.7f,6.05f,22.4f),new Vector3(.35f,1.25f,3.3f),Quaternion.identity,dark);
            sideA.AddComponent<BoxCollider>();
            var sideB=Solid(group,"Hangar side cargo stop north",new Vector3(-15.7f,6.05f,31.7f),new Vector3(.35f,1.25f,3.4f),Quaternion.identity,dark);
            sideB.AddComponent<BoxCollider>();
        }

        private static GameObject Solid(Transform parent,string name,Vector3 p,Vector3 size,Quaternion rotation,Material material)
        {
            Box(parent,name,p,size,rotation,material);
            return parent.GetChild(parent.childCount-1).gameObject;
        }

        private static void Box(Transform parent,string name,Vector3 p,Vector3 size,Quaternion rotation,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=name;
            go.transform.SetParent(parent,false);
            go.transform.SetPositionAndRotation(p,rotation);
            go.transform.localScale=size;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            go.isStatic=true;
        }
    }
}
