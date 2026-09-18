using System.Collections.Generic;

namespace Metagame.Gameplay.Parts
{
    /// <summary>
    /// 핵심 파츠를 <b>용접으로 묶인 덩어리</b>로 나눈다 (S15P21B202-562).
    ///
    /// <para><b>왜 필요한가.</b> 테두리는 마스크에 그린 <b>번호가 다른 자리</b>에만 생긴다.
    /// 그래서 용접으로 붙은 두 파츠가 같은 번호를 받으면 둘 사이에는 선이 생기지 않고
    /// <b>저절로 하나의 윤곽</b>이 된다. "결합됐다" 를 눈으로 보여 주는 것이 이 번호다.</para>
    ///
    /// <para><b>순수 계산으로 떼어 둔다.</b> 렌더러 안의 조건문으로만 있으면 검증이 닿지
    /// 못한다 — 이 저장소가 <c>-438</c>·<c>-448</c>·<c>-489</c>·<c>-505</c> 에서 네 번
    /// 겪은 자리다. 밖에서 부를 수 있게 두면 대조가 실제 경로를 지난다.</para>
    ///
    /// <para><b>'결합' 은 <see cref="PartAssemblyLink.IsFullyWelded"/> 다.</b> 얹어만 둔
    /// 것(<see cref="PartState.Placed"/>)은 아직 따로 보이는 것이 맞다 — 기획 §16 의
    /// 용접이 끝나야 하나가 된다. 링크가 끊기면(<see cref="PartAssemblyLink.IsIntact"/>)
    /// 다시 갈라진다.</para>
    /// </summary>
    public static class PartOutlineGroups
    {
        /// <summary>테두리를 그리지 않는다는 뜻. 마스크의 빈 곳과 같은 값이다.</summary>
        public const int NoGroup = 0;

        /// <summary>
        /// 파츠마다 덩어리 번호를 매긴다. 번호는 1 부터 센다 — 0 은 "없음" 이다.
        ///
        /// <para><b>번호가 안정적이어야 한다.</b> 같은 구성이면 같은 번호가 나와야 화면이
        /// 깜빡이지 않는다. 그래서 들어온 순서대로 훑으며 처음 만난 덩어리에 다음 번호를
        /// 준다 — 사전이나 해시 순회로 정하면 프레임마다 뒤바뀔 수 있다.</para>
        /// </summary>
        /// <param name="parts">핵심 파츠. null 과 중복은 걸러 준다.</param>
        /// <param name="links">살아 있는 접합 목록. 보통 <see cref="PartAssemblyLink.All"/>.</param>
        /// <param name="into">파츠 → 번호. 호출 쪽이 재사용하는 사전이며 먼저 비운다.</param>
        /// <returns>매겨진 덩어리 수.</returns>
        public static int Assign(IReadOnlyList<SpaceshipPart> parts,
                                 IReadOnlyList<PartAssemblyLink> links,
                                 Dictionary<SpaceshipPart, int> into)
        {
            if (into == null) return 0;
            into.Clear();
            if (parts == null || parts.Count == 0) return 0;

            // 유니온-파인드. 파츠 수가 한 자릿수라 배열 두 개로 족하다.
            var ordered = new List<SpaceshipPart>(parts.Count);
            var index = new Dictionary<SpaceshipPart, int>(parts.Count);
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || index.ContainsKey(part)) continue;
                index[part] = ordered.Count;
                ordered.Add(part);
            }
            if (ordered.Count == 0) return 0;

            var parent = new int[ordered.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            if (links != null)
                for (var i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (link == null || !IsWeldJoined(link)) continue;

                    var moving = link.MovingPart;
                    var target = link.TargetPart;
                    if (moving == null || target == null) continue;
                    if (!index.TryGetValue(moving, out var a)) continue;
                    if (!index.TryGetValue(target, out var b)) continue;

                    Union(parent, a, b);
                }

            var groupOf = new int[ordered.Count];
            var groups = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var root = Find(parent, i);
                if (groupOf[root] == NoGroup) groupOf[root] = ++groups;
                into[ordered[i]] = groupOf[root];
            }

            return groups;
        }

        /// <summary>
        /// <b>혼자인 덩어리를 걷어낸다</b> (S15P21B202-562).
        ///
        /// <para>테두리는 "붙었다" 를 보여 주는 표시다. 아직 혼자 굴러다니는 파츠에까지
        /// 두르면 그 뜻이 흐려진다 — 온 화면이 테두리면 무엇이 결합된 것인지 오히려
        /// 안 보인다. 그래서 <b>둘 이상이 용접으로 묶인 덩어리만</b> 남긴다.</para>
        ///
        /// <para>번호를 다시 매기지 않는다. 마스크는 번호가 <b>다른지만</b> 보므로 빈
        /// 번호가 생겨도 상관없고, 다시 매기면 그때마다 값이 흔들려 테두리가 깜빡인다.</para>
        /// </summary>
        /// <param name="groups">
        /// <see cref="Assign"/> 이 채운 사전. 혼자인 파츠는 여기서 <b>빠진다</b>.
        /// </param>
        /// <returns>남은 덩어리 수.</returns>
        public static int KeepMergedOnly(Dictionary<SpaceshipPart, int> groups)
        {
            if (groups == null || groups.Count == 0) return 0;

            var members = new Dictionary<int, int>();
            foreach (var pair in groups)
                members[pair.Value] = members.TryGetValue(pair.Value, out var n) ? n + 1 : 1;

            // 사전을 돌면서 지울 수 없다 — 지울 것을 먼저 모은다.
            var lonely = new List<SpaceshipPart>();
            foreach (var pair in groups)
                if (members[pair.Value] < 2) lonely.Add(pair.Key);

            for (var i = 0; i < lonely.Count; i++) groups.Remove(lonely[i]);

            var kept = 0;
            foreach (var pair in members)
                if (pair.Value >= 2) kept++;

            return kept;
        }

        /// <summary>
        /// 이 접합이 <b>용접으로 이어진 것</b>인가 (S15P21B202-562).
        ///
        /// <para><b>비율 1 을 요구하지 않는다.</b> 처음에는
        /// <see cref="PartAssemblyLink.IsFullyWelded"/>(<c>WeldRatio &gt;= 1</c>)로 두었는데,
        /// 그 1 은 비드 <see cref="PhysicsScale.WeldBeadFullStrengthBlobs"/>(3)덩이를 다
        /// 채워야 나오는 값이다. 사람이 두 파츠를 이어 붙인 정도로는 한참 못 미쳐
        /// <b>용접을 해도 테두리가 안 합쳐졌다</b>(실측). 비율은 강도이지 "이어졌나" 가
        /// 아니다.</para>
        ///
        /// <para><b>그렇다고 <c>IsIntact</c> 만으로도 안 된다.</b> 소켓에 얹기만 해도
        /// 접합은 생긴다(<c>PartAssembler</c>). 그때는 아직 따로 보이는 것이 맞다 —
        /// 요구가 "용접건으로 결합했으면" 이다. 그래서 <b>비드가 조금이라도 닿았는지</b>
        /// (<c>WeldRatio &gt; 0</c>)로 가른다. 0 이 되면 <c>WeldBead</c> 가 소켓 없는
        /// 접합을 풀어 버리므로, 이 값은 곧 "용접이 잡고 있다" 와 같다.</para>
        /// </summary>
        public static bool IsWeldJoined(PartAssemblyLink link) =>
            link != null && link.IsIntact && link.WeldRatio > 0f;

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];   // 경로 절반 접기
                i = parent[i];
            }
            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            var ra = Find(parent, a);
            var rb = Find(parent, b);
            if (ra == rb) return;

            // 작은 쪽을 뿌리로 둔다 — 번호가 들어온 순서를 따라가게 해서 화면이 안 흔들린다.
            if (ra < rb) parent[rb] = ra;
            else parent[ra] = rb;
        }
    }
}
