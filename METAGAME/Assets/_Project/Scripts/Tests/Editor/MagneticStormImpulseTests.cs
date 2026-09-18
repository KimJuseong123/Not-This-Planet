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

        [Category("Heavy")]
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

        [Test]
        public void WakeStaysOnPassedGroundAndFadesWithoutFrameHistory()
        {
            var settings = new AtmosphereProfile.DustStormSettings { enabled = true, wakeDuration = 4 };
            var bounds = new Bounds(Vector3.zero, new Vector3(80, 10, 80));
            var schedule = new DustStormSchedule(settings);
            var past = schedule.Evaluate(14, bounds);
            var ground = past.Center - past.Direction * past.HalfExtents.z;
            var a = schedule.EvaluateWake(15, bounds);
            var b = new DustStormSchedule(settings.WithSeed(settings.seed)).EvaluateWake(16, bounds);
            Assert.That(a.StrengthAt(ground), Is.EqualTo(.5625f).Within(.0001));
            Assert.That(b.StrengthAt(ground), Is.EqualTo(.25f).Within(.0001));
            Assert.That(schedule.Evaluate(16, bounds).Contains(ground), Is.False);
            Assert.That(schedule.EvaluateWake(18.01, bounds).StrengthAt(ground), Is.Zero);
            Assert.That(a.StrengthAt(past.Center + Vector3.up * 100), Is.Zero);
            settings.wakeDuration = 0;
            Assert.That(schedule.EvaluateWake(16, bounds).Pose.IsActive, Is.False);
            Assert.That(schedule.EvaluateWake(double.NaN, bounds).Pose.IsActive, Is.False);
        }

        [TestCase(20260903)]
        [TestCase(87654)]
        [TestCase(7)]
        public void WakeExtendsOppositeActualMotionNeverAcrossIt(int seed)
        {
            var settings = new AtmosphereProfile.DustStormSettings { enabled = true, seed = seed };
            var bounds = new Bounds(Vector3.zero, new Vector3(80, 10, 80));
            var schedule = new DustStormSchedule(settings);
            var pose = schedule.Evaluate(16, bounds);
            var next = schedule.Evaluate(16.1, bounds);
            var motion = (next.Center - pose.Center).normalized;
            var wake = schedule.EvaluateWake(16, bounds);
            var offset = wake.Pose.Center - pose.Center;
            Assert.That(Vector3.Dot(offset, motion), Is.LessThan(0));
            Assert.That(Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, motion))), Is.LessThan(.001));
            var newest = wake.Pose.Center + wake.Pose.Direction * wake.Pose.HalfExtents.z;
            Assert.That(Vector3.Distance(newest, pose.Center - motion * pose.HalfExtents.z), Is.LessThan(.001));
            Assert.That(wake.StrengthAt(newest - motion * .1f), Is.GreaterThan(wake.StrengthAt(wake.Pose.Center)));
        }

        [Test]
        public void WakeSurvivesEndOfSweepThenExpiresAndRespectsSeed()
        {
            var settings = new AtmosphereProfile.DustStormSettings { enabled = true, wakeDuration = 4 };
            var bounds = new Bounds(Vector3.zero, new Vector3(80, 10, 80));
            var schedule = new DustStormSchedule(settings);
            double end = 11d + (new Vector2(80, 80).magnitude + 17f) / 6f;
            Assert.That(schedule.Evaluate(end + 1, bounds).IsActive, Is.False);
            var wake = schedule.EvaluateWake(end + 1, bounds, 1);
            Assert.That(wake.Pose.IsActive, Is.True);
            Assert.That(wake.HeadAge, Is.EqualTo(1).Within(.0001));
            Assert.That(wake.TailAge, Is.EqualTo(4).Within(.0001));
            Assert.That(schedule.EvaluateWake(end + 4.01, bounds, 1).Pose.IsActive, Is.False);
            var other = new DustStormSchedule(settings.WithSeed(87654)).EvaluateWake(end + 1, bounds, 1);
            Assert.That(Vector3.Distance(wake.Pose.Center, other.Pose.Center), Is.GreaterThan(1));
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator ResidualImpulseIsWeakerAndStillRequiresAuthorityAndCooldown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
            profile.dustStorm.enabled = true;
            profile.dustStorm.wakeDuration = 4;
            profile.dustStorm.wakeInfluence = .2f;
            var bounds = new Bounds(Vector3.zero, new Vector3(80, 10, 80));
            var schedule = new DustStormSchedule(profile.dustStorm);
            var wake = schedule.EvaluateWake(16, bounds);
            var root = new GameObject("Residual impulse test");
            var hazard = root.AddComponent<Stage1DustStormHazard>();
            hazard.Configure(profile, bounds);
            var body = Body("Wake prop", root.transform);
            body.gameObject.AddComponent<Grabbable>();
            body.gameObject.AddComponent<SphereCollider>().radius = .2f;
            var position = wake.Pose.Center;
            body.position = position;
            Physics.SyncTransforms();
            hazard.FixedTick(16);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(.001));
            hazard.SetPhysicsAuthority(true);
            hazard.FixedTick(16);
            yield return new WaitForFixedUpdate();
            var expected = (wake.Pose.Direction * 18f + Vector3.up * 4f)
                * .2f * wake.StrengthAt(position);
            Assert.That(Vector3.Distance(body.linearVelocity, expected), Is.LessThan(.01));
            body.position = position;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            hazard.FixedTick(16.2);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(.001));
            profile.dustStorm.wakeInfluence = 0;
            hazard.FixedTick(17.2);
            yield return new WaitForFixedUpdate();
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(.001));
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
