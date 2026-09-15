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
        [SerializeField] Renderer[] bollards;
        [SerializeField] Material[] bollardFloorMaterials;
        [SerializeField] Material bollardCompleteMaterial;
        int shown = -2;

        public void Configure(ShipAssembly target, Renderer[] renderers, Material[] colors, Material ready)
        { assembly = target; lamps = renderers; floorMaterials = colors; completeMaterial = ready; }

        /// <summary>Textured bollards need masked materials rather than the solid floor materials.</summary>
        public void ConfigureBollards(Renderer[] renderers, Material[] colors, Material ready)
        {
            bollards = renderers;
            bollardFloorMaterials = colors;
            bollardCompleteMaterial = ready;
            shown = -2;
        }

        void OnEnable() => shown = -2;

        void Start()
        {
            // Enhance the authored location beacons, independently of movable parts.
            foreach (var beacon in FindObjectsByType<MeshRenderer>())
            {
                if (beacon.gameObject.scene != gameObject.scene || beacon.sharedMaterial == null) continue;
                if (beacon.name != "Module beacon \u00b7 visual guide" && beacon.name != "02 Tractor sky beacon") continue;
                var marker = new GameObject("Plasma beacon - " + beacon.name);
                marker.transform.SetParent(transform, false);
                marker.AddComponent<PartGuidanceVfx>().Configure(beacon);
            }
        }

        /// <summary>파츠 주변 경로등과 고정못 연결선이 같은 색상 원본을 사용한다.</summary>
        public Material MaterialForPart(SpaceshipPart part)
        {
            if (part == null || floorMaterials == null) return null;
            for (int i = 0; i < PartSpec.All.Length && i < floorMaterials.Length; i++)
                if (PartSpec.All[i].Name == part.name) return floorMaterials[i];
            return null;
        }
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
            var bollardMaterial = next < 0 ? bollardCompleteMaterial
                : bollardFloorMaterials != null && next < bollardFloorMaterials.Length ? bollardFloorMaterials[next] : null;
            if (bollardMaterial != null && bollards != null)
                foreach (var bollard in bollards) if (bollard != null) bollard.sharedMaterial = bollardMaterial;
            shown = next;
        }
    }
}
