using NUnit.Framework;
using UnityEngine;
using System.Reflection;

namespace Metagame.Gameplay.Tests
{
    public class FirstPersonToolVisualTests
    {
        [Test]
        public void RemoteHeldPresentationDoesNotGrantAuthorityAndClearsOnDrop()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Props/Stage1/PF_Stage1_WeldingPlasmaGun.prefab");
            var root = Object.Instantiate(prefab);
            var player = PlayerRig.Create("Remote owner", Vector3.zero, Quaternion.identity, Color.white, Color.gray, useModel:false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            var tool = root.GetComponent<PlasmaWeldingTool>();
            try
            {
                tool.ApplyRemoteHeld(true);
                tool.ApplyRemotePresentation(player, (byte)PlasmaBeamMode.Weld, 0, Vector3.forward);
                typeof(ToolBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tool, null);
                Assert.IsFalse(tool.IsEquipped);
                Assert.IsNull(tool.Owner);
                Assert.AreSame(player, tool.ViewOwner);
                Assert.AreSame(tool, player.PresentationTool);
                Assert.IsTrue(tool.FirstPersonModel.gameObject.activeSelf);
                Assert.IsFalse(tool.WeldingProgressed);
                tool.ApplyRemoteHeld(false);
                Assert.IsNull(player.PresentationTool);
                Assert.IsFalse(tool.FirstPersonModel.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(player.gameObject); }
        }

        [Test]
        public void CopyHasOnlyPresentationAndRestoresOriginalVisibility()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var camera = new GameObject("View camera");
            source.AddComponent<Rigidbody>();
            source.AddComponent<MeteorBat>();
            var originalParent = source.transform.parent;
            var originalPosition = source.transform.position;
            var renderer = source.GetComponent<Renderer>();
            try
            {
                using (var view = new FirstPersonToolVisual(source.transform, camera.transform))
                {
                    view.SetVisible(true);
                    Assert.IsTrue(renderer.forceRenderingOff);
                    Assert.IsEmpty(view.Model.GetComponentsInChildren<Collider>(true));
                    Assert.IsEmpty(view.Model.GetComponentsInChildren<Rigidbody>(true));
                    Assert.IsEmpty(view.Model.GetComponentsInChildren<MonoBehaviour>(true));
                    var grip = new Vector3(0f, -0.4f, 0f);
                    var hand = new Vector3(0.3f, -0.2f, 0.5f);
                    view.SetPose(Vector3.zero, Quaternion.Euler(20, 30, 40), Vector3.one * 0.4f, grip, hand);
                    Assert.That(Vector3.Distance(view.Model.TransformPoint(grip), hand), Is.LessThan(0.0001f));
                    Assert.AreEqual(originalPosition, source.transform.position);
                    Assert.AreSame(originalParent, source.transform.parent);
                    view.SetVisible(false);
                    Assert.IsFalse(renderer.forceRenderingOff);
                }
                Assert.AreEqual(0, camera.transform.childCount);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(camera); }
        }

        [Test]
        public void FirstPersonPoseCannotMoveGameplayMuzzle()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Props/Stage1/PF_Stage1_WeldingPlasmaGun.prefab");
            var root = Object.Instantiate(prefab);
            var player = PlayerRig.Create("View isolation", Vector3.zero, Quaternion.identity, Color.white, Color.gray, useModel:false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            var tool = root.GetComponent<PlasmaWeldingTool>();
            try
            {
                tool.Equip(player, root.GetComponent<Rigidbody>());
                var update = typeof(ToolBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                update.Invoke(tool, null);
                var before = tool.Muzzle.position;
                typeof(PlasmaWeldingTool).GetField("firstPersonVisualLocalPosition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(tool, new Vector3(20, 30, 40));
                update.Invoke(tool, null);
                Assert.That(Vector3.Distance(tool.Muzzle.position, before), Is.LessThan(0.0001f));
                Assert.IsNotNull(tool.FirstPersonModel);
                tool.Unequip();
                Assert.IsFalse(tool.FirstPersonModel.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(player.gameObject); }
        }
    }
}
