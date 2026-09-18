using Metagame.Gameplay;
using Metagame.AICompanion;
using Metagame.Core;
using Metagame.Network;
using Mirror;
using UnityEngine;

namespace Metagame.AIExperiment
{
    /// <summary>One host-simulated companion; observers only render the replicated result.</summary>
    public sealed class NetworkCompanion : NetworkBehaviour
    {
        [SyncVar] CompanionOrder _order;
        [SyncVar] string _status = "대기";
        [SyncVar] RabbitLocomotion.State _pose;
        [SyncVar] byte _emoteId;
        [SyncVar] double _emoteExpiresAt;
        readonly EmoteState _emote = new EmoteState();
        public byte EmoteId => _emoteId;
        public double EmoteExpiresAt => _emoteExpiresAt;
        public double EmoteNow => _solo ? Time.unscaledTimeAsDouble : NetworkTime.time;
        public Vector3 EmoteAnchor => _rig != null && _rig.Doll.Head != null
            ? _rig.Doll.Head.position + Vector3.up * .89f : transform.position + Vector3.up * 1f;

        public bool ShowQuestionEmote()
        {
            if (!CanControl || _rig == null || !_emote.TryShow(1, EmoteNow)) return false;
            _emoteId = _emote.Id;
            _emoteExpiresAt = _emote.ExpiresAt;
            return true;
        }

        void ClearEmote() { _emoteId = 0; _emoteExpiresAt = 0; }
        PlayerRig _rig;
        CompanionMotor _motor;
        RemoteAvatar _avatar;
        bool _solo;
        public CompanionMotor Motor => _motor;
        public string Status => _motor != null ? _motor.Status : _status;
        public CompanionOrder Order => _motor != null ? _motor.Order : _order;
        public PlayerRig Rig => _rig;
        public bool CanControl => _solo || (netIdentity != null && isServer);

        public void InitializeSolo() { _solo = true; }

        void Update()
        {
            if (_rig == null)
            {
                if (!_solo && !isServer && !isClient) return;
                var session = FindAnyObjectByType<GameSession>();
                if (session == null || session.LocalPlayer == null) return;
                bool authority = CanControl;
                Vector3 foot = authority ? new Vector3(-6.2f, 0.2f, -2.5f) : transform.position - Vector3.up * 0.8f;
                _rig = PlayerRig.Create("AI 동료", foot, Quaternion.Euler(0, 90, 0), Color.white, Color.cyan, true);
                _rig.IsLocal = false;
                _rig.NetworkDriven = !_solo;
                _rig.ServerAuthority = authority;
                (GetComponent<NetworkRigOwner>() ?? gameObject.AddComponent<NetworkRigOwner>()).Bind(_rig);
                _rig.transform.SetParent(transform, true);
                if (authority)
                {
                    // The body must NOT be parented to the moving network snapshot transform.
                    _rig.transform.SetParent(null, true);
                    _motor = _rig.gameObject.AddComponent<CompanionMotor>();
                    _motor.Initialize(_rig);
                }
                else
                {
                    _rig.transform.SetParent(null, true);
                    _avatar = RemoteAvatar.Attach(_rig, transform);
                }
            }
            if (CanControl)
            {
                transform.SetPositionAndRotation(_rig.Hips.position, _rig.Hips.rotation);
                _order = _motor.Order; _status = _motor.Status;
                _pose = RabbitLocomotion.ReadState(_rig);
            }
            else
            {
                if (_rig.Doll.Suit != null) _rig.Doll.Suit.NetworkLocomotionState = _pose;
                if (_avatar != null) _avatar.SetArmIntent(_pose.LeftArmBusy, _pose.RightArmBusy);
            }
        }

        public bool Apply(string action, Transform speaker)
            => TryApply(action, speaker, out _);

        public bool TryApply(string action, Transform speaker, out string feedback)
        {
            feedback = "지원하지 않는 요청 · 기존 행동 유지";
            if (!CanControl || _motor == null) { feedback = "동료가 준비되지 않았어요"; return false; }

            // 어느 일을 시킬지 고르는 판단은 CompanionOrders 가 한다 — Stage1 도 같은
            // 판단을 써야 해서 옮겼다. 여기서는 그 결과에 실험 씬 몫(이모티콘 지우기,
            // 로그)만 얹는다.
            if (!CompanionOrders.TryApply(_motor, action, speaker, out feedback)) return false;

            ClearEmote();
            Debug.Log($"[AIExperiment] action={action}");
            return true;
        }

        void OnDestroy() { if (_rig != null) Destroy(_rig.gameObject); }
    }
}
