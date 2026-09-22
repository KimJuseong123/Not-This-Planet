namespace Metagame.Gameplay.Audio
{
    public enum MusicRepeatStep { Start, Playing, Waiting }

    /// <summary>기존 BGM의 곡 종료 → 대기 → 재시작 판단. 출력 장치와 프레임 속도에 의존하지 않는다.</summary>
    public sealed class MusicRepeatState
    {
        bool started;
        double restartAt;

        public void Reset(double now)
        {
            started = false;
            restartAt = now;
        }

        public MusicRepeatStep Tick(double now, bool sourcePlaying, float repeatDelaySeconds)
        {
            if (!started)
            {
                if (now < restartAt) return MusicRepeatStep.Waiting;
                started = true;
                return MusicRepeatStep.Start;
            }
            if (sourcePlaying) return MusicRepeatStep.Playing;
            started = false;
            restartAt = now + repeatDelaySeconds;
            return MusicRepeatStep.Waiting;
        }
    }
}
