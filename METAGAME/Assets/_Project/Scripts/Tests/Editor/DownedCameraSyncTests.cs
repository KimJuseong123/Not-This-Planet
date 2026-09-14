using System.Collections.Generic;
using Metagame.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절 3인칭 전환이 <b>참가자에게도</b> 되는지 검증한다(S15P21B202-511).
    ///
    /// <para><b>무엇이 문제였나.</b> <c>Cam.SetDownedView(...)</c> 를 부르는 곳이
    /// <see cref="PlayerRig.GoDown"/> · <see cref="PlayerRig.Revive"/> 둘뿐이었는데
    /// <b>참가자 기계에서는 그 둘이 돌지 않는다</b> — 대신 도는
    /// <see cref="PlayerRig.SetRemoteDowned"/> 가 카메라를 건드리지 않았다. 그래서
    /// 참가자는 죽어도 1인칭 그대로였다.</para>
    ///
    /// <para><b><c>-505</c> 와 뿌리가 다르다.</b> 저쪽은 값이 <b>안 내려온</b> 것이고,
    /// 이쪽은 값이 <b>내려오는데 그 값으로 할 일을 안 한</b> 것이다. 실측에서 갈렸다 —
    /// <c>Downed=True</c> 인데 <c>DownedView=False</c> 였고, 구조 지점·잡을 지점은
    /// 제대로 켜져 있었다.</para>
    ///
    /// <para><c>-378</c> 이 "넉백·장비 해제·레이어·카메라는 서버 권위" 로 넷을 묶었는데
    /// <b>카메라만은 각자 화면의 일</b>이다 — 남이 볼 일도 서버가 알 일도 없다.</para>
    /// </summary>
    public sealed class DownedCameraSyncTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            _puffWasEnabled = Puff.Enabled;
            Puff.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── 참가자 경로 ─────────────────────────────────────────

        /// <summary><b>이 검증이 이 이슈의 핵심이다.</b></summary>
        [Test]
        public void AParticipantsOwnScreenGoesThirdPersonWhenDowned()
        {
            var rig = MakePlayerWithCamera();
            Assert.IsFalse(rig.Cam.DownedView);

            rig.SetRemoteDowned(true);   // 참가자 기계가 자기 몸에 하는 것

            Assert.IsTrue(rig.Cam.DownedView,
                "죽으면 자기 몸이 어디 있는지 볼 수 있어야 한다(기획 §10.3).");
        }

        /// <summary>
        /// §10.3 "구조되면 1인칭으로 돌아온다". <c>Revive</c> 도 참가자에서 돌지 않으므로
        /// <b>켜는 것과 끄는 것 둘 다</b> 이 이슈다 — 한쪽만 고치면 참가자는 살아나도
        /// 3인칭에 갇힌다.
        /// </summary>
        [Test]
        public void AndComesBackToFirstPersonWhenRevived()
        {
            var rig = MakePlayerWithCamera();
            rig.SetRemoteDowned(true);

            rig.SetRemoteDowned(false);

            Assert.IsFalse(rig.Cam.DownedView, "살아나도 3인칭에 갇히면 안 된다.");
        }

        /// <summary>
        /// 값이 내려오는 것과 그 값으로 하는 일이 <b>함께</b> 가야 한다. 예전에는 앞의
        /// 셋만 되고 카메라만 빠져 있었다.
        /// </summary>
        [Test]
        public void TheFlagAndTheCameraAgree()
        {
            var rig = MakePlayerWithCamera();

            rig.SetRemoteDowned(true);

            Assert.IsTrue(rig.Downed, "플래그는 원래도 섰다.");
            Assert.IsTrue(rig.RescueTarget.gameObject.activeSelf, "구조 지점도 원래 켜졌다.");
            Assert.AreEqual(rig.Downed, rig.Cam.DownedView,
                "카메라만 따로 놀면 안 된다 — 그것이 이 결함이었다.");
        }

        // ── 호스트 경로가 그대로인가 ────────────────────────────

        [Test]
        public void TheHostPathStillWorks()
        {
            var rig = MakePlayerWithCamera();

            rig.GoDown("검증");

            Assert.IsTrue(rig.Cam.DownedView, "호스트 회귀가 있으면 안 된다.");
        }

        [Test]
        public void TheHostComesBackToo()
        {
            var rig = MakePlayerWithCamera();
            rig.GoDown("검증");

            rig.Revive(rig.MaxHealth);

            Assert.IsFalse(rig.Cam.DownedView);
        }

        // ── 남의 몸에서는 아무 일도 없어야 한다 ─────────────────

        /// <summary>
        /// 원격 몸의 <see cref="PlayerRig.Cam"/> 은 <c>null</c> 이다 —
        /// <c>SpaceBootstrap</c> 이 로컬 플레이어 하나에만 붙인다. 그래서 "내 것일 때만"
        /// 같은 조건을 따로 두지 않았고, <b>그 전제가 깨지면 여기서 터진다.</b>
        /// </summary>
        [Test]
        public void ABodyWithNoCameraDoesNotThrow()
        {
            var rig = MakePlayer();
            Assert.IsNull(rig.Cam, "남의 몸에는 카메라가 없다.");

            Assert.DoesNotThrow(() => rig.SetRemoteDowned(true));
            Assert.DoesNotThrow(() => rig.SetRemoteDowned(false));

            Assert.IsFalse(rig.Downed, "그래도 상태는 따라와야 한다.");
        }

        // ── 규칙이 한 곳에 있는가 ───────────────────────────────

        /// <summary>
        /// <b>전환 규칙이 한 곳에만 있어야 한다.</b> 예전에는 <c>GoDown</c> 과
        /// <c>Revive</c> 에 인라인으로 흩어져 있었고, 그래서 세 번째 경로
        /// (<c>SetRemoteDowned</c>)를 만들 때 조용히 빠졌다. 다시 흩어지면 같은 일이
        /// 반복된다 — <c>-383</c> 의 "판정을 두 곳에 두면 반드시 갈라진다" 다.
        /// </summary>
        [Test]
        public void OnlyOnePlaceDecidesTheDownedView()
        {
            var rig = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");

            Assert.AreEqual(1, Occurrences(TestSource.Blank(rig), "SetDownedView("),
                "전환 규칙은 RefreshDownedCamera 한 곳에만 있어야 한다.");
        }

        /// <summary>세 경로가 모두 그 한 곳을 부르는가.</summary>
        [Test]
        public void AllThreePathsRefreshIt()
        {
            var rig = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");

            foreach (var signature in new[]
                     {
                         // 문자열 리터럴을 넣으면 못 찾는다 — TestSource.Blank 가 리터럴을
                         // 공백으로 덮기 때문이다. 기본값 앞까지만 적는다.
                         "public void GoDown(string source =",
                         "public void Revive(float health)",
                         "public void SetRemoteDowned(bool nowDowned)",
                     })
            {
                var body = TestSource.Method(rig, signature);

                Assert.That(body, Does.Contain("RefreshDownedCamera()"),
                    $"{signature} 가 카메라를 갱신하지 않는다.");
            }
        }

        // ── 도구 ────────────────────────────────────────────────

        static int Occurrences(string text, string needle)
        {
            var n = 0;
            for (var i = text.IndexOf(needle); i >= 0; i = text.IndexOf(needle, i + 1)) n++;
            return n;
        }

        PlayerRig MakePlayer()
        {
            // Prim.Decor 가 내는 로그만 넘긴다. 만들고 나면 바로 되돌려서 이후 경로의
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

        PlayerRig MakePlayerWithCamera()
        {
            var player = MakePlayer();

            LogAssert.ignoreFailingMessages = true;
            try
            {
                var cam = OrbitCamera.Create(player.Hips.transform, Color.black, true);
                _spawned.Add(cam.gameObject);
                player.Cam = cam;
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            return player;
        }
    }
}
