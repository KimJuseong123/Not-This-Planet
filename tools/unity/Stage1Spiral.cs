using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
public static class Stage1Spiral
{
    const string Prefix="Astra Part2 Spiral - ";
    const float HeightScale=8.17f/7.622f, Rise=7*HeightScale, StartTop=-.2f+.622f*HeightScale;
    [Serializable] class Report {public string status;public int floorSamples,clearanceSamples,railCount,movedLampGroups;public List<string> removed=new List<string>();public List<string> changedIds=new List<string>();public List<string> blockers=new List<string>();}
    static Report report;
    static Vector3 Point(float f,float radius=14)=>new Vector3(28+radius*Mathf.Cos((-90+270*f)*Mathf.Deg2Rad),StartTop+Rise*f,19+radius*Mathf.Sin((-90+270*f)*Mathf.Deg2Rad));
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Touch(Transform t){foreach(var ob in new Object[]{t,t.gameObject}){var id=GlobalObjectId.GetGlobalObjectIdSlow(ob);report.changedIds.Add(id.targetObjectId.ToString());report.changedIds.Add(id.targetPrefabId.ToString());}}
    static GameObject Add(int kind,string name,Transform parent,Vector3 p,Quaternion q,Vector3 scale){var g=Stage1Part2Details.Create(kind,"",parent,p,q,scale);g.name=Prefix+name;return g;}
    const string ScenePath="Assets/Scenes/Stage1_Astra.unity";
    static Transform[] All()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    static void Open(){if(!Application.isBatchMode||!File.Exists("../ISOLATED_PART2_TRAIL"))throw new Exception("Isolated batch only");EditorSceneManager.OpenScene(ScenePath);}
    static bool Old(Transform t)=>t.name.StartsWith("Astra Part2 - SM_Astra_")||t.name.StartsWith("Astra Part2 Detail - TrailJunction")||t.name.StartsWith("Astra Part2 Detail - CliffEdgeGuardrail");
    static void Delete(Transform t){report.removed.Add(t.name);Need(t.GetComponentsInChildren<MonoBehaviour>(true).All(m=>m&&m.GetType().FullName=="Metagame.Gameplay.Stage1GeneratedCollider"),"Gameplay component on removed object "+t.name);Object.DestroyImmediate(t.gameObject);}
    public static void Apply()
    {
        Open();report=new Report();var before=All();Need(!before.Any(t=>t.name.StartsWith(Prefix)),"Already installed");
        var old=before.Where(Old).ToArray();Need(old.Length==54,"Expected 54 old road/junction/rail roots");var parent=old.First(t=>t.name=="Astra Part2 - SM_Astra_Trail0").parent;
        foreach(var t in old)Delete(t);
        var cliffs=All().Where(t=>t.name=="Hill cliff · exposed lift face"&&t.position.z<11&&t.position.x>23&&t.position.x<34).ToArray();Need(cliffs.Length==4,"Cliff inventory changed");foreach(var t in cliffs)Delete(t);
        foreach(var name in new[]{"Perimeter outcrop 12","Perimeter outcrop 13"})Delete(All().Single(t=>t.name==name));
        Add(4,"SpiralHillAscent",parent,new Vector3(28,-.2f,19),Quaternion.identity,new Vector3(1,HeightScale,1));
        Bridge("Ground entry",parent,new Vector3(17,.05f,5),Point(0)+new Vector3(.25f,0,0),3.8f);
        Bridge("Observatory bridge",parent,Point(1)+new Vector3(0,0,.15f),new Vector3(23.7f,8.03f,16.4f),3.8f);
        Add(2,"Entry landing",parent,Point(0)-Vector3.up*.084f,Quaternion.Euler(0,90,0),Vector3.one);
        Add(2,"Summit landing",parent,Point(1)-Vector3.up*.084f,Quaternion.identity,Vector3.one);
        for(int side=0;side<2;side++)for(int i=0;i<40;i++)
        {
            if(side==0&&i>=38)continue; // Open the inward turn onto the observatory bridge.
            float r=side==0?11.95f:16.05f;float f=(i+.5f)/40;float a=(-90+270*f)*Mathf.Deg2Rad;
            var outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(side==0?-1:1);var tangent=new Vector3(-Mathf.Sin(a),Rise/(r*Mathf.PI*1.5f),Mathf.Cos(a));
            var up=Vector3.Cross(tangent,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))).normalized;
            float length=2*r*Mathf.Sin(Mathf.PI*1.5f/80);
            Add(3,"Curve rail "+side+" "+i,parent,Point(f,r),Quaternion.LookRotation(outward,up),new Vector3(length/2,1,1));
        }
        // Move the complete existing light groups, including hidden original collision pieces.
        var feet=All().Where(t=>t.name=="Lamp · foot"&&t.position.x>26&&t.position.x<46&&t.position.z>-8&&t.position.z<30&&t.position.y<7.4f&&Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(28,19))>8).OrderBy(t=>t.position.y).ToArray();
        var lamps=All().Where(t=>t.name.StartsWith("Lamp · ")).ToArray();var bollards=All().Where(t=>t.name.StartsWith("Bollard ")).ToArray();
        for(int i=0;i<feet.Length;i++)
        {
            var foot=feet[i];var previous=foot.position;float f=.05f+.9f*(i+.5f)/feet.Length;var desired=Point(f,16.05f);var delta=desired-new Vector3(previous.x,foot.GetComponent<Collider>().bounds.min.y,previous.z);
            foreach(var lamp in lamps.Where(t=>Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(previous.x,previous.z))<.02f&&Mathf.Abs(t.position.y-previous.y)<1).ToArray()){Touch(lamp);lamp.position+=delta;}
            foreach(var bollard in bollards.Where(t=>Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(previous.x,previous.z))<.25f).ToArray()){Touch(bollard);bollard.position+=delta;}
            report.movedLampGroups++;
        }
        Physics.SyncTransforms();Validate();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());report.status="PASS";File.WriteAllText("../spiral-validation.json",JsonUtility.ToJson(report,true));
    }
    static void Bridge(string name,Transform parent,Vector3 start,Vector3 end,float width)
    {
        var direction=end-start;var q=Quaternion.LookRotation(direction,Vector3.up);
        var g=Add(5,name,parent,(start+end)/2-q*Vector3.up*.18f,q,new Vector3(width/3.8f,1,direction.magnitude/2.7832742f));
        for(int side=-1;side<=1;side+=2){int n=Mathf.CeilToInt((direction.magnitude-1.4f)/2);float length=(direction.magnitude-1.4f)/n;for(int i=0;i<n;i++)
        {if((name=="Ground entry"&&i==n-1)||(name=="Observatory bridge"&&i<2))continue;
            Add(3,name+" rail "+side+" "+i,parent,start+direction.normalized*(.7f+length*(i+.5f))+q*Vector3.right*(side*(width/2-.1f)),q*Quaternion.Euler(0,side*90,0),new Vector3(length/2,1,1));}}
    }
    public static void Verify(){Open();report=JsonUtility.FromJson<Report>(File.ReadAllText("../spiral-validation.json"));report.floorSamples=0;report.clearanceSamples=0;report.blockers.Clear();Physics.SyncTransforms();Validate();File.WriteAllText("../spiral-reload-validation.json",JsonUtility.ToJson(report,true));}
    static void Validate()
    {
        var all=All();Need(!all.Any(Old),"Old segmented route remains");var spiral=all.Single(t=>t.name==Prefix+"SpiralHillAscent");var col=spiral.GetComponentInChildren<MeshCollider>();
        for(int i=0;i<143;i++)for(int lane=-1;lane<=1;lane++)
        {
            var p=Point((i+.5f)/143,14+lane*1.35f);
            Need(col.Raycast(new Ray(p+Vector3.up,Vector3.down),out var hit,2)&&Mathf.Abs(hit.point.y-p.y)<.02f,"Spiral floor gap "+i);report.floorSamples++;
            foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.55f,p+Vector3.up*1.45f,.30f,~0,QueryTriggerInteraction.Ignore))
                if(!c.GetComponentsInParent<Transform>().Any(t=>t==spiral))report.blockers.Add(i+" lane "+lane+" "+c.name+" "+c.transform.position);
            report.clearanceSamples++;
        }
        File.WriteAllLines("../spiral-clearance-blockers.txt",report.blockers);Need(report.blockers.Count==0,"Spiral walking clearance blockers: "+report.blockers.Count);
        foreach(var name in new[]{"Ground entry","Observatory bridge"})
        {
            var t=all.Single(x=>x.name==Prefix+name);var c=t.GetComponentInChildren<MeshCollider>();
            for(int i=0;i<=40;i++)for(int lane=-1;lane<=1;lane++)
            {
                var p=t.TransformPoint(new Vector3(lane, .18f,Mathf.Lerp(-1.35f,1.35f,i/40f)));
                Need(c.Raycast(new Ray(p+Vector3.up,Vector3.down),out var h,2),"Bridge floor gap");
                foreach(var obstacle in Physics.OverlapCapsule(p+Vector3.up*.55f,p+Vector3.up*1.45f,.3f,~0,QueryTriggerInteraction.Ignore))
                    report.blockers.Add(name+" "+i+" "+lane+" "+obstacle.name+" "+obstacle.transform.position);
                report.floorSamples++;report.clearanceSamples++;
            }
        }
        File.WriteAllLines("../spiral-clearance-blockers.txt",report.blockers);Need(report.blockers.Count==0,"Bridge clearance blockers: "+report.blockers.Count);
        foreach(var t in all.Where(t=>t.name.StartsWith(Prefix)))
        {
            Need(t.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap"))),"Missing material");
            Need(t.GetComponentsInChildren<MeshCollider>().All(c=>c.sharedMesh&&c.enabled&&!c.isTrigger),"Missing collider");
        }
        report.railCount=all.Count(t=>t.name.StartsWith(Prefix)&&t.name.Contains("rail"));
        Need(all.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"Missing scripts");
    }
    public static void Survey()
    {
        Open();foreach(var t in All().Where(Old).ToArray())Object.DestroyImmediate(t.gameObject);Physics.SyncTransforms();
        var lines=new List<string>();
        for(int z=-8;z<=36;z+=4)for(int x=24;x<=60;x+=4)
        {
            var hits=Physics.RaycastAll(new Vector3(x,40,z),Vector3.down,80,~0,QueryTriggerInteraction.Ignore).OrderByDescending(h=>h.point.y).ToArray();
            lines.Add(x+","+z+":"+string.Join(";",hits.Take(4).Select(h=>h.point.y.ToString("F2")+" "+h.collider.name)));
        }
        File.WriteAllLines("../spiral-survey.txt",lines);
        var blocked=new Dictionary<string,int>();
        for(int i=0;i<=144;i++)for(int lane=-1;lane<=1;lane++)
        {
            float a=(-90+270*i/144f)*Mathf.Deg2Rad;float r=14+lane*1.35f;
            var p=new Vector3(28+r*Mathf.Cos(a),.97f+7*i/144f,19+r*Mathf.Sin(a));
            foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.55f,p+Vector3.up*1.45f,.32f,~0,QueryTriggerInteraction.Ignore))
            {
                var root=PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject);string key=(root?root.name:c.name)+" at "+(root?root.transform.position:c.transform.position)+" / "+c.name;
                if(!blocked.ContainsKey(key))blocked[key]=0;blocked[key]++;
            }
        }
        File.WriteAllLines("../spiral-blockers.txt",blocked.Select(k=>k.Value+" "+k.Key));
    }
}
