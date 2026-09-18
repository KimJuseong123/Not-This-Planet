using System.Collections;
using System.IO;
using Metagame.Gameplay.Interaction;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class OccludedInteractionRenderTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [Category("Heavy")]
        [UnityTest]
        public IEnumerator LocalCameraRendersOnlyOccludedOutlinesIncludingOverlappingTargets()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var scene = SceneManager.CreateScene("Stage1_Astra");
            SceneManager.SetActiveScene(scene);
            var sessionGo = new GameObject("Test session");
            var session = sessionGo.AddComponent<GameSession>();
            session.enabled = false;
            var rigGo = new GameObject("Test local player");
            var rig = rigGo.AddComponent<PlayerRig>();
            rig.enabled = false;
            var orbit = OrbitCamera.Create(rigGo.transform, Color.black);
            orbit.enabled = false;
            rig.Cam = orbit;
            session.LocalPlayer = rig;
            var camera = orbit.Cam;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.fieldOfView = 60;
            var rt = new RenderTexture(256, 256, 24);
            camera.targetTexture = rt;
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "Hidden interactable";
            target.transform.position = Vector3.forward * 6;
            target.AddComponent<Grabbable>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Occluder";
            wall.transform.position = Vector3.forward * 3;
            wall.transform.localScale = new Vector3(2, 2, 0.2f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.gray);
            target.GetComponent<Renderer>().sharedMaterial = material;
            wall.GetComponent<Renderer>().sharedMaterial = material;
            var snapshot = new Texture2D(256, 256, TextureFormat.RGB24, false);
            var originalQuality = QualitySettings.GetQualityLevel();
            try
            {
                yield return null;
                yield return null;
                Assert.Greater(ReadYellow(rt, snapshot, "occluded"), 10, "Hidden target needs an outline.");
                wall.AddComponent<Grabbable>();
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.Greater(ReadYellow(rt, snapshot, "behind-interactable"), 10,
                    "An interactable occluder must not erase the rear object's outline.");
                QualitySettings.SetQualityLevel(0, true);
                yield return null;
                yield return null;
                Assert.Greater(ReadYellow(rt, snapshot, "low-quality"), 10, "Low quality must keep guidance.");
                QualitySettings.SetQualityLevel(originalQuality, true);
                yield return null;
                wall.SetActive(false);
                yield return null;
                yield return null;
                Assert.AreEqual(0, ReadYellow(rt, snapshot, "visible"), "Visible objects need no extra outline.");
                wall.SetActive(true);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                yield return null;
                yield return null;
                Assert.AreEqual(0, ReadYellow(rt, snapshot, "offscreen"));
                camera.transform.rotation = Quaternion.identity;
                session.LocalPlayer = null;
                yield return null;
                yield return null;
                Assert.AreEqual(0, ReadYellow(rt, snapshot, "nonlocal"), "Other cameras must not draw local guidance.");
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalQuality, true);
                camera.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(snapshot);
                Object.Destroy(material);
            }
            yield return new ExitPlayMode();
        }

        static int ReadYellow(RenderTexture rt, Texture2D snapshot, string name)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            snapshot.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            snapshot.Apply();
            RenderTexture.active = previous;
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/occluded-interactions"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), snapshot.EncodeToPNG());
            var count = 0;
            foreach (var color in snapshot.GetPixels())
                if (color.r > 0.4f && color.g > 0.3f && color.b < 0.3f &&
                    color.r > color.b * 1.5f) count++;
            return count;
        }
    }
}