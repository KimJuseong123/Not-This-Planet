using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    public enum DemoEndingStage { Playing, Boarding, Launching, Photograph, EndTitle, Lobby }

    /// <summary>서버가 확정한 완성 순간. 연출은 이 스냅샷과 공통 시각만 읽는다.</summary>
    [Serializable]
    public sealed class DemoEndingSnapshot
    {
        public double StartedAt;
        public double PlayTimeSeconds;
        public DemoEndingPart[] Parts;
        public DemoEndingPlayer[] Players;
    }

    [Serializable]
    public sealed class DemoEndingPart
    {
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public WeldBead.ChainState[] Beads;
        public bool[] HelpfulBeads;
    }

    [Serializable]
    public sealed class DemoEndingPlayer
    {
        public string Name;
        public Color Suit;
        public Color Accent;
    }

    /// <summary>비드 연결 성공과 되돌릴 수 없는 데모 엔딩. Mirror/UI 의존성은 없다.</summary>
    public sealed class DemoEnding : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/Stage1_Ending.unity";
        public const float GreetingSeconds = 2f;
        public const float BoardingSeconds = 8f;
        public const float LaunchSeconds = 5f;
        public const float PhotographSeconds = 5f;
        public const float FadeSeconds = 1.5f;
        public const float EndTitleSeconds = 3f;
        public const float TitleStartsAt = BoardingSeconds + LaunchSeconds + PhotographSeconds;
        public const float TotalSeconds = TitleStartsAt + EndTitleSeconds;

        GameSession _session;
        SpaceshipPart[] _parts;
        Func<double> _clock;
        Func<DemoEndingPlayer[]> _players;
        Func<bool> _authority;
        Func<double> _playTime;
        readonly List<SpaceshipPart> _visited = new(4);

        public DemoEndingSnapshot Snapshot { get; private set; }
        public bool Active => Snapshot != null;
        public float Elapsed => Active ? Mathf.Max(0f, (float)(_clock() - Snapshot.StartedAt)) : 0f;
        public DemoEndingStage Stage => Active ? StageAt(Elapsed) : DemoEndingStage.Playing;
        public bool Finished => Stage == DemoEndingStage.Lobby;
        public IReadOnlyList<SpaceshipPart> Parts => _parts;
        public bool PlaybackStarted => Active && _clock() >= Snapshot.StartedAt;

        /// <summary>엔딩 전용 씬에서는 게임 세션이나 물리 파츠 없이 스냅샷만 재생한다.</summary>
        public void ConfigurePlayback(DemoEndingSnapshot snapshot, Func<double> clock)
        {
            Snapshot = snapshot;
            _clock = clock;
            _parts = Array.Empty<SpaceshipPart>();
            enabled = false;
        }

        public void Configure(GameSession session, SpaceshipPart[] parts, Func<bool> authority,
            Func<double> clock, Func<DemoEndingPlayer[]> players, Func<double> playTime = null)
        {
            _session = session;
            _parts = parts;
            _authority = authority;
            _clock = clock;
            _players = players;
            _playTime = playTime;
            session.Ending = this;
        }

        public static DemoEndingStage StageAt(float elapsed)
        {
            if (elapsed < BoardingSeconds) return DemoEndingStage.Boarding;
            if (elapsed < BoardingSeconds + LaunchSeconds) return DemoEndingStage.Launching;
            if (elapsed < TitleStartsAt) return DemoEndingStage.Photograph;
            return elapsed < TotalSeconds ? DemoEndingStage.EndTitle : DemoEndingStage.Lobby;
        }

        /// <summary>네 종류가 정확히 하나씩 있고 살아 있는 비드 접합만으로 연결되는가.</summary>
        public static bool AreConnected(IReadOnlyList<SpaceshipPart> parts, List<SpaceshipPart> visited)
        {
            visited.Clear();
            if (parts == null || parts.Count != PartSpec.All.Length) return false;
            for (var type = 0; type < PartSpec.All.Length; type++)
            {
                var count = 0;
                for (var i = 0; i < parts.Count; i++)
                    if (parts[i] != null && parts[i].name == PartSpec.All[type].Name) count++;
                if (count != 1) return false;
            }

            visited.Add(parts[0]);
            for (var i = 0; i < visited.Count; i++)
                foreach (var link in PartAssemblyLink.All)
                {
                    if (link == null || !link.IsIntact || link.BeadWeldRatio <= 0f) continue;
                    var next = link.Across(visited[i]);
                    if (next == null || visited.Contains(next)) continue;
                    for (var p = 0; p < parts.Count; p++)
                        if (parts[p] == next) { visited.Add(next); break; }
                }
            return visited.Count == parts.Count;
        }

        void LateUpdate() => TryComplete();

        public bool TryComplete()
        {
            if (!_authority() || Active || _session == null || _session.Cleared
                || (_session.Recovery != null && _session.Recovery.ResetPending)
                || !AreConnected(_parts, _visited)) return false;
            var players = _players();
            if (players == null || players.Length == 0) return false;
            var snapshot = new DemoEndingSnapshot
            {
                StartedAt = _clock(), PlayTimeSeconds = Math.Max(0d, _playTime?.Invoke() ?? _session.Elapsed),
                Players = players, Parts = new DemoEndingPart[_parts.Length]
            };
            for (var i = 0; i < _parts.Length; i++)
            {
                var part = _parts[i];
                var bead = part.GetComponent<WeldBead>();
                var chains = new WeldBead.ChainState[bead != null ? bead.ChainCount : 0];
                var helpful = new bool[chains.Length];
                for (var b = 0; b < chains.Length; b++)
                { chains[b] = bead.ReadChain(b); helpful[b] = bead.IsChainContributing(b); }
                snapshot.Parts[i] = new DemoEndingPart
                {
                    Name = part.name, Position = part.transform.position,
                    Rotation = part.transform.rotation, Scale = part.transform.lossyScale, Beads = chains, HelpfulBeads = helpful
                };
            }
            ApplySnapshot(snapshot);
            _session.Clear();
            Debug.Log("[DemoEnding] 네 핵심 파츠 비드 연결 확정. 전원 엔딩 시작.");
            return true;
        }

        public void ApplySnapshot(DemoEndingSnapshot snapshot)
        {
            if (Active || snapshot == null || snapshot.Parts == null || snapshot.Players == null) return;
            Snapshot = snapshot;
            _session.SetInputSuppressed(true);
            _session.SetCursor(false);
            foreach (var link in PartAssemblyLink.All)
                if (link != null && link.IsIntact) link.SealForLaunch();
            foreach (var part in _parts)
            {
                if (part == null) continue;
                var body = part.GetComponent<Rigidbody>();
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true;
            }
            for (var i = LowGravityField.All.Count - 1; i >= 0; i--)
                if (LowGravityField.All[i] != null) LowGravityField.All[i].SetActive(false);
            foreach (var spawner in FindObjectsByType<Hazards.MeteorSpawner>())
                spawner.SetPaused(true);
            foreach (var sound in FindObjectsByType<Metagame.Gameplay.Audio.SoundRuntime>()) sound.enabled = false;
            foreach (var rig in PlayerRig.All)
            {
                if (rig == null || rig.Doll == null) continue;
                rig.SetInput(default);
                rig.DropEquipment();
                rig.Teleport(rig.FootPosition, Quaternion.LookRotation(rig.FacingDirection));
                foreach (var body in rig.Doll.Bodies) if (body != null) body.isKinematic = true;
            }
        }
    }
}
