using UnityEngine;
using Metagame.Core;

namespace Metagame.Gameplay
{
    public sealed class AstraMineDetector : AstraHandTool
    {
        [SerializeField] Renderer led;
        [SerializeField] LineRenderer[] rings;
        MaterialPropertyBlock properties;
        bool scanning;
        float proximity, phase;
        public override string UseHint => "좌클릭 유지 · 정지하고 지뢰 탐색 · 원이 빨라질수록 위험";
        public override bool RequiresStationaryPrimaryUse => true;
        public override byte ViewAction => scanning ? (byte)1 : (byte)0;
        public override float ViewProgress => proximity;
        public float PulseInterval => Mathf.Lerp(1.2f, .12f, proximity);
        public void Configure(Renderer indicator, LineRenderer[] circles) { led = indicator; rings = circles; }
        protected override void OnToolInput(ToolInputFrame input, ToolAimContext aim)
        {
            scanning = input.PrimaryHeld && !input.UtilityHeld && Owner != null && !Owner.Downed && !Owner.Ragdolled && !Owner.IsRiding;
            proximity = scanning ? 1 - AstraMinefield.Nearest(Owner.FootPosition, gameObject.scene, 6) / 6 : 0;
        }
        protected override void ApplyRemoteVisualState(byte action, float progress, Vector3 aim)
        { scanning = action == 1; proximity = Mathf.Clamp01(progress); }
        protected override void OnUnequipped() { scanning = false; proximity = 0; }
        protected override void OnDisable() { scanning = false; Show(false); base.OnDisable(); }
        void Show(bool active)
        {
            if (rings != null) foreach (var ring in rings) if (ring != null) ring.enabled = active;
        }
        protected override void LateUpdate()
        {
            bool active = scanning && IsHeldForView && ViewOwner != null && !ViewOwner.Downed && !ViewOwner.Ragdolled;
            phase = Mathf.Repeat(phase + Time.deltaTime / PulseInterval, 1);
            if (led != null)
            {
                if (properties == null) properties = new MaterialPropertyBlock();
                float brightness = active && proximity > 0 ? (phase < .45f ? 1 : .15f) : .015f;
                led.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", Color.red * brightness);
                properties.SetColor("_EmissionColor", Color.red * brightness * 4);
                led.SetPropertyBlock(properties);
            }
            base.LateUpdate();
            Show(active);
            if (!active || rings == null) return;
            var center = ViewOwner.FootPosition + Vector3.up * .08f;
            for (int r = 0; r < rings.Length; r++)
            {
                var ring = rings[r]; if (ring == null) continue;
                float progress = Mathf.Repeat(phase + r / (float)rings.Length, 1);
                float radius = .15f + progress * 2;
                var color = Color.Lerp(Color.cyan, Color.red, proximity); color.a = (1 - progress) * .9f;
                ring.startColor = ring.endColor = color;
                for (int i = 0; i < ring.positionCount; i++)
                { float angle = i * Mathf.PI * 2 / ring.positionCount; ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius); }
            }
        }
    }
}
