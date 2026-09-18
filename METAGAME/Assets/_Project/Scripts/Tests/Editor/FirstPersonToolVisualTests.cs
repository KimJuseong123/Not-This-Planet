using NUnit.Framework;
using Metagame.Core;
using UnityEngine;
using System.Reflection;

namespace Metagame.Gameplay.Tests
{
    public class FirstPersonToolVisualTests
    {
        [TestCase(PlasmaBeamMode.Weld)]
        [TestCase(PlasmaBeamMode.Cut)]
        public void AirBeamStaysParallelToAimWhenWorldGunMovesRelativeToView(PlasmaBeamMode mode)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Props/Stage1/PF_Stage1_WeldingPlasmaGun.prefab");
            var root = Object.Instantiate(prefab);
            var player = PlayerRig.Create("Moving beam owner", Vector3.zero,
                Quaternion.identity, Color.white, Color.gray, useModel: false);
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            player.Cam = camera;
            var tool = root.GetComponent<PlasmaWeldingTool>();
            var update = typeof(ToolBase).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            GameObject wall = null;
            try
            {
                tool.Equip(player, root.GetComponent<Rigidbody>());
                foreach (var offset in new[] { Vector3.zero, Vector3.right * 0.3f, Vector3.left * 0.3f, Vector3.forward * 0.5f })
                {
                    camera.transform.position = new Vector3(20f, 20f, 20f) + offset;
                    tool.SetInput(new ToolInputFrame { PrimaryHeld = mode == PlasmaBeamMode.Weld,
                            SecondaryHeld = mode == PlasmaBeamMode.Cut },
                        new ToolAimContext(player, camera, Vector3.forward));
                    foreach (var petals in root.GetComponentsInChildren<PlasmaGunPetalAnimator>(true))
                        petals.SnapToMode(mode);
                    Physics.SyncTransforms();
                    update.Invoke(tool, null);
                    var line = tool.GetComponent<LineRenderer>();
                    Assert.IsTrue(line.enabled);
                    Assert.IsFalse(tool.BeamHasContact);
                    Assert.That(Vector3.Angle(line.GetPosition(1) - line.GetPosition(0), Vector3.forward),
                        Is.LessThan(0.01f), "화면 총구와 물리 총구 사이의 위치 차이가 허공 빔 방향을 바꾸면 안 된다.");
                }
                // 접촉 검증은 화면 중앙 레이가 작업 사거리 안의 벽을 보도록 배치한다.
                camera.transform.SetPositionAndRotation(tool.Muzzle.position + Vector3.back * 0.3f,
                    Quaternion.identity);
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = tool.Muzzle.position + Vector3.forward * 0.4f;
                wall.transform.localScale = new Vector3(2f, 2f, 0.05f);
                Physics.SyncTransforms();
                update.Invoke(tool, null);
                Assert.IsTrue(tool.BeamHasContact);
                Assert.IsTrue(tool.TryGetBeamHit(Vector3.forward, out var hit));
                Assert.That(Vector3.Distance(tool.GetComponent<LineRenderer>().GetPosition(1), hit.point),
                    Is.LessThan(0.001f), "접촉 중에는 표시를 이동시켜 실제 판정/가이드 위치와 어긋나면 안 된다.");
            }
            finally { Object.DestroyImmediate(wall); Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(player.gameObject); }
        }

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

                    // **감추는 수단이 아니라 결과를 잰다**(S15P21B202-489).
                    //
                    // 예전에는 `Assert.IsTrue(renderer.forceRenderingOff)` 였다. 그
                    // 플래그는 그림자 패스까지 끄므로 들고 있는 동안 도구 그림자가
                    // 사라졌고, 고치려면 감추는 수단을 shadowCastingMode 로 바꿔야
                    // 했다 — 그러자 동작은 옳은데 이 단언이 깨졌다.
                    //
                    // 이 검증의 이름이 말하는 것은 "사본은 표시뿐이고 원본 표시는
                    // 되돌아온다" 이지 "어떤 플래그를 쓴다" 가 아니다. 수단을 재면
                    // 다른 옳은 구현을 거짓으로 몬다.
                    Assert.IsFalse(DrawnInColorPass(renderer),
                        "진짜 도구가 카메라에 그려지면 사본과 겹쳐 두 개로 보인다.");
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
                    Assert.IsTrue(DrawnInColorPass(renderer),
                        "놓으면 원본이 다시 그려져야 한다 — 3인칭에서 도구가 보인다.");
                    Assert.IsFalse(renderer.forceRenderingOff, "플래그도 원래대로.");
                }
                Assert.AreEqual(0, camera.transform.childCount);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(camera); }
        }

        /// <summary>
        /// 이 렌더러가 카메라의 색 패스에 들어가는가. "감춰졌다" 를 수단과 무관하게
        /// 재려고 둔다(S15P21B202-489) — <c>forceRenderingOff</c> · <c>enabled</c> ·
        /// <c>ShadowsOnly</c> 중 어느 것으로 감춰도 답이 같다.
        /// </summary>
        static bool DrawnInColorPass(Renderer renderer) =>
            !renderer.forceRenderingOff
            && renderer.enabled
            && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

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
