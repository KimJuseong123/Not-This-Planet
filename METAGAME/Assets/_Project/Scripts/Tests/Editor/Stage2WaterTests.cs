using System.Collections;
using System.Linq;
using Metagame.Core;
using Metagame.EditorTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering.Universal;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2WaterTests
    {
        PlayerRig rig;
        DrainageWater water;
        [UnitySetUp] public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            yield return new EnterPlayMode();yield return null;yield return null;
            GameSession.Instance.ScriptedInput=true;
            rig=GameSession.Instance.LocalPlayer;rig.SetInput(default);
            water=Object.FindFirstObjectByType<DrainageWater>();
        }
        // **연 씬을 반드시 닫는다** (S15P21B202-611). 플레이 모드만 빠져나오면 이 씬이
        // 열린 채로 남아, 뒤이어 도는 검증들이 그 콜라이더를 만난다 — `-604` 에서
        // 그렇게 운반체 검증 9건이 한꺼번에 무너졌다.
        [UnityTearDown] public IEnumerator Cleanup(){if(Application.isPlaying)yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);}
        void Move(Vector3 foot){rig.SetInput(default);rig.Teleport(foot,Quaternion.identity);Physics.SyncTransforms();}
        IEnumerator Run(InputFrame input,float seconds)
        {
            // EnterPlayMode from EditMode still uses the editor test enumerator; counting
            // yields is not counting physics ticks. Measure actual simulation time.
            float end=Time.fixedTime+seconds;
            while(Time.fixedTime<end){rig.SetInput(input);yield return null;}
        }
        [UnityTest] public IEnumerator DeepWaterFloatsAscendsDescendsAndStopsAtSurface()
        {
            Move(new Vector3(33,-1.4f,0));yield return Run(default,.5f);
            float y=rig.Position.y;
            Assert.That(rig.Swimming,Is.True);Assert.That(rig.Doll.Bodies.All(b=>!b.useGravity),Is.True);
            yield return Run(default,1);
            Assert.That(rig.Position.y,Is.EqualTo(y).Within(.2f),"Neutral buoyancy should preserve depth");
            yield return Run(new InputFrame{Jump=true},2);
            Assert.That(rig.Position.y,Is.GreaterThan(y+.5f));
            Assert.That(rig.Position.y,Is.LessThan(water.Surface+.1f),"Holding ascent must not launch the player");
            yield return Run(new InputFrame{ArmsUp=true},.6f);
            Assert.That(rig.Position.y,Is.LessThan(water.Surface-.3f));
            yield return Run(default,.8f);float stopped=rig.Position.y;
            yield return Run(default,.6f);Assert.That(rig.Position.y,Is.EqualTo(stopped).Within(.15f));
        }
        [UnityTest] public IEnumerator ShallowsSlowWalkingAndBridgeRemainsDry()
        {
            Move(new Vector3(27,.1f,0));yield return Run(new InputFrame{Move=Vector2.up},.8f);
            float dry=rig.Doll.Hips.linearVelocity.z;
            Assert.That(dry,Is.GreaterThan(.7f));
            var terrain=Object.FindFirstObjectByType<Terrain>();var p=new Vector3(35.9f,0,0);p.y=terrain.SampleHeight(p)+terrain.transform.position.y+.015f;
            Move(p);yield return Run(new InputFrame{Move=Vector2.up},.8f);
            Assert.That(rig.InWater,Is.True);Assert.That(rig.Swimming,Is.False);
            Assert.That(rig.Doll.Hips.linearVelocity.z,Is.LessThan(dry*.8f));
            Move(new Vector3(33,.18f,10));yield return Run(default,.5f);
            Assert.That(rig.InWater,Is.False,"Walking on a crossing must not count as wading");
            Assert.That(rig.Doll.Bodies.All(b=>b.useGravity),Is.True);
        }
        [UnityTest] public IEnumerator ExitRespawnAndDisabledWaterRestoreGravity()
        {
            Move(new Vector3(33,-1.3f,0));yield return Run(default,.2f);Assert.That(rig.Swimming,Is.True);
            Move(new Vector3(25,.2f,0));yield return Run(default,.2f);
            Assert.That(rig.Swimming||rig.Floating,Is.False);Assert.That(rig.Doll.Bodies.All(b=>b.useGravity),Is.True);
            Move(new Vector3(33,-1.3f,0));yield return Run(default,.2f);
            water.gameObject.SetActive(false);yield return Run(default,.2f);
            Assert.That(rig.Floating,Is.False);Assert.That(rig.Doll.Bodies.All(b=>b.useGravity),Is.True);
            water.gameObject.SetActive(true);Move(new Vector3(33,-1.3f,0));yield return Run(default,.2f);
            GameSession.Instance.RespawnPlayer(rig);yield return Run(default,.2f);
            Assert.That(rig.InWater||rig.Swimming,Is.False);
        }
        [UnityTest] public IEnumerator EndRampLetsSwimmerReturnToDryLand()
        {
            Move(new Vector3(33,-1,25));yield return Run(default,.2f);
            yield return Run(new InputFrame{Move=Vector2.up,Jump=true,AimYaw=0},6);
            Assert.That(rig.Position.z,Is.GreaterThan(31));
            Assert.That(rig.Swimming,Is.False);Assert.That(rig.Doll.Bodies.All(b=>b.useGravity),Is.True);
        }
        [UnityTest] public IEnumerator DownedPlayerCannotUseSwimInputAndReplicaUsesSameStaticVolume()
        {
            Move(new Vector3(33,-1.3f,0));yield return Run(default,.2f);rig.GoDown("water test");
            float y=rig.Position.y;yield return Run(new InputFrame{Jump=true,Move=Vector2.up},.5f);
            Assert.That(rig.Position.y,Is.LessThan(y+.2f));Assert.That(rig.Downed,Is.True);
            rig.Revive(100);rig.ServerAuthority=false;
            Move(new Vector3(33,-1.3f,0));yield return Run(new InputFrame{Jump=true},.5f);
            Assert.That(rig.Swimming,Is.True);Assert.That(rig.Position.y,Is.GreaterThan(-.9f));
            Assert.That(water.GetComponentsInChildren<Mirror.NetworkIdentity>(),Is.Empty,"Static water must not add another state authority");
        }
        [UnityTest] public IEnumerator TerrainHasShallowBanksDeepBedAndCameraHasRefractionInputs()
        {
            var terrain=Object.FindFirstObjectByType<Terrain>();
            float Height(float x,float z)=>terrain.SampleHeight(new Vector3(x,0,z))+terrain.transform.position.y;
            Assert.That(Height(33,0),Is.LessThan(-2));Assert.That(Height(35.9f,0),Is.GreaterThan(-.4f));
            Assert.That(Height(33,29),Is.GreaterThan(-.4f));
            var camera=rig.Cam.GetComponent<Camera>().GetUniversalAdditionalCameraData();
            Assert.That(camera.requiresDepthTexture,Is.True);Assert.That(camera.requiresColorTexture,Is.True);
            Assert.That(water.GetComponent<Renderer>().sharedMaterial.shader.isSupported,Is.True);
            var reflection=(Cubemap)water.GetComponent<Renderer>().sharedMaterial.GetTexture("_Environment");
            Assert.That(reflection,Is.Not.Null);
            Assert.That(reflection.GetPixels(CubemapFace.PositiveY).Average(c=>c.r+c.g+c.b),Is.GreaterThan(.01f),"Saved reflection must retain rendered RGB pixels");
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c=>c.name=="Temporary drainage cubemap camera"),Is.False);
            Stage2WaterAuthoring.Render();yield return null;
        }
    }
}
