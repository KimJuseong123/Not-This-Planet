using System;
using Metagame.Network;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// AI 작업의 끝이 <b>값으로</b> 남는가 (S15P21B202-703).
    ///
    /// <para>전에는 실패 · 취소 · 시작도 안 함이 모두 <c>!Active && !Succeeded</c> 로 같았고, 취소되면
    /// 화면이 따라다니기의 "대기" 로 덮여 이유가 사라졌다. 끝난 작업은 로그에도 남지 않았다.</para>
    /// </summary>
    public sealed class AiTaskOutcomeTests
    {
        [Test]
        public void EveryOutcomeHasItsOwnLabel()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (AiTaskOutcome outcome in Enum.GetValues(typeof(AiTaskOutcome)))
                Assert.That(seen.Add(AiTaskOutcomes.Label(outcome)), Is.True, outcome + " 의 이름이 다른 것과 같다");
            Assert.That(AiTaskOutcomes.Ended(AiTaskOutcome.Succeeded) && AiTaskOutcomes.Ended(AiTaskOutcome.Failed)
                        && AiTaskOutcomes.Ended(AiTaskOutcome.Cancelled), Is.True);
            Assert.That(AiTaskOutcomes.Ended(AiTaskOutcome.Running) || AiTaskOutcomes.Ended(AiTaskOutcome.None), Is.False,
                "돌고 있거나 받은 적 없는 것을 끝났다고 하면 화면이 틀린 결과를 붙인다");
        }

        [Test]
        public void TheLogLineSaysWhatHowAndWhy()
        {
            string line = AiTaskOutcomes.LogLine("carry_part", AiTaskOutcome.Failed, "파츠를 놓침", 12.34f);
            Assert.That(line, Does.StartWith("[AI] 작업 결과 "), "판단 로그와 같은 머리여야 이어서 읽힌다");
            Assert.That(line, Does.Contain("carry_part").And.Contain("실패").And.Contain("파츠를 놓침").And.Contain("12.3초"));
            Assert.That(AiTaskOutcomes.LogLine(null, AiTaskOutcome.Cancelled, null, 0), Does.Contain("취소"),
                "이유가 없어도 끝은 남긴다");
        }

        [Test]
        public void AFreshTaskHasNoOutcomeAndARejectedStartLeavesNone()
        {
            var task = new AiWorkTask();
            Assert.That(task.Outcome, Is.EqualTo(AiTaskOutcome.None));
            task.Cancel();
            Assert.That(task.Outcome, Is.EqualTo(AiTaskOutcome.None), "돌지 않던 작업을 멈춘 것은 취소가 아니다 — 쉬는 동안 매 틱 불린다");
            Assert.That(task.Begin("goto", null, null), Is.False);
            Assert.That(task.Outcome, Is.EqualTo(AiTaskOutcome.None), "받지 못한 명령은 결과를 바꾸지 않는다");
        }

        /// <summary>결과를 적는 순서와 자리를 고정한다. 값으로 부르려면 플레이 모드의 몸이 필요하다.</summary>
        [Test]
        public void FinishWritesTheResultBeforeCancelAndCancelRecordsWhy()
        {
            string work = TestSource.Read("Runtime/Network/AiWorkTask.cs");
            string finish = TestSource.Method(work, "void Finish(bool success, string message)");
            Assert.That(finish.IndexOf("Settle(", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(finish.IndexOf("Settle(", StringComparison.Ordinal), Is.LessThan(finish.IndexOf("Cancel();", StringComparison.Ordinal)),
                "Cancel 이 먼저 돌면 모든 실패가 취소로 남는다");
            string cancel = TestSource.Method(work, "public void Cancel(string reason)");
            Assert.That(cancel, Does.Contain("Settle(AiTaskOutcome.Cancelled, reason);"));
            string settle = TestSource.Method(work, "void Settle(AiTaskOutcome outcome, string reason)");
            Assert.That(settle, Does.Contain("if (Outcome != AiTaskOutcome.Running) return;"), "한 작업의 끝은 한 번만 적는다");
            Assert.That(settle, Does.Contain("Debug.Log(AiTaskOutcomes.LogLine("), "끝난 작업이 Player.log 에 남아야 QA 가 가를 수 있다");
            string begin = TestSource.Method(work, "public bool Begin(string command, PlayerRig actor, PlayerRig requester");
            Assert.That(begin, Does.Contain("Outcome = AiTaskOutcome.Running;"));
        }

        [Test]
        public void TheControllerSaysWhyItStoppedAndWhyItCouldNotStart()
        {
            string controller = TestSource.Read("Runtime/Network/AiCompanionController.cs");
            string tick = TestSource.Method(controller, "InputFrame TickCore(PlayerRig self, bool allowed)");
            Assert.That(tick, Does.Contain("HaltReason(self, allowed, rescuing);"));
            Assert.That(tick, Does.Contain("Stop(halt);"), "멈춘 이유가 작업의 취소 이유로 가야 한다");
            string apply = TestSource.Method(controller,
                "public bool Apply(string action, PlayerRig speaker, PlayerRig self, out string refusal,");
            Assert.That(apply, Does.Contain("refusal = work.Reason ??"), "못 시작한 그 이유를 올린다");
        }
    }
}
