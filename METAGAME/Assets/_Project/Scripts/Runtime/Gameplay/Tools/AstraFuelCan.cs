using UnityEngine;
using Metagame.Core;
using Metagame.Gameplay.Parts;

namespace Metagame.Gameplay
{
    public sealed class AstraFuelCan : AstraHandTool
    {
        public const float CanCapacity = 30f;
        [SerializeField, Range(0f, CanCapacity)] float units = CanCapacity;
        public float Units => units;
        public override float ViewProgress => units / CanCapacity;
        public override string UseHint => units > 0f
            ? "연료 " + Mathf.CeilToInt(units) + " · Part_4를 가까이 겨누고 클릭하여 주유"
            : "빈 연료통";

        public float TransferTo(AstraFuelTank tank)
        {
            if (WorldEvents.RemoteDecides || tank == null || units <= 0f) return 0f;
            float amount = tank.Add(units);
            units -= amount;
            return amount;
        }

        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            if (!input.PrimaryPressed || WorldEvents.RemoteDecides) return;
            if (FindSurface(aim.Direction, 3.5f, out var hit))
                TransferTo(hit.collider.GetComponentInParent<AstraFuelTank>());
        }
        protected override void ApplyRemoteVisualState(byte action, float progress, Vector3 aim)
            => units = Mathf.Clamp01(progress) * CanCapacity;
    }
}
