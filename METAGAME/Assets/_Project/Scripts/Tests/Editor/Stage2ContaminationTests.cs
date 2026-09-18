using System.Collections;
using System.Reflection;
using Metagame.EditorTools;
using Metagame.Gameplay.Stage2;
using Metagame.Network;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2ContaminationTests
    {
        Stage2Yard yard;
        PlayerRig rig, teammate;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnitySetUp] public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            yield return new EnterPlayMode(); yield return null; yield return null;
            GameSession.Instance.ScriptedInput=true;
            Object.FindFirstObjectByType<Stage2Bridge>().enabled=false;
            yard=Object.FindFirstObjectByType<Stage2Yard>();yard.enabled=false;yard.SetAuthority(true);
            rig=GameSession.Instance.LocalPlayer;rig.SetInput(default);yard.Register(1,rig);
            teammate=PlayerRig.Create("Hazard test teammate",new Vector3(-5,.1f,-5),Quaternion.identity,Color.white,Color.blue,false);
            teammate.IsLocal=false;teammate.SetInput(default);yard.Register(2,teammate);
        }
        // **연 씬을 반드시 닫는다** (S15P21B202-611). 플레이 모드만 빠져나오면 이 씬이
        // 열린 채로 남아, 뒤이어 도는 검증들이 그 콜라이더를 만난다 — `-604` 에서
        // 그렇게 운반체 검증 9건이 한꺼번에 무너졌다.
        [UnityTearDown] public IEnumerator Cleanup(){if(Application.isPlaying)yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);}
        void Tick(uint id,float dt,string method="UpdateContamination")
        {
            var workers=(IDictionary)typeof(Stage2Yard).GetField("_workers",Private).GetValue(yard);
            typeof(Stage2Yard).GetMethod(method,Private).Invoke(yard,new[]{workers[id],(object)dt});
        }
        void Move(PlayerRig player,Vector3 foot){player.Teleport(foot,Quaternion.identity);Physics.SyncTransforms();}
        void SetState(uint id,float contamination,YardTool tool=YardTool.None)
        {
            var worker=((IDictionary)typeof(Stage2Yard).GetField("_workers",Private).GetValue(yard))[id];
            var field=worker.GetType().GetField("State");var state=(YardPlayerState)field.GetValue(worker);
            state.Contamination=contamination;state.Tool=tool;field.SetValue(worker,state);
        }

        [Category("Heavy")] [UnityTest] public IEnumerator CycleHasSafeIntroductionWarningLeakAndRest()
        {
            Assert.That(yard.State.GasEnabled,Is.False);
            yard.State.GasClock=10;Assert.That(yard.GasActive,Is.False);
            yard.State.GasEnabled=true;
            foreach(float t in new[]{0f,5.99f,40f}){yard.State.GasClock=t;Assert.That(yard.GasWarning,Is.True);Assert.That(yard.GasActive,Is.False);}
            foreach(float t in new[]{6f,22.99f,46f}){yard.State.GasClock=t;Assert.That(yard.GasActive,Is.True);}
            foreach(float t in new[]{23f,39.99f}){yard.State.GasClock=t;Assert.That(yard.GasActive||yard.GasWarning,Is.False);}
            yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator ExposureUsesHeightBoundsAndDoesNotStackOverlappingVolumes()
        {
            yard.State.GasEnabled=true;yard.State.GasClock=7;
            Move(rig,new Vector3(12,5,10));Tick(1,11);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.2f).Within(.001f));
            Move(rig,new Vector3(10,10,9));Tick(1,11);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.2f).Within(.001f),"Above visible gas must be safe");
            Move(rig,new Vector3(10,.1f,9));Tick(1,11);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.2f).Within(.001f),"Below elevated gas must be safe");
            Move(rig,new Vector3(-5,.1f,-5));Tick(1,11);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.2f).Within(.001f),"Leaving gas stops accumulation, not existing contamination");
            yard.Remove(2);SetState(1,0);Move(rig,new Vector3(16,1,10));Tick(1,9);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.1f).Within(.001f),"Solo grace period is 90 exposure seconds");
            yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator WalkingBackToSpawnDoesNotReplaceShowerButRespawnCleans()
        {
            Move(rig,GameSession.Instance.SpawnFoot);SetState(1,.6f);Tick(1,.1f);
            Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.6f).Within(.001f),"Walking into preparation must not instantly decontaminate");
            GameSession.Instance.RespawnPlayer(rig);Tick(1,.1f);
            Assert.That(yard.PlayerState(1).Contamination,Is.Zero,"Actual personal recovery clears contamination");yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator ShowerCleansAndFullExposureDownsWithoutHealingBySpray()
        {
            yard.State.GasEnabled=true;yard.State.GasClock=7;Move(rig,new Vector3(16,1,10));Tick(1,55);
            Assert.That(rig.Downed,Is.True);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(1));
            Move(rig,new Vector3(-17,0,-12));Tick(1,3);Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.34f).Within(.001f));
            Assert.That(rig.Downed,Is.True,"Decontamination does not replace rescue or heal physical injury");yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator SprayReachesUpperFloorButRejectsRangeAndAngleAndReplicaCannotSimulate()
        {
            Move(rig,new Vector3(-5,.1f,-5));Move(teammate,new Vector3(-5,5,-1));
            SetState(1,.6f,YardTool.Sprayer);SetState(2,.8f);
            Vector3 aim=(teammate.Position-rig.Position-Vector3.up*.45f).normalized;
            yard.Submit(1,new YardCommand{Primary=true,Aim=aim});Tick(1,3,"Spray");
            Assert.That(yard.PlayerState(2).Contamination,Is.EqualTo(.26f).Within(.001f));Assert.That(yard.PlayerState(1).Contamination,Is.EqualTo(.6f));
            yard.Submit(1,new YardCommand{Primary=true,Aim=-aim});Tick(1,3,"Spray");Assert.That(yard.PlayerState(2).Contamination,Is.EqualTo(.26f).Within(.001f));
            Move(teammate,new Vector3(-5,5,15));yard.Submit(1,new YardCommand{Primary=true,Aim=(teammate.Position-rig.Position).normalized});Tick(1,3,"Spray");Assert.That(yard.PlayerState(2).Contamination,Is.EqualTo(.26f).Within(.001f));
            var snapshot=JsonUtility.FromJson<Stage2State>(JsonUtility.ToJson(yard.Capture()));yard.SetAuthority(false);yard.ApplyReplica(snapshot);
            float clock=yard.State.GasClock;typeof(Stage2Yard).GetMethod("FixedUpdate",Private).Invoke(yard,null);
            Assert.That(yard.State.GasClock,Is.EqualTo(clock));Assert.That(yard.PlayerState(2).Contamination,Is.EqualTo(.26f).Within(.001f),"Replicas retain per-player contamination");yield break;
        }
    }
}
