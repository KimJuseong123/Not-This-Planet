using System.Collections;
using System.IO;
using System.Linq;
using Metagame.Bootstrap;
using Metagame.Gameplay.Parts;
using Metagame.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class Stage2ArrivalTests
    {
        static double clock;
        [UnitySetUp] public IEnumerator Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(Application.isPlaying)yield return new ExitPlayMode();}
        [TestCase(1)] [TestCase(4)]
        public void CommonClockIncludesCrashBeforeStage2(int players)
        {
            float start=DemoEnding.BoardingDuration(players)+DemoEnding.LaunchSeconds+DemoEnding.PhotographSeconds+DemoEnding.EndTitleSeconds;
            Assert.That(DemoEnding.StageAt(start,players),Is.EqualTo(DemoEndingStage.SpaceDrift));
            Assert.That(DemoEnding.StageAt(start+8,players),Is.EqualTo(DemoEndingStage.MeteorImpact));
            Assert.That(DemoEnding.StageAt(start+14,players),Is.EqualTo(DemoEndingStage.EmergencyDescent));
            Assert.That(DemoEnding.StageAt(start+22.99f,players),Is.Not.EqualTo(DemoEndingStage.Lobby));
            Assert.That(DemoEnding.StageAt(start+23,players),Is.EqualTo(DemoEndingStage.Lobby));
        }
        [UnityTest]
        public IEnumerator AssembledSnapshotBreaksApartAndLateJoinSeeksDeterministically()
        {
            var factory=new GameObject("Snapshot factory");factory.SetActive(false);
            var snapshot=factory.AddComponent<Stage1EndingBootstrap>().CreatePreview();
            snapshot.Parts[1].Position+=new Vector3(1.2f,0,.4f);snapshot.Parts[1].Rotation=Quaternion.Euler(0,18,7);
            var root=new GameObject("Arrival fixture");root.SetActive(false);var ending=root.AddComponent<DemoEnding>();clock=0;
            ending.ConfigurePlayback(snapshot,()=>clock);
            var prefabs=PartSpec.All.Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Spaceships/Stage1/"+p.SkinPrefab+".prefab")).ToArray();
            var view=root.AddComponent<DemoEndingView>();int continued=0;view.Configure(ending,()=>{continued++;return true;},prefabs);root.SetActive(true);view.Prepare();
            var ship=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Completed Ship");
            var part=ship.Find(snapshot.Parts[1].Name);var home=part.localPosition;
            Assert.That(home,Is.EqualTo(snapshot.Parts[1].Position-snapshot.Parts[0].Position));
            void Sample(float t){clock=ending.ArrivalStartsAt+t;view.Tick();}
            Sample(3);Assert.That(ending.Finished,Is.False);Assert.That(part.localPosition,Is.EqualTo(home));Capture(root,"01-space-drift");
            Sample(7.3f);Capture(root,"02-incoming-meteor");
            Sample(8.3f);Assert.That(Vector3.Distance(part.localPosition,home),Is.GreaterThan(.05f));Capture(root,"03-impact");
            Sample(11);var position=part.position;var rotation=part.rotation;Capture(root,"04-broken-ship");
            Sample(18.5f);Capture(root,"05-emergency-descent");Assert.That(continued,Is.Zero);
            Sample(11);Assert.That(Vector3.Distance(position,part.position),Is.LessThan(.001f));Assert.That(Quaternion.Angle(rotation,part.rotation),Is.LessThan(.01f));
            Assert.That(root.GetComponentsInChildren<Mirror.NetworkIdentity>(),Is.Empty);
            Sample(22.9f);Assert.That(continued,Is.Zero);Sample(23);Assert.That(continued,Is.EqualTo(1));view.Tick();Assert.That(continued,Is.EqualTo(1));
            yield return null;
        }
        static void Capture(GameObject root,string name)
        {
            Directory.CreateDirectory("../artifacts/stage2-arrival");var camera=root.GetComponentInChildren<Camera>();
            var rt=new RenderTexture(1600,900,24);rt.Create();var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes("../artifacts/stage2-arrival/"+name+".png",image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;Object.Destroy(image);rt.Release();Object.Destroy(rt);}
        }
    }
}
