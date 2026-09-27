using System;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metagame.Network
{
    public struct TeamRetryRequest : NetworkMessage { public string Ballot; }
    public struct TeamRetryQuery : NetworkMessage { }
    public struct TeamRetryState : NetworkMessage
    {
        public string Ballot;
        public int Votes, Total;
        public bool Available, Voted, Restarting;
    }

    /// <summary>서버가 현재 연결의 만장일치를 판정한다. 씬 전환 중에도 메시지를 수신한다.</summary>
    public sealed class TeamRetryFlow : MonoBehaviour
    {
        public static TeamRetryFlow Instance { get; private set; }
        public static readonly string Stage1Path = StageCatalog.SceneFolder + StageCatalog.Stage1SceneName + ".unity";
        readonly HashSet<NetworkConnectionToClient> _participants = new();
        readonly HashSet<NetworkConnectionToClient> _votes = new();
        readonly HashSet<NetworkConnectionToClient> _next = new();
        GameSession _session;
        TeamRetryState _state;
        NetworkConnectionToServer _clientConnection;
        bool _clientRegistered, _serverRegistered, _networked, _requestPending;
        double _nextQuery;
        public TeamRetryState State => _state;
        public bool CanVote => ContextAvailable() && _state.Available && !_state.Voted && !_requestPending
            && (!_networked || (NetworkClient.isConnected && NetworkClient.ready));

        public static TeamRetryFlow Ensure()
        {
            if (Instance == null) new GameObject("Team retry vote").AddComponent<TeamRetryFlow>();
            Instance.RegisterMessages(true);
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        bool ContextAvailable()
        {
            var session = GameSession.Instance;
            return session != null && StageCatalog.IsStageScene(session.gameObject.scene.name)
                && !session.EndingActive && !session.Cleared && !session.ScriptedInput
                && !HostMigration.Pending && !HostMigration.Taking && !HostMigration.Settling
                && !MapRecoveryBridge.IsReloading
                && (session.Recovery == null || !session.Recovery.ResetPending);
        }

        void RegisterMessages(bool force = false)
        {
            if (!ReferenceEquals(_clientConnection, NetworkClient.connection))
            {
                _clientConnection = NetworkClient.connection;
                _clientRegistered = false;
                _requestPending = false;
                _nextQuery = 0;
                if (!NetworkServer.active) _state = default;
            }
            if (NetworkClient.active && (!_clientRegistered || force))
            {
                NetworkClient.ReplaceHandler<TeamRetryState>(ReceiveState);
                _clientRegistered = true;
            }
            if (NetworkServer.active && (!_serverRegistered || force))
            {
                // 호스트 이전은 새 투표다. 클라이언트가 받은 표를 서버 권한으로 승격하지 않는다.
                if (!_serverRegistered) ResetBallot();
                NetworkServer.ReplaceHandler<TeamRetryRequest>(ReceiveVote);
                NetworkServer.ReplaceHandler<TeamRetryQuery>((connection, _) =>
                {
                    if (!connection.isReady) return;
                    RefreshAuthority();
                    SendState(connection);
                });
                _serverRegistered = true;
            }
            if (!NetworkClient.active) _clientRegistered = false;
            if (!NetworkServer.active) _serverRegistered = false;
            if (NetworkServer.active || NetworkClient.active) _networked = true;
        }

        void ResetBallot()
        {
            _votes.Clear();
            _participants.Clear();
            _state = new TeamRetryState { Ballot = Guid.NewGuid().ToString("N") };
            _requestPending = false;
        }

        void Update()
        {
            RegisterMessages();
            // 로비에 돌아온 뒤 새 솔로 게임도 정상적으로 시작할 수 있게 수명을 끝낸다.
            if (GameSession.Instance == null && !StageCatalog.IsStageScene(SceneManager.GetActiveScene().name)
                && !NetworkServer.isLoadingScene && !NetworkClient.isLoadingScene)
            { Destroy(gameObject); return; }
            if (NetworkServer.active || !_networked) RefreshAuthority();
            else if (NetworkClient.isConnected && NetworkClient.ready && Time.unscaledTimeAsDouble >= _nextQuery)
            {
                // 늦은 씬 로딩/접속과 요청 응답을 복구한다. 프레임별 패킷은 보내지 않는다.
                _nextQuery = Time.unscaledTimeAsDouble + 1d;
                NetworkClient.Send(new TeamRetryQuery());
            }
        }

        void RefreshAuthority()
        {
            bool changed = false;
            if (_session != GameSession.Instance)
            {
                _session = GameSession.Instance;
                ResetBallot();
                changed = true;
            }
            bool available = ContextAvailable() && !_state.Restarting;
            if (_state.Available != available)
            {
                if (!available && !_state.Restarting) ResetBallot();
                _state.Available = available;
                changed = true;
            }
            _next.Clear();
            if (NetworkServer.active)
                foreach (var connection in NetworkServer.connections.Values)
                    if (connection.isAuthenticated) _next.Add(connection);
            // 접속 객체 자체가 표의 주인이다. 같은 숫자 ID로 재접속해도 이전 표를 받지 않는다.
            if (!_participants.SetEquals(_next))
            {
                _participants.Clear();
                _participants.UnionWith(_next);
                _votes.IntersectWith(_participants);
                changed = true;
            }
            _state.Total = NetworkServer.active ? _participants.Count : 1;
            _state.Votes = _votes.Count;
            if (changed) Publish();
            if (available && NetworkServer.active && _state.Total > 0 && _state.Votes == _state.Total)
                RestartTogether();
        }

        public bool RequestRetry()
        {
            if (!CanVote) return false;
            if (_networked)
            {
                _requestPending = true;
                NetworkClient.Send(new TeamRetryRequest { Ballot = _state.Ballot });
                return true;
            }
            RefreshAuthority();
            return _state.Available && RestartTogether();
        }

        void ReceiveVote(NetworkConnectionToClient connection, TeamRetryRequest message)
        {
            if (!connection.isAuthenticated || !connection.isReady || connection.identity == null) return;
            RefreshAuthority();
            if (!_state.Available || message.Ballot != _state.Ballot || !_participants.Contains(connection)) return;
            if (_votes.Add(connection)) Publish();
            RefreshAuthority();
        }

        bool RestartTogether()
        {
            if (_state.Restarting) return false;
            // 플레이어 객체를 제거하기 전에 상태를 전송한다. 같은 reliable 채널의 SceneMessage보다 앞선다.
            if (MapRecoveryBridge.TryReloadScene(Stage1Path, () =>
            {
                _state.Restarting = true;
                _state.Available = false;
                Publish();
                NetworkSession.Instance?.RestartRoomTimer();
            })) return true;
            ResetBallot();
            Publish();
            return false;
        }

        void ReceiveState(TeamRetryState state)
        {
            _requestPending = false;
            if (NetworkServer.active) return;
            if (state.Restarting && !_state.Restarting) NetworkSession.Instance?.RestartRoomTimer();
            _state = state;
        }

        void SendState(NetworkConnectionToClient connection)
        {
            if (!connection.isAuthenticated || !connection.isReady) return;
            var state = _state;
            state.Voted = _votes.Contains(connection);
            connection.Send(state);
        }

        void Publish()
        {
            _state.Votes = _votes.Count;
            if (NetworkServer.active)
            {
                foreach (var connection in NetworkServer.connections.Values) SendState(connection);
                _state.Voted = NetworkServer.localConnection != null && _votes.Contains(NetworkServer.localConnection);
            }
            _requestPending = false;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (_clientRegistered) NetworkClient.UnregisterHandler<TeamRetryState>();
            if (_serverRegistered)
            {
                NetworkServer.UnregisterHandler<TeamRetryRequest>();
                NetworkServer.UnregisterHandler<TeamRetryQuery>();
            }
            Instance = null;
        }
    }
}
