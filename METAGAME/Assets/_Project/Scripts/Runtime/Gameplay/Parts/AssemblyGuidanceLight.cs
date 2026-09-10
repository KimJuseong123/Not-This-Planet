using UnityEngine;

namespace Metagame.Gameplay.Parts
{
    /// <summary>Read-only presentation of develop's assembly order; no tractor or physics mutations.</summary>
    public sealed class AssemblyGuidanceLight : MonoBehaviour
    {
        [SerializeField] ShipAssembly assembly;
        [SerializeField] Renderer[] lamps;
        [SerializeField] Material[] floorMaterials;
        [SerializeField] Material completeMaterial;
        int shown = -2;

        public void Configure(ShipAssembly target, Renderer[] renderers, Material[] colors, Material ready)
        { assembly = target; lamps = renderers; floorMaterials = colors; completeMaterial = ready; }

        void OnEnable() => shown = -2;
        public static int RequiredFloor(System.Collections.Generic.IReadOnlyList<ShipAssembly.FloorState> floors)
        {
            for (int i = 0; i < floors.Count; i++)
                if (floors[i] == ShipAssembly.FloorState.Empty || floors[i] == ShipAssembly.FloorState.WrongPart
                    || floors[i] == ShipAssembly.FloorState.Tilted) return i;
            return -1;
        }
        void LateUpdate()
        {
            if (assembly == null || lamps == null || floorMaterials == null) return;
            int next = RequiredFloor(assembly.Floors);
            if (next == shown) return;
            var material = next < 0 ? completeMaterial : next < floorMaterials.Length ? floorMaterials[next] : null;
            if (material == null) return;
            foreach (var lamp in lamps) if (lamp != null) lamp.sharedMaterial = material;
            shown = next;
        }
    }
}
