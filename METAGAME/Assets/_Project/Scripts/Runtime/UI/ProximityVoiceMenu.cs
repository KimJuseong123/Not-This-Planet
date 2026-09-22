using Metagame.Core;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    public static class ProximityVoiceMenu
    {
        public static void Draw(Rect rect)
        {
            var runtime = GameSettingsRuntime.Ensure();
            var data = runtime.Current.Clone(); bool changed = false;
            GUILayout.BeginArea(rect);
            bool muted = GUILayout.Toggle(data.voice.microphoneMuted, "마이크 음소거 · 메뉴/다른 창에서는 자동으로 송신 중지");
            changed |= muted != data.voice.microphoneMuted; data.voice.microphoneMuted = muted;
            bool push = GUILayout.Toggle(data.voice.pushToTalk, "오픈 마이크 대신 V를 누를 때만 송신 (AI 명령도 함께 전달)");
            changed |= push != data.voice.pushToTalk; data.voice.pushToTalk = push;
            GUILayout.BeginHorizontal(); GUILayout.Label("음성 듣기", GUILayout.Width(75));
            float volume = GUILayout.HorizontalSlider(data.audio.voiceVolume, 0, 1);
            changed |= volume != data.audio.voiceVolume; data.audio.voiceVolume = volume; GUILayout.EndHorizontal();
            foreach (var identity in NetworkClient.spawned.Values)
            {
                var player = identity != null ? identity.GetComponent<NetPlayer>() : null;
                if (player == null || player.IsAi || player.isLocalPlayer) continue;
                string id = player.userId.ToString(); bool before = data.voice.mutedPlayerIds.Contains(id);
                bool after = GUILayout.Toggle(before, player.nickname + " · 음소거");
                if (before != after) { changed = true; if (after) data.voice.mutedPlayerIds.Add(id); else data.voice.mutedPlayerIds.Remove(id); }
            }
            GUILayout.EndArea();
            if (changed) runtime.Save(data, out _);
        }
    }
}
