using System.Collections.Generic;
using Metagame.Gameplay.Parts;
using UnityEngine;
using UnityEngine.AI;

namespace Metagame.Network
{
    /// <summary>
    /// 길찾기 소스를 <b>값으로</b> 견주고, 다시 굽게 만든 것이 <b>무엇이었는지</b> 센다 (S15P21B202-737).
    ///
    /// <para><b>왜 떼어 두나.</b> 2026-09-23 QA 의 운석 공세에서 2초마다 한 번씩 굽고 "그대로라 건너뜀" 이
    /// 거의 한 번도 안 걸렸다. 그런데 매 주기 무엇이 흔들리는지는 아무 데도 없었다. 이슈는 운석 흔적을
    /// 의심했지만 흔적은 <see cref="AiNavigationWorld.UsedForNavigation(Collider, out SpaceshipPart)"/> 에서
    /// 이미 빠진다 — <b>추측이 틀렸다는 것을 로그로 볼 수 없었던 것</b>이 문제였다. 판단을 여기 모아 씬
    /// 없이 시험하고, 로그는 이 결과를 그대로 적는다.</para>
    /// </summary>
    public static class AiNavigationSources
    {
        public enum Verdict { Same, Reordered, Changed }

        /// <summary>
        /// 굽기에 들어가는 소스 하나의, <b>비교에 쓰는 전부</b>. <see cref="NavMeshBuildSource"/> 의 공개
        /// 속성마다 칸이 하나씩 있다 — 시험이 반사로 그것을 확인한다. 하나라도 빠지면 그 값만 바뀐 변화를
        /// "그대로" 로 읽어 <b>낡은 길을 쓴다</b>.
        /// </summary>
        public readonly struct Key
        {
            public readonly Object Component, SourceObject;
            public readonly NavMeshBuildSourceShape Shape;
            public readonly int Area;
            public readonly bool GenerateLinks;
            public readonly Vector3 Size;
            public readonly Matrix4x4 Matrix;

            /// <summary>파츠의 것인가. 로그가 "파츠" 와 "그 밖" 을 가르는 데만 쓴다 — 비교에는 안 쓴다.</summary>
            public readonly bool Part;

            public Key(NavMeshBuildSource source, bool part)
                : this(source.component, source.sourceObject, source.shape, source.area, source.generateLinks,
                       source.size, source.transform, part) { }

            /// <summary>값으로 만든다 — 시험용이기도 하다(<see cref="NavMeshBuildSource.component"/> 는 밖에서 못 넣는다).</summary>
            public Key(Object component, Object sourceObject, NavMeshBuildSourceShape shape, int area, bool generateLinks,
                       Vector3 size, Matrix4x4 matrix, bool part)
            {
                Component = component; SourceObject = sourceObject; Shape = shape; Area = area;
                GenerateLinks = generateLinks; Size = size; Matrix = matrix; Part = part;
            }

            /// <summary>
            /// 굽기에 닿는 칸이 모두 <b>정확히</b> 같은가. 허용 오차는 두지 않는다 — 그것은 길찾기 품질을
            /// 바꾸는 결정이라 재 보고 정한다(로그의 "반 복셀 안" 이 그 근거가 된다).
            /// </summary>
            public bool SameAs(in Key other) => Component == other.Component && SourceObject == other.SourceObject
                && Shape == other.Shape && Area == other.Area && GenerateLinks == other.GenerateLinks
                && Size.Equals(other.Size) && Matrix.Equals(other.Matrix);

            /// <summary>자세(<see cref="Matrix"/>)만 빼고 같은가 — "움직였다" 와 "모양이 바뀌었다" 를 가른다.</summary>
            public bool SameShape(in Key other) => SourceObject == other.SourceObject && Shape == other.Shape
                && Area == other.Area && GenerateLinks == other.GenerateLinks && Size.Equals(other.Size);
        }

        /// <summary>
        /// 모든 칸에 걸친 <b>전순서</b>. 같은 것들의 모임인지 보려면 양쪽을 같은 규칙으로 늘어놓아야 한다.
        /// 물체는 <c>GetEntityId</c> 로 견준다(<c>SoundCollisionEmitter</c> 와 같은 방식).
        /// </summary>
        sealed class Order : IComparer<Key>
        {
            public int Compare(Key a, Key b)
            {
                int c = Id(a.Component).CompareTo(Id(b.Component)); if (c != 0) return c;
                c = Id(a.SourceObject).CompareTo(Id(b.SourceObject)); if (c != 0) return c;
                c = ((int)a.Shape).CompareTo((int)b.Shape); if (c != 0) return c;
                c = a.Area.CompareTo(b.Area); if (c != 0) return c;
                c = a.GenerateLinks.CompareTo(b.GenerateLinks); if (c != 0) return c;
                for (int i = 0; i < 3; i++) { c = a.Size[i].CompareTo(b.Size[i]); if (c != 0) return c; }
                for (int i = 0; i < 16; i++) { c = a.Matrix[i].CompareTo(b.Matrix[i]); if (c != 0) return c; }
                return 0;
            }
        }

        static readonly Order order = new Order();

        static EntityId Id(Object o) => o != null ? o.GetEntityId() : default;

        /// <summary>정렬한 사본을 <paramref name="into"/> 에 쓴다. 새 목록을 만들지 않는다 — 2초마다 돈다.</summary>
        public static void Sort(IReadOnlyList<Key> keys, List<Key> into)
        {
            into.Clear();
            for (int i = 0; i < keys.Count; i++) into.Add(keys[i]);
            into.Sort(order);
        }

        /// <summary>
        /// 지난번 구운 소스와 지금 소스를 견준다. <b>순서만 다르면 굽지 않는다</b> (S15P21B202-737).
        ///
        /// <para>네이티브 수집이 매번 같은 순서로 준다는 보장이 없다. 2026-09-23 로그에는 파츠가 전부
        /// 고정돼 있던 30초에도 두 번 구운 자리가 있고, 자리대로만 견주면 그것은 "바뀌었다" 로 보인다.</para>
        ///
        /// <para>먼저 지금처럼 자리대로 견준다 — 대부분 여기서 끝나 정렬 비용이 없다. 틀리면 정렬해서
        /// <b>같은 것들의 모임인지</b> 본다. <paramref name="currentSorted"/> 에는 그때 정렬한 지금 소스가
        /// 남는다(부르는 쪽이 <see cref="Diff"/> 에 그대로 쓴다). <b>구운 쪽 두 목록은 건드리지 않는다</b> —
        /// 스냅숏은 부르는 쪽이 굽기로 정한 뒤에만 바꾼다.</para>
        /// </summary>
        public static Verdict Compare(IReadOnlyList<Key> baked, IReadOnlyList<Key> bakedSorted,
                                      IReadOnlyList<Key> current, List<Key> currentSorted)
        {
            if (baked.Count == current.Count)
            {
                bool same = true;
                for (int i = 0; i < current.Count && same; i++) same = baked[i].SameAs(current[i]);
                if (same) return Verdict.Same;
            }
            Sort(current, currentSorted);
            if (bakedSorted.Count != currentSorted.Count) return Verdict.Changed;
            for (int i = 0; i < currentSorted.Count; i++)
                if (!bakedSorted[i].SameAs(currentSorted[i])) return Verdict.Changed;
            return Verdict.Reordered;
        }

        /// <summary>한 번의 "바뀌었다" 가 <b>무엇</b>이었는지.</summary>
        public struct Tally
        {
            /// <summary>파츠가 모양은 그대로 자세만 바뀌었다.</summary>
            public int PartMoved;
            /// <summary>파츠 쪽 소스가 생겼거나 없어졌거나 크기가 바뀌었다 — 용접 비드가 여기로 온다.</summary>
            public int PartShape;
            /// <summary>파츠가 아닌 것이 바뀌었다. 0 이 아니면 거르기가 놓친 것이 있다는 뜻이다.</summary>
            public int OtherChanged;
            public int Added, Removed;
            /// <summary>가장 많이 움직인 모서리의 거리(m)와 그 주인.</summary>
            public float MaxShift;
            public Object MaxShiftOwner;
            /// <summary>반 복셀보다 덜 움직인 것 — 허용 오차를 뒀다면 굽지 않았을 것.</summary>
            public int WithinHalfVoxel;
        }

        /// <summary>
        /// 정렬한 두 목록을 주인(컴포넌트)끼리 맞춰 <b>무엇이 바뀌었는지</b> 센다. 로그에만 쓴다 — 굽을지는
        /// <see cref="Compare"/> 가 정한다.
        /// </summary>
        public static Tally Diff(IReadOnlyList<Key> bakedSorted, IReadOnlyList<Key> currentSorted, float voxel)
        {
            var tally = new Tally();
            int i = 0, j = 0;
            while (i < bakedSorted.Count || j < currentSorted.Count)
            {
                int c = i >= bakedSorted.Count ? 1 : j >= currentSorted.Count ? -1
                      : Id(bakedSorted[i].Component).CompareTo(Id(currentSorted[j].Component));
                if (c < 0) { CountShape(ref tally, bakedSorted[i]); tally.Removed++; i++; continue; }
                if (c > 0) { CountShape(ref tally, currentSorted[j]); tally.Added++; j++; continue; }
                var before = bakedSorted[i]; var now = currentSorted[j]; i++; j++;
                if (before.SameAs(now)) continue;
                if (!before.SameShape(now)) { CountShape(ref tally, now); continue; }
                float shift = CornerShift(before, now);
                if (shift < voxel * .5f) tally.WithinHalfVoxel++;
                if (shift > tally.MaxShift) { tally.MaxShift = shift; tally.MaxShiftOwner = now.Component; }
                if (now.Part) tally.PartMoved++; else tally.OtherChanged++;
            }
            return tally;
        }

        static void CountShape(ref Tally tally, in Key key)
        {
            if (key.Part) tally.PartShape++; else tally.OtherChanged++;
        }

        /// <summary>
        /// 자세가 바뀌어 <b>가장 많이 움직인 모서리</b>의 거리(m). 옮김 열만 보면 제자리에서 돈 것이 0 이
        /// 되어 "안 움직였다" 로 읽힌다.
        /// </summary>
        public static float CornerShift(in Key before, in Key now)
        {
            Vector3 center = Vector3.zero, half = before.Size * .5f;
            if (before.Shape == NavMeshBuildSourceShape.Mesh && before.SourceObject is Mesh mesh)
            { center = mesh.bounds.center; half = mesh.bounds.extents; }
            float max = 0f;
            for (int n = 0; n < 8; n++)
            {
                var corner = center + new Vector3((n & 1) == 0 ? -half.x : half.x,
                                                  (n & 2) == 0 ? -half.y : half.y,
                                                  (n & 4) == 0 ? -half.z : half.z);
                max = Mathf.Max(max, (before.Matrix.MultiplyPoint3x4(corner) - now.Matrix.MultiplyPoint3x4(corner)).magnitude);
            }
            return max;
        }

        /// <summary>
        /// <b>지금 누군가 조종하고 있는 파츠인가</b> (S15P21B202-737). 견인 빔에 들렸거나 사람이 타고 있다.
        ///
        /// <para>이런 파츠는 물리 한 걸음마다 자세가 바뀌어 굽는 게이트를 매 주기 연다 — 2026-09-23 로그에서
        /// Part_4 를 타는 동안 30초에 15/15번 구웠다. 그 자리의 구멍을 길찾기에 새겨 봐야 2초 뒤에는 틀린
        /// 자리다. 가까이 걸을 때 부딪히지 않는 것은 걸음 검사(<c>AiFollowController</c> 의 SafeStep)가
        /// 이미 파츠를 장애물로 본다.</para>
        /// </summary>
        public static bool MovesUnderControl(PartState state, bool ridden) => state == PartState.Carried || ridden;
    }
}
