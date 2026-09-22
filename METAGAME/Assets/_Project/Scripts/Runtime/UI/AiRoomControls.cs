using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    public static class AiRoomControls
    {
        // 화면 입력을 처리할 뿐 Ready 상태를 생성하지 않는다. 변경 권한은 설정 API에서도 검증한다.
        public static void Draw(Rect area, GUIStyle buttonStyle, GUIStyle labelStyle, bool lobby)
        {
            bool canEdit = lobby && AiHostSettings.CanConfigure;
            float buttonWidth = (area.width - 8f) * 0.5f;
            float rowHeight = area.height * 0.55f;
            if (canEdit)
            {
                if (UiInteraction.Button(new Rect(area.x, area.y, buttonWidth, rowHeight),
                    AiHostSettings.Requested ? "AI 동료 사용: 켜짐" : "AI 동료 사용: 꺼짐", buttonStyle))
                    AiHostSettings.TrySetRequested(!AiHostSettings.Requested);
                var runtime = AiLocalRuntime.Current;
                if (runtime != null && runtime.Installer != null && runtime.Installer.Busy)
                {
                    if (UiInteraction.Button(new Rect(area.x + buttonWidth + 8f, area.y, buttonWidth, rowHeight),
                        "설치 취소", buttonStyle)) runtime.Installer.Cancel();
                }
                else if (runtime != null && !AiLocalRuntime.HasPack(runtime.PackRoot))
                {
                    if (UiInteraction.Button(new Rect(area.x + buttonWidth + 8f, area.y, buttonWidth, rowHeight),
                        "AI 추가팩 다운로드", buttonStyle)) runtime.Installer.Begin();
                }
                else if (AiLocalRuntime.Current != null && (AiHostSettings.RuntimeState == AiRuntimeState.Failed || AiHostSettings.RuntimeState == AiRuntimeState.Missing))
                {
                    if (UiInteraction.Button(new Rect(area.x + buttonWidth + 8f, area.y, buttonWidth, rowHeight),
                        "추가팩 다시 준비", buttonStyle)) AiLocalRuntime.Current.Retry();
                }
            }
            else
                GUI.Label(new Rect(area.x, area.y, area.width, rowHeight),
                    lobby ? "AI 설정은 대기 중인 방장이 관리합니다" : "AI 설정은 게임 시작 전 로비에서 변경할 수 있습니다", labelStyle);

            string status;
            if (lobby && !canEdit) status = "게임 입장 후 방장의 AI 상태가 표시됩니다";
            else if (canEdit || Mirror.NetworkServer.active) status = AiHostPolicy.Label(AiHostSettings.LocalState);
            else status = AiHostPolicy.Label(WorldSync.Current != null ? WorldSync.Current.RoomAiState : AiRoomState.Unknown);
            if (canEdit && AiHostSettings.Requested && AiLocalRuntime.Current != null)
                status = AiLocalRuntime.Current.Message;
            if (canEdit && AiLocalRuntime.Current?.Installer != null && !string.IsNullOrEmpty(AiLocalRuntime.Current.Installer.Message))
            {
                var installer = AiLocalRuntime.Current.Installer;
                if (installer.Busy || AiHostSettings.RuntimeState != AiRuntimeState.Ready) status = installer.Message;
            }
            GUI.Label(new Rect(area.x, area.y + rowHeight + 3f, area.width, area.height - rowHeight), status, labelStyle);
        }
    }
}
