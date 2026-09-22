using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Audio;
using Metagame.Gameplay.Parts;
using Metagame.Network;
using UnityEngine;

namespace Metagame.Bootstrap
{
    /// <summary>복제된 게임/컷씬 상태를 로컬 음악으로 표현한다. 별도 RPC나 서버 AudioSource는 없다.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneMusicController : MonoBehaviour
    {
        GameMusicPlayer music;
        GameSession session;
        DemoEnding ending;
        MusicCue baseline;
        bool cinematic;

        public static bool CanPlayLocally
        {
            get
            {
#if UNITY_SERVER
                return false;
#else
                return !MatchRole.DedicatedServer && !Application.isBatchMode;
#endif
            }
        }

        public static MusicCue SceneCue(string sceneName, bool assemblyTest)
        {
            if (sceneName == StageCatalog.Stage2SceneName) return MusicCue.Stage2;
            if (assemblyTest || sceneName == "PracticeLobby" || sceneName == "AIPracticeLobby"
                || sceneName == "AICompanionTest" || sceneName == "CheatArena") return MusicCue.Practice;
            return MusicCue.Default;
        }

        public void ConfigureGameplay(AudioManager manager, GameSession target, string sceneName, bool assemblyTest)
        {
            session = target;
            ending = null;
            cinematic = false;
            baseline = SceneCue(sceneName, assemblyTest);
            ConfigurePlayer(manager);
        }

        public void ConfigureEnding(AudioManager manager, DemoEnding target)
        {
            ending = target;
            session = null;
            cinematic = true;
            ConfigurePlayer(manager);
        }

        void ConfigurePlayer(AudioManager manager)
        {
            if (music == null) music = GetComponent<GameMusicPlayer>();
            if (music == null) music = gameObject.AddComponent<GameMusicPlayer>();
            music.Configure(manager);
            Tick();
        }

        void Update() => Tick();

        public void Tick()
        {
            if (music == null || !music.isActiveAndEnabled) return;
            if (cinematic)
            {
                if (ending == null || !ending.PlaybackStarted || ending.Finished)
                    music.PlayCue(MusicCue.Silence);
                else if (ending.Elapsed >= ending.ArrivalStartsAt)
                    music.PlayCue(MusicCue.CrashLanding, ending.Elapsed - ending.ArrivalStartsAt);
                else music.PlayCue(MusicCue.Escape, ending.Elapsed);
                return;
            }
            // 컷씬 씬으로 옮기기 전의 준비 시간에도 탐색/공세 음악이 남지 않게 한다.
            if (session != null && session.EndingActive)
            {
                music.PlayCue(MusicCue.Silence);
                return;
            }
            var sequence = session != null ? session.Sequence : null;
            if (baseline == MusicCue.Default && sequence != null
                && (sequence.Phase == Stage1SequencePhase.Assault || sequence.AssaultEnabled))
            {
                double age = sequence.Age;
                if (sequence.Phase == Stage1SequencePhase.Combat) age += Stage1Sequence.AssaultSeconds;
                music.PlayCue(MusicCue.Assault, age);
            }
            else music.PlayCue(baseline);
        }
    }
}
