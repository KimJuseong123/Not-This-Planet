using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 게임플레이가 "방금 이런 일이 있었다" 고 알리는 자리 (S15P21B202-152).
    ///
    /// <b>왜 이렇게 하나.</b> 네트워크 계층이 알아야 하는 사건이 있는데,
    /// <c>Metagame.Gameplay</c> 는 Mirror 를 참조하지 않는다. 그 경계가 있었기에
    /// 전용 서버에서 호스트 모드로 옮길 때 물리 코드를 한 줄도 안 고쳤다(-263).
    /// 그 경계를 지키려고 사건만 여기에 내놓고, 실어 나르는 일은 네트워크가 맡는다.
    ///
    /// <b>왜 상태가 아니라 행동인가.</b> 용접 비드는 덩어리 사슬이라 상태를 통째로
    /// 보내면 크고 복잡하다. 반면 부품은 이미 같은 자리에 맞춰져 있으므로,
    /// <b>같은 동작을 같은 자리에서 재생하면 같은 결과</b>가 나온다. 보내는 것은
    /// 좌표 하나와 양 하나뿐이다.
    ///
    /// 아무도 듣지 않으면(혼자 실행) 그냥 아무 일도 일어나지 않는다.
    /// </summary>
    public static class WorldEvents
    {
        /// <summary>
        /// 세계의 결과를 <b>다른 곳(서버)이 정하는가.</b> 매치에 참가자로 붙어 있으면 참이다.
        ///
        /// 참이면 이 화면은 그림일 뿐이므로 스스로 세계를 바꾸면 안 된다. 예를 들어 용접을
        /// 여기서도 하면 서버가 보낸 것과 겹쳐 비드가 두 배로 자란다.
        /// 혼자 실행하거나 내가 서버(방장)일 때는 거짓이다.
        /// </summary>
        public static bool RemoteDecides;

        /// <summary>서버가 이 자리에서 용접 비드를 키웠다. (노즐 위치, 이번 스텝의 양)</summary>
        public static System.Action<Vector3, float> WeldGrown;

        /// <summary>서버가 이 자리에서 용접 비드를 깎았다. (노즐 위치, 이번 스텝의 양)</summary>
        public static System.Action<Vector3, float> WeldShrunk;

        /// <summary>
        /// 듣는 쪽을 모두 떼어 낸다. 매치가 끝날 때 부른다 — 남겨 두면 다음 매치에서
        /// 이미 사라진 오브젝트를 향해 사건이 날아간다.
        /// </summary>
        public static void Clear()
        {
            WeldGrown = null;
            WeldShrunk = null;
            RemoteDecides = false;
        }
    }
}
