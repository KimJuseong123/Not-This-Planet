using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    public static class AiRoomControls
    {
        // 화면 입력을 처리할 뿐 Ready 상태를 생성하지 않는다. 변경 권한은 설정 API에서도 검증한다.
        //
        // **버튼이 없다** (S15P21B202-713). AI 수는 방 목록 아래의 "AI 동료 추가" 와 줄마다의 × 가 맡고,
        // 받기·설치·준비는 "AI 동료 추가" 를 누르면 뜨는 설치 창(AiInstallPopup)이 맡는다. 예전 이 자리의
        // "AI 추가팩 준비" / "설치 취소" 는 그 창이 들어오기 전의 임시 길이었다 — 남겨 두면 준비로
        // 들어가는 입구가 둘이 되고, 둘은 서로 다른 단계를 보고 다른 버튼을 낸다(S15P21B202-706).
        // 여기는 상태 한 줄만 그린다.
        public static void Draw(Rect area, GUIStyle labelStyle, bool lobby)
        {
            bool canEdit = lobby && AiHostSettings.CanConfigure;
            float rowHeight = area.height * 0.55f;
            if (!lobby)
                GUI.Label(new Rect(area.x, area.y, area.width, rowHeight),
                    "AI 설정은 게임 시작 전 로비에서 변경할 수 있습니다", labelStyle);

            string status;
            if (lobby && !canEdit) status = "게임 입장 후 방장의 AI 상태가 표시됩니다";
            else if (canEdit) status = HostStatus();
            else if (Mirror.NetworkServer.active) status = AiHostPolicy.Label(AiHostSettings.LocalState);
            else status = AiHostPolicy.Label(WorldSync.Current != null ? WorldSync.Current.RoomAiState : AiRoomState.Unknown);

            // 로비에서는 이 한 줄뿐이다 (S15P21B202-713). 누가 AI 를 넣고 빼는지는 목록 아래의 이유 줄
            // ("방장만 추가할 수 있습니다")이 이미 말한다 — 같은 말을 두 번 하지 않는다.
            Rect statusArea = lobby
                ? new Rect(area.x, area.y, area.width, rowHeight)
                : new Rect(area.x, area.y + rowHeight + 3f, area.width, area.height - rowHeight);
            GUI.Label(statusArea, status, labelStyle);
        }

        /// <summary>
        /// 대기 중인 방장의 상태 줄 — 받거나 데우는 중이면 <b>진행·몇 %·남은 시간</b>, 아니면 방의 AI 상태
        /// (<see cref="AiAddFlow.HostStatus"/>).
        ///
        /// <para>전에는 실행기와 설치기의 글자(<c>Message</c>)를 그대로 보였다. 그 글자는 로그용이라
        /// "모델 데우는 중" · "LLM 판단" · 파일 경로가 화면에 샜고, 설치기 글자는 설치가 끝난 순간에 멈춰
        /// 워커의 실패를 가렸다.</para>
        /// </summary>
        static string HostStatus()
        {
            AiLocalRuntime runtime = AiLocalRuntime.Current;
            bool installing = runtime != null && runtime.Installer != null && runtime.Installer.Busy;
            AiPrepSnapshot progress = runtime != null ? HostProgress(runtime) : default;
            return AiAddFlow.HostStatus(AiHostSettings.LocalState, installing, AiHostSettings.RuntimeState, progress);
        }

        /// <summary>
        /// 방장 화면의 AI 줄에 붙일 남은 시간. 워커가 데우는 중일 때만 있다 — 아니면 null
        /// (<see cref="LobbyRoster.Rows"/> 가 "준비 중" 뒤에 붙인다).
        /// </summary>
        public static string LoadingText()
        {
            AiLocalRuntime runtime = AiLocalRuntime.Current;
            if (runtime == null || runtime.State != AiRuntimeState.Loading) return null;
            return AiAddFlow.RemainingText(HostProgress(runtime));
        }

        // 데우는 동안의 막대는 줄지 않아야 한다 — 예상을 넘기면 셈이 "모름" 이 되어 0 으로 떨어진다.
        // 이 워커(데우기를 시작한 시각)에서 마지막으로 보인 값을 들고 있다.
        static float shownProgress;
        static double shownAt = -1;
        static float shownFor = -1f;

        /// <summary>
        /// 지금의 준비 상태. 받는 중이면 설치기의 것, 아니면 <b>데우기만의 상태</b>를 만든다 — 팩이 이미 있어
        /// 창 없이 넣은 경우에도 남은 시간을 말할 수 있다.
        /// </summary>
        static AiPrepSnapshot HostProgress(AiLocalRuntime runtime)
        {
            if (runtime.Installer != null && runtime.Installer.Busy) return runtime.Installer.Snapshot;
            float began = runtime.WarmupBegan;
            if (began != shownFor) { shownFor = began; shownProgress = 0f; shownAt = -1; }
            double elapsed = runtime.State == AiRuntimeState.Loading && began >= 0f
                ? Mathf.Max(0f, Time.realtimeSinceStartup - began) : 0;
            AiPrepSnapshot s = AiAddFlow.Warming(default, elapsed, elapsed, runtime.WarmupExpected, shownProgress, shownAt);
            shownProgress = s.Progress; shownAt = s.ProgressAt;
            return s;
        }
    }
}
