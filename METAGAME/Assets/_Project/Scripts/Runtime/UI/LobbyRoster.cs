using System;
using System.Collections.Generic;
using Metagame.Network;

namespace Metagame.UI
{
    /// <summary>방 목록 줄의 × 가 하는 일 (S15P21B202-713).</summary>
    public enum RosterRemove : byte { None, Kick, DropAi }

    /// <summary>줄 글자색의 뜻. 색 자체는 화면(<see cref="LobbyHud"/>)이 고른다.</summary>
    public enum RosterTone : byte { Normal, Ready, Offline, Ai }

    /// <summary>방 목록 한 줄 (S15P21B202-713). 사람 줄과 AI 줄이 같은 모양이다.</summary>
    public readonly struct RosterRow
    {
        /// <summary>화면에 그대로 쓰는 한 줄.</summary>
        public string Label { get; }
        public bool IsAi { get; }
        public bool IsHost { get; }
        public bool IsLocal { get; }

        /// <summary>사람의 userId. AI 는 0 이다 — 방은 AI 를 수로만 안다.</summary>
        public long UserId { get; }
        public RosterRemove Remove { get; }
        public RosterTone Tone { get; }

        public RosterRow(string label, bool isAi, bool isHost, bool isLocal, long userId,
                         RosterRemove remove, RosterTone tone)
        {
            Label = label;
            IsAi = isAi;
            IsHost = isHost;
            IsLocal = isLocal;
            UserId = userId;
            Remove = remove;
            Tone = tone;
        }
    }

    /// <summary>"AI 동료 추가" 를 누를 수 있나. 못 누르면 그 이유를 함께 준다.</summary>
    public readonly struct AddState
    {
        public bool Enabled { get; }

        /// <summary>막힌 이유. 누를 수 있으면 null 이다.</summary>
        public string Reason { get; }

        AddState(bool enabled, string reason)
        {
            Enabled = enabled;
            Reason = reason;
        }

        public static AddState Open => new AddState(true, null);
        public static AddState Closed(string reason) => new AddState(false, reason);
    }

    /// <summary>
    /// 방 화면의 사람·AI 목록을 값으로 만든다 (S15P21B202-713). 그리는 것은 <see cref="LobbyHud"/> 다.
    ///
    /// <para><b>왜 순수 함수인가.</b> 누가 × 를 보고, 언제 추가가 막히고, 무엇을 알리는지가 이 화면의
    /// 규칙 전부다. IMGUI 안에 두면 씬 없이는 시험할 수 없고, 규칙이 그리는 코드 사이에 흩어진다.</para>
    ///
    /// <para><b>AI 에게 색을 붙이지 않는다.</b> 판에서 AI 는 높은 번호 자리부터 앉고(<see cref="AiSeatMath"/>),
    /// 몇 번 자리가 될지는 판이 뜰 때 사람이 어디에 앉느냐로 정해진다. 로비에서 색을 보여 주면 약속하지
    /// 못할 것을 약속한다. 그래서 AI 줄은 번호뿐이고, × 는 어느 줄에서 눌러도 수를 하나 줄일 뿐이다.</para>
    /// </summary>
    public static class LobbyRoster
    {
        public const string AddLabel = "AI 동료 추가";
        public const string RemoveLabel = "×";

        // 추가가 막힌 이유 — 앞의 것이 먼저다. 방장이 아닌 사람에게 "자리가 찼다" 를 보여 주면
        // 자리가 나면 자기가 넣을 수 있는 것으로 읽는다.
        public const string NeedsServerUpdate = "서버 업데이트가 필요합니다";
        public const string HostOnly = "방장만 추가할 수 있습니다";
        public const string NotWhilePlaying = "게임 중에는 바꿀 수 없습니다";
        public static readonly string Full = "자리가 모두 찼습니다 (최대 " + TeamSlots.Count + "명)";
        public const string Waiting = "처리 중입니다";

        public const string DroppedOne = "사람이 들어와 AI 동료 하나를 뺐습니다";
        public const string Shortfall = "자리가 모자라 AI 동료를 더 넣지 못했습니다";

        /// <summary>
        /// 목록 줄을 만든다 — 사람(userId 순) 다음에 AI "AI 동료 N" 이 온다.
        ///
        /// <para><b>× 는 방장의 화면에서, 대기 중에만, 자기 줄이 아닌 줄에만</b> 붙는다. 사람 줄의 × 는
        /// 내보내기, AI 줄의 × 는 수 하나 줄이기다. 옛 서버(<see cref="RoomSnapshot.SupportsAi"/> 거짓)는
        /// 둘 다 모르는 메시지로 거절하므로 붙이지 않는다.</para>
        ///
        /// <para><b>AI 의 준비 상태는 방장 화면에만 적는다.</b> AI 는 방장 PC 에서 돈다. 참가자 PC 의
        /// 실행기 상태는 그 AI 와 아무 상관이 없어서, 적으면 틀린 말이 된다.</para>
        ///
        /// <para>IMGUI 는 한 프레임에도 여러 번 그리므로 <paramref name="into"/> 를 넘기면 그 목록을
        /// 비우고 다시 채운다 — 매번 새 목록을 만들지 않는다.</para>
        /// </summary>
        /// <param name="hostRuntime">이 PC 의 AI 실행기 상태. 이 PC 가 방장일 때만 읽는다</param>
        public static List<RosterRow> Rows(RoomSnapshot room, long localUserId,
                                           AiRuntimeState hostRuntime = AiRuntimeState.Missing,
                                           List<RosterRow> into = null)
        {
            List<RosterRow> rows = into ?? new List<RosterRow>();
            rows.Clear();
            if (room == null) return rows;

            bool hostView = room.HostUserId == localUserId;
            bool canRemove = hostView && room.IsWaiting && room.SupportsAi;

            for (int i = 0; i < room.Players.Count; i++)
            {
                PlayerSnapshot player = room.Players[i];
                bool host = player.UserId == room.HostUserId;
                bool local = player.UserId == localUserId;
                string state = player.Disconnected ? "연결 끊김" : player.Ready ? "준비 완료" : "준비 중";
                RosterTone tone = player.Disconnected ? RosterTone.Offline
                    : player.Ready ? RosterTone.Ready : RosterTone.Normal;
                rows.Add(new RosterRow(
                    player.Nickname + (local ? " (나)" : "") + " · " + (host ? "방장" : "참가자") + " · " + state,
                    false, host, local, player.UserId,
                    canRemove && !local ? RosterRemove.Kick : RosterRemove.None, tone));
            }

            string status = hostView ? " · " + AiStatus(hostRuntime) : string.Empty;
            for (int n = 1; n <= room.AiCount; n++)
                rows.Add(new RosterRow("AI 동료 " + n + status, true, false, false, 0,
                    canRemove ? RosterRemove.DropAi : RosterRemove.None, RosterTone.Ai));
            return rows;
        }

        /// <summary>
        /// 방장 화면의 AI 줄에 붙는 짧은 상태.
        ///
        /// <para>AI 줄은 방의 준비 완료(<see cref="RoomSnapshot.AllReady"/>)에 들어가지 않는다 —
        /// 실행기가 준비 중이어도 게임은 시작할 수 있고, AI 는 준비되는 대로 판에 들어온다.</para>
        /// </summary>
        public static string AiStatus(AiRuntimeState state)
        {
            switch (state)
            {
                case AiRuntimeState.Ready: return "준비 완료";
                case AiRuntimeState.Loading: return "준비 중";
                case AiRuntimeState.Failed: return "실행 실패";
                default: return "준비 필요";
            }
        }

        /// <summary>머리줄의 인원 — 사람과 AI 를 합쳐 센다. 정원 4 는 둘을 합친 수이기 때문이다.</summary>
        public static string Count(RoomSnapshot room)
        {
            int total = room == null ? 0 : room.Players.Count + room.AiCount;
            return total + "/" + TeamSlots.Count + "명";
        }

        /// <summary>
        /// "AI 동료 추가" 를 누를 수 있나. 막혔으면 이유를 준다 — 버튼이 죽어 있기만 하면 고장으로 읽는다.
        ///
        /// <para>이유는 정한 차례대로 하나만 준다: 서버 → 방장 → 대기 중 → 자리 → 처리 중.
        /// 처리 중(<paramref name="pending"/>)에 막는 것은 두 번 누르기를 막으려는 것이 아니다 —
        /// 절대값을 보내므로 두 번 가도 결과가 같다. 답이 오기 전에는 화면의 수가 아직 옛 수라서,
        /// 누른 것이 먹었는지 알 수 없기 때문이다.</para>
        /// </summary>
        public static AddState Add(RoomSnapshot room, long localUserId, bool pending)
        {
            // 방이 없으면 방장도 없다. 방 화면은 방이 있을 때만 그리므로 실제로는 오지 않는다.
            if (room == null) return AddState.Closed(HostOnly);
            if (!room.SupportsAi) return AddState.Closed(NeedsServerUpdate);
            if (room.HostUserId != localUserId) return AddState.Closed(HostOnly);
            if (!room.IsWaiting) return AddState.Closed(NotWhilePlaying);
            if (!AiSeatMath.CanAdd(room.AiCount, room.Players.Count)) return AddState.Closed(Full);
            if (pending) return AddState.Closed(Waiting);
            return AddState.Open;
        }

        /// <summary>
        /// 이 × 를 지금 누를 수 있나.
        ///
        /// <para>AI 줄의 × 는 AI 수의 답을 기다리는 동안(<paramref name="aiPending"/>) 막는다 — 방금 넣은
        /// AI 가 아직 화면에 없어서, 그 사이 누르면 옛 수에서 하나를 빼 보낸다(넣은 것이 통째로 사라진다).</para>
        ///
        /// <para>사람 줄의 × 는 AI 수와 상관없다. 다만 방금 내보낸 사람(<paramref name="kicking"/>)의 줄은
        /// 다음 방 상태가 올 때까지 막는다 — 두 번 누르면 두 번째는 이미 없는 사람이라 서버가 거절하고,
        /// "내보낼 수 없는 대상입니다" 가 뜬다. 0 은 아무도 아니다.</para>
        /// </summary>
        public static bool CanPress(RosterRow row, bool aiPending, long kicking = 0)
        {
            switch (row.Remove)
            {
                case RosterRemove.Kick: return kicking == 0 || row.UserId != kicking;
                case RosterRemove.DropAi: return !aiPending;
                default: return false;
            }
        }

        /// <summary>AI 줄의 × 를 누르면 보낼 수. 어느 줄에서 눌러도 같다 — AI 에게는 이름표가 없다.</summary>
        public static int CountAfterDrop(RoomSnapshot room)
            => room == null || room.AiCount <= 0 ? 0 : room.AiCount - 1;

        /// <summary>
        /// 사람이 들어와 AI 가 빠졌으면 그렇다고 알린다. 아니면 null.
        ///
        /// <para><b>왜 알리나.</b> 서버는 사람 + AI 가 4 를 넘지 않게 AI 를 줄인다(<c>Room.tryJoin</c>).
        /// 말없이 줄이 하나 사라지면 방장은 자기가 잘못 누른 것으로 읽는다. 사람이 나가도 AI 는 되살아나지
        /// 않으므로 방장이 다시 넣어야 한다는 것도 이 알림으로 안다.</para>
        ///
        /// <para>늦게 온 방송은 버려지므로(<see cref="NetworkSession.AcceptsRoomState"/>) 사이의 방송을
        /// 건너뛰어 한 번에 둘이 빠진 것으로 보일 수 있다. 그때는 빠진 수를 적는다.</para>
        /// </summary>
        public static string DropNotice(RoomSnapshot before, RoomSnapshot after)
        {
            if (before == null || after == null) return null;
            if (!string.Equals(before.RoomId, after.RoomId, StringComparison.OrdinalIgnoreCase)) return null;
            int dropped = before.AiCount - after.AiCount;
            if (after.Players.Count <= before.Players.Count || dropped <= 0) return null;
            return dropped == 1 ? DroppedOne : "사람이 들어와 AI 동료 " + dropped + "명을 뺐습니다";
        }

        /// <summary>
        /// 요청한 수보다 적게 돌아왔고, 그 까닭이 자리라면 알린다. 아니면 null.
        ///
        /// <para>방장이 추가를 누른 사이 사람이 들어오면 서버가 줄여서 받는다. 누른 것이 먹지 않은 것처럼
        /// 보이므로 이유를 말한다.</para>
        ///
        /// <para><b>자리가 찬 때만 말한다.</b> 요청 뒤 처음 온 방송이 그 요청의 답이라는 보장이 없다 —
        /// 다른 사람의 준비 방송이 먼저 닿을 수 있다. 그 방송은 아직 옛 수를 싣고 있어서 "적게 왔다" 로만
        /// 보면 틀린 알림이 뜬다. 자리가 이미 찼다면 서버도 요청을 그만큼 줄일 수밖에 없다.</para>
        /// </summary>
        public static string ShortfallNotice(int? asked, RoomSnapshot after)
        {
            if (!asked.HasValue || after == null || after.AiCount >= asked.Value) return null;
            if (AiSeatMath.CanAdd(after.AiCount, after.Players.Count)) return null;
            return Shortfall;
        }
    }
}
