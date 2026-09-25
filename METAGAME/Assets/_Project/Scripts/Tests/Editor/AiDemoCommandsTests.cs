using System;
using System.Collections.Generic;
using System.Linq;
using Metagame.Network;
using NUnit.Framework;

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
                    Is.EqualTo(AiAmbient.Mode.Follow).Or.EqualTo(AiAmbient.Mode.Wait), "손이 찼는데 춤·감정 표현을 고른다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, true, AiAmbient.Mode.Dance), Is.Not.EqualTo(AiAmbient.Mode.Dance),
                    "춤을 연달아 춘다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, true, AiAmbient.Mode.Emote), Is.Not.EqualTo(AiAmbient.Mode.Emote),
                    "감정 표현을 연달아 한다");
                Assert.That(AiAmbient.Pick(roll, true, true, true, true, AiAmbient.Mode.Wait), Is.EqualTo(AiAmbient.Mode.Follow),
                    "사람과 멀어졌는데 따라가지 않는다");
                Assert.That(AiAmbient.Pick(roll, true, true, false, false, AiAmbient.Mode.Wait), Is.Not.EqualTo(AiAmbient.Mode.Follow),
                    "사람이 없는데 따라가기를 고른다");
            }
            Assert.That(seen, Is.EquivalentTo(new[] { AiAmbient.Mode.Follow, AiAmbient.Mode.Wait, AiAmbient.Mode.Emote, AiAmbient.Mode.Dance }),
                "네 가지 기본 행동 중 나오지 않는 것이 있다");

            // 간격은 범위 안에서 무작위다 — 같은 간격으로 바뀌면 기계처럼 보인다
            foreach (var mode in new[] { AiAmbient.Mode.Follow, AiAmbient.Mode.Wait, AiAmbient.Mode.Dance })
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
            string tick = TestSource.Method(source, "public InputFrame Tick(PlayerRig self, bool allowed)");
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
