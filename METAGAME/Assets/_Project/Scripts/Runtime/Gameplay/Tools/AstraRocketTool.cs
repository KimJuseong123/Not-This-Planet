using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    /// <summary>A held rocket accelerates the whole ragdoll, never teleports through obstacles.</summary>
    [DefaultExecutionOrder(220)]
    public sealed class AstraRocketTool : AstraHandTool
    {
        public const float BurnSeconds = 2f;
        public const float Speed = 12f;
        public const float CooldownSeconds = 3f;
        float remaining, cooldown, puffTimer;
        Vector3 direction = Vector3.forward;
        public float Remaining => remaining;
        public bool Boosting => remaining > 0f;
        public override byte ViewAction => Boosting ? (byte)1 : cooldown > 0f ? (byte)2 : (byte)0;
        public override float ViewProgress => Boosting ? remaining / BurnSeconds : cooldown / CooldownSeconds;
        public override Vector3 ViewEffectDirection => direction;
        public override string UseHint => Boosting ? "로켓 추진 중 · 2초" : cooldown > 0f
            ? "로켓 냉각 중" : "클릭 · 바라보는 방향으로 2초 추진";

        public bool TryIgnite(Vector3 aim)
        {
            if (WorldEvents.RemoteDecides || !IsEquipped || Owner == null || Owner.Downed
                || Owner.IsRiding || Boosting || cooldown > 0f || aim.sqrMagnitude < .001f) return false;
            direction = aim.normalized;
            remaining = BurnSeconds;
            cooldown = BurnSeconds + CooldownSeconds;
            return true;
        }

        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            if (input.PrimaryPressed) TryIgnite(aim.Direction);
        }

        // ToolBase runs after PlayerRig, so ordinary walking cannot overwrite the rocket velocity.
        void FixedUpdate() => Step(Time.fixedDeltaTime);
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            cooldown = Mathf.Max(0f, cooldown - dt);
            if (!Boosting) return;
            var owner = ViewOwner;
            if (!IsHeldForView || owner == null || owner.Downed || owner.IsRiding)
            { remaining = 0f; return; }
            if (owner.Doll != null && (owner.ServerAuthority || owner.IsLocal))
            {
                foreach (var body in owner.Doll.Bodies)
                    if (body != null && !body.isKinematic)
                        body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, direction * Speed, 48f * dt);
            }
            remaining = Mathf.Max(0f, remaining - dt);
            puffTimer -= dt;
            if (puffTimer <= 0f)
            {
                puffTimer = .065f;
                Puff.Thruster(transform.position - direction * .35f, -direction);
            }
        }

        protected override void OnUnequipped() { remaining = 0f; }
        protected override void ApplyRemoteVisualState(byte action, float progress, Vector3 aim)
        {
            direction = aim.sqrMagnitude > .001f ? aim.normalized : Vector3.forward;
            remaining = action == 1 ? Mathf.Clamp01(progress) * BurnSeconds : 0f;
            cooldown = action == 1 ? remaining + CooldownSeconds : action == 2 ? Mathf.Clamp01(progress) * CooldownSeconds : 0f;
        }
    }
}
