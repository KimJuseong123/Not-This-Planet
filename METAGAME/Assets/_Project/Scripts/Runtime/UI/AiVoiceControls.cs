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
        float started, noticeUntil, resultSeen, releaseAt;
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
        static bool CanRecord(GameSession session) => Application.isFocused && Local != null && session != null
            && session.LocalPlayer != null && !session.LocalPlayer.Downed && session.CursorLocked
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
            if (recording && microphone != null && !microphone.Capturing
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
                // 여기서 곧바로 보내면 그 낱말은 잡히고도 실려 가지 않는다 — 마이크가 꼬리만큼
                // 더 여는 것과 짝이다. 한쪽만 바꾸면 끝 낱말이 잘린다.
                if (releaseAt == 0) releaseAt = Time.realtimeSinceStartup + ProximityMicrophone.ReleaseTail;
                if (Time.realtimeSinceStartup < releaseAt) return;
                SendRecording(); return;
            }
            if (!Input.GetKeyDown(KeyCode.V) || !CanRecord(session)) return;
            if (!Ready) { Notify("방장의 AI 추가팩이 준비되지 않았어요"); return; }
            if (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
            { Notify("마이크가 음소거되어 있어요 · ESC에서 해제"); return; }
            if (microphone == null || microphone.Problem != null) { Notify(microphone?.Problem ?? "마이크 준비 중"); return; }
            samples.Clear(); recording = true; started = Time.realtimeSinceStartup; releaseAt = 0;
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
            samples.Clear(); recordingOwner = null; recordingSession = null; releaseAt = 0;
            Notify("AI가 듣고 생각하는 중…", 95);
        }
        void Notify(string message, float seconds = 4) { notice = message; noticeUntil = Time.unscaledTime + seconds; }
        void Cancel()
        {
            if (recording) recordingOwner?.CancelAiListening();
            recording = false; samples.Clear(); recordingOwner = null; recordingSession = null; releaseAt = 0;
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
