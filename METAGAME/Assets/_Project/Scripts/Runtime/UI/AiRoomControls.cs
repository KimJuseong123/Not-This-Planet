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
                // **AI 수는 여기서 바꾸지 않는다** (S15P21B202-713). 방 목록 아래의 "AI 동료 추가" 와 줄마다의
                // × 가 맡는다(LobbyRoster). 예전의 켜기/끄기를 남겨 두면 수를 바꾸는 자리가 둘이 되어, 한쪽은
                // "빈 자리 전부" 를, 다른 쪽은 "하나 더" 를 보내는 날이 온다.
                //
                // 남은 것은 추가팩을 받는 버튼 하나다 — 팩이 없을 때의 임시 길이다. "AI 동료 추가" 를 누르면
                // 설치 창이 뜨는 흐름이 들어오면 그것이 이 자리를 대신한다.
                //
                // **버튼 하나가 흐름 전체를 맡는다** (S15P21B202-706). 예전에는 자리는 하나인데
                // 단계마다 다른 버튼이 나왔다 — 다운로드 / 다시 준비. 어느 쪽이 나올지 알려면
                // 방장이 지금 어느 단계인지를 먼저 알아야 했는데, 그것이 바로 화면에 없던 것이다.
                var runtime = AiLocalRuntime.Current;
                if (runtime != null)
                {
                    var spot = new Rect(area.x, area.y, buttonWidth, rowHeight);
                    if (runtime.Installer != null && runtime.Installer.Busy)
                    {
                        // "준비 취소" 는 바로 아래 준비 버튼의 이름과 같다 — 받기를 멈추려다 준비를 푼다(리뷰).
                        if (UiInteraction.Button(spot, "설치 취소", buttonStyle)) runtime.Installer.Cancel();
                    }
                    else if (NeedsPreparing(runtime))
                    {
                        if (UiInteraction.Button(spot, "AI 추가팩 준비", buttonStyle)) Prepare(runtime);
                    }
                }
            }
            else if (!lobby)
                GUI.Label(new Rect(area.x, area.y, area.width, rowHeight),
                    "AI 설정은 게임 시작 전 로비에서 변경할 수 있습니다", labelStyle);

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

            // 로비의 참가자에게는 이 한 줄뿐이다 (S15P21B202-713). 누가 AI 를 넣고 빼는지는 목록 아래의
            // 이유 줄("방장만 추가할 수 있습니다")이 이미 말한다 — 같은 말을 두 번 하지 않는다.
            // 옛 서버라서 막힌 것도 그 줄이 말한다.
            Rect statusArea = lobby && !canEdit
                ? new Rect(area.x, area.y, area.width, rowHeight)
                : new Rect(area.x, area.y + rowHeight + 3f, area.width, area.height - rowHeight);
            GUI.Label(statusArea, status, labelStyle);
        }

        /// <summary>
        /// 손댈 것이 남았는가 — 받을 것이 없고 이미 준비됐으면 버튼을 내보내지 않는다.
        ///
        /// <para><b>새 팩이 올라온 것도 "손댈 것" 이다</b> (S15P21B202-733). 전에는
        /// <c>HasPack</c> 만 봐서, 한 번 깐 사람에게는 새 팩이 영영 안 갔다.</para>
        /// </summary>
        /// <summary>
        /// 설치가 필요한지 — <b>0.5초에 한 번만 본다</b> (S15P21B202-713 리뷰). 이 함수는 IMGUI 이벤트마다
        /// (한 프레임에 두 번 이상) 불리는데, 그때마다 배포 정보 파일을 열어 해시하면 로비가 끊긴다.
        /// 설치가 끝난 뒤 버튼이 0.5초 늦게 바뀌는 것은 보이지 않는다.
        /// </summary>
        static bool NeedsPreparing(AiLocalRuntime runtime)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextPackCheck || runtime.PackRoot != checkedRoot)
            {
                wantsDownload = AiPackInstaller.WantsDownload(runtime.PackRoot);
                checkedRoot = runtime.PackRoot; nextPackCheck = now + PackCheckSeconds;
            }
            return wantsDownload
               || AiHostSettings.RuntimeState == AiRuntimeState.Failed
               || AiHostSettings.RuntimeState == AiRuntimeState.Missing;
        }

        public const float PackCheckSeconds = .5f;
        static float nextPackCheck;
        static bool wantsDownload;
        static string checkedRoot;

        /// <summary>
        /// 받기 · 설치 · 준비를 <b>한 번에</b> (S15P21B202-706).
        ///
        /// <para><b>흐름은 원래 하나였다.</b> <see cref="AiPackInstaller"/> 는 마지막에
        /// <c>Retry()</c> 를 불러 준비까지 이어 간다. 갈려 있던 것은 <b>버튼뿐</b>이었고,
        /// 그래서 방장이 "지금 무엇을 눌러야 하는가" 를 스스로 판단해야 했다.</para>
        /// </summary>
        static void Prepare(AiLocalRuntime runtime)
        {
            // 안 깔렸거나 **깔린 것이 옛 팩이면** 받는다 (S15P21B202-733).
            if (AiPackInstaller.WantsDownload(runtime.PackRoot)) { runtime.Installer?.Begin(); return; }
            // 받을 것이 없으면 준비만 다시 한다. "이미 최신" 이라고 따로 말하지 않는다
            // (S15P21B202-713) — Draw 의 상태 줄은 설치기 글자를 먼저 보이므로, 그 말이 데우는
            // 내내 서서 준비 상태를 가렸다. 할 말은 준비 상태가 한다.
            runtime.Retry();
        }
    }
}
