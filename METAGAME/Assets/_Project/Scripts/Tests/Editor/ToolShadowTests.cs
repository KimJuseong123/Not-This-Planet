using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 들고 있는 방망이·총이 그림자를 남기는지 검증한다(S15P21B202-489).
    ///
    /// <para><b>무엇이 문제였나.</b> 1인칭에서는 진짜 도구를 감추고 카메라에 붙인 사본을
    /// 그린다. 감추는 수단이 <c>forceRenderingOff</c> 였는데 그 플래그는 <b>그림자
    /// 패스도 끈다</b>. 사본은 일부러 <c>shadowCastingMode = Off</c> 라(카메라 자식이고
    /// 1인칭 자세·크기여서 그림자가 엉뚱해진다), 결과적으로 <b>그림자가 어디에도
    /// 없었다.</b></para>
    ///
    /// <para><b>모드만 봐서는 못 찾는다.</b> 실측에서 원본의 <c>shadowCastingMode</c> 는
    /// <c>On</c> 인 채였다 — 정상으로 보인다. 끄고 있던 것은 옆에 있는 별도 플래그다.
    /// 그래서 이 스위트는 <b>두 값을 함께</b> 본다.</para>
    ///
    /// <para><see cref="Player.Doll.SetVisible"/> 이 1인칭에서 몸에 대해 이미 옳게 하고
    /// 있다 — "발밑을 읽는 단서가 제 그림자"(기획 §10.1). 몸에 대한 그 판단을 손에 든
    /// 물건에도 적용한 것이 이 이슈다.</para>
    /// </summary>
    public sealed class ToolShadowTests
    {
        readonly List<GameObject> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // ── 들고 있는 동안 ──────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b></summary>
        [Test]
        public void WhileHeld_TheRealToolStillCastsAShadow()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();

            Hold(tool, visual =>
            {
                Assert.IsFalse(original.forceRenderingOff,
                    "이 플래그가 켜져 있으면 그림자 패스에도 안 들어간다 — "
                    + "shadowCastingMode 가 무엇이든 그림자가 없다.");
                Assert.AreEqual(ShadowCastingMode.ShadowsOnly, original.shadowCastingMode,
                    "메시는 빼고 그림자는 남긴다(§10.1 · Doll.SetVisible 과 같은 방법).");
            });
        }

        /// <summary>
        /// 그림자가 <b>하나뿐</b>이어야 한다. 사본까지 그림자를 만들면 얼굴 옆에 거대한
        /// 방망이가 드리운다 — 사본은 카메라의 자식이고 1인칭 자세·크기로 놓인다.
        /// </summary>
        [Test]
        public void TheFirstPersonCopyNeverCastsOne()
        {
            var tool = MakeToolModel();

            Hold(tool, visual =>
            {
                var copy = visual.Model.GetComponentInChildren<Renderer>(true);

                Assert.IsNotNull(copy, "사본이 만들어져야 한다.");
                Assert.AreEqual(ShadowCastingMode.Off, copy.shadowCastingMode,
                    "사본은 카메라 자식이라 그림자가 엉뚱한 자리에 생긴다.");
            });
        }

        /// <summary>
        /// 메시는 여전히 1인칭 카메라에 보이지 않아야 한다.
        ///
        /// <para><b>수단을 묻지 않는다.</b> 처음에는 <c>shadowCastingMode != On</c> 으로
        /// 적었는데, 그러면 <c>forceRenderingOff</c> 로 감추던 <b>병합 전 코드도 이
        /// 검증을 깨뜨린다</b> — 그때도 메시는 실제로 감춰져 있었다. 구현의 모양을
        /// 재는 검증은 다른 옳은 구현을 거짓으로 몰고, <c>-448</c> 에서 같은 것에
        /// 걸렸다(소스 글자를 보던 검증).</para>
        ///
        /// <para>재야 하는 것은 <b>"카메라 패스에 안 들어간다"</b> 하나다.</para>
        /// </summary>
        [Test]
        public void TheRealMeshIsStillHiddenFromTheCamera()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();

            Hold(tool, visual =>
            {
                var drawnInColorPass =
                    !original.forceRenderingOff
                    && original.enabled
                    && original.shadowCastingMode != ShadowCastingMode.ShadowsOnly;

                Assert.IsFalse(drawnInColorPass,
                    "진짜 도구가 카메라에 그려지면 사본과 겹쳐 두 개로 보인다.");
            });
        }

        // ── 놓았을 때 ──────────────────────────────────────────

        [Test]
        public void LettingGo_RestoresEverything()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();

            var beforeForce = original.forceRenderingOff;
            var beforeMode = original.shadowCastingMode;

            var visual = MakeVisual(tool);
            try
            {
                visual.SetVisible(true);
                visual.SetVisible(false);
            }
            finally { visual.Dispose(); }

            Assert.AreEqual(beforeForce, original.forceRenderingOff);
            Assert.AreEqual(beforeMode, original.shadowCastingMode,
                "놓으면 원래대로 — 3인칭에서 도구가 정상으로 그려져야 한다.");
        }

        /// <summary>
        /// 원래 통째로 꺼져 있던 렌더러는 계속 꺼 둔다. <c>hiddenBefore</c> 가 지키던
        /// 뜻이고, 그림자 모드로 바꾸면서 잃으면 안 되는 것이다.
        /// </summary>
        [Test]
        public void ARendererThatWasAlreadyOff_StaysOff()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();
            original.forceRenderingOff = true;

            Hold(tool, visual =>
            {
                Assert.IsTrue(original.forceRenderingOff,
                    "숨겨 둔 부품이 도구를 든다고 나타나면 안 된다.");
            });
        }

        /// <summary>
        /// 원래 그림자를 만들지 않던 렌더러에 <b>없던 그림자를 만들지 않는다.</b>
        /// 감추는 일이 그림자를 켜는 일이 되면 그것도 틀린 그림이다.
        /// </summary>
        [Test]
        public void ARendererThatNeverCastShadows_StillDoesNot()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();
            original.shadowCastingMode = ShadowCastingMode.Off;

            Hold(tool, visual =>
            {
                Assert.AreEqual(ShadowCastingMode.Off, original.shadowCastingMode,
                    "빔·이펙트처럼 일부러 그림자를 끈 것들이 있다.");
            });
        }

        // ── 순서가 뒤섞여도 안전한가 (!413 AI 리뷰) ─────────────

        /// <summary>
        /// <b>든 적 없이 놓기부터 오면?</b> 탑승·기절에서 <c>HideFirstPersonVisual</c> 이
        /// 그 경로다(<c>SetVisible(false)</c> 가 먼저 온다).
        ///
        /// <para>리뷰가 "저장 전에 복원하면 <c>shadowBefore</c> 의 배열 기본값
        /// <c>Off</c> 가 렌더러에 써져 그림자가 조용히 사라진다" 고 지적했다. 읽어서
        /// 반박하지 않고 그 시나리오를 그대로 잰다.</para>
        /// </summary>
        [Test]
        public void HidingWithoutEverShowing_LeavesTheOriginalAlone()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();
            var before = original.shadowCastingMode;

            var visual = MakeVisual(tool);
            try
            {
                visual.SetVisible(false);   // 든 적이 없다

                Assert.AreEqual(before, original.shadowCastingMode,
                    "저장한 적 없는 값으로 원본을 덮으면 안 된다.");
                Assert.IsFalse(original.forceRenderingOff);
            }
            finally { visual.Dispose(); }
        }

        /// <summary>
        /// <b>들기가 두 번 연속 오면?</b> 리뷰가 "두 번째에 <c>ShadowsOnly</c> 가
        /// 저장되어 놓은 뒤에도 그대로 남는다 — 3인칭에서 도구가 안 보인다" 고 지적했다.
        /// </summary>
        [Test]
        public void ShowingTwiceThenHiding_StillRestoresTheOriginal()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();
            var before = original.shadowCastingMode;

            var visual = MakeVisual(tool);
            try
            {
                visual.SetVisible(true);
                visual.SetVisible(true);    // 재장착·재입장 경합
                visual.SetVisible(false);

                Assert.AreEqual(before, original.shadowCastingMode,
                    "놓으면 원래대로 — ShadowsOnly 가 남으면 3인칭에서 도구가 안 보인다.");
            }
            finally { visual.Dispose(); }
        }

        /// <summary>들고 놓기를 여러 번 되풀이해도 값이 흘러가지 않는다.</summary>
        [Test]
        public void ShowAndHideRepeatedly_DoesNotDrift()
        {
            var tool = MakeToolModel();
            var original = tool.GetComponentInChildren<Renderer>();
            var before = original.shadowCastingMode;

            var visual = MakeVisual(tool);
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    visual.SetVisible(true);
                    Assert.AreEqual(ShadowCastingMode.ShadowsOnly, original.shadowCastingMode,
                        $"{i + 1}번째로 들었을 때");

                    visual.SetVisible(false);
                    Assert.AreEqual(before, original.shadowCastingMode,
                        $"{i + 1}번째로 놓았을 때");
                }
            }
            finally { visual.Dispose(); }
        }

        // ── 몸 쪽은 건드리지 않았다 ─────────────────────────────

        /// <summary>
        /// 이 이슈가 몸 경로를 건드리지 않았음을 잠근다. 1인칭에서 몸은 <b>켜진 채</b>
        /// <c>ShadowsOnly</c> 여야 한다 — 꺼 버리면 모드와 무관하게 그림자가 없다.
        /// </summary>
        [Test]
        public void TheBodyKeepsItsOwnShadowInFirstPerson()
        {
            var rig = MakePlayer();

            rig.Doll.SetVisible(false, keepShadow: true);

            var disabled = 0;
            var notShadowsOnly = 0;
            foreach (var r in rig.Doll.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) { disabled++; continue; }
                if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) notShadowsOnly++;
            }

            Assert.Zero(disabled, "꺼진 렌더러는 그림자도 없다.");
            Assert.Zero(notShadowsOnly, "1인칭에서 몸은 그림자만 남긴다(§10.1).");
        }

        // ── 도구 ────────────────────────────────────────────────

        void Hold(Transform tool, System.Action<FirstPersonToolVisual> check)
        {
            var visual = MakeVisual(tool);
            try
            {
                visual.SetVisible(true);
                check(visual);
            }
            finally { visual.Dispose(); }
        }

        FirstPersonToolVisual MakeVisual(Transform tool)
        {
            var camera = new GameObject("카메라").transform;
            _spawned.Add(camera.gameObject);
            return new FirstPersonToolVisual(tool, camera);
        }

        Transform MakeToolModel()
        {
            var root = new GameObject("도구 모델").transform;
            _spawned.Add(root.gameObject);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "몸통";
            body.transform.SetParent(root, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            return root;
        }

        PlayerRig MakePlayer()
        {
            // Prim.Decor 가 내는 로그만 넘긴다. 만들고 나면 바로 되돌려서, 이후 경로의
            // 에러는 검증을 빨간불로 만든다.
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            _spawned.Add(player.gameObject);
            return player;
        }
    }
}
