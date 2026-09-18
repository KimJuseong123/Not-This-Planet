using Metagame.Gameplay;
using Metagame.UI;
using UnityEngine;

namespace Metagame.Bootstrap
{
    /// <summary>Practice-only signage and departure. SpaceBootstrap owns all gameplay, exactly as in Stage1.</summary>
    public sealed class PracticeLobbyBootstrap : MonoBehaviour
    {
        [SerializeField] SpaceBootstrap gameplay;
        [SerializeField] Transform departure;
        [SerializeField] Transform resetConsole;
        [SerializeField] Transform battingStation;
        [SerializeField] Transform weldingStation;
        [SerializeField] Transform anchorStation;
        [SerializeField] float departureSeconds = 3f;

        public GameSession Session => gameplay != null ? gameplay.Session : null;
        public PlayerRig Player => Session != null ? Session.LocalPlayer : null;

        void Start()
        {
            if (gameplay == null || Session == null)
            {
                Debug.LogError("[PracticeLobby] Stage1 SpaceBootstrap 연결을 확인해 주세요.", this);
                return;
            }
            // No second rig, hand view, input, audio or physics initialization here.
            // Solo reset/departure must never perform a local scene load in a match.
            if (Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
            var hud = gameObject.AddComponent<PracticeLobbyHud>();
            hud.Configure(Session, departure, resetConsole, battingStation, weldingStation, anchorStation,
                Mathf.Max(1f, departureSeconds));
        }
    }
}
