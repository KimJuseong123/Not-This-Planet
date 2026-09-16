using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class MagneticStormImpulseTests
    {
        [UnityTearDown]
        public IEnumerator LeavePlayMode() { if(Application.isPlaying) yield return new ExitPlayMode(); }

        [Test]
        public void SweepIsBroadsideAndKeepsItsHeightAcrossTheEntirePass()
        {
            var settings=new AtmosphereProfile.DustStormSettings { enabled=true };
            var bounds=new Bounds(new Vector3(0,10,0),new Vector3(102,20,94));
            var schedule=new DustStormSchedule(settings);
            var a=schedule.Evaluate(12,bounds);
            var b=schedule.Evaluate(20,bounds);
            Assert.That(a.IsActive && b.IsActive,Is.True);
            Assert.That(b.Center.y,Is.EqualTo(a.Center.y));
            Assert.That(Vector3.Dot(a.Rotation*Vector3.forward,a.Direction),Is.EqualTo(1).Within(.0001));
            Assert.That(Vector3.Dot(a.Rotation*Vector3.right,a.Direction),Is.EqualTo(0).Within(.0001));
            Assert.That(a.HalfExtents.x,Is.GreaterThan(a.HalfExtents.z*4));
            Assert.That(Vector3.Distance(b.Center-a.Center,a.Direction*48),Is.LessThan(.001));
        }

        [Test]
        public void SmokeOpacityIsSerializedAndPassedToTheShader()
        {
            var root=Object.Instantiate(Resources.Load<GameObject>(MagneticStormVisual.ResourcePath));
            try
            {
                var visual=root.GetComponent<MagneticStormVisual>();
                var so=new SerializedObject(visual);
                so.FindProperty("smokeOpacity").floatValue=.27f;
                so.ApplyModifiedPropertiesWithoutUndo();
                visual.Evaluate(1.5);
                var block=new MaterialPropertyBlock();
                root.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                Assert.That(block.GetFloat("_SmokeOpacity"),Is.EqualTo(.27f).Within(.0001));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator TriggerKicksInteractiveBodiesAndWholePlayerOncePerSecondOnlyWithAuthority()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            var profile=ScriptableObject.CreateInstance<AtmosphereProfile>();
            profile.dustStorm.enabled=true;
            var bounds=new Bounds(new Vector3(0,10,0),new Vector3(102,20,94));
            var schedule=new DustStormSchedule(profile.dustStorm);
            var root=new GameObject("Impulse test");
            var hazard=root.AddComponent<Stage1DustStormHazard>();
            hazard.Configure(profile,bounds);
            var prop=Body("Interactive prop",root.transform);
            prop.gameObject.AddComponent<Grabbable>().Climbable=true; // Ship parts are marked climbable too.
            prop.mass=500;
            var extra=prop.gameObject.AddComponent<SphereCollider>(); extra.radius=.3f;
            var ignored=Body("Non-interactive body",root.transform);
            var locked=Body("Locked interactive",root.transform);
            locked.gameObject.AddComponent<Grabbable>(); locked.isKinematic=true;
            var playerRoot=new GameObject("Player"); playerRoot.transform.SetParent(root.transform);
            var player=playerRoot.AddComponent<PlayerRig>(); player.enabled=false;
            var hips=Body("Hips",playerRoot.transform);
            var chest=Body("Chest",playerRoot.transform);
            player.Doll=new Doll { Root=playerRoot.transform,Hips=hips,Chest=chest };
            player.Doll.Bodies.Add(hips); player.Doll.Bodies.Add(chest);
            var targets=new[]{prop,ignored,locked,hips,chest};
            var kick=schedule.Evaluate(12,bounds).Direction*18+Vector3.up*4;
            Place(targets,schedule.Evaluate(12,bounds));
            hazard.FixedTick(12); yield return new WaitForFixedUpdate();
            Assert.That(prop.linearVelocity.magnitude,Is.LessThan(.01),"Non-authority must not apply impulses.");
            hazard.SetPhysicsAuthority(true);
            Place(targets,schedule.Evaluate(12,bounds));
            hazard.FixedTick(12); yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(prop.linearVelocity,kick),Is.LessThan(.02),"Compound colliders must not multiply impulses, even for a heavy object.");
            Assert.That(Vector3.Distance(hips.linearVelocity,kick),Is.LessThan(.02));
            Assert.That(Vector3.Distance(chest.linearVelocity,kick),Is.LessThan(.02));
            Assert.That(player.Ragdolled,Is.True,"Walking must not cancel the kick.");
            Assert.That(ignored.linearVelocity.magnitude,Is.LessThan(.01));
            var trigger=root.GetComponentInChildren<BoxCollider>();
            Assert.That(trigger.isTrigger,Is.True);
            Assert.That(trigger.attachedRigidbody.isKinematic,Is.True);
            Place(targets,schedule.Evaluate(12.5,bounds));
            hazard.FixedTick(12.5); yield return new WaitForFixedUpdate();
            Assert.That(prop.linearVelocity.magnitude,Is.LessThan(.01),"Cooldown must prevent a second kick before one second.");
            Place(targets,schedule.Evaluate(13,bounds));
            hazard.FixedTick(13); yield return new WaitForFixedUpdate();
            Assert.That(Vector3.Distance(prop.linearVelocity,kick),Is.LessThan(.02));
            hazard.FixedTick(0);
            Assert.That(trigger.enabled,Is.False);
            Object.Destroy(root); Object.Destroy(profile);
            yield return null;
            yield return new ExitPlayMode();
        }

        static Rigidbody Body(string name,Transform parent)
        {
            var go=new GameObject(name); go.transform.SetParent(parent);
            go.AddComponent<SphereCollider>().radius=.1f;
            var rb=go.AddComponent<Rigidbody>(); rb.useGravity=false; rb.linearDamping=0;
            return rb;
        }
        static void Place(Rigidbody[] targets,StormPose pose)
        {
            for(int i=0;i<targets.Length;i++)
            {
                targets[i].transform.position=pose.Center+pose.Rotation*Vector3.right*(i-2)*2;
                if(!targets[i].isKinematic) targets[i].linearVelocity=Vector3.zero;
            }
            Physics.SyncTransforms();
        }
    }
}
