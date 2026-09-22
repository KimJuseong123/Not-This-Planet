using Metagame.Core;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 마이크 설정. <b>고를 것이 하나뿐이다</b> (S15P21B202-705).
    ///
    /// <para>예전에는 오픈 마이크/V 전용 선택, 플레이어별 음소거, 음성 듣기 크기가 있었다.
    /// 플레이어끼리 말하는 길을 걷어내면서 <b>셋 다 가리킬 것이 없어졌다</b> — 고를 수 없는
    /// 것을 남겨 두면 눌러도 아무 일이 없는 스위치가 된다.</para>
    /// </summary>
    public static class ProximityVoiceMenu
    {
        public static void Draw(Rect rect)
        {
            var runtime = GameSettingsRuntime.Ensure();
            var data = runtime.Current.Clone(); bool changed = false;
            GUILayout.BeginArea(rect);
            bool muted = GUILayout.Toggle(data.voice.microphoneMuted, "마이크 음소거");
            changed |= muted != data.voice.microphoneMuted; data.voice.microphoneMuted = muted;
            GUILayout.Label("마이크는 V를 누르는 동안에만 켜지고, 그 소리는 AI 동료에게 내리는 명령에만 쓰입니다.");
            GUILayout.EndArea();
            if (changed) runtime.Save(data, out _);
        }
    }
}
