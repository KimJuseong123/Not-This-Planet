namespace Metagame.Core
{
    /// <summary>공유할 것은 종류와 만료 시각뿐이다. 방향은 각 관찰자 화면에서 계산한다.</summary>
    public sealed class EmoteState
    {
        public const byte Count = 6;
        public const double Lifetime = 3.5;
        public const double Cooldown = 0.8;
        public byte Id { get; private set; }
        public double ExpiresAt { get; private set; }
        double _nextAllowed;

        public static bool IsValid(byte id) => id >= 1 && id <= Count;
        public static bool IsActive(byte id, double expiresAt, double now) =>
            IsValid(id) && now < expiresAt;

        public bool TryShow(byte id, double now)
        {
            if (!IsValid(id) || double.IsNaN(now) || double.IsInfinity(now)
                || now < _nextAllowed) return false;
            Id = id;
            ExpiresAt = now + Lifetime;
            _nextAllowed = now + Cooldown;
            return true;
        }
    }
}
