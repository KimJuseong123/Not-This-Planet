using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 1인칭 손은 물리 인형과 분리된 로컬 표시여야 한다. 여기서는 리소스 연결,
    /// 좌·우 입력, 시점 가시성, 물리 컴포넌트 부재를 검증한다.
    /// </summary>
    public sealed class FirstPersonHandViewTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        PlayerRig MakePlayer()
        {
            var player = PlayerRig.Create("손 표시 테스트", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, useModel: false);
            spawned.Add(player.gameObject);
            return player;
        }

        (PlayerRig Player, OrbitCamera Camera, FirstPersonHandView View) MakeView(bool firstPerson = true)
        {
            return MakeView(FirstPersonHandView.Settings.Default, firstPerson);
        }

        (PlayerRig Player, OrbitCamera Camera, FirstPersonHandView View)
            MakeView(FirstPersonHandView.Settings settings, bool firstPerson = true)
        {
            var player = MakePlayer();
            var camera = OrbitCamera.Create(player.Hips.transform, Color.black, firstPerson);
            spawned.Add(camera.gameObject);
            player.Cam = camera;
            var view = FirstPersonHandView.Create(player, camera, settings);
            Assert.IsNotNull(view, "카메라에 1인칭 손 표시를 붙이지 못했다.");
            return (player, camera, view);
        }

        [Test]
        public void CharacterHand_리소스에서양손을만든다()
        {
            var prefab = Resources.Load<GameObject>(FirstPersonHandView.ResourceKey);
            Assert.IsNotNull(prefab,
                "CharacterHand.prefab이 Resources에 없다. ArtLink.BuildPrefabs를 실행해야 한다.");

            var (player, _, view) = MakeView();

            Assert.IsNotNull(view.LeftHandRoot, "왼손 표시가 생성되지 않았다.");
            Assert.IsNotNull(view.RightHandRoot, "오른손 표시가 생성되지 않았다.");
            Assert.Greater(view.LeftHandRoot.GetComponentsInChildren<Renderer>(true).Length, 0,
                           "왼손 메시가 없다.");
            Assert.Greater(view.RightHandRoot.GetComponentsInChildren<Renderer>(true).Length, 0,
                           "오른손 메시가 없다.");

            Assert.IsTrue(view.LeftHandRoot.IsChildOf(view.transform), "왼손이 카메라 표시 계층 밖에 있다.");
            Assert.IsTrue(view.RightHandRoot.IsChildOf(view.transform), "오른손이 카메라 표시 계층 밖에 있다.");
            Assert.IsFalse(view.LeftHandRoot.IsChildOf(player.Doll.Root),
                           "왼손이 물리 Doll 아래에 있어 1인칭 숨김과 함께 사라질 수 있다.");
            Assert.IsFalse(view.RightHandRoot.IsChildOf(player.Doll.Root),
                           "오른손이 물리 Doll 아래에 있어 1인칭 숨김과 함께 사라질 수 있다.");

            foreach (var renderer in view.transform.GetComponentsInChildren<Renderer>(true))
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,
                                renderer.shadowCastingMode, "1인칭 손이 그림자를 투사한다.");
        }

        [Test]
        public void 표시손은물리컴포넌트를추가하지않는다()
        {
            var (_, _, view) = MakeView();
            var root = view.transform;

            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length,
                            "1인칭 손에 Collider가 있다.");
            Assert.AreEqual(0, root.GetComponentsInChildren<Rigidbody>(true).Length,
                            "1인칭 손에 Rigidbody가 있다.");
            Assert.AreEqual(0, root.GetComponentsInChildren<Joint>(true).Length,
                            "1인칭 손에 Joint가 있다.");
            Assert.AreEqual(0, root.GetComponentsInChildren<Animator>(true).Length,
                            "정적 손에 Animator가 있다.");
        }

        [Test]
        public void 클릭중인손만잡기포즈가된다()
        {
            var (player, _, view) = MakeView();

            player.SetInput(new InputFrame { GrabLeft = true });
            Tick(view);

            Assert.IsTrue(view.LeftGrabPoseActive, "왼쪽 클릭 중 왼손이 잡기 포즈가 아니다.");
            Assert.IsFalse(view.RightGrabPoseActive, "오른쪽을 누르지 않았는데 잡기 포즈가 됐다.");

            player.SetInput(new InputFrame());
            Tick(view);

            Assert.IsFalse(view.LeftGrabPoseActive, "클릭을 뗀 뒤 왼손이 잡기 포즈에 남아 있다.");
        }

        [Test]
        public void 설정값으로손위치와각도를조정한다()
        {
            var settings = FirstPersonHandView.Settings.Default;
            settings.leftIdlePosition = new Vector3(-0.33f, -0.08f, 0.44f);
            settings.leftIdleEuler = new Vector3(-55f, -12f, -6f);
            settings.poseSmoothTime = 0.001f;
            settings.rotationSmooth = 1000f;
            settings.maxSwayPosition = 0f;
            settings.maxSwayAngle = 0f;

            var (_, _, view) = MakeView(settings);
            Tick(view);

            Assert.That(view.LeftHandRoot.localPosition.x, Is.EqualTo(-0.33f).Within(0.02f));
            Assert.That(view.LeftHandRoot.localPosition.y, Is.EqualTo(-0.08f).Within(0.02f));
            Assert.That(view.LeftHandRoot.localPosition.z, Is.EqualTo(0.44f).Within(0.02f));
            Assert.Less(Quaternion.Angle(view.LeftHandRoot.localRotation,
                                         Quaternion.Euler(settings.leftIdleEuler)), 1f,
                        "설정한 왼손 대기 각도가 적용되지 않았다.");
        }

        [Test]
        public void 삼인칭에서는표시손을숨긴다()
        {
            var (_, camera, view) = MakeView();
            Assert.IsTrue(view.LeftHandRoot.gameObject.activeSelf, "1인칭 손이 처음부터 보이지 않는다.");

            camera.FirstPerson = false;
            Tick(view);
            Assert.IsFalse(view.LeftHandRoot.gameObject.activeSelf, "삼인칭에서 왼손이 보인다.");
            Assert.IsFalse(view.RightHandRoot.gameObject.activeSelf, "삼인칭에서 오른손이 보인다.");

            camera.FirstPerson = true;
            Tick(view);
            Assert.IsTrue(view.LeftHandRoot.gameObject.activeSelf, "1인칭 복귀 후 왼손이 나타나지 않는다.");
        }

        [Test]
        public void 로컬이아닌플레이어와탑승기절상태에서는표시손을숨긴다()
        {
            var (player, camera, view) = MakeView();

            player.IsLocal = false;
            Tick(view);
            Assert.IsFalse(view.LeftHandRoot.gameObject.activeSelf, "원격 플레이어의 1인칭 손이 보인다.");
            Assert.IsFalse(view.RightHandRoot.gameObject.activeSelf, "원격 플레이어의 1인칭 손이 보인다.");

            player.IsLocal = true;
            var seat = new GameObject("탑승 테스트 좌석");
            spawned.Add(seat);
            var seatBody = seat.AddComponent<Rigidbody>();
            Assert.IsTrue(player.TryBoardShip(seatBody, player.Hips.position), "탑승 상태를 만들지 못했다.");
            Tick(view);
            Assert.IsFalse(view.LeftHandRoot.gameObject.activeSelf, "탑승 중 1인칭 손이 보인다.");

            player.LeaveShip(Vector3.zero, Quaternion.identity);
            player.GoDown();
            camera.FirstPerson = true;
            Tick(view);
            Assert.IsFalse(view.LeftHandRoot.gameObject.activeSelf, "기절 중 1인칭 손이 보인다.");
        }

        static void Tick(FirstPersonHandView view)
        {
            // LateUpdate에서 카메라 뒤따르기와 입력 표시를 갱신한다. EditMode에서는
            // Unity 프레임이 자동 진행되지 않으므로 메시지로 해당 생명주기를 한 번 실행한다.
            view.SendMessage("LateUpdate");
        }
    }
}
