using System.Collections.Generic;
using UnityEngine;

namespace Metagame.Network
{
    /// <summary>
    /// 끊긴 사람이 돌아왔을 때 <b>어디에 세울지</b> 기억한다 (S15P21B202-150).
    ///
    /// <b>왜 필요한가.</b> Mirror 는 연결이 끊기면 그 사람의 플레이어 오브젝트를 지운다.
    /// 그래서 다시 붙으면 새 몸이 <b>스폰 지점</b>에 선다 — 균열 건너편까지 걸어간 사람이
    /// 처음으로 돌아가고, 팀은 그 사람을 다시 기다린다.
    ///
    /// <b>서버(호스트)에서만 쓴다.</b> 참가자 화면에는 판단할 것이 없다.
    ///
    /// <b>이것은 완전판이 아니다.</b> 팀 규칙(-150 완료 조건)은 "끊긴 오브젝트를 30초
    /// 유지하고 같은 userId 로 돌아오면 권한을 재부여" 하는 쪽이다. 그러면 위치뿐 아니라
    /// 들고 있던 것·탑승 상태까지 그대로 남는다. 다만 그 방식은 <c>NetworkManager</c> 의
    /// <c>OnServerDisconnect</c>·<c>OnServerAddPlayer</c> 를 재정의해야 하고, 그 컴포넌트는
    /// <b>씬 오브젝트</b>(<c>Lobby.unity</c>)라 씬을 고쳐야 한다. 지금은 배치모드 빌드가
    /// 프리팹을 지우는 문제(-351)가 열려 있어 씬·프리팹을 건드리지 않는다. 그래서
    /// <b>위치 복구까지</b> 먼저 하고, 오브젝트 유지는 후속으로 남긴다.
    /// </summary>
    public static class RejoinMemory
    {
        /// <summary>
        /// 이 시간이 지난 기억은 쓰지 않는다(초).
        ///
        /// <para>백엔드의 <c>game.reconnect-grace-ms</c>(30초)와 <b>같은 값이어야 한다</b> —
        /// 백엔드가 자리를 놓아 준 사람을 여기서만 붙들면 앞뒤가 맞지 않는다. 완료 조건이
        /// "상수를 한 곳에 둔다" 고 요구하는 이유다.</para>
        /// </summary>
        public const float GraceSeconds = 30f;

        readonly struct Spot
        {
            public readonly Vector3 Foot;
            public readonly Vector3 Forward;
            public readonly double At;

            public Spot(Vector3 foot, Vector3 forward, double at)
            {
                Foot = foot;
                Forward = forward;
                At = at;
            }
        }

        static readonly Dictionary<long, Spot> Spots = new Dictionary<long, Spot>();

        /// <summary>지금 서 있는 자리를 적어 둔다. 연결이 끊기는 순간 서버가 부른다.</summary>
        public static void Remember(long userId, Vector3 foot, Vector3 planarForward)
        {
            if (userId == 0) return;

            Spots[userId] = new Spot(foot, planarForward, Time.timeAsDouble);
        }

        /// <summary>
        /// 적어 둔 자리를 꺼낸다. <b>꺼내면 지운다</b> — 한 번만 쓴다.
        ///
        /// <para>남겨 두면 그 사람이 다음 판에서 죽거나 나갔다 들어올 때 옛 자리로
        /// 끌려간다. 유예를 넘긴 기억도 여기서 버린다.</para>
        /// </summary>
        public static bool TryTake(long userId, out Vector3 foot, out Vector3 planarForward)
        {
            foot = Vector3.zero;
            planarForward = Vector3.forward;

            if (!Spots.TryGetValue(userId, out Spot spot)) return false;

            Spots.Remove(userId);

            if (Time.timeAsDouble - spot.At > GraceSeconds) return false;

            foot = spot.Foot;
            planarForward = spot.Forward;
            return true;
        }

        /// <summary>
        /// 전부 버린다. 새 판이 시작될 때 서버가 부른다.
        ///
        /// <para>없으면 지난 판의 자리가 남아, 판을 다시 시작한 직후 들어온 사람이
        /// 엉뚱한 곳에 선다.</para>
        /// </summary>
        public static void Clear() => Spots.Clear();

        /// <summary>지금 기억하고 있는 사람 수. 로그와 시험에서 본다.</summary>
        public static int Count => Spots.Count;
    }
}
