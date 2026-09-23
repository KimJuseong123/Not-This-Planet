using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>V selects a command segment from the shared microphone.</summary>
    public sealed class AiVoiceControls : MonoBehaviour
    {
        readonly List<float> samples = new List<float>(AiVoicePacket.Rate * AiVoicePacket.Seconds);
        bool recording;
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
            if (recording && Input.GetKey(KeyCode.V) && microphone != null && !microphone.Capturing
                && Time.realtimeSinceStartup - started > .6f)
            { Notify("마이크가 아직 열리지 않았어요 · 잠시 후 다시"); Cancel(); return; }
            if (recording && (!CanRecord(session) || !Ready || session != recordingSession || local != recordingOwner
                || (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)))
            { Cancel(); return; }
            if (recording && Time.realtimeSinceStartup - started >= AiVoicePacket.Seconds)
            { SendRecording(); return; }
            if (recording && !Input.GetKey(KeyCode.V))
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
            if (!Input.GetKeyDown(KeyCode.V) || !CanRecord(session)) return;
            if (!Ready) { Notify("방장의 AI 추가팩이 준비되지 않았어요"); return; }
            if (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
            { Notify("마이크가 음소거되어 있어요 · ESC에서 해제"); return; }
            if (microphone == null || microphone.Problem != null) { Notify(microphone?.Problem ?? "마이크 준비 중"); return; }
            samples.Clear(); recording = true; started = Time.realtimeSinceStartup;
            recordingSession = session; recordingOwner = local; local.BeginAiListening();
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
            samples.Clear(); recordingOwner = null; recordingSession = null;
            Notify("AI가 듣고 생각하는 중…", 95);
        }
        void Notify(string message, float seconds = 4) { notice = message; noticeUntil = Time.unscaledTime + seconds; }
        void Cancel()
        {
            if (recording) recordingOwner?.CancelAiListening();
            recording = false; samples.Clear(); recordingOwner = null; recordingSession = null;
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
            string text = recording ? "● 듣고 있어요 · V를 놓으면 주변 AI에게 명령"
                : Time.unscaledTime < noticeUntil ? notice : Ready ? "V 누르고 말하기 · 주변 AI 동료" : null;
            string mic = microphone?.Problem ?? ((GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
                ? "마이크 꺼짐 · ESC에서 해제"
                : recording && microphone != null && microphone.Speaking ? "● 목소리 인식 중"
                : "마이크는 V를 누르는 동안에만 켜집니다");
            if (label == null) { UiTheme.Ensure(); label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true }; }
            GUI.Label(new Rect(Screen.width * .2f, Screen.height - 100, Screen.width * .6f, 70), mic + "\n" + text, label);
        }
    }
}
