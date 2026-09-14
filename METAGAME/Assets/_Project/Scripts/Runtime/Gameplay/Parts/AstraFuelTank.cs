using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>Optional vehicle fuel requirement. Vehicles without this component keep their rules.</summary>
    public sealed class AstraFuelTank : MonoBehaviour
    {
        [SerializeField, Min(1f)] float capacity = 60f;
        [SerializeField, Min(0f)] float units;
        [SerializeField, Min(.01f)] float burnPerSecond = 1f;
        [SerializeField] Transform fuelBar;
        [SerializeField] TextMesh label;
        public float Units => units;
        public float Capacity => capacity;
        public bool HasFuel => units > .001f;

        public float Add(float offered)
        {
            if (WorldEvents.RemoteDecides || offered <= 0f || float.IsNaN(offered) || float.IsInfinity(offered)) return 0f;
            float accepted = Mathf.Min(offered, capacity - units);
            units += accepted;
            return accepted;
        }

        public bool Consume(float seconds, bool boost = false)
        {
            if (WorldEvents.RemoteDecides || seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
            units = Mathf.Max(0f, units - seconds * burnPerSecond * (boost ? 1.6f : 1f));
            return HasFuel;
        }

        public void ApplySnapshot(float value) => units = Mathf.Clamp(value, 0f, capacity);
        public void ConfigureDisplay(Transform bar, TextMesh text) { fuelBar = bar; label = text; }
        void LateUpdate()
        {
            if (fuelBar != null) fuelBar.localScale = new Vector3(Mathf.Max(.02f, units / capacity), .1f, .06f);
            if (label != null) label.text = HasFuel ? "FUEL " + Mathf.CeilToInt(units) + " / " + capacity : "FUEL EMPTY";
        }
    }
}
