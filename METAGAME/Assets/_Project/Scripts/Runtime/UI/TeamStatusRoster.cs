using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Network;
using UnityEngine;

namespace Metagame.UI
{
    /// <summary>몸의 표시 여부와 무관하게 현재 참가자의 동기화된 상태를 읽는다.</summary>
    public sealed class TeamStatusRoster
    {
        public sealed class Member
        {
            public NetPlayer Player { get; internal set; }
            public PlayerRig Rig { get; internal set; }
            public string Name { get; internal set; }
            public Color Accent { get; internal set; }
            public bool Downed { get; internal set; }
            public int Appearance { get; internal set; }
            internal bool Seen;
        }

        readonly List<Member> _members = new();
        public IReadOnlyList<Member> Members => _members;
        internal bool Contains(Member member) => _members.Contains(member);

        public void Refresh(IReadOnlyList<NetPlayer> players, PlayerRig local, bool networkActive)
        {
            foreach (var member in _members) member.Seen = false;
            if (networkActive)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == null) continue;
                    var member = Find(player, null);
                    member.Rig = player.VisualRig;
                    member.Name = DisplayName(player.nickname);
                    member.Accent = player.TeamAccentColor;
                    // 원격 몸의 Health는 갱신되지 않을 수 있으므로 SyncVar를 직접 읽는다.
                    member.Downed = player.downed || player.health <= 0f;
                    member.Appearance = player.Appearance;
                }
            }
            else if (local != null)
            {
                var member = Find(null, local);
                member.Name = DisplayName(local.Nickname);
                member.Accent = local.TeamColor;
                member.Downed = local.Downed || local.IsDead;
                var appearance = local.Doll?.Root != null
                    ? local.Doll.Root.GetComponent<PlayerAppearance>() : null;
                member.Appearance = appearance != null ? appearance.Applied : 0;
            }
            _members.RemoveAll(member => !member.Seen);
            _members.Sort(Compare);
        }

        Member Find(NetPlayer player, PlayerRig local)
        {
            foreach (var member in _members)
                if (ReferenceEquals(member.Player, player)
                    && (player != null || ReferenceEquals(member.Rig, local)))
                { member.Seen = true; return member; }
            var added = new Member { Player = player, Rig = local, Seen = true };
            _members.Add(added);
            return added;
        }

        static int Compare(Member a, Member b)
        {
            if (a.Player == null || b.Player == null) return 0;
            int slot = a.Player.teamSlot.CompareTo(b.Player.teamSlot);
            return slot != 0 ? slot : a.Player.netId.CompareTo(b.Player.netId);
        }

        static string DisplayName(string value) => string.IsNullOrWhiteSpace(value) ? "비행사" : value;
    }
}
