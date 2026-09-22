using System.Collections;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.EditorTools;
using Metagame.Gameplay.Stage2;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2Tests
    {
        // **연 씬을 반드시 닫는다** (S15P21B202-632). 플레이 모드만 빠져나오면 Stage2 씬이
        // 열린 채로 남아, 뒤이어 도는 검증들이 그 콜라이더를 만난다 — 2026-09-18 에
        // `StuckEscapeTests`·`TransportAltitudeTests`·`TransportDismountTests` 셋이 그렇게
        // 흔들렸다(바닥이 없어야 하는데 하나를 찾고, 8m 에서 멈춰야 하는데 28m 까지 올랐다).
        //
        // **뒤 픽스처의 정리에 기대고 있었다.** `Stage2WaterTests` 가 `-611` 로 받은 정리가
        // 이 누수를 덮고 있었는데, 그 픽스처가 Heavy 로 빠지면서(`-611`) 실행 집합에서
        // 사라지자 드러났다. 청소를 남에게 맡기면 그 남이 안 도는 날 깨진다.
        [UnityTearDown]
        public IEnumerator CleanupPlayMode()
        { if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        static IEnumerator WaitForSimulation(float seconds)
        { float end=Time.time+seconds; while(Time.time<end) yield return null; }

        [UnityTest]
        public IEnumerator WrenchQThrowsPhysicalPropAndAllowsPickup()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Object.FindAnyObjectByType<Metagame.Bootstrap.Stage2Bootstrap>().ConfigureDemo(false);
            yield return new EnterPlayMode(); yield return null;
            var session = GameSession.Instance;
            session.ScriptedInput = true;
            session.SetCursor(true);
            var rig = session.LocalPlayer;
            rig.SetInput(default);
            var yard = Object.FindAnyObjectByType<Stage2Yard>();
            var rack = yard.Targets.First(t => t.Kind == YardTargetKind.Wrench);
            rig.Teleport(rack.transform.position + Vector3.back - Vector3.up, Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1, new YardCommand { Action = YardAction.Interact, Target = rack.Id });
            Assert.That(yard.PlayerState(1).Tool, Is.EqualTo(YardTool.Wrench));
            rig.Teleport(new Vector3(-24, 2, -15), Quaternion.identity);
            yield return WaitForSimulation(.25f);
            var consumed = session.StageInputFilter(new InputFrame { ToolAction = true, ToolUtility = true });
            Assert.That(consumed.ToolAction || consumed.ToolUtility, Is.False);
            Assert.That(yard.PlayerState(1).Tool, Is.EqualTo(YardTool.None));
            Assert.That(yard.LooseToolTargets.Count, Is.EqualTo(1));
            var loose = yard.LooseToolTargets[0];
            var body = loose.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(5));
            Vector3 start = body.position;
            session.StageInputFilter(new InputFrame { ToolAction = true });
            Assert.That(yard.LooseToolTargets.Count, Is.EqualTo(1), "Holding Q must not duplicate a throw");
            yield return WaitForSimulation(.3f);
            Assert.That(Vector3.Distance(start, body.position), Is.GreaterThan(.1f));
            var snapshot = JsonUtility.FromJson<Stage2State>(JsonUtility.ToJson(yard.Capture()));
            Assert.That(snapshot.LooseTools.Single().Id, Is.EqualTo(loose.Id));
            Assert.That(Vector3.Distance(snapshot.LooseTools[0].Position, body.position), Is.LessThan(.01f));
            rig.Teleport(body.position + Vector3.back - Vector3.up * .45f, Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1, new YardCommand { Action = YardAction.Interact, Target = loose.Id });
            Assert.That(yard.PlayerState(1).Tool, Is.EqualTo(YardTool.Wrench));
            Assert.That(yard.LooseToolTargets.Count, Is.Zero);
            Assert.That(yard.Capture().LooseTools, Is.Empty);
            // Tablet and incapacitation must not produce accidental throws.
            session.StageInputFilter(default);
            session.StageInputFilter(new InputFrame { TabletToggle = true, ToolAction = true });
            Assert.That(yard.PlayerState(1).Tool, Is.EqualTo(YardTool.Wrench));
            session.StageInputFilter(new InputFrame { TabletToggle = true });
            rig.GoDown("test");
            yard.Submit(1, new YardCommand { Action = YardAction.ThrowWrench, Aim = Vector3.forward });
            Assert.That(yard.LooseToolTargets.Count, Is.Zero);
            // A late observer reconstructs a physical prop as a non-authoritative replica.
            // Stop the local bridge so it cannot take authority back during this assertion.
            Object.FindAnyObjectByType<Metagame.Network.Stage2Bridge>().enabled = false;
            yard.SetAuthority(false);
            yard.ApplyReplica(snapshot);
            Assert.That(yard.LooseToolTargets.Count, Is.EqualTo(1));
            var replica = yard.LooseToolTargets[0];
            Assert.That(replica.GetComponent<Rigidbody>().isKinematic, Is.True);
            yard.Submit(1, new YardCommand { Action = YardAction.Interact, Target = replica.Id });
            Assert.That(yard.LooseToolTargets.Count, Is.EqualTo(1), "Observer cannot perform local pickup");
            snapshot.LooseTools = System.Array.Empty<YardLooseToolState>();
            yard.ApplyReplica(snapshot);
            Assert.That(yard.LooseToolTargets.Count, Is.Zero);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator CraneEntersThirdPersonAndExitsWithoutAimingAtControls()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Object.FindAnyObjectByType<Metagame.Bootstrap.Stage2Bootstrap>().ConfigureDemo(false);
            yield return new EnterPlayMode(); yield return null;
            var session = GameSession.Instance;
            session.ScriptedInput = true;
            session.SetCursor(true);
            var rig = session.LocalPlayer;
            rig.SetInput(default);
            var yard = Object.FindAnyObjectByType<Stage2Yard>();
            var controls = yard.Targets.First(t => t.Kind == YardTargetKind.Crane);
            bool original = rig.Cam.FirstPerson;
            float distance = rig.Cam.Distance;
            rig.Teleport(controls.transform.position + Vector3.back - Vector3.up, Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1, new YardCommand { Action = YardAction.Interact, Target = controls.Id });
            Assert.That(yard.State.Pilot, Is.EqualTo(1));
            yield return null; yield return null;
            Assert.That(rig.Cam.FirstPerson, Is.False);
            Assert.That(rig.Cam.VehicleView, Is.True);
            float radius = yard.State.Radius;
            float moveUntil = Time.time + .15f;
            while (Time.time < moveUntil)
            {
                session.StageInputFilter(new InputFrame { Move = Vector2.up });
                yield return null;
            }
            Assert.That(yard.State.Radius, Is.GreaterThan(radius));
            session.StageInputFilter(new InputFrame { TransportAction = true });
            Assert.That(yard.State.Pilot, Is.Zero, "E exits regardless of camera focus");
            yield return null; yield return null;
            Assert.That(rig.Cam.VehicleView, Is.False);
            Assert.That(rig.Cam.FirstPerson, Is.EqualTo(original));
            Assert.That(rig.Cam.Distance, Is.EqualTo(distance));
            yield return new ExitPlayMode();
        }
        [Test]
        public void CranePolarCoordinatesReachAllStoresAndSockets()
        {
            var pivot = new Vector3(-8,0,5);
            foreach (var target in new[] { new Vector3(-8,0,22),new Vector3(8,0,24),new Vector3(16,0,-1),new Vector3(9,0,9) })
            {
                var delta = target-pivot;
                Assert.That(delta.magnitude, Is.InRange(Stage2Rules.RadiusMin,Stage2Rules.RadiusMax));
                float yaw = Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
                Assert.That(Vector3.Distance(Stage2Rules.Hook(pivot,yaw,delta.magnitude,12),target+Vector3.up*12),Is.LessThan(.001f));
            }
        }
        [TestCase(.64f,11f,true)] [TestCase(.7f,0f,false)] [TestCase(0f,13f,false)]
        public void PlacementRejectsDistanceAndTilt(float distance,float tilt,bool expected)
        { Assert.That(Stage2Rules.Aligned(Vector3.right*distance,Quaternion.Euler(tilt,0,0),Vector3.zero),Is.EqualTo(expected)); }
        [Test]
        public void MouseCircleCountsTurnsButStraightAndZigzagDoNot()
        {
            var input = new CircularWrenchInput(); float amount=0;
            for(int i=0;i<=144;i++) { float angle=i*5*Mathf.Deg2Rad; amount+=input.Sample(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))); }
            Assert.That(amount,Is.InRange(1.8f,2.05f));
            input.Reset(); amount=0;
            for(int i=0;i<200;i++) amount+=input.Sample(Vector2.right*(i%2==0?1:-1));
            Assert.That(amount,Is.EqualTo(0));
        }
        [Test]
        public void PartialBoltsCannotCarryTheRocket()
        {
            Assert.That(Stage2Rules.Fastened(1,1,.99f),Is.False);
            Assert.That(Stage2Rules.Fastened(1,1,1),Is.True);
        }
        [Test]
        public void SceneHasUniqueInteractionsAndNoStage1Tools()
        {
            var scene=EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath,OpenSceneMode.Additive);
            try
            {
                var targets=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Stage2Target>()).ToArray();
                Assert.That(targets.Select(t=>t.Id).Distinct().Count(),Is.EqualTo(targets.Length));
                Assert.That(targets.Count(t=>t.Kind==YardTargetKind.Bolt),Is.EqualTo(9));
                Assert.That(targets.Count(t=>t.Kind==YardTargetKind.Container),Is.EqualTo(3));
                Assert.That(scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Parts.SpaceshipPart>()).Count(),Is.Zero);
                Assert.That(StageCatalog.SceneNameOf("stage2"),Is.EqualTo("Stage2_Earth"));
            }
            finally { EditorSceneManager.CloseScene(scene,true); }
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator SceneRunsAndHostEnforcesRangeOccupancyAssemblyAndRecovery()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Object.FindAnyObjectByType<Metagame.Bootstrap.Stage2Bootstrap>().ConfigureDemo(false);
            yield return new EnterPlayMode();
            yield return null;
            if(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                GameSession.Instance.ScriptedInput=true;
                for(int frame=0;frame<20;frame++) yield return null;
                var camera=GameObject.Find("Editor overview camera").GetComponent<Camera>();
                var rt=new RenderTexture(1600,1000,24); camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                System.IO.Directory.CreateDirectory("../artifacts/stage2-draft");
                System.IO.File.WriteAllBytes("../artifacts/stage2-draft/overview-play.png",image.EncodeToPNG());
                camera.targetTexture=null; RenderTexture.active=null;
                Object.Destroy(image); Object.Destroy(rt);
                var floor=GameObject.Find("Foundation").GetComponent<Renderer>();
                Debug.Log("[Stage2Visual] " + floor.sharedMaterial.name + " " + floor.sharedMaterial.GetColor("_BaseColor") + " " + floor.sharedMaterial.shader.name);
            }
            var yard=Object.FindAnyObjectByType<Stage2Yard>();
            var session=GameSession.Instance;
            Assert.That(session.LocalPlayer,Is.Not.Null);
            session.ScriptedInput=true;
            session.LocalPlayer.SetInput(default);
            yield return null;
            var rig=session.LocalPlayer;
            yard.Register(1,rig);
            Assert.That(yard.Authority,Is.True);
            Assert.That(yard.State.GasEnabled,Is.False);
            var container=yard.Targets.First(t=>t.Kind==YardTargetKind.Container&&t.Index==0);
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=container.Id});
            Assert.That(yard.State.Parts[0].Open,Is.False,"Remote interaction must be rejected");
            rig.Teleport(container.transform.position+Vector3.back-Vector3.up,Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=container.Id});
            Assert.That(yard.State.Parts[0].Open,Is.True);
            Assert.That(yard.Tighten(0,0,1),Is.False,"Cannot bolt an unsupported or misaligned part");

            var body=GameObject.Find("09 Rocket module 1").GetComponent<Rigidbody>();
            var socket=GameObject.Find("Assembly socket 1").transform;
            body.position=socket.position; body.rotation=Quaternion.identity;
            yard.State.Attached=0;
            Assert.That(yard.Tighten(0,0,1),Is.True);
            Assert.That(yard.State.Parts[0].Secured,Is.False);
            Assert.That(yard.Tighten(0,1,1),Is.True);
            Assert.That(yard.Tighten(0,2,1),Is.True);
            Assert.That(yard.State.Parts[0].Secured,Is.True);
            Assert.That(yard.State.GasEnabled,Is.True);
            Assert.That(yard.ReadyToLaunch(),Is.False,"Incomplete rocket and connected cable cannot launch");
            yard.State.Height=18;
            Vector3 craneDelta=socket.position-new Vector3(-8,0,5);
            yard.State.Yaw=Mathf.Atan2(craneDelta.x,craneDelta.z)*Mathf.Rad2Deg;
            yard.State.Radius=new Vector2(craneDelta.x,craneDelta.z).magnitude;
            yield return null;
            Assert.That(yard.CanDetach(0),Is.False,"Tension must be released before detaching");
            yard.State.Height=socket.position.y+1.6f;
            yield return null;
            Assert.That(yard.CanDetach(0),Is.True);

            var ladder=yard.Targets.First(t=>t.Kind==YardTargetKind.Ladder);
            rig.Teleport(ladder.transform.parent.position+Vector3.up*1.2f,Quaternion.identity);
            Physics.SyncTransforms();
            yield return WaitForSimulation(.25f);
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=ladder.Id});
            Assert.That(yard.State.LadderCarrier,Is.Zero,"Occupied ladder must not fold");
            yard.State.Pilot=1;
            yard.Remove(1);
            Assert.That(yard.State.Pilot,Is.Zero,"Disconnected pilot must release the controls");
            Assert.That(yard.State.Attached,Is.EqualTo(0),"Disconnect must retain the suspended load");

            var loose=GameObject.Find("09 Rocket module 2").GetComponent<Rigidbody>();
            loose.position=new Vector3(80,-10,0);
            yard.RecoverParts();
            Assert.That(Vector3.Distance(loose.position,new Vector3(8,1.9f,24)),Is.LessThan(.01f));
            Assert.That(body.position,Is.EqualTo(socket.position),"Recovery preserves secured modules");
            yard.Register(1,rig);
            for(int i=1;i<3;i++)
            {
                var next=GameObject.Find("09 Rocket module "+(i+1)).GetComponent<Rigidbody>();
                next.position=GameObject.Find("Assembly socket "+(i+1)).transform.position;
                next.rotation=Quaternion.identity;
                yard.State.Parts[i].Open=true; yard.State.Attached=i;
                for(int bolt=0;bolt<3;bolt++) Assert.That(yard.Tighten(i,bolt,1),Is.True);
            }
            yard.State.Attached=-1; yard.State.Yaw=-90; yard.State.Radius=15;
            var launch=yard.Targets.First(t=>t.Kind==YardTargetKind.Launch);
            rig.Revive(rig.MaxHealth);
            rig.Teleport(launch.transform.position+Vector3.back-Vector3.up,Quaternion.identity);
            Physics.SyncTransforms();
            yield return WaitForSimulation(.25f);
            Assert.That(yard.ReadyToLaunch(),Is.True,$"Launch fixture foot={rig.FootPosition}, down={rig.Downed}");
            Assert.That(yard.CanReach(rig,launch),Is.True,$"Launch panel blocked from {rig.Position} to {launch.transform.position}");
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=launch.Id});
            Assert.That(yard.State.Launched,Is.True);
            Assert.That(session.Cleared,Is.True);
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator SprayCleansTeammateButCannotCrossWallOrCleanSelf()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Object.FindAnyObjectByType<Metagame.Bootstrap.Stage2Bootstrap>().ConfigureDemo(false);
            yield return new EnterPlayMode(); yield return null;
            var session=GameSession.Instance; session.ScriptedInput=true;
            var rig=session.LocalPlayer; rig.SetInput(default);
            var yard=Object.FindAnyObjectByType<Stage2Yard>();
            var target=PlayerRig.Create("Contaminated teammate",new Vector3(15,.1f,10),Quaternion.identity,Color.white,Color.blue,false);
            target.IsLocal=false; target.SetInput(default);
            yard.Register(1,rig); yard.Register(2,target);
            var rack=yard.Targets.First(t=>t.Kind==YardTargetKind.Sprayer);
            rig.Teleport(rack.transform.position+Vector3.back-Vector3.up,Quaternion.identity);
            Physics.SyncTransforms();
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=rack.Id});
            Assert.That(yard.PlayerState(1).Tool,Is.EqualTo(YardTool.Sprayer));
            rig.Teleport(new Vector3(15,.1f,6),Quaternion.identity);
            yard.State.GasEnabled=true; yard.State.GasClock=7;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position=new Vector3(15,2,8); wall.transform.localScale=new Vector3(4,4,.25f);
            Physics.SyncTransforms();
            for(int i=0;i<10;i++)
            { yard.Submit(1,new YardCommand{Primary=true,Aim=Vector3.forward}); yield return WaitForSimulation(.1f); }
            float contaminated=yard.PlayerState(2).Contamination;
            Assert.That(contaminated,Is.GreaterThan(.005f),$"Wall must block decontamination; target={target.Position}, source={rig.Position}, clock={yard.State.GasClock}, down={target.Downed}");
            Object.Destroy(wall); yield return null;
            for(int i=0;i<10;i++)
            { yard.Submit(1,new YardCommand{Primary=true,Aim=(target.Position-rig.Position-Vector3.up*.45f).normalized}); yield return WaitForSimulation(.1f); }
            Assert.That(yard.PlayerState(2).Contamination,Is.LessThan(contaminated));
            Assert.That(yard.PlayerState(1).Contamination,Is.GreaterThan(.01f),"Sprayer must not clean itself");
            yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator Stage1EndingContinuesIntoStage2WithFreshGameplay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            var factory=new GameObject("Ending snapshot fixture").AddComponent<Stage1EndingBootstrap>();
            var snapshot=factory.CreatePreview(); Object.DestroyImmediate(factory.gameObject);
            EndingSceneFlow.Ensure().Begin(snapshot);
            // Scene loading and playback creation are asynchronous; batch frame rates are unbounded.
            double deadline=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<deadline && Object.FindAnyObjectByType<Parts.DemoEnding>()==null) yield return null;
            var playback=Object.FindAnyObjectByType<Parts.DemoEnding>();
            Assert.That(playback,Is.Not.Null);
            playback.Snapshot.StartedAt=Time.realtimeSinceStartupAsDouble-playback.ArrivalStartsAt-1;
            yield return null;yield return null;
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,Is.EqualTo(StageCatalog.Stage1EndingSceneName),"Arrival cinematic must finish before gameplay scene loads");
            Assert.That(playback.Stage,Is.EqualTo(Parts.DemoEndingStage.SpaceDrift));
            playback.Snapshot.StartedAt=Time.realtimeSinceStartupAsDouble-playback.ArrivalStartsAt-8.5;
            yield return null;yield return null;
            Assert.That(playback.Stage,Is.EqualTo(Parts.DemoEndingStage.MeteorImpact));
            playback.Snapshot.StartedAt=-10000;
            deadline=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<deadline && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!=StageCatalog.Stage2SceneName) yield return null;
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,Is.EqualTo(StageCatalog.Stage2SceneName));
            yield return null;
            Assert.That(GameSession.Instance.LocalPlayer,Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<Stage2Yard>(),Is.Not.Null);
            yield return new ExitPlayMode();
        }
    }
}
