using System.Collections;
using System.IO;
using Metagame.Bootstrap;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using Metagame.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class PracticeLobbyTests
    {
        [Test]
        public void HandToolAndClampSettingsMatchAuthoredStage1()
        {
            var stage=EditorSceneManager.OpenScene("Assets/Scenes/Stage1_Astra.unity");
            var practice=EditorSceneManager.OpenScene(LobbyTransition.PracticeScenePath,OpenSceneMode.Additive);
            var source=InScene<SpaceBootstrap>(stage);
            var target=InScene<SpaceBootstrap>(practice);
            Assert.That(target,Is.Not.Null);
            Assert.That(JsonUtility.ToJson(target.firstPersonHandSettings),Is.EqualTo(JsonUtility.ToJson(source.firstPersonHandSettings)));
            Assert.That(target.firstPerson,Is.EqualTo(source.firstPerson));
            Assert.That(target.useModel,Is.EqualTo(source.useModel));
            Assert.That(target.gravity,Is.EqualTo(new Vector3(0,-9.81f,0)));
            AssertTuning(InScene<MeteorBat>(stage),InScene<MeteorBat>(practice));
            AssertTuning(InScene<PlasmaWeldingTool>(stage),InScene<PlasmaWeldingTool>(practice));
            AssertTuning(InScene<PartClamp>(stage),InScene<PartClamp>(practice));
            AssertHierarchy(InScene<MeteorBat>(stage).transform,InScene<MeteorBat>(practice).transform,true);
            AssertHierarchy(InScene<PlasmaWeldingTool>(stage).transform,InScene<PlasmaWeldingTool>(practice).transform,true);
            AssertHierarchy(InScene<PartClamp>(stage).transform,InScene<PartClamp>(practice).transform,true);
        }

        static T InScene<T>(Scene scene) where T:Component
        {
            foreach(var root in scene.GetRootGameObjects())
            {
                var component=root.GetComponentInChildren<T>(true);
                if(component!=null) return component;
            }
            return null;
        }

        static void AssertTuning(Component source,Component target)
        {
            Assert.That(target,Is.Not.Null);
            var a=new SerializedObject(source); var b=new SerializedObject(target);
            var field=a.GetIterator();
            var enterChildren=true;
            while(field.Next(enterChildren))
            {
                enterChildren=field.propertyType!=SerializedPropertyType.ObjectReference;
                if(field.propertyPath.StartsWith("m_")) continue;
                var other=b.FindProperty(field.propertyPath);
                Assert.That(other,Is.Not.Null,field.propertyPath);
                switch(field.propertyType)
                {
                    case SerializedPropertyType.Float: Assert.That(other.doubleValue,Is.EqualTo(field.doubleValue),field.propertyPath); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum: Assert.That(other.intValue,Is.EqualTo(field.intValue),field.propertyPath); break;
                    case SerializedPropertyType.Boolean: Assert.That(other.boolValue,Is.EqualTo(field.boolValue),field.propertyPath); break;
                    case SerializedPropertyType.Vector3: Assert.That(other.vector3Value,Is.EqualTo(field.vector3Value),field.propertyPath); break;
                    case SerializedPropertyType.Color: Assert.That(other.colorValue,Is.EqualTo(field.colorValue),field.propertyPath); break;
                }
            }
        }

        static void AssertHierarchy(Transform source,Transform target,bool root=false)
        {
            Assert.That(target.childCount,Is.EqualTo(source.childCount),source.name);
            Assert.That(Vector3.Distance(target.lossyScale,source.lossyScale),Is.LessThan(0.0001f),source.name+" scale");
            if(!root)
            {
                Assert.That(target.localPosition,Is.EqualTo(source.localPosition),source.name);
                Assert.That(Quaternion.Angle(target.localRotation,source.localRotation),Is.LessThan(0.01f),source.name);
            }
            Assert.That(target.GetComponents<Collider>().Length,Is.EqualTo(source.GetComponents<Collider>().Length),source.name);
            if(source.TryGetComponent<MeshFilter>(out var mesh))
                Assert.That(target.GetComponent<MeshFilter>().sharedMesh,Is.SameAs(mesh.sharedMesh));
            for(int i=0;i<source.childCount;i++) AssertHierarchy(source.GetChild(i),target.GetChild(i));
        }

        [UnityTest]
        public IEnumerator RealPlayerInputPicksUpBothToolsAndPullsTheAuthoredClamp()
        {
            EditorSceneManager.OpenScene(LobbyTransition.PracticeScenePath);
            yield return new EnterPlayMode();
            // EditMode's test runner must wait on the game clock explicitly after EnterPlayMode.
            // Yielding WaitForSeconds here can resume without the requested physics time passing.
            for(var until=Time.time+(1f);Time.time<until;) yield return null;
            var boot=Object.FindAnyObjectByType<SpaceBootstrap>();
            Assert.That(boot.Session.CanToggleTablet,Is.True);
            boot.Exploration.ToggleTablet();
            Assert.That(boot.Exploration.TabletOpen,Is.True);
            boot.Exploration.ToggleTablet();
            boot.Session.ScriptedInput=true;
            var player=boot.Player;
            player.Cam.enabled=false; // Deterministic test aim; gameplay still runs through the production rig.
            foreach(ToolBase tool in new ToolBase[]{Object.FindAnyObjectByType<MeteorBat>(),Object.FindAnyObjectByType<PlasmaWeldingTool>()})
            {
                player.SetInput(new InputFrame());
                var target=tool.GetComponentInChildren<Collider>().bounds.center;
                var foot=target+Vector3.back*0.8f; foot.y=0.15f;
                player.Teleport(foot,Quaternion.identity);
                AimAt(player,target);
                player.RefreshInteractionFocus();
                Assert.That(player.CurrentInteractionTarget.Grabbable,Is.SameAs(tool.GetComponent<Grabbable>()),tool.name+" focus");
                player.SetInput(new InputFrame{GrabLeft=true});
                for(var until=Time.time+(0.2f);Time.time<until;) yield return null;
                Assert.That(player.ActiveTool,Is.SameAs(tool),tool.name+" pickup via click");
                player.SetInput(new InputFrame());
                for(var until=Time.time+(0.1f);Time.time<until;) yield return null;
                Assert.That(tool.FirstPersonModel,Is.Not.Null,tool.name+" first-person model");
                Assert.That(tool.FirstPersonModel.gameObject.activeSelf,Is.True);
                var swung=false;
                System.Action<MeteorBat,bool> onSwing=(_,__)=>swung=true;
                if(tool is MeteorBat observedBat) observedBat.Swung+=onSwing;
                player.SetInput(new InputFrame{ToolPrimary=true});
                for(var until=Time.time+(0.3f);Time.time<until;) yield return null;
                if(tool is MeteorBat bat)
                {
                    bat.Swung-=onSwing;
                    Assert.That(swung,Is.True,"A click must trigger the production bat swing.");
                }
                if(tool is PlasmaWeldingTool gun) Assert.That(gun.BeamMode,Is.EqualTo(PlasmaBeamMode.Weld));
                player.SetInput(new InputFrame());
                for(var until=Time.time+(0.6f);Time.time<until;) yield return null;
                AimAt(player,player.Doll.Head.position+Vector3.forward*3f);
                yield return null;
                CaptureRuntime(player.Cam.Cam,tool is MeteorBat?"stage1-bat":"stage1-welder");
                player.DropEquipment();
                player.SetInput(new InputFrame());
                for(var until=Time.time+(0.1f);Time.time<until;) yield return null;
            }
            var pin=Object.FindAnyObjectByType<PartClamp>();
            var pinTarget=pin.GetComponentInChildren<Collider>().bounds.center;
            var pinFoot=pinTarget+Vector3.back*0.45f; pinFoot.y=0.15f;
            player.Teleport(pinFoot,Quaternion.identity);
            for(var until=Time.time+(0.2f);Time.time<until;) yield return null;
            AimAt(player,pinTarget);
            player.RefreshInteractionFocus();
            Assert.That(player.CurrentInteractionTarget.CanInteract,Is.True,
                $"Clamp focus: {player.CurrentInteractionTarget.Kind}; pin={pinTarget}; hips={player.Doll.Hips.position}");
            player.SetInput(new InputFrame{GrabLeft=true,GrabRight=true});
            for(var until=Time.time+(0.25f);Time.time<until;) yield return null;
            Assert.That(player.ClampGrabbed,Is.True,"Both clicks must grab the actual Stage1 clamp.");
            Assert.That(pin.Progress,Is.EqualTo(0f),"Holding still must not release a clamp.");
            player.SetInput(new InputFrame{GrabLeft=true,GrabRight=true,Move=Vector2.down});
            for(var step=0;step<15 && !pin.IsReleased;step++)
            {
                for(var until=Time.time+(0.2f);Time.time<until;) yield return null;
            }
            Assert.That(pin.IsReleased,Is.True,"Click-hold + backwards movement must release the clamp.");
            Assert.That(pin.Part.State,Is.EqualTo(PartState.Free));
            player.SetInput(new InputFrame());
            // Dispose the tested tool owners before Unity unloads their independently-created guides.
            // The production guide's scene-unload ordering issue is recorded separately, not patched here.
            foreach(var tool in Object.FindObjectsByType<ToolBase>()) Object.Destroy(tool.gameObject);
            yield return null;
            yield return new ExitPlayMode();
        }

        static void AimAt(PlayerRig player,Vector3 target)
        {
            var camera=player.Cam;
            camera.transform.position=player.Doll.Head.position;
            camera.transform.LookAt(target);
            var angles=camera.transform.eulerAngles;
            camera.Yaw=angles.y;
            camera.Pitch=Mathf.DeltaAngle(0,angles.x);
            Physics.SyncTransforms();
        }
        [Test]
        public void LeavingOrOpeningMenuRestartsDeparture()
        {
            var state = new PracticeDeparture();
            state.Tick(true, true, 2f, 3f);
            Assert.That(state.Ready, Is.False);
            state.Tick(false, true, 1f, 3f);
            Assert.That(state.Elapsed, Is.Zero);
            state.Tick(true, true, 2f, 3f);
            state.Tick(true, false, 1f, 3f);
            Assert.That(state.Ready, Is.False);
            Assert.That(state.Elapsed, Is.Zero);
            state.Tick(true, true, 3f, 3f);
            Assert.That(state.Ready, Is.True);
        }

        [Test]
        public void InvalidTimeDoesNotCompleteDeparture()
        {
            var state = new PracticeDeparture();
            state.Tick(true, true, float.NaN, 3f);
            state.Tick(true, true, float.PositiveInfinity, 3f);
            state.Tick(true, true, -1f, 3f);
            Assert.That(state.Elapsed, Is.Zero);
            Assert.That(state.Ready, Is.False);
        }

        [Test]
        public void PracticeSceneIsIncludedInBuild()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyTransition.PracticeScenePath), Is.Not.Null);
            Assert.That(SceneUtility.GetBuildIndexByScenePath(LobbyTransition.PracticeScenePath), Is.GreaterThanOrEqualTo(0));
        }

        [UnityTest]
        public IEnumerator AuthoredRoomSpawnsProductionRigAndExercisesRealPracticeObjects()
        {
            EditorSceneManager.OpenScene(LobbyTransition.PracticeScenePath);
            yield return new EnterPlayMode();
            for(var until=Time.time+(1.5f);Time.time<until;) yield return null;
            var bootstrap = Object.FindAnyObjectByType<PracticeLobbyBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.Player, Is.Not.Null);
            Assert.That(bootstrap.Player.Cam.Cam.enabled, Is.True);
            var shared=Object.FindAnyObjectByType<SpaceBootstrap>();
            Assert.That(shared, Is.Not.Null);
            Assert.That(bootstrap.Session, Is.SameAs(shared.Session));
            Assert.That(bootstrap.Player, Is.SameAs(shared.Player));
            Assert.That(bootstrap.Session.Exploration, Is.SameAs(shared.Exploration));
            Assert.That(shared.Hud.Local, Is.SameAs(shared.Player));
            Assert.That(shared.Session.Transport, Is.SameAs(shared.Transport));
            Assert.That(shared.Session.Recovery, Is.Not.Null);
            Assert.That(shared.ExplorationHud, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<GameSession>().Length, Is.EqualTo(1));
            Assert.That(Physics.gravity.y, Is.EqualTo(-9.81f).Within(0.001f));
            Assert.That(Object.FindObjectsByType<Hazards.MeteorSpawner>().Length, Is.Zero);
            Assert.That(bootstrap.Player.Hips.position.y, Is.InRange(0.1f, 3f), "Player must stand on the authored floor.");
            bootstrap.Session.ScriptedInput = true;

            var bat = Object.FindAnyObjectByType<MeteorBat>();
            var gun = Object.FindAnyObjectByType<PlasmaWeldingTool>();
            Assert.That(bat.GetComponent<Grabbable>().Role, Is.EqualTo(GrabbableRole.Tool));
            Assert.That(gun.GetComponent<Grabbable>().Role, Is.EqualTo(GrabbableRole.Tool));
            Assert.That(bat.GetComponent<Rigidbody>().position.y, Is.GreaterThan(0f));
            Assert.That(gun.GetComponent<Rigidbody>().position.y, Is.GreaterThan(0f));
            bat.Equip(bootstrap.Player, bat.GetComponent<Rigidbody>());
            Assert.That(bat.IsEquipped, Is.True);
            bat.Unequip();
            gun.Equip(bootstrap.Player, gun.GetComponent<Rigidbody>());
            Assert.That(gun.IsEquipped, Is.True);
            gun.Unequip();

            var target = Object.FindAnyObjectByType<PracticeMeteorTarget>();
            var rb = target.GetComponent<Rigidbody>();
            Physics.SyncTransforms();
            Assert.That(bat.Swing(rb.position + Vector3.forward * 1.5f, Vector3.back), Is.True);
            Assert.That(bat.LastPushed, Is.EqualTo(rb));
            Assert.That(target.HitCount, Is.EqualTo(1));
            for(var until=Time.time+(0.4f);Time.time<until;) yield return null;
            Assert.That(rb.linearVelocity.sqrMagnitude, Is.GreaterThan(0.01f));

            var pin = Object.FindAnyObjectByType<PartClamp>();
            var cargo = pin.GetComponentInParent<SpaceshipPart>();
            Assert.That(cargo.State, Is.EqualTo(PartState.Locked));
            Assert.That(pin.TryJoinPull(), Is.True);
            for (int i = 0; i < 100 && !pin.IsReleased; i++) pin.Pull(0.1f);
            Assert.That(pin.IsReleased, Is.True);
            Assert.That(cargo.State, Is.EqualTo(PartState.Free));
            Assert.That(cargo.GetComponent<Rigidbody>().isKinematic, Is.False);

            var lower = GameObject.Find("Mini rocket lower").GetComponent<SpaceshipPart>();
            var box = lower.GetComponent<BoxCollider>();
            var from = box.bounds.center + Vector3.back * 2f;
            Assert.That(box.Raycast(new Ray(from, Vector3.forward), out var hit, 4f), Is.True);
            Assert.That(WeldBead.TryApplyAt(hit, 0.1f, false, out var bead), Is.True);
            Assert.That(bead.BlobCount, Is.GreaterThan(0));
            var beadCollider = bead.transform.Find(WeldBead.ContainerName).GetComponentInChildren<Collider>();
            Assert.That(beadCollider.Raycast(new Ray(beadCollider.bounds.center + Vector3.back, Vector3.forward), out var cutHit, 2f), Is.True);
            Assert.That(WeldBead.TryApplyAt(cutHit, 0.2f, true, out _), Is.True);

            CaptureRuntime(bootstrap.Player.Cam.Cam);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator ResetReloadsPracticeThenDepartureLoadsTheExistingStage()
        {
            EditorSceneManager.OpenScene(LobbyTransition.PracticeScenePath);
            yield return new EnterPlayMode();
            for(var until=Time.time+(1f);Time.time<until;) yield return null;
            var previous = Object.FindAnyObjectByType<PracticeLobbyBootstrap>();
            previous.Session.ScriptedInput = true;
            var oldPin = Object.FindAnyObjectByType<PartClamp>();
            Assert.That(oldPin.TryJoinPull(), Is.True);
            for (int i=0; i<100 && !oldPin.IsReleased; i++) oldPin.Pull(0.1f);
            Assert.That(oldPin.IsReleased, Is.True);
            Assert.That(LobbyTransition.Ensure().BeginPractice(), Is.True);
            float deadline=Time.realtimeSinceStartup+25f;
            while(LobbyTransition.Ensure().IsBusy && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(LobbyTransition.Ensure().IsBusy, Is.False, "Practice reset timed out.");
            var current=Object.FindAnyObjectByType<PracticeLobbyBootstrap>();
            Assert.That(current, Is.Not.Null);
            Assert.That(previous==null, Is.True, "Reset must unload the original room.");
            Assert.That(Object.FindAnyObjectByType<PartClamp>().IsReleased, Is.False);
            Assert.That(Object.FindObjectsByType<PracticeLobbyBootstrap>().Length, Is.EqualTo(1));
            Assert.That(LobbyTransition.Ensure().BeginSolo(), Is.True);
            deadline=Time.realtimeSinceStartup+35f;
            while(LobbyTransition.Ensure().IsBusy && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(LobbyTransition.Ensure().IsBusy, Is.False, "Departure timed out.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(StageCatalog.DefaultStageScene));
            Assert.That(Object.FindAnyObjectByType<PracticeLobbyBootstrap>(), Is.Null);
            Assert.That(Object.FindAnyObjectByType<SpaceBootstrap>(), Is.Not.Null);
            Assert.That(Physics.gravity.y, Is.EqualTo(-5f).Within(0.001f), "Stage1 must restore its own gravity.");
            yield return new ExitPlayMode();
        }

        static void CaptureRuntime(Camera camera,string name="runtime")
        {
            var path = Path.GetFullPath("../.local-validation/lobby-preview/"+name+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var rt = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var target = camera.targetTexture;
            try
            {
                camera.targetTexture = rt;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = target; RenderTexture.active = previous;
                Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
            }
        }
    }
}
