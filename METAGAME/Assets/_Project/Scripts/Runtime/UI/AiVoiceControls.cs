using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>
    /// 한 자리의 지금 모습 (S15P21B202-738). 1~4 는 자리에 묶일 뿐 <b>누가 앉았는지는 모른다</b> —
    /// 화면의 칸과 V 를 막는 판단이 이 값 하나를 본다.
    /// </summary>
    public struct AiSeatView
    {
        public int Slot;
        public bool Occupied, Ai, Mine, Downed;
        /// <summary>내 몸에서의 거리(m). 모르면 무한대.</summary>
        public float Distance;
    }

    /// <summary>
    /// 1~4 로 대상을 <b>고르고</b>, V 를 누르는 동안 명령을 <b>녹음한다</b> (S15P21B202-738).
    /// 두 단계는 따로다 — 고른 대상은 다시 누르거나 그 자리에 AI 가 없어질 때까지 남는다.
    /// </summary>
    public sealed class AiVoiceControls : MonoBehaviour
    {
        readonly List<float> samples = new List<float>(AiVoicePacket.Rate * AiVoicePacket.Seconds);
        readonly List<AiSeatView> seats = new List<AiSeatView>(TeamSlots.Count);
        bool recording;
        /// <summary>
        /// 1~4 로 고른 대상 (S15P21B202-738). <see cref="AiTalkKeys.Anyone"/> 이면 주변 동료에게
        /// 말하고 대상은 말로 정한다. <b>판이 바뀌면 푼다</b> — 다음 판의 같은 색 자리에 누가
        /// 앉을지는 모른다.
        /// </summary>
        int selectedTarget = AiTalkKeys.Anyone;
        GameSession selectionSession;
        /// <summary>이 녹음을 시작할 때의 대상. 녹음 중에는 고르기 키가 바꾸지 못한다.</summary>
        int recordingTarget = AiTalkKeys.Anyone;
        /// <summary>이 녹음이 서버에 연 듣기의 번호. 거절이 <b>이 녹음의 것</b>인지 가른다.</summary>
        uint recordingListen;
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
            { resultSeen = local.AiVoiceMessageUntil; }
            var session = GameSession.Instance;
            // **서버가 듣기를 거절했으면 곧바로 멈춘다** (S15P21B202-738 리뷰). 기절한 동료의
            // 키를 눌렀거나 주변에 AI 가 없으면 서버가 그 자리에서 안다 — 녹음을 계속 두면
            // 8초를 다 말한 뒤에야 같은 말을 듣는다.
            if (recording && recordingOwner != null && recordingListen != 0
                && recordingOwner.AiListenRefused == recordingListen)
            { string refusal = recordingOwner.AiListenRefusal; Cancel(); Notify(refusal); return; }
            // 마이크가 V 를 누른 뒤에야 열리므로(S15P21B202-705) 실패도 녹음이 시작된 뒤에
            // 드러난다. 이것이 없으면 "조금 더 길게 말해 주세요" 라는 엉뚱한 안내가 나간다.
            if (recording && microphone != null && microphone.Problem != null)
            { Notify(microphone.Problem); Cancel(); return; }
            // 장치가 아예 열리지 않은 녹음(3초 물러서기 중)은 한 조각도 못 받는다. 그대로 두면
            // "조금 더 길게 말해 주세요" 가 나가는데, 길이는 죄가 없다.
            //
            // **V 를 누르고 있는 동안에만 본다.** 놓은 뒤에는 마이크가 닫히는 것이 정상이라,
            // 이 가드가 거기까지 걸리면 0.6초 넘는 멀쩡한 녹음이 전부 취소된다(!596 AI 리뷰).
            if (recording && AiTalkKeys.TalkHeld() && microphone != null && !microphone.Capturing
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
            if (recording && !AiTalkKeys.TalkHeld())
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
            if (!CanRecord(session)) return;
            Seats(local, session);
            // 판이 바뀌면 고른 대상을 푼다 — 다음 판의 같은 색 자리에 누가 앉을지는 모른다.
            if (selectionSession != session) { selectionSession = session; selectedTarget = AiTalkKeys.Anyone; }
            // **대상 고르기는 말하기와 따로다** (S15P21B202-738). AI 가 꺼진 방에서는 아무 일도
            // 아니다 — 숫자키는 다른 뜻으로 눌릴 수 있다. **녹음 중에는 바꾸지 않는다** — 말하기를
            // 시작할 때 서버가 이미 그 대상을 붙잡았으므로, 바꾸면 화면과 실제가 갈린다.
            if (Ready && !recording && AiTalkKeys.TargetPressed(out int pressed))
            {
                selectedTarget = NextSelection(selectedTarget, pressed, seats, out string picked);
                Notify(picked, 2.5f);
            }
            // 고른 자리에서 AI 가 빠지면(방장이 뺐거나 사람이 들어왔으면) 푼다. 그대로 두면 V 가
            // 까닭 모르게 막힌다.
            if (!StillSelectable(selectedTarget, seats))
            {
                Notify(TeamSlots.Name(selectedTarget) + " 자리에 AI 동료가 없어져 대상을 풀었어요");
                selectedTarget = AiTalkKeys.Anyone;
            }
            if (recording || !AiTalkKeys.TalkPressed()) return;
            // 화면에는 "추가팩" 같은 속사정을 적지 않는다 (S15P21B202-713 의 규칙) — 플레이어가 알아야 할 것은
            // "지금 AI 에게 말할 수 없다" 뿐이다.
            if (!Ready) { Notify("방장의 AI 동료 기능이 아직 준비되지 않았어요"); return; }
            // **앞 명령을 올리는 중이면 새로 듣지 않는다** (S15P21B202-738 리뷰). 새로 들으면 전송을
            // 비우고 서버가 받던 것을 버려서 앞 명령이 아무 말 없이 사라진다.
            if (local.AiVoiceSending) { Notify("앞 명령을 보내는 중이에요 · 잠시 후 다시"); return; }
            if (GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false)
            { Notify("마이크가 음소거되어 있어요 · ESC에서 해제"); return; }
            if (microphone == null || microphone.Problem != null) { Notify(microphone?.Problem ?? "마이크 준비 중"); return; }
            // **말할 수 없는 대상이면 V 가 녹음을 시작하지 않는다** (S15P21B202-738). 화면이 이미
            // "V 사용 불가" 와 이유를 보여 주고 있다 — 서버도 다시 보지만 그 답은 말을 다 한
            // 뒤에야 온다. 8초를 말하고 나서 "안 돼요" 를 듣는 것이 가장 화나는 경우다.
            string problem = TalkProblem(selectedTarget, seats, VoiceRules.Range);
            if (problem != null) { Notify(problem); return; }
            int target = selectedTarget;
            samples.Clear(); recording = true; started = Time.realtimeSinceStartup; recordingTarget = target;
            recordingSession = session; recordingOwner = local; local.BeginAiListening(target);
            recordingListen = local.AiListenId;
        }

        /// <summary>자리 넷의 지금 모습을 새로 읽는다. 매 프레임 — AI 수는 판 중에도 바뀐다.</summary>
        void Seats(NetPlayer local, GameSession session)
        {
            seats.Clear();
            var me = session != null ? session.LocalPlayer : null;
            for (int slot = 0; slot < TeamSlots.Count; slot++)
            {
                var seat = new AiSeatView { Slot = slot, Distance = float.PositiveInfinity };
                foreach (var player in NetPlayer.AllClientPlayers)
                {
                    if (player == null || player.teamSlot != slot) continue;
                    seat.Occupied = true; seat.Ai = player.IsAi; seat.Mine = player == local;
                    if (player.HasBody)
                    {
                        seat.Downed = player.VisualRig.Downed;
                        if (me != null && !seat.Mine)
                            seat.Distance = Vector3.Distance(me.FootPosition, player.VisualRig.FootPosition);
                    }
                    break;
                }
                seats.Add(seat);
            }
        }

        static AiSeatView Find(IReadOnlyList<AiSeatView> seats, int slot)
        {
            if (seats != null)
                foreach (var seat in seats) if (seat.Slot == slot) return seat;
            return new AiSeatView { Slot = slot, Distance = float.PositiveInfinity };
        }

        /// <summary>
        /// 고르기 키를 눌렀을 때의 새 대상과 한 줄 안내 (S15P21B202-738).
        ///
        /// <para><b>같은 키를 다시 누르면 푼다</b> — 주변 동료에게 말하는 데로 돌아간다.
        /// <b>AI 가 없는 자리는 고르지 않는다</b> — 고른 채로 두면 V 가 막히기만 한다. 기절한 AI 는
        /// 고른다(살아날 수 있다). 그때는 V 가 막히고 화면이 이유를 말한다.</para>
        /// </summary>
        public static int NextSelection(int current, int pressed, IReadOnlyList<AiSeatView> seats, out string notice)
        {
            string name = TeamSlots.Name(pressed);
            if (pressed == current) { notice = "대상을 풀었어요 · V는 주변 동료에게"; return AiTalkKeys.Anyone; }
            var seat = Find(seats, pressed);
            if (!seat.Occupied) { notice = name + " 자리에 AI 동료가 없어요"; return current; }
            if (!seat.Ai) { notice = seat.Mine ? name + "은 나예요" : name + "은 AI 동료가 아니에요"; return current; }
            notice = name + " 동료를 골랐어요 · V를 누르고 말하세요";
            return pressed;
        }

        /// <summary>고른 대상이 아직 고를 수 있는 자리인가. 고르지 않았으면 언제나 참.</summary>
        public static bool StillSelectable(int selected, IReadOnlyList<AiSeatView> seats)
            => selected == AiTalkKeys.Anyone || Find(seats, selected).Ai;

        /// <summary>
        /// 지금 V 로 말할 수 <b>없다면</b> 그 이유. 말할 수 있으면 <c>null</c> (S15P21B202-738).
        ///
        /// <para><b>고른 대상은 거리를 보지 않는다.</b> 멀리 있는 동료에게 시킬 수 있는 것이 고르는
        /// 이유다. <b>고르지 않았으면 주변 <paramref name="range"/>m 안의 AI 만 듣는다</b> — 서버의
        /// <see cref="AiGroupOrders.Capture"/> 와 같은 규칙이고, 기절한 동료는 세지 않는다.</para>
        ///
        /// <para>값으로 받는다 — 씬 없이 확인한다. 화면의 "V 사용 불가" 와 V 를 눌렀을 때의 거절이
        /// <b>이 한 함수</b>를 본다.</para>
        /// </summary>
        public static string TalkProblem(int target, IReadOnlyList<AiSeatView> seats, float range)
        {
            if (target == AiTalkKeys.Anyone)
            {
                bool any = false;
                if (seats != null)
                    foreach (var seat in seats)
                    {
                        if (!seat.Ai) continue;
                        any = true;
                        if (!seat.Downed && seat.Distance <= range) return null;
                    }
                return any ? "주변 " + range.ToString("0") + "m 안에 들을 AI 동료가 없어요 · 1~4로 골라 말하세요"
                           : "AI 동료가 없어요";
            }
            var chosen = Find(seats, target);
            string name = TeamSlots.Name(target);
            if (!chosen.Occupied) return name + " 자리에 AI 동료가 없어요";
            if (!chosen.Ai) return name + "은 AI 동료가 아니에요";
            if (chosen.Downed) return name + " 동료가 기절해 있어요";
            return null;
        }

        /// <summary>자리 칸에 적는 한 줄. <c>1 빨강 · AI 12m</c> · <c>2 파랑 · 나</c> · <c>4 초록 · 비어 있음</c>.</summary>
        public static string SeatLabel(AiSeatView seat)
        {
            string head = AiTalkKeys.Cap(seat.Slot) + " " + TeamSlots.Name(seat.Slot) + " · ";
            if (!seat.Occupied) return head + "비어 있음";
            if (seat.Mine) return head + "나";
            if (!seat.Ai) return head + "사람";
            if (seat.Downed) return head + "AI 기절";
            return head + "AI" + (float.IsInfinity(seat.Distance) ? "" : " " + seat.Distance.ToString("0") + "m");
        }

        /// <summary>V 한 줄. 막혔으면 <b>막혔다는 것과 이유</b>를 먼저 적는다.</summary>
        public static string TalkLine(int target, string problem)
        {
            if (problem != null) return "V 사용 불가 · " + problem;
            return target == AiTalkKeys.Anyone ? "V 누르고 말하기 · 주변 동료 (대상은 말로)"
                                               : "V 누르고 말하기 · " + TeamSlots.Name(target) + "에게";
        }
        void Capture(float[] frame)
        {
            if (!recording) return;
            int remaining = AiVoicePacket.Rate * AiVoicePacket.Seconds - samples.Count;
            for (int i = 0; i < frame.Length && i < remaining; i++) samples.Add(frame[i]);
        }
        /// <summary>녹음을 보낸다. 짧거나 조용해서 안 보냈으면 <c>false</c>.</summary>
        bool SendRecording()
        {
            if (samples.Count < AiVoicePacket.Rate / 5) { Notify("조금 더 길게 말해 주세요"); Cancel(); return false; }
            float peak = 0; foreach (float value in samples) peak = Mathf.Max(peak, Mathf.Abs(value));
            if (peak < .002f) { Notify("목소리가 들리지 않았어요"); Cancel(); return false; }
            recording = false; recordingListen = 0;
            recordingOwner?.SubmitAiVoice(AiVoicePacket.Encode(samples, AiVoicePacket.Rate, 1));
            samples.Clear(); recordingOwner = null; recordingSession = null; recordingTarget = AiTalkKeys.Anyone;
            Notify("AI가 듣고 생각하는 중…", 95);
            return true;
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
        /// 화면 높이에 맞춘 배율 — 1080p 가 1 (S15P21B202-746). 설정의 글자 배율은 곱하지 않는다 — 2026-09-26 QA 에서
        /// 1.5 배까지 곱해져 "과하게 커졌다".
        /// </summary>
        static float K => Screen.height / 1080f;

        /// <summary>마이크 아이콘 지름 · 아이콘 아래 짧은 말 · 결과 배너 글자(1080p 기준 픽셀).</summary>
        public const int MicSize = 58, HintFont = 17, BannerFont = 26;

        /// <summary>
        /// 지금 V 로 말할 대상 — 왼쪽 위 동료 카드가 강조한다 (S15P21B202-746). 아래쪽의 자리 칸 넷을 없앴으므로 고른 것이
        /// 보일 곳이 거기다. 없으면 <see cref="AiTalkKeys.Anyone"/>.
        /// </summary>
        public static int SelectedSlot => instance == null ? AiTalkKeys.Anyone
            : instance.recording ? instance.recordingTarget : instance.selectedTarget;

        GUIStyle hint, keyCap, bannerStyle, bannerCap;

        /// <summary>
        /// 가운데 아래 마이크 아이콘 하나 (2026-09-26 QA). 글자는 아이콘 위의 V 뿐이다. 말하는 동안 테두리가 목소리 크기만큼
        /// 커졌다 작아지고, 말로 쳐지면 초록이 된다. 흐린 원은 말로 치는 문턱이다. 막혔으면(마이크 꺼짐 등) 아이콘이 빨갛고
        /// 아래에 짧은 까닭이 뜬다.
        /// </summary>
        void DrawMic(string blocked, string hintText)
        {
            float size = MicSize * K, cx = Screen.width * .5f, cy = Screen.height - size * .5f - 30f * K;
            var icon = new Rect(cx - size * .5f, cy - size * .5f, size, size);
            Color accent = (recording ? recordingTarget : selectedTarget) is int slot && slot != AiTalkKeys.Anyone
                ? TeamSlots.Accent(slot) : new Color(.85f, .9f, 1f);
            if (recording && microphone != null && microphone.Capturing)
            {
                float level = Mathf.Clamp01(microphone.Level / LevelFullScale);
                float floor = Mathf.Clamp01(microphone.SpeechFloor / LevelFullScale);
                float grow = size * (1.05f + .9f * level), threshold = size * (1.05f + .9f * floor);
                GUI.color = new Color(1, 1, 1, .25f);
                GUI.DrawTexture(new Rect(cx - threshold * .5f, cy - threshold * .5f, threshold, threshold), HudIcons.Ring);
                GUI.color = microphone.Speaking ? new Color(.45f, .9f, .5f, .95f) : new Color(.95f, .8f, .35f, .9f);
                GUI.DrawTexture(new Rect(cx - grow * .5f, cy - grow * .5f, grow, grow), HudIcons.Ring);
            }
            GUI.color = blocked != null ? new Color(.55f, .12f, .12f, .85f) : recording ? new Color(.1f, .14f, .2f, .92f) : new Color(0, 0, 0, .5f);
            GUI.DrawTexture(icon, HudIcons.Disc);
            GUI.color = blocked != null ? new Color(1, .7f, .7f, 1) : recording ? Color.white : new Color(1, 1, 1, .8f);
            GUI.DrawTexture(new Rect(icon.x + size * .2f, icon.y + size * .2f, size * .6f, size * .6f), HudIcons.Mic);
            GUI.color = WithAlpha(accent, recording ? 1f : .7f);
            GUI.DrawTexture(icon, HudIcons.Ring);

            // 아이콘 위의 V — 누르면 말한다
            if (keyCap == null) keyCap = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            keyCap.fontSize = Mathf.RoundToInt(15 * K);
            var cap = new Rect(cx - 13f * K, icon.y - 24f * K, 26f * K, 22f * K);
            GUI.color = new Color(0, 0, 0, .55f); GUI.DrawTexture(cap, Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(cap, "V", keyCap);

            string under = blocked ?? hintText;
            if (!string.IsNullOrEmpty(under))
            {
                if (hint == null) hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                hint.fontSize = Mathf.RoundToInt(HintFont * K);
                var line = new Rect(cx - Screen.width * .3f, icon.yMax + 2f * K, Screen.width * .6f, HintFont * 1.5f * K);
                GUI.color = new Color(0, 0, 0, .7f); GUI.Label(new Rect(line.x + 1, line.y + 1, line.width, line.height), under, hint);
                GUI.color = blocked != null ? new Color(1, .75f, .75f, 1) : Color.white; GUI.Label(line, under, hint);
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 결과 배너 (2026-09-26 QA: 너무 길었다). 보여 줄 것은 둘뿐 — <b>들은 말</b> · <b>그래서 한 명령</b>. 누가 받았는지는
        /// 팀 색 원에 키 번호로 그린다. 해냈으면 초록, 못 했으면 빨강. 그 밖의 알림(음성 처리 실패 등)은 한 줄로.
        /// </summary>
        void DrawBanner()
        {
            var local = Local;
            bool outcome = local != null && Time.unscaledTime < local.AiVoiceOutcomeUntil;
            bool plain = !outcome && local != null && Time.unscaledTime < local.AiVoiceMessageUntil;
            if (!outcome && !plain) return;
            float until = outcome ? local.AiVoiceOutcomeUntil : local.AiVoiceMessageUntil;
            float fade = Mathf.Clamp01((until - Time.unscaledTime) / .4f);
            if (bannerStyle == null) bannerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, richText = false };
            if (bannerCap == null) bannerCap = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            bannerStyle.fontSize = Mathf.RoundToInt(BannerFont * K);
            bannerCap.fontSize = Mathf.RoundToInt(BannerFont * .8f * K);
            float h = BannerFont * 1.9f * K, icon = BannerFont * 1.3f * K, gap = 10f * K;

            string heard = outcome ? (string.IsNullOrEmpty(local.AiVoiceHeard) ? "…" : "“" + local.AiVoiceHeard + "”") : local.AiVoiceMessage;
            string arrow = outcome ? "  →  " : "";
            string command = outcome ? local.AiVoiceCommand : "";
            float heardW = bannerStyle.CalcSize(new GUIContent(heard)).x;
            float commandW = outcome ? bannerStyle.CalcSize(new GUIContent(arrow + command)).x : 0f;
            int slots = outcome ? local.AiVoiceSlots : 0, count = 0;
            for (int i = 0; i < 4; i++) if ((slots & (1 << i)) != 0) count++;
            float iconsW = count > 0 ? count * (icon + gap * .5f) + gap : 0f;
            float w = Mathf.Min(Screen.width * .9f, iconsW + heardW + commandW + 2 * 18f * K);
            var box = new Rect((Screen.width - w) * .5f, Screen.height * .1f, w, h);
            GUI.color = new Color(0, 0, 0, .6f * fade); GUI.DrawTexture(box, Texture2D.whiteTexture);
            float x = box.x + 18f * K;
            for (int i = 0; i < 4; i++)
            {
                if ((slots & (1 << i)) == 0) continue;
                var disc = new Rect(x, box.y + (h - icon) * .5f, icon, icon);
                GUI.color = WithAlpha(TeamSlots.Accent(i), fade); GUI.DrawTexture(disc, HudIcons.Disc);
                GUI.color = new Color(0, 0, 0, fade); GUI.Label(disc, AiTalkKeys.Cap(i), bannerCap);
                x += icon + gap * .5f;
            }
            if (count > 0) x += gap * .5f;
            GUI.color = new Color(1, 1, 1, fade);
            GUI.Label(new Rect(x, box.y, heardW + 4f, h), heard, bannerStyle);
            if (outcome)
            {
                GUI.color = local.AiVoiceOk ? new Color(.55f, 1f, .6f, fade) : new Color(1f, .6f, .55f, fade);
                GUI.Label(new Rect(x + heardW, box.y, commandW + 4f, h), arrow + command, bannerStyle);
            }
            GUI.color = Color.white;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void Cancel()
        {
            if (recording) recordingOwner?.CancelAiListening();
            recording = false; samples.Clear(); recordingOwner = null; recordingSession = null;
            recordingTarget = AiTalkKeys.Anyone; recordingListen = 0;
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
            // 막힌 까닭은 아이콘 아래 짧게 — 마이크가 없거나 꺼졌거나 고른 동료에게 말할 수 없거나.
            string problem = Ready && !recording ? TalkProblem(selectedTarget, seats, VoiceRules.Range) : null;
            bool muted = GameSettingsRuntime.Instance?.Current?.voice?.microphoneMuted ?? false;
            string blocked = microphone?.Problem ?? (muted ? "마이크 꺼짐 · ESC" : !Ready ? null : problem);
            string hintText = Time.unscaledTime < noticeUntil ? notice : null;
            DrawMic(blocked, hintText);
            DrawBanner();
        }
    }
}
