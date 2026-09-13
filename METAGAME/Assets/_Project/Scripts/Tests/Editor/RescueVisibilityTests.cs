using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 살리기 표시와 게이지가 <b>참가자에게도</b> 보이는지 검증한다(S15P21B202-448).
    ///
    /// <para><b>두 결함이 뿌리가 다르다.</b></para>
    ///
    /// <para>① <b>후보가 호스트에서만 잡혔다.</b> 참가자 기계에서 남의 몸은 그림이라
    /// <c>RemoteAvatar</c> 가 <c>detectCollisions = false</c> 를 건다. 구조 지점 트리거는
    /// 그 몸의 <b>자식</b>이라 자기 <c>Rigidbody</c> 가 없고, 그래서 부모 액터에 붙는다 —
    /// 그 스위치는 액터를 시뮬레이션에서만이 아니라 <b>씬 질의에서도</b> 뺀다. 지점은
    /// 존재하고 켜져 있고 레이어도 맞는데 <c>OverlapSphere</c> 에 0개가 걸린다.</para>
    ///
    /// <para>② <b>쓰러진 당사자는 게이지를 볼 방법이 없었다.</b> 화면이
    /// <c>me.Downed</c> 에서 곧바로 반환했고, 열어도 그릴 값이 그 기계에 없었다 —
    /// 진행분은 서버에서만 쌓이고 전송되지 않았다.</para>
    /// </summary>
    public sealed class RescueVisibilityTests
    {
        readonly List<GameObject> _spawned = new();
        bool _puffWasEnabled;

        [SetUp]
        public void SetUp()
        {
            PlayerRig.All.Clear();
            Grabbable.All.Clear();
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
            Grabbable.All.Clear();
        }

        // ── ① 참가자 화면의 후보 ────────────────────────────────

        /// <summary>
        /// <b>이 검증이 이 이슈의 핵심이다.</b> 참가자 기계를 그대로 재현한다 —
        /// 남의 몸에 <c>RemoteAvatar</c> 를 붙이고(<c>mine: false</c>) 기절 상태만
        /// 내려 준다. 참가자는 <c>GoDown</c> 을 돌리지 않는다.
        /// </summary>
        [Test]
        public void OnAParticipantMachine_ADownedTeammateIsStillACandidate()
        {
            var me = MakePlayer(Vector3.zero);
            var downed = MakeRemoteDowned(new Vector3(0f, 0f, 1f));

            me.RescueInteractor.Tick(0.02f);

            Assert.IsNotNull(me.RescueCandidate,
                "구조 지점은 존재하고 켜져 있는데 물리 질의에 걸리지 않는다 — "
                + "detectCollisions 가 자식 트리거까지 씬 질의에서 뺀다.");
            Assert.AreSame(downed, me.RescueCandidate);
        }

        /// <summary>
        /// 원인을 못 박는다. <b>질의로는 여전히 못 찾는다</b> — 고친 것은 질의가 아니라
        /// 후보를 대는 경로다. 이 검증이 없으면 다음 사람이
        /// <c>detectCollisions</c> 를 되돌려 "간단히" 풀려 한다.
        /// </summary>
        [Test]
        public void ThePhysicsQueryStillCannotSeeIt_TheListIsWhatFindsIt()
        {
            var downed = MakeRemoteDowned(new Vector3(0f, 0f, 1f));

            var hits = UnityEngine.Physics.OverlapSphere(
                downed.Doll.Hips.position, 1.0f,
                GameLayers.InteractableMask, QueryTriggerInteraction.Collide);

            Assert.IsTrue(downed.RescueTarget.gameObject.activeSelf, "지점은 켜져 있다.");
            Assert.IsFalse(downed.Doll.Hips.detectCollisions,
                "참가자 기계에서 남의 몸은 물리에 끼지 않는다 — 이것은 옳은 처리다.");
            Assert.Zero(hits.Length,
                "질의로 찾을 수 있게 된 것이 아니다. 그랬다면 그림이 물리에 낀 것이다.");
        }

        [Test]
        public void OnTheHost_ItStillWorksTheSameWay()
        {
            var me = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            me.RescueInteractor.Tick(0.02f);

            Assert.AreSame(downed, me.RescueCandidate, "호스트 경로가 회귀하면 안 된다.");
        }

        [Test]
        public void AStandingTeammateIsNotACandidate()
        {
            var me = MakePlayer(Vector3.zero);
            MakePlayer(new Vector3(0f, 0f, 1f));
            UnityEngine.Physics.SyncTransforms();

            me.RescueInteractor.Tick(0.02f);

            Assert.IsNull(me.RescueCandidate, "멀쩡한 사람은 일으킬 대상이 아니다.");
        }

        [Test]
        public void ADownedPlayerCannotRescueAnyone()
        {
            var me = MakePlayer(Vector3.zero);
            MakeRemoteDowned(new Vector3(0f, 0f, 1f));

            me.GoDown("검증");
            me.RescueInteractor.Tick(0.02f);

            // **후보 자체가 없어야 한다**(!356 AI 리뷰). 예전에는 "후보가 없거나
            // 허용되지 않는다" 는 OR 로 단언해서, 막는 쪽이 사라져도 다른 절이 참이면
            // 초록불이었다.
            Assert.IsNull(me.RescueCandidate,
                "쓰러진 사람에게는 후보를 대지 않는다.");
        }

        [Test]
        public void YouAreNeverYourOwnRescueCandidate()
        {
            var me = MakePlayer(Vector3.zero);
            me.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            me.RescueInteractor.Tick(0.02f);

            Assert.AreNotSame(me, me.RescueCandidate);
        }

        /// <summary>
        /// 목록에서 대는 후보도 <b>같은 거리·각도 규칙</b>을 지나야 한다. 여기서 한 번
        /// 더 자르거나 덜 자르면 "4인이 각자 화면에서 같은 판단을 한다" 가 깨진다.
        /// </summary>
        [Test]
        public void ATeammateBehindYou_IsNotACandidate()
        {
            var me = MakePlayer(Vector3.zero);
            MakeRemoteDowned(new Vector3(0f, 0f, -1.2f));   // 등 뒤

            me.RescueInteractor.Tick(0.02f);

            Assert.IsNull(me.RescueCandidate, "시야각 밖은 후보가 아니다.");
        }

        [Test]
        public void ATeammateTooFarAway_IsNotACandidate()
        {
            var me = MakePlayer(Vector3.zero);
            MakeRemoteDowned(new Vector3(0f, 0f, PhysicsScale.DownedCarryReach + 3f));

            me.RescueInteractor.Tick(0.02f);

            Assert.IsNull(me.RescueCandidate, "사거리 밖은 후보가 아니다.");
        }

        // ── ② 살아나는 쪽 게이지 ────────────────────────────────

        /// <summary>
        /// 서버가 진행도를 계산한다. 이 값이 없으면 쓰러진 사람 화면에 그릴 것이 없다.
        /// </summary>
        [Test]
        public void TheServerComputesHowFarAlongMyRescueIs()
        {
            var rescuer = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            Assert.Zero(downed.BeingRescuedProgress01, "아무도 오지 않았다.");

            rescuer.RescueInteractor.Tick(0.02f);
            rescuer.RescueInteractor.TryBeginFocused();
            for (var i = 0; i < 25; i++) rescuer.RescueInteractor.Tick(0.02f);
            downed.Tick();

            Assert.Greater(downed.BeingRescuedProgress01, 0f,
                "일으켜지는 동안 당사자 쪽 값이 올라가야 한다.");
            Assert.LessOrEqual(downed.BeingRescuedProgress01, 1f);
        }

        /// <summary>
        /// 참가자는 그 값을 <b>받는다</b>. 자기 기계에서 만들 방법이 없다 — 진행분은
        /// 서버의 <c>DownedRescue</c> 에만 쌓인다.
        /// </summary>
        [Test]
        public void AParticipantReceivesItInsteadOfComputingIt()
        {
            var me = MakePlayer(Vector3.zero);
            me.ServerAuthority = false;          // 참가자의 내 몸
            me.GoDown("검증");

            me.SetRemoteBeingRescued(0.4f);

            Assert.AreEqual(0.4f, me.BeingRescuedProgress01, 1e-4f);
        }

        /// <summary>
        /// 권위가 있는 몸은 받은 값을 <b>무시한다</b>. 두 곳에서 정하면 갈라진다(-383).
        /// </summary>
        [Test]
        public void AnAuthoritativeBodyIgnoresTheIncomingValue()
        {
            var host = MakePlayer(Vector3.zero);
            Assert.IsTrue(host.ServerAuthority, "기본값이 권위 있음이다.");

            host.SetRemoteBeingRescued(0.9f);

            Assert.Zero(host.BeingRescuedProgress01,
                "서버는 자기 RescueTarget 에서 직접 읽는다.");
        }

        [Test]
        public void GettingUp_ClearsIt()
        {
            var rescuer = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            rescuer.RescueInteractor.Tick(0.02f);
            rescuer.RescueInteractor.TryBeginFocused();
            for (var i = 0; i < 25; i++) rescuer.RescueInteractor.Tick(0.02f);
            downed.Tick();
            Assert.Greater(downed.BeingRescuedProgress01, 0f);

            downed.Revive(downed.MaxHealth);
            downed.Tick();

            Assert.Zero(downed.BeingRescuedProgress01,
                "일어난 사람에게 게이지가 남아 있으면 안 된다.");
        }

        /// <summary>
        /// <b>구조자가 떠나면 게이지가 사라진다</b>(!356 AI 리뷰).
        ///
        /// <c>DownedRescue.Progress</c> 는 손을 떼도 남는다 — 다른 사람이 이어받게 하려고
        /// 일부러 그렇다. 그 값을 그대로 내보내면 아무도 오지 않는 동안에도
        /// "일으켜지는 중…" 이 계속 떠서 <b>이 이슈가 피하려던 거짓 표시</b>가 된다.
        /// </summary>
        [Test]
        public void WhenTheRescuerLetsGo_TheGaugeStops()
        {
            var rescuer = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            rescuer.RescueInteractor.Tick(0.02f);
            rescuer.RescueInteractor.TryBeginFocused();
            for (var i = 0; i < 25; i++) rescuer.RescueInteractor.Tick(0.02f);
            downed.Tick();
            Assert.Greater(downed.BeingRescuedProgress01, 0f, "누르는 동안은 보인다.");

            rescuer.RescueInteractor.Cancel();
            downed.Tick();

            Assert.Zero(downed.BeingRescuedProgress01,
                "손을 떼면 멈춘다. 남은 진행분은 구조자 쪽이 '이어서 일으키기' 로 본다.");
            Assert.Greater(downed.RescueTarget.Progress, 0f,
                "진행분 자체는 남아 있어야 한다 — 이어받기가 그것으로 성립한다.");
        }

        /// <summary>
        /// 참가자는 <b>완료를 스스로 하지 않는다</b>(!356 AI 리뷰가 빠졌다고 짚었다).
        /// 부활은 서버가 판정하고(-145 · -378), 클라이언트가 여기서 살리면 그 화면에서만
        /// 일어나고 되돌아오지도 않는다(-383).
        /// </summary>
        [Test]
        public void AParticipantDoesNotReviveOnItsOwn()
        {
            var rescuer = MakePlayer(Vector3.zero);
            rescuer.ServerAuthority = false;      // 참가자의 내 몸
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            rescuer.RescueInteractor.Tick(0.02f);
            rescuer.RescueInteractor.TryBeginFocused();

            // 홀드를 끝까지 몰아붙인다.
            for (var i = 0; i < 400; i++) rescuer.RescueInteractor.Tick(0.02f);

            Assert.IsTrue(downed.Downed,
                "참가자 화면에서 일어나면 서버와 갈라진다 — downed 훅은 값이 바뀔 때만 온다.");
        }

        [Test]
        public void TheHostDoesRevive()
        {
            var rescuer = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(0f, 0f, 1f));
            downed.GoDown("검증");
            UnityEngine.Physics.SyncTransforms();

            rescuer.RescueInteractor.Tick(0.02f);
            rescuer.RescueInteractor.TryBeginFocused();
            for (var i = 0; i < 400; i++) rescuer.RescueInteractor.Tick(0.02f);

            Assert.IsFalse(downed.Downed, "호스트에서는 실제로 일어나야 한다.");
        }

        // ── 화면이 실제로 그리기로 하는가 ────────────────────────

        /// <summary>
        /// <b>이 검증이 없으면 증상을 되살려도 초록불이다.</b> 위의 검증들은 값이
        /// 생기는지만 본다 — 화면이 그 값을 그리기로 하는지는 따로다. 예전 화면은
        /// <c>me.Downed</c> 에서 곧바로 반환했고, 그 상태에서도 값 검증은 모두 통과한다.
        /// </summary>
        [Test]
        public void TheDownedPlayersOwnScreenDrawsIt()
        {
            Assert.IsTrue(UI.DownedRescueHud.ShouldDrawBeingRescued(true, 0.3f),
                "쓰러져 있고 누군가 일으키는 중이면 그려야 한다.");
        }

        [Test]
        public void NobodyComing_DrawsNothing()
        {
            Assert.IsFalse(UI.DownedRescueHud.ShouldDrawBeingRescued(true, 0f),
                "아무도 안 오면 빈 막대를 띄우지 않는다 — 솔로는 6초 뒤 스스로 일어난다(§20.3).");
        }

        [Test]
        public void AStandingPlayerNeverDrawsIt()
        {
            Assert.IsFalse(UI.DownedRescueHud.ShouldDrawBeingRescued(false, 0.5f),
                "서 있는 사람 화면은 구조자 쪽 표시를 쓴다.");
        }

        // ── 도구 ─────────────────────────────────────────────────

        /// <summary>
        /// 참가자 기계의 남의 몸을 그대로 만든다. <c>GoDown</c> 은 부르지 않는다 —
        /// 참가자에서는 그 함수가 돌지 않고 상태만 내려온다.
        /// </summary>
        PlayerRig MakeRemoteDowned(Vector3 foot)
        {
            var rig = MakePlayer(foot);

            var source = new GameObject("서버자세").transform;
            _spawned.Add(source.gameObject);
            source.position = rig.Doll.Hips.position;

            RemoteAvatar.Attach(rig, source, mine: false);
            rig.SetRemoteDowned(true);
            UnityEngine.Physics.SyncTransforms();

            return rig;
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            // Prim.Decor 가 내는 로그만 넘긴다. 만들고 나면 바로 되돌려서, 이후 경로의
            // 에러는 검증을 빨간불로 만든다.
            LogAssert.ignoreFailingMessages = true;
            PlayerRig player;
            try
            {
                player = PlayerRig.Create("검증", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
