using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;
using Key = Metagame.Network.AiNavigationSources.Key;
using Verdict = Metagame.Network.AiNavigationSources.Verdict;

namespace Metagame.Gameplay.Tests
{
    /// <summary>
    /// 길찾기를 <b>다시 굽게 만드는 것</b>을 값으로 가른다 (S15P21B202-737).
    ///
    /// <para>2026-09-23 QA 에서 운석 공세 중 2초마다 구웠는데 무엇이 바뀌었는지 아무 데도 없어, 이슈가
    /// 원인을 운석 흔적으로 잘못 짚었다(흔적은 거르기에서 이미 빠진다). 굽을지 말지와 "무엇이 바뀌었나"
    /// 가 이 표 하나를 보므로, 여기서 틀리면 <b>낡은 길을 쓰거나</b>(굽지 않아야 할 것을 안 굽는 것보다
    /// 나쁘다) 로그가 거짓말을 한다.</para>
    /// </summary>
    public sealed class AiNavigationSourcesTests
    {
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        Collider Owner(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go.AddComponent<BoxCollider>();
        }

        static Key Box(Object owner, Vector3 at, bool part = true, float size = 1f)
            => new Key(owner, null, NavMeshBuildSourceShape.Box, part ? 1 : 0, false,
                       Vector3.one * size, Matrix4x4.TRS(at, Quaternion.identity, Vector3.one), part);

        static List<Key> Sorted(List<Key> keys)
        {
            var sorted = new List<Key>();
            AiNavigationSources.Sort(keys, sorted);
            return sorted;
        }

        [Test]
        public void TheSameSourcesInTheSameOrderAreTheSame()
        {
            var a = Owner("a"); var b = Owner("b");
            var baked = new List<Key> { Box(a, Vector3.zero), Box(b, Vector3.right) };
            var now = new List<Key> { Box(a, Vector3.zero), Box(b, Vector3.right) };
            Assert.That(AiNavigationSources.Compare(baked, Sorted(baked), now, new List<Key>()), Is.EqualTo(Verdict.Same));
        }

        /// <summary>
        /// <b>순서만 다르면 굽지 않는다.</b> 네이티브 수집이 같은 순서를 준다는 보장이 없다 — 2026-09-23 로그에
        /// 파츠가 전부 고정돼 있던 30초에도 두 번 구운 자리가 있다.
        /// </summary>
        [Test]
        public void AReorderedListIsNotAChange()
        {
            var a = Owner("a"); var b = Owner("b"); var c = Owner("c");
            var baked = new List<Key> { Box(a, Vector3.zero), Box(b, Vector3.right), Box(c, Vector3.up) };
            var now = new List<Key> { Box(c, Vector3.up), Box(a, Vector3.zero), Box(b, Vector3.right) };
            var nowSorted = new List<Key>();
            Assert.That(AiNavigationSources.Compare(baked, Sorted(baked), now, nowSorted), Is.EqualTo(Verdict.Reordered),
                "순서만 바뀐 것을 바뀐 것으로 읽는다 — 아무것도 안 움직였는데 굽는다");
            var tally = AiNavigationSources.Diff(Sorted(baked), nowSorted, AiNavigationWorld.VoxelSize);
            Assert.That(tally.PartMoved + tally.PartShape + tally.OtherChanged, Is.Zero, "순서만 다른데 무언가 바뀌었다고 센다");
        }

        /// <summary><b>허용 오차는 없다.</b> 가장 작은 차이도 바뀐 것이다 — 두는지는 로그를 보고 정한다.</summary>
        [Test]
        public void TheSmallestMoveIsStillAChange()
        {
            var a = Owner("a");
            float x = 1f, next = System.BitConverter.Int32BitsToSingle(System.BitConverter.SingleToInt32Bits(x) + 1);
            var baked = new List<Key> { Box(a, new Vector3(x, 0, 0)) };
            var now = new List<Key> { Box(a, new Vector3(next, 0, 0)) };
            Assert.That(AiNavigationSources.Compare(baked, Sorted(baked), now, new List<Key>()), Is.EqualTo(Verdict.Changed),
                "한 ULP 움직인 것을 그대로라고 한다 — 조용히 허용 오차가 생겼다");
            var tally = AiNavigationSources.Diff(Sorted(baked), Sorted(now), AiNavigationWorld.VoxelSize);
            Assert.That(tally.PartMoved, Is.EqualTo(1));
            Assert.That(tally.WithinHalfVoxel, Is.EqualTo(1), "반 복셀 안의 변화를 세지 않는다 — 허용 오차를 정할 근거가 사라진다");
        }

        /// <summary>제자리에서 돈 것도 <b>움직인 것</b>이다. 옮김 열만 보면 0 이 된다.</summary>
        [Test]
        public void TurningInPlaceMovesTheCorners()
        {
            var a = Owner("a");
            var before = new Key(a, null, NavMeshBuildSourceShape.Box, 1, false, new Vector3(4, 1, 1),
                                 Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one), true);
            var turned = new Key(a, null, NavMeshBuildSourceShape.Box, 1, false, new Vector3(4, 1, 1),
                                 Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 90, 0), Vector3.one), true);
            Assert.That(AiNavigationSources.CornerShift(before, turned), Is.GreaterThan(2f),
                "제자리에서 돈 4m 상자의 모서리가 거의 안 움직였다고 한다");
        }

        [Test]
        public void AddedRemovedAndReshapedAreCountedApart()
        {
            var kept = Owner("kept"); var gone = Owner("gone"); var fresh = Owner("fresh"); var wall = Owner("wall");
            var baked = new List<Key> { Box(kept, Vector3.zero), Box(gone, Vector3.right), Box(wall, Vector3.left, part: false) };
            var now = new List<Key> { Box(kept, Vector3.zero, size: 2f), Box(fresh, Vector3.up), Box(wall, Vector3.left * 2, part: false) };
            var nowSorted = new List<Key>();
            Assert.That(AiNavigationSources.Compare(baked, Sorted(baked), now, nowSorted), Is.EqualTo(Verdict.Changed));
            var tally = AiNavigationSources.Diff(Sorted(baked), nowSorted, AiNavigationWorld.VoxelSize);
            Assert.That(tally.Added, Is.EqualTo(1), "새로 생긴 파츠 소스를 안 센다 — 용접 비드가 안 보인다");
            Assert.That(tally.Removed, Is.EqualTo(1));
            Assert.That(tally.PartShape, Is.EqualTo(3), "생김·없어짐·크기 바뀜이 파츠 모양으로 가야 한다");
            Assert.That(tally.OtherChanged, Is.EqualTo(1), "파츠가 아닌 것의 변화를 안 센다 — 거르기가 놓친 것을 못 본다");
            Assert.That(tally.MaxShiftOwner, Is.EqualTo(wall));
            Assert.That(tally.MaxShift, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <summary>
        /// 견주기는 <b>구운 쪽을 건드리지 않는다</b>. 건너뛰는 길에서 스냅숏이 바뀌면 조금씩 움직이는 것이
        /// "지난번과 같다" 로 이어져 한 번도 안 굽게 된다.
        /// </summary>
        [Test]
        public void ComparingNeverRewritesTheBakedSnapshot()
        {
            var a = Owner("a"); var b = Owner("b");
            var baked = new List<Key> { Box(a, Vector3.zero), Box(b, Vector3.right) };
            var bakedSorted = Sorted(baked);
            var snapshot = baked.ToList(); var snapshotSorted = bakedSorted.ToList();
            AiNavigationSources.Compare(baked, bakedSorted, new List<Key> { Box(b, Vector3.up), Box(a, Vector3.zero) }, new List<Key>());
            Assert.That(baked.Zip(snapshot, (x, y) => x.SameAs(y)).All(v => v) && baked.Count == snapshot.Count, Is.True);
            Assert.That(bakedSorted.Zip(snapshotSorted, (x, y) => x.SameAs(y)).All(v => v) && bakedSorted.Count == snapshotSorted.Count, Is.True);
        }

        /// <summary>
        /// 키가 <see cref="NavMeshBuildSource"/> 의 <b>공개 속성을 모두</b> 담는다. Unity 가 속성을 하나 더하면 여기서
        /// 먼저 깨진다 — 담지 않으면 그 값만 바뀐 변화를 "그대로" 로 읽는다.
        /// </summary>
        [Test]
        public void TheKeyCoversEverySourceProperty()
        {
            var mapping = new Dictionary<string, string>
            {
                ["transform"] = "Matrix", ["size"] = "Size", ["shape"] = "Shape", ["area"] = "Area",
                ["sourceObject"] = "SourceObject", ["component"] = "Component", ["generateLinks"] = "GenerateLinks",
            };
            var fields = typeof(Key).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToList();
            foreach (var property in typeof(NavMeshBuildSource).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(mapping.ContainsKey(property.Name), Is.True,
                    $"NavMeshBuildSource.{property.Name} 를 키가 모른다 — 그 값만 바뀌면 굽지 않는다");
                Assert.That(fields, Does.Contain(mapping[property.Name]));
            }
        }

        /// <summary>
        /// <b>누가 길을 쓸 일이 있는가</b> (S15P21B202-737). 일하는 동료가 있으면 작업, 말하는 중이거나
        /// 계획을 세우는 중이면 예고, 아니면 쉼이다.
        /// </summary>
        [Test]
        public void DemandComesFromWorkThenIncomingOrders()
        {
            Assert.That(AiNavigationSources.DemandOf(false, false, false), Is.EqualTo(AiNavigationSources.Demand.Idle));
            Assert.That(AiNavigationSources.DemandOf(false, true, false), Is.EqualTo(AiNavigationSources.Demand.Incoming),
                "계획을 세우는 중인데 쉰다고 본다 — 첫 길을 묵은 길로 묻는다");
            Assert.That(AiNavigationSources.DemandOf(false, false, true), Is.EqualTo(AiNavigationSources.Demand.Incoming),
                "누가 V 를 누르고 있는데 쉰다고 본다");
            Assert.That(AiNavigationSources.DemandOf(true, false, false), Is.EqualTo(AiNavigationSources.Demand.Working));
            Assert.That(AiNavigationSources.DemandOf(true, true, true), Is.EqualTo(AiNavigationSources.Demand.Working));
        }

        /// <summary>
        /// 쉬는 동안에만 묵히고, <b>정확히</b> 그만큼 지나면 굽는다. 쓸 일이 생기면 기다리지 않는다.
        /// </summary>
        [Test]
        public void IdleChangesWaitThenBakeAndAnyDemandBakesAtOnce()
        {
            float wait = AiNavigationWorld.IdleStaleSeconds;
            Assert.That(AiNavigationSources.BakeDue(AiNavigationSources.Demand.Idle, 0f, wait), Is.False);
            Assert.That(AiNavigationSources.BakeDue(AiNavigationSources.Demand.Idle, wait - .01f, wait), Is.False);
            Assert.That(AiNavigationSources.BakeDue(AiNavigationSources.Demand.Idle, wait, wait), Is.True,
                "묵힌 시간이 다 찼는데 안 굽는다 — 경계에서 한 주기가 더 밀린다");
            Assert.That(AiNavigationSources.BakeDue(AiNavigationSources.Demand.Incoming, 0f, wait), Is.True);
            Assert.That(AiNavigationSources.BakeDue(AiNavigationSources.Demand.Working, 0f, wait), Is.True);

            Assert.That(AiNavigationSources.Wakes(AiNavigationSources.Demand.Idle, AiNavigationSources.Demand.Incoming), Is.True);
            Assert.That(AiNavigationSources.Wakes(AiNavigationSources.Demand.Idle, AiNavigationSources.Demand.Working), Is.True);
            Assert.That(AiNavigationSources.Wakes(AiNavigationSources.Demand.Incoming, AiNavigationSources.Demand.Working), Is.False,
                "이미 깨어 있는데 또 깨운다 — 주기를 무시하고 매 프레임 굽는다");
            Assert.That(AiNavigationSources.Wakes(AiNavigationSources.Demand.Idle, AiNavigationSources.Demand.Idle), Is.False);
        }

        /// <summary>
        /// <b>조종 중인 파츠</b>만 굽기에서 뺀다 (S15P21B202-737). 견인 빔에 들렸거나 누가 탔다. 내려놓인 ·
        /// 용접된 · 풀린 파츠는 제자리의 장애물이다.
        /// </summary>
        [Test]
        public void OnlyPartsUnderControlLeaveTheBake()
        {
            foreach (PartState state in System.Enum.GetValues(typeof(PartState)))
            {
                Assert.That(AiNavigationSources.MovesUnderControl(state, ridden: true), Is.True, state + " 인데 타고 있다");
                Assert.That(AiNavigationSources.MovesUnderControl(state, ridden: false), Is.EqualTo(state == PartState.Carried),
                    state + " 을 조종 중으로 본다 — 제자리의 장애물이 길찾기에서 사라진다");
            }
        }
    }
}
