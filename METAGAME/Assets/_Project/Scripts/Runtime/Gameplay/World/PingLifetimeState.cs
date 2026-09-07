using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>
    /// 플레이어 한 명의 로컬 핑 수명과 위치를 보관한다.
    /// 네트워크 핑은 Mirror SyncVar가 상태를 보관하지만, 솔로 플레이와 테스트가
    /// 같은 교체·만료 규칙을 사용하도록 수명 규칙만 이 작은 상태 객체에 둔다.
    /// </summary>
    public sealed class PingLifetimeState
    {
        public bool IsActive { get; private set; }
        public Vector3 Position { get; private set; }
        public double ExpiresAt { get; private set; }

        public void Place(Vector3 position, double now, double lifetime)
        {
            Position = position;
            ExpiresAt = now + Mathf.Max(0f, (float)lifetime);
            IsActive = lifetime > 0d;
        }

        public void Tick(double now)
        {
            if (IsActive && now >= ExpiresAt)
                IsActive = false;
        }

        public void Clear()
        {
            IsActive = false;
            Position = Vector3.zero;
            ExpiresAt = 0d;
        }
    }
}
