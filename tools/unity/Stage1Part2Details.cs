using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

// Explicit batch-only authoring in the marked isolated project.
public static class Stage1Part2Details
{
    const string ScenePath="Assets/Scenes/Stage1_Astra.unity", Prefix="Astra Part2 Detail - ";
    const string Base="Assets/_Project/Models/LongWalkingTrail/";
    static readonly string[] Folders={"07-observatory-entry-stairs","08-observatory-instrument","09-adjustable-trail-junction","10-cliff-edge-guardrail"};
    static readonly string[] Names={"ObservatoryEntryStairs","ObservatoryInstrument","TrailJunction","CliffEdgeGuardrail"};
    [Serializable] class Item { public string name; public Vector3 position, scale; public Quaternion rotation; }
    [Serializable] class Report { public string status; public int stairs,instruments,junctions,guardrails,missingScripts, floorRays, railRays, seamSamples; public float maximumJunctionRise; public List<Item> items=new List<Item>(); }
    static void Check(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    static Transform[] All()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    static void Open(){Check(Application.isBatchMode&&File.Exists("../ISOLATED_PART2_TRAIL"),"Isolated batch required");EditorSceneManager.OpenScene(ScenePath);}
    static GameObject Create(int kind,string suffix,Transform parent,Vector3 p,Quaternion q,Vector3 scale)
    {
        var root=new GameObject(Prefix+Names[kind]+suffix);
        var path=Base+Folders[kind]+"/";
        var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path+"SM_Astra_"+Names[kind]+".fbx"));
        visual.transform.SetParent(root.transform,false);
        var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Material",new[]{path.TrimEnd('/')}).Single()));
        Check(mat&&mat.GetTexture("_BaseMap"),"Missing material "+path);
        foreach(var r in visual.GetComponentsInChildren<MeshRenderer>())r.sharedMaterials=Enumerable.Repeat(mat,r.sharedMaterials.Length).ToArray();
        var collision=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path+"Colliders/UCX_SM_Astra_"+Names[kind]+".fbx"));
        PrefabUtility.UnpackPrefabInstance(collision,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        collision.name="Collision";
        collision.transform.SetParent(root.transform,false);
        foreach(var mf in collision.GetComponentsInChildren<MeshFilter>()) {var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;c.convex=false;Object.DestroyImmediate(mf);}
        foreach(var r in collision.GetComponentsInChildren<Renderer>())Object.DestroyImmediate(r);
        root.transform.SetParent(parent,false);root.transform.position=p;root.transform.rotation=q;root.transform.localScale=scale;
        foreach(var t in root.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=8;GameObjectUtility.SetStaticEditorFlags(t.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);}
        return root;
    }
    static Vector3 Top(Transform t,float z)=>t.TransformPoint(new Vector3(0,t.GetComponent<BoxCollider>().size.y/2,z));
    public static void Apply()
    {
        Open();var all=All();Check(!all.Any(t=>t.name.StartsWith(Prefix)),"Already installed");
        var stairs=all.Where(t=>t.name.StartsWith("Hill observatory")&&t.name.Contains("entry stair")).ToArray();
        var instruments=all.Where(t=>t.name=="Observatory instrument").ToArray();
        var indicators=all.Where(t=>t.name=="Instrument indicator").ToArray();
        Check(stairs.Length==4&&instruments.Length==3&&indicators.Length==3,"Unexpected replacement inventory");
        var parent=stairs[0].parent;var positions=instruments.Select(t=>t.position).ToArray();
        foreach(var old in stairs.Concat(instruments).Concat(indicators)){Check(old.childCount==0&&old.GetComponents<MonoBehaviour>().Length==0,"Gameplay/children on replacement");Object.DestroyImmediate(old.gameObject);}
        Create(0,"",parent,new Vector3(28,7.2f,12.22f),Quaternion.identity,Vector3.one);
        Physics.SyncTransforms();
        for(int i=0;i<positions.Length;i++)
        {
            var p=positions[i];Check(Physics.Raycast(new Vector3(p.x,8.3f,p.z),Vector3.down,out var hit,2,~0,QueryTriggerInteraction.Ignore),"No observatory floor");
            p.y=hit.point.y;var facing=new Vector3(28-p.x,0,19-p.z);
            Create(1," "+i,parent,p,Quaternion.LookRotation(-facing),Vector3.one);
        }
        var trails=Enumerable.Range(0,5).Select(i=>All().Single(t=>t.name=="Astra Part2 - SM_Astra_Trail"+i)).ToArray();
        for(int i=0;i<4;i++)
        {
            var a=trails[i];var b=trails[i+1];var sa=a.GetComponent<BoxCollider>().size;var sb=b.GetComponent<BoxCollider>().size;
            var pa=Top(a,sa.z/2);var pb=Top(b,-sb.z/2);
            Check(Vector3.Distance(pa,pb)<1f,"Unexpected disconnected endpoint");
            var q=Quaternion.Slerp(a.rotation,b.rotation,.5f);
            Create(2," "+i,a.parent,(pa+pb)/2-q*Vector3.up*.08f,q,Vector3.one);
        }
        foreach(var trail in trails.Skip(1).Concat(new[]{All().Single(t=>t.name=="Astra Part2 - SM_Astra_HillDeckTransition")}))
        {
            var s=trail.GetComponent<BoxCollider>().size;float inset=s.z<4?.4f:1.5f;float length=s.z-2*inset;int n=Mathf.CeilToInt(length/2);float segment=length/n;
            foreach(int side in new[]{-1,1}) for(int j=0;j<n;j++)
            {
                var local=new Vector3(side*(s.x/2-.10f),s.y/2,-s.z/2+inset+segment*(j+.5f));
                var q=trail.rotation*Quaternion.Euler(0,side==1?90:-90,0);
                Create(3," "+trail.name.Replace("Astra Part2 - SM_Astra_","")+" "+side+" "+j,trail.parent,trail.TransformPoint(local),q,new Vector3(segment/2,1,1));
            }
        }
        Physics.SyncTransforms();Validate("../details-validation.json");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
    public static void Verify(){Open();Physics.SyncTransforms();Validate("../details-reload-validation.json");}
    static void Validate(string output)
    {
        var all=All();var roots=all.Where(t=>t.name.StartsWith(Prefix)).ToArray();var report=new Report();
        Check(!all.Any(t=>t.name=="Observatory instrument"||t.name=="Instrument indicator"||(t.name.StartsWith("Hill observatory")&&t.name.Contains("entry stair"))),"Old details remain");
        foreach(var t in roots)
        {
            Check(t.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap"))),"Material missing");
            var cols=t.GetComponentsInChildren<MeshCollider>();Check(cols.Length==1&&cols.All(c=>c.sharedMesh&&c.enabled&&!c.isTrigger&&!c.convex),"Invalid collision");
            var col=cols[0];
            if(t.name.Contains("EntryStairs"))
            {
                report.stairs++;for(int i=0;i<4;i++)for(int x=-1;x<=1;x++)
                {
                    var p=t.TransformPoint(new Vector3(x, .24f+i*.19f,-1.02f+i*.48f+.24f));
                    bool hitFloor=col.Raycast(new Ray(p+Vector3.up,Vector3.down),out var hit,2);
                    Check(hitFloor&&Vector3.Distance(p,hit.point)<.015f,"Stair floor mismatch step="+i+" expected="+p+" hit="+hit.point+" bounds="+col.bounds);report.floorRays++;
                }
            }
            else if(t.name.Contains("Instrument"))report.instruments++;
            else if(t.name.Contains("Junction"))
            {
                report.junctions++;for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                {
                    var p=t.TransformPoint(new Vector3(x*1.6f,.104f,z*.7f));
                    Check(col.Raycast(new Ray(p+Vector3.up,Vector3.down),out var hit,2)&&Vector3.Distance(p,hit.point)<.015f,"Junction collision mismatch");report.floorRays++;
                }
                var road=all.Where(a=>a.name.StartsWith("Astra Part2 - SM_Astra_")).Select(a=>a.GetComponent<BoxCollider>()).ToArray();
                for(int x=-1;x<=1;x++)for(int z=-2;z<=2;z++)
                {
                    var p=t.TransformPoint(new Vector3(x*.6f,.104f,z));var ray=new Ray(p+Vector3.up,Vector3.down);
                    var floors=new List<float>();foreach(var c in road)if(c.Raycast(ray,out var h,3))floors.Add(h.point.y);
                    bool onCap=col.Raycast(ray,out var cap,3);Check(floors.Count>0||onCap,"Junction approach has no road or bridge");
                    if(onCap&&floors.Count>0) {float rise=cap.point.y-floors.Max();report.maximumJunctionRise=Mathf.Max(report.maximumJunctionRise,rise);Check(rise<.18f,"Junction step too high: "+rise);}
                    report.seamSamples++;
                }
            }
            else if(t.name.Contains("Guardrail"))
            {
                report.guardrails++;var p=t.TransformPoint(new Vector3(0,.5f,0));
                Check(col.Raycast(new Ray(p-t.forward,t.forward),out var hit,2),"Rail does not block");report.railRays++;
            }
            report.items.Add(new Item{name=t.name,position=t.position,rotation=t.rotation,scale=t.localScale});
        }
        Check(report.stairs==1&&report.instruments==3&&report.junctions==4&&report.guardrails>=40,"Missing model categories");
        // Rail colliders must leave the original center walking lane clear.
        foreach(var trail in all.Where(t=>t.name.StartsWith("Astra Part2 - SM_Astra_")))
        {
            var size=trail.GetComponent<BoxCollider>().size;
            for(int i=0;i<=30;i++)
            {
                var p=Top(trail,Mathf.Lerp(-size.z/2,size.z/2,i/30f));
                var overlap=Physics.OverlapCapsule(p+Vector3.up*.65f,p+Vector3.up*1.4f,.35f,~0,QueryTriggerInteraction.Ignore);
                Check(!overlap.Any(c=>c.transform.root&&c.GetComponentsInParent<Transform>().Any(t=>t.name.StartsWith(Prefix+"CliffEdgeGuardrail"))),"Guardrail blocks walking lane");
            }
        }
        report.missingScripts=all.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));Check(report.missingScripts==0,"Missing scripts");
        report.status="PASS";File.WriteAllText(output,JsonUtility.ToJson(report,true));
    }
}
