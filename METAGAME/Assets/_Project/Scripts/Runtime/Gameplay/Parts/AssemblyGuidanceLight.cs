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
        [SerializeField] private Renderer[] _floorFixtures;
        [SerializeField] private Material[] _floorFixtureMaterials;
        [SerializeField] private Material _floorFixtureCompleteMaterial;
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

        /// <summary>Imported floor fixtures keep their chassis in slot 0 and change only the lens in slot 1.</summary>
        public void ConfigureFloorFixtures(Renderer[] renderers, Material[] colors, Material ready)
        {
            _floorFixtures = renderers;
            _floorFixtureMaterials = colors;
            _floorFixtureCompleteMaterial = ready;
            shown = -2;
        }

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
            var lens = next < 0 ? _floorFixtureCompleteMaterial
                : _floorFixtureMaterials != null && next < _floorFixtureMaterials.Length ? _floorFixtureMaterials[next] : null;
            if (lens != null && _floorFixtures != null)
                foreach (var fixture in _floorFixtures)
                {
                    if (fixture == null) continue;
                    var materials = fixture.sharedMaterials;
                    if (materials.Length < 2) continue;
                    materials[1] = lens;
                    fixture.sharedMaterials = materials;
                }
            shown = next;
        }
    }
}
