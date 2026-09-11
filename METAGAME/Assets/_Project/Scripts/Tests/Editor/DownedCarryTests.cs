using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절한 동료 운반을 검증한다(기획 §20.4, S15P21B202-249).
    ///
    /// <b>막고 있던 것은 두 겹의 마스크였다.</b> 조준 탐색이 쓰는
    /// <see cref="RagdollBuilder.WorldMask"/> 는 Player · PlayerDowned · Interactable 을
    /// 모두 빼고 질의가 트리거도 무시한다. 마스크를 열면 고정못(파츠의 자식이다)이 파츠의
    /// 잡기 지점으로 딸려 들어오므로, 대신 <see cref="PlayerRig.All"/> 을 훑는다.
    /// </summary>
    public sealed class DownedCarryTests
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

            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            Puff.Enabled = _puffWasEnabled;

            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PlayerRig.All.Clear();
            Grabbable.All.Clear();
        }

        // --- 운반 지점 --------------------------------------------------

        /// <summary>
        /// <b>규칙이 뒤집혔다</b>(S15P21B202-442). 예전에는 "멀쩡한 사람을 끌고 다닐
        /// 수는 없다" 를 여기서 잠갔다.
        ///
        /// 그 문장은 <b>기획에 근거가 없었다</b> — §20.4 의 제목이 "기절한 동료 운반"
        /// 인 것에서 유추한 판단이 검증으로 굳은 것이다. 세계의 다른 물건은 전부
        /// 잡아서 밀고 당길 수 있는데 사람만 안 되는 것은 규칙으로 설명되지 않는다.
        /// </summary>
        [Test]
        public void AStandingPlayer_AlsoHasAGrabPoint()
        {
            var player = MakePlayer(Vector3.zero);

            Assert.IsNotNull(player.CarryTarget, "태어날 때 만들어진다.");
            Assert.IsTrue(player.CarryTarget.gameObject.activeSelf,
                "살아있어도 잡을 수 있다 — 다른 물건과 같다.");
            Assert.IsFalse(player.Downed, "기절시키지 않았다.");
        }

        [Test]
        public void GoingDown_TurnsTheCarryPointOn()
        {
            var player = MakePlayer(Vector3.zero);
            player.GoDown("운석 직접 충돌");

            Assert.IsNotNull(player.CarryTarget);
            Assert.IsTrue(player.CarryTarget.gameObject.activeSelf);
        }

        /// <summary>
        /// 일어나도 잡을 지점은 그대로 있다(S15P21B202-442). 바뀌는 것은 이름뿐이다 —
        /// HUD 가 "기절한 동료" 와 "동료" 를 구분해 보여준다.
        /// </summary>
        [Test]
        public void GettingUp_KeepsThePointButRenamesIt()
        {
            var player = MakePlayer(Vector3.zero);
            player.GoDown("검증");
            Assert.AreEqual("기절한 동료", player.CarryTarget.Label);

            player.Revive(player.MaxHealth);

            Assert.IsTrue(player.CarryTarget.gameObject.activeSelf,
                "일어났다고 잡을 수 없게 되지는 않는다.");
            Assert.AreEqual("동료", player.CarryTarget.Label);
        }

        /// <summary>
        /// 몸의 레이어를 건드리지 않는다 — §20.3 이 "충돌 매트릭스가 함께 흔들린다" 며
        /// 피한 것이고, 운반도 같은 이유로 자식 하나만 둔다.
        /// </summary>
        [Test]
        public void TheCarryPointIsAChildAndCarriesNoCollider()
        {
            var player = MakePlayer(Vector3.zero);
            player.GoDown("검증");

            var carry = player.CarryTarget;

            Assert.AreSame(player.Doll.Hips.transform, carry.transform.parent);
            Assert.AreEqual(GameLayers.Interactable, carry.gameObject.layer);
            Assert.IsNull(carry.GetComponent<Collider>(),
                "물리 질의로 찾지 않으므로 콜라이더가 필요 없다.");
        }

        // --- 물리 오브젝트처럼 (§20.4) ------------------------------------

        /// <summary>
        /// 인형 총 질량은 <b>34.9kg</b>(골반 8 + 가슴 10 + 머리 5 + 팔 3.9 + 다리 8)이고
        /// 잡히는 골반은 8kg 다. <see cref="Tuning.FixedGripMass"/> 40kg 미만이라
        /// 그대로 두면 도구처럼 <b>키네마틱으로 몸 앞에 고정</b>된다 — §20.4 는
        /// "물리 오브젝트처럼 끌고 옮길 수 있다" 고 정한다.
        /// </summary>
        [Test]
        public void TheDollIsLightEnoughToBeMistakenForATool()
        {
            var player = MakePlayer(Vector3.zero);

            Assert.AreEqual(34.9f, player.Doll.TotalMass, 0.1f,
                "이 숫자가 바뀌면 40kg 임계값과의 관계를 다시 봐야 한다.");
            Assert.Less(player.Doll.Hips.mass, Tuning.FixedGripMass,
                "그래서 PhysicsGrip 이 필요하다.");
        }

        [Test]
        public void TheCarryPointDemandsAPhysicsGrip()
        {
            var player = MakePlayer(Vector3.zero);
            player.GoDown("검증");

            Assert.IsTrue(player.CarryTarget.PhysicsGrip,
                "도구처럼 몸 앞에 붙으면 안 된다(§20.4).");
        }

        /// <summary>가벼운 일반 물체는 예전 그대로 키네마틱 운반이어야 한다.</summary>
        [Test]
        public void AnOrdinaryLightObject_StillUsesTheKinematicCarry()
        {
            var grabbable = MakeLightGrabbable();

            Assert.IsFalse(grabbable.PhysicsGrip,
                "기본값이 바뀌면 도구와 소품 운반이 전부 달라진다.");
        }

        // --- 잡고 놓기 ----------------------------------------------------

        [Test]
        public void CarryingIsReportedWhileHolding()
        {
            var carrier = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(1f, 0f, 0f));
            downed.GoDown("검증");

            Assert.IsFalse(carrier.CarryingDownedTeammate);

            Assert.IsTrue(carrier.LeftHand.GripTo(downed.CarryTarget, downed.Doll.Hips),
                "잡을 수 있어야 한다.");
            Assert.IsTrue(carrier.CarryingDownedTeammate);
        }

        /// <summary>
        /// 물리 조인트 경로로 갔는지 본다. 키네마틱 운반이면 <c>Carrying</c> 이 서고
        /// 골반이 키네마틱으로 바뀐다.
        /// </summary>
        [Test]
        public void HoldingUsesTheJointPath_NotTheKinematicOne()
        {
            var carrier = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(1f, 0f, 0f));
            downed.GoDown("검증");

            carrier.LeftHand.GripTo(downed.CarryTarget, downed.Doll.Hips);

            Assert.IsFalse(carrier.LeftHand.Carrying,
                "키네마틱 운반 경로로 가면 사람이 도구처럼 붙는다.");
            Assert.IsFalse(downed.Doll.Hips.isKinematic,
                "물리 오브젝트로 남아야 한다(§20.4).");
        }

        /// <summary>§20.4 "좌클릭을 놓으면 그 위치에 둔다".</summary>
        [Test]
        public void LettingGo_LeavesThemWhereTheyAre()
        {
            var carrier = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(1f, 0f, 0f));
            downed.GoDown("검증");

            carrier.LeftHand.GripTo(downed.CarryTarget, downed.Doll.Hips);
            var where = downed.Doll.Hips.position;

            carrier.LeftHand.Release();

            Assert.IsFalse(carrier.CarryingDownedTeammate);
            Assert.AreEqual(where, downed.Doll.Hips.position, "그 자리에 남는다.");
            Assert.IsTrue(downed.Downed, "놓는 것이 일으키는 것은 아니다.");
        }

        /// <summary>
        /// <b>규칙이 뒤집혔다</b>(S15P21B202-442). 예전에는 구조되는 순간 잡은 손을
        /// 강제로 놓게 했다 — "멀쩡한 사람을 계속 들고 있을 수 없다" 가 이유였다.
        ///
        /// 이제 살아있는 사람도 잡을 수 있으므로 놓을 이유가 없다. 대신 무엇을
        /// 들고 있는지에 대한 <b>보고가 바뀐다</b>: 여전히 잡고는 있지만
        /// "기절한 동료를 끌고 있다" 는 거짓이 된다.
        /// </summary>
        [Test]
        public void BeingRescuedWhileCarried_KeepsTheGripButChangesWhatItIs()
        {
            var carrier = MakePlayer(Vector3.zero);
            var downed = MakePlayer(new Vector3(1f, 0f, 0f));
            downed.GoDown("검증");

            carrier.LeftHand.GripTo(downed.CarryTarget, downed.Doll.Hips);
            Assert.IsTrue(carrier.CarryingDownedTeammate);

            downed.Revive(downed.MaxHealth);

            Assert.IsNotNull(carrier.LeftHand.Held, "일어났다고 손이 풀리지는 않는다.");
            Assert.IsTrue(carrier.CarryingTeammate, "여전히 사람을 잡고 있다.");
            Assert.IsFalse(carrier.CarryingDownedTeammate,
                "다만 '기절한 동료' 는 아니다 — 두 가지를 갈라 둔 이유다.");
        }

        // --- 운반 중 감속 (§20.4) -----------------------------------------

        [Test]
        public void TheSpeedScaleIsHalf()
        {
            Assert.Greater(PhysicsScale.DownedCarrySpeedScale, 0f);
            Assert.Less(PhysicsScale.DownedCarrySpeedScale, 1f,
                "§20.4 '운반 중 이동 속도가 느려지고'.");
        }

        // --- 마스크를 열지 않았다 -----------------------------------------

        /// <summary>
        /// 이 이슈에서 <b>가장 위험했던 선택</b>이다. 조준 탐색 마스크에 Interactable 을
        /// 넣으면 고정못(파츠의 자식이고 Interactable 트리거다)이 파츠의 잡기 지점으로
        /// 딸려 들어온다. 그래서 마스크를 그대로 두었다.
        /// </summary>
        [Test]
        public void TheCrosshairMaskStillExcludesPlayersAndInteractables()
        {
            var mask = RagdollBuilder.WorldMask;

            Assert.AreEqual(0, mask & (1 << GameLayers.Player));
            Assert.AreEqual(0, mask & (1 << GameLayers.PlayerDowned));
            Assert.AreEqual(0, mask & (1 << GameLayers.Interactable),
                "열면 고정못이 파츠의 잡기 지점이 된다.");
        }

        // --- 리그 ---------------------------------------------------------

        Grabbable MakeLightGrabbable()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "화물 상자";
            go.layer = GameLayers.Prop;
            _spawned.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 10f;
            body.useGravity = false;

            return go.AddComponent<Grabbable>();
        }

        PlayerRig MakePlayer(Vector3 foot)
        {
            var player = PlayerRig.Create("테스트", foot, Quaternion.identity,
                                          Color.white, Color.gray, false);
            _spawned.Add(player.gameObject);
            UnityEngine.Physics.SyncTransforms();
            return player;
        }
    }
}
