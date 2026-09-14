using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using Metagame.UI;
using Mirror;
using UnityEngine.SceneManagement;
#endif

namespace Metagame.Bootstrap
{
    /// <summary>개발 빌드/에디터 전용 F11 시점 전환과 F12 엔딩 테스트 설정 및 입력.</summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class DevManager : MonoBehaviour
    {
        [Header("개발자 테스트 (에디터 / Development Build 전용)")]
        [Tooltip("F11로 로컬 플레이어의 1인칭 / 3인칭 시점을 전환합니다.")]
        [SerializeField] bool enableF11Perspective = true;
        [Tooltip("F12로 엔딩 테스트 확인창을 엽니다. 멀티플레이에서는 호스트만 실행할 수 있습니다.")]
        [SerializeField] bool enableF12Ending = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GameSession session;
        DemoEnding ending;
        GamePauseMenu pause;
        bool networked, savedInputSuppressed, savedCursorLocked, savedPauseEnabled, restorePending;
        Font font;
        GUIStyle titleStyle, buttonStyle, hintStyle;
        string error;
        public bool IsOpen { get; private set; }
        public bool CanConfirm => isActiveAndEnabled && enableF12Ending
            && session != null && ending != null && !ending.Active
            && !session.Cleared && (!networked || NetworkServer.active)
            && !HostMigration.Pending && !HostMigration.Taking && !HostMigration.Settling;

        public void Configure(GameSession target, DemoEnding targetEnding, GamePauseMenu menu, bool isNetworked)
        { session = target; ending = targetEnding; pause = menu; networked = isNetworked; }

        void Update()
        {
            UpdateEndingShortcut();
            // 시점은 개인 화면의 상태다. InputFrame/Command에 넣지 않는다.
            if (enableF11Perspective && session != null && session.isActiveAndEnabled
                && !session.EndingActive && !session.InputSuppressed && !session.ScriptedInput
                && session.LocalPlayer != null && session.LocalPlayer.Cam != null
                && Input.GetKeyDown(KeyCode.F11))
                session.LocalPlayer.Cam.ToggleDebugPerspective();
        }

        void UpdateEndingShortcut()
        {
            if (!enableF12Ending) { if (IsOpen) Cancel(); return; }
            if (session == null || ending == null) return;
            if (ending.Active || (EndingSceneFlow.Instance != null && EndingSceneFlow.Instance.IsTransferring))
            { if (IsOpen) Cancel(); return; }
            if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F12))) Cancel();
            else if (!IsOpen && Input.GetKeyDown(KeyCode.F12)) OpenConfirmation();
        }

        public bool OpenConfirmation()
        {
            if (!isActiveAndEnabled || !enableF12Ending
                || IsOpen || restorePending || session == null || ending == null || ending.Active
                || !StageCatalog.IsStageScene(SceneManager.GetActiveScene().name)   // 맵이 둘이 되어도 맵 씬이면 연다(2맵 준비)
                || session.InputSuppressed || (pause != null && pause.IsOpen)
                || LobbyTransition.Instance != null && LobbyTransition.Instance.IsBusy) return false;
            savedInputSuppressed = session.InputSuppressed;
            savedCursorLocked = session.CursorLocked;
            savedPauseEnabled = pause != null && pause.enabled;
            if (pause != null) pause.enabled = false;
            session.SetInputSuppressed(true);
            session.SetCursor(false);
            error = null;
            IsOpen = true;
            return true;
        }

        public bool Confirm()
        {
            if (!IsOpen || !CanConfirm) return false;
            if (SceneUtility.GetBuildIndexByScenePath(EndingSceneFlow.ScenePath) < 0)
            { error = "빌드 목록에 Stage1_Ending 씬이 없습니다."; return false; }
            if (!ending.DebugAssembleAndComplete())
            { error = "파츠 4개와 참가자 상태를 확인해 주세요. 맵 복구 중에는 실행할 수 없습니다."; return false; }
            Cancel();
            return true;
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            restorePending = true;
        }

        // Esc 취소 입력이 같은 프레임의 일시 메뉴/게임 입력에 재사용되지 않게 늦게 복구한다.
        void LateUpdate() { if (restorePending) RestoreInput(); }

        void RestoreInput()
        {
            restorePending = false;
            if (pause != null) pause.enabled = savedPauseEnabled;
            if (session == null || session.EndingActive) return;
            session.SetInputSuppressed(savedInputSuppressed);
            session.SetCursor(savedCursorLocked);
        }

        void OnDisable()
        {
            if (!IsOpen && !restorePending) return;
            IsOpen = false;
            RestoreInput();
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans CJK KR", "Apple SD Gothic Neo", "Arial" }, 26);
                titleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22 };
                hintStyle = new GUIStyle(titleStyle) { fontSize = 16 };
            }
            using (new UiScale.Scope(UiScale.Value))
            {
                var oldDepth = GUI.depth;
                var oldColor = GUI.color;
                GUI.depth = -5000;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(0f, 0f, UiScale.Width, UiScale.Height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                float width = Mathf.Min(560f, UiScale.Width - 24f);
                GUI.ModalWindow(0x454E44, new Rect((UiScale.Width - width) * 0.5f,
                    (UiScale.Height - 250f) * 0.5f, width, 250f), DrawDialog, GUIContent.none);
                GUI.depth = oldDepth;
                GUI.color = oldColor;
            }
        }

        void DrawDialog(int id)
        {
            float width = Mathf.Min(560f, UiScale.Width - 24f);
            GUI.Label(new Rect(12f, 22f, width - 24f, 60f), "엔딩 씬으로 넘어가겠습니까?", titleStyle);
            var hint = !string.IsNullOrEmpty(error) ? error : !CanConfirm
                ? "멀티 테스트는 호스트만 실행할 수 있습니다. 호스트 이전 중에는 기다려 주세요."
                : "개발자 테스트: 파츠를 조립하고 현재 플레이를 종료합니다.\n멀티에서는 모든 참가자가 함께 엔딩으로 이동합니다.";
            GUI.Label(new Rect(18f, 86f, width - 36f, 70f), hint, hintStyle);
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && CanConfirm;
            if (UiInteraction.Button(new Rect(width * 0.5f - 154f, 177f, 140f, 48f), "예", buttonStyle)) Confirm();
            GUI.enabled = wasEnabled;
            if (UiInteraction.Button(new Rect(width * 0.5f + 14f, 177f, 140f, 48f), "아니오", buttonStyle)) Cancel();
        }

        void OnDestroy() { if (font != null) Destroy(font); }
#endif
    }
}
