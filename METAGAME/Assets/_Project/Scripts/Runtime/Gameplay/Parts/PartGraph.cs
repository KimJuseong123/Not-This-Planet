using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 접합으로 이어진 파츠 덩어리를 모은다(S15P21B202-229).
    ///
    /// <b>예전에는 소켓 사슬이었다.</b> <see cref="ShipAssembly"/> 와
    /// <see cref="LaunchTest"/> 가 각자 <c>PartSocket.ConnectedTo</c> 를 따라 일렬로
    /// 올라갔다. S15P21B202-228 이 비드로 <b>옆에도</b> 이을 수 있게 만든 뒤로 그 사슬은
    /// 탑의 절반만 보게 됐다 — 비드로 붙인 파츠는 소켓에 맞물려 있지 않다.
    ///
    /// 그래서 사슬이 아니라 <b>그래프</b>를 훑는다. 변은 <see cref="PartAssemblyLink"/> 가
    /// 갖고 있고, 소켓으로 맺었는지 비드로 맺었는지 구분하지 않는다.
    ///
    /// <b>한 곳에 둔 것이 요점이다.</b> 예전에 두 곳이 같은 사슬 코드를 따로 갖고 있었고,
    /// <see cref="ShipAssembly"/> 의 주석이 그 위험을 이미 적어 두었다 — "두 곳이 다른
    /// 방법을 쓰면 탑승은 됐는데 시험이 파츠를 못 찾는 어긋남이 생긴다".
    /// </summary>
    public static class PartGraph
    {
        static readonly List<PartAssemblyLink> Edges = new();

        /// <summary>
        /// <paramref name="root"/> 에서 접합으로 이어진 파츠를 <b>아래에서 위로</b> 모은다.
        ///
        /// 높이(<c>position.y</c>)로 정렬한다. 그래프에는 "i번째 층" 이 없으므로 순서를
        /// 만들어 줄 기준이 필요하고, 쌓기 퍼즐에서 그 기준은 높이다.
        ///
        /// 정렬 기준이 하나 더 있다. 높이가 같으면 이름으로 가른다 — 4인 멀티에서 각자
        /// 다른 순서를 얻으면 "누구는 탑승 가능, 누구는 불가" 가 되기 때문이다
        /// (<see cref="PartAssembler"/> 가 인스턴스 ID 를 쓰지 않는 것과 같은 이유다).
        /// </summary>
        public static void Collect(SpaceshipPart root, List<SpaceshipPart> into)
        {
            if (into == null) return;

            into.Clear();
            if (root == null) return;

            into.Add(root);

            // 너비 우선. 방문 표시를 따로 두지 않고 결과 목록을 그대로 쓴다 —
            // 파츠 수가 한 자리이므로 Contains 가 더 싸고, 목록이 곧 방문 집합이라
            // 둘이 어긋날 여지가 없다.
            for (var i = 0; i < into.Count; i++)
            {
                Edges.Clear();
                PartAssemblyLink.CollectLinksOf(into[i], Edges);

                for (var e = 0; e < Edges.Count; e++)
                {
                    var next = Edges[e].Across(into[i]);
                    if (next == null || into.Contains(next)) continue;

                    into.Add(next);
                }
            }

            into.Sort(CompareByHeight);
        }

        static int CompareByHeight(SpaceshipPart left, SpaceshipPart right)
        {
            if (left == right) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var comparison = left.transform.position.y.CompareTo(right.transform.position.y);
            return comparison != 0 ? comparison : string.CompareOrdinal(left.name, right.name);
        }

        /// <summary>
        /// <paramref name="parts"/> 안의 파츠끼리 이어진 접합 전부.
        ///
        /// 하중 시험(기획 §22.3 · §22.4)이 이 목록을 본다. 소켓 사슬을 쓸 때는 "위에
        /// 올라가는 파츠의 접합" 하나씩이면 충분했지만, 그래프에서는 옆으로 이은 접합도
        /// 하중을 받으므로 <b>모든 변</b>을 세어야 한다 — 빠뜨리면 그 자리가 실제로는
        /// 끊어지는데 시험은 통과한다.
        /// </summary>
        public static void CollectLinks(
            IReadOnlyList<SpaceshipPart> parts, List<PartAssemblyLink> into)
        {
            if (into == null) return;

            into.Clear();
            if (parts == null) return;

            for (var i = 0; i < parts.Count; i++)
            {
                Edges.Clear();
                PartAssemblyLink.CollectLinksOf(parts[i], Edges);

                for (var e = 0; e < Edges.Count; e++)
                {
                    var link = Edges[e];
                    if (into.Contains(link)) continue;

                    // 덩어리 밖으로 나가는 접합은 세지 않는다. 발사하는 것은 이 덩어리이고,
                    // 옆에 세워 둔 다른 파츠에 실수로 이어 둔 접합의 하중까지 볼 이유가 없다.
                    var across = link.Across(parts[i]);
                    if (across == null || !Contains(parts, across)) continue;

                    into.Add(link);
                }
            }
        }

        static bool Contains(IReadOnlyList<SpaceshipPart> parts, SpaceshipPart part)
        {
            for (var i = 0; i < parts.Count; i++)
                if (parts[i] == part)
                    return true;

            return false;
        }
    }
}
