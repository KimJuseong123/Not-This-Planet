using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>개발용 메뉴 안에서만 입력한다. 별도 기능 키나 게임 입력 차단 상태를 추가하지 않는다.</summary>
    public static class AiTextTestControls
    {
        static int selectedSlot = -1;
        static string text = "나 따라와";
        static AiRecipients recipients;
        static Vector2 scroll;
        static readonly string[] Modes = { "선택한 AI", "빈 동료 1명", "빈 동료 2명", "AI 전원" };
        static readonly string[] Labels = { "모여", "따라와", "기다려", "중단", "인사", "춤", "하트", "따봉", "못 하나", "못 모두",
            "도구 집기", "몽둥이 집기", "용접건 집기", "도구 놓기", "도구 가져와", "몽둥이 가져와", "용접건 가져와",
            "연습공 타격", "운석 방어", "파츠 잡기", "파츠 놓기", "파츠 운반", "용접", "용접 해제",
            "견인 탑승", "견인 하차", "견인 여기로", "나 올려줘", "파츠 들어줘", "파츠 견인", "고도 유지", "파츠 쌓기" };
        static readonly string[] Actions = { "gather", "follow", "wait", "cancel", "greet", "dance", "heart", "cheer", "pull_pin", "pull_all_pins",
            "pickup_tool", "pickup_bat", "pickup_welder", "drop_tool", "bring_tool", "bring_bat", "bring_welder",
            "hit_target", "defend_meteors", "grab_part", "release_part", "carry_part", "weld", "cut_weld",
            "board_tractor", "leave_tractor", "move_tractor", "lift_player", "lift_part", "transport_part", "hold_tractor", "stack_part" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { selectedSlot = -1; text = "나 따라와"; recipients = AiRecipients.Selected; scroll = default; }

        public static void Draw(Rect area, GUIStyle button, GUIStyle label)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var runtime = AiLocalRuntime.Current;
            if (!NetworkServer.active || runtime == null)
            {
                GUI.Label(area, "텍스트 AI 테스트: 방장만 사용 가능", label);
                return;
            }
            float textY = 124 + ((Actions.Length + 2) / 3) * 32;
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, area.width - 22, textY + 215));
            float w = area.width - 26;
            var small = new GUIStyle(button) { fontSize = 14 };
            var info = new GUIStyle(label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperLeft };
            GUI.Label(new Rect(0, 0, w, 22), "AI 지시 · 대상 선택 → 행동 실행", info);
            for (int i = 0; i < 4; i++)
                if (GUI.Button(new Rect(i*w/4,24,w/4-3,28), ((int)recipients==i ? "● " : "")+Modes[i],small)) recipients=(AiRecipients)i;
            NetPlayer selected = null;
            int count = 0;
            foreach (var player in NetPlayer.AllServerPlayers)
            {
                if (player == null || !player.IsAi) continue;
                if (selectedSlot < 0) selectedSlot = player.teamSlot;
                if (GUI.Button(new Rect(count*w/3,56,w/3-3,30),
                    (player.teamSlot == selectedSlot ? "● " : "") + player.nickname, small))
                { selectedSlot = player.teamSlot; recipients = AiRecipients.Selected; }
                if (player.teamSlot == selectedSlot) selected = player;
                count++;
            }
            if (count == 0) GUI.Label(new Rect(0,56,w,30), "활성 AI 없음 · 로비에서 AI 사용을 켜 주세요", info);
            var field = new GUIStyle(GUI.skin.textField) { font = label.font, fontSize = 18 };
            bool previous = GUI.enabled;
            GUI.enabled = previous && AiHostSettings.ServerState == AiRoomState.Active && (recipients != AiRecipients.Selected || selected != null);
            GUI.Label(new Rect(0,92,w,20), "직접 실행 · 아래 버튼은 LLM을 거치지 않음", info);
            for (int i=0; i<Actions.Length; i++)
                if (GUI.Button(new Rect((i%3)*w/3,116+(i/3)*32,w/3-3,29),Labels[i],small)) runtime.SendDirect(Actions[i],recipients,selected);
            GUI.enabled = previous;
            text = GUI.TextField(new Rect(0,textY,w-88,28), text, 300, field);
            GUI.enabled = previous && !runtime.Busy && runtime.State == AiRuntimeState.Ready && (recipients != AiRecipients.Selected || selected != null);
            if (GUI.Button(new Rect(w-84,textY,84,28),"LLM 전송",small)) runtime.SendText(text,recipients,selected);
            GUI.enabled = previous;
            if (GUI.Button(new Rect(0,textY+33,w,27),"모든 AI 즉시 중단",small)) runtime.StopOrders();
            GUI.Label(new Rect(0,textY+65,w,46),runtime.Message,info);
            int row=0;
            foreach (var ai in NetPlayer.AllServerPlayers)
                if(ai!=null && ai.IsAi) GUI.Label(new Rect(0,textY+116+row++*26,w,26),ai.nickname+" · "+ai.AiActivity,info);
            GUI.EndScrollView();
#endif
        }
    }
}
