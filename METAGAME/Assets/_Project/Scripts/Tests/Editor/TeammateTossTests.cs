using System.Collections.Generic;
using System.Linq;
using Metagame.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 동료를 양손으로 들어 위로 내던지는 기능 (S15P21B202-519).
    ///
    /// <para><b>무엇을 만들었나.</b> 잡기(<c>-442</c>)는 이미 있었지만 잡은 사람을 던지는
    /// 길은 없었다 — <c>ThrowTool</c> 은 <c>toolHeld</c> 만 본다. 여기서 더한 것은 넷이다:
    /// 던지는 판정, 던져지는 사람의 상태와 <b>시야</b>, 던지는 <b>동작</b>, 그리고
    /// <b>그 셋을 참가자에게 보내는 것</b>.</para>
    ///
    /// <para><b>멀티가 전제다</b>(사용자 규칙). 이번 주에만 <c>-448</c>·<c>-505</c>·
    /// <c>-511</c>·<c>-513</c> 이 전부 "호스트만 된다" 였다. 그래서 보내는 자리와 받는
    /// 자리를 처음부터 검증에 넣는다.</para>
    /// </summary>
    public sealed class TeammateTossTests
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

        // ── 던지는 속도 ─────────────────────────────────────────

        /// <summary><b>"높으면서 살짝 앞으로"</b> — 사용자가 정한 방향이다.</summary>
        [Test]
        public void ItThrowsMostlyUpAndSomeForward()
        {
            var velocity = PlayerRig.TossVelocity(Vector3.forward, 1f);

            Assert.Greater(velocity.y, 0f, "위로 떠야 던진 것이다.");
            Assert.Greater(velocity.z, 0f, "앞으로도 가야 던지는 쪽과 겹치지 않는다.");
            Assert.Greater(velocity.y, velocity.z, "위가 앞보다 커야 '높으면서 살짝 앞으로' 다.");
        }

        /// <summary>
        /// <b>앞 성분은 수평으로 눕힌다.</b> 조준을 그대로 쓰면 하늘을 보고 던질 때 위
        /// 성분이 두 번 더해지고, 땅을 보고 던지면 서로 상쇄되어 제자리에 떨어진다.
        /// </summary>
        [Test]
        public void LookingUpOrDownDoesNotChangeHowHighItGoes()
        {
            var level = PlayerRig.TossVelocity(Vector3.forward, 1f);
            var sky = PlayerRig.TossVelocity((Vector3.forward + Vector3.up * 3f).normalized, 1f);
            var ground = PlayerRig.TossVelocity((Vector3.forward + Vector3.down * 3f).normalized, 1f);

            Assert.AreEqual(level.y, sky.y, 1e-4f, "하늘을 봐도 같은 높이다.");
            Assert.AreEqual(level.y, ground.y, 1e-4f, "땅을 봐도 같은 높이다.");
            Assert.Greater(ground.z, 0f, "땅을 보고 던져도 앞으로는 간다.");
        }

        [Test]
        public void WithNoAimItStillGoesUp()
        {
            var velocity = PlayerRig.TossVelocity(Vector3.zero, 1f);

            Assert.Greater(velocity.y, 0f);
            Assert.AreEqual(0f, new Vector2(velocity.x, velocity.z).magnitude, 1e-4f,
                "방향이 없으면 수평 성분도 없다.");
        }

        /// <summary>
        /// <b>툭 던져도 사람이 떠야 한다.</b> 충전에 완전히 비례시키면 문턱을 갓 넘긴
        /// 던지기가 제자리 놓기와 구별되지 않는다.
        /// </summary>
        [Test]
        public void AQuickTossStillLifts()
        {
            var barely = PlayerRig.TossVelocity(Vector3.forward, Tuning.TossChargeThreshold);
            var full = PlayerRig.TossVelocity(Vector3.forward, 1f);

            Assert.Greater(barely.y, full.y * 0.4f, "짧게 눌러도 절반 가까이는 떠야 한다.");
            Assert.Greater(full.y, barely.y, "오래 충전할수록 높이 간다.");
        }

        // ── 던져진 사람 ─────────────────────────────────────────

        /// <summary><b>이 검증이 기능의 핵심이다.</b></summary>
        [Test]
        public void BeingTossedLaunchesTheWholeBody()
        {
            var victim = MakePlayer();
            var before = victim.Doll.Hips.linearVelocity.y;

            victim.LaunchTossed(Vector3.up * 8f);

            Assert.IsTrue(victim.Tossed, "던져진 상태가 켜져야 시야도 따라온다.");
            Assert.Greater(victim.Doll.Hips.linearVelocity.y, before + 1f, "위로 뜨는 속도를 받아야 한다.");
        }

        /// <summary>
        /// 골반에만 넣으면 나머지 부위가 조인트에 끌려가며 몸이 늘어나 보인다.
        /// </summary>
        [Test]
        public void EveryPartGetsTheSameLift()
        {
            var victim = MakePlayer();

            victim.LaunchTossed(Vector3.up * 8f);

            var lifted = victim.Doll.Bodies.Count(b => b != null && !b.isKinematic
                                                       && b.linearVelocity.y > 1f);
            Assert.Greater(lifted, victim.Doll.Bodies.Count / 2,
                "몸의 절반 넘게 같이 떠야 한 덩어리로 날아간다.");
        }

        /// <summary>판정은 서버가 한다 — 참가자가 스스로 날리면 두 번 날아간다(<c>-513</c>).</summary>
        [Test]
        public void AParticipantCannotLaunchItself()
        {
            var victim = MakePlayer();
            victim.ServerAuthority = false;

            victim.LaunchTossed(Vector3.up * 8f);

            Assert.IsFalse(victim.Tossed, "권한이 없는 몸은 스스로 던져지지 않는다.");
        }

        // ── 시야 ────────────────────────────────────────────────

        /// <summary>
        /// <b>사용자가 따로 못 박은 것</b> — "시야도 던져지는 시야가 제대로 적용".
        /// 기절과 같은 화면을 쓴다: 몸이 구르는 대로 넘어갔다가 3인칭으로 물러난다.
        /// </summary>
        [Test]
        public void TheViewFollowsTheTossedBody()
        {
            var victim = MakePlayer();
            Assert.IsFalse(victim.BodyViewActive, "평소에는 아니다.");

            victim.LaunchTossed(Vector3.up * 8f);

            Assert.IsTrue(victim.BodyViewActive, "던져지면 시야가 몸을 따라가야 한다.");
        }

        [Test]
        public void ItGoesBackToNormalWhenItEnds()
        {
            var victim = MakePlayer();
            victim.ServerAuthority = false;
            victim.SetRemoteTossed(true);

            victim.SetRemoteTossed(false);

            Assert.IsFalse(victim.BodyViewActive, "착지하면 평소 시야로 돌아와야 한다.");
        }

        /// <summary>기절 중이면 던지기가 끝나도 시야는 그대로 몸을 따라간다.</summary>
        [Test]
        public void BeingDownedKeepsTheBodyView()
        {
            var victim = MakePlayer();
            victim.GoDown("검증");

            Assert.IsTrue(victim.BodyViewActive, "기절도 같은 화면을 쓴다.");
        }

        // ── 참가자에게 가는가 ───────────────────────────────────

        /// <summary>
        /// <b>이것이 없으면 참가자는 던져져도 1인칭 그대로다</b> — <c>-511</c> 이
        /// 기절에서 겪은 것과 같은 자리(QA 유형 L).
        /// </summary>
        [Test]
        public void AParticipantTakesTheTossedFlagFromTheServer()
        {
            var victim = MakePlayer();
            victim.ServerAuthority = false;
            Assert.IsFalse(victim.Tossed);

            victim.SetRemoteTossed(true);

            Assert.IsTrue(victim.Tossed, "서버가 던져졌다고 하면 여기서도 던져진 것이다.");
        }

        /// <summary>
        /// <b>플래그가 아니라 카메라를 잰다.</b> <c>-511</c> 이 정확히 여기서 났다 —
        /// 값은 잘 내려오고 플래그도 서는데 <b>받는 쪽이 카메라를 안 바꿨다</b>(유형 M).
        /// <c>BodyViewActive</c> 만 보면 그 결함을 통과시킨다.
        /// </summary>
        [Test]
        public void AParticipantsCameraActuallySwitches()
        {
            var victim = MakePlayerWithCamera();
            victim.ServerAuthority = false;
            Assert.IsFalse(victim.Cam.DownedView, "평소에는 평소 시점이다.");

            victim.SetRemoteTossed(true);

            Assert.IsTrue(victim.Cam.DownedView,
                "받은 값으로 카메라까지 바꿔야 한다 — 안 바꾸면 참가자만 1인칭 그대로다.");
        }

        /// <summary>호스트 쪽도 같은 일이 일어나야 한다.</summary>
        [Test]
        public void TheHostsCameraSwitchesToo()
        {
            var victim = MakePlayerWithCamera();

            victim.LaunchTossed(Vector3.up * 8f);

            Assert.IsTrue(victim.Cam.DownedView);
        }

        /// <summary>남의 몸에는 카메라가 없다 — 거기서 터지면 안 된다(<c>-511</c>).</summary>
        [Test]
        public void ABodyWithNoCameraIsFine()
        {
            var victim = MakePlayer();
            Assert.IsNull(victim.Cam);

            Assert.DoesNotThrow(() => victim.LaunchTossed(Vector3.up * 8f));
        }

        [Test]
        public void AnAuthoritativeBodyIgnoresIt()
        {
            var victim = MakePlayer();
            Assert.IsTrue(victim.ServerAuthority);

            victim.SetRemoteTossed(true);

            Assert.IsFalse(victim.Tossed, "호스트는 자기 판정이 정답이다(-383 · -513).");
        }

        [Test]
        public void TheServerActuallySendsIt()
        {
            var net = TestSource.Blank(TestSource.Read("Runtime/Network/NetPlayer.cs"));

            Assert.That(net, Does.Contain("tossed = body.Tossed;"),
                "서버가 채우지 않으면 참가자에게 아무것도 안 온다.");
            Assert.That(net, Does.Contain("tossSeq = body.TossSeq;"),
                "던지는 동작을 알리는 수도 채워야 한다.");
            Assert.That(net, Does.Contain("body.SetRemoteTossed(tossed)"),
                "받은 값을 몸에 넣는 자리가 있어야 한다.");
            Assert.That(net, Does.Contain("PlayTossIfNew(tossSeq)"),
                "던지는 동작을 켜는 자리가 있어야 한다.");
        }

        // ── 충전 게이지 ─────────────────────────────────────────

        [Test]
        public void NoChargeMeansNoGauge()
        {
            var thrower = MakePlayer();

            Assert.IsFalse(thrower.ChargingToss, "안 누르고 있으면 게이지가 없다.");
            Assert.Zero(thrower.TossCharge01);
        }

        /// <summary>
        /// 게이지는 던지는 <b>본인 화면</b>에 뜨는데 충전을 세는 것은 서버다 —
        /// 안 보내면 참가자는 얼마나 찼는지 모른 채 Q 를 놓는다(<c>-448</c> 과 같은 자리).
        /// </summary>
        [Test]
        public void AParticipantTakesTheChargeFromTheServer()
        {
            var thrower = MakePlayer();
            thrower.ServerAuthority = false;

            thrower.SetRemoteTossCharge(0.6f);

            Assert.AreEqual(0.6f, thrower.TossCharge01, 1e-3f);
            Assert.IsTrue(thrower.ChargingToss, "값이 오면 게이지가 떠야 한다.");
        }

        [Test]
        public void AnAuthoritativeBodyIgnoresTheRemoteCharge()
        {
            var thrower = MakePlayer();
            Assert.IsTrue(thrower.ServerAuthority);

            thrower.SetRemoteTossCharge(0.6f);

            Assert.Zero(thrower.TossCharge01, "호스트는 자기가 센 값이 정답이다.");
        }

        /// <summary>
        /// <b>참가자가 자기 빈손으로 그 값을 지우면 안 된다.</b>
        ///
        /// <para>잡기 판정은 서버가 하므로 참가자 기계의 내 손에는 그 조인트가 없을 수
        /// 있다. 그러면 <c>TossVictim</c> 이 null 이고, 거기서 충전을 0 으로 되돌리면
        /// 서버가 보낸 게이지를 <b>매 틱 지운다</b> — 게이지가 호스트에만 보이게 된다.</para>
        /// </summary>
        [Test]
        public void AParticipantsGaugeSurvivesItsOwnEmptyHands()
        {
            var thrower = MakePlayer();
            thrower.ServerAuthority = false;
            thrower.SetRemoteTossCharge(0.6f);
            Assert.IsNull(thrower.TossVictim, "참가자 기계의 내 손은 비어 있을 수 있다.");

            thrower.Tick();

            Assert.AreEqual(0.6f, thrower.TossCharge01, 1e-3f,
                "자기 손이 비었다고 서버가 보낸 게이지를 지우면 안 된다.");
        }

        [Test]
        public void TheServerSendsTheChargeAndTheHudDrawsIt()
        {
            var net = TestSource.Blank(TestSource.Read("Runtime/Network/NetPlayer.cs"));
            Assert.That(net, Does.Contain("tossCharge = body.TossCharge01;"),
                "서버가 채우지 않으면 참가자 게이지가 영영 0 이다.");
            Assert.That(net, Does.Contain("body.SetRemoteTossCharge(tossCharge)"),
                "받은 값을 몸에 넣는 자리가 있어야 한다.");

            var hud = TestSource.Blank(TestSource.Read("Runtime/UI/GameHud.cs"));
            Assert.That(hud, Does.Contain("Local.ChargingToss"),
                "그리는 자리가 없으면 값이 와도 안 보인다 — 그릴 곳은 있는데 그릴 값이 "
                + "없던 -438·-448 의 반대 경우다.");
            Assert.That(hud, Does.Contain("Local.TossCharge01"));
        }

        // ── 던지는 동작을 몇 번째인지로 알린다 ──────────────────

        /// <summary>
        /// <b>처음 받은 값으로는 재생하지 않는다.</b> 늦게 들어온 사람에게는 이미 쌓인
        /// 수가 한꺼번에 오는데, 그것을 새 던지기로 보면 접속하자마자 모두가 허공에
        /// 던지는 시늉을 한다.
        /// </summary>
        [Test]
        public void TheFirstValueIsOnlyARulerNotAThrow()
        {
            Assert.IsFalse(Network.NetPlayer.ShouldPlayToss(0, 7, known: false),
                "처음 본 수는 기준점일 뿐이다.");
        }

        [Test]
        public void ANewNumberPlaysTheThrow()
        {
            Assert.IsTrue(Network.NetPlayer.ShouldPlayToss(7, 8, known: true));
        }

        [Test]
        public void TheSameNumberDoesNotPlayItAgain()
        {
            Assert.IsFalse(Network.NetPlayer.ShouldPlayToss(8, 8, known: true),
                "값이 그대로면 같은 던지기다 — 매 틱 다시 재생하면 안 된다.");
        }

        /// <summary><c>uint</c> 는 한 바퀴 돌면 작아진다. <c>&gt;</c> 로 보면 거기서 멈춘다.</summary>
        [Test]
        public void ItSurvivesTheCounterWrappingAround()
        {
            Assert.IsTrue(Network.NetPlayer.ShouldPlayToss(uint.MaxValue, 0, known: true),
                "한 바퀴 돌아도 새 던지기다.");
        }

        // ── 동작이 실제로 있는가 ────────────────────────────────

        /// <summary>
        /// <b>구운 클립이 읽는 쪽이 쓰는 뼈를 전부 움직이는가.</b>
        ///
        /// <para>블렌더에서 허리에 키를 넣어도 <c>TossBoneNames</c> 에 없으면 코드가
        /// 건너뛰고, 반대로 목록에 있어도 클립에 커브가 없으면 그 뼈는 기준 자세로
        /// 끌려간다. <b>둘 중 하나만 어긋나도 조용히 이상해진다</b> — 그래서 양쪽을
        /// 맞대어 본다.</para>
        /// </summary>
        [Test]
        public void TheBakedClipDrivesEveryBoneTheCodeReads()
        {
            var clip = Resources.Load<AnimationClip>(RabbitLocomotion.TossResource);
            Assert.IsNotNull(clip, "던지기 클립이 Resources 에 없다 — 굽지 않았다.");

            var driven = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var slash = binding.path.LastIndexOf('/');
                driven.Add(slash < 0 ? binding.path : binding.path.Substring(slash + 1));
            }

            var missing = RabbitLocomotion.TossBoneNames.Where(n => !driven.Contains(n)).ToArray();
            Assert.IsEmpty(missing, "클립이 안 움직이는 뼈: " + string.Join(", ", missing));
        }

        /// <summary>
        /// 던지기는 <b>허리와 척추</b>를 움직여야 한다. 보행 목록에는 그 뼈가 없어서,
        /// 목록을 늘리지 않으면 팔만 허우적거리는 그림이 된다.
        /// </summary>
        [Test]
        public void TheTossReachesTheSpineWhichWalkingNeverTouches()
        {
            Assert.Contains("Waist", RabbitLocomotion.TossBoneNames.ToArray());
            Assert.Contains("Spine01", RabbitLocomotion.TossBoneNames.ToArray());
            CollectionAssert.DoesNotContain(RabbitLocomotion.BoneNames.ToArray(), "Waist",
                "걷기 목록에 넣으면 걷는 동안 허리가 기준 자세로 끌려가 뻣뻣해진다.");
        }

        /// <summary>
        /// <b>굽는 목록과 읽는 목록이 같아야 한다</b> (!436 AI 리뷰).
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않으므로 뼈 이름이
        /// <b>두 곳에 따로</b> 적혀 있다(<c>RabbitLocomotionAssets</c> 가 먼저 그렇게
        /// 하고 있었다). 한쪽만 고치면 <b>커브가 있어도 읽는 쪽이 건너뛰거나, 읽는데
        /// 커브가 없다.</b> 둘 다 에러 없이 조용히 이상해진다.</para>
        ///
        /// <para>참조를 만들 수 없으니 <b>소스 글자로 맞대어 본다.</b> 굽는 쪽은 이미
        /// "하나라도 안 움직이면 실패" 로 막지만, 그것은 <b>굽을 때</b>만 돈다 — 목록이
        /// 어긋난 채 굽지 않고 머지되는 길을 여기서 막는다.</para>
        /// </summary>
        [Test]
        public void TheBakeListAndTheRuntimeListAreTheSame()
        {
            var editor = TestSource.Read("Editor/RabbitTossAssets.cs");
            var declared = System.Text.RegularExpressions.Regex.Match(
                editor, @"static readonly string\[\] BoneNames\s*=\s*\{(.*?)\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.IsTrue(declared.Success, "굽는 쪽에서 BoneNames 선언을 찾지 못했다.");

            var baked = System.Text.RegularExpressions.Regex
                .Matches(declared.Groups[1].Value, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToArray();

            CollectionAssert.AreEquivalent(RabbitLocomotion.TossBoneNames.ToArray(), baked,
                "굽는 뼈 목록과 읽는 뼈 목록이 다르다 — 한쪽만 고치면 조용히 안 움직인다.");
        }

        /// <summary>
        /// <b>구운 클립에 런타임 바인딩 표가 있는가.</b>
        ///
        /// <para><c>m_ClipBindingConstant.genericBindings</c> 가 커브를 실제 뼈에 붙이는
        /// 표다. <b>이것이 비면 에디터에서는 커브가 멀쩡히 보이는데 빌드에서는 아무 뼈도
        /// 안 움직인다.</b></para>
        ///
        /// <para>실제로 한 번 그렇게 됐다 — 덮어쓰기(<c>CopySerialized</c>)로 다시 구우면
        /// 그 표가 <b>빈 채로</b> 저장된다(표는 새 에셋을 직렬화할 때 만들어진다).
        /// 위의 <c>TheBakedClipDrivesEveryBoneTheCodeReads</c> 는 <b>에디터 커브</b>를
        /// 보므로 이 결함을 통과시킨다 — 그래서 파일 자체를 본다.</para>
        /// </summary>
        [Test]
        public void TheShippedClipCanActuallyBindAtRuntime()
        {
            var path = System.IO.Path.Combine(
                Application.dataPath,
                "_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_TossUp.anim");
            Assert.IsTrue(System.IO.File.Exists(path), "구운 클립 파일이 없다: " + path);

            var text = System.IO.File.ReadAllText(path);

            Assert.That(text, Does.Not.Contain("genericBindings: []"),
                "바인딩 표가 비었다 — 빌드에서 아무 뼈도 안 움직인다.");

            var bindings = System.Text.RegularExpressions.Regex.Matches(text, @"typeID: 4").Count;
            Assert.GreaterOrEqual(bindings, RabbitLocomotion.TossBoneNames.Count,
                "뼈 수보다 바인딩이 적다 — 일부 뼈는 빌드에서 안 움직인다.");
        }

        [Test]
        public void TheClipIsShortAndDoesNotLoop()
        {
            var clip = Resources.Load<AnimationClip>(RabbitLocomotion.TossResource);

            Assert.Less(clip.length, 2f, "한 번짜리 동작이 2초를 넘으면 조작이 멈춘 것처럼 느껴진다.");
            Assert.Greater(clip.length, 0.4f, "너무 짧으면 들어올리는 무게가 안 보인다.");
            Assert.IsFalse(clip.isLooping, "반복하면 영원히 던진다.");
        }

        // ── 동작이 물리 자세에서 들어갔다 나오는가 ──────────────

        [Test]
        public void TheThrowFadesInAndOut()
        {
            const float length = 1f;

            Assert.Zero(RabbitLocomotion.TossWeight(0f, length), "시작은 물리 자세다.");
            Assert.AreEqual(1f, RabbitLocomotion.TossWeight(length * 0.5f, length), 1e-3f,
                "가운데는 온전히 동작이다.");
            Assert.Zero(RabbitLocomotion.TossWeight(length, length), "끝나면 물리로 돌아간다.");
            Assert.Greater(RabbitLocomotion.TossWeight(0.05f, length), 0f, "들어가는 중.");
            Assert.Less(RabbitLocomotion.TossWeight(0.05f, length), 1f, "아직 다 안 들어갔다.");
        }

        [Test]
        public void ItNeverPlaysOutsideItsOwnTime()
        {
            Assert.Zero(RabbitLocomotion.TossWeight(-0.2f, 1f));
            Assert.Zero(RabbitLocomotion.TossWeight(2f, 1f));
            Assert.Zero(RabbitLocomotion.TossWeight(0.5f, 0f), "길이가 0 인 클립은 그리지 않는다.");
        }

        // ── 잡기 규칙 ───────────────────────────────────────────

        /// <summary>
        /// <b>양손이어야 던진다.</b> 동작 자체가 양손으로 허리를 잡아 넘기는 것이다.
        /// </summary>
        [Test]
        public void NobodyIsHeldSoNobodyCanBeThrown()
        {
            var thrower = MakePlayer();

            Assert.IsNull(thrower.TossVictim, "아무도 안 잡았으면 던질 사람이 없다.");
        }

        [Test]
        public void TheThrowRunsOnlyAfterTheHandsLetGo()
        {
            var rig = TestSource.Blank(TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"));
            var toss = TestSource.Method(rig, "void TossTeammate(PlayerRig victim, Vector3 aim, float charge)");

            Assert.That(toss, Does.Contain("ReleaseHand"),
                "잡은 손을 먼저 놓지 않으면 조인트가 도로 끌어당긴다.");
            Assert.That(toss, Does.Contain("pendingTossVictim"),
                "같은 틱에 속도를 넣으면 사라지지 않은 구속이 그것을 먹는다 — 도구 투척이 "
                + "이미 한 틱 늦추는 이유다.");
        }

        // ── 도구 ────────────────────────────────────────────────

        PlayerRig MakePlayer()
        {
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
            var cam = OrbitCamera.Create(player.Hips.transform, Color.black, true);
            _spawned.Add(cam.gameObject);
            player.Cam = cam;
            return player;
        }
    }
}
