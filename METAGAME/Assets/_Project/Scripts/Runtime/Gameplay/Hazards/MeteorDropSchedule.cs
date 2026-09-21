using System;

namespace Metagame.Gameplay.Hazards
{
    /// <summary>한 관리자가 소유하는 8개의 독립 낙하 시계. 매 낙하 후 1~5초를 다시 고른다.</summary>
    public sealed class MeteorDropSchedule
    {
        public const int SpawnerCount = 8;
        public const float MinSeconds = 1f;
        public const float MaxSeconds = 5f;
        readonly float[] _remaining = new float[SpawnerCount];
        Random _random;

        public MeteorDropSchedule(int seed) => Reset(seed);

        public void Reset(int seed)
        {
            _random = new Random(seed);
            for (int i = 0; i < SpawnerCount; i++) _remaining[i] = NextDelay();
        }

        public float SecondsUntilNext(int activeCount)
        {
            float next = float.MaxValue;
            for (int i = 0; i < Math.Clamp(activeCount, 1, SpawnerCount); i++)
                next = Math.Min(next, _remaining[i]);
            return next;
        }

        /// <summary>이번 틱에 만료된 스포너 비트. 긴 프레임 뒤 밀린 운석을 한꺼번에 만들지 않는다.</summary>
        public int Tick(float deltaTime, int activeCount)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return 0;
            int due = 0;
            for (int i = 0; i < Math.Clamp(activeCount, 1, SpawnerCount); i++)
            {
                _remaining[i] -= deltaTime;
                if (_remaining[i] > 0f) continue;
                due |= 1 << i;
                _remaining[i] = NextDelay();
            }
            return due;
        }

        float NextDelay() => MinSeconds + (float)_random.NextDouble() * (MaxSeconds - MinSeconds);
    }
}
