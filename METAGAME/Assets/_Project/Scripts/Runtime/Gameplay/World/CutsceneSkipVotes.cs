using System.Collections.Generic;

namespace Metagame.Gameplay
{
    /// <summary>One vote per current participant, scoped to a single cinematic revision.</summary>
    public sealed class CutsceneSkipVotes
    {
        readonly HashSet<int> _participants = new();
        readonly HashSet<int> _votes = new();
        readonly HashSet<int> _next = new();
        public int Revision { get; private set; }
        public int Total => _participants.Count;
        public int Count => _votes.Count;
        public bool Unanimous => Total > 0 && Count == Total;
        public bool HasVoted(int participant) => _votes.Contains(participant);

        public void Reset(int revision)
        {
            Revision = revision;
            _votes.Clear();
            _participants.Clear();
        }

        public bool SetParticipants(IEnumerable<int> participants)
        {
            _next.Clear();
            _next.UnionWith(participants);
            if (_participants.SetEquals(_next)) return false;
            _participants.Clear();
            _participants.UnionWith(_next);
            _votes.IntersectWith(_participants);
            return true;
        }

        public bool Vote(int participant, int revision) => revision == Revision
            && _participants.Contains(participant) && _votes.Add(participant);
    }
}
