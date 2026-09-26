using System;
using System.Collections.Generic;
using System.Linq;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 라이브 시연용 AI 동료 (S15P21B202-746) — 명령은 넷뿐이고, 명령이 없으면 사람처럼 움직인다.
    /// 여기서는 값으로 볼 수 있는 것을 고정한다. 실제 스테이지의 행동은 <c>AiDemoScenarioTests</c>.
    /// </summary>
    public sealed class AiDemoCommandsTests
    {
        [Test]
        public void OnlyFourCommandsSurviveAndSynonymsMoveToThem()
        {
            foreach (var action in AiDemoCommands.Allowed)
                Assert.That(AiDemoCommands.Map(action), Is.EqualTo(action), action + " 가 제 자신으로 가지 않는다");
            Assert.That(AiDemoCommands.Allowed, Is.EquivalentTo(new[] { "defend_meteors", "revive", "pull_pin", "come" }));

            Assert.That(AiDemoCommands.Map("pickup_bat"), Is.EqualTo("defend_meteors"), "\"방망이 집어\" 는 쳐내기의 첫 단계다");
            Assert.That(AiDemoCommands.Map("bring_bat"), Is.EqualTo("defend_meteors"));
            Assert.That(AiDemoCommands.Map("hit_target"), Is.EqualTo("defend_meteors"));
            Assert.That(AiDemoCommands.Map("pull_all_pins"), Is.EqualTo("pull_pin"));
            Assert.That(AiDemoCommands.Map("follow"), Is.EqualTo("come"));
            Assert.That(AiDemoCommands.Map("gather"), Is.EqualTo("come"));
            Assert.That(AiDemoCommands.Map("goto", "speaker"), Is.EqualTo("come"));
            Assert.That(AiDemoCommands.Map("goto", "default"), Is.EqualTo("come"));

            Assert.That(AiDemoCommands.Map("goto", "assembly"), Is.Null, "조립대로 가라는 말을 '나에게 와' 로 바꾸면 시킨 것과 다른 일을 한다");
            Assert.That(AiDemoCommands.Map("goto", "aim"), Is.Null);
            foreach (var other in new[] { "weld", "cut_weld", "board_tractor", "grab_part", "carry_part", "dance", "greet", "wait", "cancel", "pickup_welder", null, "" })
                Assert.That(AiDemoCommands.Map(other), Is.Null, other + " 는 시연에서 받지 않는다");
        }

        [Test]
        public void TheFourCommandsAreRealWorkTasks()
        {
            foreach (var action in AiDemoCommands.Allowed)
                Assert.That(AiWorkTask.Supports(action), Is.True, action + " 를 작업으로 받지 않는다 — 결과가 로그에 남지 않는다");
        }

        [Test]
        public void EveryOrderPassesTheGate()
        {
            string orders = TestSource.Read("Runtime/Network/NetPlayer.AiCommands.cs");
            string order = TestSource.Method(orders, "public bool TryApplyAiOrder(string action, PlayerRig speaker, out string refusal,");
            int gate = order.IndexOf("AiDemoCommands.Map(action, destination);", StringComparison.Ordinal);
            int apply = order.IndexOf("aiFollow.Apply(action, speaker, body, out refusal, approach, destination);", StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThanOrEqualTo(0), "명령을 넷으로 옮기지 않는다");
            Assert.That(apply, Is.GreaterThan(gate), "옮기기 전에 명령을 건넨다");

            string plan = TestSource.Method(orders, "public bool TryApplyAiPlan(AiCommandStep[] steps, PlayerRig speaker, out string refusal)");
            Assert.That(plan, Does.Contain("steps = new[] { first ?? steps[0] };"), "여러 단계 계획이 그대로 줄을 선다");
        }

        [Test]
        public void IdleCompanionsPickSomethingHumanAndNeverRepeatAShow()
        {
            var seen = new HashSet<AiAmbient.Mode>();
            for (int i = 0; i < 1000; i++)
            {
                double roll = i / 1000.0;
                var mode = AiAmbient.Pick(roll, true, true, false, true, AiAmbient.Mode.Wait);
                seen.Add(mode);
                Assert.That(AiAmbient.Pick(roll, false, false, false, true, AiAmbient.Mode.Wait),
                    Is.EqualTo(AiAmbient.Mode.Follow).Or.EqualTo(AiAmbient.Mode.Wait).Or.EqualTo(AiAmbient.Mode.Wander),
                    "손이 찼는데 춤·감정 표현을 고른다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, true, AiAmbient.Mode.Dance), Is.Not.EqualTo(AiAmbient.Mode.Dance),
                    "춤을 연달아 춘다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, true, AiAmbient.Mode.Emote), Is.Not.EqualTo(AiAmbient.Mode.Emote),
                    "감정 표현을 연달아 한다");
                Assert.That(AiAmbient.Pick(roll, true, true, true, true, AiAmbient.Mode.Wait),
                    Is.EqualTo(AiAmbient.Mode.Follow).Or.EqualTo(AiAmbient.Mode.Wander), "사람과 멀어졌는데 제자리에 있거나 춤춘다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, false, AiAmbient.Mode.Wait), Is.Not.EqualTo(AiAmbient.Mode.Follow),
                    "사람이 없는데 따라가기를 고른다");
            }
            Assert.That(seen, Is.EquivalentTo(new[] { AiAmbient.Mode.Follow, AiAmbient.Mode.Wait, AiAmbient.Mode.Emote,
                                                      AiAmbient.Mode.Dance, AiAmbient.Mode.Wander }),
                "기본 행동 중 나오지 않는 것이 있다");
            // 2026-09-25 QA: 셋이 똑같이 따라오다 멈추는 것이 어색했다 — 따라가기가 가장 흔하면 안 된다
            Assert.That(AiAmbient.WeightFollow, Is.LessThan(AiAmbient.WeightWander), "따라가기가 돌아다니기보다 흔하다");
            int follows = 0, wanders = 0;
            for (int i = 0; i < 1000; i++)
            {
                var m = AiAmbient.Pick(i / 1000.0, true, true, true, true, AiAmbient.Mode.Wait);
                if (m == AiAmbient.Mode.Follow) follows++; else if (m == AiAmbient.Mode.Wander) wanders++;
            }
            Assert.That(follows, Is.InRange(300, 700), "멀어진 셋이 모두 따라붙는다");
            Assert.That(wanders, Is.InRange(300, 700));

            // 간격은 범위 안에서 무작위다 — 같은 간격으로 바뀌면 기계처럼 보인다
            foreach (var mode in new[] { AiAmbient.Mode.Follow, AiAmbient.Mode.Wait, AiAmbient.Mode.Dance, AiAmbient.Mode.Wander })
            {
                float low = AiAmbient.Duration(mode, 0), high = AiAmbient.Duration(mode, .999);
                Assert.That(high - low, Is.GreaterThan(2f), mode + " 의 간격이 거의 같다");
                Assert.That(low, Is.GreaterThanOrEqualTo(2f), mode + " 가 너무 빨리 바뀐다");
            }
        }

        [Test]
        public void TheControllerRunsTheIdleBehaviourOnlyWithoutACommand()
        {
            string source = TestSource.Read("Runtime/Network/AiCompanionController.cs");
            string tick = TestSource.Method(source, "InputFrame TickTimed(PlayerRig self, bool allowed)");
            int idle = tick.IndexOf("if (!Busy && AiDemoCommands.Enabled) return ambient.Tick(self, allowed, AmbientCanDance, Time.time);", StringComparison.Ordinal);
            int command = tick.IndexOf("return TickCore(self, allowed);", StringComparison.Ordinal);
            Assert.That(idle, Is.GreaterThanOrEqualTo(0), "명령이 없을 때 기본 행동을 하지 않는다");
            Assert.That(command, Is.GreaterThan(idle));
            Assert.That(TestSource.Method(source, "public void Stop(string reason)"), Does.Contain("ambient.Stop();"),
                "명령이 와도 기본 행동이 이어진다 — 따라가던 발걸음이 명령과 섞인다");
            Assert.That(TestSource.Read("Runtime/Network/AiAmbient.cs"), Does.Contain("if (player == null || player.IsAi) continue;"),
                "다른 AI 동료를 따라간다");
        }

        [Test]
        public void AMeteorIsChasedOnlyWhenItCanBeReachedInTime()
        {
            const float window = 1f, assist = 2.5f, run = 4.6f;
            Assert.That(AiWorkTask.CanReach(1f, .3f, window, assist, run), Is.True, "이미 칠 자리 안이면 시간이 없어도 된다");
            Assert.That(AiWorkTask.CanReach(1f, 0f, window, assist, run), Is.False, "이미 떨어졌다");
            // 3초 뒤에 떨어지고 10m 떨어짐: 칠 때(0.6초 전)까지 2.15초 · 달려서 약 7.9m — 8.5m 를 가야 하므로 못 간다
            Assert.That(AiWorkTask.CanReach(10f, 3f, window, assist, run), Is.False);
            Assert.That(AiWorkTask.CanReach(7f, 3f, window, assist, run), Is.True);
            Assert.That(AiWorkTask.CanReach(20f, 1.5f, window, assist, run), Is.False, "제시간에 닿을 수 없는 운석으로 간다");
            Assert.That(AiWorkTask.CanReach(3f, window * .5f, window, assist, run), Is.False, "칠 때가 이미 지났는데 뛰어간다");
            Assert.That(AiWorkTask.StrikeStandOffset, Is.LessThan(assist - .5f), "착지점에서 너무 멀리 서면 판정 가장자리에 걸린다");
        }

        [Test]
        public void TheBatSwingsAtThePerfectMomentAndTheChaseNeverEndsTheJob()
        {
            string tools = TestSource.Read("Runtime/Network/AiWorkTask.Tools.cs");
            string defend = TestSource.Method(tools, "InputFrame TickDefend(MeteorBat bat)");
            Assert.That(defend, Does.Contain("MeteorBat.FindTarget(self.Position);"), "휘두를 운석을 방망이와 다른 규칙으로 고른다");
            Assert.That(defend, Does.Contain("left <= strike.BatSwingWindowSeconds * MeteorBat.PerfectAt + StrikeLead"),
                "판정이 가장 좋은 때를 기다리지 않는다");
            Assert.That(defend, Does.Not.Contain("Finish("), "운석 하나를 놓쳤다고 막기 전체를 끝낸다");
            Assert.That(TestSource.Method(tools, "bool Chase(Vector3 point, float stop, ref InputFrame input, bool run)"), Does.Not.Contain("Finish("));
        }

        /// <summary>
        /// 워커가 준비에 실패했다고 말하면 게임이 곧바로 실패로 보인다 (2026-09-25 QA). 전에는 "대답이 왔으니
        /// 데우는 중" 으로 보고 15분을 기다려, 설치 99% 에서 멈춘 것처럼 보였다.
        /// </summary>
        [Test]
        public void AWorkerThatSaysItFailedIsAFailureNotAWarmup()
        {
            Assert.That(AiLocalRuntime.ReportedFailure("{\"state\":\"failed\",\"error\":\"runtime_preparation_failed\"}"),
                Is.EqualTo("runtime_preparation_failed"));
            Assert.That(AiLocalRuntime.ReportedFailure("{\"state\":\"failed\"}"), Is.EqualTo("failed"));
            Assert.That(AiLocalRuntime.ReportedFailure("{\"state\":\"loading\"}"), Is.Null, "데우는 중을 실패로 본다");
            Assert.That(AiLocalRuntime.ReportedFailure("{\"state\":\"ready\"}"), Is.Null);
            Assert.That(AiLocalRuntime.ReportedFailure("<html>"), Is.Null);
            string watch = TestSource.Method(TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "IEnumerator Watch(int epoch)");
            int reported = watch.IndexOf("ReportedFailure(request.downloadHandler.text)", System.StringComparison.Ordinal);
            int warming = watch.IndexOf("Message = WarmupMessage(answered, waited, WarmupExpected);", System.StringComparison.Ordinal);
            Assert.That(reported, Is.GreaterThanOrEqualTo(0), "워커의 실패를 읽지 않는다");
            Assert.That(reported, Is.LessThan(warming), "실패를 읽기 전에 '데우는 중' 으로 넘어간다");
        }

        /// <summary>
        /// 고정못과 운석은 한 번 하고 끝나는 명령이 아니다 (2026-09-25). 못은 없을 때까지, 운석은 계속 — 동료가
        /// 쓰러졌다 일어나도, 방망이가 없어도 이어 간다.
        /// </summary>
        [Test]
        public void PinsAndMeteorsKeepGoingUntilThereIsNothingLeft()
        {
            Assert.That(AiWorkTask.Standing("pull_pin") && AiWorkTask.Standing("defend_meteors"), Is.True);
            Assert.That(AiWorkTask.Standing("revive") || AiWorkTask.Standing("come"), Is.False, "살리기·오기는 한 번이면 된다");

            string work = TestSource.Read("Runtime/Network/AiWorkTask.cs");
            string pin = TestSource.Method(work, "InputFrame TickPin()");
            int pulled = pin.IndexOf("pinsPulled++;", System.StringComparison.Ordinal);
            int next = pin.IndexOf("pin.Begin(self, speaker, null, attemptedPins);", pulled, System.StringComparison.Ordinal);
            Assert.That(pulled, Is.GreaterThanOrEqualTo(0), "하나 뽑고 끝낸다");
            Assert.That(next, Is.GreaterThan(pulled), "뽑은 뒤 다음 못으로 가지 않는다");
            Assert.That(pin, Does.Contain("Exhausted = true;"), "뽑을 못이 없을 때를 '할 일을 다 했다' 로 가르지 않는다");

            string tools = TestSource.Read("Runtime/Network/AiWorkTask.Tools.cs");
            Assert.That(TestSource.Method(tools, "bool BeginTool()"), Does.Contain("{ Phase(7); Status = "),
                "방망이가 없으면 운석 막기가 곧바로 실패한다");
            Assert.That(TestSource.Method(tools, "bool FindTool()"), Does.Contain("self.FootPosition"), "말한 사람 기준으로 방망이를 찾는다");

            string controller = TestSource.Read("Runtime/Network/AiCompanionController.cs");
            string tick = TestSource.Method(controller, "InputFrame TickTimed(PlayerRig self, bool allowed)");
            int resume = tick.IndexOf("if (Apply(keep, who, self, out var refused))", System.StringComparison.Ordinal);
            int idle = tick.IndexOf("return ambient.Tick(self, allowed, AmbientCanDance, Time.time);", System.StringComparison.Ordinal);
            Assert.That(resume, Is.GreaterThanOrEqualTo(0), "끊긴 명령을 다시 걸지 않는다");
            Assert.That(resume, Is.LessThan(idle), "끊긴 명령보다 기본 행동이 먼저다");
            Assert.That(tick, Does.Contain("if (work.Exhausted || standingSpeaker == null) standing = null;"),
                "뽑을 못이 없는데 계속 다시 건다");
        }

        /// <summary>받아쓰기·LLM 이 게임과 CPU 를 다투지 않는다 (2026-09-25 QA: 말하는 동안 fps 3~8).</summary>
        [Test]
        public void TheWorkerRunsBelowTheGame()
        {
            string start = TestSource.Method(TestSource.Read("Runtime/Network/AiLocalRuntime.cs"), "void StartRuntime()");
            int started = start.IndexOf("worker.Start();", System.StringComparison.Ordinal);
            int lowered = start.IndexOf("worker.PriorityClass = ProcessPriorityClass.BelowNormal;", System.StringComparison.Ordinal);
            Assert.That(lowered, Is.GreaterThan(started), "워커의 우선순위를 낮추지 않는다");
        }

        /// <summary>곧 떨어질 운석의 충격 반경 안이면 피하고, 방망이로 칠 참이면 피하지 않는다 (2026-09-26 QA).</summary>
        [Test]
        public void CompanionsTryToStepOutOfAFallingMeteor()
        {
            var at = Vector3.zero;
            Assert.That(AiMeteorDodge.Threatens(at, new Vector3(1, 0, 0), .8f, 2f, out var away), Is.True, "반경 안인데 피하지 않는다");
            Assert.That(away.x, Is.LessThan(0f), "착지점 쪽으로 피한다");
            Assert.That(AiMeteorDodge.Threatens(at, new Vector3(5, 0, 0), .8f, 2f, out _), Is.False, "멀리 떨어지는 운석까지 피한다 — 헛걸음");
            Assert.That(AiMeteorDodge.Threatens(at, new Vector3(1, 0, 0), 3f, 2f, out _), Is.False, "아직 먼 운석에 미리 달아난다");
            Assert.That(AiMeteorDodge.Threatens(at, at, .5f, 2f, out var any), Is.True);
            Assert.That(any.sqrMagnitude, Is.GreaterThan(.5f), "바로 위에 떨어지면 어느 쪽으로든 피한다");
            string controller = TestSource.Read("Runtime/Network/AiCompanionController.cs");
            string tick = TestSource.Method(controller, "InputFrame TickTimed(PlayerRig self, bool allowed)");
            Assert.That(tick, Does.Contain("dodge.Tick(self, allowed, work.Active && work.Striking, out var escape)"),
                "피하기를 부르지 않거나, 방망이로 칠 참인데도 피한다");
        }

        /// <summary>갈 수 있는 곳 앞이 운석·파츠로 막히면 돌아 나가고, 목적지가 막힌 것을 영역 이탈로 보지 않는다 (2026-09-26 QA).</summary>
        [Test]
        public void BlockedStepsGoAroundAndAnUnreachableGoalIsNotAnEscape()
        {
            foreach (var (file, method) in new[] {
                ("Runtime/Network/AiWorkTask.cs", "bool Navigate(Vector3 point, float stop, ref InputFrame input)"),
                ("Runtime/Network/AiWorkTask.Tools.cs", "bool Chase(Vector3 point, float stop, ref InputFrame input, bool run)"),
                ("Runtime/Network/AiAmbient.cs", "InputFrame TickWander(PlayerRig self, float now)"),
                ("Runtime/Network/AiFollowController.cs", "public InputFrame Tick(PlayerRig self, bool allowed)") })
                Assert.That(TestSource.Method(TestSource.Read(file), method), Does.Contain("AiFollowController.SteerAround(").Or.Contain("SteerAround(self"),
                    file + " 가 막히면 선 채로 멈춘다");
            Assert.That(TestSource.Read("Runtime/Network/AiPinTask.cs"), Does.Contain("AiFollowController.SteerAround(owner"));
            string recovery = TestSource.Read("Runtime/Network/AiMovementRecovery.cs");
            Assert.That(recovery, Does.Contain("else if (OnNavigation(self)) unreachableSince = -1;"),
                "목적지까지만 길이 없어도 '이동 영역 이탈' 로 작업을 멈춘다");
            Assert.That(AiWorkTask.ReviveSearchRange, Is.GreaterThanOrEqualTo(100f), "멀리 쓰러진 사람을 '없음' 으로 본다");
        }

        /// <summary>동료 머리 위에 지금 하는 일 · 결과를 띄운다 (2026-09-26 QA: 작은 글씨는 시연에서 안 보였다).</summary>
        [Test]
        public void EveryCommandHasABadgeAndResultsReadAtAGlance()
        {
            foreach (var action in AiDemoCommands.Allowed)
            {
                byte badge = AiBadges.Of(action, false);
                Assert.That(badge, Is.Not.EqualTo(AiBadges.None), action + " 에 표시가 없다");
                Assert.That(AiBadges.Label(badge), Is.Not.Empty);
            }
            Assert.That(AiBadges.Of("pull_pin", true), Is.EqualTo(AiBadges.Dodge), "피하는 중이 가장 급하다");
            Assert.That(AiBadges.Of(null, false), Is.EqualTo(AiBadges.None), "쉬는 중에 표시가 뜬다");
            Assert.That(AiBadges.Finished("revive", false, "기절한 동료가 없음"), Does.Contain("기절한 동료가 없음"), "실패의 까닭을 감춘다");
            Assert.That(AiBadges.Tally("pull_pin", 3), Does.Contain("3"));
            Assert.That(TeamNameplateHudFont(), Is.GreaterThanOrEqualTo(24), "머리 위 글자가 작다");
            // 2026-09-26: 하는 중인 일은 머리 위에 띄우지 않는다 — AI 동료가 사람과 구분되지 않게
            string overhead = TestSource.Method(TestSource.Read("Runtime/UI/TeamNameplateHud.cs"), "void DrawBadge(Vector3 screen, float distance, NetPlayer player)");
            Assert.That(overhead, Does.Contain("if (!note) return;"), "머리 위에 '하는 중' 을 띄운다");
            Assert.That(overhead, Does.Not.Contain("AiBadges.Label("), "머리 위에 '하는 중' 을 띄운다");
            // 2026-09-26 QA 2: 아래 글자가 과하게 컸다 — 이제 가운데 아래 마이크 아이콘 하나와 V, 결과는 위쪽 배너 한 줄
            Assert.That(Metagame.UI.AiVoiceControls.MicSize, Is.InRange(40, 90), "마이크 아이콘이 너무 작거나 화면을 가린다");
            Assert.That(Metagame.UI.AiVoiceControls.BannerFont, Is.InRange(20, 34), "결과 배너 글자가 너무 작거나 과하다");
            string gui = TestSource.Method(TestSource.Read("Runtime/UI/AiVoiceControls.cs"), "void OnGUI()");
            Assert.That(gui, Does.Not.Contain("DrawSeats"), "아래에 자리 칸 넷을 또 그린다 — 왼쪽 위 동료 카드와 겹친다");
            Assert.That(TestSource.Read("Runtime/UI/GameHud.TeamStatus.cs"), Does.Contain("AiVoiceControls.SelectedSlot"),
                "고른 대상이 동료 카드에 보이지 않는다");
        }

        static int TeamNameplateHudFont() => Metagame.UI.TeamNameplateHud.BadgeFont;

        /// <summary>한 프레임이 늦어도 물리를 몰아 돌려 늪에 빠지지 않고, 프레임이 어디에 쓰이는지 로그에 남는다.</summary>
        [Test]
        public void SlowFramesDoNotSnowballAndTheBudgetIsLogged()
        {
            Assert.That(Metagame.Core.Tuning.MaxCatchUp, Is.InRange(Metagame.Core.Tuning.FixedStep * 3f, .1f),
                "한 프레임에 따라잡는 물리가 너무 많거나(늪) 너무 적다(물리가 느려진다)");
            Assert.That(TestSource.Read("Runtime/Bootstrap/SpaceBootstrap.cs"), Does.Contain("Time.maximumDeltaTime = Tuning.MaxCatchUp;"));
        }

        [Test]
        public void ThePinIsTheNearestToTheCompanionNotToTheSpeaker()
        {
            string select = TestSource.Method(TestSource.Read("Runtime/Network/AiPinTask.cs"), "bool Select(AiNavigationWorld world)");
            Assert.That(select, Does.Contain("var from = owner.FootPosition;"));
            Assert.That(select, Does.Not.Contain("speaker.FootPosition"), "말한 사람 기준으로 고른다 — 제 옆의 못을 두고 멀리 간다");
            string pin = TestSource.Method(TestSource.Read("Runtime/Network/AiWorkTask.cs"), "InputFrame TickPin()");
            Assert.That(pin, Does.Contain("input.ToolUtility = true;"), "들고 있는 도구 때문에 못 뽑기가 조용히 끝난다");
            Assert.That(pin, Does.Contain("++pinCount > PinRetries"), "한 번 어긋나면 명령 전체를 버린다");
        }
    }
}
