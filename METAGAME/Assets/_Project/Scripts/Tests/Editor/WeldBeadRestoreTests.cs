using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using NUnit.Framework;
using UnityEngine;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 용접 상태를 읽어서 다시 세우는 것을 검증한다 (S15P21B202-386).
    ///
    /// <b>왜 이것이 필요한가.</b> 용접은 지금까지 "이 자리에서 이만큼 자랐다" 는
    /// <b>행동</b>으로만 전달됐다. 처음부터 같이 있던 사람은 그 행동을 다 들어 같은
    /// 모양이 되지만 <b>중간에 들어온 사람은 아무 비드도 없었다</b> — "지금 이만큼 되어
    /// 있다" 를 말할 수단이 없었다.
    ///
    /// <b>행동을 합쳐 보내는 압축은 틀리다.</b> 성장 한 번이 덩어리를 최대 하나만
    /// 올리므로, 열 번을 한 번으로 합치면 결과가 다르다. 그래서 상태를 주고받는다.
    /// </summary>
    public sealed class WeldBeadRestoreTests
    {
        const float Step = 0.02f;

        readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            PartSocket.All.Clear();
            WeldBead.All.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
            PartSocket.All.Clear();
            WeldBead.All.Clear();
        }

        /// <summary>읽은 상태를 그대로 넣으면 같은 모양이 나온다 — 늦참자가 보는 것이 이것이다.</summary>
        [Test]
        public void RestoringAReadSnapshotRebuildsTheSameShape()
        {
            var source = GrowOn(Body(), seconds: 0.6f);
            var snapshot = Read(source);

            var target = WeldBead.Attach(Body());
            target.RestoreChains(snapshot, 0, snapshot.Length);

            Assert.That(target.ChainCount, Is.EqualTo(source.ChainCount), "기둥 수");
            Assert.That(target.BlobCount, Is.EqualTo(source.BlobCount), "덩어리 수");
            for (int i = 0; i < snapshot.Length; i++)
            {
                var a = source.ReadChain(i);
                var b = target.ReadChain(i);
                Assert.That(b.BlobCount, Is.EqualTo(a.BlobCount));
                Assert.That(b.TipRadius, Is.EqualTo(a.TipRadius).Within(1e-4f));
                Assert.That(b.Origin, Is.EqualTo(a.Origin));
                Assert.That(b.Direction, Is.EqualTo(a.Direction));
            }
        }

        /// <summary>질량도 따라와야 한다 — 비드는 파츠 무게에 더해진다.</summary>
        [Test]
        public void RestoringBringsTheMassBack()
        {
            var source = GrowOn(Body(), seconds: 0.6f);
            var snapshot = Read(source);

            var target = WeldBead.Attach(Body());
            target.RestoreChains(snapshot, 0, snapshot.Length);

            Assert.That(target.Mass, Is.EqualTo(source.Mass).Within(1e-3f));
        }

        /// <summary>
        /// 다시 세우면 <b>먼저 있던 것은 버린다</b>. 그러지 않으면 스냅샷이 올 때마다
        /// 같은 자리에 기둥이 겹쳐 쌓인다.
        /// </summary>
        [Test]
        public void RestoringReplacesWhateverWasThere()
        {
            var bead = GrowOn(Body(), seconds: 0.6f);
            int before = bead.ChainCount;
            Assert.That(before, Is.GreaterThan(0), "먼저 뭔가 있어야 의미가 있다");

            var snapshot = Read(bead);
            bead.RestoreChains(snapshot, 0, snapshot.Length);

            Assert.That(bead.ChainCount, Is.EqualTo(before), "겹쳐 쌓이면 안 된다");
        }

        /// <summary>빈 스냅샷은 비드를 비운다 — 팀원이 깎아 없앤 경우다.</summary>
        [Test]
        public void AnEmptySnapshotClearsTheBead()
        {
            var bead = GrowOn(Body(), seconds: 0.6f);
            Assert.That(bead.ChainCount, Is.GreaterThan(0));

            bead.RestoreChains(new WeldBead.ChainState[0], 0, 0);

            Assert.That(bead.ChainCount, Is.EqualTo(0));
            Assert.That(bead.BlobCount, Is.EqualTo(0));
            Assert.That(bead.Mass, Is.EqualTo(0f).Within(1e-4f));
        }

        /// <summary>
        /// 여러 파츠를 이어 붙인 평평한 배열에서 자기 몫만 읽는다 — 전송이 그 모양이다.
        /// </summary>
        [Test]
        public void RestoringReadsOnlyItsOwnSliceOfTheArray()
        {
            var first = GrowOn(Body(), seconds: 0.6f);
            var mine = Read(first);

            // 앞에 남의 사슬이 하나 있는 배열을 만든다.
            var flat = new WeldBead.ChainState[mine.Length + 1];
            flat[0] = new WeldBead.ChainState
            {
                Origin = new Vector3(99f, 99f, 99f),
                Direction = Vector3.right,
                TipRadius = 0.05f,
                BlobCount = 3,
            };
            System.Array.Copy(mine, 0, flat, 1, mine.Length);

            var target = WeldBead.Attach(Body());
            target.RestoreChains(flat, 1, mine.Length);

            Assert.That(target.ChainCount, Is.EqualTo(mine.Length));
            Assert.That(target.ReadChain(0).Origin, Is.EqualTo(mine[0].Origin),
                "남의 몫을 읽으면 안 된다");
        }

        /// <summary>범위를 넘는 값이 와도 죽지 않는다 — 개수가 어긋난 순간을 견딘다.</summary>
        [Test]
        public void OutOfRangeCountsDoNotThrow()
        {
            var bead = WeldBead.Attach(Body());
            var one = new WeldBead.ChainState[1];

            Assert.DoesNotThrow(() => bead.RestoreChains(one, 0, 5));
            Assert.DoesNotThrow(() => bead.RestoreChains(one, 3, 1));
            Assert.DoesNotThrow(() => bead.RestoreChains(null, 0, 2));
        }

        // --- 도우미 -------------------------------------------------------

        SpaceshipPart Body()
        {
            var part = SocketTestRig.Placed(_spawned, new Vector3(_spawned.Count * 40f, 0f, 0f));
            UnityEngine.Physics.SyncTransforms();
            return part;
        }

        WeldBead GrowOn(SpaceshipPart part, float seconds)
        {
            var nozzle = part.transform.position
                         + Vector3.up * (PhysicsScale.PartHeight * 0.5f + 0.25f);

            WeldBead bead = null;
            for (float t = 0f; t < seconds; t += Step)
                WeldBead.TryGrowNear(nozzle, Step, out bead);

            Assert.That(bead, Is.Not.Null, "먼저 용접이 되어야 한다");
            return bead;
        }

        static WeldBead.ChainState[] Read(WeldBead bead)
        {
            var states = new WeldBead.ChainState[bead.ChainCount];
            for (int i = 0; i < states.Length; i++) states[i] = bead.ReadChain(i);
            return states;
        }
    }
}
