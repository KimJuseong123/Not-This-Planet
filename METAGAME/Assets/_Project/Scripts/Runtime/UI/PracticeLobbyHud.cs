using Metagame.Core;
using Metagame.Gameplay;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>Contextual practice hints. Does not replace the tools' input or targeting.</summary>
    public sealed class PracticeLobbyHud : MonoBehaviour
    {
        GameSession session;
        Transform departure, resetConsole, batting, welding, anchor;
        float departureSeconds;
        readonly PracticeDeparture countdown = new();
        GUIStyle title, body, center;
        string hint;
        string error;
        bool inDeparture, atReset;
        public void Configure(GameSession owner, Transform exit, Transform reset,
            Transform bat, Transform weld, Transform pin, float seconds)
        {
            session = owner; departure = exit; resetConsole = reset;
            batting = bat; welding = weld; anchor = pin; departureSeconds = seconds;
            LobbyTransition.Ensure().Failed += OnFailed;
        }
        void OnFailed(string message) => error = message;
        void OnDestroy()
        {
            if (LobbyTransition.Instance != null) LobbyTransition.Instance.Failed -= OnFailed;
        }
        bool Near(Transform point, float radius) => point != null &&
            Vector3.Distance(session.LocalPlayer.Hips.position, point.position) < radius;

        void Update()
        {
            if (session == null || session.LocalPlayer == null) return;
            bool canPlay = session.CursorLocked && !session.InputSuppressed && !session.LocalPlayer.Downed
                && session.Exploration?.TabletOpen != true;
            inDeparture = Near(departure, 1.8f);
            atReset = Near(resetConsole, 1.7f);
            countdown.Tick(inDeparture, canPlay, Time.deltaTime, departureSeconds);
            if (countdown.Ready && !LobbyTransition.Ensure().IsBusy)
                LobbyTransition.Ensure().BeginSolo();
            if (atReset && canPlay && Input.GetKeyDown(Controls.TransportAction))
                LobbyTransition.Ensure().BeginPractice();

            if (inDeparture) hint = $"출발까지 {Mathf.CeilToInt(Mathf.Max(0f, departureSeconds - countdown.Elapsed))}초 · 밖으로 나오면 취소";
            else if (atReset) hint = $"[{Controls.TransportAction}] 연습실 전체 다시 준비 · 장비와 내 위치가 초기화돼요";
            else if (Near(anchor, 4f)) hint = $"고정못을 클릭한 채 [{Controls.MoveBack}]로 뒤로 당겨 보세요 · 뽑은 뒤 화물을 밀어 보세요";
            else if (Near(welding, 4f)) hint = "용접건을 집고 왼쪽 클릭으로 접합 · 오른쪽 클릭으로 용접 비드 절단";
            else if (Near(batting, 4f)) hint = "몽둥이를 집고 왼쪽 클릭 · 웃는 운석을 통통 쳐 보세요";
            else hint = $"자유롭게 연습해요 · {Controls.MoveForward}{Controls.MoveLeft}{Controls.MoveBack}{Controls.MoveRight} 이동 · {Controls.Jump} 점프 · 양쪽 클릭 잡기";
        }

        void OnGUI()
        {
            if (session == null || session.LocalPlayer == null || session.InputSuppressed
                || session.Exploration?.TabletOpen == true) return;
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            body ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            center ??= new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            var saved = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.14f, 0.78f);
            GUI.DrawTexture(new Rect(Screen.width*0.5f-150, 20, 300, 83), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width*0.5f-134, 28, 280, 30), "출발 전, 우주선 놀이터", title);
            GUI.Label(new Rect(Screen.width*0.5f-134, 61, 278, 36), "혼자 연습 · 준비되면 민트색 출발 구역으로", body);
            float w = Mathf.Min(860f, Screen.width - 40f);
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height - 96f, w, 72f);
            GUI.color = new Color(0.06f, 0.06f, 0.14f, 0.80f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.82f, 1f, 0.94f);
            GUI.Label(new Rect(rect.x + 12, rect.y + 7, w - 24, 38), error ?? hint, center);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12, rect.y + 42, w - 24, 24),
                $"[{Controls.ToolAction}] 도구 놓기 / 길게 눌러 던지기    ·    [{Controls.FreeCursor}] 메뉴", body);
            // Crosshair, clamp progress, tool charge and welding gauges belong to the shared GameHud.
            GUI.color = saved;
        }
    }
}
