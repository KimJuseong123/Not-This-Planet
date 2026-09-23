using System;
using System.Collections;
using System.Collections.Generic;
using Metagame.Core;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using Mirror;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Metagame.Gameplay.Tests
{
    public sealed class Performance697Tests
    {
        [UnityTearDown]
        public IEnumerator CleanupRuntime()
        {
            if (!Application.isPlaying) yield break;
            yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Category("Heavy")]   // 느려서 MR 에서 뺀다 (S15P21B202-731)
        [UnityTest]
        public IEnumerator RuntimeRegistryTracksLateSpawnDisableAndDestruction()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Assert.That(PartOutlineTargets.Collect(), Is.False);
            var lower = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var upper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lower.name = PartSpec.All[0].Name;
            upper.name = PartSpec.All[1].Name;
            upper.transform.position = Vector3.up;
            var a = lower.AddComponent<SpaceshipPart>();
            var b = upper.AddComponent<SpaceshipPart>();
            var joint = upper.AddComponent<FixedJoint>();
            joint.connectedBody = lower.GetComponent<Rigidbody>();
            var link = upper.AddComponent<PartAssemblyLink>();
            link.BindBeadWeld(b, a, joint);
            link.SetBeadWeldRatio(.1f);
            Assert.That(PartOutlineTargets.Collect(), Is.True);
            b.enabled = false;
            Assert.That(PartOutlineTargets.Collect(), Is.False);
            b.enabled = true;
            Assert.That(PartOutlineTargets.Collect(), Is.True);
            Object.Destroy(upper);
            yield return null;
            Assert.That(PartOutlineTargets.Collect(), Is.False);
        }

        [Test]
        public void BurstPreservesActionsAndUsesLatestMovement()
        {
            var input = new RemoteInputBuffer();
            input.Receive(new InputFrame { Seq = 1, TransportAction = true, Move = Vector2.up });
            input.Receive(new InputFrame { Seq = 2 });
            input.Receive(new InputFrame { Seq = 3, PingPressed = true, FieldToggle = true, Move = Vector2.right });
            input.Receive(new InputFrame { Seq = 4, Move = Vector2.left });
            var first = input.Consume();
            Assert.That(first.TransportAction, Is.True);
            Assert.That(first.Move, Is.EqualTo(Vector2.left));
            Assert.That(first.Seq, Is.EqualTo(4));
            Assert.That(input.Consume().TransportAction, Is.False);
            var second = input.Consume();
            Assert.That(second.PingPressed && second.FieldToggle, Is.True);
            Assert.That(input.Consume().PingPressed, Is.False);
            Assert.That(input.Consume().PingPressed, Is.False);
        }

        [Test]
        public void DuplicateHeldActionAndOutOfOrderPacketsDoNotReplay()
        {
            var input = new RemoteInputBuffer();
            input.Receive(new InputFrame { Seq = uint.MaxValue, TransportAction = true });
            input.Receive(new InputFrame { Seq = 1, TransportAction = true });
            input.Receive(new InputFrame { Seq = uint.MaxValue - 1 });
            Assert.That(input.Consume().TransportAction, Is.True);
            Assert.That(input.Consume().TransportAction, Is.False);
            Assert.That(input.Consume().TransportAction, Is.False);
            input.Receive(new InputFrame { Seq = 2 });
            input.Receive(new InputFrame { Seq = 3, TransportAction = true });
            Assert.That(input.Consume().TransportAction, Is.True);
        }

        [Test]
        public void FloodIsBoundedAndDoesNotBacklogMovement()
        {
            var input = new RemoteInputBuffer();
            for (uint i = 1; i <= 100; i++)
                input.Receive(new InputFrame { Seq = i, PingPressed = i % 2 == 1, Move = Vector2.right });
            Assert.That(input.OverflowCount, Is.EqualTo(18));
            Assert.That(input.Consume().Move, Is.EqualTo(Vector2.right));
        }

        [Test]
        public void BlockedActionsDoNotReplayAfterCinematic()
        {
            var input = new RemoteInputBuffer();
            input.Receive(new InputFrame { Seq = 1, TransportAction = true });
            input.Receive(new InputFrame { Seq = 2 });
            input.Receive(new InputFrame { Seq = 3, PingPressed = true });
            input.DiscardActions();
            Assert.That(input.Consume().TransportAction, Is.False);
            Assert.That(input.Consume().PingPressed, Is.False);
            input.Receive(new InputFrame { Seq = 4 });
            input.Receive(new InputFrame { Seq = 5, PingPressed = true });
            Assert.That(input.Consume().PingPressed, Is.True);
        }

        [Test]
        public void VoiceWaitsForMatchingGrantAndDoesNotBurstAfterStall()
        {
            var transfer = new AiVoiceTransfer();
            var id = transfer.Begin(new byte[AiVoicePacket.MaxBytes], 0);
            Assert.That(transfer.TryNext(0, out _, out _), Is.False);
            transfer.Approve(id + 1, true, 0);
            Assert.That(transfer.TryNext(0, out _, out _), Is.False);
            transfer.Approve(id, true, 0);
            Assert.That(transfer.TryNext(0, out var chunk, out var last), Is.True);
            Assert.That(chunk.Count, Is.EqualTo(AiVoiceTransfer.ChunkBytes));
            Assert.That(last, Is.False);
            Assert.That(transfer.TryNext(0, out _, out _), Is.False);
            Assert.That(transfer.TryNext(3, out _, out _), Is.True);
            Assert.That(transfer.TryNext(3, out _, out _), Is.False);
        }

        [Test]
        public void VoiceReassemblesExactlyWithinDeadlineWithoutChunkCopies()
        {
            var bytes = AiVoicePacket.Encode(new float[AiVoicePacket.Rate * AiVoicePacket.Seconds], AiVoicePacket.Rate, 1);
            var transfer = new AiVoiceTransfer();
            transfer.Approve(transfer.Begin(bytes, 0), true, 0);
            int received = 0; double time = 0; bool finished = false;
            while (transfer.Active)
            {
                if (transfer.TryNext(time, out var chunk, out var last))
                {
                    Assert.That(chunk.Array, Is.SameAs(bytes));
                    Assert.That(chunk.Offset, Is.EqualTo(received));
                    received += chunk.Count; finished = last;
                }
                time += .05;
            }
            Assert.That(received, Is.EqualTo(bytes.Length));
            Assert.That(finished, Is.True);
            Assert.That(time, Is.LessThan(AiVoiceTransfer.Timeout));
        }

        [Test]
        public void VoiceChunkOffsetSurvivesMirrorSerialization()
        {
            var bytes = new byte[6000];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
            using var writer = NetworkWriterPool.Get();
            writer.WriteArraySegmentAndSize(new ArraySegment<byte>(bytes, 2048, 2048));
            using var reader = NetworkReaderPool.Get(writer.ToArraySegment());
            var received = reader.ReadArraySegmentAndSize();
            Assert.That(received.Count, Is.EqualTo(2048));
            for (int i = 0; i < received.Count; i++)
                Assert.That(received.Array[received.Offset + i], Is.EqualTo(bytes[2048 + i]));
        }

        [Test]
        public void VoiceRejectCancelTimeoutAndReplacementDiscardOldGrant()
        {
            var transfer = new AiVoiceTransfer();
            var old = transfer.Begin(new byte[100], 0);
            var current = transfer.Begin(new byte[200], 1);
            transfer.Approve(old, true, 1);
            Assert.That(transfer.TryNext(1, out _, out _), Is.False);
            transfer.Approve(current, false, 1);
            Assert.That(transfer.Active, Is.False);
            transfer.Begin(new byte[100], 2);
            Assert.That(transfer.Expire(14), Is.True);
            transfer.Approve(transfer.Begin(new byte[100], 15), true, 15);
            transfer.Clear();
            Assert.That(transfer.TryNext(15, out _, out _), Is.False);
        }

        [Test]
        public void EncodingListPreservesStereoMixAndClipping()
        {
            var bytes = AiVoicePacket.Encode(new List<float> { 1, -1, 2, 2, -2, -2 }, 16000, 2);
            Assert.That(bytes.Length, Is.EqualTo(50));
            Assert.That(BitConverter.ToInt16(bytes, 44), Is.Zero);
            Assert.That(BitConverter.ToInt16(bytes, 46), Is.EqualTo(32767));
            Assert.That(BitConverter.ToInt16(bytes, 48), Is.EqualTo(-32767));
        }

        [Test]
        public void WarmOutlineGroupingDoesNotAllocateAndResetsAfterShrink()
        {
            var a = new GameObject("part-a").AddComponent<SpaceshipPart>();
            var b = new GameObject("part-b").AddComponent<SpaceshipPart>();
            try
            {
                var parts = new List<SpaceshipPart> { a, b, a, null };
                var groups = new Dictionary<SpaceshipPart, int>();
                var workspace = new PartOutlineGroups.Workspace();
                for (int i = 0; i < 10; i++)
                {
                    PartOutlineGroups.Assign(parts, null, groups, workspace);
                    PartOutlineGroups.KeepMergedOnly(groups, workspace);
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    PartOutlineGroups.Assign(parts, null, groups, workspace);
                    PartOutlineGroups.KeepMergedOnly(groups, workspace);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
                parts.Clear(); parts.Add(b);
                Assert.That(PartOutlineGroups.Assign(parts, null, groups, workspace), Is.EqualTo(1));
                Assert.That(groups.ContainsKey(a), Is.False);
                Assert.That(groups[b], Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject); }
        }
    }
}
