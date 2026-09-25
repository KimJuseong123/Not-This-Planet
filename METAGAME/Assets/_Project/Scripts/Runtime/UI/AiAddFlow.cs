using System;
using System.Collections.Generic;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>"AI 동료 추가" 를 누른 뒤 창이 지금 어디에 있나 (S15P21B202-713).</summary>
    public enum AiAddStep : byte { Idle, Checking, AskInstall, Working, Failed, Done }

    /// <summary>이 PC 의 팩을 확인한 결과 — 바로 넣을지, 설치를 물을지.</summary>
    public enum AiAddCheck : byte { AddNow, AddNowAndRetry, AskInstall, CannotInstall }

    /// <summary>창의 버튼. 글자는 <see cref="AiAddFlow.Label"/> 이 준다.</summary>
    public enum AiAddButton : byte { Confirm, Cancel, Retry, Finish }

    /// <summary>흐름이 창(<see cref="AiInstallPopup"/>)에게 시키는 일. 흐름은 스스로 아무것도 부르지 않는다.</summary>
    public enum AiAddCommand : byte { None, Add, AddAndRetry, RetryRuntime, Install, CancelInstall }

    /// <summary>창이 한 프레임에 보는 것. 흐름은 이것만 읽고 결정한다 — 유니티도 소켓도 모른다.</summary>
    public struct AiAddInputs
    {
        /// <summary>대기 중인 방의 방장이고, 방 화면이 떠 있다. 아니면 창은 스스로 닫힌다.</summary>
        public bool HostOk;
        public bool InstallerBusy;
        public AiPrepSnapshot Installer;
        public AiRuntimeState Runtime;

        /// <summary>워커가 Loading 일 때 데우기를 시작한 뒤 흐른 초. 아니면 0.</summary>
        public double WarmupElapsed;

        /// <summary>이 PC 에서 데우기에 걸릴 것으로 보는 초. 모르면 0.</summary>
        public double WarmupExpected;

        /// <summary>방이 정한 AI 수.</summary>
        public int Requested;
    }

    /// <summary>
    /// "AI 동료 추가" 의 흐름 — 확인 → (필요하면) 설치를 묻기 → 받기·설치·준비 → 완료 (S15P21B202-713).
    ///
    /// <para><b>왜 순수한 상태 기계인가.</b> 이 흐름의 규칙은 전부 "언제 무엇을 보여 주고 무엇을 누를 수
    /// 있나" 다. 창을 그리는 IMGUI 안에 두면 씬 없이는 시험할 수 없고, 버튼 하나를 더 그리는 날 규칙이
    /// 그 사이에 흩어진다. 그래서 흐름은 시각(<c>now</c>)과 본 것(<see cref="AiAddInputs"/>)만 받고,
    /// 할 일은 <see cref="AiAddCommand"/> 로 돌려준다. 받기를 시작하고 AI 를 넣는 것은 창이 한다.</para>
    ///
    /// <para><b>받기 → 설치 → 준비는 한 흐름이다</b> (④). 방장은 중간에 아무것도 누르지 않는다 — 버튼은
    /// 재시도와 취소뿐이고, 재시도는 실패했거나 5초 넘게 멈췄을 때만 눌린다. 흐르고 있는 받기를 다시
    /// 시작하게 두면, 방장은 느린 것을 멈춘 것으로 읽고 눌러서 더 늦게 만든다.</para>
    ///
    /// <para><b>"완료되었습니다" 는 워커가 Ready 라고 할 때만</b> 나온다. 설치가 끝난 것은 완료가 아니다 —
    /// 그 뒤에 몇 분짜리 준비가 남아 있고, 전에는 그 사이 화면이 "설치 완료" 라고 해서 방장이 AI 가
    /// 안 움직이는 까닭을 몰랐다.</para>
    /// </summary>
    public sealed class AiAddFlow
    {
        /// <summary>확인이 이보다 오래 걸리면 창을 먼저 연다 — 눌렀는데 아무 일도 없어 보이면 다시 누른다.</summary>
        public const double SlowCheckSeconds = 0.15;

        /// <summary>이만큼 진행이 없으면 멈춘 것으로 보고 재시도를 연다. 받기가 "멈췄어요" 라고 말하는 때와 같다.</summary>
        public const double StallSeconds = AiPackInstaller.StallNotice;

        /// <summary>
        /// 설치가 끝난 뒤 워커가 이 안에 뜨지(Loading) 않으면 실패로 본다. 워커는 설치가 끝난 다음
        /// 프레임에 뜬다 — 이만큼 기다려도 안 뜨면 팩이 있는데도 못 띄우는 것이다. 이유를 말하지 않고
        /// 기다리게 두면 창은 끝나지 않는 준비가 된다.
        /// </summary>
        public const double StartGraceSeconds = 10;

        /// <summary>
        /// 완료를 누른 뒤 방이 AI 수를 돌려줄 때까지 워커를 붙잡아 두는 한도.
        ///
        /// <para>AI 수는 답(ROOM_STATE)이 와야 바뀐다. 그 사이 "준비 중" 표시를 먼저 내리면 수가 아직
        /// 0 이라서 실행기가 워커를 끈다 — 몇 분 데운 것이 답 한 번 사이에 날아간다. 답은 보통 수십 ms
        /// 안에 온다. 거절됐다면 수가 안 바뀐 채로 여기서 끝난다.</para>
        /// </summary>
        public const double HandoffSeconds = 5;

        public const string CheckingTitle = "AI 동료 기능을 확인하는 중입니다";
        public const string AskTitle = "AI 동료 기능을 사용하려면 설치가 필요합니다";
        public const string WorkingTitle = "AI 동료 기능을 준비하는 중입니다";
        public const string FailedTitle = "AI 동료 기능을 준비하지 못했습니다";
        public const string DoneTitle = "완료되었습니다";

        /// <summary>방 화면의 "다시 시도" — 방장의 AI 가 실행에 실패했을 때 목록 아래에 나온다.</summary>
        public const string RetryRuntimeLabel = "다시 시도";

        static readonly AiAddButton[] NoButtons = new AiAddButton[0];
        static readonly AiAddButton[] CancelOnly = { AiAddButton.Cancel };
        static readonly AiAddButton[] ConfirmOrCancel = { AiAddButton.Confirm, AiAddButton.Cancel };
        static readonly AiAddButton[] RetryOrCancel = { AiAddButton.Retry, AiAddButton.Cancel };
        static readonly AiAddButton[] FinishOnly = { AiAddButton.Finish };

        // 일하는 동안의 세부 단계. 창에는 보이지 않는다 — 보이는 것은 진행 글자 하나다.
        enum Work : byte { None, Installing, Restarting, Starting, Warming }

        Work work;
        double checkFrom, installedAt, waitFrom, lastMove, handoffUntil;
        long moveMark = -1;
        AiPrepPhase movePhase;
        AiPrepSnapshot installed;
        bool handoff;

        public AiAddStep Step { get; private set; }

        /// <summary>실패했다면 왜. 창의 글자와 재시도 버튼이 이것을 본다.</summary>
        public AiPrepFailure Failure { get; private set; }

        /// <summary>완료를 누르면 AI 를 하나 넣는가. 방 화면의 "다시 시도" 로 연 흐름은 넣지 않는다.</summary>
        public bool Adds { get; private set; }

        /// <summary>진행이 없었던 초. 재시도를 열지 정한다. 데우는 동안은 늘 0 이다(아래 <see cref="Tick"/>).</summary>
        public double StalledFor { get; private set; }

        /// <summary>창에 그릴 진행 상태. 글자는 <see cref="Body"/> 가 이것으로 만든다.</summary>
        public AiPrepSnapshot Progress { get; private set; }

        /// <summary>
        /// 워커를 띄워 두어야 하나 — <see cref="AiHostSettings.Preparing"/> 에 옮겨 적는다. 받는 동안은
        /// 아니다(설치기가 파일을 바꾼다). 설치가 끝나 워커를 기다릴 때부터 완료를 누르고 방이 수를
        /// 돌려줄 때까지다.
        /// </summary>
        public bool WantsRuntime => handoff || Step == AiAddStep.Done
            || (Step == AiAddStep.Working && (work == Work.Starting || work == Work.Warming));

        /// <summary>
        /// 이 PC 에 지금 쓸 팩이 있나, 없으면 어떻게 하나.
        ///
        /// <para><b>배포 주소가 없는 빌드(개발·에디터)에서는 깔린 팩을 최신으로 본다</b> —
        /// <see cref="AiPackInstaller.WantsDownload(string, bool, AiPackSource)"/> 와 같은 규칙이다. 그
        /// 빌드에서 "설치가 필요합니다" 라고 하면 설치는 주소가 없어 실패한다. 팩도 주소도 없으면
        /// 설치할 길이 없다고 말한다.</para>
        ///
        /// <para>팩이 최신인데 워커가 실패해 있으면 넣으면서 다시 띄운다. 넣기만 하면 실패한 워커가 그대로라
        /// AI 가 판에 들어오지 않는다.</para>
        /// </summary>
        public static AiAddCheck Decide(bool hasPack, bool sourceValid, bool installedIsCurrent, AiRuntimeState runtime)
        {
            bool current = hasPack && (!sourceValid || installedIsCurrent);
            if (current) return runtime == AiRuntimeState.Failed ? AiAddCheck.AddNowAndRetry : AiAddCheck.AddNow;
            return sourceValid ? AiAddCheck.AskInstall : AiAddCheck.CannotInstall;
        }

        /// <summary>창을 보이나. 확인 중에는 <see cref="SlowCheckSeconds"/> 가 지나야 보인다 — 빠른 확인에 창이 번쩍이지 않게.</summary>
        public static bool Shows(AiAddStep step, double checkingFor)
            => step != AiAddStep.Idle && (step != AiAddStep.Checking || checkingFor >= SlowCheckSeconds);

        public static string Title(AiAddStep step)
        {
            switch (step)
            {
                case AiAddStep.Checking: return CheckingTitle;
                case AiAddStep.AskInstall: return AskTitle;
                case AiAddStep.Working: return WorkingTitle;
                case AiAddStep.Failed: return FailedTitle;
                case AiAddStep.Done: return DoneTitle;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 이 단계의 버튼, 그리는 차례대로.
        ///
        /// <para>설치할 길이 없는 실패(<see cref="AiPrepFailure.NoSource"/>)에는 재시도가 없다 — 몇 번을
        /// 눌러도 같은 답이다. 나머지 실패는 모두 재시도와 취소다.</para>
        /// </summary>
        public static IReadOnlyList<AiAddButton> Buttons(AiAddStep step, AiPrepFailure failure = AiPrepFailure.None)
        {
            switch (step)
            {
                case AiAddStep.Checking: return CancelOnly;
                case AiAddStep.AskInstall: return ConfirmOrCancel;
                case AiAddStep.Working: return RetryOrCancel;
                case AiAddStep.Failed: return failure == AiPrepFailure.NoSource ? CancelOnly : RetryOrCancel;
                case AiAddStep.Done: return FinishOnly;
                default: return NoButtons;
            }
        }

        public static string Label(AiAddButton button)
        {
            switch (button)
            {
                case AiAddButton.Confirm: return "확인";
                case AiAddButton.Retry: return "재시도";
                case AiAddButton.Finish: return "완료";
                default: return "취소";
            }
        }

        /// <summary>
        /// 이 버튼을 지금 누를 수 있나. <b>일하는 동안의 재시도만</b> 막는다 — 멈춘 지
        /// <see cref="StallSeconds"/> 가 넘어야 열린다. 재시도는 받던 것을 버리고 다시 시작하므로,
        /// 흐르는 중에 누르면 방장은 기다린 만큼을 잃는다.
        /// </summary>
        public static bool Enabled(AiAddButton button, AiAddStep step, double stalledFor)
            => button != AiAddButton.Retry || step != AiAddStep.Working || stalledFor > StallSeconds;

        /// <summary>
        /// 데우는 동안의 진행 상태. 설치를 거쳤다면 그 상태(<paramref name="basis"/>)에 이어 붙인다 —
        /// 막대가 설치에서 준비로 넘어갈 때 뒤로 가지 않게 쓴 시간을 이어서 센다.
        /// </summary>
        /// <param name="elapsed">이 준비 전체에서 흐른 초</param>
        /// <param name="warmupElapsed">워커가 데우기 시작한 뒤 흐른 초. 아직 안 떴으면 0</param>
        /// <param name="warmupExpected">이 PC 의 예상. 0 이하면 <paramref name="basis"/> 의 값을 쓴다</param>
        /// <param name="previous">지난번 막대. 막대는 줄지 않는다</param>
        /// <param name="previousAt">지난번 막대를 정한 때(<see cref="AiPrepSnapshot.ProgressAt"/>). 모르면 음수.
        /// <b>바탕(<paramref name="basis"/>)의 때를 쓰면 안 된다</b> — 설치가 끝난 순간에 멈춰 있어, 앞서 간
        /// 막대를 따라잡는 계산이 매 프레임 "설치가 끝난 뒤 전부" 를 흐른 시간으로 쳐 순식간에 99% 로 간다(리뷰).</param>
        public static AiPrepSnapshot Warming(AiPrepSnapshot basis, double elapsed, double warmupElapsed,
                                             double warmupExpected, float previous, double previousAt = -1)
        {
            AiPrepSnapshot s = basis;
            s.Phase = AiPrepPhase.Start;
            s.Failure = AiPrepFailure.None;
            s.Idle = 0;
            s.WarmupElapsed = Math.Max(0, warmupElapsed);
            if (warmupExpected > 0) s.WarmupExpected = warmupExpected;
            s.Elapsed = Math.Max(Math.Max(0, elapsed), s.WarmupElapsed);
            if (previousAt >= 0) s.ProgressAt = previousAt;
            s.Progress = AiPrepProgress.Fraction(s, previous);
            s.ProgressAt = s.Elapsed;
            return s;
        }

        /// <summary>
        /// 방 화면의 상태 줄 (S15P21B202-713). 받거나 데우는 중이면 <b>진행 글자</b>, 아니면 방의 AI 상태다.
        ///
        /// <para><b>설치기의 로그 글자를 쓰지 않는다.</b> 전에는 설치기 글자가 있으면 그것이 먼저였는데,
        /// 그 글자는 설치가 끝난 순간(<c>남은 227초</c>)에 멈춰 선다. 그래서 워커가 실패해도 화면은 멈춘
        /// 설치 글자를 보였다 — 실패가 가려졌다.</para>
        /// </summary>
        public static string HostStatus(AiRoomState local, bool installing, AiRuntimeState runtime, AiPrepSnapshot progress)
        {
            if (installing || runtime == AiRuntimeState.Loading) return AiPrepProgress.Display(progress);
            return AiHostPolicy.Label(local);
        }

        /// <summary>
        /// 방장 화면의 AI 줄에 붙는 남은 시간 — <c>"약 3분 남음"</c>. 예상을 넘겼으면 숫자 없이
        /// <see cref="AiPrepProgress.Overrun"/> 이다. 막대 글자(<see cref="AiPrepProgress.Display"/>)와 같은 셈이다.
        /// </summary>
        public static string RemainingText(AiPrepSnapshot s)
        {
            double? left = AiPrepProgress.Remaining(s);
            if (left.HasValue) return AiPrepProgress.FormatRemaining(left);
            return s.Phase == AiPrepPhase.Start ? AiPrepProgress.Overrun : AiPrepProgress.Unknown;
        }

        /// <summary>
        /// 방 화면에 "다시 시도" 를 낼까 — 대기 중인 방장이고, AI 를 넣었는데 워커가 실패해 있다.
        ///
        /// <para>예전의 "AI 추가팩 준비" 버튼이 이 경우를 맡았다. 그 버튼을 설치 창으로 바꾸면서
        /// 이 길이 사라지면, 자리가 다 찬 방(추가를 누를 수 없다)의 방장은 실패한 AI 를 되살릴 길이 없다.</para>
        /// </summary>
        public static bool OffersRetry(bool canConfigure, int aiCount, AiRuntimeState runtime)
            => canConfigure && aiCount > 0 && runtime == AiRuntimeState.Failed;

        /// <summary>창에 적을 한 줄. 일하는 중이면 진행, 실패면 이유와 다음에 할 일. 그 밖에는 없다.</summary>
        public string Body => Step == AiAddStep.Working || Step == AiAddStep.Failed
            ? AiPrepProgress.Display(Progress) : null;

        public bool Visible(double now) => Shows(Step, now - checkFrom);

        /// <summary>"AI 동료 추가"(<paramref name="adds"/>) 또는 "다시 시도" 를 눌렀다. 이미 열려 있으면 거짓.</summary>
        public bool Begin(double now, bool adds)
        {
            if (Step != AiAddStep.Idle) return false;
            Step = AiAddStep.Checking;
            Adds = adds;
            checkFrom = now;
            work = Work.None;
            Failure = AiPrepFailure.None;
            StalledFor = 0;
            Progress = default;
            installed = default;
            installedAt = now;
            handoff = false;
            return true;
        }

        /// <summary>팩 확인이 끝났다. 최신이면 창 없이 바로 넣고, 아니면 설치를 묻는다(③).</summary>
        public AiAddCommand Checked(AiAddCheck check, double now)
        {
            if (Step != AiAddStep.Checking) return AiAddCommand.None;
            switch (check)
            {
                case AiAddCheck.AddNow:
                    Step = AiAddStep.Idle;
                    return Adds ? Handoff(now, AiAddCommand.Add) : AiAddCommand.None;
                case AiAddCheck.AddNowAndRetry:
                    Step = AiAddStep.Idle;
                    return Adds ? Handoff(now, AiAddCommand.AddAndRetry) : AiAddCommand.RetryRuntime;
                case AiAddCheck.AskInstall:
                    Step = AiAddStep.AskInstall;
                    return AiAddCommand.None;
                default:
                    Fail(AiPrepFailure.NoSource);
                    return AiAddCommand.None;
            }
        }

        /// <summary>창의 버튼을 눌렀다. 이 단계에 없거나 막힌 버튼은 아무 일도 하지 않는다.</summary>
        public AiAddCommand Press(AiAddButton button, double now)
        {
            if (!Has(button) || !Enabled(button, Step, StalledFor)) return AiAddCommand.None;
            switch (button)
            {
                case AiAddButton.Cancel:
                    return Close();
                case AiAddButton.Confirm:
                    return BeginInstall(now);
                case AiAddButton.Finish:
                    Step = AiAddStep.Idle;
                    work = Work.None;
                    return Adds ? Handoff(now, AiAddCommand.Add) : AiAddCommand.None;
                default:
                    return Retry(now);
            }
        }

        /// <summary>
        /// 설치기에 시작을 시킨 결과. 시작하지 않았으면 <b>그 이유로</b> 멈춘다 — 이유 없이 기다리면
        /// 끝나지 않는 0% 가 된다.
        /// </summary>
        public AiAddCommand Started(AiInstallStart result, double now)
        {
            if (Step != AiAddStep.Working || work != Work.Installing) return AiAddCommand.None;
            switch (result)
            {
                case AiInstallStart.Started:
                    lastMove = now;
                    moveMark = -1;
                    return AiAddCommand.None;
                case AiInstallStart.Busy:
                    // 앞의 시도가 아직 정리 중이다. 끝나면 다시 시킨다.
                    work = Work.Restarting;
                    return AiAddCommand.None;
                case AiInstallStart.NotHost:
                    return Close();
                default:
                    // 주소가 없거나 이 빌드에 실행기가 없다 — 어느 쪽이든 이 빌드에서는 설치할 수 없다.
                    Fail(AiPrepFailure.NoSource);
                    return AiAddCommand.None;
            }
        }

        /// <summary>완료를 눌렀지만 방이 받지 않았다(자리가 찼다 등). 워커를 붙잡아 둘 까닭이 없다.</summary>
        public void AddRefused() => handoff = false;

        /// <summary>창 밖의 이유로 닫는다 — 화면이 사라졌다. 받던 것이 있으면 멈추라고 돌려준다.</summary>
        public AiAddCommand Cancel() => Step == AiAddStep.Idle ? DropHandoff() : Close();

        /// <summary>
        /// 한 프레임. 본 것에 따라 단계를 옮기고, 할 일이 있으면 돌려준다.
        ///
        /// <para><b>방을 떠났거나 방장이 아니게 되면 취소다.</b> 설치는 방장의 PC 에서만 뜻이 있고, 새
        /// 방장은 자기 PC 에서 자기 팩으로 정한다.</para>
        /// </summary>
        public AiAddCommand Tick(double now, in AiAddInputs see)
        {
            if (handoff && (see.Requested > 0 || now >= handoffUntil || !see.HostOk)) handoff = false;
            if (Step == AiAddStep.Idle) return AiAddCommand.None;
            if (!see.HostOk) return Close();

            if (Step == AiAddStep.Done)
            {
                // 완료는 워커가 Ready 인 동안만이다. 누르기 전에 워커가 쓰러지면 다시 말한다.
                if (see.Runtime == AiRuntimeState.Failed) Fail(AiPrepFailure.StartFailed, Progress);
                else if (see.Runtime != AiRuntimeState.Ready) { Step = AiAddStep.Working; Wait(now); }
                return AiAddCommand.None;
            }
            if (Step != AiAddStep.Working) return AiAddCommand.None;

            switch (work)
            {
                case Work.Installing:
                    if (see.InstallerBusy) { Follow(now, see.Installer); return AiAddCommand.None; }
                    return Installed(now, see.Installer);
                case Work.Restarting:
                    // 재시도로 멈춘 받기가 정리되기를 기다렸다가 다시 시킨다. 받아 둔 파일은 설치기가 다시 쓴다.
                    //
                    // **그 사이 설치가 끝났으면 다시 시키지 않는다** (리뷰). 재시도가 설치가 끝나는 바로 그
                    // 프레임에 닿으면 멈출 받기가 없다 — 설치기는 이미 받아 둔 파일을 비우고 새 팩을 깔았다.
                    // 그대로 다시 시키면 2GB 를 처음부터 받고 방금 깐 팩을 갈아 끼운다. 끝났으면 워커를 기다린다.
                    if (see.InstallerBusy) return AiAddCommand.None;
                    return see.Installer.Phase == AiPrepPhase.Start ? Installed(now, see.Installer) : BeginInstall(now);
                default:
                    Watch(now, see);
                    return AiAddCommand.None;
            }
        }

        bool Has(AiAddButton button)
        {
            foreach (AiAddButton b in Buttons(Step, Failure))
                if (b == button) return true;
            return false;
        }

        AiAddCommand BeginInstall(double now)
        {
            Step = AiAddStep.Working;
            work = Work.Installing;
            Failure = AiPrepFailure.None;
            lastMove = now;
            moveMark = -1;
            StalledFor = 0;
            Progress = new AiPrepSnapshot { Phase = AiPrepPhase.Check, Progress = Progress.Progress };
            return AiAddCommand.Install;
        }

        /// <summary>
        /// 재시도. <b>설치를 마친 뒤의 실패면 워커만 다시 띄운다</b> — 설치를 다시 시키면 받아 둔 파일이
        /// 이미 비워져 2GB 를 처음부터 받는다. 받기·설치에서의 실패면 설치를 다시 시킨다(받아 둔 파일은
        /// 설치기가 확인해 다시 쓴다).
        /// </summary>
        AiAddCommand Retry(double now)
        {
            if (Step == AiAddStep.Working)
            {
                if (work == Work.Installing) { work = Work.Restarting; StalledFor = 0; return AiAddCommand.CancelInstall; }
                if (work == Work.Restarting) return AiAddCommand.None;
                Wait(now);
                return AiAddCommand.RetryRuntime;
            }
            if (Failure == AiPrepFailure.StartFailed || Failure == AiPrepFailure.StartTimeout)
            {
                Step = AiAddStep.Working;
                Failure = AiPrepFailure.None;
                Wait(now);
                return AiAddCommand.RetryRuntime;
            }
            return BeginInstall(now);
        }

        /// <summary>설치기가 멈췄다. 어디서 멈췄는지로 다음을 정한다.</summary>
        AiAddCommand Installed(double now, AiPrepSnapshot s)
        {
            switch (s.Phase)
            {
                case AiPrepPhase.Start:
                    // 설치는 끝났다 — 아직 완료가 아니다. 이제 워커를 기다린다.
                    installed = s;
                    installedAt = now;
                    Wait(now);
                    return AiAddCommand.None;
                case AiPrepPhase.Failed:
                    // 공간 부족이면 얼마를 비울지(NeededBytes)가 이 상태에 있다 — 그대로 넘긴다.
                    Fail(s.Failure == AiPrepFailure.None ? AiPrepFailure.Apply : s.Failure, s);
                    return AiAddCommand.None;
                case AiPrepPhase.Cancelled:
                    // 우리가 시키지 않은 취소다 — 설치기가 사라졌다(씬 정리). 창도 닫는다.
                    return Close();
                default:
                    // 끝나지 않았는데 바쁘지도 않다. 이유를 모르는 채 기다리게 두지 않는다.
                    Fail(AiPrepFailure.Apply, s);
                    return AiAddCommand.None;
            }
        }

        /// <summary>
        /// 받기·설치의 진행을 따라가며 멈춘 시간을 잰다. <b>처리한 바이트나 단계가 바뀌면 움직인 것</b>이다 —
        /// 받기만이 아니라 확인(해시)과 풀기도 센다. 그래서 2GB 를 푸는 동안 재시도가 열리지 않는다.
        /// </summary>
        void Follow(double now, AiPrepSnapshot s)
        {
            long mark = s.FetchDone + s.HashDone + s.ExtractDone;
            if (mark != moveMark || s.Phase != movePhase)
            {
                moveMark = mark;
                movePhase = s.Phase;
                lastMove = now;
            }
            StalledFor = Math.Max(0, now - lastMove);
            Progress = s;
        }

        /// <summary>
        /// 워커를 기다리고 데우는 동안.
        ///
        /// <para><b>데우는 동안은 멈춤으로 보지 않는다.</b> 워커는 데우는 동안 단계를 알려 주지 않고, 설치
        /// 뒤 첫 실행은 처음 몇십 초 동안 대답조차 없을 수 있다(백신 검사가 겹친다). 그때 재시도를 열면
        /// 멀쩡히 데우던 것을 처음부터 다시 하게 만든다. 정말 멈춘 워커는 실행기가 자기 마감
        /// (<see cref="AiLocalRuntime.SilenceLimit"/>·<see cref="AiLocalRuntime.WarmupLimit"/>)으로
        /// 실패로 돌리고, 그때 재시도가 열린다.</para>
        /// </summary>
        void Watch(double now, in AiAddInputs see)
        {
            switch (see.Runtime)
            {
                case AiRuntimeState.Ready:
                    Step = AiAddStep.Done;
                    work = Work.None;
                    StalledFor = 0;
                    Progress = new AiPrepSnapshot { Phase = AiPrepPhase.Done, Progress = 1f };
                    return;
                case AiRuntimeState.Failed:
                    Fail(AiPrepFailure.StartFailed, Progress);
                    return;
                case AiRuntimeState.Loading:
                    work = Work.Warming;
                    StalledFor = 0;
                    Progress = Warming(installed, Spent(now), see.WarmupElapsed, see.WarmupExpected, Progress.Progress, Progress.ProgressAt);
                    return;
            }

            // 아직 안 떴다. 데우다가 내려갔다면(실행기가 껐다) 다시 기다리기 시작한다.
            if (work == Work.Warming) Wait(now);
            StalledFor = Math.Max(0, now - waitFrom);
            if (now - waitFrom >= StartGraceSeconds) { Fail(AiPrepFailure.StartTimeout, Progress); return; }
            // 아직 안 뜬 워커의 예상은 지난번 값이다 — 설치기가 이번 팩으로 잡아 둔 예상(installed)을 쓴다.
            Progress = Warming(installed, Spent(now), 0, 0, Progress.Progress, Progress.ProgressAt);
        }

        /// <summary>설치 전체에 쓴 시간 — 설치기가 잰 시간에, 설치가 끝난 뒤 흐른 시간을 이어 붙인다.</summary>
        double Spent(double now) => installed.Elapsed + Math.Max(0, now - installedAt);

        /// <summary>워커를 기다리기 시작한다. 막대는 설치에서 이어 간다.</summary>
        void Wait(double now)
        {
            work = Work.Starting;
            waitFrom = now;
            StalledFor = 0;
            Progress = Warming(installed, Spent(now), 0, 0, Progress.Progress, Progress.ProgressAt);
        }

        /// <summary>멈춘다. 창의 글자는 <paramref name="basis"/> 에 실패 사유를 얹어 만든다.</summary>
        void Fail(AiPrepFailure failure, AiPrepSnapshot basis = default)
        {
            Step = AiAddStep.Failed;
            Failure = failure;
            work = Work.None;
            StalledFor = 0;
            basis.Phase = AiPrepPhase.Failed;
            basis.Failure = failure;
            Progress = basis;
        }

        AiAddCommand Handoff(double now, AiAddCommand command)
        {
            handoff = true;
            handoffUntil = now + HandoffSeconds;
            return command;
        }

        AiAddCommand DropHandoff()
        {
            handoff = false;
            return AiAddCommand.None;
        }

        /// <summary>닫는다. AI 수는 건드리지 않는다 — 취소는 아무것도 넣지 않는다. 받던 것은 멈추라고 돌려준다.</summary>
        AiAddCommand Close()
        {
            bool installing = Step == AiAddStep.Working && (work == Work.Installing || work == Work.Restarting);
            Step = AiAddStep.Idle;
            work = Work.None;
            handoff = false;
            StalledFor = 0;
            return installing ? AiAddCommand.CancelInstall : AiAddCommand.None;
        }
    }
}
