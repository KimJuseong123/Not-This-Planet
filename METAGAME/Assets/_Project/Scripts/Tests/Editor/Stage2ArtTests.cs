using System.Collections;
using System.Linq;
using Metagame.EditorTools;
using Metagame.Gameplay.Stage2;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2ArtTests
    {
        // **연 씬을 반드시 닫는다** (S15P21B202-611). 플레이 모드만 빠져나오면 이 씬이
        // 열린 채로 남아, 뒤이어 도는 검증들이 그 콜라이더를 만난다 — `-604` 에서
        // 그렇게 운반체 검증 9건이 한꺼번에 무너졌다.
        [UnityTearDown] public IEnumerator Cleanup(){if(Application.isPlaying)yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);}
        static Transform Find(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
        [Test]
        public void AuthoredModelsKeepThreeBodiesAndOpenWalkingRoutes()
        {
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Stage2ArtIntegration.Validate();
            Stage2ArtIntegration.ValidateTerrain();
            var yard=scene.GetRootGameObjects().Single(g=>g.GetComponent<Stage2Yard>());
            var so=new SerializedObject(yard.GetComponent<Stage2Yard>());
            Assert.That(so.FindProperty("_parts").arraySize,Is.EqualTo(3));
            foreach(var target in yard.GetComponentsInChildren<Stage2Target>().Where(t=>t.Kind==YardTargetKind.Bolt))
            {
                string name="Bolt_"+(target.Index%3+1);
                var pivot=target.transform.parent.GetComponentsInChildren<Transform>().First(t=>t.name==name||t.name.StartsWith(name+"."));
                Assert.That(Vector3.Distance(target.transform.position,pivot.GetComponentInChildren<Renderer>().bounds.center),Is.LessThan(.01f),"Bolt interaction must coincide with its visible model");
            }
            Physics.SyncTransforms();
            foreach(var p in new[]{new Vector3(-27,0,-21),new Vector3(-8,0,-19),new Vector3(6,0,-8),new Vector3(9,0,4.5f)})
            {
                var hits=Physics.OverlapBox(p+Vector3.up*1.1f,new Vector3(.4f,.7f,.4f));
                Assert.That(hits.Any(h=>h.transform.root.name.StartsWith("Stage2 Earth Art")),Is.False,"Environment blocks walking route at "+p);
            }
        }
        [UnityTest]
        public IEnumerator ReplicaMovesCraneLidLiftAndLadderArtWithoutNewNetworkObjects()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            yield return new EnterPlayMode();yield return null;yield return null;
            var yard=Object.FindFirstObjectByType<Stage2Yard>();Assert.That(yard.GetComponent<Stage2ArtMotion>(),Is.Not.Null);
            GameSession.Instance.ScriptedInput=true;
            // Feed a client snapshot directly; the offline bridge would reclaim host authority each Update.
            Object.FindFirstObjectByType<Metagame.Network.Stage2Bridge>().enabled=false;
            var state=yard.Capture();yard.SetAuthority(false);
            state.Yaw=72;state.Radius=21;state.Height=9;state.LiftHeight=3;state.LiftGoal=3;
            state.LadderHeight=4;state.LadderDeployed=true;state.Parts[0].Open=true;
            yard.ApplyReplica(state);yield return null;yield return null;
            var hook=Find(yard.transform,"06 Crane hook");var trolley=Find(yard.transform,"Trolley");
            Assert.That(hook.GetComponent<LineRenderer>().sharedMaterial,Is.SameAs(hook.GetComponentInChildren<MeshRenderer>(true).sharedMaterial),"Cable must use the authored URP hook material");
            Assert.That(Vector2.Distance(new Vector2(hook.position.x,hook.position.z),new Vector2(trolley.position.x,trolley.position.z)),Is.LessThan(.02f));
            var lid=Find(yard.transform,"08 Container 1").Find("Roof hinge");
            Assert.That(Quaternion.Angle(lid.localRotation,Quaternion.Euler(110,0,0)),Is.LessThan(.01f));
            Assert.That(lid.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),Is.True,"Opening roof must carry actual model");
            var lift=Find(yard.transform,"07 Cockpit lift");Assert.That(lift.position.y,Is.EqualTo(3.2f).Within(.02f));
            Assert.That(lift.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.name.Contains("LiftCabin")),Is.True);
            var ladder=Find(yard.transform,"10 Portable A-frame work ladder");
            Assert.That(ladder.Find("Platform").position.y,Is.EqualTo(ladder.position.y+4).Within(.02f));
            Assert.That(Find(ladder,"UpperPlatform").position.y,Is.EqualTo(ladder.Find("Platform").position.y).Within(.02f));
            Assert.That(ladder.Find("Art Ladder").localScale.y,Is.EqualTo(1).Within(.01f));
            Assert.That(yard.Authority,Is.False,"Art must not take authority");
            Assert.That(yard.GetComponentsInChildren<Mirror.NetworkIdentity>(true),Is.Empty);
        }
    }
}
