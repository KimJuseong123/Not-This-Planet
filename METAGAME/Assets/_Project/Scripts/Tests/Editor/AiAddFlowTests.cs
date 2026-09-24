using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Metagame.Network;
using Metagame.UI;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// "AI 동료 추가" 의 흐름을 값으로 고정한다 (S15P21B202-713) — ③ 확인 → 설치 묻기, ④ 받기·설치·준비를 한 흐름으로,
    /// ⑤ 완료를 누르면 넣기.
    ///
    /// <para>흐름(<see cref="AiAddFlow"/>)은 시각과 본 것만 받는다. 그래서 실제 받기·워커 없이 몇 분짜리 설치를
    /// 몇 줄로 돌린다. 창(<see cref="AiInstallPopup"/>)이 흐름을 제대로 부르는지는 아래 배선 검사가 본다.</para>
    /// </summary>
    public sealed class AiAddFlowTests
    {
        const long MB = ByteRate.Megabyte;

        static AiAddInputs See(AiRuntimeState runtime = AiRuntimeState.Missing, bool busy = false,
                               AiPrepSnapshot installer = default, int requested = 0,
                               double warmed = 0, double expected = 227, bool host = true)
            => new AiAddInputs
            {
                HostOk = host, InstallerBusy = busy, Installer = installer, Runtime = runtime,
                WarmupElapsed = warmed, WarmupExpected = expected, Requested = requested,
            };

        /// <summary>설치기의 상태. 속도는 모두 알려져 있다 — 남은 시간을 셀 수 있다.</summary>
        static AiPrepSnapshot Installer(AiPrepPhase phase, long fetched = 0, AiPrepFailure failure = AiPrepFailure.None,
                                        double elapsed = 60, float progress = .3f)
        {
            bool unpacked = phase >= AiPrepPhase.Apply;
            return new AiPrepSnapshot
            {
                Phase = phase, Failure = failure,
                FetchTotal = 2000 * MB, FetchDone = unpacked ? 2000 * MB : fetched, FetchRate = 4.0 * MB,
                HashTotal = 2100 * MB, HashDone = unpacked ? 2100 * MB : 0, HashRate = 400.0 * MB,
                ExtractTotal = 2400 * MB, ExtractDone = unpacked ? 2400 * MB : 0, ExtractRate = 150.0 * MB,
                WarmupExpected = 227, Elapsed = elapsed, Progress = progress,
            };
        }

        /// <summary>설치를 마친 설치기 — 워커를 기다리는 단계(Start)에 멈춰 있다.</summary>
        static AiPrepSnapshot Finished(double elapsed = 600, float progress = .72f)
            => Installer(AiPrepPhase.Start, elapsed: elapsed, progress: progress);

        /// <summary>추가를 눌러 팩이 없다는 답을 받고, 확인을 눌러 받기가 시작된 흐름.</summary>
        static AiAddFlow Installing(double now = 0, bool adds = true)
        {
            var flow = new AiAddFlow();
            Assert.That(flow.Begin(now, adds), Is.True);
            Assert.That(flow.Checked(AiAddCheck.AskInstall, now), Is.EqualTo(AiAddCommand.None));
            Assert.That(flow.Press(AiAddButton.Confirm, now), Is.EqualTo(AiAddCommand.Install), "확인이 받기를 시키지 않는다");
            Assert.That(flow.Started(AiInstallStart.Started, now), Is.EqualTo(AiAddCommand.None));
            return flow;
        }

        /// <summary>설치가 끝나고 워커가 Ready 가 된 흐름 — "완료되었습니다".</summary>
        static AiAddFlow Ready(bool adds = true)
        {
            var flow = Installing(0, adds);
            flow.Tick(100, See(installer: Finished()));
            flow.Tick(101, See(AiRuntimeState.Loading, installer: Finished(), warmed: 1));
            flow.Tick(300, See(AiRuntimeState.Ready, installer: Finished()));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Done));
            return flow;
        }

        // ---------- ③ 확인 ----------

        /// <summary>이 PC 에 쓸 팩이 있으면 <b>창 없이</b> 바로 넣는다.</summary>
        [Test]
        public void ACurrentPackIsAddedAtOnceWithoutAWindow()
        {
            foreach (AiRuntimeState state in new[] { AiRuntimeState.Missing, AiRuntimeState.Loading, AiRuntimeState.Ready })
                Assert.That(AiAddFlow.Decide(true, true, true, state), Is.EqualTo(AiAddCheck.AddNow), state.ToString());

            var flow = new AiAddFlow();
            flow.Begin(10, adds: true);
            Assert.That(flow.Checked(AiAddCheck.AddNow, 10.05), Is.EqualTo(AiAddCommand.Add));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Idle));
            Assert.That(flow.Visible(10.5), Is.False, "팩이 있는데 창이 떴다");
        }

        [TestCase(true, true, true, AiRuntimeState.Ready, AiAddCheck.AddNow)]
        [TestCase(true, true, false, AiRuntimeState.Ready, AiAddCheck.AskInstall)]       // 깔린 것이 옛 팩이다
        [TestCase(false, true, false, AiRuntimeState.Missing, AiAddCheck.AskInstall)]
        [TestCase(true, false, false, AiRuntimeState.Ready, AiAddCheck.AddNow)]          // 주소 없는 빌드 + 팩 = 최신
        [TestCase(false, false, false, AiRuntimeState.Missing, AiAddCheck.CannotInstall)] // 주소도 팩도 없다
        [TestCase(true, true, true, AiRuntimeState.Failed, AiAddCheck.AddNowAndRetry)]
        [TestCase(true, false, false, AiRuntimeState.Failed, AiAddCheck.AddNowAndRetry)]
        public void TheCheckDecides(bool hasPack, bool sourceValid, bool current, AiRuntimeState runtime, AiAddCheck expected)
        {
            Assert.That(AiAddFlow.Decide(hasPack, sourceValid, current, runtime), Is.EqualTo(expected));
        }

        /// <summary>
        /// "최신인가" 는 설치기의 규칙(<see cref="AiPackInstaller.WantsDownload(string, bool, AiPackSource)"/>)과 같다.
        /// 둘이 갈리면 창은 "필요 없다" 는데 설치기는 받으려 하거나, 그 반대가 된다.
        /// </summary>
        [Test]
        public void TheCheckAgreesWithTheInstaller()
        {
            string root = Path.Combine(Path.GetTempPath(), "ntp-add-flow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string manifest = Path.Combine(root, AiPackInstaller.ManifestName);
                File.WriteAllText(manifest, "{\"version\":\"1\"}");
                string sha;
                using (var hash = SHA256.Create()) sha = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(manifest))).Replace("-", "");
                var sources = new[]
                {
                    null,
                    new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = sha },
                    new AiPackSource { manifest_url = "https://example.com/pack-manifest.json", manifest_sha256 = new string('b', 64) },
                };
                foreach (bool hasPack in new[] { true, false })
                    foreach (AiPackSource source in sources)
                    {
                        bool valid = AiPackValidation.Source(source);
                        AiAddCheck check = AiAddFlow.Decide(hasPack, valid, AiPackInstaller.InstalledIsCurrent(root, source), AiRuntimeState.Ready);
                        bool addsAtOnce = check == AiAddCheck.AddNow || check == AiAddCheck.AddNowAndRetry;
                        Assert.That(addsAtOnce, Is.EqualTo(!AiPackInstaller.WantsDownload(root, hasPack, source)),
                            $"팩 {hasPack} · 주소 {(source == null ? "없음" : source.manifest_sha256.Substring(0, 4))} 에서 창과 설치기가 갈린다");
                    }
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>확인이 <b>150ms</b> 를 넘기면 창을 먼저 연다 — 눌렀는데 아무 일도 없어 보이면 다시 누른다.</summary>
        [Test]
        public void ASlowCheckOpensTheWindowAt150ms()
        {
            var flow = new AiAddFlow();
            flow.Begin(10, adds: true);

            Assert.That(flow.Visible(10.149), Is.False, "빠른 확인에도 창이 번쩍인다");
            Assert.That(flow.Visible(10.15), Is.True, "확인이 느린데 빈 화면으로 기다린다");
            Assert.That(AiAddFlow.SlowCheckSeconds, Is.EqualTo(0.15));
            Assert.That(AiAddFlow.Title(flow.Step), Is.EqualTo(AiAddFlow.CheckingTitle));
            Assert.That(AiAddFlow.Buttons(flow.Step), Is.EqualTo(new[] { AiAddButton.Cancel }), "확인 중에 나갈 길이 없다");

            Assert.That(flow.Checked(AiAddCheck.AskInstall, 10.4), Is.EqualTo(AiAddCommand.None));
            Assert.That(flow.Visible(10.4), Is.True);
            Assert.That(flow.Begin(10.5, adds: true), Is.False, "열린 창 위에 또 연다");
        }

        // ---------- 글자와 버튼 ----------

        /// <summary>단계마다 <b>정해진 글자와 버튼만</b> 나온다. 상수를 바꾸면 여기서 먼저 걸린다.</summary>
        [Test]
        public void EachStepSaysExactlyThisAndOffersExactlyThese()
        {
            Assert.That(AiAddFlow.Title(AiAddStep.AskInstall), Is.EqualTo("AI 동료 기능을 사용하려면 설치가 필요합니다"));
            Assert.That(AiAddFlow.Buttons(AiAddStep.AskInstall), Is.EqualTo(new[] { AiAddButton.Confirm, AiAddButton.Cancel }));

            Assert.That(AiAddFlow.Buttons(AiAddStep.Working), Is.EqualTo(new[] { AiAddButton.Retry, AiAddButton.Cancel }),
                "받는 동안 재시도·취소 말고 다른 버튼이 있다 — 한 흐름이 아니다");

            Assert.That(AiAddFlow.Title(AiAddStep.Done), Is.EqualTo("완료되었습니다"));
            Assert.That(AiAddFlow.Buttons(AiAddStep.Done), Is.EqualTo(new[] { AiAddButton.Finish }), "완료 뒤에 완료 말고 다른 버튼이 있다");

            Assert.That(AiAddFlow.Buttons(AiAddStep.Idle), Is.Empty);
            Assert.That(AiAddFlow.Label(AiAddButton.Confirm), Is.EqualTo("확인"));
            Assert.That(AiAddFlow.Label(AiAddButton.Cancel), Is.EqualTo("취소"));
            Assert.That(AiAddFlow.Label(AiAddButton.Retry), Is.EqualTo("재시도"));
            Assert.That(AiAddFlow.Label(AiAddButton.Finish), Is.EqualTo("완료"));

            var done = Ready();
            Assert.That(done.Body, Is.Null, "완료 창에 완료 말고 다른 글자가 있다");
        }

        /// <summary>
        /// 실패하면 <b>재시도와 취소, 딱 둘</b>이다. 설치할 길이 없는 빌드만 취소 하나다 — 몇 번을 눌러도 같은 답이다.
        /// 실패의 까닭은 본문이 쉬운 말로 말한다.
        /// </summary>
        [Test]
        public void AnyFailureOffersExactlyRetryAndCancel()
        {
            foreach (AiPrepFailure failure in Enum.GetValues(typeof(AiPrepFailure)))
            {
                var expected = failure == AiPrepFailure.NoSource
                    ? new[] { AiAddButton.Cancel }
                    : new[] { AiAddButton.Retry, AiAddButton.Cancel };
                Assert.That(AiAddFlow.Buttons(AiAddStep.Failed, failure), Is.EqualTo(expected), failure.ToString());
                foreach (AiAddButton button in expected)
                    Assert.That(AiAddFlow.Enabled(button, AiAddStep.Failed, 0), Is.True, failure + " 의 " + button + " 가 막혔다");
            }

            var flow = Installing();
            flow.Tick(30, See(installer: Installer(AiPrepPhase.Failed, failure: AiPrepFailure.Network)));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Failed));
            Assert.That(AiAddFlow.Buttons(flow.Step, flow.Failure), Is.EqualTo(new[] { AiAddButton.Retry, AiAddButton.Cancel }));
            Assert.That(flow.Body, Is.EqualTo(AiPrepProgress.Display(new AiPrepSnapshot { Phase = AiPrepPhase.Failed, Failure = AiPrepFailure.Network })));

            var nowhere = new AiAddFlow();
            nowhere.Begin(0, true);
            nowhere.Checked(AiAddCheck.CannotInstall, 0);
            Assert.That(nowhere.Step, Is.EqualTo(AiAddStep.Failed));
            Assert.That(nowhere.Body, Is.EqualTo("이 빌드에서는 설치할 수 없습니다"));
            Assert.That(AiAddFlow.Buttons(nowhere.Step, nowhere.Failure), Is.EqualTo(new[] { AiAddButton.Cancel }));
            Assert.That(nowhere.Press(AiAddButton.Retry, 1), Is.EqualTo(AiAddCommand.None), "없는 버튼이 눌린다");
        }

        /// <summary>
        /// <b>취소는 AI 수를 바꾸지 않는다</b> — 어느 단계에서 눌러도 넣으라는 말이 나오지 않는다. 받던 중이면
        /// 받기만 멈춘다(받아 둔 파일은 남는다). 워커를 붙잡아 두던 것도 내린다.
        /// </summary>
        [Test]
        public void CancelNeverChangesTheCount()
        {
            var flows = new Dictionary<string, AiAddFlow>();

            var checking = new AiAddFlow(); checking.Begin(0, true);
            flows["확인 중"] = checking;

            var asking = new AiAddFlow(); asking.Begin(0, true); asking.Checked(AiAddCheck.AskInstall, 0);
            flows["설치 묻기"] = asking;

            var fetching = Installing();
            fetching.Tick(1, See(busy: true, installer: Installer(AiPrepPhase.Fetch, 100 * MB)));
            flows["받는 중"] = fetching;

            var warming = Installing();
            warming.Tick(100, See(installer: Finished()));
            warming.Tick(101, See(AiRuntimeState.Loading, installer: Finished(), warmed: 1));
            flows["데우는 중"] = warming;

            var failed = Installing();
            failed.Tick(2, See(installer: Installer(AiPrepPhase.Failed, failure: AiPrepFailure.Stalled)));
            flows["실패"] = failed;

            foreach (var entry in flows)
            {
                AiAddCommand command = entry.Value.Press(AiAddButton.Cancel, 500);
                Assert.That(command, Is.Not.EqualTo(AiAddCommand.Add).And.Not.EqualTo(AiAddCommand.AddAndRetry),
                    entry.Key + " 에서 취소했는데 AI 를 넣는다");
                Assert.That(entry.Value.Step, Is.EqualTo(AiAddStep.Idle), entry.Key + " 에서 취소해도 창이 남는다");
                entry.Value.Tick(501, See(AiRuntimeState.Loading));
                Assert.That(entry.Value.WantsRuntime, Is.False, entry.Key + " 에서 취소했는데 워커를 붙잡고 있다");
            }
            Assert.That(Installing().Press(AiAddButton.Cancel, 1), Is.EqualTo(AiAddCommand.CancelInstall), "받는 중에 취소해도 받기가 계속된다");
            Assert.That(Ready().Cancel(), Is.EqualTo(AiAddCommand.None), "완료 창이 사라졌는데 AI 를 넣는다");
        }

        // ---------- ④ 한 흐름 ----------

        /// <summary>
        /// 재시도는 <b>실패했거나 5초 넘게 멈췄을 때만</b> 눌린다. 흐르는 받기를 다시 시작하면 기다린 만큼을 잃는다.
        /// 멈춘 받기에서 누르면 받기를 멈추고, 정리가 끝나면 다시 시킨다 — 받아 둔 파일은 설치기가 다시 쓴다.
        /// </summary>
        [Test]
        public void RetryOpensOnlyAfterAStall()
        {
            Assert.That(AiAddFlow.StallSeconds, Is.EqualTo(5.0));
            Assert.That(AiAddFlow.Enabled(AiAddButton.Retry, AiAddStep.Working, 5.0), Is.False);
            Assert.That(AiAddFlow.Enabled(AiAddButton.Retry, AiAddStep.Working, 5.01), Is.True);
            Assert.That(AiAddFlow.Enabled(AiAddButton.Cancel, AiAddStep.Working, 0), Is.True, "받는 중에 취소할 수 없다");

            var flow = Installing(0);
            flow.Tick(1, See(busy: true, installer: Installer(AiPrepPhase.Fetch, 10 * MB)));
            flow.Tick(5, See(busy: true, installer: Installer(AiPrepPhase.Fetch, 30 * MB)));
            flow.Tick(9, See(busy: true, installer: Installer(AiPrepPhase.Fetch, 60 * MB)));
            Assert.That(flow.StalledFor, Is.LessThanOrEqualTo(AiAddFlow.StallSeconds), "흐르는 받기를 멈춘 것으로 본다");
            Assert.That(flow.Press(AiAddButton.Retry, 9), Is.EqualTo(AiAddCommand.None), "흐르는 중에 재시도가 눌린다");

            // 푸는 동안(해시·풀기)도 움직임이다 — 받기 바이트가 그대로여도 멈춘 것이 아니다.
            var unpack = Installer(AiPrepPhase.Unpack, 2000 * MB);
            unpack.HashDone = 100 * MB;
            flow.Tick(10, See(busy: true, installer: unpack));
            unpack.HashDone = 900 * MB;
            flow.Tick(16, See(busy: true, installer: unpack));
            Assert.That(flow.StalledFor, Is.Zero, "푸는 중을 멈춘 것으로 본다");

            flow.Tick(22, See(busy: true, installer: unpack));
            Assert.That(flow.StalledFor, Is.GreaterThan(AiAddFlow.StallSeconds));
            Assert.That(flow.Press(AiAddButton.Retry, 22), Is.EqualTo(AiAddCommand.CancelInstall), "멈췄는데 재시도가 받기를 멈추지 않는다");
            Assert.That(flow.Tick(22.1, See(busy: true, installer: unpack)), Is.EqualTo(AiAddCommand.None), "정리 중에 또 시킨다");
            Assert.That(flow.Tick(22.2, See(installer: Installer(AiPrepPhase.Cancelled))), Is.EqualTo(AiAddCommand.Install),
                "멈춘 뒤 다시 시키지 않는다");
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Working), "재시도가 창을 닫았다");
            Assert.That(flow.Started(AiInstallStart.Started, 22.2), Is.EqualTo(AiAddCommand.None));
        }

        /// <summary>
        /// <b>"완료되었습니다" 는 워커가 Ready 일 때만</b>이다. 설치가 끝난 것은 완료가 아니다 — 그 뒤에 몇 분짜리
        /// 준비가 남는다. 그동안 워커를 띄우도록 붙잡고(AI 수는 아직 0), 창은 남은 시간을 말한다.
        /// </summary>
        [Test]
        public void ItIsDoneOnlyWhenTheWorkerIsReady()
        {
            var flow = Installing(0);
            flow.Tick(50, See(busy: true, installer: Installer(AiPrepPhase.Apply)));
            Assert.That(flow.WantsRuntime, Is.False, "파일을 바꾸는 중에 워커를 띄우려 한다");

            flow.Tick(100, See(installer: Finished()));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Working), "설치만 끝났는데 완료라고 한다");
            Assert.That(flow.WantsRuntime, Is.True, "AI 수가 0 이라 워커가 뜨지 않는다 — 창이 영영 기다린다");

            flow.Tick(101, See(AiRuntimeState.Loading, installer: Finished(), warmed: 40));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Working));
            Assert.That(flow.Body, Does.Contain(AiPrepProgress.FormatRemaining(187)), "데우는 동안 남은 시간을 말하지 않는다");
            Assert.That(flow.Body, Does.Not.Contain("완료"));
            Assert.That(flow.Press(AiAddButton.Retry, 101), Is.EqualTo(AiAddCommand.None), "데우는 중에 재시도가 눌린다 — 처음부터 다시 데운다");

            flow.Tick(300, See(AiRuntimeState.Ready, installer: Finished()));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Done));
            Assert.That(AiAddFlow.Title(flow.Step), Is.EqualTo("완료되었습니다"));
            Assert.That(flow.WantsRuntime, Is.True, "완료를 누르기 전에 워커를 내린다");
        }

        /// <summary>
        /// 설치가 끝났는데 워커가 뜨지 않으면 <b>그렇다고 말한다</b>(StartTimeout). 이유 없이 기다리게 두면 창은
        /// 끝나지 않는 준비가 된다. 재시도는 워커만 다시 띄운다.
        /// </summary>
        [Test]
        public void NoWorkerAfterTheInstallIsAStartTimeout()
        {
            var flow = Installing(0);
            flow.Tick(100, See(installer: Finished()));
            flow.Tick(100 + AiAddFlow.StartGraceSeconds - 0.1, See(installer: Finished()));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Working));

            flow.Tick(100 + AiAddFlow.StartGraceSeconds, See(installer: Finished()));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Failed));
            Assert.That(flow.Failure, Is.EqualTo(AiPrepFailure.StartTimeout));
            Assert.That(flow.Body, Is.EqualTo(AiPrepProgress.Display(new AiPrepSnapshot
            {
                Phase = AiPrepPhase.Failed, Failure = AiPrepFailure.StartTimeout,
            })).And.Not.Empty, "왜 멈췄는지 말하지 않는다");
            Assert.That(flow.Press(AiAddButton.Retry, 120), Is.EqualTo(AiAddCommand.RetryRuntime),
                "설치를 다시 시킨다 — 받아 둔 파일은 이미 비워져 2GB 를 처음부터 받는다");
            Assert.That(flow.WantsRuntime, Is.True);
        }

        /// <summary>워커가 실패하면 실패로, 재시도는 워커만. 받기에서의 실패면 재시도가 받기를 다시 시킨다.</summary>
        [Test]
        public void RetryRestartsOnlyWhatFailed()
        {
            var warm = Installing(0);
            warm.Tick(100, See(installer: Finished()));
            warm.Tick(101, See(AiRuntimeState.Failed, installer: Finished()));
            Assert.That(warm.Failure, Is.EqualTo(AiPrepFailure.StartFailed));
            Assert.That(warm.Press(AiAddButton.Retry, 102), Is.EqualTo(AiAddCommand.RetryRuntime));

            var fetch = Installing(0);
            fetch.Tick(10, See(installer: Installer(AiPrepPhase.Failed, failure: AiPrepFailure.Network)));
            Assert.That(fetch.Press(AiAddButton.Retry, 11), Is.EqualTo(AiAddCommand.Install));
            Assert.That(fetch.Step, Is.EqualTo(AiAddStep.Working));
        }

        /// <summary>
        /// 설치기가 시작하지 않았으면 <b>그 이유로</b> 멈춘다. 전에는 말없이 돌아와 창이 끝나지 않는 0% 가 됐다.
        /// 앞의 시도가 아직 정리 중이면 끝나기를 기다렸다가 다시 시킨다.
        /// </summary>
        [Test]
        public void TheWindowSaysWhyTheInstallerDidNotStart()
        {
            var flow = new AiAddFlow();
            flow.Begin(0, true); flow.Checked(AiAddCheck.AskInstall, 0); flow.Press(AiAddButton.Confirm, 0);
            flow.Started(AiInstallStart.NoSource, 0);
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Failed));
            Assert.That(flow.Body, Is.EqualTo("이 빌드에서는 설치할 수 없습니다"));

            var busy = new AiAddFlow();
            busy.Begin(0, true); busy.Checked(AiAddCheck.AskInstall, 0); busy.Press(AiAddButton.Confirm, 0);
            busy.Started(AiInstallStart.Busy, 0);
            Assert.That(busy.Tick(1, See(busy: true)), Is.EqualTo(AiAddCommand.None));
            Assert.That(busy.Tick(2, See()), Is.EqualTo(AiAddCommand.Install));

            var lost = new AiAddFlow();
            lost.Begin(0, true); lost.Checked(AiAddCheck.AskInstall, 0); lost.Press(AiAddButton.Confirm, 0);
            lost.Started(AiInstallStart.NotHost, 0);
            Assert.That(lost.Step, Is.EqualTo(AiAddStep.Idle));
        }

        /// <summary>
        /// <b>방을 떠나거나 방장이 아니게 되면 취소다.</b> 설치는 방장의 PC 에서만 뜻이 있다. 받던 것은 멈추고,
        /// 확인이 늦게 끝나도 AI 를 넣지 않는다.
        /// </summary>
        [Test]
        public void LeavingTheRoomOrLosingHostCancels()
        {
            var flow = Installing(0);
            flow.Tick(1, See(busy: true, installer: Installer(AiPrepPhase.Fetch, 10 * MB)));
            Assert.That(flow.Tick(2, See(busy: true, host: false)), Is.EqualTo(AiAddCommand.CancelInstall));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Idle));

            var checking = new AiAddFlow();
            checking.Begin(0, true);
            Assert.That(checking.Tick(0.1, See(host: false)), Is.EqualTo(AiAddCommand.None));
            Assert.That(checking.Checked(AiAddCheck.AddNow, 0.2), Is.EqualTo(AiAddCommand.None), "방을 떠났는데 확인 결과로 넣는다");

            var done = Ready();
            done.Tick(400, See(AiRuntimeState.Ready, host: false));
            Assert.That(done.Step, Is.EqualTo(AiAddStep.Idle));
            Assert.That(done.WantsRuntime, Is.False);
        }

        // ---------- ⑤ 완료 ----------

        /// <summary>
        /// 완료를 누르면 <b>그때</b> 넣는다. 방이 수를 돌려줄 때까지 워커를 붙잡는다 — 먼저 내리면 수가 아직 0 이라
        /// 몇 분 데운 워커가 답 한 번 사이에 꺼진다. 방이 받지 않았으면(자리가 찼다) 붙잡지 않는다.
        /// </summary>
        [Test]
        public void TheWorkerIsHeldUntilTheRoomAnswers()
        {
            var flow = Ready();
            Assert.That(flow.Press(AiAddButton.Finish, 400), Is.EqualTo(AiAddCommand.Add));
            Assert.That(flow.Step, Is.EqualTo(AiAddStep.Idle));
            flow.Tick(400.05, See(AiRuntimeState.Ready, requested: 0));
            Assert.That(flow.WantsRuntime, Is.True, "답이 오기 전에 워커를 내린다");
            flow.Tick(400.1, See(AiRuntimeState.Ready, requested: 1));
            Assert.That(flow.WantsRuntime, Is.False, "AI 가 들어왔는데 따로 붙잡고 있다");

            var slow = Ready();
            slow.Press(AiAddButton.Finish, 400);
            slow.Tick(400 + AiAddFlow.HandoffSeconds, See(AiRuntimeState.Ready));
            Assert.That(slow.WantsRuntime, Is.False, "답이 영영 안 오는데 워커를 붙잡는다");

            var refused = Ready();
            refused.Press(AiAddButton.Finish, 400);
            refused.AddRefused();
            Assert.That(refused.WantsRuntime, Is.False, "넣지 못했는데 워커를 붙잡는다");

            var retryOnly = Ready(adds: false);
            Assert.That(retryOnly.Press(AiAddButton.Finish, 400), Is.EqualTo(AiAddCommand.None),
                "\"다시 시도\" 로 연 창이 AI 를 하나 더 넣는다");
        }

        /// <summary>
        /// 팩은 최신인데 워커가 실패해 있으면 <b>넣으면서 다시 띄운다</b>. 넣기만 하면 실패한 워커 그대로라 AI 가 판에
        /// 들어오지 않는다. 방 화면의 "다시 시도" 로 열었으면 넣지 않고 다시 띄우기만 한다.
        /// </summary>
        [Test]
        public void AFailedWorkerIsAddedAndRestarted()
        {
            Assert.That(AiAddFlow.Decide(true, true, true, AiRuntimeState.Failed), Is.EqualTo(AiAddCheck.AddNowAndRetry));

            var add = new AiAddFlow();
            add.Begin(0, adds: true);
            Assert.That(add.Checked(AiAddCheck.AddNowAndRetry, 0.05), Is.EqualTo(AiAddCommand.AddAndRetry));
            Assert.That(add.WantsRuntime, Is.True, "넣으면서 다시 띄우는데 답이 오기 전에 워커를 내린다");

            var retry = new AiAddFlow();
            retry.Begin(0, adds: false);
            Assert.That(retry.Checked(AiAddCheck.AddNowAndRetry, 0.05), Is.EqualTo(AiAddCommand.RetryRuntime));
            retry = new AiAddFlow();
            retry.Begin(0, adds: false);
            Assert.That(retry.Checked(AiAddCheck.AddNow, 0.05), Is.EqualTo(AiAddCommand.None), "\"다시 시도\" 가 AI 를 넣는다");
        }

        /// <summary>
        /// "다시 시도" 는 넣은 AI 의 워커가 실패했을 때만, 대기 중인 방장에게만 나온다. 예전 "AI 추가팩 준비" 버튼이
        /// 맡던 길이다 — 없으면 자리가 다 찬 방의 방장은 실패한 AI 를 되살릴 수 없다.
        /// </summary>
        [TestCase(true, 1, AiRuntimeState.Failed, true)]
        [TestCase(true, 3, AiRuntimeState.Failed, true)]
        [TestCase(true, 0, AiRuntimeState.Failed, false)]
        [TestCase(false, 1, AiRuntimeState.Failed, false)]
        [TestCase(true, 1, AiRuntimeState.Loading, false)]
        [TestCase(true, 1, AiRuntimeState.Ready, false)]
        public void RetryIsOfferedOnlyForAFailedWorkerWithAi(bool canConfigure, int ai, AiRuntimeState runtime, bool expected)
        {
            Assert.That(AiAddFlow.OffersRetry(canConfigure, ai, runtime), Is.EqualTo(expected));
        }

        // ---------- 진행 ----------

        /// <summary>설치에서 준비로 넘어갈 때 막대가 <b>뒤로 가지 않는다</b> — 쓴 시간을 이어서 센다.</summary>
        [Test]
        public void TheBarDoesNotGoBackWhenTheInstallTurnsIntoWarmup()
        {
            var flow = Installing(0);
            var last = Installer(AiPrepPhase.Apply, elapsed: 600, progress: .72f);
            flow.Tick(99, See(busy: true, installer: last));
            float before = flow.Progress.Progress;

            flow.Tick(100, See(installer: Finished(600, .72f)));
            Assert.That(flow.Progress.Progress, Is.GreaterThanOrEqualTo(before));
            flow.Tick(101, See(AiRuntimeState.Loading, installer: Finished(600, .72f), warmed: 1));
            float warming = flow.Progress.Progress;
            Assert.That(warming, Is.GreaterThanOrEqualTo(before), "데우기에 들어서며 막대가 줄었다");
            flow.Tick(400, See(AiRuntimeState.Loading, installer: Finished(600, .72f), warmed: 300));
            Assert.That(flow.Progress.Progress, Is.GreaterThanOrEqualTo(warming), "예상을 넘기자 막대가 줄었다");
            Assert.That(flow.Body, Does.Contain(AiPrepProgress.Overrun), "예상을 넘겼는데 숫자를 댄다");
        }

        /// <summary>
        /// 상태 줄은 받거나 데우는 중이면 <b>진행 글자</b>, 아니면 방의 AI 상태다. 설치기의 멈춘 로그 글자
        /// ("… 남은 227초")가 워커의 실패를 가리던 것을 막는다.
        /// </summary>
        [Test]
        public void TheStatusLineShowsProgressNotTheFrozenInstallerLine()
        {
            AiPrepSnapshot frozen = Finished();
            Assert.That(AiAddFlow.HostStatus(AiRoomState.Failed, false, AiRuntimeState.Failed, frozen),
                Is.EqualTo(AiHostPolicy.Label(AiRoomState.Failed)), "워커가 실패했는데 설치 글자가 서 있다");
            Assert.That(AiAddFlow.HostStatus(AiRoomState.Active, false, AiRuntimeState.Ready, frozen),
                Is.EqualTo(AiHostPolicy.Label(AiRoomState.Active)));

            AiPrepSnapshot fetching = Installer(AiPrepPhase.Fetch, 500 * MB);
            Assert.That(AiAddFlow.HostStatus(AiRoomState.Off, true, AiRuntimeState.Missing, fetching),
                Is.EqualTo(AiPrepProgress.Display(fetching)));

            AiPrepSnapshot warm = AiAddFlow.Warming(default, 40, 40, 227, 0f);
            string line = AiAddFlow.HostStatus(AiRoomState.Loading, false, AiRuntimeState.Loading, warm);
            Assert.That(line, Does.Contain(AiPrepProgress.FormatRemaining(187)), "창 없이 넣었을 때 남은 시간을 말하지 않는다");
            Assert.That(AiAddFlow.RemainingText(warm), Is.EqualTo(AiPrepProgress.FormatRemaining(187)));
            Assert.That(AiAddFlow.RemainingText(AiAddFlow.Warming(default, 300, 300, 227, .9f)), Is.EqualTo(AiPrepProgress.Overrun));
        }

        // ---------- 글자 ----------

        /// <summary>
        /// 창·상태 줄이 낼 수 있는 글자가 모두 미리 그려진다 (<c>LobbyHud.WarmFont</c>). 빠진 글자는 처음 뜰 때 글꼴을
        /// 다시 굽느라 한 프레임 비어 보인다 — 진행 글자는 매 초 바뀌므로 더 자주 보인다.
        /// </summary>
        [Test]
        public void EveryWindowTextIsWarmed()
        {
            var warm = (string)typeof(LobbyHud).GetMethod("WarmCharacters", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

            var texts = new List<string>(AiPrepTextTests.WindowTexts());
            foreach (var s in AiPrepTextTests.Every())
                texts.AddRange(LobbyRoster.Rows(AiHostPolicyTests.LobbyRoom(players: 1, aiCount: 1), 0,
                    AiRuntimeState.Loading, null, AiAddFlow.RemainingText(s)).Select(row => row.Label));
            var missing = new SortedSet<char>();
            string example = null;
            foreach (string text in texts.Distinct())
                foreach (char c in text)
                    if (warm.IndexOf(c) < 0 && missing.Add(c)) example = example ?? text;
            Assert.That(new string(missing.ToArray()), Is.Empty, $"\"{example}\" 등의 글자가 미리 그려지지 않는다");
        }

        /// <summary>창의 본문이 될 수 있는 글자 — 흐름을 단계마다 실제로 돌려 얻는다(<see cref="AiPrepTextTests"/> 도 쓴다).</summary>
        internal static IEnumerable<string> EveryBody()
        {
            foreach (AiPrepPhase phase in new[] { AiPrepPhase.Check, AiPrepPhase.Fetch, AiPrepPhase.Unpack, AiPrepPhase.Apply })
            {
                var flow = Installing();
                flow.Tick(1, See(busy: true, installer: Installer(phase, 500 * MB)));
                yield return flow.Body;
            }

            var stalled = Installing();
            var quiet = Installer(AiPrepPhase.Fetch, 500 * MB);
            quiet.Idle = AiPackInstaller.StallNotice + 4;
            stalled.Tick(1, See(busy: true, installer: quiet));
            yield return stalled.Body;

            var waiting = Installing();
            waiting.Tick(100, See(installer: Finished()));
            yield return waiting.Body;
            foreach (double warmed in new double[] { 5, 200, 400 })
            {
                waiting.Tick(100 + warmed, See(AiRuntimeState.Loading, installer: Finished(), warmed: warmed));
                yield return waiting.Body;
            }
            waiting.Tick(900, See(AiRuntimeState.Failed));
            yield return waiting.Body;

            var timeout = Installing();
            timeout.Tick(100, See(installer: Finished()));
            timeout.Tick(200, See(installer: Finished()));
            yield return timeout.Body;

            foreach (AiPrepFailure failure in Enum.GetValues(typeof(AiPrepFailure)))
            {
                var failed = Installing();
                var s = Installer(AiPrepPhase.Failed, failure: failure);
                s.NeededBytes = failure == AiPrepFailure.NoSpace ? 3L * 1073741824 : 0;
                failed.Tick(1, See(installer: s));
                yield return failed.Body;
            }

            var nowhere = new AiAddFlow();
            nowhere.Begin(0, true);
            nowhere.Checked(AiAddCheck.CannotInstall, 0);
            yield return nowhere.Body;
        }

        // ---------- 배선 ----------

        /// <summary>
        /// 방 화면이 <b>설치 창을 들고</b>, 창이 떠 있는 동안 그 아래를 모두 막는다. IMGUI 는 먼저 그린 버튼이 누름을
        /// 먼저 받는다 — 막지 않으면 창 뒤의 "게임 시작" 이 창을 뚫고 눌려 설치 도중 판이 시작된다.
        /// </summary>
        [Test]
        public void TheLobbyHostsTheWindowAndBlocksWhatIsBehindIt()
        {
            string lobby = TestSource.Read("Runtime/UI/LobbyHud.cs");
            Assert.That(lobby, Does.Contain("installPopup ??= new AiInstallPopup(AddAiNow);"), "방 화면이 설치 창을 들지 않는다");

            string draw = TestSource.Method(lobby, "private void DrawMultiplayer(");
            Assert.That(draw, Does.Contain("if (popupOpen) GUI.enabled = false;"), "창이 떠 있어도 방 화면의 버튼이 눌린다");
            int restore = draw.LastIndexOf("GUI.enabled = enabledBefore;", StringComparison.Ordinal);
            int popup = draw.IndexOf("installPopup.Draw(", StringComparison.Ordinal);
            Assert.That(popup, Is.GreaterThan(0), "설치 창을 그리지 않는다");
            Assert.That(restore, Is.GreaterThan(draw.IndexOf("DrawRoom(", StringComparison.Ordinal)).And.LessThan(popup),
                "방 화면을 다 그린 뒤 되돌리고 창을 그리지 않는다 — 창의 버튼까지 막히거나, 창이 방 화면 아래에 깔린다");

            string room = TestSource.Method(lobby, "private void DrawRoom(");
            Assert.That(room, Does.Not.Contain("GUI.enabled = true;"),
                "방 화면이 막힘을 true 로 풀어 버린다 — 창이 막아 둔 \"방 나가기\" 가 다시 눌린다");

            Assert.That(TestSource.Method(lobby, "private void Update("), Does.Contain("installPopup.Tick("),
                "설치 창이 매 프레임 상태를 보지 않는다 — 워커가 Ready 가 돼도 완료가 안 뜬다");
            Assert.That(TestSource.Method(lobby, "private void OnDisable("), Does.Contain("installPopup?.Cancel();"),
                "방 화면이 사라져도 받기가 뒤에서 계속된다");
        }

        /// <summary>
        /// "AI 동료 추가" 는 <b>흐름을 거친다</b> — 팩부터 보고, 넣는 것은 한 곳(<c>AddAiNow</c>)이 그 순간의 방으로
        /// 다시 확인한 뒤에 한다(설치하는 사이 자리가 찼을 수 있다). 창은 흐름의 결정을 따른다.
        /// </summary>
        [Test]
        public void TheAddButtonGoesThroughTheFlow()
        {
            string lobby = TestSource.Read("Runtime/UI/LobbyHud.cs");
            Assert.That(TestSource.Method(lobby, "private void DrawRoom("), Does.Contain("Popup.Begin(true);"));
            string add = TestSource.Method(lobby, "private string AddAiNow(");
            Assert.That(add, Does.Contain("LobbyRoster.Add(room,"), "완료를 누른 순간 자리를 다시 보지 않는다");
            Assert.That(add, Does.Contain("if (!add.Enabled) return add.Reason;"), "자리가 찼는데 보낸다");
            Assert.That(add, Does.Contain("AskAi(room.AiCount + 1);"));

            string popup = TestSource.Read("Runtime/UI/AiInstallPopup.cs");
            Assert.That(TestSource.Method(popup, "AiAddCheck Decide("), Does.Contain("AiAddFlow.Decide("),
                "창이 흐름과 다른 규칙으로 정한다");
            Assert.That(TestSource.Method(popup, "public string Tick("), Does.Contain("AiHostSettings.TrySetPreparing(flow.WantsRuntime);"),
                "창이 워커를 붙잡지 않는다 — AI 수가 0 이라 워커가 뜨지 않는다");
            Assert.That(TestSource.Method(popup, "string Apply("), Does.Contain("flow.Started(started, Now)"),
                "설치기가 시작하지 않은 이유를 흐름에 알리지 않는다 — 끝나지 않는 0% 가 된다");
        }

        /// <summary>
        /// 팩 확인은 작업 스레드에서 돈다. <b>유니티가 주는 값(설치 폴더·배포 주소 파일)은 메인 스레드에서</b> 잡아
        /// 넘긴다 — 작업 스레드에서 <c>Application</c> 을 부르면 예외로 확인이 죽는다.
        /// </summary>
        [Test]
        public void ThePackCheckReadsUnityOnlyOnTheMainThread()
        {
            string popup = TestSource.Read("Runtime/UI/AiInstallPopup.cs");
            string start = TestSource.Method(popup, "void StartCheck(");
            Assert.That(start, Does.Contain("string root = runtime.PackRoot;"));
            Assert.That(start, Does.Contain("AiPackSource source = AiPackInstaller.LoadSource();"));
            Assert.That(start, Does.Contain("check = Task.Run(() => Facts(root, source));"), "확인을 작업 스레드로 보내지 않는다");

            string facts = TestSource.Method(popup, "static PackFacts Facts(");
            foreach (string unity in new[] { "LoadSource(", "Application.", "PackRoot", "Time.", "AiLocalRuntime.Current" })
                Assert.That(facts, Does.Not.Contain(unity), "작업 스레드가 유니티 값을 읽는다 — " + unity);
        }

        /// <summary>상태 줄이 실행기·설치기의 <b>로그 글자</b>(<c>Message</c>)를 읽지 않는다 — 화면 글자는 진행 모델에서 나온다.</summary>
        [Test]
        public void TheStatusLineReadsNoLogText()
        {
            string controls = TestSource.Read("Runtime/UI/AiRoomControls.cs");
            Assert.That(TestSource.Count(controls, ".Message"), Is.Zero, "상태 줄에 로그 글자가 돌아왔다 — 속사정과 파일 경로가 샌다");
            Assert.That(TestSource.Method(controls, "static string HostStatus("), Does.Contain("AiAddFlow.HostStatus("));
        }
    }
}
