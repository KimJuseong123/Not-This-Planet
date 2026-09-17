using Metagame.Gameplay;
using Mirror;

namespace Metagame.Network
{
    /// <summary>기존 WorldSync 신원에 붙어 늦참자에게도 컷씬 시점과 공세 상태를 전한다.</summary>
    public sealed class Stage1SequenceSync : NetworkBehaviour
    {
        public struct State
        {
            public Stage1SequencePhase Phase;
            public double StartedAt;
            public int Revision;
            public bool Assault;
        }

        [SyncVar] State _state;

        void Update()
        {
            var session = GameSession.Instance;
            var sequence = session != null ? session.Sequence : null;
            if (sequence == null) return;
            if (isServer)
            {
                _state = new State { Phase = sequence.Phase, StartedAt = sequence.StartedAt,
                    Revision = sequence.Revision, Assault = sequence.AssaultEnabled };
            }
            else if (isClient)
                sequence.ApplyReplica(_state.Phase, _state.StartedAt, _state.Revision, _state.Assault);
        }
    }
}
