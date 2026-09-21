using System.Collections.Generic;
using Metagame.Gameplay;
using Metagame.Network;
using Metagame.UI;
using Mirror;
using UnityEngine;

namespace Metagame.Bootstrap
{
    public struct CutsceneSkipRequest : NetworkMessage { public int Revision; }
    public struct CutsceneSkipQuery : NetworkMessage { }
    public struct CutsceneSkipState : NetworkMessage
    {
        public int Revision, Kind, Shot, Votes, Total;
        public double StartedAt;
        public bool Voted;
    }

    /// <summary>Lives with EndingSceneFlow across the Stage1/ending scene transfer.</summary>
    public sealed class CutsceneSkipFlow : MonoBehaviour
    {
        readonly CutsceneSkipVotes _votes = new();
        readonly List<int> _participants = new(4);
        readonly HashSet<NetworkConnectionToClient> _connections = new();
        CutsceneSkipState _state;
        bool _clientRegistered, _serverRegistered, _networked, _cursorOwned, _cursorWasLocked;
        GameSession _cursorSession;
        NetworkConnectionToServer _clientConnection;
        double _nextQuery;
        GUIStyle _style;
        public CutsceneSkipState State => _state;

        public bool RequestSkip()
        {
            if (!Visible || _state.Voted || _state.Total == 0
                || (GameSession.Instance != null && GameSession.Instance.InputSuppressed)) return false;
            int revision = _state.Revision;
            if (_networked)
            {
                NetworkClient.Send(new CutsceneSkipRequest { Revision = revision });
                return true;
            }
            RefreshAuthority();
            if (_state.Kind == 0 || !_votes.Vote(0, revision)) return false;
            Publish();
            RefreshAuthority();
            return true;
        }

        public void RegisterMessages(bool force = false)
        {
            if (!ReferenceEquals(_clientConnection, NetworkClient.connection))
            {
                _clientConnection = NetworkClient.connection;
                _clientRegistered = false;
                _nextQuery = 0;
                if (!NetworkServer.active) _state = default;
            }
            if (NetworkClient.active && (!_clientRegistered || force))
            {
                NetworkClient.ReplaceHandler<CutsceneSkipState>(state =>
                {
                    if (!NetworkServer.active && state.Revision >= _state.Revision) _state = state;
                });
                _clientRegistered = true;
            }
            if (NetworkServer.active && (!_serverRegistered || force))
            {
                if (!_serverRegistered)
                {
                    // A promoted host starts a fresh ballot; client state is not server vote ownership.
                    _state.Revision++;
                    _votes.Reset(_state.Revision);
                    _connections.Clear();
                }
                NetworkServer.ReplaceHandler<CutsceneSkipRequest>(ReceiveVote);
                NetworkServer.ReplaceHandler<CutsceneSkipQuery>((connection, _) =>
                {
                    if (!connection.isReady) return;
                    RefreshAuthority();
                    var state = _state;
                    state.Voted = _votes.HasVoted(connection.connectionId);
                    connection.Send(state);
                });
                _serverRegistered = true;
            }
            if (!NetworkClient.active) _clientRegistered = false;
            if (!NetworkServer.active) _serverRegistered = false;
            if (NetworkClient.active || NetworkServer.active) _networked = true;
        }

        CutsceneSkipState Context()
        {
            if (HostMigration.Pending || HostMigration.Taking || HostMigration.Settling) return default;
            var ending = EndingSceneFlow.Instance != null ? EndingSceneFlow.Instance.Playback : null;
            if (ending != null && ending.TryGetSkipTarget(out _))
                return new CutsceneSkipState { Kind = 2, Shot = (int)ending.Stage, StartedAt = ending.Snapshot.StartedAt };
            var session = GameSession.Instance;
            var sequence = session != null ? session.Sequence : null;
            if (session != null && !session.EndingActive && sequence != null && sequence.IsCinematic)
                return new CutsceneSkipState { Kind = 1, Shot = sequence.Revision, StartedAt = sequence.StartedAt };
            return default;
        }

        static bool SameShot(CutsceneSkipState a, CutsceneSkipState b) => a.Kind == b.Kind
            && a.Shot == b.Shot && a.StartedAt == b.StartedAt;

        void Update()
        {
            RegisterMessages();
            if (NetworkServer.active || !_networked) RefreshAuthority();
            else if (NetworkClient.isConnected && NetworkClient.ready
                && (!SameShot(Context(), _state) || _state.Total == 0)
                && Time.unscaledTimeAsDouble >= _nextQuery)
            {
                // A scene can finish loading after the server published its initial ballot.
                _nextQuery = Time.unscaledTimeAsDouble + .5d;
                NetworkClient.Send(new CutsceneSkipQuery());
            }
        }

        void RefreshAuthority()
        {
            var context = Context();
            bool changed = !SameShot(context, _state);
            if (changed)
            {
                context.Revision = _state.Revision + 1;
                _state = context;
                _votes.Reset(_state.Revision);
            }
            _participants.Clear();
            if (NetworkServer.active)
            {
                // A reconnect is a new participant even if the transport reuses its numeric ID.
                foreach (var connection in NetworkServer.connections.Values)
                    if (connection.isAuthenticated)
                    {
                        if (!_connections.Contains(connection) && _votes.HasVoted(connection.connectionId))
                        { _votes.Reset(_state.Revision); changed = true; }
                        _participants.Add(connection.connectionId);
                    }
                _connections.Clear();
                foreach (var connection in NetworkServer.connections.Values)
                    if (connection.isAuthenticated) _connections.Add(connection);
            }
            else _participants.Add(0);
            changed |= _votes.SetParticipants(_participants);
            if (_state.Kind != 0 && _votes.Unanimous)
            {
                if (_state.Kind == 1) GameSession.Instance.Sequence.TrySkip(_state.Shot);
                else EndingSceneFlow.Instance.TrySkip((Metagame.Gameplay.Parts.DemoEndingStage)_state.Shot);
                context = Context();
                context.Revision = _state.Revision + 1;
                _state = context;
                _votes.Reset(_state.Revision);
                _votes.SetParticipants(_participants);
                changed = true;
            }
            if (changed) Publish();
        }

        void ReceiveVote(NetworkConnectionToClient connection, CutsceneSkipRequest message)
        {
            if (!connection.isAuthenticated || !connection.isReady) return;
            RefreshAuthority(); // Reject a packet from a shot which just ended naturally.
            if (_state.Kind == 0 || !_votes.Vote(connection.connectionId, message.Revision)) return;
            Publish();
            RefreshAuthority();
        }

        void Publish()
        {
            _state.Votes = _votes.Count;
            _state.Total = _votes.Total;
            if (NetworkServer.active)
            {
                foreach (var connection in NetworkServer.connections.Values)
                {
                    if (!connection.isAuthenticated || !connection.isReady) continue;
                    var state = _state;
                    state.Voted = _votes.HasVoted(connection.connectionId);
                    connection.Send(state);
                }
                _state.Voted = NetworkServer.localConnection != null
                    && _votes.HasVoted(NetworkServer.localConnection.connectionId);
            }
            else _state.Voted = _votes.HasVoted(0);
        }

        bool Visible => !MatchRole.DedicatedServer && _state.Kind != 0 && SameShot(Context(), _state)
            && (!_networked || (NetworkClient.isConnected && NetworkClient.ready));

        void LateUpdate()
        {
            if (Visible)
            {
                var session = GameSession.Instance;
                if (!_cursorOwned)
                {
                    _cursorOwned = true;
                    _cursorSession = session;
                    _cursorWasLocked = session != null && session.CursorLocked;
                }
                if (session != null) session.SetCursor(false);
                else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
            else RestoreCursor();
        }

        void RestoreCursor()
        {
            if (!_cursorOwned) return;
            if (_cursorSession != null && _cursorWasLocked && !_cursorSession.EndingActive
                && !_cursorSession.InputSuppressed && !_cursorSession.WheelOpen
                && (_cursorSession.Exploration == null || !_cursorSession.Exploration.TabletOpen))
                _cursorSession.SetCursor(true);
            _cursorOwned = false;
            _cursorSession = null;
        }

        void OnGUI()
        {
            if (!Visible || (GameSession.Instance != null && GameSession.Instance.InputSuppressed)) return;
            int depth = GUI.depth;
            bool enabled = GUI.enabled;
            GUI.depth = -8000; // Above the Stage1 letterbox (-7000), below loading/failure (-9500).
            float scale = Mathf.Clamp(Screen.height / 900f, .65f, 1.5f);
            _style ??= new GUIStyle(GUI.skin.button);
            _style.fontSize = Mathf.RoundToInt(22 * scale);
            GUI.enabled = enabled && !_state.Voted && _state.Total > 0;
            var rect = new Rect(Screen.width - 234 * scale, Screen.height - 78 * scale, 210 * scale, 52 * scale);
            if (UiInteraction.Button(rect, $"SKIP ({_state.Votes}/{_state.Total})", _style))
                RequestSkip();
            GUI.enabled = enabled;
            GUI.depth = depth;
        }

        void OnDestroy()
        {
            RestoreCursor();
            if (_clientRegistered) NetworkClient.UnregisterHandler<CutsceneSkipState>();
            if (_serverRegistered)
            {
                NetworkServer.UnregisterHandler<CutsceneSkipRequest>();
                NetworkServer.UnregisterHandler<CutsceneSkipQuery>();
            }
        }
    }
}
