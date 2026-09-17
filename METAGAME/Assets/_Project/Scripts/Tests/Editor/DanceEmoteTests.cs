using System.Linq;
using System.Text.RegularExpressions;
using Metagame.Core;
using Metagame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// F2 로 고른 춤이 <b>남에게도 보이는가</b>를 지킨다 (S15P21B202-563).
    ///
    /// <para>감정표현은 남에게 보이라고 있는 기능이다. 내 화면에서만 도는 춤은 쓸모가
    /// 없으므로, 이 스위트의 무게는 "재생되는가" 가 아니라 <b>"남의 몸에서도 같은 길로
    /// 재생되는가"</b> 와 <b>"14.23초가 그대로 도는가"</b> 에 있다.</para>
    /// </summary>
    public sealed class DanceEmoteTests
    {
        PlayerRig _player;
        bool _puffEnabled;
        SpaceSuit Suit => _player.Doll.Suit;

        [SetUp]
        public void SetUp()
        {
            _puffEnabled = Puff.Enabled;
            Puff.Enabled = false;
            _player = PlayerRig.Create("Dance test", Vector3.zero, Quaternion.identity, Color.white, Color.gray);
            Assert.NotNull(Suit, "Existing rabbit prefab must still load.");
            Suit.InitializeLocomotion();
            Assert.NotNull(Suit.Locomotion, "Both baked resources must be included.");
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                Suit?.Locomotion?.Dispose();
                Object.DestroyImmediate(_player.gameObject);
            }
            Puff.Enabled = _puffEnabled;
        }

        static RabbitLocomotion.State Standing => new() { Grounded = true, Speed = 0f };

        static RabbitLocomotion.State Walking => new()
        { Grounded = true, Speed = Tuning.WalkSpeed, Direction = Vector2.up, MoveInput = true };

        /// <summary>밀려서 움직이는 몸. 방향키는 안 눌렀는데 속도만 있다.</summary>
        static RabbitLocomotion.State Shoved => new()
        { Grounded = true, Speed = RabbitLocomotion.DanceShoveSpeed + 0.5f };

        /// <summary>제자리에서 살짝 밀린 몸. 예전 문턱(0.15)은 여기서 춤을 죽였다.</summary>
        static RabbitLocomotion.State Nudged => new()
        { Grounded = true, Speed = 0.4f };

        void Look(float yaw, int frames = 60)
        {
            _player.SetInput(new InputFrame { AimYaw = yaw });
            for (var i = 0; i < frames; i++) _player.Tick();
        }

        void Advance(RabbitLocomotion.State state, int frames = 6)
        {
            for (int i = 0; i < frames; i++)
            {
                Suit.Sync();
                Suit.Locomotion.Apply(state, 1f / 60f);
            }
        }

        Transform Bone(string name) =>
            Suit.Model.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        // ── 에셋 ──────────────────────────────────────────────────────

        [Test]
        public void TheDanceClipShipsAndIsAsLongAsTheSource()
        {
            var clip = Resources.Load<AnimationClip>(RabbitLocomotion.DanceResource);
            Assert.NotNull(clip, "춤 클립이 Resources 에 없다 — F2 를 눌러도 아무 일도 안 일어난다.");

            // **원본 영상이 14.23초다.** 배속하지도 건너뛰지도 않는 것이 이 작업의 조건이라,
            // 길이가 줄면 어딘가에서 프레임을 버렸다는 뜻이다.
            Assert.That(clip.length, Is.EqualTo(14.23f).Within(0.1f),
                "춤 길이가 원본(14.23초)과 다르다 — 배속했거나 프레임을 건너뛰었다.");
            Assert.False(clip.isLooping, "한 번짜리 감정표현이다.");
            Assert.False(clip.humanMotion, "리타기팅 없이 이 뼈대 위에서 만든 클립이다.");
        }

        /// <summary>
        /// <b>구운 클립에 런타임 바인딩 표가 있는가.</b>
        ///
        /// <para>던지기에서 실제로 당한 자리다 — <c>genericBindings</c> 가 비면 에디터에서는
        /// 커브가 멀쩡히 보이는데 <b>빌드에서는 아무 뼈도 안 움직인다.</b> 에디터 커브를
        /// 보는 검증은 이 결함을 통과시키므로 파일 자체를 본다.</para>
        /// </summary>
        [Test]
        public void TheShippedDanceCanActuallyBindAtRuntime()
        {
            var path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_Dance.anim");
            Assert.IsTrue(System.IO.File.Exists(path), "구운 클립 파일이 없다: " + path);

            var text = System.IO.File.ReadAllText(path);
            Assert.That(text, Does.Not.Contain("genericBindings: []"),
                "바인딩 표가 비었다 — 빌드에서 아무 뼈도 안 움직인다.");
            Assert.GreaterOrEqual(Regex.Matches(text, @"typeID: 4").Count,
                RabbitLocomotion.TossBoneNames.Count,
                "뼈 수보다 바인딩이 적다 — 일부 뼈는 빌드에서 안 움직인다.");
        }

        /// <summary>
        /// <b>굽는 목록과 읽는 목록이 같아야 한다.</b>
        ///
        /// <para>에디터 어셈블리는 게임플레이 어셈블리를 참조하지 않아 뼈 이름이 두 곳에
        /// 따로 적혀 있다. 한쪽만 고치면 커브가 있어도 읽는 쪽이 건너뛰거나, 읽는데
        /// 커브가 없다 — 둘 다 에러 없이 조용히 이상해진다. 던지기가 겪은 그대로다.</para>
        /// </summary>
        [Test]
        public void TheDanceBakeListAndTheRuntimeListAreTheSame()
        {
            var editor = TestSource.Read("Editor/RabbitDanceAssets.cs");
            var declared = Regex.Match(editor, @"static readonly string\[\] BoneNames\s*=\s*\{(.*?)\}",
                RegexOptions.Singleline);
            Assert.IsTrue(declared.Success, "굽는 쪽에서 BoneNames 선언을 찾지 못했다.");

            var baked = Regex.Matches(declared.Groups[1].Value, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray();

            CollectionAssert.AreEquivalent(RabbitLocomotion.TossBoneNames.ToArray(), baked,
                "굽는 뼈 목록과 읽는 뼈 목록이 다르다 — 한쪽만 고치면 조용히 안 움직인다.");
        }

        // ── 재생 ──────────────────────────────────────────────────────

        [Test]
        public void DancingMovesTheUpperBodyThatWalkingLeavesAlone()
        {
            // 걷기는 허리를 물리에 맡긴다. 춤이 허리를 쓴다는 것은 곧 춤이 보행 경로가
            // 아니라 한 번짜리 경로를 탔다는 뜻이다.
            Advance(Standing);
            Suit.Sync();
            var physics = Bone("Waist").localRotation;

            Suit.PlayDance(1);
            Assert.IsTrue(Suit.Locomotion.DanceActive, "PlayDance 가 아무것도 안 켰다.");
            Advance(Standing, 20);

            Assert.Greater(Quaternion.Angle(physics, Bone("Waist").localRotation), 1f,
                "춤이 허리를 안 움직인다 — 보행 경로로 샜거나 클립이 비었다.");
            Assert.Greater(Suit.Locomotion.DanceWeightNow, 0f);
        }

        [Test]
        public void TheDanceStopsOnItsOwnWhenTheClipEnds()
        {
            Suit.PlayDance(1);
            var length = Suit.Locomotion.DanceLength(1);
            Assert.Greater(length, 1f);

            // 클립 길이를 넘겨 돌린다. 끝나고도 안 꺼지면 영영 보행으로 못 돌아간다.
            Advance(Standing, Mathf.CeilToInt(length * 60f) + 10);
            Assert.IsFalse(Suit.Locomotion.DanceActive, "클립이 끝났는데 춤이 안 꺼진다.");
        }

        /// <summary>
        /// <b>걸으면 끊긴다</b> — 그리고 <b>가만히 있으면 안 끊긴다.</b>
        ///
        /// <para>두 짝을 함께 둔다. 앞의 것만 두면 "언제나 끊는다" 는 구현이 통과하고,
        /// 그것은 춤이 아예 안 되는 것과 같다.</para>
        /// </summary>
        [Test]
        public void WalkingCancelsTheDanceButStandingStillDoesNot()
        {
            Suit.PlayDance(1);
            Advance(Standing, 10);
            Assert.IsTrue(Suit.Locomotion.DanceActive, "가만히 있는데 춤이 끊겼다.");

            Advance(Walking, 2);
            Assert.IsFalse(Suit.Locomotion.DanceActive, "걷는데 춤이 안 끊긴다 — 다리는 춤추고 몸은 미끄러진다.");
        }

        /// <summary>
        /// <b>춤을 끝내는 것은 속도가 아니라 방향키다</b> (S15P21B202-596).
        ///
        /// <para>예전 검증은 "문턱 = 걷기 시작 속도(0.15)" 를 잠그고 있었다. 596 이
        /// 그 전제를 바꿨다 — 속도는 결과라서 늦고, <b>밀려서 난 속도와 제 발로 낸
        /// 속도를 구별하지 못한다.</b> 전제를 바꿨으면 그것을 재는 검증도 같이
        /// 바꿔야 한다(576 의 유형 R).</para>
        /// </summary>
        [Test]
        public void ItIsTheMoveKeyThatEndsTheDanceNotTheSpeed()
        {
            Suit.PlayDance(1);
            Advance(Nudged, 10);
            Assert.IsTrue(Suit.Locomotion.DanceActive,
                "방향키를 안 눌렀는데 살짝 밀렸다고 춤이 죽는다 — 예전 문턱(0.15)의 증상이다.");

            var still = Standing;
            still.MoveInput = true;
            Advance(still, 2);
            Assert.IsFalse(Suit.Locomotion.DanceActive,
                "방향키를 눌렀는데 춤이 안 끊긴다 — 속도가 아직 안 붙었을 뿐이다.");
        }

        [Test]
        public void BeingShovedStillEndsTheDance()
        {
            Suit.PlayDance(1);
            Advance(Shoved, 2);
            Assert.IsFalse(Suit.Locomotion.DanceActive,
                "밀려서 걷는 만큼 움직이는데 춤이 남아 있다 — 다리는 춤추고 몸은 미끄러진다.");
        }

        /// <summary>끊는 조건은 <b>목록의 모든 춤</b>에 걸린다.</summary>
        [Test]
        public void EveryDanceEndsOnTheMoveKey()
        {
            for (byte id = 1; id <= DanceCatalog.Count; id++)
            {
                Suit.PlayDance(id);
                Advance(Standing, 4);
                Assert.IsTrue(Suit.Locomotion.DanceActive,
                    $"{id}번 '{DanceCatalog.NameOf(id)}' 이 가만히 있는데 끊겼다.");

                var moving = Standing;
                moving.MoveInput = true;
                Advance(moving, 2);
                Assert.IsFalse(Suit.Locomotion.DanceActive,
                    $"{id}번 '{DanceCatalog.NameOf(id)}' 이 방향키에 안 끊긴다.");
            }
        }

        [Test]
        public void BeingDownedCancelsTheDance()
        {
            Suit.PlayDance(1);
            var blocked = Standing;
            blocked.Blocked = true;
            Advance(blocked, 2);
            Assert.IsFalse(Suit.Locomotion.DanceActive,
                "기절·탑승이 몸을 가져갔는데 춤이 남아 있다 — 어긋난 그림이 된다.");
        }

        [Test]
        public void TheTossAndTheDanceDoNotPlayAtOnce()
        {
            Suit.PlayDance(1);
            Assert.IsTrue(Suit.Locomotion.DanceActive);

            Suit.PlayToss();
            Assert.IsTrue(Suit.Locomotion.TossActive, "던지기가 안 켜졌다.");
            Assert.IsFalse(Suit.Locomotion.DanceActive,
                "춤과 던지기가 동시에 돈다 — 두 클립이 같은 뼈를 두고 다툰다.");
        }

        // ── 남에게 보이는가 (순수 계산 + 호출 자리) ────────────────────

        /// <summary>
        /// 던지기와 같은 두 가지 함정을 그대로 막는다: 늦게 들어온 사람의 몰아보기와
        /// <c>uint</c> 한 바퀴.
        /// </summary>
        [Test]
        public void TheFirstValueIsOnlyABaselineAndTheCounterMayWrap()
        {
            Assert.IsFalse(Network.NetPlayer.ShouldPlayDance(0, 7, known: false),
                "처음 받은 값으로 재생하면 접속하자마자 모두가 춤을 춘다.");
            Assert.IsTrue(Network.NetPlayer.ShouldPlayDance(7, 8, known: true));
            Assert.IsFalse(Network.NetPlayer.ShouldPlayDance(8, 8, known: true));
            Assert.IsTrue(Network.NetPlayer.ShouldPlayDance(uint.MaxValue, 0, known: true),
                "수가 한 바퀴 돌면 춤이 영영 멈춘다 — '>' 가 아니라 '!=' 여야 한다.");
        }

        /// <summary>
        /// <b>함수를 검증했다고 그 함수가 불린다는 뜻은 아니다.</b>
        ///
        /// <para>S15P21B202-538 에서 <c>NextSendTimer</c> 를 다 검증해 두고도 호출 자리가
        /// 비어 있던 적이 있다. 여기서도 같은 구멍이 난다 — 위 규칙이 아무리 맞아도
        /// 참가자 쪽에서 부르지 않으면 <b>남의 화면에 춤이 안 보인다.</b></para>
        /// </summary>
        [Test]
        public void TheClientSideActuallyCallsTheDanceHandler()
        {
            var net = TestSource.Read("Runtime/Network/NetPlayer.cs");

            Assert.That(net, Does.Contain("PlayDanceIfNew(danceSeq, danceId)"),
                "참가자 쪽에서 춤을 켜는 호출이 없다 — 규칙만 맞고 화면엔 아무것도 안 나온다.");
            Assert.That(net, Does.Contain("[SyncVar] public uint danceSeq"),
                "순번이 동기화되지 않으면 남에게 갈 길이 없다.");
            Assert.That(net, Does.Contain("body.Doll?.Suit?.PlayDance(id)"),
                "서버 쪽에서 켜는 자리가 없다 — 호스트 화면에서 참가자의 춤이 안 보인다.");
        }

        [Test]
        public void OnlyTheOwnerCanAskAndOnlyForARealDance()
        {
            Assert.IsFalse(DanceCatalog.IsValid(0), "0 은 '없음' 이다.");
            Assert.IsTrue(DanceCatalog.IsValid(1));
            Assert.IsFalse(DanceCatalog.IsValid((byte)(DanceCatalog.Count + 1)),
                "목록 밖 번호를 받으면 남의 화면에서 엉뚱한 클립을 찾는다.");
            Assert.That(DanceCatalog.NameOf(1), Is.Not.Empty);
            Assert.That(DanceCatalog.NameOf(0), Is.Empty);

            var net = TestSource.Read("Runtime/Network/NetPlayer.cs");
            Assert.That(net, Does.Contain("if (isLocalPlayer && DanceCatalog.IsValid(id)) CmdPlayDance(id)"),
                "남의 캐릭터를 춤추게 만들 수 있으면 안 된다.");
        }

        // ── 고르는 바퀴 ────────────────────────────────────────────────

        [Test]
        public void TheWheelPicksNothingNearTheCentre()
        {
            Assert.AreEqual(-1, DanceHud.Selection(Vector2.zero, DanceCatalog.Count),
                "가운데에서 손을 떼면 아무것도 고르지 않아야 한다.");
            Assert.AreEqual(-1, DanceHud.Selection(new Vector2(10f, 0f), DanceCatalog.Count));
        }

        [Test]
        public void TheWheelSplitsEvenlyHoweverManyDancesThereAre()
        {
            // 춤이 하나뿐일 때는 어느 쪽을 가리켜도 그 하나다.
            Assert.AreEqual(0, DanceHud.Selection(new Vector2(0f, 200f), 1));
            Assert.AreEqual(0, DanceHud.Selection(new Vector2(0f, -200f), 1));

            // 넷이면 위/오른쪽/아래/왼쪽으로 갈린다. 목록이 늘어도 UI 가 안 깨지는지 본다.
            Assert.AreEqual(0, DanceHud.Selection(new Vector2(0f, 200f), 4));
            Assert.AreEqual(1, DanceHud.Selection(new Vector2(200f, 0f), 4));
            Assert.AreEqual(2, DanceHud.Selection(new Vector2(0f, -200f), 4));
            Assert.AreEqual(3, DanceHud.Selection(new Vector2(-200f, 0f), 4));
        }

        /// <summary>
        /// 바퀴가 열려 있는 동안 게임 입력이 막혀야 한다. 안 막으면 춤을 고르는 마우스
        /// 움직임이 그대로 시점 회전으로 샌다.
        /// </summary>
        [Test]
        public void TheDanceWheelBlocksGameInputTheSameWayTheEmoteWheelDoes()
        {
            var session = TestSource.Read("Runtime/Gameplay/GameSession.cs");

            Assert.That(session, Does.Contain("WheelOpen => EmoteWheelOpen || DanceWheelOpen"),
                "두 바퀴를 함께 보는 자리가 없다.");
            Assert.That(session, Does.Contain("var f = WheelOpen ? new InputFrame() : Controls.Read()"),
                "춤 바퀴가 열려 있는데 게임 입력이 그대로 들어온다.");

            // **깃발을 하나로 합치지 않았는지도 본다.** 합치면 한쪽 HUD 가 닫히면서
            // 다른 쪽이 열어 둔 깃발을 내린다.
            Assert.That(session, Does.Contain("public bool DanceWheelOpen"),
                "춤 바퀴가 제 깃발을 갖고 있지 않다.");
        }

        // ── 두 번째 춤 REDRED (S15P21B202-576) ─────────────────────────

        [Test]
        public void TheDanceListIsWhatWeShip()
        {
            CollectionAssert.AreEqual(new[] { "뮤직챌린지", "REDRED", "BAD" }, DanceCatalog.Names,
                "목록과 이름이 바뀌었다 — 순서가 곧 번호라 바뀌면 남의 화면에서 다른 춤이 돈다.");
        }

        /// <summary>
        /// <b>목록에 있는 춤은 전부 실제로 돌 수 있어야 한다.</b>
        ///
        /// <para>이름은 <c>DanceCatalog</c> 에, 클립과 슬롯은 <c>RabbitLocomotion</c> 에
        /// 따로 적힌다. 한쪽만 늘리면 <b>고를 수는 있는데 아무 일도 안 일어난다</b> —
        /// 에러도 없다. 563 에서 뼈 목록이 두 곳에 갈려 있어 같은 종류의 사고를 겪었다.</para>
        /// </summary>
        [Test]
        public void EveryDanceInTheListHasASlotAndAClip()
        {
            Assert.AreEqual(DanceCatalog.Count, Suit.Locomotion.DanceSlotCount,
                "목록의 춤 수와 재생 슬롯 수가 다르다 — 고를 수는 있는데 안 도는 춤이 생긴다.");

            for (byte id = 1; id <= DanceCatalog.Count; id++)
            {
                Assert.IsTrue(Suit.Locomotion.HasDance(id),
                    $"{id}번 '{DanceCatalog.NameOf(id)}' 의 클립이 없다.");
                Assert.Greater(Suit.Locomotion.DanceLength(id), 1f,
                    $"{id}번 '{DanceCatalog.NameOf(id)}' 클립이 비었다.");
            }
        }

        [Test]
        public void TheRedRedClipShipsAndIsAsLongAsTheSource()
        {
            var clip = Resources.Load<AnimationClip>(RabbitLocomotion.RedRedResource);
            Assert.NotNull(clip, "REDRED 클립이 Resources 에 없다.");

            // 원본 영상이 16.27초다. 줄면 프레임을 버린 것이다.
            Assert.That(clip.length, Is.EqualTo(16.27f).Within(0.1f),
                "REDRED 길이가 원본(16.27초)과 다르다 — 배속했거나 프레임을 건너뛰었다.");
            Assert.False(clip.isLooping, "한 번짜리 감정표현이다.");
            Assert.False(clip.humanMotion, "리타기팅 없이 이 뼈대 위에서 만든 클립이다.");
        }

        [Test]
        public void TheShippedRedRedCanActuallyBindAtRuntime()
        {
            var path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_RedRed.anim");
            Assert.IsTrue(System.IO.File.Exists(path), "구운 클립 파일이 없다: " + path);

            var text = System.IO.File.ReadAllText(path);
            Assert.That(text, Does.Not.Contain("genericBindings: []"),
                "바인딩 표가 비었다 — 빌드에서 아무 뼈도 안 움직인다.");
            Assert.GreaterOrEqual(Regex.Matches(text, @"typeID: 4").Count,
                RabbitLocomotion.TossBoneNames.Count,
                "뼈 수보다 바인딩이 적다 — 일부 뼈는 빌드에서 안 움직인다.");
        }

        [Test]
        public void TheRedRedBakeListAndTheRuntimeListAreTheSame()
        {
            var editor = TestSource.Read("Editor/RabbitRedRedAssets.cs");
            var declared = Regex.Match(editor, @"static readonly string\[\] BoneNames\s*=\s*\{(.*?)\}",
                RegexOptions.Singleline);
            Assert.IsTrue(declared.Success, "굽는 쪽에서 BoneNames 선언을 찾지 못했다.");

            var baked = Regex.Matches(declared.Groups[1].Value, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray();

            CollectionAssert.AreEquivalent(RabbitLocomotion.TossBoneNames.ToArray(), baked,
                "굽는 뼈 목록과 읽는 뼈 목록이 다르다 — 한쪽만 고치면 조용히 안 움직인다.");
        }

        /// <summary>
        /// <b>2번 춤도 실제로 뼈를 움직이는가.</b>
        ///
        /// <para>믹서 입력 수가 모자라면 새 클립이 그래프에 못 붙고, 그래도 에러 없이
        /// '재생 중' 으로만 보인다. 그래서 세기가 아니라 <b>뼈가 움직였는지</b>를 본다.</para>
        /// </summary>
        [Test]
        public void TheSecondDanceActuallyMovesBones()
        {
            Advance(Standing);
            Suit.Sync();
            var physics = Bone("Waist").localRotation;

            Suit.PlayDance(2);
            Assert.IsTrue(Suit.Locomotion.DanceActive, "2번 춤이 안 켜졌다.");
            Advance(Standing, 20);

            Assert.Greater(Quaternion.Angle(physics, Bone("Waist").localRotation), 1f,
                "2번 춤이 뼈를 안 움직인다 — 믹서 입력이 모자라 그래프에 못 붙었을 수 있다.");
        }

        [Test]
        public void TwoDancesDoNotPlayAtOnce()
        {
            Suit.PlayDance(1);
            Assert.IsTrue(Suit.Locomotion.DanceActive);
            Suit.PlayDance(2);
            Advance(Standing, 2);
            Assert.IsTrue(Suit.Locomotion.DanceActive, "2번을 켰는데 아무것도 안 돈다.");
            Assert.Greater(Suit.Locomotion.DanceWeightNow, 0f,
                "두 춤이 서로를 끄기만 하고 아무것도 안 남았다.");
        }

        [Test]
        public void TheWheelSplitsIntoTwoWhenThereAreTwoDances()
        {
            Assert.AreEqual(0, DanceHud.Selection(new Vector2(0f, 200f), 2), "위는 1번");
            Assert.AreEqual(1, DanceHud.Selection(new Vector2(0f, -200f), 2), "아래는 2번");
        }

        /// <summary>
        /// 셋일 때도 갈린다.
        ///
        /// <para><b>예전 검증은 <c>DanceCatalog.Count == 2</c> 를 박아 두고 있었다.</b>
        /// 춤을 하나 더 넣으면 동작은 멀쩡한데 이 줄이 먼저 터진다 — 576 에서 네 번 겪은
        /// 유형 R(고친 것을 재는 검증이 같이 안 낡는다)이라 개수를 박지 않고 고친다.</para>
        /// </summary>
        [Test]
        public void TheWheelSplitsIntoThreeWhenThereAreThreeDances()
        {
            Assert.AreEqual(0, DanceHud.Selection(new Vector2(0f, 200f), 3), "위는 1번");
            Assert.AreEqual(1, DanceHud.Selection(new Vector2(173f, -100f), 3), "오른아래는 2번");
            Assert.AreEqual(2, DanceHud.Selection(new Vector2(-173f, -100f), 3), "왼아래는 3번");
        }

        /// <summary>바퀴가 목록만큼 갈리는가. 개수가 몇이든 성립해야 한다.</summary>
        [Test]
        public void TheWheelHasExactlyOneSliceForEveryDance()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            var count = DanceCatalog.Count;
            for (var i = 0; i < count; i++)
            {
                var angle = i * Mathf.PI * 2f / count;
                var picked = DanceHud.Selection(
                    new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 200f, count);
                Assert.AreEqual(i, picked, $"{i}번 칸의 한가운데를 가리켰는데 {picked} 이 골라졌다.");
                seen.Add(picked);
            }
            Assert.AreEqual(count, seen.Count, "두 칸이 같은 춤을 가리킨다.");
        }
        // ── 세 번째 춤 BAD (S15P21B202-593) ────────────────────────────

        [Test]
        public void TheBadClipShipsAndIsAsLongAsTheSource()
        {
            var clip = Resources.Load<AnimationClip>(RabbitLocomotion.BadResource);
            Assert.NotNull(clip, "BAD 클립이 Resources 에 없다.");

            // 원본 영상이 732프레임 · 30fps 라 클립은 731/30 = 24.37초다.
            Assert.That(clip.length, Is.EqualTo(24.37f).Within(0.1f),
                "BAD 길이가 원본(24.37초)과 다르다 — 배속했거나 프레임을 건너뛰었다.");
            Assert.False(clip.isLooping, "한 번짜리 감정표현이다.");
            Assert.False(clip.humanMotion, "리타기팅 없이 이 뼈대 위에서 만든 클립이다.");
        }

        [Test]
        public void TheShippedBadCanActuallyBindAtRuntime()
        {
            var path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Animations/Characters/Rabbit/Resources/RabbitLocomotion/Rabbit_Bad.anim");
            Assert.IsTrue(System.IO.File.Exists(path), "구운 클립 파일이 없다: " + path);

            var text = System.IO.File.ReadAllText(path);
            Assert.That(text, Does.Not.Contain("genericBindings: []"),
                "바인딩 표가 비었다 — 빌드에서 아무 뼈도 안 움직인다.");
            Assert.GreaterOrEqual(Regex.Matches(text, @"typeID: 4").Count,
                RabbitLocomotion.TossBoneNames.Count,
                "뼈 수보다 바인딩이 적다 — 일부 뼈는 빌드에서 안 움직인다.");
        }

        [Test]
        public void TheBadBakeListAndTheRuntimeListAreTheSame()
        {
            var editor = TestSource.Read("Editor/RabbitBadAssets.cs");
            var declared = Regex.Match(editor, @"static readonly string\[\] BoneNames\s*=\s*\{(.*?)\}",
                RegexOptions.Singleline);
            Assert.IsTrue(declared.Success, "굽는 쪽에서 BoneNames 선언을 찾지 못했다.");

            var baked = Regex.Matches(declared.Groups[1].Value, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray();

            CollectionAssert.AreEquivalent(RabbitLocomotion.TossBoneNames.ToArray(), baked,
                "굽는 뼈 목록과 읽는 뼈 목록이 다르다 — 한쪽만 고치면 조용히 안 움직인다.");
        }

        /// <summary>
        /// <b>3번 춤도 실제로 뼈를 움직이는가.</b> 576 과 같은 이유로 세기가 아니라
        /// 뼈를 본다 — 믹서 입력이 모자라면 에러 없이 '재생 중' 으로만 보인다.
        /// </summary>
        [Test]
        public void TheThirdDanceActuallyMovesBones()
        {
            Advance(Standing);
            Suit.Sync();
            var physics = Bone("Waist").localRotation;

            Suit.PlayDance(3);
            Assert.IsTrue(Suit.Locomotion.DanceActive, "3번 춤이 안 켜졌다.");
            Advance(Standing, 20);

            Assert.Greater(Quaternion.Angle(physics, Bone("Waist").localRotation), 1f,
                "3번 춤이 뼈를 안 움직인다 — 믹서 입력이 모자라 그래프에 못 붙었을 수 있다.");
        }

        /// <summary>
        /// 셋이 서로를 밀어낸다. 둘일 때만 보던 것을 목록 전체로 넓힌다.
        /// </summary>
        [Test]
        public void OnlyTheLastPickedDanceIsLeftRunning()
        {
            for (byte id = 1; id <= DanceCatalog.Count; id++) Suit.PlayDance(id);
            Advance(Standing, 2);

            Assert.IsTrue(Suit.Locomotion.DanceActive, "마지막으로 고른 춤이 안 돈다.");
            Assert.Greater(Suit.Locomotion.DanceWeightNow, 0f,
                "춤들이 서로를 끄기만 하고 아무것도 안 남았다.");
        }

        // ── 춤 중에는 시점으로 몸이 돌지 않는다 (S15P21B202-596) ───────

        /// <summary>
        /// <b>이 스위트의 무게는 여기 있다.</b> 춤 클립은 뿌리를 안 돌리므로
        /// (<c>applyRootMotion = false</c>) 세계 방향은 전부 <c>PlayerRig.faceDir</c>
        /// 인데, 그것이 춤 중에도 카메라를 쫓으면 비틀린 상체 위에 골반까지 돌아
        /// 미리보기와 다른 그림이 된다.
        /// </summary>
        [Test]
        public void TurningTheViewWhileDancingDoesNotTurnTheBody()
        {
            Look(0f, 10);
            var before = _player.FacingDirection;

            Suit.PlayDance(1);
            Assert.IsTrue(_player.DanceHolding, "춤 중인데 몸이 잠기지 않았다.");
            Look(120f);

            Assert.Less(Vector3.Angle(before, _player.FacingDirection), 1f,
                "춤 중에 시점을 120도 돌렸더니 몸이 따라 돌았다.");
        }

        /// <summary>
        /// 그리고 <b>춤이 아닐 때는 예전처럼 따라 돈다.</b> 이걸 같이 재지 않으면
        /// "몸이 아예 안 돈다" 는 더 큰 결함을 잠그게 된다.
        /// </summary>
        [Test]
        public void TurningTheViewOutsideADanceStillTurnsTheBody()
        {
            Look(0f, 10);
            var before = _player.FacingDirection;

            Assert.IsFalse(_player.DanceHolding);
            Look(120f);

            Assert.Greater(Vector3.Angle(before, _player.FacingDirection), 90f,
                "춤이 아닌데 몸이 시점을 안 따라간다 — 조작이 죽었다.");
        }

        /// <summary>춤이 끝나면 잠금도 같이 풀린다. 안 풀리면 영영 못 돈다.</summary>
        [Test]
        public void TheBodyTurnsAgainOnceTheDanceIsCancelled()
        {
            Suit.PlayDance(1);
            Look(0f, 10);
            var before = _player.FacingDirection;

            Suit.CancelDance();
            Assert.IsFalse(_player.DanceHolding, "끝냈는데 잠금이 남아 있다.");
            Look(120f);

            Assert.Greater(Vector3.Angle(before, _player.FacingDirection), 90f,
                "춤이 끝났는데도 몸이 안 돈다.");
        }

        /// <summary>목록의 춤 전부가 몸을 잠근다 — 한 곡만 고치면 나머지는 그대로 돈다.</summary>
        [Test]
        public void EveryDanceLocksTheBody()
        {
            for (byte id = 1; id <= DanceCatalog.Count; id++)
            {
                Look(0f, 10);
                var before = _player.FacingDirection;

                Suit.PlayDance(id);
                Look(120f);
                Assert.Less(Vector3.Angle(before, _player.FacingDirection), 1f,
                    $"{id}번 '{DanceCatalog.NameOf(id)}' 중에 몸이 시점을 따라 돌았다.");

                Suit.CancelDance();
            }
        }

        /// <summary>
        /// <b>카메라는 막지 않는다.</b> 막는 것은 몸이지 둘러보는 것이 아니다 —
        /// 1인칭으로 제 춤을 보려면 시점은 돌아야 한다.
        /// </summary>
        [Test]
        public void TheViewItselfIsNotBlockedWhileDancing()
        {
            Suit.PlayDance(1);
            Look(90f);

            Assert.That(_player.CurrentInput.AimYaw, Is.EqualTo(90f).Within(0.001f),
                "춤이 시점 입력까지 막았다.");
            Assert.Greater(Vector3.Angle(_player.FacingDirection, _player.PlanarForward), 45f,
                "몸과 시점이 같이 돌았다 — 둘 중 하나만 돌아야 한다.");
        }

        [Test]
        public void F2IsBoundAndDoesNotCollideWithF1()
        {
            Assert.AreEqual(KeyCode.F2, Controls.DanceWheel);
            Assert.AreNotEqual(Controls.EmoteWheel, Controls.DanceWheel,
                "두 바퀴가 같은 키면 하나는 영영 안 열린다.");
        }
    }
}
