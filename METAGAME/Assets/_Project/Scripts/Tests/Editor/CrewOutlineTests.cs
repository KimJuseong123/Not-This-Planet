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
    }
}
