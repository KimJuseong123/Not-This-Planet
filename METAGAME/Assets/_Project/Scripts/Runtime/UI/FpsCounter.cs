using System;

namespace Metagame.UI
{
    /// <summary>Counts rendered frames over real elapsed time, independently of time scale.</summary>
    public sealed class FpsCounter
    {
        const double SampleSeconds = 0.5;
        double _elapsed;
        int _frames;

        public int FramesPerSecond { get; private set; }
        public bool HasSample { get; private set; }

        public bool AddFrame(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime)
                || unscaledDeltaTime <= 0f) return false;

            _elapsed += unscaledDeltaTime;
            _frames++;
            if (_elapsed < SampleSeconds) return false;

            FramesPerSecond = (int)Math.Round(_frames / _elapsed);
            HasSample = true;
            _elapsed = 0;
            _frames = 0;
            return true;
        }

        public void Reset()
        {
            _elapsed = 0;
            _frames = 0;
            FramesPerSecond = 0;
            HasSample = false;
        }
    }
}
