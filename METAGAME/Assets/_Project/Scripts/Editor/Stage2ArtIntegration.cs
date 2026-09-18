using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Metagame.EditorTools
{
    public static partial class Stage2ArtIntegration
    {
        const string ScenePath="Assets/Scenes/Stage2_Earth.unity";
        const string Models="Assets/_Project/Models/Stage2/";
        const string Generated="Assets/_Project/Art/Stage2Earth";
        const string ArtRoot="Stage2 Earth Art - Reclaimed Launch Yard";
        static readonly string Output=Path.GetFullPath("../artifacts/stage2-art");
        static readonly List<string> Used=new();
        static readonly Dictionary<string,int> Counts=new();
        static Transform yardRoot;
        static System.Random random;
        static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
        static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name||t.name.StartsWith(name+".",StringComparison.Ordinal));
        static Transform Child(Transform root,string name){var t=root.Find(name);Require(t!=null,"Missing authored anchor: "+name);return t;}
        static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
        static Transform Group(Transform parent,string name,Vector3 position=default)
        {var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Stage2 art");go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;}
        static GameObject Model(string id,Transform parent,Vector3 position,float yaw=0,Vector3? scale=null,bool unpack=false,bool configureMaterials=true)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Models+"S2_"+id+".fbx");Require(asset!=null,"Missing model "+id);
            var frame=Group(parent,"Art "+id,position);
            frame.localRotation=Quaternion.Euler(0,yaw,0);frame.localScale=scale??Vector3.one;
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,frame);
            if(unpack)PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var c in instance.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            if(id=="Wall"){var wall=frame.gameObject.AddComponent<BoxCollider>();wall.center=new Vector3(0,1.2f,0);wall.size=new Vector3(4,2.4f,.4f);}
            foreach(var r in instance.GetComponentsInChildren<Renderer>(true))
            {r.SetPropertyBlock(null);r.shadowCastingMode=ShadowCastingMode.On;foreach(var m in r.sharedMaterials)if(configureMaterials&&m!=null&&!m.enableInstancing){m.enableInstancing=true;EditorUtility.SetDirty(m);}}
            Used.Add(id);Counts[id]=Counts.TryGetValue(id,out int count)?count+1:1;
            return frame.gameObject;
        }
        static void Ground(GameObject model,float y)
        {var bounds=Bounds(model);model.transform.position+=Vector3.up*(y-bounds.min.y);}
        static Bounds Bounds(GameObject obj)
        {var rs=obj.GetComponentsInChildren<Renderer>(true);Require(rs.Length>0,"No mesh "+obj.name);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static void Hide(Transform root)
        {foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))if(!r.transform.name.StartsWith("Art "))r.enabled=false;}
        static void Front(Transform model,Transform front)
        {var delta=front.position-model.position;if(Vector3.Dot(delta,model.forward)>0)model.localRotation*=Quaternion.Euler(0,180,0);}
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Models+"Materials/M_S2_"+name+".mat");
        static void Proxy(Transform t,string material)
        {var r=t.GetComponent<Renderer>();if(r!=null){r.enabled=true;r.sharedMaterial=Mat(material);r.SetPropertyBlock(null);}}

        [MenuItem("Not This Planet/Scenes/Integrate Stage2 Earth Art")]
        public static void Apply()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Exit Play Mode first.");
            var loaded=SceneManager.GetSceneByPath(ScenePath);
            Require(!loaded.IsValid()||!loaded.isDirty,"Stage2 has unsaved edits; save them before integration.");
            Directory.CreateDirectory(Output);Folder(Generated);
            if(!File.Exists(Output+"/Stage2_Earth.before.unity"))File.Copy(ScenePath,Output+"/Stage2_Earth.before.unity");
            var scene=loaded.IsValid()?loaded:EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Require(!scene.GetRootGameObjects().Any(g=>g.name==ArtRoot),"Art integration already exists; edit the authored scene instead of regenerating it.");
            yardRoot=scene.GetRootGameObjects().Single(g=>g.GetComponent<Stage2Yard>()!=null).transform;
            Used.Clear();Counts.Clear();random=new System.Random(603);
            int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Stage2 model and environment integration");
            var craneImporter=(ModelImporter)AssetImporter.GetAtPath(Models+"S2_Crane.fbx");
            bool wasReadable=craneImporter.isReadable;craneImporter.isReadable=true;craneImporter.SaveAndReimport();
            try
            {
                var palette=yardRoot.GetComponent<Stage2BlockoutPalette>();if(palette!=null)Undo.DestroyObjectImmediate(palette);
                foreach(var r in yardRoot.GetComponentsInChildren<Renderer>(true))r.SetPropertyBlock(null);
                var art=Group(null,ArtRoot);
                var equipment=Group(art,"01 Equipment dressing");
                DressEquipment(equipment);
                GroundAndLandscape(art);
                PerimeterAndRuins(art);
                Planting(art);
                Lighting(scene);
                HideDraftLabels(scene);
                AlignBoltTargets(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();Require(EditorSceneManager.SaveScene(scene),"Scene save failed.");
                File.WriteAllText(Output+"/placement-counts.json",JsonUtility.ToJson(new Placement{total=Used.Count,types=Counts.Count,
                    models=Counts.Select(p=>new Count{asset=p.Key,count=p.Value}).ToArray()},true));
                Undo.CollapseUndoOperations(group);
            }
            finally{craneImporter.isReadable=wasReadable;craneImporter.SaveAndReimport();}
            Validate();Render();
            Debug.Log("[Stage2Art] Integrated models and layered environment; authoritative targets preserved.");
        }
        [Serializable] sealed class Count{public string asset;public int count;}
        [Serializable] sealed class Placement{public int total,types;public Count[] models;}

        static void DressEquipment(Transform equipment)
        {
            var prep=Child(yardRoot,"02 Crash and preparation");
            var wreck=Child(prep,"Meteor damaged alien lander");Hide(wreck);
            var craft=Model("CrashedCraft",equipment,prep.position+new Vector3(-5,0,0),-25);Ground(craft,.13f);
            foreach(var t in prep.GetComponentsInChildren<Transform>().Where(t=>t.name=="Scattered panel"))t.GetComponent<Renderer>().enabled=false;
            Model("Rubble",equipment,prep.position+new Vector3(-8,0,-4),30,new Vector3(1.8f,.5f,1.4f));
            foreach(var target in yardRoot.GetComponentsInChildren<Stage2Target>().Where(t=>t.Kind==YardTargetKind.Wrench||t.Kind==YardTargetKind.Sprayer))
            {
                var p=target.transform.position;var rack=Model("ToolRack",equipment,new Vector3(p.x,0,p.z),180);
                Hide(target.transform.parent);
                Model(target.Kind==YardTargetKind.Wrench?"Wrench":"Sprayer",rack.transform,new Vector3(0,.65f,-.1f),0,Vector3.one*1.5f);
                target.GetComponent<Renderer>().enabled=false;
            }
            var extras=Model("FilterRack",equipment,new Vector3(-19,.12f,-23),180);
            Model("HelmetFilter",extras.transform,new Vector3(0,1.03f,.02f));
            // Optional filters are dressing only; the planning document does not adopt a new protection rule.
            foreach(var id in new[]{"BoltLoose","BoltTight","Nut","Bracket"})
                Model(id,equipment,new Vector3(-22+Array.IndexOf(new[]{"BoltLoose","BoltTight","Nut","Bracket"},id)*.7f,.16f,-15),180);
            var shower=Child(yardRoot,"03 Fixed decontamination shower");Hide(shower);Model("Shower",shower,Vector3.zero,180,Vector3.one*1.2f);
            foreach(var c in shower.GetComponentsInChildren<Collider>())if(c.name!="Blue wash floor")c.enabled=false;
            var launch=Child(yardRoot,"04 Launchpad");
            foreach(Transform t in launch)if(t.GetComponent<Stage2Target>()==null&&t.GetComponent<TextMesh>()==null)Hide(t);
            var pad=Model("Launchpad",launch,new Vector3(0,-.32f,0),0,new Vector3(1.6f,1,1.6f));
            Model("RocketBase",launch,Vector3.zero,180);
            var crane=Child(yardRoot,"05 Fixed-axis crane");Hide(crane);
            var rig=Model("Crane",crane,Vector3.zero,0,null,true).transform;
            var slew=Find(rig,"SlewPivot");var trolley=Find(rig,"Trolley");var hookModel=Find(rig,"HookHeight");
            Require(slew!=null&&trolley!=null&&hookModel!=null,"Crane model pivot missing");
            var forward=Vector3.ProjectOnPlane(trolley.position-slew.position,Vector3.up);
            rig.rotation=Quaternion.FromToRotation(forward.normalized,Vector3.forward)*rig.rotation;
            // Remove baked static cables: the existing yard renders the authoritative cable length.
            FilterMesh(Find(trolley,"Trolley_Mesh").GetComponent<MeshFilter>(),p=>p.y>18.45f,"CraneTrolley",crane);
            var rootMesh=Find(rig,"S2_Crane_Root_Mesh");
            if(rootMesh!=null)FilterMesh(rootMesh.GetComponent<MeshFilter>(),p=>p.y<5.9f||p.y>9.6f||new Vector2(p.x,p.z).magnitude<.85f,"CraneMast",crane);
            slew.SetParent(Child(crane,"Rotating boom"),true);
            var hook=Child(yardRoot,"06 Crane hook");Hide(hook);hook.GetComponentInChildren<Renderer>().sharedMaterial=Mat("Steel");
            hookModel.SetParent(hook,true);hookModel.position=hook.position;
            var balcony=Child(crane,"Fixed cockpit balcony");
            foreach(string n in new[]{"Deck","Below sight obstruction","Cabin roof"})Proxy(Child(balcony,n),n=="Below sight obstruction"?"Orange":"Teal");
            Model("Console",balcony,new Vector3(0,.15f,1.1f),180);
            for(int side=-1;side<=1;side+=2)Model("Rail",balcony,new Vector3(side*2.1f,.15f,0),90);
            var lift=Child(yardRoot,"07 Cockpit lift");Hide(lift);
            var elevator=Model("Elevator",equipment,new Vector3(lift.position.x,0,lift.position.z),90,null,true).transform;
            var cabin=Find(elevator,"LiftCabin");Require(cabin!=null,"Elevator cabin missing");cabin.SetParent(lift,true);
            var gate=Find(cabin,"CabinGate");if(gate!=null)gate.SetParent(Child(lift,"Gate"),true);
            Proxy(Child(yardRoot,"Lift upper bridge"),"Teal");Proxy(Child(yardRoot,"Upper lift gate"),"Orange");Proxy(Child(yardRoot,"Lower lift gate"),"Orange");
            var bolts=new List<Renderer>();
            for(int i=0;i<3;i++)
            {
                var container=Child(yardRoot,"08 Container "+(i+1));Hide(container);
                var box=Model("Container",container,Vector3.zero,0,null,true).transform;
                var lid=Find(box,"RoofHinge");Require(lid!=null,"Container lid missing");
                if((lid.position-container.position).z<0)box.localRotation=Quaternion.Euler(0,180,0);
                lid.SetParent(Child(container,"Roof hinge"),true);
                var part=Child(yardRoot,"09 Rocket module "+(i+1));Hide(part);
                var module=Model(new[]{"RocketTank","RocketService","RocketCapsule"}[i],part,new Vector3(0,-1.5f,0)).transform;
                Front(module,Find(module,"Bolt_2"));
                for(int b=1;b<=3;b++)bolts.Add(Find(module,"Bolt_"+b).GetComponentInChildren<Renderer>());
            }
            var ladder=Child(yardRoot,"10 Portable A-frame work ladder");Hide(ladder);
            var ladderFrame=Model("Ladder",ladder,Vector3.zero,180,null,true).transform;
            var platform=Find(ladderFrame,"UpperPlatform");Require(platform!=null,"Ladder platform missing");
            var legacyPlatform=Child(ladder,"Platform");platform.SetParent(legacyPlatform,true);
            platform.position=legacyPlatform.position;
            // Exported pivot rotation can swap axes; an outer wrapper keeps scaling in Unity Y-up space.
            platform.localScale=Vector3.one;
            var platformScale=Group(legacyPlatform,"Art platform width");platform.SetParent(platformScale,true);platformScale.localScale=new Vector3(2,1,2);
            ladderFrame.localScale=new Vector3(1.55f,.25f,1.1f);
            var follower=yardRoot.gameObject.AddComponent<Stage2ArtMotion>();follower.Configure(yardRoot.GetComponent<Stage2Yard>(),trolley,hook,ladderFrame,bolts.ToArray());
            foreach(var t in yardRoot.GetComponentsInChildren<Transform>().Where(t=>t.name=="Ruptured toxic tank"||t.name=="Leaking feed pipe"))Hide(t);
            Model("GasTank",equipment,new Vector3(26,0,8),90);Model("GasTank",equipment,new Vector3(26,0,16),90);
            foreach(var p in new[]{new Vector3(-18,0,-22),new Vector3(-16,0,15),new Vector3(19,0,25),new Vector3(17,0,-10)})Model("Floodlight",equipment,p,180);
        }

        static void FilterMesh(MeshFilter filter,Func<Vector3,bool> keep,string name,Transform reference)
        {
            Require(filter!=null,"Missing crane mesh "+name);var mesh=Object.Instantiate(filter.sharedMesh);mesh.name=name;
            var vertices=mesh.vertices;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var triangles=mesh.GetTriangles(sub);var output=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {var point=(vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3;
                    point=reference.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if(keep(point)){output.Add(triangles[i]);output.Add(triangles[i+1]);output.Add(triangles[i+2]);}}
                mesh.SetTriangles(output,sub);
            }
            mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Generated+"/"+name+".asset");filter.sharedMesh=mesh;
        }

        static void GroundAndLandscape(Transform art)
        {
            var ground=Group(art,"02 Apron slabs and route markings");
            var old=Child(yardRoot,"01 Ground and wayfinding");
            foreach(Transform t in old)if(t.name.Contains("route")||t.name.Contains("expansion"))Hide(t);
            var foundation=Child(old,"Foundation").GetComponent<Renderer>();foundation.sharedMaterial=Mat("Concrete");
            for(int x=-36;x<=36;x+=6)for(int z=-30;z<=30;z+=6)
                Model("Slab",ground,new Vector3(x,-.135f,z),(x+z)%12==0?0:180,new Vector3(2.95f,1,2.95f));
            for(int x=-34;x<=6;x+=2)Model("RouteBlue",ground,new Vector3(x,-.112f,-19),90,new Vector3(1.25f,1,1));
            for(int z=-16;z<=4;z+=2)Model("RouteAmber",ground,new Vector3(6,-.108f,z),0,new Vector3(1.25f,1,1));
            for(int x=-34;x<=6;x+=6)for(int side=-1;side<=1;side+=2)Model("Bollard",ground,new Vector3(x,0,-19+side*2.1f));
            for(int z=-12;z<=0;z+=6)for(int side=-1;side<=1;side+=2)Model("Bollard",ground,new Vector3(6+side*2.4f,0,z));
            var landscape=Group(art,"03 Reclaimed earth outside apron");
            const int n=65;var v=new Vector3[n*n];var uv=new Vector2[v.Length];var indices=new List<int>();
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {float px=-128+x*4,pz=-128+z*4;float edge=Mathf.Max(Mathf.Abs(px)/43,Mathf.Abs(pz)/39);
                float h=edge<1.02f?-.18f:Mathf.SmoothStep(0,1,Mathf.Clamp01((edge-1)/.7f))*(1.8f+Mathf.PerlinNoise(px*.023f+3,pz*.023f+7)*6);
                v[z*n+x]=new Vector3(px,h-.2f,pz);uv[z*n+x]=new Vector2(px*.1f,pz*.1f);
                if(x<n-1&&z<n-1){int a=z*n+x;indices.AddRange(new[]{a,a+n,a+1,a+1,a+n,a+n+1});}}
            var mesh=new Mesh{name="Stage2 surrounding earth"};mesh.vertices=v;mesh.uv=uv;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,Generated+"/EarthSurround.asset");
            var go=Group(landscape,"Undulating earth berms").gameObject;go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var mat=new Material(Mat("Concrete")){name="Stage2 Earth Soil",enableInstancing=true};mat.SetColor("_BaseColor",new Color(.57f,.61f,.39f));
            AssetDatabase.CreateAsset(mat,Generated+"/EarthSoil.mat");go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }

        static void PerimeterAndRuins(Transform art)
        {
            var old=Child(yardRoot,"12 Ruined skyline and safety boundary");Hide(old);
            foreach(var collider in old.GetComponentsInChildren<Collider>())if(!collider.name.EndsWith("boundary",StringComparison.Ordinal))collider.enabled=false;
            var walls=Group(art,"04 Broken perimeter and reclaimed drainage");
            for(int x=-38;x<=38;x+=4)
            {Model("Wall",walls,new Vector3(x,0,36),180);if(x<-30||x>-18)Model("Wall",walls,new Vector3(x,0,-35));}
            for(int z=-31;z<=31;z+=4)
            {Model("Wall",walls,new Vector3(-40,0,z),90);Model("Wall",walls,new Vector3(40,0,z),-90);}
            for(int z=-28;z<=28;z+=2)
            {Model("Drain",walls,new Vector3(33,-.18f,z));if(z%6==0)Model("Reeds",walls,new Vector3(34,.02f,z),z*7,Vector3.one*1.6f);}
            var ruins=Group(art,"05 Factory ruins and salvage islands");
            foreach(var center in new[]{new Vector3(-29,0,27),new Vector3(-31,0,9),new Vector3(28,0,29),new Vector3(29,0,-25)})
            {
                Model("Facade",ruins,center,180,new Vector3(1.7f,1.4f,1.5f));
                Model("Facade",ruins,center+new Vector3(3,0,3),90,new Vector3(1.4f,1.1f,1.5f));
                Model("Rubble",ruins,center+new Vector3(0,0,-3),25,Vector3.one*2);
                for(int i=0;i<3;i++)Model("IvyHanging",ruins,center+new Vector3(i*1.1f-1,4.2f,-.5f),0,Vector3.one*1.6f);
            }
            var skyline=Group(art,"06 Distant industrial skyline");
            foreach(var p in new[]{new Vector3(-55,1,58),new Vector3(-31,1,67),new Vector3(54,1,58)})Model("CoolingTower",skyline,p,0,Vector3.one*1.8f);
            for(int i=0;i<7;i++)Model("Chimney",skyline,new Vector3(-70+i*22,2,80+(i%2)*14),0,Vector3.one*(1.8f+(i%3)*.3f));
            for(int i=0;i<17;i++)Model("Facade",skyline,new Vector3(-80+i*10,1,60+(i%3)*8),180,new Vector3(2.5f,2.1f+(i%3)*.5f,2));
            for(int i=0;i<8;i++)
            {Model("Facade",skyline,new Vector3(-59,1,-30+i*11),90,new Vector3(2.4f,2,2));Model("Facade",skyline,new Vector3(58,1,-25+i*12),-90,new Vector3(2.3f,2.5f,2));}
        }
        static float Range(float min,float max)=>(float)(min+random.NextDouble()*(max-min));
        static bool ClearWorkArea(Vector3 p)
        {
            foreach(var q in new[]{new Vector3(9,0,9),new Vector3(-8,0,5),new Vector3(-8,0,22),new Vector3(8,0,24),new Vector3(16,0,-1)})if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(q.x,q.z))<8)return false;
            if(p.x>-37&&p.x<-15&&p.z>-26&&p.z<-7)return false;
            if(p.z>-23&&p.z<-15&&p.x>-37&&p.x<10)return false;
            if(p.x>2&&p.x<10&&p.z>-19&&p.z<7)return false;
            if(p.x>-17&&p.x<-7&&p.z>4&&p.z<13)return false;
            return true;
        }
        static void Planting(Transform art)
        {
            var vegetation=Group(art,"07 Earth vegetation - edge clusters");
            var species=new[]{"GrassDry","GrassDry","GrassSeed","WeedRosette","Wildflowers","ScrubDry","Shrub","Moss","Lichen"};
            for(int cluster=0;cluster<54;cluster++)
            {
                float angle=cluster*Mathf.PI*2/54;
                var center=new Vector3(Mathf.Cos(angle)*37,0,Mathf.Sin(angle)*32);
                if(cluster%6==0)center*=.77f;
                var group=Group(vegetation,"Cluster "+cluster.ToString("00"));
                for(int i=0;i<20;i++)
                {
                    var p=center+new Vector3(Range(-3.5f,3.5f),.015f,Range(-2.8f,2.8f));if(!ClearWorkArea(p))continue;
                    string id=species[random.Next(species.Length)];float size=Range(.9f,2.4f);
                    var instance=Model(id,group,p,Range(0,360),Vector3.one*size);
                    foreach(var r in instance.GetComponentsInChildren<Renderer>())r.shadowCastingMode=id=="Shrub"?ShadowCastingMode.On:ShadowCastingMode.Off;
                }
                if(cluster%4==0&&ClearWorkArea(center))Model("DeadSapling",group,center,Range(0,360),Vector3.one*2.2f);
                if(cluster%3==0&&ClearWorkArea(center))Model("IvyLow",group,center+Vector3.up*.015f,Range(0,360),Vector3.one*2);
            }
        }
        static void Lighting(Scene scene)
        {
            var sun=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>()).First(l=>l.type==LightType.Directional);
            sun.color=new Color(1,.91f,.74f);sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(38,-32,0);sun.shadows=LightShadows.Soft;
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.48f,.57f,.61f);RenderSettings.ambientEquatorColor=new Color(.34f,.39f,.37f);RenderSettings.ambientGroundColor=new Color(.22f,.24f,.19f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.59f,.65f,.61f);RenderSettings.fogStartDistance=85;RenderSettings.fogEndDistance=215;
            var sky=new Material(Shader.Find("Skybox/Procedural")){name="Stage2 hazy earth sky"};
            sky.SetColor("_SkyTint",new Color(.52f,.64f,.7f));sky.SetColor("_GroundColor",new Color(.33f,.36f,.28f));sky.SetFloat("_AtmosphereThickness",1.15f);sky.SetFloat("_Exposure",.9f);sky.SetFloat("_SunSize",.035f);
            AssetDatabase.CreateAsset(sky,Generated+"/EarthSky.mat");RenderSettings.skybox=sky;
            var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).First(c=>c.name=="Editor overview camera");
            camera.transform.position=new Vector3(65,52,-70);camera.transform.LookAt(new Vector3(0,4,5));camera.clearFlags=CameraClearFlags.Skybox;camera.farClipPlane=350;
        }
    }
}
