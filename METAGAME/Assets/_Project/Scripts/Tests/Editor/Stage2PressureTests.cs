using System.Collections;
using System.Linq;
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
    public sealed class Stage2PressureTests
    {
        Stage2Yard yard;PlayerRig rig;
        [UnitySetUp] public IEnumerator Setup()
        {
            EditorSceneManager.OpenScene(Stage2SceneBuilder.ScenePath);
            Object.FindAnyObjectByType<Metagame.Bootstrap.Stage2Bootstrap>().ConfigureDemo(false);
            yield return new EnterPlayMode();yield return null;yield return null;
            GameSession.Instance.ScriptedInput=true;Object.FindFirstObjectByType<Stage2Bridge>().enabled=false;
            yard=Object.FindFirstObjectByType<Stage2Yard>();yard.enabled=false;yard.SetAuthority(true);
            rig=GameSession.Instance.LocalPlayer;rig.SetInput(default);yard.Register(1,rig);
        }
        // **연 씬을 반드시 닫는다** (S15P21B202-611). 플레이 모드만 빠져나오면 이 씬이
        // 열린 채로 남아, 뒤이어 도는 검증들이 그 콜라이더를 만난다 — `-604` 에서
        // 그렇게 운반체 검증 9건이 한꺼번에 무너졌다.
        [UnityTearDown] public IEnumerator Cleanup(){if(Application.isPlaying)yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);}
        void Tick(float dt=.02f)=>typeof(Stage2Yard).GetMethod("TickPressureSurge",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(yard,new object[]{dt});
        void PlaceAt(Vector3 hips)
        {
            rig.Teleport(hips-(rig.Position-rig.FootPosition),Quaternion.identity);Physics.SyncTransforms();
            foreach(var body in rig.Doll.Bodies)if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
        }
        [Category("Heavy")] [UnityTest] public IEnumerator SafeIntroductionWarningBurstRestAndRoundRobin()
        {
            yard.State.PressureClock=18;Assert.That(yard.PressurePhase(0),Is.Zero);Tick();Assert.That(yard.State.PressureClock,Is.EqualTo(18));
            yard.State.GasEnabled=true;
            foreach(float t in new[]{0f,11.99f,21f,41.9f}){yard.State.PressureClock=t;Assert.That(yard.PressurePhase(0),Is.Zero);}
            yard.State.PressureClock=12;Assert.That(yard.PressurePhase(0),Is.EqualTo(1));
            yard.State.PressureClock=18;Assert.That(yard.PressurePhase(0),Is.EqualTo(2));
            yard.State.PressureClock=60;Assert.That(yard.PressurePhase(1),Is.EqualTo(2));Assert.That(yard.PressurePhase(0),Is.Zero);
            yard.State.Launched=true;Assert.That(yard.PressurePhase(1),Is.Zero);yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator BurstPhysicallyPushesOncePerCooldownAndWallBlocksIt()
        {
            var pump=yard.PressurePumps[0];var p=pump.Nozzle.position+pump.Direction*2;
            PlaceAt(p);Assert.That(pump.Contains(rig.Position),Is.True);Assert.That(pump.ClearTo(rig),Is.True);
            yard.State.GasEnabled=true;yard.State.PressureClock=18;Tick();
            Assert.That(Vector3.Dot(rig.Hips.linearVelocity,pump.Direction),Is.GreaterThan(2));
            float v=rig.Hips.linearVelocity.magnitude;Tick();Assert.That(rig.Hips.linearVelocity.magnitude,Is.EqualTo(v).Within(.001f));
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=pump.Nozzle.position+pump.Direction;
            wall.transform.localScale=Vector3.one*.7f;Physics.SyncTransforms();PlaceAt(p);
            yard.State.PressureClock=19;Tick();Assert.That(pump.ClearTo(rig),Is.False);Assert.That(rig.Hips.linearVelocity.magnitude,Is.LessThan(.001f));
            Object.Destroy(wall);yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator ValveRequiresReachAndSuppressesOnlyItsPump()
        {
            var target=yard.Targets.First(t=>t.Kind==YardTargetKind.PressureRelief && t.Index==0);
            yard.State.GasEnabled=true;yard.State.PressureClock=18;
            PlaceAt(target.transform.position+Vector3.up*10);
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=target.Id});Assert.That(yard.State.PressureRelief[0],Is.Zero);
            PlaceAt(target.transform.position+yard.PressurePumps[0].Direction*1.3f);
            Assert.That(yard.CanReach(rig,target),Is.True);
            yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=target.Id});
            Assert.That(yard.State.PressureRelief[0],Is.EqualTo(45));Assert.That(yard.PressurePhase(0),Is.Zero);Assert.That(yard.State.PressureRelief[1],Is.Zero);
            Tick(1);Assert.That(yard.State.PressureRelief[0],Is.EqualTo(44));yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator ReplicaCannotPushOrRelieveAndLateJoinRetainsPhase()
        {
            yard.State.GasEnabled=true;yard.State.PressureClock=18;yard.State.PressureRelief[2]=30;
            var snapshot=JsonUtility.FromJson<Stage2State>(JsonUtility.ToJson(yard.Capture()));
            yard.SetAuthority(false);yard.ApplyReplica(snapshot);var pump=yard.PressurePumps[0];PlaceAt(pump.Nozzle.position+pump.Direction*2);
            Tick();Assert.That(rig.Hips.linearVelocity.magnitude,Is.LessThan(.001f));Assert.That(yard.PressurePhase(0),Is.EqualTo(2));Assert.That(yard.State.PressureRelief[2],Is.EqualTo(30));
            var target=yard.Targets.First(t=>t.Kind==YardTargetKind.PressureRelief);yard.Submit(1,new YardCommand{Action=YardAction.Interact,Target=target.Id});Assert.That(yard.State.PressureRelief[0],Is.Zero);yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator SceneHasThreeTexturedPumpsAndClearUsableJets()
        {
            Assert.That(yard.PressurePumps.Length,Is.EqualTo(3));Assert.That(yard.Targets.Length,Is.EqualTo(29));Assert.That(yard.Targets.Select(t=>t.Id).Distinct().Count(),Is.EqualTo(29));
            foreach(var pump in yard.PressurePumps)
            {
                Assert.That(pump.Nozzle.position.y,Is.InRange(.7f,1.2f));
                Assert.That(pump.Needle!=null && pump.Wheel!=null && pump.Flap!=null,Is.True);
                foreach(var renderer in pump.GetComponentsInChildren<MeshRenderer>())foreach(var material in renderer.sharedMaterials)
                    Assert.That(material.GetTexture("_BaseMap"),Is.Not.Null,material.name);
                PlaceAt(pump.Nozzle.position+pump.Direction*2);Assert.That(pump.ClearTo(rig),Is.True,"Blocked nozzle "+pump.Index);
                Assert.That(pump.Contains(pump.Nozzle.position-pump.Direction),Is.False);
                Assert.That(pump.Contains(pump.Nozzle.position+pump.Direction*9),Is.False);
            }
            yield break;
        }
        [Category("Heavy")] [UnityTest] public IEnumerator RuntimeJetTravelsAndStopsAfterBurst()
        {
            yard.State.GasEnabled=true;yard.State.PressureClock=18.3f;
            var pump=yard.PressurePumps[0];
            PlaceAt(pump.Nozzle.position+pump.Direction*2);
            Vector3 before=rig.Position;
            float previous=Time.fixedTime,end=previous+.6f;
            while(Time.fixedTime<end){if(Time.fixedTime>previous){Tick(Time.fixedTime-previous);previous=Time.fixedTime;}yield return null;}
            Assert.That(Vector3.Dot(rig.Position-before,pump.Direction),Is.GreaterThan(.15f),"Motor must not erase every water impulse");
            Assert.That(pump.Jet.isPlaying,Is.True);Assert.That(pump.Beacon.enabled,Is.True);Assert.That(pump.Warning.enabled,Is.True);
            var particles=new ParticleSystem.Particle[600];int count=pump.Jet.GetParticles(particles);
            Assert.That(count,Is.GreaterThan(10));
            Assert.That(particles.Take(count).Max(p=>Vector3.Dot(p.position-pump.Nozzle.position,pump.Direction)),Is.GreaterThan(3),"Jet must emerge beyond its own collider");
            pump.enabled=false;Assert.That(pump.Jet.isEmitting,Is.False);Assert.That(pump.Warning.enabled,Is.False);
            pump.enabled=true;yield return null;yield return null;Assert.That(pump.Jet.isEmitting,Is.True);
            end=Time.fixedTime+.4f;while(Time.fixedTime<end)yield return null;
            Assert.That(pump.Jet.particleCount,Is.GreaterThan(10),"Re-enabled effect must emit actual particles");
            PlaceAt(new Vector3(-27,1,-18));Stage2PressureAuthoring.Render();
            yard.State.PressureClock=22;yield return null;yield return null;
            Assert.That(pump.Jet.isEmitting,Is.False);Assert.That(pump.Warning.enabled,Is.False);Assert.That(pump.Beacon.enabled,Is.False);
        }
    }
}
