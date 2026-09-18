using Metagame.Core;
using Metagame.Gameplay;
using Metagame.Gameplay.Stage2;
using Metagame.Network;
using Metagame.UI;
using UnityEngine;

namespace Metagame.Bootstrap
{
    [RequireComponent(typeof(SpaceBootstrap))]
    public sealed class Stage2Bootstrap : MonoBehaviour
    {
        [SerializeField] Stage2Yard _yard;
        SpaceBootstrap _space;
        Stage2Bridge _bridge;
        Stage2Hud _hud;
        readonly CircularWrenchInput _circle = new();
        float _sendAt, _turns;
        bool _interactHeld, _secondaryHeld;
        int _bolt = -1;
        public void Configure(Stage2Yard yard) => _yard = yard;

        void Start()
        {
            if (Mirror.NetworkManager.singleton != null)
                Mirror.NetworkManager.singleton.onlineScene = gameObject.scene.path;
            _space = GetComponent<SpaceBootstrap>();
            _bridge = gameObject.AddComponent<Stage2Bridge>();
            _bridge.Configure(_yard);
            // Reuse Stage1's player/session/network infrastructure, replace chapter-specific guidance.
            if (_space.ExplorationHud != null) _space.ExplorationHud.enabled = false;
            if (_space.Exploration != null) _space.Exploration.enabled = false;
            if (_space.Hud != null) _space.Hud.enabled = false;
            _space.Session.Exploration = null;
            _space.Session.StageInputFilter = Filter;
            _yard.Completed += _space.Session.Clear;
            _space.Session.PlayerRespawned += _yard.ClearContaminationAfterRespawn;
            _hud = gameObject.AddComponent<Stage2Hud>();
            _hud.Configure(_yard, _space.Session, () => _bridge.LocalId);
        }

        InputFrame Filter(InputFrame frame)
        {
            var player = _space.Session.LocalPlayer;
            if (player == null || player.Cam == null) return frame;
            bool active = _space.Session.CursorLocked && !_space.Session.InputSuppressed && !player.Downed;
            var target = active ? Focus(player) : null;
            _hud.Focus = target;
            if (frame.TabletToggle) _hud.GuideOpen = !_hud.GuideOpen;
            var tool = _yard.PlayerState(_bridge.LocalId).Tool;
            bool pilot = _yard.State.Pilot == _bridge.LocalId;
            bool climbing = _yard.State.Climber == _bridge.LocalId;
            bool wrench = active && !pilot && !climbing && !_hud.GuideOpen && tool == YardTool.Wrench
                && target != null && target.Kind == YardTargetKind.Bolt && frame.ToolPrimary;
            if (!wrench || _bolt != target.Id) { _circle.Reset(); _bolt = wrench ? target.Id : -1; }
            if (wrench)
            {
                float turn = _circle.Sample(frame.Look);
                _turns += turn; _hud.WrenchAngle += turn * 360f;
            }
            bool interact = active && !_hud.GuideOpen && frame.TransportAction && !_interactHeld;
            bool unfasten = active && wrench && frame.ToolSecondary && !_secondaryHeld;
            _interactHeld = frame.TransportAction;
            _secondaryHeld = frame.ToolSecondary;
            if (Time.unscaledTime >= _sendAt || interact || unfasten)
            {
                _sendAt = Time.unscaledTime + .05f;
                _bridge.Send(new YardCommand
                {
                    Target = target != null ? target.Id : -1,
                    Action = unfasten ? YardAction.Unfasten : interact ? YardAction.Interact : YardAction.None,
                    Axes = active && pilot && !_hud.GuideOpen ? new Vector3(frame.Move.x, frame.Move.y, (frame.Jump ? 1 : 0) - (frame.ArmsUp ? 1 : 0)) : Vector3.zero,
                    Slow = frame.Run,
                    Primary = active && !_hud.GuideOpen && frame.ToolPrimary,
                    Aim = player.Cam.transform.forward,
                    Turns = _turns
                });
                _turns = 0;
            }
            if (wrench || _hud.GuideOpen) frame.Look = Vector2.zero;
            if (pilot || climbing || _hud.GuideOpen) { frame.Move = Vector2.zero; frame.Jump = frame.ArmsUp = false; }
            if (tool != YardTool.None || pilot || climbing || _hud.GuideOpen)
            { frame.GrabLeft = frame.GrabRight = frame.ToolPrimary = frame.ToolSecondary = frame.ToolAction = false; }
            frame.TransportAction = false;
            return frame;
        }

        Stage2Target Focus(PlayerRig player)
        {
            var camera = player.Cam.transform;
            Stage2Target best = null;
            float score = .9f;
            foreach (var target in _yard.Targets)
            {
                Vector3 delta = target.transform.position - camera.position;
                float dot = Vector3.Dot(camera.forward, delta.normalized);
                if (dot <= score || !_yard.CanReach(player, target)) continue;
                score = dot; best = target;
            }
            return best;
        }

        void OnDestroy()
        {
            if (_space != null && _space.Session != null)
            {
                _space.Session.StageInputFilter = null;
                if (_yard != null)
                {
                    _yard.Completed -= _space.Session.Clear;
                    _space.Session.PlayerRespawned -= _yard.ClearContaminationAfterRespawn;
                }
            }
        }
    }
}
