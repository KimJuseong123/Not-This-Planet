using UnityEngine;

namespace Metagame.Gameplay
{
    /// <summary>Continuous presence is required. Leaving or opening a menu cancels departure.</summary>
    public sealed class PracticeDeparture
    {
        public float Elapsed { get; private set; }
        public bool Ready { get; private set; }
        public void Tick(bool inside, bool canPlay, float deltaTime, float duration)
        {
            if (!inside || !canPlay) { Elapsed = 0f; Ready = false; return; }
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) return;
            Elapsed += deltaTime;
            Ready = Elapsed >= Mathf.Max(1f, duration);
        }
    }
}
