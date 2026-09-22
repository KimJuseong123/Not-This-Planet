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
            if (recording && (!CanRecord(session) || !Ready || session != recordingSession || local != recordingOwner
                || (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)))
            { Cancel(); return; }
            if (recording && (Input.GetKeyUp(KeyCode.V) || Time.realtimeSinceStartup - started >= AiVoicePacket.Seconds))
            { SendRecording(); return; }
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
                ? "마이크 꺼짐" : microphone != null && microphone.Speaking ? "● 주변에 말하는 중" : "마이크 켜짐 · 주변 음성 채팅");
            if (label == null) { UiTheme.Ensure(); label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true }; }
            GUI.Label(new Rect(Screen.width * .2f, Screen.height - 100, Screen.width * .6f, 70), mic + "\n" + text, label);
            int row = 0;
            foreach (var identity in NetworkClient.spawned.Values)
            {
                var player = identity != null ? identity.GetComponent<NetPlayer>() : null;
                if (player == null || !player.VoiceSpeaking || player.VisualRig == null
                    || !VoiceRules.Audible(player.VisualRig.FootPosition, GameSession.Instance.LocalPlayer.FootPosition)
                    || (GameSettingsRuntime.Instance?.Current?.voice?.mutedPlayerIds?.Contains(player.userId.ToString()) ?? false)) continue;
                GUI.Label(new Rect(Screen.width - 270, 150 + 28 * row++, 250, 26), "● " + player.nickname, label);
            }
        }
    }
}
