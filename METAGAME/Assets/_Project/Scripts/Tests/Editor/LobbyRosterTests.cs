using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Metagame.Network;
using Metagame.UI;
using NUnit.Framework;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 방 화면의 사람·AI 목록을 값으로 고정한다 (S15P21B202-713).
    ///
    /// <para>방은 방장이 1번이고 사람은 1, 2, 3… 순으로 들어온다. 닉네임은 비워 둔다 — 비면 userId 가
    /// 대신 들어가므로 줄 글자를 숫자로 읽을 수 있다.</para>
    /// </summary>
    public sealed class LobbyRosterTests
    {
        const long Host = 1;

        static RoomSnapshot Room(string status = "WAITING", int humans = 1, int? ai = 0,
                                 long[] ready = null, long[] offline = null, string id = "ROSTER")
        {
            var players = new Dictionary<long, PlayerSummary>();
            for (long user = Host; user < Host + humans; user++)
                players[user] = new PlayerSummary { ready = ready != null && ready.Contains(user) };
            return RoomSnapshot.From(new IncomingData
            {
                roomId = id, hostUserId = Host, status = status, players = players, aiCount = ai,
                disconnected = offline
            });
        }

        static RosterRemove[] Removes(List<RosterRow> rows) => rows.Select(row => row.Remove).ToArray();

        // ---------- × ----------

        /// <summary>방장은 자기 줄을 뺀 모든 줄에 × 를 본다. 사람은 내보내기, AI 는 수 하나 줄이기다.</summary>
        [Test]
        public void TheHostSeesAnXOnEveryRowButTheirOwn()
        {
            var rows = LobbyRoster.Rows(Room(humans: 3, ai: 1), Host);

            Assert.That(Removes(rows), Is.EqualTo(new[]
            {
                RosterRemove.None, RosterRemove.Kick, RosterRemove.Kick, RosterRemove.DropAi
            }));
            Assert.That(rows[0].IsLocal && rows[0].IsHost, Is.True, "첫 줄이 방장 자신이 아니다");
            Assert.That(rows.Where(row => row.Remove == RosterRemove.Kick).Select(row => row.UserId),
                Is.EqualTo(new long[] { 2, 3 }), "× 가 엉뚱한 사람을 내보낸다");
        }

        /// <summary>참가자에게는 × 가 없다 — 자기 줄에도, 남의 줄에도, AI 줄에도.</summary>
        [Test]
        public void OthersSeeNoX()
        {
            var rows = LobbyRoster.Rows(Room(humans: 2, ai: 2), 2);

            Assert.That(rows, Has.Count.EqualTo(4));
            Assert.That(Removes(rows), Has.All.EqualTo(RosterRemove.None), "방장이 아닌데 × 가 보인다");
        }

        /// <summary>
        /// 판이 돌고 있으면 방장에게도 × 가 없다 — 서버가 대기 중에만 받는다. 이전을 기다리는 동안
        /// (방 상태가 게임 중) 누르면 거절만 돌아온다.
        /// </summary>
        [TestCase("PLAYING")]
        [TestCase("")]
        public void NoXOutsideTheWaitingRoom(string status)
        {
            var rows = LobbyRoster.Rows(Room(status, humans: 2, ai: 1), Host);

            Assert.That(Removes(rows), Has.All.EqualTo(RosterRemove.None));
        }

        /// <summary>옛 서버는 내보내기도 AI 수도 모른다 — × 를 붙이면 모르는 메시지 오류만 돌아온다.</summary>
        [Test]
        public void AnOldServerGetsNoX()
        {
            var rows = LobbyRoster.Rows(Room(humans: 3, ai: null), Host);

            Assert.That(rows, Has.Count.EqualTo(3), "옛 서버 방에 AI 줄이 생겼다");
            Assert.That(Removes(rows), Has.All.EqualTo(RosterRemove.None));
        }

        /// <summary>
        /// AI 줄의 × 는 AI 수의 답을 기다리는 동안 막는다. 방금 넣은 AI 가 아직 목록에 없어서, 그 사이
        /// 누르면 옛 수에서 하나를 빼 보낸다 — 넣은 것이 통째로 사라진다. 내보내기는 AI 수와 상관없다.
        /// </summary>
        [Test]
        public void AnAiRowXWaitsForTheAnswerButAKickDoesNot()
        {
            var rows = LobbyRoster.Rows(Room(humans: 2, ai: 1), Host);
            RosterRow kick = rows[1];
            RosterRow drop = rows[2];

            Assert.That(LobbyRoster.CanPress(drop, aiPending: false), Is.True);
            Assert.That(LobbyRoster.CanPress(drop, aiPending: true), Is.False, "답을 기다리는데 AI 를 뺄 수 있다");
            Assert.That(LobbyRoster.CanPress(kick, aiPending: true), Is.True, "AI 요청이 내보내기를 막는다");
            Assert.That(LobbyRoster.CanPress(rows[0], aiPending: false), Is.False, "× 가 없는 줄이 눌린다");
        }

        /// <summary>
        /// 방금 내보낸 사람의 줄은 다음 방 상태까지 막는다. 두 번 누르면 두 번째는 이미 없는 사람이라
        /// "내보낼 수 없는 대상입니다" 가 뜬다. 다른 사람의 줄은 막지 않는다.
        /// </summary>
        [Test]
        public void AKickedRowWaitsForTheRoomToAnswer()
        {
            var rows = LobbyRoster.Rows(Room(humans: 3), Host);

            Assert.That(LobbyRoster.CanPress(rows[1], false, kicking: 2), Is.False, "같은 사람을 두 번 내보낸다");
            Assert.That(LobbyRoster.CanPress(rows[2], false, kicking: 2), Is.True, "한 사람을 내보내는 동안 다른 사람을 못 내보낸다");
            Assert.That(LobbyRoster.CanPress(rows[1], false), Is.True);
        }

        /// <summary>AI 줄의 × 는 수를 하나 줄여 보낸다. 어느 AI 줄에서 눌러도 같다 — AI 에게는 이름표가 없다.</summary>
        [TestCase(3, 2)]
        [TestCase(1, 0)]
        [TestCase(0, 0)]
        public void DroppingAnAiAsksForOneLess(int ai, int expected)
        {
            Assert.That(LobbyRoster.CountAfterDrop(Room(ai: ai)), Is.EqualTo(expected));
        }

        // ---------- 줄 ----------

        /// <summary>AI 줄의 수는 방의 AI 수와 같고, 사람 줄 뒤에 번호 순으로 온다.</summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void AiRowsFollowTheRoomsCount(int ai)
        {
            var rows = LobbyRoster.Rows(Room(humans: 1, ai: ai), 2);

            Assert.That(rows, Has.Count.EqualTo(1 + ai));
            Assert.That(rows[0].IsAi, Is.False, "사람 줄이 먼저 와야 한다");
            for (int n = 1; n <= ai; n++)
            {
                Assert.That(rows[n].IsAi, Is.True);
                Assert.That(rows[n].UserId, Is.Zero, "AI 줄에 사람 번호가 붙었다 — × 가 사람을 내보낼 수 있다");
                Assert.That(rows[n].Label, Is.EqualTo("AI 동료 " + n));
                Assert.That(rows[n].Tone, Is.EqualTo(RosterTone.Ai));
            }
        }

        /// <summary>사람 줄은 예전 그대로 읽힌다 — 이름 · 역할 · 상태, 내 줄에는 (나).</summary>
        [Test]
        public void HumanRowsReadAsBefore()
        {
            var rows = LobbyRoster.Rows(Room(humans: 3, ready: new long[] { 2 }, offline: new long[] { 3 }), 2);

            Assert.That(rows.Select(row => row.Label), Is.EqualTo(new[]
            {
                "1 · 방장 · 준비 중", "2 (나) · 참가자 · 준비 완료", "3 · 참가자 · 연결 끊김"
            }));
            Assert.That(rows.Select(row => row.Tone), Is.EqualTo(new[]
            {
                RosterTone.Normal, RosterTone.Ready, RosterTone.Offline
            }));
        }

        /// <summary>
        /// AI 의 준비 상태는 방장 화면에만 적는다. AI 는 방장 PC 에서 돈다 — 참가자 PC 의 실행기 상태를
        /// 적으면 그 AI 와 상관없는 말이 된다.
        /// </summary>
        [Test]
        public void OnlyTheHostSeesTheAiStatus()
        {
            RoomSnapshot room = Room(humans: 2, ai: 1);

            Assert.That(LobbyRoster.Rows(room, Host, AiRuntimeState.Ready)[2].Label,
                Is.EqualTo("AI 동료 1 · " + LobbyRoster.AiStatus(AiRuntimeState.Ready)));
            Assert.That(LobbyRoster.Rows(room, 2, AiRuntimeState.Ready)[2].Label, Is.EqualTo("AI 동료 1"),
                "참가자 화면에 자기 PC 의 실행기 상태가 AI 상태처럼 적혔다");
        }

        /// <summary>실행기 상태마다 다른 말이 나온다 — 같으면 방장이 준비 중인지 실패인지 모른다.</summary>
        [Test]
        public void EveryRuntimeStateSaysSomethingDifferent()
        {
            var said = new[] { AiRuntimeState.Missing, AiRuntimeState.Loading, AiRuntimeState.Ready, AiRuntimeState.Failed }
                .Select(LobbyRoster.AiStatus).ToArray();

            Assert.That(said, Has.All.Not.Empty);
            Assert.That(said.Distinct().Count(), Is.EqualTo(said.Length), string.Join(" / ", said));
        }

        /// <summary>
        /// AI 줄은 방의 준비 완료에 들어가지 않는다. 시작 버튼은 사람의 준비만 본다 — AI 는 준비 단추가
        /// 없고, 실행기가 준비 중이어도 판에 늦게 들어올 뿐이다.
        /// </summary>
        [Test]
        public void AiRowsDoNotCountTowardReady()
        {
            Assert.That(Room(humans: 2, ai: 2, ready: new long[] { 1, 2 }).AllReady, Is.True,
                "사람이 모두 준비했는데 AI 때문에 시작할 수 없다");
            Assert.That(Room(humans: 2, ai: 2, ready: new long[] { 1 }).AllReady, Is.False);
        }

        /// <summary>머리줄의 인원은 사람과 AI 를 합친다 — 정원 4 는 둘을 합친 수다.</summary>
        [TestCase(1, 0, "1/4명")]
        [TestCase(2, 1, "3/4명")]
        [TestCase(1, 3, "4/4명")]
        public void TheCountAddsHumansAndAi(int humans, int ai, string expected)
        {
            Assert.That(LobbyRoster.Count(Room(humans: humans, ai: ai)), Is.EqualTo(expected));
        }

        // ---------- 추가 ----------

        /// <summary>
        /// 막힌 이유는 정한 차례대로 하나다: 서버 → 방장 → 대기 중 → 자리 → 처리 중. 차례가 바뀌면 참가자가
        /// "자리가 찼다" 를 읽고 자리가 나면 자기가 넣을 수 있다고 여긴다.
        /// </summary>
        [TestCase(true, 1, "WAITING", null, false, "server")]
        [TestCase(false, 4, "PLAYING", null, true, "server")]      // 무엇보다 서버가 먼저다
        [TestCase(false, 4, "PLAYING", 0, true, "host")]
        [TestCase(true, 4, "PLAYING", 0, true, "playing")]
        [TestCase(true, 4, "WAITING", 0, true, "full")]
        [TestCase(true, 1, "WAITING", 3, true, "full")]
        [TestCase(true, 1, "WAITING", 2, true, "pending")]
        [TestCase(true, 1, "WAITING", 2, false, "")]
        [TestCase(true, 2, "WAITING", 1, false, "")]
        public void TheAddButtonSaysWhyItIsClosed(bool isHost, int humans, string status, int? ai,
                                                  bool pending, string expected)
        {
            AddState add = LobbyRoster.Add(Room(status, humans, ai), isHost ? Host : 2, pending);

            string reason = expected == "server" ? LobbyRoster.NeedsServerUpdate
                : expected == "host" ? LobbyRoster.HostOnly
                : expected == "playing" ? LobbyRoster.NotWhilePlaying
                : expected == "full" ? LobbyRoster.Full
                : expected == "pending" ? LobbyRoster.Waiting
                : null;
            Assert.That(add.Enabled, Is.EqualTo(reason == null));
            Assert.That(add.Reason, Is.EqualTo(reason));
        }

        /// <summary>화면에 뜨는 말 그대로를 적어 둔다 — 상수를 바꾸면 여기서 먼저 걸린다.</summary>
        [Test]
        public void TheReasonsReadAsPlanned()
        {
            Assert.That(LobbyRoster.NeedsServerUpdate, Is.EqualTo("서버 업데이트가 필요합니다"));
            Assert.That(LobbyRoster.HostOnly, Is.EqualTo("방장만 추가할 수 있습니다"));
            Assert.That(LobbyRoster.NotWhilePlaying, Is.EqualTo("게임 중에는 바꿀 수 없습니다"));
            Assert.That(LobbyRoster.Full, Is.EqualTo("자리가 모두 찼습니다 (최대 4명)"));
            Assert.That(LobbyRoster.Waiting, Is.EqualTo("처리 중입니다"));
            Assert.That(LobbyRoster.AddLabel, Is.EqualTo("AI 동료 추가"));
        }

        // ---------- 알림 ----------

        /// <summary>사람이 들어와 AI 가 빠졌을 때만 알린다. 말없이 줄이 사라지면 방장은 자기가 잘못 누른 줄 안다.</summary>
        [Test]
        public void AJoinThatPushesAnAiOutIsAnnounced()
        {
            Assert.That(LobbyRoster.DropNotice(Room(humans: 2, ai: 2), Room(humans: 3, ai: 1)),
                Is.EqualTo("사람이 들어와 AI 동료 하나를 뺐습니다"));
            // 사이의 방송을 건너뛰면(늦게 온 방송은 버린다) 한 번에 둘이 빠진 것으로 보인다.
            Assert.That(LobbyRoster.DropNotice(Room(humans: 1, ai: 3), Room(humans: 3, ai: 1)),
                Is.EqualTo("사람이 들어와 AI 동료 2명을 뺐습니다"));
        }

        [TestCase(1, 1, 2, 1)]   // 자리가 남아 AI 는 그대로
        [TestCase(1, 2, 1, 1)]   // 방장이 × 로 뺐다 — 방장이 한 일이다
        [TestCase(3, 1, 2, 1)]   // 사람이 나갔다
        [TestCase(2, 2, 1, 0)]   // 방장이 나가 수가 0 이 됐다 — 들어온 사람이 없다
        [TestCase(2, 1, 2, 1)]   // 준비 같은 다른 방송
        public void OtherChangesAreNotAnnounced(int humansBefore, int aiBefore, int humansAfter, int aiAfter)
        {
            Assert.That(LobbyRoster.DropNotice(Room(humans: humansBefore, ai: aiBefore),
                                               Room(humans: humansAfter, ai: aiAfter)), Is.Null);
        }

        /// <summary>다른 방으로 옮긴 것은 AI 가 빠진 것이 아니다. 처음 받은 방도 견줄 것이 없다.</summary>
        [Test]
        public void ANewRoomIsNotADrop()
        {
            Assert.That(LobbyRoster.DropNotice(Room(humans: 2, ai: 2, id: "A"), Room(humans: 3, ai: 1, id: "B")), Is.Null);
            Assert.That(LobbyRoster.DropNotice(null, Room(humans: 3, ai: 1)), Is.Null);
            Assert.That(LobbyRoster.DropNotice(Room(humans: 2, ai: 2), null), Is.Null);
        }

        /// <summary>
        /// 넣으려던 AI 가 자리 때문에 덜 들어왔으면 알린다. 추가를 누른 사이 사람이 들어온 경우다.
        ///
        /// <para>요청 뒤 처음 온 방송이 그 답이라는 보장은 없다 — 남의 준비 방송이 먼저 닿으면 아직 옛
        /// 수를 싣고 있다. 그때 알리면 틀린 말이다. 그래서 자리가 이미 찬 때만 말한다.</para>
        /// </summary>
        [Test]
        public void ARequestCutShortByTheCapIsAnnounced()
        {
            Assert.That(LobbyRoster.ShortfallNotice(3, Room(humans: 2, ai: 2)),
                Is.EqualTo("자리가 모자라 AI 동료를 더 넣지 못했습니다"));
            Assert.That(LobbyRoster.ShortfallNotice(2, Room(humans: 1, ai: 1)), Is.Null,
                "답보다 먼저 온 다른 방송을 거절로 읽었다");
            Assert.That(LobbyRoster.ShortfallNotice(2, Room(humans: 1, ai: 2)), Is.Null);
            Assert.That(LobbyRoster.ShortfallNotice(1, Room(humans: 1, ai: 2)), Is.Null, "줄이려던 요청이다");
            Assert.That(LobbyRoster.ShortfallNotice(null, Room(humans: 4, ai: 0)), Is.Null);
        }

        // ---------- 글자 ----------

        /// <summary>
        /// 목록이 낼 수 있는 글자가 모두 미리 그려진다 (<c>LobbyHud.WarmFont</c>). 빠진 글자는 처음 뜰 때
        /// 글꼴을 다시 굽느라 한 프레임 비어 보인다. "새 문구는 여기에도 더한다" 는 주석만으로는 잊힌다.
        /// </summary>
        [Test]
        public void EveryRosterTextIsWarmed()
        {
            var warm = (string)typeof(LobbyHud).GetMethod("WarmCharacters", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, null);

            var texts = new List<string>
            {
                LobbyRoster.AddLabel, LobbyRoster.RemoveLabel, LobbyRoster.NeedsServerUpdate, LobbyRoster.HostOnly,
                LobbyRoster.NotWhilePlaying, LobbyRoster.Full, LobbyRoster.Waiting,
                LobbyRoster.ShortfallNotice(3, Room(humans: 2, ai: 2)),
                LobbyRoster.DropNotice(Room(humans: 2, ai: 2), Room(humans: 3, ai: 1)),
                LobbyRoster.DropNotice(Room(humans: 1, ai: 3), Room(humans: 3, ai: 1)),
            };
            for (int humans = 1; humans <= 4; humans++)
                texts.Add(LobbyRoster.Count(Room(humans: humans, ai: 4 - humans)));
            RoomSnapshot room = Room(humans: 3, ai: 1, ready: new long[] { 2 }, offline: new long[] { 3 });
            foreach (AiRuntimeState state in new[] { AiRuntimeState.Missing, AiRuntimeState.Loading,
                                                     AiRuntimeState.Ready, AiRuntimeState.Failed })
                foreach (long local in new long[] { Host, 2 })
                    texts.AddRange(LobbyRoster.Rows(room, local, state).Select(row => row.Label));

            foreach (string text in texts)
            {
                string missing = new string(text.Where(c => warm.IndexOf(c) < 0).Distinct().ToArray());
                Assert.That(missing, Is.Empty, $"\"{text}\" 의 글자가 미리 그려지지 않는다");
            }
        }

        // ---------- 배선 ----------

        /// <summary>
        /// 방 화면이 <b>이 목록으로</b> 그린다. 값 검사는 규칙이 맞다는 것만 말한다 — 화면이 방 상태를 따로
        /// 읽어 줄을 만들면 × 규칙과 AI 줄이 두 벌이 되고, 둘은 언젠가 어긋난다.
        /// </summary>
        [Test]
        public void TheRoomScreenDrawsFromTheRoster()
        {
            string lobby = TestSource.Read("Runtime/UI/LobbyHud.cs");
            string draw = TestSource.Method(lobby, "private void DrawRoom(");

            Assert.That(draw, Does.Contain("LobbyRoster.Rows(room, network.UserId, AiHostSettings.RuntimeState, rosterRows);"),
                "방 목록을 LobbyRoster 로 만들지 않는다");
            Assert.That(draw, Does.Not.Contain("room.Players"),
                "사람 줄을 목록 밖에서 따로 그린다 — AI 줄과 × 규칙이 두 벌이 된다");
            Assert.That(draw, Does.Contain("LobbyRoster.CanPress(row, pending, kickAsked)"),
                "답을 기다리는 동안 AI 줄의 × 가, 내보내는 동안 그 사람의 × 가 눌린다");
            Assert.That(draw, Does.Contain("LobbyRoster.Add(room, network.UserId, pending);"),
                "추가 버튼이 막힌 이유를 모른다");
            Assert.That(draw, Does.Contain("AskAi(room.AiCount + 1);"), "추가 버튼이 하나씩 넣지 않는다");
            Assert.That(draw, Does.Contain("room.AllReady;"), "시작 조건이 사람의 준비만 보지 않는다");

            string remove = TestSource.Method(lobby, "private void RemoveRow(");
            Assert.That(remove, Does.Contain("network.KickPlayer(row.UserId);"), "사람 줄의 × 가 내보내지 않는다");
            Assert.That(remove, Does.Contain("kickAsked = row.UserId;"), "내보낸 사람의 × 가 다시 눌린다");
            Assert.That(remove, Does.Contain("AskAi(LobbyRoster.CountAfterDrop(room));"), "AI 줄의 × 가 수를 줄이지 않는다");

            string ask = TestSource.Method(lobby, "private void AskAi(");
            Assert.That(ask, Does.Contain("AiHostSettings.TrySetRequestedCount(count)"),
                "AI 수를 서버에 묻지 않는다 — 방장·대기 중 확인과 옛 서버 거르기를 건너뛴다");

            string onRoom = TestSource.Method(lobby, "private void OnRoomState(");
            Assert.That(onRoom, Does.Contain("LobbyRoster.DropNotice(shownRoom, room)"), "AI 가 빠져도 말하지 않는다");
            Assert.That(onRoom, Does.Contain("LobbyRoster.ShortfallNotice(aiAsked, room)"), "덜 들어와도 말하지 않는다");
            Assert.That(TestSource.Count(lobby, "network.RoomStateChanged += OnRoomState;"), Is.EqualTo(1),
                "알림이 방 상태를 듣지 않는다");
        }
    }
}
