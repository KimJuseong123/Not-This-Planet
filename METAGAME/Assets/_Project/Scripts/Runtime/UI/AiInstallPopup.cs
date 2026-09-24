using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// "AI 동료 추가" 를 눌렀을 때 뜨는 설치 창 (S15P21B202-713). <see cref="LobbyHud"/> 가 들고 그린다.
    ///
    /// <para><b>규칙은 여기 없다.</b> 무엇을 보이고 무엇을 누를 수 있는지는 <see cref="AiAddFlow"/> 가
    /// 정한다. 이 창은 흐름이 시킨 일(받기 시작·취소·워커 다시 띄우기·AI 넣기)을 하고, 매 프레임 본 것을
    /// 흐름에 알리고, 흐름의 글자와 버튼을 그릴 뿐이다.</para>
    ///
    /// <para><b>준비로 들어가는 입구는 이것 하나다</b> (S15P21B202-706). 받기(<c>Installer.Begin</c>)와
    /// 다시 띄우기(<c>Retry</c>)를 부르는 화면은 이 창뿐이다 — 예전 방 화면의 "AI 추가팩 준비" 버튼은
    /// 이 창이 대신한다.</para>
    /// </summary>
    public sealed class AiInstallPopup
    {
        /// <summary>이 PC 의 팩을 본 결과. 작업 스레드에서 만든다.</summary>
        readonly struct PackFacts
        {
            public readonly bool HasPack, Current;

            public PackFacts(bool hasPack, bool current)
            {
                HasPack = hasPack;
                Current = current;
            }
        }

        readonly AiAddFlow flow = new AiAddFlow();

        /// <summary>
        /// AI 를 하나 넣는다. <b>누른 그 순간의 방으로 다시 확인한다</b> — 설치하는 몇 분 사이 사람이 들어와
        /// 자리가 찼을 수 있다. 넣지 못했으면 그 이유를, 넣었으면 null 을 돌려준다.
        /// </summary>
        readonly Func<string> add;

        Task<PackFacts> check;
        bool checkedSource;

        public AiInstallPopup(Func<string> add) => this.add = add;

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>흐름이 열려 있다(확인 중 포함). 그동안 방 화면은 추가·다시 시도를 막는다.</summary>
        public bool Busy => flow.Step != AiAddStep.Idle;

        /// <summary>창이 보인다. 보이는 동안 방 화면의 버튼은 모두 막힌다 — IMGUI 는 먼저 그린 버튼이 누름을 먼저 받는다.</summary>
        public bool Visible => flow.Visible(Now);

        /// <summary>
        /// "AI 동료 추가"(<paramref name="adds"/> 참) 또는 "다시 시도"(거짓)를 눌렀다. 이 PC 의 팩부터 본다.
        ///
        /// <para><b>확인은 작업 스레드에서 한다.</b> 배포 정보 파일을 해시하므로 디스크가 느리면 수백 ms 가
        /// 걸린다. 그동안 화면이 서 있으면 안 된다. <see cref="AiAddFlow.SlowCheckSeconds"/> 가 지나도
        /// 안 끝나면 창을 먼저 연다.</para>
        /// </summary>
        public void Begin(bool adds)
        {
            if (!flow.Begin(Now, adds)) return;
            StartCheck();
        }

        /// <summary>
        /// 확인을 시작한다. <b>유니티가 주는 값은 여기(메인 스레드)에서 잡는다</b> — 설치 폴더(<c>PackRoot</c>)와
        /// 배포 주소 파일(<c>streamingAssetsPath</c>). 작업 스레드는 파일만 읽는다.
        /// </summary>
        void StartCheck()
        {
            AiLocalRuntime runtime = AiLocalRuntime.Current;
            if (runtime == null)
            {
                // 이 빌드에는 실행기가 없다(Windows 가 아니다). 팩이 있어도 띄울 수 없다.
                checkedSource = false;
                check = Task.FromResult(new PackFacts(false, false));
                return;
            }
            string root = runtime.PackRoot;
            AiPackSource source = AiPackInstaller.LoadSource();
            checkedSource = AiPackValidation.Source(source);
            check = Task.Run(() => Facts(root, source));
        }

        /// <summary>작업 스레드에서 도는 부분. 유니티 API 를 부르지 않는다.</summary>
        static PackFacts Facts(string root, AiPackSource source)
        {
            return new PackFacts(AiLocalRuntime.HasPack(root), AiPackInstaller.InstalledIsCurrent(root, source));
        }

        /// <summary>
        /// 한 프레임. 방 화면이 떠 있는지(<paramref name="shown"/>)와 이 PC 의 상태를 흐름에 알리고, 흐름이
        /// 시킨 일을 한다. 로비 메시지 줄에 띄울 말(AI 를 넣지 못한 이유)이 있으면 돌려준다.
        /// </summary>
        public string Tick(bool shown)
        {
            double now = Now;
            AiLocalRuntime runtime = AiLocalRuntime.Current;
            AiPackInstaller installer = runtime != null ? runtime.Installer : null;
            var see = new AiAddInputs
            {
                HostOk = shown && AiHostSettings.CanConfigure,
                InstallerBusy = installer != null && installer.Busy,
                Installer = installer != null ? installer.Snapshot : default,
                Runtime = AiHostSettings.RuntimeState,
                WarmupElapsed = runtime != null && runtime.State == AiRuntimeState.Loading && runtime.WarmupBegan >= 0f
                    ? Math.Max(0, Time.realtimeSinceStartup - runtime.WarmupBegan) : 0,
                WarmupExpected = runtime != null ? runtime.WarmupExpected : 0,
                Requested = AiHostSettings.RequestedCount,
            };

            // 방을 떠났거나 방장이 아니면 여기서 닫힌다 — 그 뒤에 확인 결과로 AI 를 넣지 않는다.
            string said = Apply(flow.Tick(now, see), runtime);
            if (flow.Step == AiAddStep.Checking && check != null && check.IsCompleted)
                said = Apply(flow.Checked(Decide(), now), runtime) ?? said;

            AiHostSettings.TrySetPreparing(flow.WantsRuntime);
            return said;
        }

        /// <summary>확인이 끝났다. 실패했으면 <b>"최신이 아니다" 로 본다</b> — 헛걸음은 설치를 한 번 더 묻는 것이다.</summary>
        AiAddCheck Decide()
        {
            PackFacts facts = default;
            if (check.Status == TaskStatus.RanToCompletion) facts = check.Result;
            else Debug.LogWarning("[AI] 팩 확인 실패 · " + check.Exception?.GetBaseException());
            check = null;
            return AiAddFlow.Decide(facts.HasPack, checkedSource, facts.Current, AiHostSettings.RuntimeState);
        }

        /// <summary>화면이 사라진다(방 화면을 떠남·씬 전환). 받던 것은 멈추고 워커 붙잡기도 내린다.</summary>
        public void Cancel()
        {
            AiLocalRuntime runtime = AiLocalRuntime.Current;
            Apply(flow.Cancel(), runtime);
            check = null;
            AiHostSettings.TrySetPreparing(false);
        }

        /// <summary>흐름이 시킨 일을 한다. 흐름은 스스로 아무것도 부르지 않는다.</summary>
        string Apply(AiAddCommand command, AiLocalRuntime runtime)
        {
            switch (command)
            {
                case AiAddCommand.Add:
                    return Add();
                case AiAddCommand.AddAndRetry:
                {
                    // 팩은 최신인데 워커가 실패해 있다 — 넣기만 하면 AI 가 판에 들어오지 않는다.
                    string why = Add();
                    runtime?.Retry();
                    return why;
                }
                case AiAddCommand.RetryRuntime:
                    runtime?.Retry();
                    return null;
                case AiAddCommand.Install:
                {
                    AiInstallStart started = runtime != null && runtime.Installer != null
                        ? runtime.Installer.Begin() : AiInstallStart.NoRuntime;
                    return Apply(flow.Started(started, Now), runtime);
                }
                case AiAddCommand.CancelInstall:
                    // 받아 둔 파일은 남는다 — 다음에 누르면 확인된 것은 다시 받지 않는다.
                    runtime?.Installer?.Cancel();
                    return null;
                default:
                    return null;
            }
        }

        string Add()
        {
            string why = add != null ? add() : null;
            if (why != null) flow.AddRefused();
            return why;
        }

        /// <summary>
        /// 창을 그린다 — 화면 전체를 어둡게 덮고 가운데에 제목·진행·버튼. 방 화면을 <b>다 그린 뒤에</b>
        /// 불러야 위에 그려진다. 버튼을 누르면 흐름에 알리고, 로비에 띄울 말이 있으면 돌려준다.
        /// </summary>
        public string Draw(GUIStyle titleStyle, GUIStyle bodyStyle, GUIStyle buttonStyle, GUIStyle primaryStyle)
        {
            double now = Now;
            if (!flow.Visible(now)) return null;

            UiTheme.DrawDimmer(new Rect(0f, 0f, UiScale.Width, UiScale.Height));
            float width = Mathf.Min(620f, UiScale.Width - 40f);
            const float height = 270f;
            var panel = new Rect((UiScale.Width - width) * 0.5f, (UiScale.Height - height) * 0.5f, width, height);
            UiTheme.DrawPanel(panel);

            float inner = width - 64f;
            Label(new Rect(panel.x + 32f, panel.y + 28f, inner, 64f), AiAddFlow.Title(flow.Step), titleStyle, UiTheme.Text);

            string body = flow.Body;
            if (body != null)
                Label(new Rect(panel.x + 32f, panel.y + 100f, inner, 52f), body, bodyStyle,
                    flow.Step == AiAddStep.Failed ? new Color(1f, 0.72f, 0.62f) : UiTheme.MutedText);

            if (flow.Step == AiAddStep.Working)
            {
                var bar = new Rect(panel.x + 32f, panel.y + 160f, inner, 10f);
                UiTheme.DrawSolid(bar, UiTheme.Surface);
                float fill = Mathf.Clamp01(flow.Progress.Progress);
                if (fill > 0f) UiTheme.DrawSolid(new Rect(bar.x, bar.y, bar.width * fill, bar.height), UiTheme.Cyan);
            }

            IReadOnlyList<AiAddButton> buttons = AiAddFlow.Buttons(flow.Step, flow.Failure);
            const float buttonWidth = 132f, buttonHeight = 44f, gap = 12f;
            float x = panel.xMax - 32f - buttons.Count * buttonWidth - (buttons.Count - 1) * gap;
            float y = panel.yMax - 28f - buttonHeight;
            AiAddButton? pressed = null;
            for (int i = 0; i < buttons.Count; i++)
            {
                AiAddButton button = buttons[i];
                bool before = GUI.enabled;
                GUI.enabled = before && AiAddFlow.Enabled(button, flow.Step, flow.StalledFor);
                bool primary = button == AiAddButton.Confirm || button == AiAddButton.Finish;
                if (UiInteraction.Button(new Rect(x + i * (buttonWidth + gap), y, buttonWidth, buttonHeight),
                        AiAddFlow.Label(button), primary ? primaryStyle : buttonStyle))
                    pressed = button;
                GUI.enabled = before;
            }

            if (!pressed.HasValue) return null;
            string said = Apply(flow.Press(pressed.Value, now), AiLocalRuntime.Current);
            AiHostSettings.TrySetPreparing(flow.WantsRuntime);
            return said;
        }

        static void Label(Rect area, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(area, text ?? string.Empty, style);
            GUI.color = previous;
        }
    }
}
