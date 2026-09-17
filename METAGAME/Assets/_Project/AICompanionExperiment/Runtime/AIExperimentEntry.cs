using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.AIExperiment
{
    public sealed class AIExperimentEntry : MonoBehaviour
    {
        [SerializeField] AIExperimentManager _manager;
        public void Configure(AIExperimentManager manager) => _manager = manager;
        static bool RoomSceneReady => Application.CanStreamedLevelBeLoaded(AIExperimentManager.RoomScene);

        void Start()
        {
            Application.runInBackground = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            string[] args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-aiHost") >= 0) _manager.StartHost();
            else if (System.Array.IndexOf(args, "-aiClient") >= 0) _manager.StartClient();
            else if (System.Array.IndexOf(args, "-aiSolo") >= 0) SceneManager.LoadScene(AIExperimentManager.RoomScene);
        }

        void OnGUI()
        {
            GUI.skin.label.fontSize = 20;
            GUI.skin.button.fontSize = 20;
            GUILayout.BeginArea(new Rect(35, 35, 650, 440), GUI.skin.box);
            GUILayout.Label("AI 동료 · 로비 실험");
            GUILayout.Label("v4 · 이동/못 작업 + 도구 줍기·내려놓기·공 타격·운석 방어");
            GUILayout.Space(18);
            if (!RoomSceneReady)
            {
                // 실험 씬은 각자 PC 에서만 빌드 목록에 등록한다(출시 빌드 오염 방지).
                // 등록 전에는 로드가 조용히 실패해 검은 화면이 되므로 여기서 먼저 막는다.
                GUILayout.Label("실험 씬이 이 PC 의 빌드 목록에 없습니다.");
                GUILayout.Label("메뉴: Tools > Not This Planet > AI 실험 >\n실험 씬을 이 PC 빌드 목록에 등록");
            }
            else if (!NetworkServer.active && !NetworkClient.active)
            {
                if (GUILayout.Button("혼자 테스트", GUILayout.Height(50))) SceneManager.LoadScene(AIExperimentManager.RoomScene);
                if (GUILayout.Button("A 창: 로컬 방장 시작", GUILayout.Height(50))) _manager.StartHost();
                if (GUILayout.Button("B 창: 같은 PC 방에 참가", GUILayout.Height(50))) _manager.StartClient();
            }
            else GUILayout.Label("로비에 연결하는 중…");
            GUILayout.Space(15);
            GUILayout.Label("V를 누르는 동안 말하기 / F8 즉시 중단\n음성은 방장만 사용. 다른 창은 동료의 움직임을 확인합니다.\n음성 서버: 127.0.0.1:8765 · 외부 API/과금 없음\n이 테스트는 같은 PC 직접 연결 전용입니다.");
            GUILayout.EndArea();
        }
    }
}
