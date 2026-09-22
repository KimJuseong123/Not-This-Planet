using System.Collections;
using Metagame.Bootstrap;
using Metagame.Gameplay.Hazards;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Metagame.Gameplay.Tests
{
    public sealed class TeamRetrySceneTests
    {
        [UnityTest, Category("Heavy"), Timeout(180000)]
        public IEnumerator SoloAssaultReloadsAuthoredStageTwiceWithFreshWorldAndPlayer()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync(TeamRetryFlow.Stage1Path, LoadSceneMode.Single);
            yield return null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var session = GameSession.Instance;
                Assert.That(session, Is.Not.Null);
                Assert.That(session.LocalPlayer, Is.Not.Null);
                var sequence = session.Sequence;
                Assert.That(sequence, Is.Not.Null);
                if (sequence.Phase == Stage1SequencePhase.Waiting) sequence.TryBeginEntry();
                if (sequence.IsCinematic) sequence.TrySkip(sequence.Revision);
                Assert.That(sequence.TryBeginAssault(), Is.True);
                sequence.TrySkip(sequence.Revision);
                Assert.That(sequence.AssaultEnabled, Is.True);
                var oldPlayer = session.LocalPlayer;
                var flow = TeamRetryFlow.Instance;
                // 가산 연출 로딩이 끝날 때까지 실제 프레임을 진행한다.
                yield return null;
                Assert.That(flow.RequestRetry(), Is.True);
                double deadline = Time.realtimeSinceStartupAsDouble + 60d;
                while ((GameSession.Instance == session || MapRecoveryBridge.IsReloading)
                    && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.That(GameSession.Instance != session, Is.True, "A fresh GameSession must replace the old run.");
                yield return null;
                var fresh = GameSession.Instance;
                Assert.That(fresh.LocalPlayer != oldPlayer, Is.True);
                Assert.That(fresh.Sequence.AssaultEnabled, Is.False);
                Assert.That(Object.FindAnyObjectByType<MeteorSpawner>().AssaultEnabled, Is.False);
                Assert.That(fresh.Sequence.Phase, Is.EqualTo(Stage1SequencePhase.Waiting).Or.EqualTo(Stage1SequencePhase.Entry));
                Assert.That(fresh.InputSuppressed, Is.False);
                Assert.That(fresh.CheckpointIndex, Is.Zero);
                Assert.That(fresh.Elapsed, Is.LessThan(5f));
                Assert.That(flow.State.Votes, Is.Zero);
                Assert.That(Object.FindObjectsByType<SpaceBootstrap>().Length, Is.EqualTo(1));
                foreach (var clamp in PartClamp.All)
                    if (clamp != null && clamp.gameObject.scene.path == TeamRetryFlow.Stage1Path)
                        Assert.That(clamp.IsReleased, Is.False, "Released clamps must not survive the new run.");
            }
            yield return new ExitPlayMode();
        }
    }
}
