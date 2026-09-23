using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Metagame.Gameplay.Interaction;
using Metagame.UI;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 사람에게 투시 테두리가 붙는가, 그리고 <b>색이 쓰러짐을 말하는가</b>
    /// (S15P21B202-724).
    ///
    /// <para>세계 안에는 누가 쓰러졌는지 알려 주는 표시가 없었다. 팀 목록에 빨간
    /// 이름으로만 뜨고, 머리 위 이름표는 쓰러져도 팀 색 그대로다. 게다가 사람에게는
    /// 테두리가 아예 없었다 — <c>IsEligible</c> 이 <c>PlayerRig</c> 아래를 통째로
    /// 제외했다(-591 이 손에 든 도구를 막으려던 것이다).</para>
    ///
    /// <para><b>여기서 재는 것은 그리는 그림이 아니라 정하는 규칙이다.</b> 실제로
    /// 그리는 검사(<c>OccludedInteractionRenderTests</c>)는 Play 모드와 화면이
    /// 필요해 <c>-nographics</c> 에서 못 돈다.</para>
    /// </summary>
    public sealed class CrewOutlineTests
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
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            PlayerRig.All.Clear();
        }

        PlayerRig MakePlayer()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", Vector3.zero, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            _spawned.Add(player.gameObject);
            return player;
        }

        // --- 사람이 대상이 된다 ---------------------------------------------------------

        [Test]
        public void APersonIsAnOutlineTarget()
        {
            // -591 은 PlayerRig 아래를 통째로 제외했다. 그 규칙은 **손에 든 도구**를
            // 막으려던 것이라, 리그 자신은 거기 걸리면 안 된다.
            Assert.That(OccludedInteractionTargets.IsEligible(MakePlayer()), Is.True,
                "사람이 테두리 대상이 아니다 — 쓰러져 있어도 못 찾는다.");
        }

        [Test]
        public void ADownedPersonIsStillATarget()
        {
            // 이것이 이 이슈의 전부다. 쓰러진 사람이 빠지면 고친 것이 없다.
            var player = MakePlayer();
            player.SetRemoteDowned(true);
            Assert.That(OccludedInteractionTargets.IsEligible(player), Is.True);
        }

        // --- 색이 상태를 말한다 ---------------------------------------------------------

        [Test]
        public void AStandingPersonIsGreenAndADownedPersonIsRed()
        {
            var player = MakePlayer();
            Assert.That(OccludedInteractionTargets.ColorFor(player),
                Is.EqualTo(OccludedInteractionTargets.CrewColor),
                "멀쩡한 사람이 초록이 아니다.");

            player.SetRemoteDowned(true);
            Assert.That(OccludedInteractionTargets.ColorFor(player),
                Is.EqualTo(OccludedInteractionTargets.DownedColor),
                "쓰러졌는데 색이 안 바뀐다 — 죽은 것을 알 수 없다.");
        }

        [Test]
        public void TheDownedColourIsTheOneTheTeamListAlreadyUses()
        {
            // **화면 두 곳이 같은 뜻에 같은 색을 써야 한다.** 팀 목록은 쓰러진 사람을
            // 이미 이 색으로 그린다 — 세계에서 다른 빨강을 쓰면 둘이 다른 뜻으로 읽힌다.
            var mine = OccludedInteractionTargets.DownedColor;
            var list = GameHud.TeamDownedColor;
            Assert.That(mine.r, Is.EqualTo(list.r).Within(0.001f));
            Assert.That(mine.g, Is.EqualTo(list.g).Within(0.001f));
            Assert.That(mine.b, Is.EqualTo(list.b).Within(0.001f));
        }

        [Test]
        public void ThePeopleColoursAreNotTheObjectColour()
        {
            // 노랑은 -591 의 상호작용 물체다. 사람이 같은 색이면 구별이 안 된다.
            Assert.That(OccludedInteractionTargets.CrewColor,
                Is.Not.EqualTo(OccludedInteractionTargets.ObjectColor));
            Assert.That(OccludedInteractionTargets.DownedColor,
                Is.Not.EqualTo(OccludedInteractionTargets.ObjectColor));
        }

        [Test]
        public void GreenAndRedAreTellableApart()
        {
            // 색맹까지는 아니어도, 둘이 비슷하면 상태를 못 읽는다.
            var green = OccludedInteractionTargets.CrewColor;
            var red = OccludedInteractionTargets.DownedColor;
            Assert.That(Mathf.Abs(green.r - red.r) + Mathf.Abs(green.g - red.g),
                Is.GreaterThan(0.8f), "초록과 빨강이 너무 가깝다.");
        }

        // --- 색이 쓰러진 순간을 놓치지 않는다 (AI 리뷰가 잡은 것) -----------------------

        Camera MakeCamera()
        {
            var go = new GameObject("검증 카메라");
            _spawned.Add(go);
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;                     // 그리지는 않는다
            camera.transform.position = new Vector3(0f, 1f, -4f);
            camera.transform.rotation = Quaternion.identity;
            return camera;
        }

        /// <summary>
        /// <b>색을 캐시하면 안 된다.</b> 처음에는 <c>RefreshMeshes</c> 에서 한 번 정해
        /// 담았는데, 그것은 0.5초마다만 돈다 — <c>downed</c> 는 그 사이에 바뀌는
        /// SyncVar 라 <b>쓰러져도 최대 0.5초 동안 초록이었다.</b>
        ///
        /// <para>쓰러진 것을 알아채라고 만든 기능에서 가장 중요한 순간이 늦는다.
        /// AI 리뷰가 잡았고, 맞았다.</para>
        ///
        /// <para>이 검사는 <b>두 번째 수집을 갱신 주기 안에</b> 한다 — 그래야 캐시를
        /// 되살려도 빨간불이 된다.</para>
        /// </summary>
        [Test]
        public void TheColourChangesTheMomentSomeoneGoesDown()
        {
            var player = MakePlayer();
            var camera = MakeCamera();
            var targets = new OccludedInteractionTargets();

            Assert.That(targets.Collect(camera), Is.True, "사람을 하나도 못 모았다.");
            Assert.That(targets.Targets[0].Outline,
                Is.EqualTo(OccludedInteractionTargets.CrewColor), "멀쩡한데 초록이 아니다.");

            player.SetRemoteDowned(true);

            // **기다리지 않는다.** RefreshMeshes 는 0.5초마다만 도는데, 그 전에도
            // 색이 바뀌어야 한다.
            Assert.That(targets.Collect(camera), Is.True);
            Assert.That(targets.Targets[0].Outline,
                Is.EqualTo(OccludedInteractionTargets.DownedColor),
                "쓰러졌는데 아직 초록이다 — 색이 갱신 주기에 묶여 있다.");
        }

        [Test]
        public void GettingUpTurnsItBackToGreen()
        {
            var player = MakePlayer();
            var camera = MakeCamera();
            var targets = new OccludedInteractionTargets();

            player.SetRemoteDowned(true);
            targets.Collect(camera);
            Assert.That(targets.Targets[0].Outline,
                Is.EqualTo(OccludedInteractionTargets.DownedColor));

            player.SetRemoteDowned(false);
            targets.Collect(camera);
            Assert.That(targets.Targets[0].Outline,
                Is.EqualTo(OccludedInteractionTargets.CrewColor),
                "일어났는데 빨강이 남았다.");
        }

        // --- 남의 것만 그린다 -----------------------------------------------------------

        [Test]
        public void AToolInSomebodysHandIsStillExcluded()
        {
            // -591 이 막아 둔 것이다. 사람을 받아들이면서 이것까지 열면 안 된다.
            var player = MakePlayer();
            var tool = new GameObject("도구");
            _spawned.Add(tool);
            tool.transform.SetParent(player.transform, false);
            var bat = tool.AddComponent<MeteorBat>();

            Assert.That(OccludedInteractionTargets.IsEligible(bat), Is.False,
                "손에 든 도구에 테두리가 붙는다.");
        }

        [Test]
        public void MyOwnBodyIsNotOutlined()
        {
            // 1인칭에서는 인형이 이미 꺼져 있지만, 기절·밀기·탑승은 3인칭이라 내 등에
            // 테두리가 붙어 화면을 먹는다. 규칙을 그 우연에 기대지 않는다.
            var me = MakePlayer();
            Assert.That(OccludedInteractionTargets.IsCrewTarget(me, me), Is.False,
                "내 몸에 테두리가 붙는다.");
        }

        [Test]
        public void SomebodyElseIsOutlined()
        {
            var me = MakePlayer();
            var other = MakePlayer();
            Assert.That(OccludedInteractionTargets.IsCrewTarget(other, me), Is.True);
        }

        [Test]
        public void WithNobodyKnownToBeMeEveryoneIsATarget()
        {
            // 누가 '나' 인지 모를 때(검증·컷씬)는 다 그린다 — 빼는 쪽으로 틀리면
            // 아무도 안 보인다.
            //
            // **보는 쪽을 인수로 받는다.** 처음에는 GameSession.Instance 를 읽는
            // 검사로 적었다가, 앞 검사가 남긴 세션 때문에 혼자 돌 때와 같이 돌 때의
            // 결과가 달랐다 — 전역 상태에 기댄 검사였다.
            Assert.That(OccludedInteractionTargets.IsCrewTarget(MakePlayer(), null), Is.True);
        }

        /// <summary>
        /// <b>손에 든 도구가 사람의 일부가 되면 안 된다</b> (AI 리뷰가 잡은 것).
        ///
        /// <para>도구는 제 루트로 먼저 훑으므로 보통은 사람 루트로 안 온다. 그런데
        /// 씬 이름 검사를 사람에게만 면제했기 때문에, <b>런타임에 만들어져
        /// <c>Stage1_Astra</c> 밖에 있는 도구</b>는 도구 루트에서 건너뛰어지고 그
        /// 렌더러가 사람 루트로 흘러든다 — 그러면 방망이에 초록 테두리가 붙는다.</para>
        ///
        /// <para><c>IsEligible(bat) == false</c> 만 재는 검사로는 이 경로를 못 잡는다.
        /// <b>여기서는 실제 수집 결과의 색을 본다</b> — 도구가 사람 색으로 나오면
        /// 빨간불이다.</para>
        /// </summary>
        [Test]
        public void AToolUnderAPersonNeverTakesThePersonColour()
        {
            var player = MakePlayer();
            var tool = new GameObject("방망이");
            _spawned.Add(tool);
            tool.transform.SetParent(player.transform, false);
            tool.AddComponent<MeteorBat>();
            tool.AddComponent<MeshFilter>().sharedMesh =
                Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = tool.AddComponent<MeshRenderer>();

            var targets = new OccludedInteractionTargets();
            targets.Collect(MakeCamera());

            foreach (var target in targets.Targets)
            {
                if (target.Renderer != renderer) continue;
                Assert.That(target.Outline,
                    Is.Not.EqualTo(OccludedInteractionTargets.CrewColor),
                    "손에 든 도구가 사람 색으로 그려진다.");
                Assert.That(target.Outline,
                    Is.Not.EqualTo(OccludedInteractionTargets.DownedColor),
                    "손에 든 도구가 쓰러짐 색으로 그려진다.");
            }
        }

        /// <summary>
        /// <b>"내가 아닌가" 는 매 수집마다 다시 본다.</b> 리뷰가 0.5초 갱신 주기에
        /// 묶여 있을 가능성을 짚었는데, <c>IsEligible</c> 은 <c>Collect</c> 의 루프에서
        /// 불린다 — <c>RefreshMeshes</c> 가 아니다. 말로 반박하지 않고 잰다.
        /// </summary>
        [Test]
        public void WhoIsMeIsCheckedEveryCollectNotEveryRefresh()
        {
            var player = MakePlayer();
            var camera = MakeCamera();
            var targets = new OccludedInteractionTargets();

            Assert.That(targets.Collect(camera), Is.True);
            var before = targets.Targets.Count;

            // 갱신 주기를 기다리지 않고 리그를 끈다. 후보 목록(_candidates)은 그대로인데
            // IsEligible 이 매번 불리므로 결과에서 빠져야 한다.
            player.gameObject.SetActive(false);

            targets.Collect(camera);
            Assert.That(targets.Targets.Count, Is.LessThan(before),
                "규칙이 갱신 주기에 묶여 있다 — 매 수집마다 다시 보지 않는다.");
        }

        /// <summary>
        /// <b>수집 경로가 실제로 <c>IsEligible</c> 을 지난다.</b> 규칙만 있고 부르는
        /// 곳이 없으면 아무 소용이 없다 — AI 리뷰가 그 가능성을 짚었다.
        ///
        /// <para><b>이름이 <c>PartSpec</c> 에 없는 파츠</b>로 잰다. 그것은 세션도
        /// 들고 있는 상태도 필요 없이 <c>IsEligible</c> 하나로만 걸러지는 경우라,
        /// 전역 상태를 건드리지 않고 "거르는 문을 실제로 지난다" 를 보일 수 있다.</para>
        ///
        /// <para>처음에는 손에 든 방망이로 재려 했는데, 하네스의 방망이는 실제로
        /// 들려 있지 않아(<c>IsHeldForView</c> 가 거짓) <b>월드 오브젝트로서 정당하게
        /// 수집된다</b> — 검사의 전제가 틀렸다.</para>
        /// </summary>
        [Test]
        public void TheCollectPathActuallyAsksIsEligible()
        {
            var stray = new GameObject("이름없는파츠") { layer = 0 };
            _spawned.Add(stray);
            stray.AddComponent<Parts.SpaceshipPart>();
            stray.AddComponent<MeshFilter>().sharedMesh =
                Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            stray.AddComponent<MeshRenderer>();

            Assert.That(OccludedInteractionTargets.IsEligible(
                stray.GetComponent<Parts.SpaceshipPart>()), Is.False,
                "규칙이 이미 통과시킨다 — 이 검사가 아무것도 안 지킨다.");

            var targets = new OccludedInteractionTargets();
            targets.Collect(MakeCamera());

            foreach (var target in targets.Targets)
                Assert.That(target.Renderer.gameObject, Is.Not.SameAs(stray),
                    "규칙이 거르는 것이 수집됐다 — 수집 경로가 IsEligible 을 안 지난다.");
        }

    }
}
