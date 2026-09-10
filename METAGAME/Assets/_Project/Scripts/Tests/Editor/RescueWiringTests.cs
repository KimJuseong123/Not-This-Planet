using System.Collections.Generic;
using System.Reflection;
using Metagame.Core;
using Metagame.Gameplay.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 기절한 동료 일으키기가 <b>실제로 배선돼 있는지</b> 지킨다 (S15P21B202-390).
    ///
    /// <para><b>왜 필요했나.</b> <see cref="DownedRescue"/> 는 홀드·진행분 누적·
    /// 이어받기까지 <b>완성돼 있었는데 부르는 곳이 없었다.</b> 3인 QA 로그 64,000줄에
    /// 구조 흔적이 0건이었고, 신고는 "게이지가 안 보인다" 로 왔다. 그러니 이 스위트가
    /// 지키는 것은 게이지가 아니라 <b>사슬이 끊기지 않았는가</b> 다 —
    /// 키 → <c>InputFrame</c> → 서버 전송 → 인터랙터 → <c>DownedRescue</c>.</para>
    /// </summary>
    public sealed class RescueWiringTests
    {
        /// <summary>
        /// <b>기본 키가 다른 것과 겹치지 않는다.</b> 이것을 값으로 확인한다 — 처음에
        /// <c>T</c> 를 쓰려 했는데 <c>StuckEscape</c> 가 기획 §21 로 이미 예약해
        /// 두었다. 사람 눈으로 훑어서는 놓친다.
        ///
        /// <para>일부러 겹치는 것들은 뺀다 — <c>Help</c> 는 <c>Tablet</c> 과 같은
        /// <c>Tab</c> 이고, <c>Tablet*PageAlternate</c> 는 <c>A</c>·<c>D</c> 를
        /// 이동과 나눠 쓴다(태블릿을 열었을 때만 쓰이므로 충돌하지 않는다).</para>
        /// </summary>
        [Test]
        public void DefaultKeyBindingsDoNotCollide()
        {
            var aliases = new HashSet<string>
            {
                "Help",                          // Tablet 과 같은 Tab
                "TabletPreviousPageAlternate",   // MoveLeft 와 같은 A
                "TabletNextPageAlternate",       // MoveRight 와 같은 D
            };

            var seen = new Dictionary<KeyCode, string>();

            foreach (var field in typeof(Controls).GetFields(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(KeyCode)) continue;
                if (aliases.Contains(field.Name)) continue;

                var key = (KeyCode)field.GetValue(null);
                if (seen.TryGetValue(key, out var other))
                    Assert.Fail($"{other} 와 {field.Name} 이 같은 키({key})다 — "
                                + "둘 중 하나가 눌러도 동작하지 않거나 엉뚱한 것이 돈다");

                seen[key] = field.Name;
            }

            Assert.That(seen.ContainsValue("RescueTeammate"), Is.True,
                "일으키기 키가 Controls 에 없다");
        }

        /// <summary>
        /// <b>입력 한 칸이 생겼고 채워진다.</b> 채우지 않으면 키를 눌러도 아무 데도
        /// 전달되지 않는다.
        /// </summary>
        [Test]
        public void TheKeyIsReadIntoTheInputFrame()
        {
            var frame = typeof(InputFrame).GetField("RescueHeld",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(frame, Is.Not.Null, "InputFrame.RescueHeld 가 없다");
            Assert.That(frame.FieldType, Is.EqualTo(typeof(bool)));

            var source = TestSource.Read("Runtime/Core/Controls.cs");
            Assert.That(TestSource.Blank(source), Does.Contain("f.RescueHeld"),
                "읽어 채우는 곳이 없으면 그 칸은 언제나 false 다");
        }

        /// <summary>
        /// <b>입력 비교에 들어가야 서버로 간다.</b> <c>NetPlayer.Same()</c> 이 같다고
        /// 보면 그 프레임을 보내지 않으므로, 새 칸이 여기 빠지면 <b>눌러도 서버가
        /// 모른다.</b> <c>StuckEscapeHeld</c> 가 실제로 그 상태다(채워지지만 비교에도
        /// 없고 읽는 곳도 없다).
        /// </summary>
        [Test]
        public void TheInputReachesTheServer()
        {
            var body = TestSource.Method(
                TestSource.Read("Runtime/Network/NetPlayer.cs"), "static bool Same(InputFrame a, InputFrame b)");

            Assert.That(body, Does.Contain("a.RescueHeld == b.RescueHeld"),
                "입력 비교에 없으면 눌러도 그 프레임이 서버로 가지 않는다");
        }

        /// <summary>
        /// <b>인터랙터가 구조 지점만 받는다.</b> 필터가 없으면 후보 탐색이
        /// <c>Interactable</c> 레이어의 아무 대상이나 잡아 <b>고정쇠를 점유해
        /// 버린다</b> — <c>PartClamp</c> 는 <c>HoldSeconds</c> 가 0 이라 그 자리에서
        /// 완료로 처리되고, 실제 고정쇠 조작이 막힌다.
        /// </summary>
        [Test]
        public void TheRescueInteractorTakesOnlyRescuePoints()
        {
            var accepts = typeof(Interactor).GetField("Accepts",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(accepts, Is.Not.Null, "Interactor.Accepts 필터가 없다");

            var find = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Interaction/Interactor.cs"),
                "IInteractable FindBestCandidate()");
            Assert.That(find, Does.Contain("Accepts"),
                "필터를 두고 후보 탐색에서 쓰지 않으면 아무 효과가 없다");

            var rig = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");
            Assert.That(TestSource.Blank(rig), Does.Contain("RescueInteractor.Accepts"),
                "살리기 인터랙터에 필터를 걸지 않으면 고정쇠를 점유한다");
        }

        /// <summary>
        /// <b>홀드를 도는 곳이 있다.</b> 이것이 없어서 기능이 돌지 않았다 —
        /// <c>DownedRescue</c> 는 완성돼 있었고 부르는 곳만 없었다.
        /// </summary>
        [Test]
        public void SomethingActuallyDrivesTheHold()
        {
            var rig = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");
            var tick = TestSource.Method(rig, "void TickRescue()");

            Assert.That(tick, Does.Contain("input.RescueHeld"),
                "입력을 보지 않으면 시작할 수 없다");
            Assert.That(tick, Does.Contain("TryBegin()"),
                "홀드를 시작하지 않으면 진행도도 완료도 없다");
            Assert.That(tick, Does.Contain("Cancel()"),
                "손을 뗄 때 놓지 않으면 홀드가 남는다 — 진행분 이어받기가 그것에 달렸다");

            Assert.That(TestSource.Blank(rig), Does.Contain("TickRescue()"),
                "정의만 있고 부르는 곳이 없으면 이 이슈 전과 똑같다");
        }

        /// <summary>
        /// <b>기절한 사람은 남을 일으킬 수 없다.</b> 그러지 않으면 둘이 서로를 붙들고
        /// 아무도 일어나지 못하는 자리가 생긴다.
        /// </summary>
        [Test]
        public void TheDownedCannotRescue()
        {
            var tick = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"), "void TickRescue()");

            Assert.That(tick, Does.Contain("Downed"),
                "기절 상태를 보지 않으면 기절한 사람도 홀드를 시작한다");
        }

        /// <summary>
        /// <b>과정이 로그에 남는다.</b> 이 기능에 계측이 0건이어서 QA 신고를 로그로
        /// 확인할 수 없었다 — <c>-354</c>·<c>-361</c>·<c>-383</c> 과 같은 계열을
        /// 만들지 않는다.
        /// </summary>
        [Test]
        public void TheAttemptIsLogged()
        {
            var rig = TestSource.Blank(TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"));

            Assert.That(rig, Does.Contain("LogRescue"),
                "시작·완료·중단이 남지 않으면 다음 신고도 로그로 못 가른다");
        }

        /// <summary>
        /// <b>다른 키처럼 설정에서 바꿀 수 있다.</b> 세 자리가 다 있어야 한다 —
        /// 필드 · 값 정규화 · 저장본에서 복사. 정규화나 복사가 빠지면 <b>바꾼 키가
        /// 다음 실행에 사라진다</b>(컴파일은 되므로 조용히 그렇게 된다).
        ///
        /// <para>기획이 다른 키를 정하면 이 배선 덕에 설정 한 줄로 바뀐다 —
        /// 기본값이 <c>G</c> 인 것은 <c>T</c> 가 기획 §21 로 예약돼 있어서다.</para>
        /// </summary>
        [Test]
        public void TheKeyIsReboundLikeEveryOtherKey()
        {
            var settings = TestSource.Blank(TestSource.Read("Runtime/Core/GameSettingsData.cs"));

            Assert.That(settings, Does.Contain("public KeyCode rescueTeammate"),
                "설정 필드가 없으면 바꿀 방법이 없다");
            Assert.That(settings, Does.Contain("NormalizeKey(controls.rescueTeammate"),
                "정규화가 빠지면 잘못된 값이 그대로 들어온다");
            Assert.That(settings, Does.Contain("rescueTeammate = controls != null"),
                "저장본에서 복사하지 않으면 바꾼 키가 다음 실행에 사라진다");

            var apply = TestSource.Method(
                TestSource.Read("Runtime/Core/Controls.cs"),
                "public static void Apply(GameSettingsData.ControlSettings settings)");
            Assert.That(apply, Does.Contain("settings.rescueTeammate"),
                "설정을 적용하지 않으면 기본값에서 벗어날 수 없다");
        }

        /// <summary>
        /// <b>매 고정틱 돌아야 한다.</b> 첫 판은 <c>TickRescue()</c> 를
        /// <c>ConfigureTransportInteractionTargets</c>(운반 장치 목록 갱신) 안에
        /// 넣었다 — <c>RefreshInteractionFocus();</c> 의 첫 번째 자리를 잡은 실수였고
        /// 그러면 <b>게이지가 목록이 바뀔 때만 움직인다</b>(<c>!297</c> AI 리뷰).
        /// <c>Time.fixedDeltaTime</c> 을 적산하므로 호출 빈도가 곧 게이지 속도다.
        /// </summary>
        [Test]
        public void TheHoldIsDrivenFromTheFixedTick()
        {
            var rig = TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs");

            var tick = TestSource.Method(rig, "public void Tick()");
            Assert.That(tick, Does.Contain("TickRescue()"),
                "고정틱 진입점에서 부르지 않으면 게이지 속도가 호출 빈도에 휘둘린다");

            var vehicles = TestSource.Method(
                rig, "public void ConfigureTransportInteractionTargets(TransportVehicle[] vehicles)");
            Assert.That(vehicles, Does.Not.Contain("TickRescue()"),
                "운반 장치 목록 갱신은 매 틱 불리지 않는다 — 첫 판이 거기 있었다");
        }

        /// <summary>
        /// <b>진행도는 <see cref="DownedRescue"/> 의 값이다.</b> 인터랙터의
        /// <c>HoldProgress</c> 는 <b>이번에 누른 시간</b>만 알아서, 그것을 그리면
        /// 이어받는 사람 화면에서 게이지가 0 부터 다시 차 보인다(<c>!297</c> AI 리뷰).
        /// 세는 곳은 한 곳이어야 한다 — <c>-383</c> 에서 겪은 것이다.
        /// </summary>
        [Test]
        public void TheGaugeReadsTheAccumulatedProgress()
        {
            var body = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"),
                "public float RescueProgress01");

            Assert.That(body, Does.Contain(".Progress"),
                "DownedRescue 의 진행도를 읽어야 앞사람이 남긴 분이 보인다");
            Assert.That(body, Does.Not.Contain("HoldProgress"),
                "인터랙터의 홀드 진행도를 그리면 이어받기가 0 부터 다시 차 보인다");

            // 진행도를 **정규화하는** 식이 두 곳에 생기지 않았는지. 누적 자체
            // (Release 가 _liveSeconds 를 _rescuedSeconds 에 접는 것)는 다른 일이므로
            // 세지 않는다 — 처음에 그 둘을 같은 것으로 세어 틀렸다.
            var rescue = TestSource.Blank(
                TestSource.Read("Runtime/Gameplay/Player/DownedRescue.cs"));
            Assert.That(TestSource.Count(rescue, "/ PhysicsScale.RescueSeconds"), Is.EqualTo(1),
                "진행도를 0~1 로 만드는 식이 둘이면 갈라진다");
        }

        /// <summary>
        /// <b>부활은 서버가 한다</b>(`-145`). 클라이언트에서 홀드가 완료되어도
        /// 되살리면 <b>그 화면에서만 일어난다</b> — <c>downed</c> 훅은 값이 바뀔 때만
        /// 불리므로 서버가 여전히 <c>true</c> 인 동안 되돌아오지도 않는다(`-378`).
        /// </summary>
        [Test]
        public void OnlyTheServerActuallyRevives()
        {
            var completed = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/DownedRescue.cs"),
                "public override void OnCompleted(Interactor actor)");

            Assert.That(completed, Does.Contain("ServerAuthority"),
                "권위 가드가 없으면 클라이언트에서 부활이 일어나 서버와 어긋난다");

            var authority = typeof(PlayerRig).GetField("ServerAuthority",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(authority, Is.Not.Null, "PlayerRig.ServerAuthority 가 없다");

            // **기본이 참이다.** 처음에는 거짓으로 두고 서버가 켜게 했는데, 에디터
            // 테스트와 혼자 실행에는 NetPlayer 가 없어 아무도 켜 주지 않아
            // DownedRescueTests 2건이 깨졌다.
            var probe = new GameObject("권위 기본값").AddComponent<PlayerRig>();
            try
            {
                Assert.That(probe.ServerAuthority, Is.True,
                    "기본이 거짓이면 NetPlayer 가 없는 곳(에디터 테스트·혼자 실행)에서 "
                    + "부활이 영원히 일어나지 않는다");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }

            // **내려놓는 곳은 클라이언트 한 곳뿐이다.** RemoteAvatar.Attach 가 이미
            // NetworkDriven 을 켜고 ClampInteractor 를 끄는 자리다 — 같은 이유의 같은
            // 자리라 새 불변식을 만들지 않는다. 내 몸과 남의 몸 **두 경로**가 있다.
            var avatar = TestSource.Blank(TestSource.Read("Runtime/Network/RemoteAvatar.cs"));
            Assert.That(TestSource.Count(avatar, "ServerAuthority = false"), Is.EqualTo(2),
                "클라이언트의 두 경로(내 몸·남의 몸)에서 모두 내려놓아야 한다 — "
                + "한쪽이 빠지면 그 몸에서 부활이 일어나 서버와 어긋난다");

            // 쓰는 곳이 둘이면 갈라진다 — 서버 쪽에서 다시 켜지 않는다.
            var net = TestSource.Blank(TestSource.Read("Runtime/Network/NetPlayer.cs"));
            Assert.That(net, Does.Not.Contain("ServerAuthority"),
                "서버가 다시 켜면 쓰는 곳이 둘이 된다 — 기본값이 이미 참이다");
        }

        /// <summary>
        /// <b>클라이언트의 예측이 완료로 넘어가지 않는다.</b> 넘어가면 아무 일도
        /// 일어나지 않는 완료를 매 틱 되풀이한다 — 위 가드가 되살리지 않으므로.
        /// </summary>
        [Test]
        public void ThePredictionStopsShortOfCompleting()
        {
            var tick = TestSource.Method(
                TestSource.Read("Runtime/Gameplay/Player/PlayerRig.cs"), "void TickRescue()");

            Assert.That(tick, Does.Contain("ServerAuthority"),
                "클라이언트와 서버를 가르지 않으면 예측이 완료까지 간다");
            Assert.That(tick, Does.Contain("PredictionCap"),
                "멈추는 자리가 없으면 인터랙터가 그 프레임에 완료로 넘어간다");

            Assert.That(PlayerRig.RescuePredictionCap, Is.LessThan(1f),
                "1 이면 완료를 막지 못한다");
            Assert.That(PlayerRig.RescuePredictionCap, Is.GreaterThan(0.5f),
                "너무 낮으면 게이지가 어중간한 데서 멈춰 사람이 고장으로 본다");
        }

        /// <summary>홀드 시간이 기획 값 그대로다 — 게이지가 채워지는 속도가 이 값이다.</summary>
        [Test]
        public void TheHoldTakesThePlannedTime()
        {
            Assert.That(PhysicsScale.RescueSeconds, Is.GreaterThan(0f),
                "0 이면 누른 순간 완료돼 게이지가 보이지 않는다");
            Assert.That(PhysicsScale.RescueHealthRatio, Is.InRange(0f, 1f));
        }
    }
}
