using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// V(주변 동료) 또는 1~4(그 색 동료)를 누르는 동안 명령을 녹음한다 (S15P21B202-738).
    /// </summary>
    public sealed class AiVoiceControls : MonoBehaviour
    {
        readonly List<float> samples = new List<float>(AiVoicePacket.Rate * AiVoicePacket.Seconds);
        readonly List<int> aiSlots = new List<int>(3);
        bool recording;
        /// <summary>
        /// 누르고 있는 키가 고른 대상 (S15P21B202-738). <b>놓았는지는 이 키로 본다</b> — V 로만
        /// 보면 1 을 누르고 말하는 동안 "놓았다" 로 읽혀 첫 프레임에 보내 버린다.
        /// </summary>
        int recordingTarget = AiTalkKeys.Anyone;
        string notice;
        float started, noticeUntil, resultSeen;
        GameSession recordingSession;
        NetPlayer recordingOwner;
        ProximityMicrophone microphone;
        GUIStyle label;
        static AiVoiceControls instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isBatchMode || instance != null) return;
            instance = new GameObject("AI voice commands").AddComponent<AiVoiceControls>();
            DontDestroyOnLoad(instance.gameObject);
        }
        static NetPlayer Local => NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<NetPlayer>() : null;
        static bool Ready => (NetworkServer.active ? AiHostSettings.ServerState
            : WorldSync.Current != null ? WorldSync.Current.RoomAiState : AiRoomState.Unknown) == AiRoomState.Active;
        /// <summary>
        /// <b>쓰러져도 말은 할 수 있다</b> (S15P21B202-730).
        ///
        /// <para>여기가 여섯 개 관문 중 <b>맨 앞</b>이었다. 기절하면 V 자체가 안 먹어서
        /// 뒤쪽을 다 열어도 소용이 없다 — <c>살려줘</c> 를 <b>가장 필요한 사람이</b>
        /// 말할 수 없었다.</para>
        ///
        /// <para>몸은 여전히 못 쓴다. 뜻을 잃는 명령은 <see cref="AiCommandPlan.OrderRefusal"/>
        /// 이 <b>이름 있는 사유로</b> 거절하고, 그 한 마디가 화면에 돌아온다.</para>
        /// </summary>
        static bool CanRecord(GameSession session) => Application.isFocused && Local != null && session != null
            && session.LocalPlayer != null && session.CursorLocked
            && !session.WheelOpen && !session.CinematicInputBlocked && !session.EndingActive && !session.Cleared;
        void Update()
        {
            if (microphone != ProximityMicrophone.Current)
            {
                if (microphone != null) microphone.FrameReady -= Capture;
                microphone = ProximityMicrophone.Current;
                if (microphone != null) microphone.FrameReady += Capture;
            }
            var local = Local;
            if (local != null && local.AiVoiceMessageUntil > resultSeen)
            { resultSeen = local.AiVoiceMessageUntil; Notify(local.AiVoiceMessage); }
            var session = GameSession.Instance;
            // 마이크가 V 를 누른 뒤에야 열리므로(S15P21B202-705) 실패도 녹음이 시작된 뒤에
            // 드러난다. 이것이 없으면 "조금 더 길게 말해 주세요" 라는 엉뚱한 안내가 나간다.
            if (recording && microphone != null && microphone.Problem != null)
            { Notify(microphone.Problem); Cancel(); return; }
            // 장치가 아예 열리지 않은 녹음(3초 물러서기 중)은 한 조각도 못 받는다. 그대로 두면
            // "조금 더 길게 말해 주세요" 가 나가는데, 길이는 죄가 없다.
            //
            // **V 를 누르고 있는 동안에만 본다.** 놓은 뒤에는 마이크가 닫히는 것이 정상이라,
            // 이 가드가 거기까지 걸리면 0.6초 넘는 멀쩡한 녹음이 전부 취소된다(!596 AI 리뷰).
            if (recording && AiTalkKeys.Held(recordingTarget) && microphone != null && !microphone.Capturing
                && Time.realtimeSinceStartup - started > .6f)
            { Notify("마이크가 아직 열리지 않았어요 · 잠시 후 다시"); Cancel(); return; }
            if (recording && (!CanRecord(session) || !Ready || session != recordingSession || local != recordingOwner
                || (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)))
            { Cancel(); return; }
            // **묻히고 있으면 말하는 도중에 말해 준다** (S15P21B202-735). 전에는 V 를 놓은
            // 뒤에야 "목소리가 들리지 않았어요" 가 나왔다 — 그때는 이미 10초를 쓴 뒤고,
            // 다시 말해도 같은 목소리로 되풀이하게 된다.
            if (recording && microphone != null && microphone.Capturing
                && Time.realtimeSinceStartup - started > QuietNotice && !microphone.Speaking
                && Time.unscaledTime >= noticeUntil)
                Notify("목소리가 잡음에 묻혀요 · 더 크게 말해 주세요", 1.5f);

            if (recording && Time.realtimeSinceStartup - started >= AiVoicePacket.Seconds)
            { SendRecording(); return; }
            if (recording && !AiTalkKeys.Held(recordingTarget))
            {
                // 마이크의 링 버퍼에 아직 읽지 않은 마지막 조각이 남아 있다 (S15P21B202-705).
                // 여기서 곧바로 보내면 그 낱말은 잡히고도 실려 가지 않는다.
                //
                // **닫히기를 기다린다 — 꼬리를 제 시계로 재지 않는다.** 양쪽이 각자 0.35초를
                // 세면 마이크는 "마지막으로 눌린 프레임"에서 unscaledTime 으로, 이쪽은 "놓인
                // 프레임"에서 realtimeSinceStartup 으로 재게 되어 **마이크가 늘 먼저 닫힌다**
                // (!596 AI 리뷰). 더 올 조각이 없다는 참인 신호는 마이크가 닫힌 것 하나뿐이다.
                if (microphone != null && microphone.Capturing) return;
                SendRecording(); return;
            }
            if (!AiTalkKeys.PressedNow(out int target) || !CanRecord(session)) return;
            if (!Ready) { Notify("방장의 AI 추가팩이 준비되지 않았어요"); return; }
            if (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
            { Notify("마이크가 음소거되어 있어요 · ESC에서 해제"); return; }
            if (microphone == null || microphone.Problem != null) { Notify(microphone?.Problem ?? "마이크 준비 중"); return; }
            // 그 자리에 AI 가 없으면 **마이크를 열기 전에** 말해 준다. 서버도 다시 보지만
            // 그 답은 말을 다 한 뒤에야 온다 — 8초를 말하고 나서 "없어요" 를 듣게 된다.
            string missing = TargetProblem(target, AiSlots());
            if (missing != null) { Notify(missing); return; }
            samples.Clear(); recording = true; started = Time.realtimeSinceStartup; recordingTarget = target;
            recordingSession = session; recordingOwner = local; local.BeginAiListening(target);
        }

        /// <summary>지금 AI 가 앉은 자리들. 화면 안내와 누르기 전 확인이 같이 쓴다.</summary>
        List<int> AiSlots()
        {
            aiSlots.Clear();
            foreach (var player in NetPlayer.AllClientPlayers)
                if (player != null && player.IsAi && !aiSlots.Contains(player.teamSlot)) aiSlots.Add(player.teamSlot);
            aiSlots.Sort();
            return aiSlots;
        }

        /// <summary>
        /// 이 대상에게 지금 말할 수 없다면 그 이유. 말할 수 있으면 <c>null</c> (S15P21B202-738).
        /// 값으로 받는다 — 씬 없이 확인한다.
        /// </summary>
        public static string TargetProblem(int target, IReadOnlyList<int> aiSlots)
        {
            if (target == AiTalkKeys.Anyone) return null;
            if (aiSlots != null)
                foreach (int slot in aiSlots) if (slot == target) return null;
            return TeamSlots.Name(target) + " 자리에 AI 동료가 없어요";
        }

        /// <summary>
        /// 누르기 전 안내 한 줄 (S15P21B202-738). <b>AI 가 앉은 자리만</b> 적는다 — 사람이
        /// 앉은 색의 키를 적어 두면 눌러 보고서야 안 된다는 것을 알게 된다.
        /// </summary>
        public static string KeyHint(IReadOnlyList<int> aiSlots)
        {
            var text = new System.Text.StringBuilder("누르고 말하기 · ").Append(AiTalkKeys.Cap(AiTalkKeys.Anyone)).Append(" 주변 동료");
            if (aiSlots != null)
                foreach (int slot in aiSlots)
                    text.Append(" · ").Append(AiTalkKeys.Cap(slot)).Append(' ').Append(TeamSlots.Name(slot));
            return text.ToString();
        }
        void Capture(float[] frame)
        {
            if (!recording) return;
            int remaining = AiVoicePacket.Rate * AiVoicePacket.Seconds - samples.Count;
            for (int i = 0; i < frame.Length && i < remaining; i++) samples.Add(frame[i]);
        }
        void SendRecording()
        {
            if (samples.Count < AiVoicePacket.Rate / 5) { Notify("조금 더 길게 말해 주세요"); Cancel(); return; }
            float peak = 0; foreach (float value in samples) peak = Mathf.Max(peak, Mathf.Abs(value));
            if (peak < .002f) { Notify("목소리가 들리지 않았어요"); Cancel(); return; }
            recording = false;
            recordingOwner?.SubmitAiVoice(AiVoicePacket.Encode(samples, AiVoicePacket.Rate, 1));
            samples.Clear(); recordingOwner = null; recordingSession = null; recordingTarget = AiTalkKeys.Anyone;
            Notify("AI가 듣고 생각하는 중…", 95);
        }
        void Notify(string message, float seconds = 4) { notice = message; noticeUntil = Time.unscaledTime + seconds; }

        /// <summary>
        /// 막대의 오른쪽 끝이 뜻하는 크기 (S15P21B202-735).
        ///
        /// <para>보통 말소리의 rms 는 0.02~0.08 이고 문턱은 0.003 근처다. 1.0 을 끝으로
        /// 잡으면 <b>막대가 거의 안 움직여</b> 아무것도 못 말한다.</para>
        /// </summary>
        public const float LevelFullScale = .12f;

        /// <summary>
        /// 이만큼 말했는데도 말로 안 쳐지면 <b>그 자리에서</b> 알려 준다(초).
        ///
        /// <para>너무 짧으면 숨 고르는 사이에도 뜨고, 너무 길면 말이 다 끝난 뒤가 된다.
        /// 최대 녹음이 8초이므로 그 안에서 다시 말할 여유가 남는 값이어야 한다.</para>
        /// </summary>
        public const float QuietNotice = 1.2f;

        /// <summary>
        /// <b>지금 내 목소리가 들어가고 있는가</b>를 말하는 동안 보여 준다 (S15P21B202-735).
        ///
        /// <para>전에는 글자 두 줄뿐이라, 시끄러운 방에서 목소리가 묻히는지 알 수 없었다.
        /// 알게 되는 것은 V 를 놓은 <b>뒤</b>였고(<c>목소리가 들리지 않았어요</c>) 그때는
        /// 이미 10초를 쓴 뒤다.</para>
        ///
        /// <para><b>문턱 선을 같이 그린다.</b> 크기만 보여 주면 "이 정도면 큰 건가" 를 알 수
        /// 없다 — 필요한 것은 내 목소리가 <b>잡음 위에 있는가</b> 이고, 그 선이 곧 필터가
        /// 말/침묵을 가르는 자리다.</para>
        /// </summary>
        void DrawLevel()
        {
            if (!recording || microphone == null || !microphone.Capturing) return;

            float w = Screen.width * .3f, h = 10f;
            var box = new Rect((Screen.width - w) * .5f, Screen.height - 118, w, h);
            GUI.color = new Color(1, 1, 1, .18f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            float level = Mathf.Clamp01(microphone.Level / LevelFullScale);
            GUI.color = microphone.Speaking ? new Color(.45f, .85f, .45f, .9f)
                                            : new Color(.9f, .75f, .3f, .9f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width * level, h), Texture2D.whiteTexture);

            // 말로 치는 문턱. 막대가 이 선을 넘어야 들어간다.
            float floor = Mathf.Clamp01(microphone.SpeechFloor / LevelFullScale);
            GUI.color = new Color(1, 1, 1, .55f);
            GUI.DrawTexture(new Rect(box.x + box.width * floor, box.y - 2f, 2f, h + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        void Cancel()
        {
            if (recording) recordingOwner?.CancelAiListening();
            recording = false; samples.Clear(); recordingOwner = null; recordingSession = null;
            recordingTarget = AiTalkKeys.Anyone;
        }
        void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        void OnDisable() => Cancel();
        void OnDestroy()
        {
            Cancel(); if (microphone != null) microphone.FrameReady -= Capture;
            if (instance == this) instance = null;
        }
        void OnGUI()
        {
            if (!CanRecord(GameSession.Instance)) return;
            // **지금 누구에게 말하는지**가 보여야 한다 (S15P21B202-738) — 1 을 눌렀는데
            // "주변 AI에게" 라고 적혀 있으면 버튼이 먹었는지 알 수 없다.
            string text = recording
                ? (recordingTarget == AiTalkKeys.Anyone ? "● 듣고 있어요 · V를 놓으면 주변 AI에게 명령"
                   : "● " + TeamSlots.Name(recordingTarget) + "에게 말하는 중 · 키를 놓으면 명령")
                : Time.unscaledTime < noticeUntil ? notice : Ready ? KeyHint(AiSlots()) : null;
            string mic = microphone?.Problem ?? ((GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
                ? "마이크 꺼짐 · ESC에서 해제"
                : recording && microphone != null && microphone.Speaking ? "● 목소리 인식 중"
                : "마이크는 말하기 키를 누르는 동안에만 켜집니다");
            if (label == null) { UiTheme.Ensure(); label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true }; }
            GUI.Label(new Rect(Screen.width * .2f, Screen.height - 100, Screen.width * .6f, 70), mic + "\n" + text, label);
            DrawLevel();
        }
    }
}
