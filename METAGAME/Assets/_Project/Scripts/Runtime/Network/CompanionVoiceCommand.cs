using Metagame.AICompanion;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// Stage1 에서 <b>V 를 누르는 동안 말하면</b> 가장 가까운 AI 동료가 그 일을 한다.
    ///
    /// <para><b>왜 따로 만들었나.</b> 같은 일을 하는 <c>AICompanionTrial</c> 은 AI 실험 씬
    /// 전용이고 진단 HUD·F6 운석 같은 실험 도구가 함께 붙어 있다. 출시 경로에는 그것들을
    /// 들이지 않는다.</para>
    ///
    /// <para><b>방장만 명령한다.</b> 여러 사람의 명령을 중재하는 규칙이 아직 없다
    /// (CLAUDE.md 설계 경계). 참가자가 눌러도 아무 일도 일어나지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionVoiceCommand : MonoBehaviour
    {
        /// <summary>누르는 동안 듣는다. <c>Controls</c> 에 아직 없는 키다.</summary>
        const KeyCode TalkKey = KeyCode.V;

        /// <summary>
        /// 명령 기록을 켜고 끄는 키.
        ///
        /// <para>F1~F12 가 이미 전부 쓰이고 있어(휠·춤·개발자 기능) 남는 키로 잡았다.</para>
        /// </summary>
        const KeyCode LogKey = KeyCode.Backslash;

        /// <summary>기록에 남겨 두는 줄 수. 넘으면 오래된 것부터 지운다.</summary>
        const int LogLines = 12;

        GameSession _session;
        CompanionVoiceService _voice;
        readonly System.Collections.Generic.List<string> _log =
            new System.Collections.Generic.List<string>();
        bool _logOpen;

        void Awake()
        {
            _voice = gameObject.AddComponent<CompanionVoiceService>();
        }

        void Update()
        {
            if (_session == null) _session = FindAnyObjectByType<GameSession>();

            // 서버가 아니면 시킬 상대가 없다 — 자리와 두뇌는 서버에만 있다.
            if (!NetworkServer.active || _session == null || _session.LocalPlayer == null) return;

            if (Input.GetKeyDown(LogKey)) _logOpen = !_logOpen;

            // 글자를 치는 중이거나 멈춘 화면에서는 듣지 않는다.
            if (Time.timeScale == 0) return;

            if (Input.GetKeyDown(TalkKey)) BeginTalking();

            if (_voice.Recording && (Input.GetKeyUp(TalkKey) || _voice.RecordingTooLong))
                _voice.FinishRecording(OnHeard);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TickTextCommand();
#endif
        }

        void BeginTalking()
        {
            if (!_voice.StartRecording(out string problem)) { Show(problem); return; }
            Show("● 듣는 중");
        }

        /// <summary>서버가 들은 말과 고른 행동을 받는다.</summary>
        void OnHeard(string text, string action, string error)
        {
            if (error != null) { Show(error); return; }

            Transform speaker = _session != null && _session.LocalPlayer != null
                ? _session.LocalPlayer.Hips.transform : null;

            if (TeamSeats.Server == null || speaker == null)
            { Show("명령을 받을 동료가 없어요"); return; }

            if (string.IsNullOrWhiteSpace(text))
            { Show("잘 못 들었어요 · 다시 말해 주세요"); return; }

            TeamSeats.Server.TryCommandNearest(speaker, action, out string feedback);

            // 들린 말과 고른 행동을 같이 남긴다. 셋을 나란히 봐야 어디서 틀렸는지 갈린다 —
            // 잘못 들은 것인지, 엉뚱한 행동을 고른 것인지, 행동이 거절된 것인지.
            Show($"\"{text}\" → {action} · {feedback}");
        }

        void Show(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            _log.Add($"[{System.DateTime.Now:HH:mm:ss}] {line}");
            if (_log.Count > LogLines) _log.RemoveAt(0);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// <b>개발용 글자 명령(F7).</b> 음성 인식을 건너뛰고 문장을 그대로 보낸다.
        ///
        /// <para>STT 가 틀렸을 때 <b>인식 실패인지 행동 버그인지</b> 가릴 유일한 수단이다.
        /// 출시 빌드에서는 이 블록이 통째로 컴파일에서 빠진다 — <c>DevManager</c> 의
        /// F9~F12 와 같은 방식이다.</para>
        /// </summary>
        bool _typing;
        string _typed = string.Empty;

        void TickTextCommand()
        {
            if (!Input.GetKeyDown(KeyCode.F7)) return;
            _typing = !_typing;
            if (_typing) _typed = string.Empty;
        }

        void DrawTextCommand()
        {
            if (!_typing) return;

            var box = new Rect(20, Screen.height - 90, 560, 66);
            GUI.Box(box, "개발용 · 문장으로 명령 (F7 로 닫기, Enter 로 보냄)");

            GUI.SetNextControlName("aiText");
            _typed = GUI.TextField(new Rect(box.x + 12, box.y + 30, box.width - 24, 24), _typed, 120);
            GUI.FocusControl("aiText");

            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                _voice.SendText(_typed, OnHeard);
                _typing = false;
                Event.current.Use();
            }
        }
#endif

        void OnGUI()
        {
            DrawLog();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DrawTextCommand();
#endif
        }

        /// <summary>
        /// 주고받은 명령 기록. <b>들린 말 · 고른 행동 · 결과</b>를 나란히 놓는다.
        ///
        /// <para>셋을 같이 봐야 어디서 틀렸는지 갈린다 — 잘못 들은 것인지, 엉뚱한 행동을
        /// 고른 것인지, 행동이 거절된 것인지.</para>
        /// </summary>
        void DrawLog()
        {
            // 녹음 중에는 접어 두었어도 알려 준다. 말하는 동안 아무 표시가 없으면
            // 눌린 것인지 알 수 없다.
            if (_voice != null && _voice.Recording)
            {
                var dot = new Rect(20, 20, 200, 24);
                GUI.Box(dot, "● 듣는 중");
            }

            if (!_logOpen || _log.Count == 0) return;

            float height = 30 + _log.Count * 20;
            var box = new Rect(20, 54, 640, height);
            GUI.Box(box, $"AI 명령 기록  (\\ 로 닫기)");

            for (int i = 0; i < _log.Count; i++)
                GUI.Label(new Rect(box.x + 12, box.y + 26 + i * 20, box.width - 24, 20), _log[i]);
        }
    }
}
