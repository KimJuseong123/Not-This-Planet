using UnityEngine;

namespace Metagame.UI
{
    public static class CustomizerFlow
    {
        public const string SceneName = "Customizer";
        public static bool Multiplayer { get; private set; }
        public static bool Begin(bool multiplayer)
        {
            if (LobbyTransition.Ensure().IsBusy) return false;
            Multiplayer = multiplayer;
            return LobbyTransition.Ensure().BeginCustomizer();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Multiplayer = false; }
    }
}
